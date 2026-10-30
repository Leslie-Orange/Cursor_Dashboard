using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

internal static class QuotaPresentation
{
    public static string CardBadge(QuotaWindow window, bool secondary)
    {
        if (secondary) return "额外额度池";
        if (window != null && window.WindowMinutes.HasValue)
            return window.WindowMinutes.Value <= 10080 ? "周限额" : "月限额";
        return window != null && !string.IsNullOrEmpty(window.Title) ? window.Title : "套餐限额";
    }

    public static string Amount(double? cents, string currency)
    {
        if (!cents.HasValue || double.IsNaN(cents.Value) || double.IsInfinity(cents.Value)) return "—";
        string prefix = string.IsNullOrEmpty(currency) || currency.Equals("USD", StringComparison.OrdinalIgnoreCase) ? "$" : currency.ToUpperInvariant() + " ";
        return prefix + (cents.Value / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static string Value(QuotaWindow window, bool secondary)
    {
        if (!secondary) return QuotaFormatter.Percent(window != null ? window.Remaining : (double?)null);
        return Amount(window != null ? window.RemainingAmountCents : null, window != null ? window.CurrencyCode : null)
            + " / " + Amount(window != null ? window.LimitAmountCents : null, window != null ? window.CurrencyCode : null);
    }

    public static string Detail(QuotaWindow window, bool secondary)
    {
        if (window == null) return "—";
        if (secondary && window.RemainingAmountCents.HasValue && window.LimitAmountCents.HasValue)
            return "已用 " + Amount(Math.Max(0, window.LimitAmountCents.Value - window.RemainingAmountCents.Value), window.CurrencyCode);
        if (!secondary && window.Used.HasValue) return "已用 " + QuotaFormatter.Percent(window.Used);
        return string.IsNullOrEmpty(window.Detail) ? "—" : window.Detail;
    }

    public static string TrayLine(QuotaWindow window, bool secondary)
    {
        if (!secondary) return QuotaFormatter.Line(window, "内置");
        string value = window != null && window.Remaining.HasValue ? QuotaFormatter.Percent(window.Remaining)
            : window != null && window.RemainingAmountCents.HasValue ? Amount(window.RemainingAmountCents, window.CurrencyCode) : "—";
        return "额外 " + value;
    }
}

internal sealed class QuotaColors
{
    public static bool? PreviewDark;
    public readonly bool Dark;
    public Color Foreground, Muted, Tertiary, PanelTop, PanelBottom, Card, Border, Track, Control, Success;

    public static bool IsDark
    {
        get
        {
            if (PreviewDark.HasValue) return PreviewDark.Value;
            if (SystemInformation.HighContrast) return SystemColors.Window.GetBrightness() < 0.5f;
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value != null && Convert.ToInt32(value) == 0;
                }
            }
            catch { return false; }
        }
    }

    public QuotaColors(bool dark)
    {
        Dark = dark;
        Foreground = dark ? Color.FromArgb(242, 241, 236) : Color.FromArgb(32, 34, 33);
        Muted = dark ? Color.FromArgb(170, 173, 168) : Color.FromArgb(116, 119, 114);
        Tertiary = dark ? Color.FromArgb(139, 143, 138) : Color.FromArgb(146, 149, 144);
        PanelTop = dark ? Color.FromArgb(248, 42, 44, 43) : Color.FromArgb(250, 252, 252, 250);
        PanelBottom = dark ? Color.FromArgb(248, 32, 34, 33) : Color.FromArgb(248, 245, 246, 243);
        Card = dark ? Color.FromArgb(64, 90, 94, 88) : Color.FromArgb(70, 231, 233, 228);
        Border = dark ? Color.FromArgb(28, 239, 241, 235) : Color.FromArgb(24, 45, 49, 43);
        Track = dark ? Color.FromArgb(68, 156, 161, 151) : Color.FromArgb(75, 199, 202, 194);
        Control = dark ? Color.FromArgb(28, 230, 233, 224) : Color.FromArgb(45, 255, 255, 255);
        Success = dark ? Color.FromArgb(77, 207, 151) : Color.FromArgb(22, 148, 108);
        if (SystemInformation.HighContrast)
        {
            Foreground = Muted = Tertiary = SystemColors.WindowText;
            PanelTop = PanelBottom = Card = SystemColors.Window;
            Border = Track = SystemColors.WindowText;
            Control = SystemColors.Control;
        }
    }

