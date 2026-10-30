using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class InstallIdentity
{
    internal const string MarkerName = ".cursorquotapet-install";
    internal const string Marker = "CursorQuotaPet.UserInstall.v1";
    internal static string UninstallKeyPath
    {
        get
        {
#if UNINSTALL_TEST
            Guid id = new Guid(Environment.GetEnvironmentVariable("CQP_UNINSTALL_TEST_ID"));
            return @"Software\CursorQuotaPet\UninstallTests\" + id.ToString("N");
#else
            return @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
#endif
        }
    }
    internal static string ShortcutDirectory(bool desktop)
    {
#if UNINSTALL_TEST
        return Path.Combine(Path.GetTempPath(), "CursorQuotaPet-UninstallTest-" + new Guid(Environment.GetEnvironmentVariable("CQP_UNINSTALL_TEST_ID")).ToString("N"), desktop ? "Desktop" : "Programs");
#else
        return Environment.GetFolderPath(desktop ? Environment.SpecialFolder.DesktopDirectory : Environment.SpecialFolder.Programs);
#endif
    }
    internal static bool SamePath(string first, string second)
    {
        return !string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(second) &&
            string.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }
    internal static void RejectReparsePath(string path)
    {
        for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("卸载路径包含链接或重解析点：" + current);
        }
    }
}

