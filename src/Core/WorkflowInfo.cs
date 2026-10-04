using System.Collections.Generic;

namespace ComfyShellExt.Core
{
    /// <summary>Which flavour of ComfyUI payload a JSON blob turned out to be.</summary>
    public enum WorkflowKind
    {
        None = 0,
        /// <summary>The editor graph: last_node_id / nodes / links.</summary>
        Graph,
        /// <summary>The API/execution format: { "7": { "class_type": ..., "inputs": ... } }.</summary>
        Prompt
    }

    /// <summary>Result of inspecting one media file.</summary>
    public sealed class WorkflowInfo
    {
        /// <summary>True when a ComfyUI workflow or prompt graph was found.</summary>
        public bool HasWorkflow;
        /// <summary>Container that was parsed: png, jpeg, webp, gif, mp4, mkv, avi, flac, raw.</summary>
        public string Container;
        /// <summary>Origin of the first hit, e.g. "png:tEXt:workflow".</summary>
        public string Source;
        /// <summary>
        /// Which AI tool produced the file: ComfyUI, A1111, NovelAI, SwarmUI, Fooocus, InvokeAI.
        /// Null when nothing recognised. HasWorkflow implies Generator == "ComfyUI".
        /// </summary>
        public string Generator;
        /// <summary>Generation settings in A1111 "parameters" text form (capture only).</summary>
        public string RawText;
        /// <summary>Settings JSON of a non ComfyUI tool such as NovelAI or SwarmUI (capture only).</summary>
        public string ToolJson;
        /// <summary>True when any known AI generation metadata was found, ComfyUI or not.</summary>
        public bool HasAiMeta
        {
            get { return HasWorkflow || Generator != null; }
        }
        /// <summary>Editor graph JSON when present.</summary>
        public string WorkflowJson;
        /// <summary>API format prompt JSON when present.</summary>
        public string PromptJson;
        /// <summary>
        /// Payload that carries a ComfyUI signature but does not parse, normally because the
        /// container truncated it. Kept so the file is still reported and can be inspected.
        /// </summary>
        public string RawJson;
        /// <summary>True when only RawJson could be recovered.</summary>
        public bool Incomplete;
        /// <summary>Every text metadata entry seen, when DetectOptions.KeepMetadata is set.</summary>
        public List<MetaEntry> Metadata;
        /// <summary>Node class names, filled in when DetectOptions.Summarize is set.</summary>
        public List<string> NodeTypes;
        /// <summary>Checkpoint / LoRA / VAE file names referenced by the graph.</summary>
        public List<string> Models;
        /// <summary>Prompt-ish text widgets, used for full text search in the index.</summary>
        public List<string> Texts;
        public int NodeCount;
        /// <summary>Set when parsing failed; detection result is then best effort.</summary>
        public string Error;

        public string BestJson
        {
            get { return WorkflowJson ?? PromptJson ?? RawJson; }
        }
    }

    public sealed class DetectOptions
    {
        /// <summary>Stop at the first hit and skip JSON capture. Used by the thumbnail handler.</summary>
        public bool FastDetectOnly;
        /// <summary>Keep every metadata entry in WorkflowInfo.Metadata.</summary>
        public bool KeepMetadata;
        /// <summary>Parse the captured JSON to fill NodeTypes / Models / Texts / NodeCount.</summary>
        public bool Summarize;
        /// <summary>Brute force search for workflow markers when container parsing found nothing.</summary>
        public bool EnableRawScan = true;
        /// <summary>Upper bound for the brute force search, from each end of the file.</summary>
        public long MaxRawScanBytes = 8L * 1024 * 1024;

        public static DetectOptions Fast()
        {
            return new DetectOptions { FastDetectOnly = true, Summarize = false, KeepMetadata = false };
        }

        public static DetectOptions Full()
        {
            return new DetectOptions { FastDetectOnly = false, Summarize = true, KeepMetadata = true };
        }
    }
}
