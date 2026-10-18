using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Infrastructure;

/// <summary>
/// Idempotency for state-changing API calls. A client sends <c>Idempotency-Key</c> (1–100 letters, digits, '-', '_', '.', ':') with a POST, PUT,
/// PATCH or DELETE. The first request runs and its response is stored for 24 hours; a retry with the same key and the same request receives the
/// stored response with <c>Idempotent-Replayed: true</c> and does not run again. The same key with a different request is refused (422), and a retry
/// while the first request is still running gets 409. Server errors are not stored, so the request can be retried.
/// </summary>
public sealed partial class IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> log)
{
    public const string Header = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan StaleInFlight = TimeSpan.FromMinutes(10);
    private const int MaxFingerprintBody = 1024 * 1024;
    private const int MaxStoredResponse = 2 * 1024 * 1024;
    private static long lastPurgeTicks;

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,100}$")] private static partial Regex KeyRx();

    public async Task InvokeAsync(HttpContext ctx, AppDbContext db)
    {
        var method = ctx.Request.Method;
        if (!ctx.Request.Headers.TryGetValue(Header, out var raw) || !ctx.Request.Path.StartsWithSegments("/api")
            || HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            await next(ctx);
            return;
        }
        var key = raw.ToString().Trim();
        if (!KeyRx().IsMatch(key)) { await Problem(ctx, 400, "The Idempotency-Key header must be 1 to 100 letters, digits or - _ . :"); return; }
        var caller = Caller(ctx.User);
        if (caller is null) { await next(ctx); return; } // anonymous calls are refused or handled by their own endpoint rules

        var fingerprint = await FingerprintAsync(ctx.Request);
        var now = DateTimeOffset.UtcNow;
        await PurgeAsync(db, now);
        var existing = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Caller == caller && x.Key == key, ctx.RequestAborted);
        if (existing is not null)
        {
            if (existing.Fingerprint != fingerprint) { await Problem(ctx, 422, "This idempotency key was already used for a different request."); return; }
            if (existing.CompletedAt is null)
            {
                if (now - existing.CreatedAt < StaleInFlight) { await Problem(ctx, 409, "A request with this idempotency key is still being processed."); return; }
                await db.IdempotencyRecords.Where(x => x.Id == existing.Id).ExecuteDeleteAsync(ctx.RequestAborted);
            }
            else
            {
                ctx.Response.StatusCode = existing.StatusCode ?? 200;
                ctx.Response.Headers[ReplayedHeader] = "true";
                if (existing.ContentType is not null) ctx.Response.ContentType = existing.ContentType;
                if (existing.Body is { Length: > 0 } body) await ctx.Response.Body.WriteAsync(body, ctx.RequestAborted);
                return;
            }
        }

        var record = new IdempotencyRecord { Caller = caller, Key = key, Method = method, Path = Truncate(ctx.Request.Path.Value ?? "/", 500), Fingerprint = fingerprint, CreatedAt = now };
        db.IdempotencyRecords.Add(record);
        try { await db.SaveChangesAsync(ctx.RequestAborted); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            await Problem(ctx, 409, "A request with this idempotency key is still being processed.");
            return;
        }
        db.ChangeTracker.Clear();

        var original = ctx.Response.Body;
        await using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;
        var completed = false;
        try
        {
            await next(ctx);
            completed = true;
        }
        finally
        {
            ctx.Response.Body = original;
            buffer.Position = 0;
            if (buffer.Length > 0) await buffer.CopyToAsync(original, CancellationToken.None);
            try
            {
                if (completed && ctx.Response.StatusCode < 500 && buffer.Length <= MaxStoredResponse)
                    await db.IdempotencyRecords.Where(x => x.Id == record.Id).ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow).SetProperty(x => x.StatusCode, ctx.Response.StatusCode)
                        .SetProperty(x => x.ContentType, ctx.Response.ContentType).SetProperty(x => x.Body, buffer.ToArray()), CancellationToken.None);
                else
                    await db.IdempotencyRecords.Where(x => x.Id == record.Id).ExecuteDeleteAsync(CancellationToken.None);
            }
            catch (Exception ex) { log.LogWarning(ex, "Could not store the idempotent response for key {Key}", key); }
        }
    }

    private static string? Caller(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return null;
        var client = user.FindFirst(ApiKeyHandler.ClientClaim)?.Value;
        if (client is not null) return Truncate("client:" + client, 100);
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value ?? user.Identity.Name;
        return id is null ? null : Truncate("user:" + id, 100);
    }

    /// <summary>Method, path, query, content type and the body; multipart and large bodies contribute their length instead of their bytes.</summary>
    private static async Task<string> FingerprintAsync(HttpRequest req)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var mediaType = (req.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        sha.AppendData(Encoding.UTF8.GetBytes($"{req.Method}\n{req.Path.Value}\n{req.QueryString.Value}\n{mediaType}\n{req.ContentLength}\n"));
        if (!mediaType.StartsWith("multipart/") && req.ContentLength is > 0 and <= MaxFingerprintBody)
        {
            req.EnableBuffering();
            var buf = new byte[81920];
            int n;
            while ((n = await req.Body.ReadAsync(buf)) > 0) sha.AppendData(buf, 0, n);
            req.Body.Position = 0;
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static async Task PurgeAsync(AppDbContext db, DateTimeOffset now)
    {
        var last = Interlocked.Read(ref lastPurgeTicks);
        if (now.UtcTicks - last < TimeSpan.FromMinutes(10).Ticks || Interlocked.CompareExchange(ref lastPurgeTicks, now.UtcTicks, last) != last) return;
        var cutoff = now - Lifetime;
        await db.IdempotencyRecords.Where(x => x.CreatedAt < cutoff).ExecuteDeleteAsync();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private static Task Problem(HttpContext ctx, int status, string title)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { status, title, traceId = ctx.TraceIdentifier }, options: null, contentType: "application/problem+json");
    }
}

/// <summary>
/// Correlation IDs: a caller may send <c>X-Correlation-Id</c> (1–64 letters, digits, '-', '_', '.', ':'); otherwise the request's trace id is used.
/// The value is returned in the same header, written to problem details as traceId and stored with audit entries.
/// </summary>
public static partial class CorrelationId
{
    public const string Header = "X-Correlation-Id";
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$")] private static partial Regex Rx();

    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Headers.TryGetValue(Header, out var v) && v.ToString().Trim() is var id && Rx().IsMatch(id)) ctx.TraceIdentifier = id;
        ctx.Response.Headers[Header] = ctx.TraceIdentifier;
        await next();
    });
}
