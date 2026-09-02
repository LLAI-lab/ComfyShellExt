using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ComfyShellExt.Core;
using ComfyShellExt.Core.Store;

namespace ComfyShellExt.Cli
{
    /// <summary>Walks folders, inspects media and updates the workflow database.</summary>
    internal static class ScanCommand
    {
        private static readonly string[] DefaultExtensions =
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".avif",
            ".mp4", ".mov", ".m4v", ".mkv", ".webm", ".avi", ".flac"
        };

        public static int Run(Args args)
        {
            if (args.Values.Count == 0)
            {
                Console.Error.WriteLine("usage: ComfyWorkflowDb scan <dir|file> [...] [--db dir]");
                return 2;
            }
            var index = new WorkflowIndex(args.Str("db"));
            index.Load();
            var extensions = ParseExtensions(args.Str("ext"));
            bool quiet = args.Has("quiet");
            bool rescanAll = args.Has("all");
            var files = Collect(args.Values, extensions, !args.Has("no-recurse"));
            Console.WriteLine("database : {0}", index.Dir);
            Console.WriteLine("candidates: {0} file(s), {1} already indexed", files.Count, index.Count);

            int jobs = Math.Max(1, Math.Min(args.Int("jobs", Environment.ProcessorCount), 8));
            var clock = Stopwatch.StartNew();
            int scanned = 0, skipped = 0, found = 0, failed = 0, next = 0;
            var workers = new Thread[jobs];
            for (int w = 0; w < jobs; w++)
            {
                workers[w] = new Thread(() =>
                {
                    while (true)
                    {
                        int i = Interlocked.Increment(ref next) - 1;
                        if (i >= files.Count) return;
                        var path = files[i];
                        try
                        {
                            var fi = new FileInfo(path);
                            long mtime = DbRecord.ToUnix(fi.LastWriteTimeUtc);
                            if (!rescanAll && !index.NeedsScan(path, fi.Length, mtime))
                            {
                                Interlocked.Increment(ref skipped);
                                continue;
                            }
                            var info = WorkflowDetector.InspectFile(path, DetectOptions.Full());
                            var record = new DbRecord
                            {
                                Path = path,
                                Size = fi.Length,
                                MTime = mtime,
                                Container = info.Container,
                                HasWorkflow = info.HasWorkflow,
                                WorkflowRef = index.PutObject(info.WorkflowJson),
                                PromptRef = index.PutObject(info.PromptJson),
                                NodeCount = info.NodeCount,
                                NodeTypes = info.NodeTypes,
                                Models = info.Models,
                                Texts = info.Texts,
                                Source = info.Source,
                                ScannedAt = DbRecord.ToUnix(DateTime.UtcNow)
                            };
                            index.Put(record);
                            Interlocked.Increment(ref scanned);
                            if (info.HasWorkflow)
                            {
                                Interlocked.Increment(ref found);
                                if (!quiet) Console.WriteLine("  + {0}  [{1}]", path, info.Source);
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failed);
                            Console.Error.WriteLine("  ! {0}: {1}", path, ex.Message);
                        }
                    }
                });
                workers[w].IsBackground = true;
                workers[w].Start();
            }
            foreach (var t in workers) t.Join();

            int pruned = 0;
            if (args.Has("prune")) pruned = index.Remove(r => !File.Exists(r.Path));
            if (index.Dirty) index.Save();
            Console.WriteLine();
            Console.WriteLine("scanned {0}, skipped {1}, with workflow {2}, failed {3}, pruned {4} in {5:0.0}s",
                scanned, skipped, found, failed, pruned, clock.Elapsed.TotalSeconds);
            Console.WriteLine("index    : {0} record(s) -> {1}", index.Count, index.IndexPath);
            return 0;
        }

        private static HashSet<string> ParseExtensions(string value)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var source = string.IsNullOrEmpty(value) ? DefaultExtensions
                : value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in source)
            {
                var e = raw.Trim().ToLowerInvariant();
                if (e.Length == 0) continue;
                set.Add(e[0] == '.' ? e : "." + e);
            }
            return set;
        }

        private static List<string> Collect(List<string> inputs, HashSet<string> extensions, bool recurse)
        {
            var files = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var input in inputs)
            {
                try
                {
                    if (File.Exists(input))
                    {
                        Add(files, seen, Path.GetFullPath(input), extensions, true);
                    }
                    else if (Directory.Exists(input))
                    {
                        var option = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                        foreach (var file in Directory.EnumerateFiles(input, "*", option))
                            Add(files, seen, file, extensions, false);
                    }
                    else
                    {
                        Console.Error.WriteLine("  ! not found: " + input);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("  ! {0}: {1}", input, ex.Message);
                }
            }
            return files;
        }

        private static void Add(List<string> files, HashSet<string> seen, string path,
            HashSet<string> extensions, bool explicitFile)
        {
            if (!explicitFile && !extensions.Contains(Path.GetExtension(path))) return;
            var full = Path.GetFullPath(path);
            if (seen.Add(full)) files.Add(full);
        }
    }
}
