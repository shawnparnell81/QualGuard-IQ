using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace QualGuardIQ.Infrastructure.Security;

/// <summary>
/// Keeps addresses that administrators type in (webhooks, Teams and SMS targets, print agents, carriers, ERP, messaging, generation
/// providers, single sign-on providers) from
/// reaching the server itself or the internal network. The host name is looked up once and the connection goes to the address
/// that was checked, so a name can't switch to an internal address between the check and the connection.
/// Blocked unless listed in Outbound:AllowedNetworks: loopback, private (10/8, 172.16/12, 192.168/16), link-local
/// (including cloud metadata 169.254.169.254), shared carrier ranges (100.64/10), IPv6 local ranges, multicast and reserved.
/// Raw network printers may only use Outbound:PrinterNetworks (default: the private ranges, never loopback or link-local).
/// </summary>
public sealed class OutboundGuard
{
    static readonly IPNetwork[] Internal =
    [
        IPNetwork.Parse("0.0.0.0/8"), IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("100.64.0.0/10"), IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"), IPNetwork.Parse("172.16.0.0/12"), IPNetwork.Parse("192.0.0.0/24"), IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.168.0.0/16"), IPNetwork.Parse("198.18.0.0/15"), IPNetwork.Parse("198.51.100.0/24"), IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/4"), IPNetwork.Parse("240.0.0.0/4"),
        IPNetwork.Parse("::/128"), IPNetwork.Parse("::1/128"), IPNetwork.Parse("64:ff9b:1::/48"), IPNetwork.Parse("100::/64"), IPNetwork.Parse("2001:db8::/32"),
        IPNetwork.Parse("fc00::/7"), IPNetwork.Parse("fe80::/10"), IPNetwork.Parse("ff00::/8"),
    ];
    const string DefaultPrinterNetworks = "10.0.0.0/8,172.16.0.0/12,192.168.0.0/16";

    readonly IPNetwork[] _allowed;
    readonly IPNetwork[] _printers;

    public OutboundGuard(IConfiguration config)
    {
        _allowed = Parse(config["Outbound:AllowedNetworks"], "Outbound:AllowedNetworks");
        _printers = Parse(config["Outbound:PrinterNetworks"] is { Length: > 0 } p ? p : DefaultPrinterNetworks, "Outbound:PrinterNetworks");
    }

    /// <summary>Reads a comma-separated list of addresses or ranges from a setting; a bad entry stops start-up with the setting's name.</summary>
    public static IPNetwork[] Parse(string? value, string setting) =>
        (value ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => IPNetwork.TryParse(n.Contains('/') ? n : n + (n.Contains(':') ? "/128" : "/32"), out var net) ? net
                : throw new InvalidOperationException($"{setting} has an invalid address or range: {n}"))
            .ToArray();

    static IPAddress Normal(IPAddress a) => a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a;

    /// <summary>True when an address typed by an administrator may be called.</summary>
    public bool IsAllowed(IPAddress address)
    {
        var a = Normal(address);
        return _allowed.Any(n => n.Contains(a)) || !Internal.Any(n => n.Contains(a));
    }

    /// <summary>True when a raw network printer may be at this address.</summary>
    public bool IsPrinterAllowed(IPAddress address)
    {
        var a = Normal(address);
        return _allowed.Any(n => n.Contains(a)) || _printers.Any(n => n.Contains(a)) && !IPAddress.IsLoopback(a) && !IPNetwork.Parse("169.254.0.0/16").Contains(a);
    }

    /// <summary>Looks the host up and returns the addresses that pass <paramref name="rule"/>; refuses when none do.</summary>
    public static async Task<IPAddress[]> ResolveAsync(string host, Func<IPAddress, bool> rule, CancellationToken ct)
    {
        var all = IPAddress.TryParse(host.Trim('[', ']'), out var ip) ? [ip] : await Dns.GetHostAddressesAsync(host, ct);
        var ok = all.Where(rule).ToArray();
        if (ok.Length == 0)
            throw new HttpRequestException($"{host} is an internal address, which is not allowed here. An administrator can allow it with Outbound:AllowedNetworks.");
        return ok;
    }

    /// <summary>True when the address is allowed by the general rule or is inside one of <paramref name="extra"/>. Link-local
    /// addresses (cloud metadata) stay blocked unless Outbound:AllowedNetworks lists them, whatever <paramref name="extra"/> says.</summary>
    public bool IsAllowed(IPAddress address, IReadOnlyCollection<IPNetwork> extra)
    {
        var a = Normal(address);
        return IsAllowed(a) || extra.Any(n => n.Contains(a)) && !LinkLocal.Any(n => n.Contains(a));
    }
    static readonly IPNetwork[] LinkLocal = [IPNetwork.Parse("169.254.0.0/16"), IPNetwork.Parse("fe80::/10")];

    /// <summary>An HTTP handler whose connections pass the check above.</summary>
    public SocketsHttpHandler Handler(Action<SocketsHttpHandler>? configure = null) => Handler(IsAllowed, configure);

    /// <summary>An HTTP handler whose connections pass the general check or go to one of <paramref name="extra"/> (never link-local).</summary>
    public SocketsHttpHandler Handler(IReadOnlyCollection<IPNetwork> extra, Action<SocketsHttpHandler>? configure = null) =>
        extra.Count == 0 ? Handler(configure) : Handler(a => IsAllowed(a, extra), configure);

    SocketsHttpHandler Handler(Func<IPAddress, bool> rule, Action<SocketsHttpHandler>? configure)
    {
        var h = new SocketsHttpHandler { ConnectCallback = (ctx, ct) => ConnectAsync(ctx, rule, ct) };
        configure?.Invoke(h);
        return h;
    }

    static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext ctx, Func<IPAddress, bool> rule, CancellationToken ct)
    {
        var addresses = await ResolveAsync(ctx.DnsEndPoint.Host, rule, ct);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, ctx.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
