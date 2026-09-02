using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using ComfyShellExt.Core.Util;

namespace ComfyShellExt.Shell
{
    /// <summary>
    /// Frame grabber used when Windows itself cannot thumbnail a video. That is common for the webm
    /// and vp9 files VideoHelperSuite writes, so ComfyUI users normally have ffmpeg available.
    /// </summary>
    public static class FfmpegFrameGrabber
    {
        private static string _resolved;
        private static bool _searched;

        public static Bitmap Grab(string path, int cx, Settings settings)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var exe = Locate(settings.FfmpegPath);
            if (exe == null) return null;
            var temp = Path.Combine(Path.GetTempPath(),
                "comfyshellext_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                if (!Extract(exe, path, cx, settings.VideoSeekSeconds, settings.VideoTimeoutMs, temp) &&
                    !Extract(exe, path, cx, 0, settings.VideoTimeoutMs, temp))
                    return null;
                using (var fs = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var image = Image.FromStream(fs, false, false))
                    return new Bitmap(image);
            }
            catch (Exception ex)
            {
                Log.Error("ffmpeg grab " + path, ex);
                return null;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private static bool Extract(string exe, string path, int cx, double seek, int timeoutMs, string temp)
        {
            var scale = string.Format(CultureInfo.InvariantCulture,
                "scale=w='min({0},iw)':h='min({0},ih)':force_original_aspect_ratio=decrease:flags=lanczos", cx);
            var args = string.Format(CultureInfo.InvariantCulture,
                "-hide_banner -loglevel error -nostdin -y -ss {0} -i \"{1}\" -frames:v 1 -vf \"{2}\" " +
                "-f image2 -c:v png \"{3}\"", seek.ToString("0.###", CultureInfo.InvariantCulture),
                path, scale, temp);
            try
            {
                var info = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var process = Process.Start(info))
                {
                    if (process == null) return false;
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(Math.Max(1000, timeoutMs)))
                    {
                        try { process.Kill(); } catch { }
                        Log.Write("ffmpeg timed out for {0}", path);
                        return false;
                    }
                    if (process.ExitCode != 0) return false;
                }
                var info2 = new FileInfo(temp);
                return info2.Exists && info2.Length > 0;
            }
            catch (Exception ex)
            {
                Log.Error("ffmpeg run", ex);
                return false;
            }
        }

        public static string Locate(string configured)
        {
            if (!string.IsNullOrEmpty(configured))
            {
                var direct = configured;
                if (Directory.Exists(direct)) direct = Path.Combine(direct, "ffmpeg.exe");
                if (File.Exists(direct)) return direct;
            }
            if (_searched) return _resolved;
            _searched = true;
            foreach (var candidate in Candidates())
            {
                try { if (File.Exists(candidate)) { _resolved = candidate; break; } }
                catch { }
            }
            Log.Write("ffmpeg resolved to {0}", _resolved ?? "(not found)");
            return _resolved;
        }

        private static System.Collections.Generic.IEnumerable<string> Candidates()
        {
            var app = Paths.AppDir;
            yield return Path.Combine(app, "ffmpeg.exe");
            yield return Path.Combine(app, "bin", "ffmpeg.exe");
            yield return Path.Combine(app, "ffmpeg", "bin", "ffmpeg.exe");
            var env = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in env.Split(';'))
            {
                if (dir.Trim().Length == 0) continue;
                string combined = null;
                try { combined = Path.Combine(dir.Trim(), "ffmpeg.exe"); } catch { }
                if (combined != null) yield return combined;
            }
        }
    }
}
