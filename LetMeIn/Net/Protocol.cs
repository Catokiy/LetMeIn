using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LetMeIn.Net;

public sealed class Packet
{
    public const string Hello = "hello";
    public const string Msg = "msg";

    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Port { get; set; }
    public string Text { get; set; } = "";
    public long Ts { get; set; }
    public string Mac { get; set; } = "";
}

public sealed class Reply
{
    public bool Ok { get; set; }
    public string Reason { get; set; } = "";
    public int RetryAfterSec { get; set; }
}

public static class Protocol
{
    public const int MaxTextLength = 200;
    public const int MaxNameLength = 32;
    public const int MaxPacketBytes = 4096;
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(2);

    public static byte[] DeriveKey(string sharedKey) =>
        SHA256.HashData(Encoding.UTF8.GetBytes("LetMeIn:v1:" + sharedKey));

    public static byte[] Encode(Packet p, byte[] key)
    {
        p.Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        p.Mac = Convert.ToBase64String(HMACSHA256.HashData(key, Canonical(p)));
        return JsonSerializer.SerializeToUtf8Bytes(p);
    }

    /// <summary>Разбирает пакет и проверяет подпись и время. null — пакет невалидный/чужой.</summary>
    public static Packet? Decode(ReadOnlySpan<byte> data, byte[] key)
    {
        if (data.Length > MaxPacketBytes) return null;
        Packet? p;
        try { p = JsonSerializer.Deserialize<Packet>(data); }
        catch (JsonException) { return null; }
        if (p is null || string.IsNullOrEmpty(p.Mac)) return null;

        byte[] mac;
        try { mac = Convert.FromBase64String(p.Mac); }
        catch (FormatException) { return null; }
        if (!CryptographicOperations.FixedTimeEquals(mac, HMACSHA256.HashData(key, Canonical(p))))
            return null;

        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(p.Ts);
        if (age.Duration() > MaxClockSkew) return null;

        if (p.Name.Length > MaxNameLength) p.Name = p.Name[..MaxNameLength];
        if (p.Text.Length > MaxTextLength) p.Text = p.Text[..MaxTextLength];
        return p;
    }

    private static byte[] Canonical(Packet p) =>
        Encoding.UTF8.GetBytes(string.Join('\n', p.Type, p.Id, p.Name, p.Port, p.Text, p.Ts));

    /// <summary>Активные IPv4-интерфейсы (без loopback) с адресом и маской.</summary>
    public static IEnumerable<(IPAddress Address, IPAddress Mask)> LocalIPv4()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork && ua.IPv4Mask is not null)
                    yield return (ua.Address, ua.IPv4Mask);
            }
        }
    }

    public static IPAddress BroadcastOf(IPAddress addr, IPAddress mask)
    {
        var a = addr.GetAddressBytes();
        var m = mask.GetAddressBytes();
        var b = new byte[4];
        for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
        return new IPAddress(b);
    }

    public static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254);
    }

    /// <summary>Адрес из нашей локальной подсети (или loopback) и из приватного диапазона.</summary>
    public static bool IsLocalSubnet(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (!IsPrivate(ip)) return false;
        var r = ip.GetAddressBytes();
        foreach (var (addr, mask) in LocalIPv4())
        {
            var a = addr.GetAddressBytes();
            var m = mask.GetAddressBytes();
            bool same = true;
            for (int i = 0; i < 4 && same; i++) same = (a[i] & m[i]) == (r[i] & m[i]);
            if (same) return true;
        }
        return false;
    }
}
