using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ComfyShellExt.Shell.Interop;

namespace ComfyShellExt.Shell
{
    /// <summary>
    /// HBITMAP conversions for thumbnail handlers. Thumbnails must be returned as top-down 32 bit
    /// DIB sections with premultiplied alpha, which is what CreateDIBSection with a negative height
    /// and GDI+ Format32bppPArgb give us.
    /// </summary>
    internal static class BitmapUtil
    {
        public static Bitmap FromHBitmap(IntPtr hbmp, WtsAlphaType alpha)
        {
            BitmapStruct info;
            if (hbmp == IntPtr.Zero || NativeMethods.GetObject(hbmp, Marshal.SizeOf(typeof(BitmapStruct)),
                    out info) == 0) return null;
            int width = info.bmWidth, height = info.bmHeight;
            if (width <= 0 || height <= 0 || (long)width * height > 64L * 1024 * 1024) return null;

            var header = new BitmapInfoHeader
            {
                biSize = Marshal.SizeOf(typeof(BitmapInfoHeader)),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BiRgb
            };
            var pixels = new byte[width * height * 4];
            var hdc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            try
            {
                if (NativeMethods.GetDIBits(hdc, hbmp, 0, height, pixels, ref header,
                        NativeMethods.DibRgbColors) == 0) return null;
            }
            finally { NativeMethods.DeleteDC(hdc); }

            NormaliseAlpha(pixels, alpha, info.bmBitsPixel);
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly,
                PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < height; y++)
                    Marshal.Copy(pixels, y * width * 4, data.Scan0 + y * data.Stride, width * 4);
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }

        /// <summary>
        /// Sources that report RGB, or that lie about having alpha and return an all zero channel,
        /// have to be forced opaque or the composed thumbnail would be invisible.
        /// </summary>
        private static void NormaliseAlpha(byte[] pixels, WtsAlphaType alpha, int sourceDepth)
        {
            bool force = alpha != WtsAlphaType.Argb || sourceDepth < 32;
            if (!force)
            {
                force = true;
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    if (pixels[i] != 0) { force = false; break; }
                }
            }
            if (!force) return;
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 0xFF;
        }

        public static IntPtr ToHBitmap(Bitmap source)
        {
            if (source == null) return IntPtr.Zero;
            int width = source.Width, height = source.Height;
            var header = new BitmapInfoHeader
            {
                biSize = Marshal.SizeOf(typeof(BitmapInfoHeader)),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BiRgb
            };
            IntPtr bits;
            var hbmp = NativeMethods.CreateDIBSection(IntPtr.Zero, ref header,
                NativeMethods.DibRgbColors, out bits, IntPtr.Zero, 0);
            if (hbmp == IntPtr.Zero || bits == IntPtr.Zero) return IntPtr.Zero;
            using (var target = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits))
            using (var g = Graphics.FromImage(target))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImageUnscaled(source, 0, 0);
            }
            return hbmp;
        }

        /// <summary>Scales an image into a cx by cx box, preserving aspect ratio, never upscaling.</summary>
        public static Bitmap Fit(Image source, int cx)
        {
            if (source == null || cx <= 0) return null;
            double scale = Math.Min((double)cx / source.Width, (double)cx / source.Height);
            if (scale > 1) scale = 1;
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, width, height));
            }
            return bitmap;
        }
    }
}
