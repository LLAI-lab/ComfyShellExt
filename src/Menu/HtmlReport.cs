using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ComfyShellExt.Core;

namespace ComfyShellExt.Menu
{
    /// <summary>One inspected file and its extracted facts, ready for the page builder.</summary>
    internal sealed class FileReport
    {
        public string Path;
        public WorkflowInfo Info;
        public WorkflowFacts Facts;
        public string Generator
        {
            get { return Info == null ? null : (Info.Generator ?? "未知 Unknown"); }
        }
    }

    /// <summary>
    /// Builds a self contained HTML page for the right click "查看 AI 生图信息" command. One file
    /// renders as a single report; a selection renders one section per file on the same page.
    /// </summary>
    internal static partial class HtmlReport
    {
        private const int MetaPreviewChars = 800;

        public static string BuildPage(List<FileReport> reports)
        {
            var sb = new StringBuilder(64 * 1024);
            var first = reports[0];
            string title = reports.Count == 1
                ? Path.GetFileName(first.Path)
                : reports.Count + " 个文件 - AI 生图信息";
            sb.Append("<!DOCTYPE html>\n<html lang=\"zh-CN\">\n<head>\n<meta charset=\"utf-8\">\n");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n<title>");
            sb.Append(E(title)).Append("</title>\n<style>\n").Append(Css).Append("\n</style>\n</head>\n<body>\n");

            if (reports.Count == 1)
            {
                sb.Append("<header>\n<h1>").Append(E(Path.GetFileName(first.Path))).Append("</h1>\n")
                  .Append("<div class=\"path\">").Append(E(first.Path)).Append("</div>\n<div class=\"chips\">");
                Chips(sb, first);
                sb.Append("</div>\n</header>\n<main>\n");
                Cards(sb, first, 0);
            }
            else
            {
                sb.Append("<header>\n<h1>AI 生图信息 · ").Append(reports.Count).Append(" 个文件</h1>\n")
                  .Append("<div class=\"chips\">");
                foreach (var report in reports)
                    sb.Append("<span class=\"chip\"><b>").Append(E(report.Generator)).Append("</b>")
                      .Append(E(Path.GetFileName(report.Path))).Append("</span>");
                sb.Append("</div>\n</header>\n<main>\n");
                for (int i = 0; i < reports.Count; i++)
                {
                    sb.Append("<section class=\"file\"><h1>").Append(E(Path.GetFileName(reports[i].Path)))
                      .Append("</h1>\n<div class=\"path\">").Append(E(reports[i].Path))
                      .Append("</div>\n<div class=\"chips\">");
                    Chips(sb, reports[i]);
                    sb.Append("</div>\n");
                    Cards(sb, reports[i], i);
                    sb.Append("</section>\n");
                }
            }

            sb.Append("</main>\n");
            sb.Append("<script>\n").Append(Script).Append("\n</script>\n</body>\n</html>\n");
            return sb.ToString();
        }

        private static void Chips(StringBuilder sb, FileReport report)
        {
            var info = report.Info;
            Chip(sb, "生成工具", report.Generator);
            Chip(sb, "容器", info.Container);
            Chip(sb, "来源", info.Source);
            if (info.NodeCount > 0) Chip(sb, "节点", info.NodeCount.ToString());
            if (info.HasWorkflow)
                Chip(sb, "格式", (info.WorkflowJson != null ? "workflow" : "") +
                                 (info.WorkflowJson != null && info.PromptJson != null ? " + " : "") +
                                 (info.PromptJson != null ? "prompt" : ""));
        }

