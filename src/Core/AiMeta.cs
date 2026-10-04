using System;

namespace ComfyShellExt.Core
{
    /// <summary>
    /// Recognises the metadata that AI image tools embed in their output. ComfyUI has its own
    /// detector (the JSON markers in JsonScan); this covers the text/JSON styles written by
    /// Stable Diffusion WebUI and friends: A1111 / Forge "parameters", NovelAI's Software +
    /// Comment pair, SwarmUI and Fooocus JSON inside "parameters", InvokeAI metadata JSON.
    /// Anything unknown still shows up in the viewer's raw metadata list.
    /// </summary>
    public static class AiMeta
    {
        public const string Comfy = "ComfyUI";
        public const string A1111 = "A1111";
        public const string SwarmUI = "SwarmUI";
        public const string Fooocus = "Fooocus";
        public const string InvokeAI = "InvokeAI";
        public const string NovelAI = "NovelAI";

        private const int MaxValueChars = 16 * 1024 * 1024;

        /// <summary>
        /// Sniffs one metadata entry. Returns true when a known generator was recognised, in
        /// which case info.Generator / info.HasAiMeta are set. RawText or ToolJson are only
        /// captured when <paramref name="capture"/> is set, so the fast paths stay cheap.
        /// </summary>
        public static bool Sniff(string key, string value, WorkflowInfo info, bool capture)
        {
            if (value == null || value.Length == 0 || value.Length > MaxValueChars) return false;
            if (info.HasWorkflow) return false;
            string k = key == null ? "" : key.Trim();

            string trimmed = null;
            if (k == "parameters" || k == "UserComment" || k == "ImageDescription")
            {
                trimmed = Trim(value);
                if (LooksLikeJson(trimmed))
                {
                    if (trimmed.IndexOf("sui_image_params", StringComparison.Ordinal) >= 0)
                        return Found(info, SwarmUI, null, trimmed, capture);
                    if (trimmed.IndexOf("\"Prompt\"", StringComparison.Ordinal) >= 0 &&
                        trimmed.IndexOf("\"Settings\"", StringComparison.Ordinal) >= 0)
                        return Found(info, Fooocus, null, trimmed, capture);
                    return false;
                }
                if (IsA1111Text(trimmed)) return Found(info, A1111, trimmed, null, capture);
                if (IsNovelAiJson(trimmed)) return Found(info, NovelAI, null, trimmed, capture);
                return false;
            }
            if (k == "invokeai_metadata")
            {
                trimmed = Trim(value);
                if (LooksLikeJson(trimmed) &&
                    trimmed.IndexOf("\"prompt\"", StringComparison.Ordinal) >= 0)
                    return Found(info, InvokeAI, null, trimmed, capture);
                return false;
            }
            if (k == "Software")
                return value.IndexOf("NovelAI", StringComparison.Ordinal) >= 0 &&
                       Found(info, NovelAI, null, null, capture);
            if (k == "Comment" || k.Length == 0)
            {
                // NovelAI GIFs write one comment block: "Software: NovelAI ...\nComment: {...}"
                if (value.IndexOf("NovelAI", StringComparison.Ordinal) >= 0)
                {
                    trimmed = Trim(value);
                    string json = LooksLikeJson(trimmed) ? trimmed : ExtractJson(trimmed);
                    return Found(info, NovelAI, null, IsNovelAiJson(json) ? json : null, capture);
                }
                trimmed = Trim(value);
                if (IsNovelAiJson(trimmed)) return Found(info, NovelAI, null, trimmed, capture);
                return false;
            }
            return false;
        }

        private static bool Found(WorkflowInfo info, string generator, string rawText,
            string toolJson, bool capture)
        {
            info.Generator = generator;
            if (capture)
            {
                if (rawText != null && info.RawText == null) info.RawText = rawText;
                if (toolJson != null && info.ToolJson == null) info.ToolJson = toolJson;
            }
            return true;
        }

        /// <summary>The A1111 settings footer is a stable signature nobody else produces.</summary>
        private static bool IsA1111Text(string value)
        {
            if (value == null) return false;
            if (value.IndexOf("\nNegative prompt:", StringComparison.Ordinal) >= 0) return true;
            return value.IndexOf("Steps: ", StringComparison.Ordinal) >= 0 &&
                   (value.IndexOf(" Sampler: ", StringComparison.Ordinal) >= 0 ||
                    value.IndexOf(", Sampler: ", StringComparison.Ordinal) >= 0 ||
                    value.IndexOf("CFG scale:", StringComparison.Ordinal) >= 0);
        }

        /// <summary>NovelAI stores its settings as JSON with prompt / uc (ultra negative).</summary>
        private static bool IsNovelAiJson(string value)
        {
            if (value == null || !LooksLikeJson(value)) return false;
            return value.IndexOf("\"prompt\"", StringComparison.Ordinal) >= 0 &&
                   (value.IndexOf("\"uc\"", StringComparison.Ordinal) >= 0 ||
                    value.IndexOf("\"v4_prompt\"", StringComparison.Ordinal) >= 0);
        }

        private static bool LooksLikeJson(string value)
        {
            return value != null && value.Length > 1 && value[0] == '{';
        }

        private static string Trim(string value)
        {
            int start = 0, end = value.Length - 1;
            while (start <= end && (value[start] < 0x20 || value[start] == 0x20)) start++;
            while (end >= start && (value[end] < 0x20 || value[end] == 0x20)) end--;
            return start > end ? "" : value.Substring(start, end - start + 1);
        }

        /// <summary>Finds the first balanced {...} object inside a longer text, or null.</summary>
        private static string ExtractJson(string value)
        {
            if (value == null) return null;
            int start = value.IndexOf('{');
            if (start < 0) return null;
            int depth = 0;
            bool inString = false;
            for (int i = start; i < value.Length; i++)
            {
                char c = value[i];
                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 0) return value.Substring(start, i - start + 1);
                }
            }
            return null;
        }
    }
}
