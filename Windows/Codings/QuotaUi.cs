using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;

internal static class GlassTheme
{
    public static readonly Color Mint = Color.FromArgb(22, 148, 108);
    public static readonly Color Cyan = Color.FromArgb(125, 211, 252);
    public static readonly Color Peach = Color.FromArgb(255, 183, 162);
    public static readonly Color Lavender = Color.FromArgb(196, 181, 253);
    public static readonly Color Slate = Color.FromArgb(36, 48, 62);
    public static readonly Color SlateMuted = Color.FromArgb(92, 106, 122);
    public static readonly Color Warning = Color.FromArgb(245, 153, 15);
    public static readonly Color Danger = Color.FromArgb(240, 74, 77);
    public const int PanelWidth = 386;
    public const int PanelHeight = 292;
    public const int Shadow = 18;
    public const int PanelRadius = 28;
    public const int CardRadius = 16;
    public const int TipGap = 8;

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

internal static class PopupAnchor
{
    public static Point Above(Rectangle icon, Size window, int insetX, int insetY, Size content, int gap, Rectangle screen)
    {
        int x = icon.Left + (icon.Width - content.Width) / 2 - insetX;
        int y = icon.Top - gap - content.Height - insetY;
        int minX = screen.Left + 4;
        int maxX = screen.Right - window.Width - 4;
        if (x < minX)
        {
            x = minX;
        }
        if (x > maxX)
        {
            x = Math.Max(minX, maxX);
        }
        if (y < screen.Top + 4)
        {
            y = icon.Bottom + gap - insetY;
            int maxY = screen.Bottom - window.Height - 4;
            if (y > maxY)
            {
                y = screen.Top + 4;
            }
        }
        return new Point(x, y);
    }
}

internal static class IconLocator
{
    public static Rectangle Resolve(Point mouse, Rectangle traySlot, bool hasElement, Rectangle element, out bool anchored)
    {
        if (traySlot.Width > 0 && traySlot.Height > 0)
        {
            Rectangle hit = traySlot;
            hit.Inflate(4, 4);
            if (hit.Contains(mouse))
            {
                anchored = true;
                return traySlot;
            }
        }
        if (hasElement && element.Width >= 16 && element.Height >= 16 && element.Contains(mouse))
        {
            anchored = true;
            return element;
        }
        anchored = false;
        int width = traySlot.Width >= 16 ? traySlot.Width : 32;
        int height = traySlot.Height >= 16 ? traySlot.Height : 32;
        return new Rectangle(mouse.X - width / 2, mouse.Y - height / 2, width, height);
    }

    public static bool TryVisualElement(Point mouse, IntPtr excludeA, IntPtr excludeB, out Rectangle bounds)
    {
        if (TryAutomation(mouse, excludeA, excludeB, out bounds))
        {
            return true;
        }
        return TryAccessible(mouse, excludeA, excludeB, out bounds);
    }

