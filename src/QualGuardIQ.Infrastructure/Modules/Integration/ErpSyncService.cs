using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Integration;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Integration.Erp;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Integration;

/// <summary>Encrypts connection secrets with ASP.NET Core Data Protection. Decryption happens only inside a sync or test run.</summary>
public sealed class SecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _p = provider.CreateProtector("QualGuardIQ.Integration.Secrets.v1");
    public string Protect(string plain) => _p.Protect(plain);
    public string? Unprotect(string? cipher) => cipher is null ? null : _p.Unprotect(cipher);
}

/// <summary>Supplies the HTTP handler for adapters. Tests swap in recorded vendor fixtures; production uses one pooled handler without cookies.</summary>
public sealed class ErpHttpHandlerProvider
{
    private static readonly SocketsHttpHandler Shared = new() { UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5), AutomaticDecompression = System.Net.DecompressionMethods.All };
    public Func<HttpMessageHandler>? Override { get; set; }
    public HttpClient Create() => Override is { } f ? new HttpClient(f(), disposeHandler: true) { Timeout = TimeSpan.FromMinutes(11) } : new HttpClient(Shared, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(11) };
}

public sealed class ErpAdapterRegistry(IEnumerable<IErpAdapter> adapters)
{
    private readonly Dictionary<ErpSystem, IErpAdapter> _byKind = adapters.ToDictionary(a => a.System);
    public IErpAdapter Get(ErpSystem s) => _byKind.TryGetValue(s, out var a) ? a : throw new AppException($"No adapter for {s}.");
    public IEnumerable<IErpAdapter> All => _byKind.Values;

    public string SetupNotes(ErpSystem s, string edition)
    {
        var a = Get(s);
        var cap = a.Capabilities(edition);
        var sb = new StringBuilder();
        sb.AppendLine($"{s} — {edition}. Source: {cap.Doc}");
        sb.AppendLine($"Auth: {cap.Auth}");
        sb.AppendLine($"API: {cap.Api}");
        sb.AppendLine($"Paging: {cap.Paging}");
        sb.AppendLine($"Limits: {cap.RateLimits}");
        sb.AppendLine($"Events: {cap.Events}");
        sb.AppendLine($"Reads: {string.Join(", ", cap.Read)}. Writes: none (read-only adapter).");
        var u = a.Unverified(edition);
        if (u.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"UNVERIFIED ({u.Count}) — confirm against the customer's sandbox before enabling:");
            foreach (var i in u) sb.AppendLine($"- {i.Topic}: {i.Detail}");
        }
        return sb.ToString().TrimEnd();
    }
}

public sealed record ConnectionInput(string Name, ErpSystem System, string? Edition, string? BaseUrl, string? Account, string? Company, string? ClientId, string? Username,
    Dictionary<string, JsonElement>? Options, int? MaxConcurrency, int? RequestsPerMinute, bool InventorySystemOfRecord, string? AdminNotes, Guid SiteId, Guid? DepartmentId);
/// <summary>Write-only secrets: null leaves the stored value unchanged, empty string clears it.</summary>
public sealed record SecretInput(string? Secret, string? SecondarySecret);
public sealed record MappingInput(SyncDirection Direction, SyncMode Mode, ConflictPolicy Conflict, string Cron, Dictionary<string, string>? FieldMap, JsonElement? ValidationRules);
public sealed record SyncResult(Guid RunId, SyncRunStatus Status, int Read, int Created, int Updated, int Unchanged, int Queued, int Rejected, int Requests, string? Error);

