using System.Net;
using System.Net.Sockets;

namespace Codex.Application.Network;

// Whether an address is on the public internet. A look-up never connects anywhere else.
public static class PublicAddress
{
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            // ::/96 (IPv4-compatible), 64:ff9b::/96 (NAT64), 2002::/16 (6to4) and 2001::/32 (Teredo) can carry any
            // IPv4 address, so none is a public host.
            var embedsIpv4 = bytes.Take(12).All(x => x == 0)
                             || (bytes[0] == 0 && bytes[1] == 0x64 && bytes[2] == 0xff && bytes[3] == 0x9b && bytes.Skip(4).Take(8).All(x => x == 0))
                             || (bytes[0] == 0x20 && bytes[1] == 0x02)
                             || (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0 && bytes[3] == 0);
            return !(embedsIpv4 || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (bytes[0] & 0xFE) == 0xFC);
        }

        var b = address.GetAddressBytes();
        return !(b[0] is 0 or 10 or >= 224
                 || (b[0] == 100 && b[1] is >= 64 and <= 127)
                 || (b[0] == 169 && b[1] == 254)
                 || (b[0] == 172 && b[1] is >= 16 and <= 31)
                 || (b[0] == 192 && b[1] == 168)
                 || (b[0] == 198 && b[1] is 18 or 19)
                 || (b[0] == 192 && b[1] == 0 && b[2] == 0));
    }
}
