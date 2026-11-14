using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors.PackageSupport
{
    public static class UMASrpPackageBuilder
    {
        private const string Root = UMASrpPackageArchiveValidator.SrpRoot;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public sealed class MaterialReferenceIssue
        {
            public string Path { get; }
            public string Reason { get; }

            internal MaterialReferenceIssue(string path, string reason) { Path = path; Reason = reason; }
            public override string ToString() => Path + "\n  " + Reason;
        }

        [Serializable]
        private sealed class Manifest
        {
            public string pipeline;
            public int formatVersion = 2;
            public string umaVersion;
            public string[] requiredPaths;
            public string[] sharedPaths;
            public string[] ownedPaths;
        }

        private sealed class Source
        {
            public Manifest manifest;
            public string manifestPath, manifestGuid, manifestMeta, json;
            public Dictionary<string, string> guids;
            public Dictionary<string, string> exportedText;
        }

        [MenuItem("UMA/Build/Build Active SRP Package", priority = 120)]
        private static void BuildMenu() => RunMenu(true);

        [MenuItem("UMA/Build/Rebuild Active SRP Manifest", priority = 121)]
        private static void ManifestMenu() => RunMenu(false);

        [MenuItem("UMA/Build/Build Active SRP Package", true)]
        [MenuItem("UMA/Build/Rebuild Active SRP Manifest", true)]
        private static bool CanBuild() => !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
            !EditorApplication.isPlayingOrWillChangePlaymode &&
            !File.Exists("Library/UMA/ContentInstaller/pending.json") &&
            !File.Exists("Library/UMA/PluginRemoval/pending.json");

        public static string ActivePipeline()
        {
            for (Type type = GraphicsSettings.currentRenderPipeline?.GetType(); type != null; type = type.BaseType)
            {
                if (type.FullName == "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset") return "URP";
                if (type.FullName == "UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset") return "HDRP";
            }
            return null;
        }

        private static void RunMenu(bool package)
        {
            string pipeline = ActivePipeline();
            if (pipeline == null)
            {
                int choice = EditorUtility.DisplayDialogComplex("Choose SRP package",
                    "The active render pipeline is Built-in or could not be identified. Which SRP source is in Assets/UMA/SRP?",
                    "URP", "Cancel", "HDRP");
                if (choice == 1) return;
                pipeline = choice == 0 ? "URP" : "HDRP";
            }
            try
            {
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayProgressBar("Build UMA " + pipeline,
                    package ? "Preparing and validating the SRP archive" : "Rebuilding the SRP manifest", 0f);
                string[] excluded = Array.Empty<string>();
                IReadOnlyList<MaterialReferenceIssue> manifestIssues = Array.Empty<MaterialReferenceIssue>();
                string path = package ? Build(pipeline, confirmExclusion: issues =>
                {
                    EditorUtility.ClearProgressBar();
                    if (!EditorUtility.DisplayDialog("Exclude broken UMA materials?",
                            issues.Count + " UMAMaterial(s) reference missing or unpackaged materials:\n\n" +
                            string.Join("\n\n", issues) +
                            "\n\nExclude these UMAMaterials from this package and its manifest? Source files will be preserved. " +
                            "The build will still stop if other exported assets reference an excluded UMAMaterial.",
                            "Exclude and Build", "Cancel")) return false;
                    excluded = issues.Select(issue => issue.Path).ToArray();
                    EditorUtility.DisplayProgressBar("Build UMA " + pipeline, "Building and validating the SRP archive", 0f);
                    return true;
                }) : RebuildManifest(pipeline, out manifestIssues);
                string exclusions = excluded.Length == 0 ? string.Empty :
                    "\n\nExcluded " + excluded.Length + " UMAMaterial(s). Source files were preserved.";
                Debug.Log("[UMA SRP] Built " + path);
                if (excluded.Length > 0) Debug.Log("[UMA SRP] Excluded from export:\n" + string.Join("\n", excluded));
                EditorUtility.ClearProgressBar();
                if (manifestIssues.Count > 0)
                {
                    if (EditorUtility.DisplayDialog("UMA " + pipeline + " manifest rebuilt with warnings",
                            path + "\n\n" + MaterialReferenceWarning(pipeline, manifestIssues), "Open Folder", "Close"))
                        EditorUtility.RevealInFinder(path);
                }
                else if (EditorUtility.DisplayDialog("UMA " + pipeline + " build complete", path + exclusions, "Open Folder", "Close"))
                    EditorUtility.RevealInFinder(path);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("UMA SRP build failed", exception.Message, "OK");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        public static string RebuildManifest(string pipeline) => RebuildManifest(pipeline, out _);

        /// <summary>
        /// Rebuilds the manifest and reports the UMAMaterials whose active material would not be in the package
        /// (missing, or outside Assets/UMA/SRP). The manifest only lists files under the SRP folder, so it is
        /// written either way; a package build stops on these and offers to exclude them.
        /// </summary>
        public static string RebuildManifest(string pipeline, out IReadOnlyList<MaterialReferenceIssue> materialIssues)
        {
            Source source = Prepare(pipeline);
            materialIssues = FindMaterialReferenceIssues(source).AsReadOnly();
            PublishManifest(source, null);
            if (materialIssues.Count > 0) Debug.LogWarning("[UMA SRP] " + MaterialReferenceWarning(pipeline, materialIssues));
            return Path.GetFullPath(source.manifestPath);
        }

        internal static string MaterialReferenceWarning(string pipeline, IReadOnlyList<MaterialReferenceIssue> issues) =>
            issues.Count + " UMAMaterial(s) in the " + pipeline + " manifest use a material that is missing or outside " +
            Root + ", so it is not in the package. Building the package will ask to exclude them. Move the material into " +
            Root + " (keep its GUID by moving it in Unity) or assign one that is already there.\n\n" + string.Join("\n\n", issues);

        public static string Build(string pipeline, string outputDirectory = Root,
            Func<IReadOnlyList<MaterialReferenceIssue>, bool> confirmExclusion = null)
        {
            Source source = Prepare(pipeline);
            ResolveMaterialReferences(source, confirmExclusion);
            string output = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(output);
            string target = Path.Combine(output, "UMA" + pipeline + ".unitypackage");
            string temporary = Path.Combine(Path.GetFullPath("Library/UMA/SrpBuild"),
                Guid.NewGuid().ToString("N") + ".unitypackage");
            Directory.CreateDirectory(Path.GetDirectoryName(temporary));
            bool validated = false;
            try
            {
                WriteArchive(source, temporary);
                if (!UMASrpPackageArchiveValidator.TryValidate(temporary, pipeline, out var archive, out string error))
                    throw new InvalidDataException(error);
                var includedGuids = new HashSet<string>(source.guids.Values, StringComparer.OrdinalIgnoreCase);
                foreach (var references in archive.ReferencedGuidsByPath)
                    foreach (string guid in references.Value)
                    {
                        string dependency = AssetDatabase.GUIDToAssetPath(guid);
                        if (dependency.StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase) && !includedGuids.Contains(guid))
                            throw new InvalidDataException(references.Key + " references excluded SRP source: " + dependency);
                    }
                validated = true;
                PublishManifest(source, () => UMAPluginPackageBuilder.PublishValidatedArchive(temporary, target));
                AssetDatabase.Refresh();
                return target;
            }
            catch (Exception exception)
            {
                throw new IOException("Could not build UMA" + pipeline + ": " + exception.Message +
                    (validated && File.Exists(temporary) ? "\nValidated archive preserved at: " + temporary : ""), exception);
            }
            finally
            {
                if (!validated && File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void WriteArchive(Source source, string destination)
        {
            using var file = File.Create(destination);
            using var gzip = new GZipStream(file, System.IO.Compression.CompressionLevel.Optimal);
            foreach (string path in source.manifest.ownedPaths)
            {
                string guid = path == source.manifestPath ? source.manifestGuid : source.guids[path];
                if (path == source.manifestPath)
                {
                    UMAPluginPackageBuilder.WriteText(gzip, guid + "/asset", source.json);
                    UMAPluginPackageBuilder.WriteText(gzip, guid + "/asset.meta", source.manifestMeta);
                }
                else
                {
                    if (source.exportedText.TryGetValue(path, out string text))
                        UMAPluginPackageBuilder.WriteText(gzip, guid + "/asset", text);
                    else if (File.Exists(path)) UMAPluginPackageBuilder.WriteFile(gzip, guid + "/asset", path);
                    UMAPluginPackageBuilder.WriteFile(gzip, guid + "/asset.meta", path + ".meta");
                }
                UMAPluginPackageBuilder.WriteText(gzip, guid + "/pathname", path);
            }
            gzip.Write(new byte[1024], 0, 1024);
        }

        private static Source Prepare(string pipeline)
        {
            if (pipeline != "URP" && pipeline != "HDRP") throw new ArgumentException("Choose URP or HDRP.");
            if (!CanBuild()) throw new InvalidOperationException("Wait for the current import, compilation, or package operation to finish.");
            var settings = UMASettings.GetSettings();
            if (settings == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(settings)))
                throw new InvalidOperationException("An installed UMASettings asset is required.");
            UMAPackageVersionUtility.Normalize(settings.UMAVersion, out string version);
            string manifestPath = Root + "/UMA" + pipeline + "Manifest.json";
            var owned = new SortedSet<string>(StringComparer.Ordinal);
            var shared = new SortedSet<string>(StringComparer.Ordinal);
            var exportedText = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in EnumerateSourceFiles(Root))
            {
                if (!IncludeFile(file, pipeline)) continue;
                var paths = IsShared(file) ? shared : owned;
                if (paths == owned)
                {
                    string text = file.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase)
                        ? UMASrpPackageSource.ExportShaderGraph(file, pipeline)
                        : UMASrpPackageSource.ExportYaml(file, pipeline);
                    if (text == string.Empty) continue;
                    if (text != null) exportedText.Add(file, text);
                }
                paths.Add(file);
                for (string parent = Path.GetDirectoryName(file).Replace('\\', '/'); parent != Root;
                     parent = Path.GetDirectoryName(parent).Replace('\\', '/')) paths.Add(parent);
            }
            owned.Add(manifestPath);
            var required = new List<string>
            {
                Root + "/Textures/ReallyWhite.png", Root + "/Environment/U3Environment.prefab",
                Root + "/ShaderGraphs/Graphs/UMA3_SkinShader_" + pipeline + ".shadergraph"
            };
            if (pipeline == "URP") required.Add(Root + "/ShaderGraphs/Materials/UMA3_SkinShader_URP.asset");
            else required.AddRange(new[] { Root + "/HDRPSetup/UMAHDRPSetup.cs",
                Root + "/HDRPSetup/UMAHDRPSetup.prefab", Root + "/DiffusionProfiles/UMAEye.asset" });
            required.AddRange(owned.Where(p => p.StartsWith(Root + "/Samples/Scenes/", StringComparison.Ordinal) &&
                p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)));
            foreach (string path in required)
                if (!File.Exists(path) || (!owned.Contains(path) && !shared.Contains(path)))
                    throw new FileNotFoundException("The " + pipeline + " source is incomplete. Missing required asset: " + path);
            if (shared.Count == 0) throw new InvalidDataException("The shared SRP textures are missing.");
            string manifestGuid = pipeline == "URP" ? "d890b9061184417696a598c9ae128bcb" : "cf83166aa5544ca18bdbbbdfcc19d771";
            string metaPath = manifestPath + ".meta";
            string manifestMeta = File.Exists(metaPath) ? File.ReadAllText(metaPath) :
                "fileFormatVersion: 2\nguid: " + manifestGuid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
            if (ReadGuid(manifestMeta, metaPath) != manifestGuid)
                throw new InvalidDataException("The manifest metadata has an unexpected GUID: " + metaPath);
            var guids = new Dictionary<string, string>(StringComparer.Ordinal);
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { manifestGuid };
            foreach (string path in owned.Concat(shared).Where(p => p != manifestPath))
            {
                if (!File.Exists(path + ".meta")) throw new FileNotFoundException("Import this source in Unity before building: " + path);
                string guid = ReadGuid(File.ReadAllText(path + ".meta"), path);
                if (!unique.Add(guid)) throw new InvalidDataException("Duplicate source GUID: " + path);
                guids.Add(path, guid);
            }
            var manifest = new Manifest { pipeline = pipeline, umaVersion = version,
                ownedPaths = owned.ToArray(), sharedPaths = shared.ToArray(), requiredPaths = required.ToArray() };
            return new Source { manifest = manifest, manifestPath = manifestPath, manifestGuid = manifestGuid,
                manifestMeta = manifestMeta, json = JsonUtility.ToJson(manifest, true) + "\n", guids = guids,
                exportedText = exportedText };
        }

        private static string ReadGuid(string meta, string path)
        {
            var matches = Regex.Matches(meta, @"(?m)^guid:\s*([a-fA-F0-9]{32})\s*$");
            if (matches.Count != 1) throw new InvalidDataException("Missing or ambiguous metadata GUID: " + path);
            return matches[0].Groups[1].Value.ToLowerInvariant();
        }

        private static void ValidateMaterialReferences(Source source)
        {
            var errors = FindMaterialReferenceIssues(source);
            if (errors.Count > 0) throw new InvalidDataException(MaterialReferenceError(source, errors));
        }

        private static string MaterialReferenceError(Source source, IReadOnlyList<MaterialReferenceIssue> issues) =>
            "Cannot build " + source.manifest.pipeline + ": " + issues.Count +
            " UMA material(s) reference missing or unpackaged materials. Repair their material assignments or use the build menu to exclude them.\n\n" +
            string.Join("\n\n", issues);

        private static void ResolveMaterialReferences(Source source,
            Func<IReadOnlyList<MaterialReferenceIssue>, bool> confirmExclusion)
        {
            var issues = FindMaterialReferenceIssues(source);
            if (issues.Count == 0) return;
            if (confirmExclusion == null) throw new InvalidDataException(MaterialReferenceError(source, issues));
            var excluded = new HashSet<string>(issues.Select(issue => issue.Path), StringComparer.Ordinal);
            if (source.manifest.requiredPaths.Any(excluded.Contains))
                throw new InvalidDataException("Required SRP assets cannot be excluded.\n\n" + MaterialReferenceError(source, issues));
            if (!confirmExclusion(issues.AsReadOnly())) throw new OperationCanceledException("SRP package build cancelled.");
            var retained = source.manifest.ownedPaths.Where(path => !excluded.Contains(path)).ToArray();
            foreach (string path in retained.OrderByDescending(path => path.Length))
                if (Directory.Exists(path) && !retained.Any(child => !excluded.Contains(child) && child.StartsWith(path + "/", StringComparison.Ordinal)))
                    excluded.Add(path);
            source.manifest.ownedPaths = retained.Where(path => !excluded.Contains(path)).ToArray();
            foreach (string path in excluded)
            {
                source.guids.Remove(path);
                source.exportedText.Remove(path);
            }
            source.json = JsonUtility.ToJson(source.manifest, true) + "\n";
            ValidateMaterialReferences(source);
        }

        private static List<MaterialReferenceIssue> FindMaterialReferenceIssues(Source source)
        {
            var included = new HashSet<string>(source.guids.Values, StringComparer.OrdinalIgnoreCase);
            var errors = new List<MaterialReferenceIssue>();
            foreach (string path in source.manifest.ownedPaths.Where(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)))
            {
                string text = source.exportedText.TryGetValue(path, out string exported) ? exported : UMASrpPackageSource.ReadYaml(path);
                if (text == null || !Regex.IsMatch(text, @"(?m)^  _HDRPMaterial:")) continue;
                var active = Regex.Match(text, source.manifest.pipeline == "HDRP"
                    ? @"(?m)^  _HDRPMaterial:\s*\{[^\r\n]*guid:\s*([0-9a-fA-F]{32})"
                    : @"(?m)^  _material:\s*\{[^\r\n]*guid:\s*([0-9a-fA-F]{32})");
                if (!active.Success && source.manifest.pipeline == "HDRP")
                    active = Regex.Match(text, @"(?m)^  _material:\s*\{[^\r\n]*guid:\s*([0-9a-fA-F]{32})");
                string guid = active.Success ? active.Groups[1].Value : string.Empty;
                if (included.Contains(guid)) continue;
                string dependency = string.IsNullOrEmpty(guid) ? "No active material assigned" : AssetDatabase.GUIDToAssetPath(guid);
                errors.Add(new MaterialReferenceIssue(path, string.IsNullOrEmpty(dependency) ? "Missing material GUID: " + guid : dependency));
            }
            return errors;
        }

        private static bool IsShared(string path) => path.StartsWith(Root + "/Textures/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(Root + "/ShaderPackages/", StringComparison.OrdinalIgnoreCase);

        private static bool IncludeFile(string path, string pipeline)
        {
            string name = Path.GetFileName(path);
            if (path.Split('/').Any(part => part.StartsWith(".", StringComparison.Ordinal)) ||
                name.EndsWith("~", StringComparison.Ordinal) ||
                new[] { ".meta", ".unitypackage", ".zip", ".tmp", ".previous" }.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) ||
                name.Equals("UMAURPManifest.json", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("UMAHDRPManifest.json", StringComparison.OrdinalIgnoreCase)) return false;
            if (IsShared(path)) return true;
            if (pipeline == "URP")
                return path.IndexOf("HDRP", StringComparison.OrdinalIgnoreCase) < 0 &&
                    !path.StartsWith(Root + "/DiffusionProfiles/", StringComparison.OrdinalIgnoreCase) &&
                    !path.StartsWith(Root + "/Objects/", StringComparison.OrdinalIgnoreCase) &&
                    path != Root + "/SampleSceneProfile.asset";
            return path.IndexOf("URP", StringComparison.OrdinalIgnoreCase) < 0 &&
                !path.StartsWith(Root + "/Settings/", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateSourceFiles(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("SRP builds do not follow symbolic links: " + directory);
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("SRP builds do not follow symbolic links: " + path);
                if (Directory.Exists(path))
                {
                    foreach (string file in EnumerateSourceFiles(path)) yield return file;
                }
                else yield return path.Replace('\\', '/');
            }
        }

        private static void PublishManifest(Source source, Action publishPackage)
        {
            string path = source.manifestPath;
            byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            byte[] previousMeta = File.Exists(path + ".meta") ? File.ReadAllBytes(path + ".meta") : null;
            try
            {
                File.WriteAllText(path, source.json, Utf8);
                if (previousMeta == null) File.WriteAllText(path + ".meta", source.manifestMeta, Utf8);
                if (!UMASrpPackageArchiveValidator.TryValidateInstalledSupport(source.manifest.pipeline, out string error))
                    throw new InvalidDataException(error);
                publishPackage?.Invoke();
            }
            catch
            {
                if (previous != null) File.WriteAllBytes(path, previous);
                else if (File.Exists(path)) File.Delete(path);
                if (previousMeta == null && File.Exists(path + ".meta")) File.Delete(path + ".meta");
                throw;
            }
            finally { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); }
        }
    }
}
