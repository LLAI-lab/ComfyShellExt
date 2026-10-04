using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ComfyShellExt.Menu
{
    /// <summary>
    /// Decodes images GDI+ cannot read (webp, avif) through WPF's wrapper of WIC — the same
    /// codec pipeline, without hand-declared COM interfaces. Encoding stays with GDI+ (PNG),
    /// so only this one direction needs WPF.
    /// </summary>
    internal static class WicLoader
    {
        public static Bitmap Load(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var decoder = BitmapDecoder.Create(stream,
                    BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) throw new InvalidOperationException("解码结果为空。");
                BitmapSource source = decoder.Frames[0];
                if (source.Format != PixelFormats.Bgra32)
                    source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

                int width = source.PixelWidth, height = source.PixelHeight;
                if (width == 0 || height == 0) throw new InvalidOperationException("解码结果尺寸为 0。");
                int stride = width * 4;
                var pixels = new byte[stride * (long)height];
                source.CopyPixels(pixels, stride, 0);

                // WPF Bgra32 and GDI+ Format32bppArgb have the same byte order (B,G,R,A).
                var bmp = new Bitmap(width, height, GdiPixelFormat.Format32bppArgb);
                var data = bmp.LockBits(new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly, GdiPixelFormat.Format32bppArgb);
                try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
                finally { bmp.UnlockBits(data); }
                return bmp;
            }
        }
    }
}
