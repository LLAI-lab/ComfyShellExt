using System;
using System.Collections.Generic;

namespace ComfyShellExt.Core.Json
{
    /// <summary>
    /// Pulls the searchable bits out of a workflow: node classes, model file names and prompt text.
    /// Handles both the editor graph format and the API prompt format.
    /// </summary>
    public static class JsonSummary
    {
        private static readonly string[] ModelExtensions =
        {
            ".safetensors", ".ckpt", ".pt", ".pth", ".bin", ".gguf", ".sft", ".onnx", ".pat"
        };

        private const int MaxNodeTypes = 400;
        private const int MaxModels = 200;
        private const int MaxTexts = 60;
        private const int MaxTextChars = 2000;

        public static void Summarize(string json, WorkflowInfo info)
        {
            object parsed;
            if (!MiniJson.TryParse(json, out parsed)) return;
            var nodeTypes = new OrderedSet(MaxNodeTypes);
            var models = new OrderedSet(MaxModels);
            var texts = new OrderedSet(MaxTexts);
            int nodeCount = 0;

            var root = parsed as Dictionary<string, object>;
            if (root != null && root.ContainsKey("nodes"))
            {
                var nodes = root["nodes"] as List<object>;
                if (nodes != null)
                {
                    nodeCount = nodes.Count;
                    foreach (var n in nodes) ReadGraphNode(n as Dictionary<string, object>, nodeTypes, models, texts);
                }
            }
            else if (root != null)
            {
                foreach (var kv in root)
                {
                    var node = kv.Value as Dictionary<string, object>;
                    if (node == null) continue;
                    object ct;
                    if (!node.TryGetValue("class_type", out ct) || !(ct is string)) continue;
                    nodeCount++;
                    nodeTypes.Add((string)ct);
                    ReadInputs(node, models, texts);
                }
            }

            if (nodeCount > 0) info.NodeCount = nodeCount;
            if (nodeTypes.Count > 0) info.NodeTypes = nodeTypes.ToList();
            if (models.Count > 0) info.Models = models.ToList();
            if (texts.Count > 0) info.Texts = texts.ToList();
        }

        private static void ReadGraphNode(Dictionary<string, object> node, OrderedSet types,
            OrderedSet models, OrderedSet texts)
        {
            if (node == null) return;
            object t;
            string type = node.TryGetValue("type", out t) ? t as string : null;
            if (!string.IsNullOrEmpty(type)) types.Add(type);
            object w;
            if (!node.TryGetValue("widgets_values", out w)) return;
            bool textNode = type != null &&
                            (type.IndexOf("TextEncode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             type.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             type.IndexOf("String", StringComparison.OrdinalIgnoreCase) >= 0);
            var list = w as List<object>;
            if (list != null)
            {
                foreach (var v in list) Classify(v as string, textNode, models, texts);
            }
            else
            {
                var map = w as Dictionary<string, object>;
                if (map != null)
                    foreach (var kv in map) Classify(kv.Value as string, textNode, models, texts);
            }
        }

        private static void ReadInputs(Dictionary<string, object> node, OrderedSet models, OrderedSet texts)
        {
            object inputsObj;
            if (!node.TryGetValue("inputs", out inputsObj)) return;
            var inputs = inputsObj as Dictionary<string, object>;
            if (inputs == null) return;
            foreach (var kv in inputs)
            {
                var s = kv.Value as string;
                if (s == null) continue;
                bool isText = kv.Key.Equals("text", StringComparison.OrdinalIgnoreCase) ||
                              kv.Key.Equals("prompt", StringComparison.OrdinalIgnoreCase) ||
                              kv.Key.Equals("string", StringComparison.OrdinalIgnoreCase);
                Classify(s, isText, models, texts);
            }
        }

        private static void Classify(string value, bool preferText, OrderedSet models, OrderedSet texts)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxTextChars) return;
            if (IsModelName(value)) { models.Add(value); return; }
            if (preferText || (value.Length >= 12 && value.IndexOf(' ') > 0)) texts.Add(value);
        }

        public static bool IsModelName(string value)
        {
            if (value == null || value.Length < 5 || value.Length > 260) return false;
            foreach (var ext in ModelExtensions)
                if (value.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private sealed class OrderedSet
        {
            private readonly List<string> _list = new List<string>();
            private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
            private readonly int _max;

            public OrderedSet(int max) { _max = max; }

            public int Count { get { return _list.Count; } }

            public void Add(string value)
            {
                if (string.IsNullOrEmpty(value) || _list.Count >= _max) return;
                if (_seen.Add(value)) _list.Add(value);
            }

            public List<string> ToList() { return _list; }
        }
    }
}
