using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using ComfyShellExt.Core.Util;

namespace ComfyShellExt.Shell
{
    /// <summary>Draws the AI tool badge into a corner of a thumbnail.</summary>
    internal static class BadgeRenderer
    {
        public static void Draw(Bitmap bitmap, Settings settings)
        {
            Draw(bitmap, settings, null, null);
        }

        public static void Draw(Bitmap bitmap, Settings settings, string position)
        {
            Draw(bitmap, settings, null, position);
        }

        /// <summary>
        /// The badge text is the generating tool's abbreviation ("Comfy", "A1111", "NAI", ...);
        /// null shows the plain badge.text fallback.
        /// </summary>
        public static void Draw(Bitmap bitmap, Settings settings, string generator, string position)
        {
            if (bitmap == null || settings == null) return;
            if (string.IsNullOrEmpty(position)) position = settings.BadgePosition;
            var badgeText = settings.BadgeTextFor(generator);
            // Explorer fits the thumbnail into a cx box, so the long edge tracks the requested size.
            // Scaling off the long edge keeps the badge the same visual size for any aspect ratio.
            int metric = Math.Max(bitmap.Width, bitmap.Height);
            if (metric < 16 || metric < settings.BadgeMinPx) return;

            var fill = ParseColor(settings.BadgeFill, Color.FromArgb(230, 17, 24, 39));
            var border = ParseColor(settings.BadgeBorder, Color.FromArgb(255, 34, 211, 238));
            var ink = ParseColor(settings.BadgeTextColor, Color.White);
            double scale = settings.BadgeScale <= 0 ? 1 : settings.BadgeScale / 100.0;
            int height = Clamp((int)Math.Round(4 + metric * 0.075), 9, 44);
            height = Math.Max(6, (int)Math.Round(height * scale));
            height = Math.Min(height, Math.Max(8, (int)(bitmap.Height * 0.6)));
            int margin = Math.Max(0, (int)Math.Round(metric * settings.BadgeMargin / 100.0));
            int offsetX = (int)Math.Round(metric * settings.BadgeOffsetX / 100.0);
            int offsetY = (int)Math.Round(metric * settings.BadgeOffsetY / 100.0);
            bool withText = metric >= settings.BadgeTextMinPx && !string.IsNullOrEmpty(badgeText);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (!withText)
                {
                    DrawChip(g, bitmap, height, margin, offsetX, offsetY, fill, border, position);
                    return;
                }
                float emSize = Math.Max(6f, height * 0.66f);
                using (var font = new Font("Segoe UI", emSize, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    var format = StringFormat.GenericTypographic;
                    format.FormatFlags |= StringFormatFlags.NoWrap;
                    var measured = g.MeasureString(badgeText, font, PointF.Empty, format);
                    int padX = Math.Max(3, (int)Math.Round(height * 0.30));
                    int width = (int)Math.Ceiling(measured.Width) + padX * 2;
                    width = Math.Min(width, bitmap.Width - margin * 2);
                    var rect = Place(bitmap, width, height, margin, offsetX, offsetY, position);
                    Fill(g, rect, height, fill, border);
                    var textRect = new RectangleF(rect.X + padX, rect.Y, rect.Width - padX * 2, rect.Height);
                    format.LineAlignment = StringAlignment.Center;
                    format.Alignment = StringAlignment.Center;
                    using (var brush = new SolidBrush(ink))
                        g.DrawString(badgeText, font, brush, textRect, format);
                }
            }
        }

        /// <summary>
        /// At list and details view sizes there is no room for text, so use a solid accent chip:
        /// a filled shape reads at 9 pixels where an outline or a glyph would not.
        /// </summary>
        private static void DrawChip(Graphics g, Bitmap bitmap, int height, int margin,
            int offsetX, int offsetY, Color fill, Color border, string position)
        {
            int width = Math.Max(6, (int)Math.Round(height * 1.35));
            var rect = Place(bitmap, width, height, margin, offsetX, offsetY, position);
            Fill(g, rect, height, border, fill);
        }

        private static void Fill(Graphics g, Rectangle rect, int height, Color fill, Color border)
        {
            int radius = Math.Max(2, height / 3);
            using (var shadowPath = RoundedRect(new Rectangle(rect.X, rect.Y + 1, rect.Width, rect.Height), radius))
            using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                g.FillPath(shadow, shadowPath);
            using (var path = RoundedRect(rect, radius))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }

        /// <summary>
        /// Nine anchor positions. An axis without a keyword is centred on that axis, so "TopCenter",
        /// "Top" and "Center" all work, and the four corner names keep their old meaning.
        /// </summary>
        private static Rectangle Place(Bitmap bitmap, int width, int height, int margin,
            int offsetX, int offsetY, string position)
        {
            var p = (position ?? "").Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();
            int x;
            if (p.Contains("left")) x = margin;
            else if (p.Contains("right")) x = bitmap.Width - width - margin;
            else x = (bitmap.Width - width) / 2;
            int y;
            if (p.Contains("top")) y = margin;
            else if (p.Contains("bottom")) y = bitmap.Height - height - margin;
            else y = (bitmap.Height - height) / 2;
            x += offsetX;
            y += offsetY;
            x = Clamp(x, 0, Math.Max(0, bitmap.Width - width));
            y = Clamp(y, 0, Math.Max(0, bitmap.Height - height));
            return new Rectangle(x, y, width, height);
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height) - 1));
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d - 1, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d - 1, rect.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static int Clamp(int value, int low, int high)
        {
            return value < low ? low : value > high ? high : value;
        }

        /// <summary>Parses AARRGGBB or RRGGBB hex.</summary>
        internal static Color ParseColor(string text, Color fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            var hex = text.Trim().TrimStart('#');
            uint value;
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                return fallback;
            if (hex.Length == 6) return Color.FromArgb(255, (int)(value >> 16 & 0xFF),
                (int)(value >> 8 & 0xFF), (int)(value & 0xFF));
            if (hex.Length == 8) return Color.FromArgb((int)(value >> 24 & 0xFF), (int)(value >> 16 & 0xFF),
                (int)(value >> 8 & 0xFF), (int)(value & 0xFF));
            return fallback;
        }
    }
}
