using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ComfyShellExt.Core;

namespace ComfyShellExt.Menu
{
    /// <summary>Builds a self contained HTML page for the "view workflow" command.</summary>
    internal static partial class HtmlReport
    {
        public static string Build(string path, WorkflowInfo info, WorkflowFacts facts)
        {
            var name = Path.GetFileName(path);
            var sb = new StringBuilder(64 * 1024);
            sb.Append("<!DOCTYPE html>\n<html lang=\"zh-CN\">\n<head>\n<meta charset=\"utf-8\">\n");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n<title>");
            sb.Append(E(name)).Append(" - ComfyUI 工作流</title>\n<style>\n").Append(Css).Append("\n</style>\n</head>\n<body>\n");

            sb.Append("<header>\n<h1>").Append(E(name)).Append("</h1>\n<div class=\"path\">")
              .Append(E(path)).Append("</div>\n<div class=\"chips\">");
            Chip(sb, "容器", info.Container);
            Chip(sb, "来源", info.Source);
            if (info.NodeCount > 0) Chip(sb, "节点", info.NodeCount.ToString());
            Chip(sb, "格式", (info.WorkflowJson != null ? "workflow" : "") +
                             (info.WorkflowJson != null && info.PromptJson != null ? " + " : "") +
                             (info.PromptJson != null ? "prompt" : ""));
            sb.Append("</div>\n</header>\n<main>\n");

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

            sb.Append("<section class=\"card json\"><div class=\"jsonbar\"><h2>JSON</h2><div class=\"tabs\">");
            if (info.WorkflowJson != null)
                sb.Append("<button data-key=\"wf\" class=\"on\">workflow（编辑器图，可拖回 ComfyUI）</button>");
            if (info.PromptJson != null)
                sb.Append("<button data-key=\"pr\"").Append(info.WorkflowJson == null ? " class=\"on\"" : "")
                  .Append(">prompt（API 格式）</button>");
            sb.Append("</div><div class=\"actions\"><button id=\"copy\">复制</button>")
              .Append("<a id=\"save\" href=\"#\">另存为 .json</a></div></div><pre id=\"out\"></pre></section>\n");

            sb.Append("</main>\n");
            Data(sb, "wf", info.WorkflowJson);
            Data(sb, "pr", info.PromptJson);
            sb.Append("<script id=\"stem\" type=\"text/plain\">")
              .Append(E(Path.GetFileNameWithoutExtension(name))).Append("</script>\n");
            sb.Append("<script>\n").Append(Script).Append("\n</script>\n</body>\n</html>\n");
            return sb.ToString();
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

        /// <summary>
        /// The payload lives in a non executed script block. Escaping "&lt;" is enough to make
        /// "&lt;/script" impossible, and inside valid JSON that character only occurs in strings.
        /// </summary>
        private static void Data(StringBuilder sb, string key, string json)
        {
            if (json == null) return;
            sb.Append("<script id=\"data-").Append(key).Append("\" type=\"application/json\">")
              .Append(json.Replace("<", "\\u003c")).Append("</script>\n");
        }

        private static string E(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
