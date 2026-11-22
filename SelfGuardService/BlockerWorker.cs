using System.Runtime.InteropServices;

namespace SelfGuardService;

/// <summary>
/// Intercepts outbound DNS (53 UDP/TCP) and TLS ClientHello (443 TCP) via WinDivert.
/// Blocked packets are dropped (not re-injected) and the UI is notified over a named pipe.
/// Also blocks QUIC (UDP 443) so browsers fall back to TCP/TLS where SNI is visible.
/// </summary>
public sealed class BlockerWorker : BackgroundService
{
    private const string Filter =
        "outbound and !loopback and (" +
        "(udp.DstPort == 53) or (tcp.DstPort == 53 and tcp.PayloadLength > 0) or " +
        "(tcp.DstPort == 443 and tcp.PayloadLength > 0) or (udp.DstPort == 443))";

    private readonly ILogger<BlockerWorker> _log;
    private readonly DomainList _domains = new();
    private readonly CoolingOffManager _cooling = new();
    private PipeServer? _pipes;
    private IntPtr _handle = IntPtr.Zero;
    private DateTime _lastNotify = DateTime.MinValue;

    public BlockerWorker(ILogger<BlockerWorker> log) => _log = log;

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("SelfGuard started. {Count} domains loaded.", _domains.Count);
        _pipes = new PipeServer(_log, _cooling);
        _pipes.Start(ct);

        _handle = WinDivert.WinDivertOpen(Filter, WinDivert.LAYER_NETWORK, 0, 0);
        if (_handle == WinDivert.INVALID_HANDLE_VALUE || _handle == IntPtr.Zero)
            throw new InvalidOperationException($"WinDivertOpen failed: Win32 error {Marshal.GetLastWin32Error()}");

        ct.Register(() => { if (_handle != IntPtr.Zero) WinDivert.WinDivertClose(_handle); });
        return Task.Factory.StartNew(() => Loop(ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private void Loop(CancellationToken ct)
    {
        var buf = new byte[0xFFFF];
        var addr = new WinDivert.Address();
        while (!ct.IsCancellationRequested)
        {
            if (!WinDivert.WinDivertRecv(_handle, buf, (uint)buf.Length, out uint len, ref addr))
            {
                if (ct.IsCancellationRequested) break;
                continue;
            }

            bool drop = false;
            try
            {
                int ipHdr = (buf[0] >> 4) == 4 ? (buf[0] & 0x0F) * 4 : 40;
                byte proto = (buf[0] >> 4) == 4 ? buf[9] : buf[6];
                int dport = (buf[ipHdr + 2] << 8) | buf[ipHdr + 3];

                if (proto == 17 && dport == 443) drop = true;                 // QUIC → force TCP
                else
                {
                    string? host = PacketInspector.ExtractHost(buf, (int)len);
                    if (host is not null && _domains.IsBlocked(host))
                    {
                        drop = true;
                        _log.LogInformation("Blocked: {Host}", host);
                        if ((DateTime.UtcNow - _lastNotify).TotalSeconds > 3)  // debounce
                        {
                            _lastNotify = DateTime.UtcNow;
                            _pipes?.Broadcast($"BLOCKED|{host}");
                        }
                    }
                }
            }
            catch (Exception ex) { _log.LogDebug(ex, "Parse error"); }

            if (!drop) WinDivert.WinDivertSend(_handle, buf, len, out _, ref addr);
        }
    }
}
