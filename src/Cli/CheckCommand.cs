using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ComfyShellExt.Core;

namespace ComfyShellExt.Cli
{
    /// <summary>Detection diagnostics: what exactly was found in a file, and a fixture self test.</summary>
    internal static class CheckCommand
    {
        public static int Run(Args args)
        {
            if (args.Values.Count == 0)
            {
                Console.Error.WriteLine("usage: ComfyWorkflowDb check <file> [...] [--json] [--dump]");
                return 2;
            }
            bool asJson = args.Has("json");
            int hits = 0;
            var options = DetectOptions.Full();
            if (args.Has("deep")) options.MaxRawScanBytes = long.MaxValue;
            foreach (var path in Expand(args.Values))
            {
                var info = WorkflowDetector.InspectFile(path, options);
                if (info.HasAiMeta) hits++;
                if (asJson) PrintJson(path, info);
                else Print(path, info, args.Has("dump"));
            }
            return hits > 0 ? 0 : 1;
        }

        private static IEnumerable<string> Expand(List<string> inputs)
        {
            foreach (var input in inputs)
            {
                if (Directory.Exists(input))
                    foreach (var file in Directory.GetFiles(input))
                        yield return file;
                else
                    yield return input;
            }
        }

        private static void Print(string path, WorkflowInfo info, bool dump)
        {
            Console.WriteLine(Path.GetFileName(path));
            Console.WriteLine("  container : {0}", info.Container);
            Console.WriteLine("  workflow  : {0}{1}", info.HasWorkflow ? "YES" : "no",
                info.Source == null ? "" : "  <- " + info.Source);
            if (info.Generator != null && !info.HasWorkflow)
                Console.WriteLine("  generator : {0}", info.Generator);
            if (info.WorkflowJson != null)
                Console.WriteLine("  graph     : {0} chars", info.WorkflowJson.Length);
            if (info.PromptJson != null)
                Console.WriteLine("  prompt    : {0} chars", info.PromptJson.Length);
            if (info.NodeCount > 0) Console.WriteLine("  nodes     : {0}", info.NodeCount);
            if (info.Incomplete)
                Console.WriteLine("  note      : payload truncated in the file, {0} chars recovered",
                    info.RawJson == null ? 0 : info.RawJson.Length);
            if (info.NodeTypes != null)
                Console.WriteLine("  types     : {0}", Join(info.NodeTypes, 8));
            if (info.Models != null) Console.WriteLine("  models    : {0}", Join(info.Models, 6));
            if (info.Texts != null) Console.WriteLine("  texts     : {0}", Join(info.Texts, 3));
            if (info.Metadata != null && info.Metadata.Count > 0)
            {
                var parts = new List<string>();
                foreach (var m in info.Metadata) parts.Add(m.Origin + "(" + m.Value.Length + ")");
                Console.WriteLine("  metadata  : {0}", Join(parts, 10));
            }
            if (info.Error != null) Console.WriteLine("  error     : {0}", info.Error);
            if (dump && info.BestJson != null) Console.WriteLine(info.BestJson);
            Console.WriteLine();
        }

        private static void PrintJson(string path, WorkflowInfo info)
        {
            var sb = new StringBuilder();
            bool first = true;
            sb.Append('{');
            Core.Json.JsonText.Member(sb, ref first, "file", path);
            Core.Json.JsonText.Member(sb, ref first, "container", info.Container);
            Core.Json.JsonText.Member(sb, ref first, "has", info.HasWorkflow);
            Core.Json.JsonText.Member(sb, ref first, "generator", info.Generator);
            Core.Json.JsonText.Member(sb, ref first, "source", info.Source);
            Core.Json.JsonText.Member(sb, ref first, "nodes", info.NodeCount);
            Core.Json.JsonText.Member(sb, ref first, "types", info.NodeTypes);
            Core.Json.JsonText.Member(sb, ref first, "models", info.Models);
            sb.Append('}');
            Console.WriteLine(sb.ToString());
        }

        private static string Join(List<string> values, int max)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Count && i < max; i++)
            {
                if (i > 0) sb.Append(", ");
                var v = values[i].Replace("\r", " ").Replace("\n", " ");
                sb.Append(v.Length > 60 ? v.Substring(0, 57) + "..." : v);
            }
            if (values.Count > max) sb.Append(" (+").Append(values.Count - max).Append(" more)");
            return sb.ToString();
        }

        /// <summary>Compares detection against tests/fixtures/expected.json.</summary>
        public static int SelfTest(Args args)
        {
            var dir = args.Values.Count > 0 ? args.Values[0] : "tests/fixtures";
            var manifest = Path.Combine(dir, "expected.json");
            if (!File.Exists(manifest))
            {
                Console.Error.WriteLine("no expected.json in " + dir);
                return 2;
            }
            object parsed;
            if (!Core.Json.MiniJson.TryParse(File.ReadAllText(manifest), out parsed))
            {
                Console.Error.WriteLine("expected.json is not valid JSON");
                return 2;
            }
            var cases = parsed as List<object>;
            if (cases == null) return 2;
            int pass = 0, fail = 0;
            foreach (var item in cases)
            {
                var map = item as Dictionary<string, object>;
                if (map == null) continue;
                var name = Core.Json.JsonText.Str(map, "file");
                bool want = Core.Json.JsonText.Flag(map, "has");
                var wantContainer = Core.Json.JsonText.Str(map, "container");
                var wantGenerator = Core.Json.JsonText.Str(map, "generator");
                var info = WorkflowDetector.InspectFile(Path.Combine(dir, name), DetectOptions.Full());
                bool ok = info.HasWorkflow == want &&
                          (wantContainer == null || wantContainer == info.Container) &&
                          (wantGenerator == null ||
                           string.Equals(info.Generator, wantGenerator, StringComparison.OrdinalIgnoreCase));
                if (ok) pass++;
                else fail++;
                Console.WriteLine("{0}  {1,-24} has={2,-5} container={3,-9} {4}",
                    ok ? "PASS" : "FAIL", name, info.HasWorkflow, info.Container,
                    ok ? Detail(info) : "expected has=" + want + " container=" + wantContainer +
                        " generator=" + (wantGenerator ?? "-"));
            }
            Console.WriteLine();
            Console.WriteLine("{0} passed, {1} failed", pass, fail);
            return fail == 0 ? 0 : 1;
        }

        private static string Detail(WorkflowInfo info)
        {
            if (info.Generator != null && !info.HasWorkflow) return "generator=" + info.Generator;
            if (!info.HasWorkflow) return "";
            return string.Format("src={0} nodes={1} graph={2} prompt={3}", info.Source, info.NodeCount,
                info.WorkflowJson == null ? 0 : info.WorkflowJson.Length,
                info.PromptJson == null ? 0 : info.PromptJson.Length);
        }
    }
}
