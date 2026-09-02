using System;
using System.Collections.Generic;
using System.Globalization;
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
            if (info.PromptJson != null && facts.ReadApiFormat(info.PromptJson)) return facts;
            facts.ReadGraphFormat(info);
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
            return map != null && map.TryGetValue(key, out value) ? value as string : null;
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