/// <summary>
/// Connector framework: connections, mappings, scheduled/manual sync with delta cursors, idempotent external-id links,
/// and the inbound change queue routed through the shared approval engine.
/// </summary>
public sealed class ErpSyncService(AppDbContext db, ModuleSupport m, IAccessService access, ICurrentUser current, ErpAdapterRegistry adapters, SecretProtector secrets,
    ErpHttpHandlerProvider handlers, ErpTokenCache tokens, INumberingService numbers, INotifier notifier, WorkflowService workflow, ISiteCalendar calendar, IClock clock)
{
    public const string ChangeType = "ErpChange";
    public const int MaxPagesPerRun = 200;
    public static readonly TimeSpan Overlap = TimeSpan.FromMinutes(2);

    // ------------------------------------------------------------------ connections
    public async Task<List<ErpConnection>> ListAsync(CancellationToken ct) =>
        await (await m.ScopeAsync(db.ErpConnections.AsNoTracking(), Permissions.IntegrationsView, ct)).OrderBy(x => x.Name).ToListAsync(ct);

    public async Task<ErpConnection> GetAsync(Guid id, CancellationToken ct)
    {
        var c = await db.ErpConnections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Connection");
        if (!await m.CanAsync(Permissions.IntegrationsView, c.SiteId, c.DepartmentId, ct)) throw new NotFoundException("Connection");
        return c;
    }

    public async Task<ErpConnection> SaveAsync(Guid? id, ConnectionInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Name, "Name", 200);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.IntegrationsEdit, r.SiteId, r.DepartmentId, ct);
        var adapter = adapters.Get(r.System);
        var edition = string.IsNullOrWhiteSpace(r.Edition) ? adapter.Editions[0] : r.Edition.Trim();
        if (!adapter.Editions.Contains(edition)) throw new AppException($"{r.System} editions: {string.Join(", ", adapter.Editions)}.");
        if (!string.IsNullOrWhiteSpace(r.BaseUrl) && !(Uri.TryCreate(r.BaseUrl, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.IsLoopback)))
            throw new AppException("The base URL must be an absolute https URL.");
        ErpConnection c;
        if (id is { } x)
        {
            c = await db.ErpConnections.FirstOrDefaultAsync(e => e.Id == x, ct) ?? throw new NotFoundException("Connection");
            await m.RequireAsync(Permissions.IntegrationsEdit, c.SiteId, c.DepartmentId, ct);
            if (c.System != r.System) throw new AppException("The ERP of a connection cannot be changed; create a new connection.");
        }
        else
        {
            if (await db.ErpConnections.AnyAsync(e => e.Name == r.Name.Trim(), ct)) throw new ConflictException("A connection with this name exists.");
            c = new ErpConnection { System = r.System, OwnerId = m.UserId, Enabled = false, AllowWrites = false };
            db.ErpConnections.Add(c);
        }
        var cap = adapter.Capabilities(edition);
        c.Name = r.Name.Trim(); c.Edition = edition; c.BaseUrl = r.BaseUrl?.Trim() ?? ""; c.Account = r.Account?.Trim(); c.Company = r.Company?.Trim();
        c.ClientId = r.ClientId?.Trim(); c.Username = r.Username?.Trim();
        c.Options = JsonSerializer.Serialize(r.Options ?? new());
        c.MaxConcurrency = Math.Clamp(r.MaxConcurrency ?? cap.DefaultConcurrency, 1, 50);
        c.RequestsPerMinute = Math.Clamp(r.RequestsPerMinute ?? cap.DefaultRequestsPerMinute, 0, 100000);
        c.InventorySystemOfRecord = r.InventorySystemOfRecord;
        c.AdminNotes = r.AdminNotes; c.SiteId = r.SiteId; c.DepartmentId = r.DepartmentId;
        c.SetupNotes = adapters.SetupNotes(c.System, edition);
        if (id is null)
            foreach (var e in cap.Read)
                db.ErpEntityMappings.Add(new ErpEntityMapping { ConnectionId = c.Id, Entity = e, Direction = SyncDirection.Inbound, Mode = SyncMode.ApprovalRequired, Conflict = ConflictPolicy.Manual, Cron = "0 * * * *" });
        await db.SaveChangesAsync(ct);
        tokens.InvalidateAll(c.Id);
        return c;
    }

    public async Task SetSecretsAsync(Guid id, SecretInput r, CancellationToken ct)
    {
        var c = await EditableAsync(id, ct);
        if (r.Secret is not null) { c.EncryptedSecret = r.Secret.Length == 0 ? null : secrets.Protect(r.Secret); c.SecretSetAt = r.Secret.Length == 0 ? null : clock.UtcNow; }
        if (r.SecondarySecret is not null) c.EncryptedSecondarySecret = r.SecondarySecret.Length == 0 ? null : secrets.Protect(r.SecondarySecret);
        await db.SaveChangesAsync(ct);
        tokens.InvalidateAll(c.Id);
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        var c = await EditableAsync(id, ct);
        if (enabled && c.LastTestOk != true) throw new AppException("Run Test connection successfully before enabling.");
        c.Enabled = enabled;
        await db.SaveChangesAsync(ct);
    }

    async Task<ErpConnection> EditableAsync(Guid id, CancellationToken ct)
    {
        var c = await db.ErpConnections.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Connection");
        await m.RequireAsync(Permissions.IntegrationsEdit, c.SiteId, c.DepartmentId, ct);
        return c;
    }

    public async Task<List<ErpEntityMapping>> MappingsAsync(Guid id, CancellationToken ct)
    {
        await GetAsync(id, ct);
        return await db.ErpEntityMappings.AsNoTracking().Where(x => x.ConnectionId == id).OrderBy(x => x.Entity).ToListAsync(ct);
    }

    public async Task SaveMappingAsync(Guid id, ErpEntity entity, MappingInput r, CancellationToken ct)
    {
        var c = await EditableAsync(id, ct);
        var a = adapters.Get(c.System);
        if (!a.Capabilities(c.Edition).Read.Contains(entity) && r.Mode != SyncMode.Disabled) throw new AppException($"{c.System} ({c.Edition}) has no documented read for {entity}.");
        if (r.Direction != SyncDirection.Inbound && !a.Capabilities(c.Edition).Write.Contains(entity)) throw new AppException("This adapter is read-only: only inbound sync is available.");
        try { CronSchedule.Parse(r.Cron); } catch (Exception e) when (e is FormatException or OverflowException) { throw new AppException("The schedule is not a valid cron expression."); }
        var rules = r.ValidationRules is { ValueKind: JsonValueKind.Array } v ? v.GetRawText() : "[]";
        ParseRules(rules);
        var map = await db.ErpEntityMappings.FirstOrDefaultAsync(x => x.ConnectionId == id && x.Entity == entity, ct);
        if (map is null) { map = new ErpEntityMapping { ConnectionId = id, Entity = entity }; db.ErpEntityMappings.Add(map); }
        map.Direction = r.Direction; map.Mode = r.Mode; map.Conflict = r.Conflict; map.Cron = r.Cron.Trim();
        map.FieldMap = JsonSerializer.Serialize(r.FieldMap ?? new()); map.ValidationRules = rules; map.Version++;
        map.NextRunAt = Next(map.Cron, clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    static DateTimeOffset? Next(string cron, DateTimeOffset from) => CronSchedule.Parse(cron).Next(from);

    async Task<ErpCallContext> ContextAsync(ErpConnection c, ErpEntityMapping? map, HttpClient http)
    {
        var a = adapters.Get(c.System);
        var opts = JsonNode.Parse(string.IsNullOrWhiteSpace(c.Options) ? "{}" : c.Options) as JsonObject ?? new JsonObject();
        var errorParser = (Func<HttpResponseMessage, string, string?>?)null;
        await Task.CompletedTask;
        return new ErpCallContext
        {
            Connection = c, Options = opts, Secret = secrets.Unprotect(c.EncryptedSecret), SecondarySecret = secrets.Unprotect(c.EncryptedSecondarySecret),
            Http = new ErpHttp(http, c.Id, c.MaxConcurrency, c.RequestsPerMinute, errorParser), Tokens = tokens,
            FieldMap = map is null ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(map.FieldMap) ?? new(),
            PageSize = opts["pageSize"] is JsonValue pv && pv.TryGetValue<int>(out var ps) ? ps : 0, Now = clock.UtcNow
        };
    }

    public async Task<(bool Ok, string Message)> TestAsync(Guid id, CancellationToken ct)
    {
        var c = await EditableAsync(id, ct);
        using var http = handlers.Create();
        string msg; bool ok;
        try { msg = await adapters.Get(c.System).TestAsync(await ContextAsync(c, null, http), ct); ok = true; }
        catch (Exception e) when (e is ErpException or HttpRequestException or TaskCanceledException or CryptographicException or JsonException or AppException)
        { msg = e.Message; ok = false; tokens.InvalidateAll(c.Id); }
        c.LastTestAt = clock.UtcNow; c.LastTestOk = ok; c.LastTestMessage = msg.Length > 1000 ? msg[..1000] : msg;
        await db.SaveChangesAsync(ct);
        return (ok, msg);
    }

    // ------------------------------------------------------------------ sync
    /// <summary>Manual run by a signed-in user (needs integrations.edit at the connection's site).</summary>
    public async Task<SyncResult> RunNowAsync(Guid id, ErpEntity entity, CancellationToken ct)
    {
        var c = await EditableAsync(id, ct);
        return await RunAsync(c.Id, entity, "Manual", ct);
    }

    /// <summary>Runs one sync for a connection and entity. Used by the scheduler (system identity) and RunNowAsync.</summary>
    public async Task<SyncResult> RunAsync(Guid connectionId, ErpEntity entity, string trigger, CancellationToken ct)
    {
        var c = await db.ErpConnections.FirstAsync(x => x.Id == connectionId, ct);
        if (!c.Enabled) throw new AppException("The connection is disabled. Test it, then enable it.");
        var map = await db.ErpEntityMappings.FirstOrDefaultAsync(x => x.ConnectionId == c.Id && x.Entity == entity, ct) ?? throw new AppException($"No mapping for {entity}.");
        if (map.Mode == SyncMode.Disabled) throw new AppException($"Sync of {entity} is disabled on this connection.");
        var adapter = adapters.Get(c.System);
        var run = new ErpSyncRun { ConnectionId = c.Id, Entity = entity, StartedAt = clock.UtcNow, Status = SyncRunStatus.Running, Trigger = trigger };
        db.ErpSyncRuns.Add(run);
        await db.SaveChangesAsync(ct);
        var cursor = await db.ErpSyncCursors.FirstOrDefaultAsync(x => x.ConnectionId == c.Id && x.Entity == entity, ct);
        if (cursor is null) { cursor = new ErpSyncCursor { ConnectionId = c.Id, Entity = entity }; db.ErpSyncCursors.Add(cursor); await db.SaveChangesAsync(ct); }
        using var http = handlers.Create();
        var ctx = await ContextAsync(c, map, http);
        DateTimeOffset? maxSeen = null;
        try
        {
            var pageToken = cursor.PageToken;
            for (var p = 0; p < MaxPagesPerRun; p++)
            {
                var page = await adapter.ReadAsync(ctx, entity, new ErpCursor(cursor.Watermark, pageToken), ct);
                await ProcessPageAsync(c, map, run, page.Records, ct);
                foreach (var r in page.Records) if (r.LastModified is { } lm && (maxSeen is null || lm > maxSeen)) maxSeen = lm;
                pageToken = page.NextPageToken;
                cursor.PageToken = pageToken;   // resume point if a later page fails
                run.Requests = ctx.Http.Requests;
                await db.SaveChangesAsync(ct);
                if (pageToken is null) break;
            }
            if (pageToken is null)
            {
                if (maxSeen is { } w) cursor.Watermark = w - Overlap;   // overlap window: rows changed during the run are read again and skipped by hash
                cursor.PageToken = null;
            }
            run.Status = run.Rejected > 0 ? SyncRunStatus.PartiallySucceeded : SyncRunStatus.Succeeded;
        }
        catch (Exception e) when (e is ErpException or HttpRequestException or TaskCanceledException or CryptographicException or JsonException or AppException)
        {
            run.Status = SyncRunStatus.Failed; run.Error = e.Message.Length > 2000 ? e.Message[..2000] : e.Message;
            tokens.InvalidateAll(c.Id);
        }
        run.FinishedAt = clock.UtcNow; run.Requests = ctx.Http.Requests;
        map.NextRunAt = Next(map.Cron, clock.UtcNow);
        db.ChangeTracker.Clear();
        db.ErpSyncRuns.Update(run); db.ErpSyncCursors.Update(cursor); db.ErpEntityMappings.Update(map);
        await db.SaveChangesAsync(ct);
        if (run.Status == SyncRunStatus.Failed)
            await notifier.NotifyAsync([c.OwnerId], $"ERP sync failed: {c.Name} / {entity}", run.Error, null, ct: ct);
        return new SyncResult(run.Id, run.Status, run.Read, run.Created, run.Updated, run.Unchanged, run.Queued, run.Rejected, run.Requests, run.Error);
    }

    /// <summary>Imports an uploaded CSV through the same pipeline (CsvExchange connections).</summary>
    public async Task<SyncResult> ImportCsvAsync(Guid id, ErpEntity entity, string text, CancellationToken ct)
    {
        var c = await EditableAsync(id, ct);
        if (c.System != ErpSystem.CsvExchange) throw new AppException("CSV upload is for CSV exchange connections.");
        if (!c.Enabled) throw new AppException("The connection is disabled.");
        var map = await db.ErpEntityMappings.FirstOrDefaultAsync(x => x.ConnectionId == c.Id && x.Entity == entity, ct) ?? throw new AppException($"No mapping for {entity}.");
        using var http = handlers.Create();
        var ctx = await ContextAsync(c, map, http);
        var csv = new CsvExchangeAdapter();
        var rows = CsvExchangeAdapter.Parse(text);
        List<ExternalRecord> records;
        if (entity == ErpEntity.PurchaseOrder)
        {
            // one row per PO line: group by PO number
            records = rows.GroupBy(r => r.GetValueOrDefault("number") ?? "").Where(g => g.Key.Length > 0).Select(g =>
            {
                var head = g.First();
                var fields = new JsonObject { ["number"] = g.Key, ["supplierCode"] = head.GetValueOrDefault("supplierCode"), ["orderedOn"] = head.GetValueOrDefault("orderedOn") };
                fields["lines"] = new JsonArray(g.Select(r => (JsonNode)new JsonObject { ["lineNumber"] = r.GetValueOrDefault("lineNumber"), ["item"] = r.GetValueOrDefault("item"), ["revision"] = r.GetValueOrDefault("revision"), ["quantity"] = r.GetValueOrDefault("quantity"), ["dueOn"] = r.GetValueOrDefault("dueOn") }).ToArray());
                return new ExternalRecord(g.Key, null, null, fields, JsonSerializer.Serialize(g.ToList()));
            }).ToList();
        }
        else records = rows.Select(r => csv.ToRecord(ctx, entity, r)).ToList();
        var run = new ErpSyncRun { ConnectionId = c.Id, Entity = entity, StartedAt = clock.UtcNow, Status = SyncRunStatus.Running, Trigger = "CSV upload" };
        db.ErpSyncRuns.Add(run);
        await ProcessPageAsync(c, map, run, records, ct);
        run.Status = run.Rejected > 0 ? SyncRunStatus.PartiallySucceeded : SyncRunStatus.Succeeded; run.FinishedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return new SyncResult(run.Id, run.Status, run.Read, run.Created, run.Updated, run.Unchanged, run.Queued, run.Rejected, 0, null);
    }

    public static string Hash(JsonObject fields)
    {
        var json = Canonical(fields).ToJsonString();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    static JsonNode? Canonical(JsonNode? n) => n switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Canonical(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Canonical).ToArray()),
        _ => n?.DeepClone()
    };

    async Task ProcessPageAsync(ErpConnection c, ErpEntityMapping map, ErpSyncRun run, IReadOnlyList<ExternalRecord> records, CancellationToken ct)
    {
        var rules = ParseRules(map.ValidationRules);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var r in records)
        {
            run.Read++;
            var hash = Hash(r.Fields);
            var link = await db.ErpExternalLinks.FirstOrDefaultAsync(x => x.ConnectionId == c.Id && x.Entity == map.Entity && x.ExternalId == r.ExternalId, ct);
            if (link is not null && link.PayloadHash == hash) { run.Unchanged++; continue; }
            if (await db.ErpChanges.AnyAsync(x => x.ConnectionId == c.Id && x.Entity == map.Entity && x.ExternalId == r.ExternalId && x.PayloadHash == hash
                    && (x.Status == ChangeQueueStatus.Pending || x.Status == ChangeQueueStatus.PendingApproval || x.Status == ChangeQueueStatus.Rejected), ct))
            { run.Unchanged++; continue; }
            var errors = Validate(map.Entity, r.Fields, rules);
            var change = new ErpChange
            {
                Number = await numbers.NextAsync(ChangeType, c.SiteId, ct), ConnectionId = c.Id, Entity = map.Entity, Direction = SyncDirection.Inbound, ExternalId = r.ExternalId,
                Action = link is null ? "Create" : "Update", After = r.Fields.ToJsonString(), PayloadHash = hash, RunId = run.Id, SiteId = c.SiteId, DepartmentId = c.DepartmentId,
                Before = link is null ? null : await SnapshotAsync(link, ct)
            };
            db.ErpChanges.Add(change);
            // older pending changes for the same external record are superseded by this one
            foreach (var old in await db.ErpChanges.Where(x => x.ConnectionId == c.Id && x.Entity == map.Entity && x.ExternalId == r.ExternalId && x.Status == ChangeQueueStatus.Pending).ToListAsync(ct))
                old.Status = ChangeQueueStatus.Superseded;
            if (errors.Count == 0) errors.AddRange(await ReferenceErrorsAsync(map.Entity, r.Fields, ct));
            if (errors.Count > 0) { change.Status = ChangeQueueStatus.Rejected; change.ValidationErrors = string.Join("\n", errors); run.Rejected++; continue; }
            var manual = map.Mode == SyncMode.ApprovalRequired || (link is not null && map.Conflict == ConflictPolicy.Manual && map.Mode == SyncMode.Auto && await LocalChangedAsync(link, ct));
            if (link is not null && map.Conflict == ConflictPolicy.QmsWins && await LocalChangedAsync(link, ct))
            { change.Status = ChangeQueueStatus.Rejected; change.ValidationErrors = "Not applied: the QMS record changed since the last sync and the conflict policy is QMS wins."; run.Rejected++; continue; }
            if (manual) { change.Status = ChangeQueueStatus.Pending; run.Queued++; continue; }
            await db.SaveChangesAsync(ct);
            var created = await ApplyAsync(c, change, ct);
            if (created) run.Created++; else run.Updated++;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    async Task<bool> LocalChangedAsync(ErpExternalLink link, CancellationToken ct)
    {
        var updated = link.LocalType switch
        {
            "Part" => await db.Parts.Where(x => x.Id == link.LocalId).Select(x => (DateTimeOffset?)x.UpdatedAt).FirstOrDefaultAsync(ct),
            "Customer" => await db.Customers.Where(x => x.Id == link.LocalId).Select(x => (DateTimeOffset?)x.UpdatedAt).FirstOrDefaultAsync(ct),
            "Supplier" => await db.Suppliers.Where(x => x.Id == link.LocalId).Select(x => (DateTimeOffset?)x.UpdatedAt).FirstOrDefaultAsync(ct),
            _ => null
        };
        return updated is { } u && u > link.LastSyncedAt.AddSeconds(1);
    }

    async Task<string?> SnapshotAsync(ErpExternalLink link, CancellationToken ct)
    {
        object? o = link.LocalType switch
        {
            "Part" => await db.Parts.AsNoTracking().Where(x => x.Id == link.LocalId).Select(x => new { number = x.PartNumber, name = x.Name, revision = x.Revision, uom = x.UnitOfMeasure }).FirstOrDefaultAsync(ct),
            "Customer" => await db.Customers.AsNoTracking().Where(x => x.Id == link.LocalId).Select(x => new { code = x.Code, name = x.Name, email = x.Email }).FirstOrDefaultAsync(ct),
            "Supplier" => await db.Suppliers.AsNoTracking().Where(x => x.Id == link.LocalId).Select(x => new { code = x.Code, name = x.LegalName }).FirstOrDefaultAsync(ct),
            "PurchaseOrder" => await db.PurchaseOrders.AsNoTracking().Where(x => x.Id == link.LocalId).Select(x => new { number = x.Number, status = x.Status.ToString() }).FirstOrDefaultAsync(ct),
            "WorkOrder" => await db.WorkOrders.AsNoTracking().Where(x => x.Id == link.LocalId).Select(x => new { number = x.Number, item = x.ItemNumber, revision = x.Revision, quantity = x.Quantity }).FirstOrDefaultAsync(ct),
            _ => null
        };
        return o is null ? null : JsonSerializer.Serialize(o);
    }

    public sealed record Rule(string Field, bool Required, string? Pattern, int? Max, decimal? Min);

    static List<Rule> ParseRules(string json)
    {
        try
        {
            var arr = JsonNode.Parse(json) as JsonArray ?? [];
            return arr.OfType<JsonObject>().Select(o => new Rule(o["field"]?.GetValue<string>() ?? throw new AppException("Each validation rule needs a field."),
                o["required"]?.GetValue<bool>() ?? false, o["pattern"]?.GetValue<string>(), o["max"]?.GetValue<int>(), o["min"]?.GetValue<decimal>())).ToList();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { throw new AppException("Validation rules must be a JSON array of {field, required, pattern, max, min}."); }
    }

    static readonly Dictionary<ErpEntity, string[]> RequiredFields = new()
    {
        [ErpEntity.Item] = ["number"], [ErpEntity.Customer] = ["code"], [ErpEntity.Supplier] = ["code"], [ErpEntity.PurchaseOrder] = ["number", "supplierCode"],
        [ErpEntity.ProductionOrder] = ["number"], [ErpEntity.WorkCenter] = ["code"], [ErpEntity.Employee] = ["number"], [ErpEntity.Bom] = ["parent"], [ErpEntity.Lot] = ["number", "item"],
    };

    public static List<string> Validate(ErpEntity entity, JsonObject f, IReadOnlyList<Rule> rules)
    {
        var errors = new List<string>();
        if (!RequiredFields.ContainsKey(entity)) { errors.Add($"There is no QMS mapping for {entity} yet."); return errors; }
        string? S(string k) => f[k] is JsonValue v ? v.ToString() : f[k]?.ToJsonString();
        foreach (var k in RequiredFields[entity]) if (string.IsNullOrWhiteSpace(S(k))) errors.Add($"{k} is required.");
        foreach (var r in rules)
        {
            var v = S(r.Field);
            if (r.Required && string.IsNullOrWhiteSpace(v)) { errors.Add($"{r.Field} is required."); continue; }
            if (string.IsNullOrEmpty(v)) continue;
            if (r.Max is { } mx && v.Length > mx) errors.Add($"{r.Field} is longer than {mx} characters.");
            if (r.Pattern is { } p && !Regex.IsMatch(v, p, RegexOptions.None, TimeSpan.FromMilliseconds(200))) errors.Add($"{r.Field} '{v}' does not match {p}.");
            if (r.Min is { } mn && (!decimal.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var d) || d < mn)) errors.Add($"{r.Field} must be a number ≥ {mn}.");
        }
        if (entity == ErpEntity.PurchaseOrder && f["lines"] is JsonArray lines)
            foreach (var l in lines.OfType<JsonObject>())
                if (!decimal.TryParse(l["quantity"]?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var q) || q < 0) errors.Add($"PO line {l["lineNumber"]}: quantity must be a non-negative number.");
        return errors;
    }

    async Task<List<string>> ReferenceErrorsAsync(ErpEntity e, JsonObject f, CancellationToken ct)
    {
        var errors = new List<string>();
        if (e == ErpEntity.PurchaseOrder && Str(f, "supplierCode") is { } s && !await db.Suppliers.AnyAsync(x => x.Code == s, ct))
            errors.Add($"Supplier {s} is not in the QMS yet; sync suppliers first, then retry this change.");
        if (e == ErpEntity.Bom && Str(f, "parent") is { } p && !await db.Parts.AnyAsync(x => x.PartNumber == p, ct))
            errors.Add($"Parent part {p} is not in the QMS yet; sync items first.");
        if (e == ErpEntity.Lot && Str(f, "item") is { } i && !await db.Parts.AnyAsync(x => x.PartNumber == i, ct))
            errors.Add($"Part {i} is not in the QMS yet; sync items first.");
        return errors;
    }

    static string? Str(JsonObject f, string k) => f[k] switch { null => null, JsonValue v => v.ToString().Trim() is { Length: > 0 } s ? s : null, var n => n.ToJsonString() };
    static decimal Dec(JsonObject f, string k) => decimal.TryParse(Str(f, k), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
    static DateOnly? Day(JsonObject f, string k) => ErpWire.Date(f, k) is { } d ? DateOnly.FromDateTime(d.UtcDateTime) : null;

    /// <summary>Applies one validated change to the QMS master/transaction records and records the external link. Returns true when a record was created.</summary>
    async Task<bool> ApplyAsync(ErpConnection c, ErpChange change, CancellationToken ct)
    {
        var f = JsonNode.Parse(change.After) as JsonObject ?? new JsonObject();
        var link = await db.ErpExternalLinks.FirstOrDefaultAsync(x => x.ConnectionId == c.Id && x.Entity == change.Entity && x.ExternalId == change.ExternalId, ct);
        var today = await calendar.TodayAsync(c.SiteId, ct);
        (string Type, Guid Id, bool Created) result;
        switch (change.Entity)
        {
            case ErpEntity.Item:
            {
                var number = Str(f, "number")!;
                var part = link is not null ? await db.Parts.FirstOrDefaultAsync(x => x.Id == link.LocalId, ct) : await db.Parts.FirstOrDefaultAsync(x => x.PartNumber == number, ct);
                var created = part is null;
                var rev = Str(f, "revision");
                if (part is null)
                {
                    part = new Part { PartNumber = number, Revision = rev ?? "A", SiteId = c.SiteId, Status = MasterStatus.Active };
                    db.Parts.Add(part);
                    db.PartRevisions.Add(new PartRevision { PartId = part.Id, Revision = part.Revision, Reason = $"Initial revision from {c.Name}", EffectiveOn = today });
                }
                else if (rev is not null && !string.Equals(rev, part.Revision, StringComparison.Ordinal))
                {
                    // revision change from the ERP: keep history and flag that a delta FAI is due
                    db.PartRevisions.Add(new PartRevision { PartId = part.Id, Revision = rev, Reason = $"Revision {rev} from {c.Name} ({change.ExternalId}); first article required", EffectiveOn = today });
                    var old = part.Revision;
                    part.Revision = rev;
                    change.DecisionNote = $"Revision {old} → {rev}: first article inspection required.";
                    await db.SaveChangesAsync(ct);
                    await notifier.NotifyAsync([c.OwnerId], $"FAI required: {number} rev {rev}", $"{c.Name} changed {number} from rev {old} to rev {rev}.", await m.RecordAsync(MasterData.MasterDataService.PartRecordType, part.Id, ct), ct: ct);
                }
                part.Name = Str(f, "name") ?? Str(f, "description") ?? (part.Name.Length > 0 ? part.Name : number);
                if (Str(f, "uom") is { } uom) part.UnitOfMeasure = uom.Length > 10 ? uom[..10] : uom;
                if (Str(f, "description") is { } d && Str(f, "name") is not null) part.Description = d;
                result = ("Part", part.Id, created);
                break;
            }
            case ErpEntity.Customer:
            {
                var code = Str(f, "code")!;
                var cu = link is not null ? await db.Customers.FirstOrDefaultAsync(x => x.Id == link.LocalId, ct) : await db.Customers.FirstOrDefaultAsync(x => x.Code == code, ct);
                var created = cu is null;
                if (cu is null) { cu = new Customer { Code = code, SiteId = c.SiteId, Status = MasterStatus.Active }; db.Customers.Add(cu); }
                cu.Name = Str(f, "name") ?? code; if (Str(f, "email") is { } em) cu.Email = em;
                result = ("Customer", cu.Id, created);
                break;
            }
            case ErpEntity.Supplier:
            {
                var code = Str(f, "code")!;
                var s = link is not null ? await db.Suppliers.FirstOrDefaultAsync(x => x.Id == link.LocalId, ct) : await db.Suppliers.FirstOrDefaultAsync(x => x.Code == code, ct);
                var created = s is null;
                // New ERP vendors start as Prospective: qualification stays in the QMS approval workflow.
                if (s is null) { s = new Supplier { Code = code, SiteId = c.SiteId, Status = SupplierStatus.Prospective, Category = "ERP", OwnerId = c.OwnerId }; db.Suppliers.Add(s); }
                s.LegalName = Str(f, "name") ?? code;
                result = ("Supplier", s.Id, created);
                break;
            }
            case ErpEntity.PurchaseOrder:
            {
                var number = Str(f, "number")!;
                var supplier = await db.Suppliers.FirstAsync(x => x.Code == Str(f, "supplierCode"), ct);
                var po = link is not null ? await db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == link.LocalId, ct) : await db.PurchaseOrders.FirstOrDefaultAsync(x => x.Number == number, ct);
                var created = po is null;
                if (po is null) { po = new PurchaseOrder { Number = number, Source = RecordSource.Erp, SiteId = c.SiteId }; db.PurchaseOrders.Add(po); }
                po.SupplierId = supplier.Id; po.OrderedOn = Day(f, "orderedOn") ?? po.OrderedOn; po.ExternalRef = change.ExternalId;
                if (f["lines"] is JsonArray lines)
                {
                    var existing = await db.PurchaseOrderLines.Where(x => x.PurchaseOrderId == po.Id).ToListAsync(ct);
                    var n = 0;
                    foreach (var l in lines.OfType<JsonObject>())
                    {
                        n++;
                        var ln = int.TryParse(Str(l, "lineNumber"), out var li) ? li : n * 10;
                        var line = existing.FirstOrDefault(x => x.LineNumber == ln);
                        if (line is null) { line = new PurchaseOrderLine { PurchaseOrderId = po.Id, LineNumber = ln }; db.PurchaseOrderLines.Add(line); existing.Add(line); }
                        line.ItemNumber = Str(l, "item") ?? ""; line.Revision = Str(l, "revision"); line.Quantity = Dec(l, "quantity"); line.DueOn = Day(l, "dueOn") ?? line.DueOn;
                        if (Str(l, "uom") is { } u) line.UnitOfMeasure = u;
                        line.PartId = line.ItemNumber.Length == 0 ? null : await db.Parts.Where(x => x.PartNumber == line.ItemNumber).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
                    }
                }
                result = ("PurchaseOrder", po.Id, created);
                break;
            }
            case ErpEntity.ProductionOrder:
            {
                var number = Str(f, "number")!;
                var wo = link is not null ? await db.WorkOrders.FirstOrDefaultAsync(x => x.Id == link.LocalId, ct) : await db.WorkOrders.FirstOrDefaultAsync(x => x.Number == number, ct);
                var created = wo is null;
                if (wo is null) { wo = new WorkOrder { Number = number, Source = RecordSource.Erp, SiteId = c.SiteId, Kind = c.System is ErpSystem.Epicor or ErpSystem.Infor && c.Edition == InforAdapter.Mongoose ? WorkOrderKind.Job : WorkOrderKind.ProductionOrder }; db.WorkOrders.Add(wo); }
                wo.ItemNumber = Str(f, "item") ?? wo.ItemNumber; wo.Revision = Str(f, "revision") ?? wo.Revision; wo.Quantity = Dec(f, "quantity"); wo.DueOn = Day(f, "dueOn") ?? wo.DueOn; wo.ExternalRef = change.ExternalId;
                wo.PartId = await db.Parts.Where(x => x.PartNumber == wo.ItemNumber).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
                var st = (Str(f, "status") ?? "").ToLowerInvariant();
                wo.Status = st.Contains("clos") ? WorkOrderStatus.Closed : st.Contains("complet") || st.Contains("built") ? WorkOrderStatus.Completed : st.Contains("releas") ? WorkOrderStatus.Released
                    : st.Contains("progress") || st.Contains("start") ? WorkOrderStatus.InProcess : st.Contains("cancel") ? WorkOrderStatus.Cancelled : wo.Status;
                result = ("WorkOrder", wo.Id, created);
                break;
            }
            case ErpEntity.WorkCenter:
            {
                var code = Str(f, "code")!;
                var w = await db.WorkCenters.FirstOrDefaultAsync(x => x.SiteId == c.SiteId && x.Code == code, ct);
                var created = w is null;
                if (w is null) { w = new WorkCenter { Code = code, SiteId = c.SiteId, Source = RecordSource.Erp }; db.WorkCenters.Add(w); }
                w.Name = Str(f, "name") ?? code;
                result = ("WorkCenter", w.Id, created);
                break;
            }
            case ErpEntity.Employee:
            {
                var num = Str(f, "number")!;
                var e = await db.Employees.FirstOrDefaultAsync(x => x.Number == num, ct);
                var created = e is null;
                if (e is null) { e = new Employee { Number = num, SiteId = c.SiteId, Source = RecordSource.Erp }; db.Employees.Add(e); }
                e.Name = Str(f, "name") ?? num;
                result = ("Employee", e.Id, created);
                break;
            }
            case ErpEntity.Bom:
            {
                var parent = await db.Parts.FirstAsync(x => x.PartNumber == Str(f, "parent"), ct);
                var rev = Str(f, "revision");
                var old = await db.BomLines.Where(x => x.ParentPartId == parent.Id && x.ParentRevision == rev).ToListAsync(ct);
                db.BomLines.RemoveRange(old);
                foreach (var l in (f["lines"] as JsonArray ?? []).OfType<JsonObject>())
                    if (Str(l, "component") is { } comp && await db.Parts.Where(x => x.PartNumber == comp).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct) is { } cid)
                        db.BomLines.Add(new BomLine { ParentPartId = parent.Id, ParentRevision = rev, ComponentPartId = cid, Quantity = Dec(l, "quantity"), Source = RecordSource.Erp });
                result = ("Part", parent.Id, false);
                break;
            }
            case ErpEntity.Lot:
            {
                var num = Str(f, "number")!;
                var part = await db.Parts.FirstAsync(x => x.PartNumber == Str(f, "item"), ct);
                var lot = await db.Lots.FirstOrDefaultAsync(x => x.Number == num && x.PartId == part.Id, ct);
                var created = lot is null;
                if (lot is null) { lot = new Lot { Number = num, PartId = part.Id, Kind = LotKind.Component, SiteId = c.SiteId, Source = RecordSource.Erp }; db.Lots.Add(lot); }
                result = ("Lot", lot.Id, created);
                break;
            }
            default: throw new AppException($"There is no QMS mapping for {change.Entity}.");
        }
        if (link is null)
        {
            link = new ErpExternalLink { ConnectionId = c.Id, Entity = change.Entity, ExternalId = change.ExternalId, LocalType = result.Type, LocalId = result.Id };
            db.ErpExternalLinks.Add(link);
        }
        link.PayloadHash = change.PayloadHash; link.LastSyncedAt = clock.UtcNow; link.LocalId = result.Id; link.LocalType = result.Type;
        change.Status = ChangeQueueStatus.Applied; change.AppliedAt = clock.UtcNow; change.LocalType = result.Type; change.LocalId = result.Id;
        await db.SaveChangesAsync(ct);
        return result.Created;
    }

    // ------------------------------------------------------------------ change queue
    public async Task<List<ErpChange>> ChangesAsync(Guid? connectionId, ChangeQueueStatus? status, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.ErpChanges.AsNoTracking(), Permissions.IntegrationsView, ct);
        if (connectionId is { } c) q = q.Where(x => x.ConnectionId == c);
        if (status is { } s) q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    }

    public async Task<ErpChange> ChangeAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ChangeType, id, ct);
        return await db.ErpChanges.AsNoTracking().FirstAsync(x => x.Id == id, ct);
    }

    public async Task SubmitChangeAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(ChangeType, id, ct);
        var ch = await db.ErpChanges.FirstAsync(x => x.Id == id, ct);
        if (ch.Status != ChangeQueueStatus.Pending) throw new ConflictException("Only pending changes can be submitted for approval.");
        await workflow.SubmitAsync(ChangeType, id, ct);
        ch.Status = ChangeQueueStatus.PendingApproval;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Re-validates a rejected change (for example after its supplier was synced) and queues or applies it.</summary>
    public async Task RetryChangeAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(ChangeType, id, ct);
        var ch = await db.ErpChanges.FirstAsync(x => x.Id == id, ct);
        if (ch.Status is not (ChangeQueueStatus.Rejected or ChangeQueueStatus.Failed)) throw new ConflictException("Only rejected or failed changes can be retried.");
        var map = await db.ErpEntityMappings.FirstAsync(x => x.ConnectionId == ch.ConnectionId && x.Entity == ch.Entity, ct);
        var f = JsonNode.Parse(ch.After) as JsonObject ?? new JsonObject();
        var errors = Validate(ch.Entity, f, ParseRules(map.ValidationRules));
        if (errors.Count == 0) errors.AddRange(await ReferenceErrorsAsync(ch.Entity, f, ct));
        ch.ValidationErrors = errors.Count == 0 ? null : string.Join("\n", errors);
        if (errors.Count == 0)
        {
            if (map.Mode == SyncMode.Auto) { await db.SaveChangesAsync(ct); await ApplyAsync(await db.ErpConnections.FirstAsync(x => x.Id == ch.ConnectionId, ct), ch, ct); return; }
            ch.Status = ChangeQueueStatus.Pending;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var ch = await db.ErpChanges.FirstAsync(x => x.Id == id, ct);
        var c = await db.ErpConnections.FirstAsync(x => x.Id == ch.ConnectionId, ct);
        var f = JsonNode.Parse(ch.After) as JsonObject ?? new JsonObject();
        var refs = await ReferenceErrorsAsync(ch.Entity, f, ct);
        if (refs.Count > 0) { ch.Status = ChangeQueueStatus.Failed; ch.ValidationErrors = string.Join("\n", refs); await db.SaveChangesAsync(ct); return; }
        await ApplyAsync(c, ch, ct);
    }

    public async Task OnRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var ch = await db.ErpChanges.FirstAsync(x => x.Id == id, ct);
        ch.Status = ChangeQueueStatus.Rejected; ch.DecisionNote = comment;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ErpSyncRun>> RunsAsync(Guid id, CancellationToken ct)
    {
        await GetAsync(id, ct);
        return await db.ErpSyncRuns.AsNoTracking().Where(x => x.ConnectionId == id).OrderByDescending(x => x.StartedAt).Take(100).ToListAsync(ct);
    }

    /// <summary>Scheduler entry (system identity): runs every enabled inbound mapping whose next run is due.</summary>
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await (from map in db.ErpEntityMappings join c in db.ErpConnections on map.ConnectionId equals c.Id
                         where c.Enabled && map.Mode != SyncMode.Disabled && (map.NextRunAt == null || map.NextRunAt <= now) && c.System != ErpSystem.CsvExchange
                         select new { c.Id, map.Entity }).ToListAsync(ct);
        foreach (var d in due)
        {
            try { await RunAsync(d.Id, d.Entity, "Schedule", ct); }
            catch (AppException) { }
            db.ChangeTracker.Clear();
        }
        return due.Count;
    }

    /// <summary>The site's inventory is mirrored read-only when an enabled connection there is the inventory system of record.</summary>
    public Task<bool> IsMirrorSiteAsync(Guid siteId, CancellationToken ct) => db.ErpConnections.AnyAsync(x => x.SiteId == siteId && x.Enabled && x.InventorySystemOfRecord, ct);
}
