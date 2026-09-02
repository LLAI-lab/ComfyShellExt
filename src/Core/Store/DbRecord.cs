using System;
using System.Collections.Generic;
using System.Text;
using ComfyShellExt.Core.Json;

namespace ComfyShellExt.Core.Store
{
    /// <summary>One line of index.jsonl: what was found in a single media file.</summary>
    public sealed class DbRecord
    {
        public string Path;
        public long Size;
        public long MTime;
        public string Container;
        public bool HasWorkflow;
        /// <summary>Object store hash of the editor graph JSON.</summary>
        public string WorkflowRef;
        /// <summary>Object store hash of the API prompt JSON.</summary>
        public string PromptRef;
        public int NodeCount;
        public List<string> NodeTypes;
        public List<string> Models;
        public List<string> Texts;
        public string Source;
        public long ScannedAt;

        public string ToJson()
        {
            var sb = new StringBuilder(256);
            bool first = true;
            sb.Append('{');
            JsonText.Member(sb, ref first, "p", Path);
            JsonText.Member(sb, ref first, "s", Size);
            JsonText.Member(sb, ref first, "m", MTime);
            JsonText.Member(sb, ref first, "c", Container);
            JsonText.Member(sb, ref first, "w", HasWorkflow);
            JsonText.Member(sb, ref first, "wf", WorkflowRef);
            JsonText.Member(sb, ref first, "pr", PromptRef);
            if (NodeCount > 0) JsonText.Member(sb, ref first, "n", NodeCount);
            JsonText.Member(sb, ref first, "nt", NodeTypes);
            JsonText.Member(sb, ref first, "md", Models);
            JsonText.Member(sb, ref first, "tx", Texts);
            JsonText.Member(sb, ref first, "src", Source);
            JsonText.Member(sb, ref first, "t", ScannedAt);
            sb.Append('}');
            return sb.ToString();
        }

        public static DbRecord FromJson(string line)
        {
            object parsed;
            if (!MiniJson.TryParse(line, out parsed)) return null;
            var map = parsed as Dictionary<string, object>;
            if (map == null) return null;
            var path = JsonText.Str(map, "p");
            if (string.IsNullOrEmpty(path)) return null;
            return new DbRecord
            {
                Path = path,
                Size = JsonText.Num(map, "s"),
                MTime = JsonText.Num(map, "m"),
                Container = JsonText.Str(map, "c"),
                HasWorkflow = JsonText.Flag(map, "w"),
                WorkflowRef = JsonText.Str(map, "wf"),
                PromptRef = JsonText.Str(map, "pr"),
                NodeCount = (int)JsonText.Num(map, "n"),
                NodeTypes = JsonText.Array(map, "nt"),
                Models = JsonText.Array(map, "md"),
                Texts = JsonText.Array(map, "tx"),
                Source = JsonText.Str(map, "src"),
                ScannedAt = JsonText.Num(map, "t")
            };
        }

        public static long ToUnix(DateTime utc)
        {
            return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}
