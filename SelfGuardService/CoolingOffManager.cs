using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SelfGuardService;

/// <summary>
/// Persists the 24-hour unlock request in ProgramData\SelfGuard\state.dat,
/// encrypted with DPAPI (LocalMachine scope, readable only by SYSTEM/admins via ACL).
/// </summary>
public sealed class CoolingOffManager
{
    public static readonly TimeSpan Delay = TimeSpan.FromHours(24);
    private static readonly string Dir  = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SelfGuard");
    private static readonly string File_ = Path.Combine(Dir, "state.dat");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SelfGuard.CoolingOff.v1");
    private readonly object _lock = new();

    private sealed class State { public DateTime? UnlockRequestedUtc { get; set; } }

    public CoolingOffManager() => Directory.CreateDirectory(Dir);

    private State Load()
    {
        try
        {
            if (!File.Exists(File_)) return new State();
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(File_), Entropy, DataProtectionScope.LocalMachine);
            return JsonSerializer.Deserialize<State>(plain) ?? new State();
        }
        catch { return new State(); }
    }

    private void Save(State s)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(s);
        File.WriteAllBytes(File_, ProtectedData.Protect(plain, Entropy, DataProtectionScope.LocalMachine));
    }

    /// <summary>Returns: "NONE" (just started), "PENDING|seconds-left", or "READY".</summary>
    public string QueryOrStart()
    {
        lock (_lock)
        {
            var s = Load();
            if (s.UnlockRequestedUtc is null)
            {
                s.UnlockRequestedUtc = DateTime.UtcNow;
                Save(s);
                return $"STARTED|{(long)Delay.TotalSeconds}";
            }
            var left = s.UnlockRequestedUtc.Value + Delay - DateTime.UtcNow;
            return left <= TimeSpan.Zero ? "READY" : $"PENDING|{(long)left.TotalSeconds}";
        }
    }

    public void Cancel() { lock (_lock) Save(new State()); }
}
