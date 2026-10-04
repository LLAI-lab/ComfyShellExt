using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ComfyShellExt.Core.Json;

namespace ComfyShellExt.Core
{
    /// <summary>
    /// Human readable facts pulled out of a workflow: which checkpoint, which sampler settings and
    /// which prompts. For the API format the sampler's positive and negative inputs are followed
    /// through the graph, so the prompts are the real ones rather than a guess.
    /// </summary>
    public sealed class WorkflowFacts
    {
        public readonly List<KeyValuePair<string, string>> Items =
            new List<KeyValuePair<string, string>>();

        public string Positive;
        public string Negative;

        private Dictionary<string, object> _nodes;

        public static WorkflowFacts From(WorkflowInfo info)
        {
            var facts = new WorkflowFacts();
            if (info == null) return facts;
            if (info.HasWorkflow || info.RawJson != null)
            {
                if (info.PromptJson != null && facts.ReadApiFormat(info.PromptJson)) return facts;
                facts.ReadGraphFormat(info);
                return facts;
            }
            if (info.RawText != null && facts.ReadParametersText(info.RawText)) return facts;
            if (info.ToolJson != null) facts.ReadToolJson(info);
            return facts;
        }

        private void Add(string label, string value)
        {
            if (!string.IsNullOrEmpty(value)) Items.Add(new KeyValuePair<string, string>(label, value));
        }

