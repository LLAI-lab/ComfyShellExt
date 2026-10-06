using System;
using System.Collections.Generic;
using Microsoft.Win32;
using ComfyShellExt.Core.Util;
using ComfyShellExt.Shell.Interop;

namespace ComfyShellExt.Shell
{
    /// <summary>
    /// Called by regasm. Takes over the thumbnail handler for the configured extensions, records
    /// what was there before so uninstall can put it back, and verifies through the shell's own
    /// association lookup that our handler actually won.
    /// </summary>
    public static class Registrar
    {
        private const string Iid = ShellConstants.ThumbnailProviderIid;

        public static void Register(Type type, MediaKind kind)
        {
            var clsid = Format(type.GUID);
            var settings = Settings.Current;
            var extensions = kind == MediaKind.Video ? settings.VideoExtensions : settings.ImageExtensions;
            RegistryHelper.Approve(clsid, "ComfyShellExt " + kind + " Thumbnail Provider", false);
            if (kind == MediaKind.Video)
            {
                // The sandboxed thumbnail host can only initialise handlers through a stream. This
                // one needs the file path (Windows' own video handler and ffmpeg both do), so it has
                // to opt out of process isolation or Explorer silently gets no thumbnail at all.
                if (settings.VideoDisableProcessIsolation)
                    Report(RegistryHelper.SetClsidValue(clsid, "DisableProcessIsolation", 1)
                        ? "DisableProcessIsolation=1 set (required for path based video handlers)"
                        : "WARNING: could not set DisableProcessIsolation, video badges will not appear");
                else
                    Report("WARNING: video.disableprocessisolation=0, Explorer will not be able to " +
                           "initialise the video handler");
            }
            else if (settings.ImageDisableProcessIsolation)
            {
                // Same reasoning: stream initialisation hides the file name, which the obfuscation
                // preview's keyword matching needs. The image decoder is managed GDI+/WIC, so
                // running inside Explorer is an acceptable risk, same as the video handler.
                Report(RegistryHelper.SetClsidValue(clsid, "DisableProcessIsolation", 1)
                    ? "DisableProcessIsolation=1 set (obfuscation preview needs the file name)"
                    : "WARNING: could not set DisableProcessIsolation, obfuscation preview keywords " +
                      "will not match in Explorer");
            }
            foreach (var extension in extensions)
            {
                try { RegisterExtension(extension.ToLowerInvariant(), clsid); }
                catch (Exception ex) { Report(extension + ": " + ex.Message); }
            }
        }

        /// <summary>
        /// Dry run: reports the registry keys install would claim for each extension and what they
        /// hold today, without changing anything.
        /// </summary>
        public static List<string> Plan(Type type, MediaKind kind)
        {
            var clsid = Format(type.GUID);
            var settings = Settings.Current;
            var extensions = kind == MediaKind.Video ? settings.VideoExtensions : settings.ImageExtensions;
            var lines = new List<string>();
            foreach (var raw in extensions)
            {
                var extension = raw.ToLowerInvariant();
                lines.Add(extension + "  currently -> " + (AssocResolver.Resolve(extension) ?? "(none)"));
                foreach (var candidate in CandidateKeys(extension))
                {
                    var previous = RegistryHelper.ReadClassesDefault(candidate);
                    lines.Add("    HKCR\\" + candidate);
                    lines.Add("        now: " + (previous ?? "(key absent)") + "   after: " + clsid);
                }
            }
            return lines;
        }

        public static void Unregister(Type type)
        {
            var clsid = Format(type.GUID);
            RegistryHelper.Approve(clsid, null, true);
            var extensions = new List<string>();
            using (var backups = RegistryHelper.OpenOwnKey("Backup", false))
            {
                if (backups != null)
                    foreach (var name in backups.GetSubKeyNames())
                    {
                        using (var entry = backups.OpenSubKey(name))
                        {
                            var owner = entry == null ? null : entry.GetValue("owner") as string;
                            if (string.Equals(owner, clsid, StringComparison.OrdinalIgnoreCase))
                                extensions.Add(name);
                        }
                    }
            }
            foreach (var extension in extensions)
            {
                try { RestoreExtension(extension); }
                catch (Exception ex) { Report(extension + ": " + ex.Message); }
            }
        }

