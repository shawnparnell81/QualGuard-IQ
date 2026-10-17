using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Sockets;
using QualGuardIQ.Domain.Quality;

namespace QualGuardIQ.Infrastructure.Modules.Logistics;

/// <summary>A print attempt that did not reach paper. Offline = the printer or agent could not be reached (retry later).</summary>
public sealed class PrintFailure(string message, bool offline) : Exception(message) { public bool Offline => offline; }

public interface IPrintTransport
{
    PrinterDriver Driver { get; }
    Task SendAsync(LabelPrinter printer, string? secret, byte[] payload, string contentType, CancellationToken ct);
}

/// <summary>
/// Test printer with no physical device: records every payload it receives (newest 500, per process) so tests and demonstrations can
/// see the output. Address selects a behaviour: empty or "ok" prints, "offline" is unreachable, "error" reports a printer fault,
/// "flaky" fails every other attempt.
/// </summary>
public sealed class VirtualPrintTransport : IPrintTransport
{
    public sealed record Output(Guid PrinterId, string ContentType, byte[] Payload, DateTimeOffset At);
    static readonly ConcurrentQueue<Output> Outputs = new();
    static readonly ConcurrentDictionary<Guid, int> Calls = new();
    public static IReadOnlyList<Output> Received(Guid printerId) => Outputs.Where(o => o.PrinterId == printerId).ToList();
    public PrinterDriver Driver => PrinterDriver.Virtual;

    public Task SendAsync(LabelPrinter printer, string? secret, byte[] payload, string contentType, CancellationToken ct)
    {
        var n = Calls.AddOrUpdate(printer.Id, 1, (_, v) => v + 1);
        switch ((printer.Address ?? "ok").Trim().ToLowerInvariant())
        {
            case "offline": throw new PrintFailure("Printer offline: no response from the virtual printer.", offline: true);
            case "error": throw new PrintFailure("Printer reported an error: media out (virtual printer).", offline: false);
            case "flaky" when n % 2 == 1: throw new PrintFailure("Printer offline: connection dropped (virtual printer).", offline: true);
        }
        Outputs.Enqueue(new Output(printer.Id, contentType, payload, DateTimeOffset.UtcNow));
        while (Outputs.Count > 500) Outputs.TryDequeue(out _);
        return Task.CompletedTask;
    }
}

/// <summary>Raw TCP to a network label printer (ZPL; port 9100 unless the address names another). 5-second connect and send timeout.</summary>
public sealed class RawTcpPrintTransport : IPrintTransport
{
    public PrinterDriver Driver => PrinterDriver.RawTcp;
    public static (string Host, int Port) Parse(string? address)
    {
        var a = (address ?? "").Trim();
        if (a.Length == 0) throw new PrintFailure("The printer has no network address.", offline: false);
        var i = a.LastIndexOf(':');
        if (i > 0 && int.TryParse(a[(i + 1)..], out var port) && port is > 0 and < 65536) return (a[..i], port);
        return (a, 9100);
    }

    public async Task SendAsync(LabelPrinter printer, string? secret, byte[] payload, string contentType, CancellationToken ct)
    {
        var (host, port) = Parse(printer.Address);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cts.Token);
            await using var s = client.GetStream();
            await s.WriteAsync(payload, cts.Token);
            await s.FlushAsync(cts.Token);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or IOException)
        {
            if (ct.IsCancellationRequested) throw;
            throw new PrintFailure($"Printer offline: {host}:{port} did not accept the label ({(e is OperationCanceledException ? "timed out" : e.Message)}).", offline: true);
        }
    }
}

/// <summary>Posts the label to a print agent (HTTPS, or HTTP on the same machine) with an optional bearer token. 10-second timeout.</summary>
public sealed class HttpRelayPrintTransport(IHttpClientFactory http) : IPrintTransport
{
    public PrinterDriver Driver => PrinterDriver.HttpRelay;
    public static Uri Validate(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var u) || !(u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback)))
            throw new PrintFailure("The print agent address must be an https:// URL (http:// only on the same machine).", offline: false);
        return u;
    }

    public async Task SendAsync(LabelPrinter printer, string? secret, byte[] payload, string contentType, CancellationToken ct)
    {
        var u = Validate(printer.Address);
        var c = http.CreateClient("print");
        using var req = new HttpRequestMessage(HttpMethod.Post, u) { Content = new ByteArrayContent(payload) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        req.Headers.Add("X-Printer", printer.Code);
        if (!string.IsNullOrEmpty(secret)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        HttpResponseMessage res;
        try { res = await c.SendAsync(req, ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            throw new PrintFailure($"Printer offline: the print agent at {u.Host} could not be reached ({(e is TaskCanceledException ? "timed out" : e.Message)}).", offline: true);
        }
        using (res)
            if (!res.IsSuccessStatusCode)
                throw new PrintFailure($"The print agent refused the label: HTTP {(int)res.StatusCode} {res.ReasonPhrase}.", offline: (int)res.StatusCode is 502 or 503 or 504);
    }
}
