using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Keep this geometry in sync with Mac/Codings/CursorBrand.swift.
internal static class AppMarkPainter
{
    public static readonly Color Graphite = Color.FromArgb(32, 34, 33);
    public static readonly Color WarmWhite = Color.FromArgb(242, 241, 236);
    public static readonly Color LightQuota = Color.FromArgb(174, 177, 172);
    public static readonly Color DarkQuota = Color.FromArgb(119, 123, 119);

    public static Bitmap Render(int size, bool tray, double? primary, double? secondary)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException("size");
        Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            // Windows has no template-image tinting. The graphite tile keeps the
            // monochrome mark readable on both light and dark taskbars.
            float inset = Math.Max(size * 0.045f, size <= 32 ? 0.5f : 0f);
            RectangleF tile = new RectangleF(inset, inset, size - inset * 2f, size - inset * 2f);
            using (GraphicsPath path = Rounded(tile, tile.Width * 0.22f))
            using (SolidBrush fill = new SolidBrush(Graphite))
            {
                g.FillPath(fill, path);
            }
            DrawMark(g, size, tray, primary, secondary);
        }
        return bitmap;
    }

    private static void DrawMark(Graphics g, float side, bool tray, double? primary, double? secondary)
    {
        // AppKit uses a bottom-left origin; GDI+ uses a top-left origin.
        PointF top = Point(side, 0.5f, 0.875f);
        PointF upperLeft = Point(side, 0.205f, 0.715f);
        PointF center = Point(side, 0.5f, 0.555f);
        PointF upperRight = Point(side, 0.795f, 0.715f);
        PointF lowerLeft = Point(side, 0.205f, 0.405f);
        PointF bottom = Point(side, 0.5f, 0.245f);
        PointF lowerRight = Point(side, 0.795f, 0.405f);

        using (SolidBrush fill = new SolidBrush(WarmWhite))
        {
            g.FillPolygon(fill, new PointF[] { top, upperRight, lowerRight, bottom, lowerLeft, upperLeft });
        }
        using (Pen seam = new Pen(Graphite, Math.Max(side * 0.014f, side <= 32 ? 0.8f : 0f)))
        {
            seam.StartCap = LineCap.Flat;
            seam.EndCap = LineCap.Flat;
            seam.LineJoin = LineJoin.Miter;
            g.DrawLines(seam, new PointF[] { upperLeft, center, upperRight });
            g.DrawLine(seam, center, bottom);
        }

        float barHeight = Math.Max(side * 0.04f, side <= 32 ? 1f : 0f);
        DrawQuotaBar(g, side, 0.20f, 0.43f, barHeight, LightQuota, tray, primary);
        DrawQuotaBar(g, side, 0.10f, 0.64f, barHeight, DarkQuota, tray, secondary);
    }

    private static PointF Point(float side, float x, float y)
    {
        return new PointF(side * x, side * (1f - y));
    }

    private static void DrawQuotaBar(Graphics g, float side, float y, float width, float height,
        Color appColor, bool tray, double? remaining)
    {
        RectangleF rect = new RectangleF(side * (0.5f - width / 2f), side * (1f - y) - height / 2f,
            side * width, height);
        using (GraphicsPath path = Rounded(rect, height / 2f))
        {
            using (SolidBrush track = new SolidBrush(tray ? DarkQuota : appColor))
            {
                g.FillPath(track, path);
            }
            if (!tray || !remaining.HasValue || double.IsNaN(remaining.Value) || double.IsInfinity(remaining.Value))
            {
                return;
            }
            float fraction = (float)Math.Max(0, Math.Min(1, remaining.Value / 100.0));
            if (fraction <= 0) return;
            GraphicsState state = g.Save();
            try
            {
                g.SetClip(path, CombineMode.Intersect);
                using (SolidBrush fill = new SolidBrush(WarmWhite))
                {
                    g.FillRectangle(fill, rect.X, rect.Y, rect.Width * fraction, rect.Height);
                }
            }
            finally
            {
                g.Restore(state);
            }
        }
    }

    private static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        float d = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
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