internal static class UninstallProgram
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string item1, string item2);

    [STAThread]
    private static int Main(string[] args)
    {
        bool quiet = Array.IndexOf(args, "--quiet") >= 0 || (args.Length == 4 && args[0] == "--cleanup" && args[3] == "quiet");
        try
        {
            if (args.Length == 4 && args[0] == "--cleanup")
                return RunHelper(int.Parse(args[1]), args[2], args[3] == "quiet");
            foreach (string arg in args)
                if (arg != "--quiet") throw new ArgumentException("不支持的卸载参数。");
            string original = Assembly.GetExecutingAssembly().Location;
            string root = Path.GetDirectoryName(original);
            ValidateInstall(root, original);
            if (!quiet && MessageBox.Show("确定卸载 Cursor仪表盘？\r\n\r\nCursor 编辑器及单独保存的登录信息和缓存将保留。", "卸载 Cursor仪表盘", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return 0;

            string temporary = Path.Combine(Path.GetTempPath(), "CursorQuotaPet-Uninstall-" + Guid.NewGuid().ToString("N"));
#if UNINSTALL_TEST
            temporary = Path.Combine(Path.GetDirectoryName(InstallIdentity.ShortcutDirectory(false)), "Helper-" + Guid.NewGuid().ToString("N"));
#endif
            Directory.CreateDirectory(temporary);
            string helper = Path.Combine(temporary, "Uninstall.exe");
            File.Copy(original, helper);
            string eventName = "Local\\CursorQuotaPet-Uninstall-" + Guid.NewGuid().ToString("N");
            using (EventWaitHandle ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName))
            using (Process child = Process.Start(new ProcessStartInfo(helper, "--cleanup " + Process.GetCurrentProcess().Id + " " + eventName + " " + (quiet ? "quiet" : "interactive")) { UseShellExecute = false, CreateNoWindow = true }))
            {
                if (!ready.WaitOne(30000)) throw new IOException("卸载清理程序未能完成，请重试。");
                string result = File.ReadAllText(Path.Combine(temporary, "result.txt"));
                if (result != "ok") throw new IOException(result);
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (!quiet) MessageBox.Show("卸载未完成：" + ex.Message, "Cursor仪表盘", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int RunHelper(int parentId, string eventName, bool quiet)
    {
        string root = null;
        string resultPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "result.txt");
        using (Process parent = Process.GetProcessById(parentId))
        using (EventWaitHandle ready = EventWaitHandle.OpenExisting(eventName))
        {
            try
            {
                string original = parent.MainModule.FileName;
                root = Path.GetDirectoryName(original);
                ValidateInstall(root, original);
                // Derive the deletion root from the running original, never argv.
                if (InstallIdentity.SamePath(original, Assembly.GetExecutingAssembly().Location) || Hash(original) != Hash(Assembly.GetExecutingAssembly().Location))
                    throw new IOException("卸载清理程序校验失败。");
                RemoveInstall(root);
                File.WriteAllText(resultPath, "ok");
            }
            catch (Exception ex)
            {
                File.WriteAllText(resultPath, ex.Message);
#if UNINSTALL_TEST
                File.WriteAllText(Environment.GetEnvironmentVariable("CQP_UNINSTALL_TEST_RESULT"), "error: " + ex.Message);
#endif
                ready.Set();
                return 1;
            }
            // The parent reports success only after app files, shortcuts and its
            // registry entry were cleaned. It then exits so we can remove its EXE.
            ready.Set();
            if (!parent.WaitForExit(15000)) throw new IOException("卸载程序仍在运行。");
        }
        try
        {
            InstallIdentity.RejectReparsePath(root);
            File.Delete(Path.Combine(root, "Uninstall.exe"));
            File.Delete(Path.Combine(root, InstallIdentity.MarkerName));
            if (Directory.GetFileSystemEntries(root).Length == 0) Directory.Delete(root, false);
#if UNINSTALL_TEST
            File.WriteAllText(Environment.GetEnvironmentVariable("CQP_UNINSTALL_TEST_RESULT"), "ok");
#endif
            if (!quiet) MessageBox.Show("Cursor仪表盘已卸载。", "Cursor仪表盘", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(resultPath), "cleanup-error.log"), ex.ToString());
#if UNINSTALL_TEST
            File.WriteAllText(Environment.GetEnvironmentVariable("CQP_UNINSTALL_TEST_RESULT"), "error: " + ex.Message);
#endif
            if (!quiet) MessageBox.Show("卸载未完成：" + ex.Message, "Cursor仪表盘", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static string Hash(string path)
    {
        using (SHA256 algorithm = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
            return Convert.ToBase64String(algorithm.ComputeHash(stream));
    }

    private static void ValidateInstall(string root, string original)
    {
        InstallIdentity.RejectReparsePath(root);
        InstallIdentity.RejectReparsePath(original);
        string marker = Path.Combine(root, InstallIdentity.MarkerName);
        InstallIdentity.RejectReparsePath(marker);
        if (!string.Equals(Path.GetFileName(original), "Uninstall.exe", StringComparison.OrdinalIgnoreCase) ||
            File.ReadAllText(marker) != InstallIdentity.Marker || InstallIdentity.SamePath(root, Path.GetPathRoot(root)))
            throw new IOException("找不到有效的 Cursor仪表盘安装标记。");
    }

    private static void RemoveInstall(string root)
    {
        ValidateInstall(root, Path.Combine(root, "Uninstall.exe"));
        string app = Path.Combine(root, "CursorQuotaPet.exe");
        foreach (Process process in Process.GetProcessesByName("CursorQuotaPet"))
        {
            using (process)
            {
                string image;
                try { image = process.MainModule.FileName; }
                catch { continue; }
                if (!InstallIdentity.SamePath(image, app)) continue;
                process.Kill();
                if (!process.WaitForExit(5000)) throw new IOException("Cursor仪表盘仍在运行。");
            }
        }
        RemoveShortcut(Path.Combine(InstallIdentity.ShortcutDirectory(false), "Cursor仪表盘.lnk"), app);
        RemoveShortcut(Path.Combine(InstallIdentity.ShortcutDirectory(true), "Cursor仪表盘.lnk"), app);

        // Exact known files only. Unknown files and subdirectories are retained.
        foreach (string file in Directory.GetFiles(root))
        {
            string name = Path.GetFileName(file);
            bool owned = name == "CursorQuotaPet.exe" || name == "CursorQuotaPet.ico" ||
                name == "CursorQuotaPet.core.exe" || name == "CursorQuotaPet.original.exe" ||
                Regex.IsMatch(name, @"^CursorQuotaPet-[0-9a-f]{16}\.ico$");
            if (!owned) continue;
            InstallIdentity.RejectReparsePath(file);
            File.Delete(file);
        }
        string keyPath = InstallIdentity.UninstallKeyPath + @"\CursorQuotaPet";
        bool sameInstall;
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath))
            sameInstall = key != null && InstallIdentity.SamePath(key.GetValue("InstallLocation") as string, root);
        if (sameInstall) Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
        SHChangeNotify(0x08000000, 0x2000, null, null);
    }

    private static void RemoveShortcut(string path, string app)
    {
        if (!File.Exists(path)) return;
        InstallIdentity.RejectReparsePath(path);
        object shell = null;
        object shortcut = null;
        try
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            shell = Activator.CreateInstance(type);
            shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            string target = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
            if (!InstallIdentity.SamePath(target, app)) return;
            File.Delete(path);
            SHChangeNotify(0x00000004, 0x2005, path, null);
        }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }
}
