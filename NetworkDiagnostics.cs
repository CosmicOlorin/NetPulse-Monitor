using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetPulseMonitor;

internal static class NetworkDiagnostics
{
    public static async Task<DiagnosticResult> RunAsync(CancellationToken token)
    {
        string gateway = FindGateway() ?? "Not detected";
        string gatewayPing = "N/A";

        if (gateway != "Not detected")
        {
            try
            {
                using var ping = new Ping();
                PingReply reply = await ping.SendPingAsync(gateway, 2500);
                gatewayPing = reply.Status == IPStatus.Success
                    ? reply.RoundtripTime + " ms"
                    : reply.Status.ToString();
            }
            catch (Exception ex)
            {
                gatewayPing = ex.Message;
            }
        }

        string dnsLookup;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync("google.com", token);
            stopwatch.Stop();
            dnsLookup = addresses.Length > 0
                ? stopwatch.ElapsedMilliseconds + " ms"
                : "No address returned";
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            dnsLookup = ex.Message;
        }

        bool ipv4 = NetworkInterface.GetAllNetworkInterfaces()
            .Any(n => n.OperationalStatus == OperationalStatus.Up &&
                      n.GetIPProperties().UnicastAddresses
                       .Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork));

        bool ipv6 = NetworkInterface.GetAllNetworkInterfaces()
            .Any(n => n.OperationalStatus == OperationalStatus.Up &&
                      n.GetIPProperties().UnicastAddresses
                       .Any(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                                 !a.Address.IsIPv6LinkLocal));

        return new DiagnosticResult
        {
            Gateway = gateway,
            GatewayPing = gatewayPing,
            DnsLookup = dnsLookup,
            IPv4 = ipv4 ? "Available" : "Not detected",
            IPv6 = ipv6 ? "Available" : "Not detected"
        };
    }

    private static string? FindGateway()
    {
        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (GatewayIPAddressInformation gateway in
                     adapter.GetIPProperties().GatewayAddresses)
            {
                if (gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !gateway.Address.Equals(IPAddress.Any))
                    return gateway.Address.ToString();
            }
        }

        return null;
    }
}
