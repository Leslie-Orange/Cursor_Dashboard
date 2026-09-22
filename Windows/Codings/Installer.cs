using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class SetupProgram
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        SetupOptions options = SetupOptions.Parse(args);
        if (options.Quiet)
        {
            try
            {
                SetupInstaller.Install(options);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        Application.Run(new SetupForm(options));
        return 0;
    }
}

internal sealed class SetupOptions
{
    public string Root;
    public bool Quiet;
    public bool NoShortcuts;
    public bool SkipProcessStop;

    public static SetupOptions Parse(string[] args)
    {
        SetupOptions options = new SetupOptions();
        if (args == null)
        {
            return options;
        }
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i] ?? "";
            if (Same(arg, "--quiet"))
            {
                options.Quiet = true;
            }
            else if (Same(arg, "--no-shortcuts"))
            {
                options.NoShortcuts = true;
            }
            else if (Same(arg, "--skip-process-stop"))
            {
                options.SkipProcessStop = true;
            }
            else if (Same(arg, "--root") && i + 1 < args.Length)
            {
                i++;
                options.Root = args[i];
            }
        }
        return options;
    }

    private static bool Same(string value, string expected)
    {
        return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class SetupInstaller
{
    public const string AppTitle = "Cursor仪表盘";

    public static string Install(SetupOptions options)
    {
        string root = options.Root;
        if (string.IsNullOrEmpty(root))
        {
            root = FindExistingRoot();
        }
        if (string.IsNullOrEmpty(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppTitle);
        }
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);

        string targetExe = Path.Combine(root, "CursorQuotaPet.exe");
        string targetIcon = Path.Combine(root, "CursorQuotaPet.ico");
        if (!options.SkipProcessStop)
        {
            StopInstalledProcess(targetExe);
            Thread.Sleep(300);
        }

        Extract("CursorQuotaPet.exe", targetExe);
        Extract("CursorQuotaPet.ico", targetIcon);
        DeleteIfExists(Path.Combine(root, "CursorQuotaPet.core.exe"));
        DeleteIfExists(Path.Combine(root, "CursorQuotaPet.original.exe"));
        if (!options.NoShortcuts)
        {
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppTitle + ".lnk"), targetExe, root);
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppTitle + ".lnk"), targetExe, root);
        }
        RememberInstall(root, targetExe);
        return root;
    }

    private static void Extract(string resourceName, string destination)
    {
        using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
        {
            if (input == null)
            {
                throw new InvalidOperationException("安装包里缺少 " + resourceName);
            }
            string temporary = destination + ".new";
            using (FileStream output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                }
            }
            if (File.Exists(destination))
            {
                File.Replace(temporary, destination, null);
            }
            else
            {
                File.Move(temporary, destination);
            }
        }
    }

    private static void StopInstalledProcess(string targetExe)
    {
        string fullTarget = Path.GetFullPath(targetExe);
        Process[] processes = Process.GetProcessesByName("CursorQuotaPet");
        for (int i = 0; i < processes.Length; i++)
        {
            try
            {
                string path = processes[i].MainModule.FileName;
                if (!string.IsNullOrEmpty(path) && string.Equals(Path.GetFullPath(path), fullTarget, StringComparison.OrdinalIgnoreCase))
                {
                    processes[i].Kill();
                }
            }
            catch
            {
            }
        }
    }

    private static string FindExistingRoot()
    {
        string fromRegistry = FindInUninstall(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", false);
        if (fromRegistry != null)
        {
            return fromRegistry;
        }
        fromRegistry = FindInUninstall(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true);
        if (fromRegistry != null)
        {
            return fromRegistry;
        }
        fromRegistry = FindInUninstall(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", true);
        if (fromRegistry != null)
        {
            return fromRegistry;
        }
        DriveInfo[] drives = DriveInfo.GetDrives();
        for (int i = 0; i < drives.Length; i++)
        {
            try
            {
                if (!drives[i].IsReady || drives[i].DriveType == DriveType.Network || drives[i].DriveType == DriveType.CDRom)
                {
                    continue;
                }
                string candidate = Path.Combine(drives[i].RootDirectory.FullName, AppTitle);
                if (File.Exists(Path.Combine(candidate, "CursorQuotaPet.exe")))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }
        return null;
    }

    private static string FindInUninstall(string subKey, bool localMachine)
    {
        try
        {
            Microsoft.Win32.RegistryKey hive = localMachine ? Microsoft.Win32.Registry.LocalMachine : Microsoft.Win32.Registry.CurrentUser;
            using (Microsoft.Win32.RegistryKey root = hive.OpenSubKey(subKey))
            {
                if (root == null)
                {
                    return null;
                }
                string[] names = root.GetSubKeyNames();
                for (int i = 0; i < names.Length; i++)
                {
                    using (Microsoft.Win32.RegistryKey key = root.OpenSubKey(names[i]))
                    {
                        if (key == null)
                        {
                            continue;
                        }
                        object displayName = key.GetValue("DisplayName");
                        object location = key.GetValue("InstallLocation");
                        if (displayName == null || location == null || displayName.ToString() != AppTitle)
                        {
                            continue;
                        }
                        string candidate = Environment.ExpandEnvironmentVariables(location.ToString().Trim());
                        if (File.Exists(Path.Combine(candidate, "CursorQuotaPet.exe")))
                        {
                            return candidate;
                        }
                    }
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static void CreateShortcut(string shortcutPath, string targetExe, string workingDirectory)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(shellType);
        object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
        Type shortcutType = shortcut.GetType();
        shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe });
        shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { "--show" });
        shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
        shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe + ",0" });
        shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
    }

    private static void RememberInstall(string root, string targetExe)
    {
        try
        {
            using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\CursorQuotaPet"))
            {
                key.SetValue("DisplayName", AppTitle);
                key.SetValue("InstallLocation", root);
                key.SetValue("DisplayIcon", targetExe);
                key.SetValue("Publisher", "CursorQuotaPet");
                key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
        }
        catch
        {
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}

internal sealed class SetupForm : Form
{
    private readonly SetupOptions _options;
    private readonly Label _status;
    private readonly Button _close;

    public SetupForm(SetupOptions options)
    {
        _options = options;
        Text = SetupInstaller.AppTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 168);
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        _status = new Label();
        _status.Left = 24;
        _status.Top = 24;
        _status.Width = 392;
        _status.Height = 84;
        _status.Text = "正在安装…";
        _close = new Button();
        _close.Text = "完成";
        _close.Width = 88;
        _close.Height = 30;
        _close.Left = ClientSize.Width - _close.Width - 24;
        _close.Top = ClientSize.Height - _close.Height - 18;
        _close.Enabled = false;
        _close.Click += delegate { Close(); };
        Controls.Add(_status);
        Controls.Add(_close);
        AcceptButton = _close;
        Shown += delegate { BeginInvoke(new Action(RunInstall)); };
    }

    private void RunInstall()
    {
        try
        {
            string root = SetupInstaller.Install(_options);
            _status.Text = "已安装到：" + root + "\r\n\r\n请看任务栏通知区域。鼠标移到图标上，余额会显示在图标正上方。";
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(root, "CursorQuotaPet.exe"),
                    Arguments = "--show",
                    WorkingDirectory = root,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }
        catch (Exception ex)
        {
            _status.Text = "安装失败：" + ex.Message;
            _close.Text = "关闭";
        }
        _close.Enabled = true;
        _close.Focus();
    }
}