        /// <summary>The body cards of one file. Every id that JS touches carries the index.</summary>
        private static void Cards(StringBuilder sb, FileReport report, int index)
        {
            var info = report.Info;
            var facts = report.Facts;
            string key = index.ToString();

            if (facts.Items.Count > 0)
            {
                sb.Append("<section class=\"card\"><h2>生成参数</h2><table>\n");
                foreach (var item in facts.Items)
                    sb.Append("<tr><th>").Append(E(item.Key)).Append("</th><td>")
                      .Append(E(item.Value)).Append("</td></tr>\n");
                sb.Append("</table></section>\n");
            }

            if (!string.IsNullOrEmpty(facts.Positive) || !string.IsNullOrEmpty(facts.Negative))
            {
                sb.Append("<section class=\"card prompts\"><h2>提示词</h2>\n");
                Prompt(sb, "正面 Positive", facts.Positive, "pos");
                Prompt(sb, "负面 Negative", facts.Negative, "neg");
                sb.Append("</section>\n");
            }

            if (info.NodeTypes != null && info.NodeTypes.Count > 0)
            {
                sb.Append("<section class=\"card\"><h2>用到的节点</h2><div class=\"tags\">");
                foreach (var nodeType in info.NodeTypes)
                    sb.Append("<span>").Append(E(nodeType)).Append("</span>");
                sb.Append("</div></section>\n");
            }

            JsonCard(sb, report, key);

            if (info.Metadata != null && info.Metadata.Count > 0)
            {
                sb.Append("<section class=\"card\"><details><summary><h2>全部元数据（")
                  .Append(info.Metadata.Count).Append(" 条）</h2></summary><table>\n");
                foreach (var m in info.Metadata)
                {
                    var value = m.Value ?? "";
                    var shown = value.Length > MetaPreviewChars
                        ? value.Substring(0, MetaPreviewChars) + "\n... (" + value.Length + " 字符，已截断)"
                        : value;
                    sb.Append("<tr><th>").Append(E(m.Origin)).Append("</th><td><pre class=\"meta\">")
                      .Append(E(shown)).Append("</pre></td></tr>\n");
                }
                sb.Append("</table></details></section>\n");
            }
        }

        /// <summary>
        /// The JSON tabs for ComfyUI payloads. A non executed script block per tab carries the
        /// payload; escaping "&lt;" is enough to make "&lt;/script" impossible.
        /// </summary>
        private static void JsonCard(StringBuilder sb, FileReport report, string key)
        {
            var info = report.Info;
            if (info.WorkflowJson == null && info.PromptJson == null && info.RawJson == null &&
                info.ToolJson == null) return;
            var stem = Path.GetFileNameWithoutExtension(report.Path);
            sb.Append("<section class=\"card jsoncard\" data-stem=\"").Append(E(stem)).Append("\">\n");
            sb.Append("<div class=\"jsonbar\"><h2>JSON</h2><div class=\"tabs\">");
            int tab = 0;
            Tab(sb, info.WorkflowJson != null, key + "wf", "workflow（编辑器图，可拖回 ComfyUI）", ref tab);
            Tab(sb, info.PromptJson != null, key + "pr", "prompt（API 格式）", ref tab);
            Tab(sb, info.ToolJson != null, key + "tool", "生成工具设置", ref tab);
            Tab(sb, info.RawJson != null, key + "raw", "恢复的原始数据（文件内被截断）", ref tab);
            sb.Append("</div><div class=\"actions\"><button data-act=\"copy\">复制</button>")
              .Append("<a href=\"#\" data-act=\"save\">另存为 .json</a></div></div>")
              .Append("<pre class=\"jsonout\"></pre></section>\n");

            Data(sb, key + "wf", info.WorkflowJson);
            Data(sb, key + "pr", info.PromptJson);
            Data(sb, key + "tool", info.ToolJson);
            Data(sb, key + "raw", info.RawJson);
        }

        private static void Tab(StringBuilder sb, bool present, string dataKey, string label, ref int tab)
        {
            if (!present) return;
            sb.Append("<button data-key=\"").Append(dataKey).Append('"');
            if (tab == 0) sb.Append(" class=\"on\"");
            sb.Append(">").Append(E(label)).Append("</button>");
            tab++;
        }

        private static void Data(StringBuilder sb, string key, string json)
        {
            if (json == null) return;
            sb.Append("<script id=\"data-").Append(key).Append("\" type=\"application/json\">")
              .Append(json.Replace("<", "\\u003c")).Append("</script>\n");
        }

        private static void Chip(StringBuilder sb, string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            sb.Append("<span class=\"chip\"><b>").Append(E(label)).Append("</b>")
              .Append(E(value)).Append("</span>");
        }

        private static void Prompt(StringBuilder sb, string title, string text, string cls)
        {
            if (string.IsNullOrEmpty(text)) return;
            sb.Append("<div class=\"p ").Append(cls).Append("\"><h3>").Append(E(title))
              .Append("</h3><pre>").Append(E(text)).Append("</pre></div>\n");
        }

        private static string E(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
