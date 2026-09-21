using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class CursorQuotaPetHotfix
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const int TipGap = 8;

    private static ApplicationContext _context;
    private static MethodInfo _tryGetTrayIconRect;
    private static Form _tipForm;
    private static bool _aligning;
    private static bool _hasIconCache;
    private static Rectangle _cachedIcon;

    [STAThread]
    private static void Main(string[] args)
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string corePath = Path.Combine(baseDirectory, "CursorQuotaPet.core.exe");
        if (!File.Exists(corePath))
        {
            MessageBox.Show(
                "缺少 CursorQuotaPet.core.exe，无法启动额度仪表盘。",
                "Cursor仪表盘",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (HasArgument(args, "--probe"))
        {
            RunProbe(corePath);
            return;
        }

        Assembly core = Assembly.LoadFrom(corePath);
        Type singleInstanceType = core.GetType("SingleInstance", true);
        MethodInfo tryAcquire = singleInstanceType.GetMethod("TryAcquire", AnyStatic);
        MethodInfo requestShow = singleInstanceType.GetMethod("RequestShow", AnyStatic);
        MethodInfo release = singleInstanceType.GetMethod("Release", AnyStatic);
        bool showOnStart = HasArgument(args, "--show");

        bool acquired = (bool)tryAcquire.Invoke(null, null);
        if (!acquired)
        {
            if (showOnStart && requestShow != null)
            {
                requestShow.Invoke(null, null);
            }
            return;
        }

        Timer discoverTimer = null;
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _context = CreateContext(core, showOnStart, out _tryGetTrayIconRect);

            discoverTimer = new Timer();
            discoverTimer.Interval = 200;
            discoverTimer.Tick += delegate { HookTipFormIfNeeded(); };
            discoverTimer.Start();

            Application.Run(_context);
        }
        finally
        {
            if (discoverTimer != null)
            {
                discoverTimer.Stop();
                discoverTimer.Dispose();
            }
            release.Invoke(null, null);
        }
    }

    private static ApplicationContext CreateContext(
        Assembly core,
        bool showOnStart,
        out MethodInfo tryGetTrayIconRect)
    {
        Type contextType = core.GetType("QuotaApplicationContext", true);
        tryGetTrayIconRect = contextType.GetMethod("TryGetTrayIconRect", AnyInstance);
        if (tryGetTrayIconRect == null)
        {
            throw new MissingMethodException(contextType.FullName, "TryGetTrayIconRect");
        }

        return (ApplicationContext)Activator.CreateInstance(
            contextType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { showOnStart },
            null);
    }

    private static void HookTipFormIfNeeded()
    {
        foreach (Form form in Application.OpenForms)
        {
            if (!string.Equals(form.GetType().Name, "QuotaTrayTipForm", StringComparison.Ordinal))
            {
                continue;
            }
            if (_tipForm == form)
            {
                return;
            }

            _tipForm = form;
            NativeAlign.EnsureMousePassthrough(form.Handle);
            form.VisibleChanged += OnTipVisibleChanged;
            form.LocationChanged += OnTipLocationChanged;
            if (form.Visible)
            {
                AlignTip(true);
            }
            return;
        }
    }

    private static void OnTipVisibleChanged(object sender, EventArgs e)
    {
        if (_tipForm == null || !_tipForm.Visible)
        {
            _hasIconCache = false;
            return;
        }
        NativeAlign.EnsureMousePassthrough(_tipForm.Handle);
        AlignTip(true);
    }

    private static void OnTipLocationChanged(object sender, EventArgs e)
    {
        if (_aligning || _tipForm == null || !_tipForm.Visible)
        {
            return;
        }
        AlignTip(false);
    }

    private static void AlignTip(bool refreshIcon)
    {
        if (_tipForm == null || !_tipForm.Visible || _aligning)
        {
            return;
        }

        if (refreshIcon || !_hasIconCache)
        {
            _cachedIcon = ResolveHoverIconRect(_tipForm.Handle);
            _hasIconCache = _cachedIcon.Width > 0 && _cachedIcon.Height > 0;
        }
        if (!_hasIconCache)
        {
            return;
        }

        Point location = LocationAboveIcon(_cachedIcon, _tipForm.Size);
        if (Math.Abs(_tipForm.Left - location.X) <= 2 && Math.Abs(_tipForm.Top - location.Y) <= 2)
        {
            return;
        }

        _aligning = true;
        try
        {
            _tipForm.Location = location;
        }
        finally
        {
            _aligning = false;
        }
    }

    private static Rectangle ResolveHoverIconRect(IntPtr tipHandle)
    {
        Point mouse = Control.MousePosition;
        Rectangle trayRect;
        if (TryInvokeTrayIconRect(out trayRect))
        {
            Rectangle hit = trayRect;
            hit.Inflate(12, 12);
            if (hit.Contains(mouse))
            {
                return trayRect;
            }
        }

        Rectangle fromPoint;
        if (TryGetVisibleIconRectFromPoint(mouse, tipHandle, out fromPoint))
        {
            return fromPoint;
        }

        int size = 40;
        if (trayRect.Width > 0 && trayRect.Height > 0)
        {
            size = Math.Max(24, Math.Max(trayRect.Width, trayRect.Height));
        }
        else
        {
            int metric = NativeAlign.GetSystemMetrics(NativeAlign.SmCxsmicon);
            if (metric >= 16)
            {
                size = Math.Max(32, metric * 2);
            }
        }
        return new Rectangle(mouse.X - size / 2, mouse.Y - size / 2, size, size);
    }

    private static bool TryInvokeTrayIconRect(out Rectangle trayRect)
    {
        trayRect = Rectangle.Empty;
        object[] arguments = { Rectangle.Empty };
        try
        {
            bool found = (bool)_tryGetTrayIconRect.Invoke(_context, arguments);
            if (!found || !(arguments[0] is Rectangle))
            {
                return false;
            }
            trayRect = (Rectangle)arguments[0];
            return trayRect.Width > 0 && trayRect.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetVisibleIconRectFromPoint(Point screenPoint, IntPtr tipHandle, out Rectangle bounds)
    {
        if (TryGetAccessibleIconRect(screenPoint, tipHandle, out bounds))
        {
            return true;
        }

        IntPtr hwnd = NativeAlign.WindowFromPoint(screenPoint.X, screenPoint.Y);
        for (int i = 0; i < 8 && hwnd != IntPtr.Zero; i++)
        {
            if (hwnd == tipHandle)
            {
                break;
            }
            NativeAlign.Rect rect;
            if (NativeAlign.GetWindowRect(hwnd, out rect))
            {
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width >= 16 && width <= 80 && height >= 16 && height <= 80)
                {
                    bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                    return true;
                }
            }
            IntPtr parent = NativeAlign.GetAncestor(hwnd, NativeAlign.GaParent);
            if (parent == IntPtr.Zero || parent == hwnd)
            {
                break;
            }
            hwnd = parent;
        }

        bounds = Rectangle.Empty;
        return false;
    }

    private static bool TryGetAccessibleIconRect(Point screenPoint, IntPtr tipHandle, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        try
        {
            NativeAlign.POINT pt = new NativeAlign.POINT();
            pt.X = screenPoint.X;
            pt.Y = screenPoint.Y;
            object acc;
            object child;
            if (NativeAlign.AccessibleObjectFromPoint(pt, out acc, out child) != 0 || acc == null)
            {
                return false;
            }

            object[] args = new object[] { 0, 0, 0, 0, child };
            ParameterModifier modifiers = new ParameterModifier(5);
            modifiers[0] = true;
            modifiers[1] = true;
            modifiers[2] = true;
            modifiers[3] = true;
            acc.GetType().InvokeMember(
                "accLocation",
                BindingFlags.InvokeMethod,
                null,
                acc,
                args,
                new ParameterModifier[] { modifiers },
                null,
                null);
            int x = Convert.ToInt32(args[0]);
            int y = Convert.ToInt32(args[1]);
            int width = Convert.ToInt32(args[2]);
            int height = Convert.ToInt32(args[3]);
            if (width < 12 || width > 80 || height < 12 || height > 80)
            {
                return false;
            }

            bounds = new Rectangle(x, y, width, height);
            if (tipHandle != IntPtr.Zero)
            {
                NativeAlign.Rect tipRect;
                if (NativeAlign.GetWindowRect(tipHandle, out tipRect))
                {
                    Rectangle tipBounds = Rectangle.FromLTRB(tipRect.Left, tipRect.Top, tipRect.Right, tipRect.Bottom);
                    if (tipBounds.Contains(bounds) || bounds.Contains(tipBounds.Location))
                    {
                        bounds = Rectangle.Empty;
                        return false;
                    }
                }
            }
            return true;
        }
        catch
        {
        }
        return false;
    }

    private static Point LocationAboveIcon(Rectangle iconBounds, Size tipSize)
    {
        if (iconBounds.Width <= 0 || iconBounds.Height <= 0)
        {
            Point mouse = Control.MousePosition;
            iconBounds = new Rectangle(mouse.X - 20, mouse.Y - 20, 40, 40);
        }

        Screen screen = Screen.FromRectangle(iconBounds);
        Rectangle area = screen.Bounds;
        int x = iconBounds.Left + (iconBounds.Width - tipSize.Width) / 2;
        int y = iconBounds.Top - tipSize.Height - TipGap;
        if (x < area.Left + TipGap)
        {
            x = area.Left + TipGap;
        }
        if (x + tipSize.Width > area.Right - TipGap)
        {
            x = Math.Max(area.Left + TipGap, area.Right - tipSize.Width - TipGap);
        }
        if (y < area.Top + TipGap)
        {
            y = iconBounds.Bottom + TipGap;
            if (y + tipSize.Height > area.Bottom - TipGap)
            {
                y = area.Top + TipGap;
            }
        }
        return new Point(x, y);
    }

    private static bool HasArgument(string[] args, string expected)
    {
        if (args == null)
        {
            return false;
        }
        foreach (string arg in args)
        {
            if (string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static void RunProbe(string corePath)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = corePath,
            Arguments = "--probe",
            WorkingDirectory = Path.GetDirectoryName(corePath),
            UseShellExecute = false
        };
        using (Process process = Process.Start(startInfo))
        {
            process.WaitForExit();
            Environment.ExitCode = process.ExitCode;
        }
    }
}

internal static class NativeAlign
{
    public const int SmCxsmicon = 49;
    public const uint GaParent = 1;
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolwindow = 0x00000080;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    public static IntPtr WindowFromPoint(int x, int y)
    {
        POINT point = new POINT();
        point.X = x;
        point.Y = y;
        return WindowFromPoint(point);
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out Rect lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newLong);

    [DllImport("oleacc.dll")]
    public static extern int AccessibleObjectFromPoint(
        POINT pt,
        [MarshalAs(UnmanagedType.IUnknown)] out object acc,
        [MarshalAs(UnmanagedType.Struct)] out object child);

    public static void EnsureMousePassthrough(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        try
        {
            int style = GetWindowLong(hwnd, GwlExStyle);
            int next = style | WsExTransparent | WsExNoActivate | WsExToolwindow;
            if (next != style)
            {
                SetWindowLong(hwnd, GwlExStyle, next);
            }
        }
        catch
        {
        }
    }
}
