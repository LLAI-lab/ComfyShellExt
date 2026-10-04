using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using ComfyShellExt.Core.Obfuscator;
using ComfyShellExt.Core.Util;
using ComfyShellExt.Menu;

namespace ComfyShellExt.Menu
{
    /// <summary>
    /// obfuscate / deobfuscate: shuffles the pixels of the selected images along a Gilbert curve
    /// so the picture becomes noise, and undoes it again. Output is always PNG (lossless, so the
    /// shuffle can be undone exactly) with all metadata stripped — the point of obfuscating is
    /// that the AI workflow hidden in the file does not survive. Encoding stays GDI+; the webp /
    /// avif inputs GDI+ cannot read go through WIC.
    /// </summary>
    internal static class ObfuscateCommand
    {
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".jpe", ".webp", ".gif", ".bmp", ".dib", ".tif", ".tiff", ".avif" };

        public static int Run(List<string> files, bool encrypt, bool noOpen)
        {
            var written = new List<string>();
            var skipped = new List<string>();
            foreach (var path in files)
            {
                if (!File.Exists(path)) { skipped.Add(Path.GetFileName(path)); continue; }
                if (!ImageExtensions.Contains(Path.GetExtension(path)))
                {
                    skipped.Add(Path.GetFileName(path));
                    continue;
                }
                try
                {
                    written.Add(Process(path, encrypt));
                    if (Program.Quiet)
                        Console.WriteLine((encrypt ? "obfuscated " : "restored ") + written[written.Count - 1]);
                }
                catch (Exception ex)
                {
                    Log.Error("obfuscate " + path, ex);
                    skipped.Add(Path.GetFileName(path) + "（" + ex.Message + "）");
                }
            }

            if (written.Count == 0)
            {
                var names = new System.Text.StringBuilder();
                for (int i = 0; i < skipped.Count && i < 8; i++)
                    names.Append(names.Length > 0 ? "\n" : "").Append("  ").Append(skipped[i]);
                Program.Show("没有生成任何文件。\n\n" + (skipped.Count > 0 ? "失败的文件：\n" + names : "没有可处理的图片文件。"),
                    MessageBoxIcon.Warning);
                return 1;
            }
            if (written.Count == 1 && skipped.Count == 0)
            {
                if (!noOpen) Program.Reveal(written[0]);
                return 0;
            }
            var message = new System.Text.StringBuilder();
            string label = encrypt ? "混淆" : "解混淆";
            message.Append("已").Append(label).Append(" ").Append(written.Count).Append(" 个文件：\n");
            for (int i = 0; i < written.Count && i < 12; i++)
                message.Append("  ").Append(Path.GetFileName(written[i])).Append('\n');
            if (written.Count > 12) message.Append("  ...\n");
            if (skipped.Count > 0)
            {
                message.Append("\n跳过 ").Append(skipped.Count).Append(" 个文件：\n");
                for (int i = 0; i < skipped.Count && i < 8; i++)
                    message.Append("  ").Append(skipped[i]).Append('\n');
            }
            Program.Show(message.ToString(), MessageBoxIcon.Information);
            if (!noOpen) Program.Reveal(written[0]);
            return 0;
        }

        private static string Process(string path, bool encrypt)
        {
            string output = Program.Unique(Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(path)),
                Path.GetFileNameWithoutExtension(path) + (encrypt ? "_混淆" : "_还原") + ".png"));
            // Metadata preservation: obfuscation embeds the original entries when keepmeta is on;
            // deobfuscation always restores whatever the obfuscated file actually carries.
            byte[] captured = null;
            List<MetaVault.Record> carried = null;
            if (encrypt)
            {
                if (Settings.Current.ObfuscateKeepMeta) captured = MetaVault.Capture(path);
            }
            else
            {
                carried = MetaVault.Extract(path);
            }
            int width, height;
            using (Bitmap source = Load(path))
            using (Bitmap clean = Normalize(source))
            {
                width = clean.Width;
                height = clean.Height;
                PixelShuffle.Apply(clean, encrypt);
                clean.Save(output, ImageFormat.Png);
            }
            if (encrypt && captured != null) MetaVault.Embed(output, captured);
            if (!encrypt && carried != null) MetaVault.Restore(output, carried);
            return output;
        }

        /// <summary>GDI+ first (covers png/jpg/bmp/gif/tiff), WIC for the rest (webp/avif).</summary>
        private static Bitmap Load(string path)
        {
            try
            {
                using (var raw = new Bitmap(path))
                    return Normalize(raw);
            }
            catch (Exception ex)
            {
                Log.Error("gdi+ decode " + path + ", trying WIC", ex);
                return WicLoader.Load(path);
            }
        }

        /// <summary>
        /// Redraws into a fresh 32bppArgb bitmap: normalises indexed / 16bpp sources and drops
        /// every property item, so no workflow metadata reaches the output file.
        /// </summary>
        private static Bitmap Normalize(Bitmap source)
        {
            var clean = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(clean))
                graphics.DrawImageUnscaled(source, 0, 0);
            return clean;
        }
    }
}
