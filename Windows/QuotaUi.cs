using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class GlassTheme
{
    public static readonly Color Mint = Color.FromArgb(22, 148, 108);
    public static readonly Color MintDeep = Color.FromArgb(14, 122, 92);
    public static readonly Color Cyan = Color.FromArgb(125, 211, 252);
    public static readonly Color Peach = Color.FromArgb(255, 183, 162);
    public static readonly Color Lavender = Color.FromArgb(196, 181, 253);
    public static readonly Color Slate = Color.FromArgb(36, 48, 62);
    public static readonly Color SlateMuted = Color.FromArgb(92, 106, 122);
    public static readonly Color Warning = Color.FromArgb(245, 153, 15);
    public static readonly Color Danger = Color.FromArgb(240, 74, 77);
    public const int PanelRadius = 30;
    public const int CardRadius = 22;

    public static Color Emphasis(double? remaining)
    {
        if (!remaining.HasValue)
        {
            return Color.FromArgb(180, SlateMuted);
        }
        if (remaining.Value <= 10)
        {
            return Danger;
        }
        if (remaining.Value <= 30)
        {
            return Warning;
        }
        return Mint;
    }
}

internal sealed class QuotaPopupForm : Form
{
    private readonly QuotaModel _model;
    private readonly Action _refresh;
    private readonly Action _dismiss;
    private Rectangle _refreshRect;
    private Rectangle _closeRect;
    private bool _refreshHot;
    private bool _closeHot;
    private Bitmap _surface;

