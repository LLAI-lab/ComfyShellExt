using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ComfyShellExt.Core;
using ComfyShellExt.Core.Util;

namespace ComfyShellExt.Menu
{
    /// <summary>
    /// The process the right click entries start. A windowed application on purpose: launched from
    /// Explorer it must not flash a console, and it reports problems with a message box.
    /// </summary>
    internal static class Program
    {
        private const string Title = "ComfyShellExt";
        private static bool _quiet;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                var files = new List<string>();
                string verb = null;
                string target = null;
                bool saveAs = false, noOpen = false;
                for (int i = 0; i < args.Length; i++)
                {
                    var arg = args[i];
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        var name = arg.Substring(2).ToLowerInvariant();
                        if (name == "saveas") saveAs = true;
                        else if (name == "no-open") noOpen = true;
                        else if (name == "quiet") _quiet = true;
                        else if (name == "to" && i + 1 < args.Length) target = args[++i];
                        continue;
                    }
                    if (verb == null) verb = arg.ToLowerInvariant();
                    else files.Add(arg);
                }
                // Windowed application, so messages normally go to a dialog. --quiet borrows the
                // parent console instead, which makes the commands usable from a script.
                if (_quiet) try { AttachConsole(-1); } catch { }
                if (verb == null || files.Count == 0) return Usage();
                switch (verb)
                {
                    case "view": return View(files[0], noOpen);
                    case "export": return Export(files, target, saveAs, noOpen);
                    default: return Usage();
                }
            }
            catch (Exception ex)
            {
                Show("出错了：" + ex.Message, MessageBoxIcon.Error);
                return 3;
            }
        }

        private static int Usage()
        {
            Show("用法：\n\n" +
                 "ComfyWorkflowMenu.exe view <文件>\n    查看内嵌的 ComfyUI 工作流\n\n" +
                 "ComfyWorkflowMenu.exe export <文件> [...]\n    导出工作流为 .json（默认存到源文件旁边）\n" +
                 "    --saveas    弹出保存对话框\n    --to <路径>  指定输出文件\n",
                MessageBoxIcon.Information);
            return 2;
        }

        private static int View(string path, bool noOpen)
        {
            var info = Inspect(path);
            if (info == null) return 1;
            var facts = WorkflowFacts.From(info);
            var html = HtmlReport.Build(Path.GetFullPath(path), info, facts);
            var dir = Path.Combine(Path.GetTempPath(), "ComfyShellExt");
            Paths.EnsureDir(dir);
            Sweep(dir);
            var file = Path.Combine(dir, Safe(Path.GetFileName(path)) + ".html");
            File.WriteAllText(file, html, new UTF8Encoding(false));
            if (!noOpen) Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            return 0;
        }

        private static int Export(List<string> files, string target, bool saveAs, bool noOpen)
        {
            var written = new List<string>();
            var skipped = new List<string>();
            foreach (var path in files)
            {
                var info = Inspect(path, files.Count == 1);
                if (info == null) { skipped.Add(Path.GetFileName(path)); continue; }
                var json = info.WorkflowJson ?? info.PromptJson;
                var suffix = info.WorkflowJson != null ? ".workflow.json" : ".prompt.json";
                var output = target;
                if (output == null && saveAs && files.Count == 1) output = Ask(path, suffix);
                if (output == null && saveAs) return 1;
                if (output == null)
                    output = Unique(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)),
                        Path.GetFileNameWithoutExtension(path) + suffix));
                File.WriteAllText(output, json, new UTF8Encoding(false));
                written.Add(output);
                target = null;
            }
            if (written.Count == 1 && skipped.Count == 0)
            {
                if (_quiet) Console.WriteLine("exported " + written[0]);
                if (!noOpen) Reveal(written[0]);
                return 0;
            }
            // A single failing file already reported itself; do not say it twice.
            if (written.Count == 0 && files.Count == 1) return 1;
            var message = new StringBuilder();
            if (written.Count > 0)
            {
                message.Append("已导出 ").Append(written.Count).Append(" 个文件：\n");
                for (int i = 0; i < written.Count && i < 12; i++)
                    message.Append("  ").Append(Path.GetFileName(written[i])).Append('\n');
                if (written.Count > 12) message.Append("  ...\n");
            }
            if (skipped.Count > 0)
            {
                message.Append("\n跳过 ").Append(skipped.Count).Append(" 个没有工作流的文件：\n");
                for (int i = 0; i < skipped.Count && i < 8; i++)
                    message.Append("  ").Append(skipped[i]).Append('\n');
            }
            Show(message.ToString(), written.Count > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            if (written.Count > 0 && !noOpen) Reveal(written[0]);
            return written.Count > 0 ? 0 : 1;
        }

        private static WorkflowInfo Inspect(string path, bool complain = true)
        {
            if (!File.Exists(path))
            {
                if (complain) Show("找不到文件：\n" + path, MessageBoxIcon.Error);
                return null;
            }
            var info = WorkflowDetector.InspectFile(path, DetectOptions.Full());
            if (info.HasWorkflow) return info;
            if (complain)
                Show("这个文件里没有找到 ComfyUI 工作流。\n\n" + Path.GetFileName(path) +
                     "\n容器：" + info.Container, MessageBoxIcon.Information);
            return null;
        }

        private static string Ask(string source, string suffix)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "导出 ComfyUI 工作流";
                dialog.Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*";
                dialog.FileName = Path.GetFileNameWithoutExtension(source) + suffix;
                dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(source));
                dialog.OverwritePrompt = true;
                return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
            }
        }

        /// <summary>Never overwrites: adds a numeric suffix when the name is taken.</summary>
        private static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);
            int dot = name.IndexOf('.');
            var stem = dot < 0 ? name : name.Substring(0, dot);
            var rest = dot < 0 ? "" : name.Substring(dot);
            for (int i = 2; i < 1000; i++)
            {
                var candidate = Path.Combine(dir, stem + " (" + i + ")" + rest);
                if (!File.Exists(candidate)) return candidate;
            }
            return path;
        }

        private static void Reveal(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"")
                { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error("reveal", ex); }
        }

        /// <summary>Drops generated pages older than three days.</summary>
        private static void Sweep(string dir)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-3);
                foreach (var file in Directory.GetFiles(dir, "*.html"))
                    if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
            }
            catch { }
        }

        private static string Safe(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name) sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        private static void Show(string message, MessageBoxIcon icon)
        {
            if (_quiet)
            {
                Console.WriteLine("[" + icon + "] " + message);
                return;
            }
            MessageBox.Show(message, Title, MessageBoxButtons.OK, icon);
        }
    }
}
