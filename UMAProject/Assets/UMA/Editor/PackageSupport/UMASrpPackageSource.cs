using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace UMA.Editors.PackageSupport
{
    internal static class UMASrpPackageSource
    {
        internal static string ExportShaderGraph(string path, string pipeline)
        {
            string original = File.ReadAllText(path);
            string opposite = pipeline == "URP" ? "UnityEditor.Rendering.HighDefinition.ShaderGraph." :
                "UnityEditor.Rendering.Universal.ShaderGraph.";
            if (!original.Contains(opposite)) return null;
            // Shader Graph uses multi-object JSON, not Unity YAML. Use its own object
            // serializer and target API rather than editing serialized text. Reflection
            // keeps the optional Shader Graph editor assembly out of UMA Core references.
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetType("UnityEditor.ShaderGraph.GraphData") != null);
            if (assembly == null) throw new InvalidOperationException("Install Shader Graph before exporting " + path);
            var graphType = assembly.GetType("UnityEditor.ShaderGraph.GraphData", true);
            var serializer = assembly.GetType("UnityEditor.ShaderGraph.Serialization.MultiJson", true);
            var targetType = assembly.GetType("UnityEditor.ShaderGraph.Target", true);
            object graph = Activator.CreateInstance(graphType, true);
            graphType.GetProperty("assetGuid").SetValue(graph, AssetDatabase.AssetPathToGUID(path));
            var messages = graphType.GetProperty("messageManager");
            messages.SetValue(graph, Activator.CreateInstance(messages.PropertyType, true));
            serializer.GetMethod("Deserialize", BindingFlags.Static | BindingFlags.Public).MakeGenericMethod(graphType)
                .Invoke(null, new object[] { graph, original, null, false });
            var targets = ((IEnumerable)graphType.GetProperty("activeTargets").GetValue(graph)).Cast<object>().ToArray();
            var deactivate = graphType.GetMethod("SetTargetInactive", new[] { targetType, typeof(bool) });
            foreach (object target in targets)
                if (target.GetType().FullName.StartsWith(opposite, StringComparison.Ordinal))
                    deactivate.Invoke(graph, new object[] { target, true });
            string exported = (string)serializer.GetMethod("Serialize", BindingFlags.Static | BindingFlags.Public)
                .Invoke(null, new[] { graph });
            if (exported.Contains(opposite))
                throw new InvalidDataException("Shader Graph could not remove the opposite pipeline target from " + path +
                    ". Open it in Shader Graph and select only " + pipeline + " before exporting.");
            return exported;
        }

        internal static string ExportYaml(string path, string pipeline)
        {
            string original = ReadYaml(path);
            if (original == null) return null;
            string updated = NormalizeYaml(original, pipeline);
            return original == updated ? null : updated;
        }

        internal static string ReadYaml(string path) => HasUnityYamlHeader(path) ? File.ReadAllText(path) : null;

        private static bool HasUnityYamlHeader(string path)
        {
            using var file = File.OpenRead(path);
            byte[] prefix = new byte[64];
            int count = file.Read(prefix, 0, prefix.Length);
            int offset = count >= 3 && prefix[0] == 0xef && prefix[1] == 0xbb && prefix[2] == 0xbf ? 3 : 0;
            foreach (string newline in new[] { "\n", "\r\n" })
            {
                byte[] header = Encoding.ASCII.GetBytes("%YAML 1.1" + newline + "%TAG !u! tag:unity3d.com,2011:" + newline);
                if (count >= offset + header.Length && header.Select((b, i) => prefix[offset + i] == b).All(match => match))
                    return true;
            }
            return false;
        }

        private static string NormalizeYaml(string text, string pipeline)
        {
            var kept = new List<string>();
            var removed = new HashSet<string>(StringComparer.Ordinal);
            foreach (string part in Regex.Split(text, @"(?m)(?=^--- !u!\d+ &-?\d+(?: stripped)?\r?$)"))
            {
                var header = Regex.Match(part, @"^--- !u!\d+ &(-?\d+)");
                var script = Regex.Match(part, @"(?m)^  m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*([0-9a-fA-F]{32}),\s*type:\s*3\}");
                if (header.Success && script.Success &&
                    UMASrpPackageArchiveValidator.IsOppositePipelineScript(script.Groups[1].Value, pipeline))
                    removed.Add(header.Groups[1].Value);
                else kept.Add(part);
            }
            string updated = string.Concat(kept);
            foreach (string id in removed)
            {
                string reference = @"\{fileID:\s*" + Regex.Escape(id) + @"\s*\}";
                updated = Regex.Replace(updated, @"(?m)^[ \t]*-[^\r\n]*" + reference + @"[ \t]*\r?\n", "");
                updated = Regex.Replace(updated, reference, "{fileID: 0}");
            }
            if (!Regex.IsMatch(updated, @"(?m)^--- !u!"))
                return string.Empty;
            if (pipeline == "URP")
                updated = Regex.Replace(updated, @"(?m)^  (_HDRPMaterial|_HDRPSecondPass):[^\r\n]*", "  $1: {fileID: 0}");
            else
            {
                if (Regex.IsMatch(updated, @"(?m)^  _HDRPMaterial:\s*\{[^\r\n]*guid:"))
                    updated = Regex.Replace(updated, @"(?m)^  _material:[^\r\n]*", "  _material: {fileID: 0}");
                if (Regex.IsMatch(updated, @"(?m)^  _HDRPSecondPass:\s*\{[^\r\n]*guid:"))
                    updated = Regex.Replace(updated, @"(?m)^  _secondPass:[^\r\n]*", "  _secondPass: {fileID: 0}");
            }
            return updated;
        }
    }
}
