using System;
using System.IO;
using ComfyShellExt.Core.Formats;
using ComfyShellExt.Core.Json;
using ComfyShellExt.Core.Util;

namespace ComfyShellExt.Core
{
    /// <summary>Entry point: inspects a media file and reports whether it carries a ComfyUI workflow.</summary>
    public static class WorkflowDetector
    {
        public static WorkflowInfo InspectFile(string path, DetectOptions options)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                           64 * 1024, FileOptions.SequentialScan))
                    return Inspect(fs, options);
            }
            catch (Exception ex)
            {
                Log.Error("InspectFile " + path, ex);
                return new WorkflowInfo { Container = "error", Error = ex.Message };
            }
        }

        public static WorkflowInfo Inspect(Stream stream, DetectOptions options)
        {
            var info = new WorkflowInfo { Container = "unknown" };
            if (stream == null) return info;
            if (options == null) options = DetectOptions.Full();
            try
            {
                var reader = new ByteReader(stream);
                string name;
                var type = ContainerSniffer.Sniff(reader, out name);
                info.Container = name;

                var sink = new MetaSink(e => Consume(e, info, options), options.KeepMetadata);
                ContainerSniffer.Dispatch(type, reader, sink);

                if (!info.HasWorkflow && options.EnableRawScan)
                {
                    long window = (long)Math.Max(1, options.MaxRawScanBytes);
                    // For ISO base media files the metadata all lives in moov; scanning just that box
                    // finds a workflow in a multi gigabyte video without reading the whole file.
                    long moovStart, moovEnd;
                    if (type == ContainerType.IsoBmff &&
                        IsoBmffReader.TryFindMoov(reader, out moovStart, out moovEnd))
                        RawScanner.ScanRange(reader, moovStart, moovEnd, "raw:moov", sink);
                    if (!info.HasWorkflow) RawScanner.Scan(reader, window, sink);
                }
                if (options.KeepMetadata) info.Metadata = sink.Kept;
                info.Incomplete = info.HasWorkflow && info.WorkflowJson == null && info.PromptJson == null;
                if (options.Summarize && info.BestJson != null)
                {
                    JsonSummary.Summarize(info.WorkflowJson ?? info.PromptJson, info);
                    if (info.NodeCount == 0 && info.PromptJson != null && info.WorkflowJson != null)
                        JsonSummary.Summarize(info.PromptJson, info);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Inspect", ex);
                info.Error = ex.Message;
            }
            return info;
        }

        /// <summary>Fast path used by the thumbnail handler: stops at the first hit.</summary>
        public static bool HasWorkflow(Stream stream)
        {
            var settings = Settings.Current;
            var options = DetectOptions.Fast();
            options.EnableRawScan = settings.EnableRawScan;
            options.MaxRawScanBytes = Math.Max(1, settings.MaxRawScanMB) * 1024L * 1024L;
            return Inspect(stream, options).HasWorkflow;
        }

        /// <summary>
        /// Fast path used by the thumbnail handler: returns which AI tool produced the file
        /// ("ComfyUI", "A1111", ...), or null when nothing recognised. The badge text comes from
        /// this, so the thumbnail handler only needs one pass over the file.
        /// </summary>
        public static string DetectGenerator(Stream stream)
        {
            var settings = Settings.Current;
            var options = DetectOptions.Fast();
            options.EnableRawScan = settings.EnableRawScan;
            options.MaxRawScanBytes = Math.Max(1, settings.MaxRawScanMB) * 1024L * 1024L;
            var info = Inspect(stream, options);
            return info.HasAiMeta ? (info.Generator ?? AiMeta.Comfy) : null;
        }

        /// <summary>Called for every metadata entry. Returns false to stop reading the file.</summary>
        private static bool Consume(MetaEntry entry, WorkflowInfo info, DetectOptions options)
        {
            var value = entry.Value;
            if (!JsonScan.HasMarker(value))
            {
                bool unescape = value.IndexOf("&quot;", StringComparison.Ordinal) >= 0 ||
                                value.IndexOf("&#34;", StringComparison.Ordinal) >= 0;
                var plain = unescape ? TextCodec.UnescapeXml(value) : value;
                if (!JsonScan.HasMarker(plain))
                {
                    // Not ComfyUI. Sniff the entry for other AI tools before moving on; when the
                    // value was XMP escaped the tool text hides behind &quot; too.
                    var same = entry;
                    same.Value = plain;
                    AiMeta.Sniff(same.Key, same.Value, info, !options.FastDetectOnly);
                    return !(options.FastDetectOnly && info.HasAiMeta);
                }
                value = plain;
            }

            // Detection never depends on the payload being complete: a container may well have
            // truncated it, and the file still deserves its badge.
            if (!info.HasWorkflow)
            {
                info.HasWorkflow = true;
                info.Source = entry.Origin;
                info.Generator = AiMeta.Comfy;
            }
            if (options.FastDetectOnly) return false;

            var candidate = JsonScan.Unwrap(value);
            if (candidate == null)
            {
                if (info.RawJson == null) info.RawJson = JsonScan.Salvage(value) ?? value;
                return true;
            }

            string graph = null, prompt = null;
            string member;
            if (TryMember(candidate, "workflow", out member) && JsonScan.Sniff(member) == WorkflowKind.Graph)
                graph = member;
            if (TryMember(candidate, "prompt", out member) && JsonScan.Sniff(member) == WorkflowKind.Prompt)
                prompt = member;
            if (graph == null && prompt == null)
            {
                var kind = JsonScan.Sniff(candidate);
                if (kind == WorkflowKind.Graph) graph = candidate;
                else if (kind == WorkflowKind.Prompt) prompt = candidate;
            }
            if (graph != null && info.WorkflowJson == null) info.WorkflowJson = graph;
            if (prompt != null && info.PromptJson == null) info.PromptJson = prompt;
            return info.WorkflowJson == null || info.PromptJson == null;
        }

        /// <summary>
        /// Finds an object valued member by name and returns its exact source text, so wrappers such
        /// as {"prompt":{...},"workflow":{...}} can be split without re-serialising.
        /// </summary>
        private static bool TryMember(string json, string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(json)) return false;
            var needle = "\"" + key + "\"";
            int at = json.IndexOf(needle, StringComparison.Ordinal);
            while (at >= 0)
            {
                int i = at + needle.Length;
                while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\r' || json[i] == '\n')) i++;
                if (i < json.Length && json[i] == ':')
                {
                    i++;
                    while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\r' || json[i] == '\n')) i++;
                    if (i < json.Length && json[i] == '{')
                    {
                        var body = JsonScan.ExtractBalanced(json, i);
                        if (body != null) { value = body; return true; }
                    }
                }
                at = json.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
            }
            return false;
        }
    }
}
