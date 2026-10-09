using System.Net.Sockets;
using System.Text.Json;

namespace LetMeIn.Net;

public enum SendStatus { Delivered, Cooldown, Offline, Rejected }

public sealed record SendResult(SendStatus Status, int RetryAfterSec = 0);

public static class MessageClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public static async Task<SendResult> SendAsync(Settings settings, Peer peer, string text)
    {
        var key = Protocol.DeriveKey(settings.SharedKey);
        var data = Protocol.Encode(new Packet
        {
            Type = Packet.Msg,
            Id = settings.Id,
            Name = settings.Name,
            Port = settings.MessagePort,
            Text = text,
        }, key);

        using var cts = new CancellationTokenSource(Timeout);
        try
        {
            using var client = new TcpClient(peer.Address.AddressFamily);
            await client.ConnectAsync(peer.Address, peer.Port, cts.Token);
            var stream = client.GetStream();
            await LineIO.WriteLineAsync(stream, data, cts.Token);

            var line = await LineIO.ReadLineAsync(stream, Protocol.MaxPacketBytes, cts.Token);
            if (line is null) return new SendResult(SendStatus.Rejected);

            var reply = JsonSerializer.Deserialize<Reply>(line);
            return reply switch
            {
                { Ok: true } => new SendResult(SendStatus.Delivered),
                { Reason: "cooldown" } => new SendResult(SendStatus.Cooldown, reply.RetryAfterSec),
                _ => new SendResult(SendStatus.Rejected),
            };
        }
        catch (Exception e) when (e is SocketException or IOException or OperationCanceledException)
        {
            return new SendResult(SendStatus.Offline);
        }
        catch (JsonException)
        {
            return new SendResult(SendStatus.Rejected);
        }
    }
}
