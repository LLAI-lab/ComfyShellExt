using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ComfyShellExt.Core.Util
{
    /// <summary>ComfyShellExt.ini, reloaded automatically when the file changes.</summary>
    public sealed class Settings
    {
        private static readonly object Gate = new object();
        private static Settings _cached;
        private static DateTime _stamp;
        private static string _stampPath;

        public string BadgeText = "JSON";
        /// <summary>
        /// Nine anchors: TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight,
        /// BottomLeft, BottomCenter, BottomRight. An axis with no keyword is centred on that axis.
        /// Top right by default: Explorer paints the default player's icon over the bottom right
        /// corner of a video thumbnail, which would bury a badge placed there.
        /// </summary>
        public string BadgePosition = "TopRight";
        /// <summary>
        /// Anchor used when the badge lands on a file type icon instead of a real thumbnail. Icons
        /// usually carry a format ribbon in the bottom right, so the mark goes elsewhere.
        /// </summary>
        public string BadgeIconPosition = "TopRight";
        /// <summary>Badge size as a percentage of the automatic size. 100 = unchanged.</summary>
        public double BadgeScale = 100;
        /// <summary>Distance from the edges, as a percentage of the thumbnail's long edge.</summary>
        public double BadgeMargin = 3;
        /// <summary>Fine nudge in percent of the long edge; positive moves right / down.</summary>
        public double BadgeOffsetX;
        public double BadgeOffsetY;
        /// <summary>Thumbnails whose long edge is below this get a plain chip instead of readable text.</summary>
        public int BadgeTextMinPx = 88;
        /// <summary>Do not badge thumbnails smaller than this at all (0 = always badge).</summary>
        public int BadgeMinPx = 0;
        public string BadgeFill = "E6111827";
        public string BadgeBorder = "FF22D3EE";
        public string BadgeTextColor = "FFFFFFFF";
        public bool BadgeShowPromptOnly = true;
        /// <summary>Draw a neutral tile when no handler can produce a frame but a workflow was found.</summary>
        public bool BadgePlaceholder = true;
        /// <summary>
        /// When nothing can produce a frame, badge the file type icon Explorer would otherwise show.
        /// This is what makes a workflow visible on video Windows cannot decode.
        /// </summary>
        public bool BadgeOnIconFallback = true;

        public bool EnableRawScan = true;
        public int MaxRawScanMB = 8;

        public string FfmpegPath = "";
        public double VideoSeekSeconds = 1.0;
        public int VideoTimeoutMs = 10000;
        public bool EnableFfmpegFallback = true;
        /// <summary>
        /// Video needs a file path, which the isolated thumbnail host cannot provide, so the class
        /// must opt out of process isolation. Turning this off stops video badges from working.
        /// </summary>
        public bool VideoDisableProcessIsolation = true;

        public bool LogEnabled;
        public string LogPath = "";

        public string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".avif" };
        public string[] VideoExtensions = { ".mp4", ".webm", ".mkv", ".mov", ".avi" };

        /// <summary>Content aware handler: the entries only appear when a workflow was found.</summary>
        public bool MenuDynamic = true;
        /// <summary>
        /// Static registry verbs. These reach the Windows 11 top level context menu, which legacy
        /// handlers cannot, but they show for every file of the type regardless of content.
        /// </summary>
        public bool MenuStaticVerbs;
        public string MenuViewLabel = "查看 ComfyUI 工作流";
        public string MenuExportLabel = "导出工作流 JSON";

        public static Settings Current
        {
            get
            {
                lock (Gate)
                {
                    var path = Paths.IniPath;
                    DateTime stamp;
                    try { stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue; }
                    catch { stamp = DateTime.MinValue; }
                    if (_cached == null || stamp != _stamp || path != _stampPath)
                    {
                        _cached = Load(path);
                        _stamp = stamp;
                        _stampPath = path;
                        Log.Configure(_cached.LogEnabled,
                            string.IsNullOrEmpty(_cached.LogPath) ? Paths.DefaultLogPath : _cached.LogPath);
                    }
                    return _cached;
                }
            }
        }

        public static void Invalidate()
        {
            lock (Gate) { _cached = null; }
        }

        private static Settings Load(string path)
        {
            var s = new Settings();
            var map = ReadIni(path);
            s.BadgeText = Str(map, "badge.text", s.BadgeText);
            s.BadgePosition = Str(map, "badge.position", Str(map, "badge.corner", s.BadgePosition));
            s.BadgeIconPosition = Str(map, "badge.iconposition",
                Str(map, "badge.iconcorner", s.BadgeIconPosition));
            s.BadgeScale = Dbl(map, "badge.scale", s.BadgeScale);
            s.BadgeMargin = Dbl(map, "badge.margin", s.BadgeMargin);
            s.BadgeOffsetX = Dbl(map, "badge.offsetx", s.BadgeOffsetX);
            s.BadgeOffsetY = Dbl(map, "badge.offsety", s.BadgeOffsetY);
            s.BadgeTextMinPx = Int(map, "badge.textminpx", s.BadgeTextMinPx);
            s.BadgeMinPx = Int(map, "badge.minpx", s.BadgeMinPx);
            s.BadgeFill = Str(map, "badge.fill", s.BadgeFill);
            s.BadgeBorder = Str(map, "badge.border", s.BadgeBorder);
            s.BadgeTextColor = Str(map, "badge.textcolor", s.BadgeTextColor);
            s.BadgeShowPromptOnly = Bool(map, "badge.showpromptonly", s.BadgeShowPromptOnly);
            s.BadgePlaceholder = Bool(map, "badge.placeholder", s.BadgePlaceholder);
            s.BadgeOnIconFallback = Bool(map, "badge.oniconfallback", s.BadgeOnIconFallback);
            s.EnableRawScan = Bool(map, "detect.enablerawscan", s.EnableRawScan);
            s.MaxRawScanMB = Int(map, "detect.maxrawscanmb", s.MaxRawScanMB);
            s.FfmpegPath = Str(map, "video.ffmpegpath", s.FfmpegPath);
            s.VideoSeekSeconds = Dbl(map, "video.seekseconds", s.VideoSeekSeconds);
            s.VideoTimeoutMs = Int(map, "video.timeoutms", s.VideoTimeoutMs);
            s.EnableFfmpegFallback = Bool(map, "video.enableffmpegfallback", s.EnableFfmpegFallback);
            s.VideoDisableProcessIsolation =
                Bool(map, "video.disableprocessisolation", s.VideoDisableProcessIsolation);
            s.LogEnabled = Bool(map, "log.enabled", s.LogEnabled);
            s.LogPath = Str(map, "log.path", s.LogPath);
            s.ImageExtensions = Exts(map, "extensions.image", s.ImageExtensions);
            s.VideoExtensions = Exts(map, "extensions.video", s.VideoExtensions);
            s.MenuDynamic = Bool(map, "menu.dynamic", s.MenuDynamic);
            s.MenuStaticVerbs = Bool(map, "menu.staticverbs", s.MenuStaticVerbs);
            s.MenuViewLabel = Str(map, "menu.viewlabel", s.MenuViewLabel);
            s.MenuExportLabel = Str(map, "menu.exportlabel", s.MenuExportLabel);
            return s;
        }

        private static Dictionary<string, string> ReadIni(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return map;
                string section = "";
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = line.Substring(0, eq).Trim();
                    var value = line.Substring(eq + 1).Trim();
                    map[section + "." + key] = value;
                }
            }
            catch { }
            return map;
        }

        private static string Str(Dictionary<string, string> m, string k, string dflt)
        {
            string v;
            return m.TryGetValue(k, out v) && v.Length > 0 ? v : dflt;
        }

        private static int Int(Dictionary<string, string> m, string k, int dflt)
        {
            string v; int r;
            return m.TryGetValue(k, out v) && int.TryParse(v, out r) ? r : dflt;
        }

        private static double Dbl(Dictionary<string, string> m, string k, double dflt)
        {
            string v; double r;
            return m.TryGetValue(k, out v) &&
                   double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : dflt;
        }

        private static bool Bool(Dictionary<string, string> m, string k, bool dflt)
        {
            string v;
            if (!m.TryGetValue(k, out v)) return dflt;
            v = v.Trim();
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        private static string[] Exts(Dictionary<string, string> m, string k, string[] dflt)
        {
            string v;
            if (!m.TryGetValue(k, out v)) return dflt;
            var list = new List<string>();
            foreach (var part in v.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var e = part.Trim().ToLowerInvariant();
                if (e.Length == 0) continue;
                if (e[0] != '.') e = "." + e;
                if (!list.Contains(e)) list.Add(e);
            }
            return list.Count == 0 ? dflt : list.ToArray();
        }
    }
}
