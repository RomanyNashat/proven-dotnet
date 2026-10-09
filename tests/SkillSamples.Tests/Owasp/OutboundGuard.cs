using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Owasp;

public sealed class BlockedDestinationException(string host, IPAddress address)
    : IOException($"Outbound call to {host} refused: it resolves to {address}, which is not a public address.");

/// <summary>
/// For calls to a URL someone else chose (a partner's webhook, an image URL in a request). The check runs on
/// the address the socket is about to connect to, after DNS, on every connection including redirects. A check
/// on the URL's host name can't do that: the name can resolve to 10.x, 127.0.0.1 or the cloud metadata
/// address, and can resolve differently a second later (DNS rebinding).
/// </summary>
public static class OutboundGuard
{
    private static readonly IPNetwork[] NotPublic =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),     // carrier-grade NAT, used inside some clusters
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),    // link-local, including the cloud metadata service 169.254.169.254
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),
        IPNetwork.Parse("::/128"),
        IPNetwork.Parse("::1/128"),
        IPNetwork.Parse("fc00::/7"),          // unique local
        IPNetwork.Parse("fe80::/10"),         // link-local
        IPNetwork.Parse("ff00::/8"),
    ];

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();   // ::ffff:10.0.0.1 is 10.0.0.1
        }

        return !NotPublic.Any(network => network.Contains(address));
    }

    public static IHttpClientBuilder AddPublicOnlyHttpClient(this IServiceCollection services, string name) =>
        services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => CreateHandler());

    public static SocketsHttpHandler CreateHandler() => new()
    {
        // Through a proxy, the socket connects to the proxy, so this check would only ever see the proxy's
        // address. A client that must use a proxy needs the proxy to block internal addresses instead.
        UseProxy = false,
        ConnectCallback = async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = await Dns.GetHostAddressesAsync(host, ct);
            if (addresses.Length == 0)
            {
                throw new HttpRequestException($"{host} did not resolve.");
            }

            // Refuse if ANY address is internal: a name with one public and one private record is an attack.
            if (addresses.FirstOrDefault(a => !IsPublic(a)) is { } blocked)
            {
                throw new BlockedDestinationException(host, blocked);
            }

            // Connect to the addresses just checked, not to the name: resolving again could give another answer.
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}
