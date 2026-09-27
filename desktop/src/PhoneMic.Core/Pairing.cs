using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace PhoneMic.Core;

public static class Pairing
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string NewToken() =>
        new(Enumerable.Range(0, 8).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());

    /// <summary>IPv4 addresses the phone could reach us on, real network adapters first.</summary>
    public static IReadOnlyList<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderBy(n => IsVirtual(n) ? 1 : 0)
            .ThenBy(n => n.GetIPProperties().GatewayAddresses.Count > 0 ? 0 : 1)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254."))
            .Distinct()
            .ToList();

    private static bool IsVirtual(NetworkInterface n)
    {
        var d = n.Description.ToLowerInvariant();
        return d.Contains("virtual") || d.Contains("hyper-v") || d.Contains("vmware") || d.Contains("vpn") || d.Contains("wsl");
    }

    public static string Uri(IEnumerable<IPAddress> addresses, int port, string token, string pcName) =>
        $"phonemic://pair?a={string.Join(',', addresses)}&p={port}&t={token}&n={System.Uri.EscapeDataString(pcName)}";
}