    public QuotaPopupForm(QuotaModel model, Action refresh, Action dismiss)
    {
        _model = model;
        _refresh = refresh;
        _dismiss = dismiss;

        Text = "Cursor仪表盘";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        Size = new Size(386, 292);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                _dismiss();
            }
        };
        Deactivate += delegate { _dismiss(); };
        MouseMove += OnMouseMove;
        MouseClick += OnMouseClick;
        MouseLeave += delegate
        {
            _refreshHot = false;
            _closeHot = false;
            Present();
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WsExLayered | NativeMethods.WsExToolwindow;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.DisableSystemRounding(Handle);
        Present();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Present();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Present();
    }

    public void Relayout()
    {
        Present();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        bool refreshHot = _refreshRect.Contains(e.Location);
        bool closeHot = _closeRect.Contains(e.Location);
        if (refreshHot != _refreshHot || closeHot != _closeHot)
        {
            _refreshHot = refreshHot;
            _closeHot = closeHot;
            Present();
        }
        Cursor = (refreshHot || closeHot) ? Cursors.Hand : Cursors.Default;
    }

    private void OnMouseClick(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }
        if (_refreshRect.Contains(e.Location))
        {
            _refresh();
            return;
        }
        if (_closeRect.Contains(e.Location))
        {
            _dismiss();
        }
    }

    private void Present()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0)
        {
            return;
        }

        EnsureSurface();
        using (Graphics g = Graphics.FromImage(_surface))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Render(g);
        }

        NativeMethods.PresentLayered(Handle, _surface);
    }

    private void EnsureSurface()
    {
        if (_surface != null && _surface.Width == Width && _surface.Height == Height)
        {
            return;
        }
        if (_surface != null)
        {
            _surface.Dispose();
        }
        _surface = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
    }

    private void Render(Graphics g)
    {
        RectangleF panelBounds = new RectangleF(0f, 0f, Width, Height);
        RectangleF panelBorderBounds = new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
        using (GraphicsPath panel = RoundedRect(panelBounds, GlassTheme.PanelRadius))
        {
            using (Bitmap interior = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
            {
                using (Graphics ig = Graphics.FromImage(interior))
                {
                    ig.CompositingMode = CompositingMode.SourceCopy;
                    ig.Clear(Color.FromArgb(255, 236, 244, 248));
                    ig.CompositingMode = CompositingMode.SourceOver;
                    ig.SmoothingMode = SmoothingMode.AntiAlias;
                    ig.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    DrawBlob(ig, new Rectangle(-80, -90, 260, 260), Color.FromArgb(110, GlassTheme.Cyan));
                    DrawBlob(ig, new Rectangle(230, 10, 280, 280), Color.FromArgb(100, GlassTheme.Peach));
                    DrawBlob(ig, new Rectangle(210, 140, 240, 240), Color.FromArgb(90, GlassTheme.Lavender));
                    DrawBlob(ig, new Rectangle(90, 70, 180, 180), Color.FromArgb(70, Color.White));
                    using (LinearGradientBrush sheen = new LinearGradientBrush(panelBounds, Color.FromArgb(90, Color.White), Color.FromArgb(18, Color.White), LinearGradientMode.Vertical))
                    {
                        ig.FillRectangle(sheen, 0, 0, Width, Height);
                    }
                }
                using (TextureBrush brush = new TextureBrush(interior, WrapMode.Clamp))
                {
                    g.FillPath(brush, panel);
                }
            }

            using (GraphicsPath borderPath = RoundedRect(panelBorderBounds, GlassTheme.PanelRadius - 0.75f))
            using (Pen border = new Pen(Color.FromArgb(200, Color.White), 1f))
            {
                g.DrawPath(border, borderPath);
            }
        }

        QuotaSnapshot snapshot = _model.DisplaySnapshot;
        DrawHeader(g, snapshot);
        DrawCard(g, new Rectangle(14, 70, Width - 28, 78), snapshot != null ? snapshot.Primary : Placeholder("内置"));
        DrawCard(g, new Rectangle(14, 158, Width - 28, 78), snapshot != null ? snapshot.Secondary : Placeholder("其他"));
        DrawFooter(g, snapshot);
    }

    private static QuotaWindow Placeholder(string badge)
    {
        QuotaWindow window = new QuotaWindow();
        window.Badge = badge;
        window.Title = "等待额度数据";
        return window;
    }

    private void DrawHeader(Graphics g, QuotaSnapshot snapshot)
    {
        Rectangle icon = new Rectangle(18, 16, 36, 36);
        using (GraphicsPath circle = new GraphicsPath())
        {
            circle.AddEllipse(icon);
            using (LinearGradientBrush brush = new LinearGradientBrush(icon, Color.FromArgb(97, 158, 255), Color.FromArgb(125, 92, 245), LinearGradientMode.ForwardDiagonal))
            {
                g.FillPath(brush, circle);
            }
        }
        using (Pen gauge = new Pen(Color.White, 2.2f))
        {
            gauge.StartCap = LineCap.Round;
            gauge.EndCap = LineCap.Round;
            g.DrawArc(gauge, icon.X + 8, icon.Y + 9, 20, 20, 140, 260);
            g.FillEllipse(Brushes.White, icon.X + 17, icon.Y + 17, 3, 3);
        }

        string title = QuotaFormatter.PlanTitle(snapshot != null ? snapshot.PlanType : null);
        using (Font font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold))
        using (SolidBrush brush = new SolidBrush(GlassTheme.Slate))
        {
            g.DrawString(title, font, brush, new PointF(62, 22));
        }

        _refreshRect = new Rectangle(Width - 76, 20, 28, 28);
        _closeRect = new Rectangle(Width - 42, 20, 28, 28);
        DrawIconButton(g, _refreshRect, _refreshHot, true);
        DrawIconButton(g, _closeRect, _closeHot, false);
    }

    private static void DrawIconButton(Graphics g, Rectangle rect, bool hot, bool refresh)
    {
        using (GraphicsPath circle = new GraphicsPath())
        {
            circle.AddEllipse(rect);
            using (SolidBrush fill = new SolidBrush(hot ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(180, 255, 255, 255)))
            {
                g.FillPath(fill, circle);
            }
            using (Pen border = new Pen(Color.FromArgb(180, Color.White), 1f))
            {
                g.DrawPath(border, circle);
            }
        }

        using (Pen mark = new Pen(Color.FromArgb(200, GlassTheme.Slate), 1.6f))
        {
            mark.StartCap = LineCap.Round;
            mark.EndCap = LineCap.Round;
            if (refresh)
            {
                g.DrawArc(mark, rect.X + 8, rect.Y + 8, 12, 12, 40, 280);
                g.DrawLine(mark, rect.X + 18, rect.Y + 8, rect.X + 18, rect.Y + 13);
            }
            else
            {
                g.DrawLine(mark, rect.X + 9, rect.Y + 9, rect.X + 19, rect.Y + 19);
                g.DrawLine(mark, rect.X + 19, rect.Y + 9, rect.X + 9, rect.Y + 19);
            }
        }
    }

    private static void DrawCard(Graphics g, Rectangle rect, QuotaWindow window)
    {
        using (GraphicsPath path = RoundedRect(rect, GlassTheme.CardRadius))
        {
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
            {
                g.FillPath(fill, path);
            }
            using (Pen border = new Pen(Color.FromArgb(70, Color.White), 1f))
            {
                border.Alignment = PenAlignment.Inset;
                g.DrawPath(border, path);
            }
        }

        Color tint = GlassTheme.Emphasis(window != null ? window.Remaining : null);
        float progress = 0;
        if (window != null && window.Remaining.HasValue)
        {
            double remain = window.Remaining.Value;
            if (remain < 0) remain = 0;
            if (remain > 100) remain = 100;
            progress = (float)(remain / 100.0);
        }

        Rectangle ring = new Rectangle(rect.X + 14, rect.Y + 12, 54, 54);
        using (Pen track = new Pen(Color.FromArgb(90, Color.White), 6.5f))
        {
            g.DrawEllipse(track, ring);
        }
        if (progress > 0)
        {
            using (Pen arc = new Pen(tint, 6.5f))
            {
                arc.StartCap = LineCap.Round;
                arc.EndCap = LineCap.Round;
                // The colored arc is the remaining quota. Its missing segment is
                // the consumed quota, so a negative sweep makes consumption grow
                // clockwise from the 12 o'clock position.
                g.DrawArc(arc, ring, -90, -360f * progress);
            }
        }

        string badge = window != null && window.Badge != null ? window.Badge : "—";
        using (Font badgeFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold))
        using (SolidBrush brush = new SolidBrush(Color.FromArgb(200, GlassTheme.Slate)))
        {
            SizeF size = g.MeasureString(badge, badgeFont);
            g.DrawString(badge, badgeFont, brush, ring.X + (ring.Width - size.Width) / 2f, ring.Y + (ring.Height - size.Height) / 2f);
        }

        string caption = QuotaFormatter.Caption(window != null ? window.WindowMinutes : null, window != null ? window.Title : "等待额度数据");
        string reset = QuotaFormatter.Reset(window != null ? window.ResetAt : null);
        using (Font titleFont = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold))
        using (Font mutedFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular))
        using (SolidBrush titleBrush = new SolidBrush(GlassTheme.Slate))
        using (SolidBrush mutedBrush = new SolidBrush(GlassTheme.SlateMuted))
        {
            g.DrawString(caption, titleFont, titleBrush, rect.X + 80, rect.Y + 16);
            g.DrawString(reset, mutedFont, mutedBrush, rect.X + 80, rect.Y + 40);
        }

        string percent = QuotaFormatter.Percent(window != null ? window.Remaining : null);
        string burn = QuotaFormatter.BurnRatePerDay(
            window != null ? window.Used : null,
            window != null ? window.Remaining : null,
            window != null ? window.ResetAt : null,
            window != null ? window.WindowMinutes : null);
        using (Font percentFont = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold))
        using (Font burnFont = new Font("Consolas", 8f, FontStyle.Regular))
        using (SolidBrush percentBrush = new SolidBrush(tint))
        using (SolidBrush mutedBrush = new SolidBrush(GlassTheme.SlateMuted))
        {
            SizeF percentSize = g.MeasureString(percent, percentFont);
            g.DrawString(percent, percentFont, percentBrush, rect.Right - percentSize.Width - 14, rect.Y + 12);
            SizeF burnSize = g.MeasureString(burn, burnFont);
            g.DrawString(burn, burnFont, mutedBrush, rect.Right - burnSize.Width - 14, rect.Y + 46);
        }
    }

    private void DrawFooter(Graphics g, QuotaSnapshot snapshot)
    {
        Color tint = (snapshot != null && snapshot.SourceName == "cursor-api") ? GlassTheme.Mint : GlassTheme.Warning;
        string badge = "WAIT";
        if (snapshot != null)
        {
            badge = snapshot.SourceName == "cursor-api" ? "LIVE" : "SNAPSHOT";
        }

        int y = Height - 28;
        using (SolidBrush dot = new SolidBrush(tint))
        {
            g.FillEllipse(dot, 18, y + 4, 7, 7);
        }
        using (Font font = new Font("Microsoft YaHei UI", 7.5f, FontStyle.Regular))
        using (Font badgeFont = new Font("Microsoft YaHei UI", 7f, FontStyle.Bold))
        using (SolidBrush muted = new SolidBrush(GlassTheme.SlateMuted))
        using (SolidBrush badgeBrush = new SolidBrush(tint))
        {
            g.DrawString(_model.Footer, font, muted, 32, y);
            SizeF size = g.MeasureString(badge, badgeFont);
            g.DrawString(badge, badgeFont, badgeBrush, Width - size.Width - 18, y);
        }
    }

    private static void DrawBlob(Graphics g, Rectangle rect, Color color)
    {
        using (GraphicsPath path = new GraphicsPath())
        {
            path.AddEllipse(rect);
            using (PathGradientBrush brush = new PathGradientBrush(path))
            {
                brush.CenterColor = color;
                brush.SurroundColors = new Color[] { Color.FromArgb(0, color) };
                g.FillPath(brush, path);
            }
        }
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        return RoundedRect(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), radius);
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        float d = radius * 2f;
        if (d > rect.Width)
        {
            d = rect.Width;
        }
        if (d > rect.Height)
        {
            d = rect.Height;
        }
        GraphicsPath path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _surface != null)
        {
            _surface.Dispose();
            _surface = null;
        }
        base.Dispose(disposing);
    }
}

