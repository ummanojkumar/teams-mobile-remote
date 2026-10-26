using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TeamsRemote.Companion.Network;

public sealed record NetworkAdapterInfo(
    string Id,
    string Name,
    string Description,
    IPAddress IpAddress,
    string IpAddressString,
    bool IsWireless)
{
    public string DisplayName => $"{(IsWireless ? "Wi-Fi" : "Ethernet")}: {Name} ({IpAddressString})";
}

public static class NetworkInterfaceScanner
{
    public static IReadOnlyList<NetworkAdapterInfo> GetPrivateRfc1918Adapters()
    {
        var result = new List<NetworkAdapterInfo>();

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var iface in interfaces)
            {
                // Must be up and active
                if (iface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                // Strictly Wi-Fi or Ethernet (exclude loopback, tunnels, ppp)
                if (iface.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 &&
                    iface.NetworkInterfaceType != NetworkInterfaceType.Ethernet)
                {
                    continue;
                }

                // Filter out obvious virtual / container / hypervisor switches if description indicates it
                string desc = iface.Description.ToLowerInvariant();
                if (desc.Contains("virtual") || desc.Contains("hyper-v") || desc.Contains("vethernet") ||
                    desc.Contains("wsl") || desc.Contains("docker") || desc.Contains("vpn"))
                {
                    continue;
                }

                var ipProps = iface.GetIPProperties();
                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    if (IsPrivateRfc1918(unicast.Address))
                    {
                        bool isWireless = iface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                        result.Add(new NetworkAdapterInfo(
                            Id: iface.Id,
                            Name: iface.Name,
                            Description: iface.Description,
                            IpAddress: unicast.Address,
                            IpAddressString: unicast.Address.ToString(),
                            IsWireless: isWireless));
                    }
                }
            }
        }
        catch
        {
            // Fall back gracefully to empty list on network enumeration error
        }

        // Return Wi-Fi adapters first, then Ethernet, sorted alphabetically
        return result
            .OrderByDescending(a => a.IsWireless)
            .ThenBy(a => a.Name)
            .ToList();
    }

    public static bool IsPrivateRfc1918(IPAddress ipAddress)
    {
        if (ipAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] bytes = ipAddress.GetAddressBytes();

        // 10.0.0.0/8 (10.0.0.0 - 10.255.255.255)
        if (bytes[0] == 10)
        {
            return true;
        }

        // 172.16.0.0/12 (172.16.0.0 - 172.31.255.255)
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            return true;
        }

        // 192.168.0.0/16 (192.168.0.0 - 192.168.255.255)
        if (bytes[0] == 192 && bytes[1] == 168)
        {
            return true;
        }

        // Excludes loopback (127.x.x.x), APIPA (169.254.x.x), multicast (224.x.x.x), public IPs
        return false;
    }
}
