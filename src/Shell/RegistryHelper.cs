using System;
using Microsoft.Win32;
using ComfyShellExt.Core.Util;

namespace ComfyShellExt.Shell
{
    /// <summary>
    /// Registry access for installation. Everything goes into the 64 bit HKLM\SOFTWARE\Classes view,
    /// which is the machine wide HKCR; the file association subtree is not Wow64 redirected.
    /// </summary>
    public static class RegistryHelper
    {
        public static RegistryKey OpenHklm()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        }

        private static string Classes(string relative) { return @"SOFTWARE\Classes\" + relative; }

        public static string ReadClassesDefault(string relative)
        {
            using (var hklm = OpenHklm())
            using (var key = hklm.OpenSubKey(Classes(relative)))
                return key == null ? null : key.GetValue("") as string;
        }

        public static bool ClassesKeyExists(string relative)
        {
            using (var hklm = OpenHklm())
            using (var key = hklm.OpenSubKey(Classes(relative)))
                return key != null;
        }

        public static void WriteClassesDefault(string relative, string value)
        {
            using (var hklm = OpenHklm())
            using (var key = hklm.CreateSubKey(Classes(relative)))
            {
                if (key == null) throw new InvalidOperationException("cannot create HKCR\\" + relative);
                key.SetValue("", value, RegistryValueKind.String);
            }
        }

        /// <summary>Deletes a key and then any parent left empty, so uninstall leaves no husks.</summary>
        public static void DeleteClassesKey(string relative, int pruneParents)
        {
            using (var hklm = OpenHklm())
            {
                try { hklm.DeleteSubKeyTree(Classes(relative), false); }
                catch (Exception ex) { Log.Error("delete " + relative, ex); }
                var current = relative;
                for (int i = 0; i < pruneParents; i++)
                {
                    int slash = current.LastIndexOf('\\');
                    if (slash <= 0) return;
                    current = current.Substring(0, slash);
                    try
                    {
                        using (var key = hklm.OpenSubKey(Classes(current)))
                        {
                            if (key == null) return;
                            if (key.SubKeyCount != 0 || key.ValueCount != 0) return;
                        }
                        hklm.DeleteSubKey(Classes(current), false);
                    }
                    catch (Exception ex) { Log.Error("prune " + current, ex); return; }
                }
            }
        }

        public static RegistryKey CreateOwnKey(string relative)
        {
            using (var hklm = OpenHklm())
                return hklm.CreateSubKey(OriginalProviders.RegistryRoot +
                                         (string.IsNullOrEmpty(relative) ? "" : "\\" + relative));
        }

        public static RegistryKey OpenOwnKey(string relative, bool writable)
        {
            using (var hklm = OpenHklm())
                return hklm.OpenSubKey(OriginalProviders.RegistryRoot +
                                       (string.IsNullOrEmpty(relative) ? "" : "\\" + relative), writable);
        }

        public static void DeleteOwnKey(string relative)
        {
            using (var hklm = OpenHklm())
            {
                try { hklm.DeleteSubKeyTree(OriginalProviders.RegistryRoot + "\\" + relative, false); }
                catch (Exception ex) { Log.Error("delete own " + relative, ex); }
            }
        }

        private const string ApprovedKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved";

        /// <summary>
        /// Writes a value under the class registration, in whichever bitness views regasm created.
        /// Never creates the CLSID key: an orphan key would look like a broken registration.
        /// </summary>
        public static bool SetClsidValue(string clsid, string name, int value)
        {
            bool wrote = false;
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = baseKey.OpenSubKey(@"SOFTWARE\Classes\CLSID\" + clsid, true))
                    {
                        if (key == null) continue;
                        key.SetValue(name, value, RegistryValueKind.DWord);
                        wrote = true;
                    }
                }
                catch (Exception ex) { Log.Error("SetClsidValue " + clsid, ex); }
            }
            return wrote;
        }

        public static int ReadClsidValue(string clsid, string name, int fallback)
        {
            try
            {
                using (var key = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid))
                {
                    if (key == null) return fallback;
                    var value = key.GetValue(name);
                    return value is int ? (int)value : fallback;
                }
            }
            catch { return fallback; }
        }

        public static void Approve(string clsid, string name, bool remove)
        {
            try
            {
                using (var hklm = OpenHklm())
                using (var key = hklm.CreateSubKey(ApprovedKey))
                {
                    if (key == null) return;
                    if (remove) key.DeleteValue(clsid, false);
                    else key.SetValue(clsid, name, RegistryValueKind.String);
                }
            }
            catch (Exception ex) { Log.Error("approve " + clsid, ex); }
        }
    }
}
