using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using ComfyShellExt.Core.Util;

namespace ComfyShellExt.Shell
{
    /// <summary>
    /// Installs the right click entry. Two independent mechanisms, both driven by the ini:
    /// a content aware COM handler (Windows 11 shows it under "show more options"), and a static
    /// registry verb (reaches the new top level menu but cannot look inside the file). The viewer
    /// offers the export buttons, so one entry is enough. Every key created is recorded so
    /// uninstall removes exactly what was added.
    /// </summary>
    public static class MenuRegistrar
    {
        private const string HandlerName = "ComfyShellExt";
        private const string ViewVerb = "ComfyShellExt.ViewAIInfo";
        private const string ObfuscateVerb = "ComfyShellExt.Obfuscate";
        private const string DeobfuscateVerb = "ComfyShellExt.Deobfuscate";
        /// <summary>Static verb of older installs, removed when they upgrade.</summary>
        private const string LegacyViewVerb = "ComfyShellExt.ViewWorkflow";
        private const string LegacyExportVerb = "ComfyShellExt.ExportWorkflow";
        private const string KeyListName = "MenuKeys";

        public static void Register(Type type)
        {
            var clsid = "{" + type.GUID.ToString().ToUpperInvariant() + "}";
            var settings = Settings.Current;
            RegistryHelper.Approve(clsid, "ComfyShellExt AI Info Context Menu", false);
            var created = new List<string>();
            var exe = Path.Combine(Paths.AppDir, "ComfyWorkflowMenu.exe");
            if (!File.Exists(exe)) Report("WARNING: ComfyWorkflowMenu.exe missing, menu commands will do nothing");

            foreach (var extension in Extensions(settings))
            {
                try
                {
                    if (settings.MenuDynamic)
                    {
                        var key = @"SystemFileAssociations\" + extension +
                                  @"\ShellEx\ContextMenuHandlers\" + HandlerName;
                        RegistryHelper.WriteClassesDefault(key, clsid);
                        created.Add(key);
                    }
                    if (settings.MenuStaticVerbs)
                        created.Add(WriteVerb(extension, ViewVerb, settings.MenuViewLabel, exe, "view"));
                    // The shuffle only makes sense on images, so the static verbs skip videos
                    // even though the view verb above covers everything.
                    if (settings.MenuStaticVerbs && settings.MenuObfuscate &&
                        settings.ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        created.Add(WriteVerb(extension, ObfuscateVerb,
                            settings.MenuObfuscateLabel, exe, "obfuscate"));
                        created.Add(WriteVerb(extension, DeobfuscateVerb,
                            settings.MenuDeobfuscateLabel, exe, "deobfuscate"));
                    }
                    // Upgrades from the two entry layout leave these behind.
                    RemoveLegacyVerb(extension, LegacyViewVerb);
                    RemoveLegacyVerb(extension, LegacyExportVerb);
                }
                catch (Exception ex) { Report(extension + ": " + ex.Message); }
            }
            SaveKeyList(created);
            Report(string.Format("context menu: dynamic={0} staticVerbs={1} on {2} extension(s)",
                settings.MenuDynamic ? "on" : "off", settings.MenuStaticVerbs ? "on" : "off",
                Extensions(settings).Count));
        }

