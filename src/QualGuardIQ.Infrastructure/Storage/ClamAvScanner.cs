using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QualGuardIQ.Domain.Shared;

namespace QualGuardIQ.Infrastructure.Storage;

/// <summary>
/// Scans uploads with a local ClamAV daemon (clamd) using the INSTREAM protocol over TCP or a Unix socket.
/// Enable with MalwareScanning:Provider = "ClamAV". When the daemon cannot be reached the result is Failed and
/// the upload is rejected unless MalwareScanning:AllowWhenUnavailable is true.
/// </summary>
public sealed class ClamAvScanner(IConfiguration config, ILogger<ClamAvScanner> log) : IMalwareScanner
{
    private const int ChunkSize = 64 * 1024;
    private readonly string? _socket = config["MalwareScanning:ClamAV:Socket"];
    private readonly string _host = config["MalwareScanning:ClamAV:Host"] ?? "127.0.0.1";
    private readonly int _port = config.GetValue("MalwareScanning:ClamAV:Port", 3310);
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(config.GetValue("MalwareScanning:ClamAV:TimeoutSeconds", 60));

    public async Task<ScanStatus> ScanAsync(Stream content, string fileName, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        try
        {
            using var socket = _socket is { Length: > 0 }
                ? new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
                : new Socket(SocketType.Stream, ProtocolType.Tcp);
            if (_socket is { Length: > 0 }) await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socket), cts.Token);
            else await socket.ConnectAsync(_host, _port, cts.Token);
            await using var net = new NetworkStream(socket, ownsSocket: false);
            await net.WriteAsync("zINSTREAM\0"u8.ToArray(), cts.Token);
            var buffer = new byte[ChunkSize];
            var size = new byte[4];
            int read;
            while ((read = await content.ReadAsync(buffer, cts.Token)) > 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(size, (uint)read);
                await net.WriteAsync(size, cts.Token);
                await net.WriteAsync(buffer.AsMemory(0, read), cts.Token);
            }
            BinaryPrimitives.WriteUInt32BigEndian(size, 0);
            await net.WriteAsync(size, cts.Token);
            using var reply = new MemoryStream();
            await net.CopyToAsync(reply, cts.Token);
            var text = Encoding.ASCII.GetString(reply.ToArray()).TrimEnd('\0', '\n', ' ');
            if (content.CanSeek) content.Position = 0;
            return Interpret(text, fileName);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            log.LogError(ex, "Malware scan of {FileName} failed: ClamAV not reachable", fileName);
            if (content.CanSeek) content.Position = 0;
            return ScanStatus.Failed;
        }
    }

    internal ScanStatus Interpret(string reply, string fileName)
    {
        if (reply.EndsWith("OK", StringComparison.Ordinal)) return ScanStatus.Clean;
        if (reply.EndsWith("FOUND", StringComparison.Ordinal))
        {
            log.LogWarning("Malware detected in {FileName}: {Reply}", fileName, reply);
            return ScanStatus.Infected;
        }
        log.LogError("Unexpected ClamAV reply for {FileName}: {Reply}", fileName, reply);
        return ScanStatus.Failed;
    }
}
