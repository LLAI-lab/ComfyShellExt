using System;
using Microsoft.Win32;
using ComfyShellExt.Core.Util;
using ComfyShellExt.Shell.Interop;

namespace ComfyShellExt.Shell
{
    public enum MediaKind
    {
        Image = 0,
        Video = 1
    }

    /// <summary>
    /// Remembers which handler owned a file type before installation, so the thumbnail we badge is
    /// the one Windows would have drawn. install.bat records these; the built in defaults are the
    /// stock Windows handlers and are used when nothing was recorded.
    /// </summary>
    public static class OriginalProviders
    {
        public const string RegistryRoot = @"SOFTWARE\ComfyShellExt";
        public const string OriginalsKey = RegistryRoot + @"\Originals";

        public static Guid Resolve(string extension, MediaKind kind)
        {
            var saved = ReadSaved(extension);
            if (saved != Guid.Empty && !IsOurs(saved)) return saved;
            return kind == MediaKind.Video
                ? ShellConstants.PropertyThumbnailHandler
                : ShellConstants.PhotoThumbnailProvider;
        }

        public static Guid ReadSaved(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return Guid.Empty;
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = baseKey.OpenSubKey(OriginalsKey))
                    {
                        var value = key == null ? null : key.GetValue(extension.ToLowerInvariant()) as string;
                        Guid parsed;
                        if (!string.IsNullOrEmpty(value) && TryParse(value, out parsed)) return parsed;
                    }
                }
                catch (Exception ex) { Log.Error("ReadSaved " + extension, ex); }
            }
            return Guid.Empty;
        }

        public static bool IsOurs(Guid clsid)
        {
            return clsid == typeof(ImageThumbnailProvider).GUID ||
                   clsid == typeof(VideoThumbnailProvider).GUID;
        }

        public static bool TryParse(string text, out Guid value)
        {
            value = Guid.Empty;
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                value = new Guid(text.Trim());
                return true;
            }
            catch { return false; }
        }
    }
}