        /// <summary>Dry run: the keys install would create for the right click entry.</summary>
        public static List<string> Plan(Type type)
        {
            var clsid = "{" + type.GUID.ToString().ToUpperInvariant() + "}";
            var settings = Settings.Current;
            var lines = new List<string>();
            var exe = Path.Combine(Paths.AppDir, "ComfyWorkflowMenu.exe");
            foreach (var extension in Extensions(settings))
            {
                if (settings.MenuDynamic)
                    lines.Add(@"HKCR\SystemFileAssociations\" + extension +
                              @"\ShellEx\ContextMenuHandlers\" + HandlerName + " = " + clsid);
                if (settings.MenuStaticVerbs)
                    lines.Add(@"HKCR\SystemFileAssociations\" + extension + @"\shell\" + ViewVerb +
                              "  -> \"" + exe + "\" view \"%1\"");
                if (settings.MenuStaticVerbs && settings.MenuObfuscate &&
                    settings.ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    lines.Add(@"HKCR\SystemFileAssociations\" + extension + @"\shell\" + ObfuscateVerb +
                              "  -> \"" + exe + "\" obfuscate \"%1\"");
                    lines.Add(@"HKCR\SystemFileAssociations\" + extension + @"\shell\" + DeobfuscateVerb +
                              "  -> \"" + exe + "\" deobfuscate \"%1\"");
                }
            }
            if (lines.Count == 0) lines.Add("(both menu.dynamic and menu.staticverbs are off)");
            return lines;
        }

        public static void Unregister(Type type)
        {            var clsid = "{" + type.GUID.ToString().ToUpperInvariant() + "}";
            RegistryHelper.Approve(clsid, null, true);
            var keys = LoadKeyList();
            foreach (var key in keys)
            {
                RegistryHelper.DeleteClassesKey(key, 3);
                Report("removed HKCR\\" + key);
            }
            RegistryHelper.DeleteOwnKey(KeyListName);
            if (keys.Count == 0) Report("context menu: nothing was registered");
        }

        private static string WriteVerb(string extension, string verb, string label, string exe, string command)
        {
            var key = @"SystemFileAssociations\" + extension + @"\shell\" + verb;
            using (var hklm = RegistryHelper.OpenHklm())
            using (var node = hklm.CreateSubKey(@"SOFTWARE\Classes\" + key))
            {
                if (node == null) throw new InvalidOperationException("cannot create " + key);
                node.SetValue("MUIVerb", label, RegistryValueKind.String);
                // Without this a verb here can be promoted to the double click default.
                node.SetValue("NeverDefault", "", RegistryValueKind.String);
                node.SetValue("MultiSelectModel", "Player", RegistryValueKind.String);
                using (var commandKey = node.CreateSubKey("command"))
                    if (commandKey != null)
                        commandKey.SetValue("", "\"" + exe + "\" " + command + " \"%1\"",
                            RegistryValueKind.String);
            }
            return key;
        }

        private static void RemoveLegacyVerb(string extension, string verb)
        {
            var key = @"SystemFileAssociations\" + extension + @"\shell\" + verb;
            if (!RegistryHelper.ClassesKeyExists(key)) return;
            RegistryHelper.DeleteClassesKey(key, 3);
            Report("removed old entry HKCR\\" + key);
        }

        private static List<string> Extensions(Settings settings)
        {
            var list = new List<string>();
            foreach (var extension in settings.ImageExtensions)
            {
                var e = extension.ToLowerInvariant();
                if (!list.Contains(e)) list.Add(e);
            }
            foreach (var extension in settings.VideoExtensions)
            {
                var e = extension.ToLowerInvariant();
                if (!list.Contains(e)) list.Add(e);
            }
            return list;
        }

        private static void SaveKeyList(List<string> keys)
        {
            using (var node = RegistryHelper.CreateOwnKey(KeyListName))
            {
                if (node == null) return;
                foreach (var name in node.GetValueNames()) node.DeleteValue(name, false);
                for (int i = 0; i < keys.Count; i++)
                    if (!string.IsNullOrEmpty(keys[i]))
                        node.SetValue("key" + i, keys[i], RegistryValueKind.String);
            }
        }

        private static List<string> LoadKeyList()
        {
            var keys = new List<string>();
            using (var node = RegistryHelper.OpenOwnKey(KeyListName, false))
            {
                if (node == null) return keys;
                for (int i = 0; ; i++)
                {
                    var value = node.GetValue("key" + i) as string;
                    if (string.IsNullOrEmpty(value)) break;
                    keys.Add(value);
                }
            }
            return keys;
        }

        private static void Report(string message)
        {
            Log.Write(message);
            try { Console.WriteLine("  " + message); } catch { }
        }
    }
}