    private static bool TryAutomation(Point mouse, IntPtr excludeA, IntPtr excludeB, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        try
        {
            AutomationElement element = AutomationElement.FromPoint(new System.Windows.Point(mouse.X, mouse.Y));
            Rectangle best = Rectangle.Empty;
            while (element != null)
            {
                System.Windows.Rect rect = element.Current.BoundingRectangle;
                if (rect.IsEmpty || double.IsInfinity(rect.Width) || double.IsInfinity(rect.Height))
                {
                    break;
                }
                Rectangle candidate = Rectangle.Truncate(new RectangleF((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height));
                if (!candidate.Contains(mouse))
                {
                    break;
                }
                if (candidate.Width >= 16 && candidate.Width <= 96 && candidate.Height >= 16 && candidate.Height <= 96
                    && !OverlapsWindow(candidate, excludeA) && !OverlapsWindow(candidate, excludeB))
                {
                    best = candidate;
                }
                else if (candidate.Width > 96 || candidate.Height > 96)
                {
                    break;
                }
                element = TreeWalker.ControlViewWalker.GetParent(element);
            }
            if (best.Width <= 0)
            {
                return false;
            }
            bounds = best;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryAccessible(Point mouse, IntPtr excludeA, IntPtr excludeB, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        try
        {
            NativeMethods.POINT point = new NativeMethods.POINT();
            point.X = mouse.X;
            point.Y = mouse.Y;
            object accessible;
            object child;
            if (NativeMethods.AccessibleObjectFromPoint(point, out accessible, out child) != 0 || accessible == null)
            {
                return false;
            }
            object[] args = new object[] { 0, 0, 0, 0, child };
            ParameterModifier modifiers = new ParameterModifier(5);
            modifiers[0] = true;
            modifiers[1] = true;
            modifiers[2] = true;
            modifiers[3] = true;
            accessible.GetType().InvokeMember(
                "accLocation",
                BindingFlags.InvokeMethod,
                null,
                accessible,
                args,
                new ParameterModifier[] { modifiers },
                null,
                null);
            int x = Convert.ToInt32(args[0]);
            int y = Convert.ToInt32(args[1]);
            int width = Convert.ToInt32(args[2]);
            int height = Convert.ToInt32(args[3]);
            if (width < 16 || width > 96 || height < 16 || height > 96)
            {
                return false;
            }
            Rectangle candidate = new Rectangle(x, y, width, height);
            if (!candidate.Contains(mouse) || OverlapsWindow(candidate, excludeA) || OverlapsWindow(candidate, excludeB))
            {
                return false;
            }
            bounds = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool OverlapsWindow(Rectangle bounds, IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }
        NativeMethods.RECT rect;
        if (!NativeMethods.GetWindowRect(hwnd, out rect))
        {
            return false;
        }
        Rectangle window = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return window.Width > 0 && window.IntersectsWith(bounds) && window.Contains(bounds);
    }
}

internal static class DetailPainter
{
    public static Bitmap Render(QuotaSnapshot snapshot, string footer, out Rectangle refresh, out Rectangle close)
    {
        int width = GlassTheme.PanelWidth + GlassTheme.Shadow * 2;
        int height = GlassTheme.PanelHeight + GlassTheme.Shadow * 2;
        Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            Rectangle panel = new Rectangle(GlassTheme.Shadow, GlassTheme.Shadow, GlassTheme.PanelWidth, GlassTheme.PanelHeight);
            DrawPanel(g, panel);
            DrawHeader(g, panel, snapshot, out refresh, out close);
            QuotaWindow primary = snapshot != null ? snapshot.Primary : Placeholder("内置");
            QuotaWindow secondary = snapshot != null ? snapshot.Secondary : Placeholder("其他");
            DrawCard(g, new Rectangle(panel.X + 14, panel.Y + 70, panel.Width - 28, 82), primary);
            DrawCard(g, new Rectangle(panel.X + 14, panel.Y + 160, panel.Width - 28, 82), secondary);
            DrawFooter(g, panel, snapshot, footer);
        }
        return bitmap;
    }

    private static QuotaWindow Placeholder(string badge)
    {
        QuotaWindow window = new QuotaWindow();
        window.Badge = badge;
        window.Title = "等待额度数据";
        return window;
    }

    private static void DrawPanel(Graphics g, Rectangle panel)
    {
        using (GraphicsPath path = Rounded(panel, GlassTheme.PanelRadius))
        using (Bitmap interior = new Bitmap(panel.Width, panel.Height, PixelFormat.Format32bppPArgb))
        {
            using (Graphics ig = Graphics.FromImage(interior))
            {
                ig.SmoothingMode = SmoothingMode.AntiAlias;
                ig.Clear(Color.FromArgb(248, 236, 244, 248));
                DrawBlob(ig, new Rectangle(-70, -80, 240, 240), Color.FromArgb(90, GlassTheme.Cyan));
                DrawBlob(ig, new Rectangle(panel.Width - 150, 0, 250, 250), Color.FromArgb(80, GlassTheme.Peach));
                DrawBlob(ig, new Rectangle(panel.Width - 180, 120, 220, 220), Color.FromArgb(70, GlassTheme.Lavender));
            }
            using (TextureBrush brush = new TextureBrush(interior, WrapMode.Clamp))
            {
                brush.TranslateTransform(panel.X, panel.Y);
                g.FillPath(brush, path);
            }
            using (Pen border = new Pen(Color.FromArgb(220, Color.White), 1.2f))
            {
                g.DrawPath(border, path);
            }
        }
    }

    private static void DrawHeader(Graphics g, Rectangle panel, QuotaSnapshot snapshot, out Rectangle refresh, out Rectangle close)
    {
        Rectangle icon = new Rectangle(panel.X + 18, panel.Y + 16, 36, 36);
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
        using (Font font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (SolidBrush brush = new SolidBrush(GlassTheme.Slate))
        {
            g.DrawString(title, font, brush, panel.X + 62, panel.Y + 16);
        }
        string reset = QuotaFormatter.ResetAt(snapshot != null && snapshot.Primary != null ? snapshot.Primary.ResetAt : (double?)null);
        if (!string.IsNullOrEmpty(reset))
        {
            using (Font font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush brush = new SolidBrush(GlassTheme.SlateMuted))
            {
                g.DrawString(reset, font, brush, panel.X + 62, panel.Y + 36);
            }
        }

        refresh = new Rectangle(panel.Right - 76, panel.Y + 18, 28, 28);
        close = new Rectangle(panel.Right - 42, panel.Y + 18, 28, 28);
        DrawIconButton(g, refresh, false, true);
        DrawIconButton(g, close, false, false);
    }

    public static void DrawIconButton(Graphics g, Rectangle rect, bool hot, bool refresh)
    {
        using (SolidBrush fill = new SolidBrush(hot ? Color.FromArgb(230, 255, 255, 255) : Color.FromArgb(170, 255, 255, 255)))
        {
            g.FillEllipse(fill, rect);
        }
        using (Pen mark = new Pen(Color.FromArgb(210, GlassTheme.Slate), 1.6f))
        {
            mark.StartCap = LineCap.Round;
            mark.EndCap = LineCap.Round;
            if (refresh)
            {
                g.DrawArc(mark, rect.X + 7, rect.Y + 7, 14, 14, 40, 280);
                g.DrawLine(mark, rect.Right - 8, rect.Y + 8, rect.Right - 8, rect.Y + 13);
            }
            else
            {
                g.DrawLine(mark, rect.X + 9, rect.Y + 9, rect.Right - 9, rect.Bottom - 9);
                g.DrawLine(mark, rect.Right - 9, rect.Y + 9, rect.X + 9, rect.Bottom - 9);
            }
        }
    }

    private static void DrawCard(Graphics g, Rectangle rect, QuotaWindow window)
    {
        using (GraphicsPath path = Rounded(rect, GlassTheme.CardRadius))
        using (SolidBrush fill = new SolidBrush(Color.FromArgb(168, 255, 255, 255)))
        {
            g.FillPath(fill, path);
        }

        Color tint = GlassTheme.Emphasis(window != null ? window.Remaining : (double?)null);
        float progress = 0;
        if (window != null && window.Remaining.HasValue)
        {
            progress = (float)(Math.Max(0, Math.Min(100, window.Remaining.Value)) / 100.0);
        }
        Rectangle ring = new Rectangle(rect.X + 14, rect.Y + 14, 54, 54);
        using (Pen track = new Pen(Color.FromArgb(40, GlassTheme.Slate), 6f))
        {
            g.DrawEllipse(track, ring);
        }
        if (progress > 0)
        {
            using (Pen arc = new Pen(tint, 6f))
            {
                arc.StartCap = LineCap.Round;
                arc.EndCap = LineCap.Round;
                g.DrawArc(arc, ring, -90, -360f * progress);
            }
        }

        string badge = window != null && !string.IsNullOrEmpty(window.Badge) ? window.Badge : "—";
        using (Font font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (SolidBrush brush = new SolidBrush(Color.FromArgb(200, GlassTheme.Slate)))
        using (StringFormat format = new StringFormat())
        {
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            g.DrawString(badge, font, brush, ring, format);
        }

        string caption = QuotaFormatter.Caption(window != null ? window.WindowMinutes : (double?)null, window != null ? window.Title : "等待额度数据");
        string reset = QuotaFormatter.Reset(window != null ? window.ResetAt : (double?)null);
        string percent = QuotaFormatter.Percent(window != null ? window.Remaining : (double?)null);
        string burn = QuotaFormatter.BurnRatePerDay(
            window != null ? window.Used : (double?)null,
            window != null ? window.Remaining : (double?)null,
            window != null ? window.ResetAt : (double?)null,
            window != null ? window.WindowMinutes : (double?)null);

        RectangleF text = new RectangleF(rect.X + 80, rect.Y + 16, rect.Width - 80 - 92, 22);
        RectangleF sub = new RectangleF(rect.X + 80, rect.Y + 42, rect.Width - 80 - 92, 22);
        using (StringFormat format = new StringFormat())
        {
            format.Trimming = StringTrimming.EllipsisCharacter;
            format.FormatFlags = StringFormatFlags.NoWrap;
            using (Font titleFont = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font mutedFont = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush titleBrush = new SolidBrush(GlassTheme.Slate))
            using (SolidBrush mutedBrush = new SolidBrush(GlassTheme.SlateMuted))
            {
                g.DrawString(caption, titleFont, titleBrush, text, format);
                g.DrawString(reset, mutedFont, mutedBrush, sub, format);
            }
        }

        using (Font percentFont = new Font("Microsoft YaHei UI", 22f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (Font burnFont = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (SolidBrush percentBrush = new SolidBrush(tint))
        using (SolidBrush mutedBrush = new SolidBrush(GlassTheme.SlateMuted))
        using (StringFormat format = new StringFormat())
        {
            format.Alignment = StringAlignment.Far;
            format.LineAlignment = StringAlignment.Near;
            RectangleF percentBox = new RectangleF(rect.Right - 96, rect.Y + 12, 82, 28);
            RectangleF burnBox = new RectangleF(rect.Right - 96, rect.Y + 44, 82, 20);
            g.DrawString(percent, percentFont, percentBrush, percentBox, format);
            g.DrawString(burn, burnFont, mutedBrush, burnBox, format);
        }
    }

    private static void DrawFooter(Graphics g, Rectangle panel, QuotaSnapshot snapshot, string footer)
    {
        bool live = snapshot != null && snapshot.SourceName == "cursor-api";
        Color tint = live ? GlassTheme.Mint : GlassTheme.Warning;
        string badge = QuotaFormatter.StatusBadge(snapshot);
        int y = panel.Bottom - 28;
        using (SolidBrush dot = new SolidBrush(tint))
        {
            g.FillEllipse(dot, panel.X + 18, y + 4, 7, 7);
        }
        using (Font font = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (Font badgeFont = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (SolidBrush muted = new SolidBrush(GlassTheme.SlateMuted))
        using (SolidBrush badgeBrush = new SolidBrush(tint))
        using (StringFormat format = new StringFormat())
        {
            format.Trimming = StringTrimming.EllipsisCharacter;
            format.FormatFlags = StringFormatFlags.NoWrap;
            g.DrawString(footer ?? "", font, muted, new RectangleF(panel.X + 32, y, panel.Width - 120, 18), format);
            SizeF size = g.MeasureString(badge, badgeFont);
            g.DrawString(badge, badgeFont, badgeBrush, panel.Right - size.Width - 18, y);
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

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        float d = radius * 2f;
        if (d > rect.Width) d = rect.Width;
        if (d > rect.Height) d = rect.Height;
        GraphicsPath path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class QuotaPopupForm : Form
{
    private readonly Action _refresh;
    private readonly Action _dismiss;
    private QuotaSnapshot _snapshot;
    private string _footer = "正在读取 Cursor 登录态…";
    private Rectangle _refreshRect;
    private Rectangle _closeRect;
    private bool _refreshHot;
    private bool _closeHot;
    private Bitmap _surface;
    private DateTime _shownAtUtc = DateTime.MinValue;

    public QuotaPopupForm(Action refresh, Action dismiss)
    {
        _refresh = refresh;
        _dismiss = dismiss;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        Size = new Size(GlassTheme.PanelWidth + GlassTheme.Shadow * 2, GlassTheme.PanelHeight + GlassTheme.Shadow * 2);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                _dismiss();
            }
        };
        Deactivate += delegate
        {
            if ((DateTime.UtcNow - _shownAtUtc).TotalMilliseconds < 250)
            {
                return;
            }
            _dismiss();
        };
        MouseMove += OnMouseMove;
        MouseClick += OnMouseClick;
        MouseLeave += delegate
        {
            _refreshHot = false;
            _closeHot = false;
            Present();
        };
    }

    public void ShowSnapshot(QuotaSnapshot snapshot, string footer, Rectangle icon)
    {
        _snapshot = snapshot;
        _footer = footer;
        Screen screen = Screen.FromRectangle(icon);
        Location = PopupAnchor.Above(icon, Size, GlassTheme.Shadow, GlassTheme.Shadow, new Size(GlassTheme.PanelWidth, GlassTheme.PanelHeight), GlassTheme.TipGap, screen.WorkingArea);
        _shownAtUtc = DateTime.UtcNow;
        Present();
        if (!Visible)
        {
            Show();
        }
        Activate();
        NativeMethods.DisableSystemRounding(Handle);
    }

    public void UpdateSnapshot(QuotaSnapshot snapshot, string footer)
    {
        _snapshot = snapshot;
        _footer = footer;
        if (Visible)
        {
            Present();
        }
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
        Present();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Present();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84)
        {
            int packed = m.LParam.ToInt32();
            int x = (short)(packed & 0xFFFF);
            int y = (short)((packed >> 16) & 0xFFFF);
            Point client = PointToClient(new Point(x, y));
            if (!InsidePanel(client))
            {
                m.Result = (IntPtr)(-1);
                return;
            }
        }
        base.WndProc(ref m);
    }

    private bool InsidePanel(Point client)
    {
        Rectangle panel = new Rectangle(GlassTheme.Shadow, GlassTheme.Shadow, GlassTheme.PanelWidth, GlassTheme.PanelHeight);
        if (!panel.Contains(client))
        {
            return false;
        }
        float radius = GlassTheme.PanelRadius;
        float left = panel.Left + radius;
        float right = panel.Right - radius;
        float top = panel.Top + radius;
        float bottom = panel.Bottom - radius;
        if (client.X >= left && client.X <= right)
        {
            return true;
        }
        if (client.Y >= top && client.Y <= bottom)
        {
            return true;
        }
        float cx = client.X < panel.Left + radius ? panel.Left + radius : panel.Right - radius;
        float cy = client.Y < panel.Top + radius ? panel.Top + radius : panel.Bottom - radius;
        float dx = client.X - cx;
        float dy = client.Y - cy;
        return dx * dx + dy * dy <= radius * radius;
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
        if (_surface != null)
        {
            _surface.Dispose();
        }
        bool refreshHot = _refreshHot;
        bool closeHot = _closeHot;
        _surface = DetailPainter.Render(_snapshot, _footer, out _refreshRect, out _closeRect);
        if (refreshHot || closeHot)
        {
            using (Graphics g = Graphics.FromImage(_surface))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (refreshHot)
                {
                    DetailPainter.DrawIconButton(g, _refreshRect, true, true);
                }
                if (closeHot)
                {
                    DetailPainter.DrawIconButton(g, _closeRect, true, false);
                }
            }
        }
        NativeMethods.PresentLayered(Handle, _surface, Left, Top);
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
    private string _line1 = "内置 —";
    private string _line2 = "其他 —";
    private Color _color1 = Color.White;
    private Color _color2 = Color.White;
    private Bitmap _surface;

    public QuotaTrayTipForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(120, 52);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
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
            cp.ExStyle |= NativeMethods.WsExLayered | NativeMethods.WsExToolwindow | NativeMethods.WsExNoActivate | NativeMethods.WsExTransparent;
            return cp;
        }
    }

    public void ShowTip(string line1, string line2, Color color1, Color color2, Rectangle icon)
    {
        _line1 = line1;
        _line2 = line2;
        _color1 = color1;
        _color2 = color2;
        Size measured = Measure();
        Size = measured;
        Screen screen = Screen.FromRectangle(icon);
        Location = PopupAnchor.Above(icon, measured, 0, 0, measured, GlassTheme.TipGap, screen.Bounds);
        Present();
        if (!Visible)
        {
            Show();
        }
        NativeMethods.DisableSystemRounding(Handle);
    }

    public void HideTip()
    {
        if (Visible)
        {
            Hide();
        }
    }

    private Size Measure()
    {
        using (Font font = TipFont())
        using (Bitmap bitmap = new Bitmap(1, 1))
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            SizeF first = g.MeasureString(_line1, font);
            SizeF second = g.MeasureString(_line2, font);
            int width = (int)Math.Ceiling(Math.Max(first.Width, second.Width)) + 22;
            int height = (int)Math.Ceiling(first.Height + second.Height) + 14;
            return new Size(Math.Max(88, width), Math.Max(44, height));
        }
    }

    private void Present()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0)
        {
            return;
        }
        if (_surface != null)
        {
            _surface.Dispose();
        }
        _surface = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(_surface))
        using (GraphicsPath path = new GraphicsPath())
        using (Font font = TipFont())
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            float d = 16;
            path.AddArc(box.X, box.Y, d, d, 180, 90);
            path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
            path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
            path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(235, 36, 48, 62)))
            {
                g.FillPath(fill, path);
            }
            using (SolidBrush first = new SolidBrush(_color1))
            using (SolidBrush second = new SolidBrush(_color2))
            {
                float y = 7f;
                g.DrawString(_line1, font, first, 11f, y);
                SizeF size = g.MeasureString(_line1, font);
                g.DrawString(_line2, font, second, 11f, y + size.Height - 2f);
            }
        }
        NativeMethods.PresentLayered(Handle, _surface, Left, Top);
    }

    private static Font TipFont()
    {
        return new Font("Microsoft YaHei UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _surface != null)
        {
            _surface.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class TrayIconFactory
{
    public static Icon Create(double? primary, double? secondary)
    {
        using (Bitmap bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bitmap))
        using (Font font = new Font("Segoe UI", 8f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (SolidBrush primaryBrush = new SolidBrush(GlassTheme.Emphasis(primary)))
        using (SolidBrush secondaryBrush = new SolidBrush(GlassTheme.Emphasis(secondary)))
        using (StringFormat format = new StringFormat())
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using (SolidBrush plate = new SolidBrush(Color.FromArgb(235, 36, 48, 62)))
            {
                g.FillEllipse(plate, 1, 1, 30, 30);
            }
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            g.DrawString(QuotaFormatter.ShortNumber(primary), font, primaryBrush, new RectangleF(0, 2, 32, 14), format);
            g.DrawString(QuotaFormatter.ShortNumber(secondary), font, secondaryBrush, new RectangleF(0, 15, 32, 14), format);
            IntPtr handle = bitmap.GetHicon();
            Icon created = Icon.FromHandle(handle);
            Icon clone = (Icon)created.Clone();
            created.Dispose();
            NativeMethods.DestroyIcon(handle);
            return clone;
        }
    }
}

internal static class NativeMethods
{
    public const int WsExLayered = 0x00080000;
    public const int WsExToolwindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExTransparent = 0x00000020;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONIDENTIFIER
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public Guid guidItem;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr handle);

    [DllImport("oleacc.dll")]
    public static extern int AccessibleObjectFromPoint(POINT pt, [MarshalAs(UnmanagedType.IUnknown)] out object acc, [MarshalAs(UnmanagedType.Struct)] out object child);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

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

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    public static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetStdHandle(int nStdHandle);

    public static void DisableSystemRounding(IntPtr hwnd)
    {
        try
        {
            int preference = 1;
            DwmSetWindowAttribute(hwnd, 33, ref preference, 4);
        }
        catch
        {
        }
    }

    public static void PresentLayered(IntPtr hwnd, Bitmap bitmap, int x, int y)
    {
        if (hwnd == IntPtr.Zero || bitmap == null)
        {
            return;
        }
        int width = bitmap.Width;
        int height = bitmap.Height;
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        byte[] pixels = new byte[width * height * 4];
        try
        {
            int stride = data.Stride;
            byte[] raw = new byte[Math.Abs(stride) * height];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            for (int row = 0; row < height; row++)
            {
                int source = stride >= 0 ? row * stride : (height - 1 - row) * (-stride);
                Buffer.BlockCopy(raw, source, pixels, row * width * 4, width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr section = IntPtr.Zero;
        IntPtr old = IntPtr.Zero;
        try
        {
            BITMAPINFO info = new BITMAPINFO();
            info.bmiHeader.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            info.bmiHeader.biWidth = width;
            info.bmiHeader.biHeight = -height;
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            info.bmiHeader.biCompression = 0;
            IntPtr bits;
            section = CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0);
            if (section == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return;
            }
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            old = SelectObject(mem, section);
            SIZE size = new SIZE();
            size.Cx = width;
            size.Cy = height;
            POINT dest = new POINT();
            dest.X = x;
            dest.Y = y;
            POINT sourcePoint = new POINT();
            BLENDFUNCTION blend = new BLENDFUNCTION();
            blend.BlendOp = 0;
            blend.SourceConstantAlpha = 255;
            blend.AlphaFormat = 1;
            UpdateLayeredWindow(hwnd, screen, ref dest, ref size, mem, ref sourcePoint, 0, ref blend, 2);
        }
        finally
        {
            if (old != IntPtr.Zero)
            {
                SelectObject(mem, old);
            }
            if (section != IntPtr.Zero)
            {
                DeleteObject(section);
            }
            if (mem != IntPtr.Zero)
            {
                DeleteDC(mem);
            }
            if (screen != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, screen);
            }
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
    private readonly Timer _hoverTimer;
    private Icon _currentIcon;
    private DateTime _hiddenAtUtc = DateTime.MinValue;
    private DateTime _hoverStartUtc = DateTime.MinValue;
    private DateTime _hoverMoveUtc = DateTime.MinValue;
    private bool _hovering;
    private Rectangle _anchoredIcon = Rectangle.Empty;
    private bool _hasAnchoredIcon;

    public QuotaApplicationContext(bool showOnStart)
    {
        _popup = new QuotaPopupForm(RefreshQuota, ClosePopup);
        if (_popup.Handle == IntPtr.Zero)
        {
        }
        _trayTip = new QuotaTrayTipForm();
        if (_trayTip.Handle == IntPtr.Zero)
        {
        }
        _model = new QuotaModel(_popup);
        _menu = BuildMenu();
        _menu.Opening += delegate { HideTip(); };
        _hoverTimer = new Timer();
        _hoverTimer.Interval = 80;
        _hoverTimer.Tick += OnHoverTick;
        _notifyIcon = new NotifyIcon();
        _notifyIcon.Visible = true;
        _notifyIcon.Text = " ";
        _notifyIcon.ContextMenuStrip = _menu;
        _notifyIcon.MouseMove += OnTrayMouseMove;
        _notifyIcon.MouseDown += delegate { HideTip(); };
        _notifyIcon.MouseUp += OnTrayMouseUp;
        _model.Changed += UpdateTray;
        _model.Start();
        UpdateTray();
        if (showOnStart)
        {
            ShowPopup(false);
        }
    }

    public void ShowFromExternal()
    {
        if (_popup.IsHandleCreated && _popup.InvokeRequired)
        {
            _popup.BeginInvoke((Action)delegate { ShowPopup(false); });
            return;
        }
        ShowPopup(false);
    }

    private ContextMenuStrip BuildMenu()
    {
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("显示额度", null, delegate { ShowPopup(false); });
        menu.Items.Add("立即刷新", null, delegate { RefreshQuota(); });
        menu.Items.Add("打开 Cursor 用量页", null, delegate { OpenDashboard(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, delegate { Quit(); });
        return menu;
    }

    private void OnTrayMouseMove(object sender, MouseEventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        if (!_hovering)
        {
            _hovering = true;
            _hoverStartUtc = now;
            _hoverTimer.Start();
        }
        _hoverMoveUtc = now;
    }

    private void OnHoverTick(object sender, EventArgs e)
    {
        if (_popup.Visible || !CursorStillOverIcon())
        {
            HideTip();
            return;
        }
        if ((DateTime.UtcNow - _hoverStartUtc).TotalMilliseconds < 280)
        {
            return;
        }
        ShowTip(CurrentIcon());
    }

    private bool CursorStillOverIcon()
    {
        Point mouse = Control.MousePosition;
        Rectangle icon = CurrentIcon();
        Rectangle hit = icon;
        hit.Inflate(10, 10);
        if (hit.Contains(mouse))
        {
            return true;
        }
        if (_trayTip.Visible)
        {
            Rectangle tip = _trayTip.Bounds;
            tip.Inflate(6, 6);
            if (tip.Contains(mouse))
            {
                return true;
            }
        }
        return (DateTime.UtcNow - _hoverMoveUtc).TotalMilliseconds <= 160;
    }

    private Rectangle CurrentIcon()
    {
        Point mouse = Control.MousePosition;
        if (_hasAnchoredIcon && _anchoredIcon.Contains(mouse))
        {
            return _anchoredIcon;
        }
        Rectangle slot = TraySlot();
        Rectangle element;
        bool hasElement = IconLocator.TryVisualElement(mouse, _trayTip.Handle, _popup.Handle, out element);
        bool anchored;
        Rectangle resolved = IconLocator.Resolve(mouse, slot, hasElement, element, out anchored);
        if (anchored)
        {
            _anchoredIcon = resolved;
            _hasAnchoredIcon = true;
        }
        else
        {
            _hasAnchoredIcon = false;
        }
        return resolved;
    }

    private Rectangle TraySlot()
    {
        Rectangle bounds;
        if (TryGetTrayIconRect(out bounds))
        {
            return bounds;
        }
        return Rectangle.Empty;
    }

    private bool TryGetTrayIconRect(out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        try
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo idField = typeof(NotifyIcon).GetField("id", flags);
            if (idField == null)
            {
                idField = typeof(NotifyIcon).GetField("_id", flags);
            }
            FieldInfo windowField = typeof(NotifyIcon).GetField("window", flags);
            if (windowField == null)
            {
                windowField = typeof(NotifyIcon).GetField("_window", flags);
            }
            if (idField == null || windowField == null)
            {
                return false;
            }
            NativeWindow window = windowField.GetValue(_notifyIcon) as NativeWindow;
            if (window == null || window.Handle == IntPtr.Zero)
            {
                return false;
            }
            NativeMethods.NOTIFYICONIDENTIFIER identifier = new NativeMethods.NOTIFYICONIDENTIFIER();
            identifier.cbSize = Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONIDENTIFIER));
            identifier.hWnd = window.Handle;
            object idValue = idField.GetValue(_notifyIcon);
            identifier.uID = idValue == null ? 0 : Convert.ToInt32(idValue);
            NativeMethods.RECT rect;
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

    private void ShowTip(Rectangle icon)
    {
        QuotaSnapshot snapshot = _model.Display;
        double? primary = snapshot != null && snapshot.Primary != null ? snapshot.Primary.Remaining : (double?)null;
        double? secondary = snapshot != null && snapshot.Secondary != null ? snapshot.Secondary.Remaining : (double?)null;
        _trayTip.ShowTip(
            "内置 " + QuotaFormatter.Percent(primary),
            "其他 " + QuotaFormatter.Percent(secondary),
            GlassTheme.Emphasis(primary),
            GlassTheme.Emphasis(secondary),
            icon);
    }

    private void HideTip()
    {
        _hovering = false;
        _hasAnchoredIcon = false;
        _hoverTimer.Stop();
        _trayTip.HideTip();
    }

    private void OnTrayMouseUp(object sender, MouseEventArgs e)
    {
        HideTip();
        if (e.Button == MouseButtons.Right)
        {
            QueueTrayMenuFallback();
            return;
        }
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
            ShowPopup(true);
        }
    }

    private void QueueTrayMenuFallback()
    {
        if (_menu.IsDisposed)
        {
            return;
        }

        Point screenPoint = Control.MousePosition;
        try
        {
            _popup.BeginInvoke((Action)delegate
            {
                if (_menu.IsDisposed || _menu.Visible)
                {
                    return;
                }
                _menu.Show(screenPoint);
            });
        }
        catch
        {
        }
    }

    private void ShowPopup(bool fromPointer)
    {
        HideTip();
        Rectangle icon = fromPointer ? CurrentIcon() : TraySlot();
        if (icon.Width <= 0)
        {
            Point mouse = Control.MousePosition;
            icon = new Rectangle(mouse.X - 16, mouse.Y - 16, 32, 32);
        }
        _popup.ShowSnapshot(_model.Display, _model.FooterText, icon);
    }

    private void ClosePopup()
    {
        if (_popup.Visible)
        {
            _popup.Hide();
            _hiddenAtUtc = DateTime.UtcNow;
        }
    }

    private void RefreshQuota()
    {
        _model.Refresh();
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
        QuotaSnapshot snapshot = _model.Display;
        double? primary = snapshot != null && snapshot.Primary != null ? snapshot.Primary.Remaining : (double?)null;
        double? secondary = snapshot != null && snapshot.Secondary != null ? snapshot.Secondary.Remaining : (double?)null;
        if (_trayTip.Visible)
        {
            ShowTip(CurrentIcon());
        }
        if (_popup.Visible)
        {
            _popup.UpdateSnapshot(snapshot, _model.FooterText);
        }
        if (!_model.IconChanged && _currentIcon != null)
        {
            return;
        }
        Icon icon = TrayIconFactory.Create(primary, secondary);
        Icon old = _currentIcon;
        _notifyIcon.Icon = icon;
        _currentIcon = icon;
        if (old != null)
        {
            old.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model.Stop();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            if (_currentIcon != null)
            {
                _currentIcon.Dispose();
            }
            _menu.Dispose();
            _hoverTimer.Dispose();
            _trayTip.Dispose();
            _popup.Dispose();
        }
        base.Dispose(disposing);
    }
}
