using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;

internal static class AppMarkPainter
{
    public static readonly Color Blue = Color.FromArgb(69, 125, 250);
    public static readonly Color Purple = Color.FromArgb(97, 74, 230);

    public static Bitmap Render(int size, bool tray, double? primary, double? secondary)
    {
        Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            float pad = size <= 20 ? 0.5f : size * 0.04f;
            RectangleF box = new RectangleF(pad, pad, size - pad * 2f, size - pad * 2f);
            float radius = box.Width * 0.223f;
            using (GraphicsPath path = Rounded(box, radius))
            {
                using (LinearGradientBrush fill = new LinearGradientBrush(
                    Rectangle.Round(RectangleF.Inflate(box, 1, 1)),
                    Blue,
                    Purple,
                    135f))
                {
                    g.FillPath(fill, path);
                }

                GraphicsState state = g.Save();
                try
                {
                    g.SetClip(path, CombineMode.Intersect);
                    RectangleF shine = new RectangleF(
                        box.X + box.Width * 0.08f,
                        box.Y - box.Height * 0.08f,
                        box.Width * 0.84f,
                        box.Height * 0.58f);
                    using (GraphicsPath oval = new GraphicsPath())
                    {
                        oval.AddEllipse(shine);
                        using (PathGradientBrush glow = new PathGradientBrush(oval))
                        {
                            glow.CenterColor = Color.FromArgb(48, Color.White);
                            glow.SurroundColors = new Color[] { Color.FromArgb(0, Color.White) };
                            g.FillPath(glow, oval);
                        }
                    }
                }
                finally
                {
                    g.Restore(state);
                }

                using (Pen edge = new Pen(Color.FromArgb(36, Color.White), Math.Max(1f, size / 64f)))
                {
                    g.DrawPath(edge, path);
                }
            }

            if (tray && size >= 24)
            {
                DrawTrayNumbers(g, box, primary, secondary);
            }
            else
            {
                DrawGauge(g, box, Color.White);
            }
        }
        return bitmap;
    }

    private static void DrawTrayNumbers(Graphics g, RectangleF box, double? primary, double? secondary)
    {
        Color first = NumberColor(primary);
        Color second = NumberColor(secondary);
        float fontSize = Math.Max(7.5f, box.Height * 0.30f);
        using (Font font = new Font("Segoe UI Semibold", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
        using (SolidBrush brush1 = new SolidBrush(first))
        using (SolidBrush brush2 = new SolidBrush(second))
        using (StringFormat format = new StringFormat())
        {
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            format.FormatFlags = StringFormatFlags.NoWrap;
            float inset = box.Width * 0.06f;
            RectangleF top = new RectangleF(box.X + inset, box.Y + box.Height * 0.10f, box.Width - inset * 2f, box.Height * 0.40f);
            RectangleF bottom = new RectangleF(box.X + inset, box.Y + box.Height * 0.48f, box.Width - inset * 2f, box.Height * 0.40f);
            g.DrawString(Short(primary), font, brush1, top, format);
            g.DrawString(Short(secondary), font, brush2, bottom, format);
        }
    }

    private static Color NumberColor(double? remaining)
    {
        if (!remaining.HasValue)
        {
            return Color.FromArgb(220, 255, 255, 255);
        }
        if (remaining.Value <= 10)
        {
            return Color.FromArgb(255, 214, 92, 92);
        }
        if (remaining.Value <= 30)
        {
            return Color.FromArgb(255, 255, 196, 86);
        }
        return Color.White;
    }

    private static string Short(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value))
        {
            return "—";
        }
        return Math.Round(value.Value).ToString(CultureInfo.InvariantCulture);
    }

    public static void DrawGauge(Graphics g, RectangleF box, Color color)
    {
        float unit = box.Width;
        RectangleF arc = new RectangleF(
            box.X + unit * 0.18f,
            box.Y + unit * 0.20f,
            unit * 0.64f,
            unit * 0.64f);
        float thickness = Math.Max(1.6f, unit * 0.085f);
        using (Pen ring = new Pen(color, thickness))
        {
            ring.StartCap = LineCap.Round;
            ring.EndCap = LineCap.Round;
            g.DrawArc(ring, arc, 136f, 268f);
        }

        PointF center = new PointF(arc.X + arc.Width / 2f, arc.Y + arc.Height / 2f + unit * 0.02f);
        DrawTicks(g, center, arc.Width / 2f, thickness, color, unit);

        float needle = arc.Width * 0.42f;
        double angle = -38 * Math.PI / 180.0;
        PointF tip = new PointF(
            center.X + (float)(Math.Cos(angle) * needle),
            center.Y + (float)(Math.Sin(angle) * needle));
        using (GraphicsPath shaft = new GraphicsPath())
        {
            float hub = Math.Max(2.2f, unit * 0.07f);
            PointF left = Offset(center, tip, hub, -1);
            PointF right = Offset(center, tip, hub, 1);
            shaft.AddPolygon(new PointF[] { left, right, tip });
            using (SolidBrush fill = new SolidBrush(color))
            {
                g.FillPath(fill, shaft);
            }
        }

        float mint = Math.Max(1.4f, unit * 0.035f);
        using (Pen accent = new Pen(Color.FromArgb(90, 214, 148), mint))
        {
            accent.StartCap = LineCap.Round;
            accent.EndCap = LineCap.Round;
            PointF inner = Lerp(center, tip, 0.62f);
            g.DrawLine(accent, inner, Lerp(center, tip, 0.86f));
        }

        float cap = Math.Max(2.8f, unit * 0.09f);
        using (SolidBrush hubBrush = new SolidBrush(color))
        {
            g.FillEllipse(hubBrush, center.X - cap / 2f, center.Y - cap / 2f, cap, cap);
        }
    }

    private static void DrawTicks(Graphics g, PointF center, float radius, float thickness, Color color, float unit)
    {
        float[] degrees = new float[] { 150f, 110f, 70f, 30f, 0f, -30f };
        float outer = radius - thickness * 0.15f;
        float inner = outer - Math.Max(2.2f, unit * 0.07f);
        using (Pen tick = new Pen(color, Math.Max(1.3f, unit * 0.038f)))
        {
            tick.StartCap = LineCap.Round;
            tick.EndCap = LineCap.Round;
            for (int i = 0; i < degrees.Length; i++)
            {
                double rad = degrees[i] * Math.PI / 180.0;
                float cx = (float)Math.Cos(rad);
                float sy = -(float)Math.Sin(rad);
                g.DrawLine(
                    tick,
                    center.X + cx * inner,
                    center.Y + sy * inner,
                    center.X + cx * outer,
                    center.Y + sy * outer);
            }
        }
    }

    private static PointF Offset(PointF from, PointF to, float width, int side)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        float length = (float)Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.01f)
        {
            return from;
        }
        float nx = -dy / length * width * 0.45f * side;
        float ny = dx / length * width * 0.45f * side;
        return new PointF(from.X + nx, from.Y + ny);
    }

    private static PointF Lerp(PointF a, PointF b, float t)
    {
        return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    private static GraphicsPath Rounded(RectangleF rect, float radius)
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

internal static class IconFile
{
    public static void Write(string path, int[] sizes, List<byte[]> pngs)
    {
        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)pngs.Count);
            int offset = 6 + 16 * pngs.Count;
            for (int i = 0; i < pngs.Count; i++)
            {
                int size = sizes[i];
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(pngs[i].Length);
                writer.Write(offset);
                offset += pngs[i].Length;
            }
            for (int i = 0; i < pngs.Count; i++)
            {
                writer.Write(pngs[i]);
            }
        }
    }
}
