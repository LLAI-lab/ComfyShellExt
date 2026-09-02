using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ComfyShellExt.Shell
{
    /// <summary>
    /// Neutral tile used when neither Windows nor ffmpeg can produce a frame. Only ever shown for
    /// files that do carry a workflow, where the alternative is a blank generic icon.
    /// </summary>
    internal static class PlaceholderRenderer
    {
        public static Bitmap Render(int cx, string label)
        {
            int size = Math.Max(32, Math.Min(cx, 1024));
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                int inset = Math.Max(1, size / 16);
                var rect = new Rectangle(inset, inset, size - inset * 2, size - inset * 2);
                using (var brush = new LinearGradientBrush(rect, Color.FromArgb(255, 39, 46, 63),
                           Color.FromArgb(255, 22, 27, 38), LinearGradientMode.ForwardDiagonal))
                    g.FillRectangle(brush, rect);
                using (var pen = new Pen(Color.FromArgb(120, 148, 163, 184), Math.Max(1f, size / 128f)))
                    g.DrawRectangle(pen, rect);
                if (!string.IsNullOrEmpty(label) && size >= 64)
                {
                    float em = Math.Max(9f, size * 0.14f);
                    using (var font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var brush = new SolidBrush(Color.FromArgb(200, 148, 163, 184)))
                    {
                        var format = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };
                        g.DrawString(label.ToUpperInvariant(), font, brush, rect, format);
                        format.Dispose();
                    }
                }
            }
            return bitmap;
        }
    }
}
