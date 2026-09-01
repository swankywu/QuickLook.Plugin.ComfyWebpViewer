using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace QuickLook.Plugin.ComfyWebpViewer
{
    internal sealed class ComfySummary
    {
        public List<KeyValuePair<string, string>> Fields = new List<KeyValuePair<string, string>>();
        public string Positive;
        public string Negative;
        public string PromptJson;
        public string WorkflowJson;

        public bool IsEmpty =>
            Fields.Count == 0 &&
            string.IsNullOrWhiteSpace(Positive) &&
            string.IsNullOrWhiteSpace(Negative) &&
            string.IsNullOrWhiteSpace(PromptJson) &&
            string.IsNullOrWhiteSpace(WorkflowJson);
    }

    /// <summary>
    /// Turns the raw ComfyUI payloads embedded in the WebP EXIF chunk into something
    /// a human can read at a glance: checkpoint, sampler, steps, CFG, seed, prompts...
    /// </summary>
    internal static class ComfyMetadata
    {
        public static ComfySummary Build(WebpFile file)
        {
            var summary = new ComfySummary();

            if (file == null)
                return summary;

            try
            {
                object promptNode = null;
                string promptRaw;
                if (file.ComfyPayloads.TryGetValue("prompt", out promptRaw))
                {
                    summary.PromptJson = SafePretty(promptRaw);
                    Json.TryParse(promptRaw, out promptNode);
                }

                string workflowRaw;
                if (file.ComfyPayloads.TryGetValue("workflow", out workflowRaw))
                    summary.WorkflowJson = SafePretty(workflowRaw);

                foreach (var kv in file.ComfyPayloads)
                {
                    if (string.Equals(kv.Key, "prompt", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(kv.Key, "workflow", StringComparison.OrdinalIgnoreCase))
                        continue;

                    summary.WorkflowJson = summary.WorkflowJson ?? SafePretty(kv.Value);
                }

                var graph = Json.AsObject(promptNode);
                if (graph == null)
                    return summary;

                var sampler = FindSampler(graph);

                if (sampler != null)
                {
                    Add(summary, "Sampler", Text(sampler.Node, "sampler_name"));
                    Add(summary, "Scheduler", Text(sampler.Node, "scheduler"));
                    Add(summary, "Steps", Text(sampler.Node, "steps"));
                    Add(summary, "CFG", Text(sampler.Node, "cfg"));
                    Add(summary, "Denoise", Text(sampler.Node, "denoise"));

                    var seed = Text(sampler.Node, "seed") ?? Text(sampler.Node, "noise_seed");
                    Add(summary, "Seed", seed);

                    var model = Resolve(graph, sampler.Node, "model");
                    var ckpt = model != null
                        ? FirstText(model, "ckpt_name", "unet_name", "model_name")
                        : null;
                    if (ckpt == null)
                        ckpt = FirstValueInGraph(graph, "ckpt_name", "unet_name");
                    Add(summary, "Checkpoint", ckpt);

                    var positive = Resolve(graph, sampler.Node, "positive");
                    var negative = Resolve(graph, sampler.Node, "negative");
                    summary.Positive = positive != null ? Text(positive, "text") : null;
                    summary.Negative = negative != null ? Text(negative, "text") : null;

                    var latent = Resolve(graph, sampler.Node, "latent_image");
                    if (latent != null)
                    {
                        var lw = Text(latent, "width");
                        var lh = Text(latent, "height");
                        if (lw != null && lh != null)
                            Add(summary, "Latent", lw + " × " + lh);
                    }
                }

                if (string.IsNullOrWhiteSpace(summary.Positive))
                {
                    summary.Positive = LongestTextInGraph(graph, "CLIPTextEncode");
                }

                Add(summary, "Nodes", graph.Count.ToString(CultureInfo.InvariantCulture));
            }
            catch
            {
                // Never fail the preview because of metadata.
            }

            return summary;
        }

        // ------------------------------------------------------------------ graph walk

        private sealed class SamplerHit
        {
            public string Id;
            public Dictionary<string, object> Node;
        }

        private static SamplerHit FindSampler(Dictionary<string, object> graph)
        {
            SamplerHit fallback = null;

            foreach (var kv in graph)
            {
                var node = Json.AsObject(kv.Value);
                if (node == null)
                    continue;

                var type = Json.AsText(Json.Get(node, "class_type"));
                if (string.IsNullOrEmpty(type))
                    continue;

                var isSampler = type.IndexOf("Sampler", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isSampler)
                    continue;

                var hit = new SamplerHit { Id = kv.Key, Node = node };
                if (string.Equals(type, "KSampler", StringComparison.Ordinal) ||
                    string.Equals(type, "KSamplerAdvanced", StringComparison.Ordinal))
                    return hit;

                fallback = fallback ?? hit;
            }

            return fallback;
        }

        /// <summary>Follows a ComfyUI link tuple such as ["4", 0] back to its source node.</summary>
        private static Dictionary<string, object> Resolve(
            Dictionary<string, object> graph, Dictionary<string, object> node, string inputName)
        {
            var inputs = Json.AsObject(Json.Get(node, "inputs"));
            if (inputs == null)
                return null;

            object link;
            if (!inputs.TryGetValue(inputName, out link))
                return null;

            var tuple = Json.AsArray(link);
            if (tuple == null || tuple.Count < 1)
                return null;

            var targetId = Json.AsText(tuple[0]);
            if (targetId == null)
                return null;

            object target;
            if (!graph.TryGetValue(targetId, out target))
                return null;

            return Json.AsObject(target);
        }

        // ------------------------------------------------------------------ accessors

        private static void Add(ComfySummary summary, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            summary.Fields.Add(new KeyValuePair<string, string>(label, value.Trim()));
        }

        private static string Text(Dictionary<string, object> node, string key)
        {
            var inputs = Json.AsObject(Json.Get(node, "inputs"));
            if (inputs == null)
                return null;

            object v;
            return inputs.TryGetValue(key, out v) ? Json.AsText(v) : null;
        }

        private static string FirstText(Dictionary<string, object> node, params string[] keys)
        {
            foreach (var key in keys)
            {
                var direct = Json.AsText(Json.Get(node, key));
                if (!string.IsNullOrWhiteSpace(direct))
                    return direct;

                var inInputs = Text(node, key);
                if (!string.IsNullOrWhiteSpace(inInputs))
                    return inInputs;
            }

            return null;
        }

        private static string FirstValueInGraph(Dictionary<string, object> graph, params string[] keys)
        {
            foreach (var kv in graph)
            {
                var node = Json.AsObject(kv.Value);
                if (node == null)
                    continue;

                var value = FirstText(node, keys);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        private static string LongestTextInGraph(Dictionary<string, object> graph, string classType)
        {
            string best = null;

            foreach (var kv in graph)
            {
                var node = Json.AsObject(kv.Value);
                if (node == null)
                    continue;

                if (!string.Equals(Json.AsText(Json.Get(node, "class_type")) ?? string.Empty, classType, StringComparison.Ordinal))
                    continue;

                var text = Text(node, "text");
                if (!string.IsNullOrWhiteSpace(text) && (best == null || text.Length > best.Length))
                    best = text;
            }

            return best;
        }

        private static string SafePretty(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return raw;

            object node;
            if (!Json.TryParse(raw, out node) || node == null)
                return raw;

            try
            {
                var pretty = Json.Pretty(node);
                // Keep the panel responsive for gigantic workflows.
                return pretty.Length > 400000 ? pretty.Substring(0, 400000) + "\n…" : pretty;
            }
            catch
            {
                return raw;
            }
        }

        // --------------------------------------------------------------- file summary

        public static string DescribeFile(WebpFile file)
        {
            var sb = new StringBuilder();
            sb.Append(file.Width).Append('×').Append(file.Height);
            sb.Append(" · ").Append(file.Codec);
            if (file.HasAlpha)
                sb.Append(" · Alpha");
            sb.Append(" · ").Append(FormatSize(file.FileSize));
            return sb.ToString();
        }

        public static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            var size = (double)bytes;
            var unit = 0;

            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return unit == 0
                ? bytes.ToString(CultureInfo.InvariantCulture) + " B"
                : size.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[unit];
        }
    }
}
