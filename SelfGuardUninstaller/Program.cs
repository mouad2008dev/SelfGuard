using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Microsoft.Win32;

namespace SelfGuard.Setup;

internal static class Program
{
    const string InstallDir   = @"C:\Program Files\SelfGuard";
    const string DataDir      = @"C:\ProgramData\SelfGuard";
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SelfGuard";
    const string RunKey       = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const MessageBoxOptions Rtl = MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign;

    [STAThread]
    static int Main()
    {
        ApplicationConfiguration.Initialize();

        string? status = AskService("UNLOCK_STATUS");
        if (status is null)
        {
            // Service not reachable (stopped/broken). Fail closed: do not uninstall silently.
            Info("تعذّر الاتصال بخدمة SelfGuard. تأكد أنها تعمل ثم أعد المحاولة.", MessageBoxIcon.Warning);
            return 3;
        }

        var parts = status.Split('|');
        switch (parts[0])
        {
            case "STARTED":
                Info("تم تسجيل طلب إلغاء التثبيت.\n\nبدأت مهلة التهدئة (24 ساعة). " +
                     "بعد انقضائها شغّل أداة الإزالة مجدداً لإتمام الحذف.\n\n" +
                     "إن غيّرت رأيك فلا حاجة لفعل شيء — ستبقى الحماية تعمل.", MessageBoxIcon.Information);
                return 0;

            case "PENDING":
                var left = TimeSpan.FromSeconds(long.Parse(parts[1]));
                var r = MessageBox.Show(
                    $"مهلة التهدئة ما زالت سارية.\n\nالوقت المتبقي: {(int)left.TotalHours} ساعة و {left.Minutes} دقيقة.\n\n" +
                    "هل تريد إلغاء طلب الإزالة والإبقاء على الحماية؟",
                    "SelfGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2, Rtl);
                if (r == DialogResult.Yes) { AskService("CANCEL_UNLOCK"); Info("تم إلغاء طلب الإزالة. الحماية مستمرة.", MessageBoxIcon.Information); }
                return 0;

            case "READY":
                return Finalize();

            default:
                Info("استجابة غير متوقعة من الخدمة: " + status, MessageBoxIcon.Error);
                return 4;
        }
    }

    static int Finalize()
    {
        // A running exe cannot delete its own folder: relaunch a temp copy that performs the cleanup.
        string self = Environment.ProcessPath!;
        if (self.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase))
        {
            string tmp = Path.Combine(Path.GetTempPath(), $"SelfGuardUninstaller_{Guid.NewGuid():N}.exe");
            File.Copy(self, tmp, true);
            Process.Start(new ProcessStartInfo(tmp) { UseShellExecute = true, Verb = "runas" });
            return 0;
        }

        if (MessageBox.Show("انقضت مهلة التهدئة (24 ساعة).\n\nهل تريد إزالة SelfGuard نهائياً الآن؟",
                "SelfGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, Rtl) != DialogResult.Yes)
        {
            AskService("CANCEL_UNLOCK");
            Info("تم إلغاء الإزالة. الحماية مستمرة.", MessageBoxIcon.Information);
            return 0;
        }

        try
        {
            ServiceApi.StopAndDelete();                                     // ControlService + DeleteService
            foreach (var p in Process.GetProcessesByName("SelfGuardUI"))    // close UI in all sessions
                try { p.Kill(); p.WaitForExit(3000); } catch { } finally { p.Dispose(); }

            using (var run = Registry.LocalMachine.OpenSubKey(RunKey, true)) run?.DeleteValue("SelfGuardUI", false);
            Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);

            Thread.Sleep(1000);                                             // let the driver/handles release
            TryDeleteDir(InstallDir);
            TryDeleteDir(DataDir);

            Info("تمت إزالة SelfGuard بالكامل.", MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            Info("حدث خطأ أثناء الإزالة:\n" + ex.Message, MessageBoxIcon.Error);
            return 5;
        }
    }

    static void TryDeleteDir(string d)
    {
        for (int i = 0; i < 5 && Directory.Exists(d); i++)
        {
            try { Directory.Delete(d, true); } catch { Thread.Sleep(1000); }
        }
    }

    static string? AskService(string cmd)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", "SelfGuard.Control", PipeDirection.InOut);
            pipe.Connect(5000);
            using var w = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            using var r = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
            w.WriteLine(cmd);
            return r.ReadLine();
        }
        catch { return null; }
    }

    static void Info(string msg, MessageBoxIcon icon) =>
        MessageBox.Show(msg, "SelfGuard", MessageBoxButtons.OK, icon, MessageBoxDefaultButton.Button1, Rtl);
}
