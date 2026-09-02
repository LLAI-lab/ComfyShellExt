using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using ComfyShellExt.Shell.Interop;

namespace ComfyShellExt.Shell
{
    /// <summary>
    /// Asks the shell for a thumbnail the same way Explorer does. Comparing this with what
    /// ThumbnailPreview renders answers the question "is Explorer actually using our handler".
    /// </summary>
    public static class ShellThumbnail
    {
        /// <summary>The thumbnail Explorer would draw, or null when the shell has none.</summary>
        public static Bitmap GetThumbnail(string path, int size, out string note)
        {
            return Get(path, size, Siigbf.BiggerSizeOk | Siigbf.ThumbnailOnly, out note);
        }

        /// <summary>
        /// The file type icon Explorer falls back to, for example the default player's icon for a
        /// video Windows cannot decode. ICONONLY never calls thumbnail providers, so this cannot
        /// recurse back into our own handler.
        /// </summary>
        public static Bitmap GetIcon(string path, int size, out string note)
        {
            return Get(path, size, Siigbf.IconOnly, out note);
        }

        public static Bitmap Get(string path, int size, Siigbf flags, out string note)
        {
            note = null;
            var full = Path.GetFullPath(path);
            if (!File.Exists(full)) throw new FileNotFoundException(full);
            var iid = typeof(IShellItemImageFactory).GUID;
            object item;
            NativeMethods.SHCreateItemFromParsingName(full, IntPtr.Zero, ref iid, out item);
            var factory = item as IShellItemImageFactory;
            if (factory == null)
            {
                note = "the shell did not return IShellItemImageFactory";
                return null;
            }
            IntPtr hbmp = IntPtr.Zero;
            try
            {
                factory.GetImage(new SizeI { cx = size, cy = size }, flags, out hbmp);
                if (hbmp == IntPtr.Zero)
                {
                    note = "the shell returned no bitmap";
                    return null;
                }
                // The shell hands back an ARGB DIB section for both thumbnails and icons.
                return BitmapUtil.FromHBitmap(hbmp, WtsAlphaType.Argb);
            }
            catch (COMException ex)
            {
                note = "shell refused: 0x" + ex.ErrorCode.ToString("X8") +
                       ((flags & Siigbf.ThumbnailOnly) != 0
                           ? " (no thumbnail available for this file)" : "");
                return null;
            }
            finally
            {
                if (hbmp != IntPtr.Zero) NativeMethods.DeleteObject(hbmp);
                if (item != null) try { Marshal.FinalReleaseComObject(item); } catch { }
            }
        }
    }
}