        private static void RegisterExtension(string extension, string clsid)
        {
            var before = AssocResolver.Resolve(extension);
            SaveOriginal(extension, before);
            var written = new List<KeyValuePair<string, string>>();
            bool won = false;
            foreach (var candidate in CandidateKeys(extension))
            {
                var previous = RegistryHelper.ReadClassesDefault(candidate);
                if (!string.Equals(previous, clsid, StringComparison.OrdinalIgnoreCase))
                {
                    RegistryHelper.WriteClassesDefault(candidate, clsid);
                    written.Add(new KeyValuePair<string, string>(candidate, previous));
                }
                if (AssocResolver.ResolvesTo(extension, clsid)) { won = true; break; }
            }
            SaveBackup(extension, clsid, written);
            Report(string.Format("{0,-6} original={1} {2}", extension, before ?? "(none)",
                won ? "-> ComfyShellExt" : "WARNING: another handler still wins"));
        }

        private static void RestoreExtension(string extension)
        {
            using (var entry = RegistryHelper.OpenOwnKey("Backup\\" + extension, false))
            {
                if (entry == null) return;
                for (int i = 0; ; i++)
                {
                    var path = entry.GetValue("key" + i) as string;
                    if (string.IsNullOrEmpty(path)) break;
                    var previous = entry.GetValue("prev" + i) as string;
                    if (previous == null) RegistryHelper.DeleteClassesKey(path, 2);
                    else RegistryHelper.WriteClassesDefault(path, previous);
                    Report(string.Format("{0,-6} restored {1} = {2}", extension, path,
                        previous ?? "(removed)"));
                }
            }
            RegistryHelper.DeleteOwnKey("Backup\\" + extension);
            using (var originals = RegistryHelper.OpenOwnKey("Originals", true))
                if (originals != null) originals.DeleteValue(extension, false);
        }

        /// <summary>
        /// Places to claim the handler, least invasive first. We stop at the first one that actually
        /// wins the shell's lookup, so a third party ProgID is only touched when it has to be.
        /// </summary>
        private static IEnumerable<string> CandidateKeys(string extension)
        {
            yield return @"SystemFileAssociations\" + extension + @"\ShellEx\" + Iid;
            if (RegistryHelper.ClassesKeyExists(extension))
                yield return extension + @"\ShellEx\" + Iid;
            var userChoice = ReadUserChoice(extension);
            if (!string.IsNullOrEmpty(userChoice) && RegistryHelper.ClassesKeyExists(userChoice))
                yield return userChoice + @"\ShellEx\" + Iid;
            var progId = RegistryHelper.ReadClassesDefault(extension);
            if (!string.IsNullOrEmpty(progId) && progId != userChoice &&
                RegistryHelper.ClassesKeyExists(progId))
                yield return progId + @"\ShellEx\" + Iid;
        }

        private static string ReadUserChoice(string extension)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + extension +
                    @"\UserChoice"))
                    return key == null ? null : key.GetValue("ProgId") as string;
            }
            catch { return null; }
        }

        private static void SaveOriginal(string extension, string original)
        {
            Guid parsed;
            if (string.IsNullOrEmpty(original) || !OriginalProviders.TryParse(original, out parsed) ||
                OriginalProviders.IsOurs(parsed)) return;
            using (var key = RegistryHelper.CreateOwnKey("Originals"))
            {
                if (key == null) return;
                // Never overwrite: the first value recorded is the true pre-install handler.
                if (key.GetValue(extension) == null) key.SetValue(extension, original, RegistryValueKind.String);
            }
        }

        private static void SaveBackup(string extension, string clsid,
            List<KeyValuePair<string, string>> written)
        {
            using (var key = RegistryHelper.CreateOwnKey("Backup\\" + extension))
            {
                if (key == null) return;
                key.SetValue("owner", clsid, RegistryValueKind.String);
                int index = 0;
                while (key.GetValue("key" + index) != null) index++;
                foreach (var pair in written)
                {
                    key.SetValue("key" + index, pair.Key, RegistryValueKind.String);
                    if (pair.Value != null) key.SetValue("prev" + index, pair.Value, RegistryValueKind.String);
                    index++;
                }
            }
        }

        private static string Format(Guid guid)
        {
            return "{" + guid.ToString().ToUpperInvariant() + "}";
        }

        /// <summary>regasm shows console output, so installation is self documenting.</summary>
        private static void Report(string message)
        {
            Log.Write(message);
            try { Console.WriteLine("  " + message); } catch { }
        }
    }
}
