using System.Net.NetworkInformation;

namespace NetPulseMonitor;

internal sealed record LocalLinkInfo(
    string Kind,
    string AdapterName,
    long? SpeedBitsPerSecond);

internal static class LocalNetworkInfo
{
    public static LocalLinkInfo ReadActiveLink()
    {
        try
        {
            NetworkInterface? adapter = NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up)
                .Where(item => item.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                               item.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .OrderByDescending(HasDefaultGateway)
                .ThenByDescending(item => item.Speed)
                .FirstOrDefault();

            if (adapter is null)
                return new LocalLinkInfo("Not detected", "-", null);

            string kind = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                ? "Wi-Fi"
                : adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or
                    NetworkInterfaceType.GigabitEthernet or
                    NetworkInterfaceType.FastEthernetFx or
                    NetworkInterfaceType.FastEthernetT
                    ? "Ethernet"
                    : adapter.NetworkInterfaceType.ToString();
            return new LocalLinkInfo(
                kind,
                string.IsNullOrWhiteSpace(adapter.Name) ? "-" : adapter.Name,
                adapter.Speed > 0 ? adapter.Speed : null);
        }
        catch
        {
            return new LocalLinkInfo("Not detected", "-", null);
        }
    }

    private static bool HasDefaultGateway(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().GatewayAddresses.Any(item =>
                !item.Address.Equals(System.Net.IPAddress.Any) &&
                !item.Address.Equals(System.Net.IPAddress.IPv6Any));
        }
        catch
        {
            return false;
        }
    }
}
