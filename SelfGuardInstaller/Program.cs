using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SelfGuard.Setup;

/// <summary>
/// SelfGuardInstaller.exe – place it next to the "payload" folder produced by publish:
///   payload\SelfGuardService.exe (+ deps), payload\SelfGuardUI.exe (+ deps),
///   payload\SelfGuardUninstaller.exe, payload\WinDivert.dll, payload\WinDivert64.sys
/// </summary>
internal static class Program
{
    const string InstallDir = @"C:\Program Files\SelfGuard";
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SelfGuard";
    const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    [STAThread]
    static int Main()
    {
        ApplicationConfiguration.Initialize();
        var ok = MessageBox.Show(
            "سيتم تثبيت SelfGuard لحظر المواقع الإباحية على هذا الجهاز.\n\n" +
            "• يعمل كخدمة ويندوز باسم صريح: SelfGuard Content Blocker\n" +
            "• يظهر في «التطبيقات المثبتة» ويمكن إزالته بعد مهلة تهدئة 24 ساعة.\n\nهل تريد المتابعة؟",
            "SelfGuard Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        if (ok != DialogResult.Yes) return 1;

        try
        {
            string payload = Path.Combine(AppContext.BaseDirectory, "payload");
            if (!Directory.Exists(payload)) throw new DirectoryNotFoundException($"payload folder not found: {payload}");
            foreach (var req in new[] { "SelfGuardService.exe", "SelfGuardUI.exe", "SelfGuardUninstaller.exe", "WinDivert.dll", "WinDivert64.sys" })
                if (!File.Exists(Path.Combine(payload, req))) throw new FileNotFoundException($"Missing payload file: {req}");

            // 1) Stop previous version if present, then copy files
            try { ServiceApi.StopAndDelete(); } catch { }
            KillUi();
            Directory.CreateDirectory(InstallDir);
            CopyDir(payload, InstallDir);

            // 2) Register + configure + start the service (CreateService / ChangeServiceConfig2 / StartService)
            ServiceApi.Install(Path.Combine(InstallDir, "SelfGuardService.exe"));

            // 3) Autostart UI for every user + launch now in current session
            string uiExe = Path.Combine(InstallDir, "SelfGuardUI.exe");
            using (var run = Registry.LocalMachine.CreateSubKey(RunKey, true))
                run.SetValue("SelfGuardUI", $"\"{uiExe}\"");
            LaunchUiAsCurrentUser(uiExe);

            // 4) Apps & Features entry
            using (var k = Registry.LocalMachine.CreateSubKey(UninstallKey, true))
            {
                string un = Path.Combine(InstallDir, "SelfGuardUninstaller.exe");
                k.SetValue("DisplayName", "SelfGuard Content Blocker");
                k.SetValue("DisplayVersion", "1.0.0");
                k.SetValue("Publisher", "SelfGuard");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("DisplayIcon", uiExe);
                k.SetValue("UninstallString", $"\"{un}\"");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", DirSizeKb(InstallDir), RegistryValueKind.DWord);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }

            MessageBox.Show("تم تثبيت SelfGuard وتشغيل الحماية بنجاح.", "SelfGuard Setup",
                MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("فشل التثبيت:\n" + ex.Message, "SelfGuard Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }
    }

    static void CopyDir(string src, string dst)
    {
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(src, dst));
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(f, f.Replace(src, dst), overwrite: true);
    }

    static int DirSizeKb(string d) =>
        (int)(Directory.GetFiles(d, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024);

    static void KillUi()
    {
        foreach (var p in Process.GetProcessesByName("SelfGuardUI"))
            try { p.Kill(); p.WaitForExit(3000); } catch { } finally { p.Dispose(); }
    }

    /// <summary>
    /// The installer is elevated; launching via explorer.exe starts the UI de-elevated
    /// as the interactive user (standard, documented shell behavior).
    /// </summary>
    static void LaunchUiAsCurrentUser(string exe)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exe}\"") { UseShellExecute = false }); }
        catch { /* UI will start at next logon via HKLM Run */ }
    }
}
