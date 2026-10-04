using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ComfyShellExt.Core;
using ComfyShellExt.Core.Formats;
using ComfyShellExt.Core.Json;

namespace ComfyShellExt.Cli
{
    /// <summary>
    /// Shows everything a file carries. Use this when a file should have a workflow but does not
    /// show one: it distinguishes "the metadata is not there" from "it is there and we misread it".
    /// </summary>
    internal static class DumpCommand
    {
        public static int Run(Args args)
        {
            if (args.Values.Count == 0)
            {
                Console.Error.WriteLine(
                    "usage: ComfyWorkflowDb dump <file> [...] [--tree] [--deep] [--chars 200]");
                return 2;
            }
            int preview = args.Int("chars", 200);
            int found = 0;
            foreach (var path in Expand(args.Values))
            {
                if (!File.Exists(path)) { Console.WriteLine("not found: " + path); continue; }
                var info = new FileInfo(path);
                Console.WriteLine("=== {0}", path);
                Console.WriteLine("    {0:N0} bytes, modified {1:yyyy-MM-dd HH:mm}",
                    info.Length, info.LastWriteTime);
                if (args.Has("tree")) Tree(path);

                var options = DetectOptions.Full();
                if (args.Has("deep")) options.MaxRawScanBytes = long.MaxValue;
                var result = WorkflowDetector.InspectFile(path, options);
                if (result.HasWorkflow) found++;

                Console.WriteLine("    container : {0}", result.Container);
                var entries = result.Metadata;
                Console.WriteLine("    metadata  : {0} text entr{1}",
                    entries == null ? 0 : entries.Count, entries != null && entries.Count == 1 ? "y" : "ies");
                if (entries != null)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var entry = entries[i];
                        var kind = JsonScan.Sniff(entry.Value);
                        var unwrapped = JsonScan.Unwrap(entry.Value);
                        Console.WriteLine("      [{0}] {1}  {2:N0} chars  comfy={3}  json={4}",
                            i + 1, entry.Origin, entry.Value.Length,
                            kind == WorkflowKind.None ? "no" : kind.ToString().ToLowerInvariant(),
                            unwrapped != null ? "complete" : kind == WorkflowKind.None ? "-" : "TRUNCATED");
                        Console.WriteLine("           {0}", Flatten(entry.Value, preview));
                    }
                }
                Console.WriteLine("    workflow  : {0}{1}", result.HasWorkflow ? "YES" : "no",
                    result.Source == null ? "" : "  <- " + result.Source);
                if (result.Generator != null && !result.HasWorkflow)
                    Console.WriteLine("    generator : {0}", result.Generator);
                if (result.WorkflowJson != null)
                    Console.WriteLine("    graph     : {0:N0} chars", result.WorkflowJson.Length);
                if (result.PromptJson != null)
                    Console.WriteLine("    prompt    : {0:N0} chars", result.PromptJson.Length);
                if (result.Incomplete)
                    Console.WriteLine("    note      : payload is truncated in the file, only partial JSON recovered");
                if (!result.HasAiMeta)
                    Console.WriteLine("    note      : no AI generation signature anywhere; try --deep, and check " +
                                      "whether the file was re-encoded or exported by another tool");
                Console.WriteLine();
            }
            return found > 0 ? 0 : 1;
        }

        private static void Tree(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                    foreach (var line in ContainerOutline.Describe(stream))
                        Console.WriteLine("    " + line);
            }
            catch (Exception ex)
            {
                Console.WriteLine("    (outline failed: " + ex.Message + ")");
            }
        }

        private static IEnumerable<string> Expand(List<string> inputs)
        {
            foreach (var input in inputs)
            {
                if (Directory.Exists(input))
                    foreach (var file in Directory.GetFiles(input)) yield return file;
                else yield return input;
            }
        }

        private static string Flatten(string value, int max)
        {
            var sb = new StringBuilder(Math.Min(value.Length, max) + 4);
            foreach (var c in value)
            {
                if (sb.Length >= max) { sb.Append(" ..."); break; }
                sb.Append(c == '\r' || c == '\n' || c == '\t' ? ' ' : c < 0x20 ? '.' : c);
            }
            return sb.ToString();
        }
    }
}
