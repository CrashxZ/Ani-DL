using System.Net;
using System.Net.Sockets;

namespace Jellyfin.Plugin.AniDL.Security;

public static class RemoteUriGuard
{
    public static async Task EnsurePublicHttpsAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("Only absolute HTTPS media URLs without embedded credentials are allowed.");
        }

        _ = await GetPublicAddressesAsync(uri.DnsSafeHost, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IPAddress[]> GetPublicAddressesAsync(string host, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(IsNonPublic))
        {
            throw new InvalidOperationException("The media URL resolved to a non-public network address.");
        }

        return addresses;
    }

    private static bool IsNonPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                return IsNonPublic(address.MapToIPv4());
            }

            var ipv6Bytes = address.GetAddressBytes();
            return address.Equals(IPAddress.IPv6Any)
                || address.Equals(IPAddress.IPv6None)
                || address.IsIPv6LinkLocal
                || address.IsIPv6Multicast
                || (ipv6Bytes[0] & 0xfe) == 0xfc
                || (ipv6Bytes[0] == 0x20 && ipv6Bytes[1] == 0x01 && ipv6Bytes[2] == 0x0d && ipv6Bytes[3] == 0xb8);
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 0
            || bytes[0] == 10
            || bytes[0] == 127
            || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            || (bytes[0] == 169 && bytes[1] == 254)
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
            || (bytes[0] == 198 && bytes[1] is 18 or 19)
            || (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
            || (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
            || bytes[0] >= 224;
    }
}
