using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace UMA.Editors.PackageSupport
{
    [Flags]
    internal enum SrpPipelines
    {
        None = 0,
        URP = 1,
        HDRP = 2,
        Both = URP | HDRP
    }

    /// <summary>
    /// Decides which render pipelines can use each file of the dual-pipeline SRP source, from what the file
    /// contains rather than from its name: a Shader Graph by its targets, a hand-written shader by its SubShader
    /// RenderPipeline tags, an include file by the shaders and graphs that use it, and a material by its shader
    /// and by how UMAMaterials reference it (a material reached only through _HDRPMaterial/_HDRPSecondPass is
    /// HDRP-only; one reached only through a _material/_secondPass that HDRP replaces is URP-only). Everything
    /// else is usable by both pipelines.
    /// </summary>
    internal static class UMASrpPipelineClassifier
    {
        private static readonly Regex Include = new Regex(@"#include(?:_with_pragmas)?\s*""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex RenderPipelineTag = new Regex(@"""RenderPipeline""\s*=\s*""(\w+)""", RegexOptions.Compiled);
        private static readonly Regex SubShader = new Regex(@"(?m)^\s*SubShader\b", RegexOptions.Compiled);
        private static readonly Regex MaterialShader = new Regex(@"(?m)^  m_Shader:\s*\{fileID:\s*-?\d+,\s*guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);
        private static readonly Regex Reference = new Regex(@"(?m)^[ \t]*(?:-[ \t]*)?(?:(\w+):[ \t]*)?\{fileID:\s*-?\d+,\s*guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);
        private static readonly Regex LiveHdrpMaterial = new Regex(@"(?m)^  _HDRPMaterial:\s*\{[^\r\n]*guid:", RegexOptions.Compiled);
        private static readonly Regex LiveHdrpSecondPass = new Regex(@"(?m)^  _HDRPSecondPass:\s*\{[^\r\n]*guid:", RegexOptions.Compiled);

        private const string UrpGraphTarget = "UnityEditor.Rendering.Universal.ShaderGraph.";
        private const string HdrpGraphTarget = "UnityEditor.Rendering.HighDefinition.ShaderGraph.";

        internal static bool Supports(SrpPipelines pipelines, string pipeline) =>
            (pipelines & (pipeline == "HDRP" ? SrpPipelines.HDRP : SrpPipelines.URP)) != 0;

        /// <param name="paths">Source files (no .meta files).</param>
        /// <param name="readText">Text of a file, or null for a binary or unreadable file.</param>
        /// <param name="guidOfPath">GUID from a source file's .meta, or null.</param>
        /// <param name="pathOfGuid">Project path of any GUID (source, project or package), or null.</param>
        internal static Dictionary<string, SrpPipelines> Classify(IEnumerable<string> paths, Func<string, string> readText,
            Func<string, string> guidOfPath, Func<string, string> pathOfGuid)
        {
            string[] files = paths.ToArray();
            var text = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in files)
            {
                string content = IsTextKind(path) ? readText(path) : null;
                if (content != null) text[path] = content;
            }
            var guids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in files)
            {
                string guid = guidOfPath(path);
                if (!string.IsNullOrEmpty(guid)) guids[path] = guid.ToLowerInvariant();
            }

            var result = new Dictionary<string, SrpPipelines>(StringComparer.Ordinal);
            foreach (string path in files)
            {
                if (!text.TryGetValue(path, out string content)) continue;
                if (IsGraph(path)) result[path] = GraphPipelines(content);
                else if (Has(path, ".shader")) result[path] = ShaderPipelines(content);
            }

            var includeFiles = files.Where(p => Has(p, ".hlsl") || Has(p, ".cginc")).ToArray();
            var users = includeFiles.ToDictionary(p => p, p => FindUsers(p, files, text, guids), StringComparer.Ordinal);
            foreach (string path in includeFiles)
                result[path] = IncludePipelines(path, users, result, new HashSet<string>(StringComparer.Ordinal));

            var referenceRoles = CollectMaterialReferenceRoles(files, text);
            foreach (string path in files.Where(p => Has(p, ".mat")))
            {
                SrpPipelines byShader = SrpPipelines.Both;
                if (text.TryGetValue(path, out string content))
                {
                    Match shader = MaterialShader.Match(content);
                    if (shader.Success) byShader = PipelinesOfShader(pathOfGuid(shader.Groups[1].Value.ToLowerInvariant()), result);
                }
                SrpPipelines byUse = guids.TryGetValue(path, out string guid) && referenceRoles.TryGetValue(guid, out SrpPipelines roles)
                    ? roles : SrpPipelines.Both;
                SrpPipelines combined = byShader & byUse;
                // A material whose uses and shader disagree is still kept where its shader works; the
                // builder's material check then reports the UMAMaterial that needs a pipeline-specific one.
                result[path] = combined == SrpPipelines.None ? byShader : combined;
            }

            foreach (string path in files)
                if (!result.ContainsKey(path)) result[path] = SrpPipelines.Both;
            return result;
        }

        private static SrpPipelines GraphPipelines(string content)
        {
            SrpPipelines pipelines = SrpPipelines.None;
            if (content.IndexOf(UrpGraphTarget, StringComparison.Ordinal) >= 0) pipelines |= SrpPipelines.URP;
            if (content.IndexOf(HdrpGraphTarget, StringComparison.Ordinal) >= 0) pipelines |= SrpPipelines.HDRP;
            return pipelines == SrpPipelines.None ? SrpPipelines.Both : pipelines;
        }

        private static SrpPipelines ShaderPipelines(string content)
        {
            var tags = RenderPipelineTag.Matches(content).Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            // An untagged SubShader runs on any pipeline, so the shader is only limited when every one is tagged.
            if (tags.Length == 0 || tags.Length < SubShader.Matches(content).Count) return SrpPipelines.Both;
            SrpPipelines pipelines = SrpPipelines.None;
            foreach (string tag in tags)
            {
                if (tag == "UniversalPipeline") pipelines |= SrpPipelines.URP;
                else if (tag == "HDRenderPipeline") pipelines |= SrpPipelines.HDRP;
                else return SrpPipelines.Both;
            }
            return pipelines;
        }

        private static List<string> FindUsers(string includePath, string[] files, Dictionary<string, string> text,
            Dictionary<string, string> guids)
        {
            string name = Path.GetFileName(includePath);
            guids.TryGetValue(includePath, out string guid);
            var users = new List<string>();
            foreach (string path in files)
            {
                if (path == includePath || !text.TryGetValue(path, out string content)) continue;
                if (IsGraph(path))
                {
                    if (guid != null && content.IndexOf(guid, StringComparison.OrdinalIgnoreCase) >= 0) users.Add(path);
                }
                else if (Has(path, ".shader") || Has(path, ".hlsl") || Has(path, ".cginc"))
                {
                    if (Include.Matches(content).Cast<Match>().Any(m =>
                            string.Equals(Path.GetFileName(m.Groups[1].Value), name, StringComparison.OrdinalIgnoreCase)))
                        users.Add(path);
                }
            }
            return users;
        }

        private static SrpPipelines IncludePipelines(string path, Dictionary<string, List<string>> users,
            Dictionary<string, SrpPipelines> known, HashSet<string> visiting)
        {
            if (!visiting.Add(path)) return SrpPipelines.None;
            SrpPipelines pipelines = SrpPipelines.None;
            bool anyUser = false;
            foreach (string user in users[path])
            {
                SrpPipelines used = users.ContainsKey(user)
                    ? IncludePipelines(user, users, known, visiting)
                    : known.TryGetValue(user, out SrpPipelines p) ? p : SrpPipelines.Both;
                if (used == SrpPipelines.None) continue;
                anyUser = true;
                pipelines |= used;
            }
            visiting.Remove(path);
            return anyUser ? pipelines : SrpPipelines.Both;
        }

        private static Dictionary<string, SrpPipelines> CollectMaterialReferenceRoles(string[] files, Dictionary<string, string> text)
        {
            var roles = new Dictionary<string, SrpPipelines>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in files)
            {
                if (IsGraph(path) || Has(path, ".shader") || Has(path, ".hlsl") || Has(path, ".cginc") ||
                    !text.TryGetValue(path, out string content)) continue;
                bool hdrpReplacesMaterial = LiveHdrpMaterial.IsMatch(content);
                bool hdrpReplacesSecondPass = LiveHdrpSecondPass.IsMatch(content);
                foreach (Match match in Reference.Matches(content))
                {
                    string field = match.Groups[1].Value;
                    string guid = match.Groups[2].Value.ToLowerInvariant();
                    SrpPipelines role =
                        field == "_HDRPMaterial" || field == "_HDRPSecondPass" ? SrpPipelines.HDRP :
                        (field == "_material" && hdrpReplacesMaterial) || (field == "_secondPass" && hdrpReplacesSecondPass)
                            ? SrpPipelines.URP : SrpPipelines.Both;
                    roles[guid] = roles.TryGetValue(guid, out SrpPipelines existing) ? existing | role : role;
                }
            }
            return roles;
        }

        private static SrpPipelines PipelinesOfShader(string shaderPath, Dictionary<string, SrpPipelines> known)
        {
            if (string.IsNullOrEmpty(shaderPath)) return SrpPipelines.Both;
            if (known.TryGetValue(shaderPath, out SrpPipelines pipelines)) return pipelines;
            if (shaderPath.StartsWith("Packages/com.unity.render-pipelines.universal/", StringComparison.OrdinalIgnoreCase))
                return SrpPipelines.URP;
            if (shaderPath.StartsWith("Packages/com.unity.render-pipelines.high-definition/", StringComparison.OrdinalIgnoreCase))
                return SrpPipelines.HDRP;
            return SrpPipelines.Both;
        }

        private static bool IsTextKind(string path) =>
            IsGraph(path) || Has(path, ".shader") || Has(path, ".hlsl") || Has(path, ".cginc") ||
            Has(path, ".mat") || Has(path, ".asset") || Has(path, ".prefab") || Has(path, ".unity");

        private static bool IsGraph(string path) => Has(path, ".shadergraph") || Has(path, ".shadersubgraph");

        private static bool Has(string path, string extension) => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }
}
