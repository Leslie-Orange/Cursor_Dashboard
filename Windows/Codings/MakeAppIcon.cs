using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

internal static class MakeAppIconProgram
{
    private static int Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "CursorQuotaPet.ico";
        int[] sizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        List<byte[]> images = new List<byte[]>();
        for (int i = 0; i < sizes.Length; i++)
        {
            using (Bitmap bitmap = AppMarkPainter.Render(sizes[i], false, null, null))
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                images.Add(stream.ToArray());
            }
        }
        IconFile.Write(path, sizes, images);
        Console.WriteLine(path);
        return 0;
    }
}
