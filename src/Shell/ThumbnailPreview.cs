using System;
using System.Drawing;
using System.IO;
using System.Text;
using ComfyShellExt.Core;
using ComfyShellExt.Core.Util;
using ComfyShellExt.Shell.Interop;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace ComfyShellExt.Shell
{
    /// <summary>
    /// Runs the real handler code path outside Explorer so a thumbnail can be inspected without
    /// installing anything. Used by the preview command and during development.
    /// </summary>
    public static class ThumbnailPreview
    {
        public static Bitmap Render(string path, int cx, bool viaStream, StringBuilder report)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            var full = Path.GetFullPath(path);
            var kind = ClassifyByExtension(Path.GetExtension(full));
            ThumbnailProviderBase provider = kind == MediaKind.Video
                ? (ThumbnailProviderBase)new VideoThumbnailProvider()
                : new ImageThumbnailProvider();

            var image = provider as ImageThumbnailProvider;
            if (viaStream && image != null)
            {
                // The image handler no longer takes streams (path needed for obfuscation preview
                // keywords), so this mode now means "file path only, no extension info".
                image.Initialize(full, ShellConstants.StgmRead);
                Append(report, "initialised with a file path");
            }
            else
            {
                ((IInitializeWithFile)provider).Initialize(full, ShellConstants.StgmRead);
                Append(report, "initialised with a file path");
            }

            var original = OriginalProviders.Resolve(viaStream ? null : Path.GetExtension(full), kind);
            Append(report, "kind          : " + kind);
            Append(report, "base handler  : " + AssocResolver.Describe("{" + original.ToString().ToUpperInvariant() + "}"));
            var info = WorkflowDetector.InspectFile(full, DetectOptions.Fast());
            Append(report, "workflow      : " + (info.HasWorkflow ? "YES via " + info.Source : "no"));

            IntPtr hbmp;
            WtsAlphaType alpha;
            provider.GetThumbnail((uint)cx, out hbmp, out alpha);
            try
            {
                var bitmap = BitmapUtil.FromHBitmap(hbmp, alpha);
                if (bitmap != null)
                    Append(report, string.Format("result        : {0}x{1} alpha={2}",
                        bitmap.Width, bitmap.Height, alpha));
                return bitmap;
            }
            finally { if (hbmp != IntPtr.Zero) NativeMethods.DeleteObject(hbmp); }
        }

        /// <summary>Size and colour depth of a returned HBITMAP, for diagnostics.</summary>
        public static string DescribeBitmap(IntPtr hbmp, out int bitsPerPixel)
        {
            bitsPerPixel = 0;
            if (hbmp == IntPtr.Zero) return "(null bitmap)";
            BitmapStruct info;
            if (NativeMethods.GetObject(hbmp,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(BitmapStruct)), out info) == 0)
                return "(not a bitmap)";
            bitsPerPixel = info.bmBitsPixel;
            return info.bmWidth + "x" + info.bmHeight + " " + info.bmBitsPixel + "bpp";
        }

        public static void ReleaseBitmap(IntPtr hbmp)
        {
            if (hbmp != IntPtr.Zero) NativeMethods.DeleteObject(hbmp);
        }

        public static MediaKind ClassifyByExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return MediaKind.Image;
            var lower = extension.ToLowerInvariant();
            foreach (var candidate in Settings.Current.VideoExtensions)
                if (candidate == lower) return MediaKind.Video;
            return MediaKind.Image;
        }

        private static void Append(StringBuilder report, string line)
        {
            if (report != null) report.AppendLine(line);
        }
    }
}
