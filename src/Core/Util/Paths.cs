using System;
using System.IO;
using System.Reflection;

namespace ComfyShellExt.Core.Util
{
    public static class Paths
    {
        /// <summary>Folder that holds ComfyShellExt.dll, the ini and the optional ffmpeg.exe.</summary>
        public static string AppDir
        {
            get
            {
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var loc = asm.Location;
                    if (string.IsNullOrEmpty(loc) && !string.IsNullOrEmpty(asm.CodeBase))
                        loc = new Uri(asm.CodeBase).LocalPath;
                    var dir = Path.GetDirectoryName(loc);
                    if (!string.IsNullOrEmpty(dir)) return dir;
                }
                catch { }
                return Environment.CurrentDirectory;
            }
        }

        /// <summary>Per user state: log file and the default workflow database.</summary>
        public static string DataDir
        {
            get
            {
                try
                {
                    var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    return Path.Combine(root, "ComfyShellExt");
                }
                catch { return AppDir; }
            }
        }

        public static string IniPath { get { return Path.Combine(AppDir, "ComfyShellExt.ini"); } }

        public static string DefaultDbDir { get { return Path.Combine(DataDir, "db"); } }

        public static string DefaultLogPath { get { return Path.Combine(DataDir, "ComfyShellExt.log"); } }

        public static void EnsureDir(string dir)
        {
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }
}
