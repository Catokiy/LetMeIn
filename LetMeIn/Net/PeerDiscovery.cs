using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LetMeIn.Net;

public sealed record Peer(string Id, string Name, IPAddress Address, int Port, DateTime LastSeenUtc)
{
    public override string ToString() => $"{Name} ({Address})";
}

/// <summary>Анонсирует себя UDP-broadcast'ом и собирает список соседей в LAN.</summary>
public sealed class PeerDiscovery : IDisposable
{
    private static readonly TimeSpan AnnounceInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(15);

    private readonly Settings _settings;
    private readonly byte[] _key;
    private readonly UdpClient _udp;
    private readonly ConcurrentDictionary<string, Peer> _peers = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Peer? _loopbackPeer;

    public event Action? PeersChanged;

    public PeerDiscovery(Settings settings, bool debugLoopback)
    {
        _settings = settings;
        _key = Protocol.DeriveKey(settings.SharedKey);
        _udp = new UdpClient { EnableBroadcast = true };
        _udp.Client.ExclusiveAddressUse = true;
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, settings.DiscoveryPort));

        if (debugLoopback)
            _loopbackPeer = new Peer(settings.Id, settings.Name + " (я, отладка)", IPAddress.Loopback,
                settings.MessagePort, DateTime.MaxValue);
    }

    public IReadOnlyList<Peer> Peers
    {
        get
        {
            var list = _peers.Values.OrderBy(p => p.Name).ToList();
            if (_loopbackPeer is not null) list.Add(_loopbackPeer);
            return list;
        }
    }

    public void Start()
    {
        _ = Task.Run(ReceiveLoop);
        _ = Task.Run(AnnounceLoop);
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
    }

    // Подключился/отключился VPN, сменился Wi-Fi — анонсируемся сразу, не ждём 5 сек.
    private void OnNetworkChanged(object? sender, EventArgs e) => _ = Task.Run(Broadcast);

    private async Task AnnounceLoop()
    {
        using var timer = new PeriodicTimer(AnnounceInterval);
        do
        {
            await Broadcast();
            Prune();
        } while (await SafeWait(timer));
    }

    private async Task<bool> SafeWait(PeriodicTimer timer)
    {
        try { return await timer.WaitForNextTickAsync(_cts.Token); }
        catch (OperationCanceledException) { return false; }
    }

    private byte[] HelloBytes() => Protocol.Encode(new Packet
    {
        Type = Packet.Hello,
        Id = _settings.Id,
        Name = _settings.Name,
        Port = _settings.MessagePort,
    }, _key);

    /// <summary>
    /// Шлём анонс отдельно через каждый LAN-интерфейс с сокета, привязанного к его адресу:
    /// так пакет гарантированно уходит в домашнюю сеть, а не в VPN-туннель.
    /// </summary>
    private async Task Broadcast()
    {
        if (_cts.IsCancellationRequested) return;
        var data = HelloBytes();
        foreach (var (addr, mask) in Protocol.LanInterfaces().ToList())
        {
            var targets = new[] { Protocol.BroadcastOf(addr, mask), IPAddress.Broadcast };
            foreach (var target in targets)
                await SendFrom(addr, data, target);
        }
    }

    private async Task SendFrom(IPAddress? local, byte[] data, IPAddress target)
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                EnableBroadcast = true,
            };
            s.Bind(new IPEndPoint(local ?? IPAddress.Any, 0));
            await s.SendToAsync(data, new IPEndPoint(target, _settings.DiscoveryPort), _cts.Token);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Интерфейс мог пропасть между перечислением и отправкой — не страшно.
        }
    }

    private async Task ReceiveLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            UdpReceiveResult res;
            try { res = await _udp.ReceiveAsync(_cts.Token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; } // ICMP port unreachable и т.п.

            if (!Protocol.IsLocalSubnet(res.RemoteEndPoint.Address)) continue;
            var p = Protocol.Decode(res.Buffer, _key);
            if (p is null || p.Type != Packet.Hello || p.Id == _settings.Id) continue;

            var addr = res.RemoteEndPoint.Address.IsIPv4MappedToIPv6
                ? res.RemoteEndPoint.Address.MapToIPv4()
                : res.RemoteEndPoint.Address;
            var peer = new Peer(p.Id, p.Name, addr, p.Port, DateTime.UtcNow);

            bool isNew = !_peers.TryGetValue(p.Id, out var old);
            bool changed = isNew || old!.Name != peer.Name || !old.Address.Equals(peer.Address);
            _peers[p.Id] = peer;

            // Отвечаем напрямую, чтобы новый сосед увидел нас сразу, а не через 5 сек.
            if (isNew)
                await SendFrom(Protocol.LocalAddressFor(addr), HelloBytes(), addr);
            if (changed)
                PeersChanged?.Invoke();
        }
    }

    private void Prune()
    {
        bool changed = false;
        foreach (var (id, peer) in _peers)
        {
            if (DateTime.UtcNow - peer.LastSeenUtc > PeerTimeout && _peers.TryRemove(id, out _))
                changed = true;
        }
        if (changed) PeersChanged?.Invoke();
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        _cts.Cancel();
        _udp.Dispose();
    }
}
