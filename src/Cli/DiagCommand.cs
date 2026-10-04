using System;
using System.Collections.Generic;
using System.IO;
using ComfyShellExt.Core.Util;
using ComfyShellExt.Shell;

namespace ComfyShellExt.Cli
{
    /// <summary>Shows which handler Windows resolves for each file type and what we recorded.</summary>
    internal static class DiagCommand
    {
        public static int Run(Args args)
        {
            var settings = Settings.Current;
            Console.WriteLine("version    : {0}",
                typeof(Registrar).Assembly.GetName().Version);
            Console.WriteLine("assembly   : {0}", typeof(Registrar).Assembly.Location);
            Console.WriteLine("ini        : {0}{1}", Paths.IniPath,
                File.Exists(Paths.IniPath) ? "" : "  (missing, using defaults)");
            Console.WriteLine("data dir   : {0}", Paths.DataDir);
            Console.WriteLine("log        : {0}", settings.LogEnabled
                ? (string.IsNullOrEmpty(settings.LogPath) ? Paths.DefaultLogPath : settings.LogPath)
                : "disabled");
            Console.WriteLine("badge      : text='{0}' pos={1} icon={2} scale={3}% margin={4}% " +
                              "offset={5}%,{6}% textMinPx={7} placeholder={8}",
                settings.BadgeText, settings.BadgePosition, settings.BadgeIconPosition,
                settings.BadgeScale, settings.BadgeMargin, settings.BadgeOffsetX,
                settings.BadgeOffsetY, settings.BadgeTextMinPx, settings.BadgePlaceholder);
            Console.WriteLine("raw scan   : {0}, up to {1} MB per end",
                settings.EnableRawScan ? "on" : "off", settings.MaxRawScanMB);
            Console.WriteLine("ffmpeg     : {0}", FfmpegPath(settings));
            Console.WriteLine("image clsid: {0}", ImageThumbnailProvider.Clsid);
            Console.WriteLine("video clsid: {0}  DisableProcessIsolation={1}{2}",
                VideoThumbnailProvider.Clsid,
                RegistryHelper.ReadClsidValue(VideoThumbnailProvider.Clsid, "DisableProcessIsolation", -1),
                RegistryHelper.ReadClsidValue(VideoThumbnailProvider.Clsid, "DisableProcessIsolation", -1) == 1
                    ? "" : "  <- video thumbnails need this to be 1, re-run install.bat");
            Console.WriteLine("menu       : dynamic={0} staticVerbs={1}  \"{2}\"",
                settings.MenuDynamic ? "on" : "off", settings.MenuStaticVerbs ? "on" : "off",
                settings.MenuViewLabel);
            Console.WriteLine("obfuscate  : menu={0} keepmeta={1} preview={2} keywords=[{3}]",
                settings.MenuObfuscate ? "on" : "off", settings.ObfuscateKeepMeta ? "on" : "off",
                settings.ObfuscatePreview ? "on" : "off",
                string.Join(", ", settings.ObfuscateKeywords));
            Console.WriteLine("menu clsid : {0}", WorkflowContextMenu.Clsid);
            Console.WriteLine("menu exe   : {0}{1}", MenuExe(), File.Exists(MenuExe()) ? "" : "  (MISSING)");
            Console.WriteLine();
            Console.WriteLine("{0,-8} {1,-9} {2}", "ext", "state", "handler currently resolved by the shell");
            var rows = new List<string>();
            foreach (var extension in settings.ImageExtensions) rows.Add(extension);
            foreach (var extension in settings.VideoExtensions) rows.Add(extension);
            int ours = 0;
            foreach (var extension in rows)
            {
                var current = AssocResolver.Resolve(extension);
                bool mine = current != null &&
                            (current.Equals(ImageThumbnailProvider.Clsid, StringComparison.OrdinalIgnoreCase) ||
                             current.Equals(VideoThumbnailProvider.Clsid, StringComparison.OrdinalIgnoreCase));
                if (mine) ours++;
                Console.WriteLine("{0,-8} {1,-9} {2}", extension, mine ? "ComfyShell" : "original",
                    AssocResolver.Describe(current));
                var saved = OriginalProviders.ReadSaved(extension);
                if (saved != Guid.Empty)
                    Console.WriteLine("{0,-8} {1,-9} {2}", "", "saved",
                        AssocResolver.Describe("{" + saved.ToString().ToUpperInvariant() + "}"));
            }
            Console.WriteLine();
            Console.WriteLine(ours == 0
                ? "not installed: run install.bat as administrator to take over these file types"
                : ours + " of " + rows.Count + " extension(s) are handled by ComfyShellExt");
            if (args.Has("plan"))
            {
                Console.WriteLine();
                Console.WriteLine("install candidates per extension, tried in this order and stopping");
                Console.WriteLine("at the first key that makes the shell resolve to ComfyShellExt:");
                foreach (var line in Registrar.Plan(typeof(ImageThumbnailProvider), MediaKind.Image))
                    Console.WriteLine("  " + line);
                foreach (var line in Registrar.Plan(typeof(VideoThumbnailProvider), MediaKind.Video))
                    Console.WriteLine("  " + line);
                Console.WriteLine();
                Console.WriteLine("right click entries install would create:");
                foreach (var line in MenuRegistrar.Plan(typeof(WorkflowContextMenu)))
                    Console.WriteLine("  " + line);
            }
            return 0;
        }

        private static string MenuExe()
        {
            return Path.Combine(Paths.AppDir, "ComfyWorkflowMenu.exe");
        }

        private static string FfmpegPath(Settings settings)
        {
            return FfmpegFrameGrabber.Locate(settings.FfmpegPath) ??
                   "not found (video fallback unavailable)";
        }
    }
}
