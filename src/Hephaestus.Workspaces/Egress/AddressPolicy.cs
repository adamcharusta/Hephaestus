using System.Net;
using System.Net.Sockets;

namespace Hephaestus.Workspaces.Egress;

/// <summary>
/// Which addresses the egress proxy will connect to (AD-7, T7, T12): public ones, and nothing else. A name that resolves to a loopback, link-local, or private
/// address would reach the Docker host, the Owner's local network, a cloud metadata service, or the dashboard port, so the request is refused, whatever the Project's list says.
/// The test is stricter than the three ranges the architecture names: every range that is reserved, shared, documentation, or multicast is refused too, because none is a destination a build needs.
/// </summary>
public static class AddressPolicy
{
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicV4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicV6(address.GetAddressBytes()),
            _ => false,
        };
    }

    private static bool IsPublicV4(byte[] a) => !(
        a[0] == 0                                          // 0.0.0.0/8: this network
        || a[0] == 10                                      // 10.0.0.0/8: private
        || (a[0] == 100 && a[1] is >= 64 and <= 127)       // 100.64.0.0/10: shared address space
        || a[0] == 127                                     // 127.0.0.0/8: loopback
        || (a[0] == 169 && a[1] == 254)                    // 169.254.0.0/16: link-local, which includes cloud metadata services
        || (a[0] == 172 && a[1] is >= 16 and <= 31)        // 172.16.0.0/12: private
        || (a[0] == 192 && a[1] == 0 && a[2] is 0 or 2)    // 192.0.0.0/24 and 192.0.2.0/24: protocol assignments and documentation
        || (a[0] == 192 && a[1] == 88 && a[2] == 99)       // 192.88.99.0/24: 6to4 relays
        || (a[0] == 192 && a[1] == 168)                    // 192.168.0.0/16: private
        || (a[0] == 198 && a[1] is 18 or 19)               // 198.18.0.0/15: benchmarking
        || (a[0] == 198 && a[1] == 51 && a[2] == 100)      // 198.51.100.0/24: documentation
        || (a[0] == 203 && a[1] == 0 && a[2] == 113)       // 203.0.113.0/24: documentation
        || a[0] >= 224);                                   // 224.0.0.0/4 multicast, 240.0.0.0/4 reserved, and the broadcast address

    /// <summary>
    /// Only global unicast, <c>2000::/3</c>, is a destination: that leaves out the loopback and unspecified addresses, link-local, unique local, multicast, and every
    /// transition range that carries an IPv4 address in it, except 6to4, whose embedded address is tested as an IPv4 one.
    /// </summary>
    private static bool IsPublicV6(byte[] a)
    {
        if ((a[0] & 0xE0) != 0x20)
        {
            return false;
        }

        if (a[0] == 0x20 && a[1] == 0x01 && a[2] == 0x00 && (a[3] == 0x00 || a[3] == 0x02 || (a[3] & 0xF0) is 0x10 or 0x20))
        {
            return false; // 2001::/32 Teredo, 2001:2::/48 benchmarking, 2001:10::/28 and 2001:20::/28 ORCHID
        }

        if (a[0] == 0x20 && a[1] == 0x01 && a[2] == 0x0D && a[3] == 0xB8)
        {
            return false; // 2001:db8::/32: documentation
        }

        if (a[0] == 0x3F && a[1] == 0xFF && (a[2] & 0xF0) == 0)
        {
            return false; // 3fff::/20: documentation
        }

        if (a[0] == 0x20 && a[1] == 0x02)
        {
            return IsPublicV4([a[2], a[3], a[4], a[5]]); // 2002::/16: 6to4
        }

        return true;
    }
}