internal sealed class QuotaTrayTipForm : Form
{
    private string _line1 = "内置：—";
    private string _line2 = "其他：—";

    public QuotaTrayTipForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(255, 36, 48, 62);
        ForeColor = Color.White;
        Size = new Size(108, 54);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override bool ShowWithoutActivation
    {
        get { return true; }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WsExToolwindow | NativeMethods.WsExNoActivate | NativeMethods.WsExTransparent;
            cp.ClassStyle |= NativeMethods.CsDropShadow;
            return cp;
        }
    }

    public void ShowTip(string line1, string line2)
    {
        _line1 = line1;
        _line2 = line2;
        using (Bitmap measure = new Bitmap(1, 1))
        using (Graphics g = Graphics.FromImage(measure))
        using (Font font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular))
        {
            SizeF first = g.MeasureString(_line1, font);
            SizeF second = g.MeasureString(_line2, font);
            int width = (int)Math.Ceiling(Math.Max(first.Width, second.Width)) + 20;
            int height = (int)Math.Ceiling(first.Height + second.Height) + 16;
            Size = new Size(width, height);
        }
        PositionNearTray();
        if (!Visible)
        {
            Show();
        }
        Invalidate();
        Update();
    }

    public void HideTip()
    {
        if (Visible)
        {
            Hide();
        }
    }

    private void PositionNearTray()
    {
        Point cursor = Control.MousePosition;
        Screen screen = Screen.FromPoint(cursor);
        Rectangle working = screen.WorkingArea;
        int x = cursor.X - Width / 2;
        int y = working.Bottom - Height - 8;
        if (cursor.Y < working.Top + 80)
        {
            y = working.Top + 8;
        }
        if (x < working.Left + 8)
        {
            x = working.Left + 8;
        }
        if (x + Width > working.Right - 8)
        {
            x = working.Right - Width - 8;
        }
        Location = new Point(x, y);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (Font font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular))
        using (SolidBrush brush = new SolidBrush(ForeColor))
        {
            float y = 8f;
            g.DrawString(_line1, font, brush, 10f, y);
            SizeF size = g.MeasureString(_line1, font);
            g.DrawString(_line2, font, brush, 10f, y + size.Height);
        }
    }
}

