using System;
using System.Collections.Generic;
using System.IO;
using ComfyShellExt.Core.Store;

namespace ComfyShellExt.Cli
{
    /// <summary>Reads the database: list, full text search, workflow export and statistics.</summary>
    internal static class QueryCommands
    {
        private static WorkflowIndex Open(Args args)
        {
            var index = new WorkflowIndex(args.Str("db"));
            index.Load();
            return index;
        }

        public static int List(Args args)
        {
            var index = Open(args);
            bool wantMissing = args.Has("no-workflow");
            int limit = args.Int("limit", 200);
            int shown = 0;
            foreach (var record in Sorted(index.All()))
            {
                if (record.HasWorkflow == wantMissing) continue;
                if (shown++ >= limit) break;
                Print(record, args.Has("paths-only"));
            }
            Console.Error.WriteLine("-- {0} shown of {1} indexed", shown, index.Count);
            return shown > 0 ? 0 : 1;
        }

        public static int Find(Args args)
        {
            var index = Open(args);
            var text = args.Values.Count > 0 ? args.Values[0] : null;
            var node = args.Str("node");
            var model = args.Str("model");
            int limit = args.Int("limit", 200);
            if (text == null && node == null && model == null)
            {
                Console.Error.WriteLine("usage: ComfyWorkflowDb find <text> [--node class] [--model name]");
                return 2;
            }
            int shown = 0;
            foreach (var record in Sorted(index.All()))
            {
                if (!record.HasWorkflow) continue;
                if (node != null && !ContainsPart(record.NodeTypes, node)) continue;
                if (model != null && !ContainsPart(record.Models, model)) continue;
                if (text != null && !Matches(record, text)) continue;
                if (shown++ >= limit) break;
                Print(record, args.Has("paths-only"));
            }
            Console.Error.WriteLine("-- {0} match(es) of {1} indexed", shown, index.Count);
            return shown > 0 ? 0 : 1;
        }

        public static int Get(Args args)
        {
            if (args.Values.Count == 0)
            {
                Console.Error.WriteLine("usage: ComfyWorkflowDb get <file> [--graph|--prompt] [-o out.json]");
                return 2;
            }
            var index = Open(args);
            var path = Path.GetFullPath(args.Values[0]);
            var record = index.Get(path);
            if (record == null)
            {
                Console.Error.WriteLine("not in the database: " + path);
                Console.Error.WriteLine("run: ComfyWorkflowDb scan \"" + Path.GetDirectoryName(path) + "\"");
                return 1;
            }
            bool wantPrompt = args.Has("prompt");
            var hash = wantPrompt ? record.PromptRef : record.WorkflowRef ?? record.PromptRef;
            var json = index.GetObject(hash);
            if (json == null)
            {
                Console.Error.WriteLine("no stored JSON for " + path);
                return 1;
            }
            var output = args.Str("o") ?? args.Str("out");
            if (output == null) Console.WriteLine(json);
            else
            {
                File.WriteAllText(output, json, new System.Text.UTF8Encoding(false));
                Console.Error.WriteLine("wrote {0} ({1} chars)", output, json.Length);
            }
            return 0;
        }

        public static int Stats(Args args)
        {
            var index = Open(args);
            var records = index.All();
            int withWorkflow = 0;
            long bytes = 0;
            var containers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var nodes = new Dictionary<string, int>(StringComparer.Ordinal);
            var models = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var graphs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                bytes += record.Size;
                Bump(containers, record.Container ?? "unknown");
                if (!record.HasWorkflow) continue;
                withWorkflow++;
                if (record.WorkflowRef != null) graphs.Add(record.WorkflowRef);
                if (record.NodeTypes != null) foreach (var n in record.NodeTypes) Bump(nodes, n);
                if (record.Models != null) foreach (var m in record.Models) Bump(models, m);
            }
            Console.WriteLine("database   : {0}", index.Dir);
            Console.WriteLine("files      : {0} indexed, {1} with a workflow", records.Count, withWorkflow);
            Console.WriteLine("media size : {0:0.0} MB", bytes / 1048576.0);
            Console.WriteLine("unique graphs: {0}", graphs.Count);
            Console.WriteLine("containers : {0}", TopList(containers, 12));
            Console.WriteLine("top nodes  : {0}", TopList(nodes, 12));
            Console.WriteLine("top models : {0}", TopList(models, 10));
            return 0;
        }

        private static List<DbRecord> Sorted(List<DbRecord> records)
        {
            records.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
            return records;
        }

        private static void Print(DbRecord record, bool pathsOnly)
        {
            if (pathsOnly) { Console.WriteLine(record.Path); return; }
            Console.WriteLine("{0}", record.Path);
            Console.WriteLine("    {0}  nodes={1}  src={2}", record.Container, record.NodeCount,
                record.Source ?? "-");
            if (record.Models != null) Console.WriteLine("    models: {0}", string.Join(", ", record.Models));
            if (record.Texts != null && record.Texts.Count > 0)
                Console.WriteLine("    text  : {0}", Shorten(record.Texts[0]));
        }

        private static bool Matches(DbRecord record, string text)
        {
            if (record.Path.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (ContainsPart(record.Texts, text)) return true;
            if (ContainsPart(record.NodeTypes, text)) return true;
            return ContainsPart(record.Models, text);
        }

        private static bool ContainsPart(List<string> values, string needle)
        {
            if (values == null) return false;
            foreach (var v in values)
                if (v.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static void Bump(Dictionary<string, int> counter, string key)
        {
            int n;
            counter[key] = counter.TryGetValue(key, out n) ? n + 1 : 1;
        }

        private static string TopList(Dictionary<string, int> counter, int max)
        {
            var items = new List<KeyValuePair<string, int>>(counter);
            items.Sort((a, b) => b.Value.CompareTo(a.Value));
            var parts = new List<string>();
            for (int i = 0; i < items.Count && i < max; i++)
                parts.Add(items[i].Key + "=" + items[i].Value);
            return parts.Count == 0 ? "-" : string.Join(", ", parts);
        }

        private static string Shorten(string value)
        {
            value = value.Replace("\r", " ").Replace("\n", " ");
            return value.Length > 90 ? value.Substring(0, 87) + "..." : value;
        }
    }
}
