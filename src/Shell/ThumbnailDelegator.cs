using System;
using System.Runtime.InteropServices;
using ComfyShellExt.Core.Util;
using ComfyShellExt.Shell.Interop;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace ComfyShellExt.Shell
{
    /// <summary>
    /// Asks the handler that Windows used before we took over the file type for the base thumbnail,
    /// so images and videos keep looking exactly the way they did; we only paint the badge on top.
    /// </summary>
    internal static class ThumbnailDelegator
    {
        [ThreadStatic] private static bool _busy;

        public static bool TryGet(Guid clsid, uint cx, ComTypes.IStream stream, IShellItem item,
            string path, out IntPtr hbmp, out WtsAlphaType alpha)
        {
            hbmp = IntPtr.Zero;
            alpha = WtsAlphaType.Unknown;
            if (clsid == Guid.Empty || _busy) return false;
            object instance = null;
            _busy = true;
            try
            {
                var type = Type.GetTypeFromCLSID(clsid, false);
                if (type == null) return false;
                instance = Activator.CreateInstance(type);
                var provider = instance as IThumbnailProvider;
                if (provider == null)
                {
                    Log.Write("delegate {0} does not implement IThumbnailProvider", clsid);
                    return false;
                }
                if (!Initialize(instance, stream, item, path))
                {
                    Log.Write("delegate {0} could not be initialised", clsid);
                    return false;
                }
                provider.GetThumbnail(cx, out hbmp, out alpha);
                return hbmp != IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Log.Error("delegate " + clsid, ex);
                hbmp = IntPtr.Zero;
                return false;
            }
            finally
            {
                _busy = false;
                if (instance != null)
                {
                    try { Marshal.FinalReleaseComObject(instance); } catch { }
                }
            }
        }

        private static bool Initialize(object instance, ComTypes.IStream stream, IShellItem item, string path)
        {
            var withStream = instance as IInitializeWithStream;
            if (withStream != null && stream != null)
            {
                Rewind(stream);
                withStream.Initialize(stream, ShellConstants.StgmRead);
                return true;
            }
            var withItem = instance as IInitializeWithItem;
            if (withItem != null && item != null)
            {
                withItem.Initialize(item, ShellConstants.StgmRead);
                return true;
            }
            var withFile = instance as IInitializeWithFile;
            if (withFile != null && !string.IsNullOrEmpty(path))
            {
                withFile.Initialize(path, ShellConstants.StgmRead);
                return true;
            }
            if (withStream != null && !string.IsNullOrEmpty(path))
            {
                ComTypes.IStream fileStream;
                if (NativeMethods.SHCreateStreamOnFileEx(path, ShellConstants.StgmRead, 0, false,
                        IntPtr.Zero, out fileStream) == 0 && fileStream != null)
                {
                    withStream.Initialize(fileStream, ShellConstants.StgmRead);
                    return true;
                }
            }
            return false;
        }

        public static void Rewind(ComTypes.IStream stream)
        {
            try { stream.Seek(0, 0, IntPtr.Zero); }
            catch (Exception ex) { Log.Error("rewind", ex); }
        }
    }
}