internal sealed class QuotaApplicationContext : ApplicationContext
{
    private readonly QuotaModel _model;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly QuotaPopupForm _popup;
    private readonly QuotaTrayTipForm _trayTip;
    private readonly Timer _trayTipTimer;
    private readonly bool _showOnStart;
    private System.Threading.RegisteredWaitHandle _showRegistration;
    private Icon _currentIcon;
    private DateTime _hiddenAtUtc = DateTime.MinValue;
    private DateTime _trayHoverStartUtc = DateTime.MinValue;
    private DateTime _trayHoverLastUtc = DateTime.MinValue;
    private bool _trayHovering;
    private string _tipLine1 = "内置：—";
    private string _tipLine2 = "其他：—";

    public QuotaApplicationContext(bool showOnStart)
    {
        _showOnStart = showOnStart;
        _model = new QuotaModel();
        _popup = new QuotaPopupForm(_model, RefreshQuota, ClosePopup);
        IntPtr unusedHandle = _popup.Handle;
        if (unusedHandle == IntPtr.Zero)
        {
        }
        _model.MarshalControl = _popup;
        _menu = BuildMenu();
        _menu.Opening += delegate { HideTrayTip(); };

        _trayTip = new QuotaTrayTipForm();
        _trayTipTimer = new Timer();
        _trayTipTimer.Interval = 80;
        _trayTipTimer.Tick += OnTrayTipTimerTick;

        _notifyIcon = new NotifyIcon();
        _notifyIcon.Visible = true;
        _notifyIcon.Text = "";
        _notifyIcon.ContextMenuStrip = _menu;
        _notifyIcon.MouseMove += OnTrayMouseMove;
        _notifyIcon.MouseDown += OnTrayMouseDown;
        _notifyIcon.MouseUp += OnTrayMouseUp;

        _model.Changed += UpdateTray;
        _model.Start();
        UpdateTray();

        _showRegistration = System.Threading.ThreadPool.RegisterWaitForSingleObject(
            SingleInstance.ShowEvent,
            delegate(object state, bool timedOut)
            {
                if (timedOut || _popup.IsDisposed || !_popup.IsHandleCreated)
                {
                    return;
                }
                try
                {
                    _popup.BeginInvoke(new MethodInvoker(ShowPopup));
                }
                catch
                {
                }
            },
            null,
            System.Threading.Timeout.Infinite,
            false);

        if (_showOnStart)
        {
            ShowPopup();
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("显示额度", null, delegate { ShowPopup(); });
        menu.Items.Add("立即刷新", null, delegate { RefreshQuota(); });
        menu.Items.Add("打开 Cursor 用量页", null, delegate { OpenDashboard(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, delegate { Quit(); });
        return menu;
    }

    private void OnTrayMouseMove(object sender, MouseEventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        if (!_trayHovering)
        {
            _trayHovering = true;
            _trayHoverStartUtc = now;
            _trayTipTimer.Start();
        }
        _trayHoverLastUtc = now;
    }

    private void OnTrayMouseDown(object sender, MouseEventArgs e)
    {
        HideTrayTip();
    }

    private void OnTrayTipTimerTick(object sender, EventArgs e)
    {
        if (!IsCursorOverTrayIcon() || _popup.Visible)
        {
            HideTrayTip();
            return;
        }
        if ((DateTime.UtcNow - _trayHoverStartUtc).TotalMilliseconds >= 400 && !_trayTip.Visible)
        {
            _trayTip.ShowTip(_tipLine1, _tipLine2);
        }
    }

    private bool IsCursorOverTrayIcon()
    {
        Rectangle bounds;
        if (TryGetTrayIconRect(out bounds))
        {
            bounds.Inflate(8, 8);
            return bounds.Contains(Control.MousePosition);
        }
        return (DateTime.UtcNow - _trayHoverLastUtc).TotalMilliseconds <= 800;
    }

    private bool TryGetTrayIconRect(out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        try
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo idField = typeof(NotifyIcon).GetField("id", flags);
            FieldInfo windowField = typeof(NotifyIcon).GetField("window", flags);
            if (idField == null || windowField == null)
            {
                return false;
            }
            NativeWindow window = windowField.GetValue(_notifyIcon) as NativeWindow;
            if (window == null || window.Handle == IntPtr.Zero)
            {
                return false;
            }
            NativeMethods.NotifyIconIdentifier identifier = new NativeMethods.NotifyIconIdentifier();
            identifier.cbSize = Marshal.SizeOf(typeof(NativeMethods.NotifyIconIdentifier));
            identifier.hWnd = window.Handle;
            identifier.uID = (int)idField.GetValue(_notifyIcon);
            NativeMethods.Rect rect;
            if (NativeMethods.Shell_NotifyIconGetRect(ref identifier, out rect) != 0)
            {
                return false;
            }
            bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return bounds.Width > 0 && bounds.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    private void HideTrayTip()
    {
        _trayHovering = false;
        _trayTipTimer.Stop();
        _trayTip.HideTip();
    }

    private void OnTrayMouseUp(object sender, MouseEventArgs e)
    {
        HideTrayTip();
        if (e.Button != MouseButtons.Left)
        {
            return;
        }
        if ((DateTime.UtcNow - _hiddenAtUtc).TotalMilliseconds < 400)
        {
            return;
        }
        if (_popup.Visible)
        {
            ClosePopup();
        }
        else
        {
            ShowPopup();
        }
    }

    private void ShowPopup()
    {
        HideTrayTip();
        PositionPopup();
        _popup.Relayout();
        _popup.Show();
        _popup.Activate();
        NativeMethods.DisableSystemRounding(_popup.Handle);
    }

    private void ClosePopup()
    {
        if (_popup.Visible)
        {
            _popup.Hide();
            _hiddenAtUtc = DateTime.UtcNow;
        }
    }

    private void PositionPopup()
    {
        Rectangle working = Screen.PrimaryScreen.WorkingArea;
        Point cursor = Control.MousePosition;
        Screen screen = Screen.FromPoint(cursor);
        working = screen.WorkingArea;
        int x = cursor.X - _popup.Width / 2;
        int y = working.Bottom - _popup.Height - 8;
        if (cursor.Y < working.Top + 80)
        {
            y = working.Top + 8;
        }
        if (x < working.Left + 8)
        {
            x = working.Left + 8;
        }
        if (x + _popup.Width > working.Right - 8)
        {
            x = working.Right - _popup.Width - 8;
        }
        _popup.Location = new Point(x, y);
    }

    private void RefreshQuota()
    {
        _model.Refresh(true);
    }

    private static void OpenDashboard()
    {
        try
        {
            System.Diagnostics.Process.Start("https://cursor.com/dashboard/usage");
        }
        catch
        {
        }
    }

    private void Quit()
    {
        _model.Stop();
        _notifyIcon.Visible = false;
        _popup.Close();
        ExitThread();
    }

    private void UpdateTray()
    {
        QuotaSnapshot snapshot = _model.DisplaySnapshot;
        double? primaryRemain = null;
        double? secondaryRemain = null;
        if (snapshot != null && snapshot.Primary != null)
        {
            primaryRemain = snapshot.Primary.Remaining;
        }
        if (snapshot != null && snapshot.Secondary != null)
        {
            secondaryRemain = snapshot.Secondary.Remaining;
        }

        _tipLine1 = "内置：" + QuotaFormatter.Percent(primaryRemain);
        _tipLine2 = "其他：" + QuotaFormatter.Percent(secondaryRemain);
        _notifyIcon.Text = "";
        if (_trayTip.Visible)
        {
            _trayTip.ShowTip(_tipLine1, _tipLine2);
        }

        Icon icon = TrayIconFactory.Create(primaryRemain, secondaryRemain);
        Icon old = _currentIcon;
        _notifyIcon.Icon = icon;
        _currentIcon = icon;
        if (old != null)
        {
            old.Dispose();
        }

        if (_popup.Visible)
        {
            _popup.Relayout();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_showRegistration != null)
            {
                _showRegistration.Unregister(null);
                _showRegistration = null;
            }
            if (_model != null)
            {
                _model.Stop();
            }
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            if (_currentIcon != null)
            {
                _currentIcon.Dispose();
            }
            if (_menu != null)
            {
                _menu.Dispose();
            }
            if (_trayTipTimer != null)
            {
                _trayTipTimer.Stop();
                _trayTipTimer.Dispose();
            }
            if (_trayTip != null)
            {
                _trayTip.Dispose();
            }
            if (_popup != null)
            {
                _popup.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}

internal static class TrayIconFactory
{
    public static Icon Create(double? primary, double? secondary)
    {
        Bitmap bitmap = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.Transparent);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(1, 1, 30, 30);
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(235, 36, 48, 62)))
                {
                    g.FillPath(fill, path);
                }
            }

            using (Font font = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (SolidBrush primaryBrush = new SolidBrush(GlassTheme.Emphasis(primary)))
            using (SolidBrush secondaryBrush = new SolidBrush(GlassTheme.Emphasis(secondary)))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                g.DrawString(ShortPercent(primary), font, primaryBrush, new RectangleF(0, 2, 32, 14), format);
                g.DrawString(ShortPercent(secondary), font, secondaryBrush, new RectangleF(0, 15, 32, 14), format);
            }
        }

        IntPtr handle = bitmap.GetHicon();
        Icon created = Icon.FromHandle(handle);
        Icon clone = (Icon)created.Clone();
        created.Dispose();
        NativeMethods.DestroyIcon(handle);
        bitmap.Dispose();
        return clone;
    }

    private static string ShortPercent(double? value)
    {
        if (!value.HasValue)
        {
            return "—";
        }
        return ((int)Math.Round(value.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

internal static class NativeMethods
{
    public const int WsExLayered = 0x00080000;
    public const int WsExToolwindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExTransparent = 0x00000020;
    public const int CsDropShadow = 0x00020000;
    private const int UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpDoNotRound = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NotifyIconIdentifier
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public Guid guidItem;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect iconLocation);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        IntPtr pptDst,
        ref SIZE psize,
        IntPtr hdcSrc,
        ref POINT pptSrc,
        int crKey,
        ref BLENDFUNCTION pblend,
        int dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void DisableSystemRounding(IntPtr hwnd)
    {
        try
        {
            int preference = DwmwcpDoNotRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, 4);
        }
        catch
        {
        }
    }

    public static void PresentLayered(IntPtr hwnd, Bitmap bitmap)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;
        try
        {
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            oldBitmap = SelectObject(memDc, hBitmap);
            SIZE size = new SIZE();
            size.Cx = bitmap.Width;
            size.Cy = bitmap.Height;
            POINT source = new POINT();
            BLENDFUNCTION blend = new BLENDFUNCTION();
            blend.BlendOp = AcSrcOver;
            blend.BlendFlags = 0;
            blend.SourceConstantAlpha = 255;
            blend.AlphaFormat = AcSrcAlpha;
            UpdateLayeredWindow(hwnd, screenDc, IntPtr.Zero, ref size, memDc, ref source, 0, ref blend, UlwAlpha);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
            {
                SelectObject(memDc, oldBitmap);
            }
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }
            if (memDc != IntPtr.Zero)
            {
                DeleteDC(memDc);
            }
            if (screenDc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }
}
