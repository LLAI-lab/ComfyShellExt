using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using ComfyShellExt.Shell;

namespace ComfyShellExt.Cli
{
    /// <summary>Renders thumbnails through the real handler code path so they can be eyeballed.</summary>
    internal static class PreviewCommand
    {
        public static int Run(Args args)
        {
            if (args.Values.Count == 0)
            {
                Console.Error.WriteLine(
                    "usage: ComfyWorkflowDb preview <file|dir> [...] [--size 256] [-o out.png] [--sheet sheet.png] [--stream]");
                return 2;
            }
            int size = args.Int("size", 256);
            bool viaStream = args.Has("stream");
            bool viaShell = args.Has("shell");
            var files = new List<string>();
            foreach (var input in args.Values)
            {
                if (Directory.Exists(input)) files.AddRange(Directory.GetFiles(input));
                else if (File.Exists(input)) files.Add(input);
                else Console.Error.WriteLine("not found: " + input);
            }
            if (files.Count == 0) return 1;

            var rendered = new List<KeyValuePair<string, Bitmap>>();
            foreach (var file in files)
            {
                var report = new StringBuilder();
                Bitmap bitmap = null;
                try
                {
                    if (viaShell)
                    {
                        string note;
                        bitmap = ShellThumbnail.GetThumbnail(file, size, out note);
                        if (bitmap != null)
                            report.AppendLine("the shell returned a THUMBNAIL, this is what Explorer draws");
                        else
                        {
                            report.AppendLine("the shell has no thumbnail for this file: " + note);
                            bitmap = ShellThumbnail.GetIcon(file, size, out note);
                            report.AppendLine(bitmap != null
                                ? "falling back to the file type ICON, which is what Explorer draws instead"
                                : "the shell has no icon either: " + note);
                        }
                        if (bitmap != null)
                            report.AppendLine(string.Format("result        : {0}x{1}",
                                bitmap.Width, bitmap.Height));
                    }
                    else bitmap = ThumbnailPreview.Render(file, size, viaStream, report);
                }
                catch (Exception ex) { report.AppendLine("failed: " + ex.GetType().Name + ": " + ex.Message); }
                Console.WriteLine(Path.GetFileName(file));
                foreach (var line in report.ToString().Split('\n'))
                    if (line.Trim().Length > 0) Console.WriteLine("  " + line.TrimEnd());
                if (bitmap != null) rendered.Add(new KeyValuePair<string, Bitmap>(file, bitmap));
            }

            var sheet = args.Str("sheet");
            var single = args.Str("o") ?? args.Str("out");
            try
            {
                if (sheet != null) SaveSheet(rendered, sheet, size);
                else if (single != null && rendered.Count == 1)
                    rendered[0].Value.Save(single, System.Drawing.Imaging.ImageFormat.Png);
                else if (single != null)
                {
                    Directory.CreateDirectory(single);
                    foreach (var pair in rendered)
                        pair.Value.Save(Path.Combine(single,
                            Path.GetFileNameWithoutExtension(pair.Key) + "_" +
                            Path.GetExtension(pair.Key).TrimStart('.') + ".png"),
                            System.Drawing.Imaging.ImageFormat.Png);
                    Console.WriteLine("wrote {0} preview(s) to {1}", rendered.Count, single);
                }
            }
            finally
            {
                foreach (var pair in rendered) pair.Value.Dispose();
            }
            return rendered.Count == files.Count ? 0 : 1;
        }

        /// <summary>Contact sheet on a checkerboard, so alpha and badge contrast are both visible.</summary>
        private static void SaveSheet(List<KeyValuePair<string, Bitmap>> items, string path, int cell)
        {
            if (items.Count == 0) return;
            int label = Math.Max(14, cell / 12);
            int pad = Math.Max(6, cell / 24);
            int columns = Math.Min(items.Count, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(items.Count) * 1.3)));
            int rows = (items.Count + columns - 1) / columns;
            int cw = cell + pad * 2, ch = cell + label + pad * 2;
            using (var sheet = new Bitmap(columns * cw, rows * ch))
            using (var g = Graphics.FromImage(sheet))
            {
                DrawCheckerboard(g, sheet.Width, sheet.Height, Math.Max(8, cell / 16));
                using (var font = new Font("Segoe UI", label * 0.62f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var ink = new SolidBrush(Color.FromArgb(255, 20, 20, 20)))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        int cx = (i % columns) * cw, cy = (i / columns) * ch;
                        var bitmap = items[i].Value;
                        int x = cx + pad + (cell - bitmap.Width) / 2;
                        int y = cy + pad + (cell - bitmap.Height) / 2;
                        g.DrawImageUnscaled(bitmap, x, y);
                        g.DrawString(Path.GetFileName(items[i].Key), font, ink,
                            new RectangleF(cx, cy + cell + pad, cw, label), format);
                    }
                }
                sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine("wrote contact sheet {0} ({1} tiles)", path, items.Count);
        }

        private static void DrawCheckerboard(Graphics g, int width, int height, int tile)
        {
            using (var light = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
            using (var dark = new SolidBrush(Color.FromArgb(255, 214, 214, 214)))
            {
                g.FillRectangle(light, 0, 0, width, height);
                for (int y = 0; y < height; y += tile)
                    for (int x = 0; x < width; x += tile)
                        if (((x / tile) + (y / tile)) % 2 == 1)
                            g.FillRectangle(dark, x, y, tile, tile);
            }
            g.CompositingQuality = CompositingQuality.HighQuality;
        }
    }
}
