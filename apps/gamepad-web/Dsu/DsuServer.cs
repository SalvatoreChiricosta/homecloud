using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using GamepadWeb.Input;
using Microsoft.Extensions.Options;

namespace GamepadWeb.Dsu;

public sealed class DsuOptions
{
    /// <summary>Porta UDP standard di cemuhook/DSU.</summary>
    public int Port { get; set; } = 26760;

    /// <summary>Frequenza di invio dello stato ai client (pacchetti al secondo per porta).</summary>
    public int SendRateHz { get; set; } = 125;

    /// <summary>Se true, la porta 1 viene animata da DemoInputService (test senza telefono).</summary>
    public bool Demo { get; set; }
}

/// <summary>
/// Server DSU: risponde alle richieste degli emulatori (es. Dolphin) e invia loro
/// lo stato delle porte occupate a frequenza costante, finché il client continua a chiederlo.
/// </summary>
public sealed class DsuServer(ControllerSlots slots, IOptions<DsuOptions> options, ILogger<DsuServer> logger)
    : BackgroundService
{
    /// <summary>Se un client non rinnova la richiesta entro questo tempo, smettiamo di inviargli dati.</summary>
    private const long ClientTimeoutMs = 5_000;

    private readonly uint _serverId = (uint)Random.Shared.Next();
    private readonly ConcurrentDictionary<IPEndPoint, Subscription> _subscriptions = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private sealed class Subscription
    {
        public long LastSeenMs;
        public bool AllSlots;
        public readonly bool[] Slots = new bool[ControllerSlots.Count];
        public readonly uint[] PacketNumbers = new uint[ControllerSlots.Count];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = options.Value.Port;
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        logger.LogInformation("Server DSU in ascolto su UDP {Port}", port);

        try
        {
            await Task.WhenAll(ReceiveLoopAsync(udp, stoppingToken), SendLoopAsync(udp, stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // arresto normale del servizio
        }
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(ct);
            }
            catch (SocketException ex)
            {
                // Su Windows un invio verso una porta chiusa genera ConnectionReset sulla ricezione successiva: si ignora.
                logger.LogDebug(ex, "Errore UDP in ricezione (ignorato)");
                continue;
            }

            HandleRequest(udp, result.Buffer, result.RemoteEndPoint);
        }
    }

    private void HandleRequest(UdpClient udp, byte[] data, IPEndPoint client)
    {
        if (!DsuProtocol.TryParseRequest(data, out var type, out var payload))
            return;

        switch (type)
        {
            case DsuProtocol.MsgVersion:
                Send(udp, DsuProtocol.BuildVersionResponse(_serverId), client);
                break;

            case DsuProtocol.MsgInfo:
            {
                // int32 numero di porte richieste, poi un byte per porta
                if (payload.Length < 4) return;
                var count = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(payload), 0, ControllerSlots.Count);
                for (var i = 0; i < count && 4 + i < payload.Length; i++)
                {
                    var slot = payload[4 + i];
                    if (slot >= ControllerSlots.Count) continue;
                    Send(udp, DsuProtocol.BuildInfoResponse(_serverId, slot, slots.IsConnected(slot)), client);
                }
                break;
            }

            case DsuProtocol.MsgData:
            {
                // flag: 0 = tutte le porte, 1 = per numero di porta, 2 = per MAC (trattato come "tutte")
                if (payload.Length < 2) return;
                var subscription = _subscriptions.GetOrAdd(client, ep =>
                {
                    logger.LogInformation("Nuovo client DSU: {Client}", ep);
                    return new Subscription();
                });
                subscription.LastSeenMs = _clock.ElapsedMilliseconds;

                var flags = payload[0];
                if (flags == 0 || (flags & 2) != 0) subscription.AllSlots = true;
                if ((flags & 1) != 0 && payload[1] < ControllerSlots.Count) subscription.Slots[payload[1]] = true;
                break;
            }
        }
    }

    private async Task SendLoopAsync(UdpClient udp, CancellationToken ct)
    {
        var period = TimeSpan.FromSeconds(1.0 / Math.Max(1, options.Value.SendRateHz));
        using var timer = new PeriodicTimer(period);

        while (await timer.WaitForNextTickAsync(ct))
        {
            var nowMs = _clock.ElapsedMilliseconds;
            var timestampUs = (ulong)(_clock.Elapsed.Ticks / (TimeSpan.TicksPerMillisecond / 1000));
            var snapshot = slots.Snapshot();

            foreach (var (client, subscription) in _subscriptions)
            {
                if (nowMs - subscription.LastSeenMs > ClientTimeoutMs)
                {
                    _subscriptions.TryRemove(client, out _);
                    logger.LogInformation("Client DSU scaduto: {Client}", client);
                    continue;
                }

                for (var slot = 0; slot < snapshot.Length; slot++)
                {
                    if (snapshot[slot] is not { } state) continue;
                    if (!subscription.AllSlots && !subscription.Slots[slot]) continue;

                    var packet = DsuProtocol.BuildDataPacket(
                        _serverId, (byte)slot, state, ++subscription.PacketNumbers[slot], timestampUs);
                    Send(udp, packet, client);
                }
            }
        }
    }

    private void Send(UdpClient udp, byte[] packet, IPEndPoint client)
    {
        try
        {
            udp.Send(packet, packet.Length, client);
        }
        catch (SocketException ex)
        {
            logger.LogDebug(ex, "Invio UDP fallito verso {Client}", client);
        }
    }
}
