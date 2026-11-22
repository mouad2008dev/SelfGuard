using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace SelfGuardService;

/// <summary>
/// Two named pipes:
///   \\.\pipe\SelfGuard.Events  – service → UI   (BLOCKED|host)
///   \\.\pipe\SelfGuard.Control – uninstaller → service (UNLOCK_STATUS / CANCEL_UNLOCK)
/// </summary>
public sealed class PipeServer
{
    public const string EventsPipe  = "SelfGuard.Events";
    public const string ControlPipe = "SelfGuard.Control";

    private readonly ILogger _log;
    private readonly CoolingOffManager _cooling;
    private readonly ConcurrentDictionary<Guid, StreamWriter> _clients = new();

    public PipeServer(ILogger log, CoolingOffManager cooling) { _log = log; _cooling = cooling; }

    private static PipeSecurity Security(bool usersCanWrite)
    {
        var ps = new PipeSecurity();
        ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            usersCanWrite ? PipeAccessRights.ReadWrite : PipeAccessRights.Read | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return ps;
    }

    public void Start(CancellationToken ct)
    {
        _ = Task.Run(() => EventsLoop(ct), ct);
        _ = Task.Run(() => ControlLoop(ct), ct);
    }

    private async Task EventsLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = NamedPipeServerStreamAcl.Create(EventsPipe, PipeDirection.Out,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous, 0, 0, Security(false));
                await pipe.WaitForConnectionAsync(ct);
                var id = Guid.NewGuid();
                _clients[id] = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
                _log.LogInformation("UI client connected ({Count} total).", _clients.Count);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "Events pipe error"); await Task.Delay(500, ct); }
        }
    }

    public void Broadcast(string message)
    {
        foreach (var (id, w) in _clients)
        {
            try { w.WriteLine(message); }
            catch { if (_clients.TryRemove(id, out var dead)) dead.Dispose(); }
        }
    }

    private async Task ControlLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = NamedPipeServerStreamAcl.Create(ControlPipe, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, Security(true));
                await pipe.WaitForConnectionAsync(ct);
                using var r = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                using var w = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                string? cmd = await r.ReadLineAsync(ct);

                string reply;
                switch (cmd)
                {
                    case "UNLOCK_STATUS":
                        reply = _cooling.QueryOrStart();
                        break;
                    case "CANCEL_UNLOCK":
                        _cooling.Cancel();
                        reply = "OK";
                        break;
                    default:
                        reply = "ERR";
                        break;
                }

                await w.WriteLineAsync(reply);
                _log.LogInformation("Control: {Cmd} -> {Reply}", cmd, reply);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "Control pipe error"); await Task.Delay(500, ct); }
        }
    }
}