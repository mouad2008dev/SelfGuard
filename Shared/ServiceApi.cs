using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SelfGuard.Setup;

/// <summary>advapi32 Service Control Manager P/Invoke (shared by Installer & Uninstaller via linked file).</summary>
internal static class ServiceApi
{
    public const string ServiceName = "SelfGuardBlockerService";
    public const string DisplayName = "SelfGuard Content Blocker";
    public const string Description = "SelfGuard self-control content blocker (DNS/SNI filtering). Uninstall from Apps & Features.";

    const uint SC_MANAGER_ALL_ACCESS = 0xF003F, SERVICE_ALL_ACCESS = 0xF01FF;
    const uint SERVICE_WIN32_OWN_PROCESS = 0x10, SERVICE_AUTO_START = 0x2, SERVICE_ERROR_NORMAL = 0x1;
    const uint SERVICE_CONFIG_DESCRIPTION = 1, SERVICE_CONFIG_FAILURE_ACTIONS = 2;
    const uint SERVICE_CONTROL_STOP = 1;
    const int SC_ACTION_RESTART = 1;
    const int ERROR_SERVICE_DOES_NOT_EXIST = 1060, ERROR_SERVICE_ALREADY_RUNNING = 1056;

    [StructLayout(LayoutKind.Sequential)] struct SERVICE_STATUS { public uint a, b, c, d, e, f, g; }
    [StructLayout(LayoutKind.Sequential)] struct SC_ACTION { public int Type; public uint Delay; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SERVICE_FAILURE_ACTIONS { public uint dwResetPeriod; public string? lpRebootMsg; public string? lpCommand; public uint cActions; public IntPtr lpsaActions; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SERVICE_DESCRIPTION { public string lpDescription; }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr OpenSCManager(string? m, string? d, uint a);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateService(IntPtr scm, string name, string disp, uint access, uint type, uint start, uint err, string path, string? grp, IntPtr tag, string? deps, string? user, string? pwd);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr OpenService(IntPtr scm, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool ChangeServiceConfig2(IntPtr svc, uint level, ref SERVICE_FAILURE_ACTIONS info);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool ChangeServiceConfig2(IntPtr svc, uint level, ref SERVICE_DESCRIPTION info);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool StartService(IntPtr svc, uint n, IntPtr args);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool ControlService(IntPtr svc, uint ctl, ref SERVICE_STATUS st);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool QueryServiceStatus(IntPtr svc, ref SERVICE_STATUS st);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool DeleteService(IntPtr svc);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool CloseServiceHandle(IntPtr h);

    static IntPtr Scm() { var h = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS); if (h == IntPtr.Zero) throw new Win32Exception(); return h; }

    public static void Install(string exePath)
    {
        IntPtr scm = Scm();
        try
        {
            IntPtr svc = OpenService(scm, ServiceName, SERVICE_ALL_ACCESS);
            if (svc == IntPtr.Zero)
            {
                svc = CreateService(scm, ServiceName, DisplayName, SERVICE_ALL_ACCESS, SERVICE_WIN32_OWN_PROCESS,
                    SERVICE_AUTO_START, SERVICE_ERROR_NORMAL, $"\"{exePath}\"", null, IntPtr.Zero, null, null /*LocalSystem*/, null);
                if (svc == IntPtr.Zero) throw new Win32Exception();
            }
            try
            {
                var desc = new SERVICE_DESCRIPTION { lpDescription = Description };
                ChangeServiceConfig2(svc, SERVICE_CONFIG_DESCRIPTION, ref desc);

                // Recovery: restart after 1s, 1s, 5s; reset failure count daily.
                var actions = new[] { new SC_ACTION { Type = SC_ACTION_RESTART, Delay = 1000 },
                                      new SC_ACTION { Type = SC_ACTION_RESTART, Delay = 1000 },
                                      new SC_ACTION { Type = SC_ACTION_RESTART, Delay = 5000 } };
                int sz = Marshal.SizeOf<SC_ACTION>();
                IntPtr p = Marshal.AllocHGlobal(sz * actions.Length);
                try
                {
                    for (int i = 0; i < actions.Length; i++) Marshal.StructureToPtr(actions[i], p + i * sz, false);
                    var fa = new SERVICE_FAILURE_ACTIONS { dwResetPeriod = 86400, cActions = (uint)actions.Length, lpsaActions = p };
                    if (!ChangeServiceConfig2(svc, SERVICE_CONFIG_FAILURE_ACTIONS, ref fa)) throw new Win32Exception();
                }
                finally { Marshal.FreeHGlobal(p); }

                if (!StartService(svc, 0, IntPtr.Zero) && Marshal.GetLastWin32Error() != ERROR_SERVICE_ALREADY_RUNNING)
                    throw new Win32Exception();
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }

    public static void StopAndDelete()
    {
        IntPtr scm = Scm();
        try
        {
            IntPtr svc = OpenService(scm, ServiceName, SERVICE_ALL_ACCESS);
            if (svc == IntPtr.Zero) { if (Marshal.GetLastWin32Error() == ERROR_SERVICE_DOES_NOT_EXIST) return; throw new Win32Exception(); }
            try
            {
                var st = new SERVICE_STATUS();
                ControlService(svc, SERVICE_CONTROL_STOP, ref st);
                for (int i = 0; i < 30; i++) { QueryServiceStatus(svc, ref st); if (st.b == 1 /*STOPPED*/) break; Thread.Sleep(500); }
                if (!DeleteService(svc)) throw new Win32Exception();
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }
}
