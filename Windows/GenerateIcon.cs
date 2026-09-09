using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

internal static class GenerateIcon
{
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: GenerateIcon <app.ico>");
            return 1;
        }

        int[] sizes = new int[] { 16, 32, 48, 256 };
        Bitmap[] bitmaps = new Bitmap[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            bitmaps[i] = Draw(sizes[i]);
        }

        SaveIco(args[0], bitmaps);
        for (int i = 0; i < bitmaps.Length; i++)
        {
            bitmaps[i].Dispose();
        }
        return 0;
    }

    private static Bitmap Draw(int size)
    {
        Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            int pad = Math.Max(1, size / 16);
            Rectangle rect = new Rectangle(pad, pad, size - pad * 2, size - pad * 2);
            using (LinearGradientBrush brush = new LinearGradientBrush(
                rect,
                Color.FromArgb(97, 158, 255),
                Color.FromArgb(125, 92, 245),
                LinearGradientMode.ForwardDiagonal))
            {
                g.FillEllipse(brush, rect);
            }

            float width = Math.Max(1.6f, size / 12f);
            using (Pen pen = new Pen(Color.White, width))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int inset = Math.Max(2, size / 5);
                g.DrawArc(pen, rect.X + inset, rect.Y + inset, rect.Width - inset * 2, rect.Height - inset * 2, 140, 260);
            }

            int dot = Math.Max(2, size / 12);
            g.FillEllipse(Brushes.White, size / 2 - dot / 2, size / 2 - dot / 2, dot, dot);
        }
        return bitmap;
    }

    private static void SaveIco(string path, Bitmap[] images)
    {
        MemoryStream[] payloads = new MemoryStream[images.Length];
        for (int i = 0; i < images.Length; i++)
        {
            payloads[i] = EncodeImage(images[i]);
        }

        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)images.Length);

            int offset = 6 + 16 * images.Length;
            for (int i = 0; i < images.Length; i++)
            {
                int width = images[i].Width;
                int height = images[i].Height;
                writer.Write((byte)(width >= 256 ? 0 : width));
                writer.Write((byte)(height >= 256 ? 0 : height));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((short)1);
                writer.Write((short)32);
                writer.Write((int)payloads[i].Length);
                writer.Write(offset);
                offset += (int)payloads[i].Length;
            }

            for (int i = 0; i < payloads.Length; i++)
            {
                writer.Write(payloads[i].ToArray());
                payloads[i].Dispose();
            }
        }
    }

    private static MemoryStream EncodeImage(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        MemoryStream memory = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(memory);

        int andRow = ((width + 31) / 32) * 4;
        int xorSize = width * height * 4;
        int andSize = andRow * height;

        writer.Write(40);
        writer.Write(width);
        writer.Write(height * 2);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(0);
        writer.Write(xorSize + andSize);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            byte[] row = new byte[width * 4];
            for (int y = height - 1; y >= 0; y--)
            {
                IntPtr source = new IntPtr(data.Scan0.ToInt64() + y * data.Stride);
                Marshal.Copy(source, row, 0, row.Length);
                writer.Write(row);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        writer.Write(new byte[andSize]);
        writer.Flush();
        memory.Position = 0;
        return memory;
    }
}
