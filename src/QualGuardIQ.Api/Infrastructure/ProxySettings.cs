using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace QualGuardIQ.Api.Infrastructure;

/// <summary>
/// Which reverse proxies may report the real client address (X-Forwarded-For) and scheme (X-Forwarded-Proto). Only requests that
/// arrive from a trusted proxy have the headers applied, and only the last hop is used, so a client can't fake its address to dodge
/// the sign-in limit. Configure with ForwardedHeaders:KnownProxies and ForwardedHeaders:KnownNetworks (comma-separated addresses or
/// CIDR ranges). Loopback is always trusted. Docker compose sets the compose network.
/// </summary>
public static class ProxySettings
{
    public static void Apply(ForwardedHeadersOptions o, IConfiguration config)
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1;
        foreach (var p in Split(config["ForwardedHeaders:KnownProxies"]))
            o.KnownProxies.Add(IPAddress.TryParse(p, out var ip) ? ip : throw new InvalidOperationException($"ForwardedHeaders:KnownProxies has an invalid address: {p}"));
        foreach (var n in Split(config["ForwardedHeaders:KnownNetworks"]))
            o.KnownIPNetworks.Add(System.Net.IPNetwork.TryParse(n, out var net) ? net : throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks has an invalid range: {n}"));
    }

    static IEnumerable<string> Split(string? v) => (v ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
