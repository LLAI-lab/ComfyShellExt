using System;
using System.Text;
using ComfyShellExt.Shell.Interop;

namespace ComfyShellExt.Shell
{
    /// <summary>
    /// Asks the shell which thumbnail handler an extension currently resolves to. This is the same
    /// lookup Explorer performs, so it accounts for ProgID, UserChoice, SystemFileAssociations and
    /// perceived type precedence instead of us guessing at registry layout.
    /// </summary>
    public static class AssocResolver
    {
        public static string Resolve(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return null;
            if (extension[0] != '.') extension = "." + extension;
            var buffer = new StringBuilder(512);
            int size = buffer.Capacity;
            int hr = NativeMethods.AssocQueryString(NativeMethods.AssocfNoTruncate,
                NativeMethods.AssocstrShellExtension, extension,
                ShellConstants.ThumbnailProviderIid, buffer, ref size);
            if (hr != 0) return null;
            var value = buffer.ToString().Trim();
            return value.Length == 0 ? null : value;
        }

        public static bool ResolvesTo(string extension, string clsid)
        {
            var current = Resolve(extension);
            return current != null && string.Equals(current, clsid, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Friendly name of a CLSID plus its server dll, for diagnostics.</summary>
        public static string Describe(string clsid)
        {
            if (string.IsNullOrEmpty(clsid)) return "(none)";
            try
            {
                using (var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid))
                {
                    if (key == null) return clsid + " (unregistered)";
                    var name = key.GetValue("") as string;
                    string server = null;
                    using (var inproc = key.OpenSubKey("InprocServer32"))
                        if (inproc != null) server = inproc.GetValue("") as string;
                    return clsid + (string.IsNullOrEmpty(name) ? "" : " " + name) +
                           (string.IsNullOrEmpty(server) ? "" : " [" + server + "]");
                }
            }
            catch { return clsid; }
        }
    }
}
