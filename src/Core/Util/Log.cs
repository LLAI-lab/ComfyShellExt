using System;
using System.IO;
using System.Text;

namespace ComfyShellExt.Core.Util
{
    /// <summary>
    /// Optional diagnostic log. Disabled unless the ini turns it on, because this code runs
    /// inside the Explorer thumbnail host where an unexpected exception is expensive.
    /// </summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;
        private static bool _enabled;

        public static bool Enabled { get { return _enabled; } }

        public static void Configure(bool enabled, string path)
        {
            _enabled = enabled;
            _path = path;
        }

        public static void Write(string format, params object[] args)
        {
            if (!_enabled) return;
            string text;
            try { text = args == null || args.Length == 0 ? format : string.Format(format, args); }
            catch { text = format; }
            Emit(text);
        }

        public static void Error(string context, Exception ex)
        {
            if (!_enabled) return;
            Emit(context + " FAILED: " + (ex == null ? "(null)" : ex.GetType().Name + ": " + ex.Message));
        }

        private static void Emit(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(_path)) return;
                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [").Append(System.Diagnostics.Process.GetCurrentProcess().Id)
                    .Append('/').Append(System.Threading.Thread.CurrentThread.ManagedThreadId).Append("] ")
                    .Append(text).Append(Environment.NewLine);
                lock (Gate)
                {
                    var dir = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.AppendAllText(_path, line.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // logging must never break a thumbnail request
            }
        }
    }
}