    public Color Emphasis(double? remaining)
    {
        if (!remaining.HasValue) return Muted;
        if (remaining.Value <= 10) return GlassTheme.Danger;
        if (remaining.Value <= 30) return GlassTheme.Warning;
        return Foreground;
    }
}

internal static class DetailPainter
{
    public static Bitmap Render(QuotaSnapshot snapshot, string footer, out Rectangle refresh, out Rectangle close)
    {
        return Render(snapshot, footer, DetailLayout.Create(new Rectangle(0, 0, 1920, 1080), 96), false, false, out refresh, out close);
    }

    public static Bitmap Render(QuotaSnapshot snapshot, string footer, DetailLayout layout, bool refreshHot, bool closeHot, out Rectangle refresh, out Rectangle close)
    {
        return Render(snapshot, footer, layout, refreshHot, closeHot, QuotaColors.IsDark, out refresh, out close);
    }

    public static Bitmap Render(QuotaSnapshot snapshot, string footer, DetailLayout layout, bool refreshHot, bool closeHot, bool dark, out Rectangle refresh, out Rectangle close)
    {
        QuotaColors colors = new QuotaColors(dark);
        Bitmap bitmap = new Bitmap(Math.Max(1, layout.WindowSize.Width), Math.Max(1, layout.WindowSize.Height), PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            g.ScaleTransform(layout.Scale, layout.Scale);
            Rectangle panel = new Rectangle(GlassTheme.Shadow, GlassTheme.Shadow, GlassTheme.PanelWidth, GlassTheme.PanelHeight);
            DrawPanel(g, panel, colors);
            DrawHeader(g, panel, snapshot, colors, refreshHot, closeHot, out refresh, out close);
            Rectangle first = new Rectangle(panel.X + GlassTheme.PanelInset, panel.Y + 65, panel.Width - GlassTheme.PanelInset * 2, 81);
            Rectangle second = new Rectangle(first.X, first.Bottom + 9, first.Width, 81);
            DrawCard(g, first, snapshot != null ? snapshot.Primary : null, false, colors);
            DrawCard(g, second, snapshot != null ? snapshot.Secondary : null, true, colors);
            DrawFooter(g, panel, snapshot, footer, colors);
        }
        refresh = layout.ToPixels(refresh);
        close = layout.ToPixels(close);
        return bitmap;
    }

