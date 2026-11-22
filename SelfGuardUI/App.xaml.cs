using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace SelfGuardUI;

/// <summary>
/// Background tray-less companion: listens on \\.\pipe\SelfGuard.Events and shows
/// one ReminderWindow per monitor when the service reports a blocked domain.
/// </summary>
public partial class App : System.Windows.Application
{
    private Mutex? _single;
    private readonly List<ReminderWindow> _open = new();

    private static readonly string[] Browsers =
        { "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "iexplore", "yandex", "waterfox", "librewolf" };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _single = new Mutex(true, @"Local\SelfGuardUI.Single", out bool created);
        if (!created) { Shutdown(); return; }
        _ = Task.Run(ListenLoop);
    }

    private async Task ListenLoop()
    {
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", "SelfGuard.Events", PipeDirection.In);
                await pipe.ConnectAsync(5000);
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                    if (line.StartsWith("BLOCKED|"))
                        Dispatcher.Invoke(ShowReminder);
            }
            catch { /* service not running yet / restarted */ }
            await Task.Delay(2000);
        }
    }

    private void ShowReminder()
    {
        if (_open.Count > 0) return;                     // already showing
        CloseBrowsers();                                  // cut the page immediately

        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var w = new ReminderWindow(screen.Primary);
            w.Left = screen.Bounds.Left; w.Top = screen.Bounds.Top;
            w.Width = screen.Bounds.Width; w.Height = screen.Bounds.Height;
            w.Finished += OnCountdownFinished;
            _open.Add(w);
            w.Show();
            w.WindowState = WindowState.Maximized;
        }
    }

    private void OnCountdownFinished()
    {
        foreach (var w in _open.ToList()) w.Close();
        _open.Clear();
        CloseBrowsers();
        LockWorkStation();                                // safe, documented: locks the session
    }

    private static void CloseBrowsers()
    {
        foreach (var name in Browsers)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow();   // graceful first
                    if (!p.WaitForExit(1500)) p.Kill(entireProcessTree: true);
                }
                catch { }
                finally { p.Dispose(); }
            }
    }
}
