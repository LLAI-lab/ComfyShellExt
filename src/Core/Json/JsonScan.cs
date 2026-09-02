using System;

namespace ComfyShellExt.Core.Json
{
    /// <summary>Cheap text level tests that recognise ComfyUI JSON without a full parse.</summary>
    public static class JsonScan
    {
        public const string PromptMarker = "\"class_type\"";
        public const string GraphMarker = "\"last_node_id\"";
        public const string GraphMarkerAlt = "\"last_link_id\"";
        public const string GraphMarkerWidgets = "\"widgets_values\"";

        /// <summary>Every substring that identifies a ComfyUI payload, for brute force searching.</summary>
        public static readonly string[] Markers =
        {
            PromptMarker, GraphMarker, GraphMarkerAlt, GraphMarkerWidgets
        };

        /// <summary>True when the text carries a ComfyUI signature, valid JSON or not.</summary>
        public static bool HasMarker(string text)
        {
            return Sniff(text) != WorkflowKind.None;
        }

        public static WorkflowKind Sniff(string text)
        {
            if (string.IsNullOrEmpty(text)) return WorkflowKind.None;
            if (text.IndexOf(PromptMarker, StringComparison.Ordinal) >= 0) return WorkflowKind.Prompt;
            if (text.IndexOf(GraphMarker, StringComparison.Ordinal) >= 0 ||
                text.IndexOf(GraphMarkerAlt, StringComparison.Ordinal) >= 0 ||
                text.IndexOf(GraphMarkerWidgets, StringComparison.Ordinal) >= 0) return WorkflowKind.Graph;
            if (text.IndexOf("\"nodes\"", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("\"links\"", StringComparison.Ordinal) >= 0 &&
                text.IndexOf("\"type\"", StringComparison.Ordinal) >= 0) return WorkflowKind.Graph;
            return WorkflowKind.None;
        }

        /// <summary>
        /// ComfyUI writes WebP and video metadata as "workflow:{...}" or "prompt:{...}", and some
        /// containers pad values with NULs. Returns the JSON body, or null when there is none.
        /// </summary>
        public static string Unwrap(string value)
        {
            int start;
            return Unwrap(value, out start);
        }

        public static string Unwrap(string value, out int start)
        {
            start = -1;
            if (string.IsNullOrEmpty(value)) return null;
            int brace = -1;
            int limit = Math.Min(value.Length, PrefixLimit);
            for (int i = 0; i < limit; i++)
            {
                char c = value[i];
                if (c == '{' || c == '[') { brace = i; break; }
            }
            if (brace < 0) return null;
            if (brace > 0 && !IsLabel(value, brace)) return null;
            start = brace;
            return ExtractBalanced(value, brace);
        }

        /// <summary>How far into a value we look for the opening brace of an embedded document.</summary>
        private const int PrefixLimit = 4096;

        /// <summary>
        /// Best effort recovery for a payload that is not balanced, which happens when a container
        /// truncated the value. Returns the text from the first brace to the last closing brace so
        /// the content is still viewable, even though it will not parse.
        /// </summary>
        public static string Salvage(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            int start = -1;
            int limit = Math.Min(value.Length, PrefixLimit);
            for (int i = 0; i < limit; i++)
                if (value[i] == '{' || value[i] == '[') { start = i; break; }
            if (start < 0) return null;
            int end = value.LastIndexOfAny(new[] { '}', ']' });
            if (end <= start) end = value.Length - 1;
            return value.Substring(start, end - start + 1);
        }

        /// <summary>
        /// True when the text before the brace is a short label rather than data. Deliberately
        /// permissive: the caller has already confirmed a ComfyUI signature, so the prefix only has
        /// to be plausible.
        /// </summary>
        private static bool IsLabel(string value, int brace)
        {
            if (brace > 256) return false;
            for (int i = 0; i < brace; i++)
                if (value[i] == '}' || value[i] == ']') return false;
            return true;
        }

        /// <summary>
        /// Cuts out the balanced object or array that starts at <paramref name="start"/>, ignoring
        /// braces that appear inside string literals. Returns null when the text is truncated.
        /// </summary>
        public static string ExtractBalanced(string text, int start)
        {
            if (text == null || start < 0 || start >= text.Length) return null;
            char open = text[start];
            char close = open == '{' ? '}' : open == '[' ? ']' : '\0';
            if (close == '\0') return null;
            int depth = 0;
            bool inString = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 0)
                        return c == close ? text.Substring(start, i - start + 1) : null;
                    if (depth < 0) return null;
                }
            }
            return null;
        }

        /// <summary>
        /// Locates the JSON document that contains the marker at <paramref name="marker"/> inside a
        /// larger blob. Walks back to the nearest control character, which is how JSON payloads are
        /// delimited inside binary containers, then balances forward.
        /// </summary>
        public static bool TryExtractAround(string text, int marker, int backLimit, out string json)
        {
            json = null;
            if (text == null || marker < 0 || marker >= text.Length) return false;
            int lo = Math.Max(0, marker - backLimit);
            int b = marker;
            while (b > lo)
            {
                char c = text[b - 1];
                if (c < 0x09 || (c > 0x0D && c < 0x20)) break;
                b--;
            }
            int start = -1;
            for (int i = b; i <= marker; i++)
            {
                if (text[i] == '{' || text[i] == '[') { start = i; break; }
            }
            if (start < 0) return false;
            var body = ExtractBalanced(text, start);
            if (body == null || start + body.Length <= marker) return false;
            json = body;
            return true;
        }
    }
}