        private bool ReadApiFormat(string json)
        {
            object parsed;
            if (!MiniJson.TryParse(json, out parsed)) return false;
            _nodes = parsed as Dictionary<string, object>;
            if (_nodes == null) return false;
            Dictionary<string, object> sampler = null;
            foreach (var kv in _nodes)
            {
                var node = kv.Value as Dictionary<string, object>;
                var type = node == null ? null : Str(node, "class_type");
                if (type == null || type.IndexOf("KSampler", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (sampler == null || type == "KSampler") sampler = node;
            }
            if (sampler == null) return false;
            var inputs = Map(sampler, "inputs");
            if (inputs == null) return false;

            var checkpoint = Follow(inputs, "model", "ckpt_name", 8) ?? Follow(inputs, "model", "unet_name", 8);
            Add("模型 Checkpoint", checkpoint);
            var loras = new List<string>();
            CollectLoras(inputs, loras, 8);
            if (loras.Count > 0) Add("LoRA", string.Join(", ", loras.ToArray()));
            Add("采样器 Sampler", Str(inputs, "sampler_name"));
            Add("调度器 Scheduler", Str(inputs, "scheduler"));
            Add("步数 Steps", Num(inputs, "steps"));
            Add("CFG", Num(inputs, "cfg"));
            Add("种子 Seed", Num(inputs, "seed") ?? Num(inputs, "noise_seed"));
            Add("降噪 Denoise", Num(inputs, "denoise"));
            var latent = Resolve(inputs, "latent_image");
            var latentInputs = latent == null ? null : Map(latent, "inputs");
            if (latentInputs != null)
            {
                var width = Num(latentInputs, "width");
                var height = Num(latentInputs, "height");
                if (width != null && height != null) Add("尺寸 Size", width + " x " + height);
                Add("批量 Batch", Num(latentInputs, "batch_size"));
            }
            Positive = Text(inputs, "positive", 6);
            Negative = Text(inputs, "negative", 6);
            return true;
        }

        private void ReadGraphFormat(WorkflowInfo info)
        {
            if (info.NodeTypes != null) Add("节点类型 Node types", string.Join(", ", info.NodeTypes.ToArray()));
            if (info.Models != null) Add("模型 Models", string.Join(", ", info.Models.ToArray()));
            if (info.Texts == null) return;
            if (info.Texts.Count > 0) Positive = info.Texts[0];
            if (info.Texts.Count > 1) Negative = info.Texts[1];
        }

        // ------------------------------------------------------------------
        // Non ComfyUI generators: A1111 "parameters" text and the settings
        // JSON that NovelAI / SwarmUI / Fooocus / InvokeAI embed.
        // ------------------------------------------------------------------

        /// <summary>
        /// A1111 layout: prompt lines, then "Negative prompt: ..." lines, then settings lines
        /// starting at "Steps: " (some forks continue the settings on further lines).
        /// </summary>
        private bool ReadParametersText(string text)
        {
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            int neg = -1, settings = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (neg < 0 && line.StartsWith("Negative prompt:", StringComparison.Ordinal))
                { neg = i; continue; }
                if (settings < 0 && LooksLikeSettings(line)) { settings = i; break; }
            }
            if (neg < 0 && settings < 0) return false;

            var prompt = new StringBuilder();
            for (int i = 0; i < (neg >= 0 ? neg : (settings >= 0 ? settings : lines.Length)); i++)
            {
                if (prompt.Length > 0) prompt.Append('\n');
                prompt.Append(lines[i]);
            }
            if (neg >= 0)
            {
                var negative = new StringBuilder();
                int end = settings >= 0 ? settings : lines.Length;
                for (int i = neg; i < end; i++)
                {
                    var line = i == neg
                        ? lines[i].Substring("Negative prompt:".Length).Trim()
                        : lines[i];
                    if (i > neg) negative.Append('\n');
                    negative.Append(line);
                }
                Negative = negative.ToString().Trim();
            }
            Positive = prompt.ToString().Trim();
            if (settings >= 0)
                for (int i = settings; i < lines.Length; i++)
                    ReadSettingsLine(lines[i]);
            return true;
        }

        private static bool LooksLikeSettings(string line)
        {
            foreach (var prefix in SettingsKeys)
                if (line.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        private static readonly string[] SettingsKeys =
        {
            "Steps: ", "Lora hashes: ", "Version: ", "Size: ", "Model hash: ",
            "Model: ", "Module 1: ", "TIFF", "Hashes: ", "Raw prompt: "
        };

        /// <summary>
        /// One settings line is "k: v, k: v, ...". Commas inside quoted values (Lora hashes) do
        /// not split. Keys that need no translation keep their original spelling.
        /// </summary>
        private void ReadSettingsLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            bool inQuote = false;
            int tokenStart = 0;
            for (int i = 0; i <= line.Length; i++)
            {
                bool split = false;
                if (i == line.Length) split = true;
                else if (line[i] == '"') inQuote = !inQuote;
                else if (line[i] == ',' && !inQuote) split = true;
                if (!split) continue;
                var token = line.Substring(tokenStart, i - tokenStart).Trim();
                tokenStart = i + 1;
                int colon = token.IndexOf(": ", StringComparison.Ordinal);
                if (colon <= 0) continue;
                var key = token.Substring(0, colon).Trim();
                var value = token.Substring(colon + 2).Trim();
                Add(FactLabel(key), value);
            }
        }

        private static string FactLabel(string key)
        {
            switch (key.ToLowerInvariant())
            {
                case "steps": return "步数 Steps";
                case "sampler": return "采样器 Sampler";
                case "schedule type": return "调度器 Scheduler";
                case "cfg scale": return "CFG";
                case "seed": return "种子 Seed";
                case "size": return "尺寸 Size";
                case "model": return "模型 Model";
                case "model hash": return "模型哈希 Model hash";
                case "vae": return "VAE";
                case "denoising strength": return "降噪 Denoise";
                case "clip skip": return "Clip skip";
                case "version": return "工具版本 Version";
                case "lora hashes": return "LoRA 哈希";
                default: return key;
            }
        }

        /// <summary>Routes the tool JSON to the right reader. Unknown keys are simply not shown.</summary>
        private bool ReadToolJson(WorkflowInfo info)
        {
            object parsed;
            if (!MiniJson.TryParse(info.ToolJson, out parsed)) return false;
            var root = parsed as Dictionary<string, object>;
            if (root == null) return false;
            object nested;
            switch (info.Generator)
            {
                case AiMeta.SwarmUI:
                    if (!root.TryGetValue("sui_image_params", out nested)) return false;
                    ReadNamedMap(nested as Dictionary<string, object>, new[]
                    {
                        new[] { "prompt" }, new[] { "negativeprompt" },
                        new[] { "model", "image_model" }, new[] { "sampler_name", "sampler" },
                        new[] { "steps" }, new[] { "cfgscale", "cfg_scale" },
                        new[] { "seed" }, new[] { "width" }, new[] { "height" }
                    });
                    return true;
                case AiMeta.Fooocus:
                    Positive = Str(root, "Prompt");
                    Negative = Str(root, "Negative Prompt");
                    if (!root.TryGetValue("Settings", out nested)) return Positive != null || Negative != null;
                    ReadNamedMap(nested as Dictionary<string, object>, new[]
                    {
                        new[] { "base_model" }, new[] { "sampler_name" }, new[] { "scheduler_name" },
                        new[] { "steps" }, new[] { "cfg_scale" }, new[] { "seed" },
                        new[] { "width" }, new[] { "height" }, new[] { "performance_selection" }
                    });
                    return true;
                case AiMeta.InvokeAI:
                    Positive = Str(root, "prompt");
                    Negative = Str(root, "negative_prompt");
                    object model, sampler;
                    if (root.TryGetValue("model", out model) && model is Dictionary<string, object>)
                        Add("模型 Model", Str((Dictionary<string, object>)model, "name"));
                    if (root.TryGetValue("sampler", out sampler) && sampler is Dictionary<string, object>)
                        Add("采样器 Sampler", Str((Dictionary<string, object>)sampler, "name"));
                    ReadNamedMap(root, new[]
                    {
                        new[] { "scheduler" }, new[] { "steps" }, new[] { "cfg_scale" },
                        new[] { "seed" }, new[] { "width" }, new[] { "height" }
                    });
                    return true;
                default: // NovelAI comment JSON
                    Positive = Str(root, "prompt");
                    Negative = Str(root, "uc");
                    ReadNamedMap(root, new[]
                    {
                        new[] { "steps" }, new[] { "scale" }, new[] { "sampler" },
                        new[] { "seed" }, new[] { "width" }, new[] { "height" },
                        new[] { "noise_schedule" }, new[] { "cfg_rescale" }, new[] { "sm" },
                        new[] { "dyn" }, new[] { "skip_cfg_above_sigma" }
                    });
                    return true;
            }
        }

        /// <summary>
        /// Emits "label value" rows from a settings map. Each keys array lists the spellings a
        /// value may hide under; the first present key wins. Row order follows the array.
        /// </summary>
        private void ReadNamedMap(Dictionary<string, object> map, string[][] keys)
        {
            if (map == null) return;
            foreach (var group in keys)
            {
                object value;
                foreach (var key in group)
                    if (map.TryGetValue(key, out value))
                    {
                        var label = FactLabel(group[0]);
                        var text = Value(value);
                        if (text != null) Add(label, text);
                        break;
                    }
            }
        }

        private static string Value(object value)
        {
            if (value is string) return ((string)value).Trim();
            if (value is bool) return (bool)value ? "true" : "false";
            if (value is double || value is long || value is int) return NumLike(value);
            return null;
        }

        private static string NumLike(object value)
        {
            var d = value as double?;
            if (d.HasValue)
                return d.Value == Math.Floor(d.Value) && Math.Abs(d.Value) < 1e15
                    ? ((long)d.Value).ToString(CultureInfo.InvariantCulture)
                    : d.Value.ToString("0.####", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>Walks an input reference chain looking for a named widget value.</summary>
        private string Follow(Dictionary<string, object> inputs, string link, string wanted, int depth)
        {
            if (depth <= 0) return null;
            var node = Resolve(inputs, link);
            if (node == null) return null;
            var nodeInputs = Map(node, "inputs");
            if (nodeInputs == null) return null;
            var direct = Str(nodeInputs, wanted);
            if (direct != null) return direct;
            return Follow(nodeInputs, link, wanted, depth - 1);
        }

        private void CollectLoras(Dictionary<string, object> inputs, List<string> found, int depth)
        {
            if (depth <= 0) return;
            var node = Resolve(inputs, "model");
            if (node == null) return;
            var nodeInputs = Map(node, "inputs");
            if (nodeInputs == null) return;
            var lora = Str(nodeInputs, "lora_name");
            if (lora != null && !found.Contains(lora)) found.Add(lora);
            CollectLoras(nodeInputs, found, depth - 1);
        }

        private string Text(Dictionary<string, object> inputs, string link, int depth)
        {
            if (depth <= 0) return null;
            var node = Resolve(inputs, link);
            if (node == null) return null;
            var nodeInputs = Map(node, "inputs");
            if (nodeInputs == null) return null;
            var text = Str(nodeInputs, "text") ?? Str(nodeInputs, "string") ?? Str(nodeInputs, "prompt");
            if (text != null) return text;
            foreach (var key in new[] { "conditioning", "conditioning_1", "text", link })
            {
                var next = Text(nodeInputs, key, depth - 1);
                if (next != null) return next;
            }
            return null;
        }

        /// <summary>An input value of the form ["7", 0] points at another node.</summary>
        private Dictionary<string, object> Resolve(Dictionary<string, object> inputs, string key)
        {
            object value;
            if (inputs == null || _nodes == null || !inputs.TryGetValue(key, out value)) return null;
            var reference = value as List<object>;
            if (reference == null || reference.Count == 0) return null;
            var id = reference[0] as string;
            object node;
            if (id == null || !_nodes.TryGetValue(id, out node)) return null;
            return node as Dictionary<string, object>;
        }

        private static Dictionary<string, object> Map(Dictionary<string, object> node, string key)
        {
            object value;
            return node != null && node.TryGetValue(key, out value)
                ? value as Dictionary<string, object> : null;
        }

        private static string Str(Dictionary<string, object> map, string key)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value)) return null;
            var s = value as string;
            return s != null ? s.Trim() : null;
        }

        private static string Num(Dictionary<string, object> map, string key)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value)) return null;
            if (value is double)
            {
                var d = (double)value;
                return d == Math.Floor(d) && Math.Abs(d) < 1e15
                    ? ((long)d).ToString(CultureInfo.InvariantCulture)
                    : d.ToString("0.####", CultureInfo.InvariantCulture);
            }
            return value as string;
        }
    }
}