    private static void DrawPanel(Graphics g, Rectangle panel, QuotaColors colors)
    {
        for (int i = 10; i >= 1; i--)
        {
            Rectangle shadow = Rectangle.Inflate(panel, i, i);
            shadow.Offset(0, Math.Max(1, i / 2));
            using (GraphicsPath path = Rounded(shadow, GlassTheme.PanelRadius + i))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(3 + i, 10, 12, 10)))
                g.FillPath(brush, path);
        }
        using (GraphicsPath path = Rounded(panel, GlassTheme.PanelRadius))
        using (LinearGradientBrush fill = new LinearGradientBrush(panel, colors.PanelTop, colors.PanelBottom, LinearGradientMode.Vertical))
        using (Pen border = new Pen(colors.Border, 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }
    }

    private static void DrawHeader(Graphics g, Rectangle panel, QuotaSnapshot snapshot, QuotaColors colors, bool refreshHot, bool closeHot, out Rectangle refresh, out Rectangle close)
    {
        using (Bitmap mark = AppMarkPainter.Render(56, false, null, null))
            g.DrawImage(mark, new Rectangle(panel.X + 16, panel.Y + 17, 28, 28));
        close = new Rectangle(panel.Right - GlassTheme.ButtonTrailing - GlassTheme.ButtonSize, panel.Y + GlassTheme.HeaderTop, GlassTheme.ButtonSize, GlassTheme.ButtonSize);
        refresh = new Rectangle(close.X - GlassTheme.ButtonGap - GlassTheme.ButtonSize, close.Y, GlassTheme.ButtonSize, GlassTheme.ButtonSize);
        string title = QuotaFormatter.PlanTitle(snapshot != null ? snapshot.PlanType : null);
        QuotaWindow primary = snapshot != null ? snapshot.Primary : null;
        string subtitle = QuotaFormatter.ResetAt(primary != null ? primary.ResetAt : null);
        if (string.IsNullOrEmpty(subtitle)) subtitle = primary != null && !string.IsNullOrEmpty(primary.Schedule) ? primary.Schedule : "重置时间未知";
        Text(g, title, new RectangleF(panel.X + 53, panel.Y + 15, refresh.Left - panel.X - 60, 23), 14, true, colors.Foreground, false);
        Text(g, subtitle, new RectangleF(panel.X + 53, panel.Y + 37, refresh.Left - panel.X - 60, 17), 10, false, colors.Muted, false);
        DrawIconButton(g, refresh, refreshHot, true, colors);
        DrawIconButton(g, close, closeHot, false, colors);
    }

    private static void DrawIconButton(Graphics g, Rectangle rect, bool hot, bool refresh, QuotaColors colors)
    {
        using (SolidBrush fill = new SolidBrush(hot ? (colors.Dark ? Color.FromArgb(90, 170, 174, 164) : Color.FromArgb(235, 255, 255, 255)) : colors.Control))
        using (Pen border = new Pen(colors.Border, 1f))
        using (Pen mark = new Pen(colors.Muted, 1.5f))
        {
            g.FillEllipse(fill, rect);
            g.DrawEllipse(border, rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1f, rect.Height - 1f);
            mark.StartCap = mark.EndCap = LineCap.Round;
            mark.LineJoin = LineJoin.Round;
            if (refresh)
            {
                // Finish the clockwise arc at the arrow tip so every stroke joins.
                g.DrawArc(mark, rect.X + 7, rect.Y + 7, 14, 14, 35, 280);
                g.DrawLines(mark, new PointF[] {
                    new PointF(rect.X + 14.95f, rect.Y + 9.05f),
                    new PointF(rect.X + 18.95f, rect.Y + 9.05f),
                    new PointF(rect.X + 18.95f, rect.Y + 5.05f)
                });
            }
            else
            {
                g.DrawLine(mark, rect.X + 10, rect.Y + 10, rect.Right - 10, rect.Bottom - 10);
                g.DrawLine(mark, rect.Right - 10, rect.Y + 10, rect.X + 10, rect.Bottom - 10);
            }
        }
    }

    private static void DrawCard(Graphics g, Rectangle rect, QuotaWindow window, bool secondary, QuotaColors colors)
    {
        using (GraphicsPath path = Rounded(rect, GlassTheme.CardRadius))
        using (SolidBrush fill = new SolidBrush(colors.Card))
        using (Pen border = new Pen(Color.FromArgb(colors.Dark ? 12 : 8, colors.Foreground), 0.5f))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }
        string badge = QuotaPresentation.CardBadge(window, secondary);
        using (Font font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            float badgeWidth = Math.Min(102, g.MeasureString(badge, font).Width + 14);
            Rectangle badgeRect = new Rectangle(rect.X + 13, rect.Y + 16, (int)Math.Ceiling(badgeWidth), 20);
            using (GraphicsPath path = Rounded(badgeRect, 10))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(colors.Dark ? 22 : 11, colors.Foreground)))
                g.FillPath(fill, path);
            Text(g, badge, badgeRect, 9, true, colors.Muted, false, true);
        }
        Color tint = colors.Emphasis(window != null ? window.Remaining : null);
        RectangleF valueRect = new RectangleF(rect.X + 110, rect.Y + 9, rect.Width - 123, 35);
        FitText(g, QuotaPresentation.Value(window, secondary), valueRect, secondary ? 23 : 27, secondary ? 14 : 21, tint);
        Rectangle track = new Rectangle(rect.X + 13, rect.Y + 48, rect.Width - 26, 7);
        using (GraphicsPath path = Rounded(track, 4))
        using (SolidBrush fill = new SolidBrush(colors.Track))
            g.FillPath(fill, path);
        double progress = window != null && window.Remaining.HasValue ? Math.Max(0, Math.Min(100, window.Remaining.Value)) / 100.0 : 0;
        if (progress > 0)
        {
            float width = (float)(track.Width * progress);
            GraphicsState saved = g.Save();
            g.SetClip(new RectangleF(track.X, track.Y, width, track.Height));
            using (GraphicsPath path = Rounded(new Rectangle(track.X, track.Y, Math.Max(7, (int)Math.Ceiling(width)), track.Height), 4))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(200, tint)))
                g.FillPath(fill, path);
            g.Restore(saved);
        }
        string reset = secondary && (window == null || (!window.ResetAt.HasValue && !window.WindowMinutes.HasValue))
            ? "余额实时更新" : QuotaFormatter.Reset(window != null ? window.ResetAt : null);
        if (!secondary && window != null && !window.ResetAt.HasValue && !string.IsNullOrEmpty(window.Schedule)) reset = window.Schedule;
        Text(g, reset, new RectangleF(rect.X + 13, rect.Y + 62, rect.Width - 122, 16), 10, false, colors.Muted, false);
        Text(g, QuotaPresentation.Detail(window, secondary), new RectangleF(rect.Right - 126, rect.Y + 62, 113, 16), 10, false, colors.Tertiary, true);
    }

    private static void DrawFooter(Graphics g, Rectangle panel, QuotaSnapshot snapshot, string footer, QuotaColors colors)
    {
        bool partial = snapshot != null && snapshot.SourceName == "partial";
        bool failed = snapshot != null && snapshot.SourceName != "cursor-api";
        bool live = snapshot != null && !failed;
        bool refreshing = footer != null && footer.StartsWith("正在刷新", StringComparison.Ordinal);
        Color statusColor = live && !refreshing ? colors.Success : colors.Muted;
        string badge = snapshot == null ? "等待" : partial ? "部分" : live ? "实时" : "快照";
        int separator = failed && footer != null ? footer.LastIndexOf(" · ", StringComparison.Ordinal) : -1;
        int y = panel.Bottom - (separator >= 0 ? 40 : 26);
        Text(g, separator >= 0 ? footer.Substring(0, separator) : footer ?? "", new RectangleF(panel.X + 16, y, panel.Width - 86, 17), 10, false, statusColor, false);
        if (separator >= 0)
            Text(g, footer.Substring(separator + 3), new RectangleF(panel.X + 16, y + 16, panel.Width - 32, 17), 10, false, GlassTheme.Warning, false);
        using (SolidBrush dot = new SolidBrush(refreshing ? colors.Muted : live ? colors.Success : GlassTheme.Warning))
            g.FillEllipse(dot, panel.Right - 53, y + 5, 5, 5);
        Text(g, refreshing ? "刷新" : badge, new RectangleF(panel.Right - 44, y, 29, 17), 10, true, statusColor, true);
    }

    private static void FitText(Graphics g, string value, RectangleF rect, float preferred, float minimum, Color color)
    {
        float size = preferred;
        while (size > minimum)
        {
            using (Font font = new Font("Microsoft YaHei UI", size, FontStyle.Bold, GraphicsUnit.Pixel))
                if (g.MeasureString(value, font).Width <= rect.Width) break;
            size -= 0.5f;
        }
        Text(g, value, rect, size, true, color, true);
    }

    private static void Text(Graphics g, string value, RectangleF rect, float size, bool bold, Color color, bool right, bool center = false)
    {
        using (Font font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
        using (SolidBrush brush = new SolidBrush(color))
        using (StringFormat format = new StringFormat())
        {
            format.Alignment = center ? StringAlignment.Center : right ? StringAlignment.Far : StringAlignment.Near;
            format.LineAlignment = StringAlignment.Center;
            format.Trimming = StringTrimming.EllipsisCharacter;
            format.FormatFlags = StringFormatFlags.NoWrap;
            g.DrawString(value ?? "", font, brush, rect, format);
        }
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        float d = Math.Max(1, Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height)));
        GraphicsPath path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
