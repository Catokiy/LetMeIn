using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace LetMeIn.Net;

/// <summary>Принимает сообщения от соседей по TCP.</summary>
public sealed class MessageServer : IDisposable
{
    /// <summary>Чуть меньше минуты, чтобы небольшая разница во времени не мешала честному отправителю.</summary>
    public static readonly TimeSpan ReceiveCooldown = TimeSpan.FromSeconds(55);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(3);

    private readonly byte[] _key;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastFrom = new();

    public event Action<string, string>? MessageReceived;

    public MessageServer(Settings settings)
    {
        _key = Protocol.DeriveKey(settings.SharedKey);
        _listener = new TcpListener(IPAddress.Any, settings.MessagePort);
        _listener.ExclusiveAddressUse = true;
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }
            _ = Task.Run(() => Handle(client));
        }
    }

    private async Task Handle(TcpClient client)
    {
        using var _ = client;
        if (client.Client.RemoteEndPoint is not IPEndPoint remote || !Protocol.IsLocalSubnet(remote.Address))
            return;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        timeout.CancelAfter(ReadTimeout);
        var stream = client.GetStream();

        try
        {
            var line = await LineIO.ReadLineAsync(stream, Protocol.MaxPacketBytes, timeout.Token);
            if (line is null) return;

            var p = Protocol.Decode(line, _key);
            if (p is null || p.Type != Packet.Msg || string.IsNullOrWhiteSpace(p.Text))
            {
                await Send(stream, new Reply { Ok = false, Reason = "bad" }, timeout.Token);
                return;
            }

            var now = DateTime.UtcNow;
            if (_lastFrom.TryGetValue(p.Id, out var last) && now - last < ReceiveCooldown)
            {
                var wait = (int)Math.Ceiling((ReceiveCooldown - (now - last)).TotalSeconds);
                await Send(stream, new Reply { Ok = false, Reason = "cooldown", RetryAfterSec = wait }, timeout.Token);
                return;
            }
            _lastFrom[p.Id] = now;

            MessageReceived?.Invoke(p.Name, p.Text.Trim());
            await Send(stream, new Reply { Ok = true }, timeout.Token);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or SocketException) { }
    }

    private static Task Send(NetworkStream stream, Reply reply, CancellationToken ct) =>
        LineIO.WriteLineAsync(stream, JsonSerializer.SerializeToUtf8Bytes(reply), ct);

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}

internal static class LineIO
{
    /// <summary>Читает байты до '\n' (не включая). null — соединение закрыто или строка слишком длинная.</summary>
    public static async Task<byte[]?> ReadLineAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        var buf = new byte[maxBytes + 1];
        int len = 0;
        while (len < buf.Length)
        {
            int n = await stream.ReadAsync(buf.AsMemory(len), ct);
            if (n == 0) return null;
            int nl = Array.IndexOf(buf, (byte)'\n', len, n);
            len += n;
            if (nl >= 0) return buf[..nl];
        }
        return null;
    }

    public static async Task WriteLineAsync(Stream stream, byte[] data, CancellationToken ct)
    {
        await stream.WriteAsync(data, ct);
        await stream.WriteAsync("\n"u8.ToArray(), ct);
        await stream.FlushAsync(ct);
    }
}
