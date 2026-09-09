using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal static class CursorQuotaPetMain
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool probe = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--probe", StringComparison.OrdinalIgnoreCase))
            {
                probe = true;
                break;
            }
        }

        try
        {
            ServicePointInit.EnableTls();
            Application.SetCompatibleTextRenderingDefault(false);
            TryDpiAware();
        }
        catch
        {
        }

        if (probe)
        {
            TextWriter writer = ProbeConsole.Open();
            Console.SetOut(writer);
            Console.SetError(writer);
            int code = ProbeRunner.Run();
            writer.Flush();
            Environment.Exit(code);
            return;
        }

        if (!SingleInstance.TryAcquire())
        {
            return;
        }

        NativeBootstrap.HideConsoleWindow();
        Application.EnableVisualStyles();
        Application.Run(new QuotaApplicationContext());
    }

    private static void TryDpiAware()
    {
        try
        {
            NativeBootstrap.SetProcessDPIAware();
        }
        catch
        {
        }
    }
}

internal static class ProbeConsole
{
    public static TextWriter Open()
    {
        const int attachParentProcess = -1;
        NativeBootstrap.AttachConsole(attachParentProcess);
        Stream stdout = Console.OpenStandardOutput();
        if (stdout == Stream.Null)
        {
            NativeBootstrap.AllocConsole();
            stdout = Console.OpenStandardOutput();
        }
        StreamWriter writer = new StreamWriter(stdout, new UTF8Encoding(false));
        writer.AutoFlush = true;
        return writer;
    }
}

internal static class ServicePointInit
{
    public static void EnableTls()
    {
        try
        {
            System.Net.ServicePointManager.SecurityProtocol =
                (System.Net.SecurityProtocolType)0xC00 | (System.Net.SecurityProtocolType)0x3000;
        }
        catch
        {
            System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)0xC00;
        }
    }
}

internal static class SingleInstance
{
    private static System.Threading.Mutex _mutex;

    public static bool TryAcquire()
    {
        bool created;
        _mutex = new System.Threading.Mutex(true, @"Local\CursorQuotaPet", out created);
        return created;
    }
}

internal static class NativeBootstrap
{
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static void HideConsoleWindow()
    {
        try
        {
            IntPtr hwnd = GetConsoleWindow();
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, 0);
            }
        }
        catch
        {
        }
    }
}
