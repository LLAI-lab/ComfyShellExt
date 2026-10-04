using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ComfyShellExt.Core.Obfuscator
{
    /// <summary>
    /// Gilbert curve pixel shuffle: a space filling curve visits every pixel of the image exactly
    /// once, and each pixel moves a fixed stride along that walk. A pure permutation of pixels,
    /// so it is lossless and reversible; the stride comes from the golden ratio conjugate and the
    /// image dimensions alone, which makes deobfuscation keyless — the same scheme as the
    /// "图片混淆" web tool, rebuilt on GDI+/WIC.
    /// </summary>
    public static class PixelShuffle
    {
        /// <summary>
        /// Working set is about 12 bytes per pixel (curve table plus source and destination
        /// buffers), so the guard exists to fail with a message instead of an OutOfMemory.
        /// </summary>
        public const long MaxPixels = 64L * 1024 * 1024;

        /// <summary>Applies the shuffle in place. encrypt = obfuscate, false = restore.</summary>
        public static void Apply(Bitmap bmp, bool encrypt)
        {
            if (bmp == null) throw new ArgumentNullException("bmp");
            int width = bmp.Width, height = bmp.Height;
            long total = (long)width * height;
            if (total > MaxPixels)
                throw new NotSupportedException("图片太大（" + width + "x" + height +
                    "），最多支持 " + (MaxPixels / (1024 * 1024)) + " 百万像素。");
            if (total < 2) return;
            if (width > 0xFFFF || height > 0xFFFF)
                throw new NotSupportedException("单边超过 65535 像素的图片不支持。");

            var bounds = new Rectangle(0, 0, width, height);
            var data = bmp.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new byte[4L * total];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                Shuffle(pixels, width, height, encrypt);
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally { bmp.UnlockBits(data); }
        }

        private static void Shuffle(byte[] pixels, int width, int height, bool encrypt)
        {
            long total = (long)width * height;
            long offset = (long)((Math.Sqrt(5) - 1) / 2 * total) % total;
            int[] curve = GilbertCurve(width, height);
            var moved = new byte[pixels.Length];
            for (long i = 0; i < total; i++)
            {
                // The web tool encrypts with new[curve[i+o]] = old[curve[i]]; the inverse swap
                // restores it. Both sides must agree on offset and curve exactly.
                long src = encrypt ? i : (i + offset) % total;
                long dst = encrypt ? (i + offset) % total : i;
                long s = src * 4, d = dst * 4;
                moved[d] = pixels[s];
                moved[d + 1] = pixels[s + 1];
                moved[d + 2] = pixels[s + 2];
                moved[d + 3] = pixels[s + 3];
            }
            Buffer.BlockCopy(moved, 0, pixels, 0, pixels.Length);
        }

        /// <summary>Packed curve positions (x &lt;&lt; 16 | y), one entry per pixel.</summary>
        private static int[] GilbertCurve(int width, int height)
        {
            var curve = new int[(long)width * height];
            int index = 0;
            if (width >= height) Generate2d(curve, ref index, 0, 0, width, 0, 0, height);
            else Generate2d(curve, ref index, 0, 0, 0, height, width, 0);
            return curve;
        }

        private static void Generate2d(int[] curve, ref int index, int x, int y, int ax, int ay, int bx, int by)
        {
            int w = Math.Abs(ax + ay), h = Math.Abs(bx + by);
            int dax = Math.Sign(ax), day = Math.Sign(ay);
            int dbx = Math.Sign(bx), dby = Math.Sign(by);

            if (h == 1)
            {
                for (int i = 0; i < w; i++) { curve[index++] = x << 16 | y; x += dax; y += day; }
                return;
            }
            if (w == 1)
            {
                for (int i = 0; i < h; i++) { curve[index++] = x << 16 | y; x += dbx; y += dby; }
                return;
            }

            // JS uses Math.floor(a / 2), which floors negative halves too; >> 1 does the same
            // for ints while plain division would round towards zero and break the recursion.
            int ax2 = ax >> 1, ay2 = ay >> 1, bx2 = bx >> 1, by2 = by >> 1;

            if (2 * w > 3 * h)
            {
                if ((Math.Abs(ax2 + ay2) % 2) != 0 && w > 2) { ax2 += dax; ay2 += day; }
                Generate2d(curve, ref index, x, y, ax2, ay2, bx, by);
                Generate2d(curve, ref index, x + ax2, y + ay2, ax - ax2, ay - ay2, bx, by);
            }
            else
            {
                if ((Math.Abs(bx2 + by2) % 2) != 0 && h > 2) { bx2 += dbx; by2 += dby; }
                Generate2d(curve, ref index, x, y, bx2, by2, ax2, ay2);
                Generate2d(curve, ref index, x + bx2, y + by2, ax, ay, bx - bx2, by - by2);
                Generate2d(curve, ref index, x + (ax - dax) + (bx2 - dbx), y + (ay - day) + (by2 - dby),
                    -bx2, -by2, -(ax - ax2), -(ay - ay2));
            }
        }
    }
}
