using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    public enum UmaUnusedAssetKind { UMAMaterial, Material, Shader }

    public sealed class UmaUnusedAssetResult
    {
        public string Guid { get; internal set; }
        public string Path { get; internal set; }
        public string Name { get; internal set; }
        internal Hash128 ContentHash { get; set; }
        internal string AssetTypeName { get; set; }
        public UmaUnusedAssetKind Kind { get; internal set; }
        public string Protection { get; internal set; }
        public readonly List<string> References = new List<string>();
        public readonly List<string> RegisteredIn = new List<string>();
        public bool IsUnused => string.IsNullOrEmpty(Protection) && References.Count == 0;
    }

    /// <summary>
    /// Conservative incoming-reference search, not build reachability analysis. A dependency
    /// remains used even when its owner is itself unused; cleanup never cascades implicitly.
    /// All native/binary assets are inspected through Unity APIs, never decoded as text.
    /// </summary>
    public sealed class UmaUnusedAssetScan
    {
        public readonly List<UmaUnusedAssetResult> Results = new List<UmaUnusedAssetResult>();
        public bool Complete { get; private set; }
        public int Processed { get; private set; }
        public int Total { get; private set; }
        private readonly string scope;
        private readonly Dictionary<string, UmaUnusedAssetResult> byPath =
            new Dictionary<string, UmaUnusedAssetResult>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<UmaUnusedAssetResult>> byShaderName =
            new Dictionary<string, List<UmaUnusedAssetResult>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<UmaUnusedAssetResult>> byName =
            new Dictionary<string, List<UmaUnusedAssetResult>>(StringComparer.Ordinal);
        private static readonly Regex ShaderFind = new Regex(
            @"\bShader\s*\.\s*Find\s*\(\s*@?""([^""\r\n]+)""\s*\)", RegexOptions.Compiled);

        public UmaUnusedAssetScan(string folder = "Assets")
        {
            scope = UmaUnusedAssetUtility.Normalize(folder).TrimEnd('/');
            if (!UmaUnusedAssetUtility.IsAssetsFolder(scope))
                throw new ArgumentException("Choose an existing folder inside Assets.", nameof(folder));
        }

        public IEnumerable<string> Run()
        {
            Complete = false;
            Results.Clear();
            byPath.Clear();
            byShaderName.Clear();
            byName.Clear();
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string filter in new[] { "t:UMAMaterial", "t:Material", "t:Shader" })
                foreach (string guid in AssetDatabase.FindAssets(filter, new[] { scope }))
                    candidates.Add(AssetDatabase.GUIDToAssetPath(guid));

            foreach (string path in candidates)
            {
                if (!UmaUnusedAssetUtility.TryGetKind(path, out UmaUnusedAssetKind kind)) continue;
                Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null) throw new InvalidOperationException("Could not inspect " + path);
                var result = new UmaUnusedAssetResult
                {
                    Guid = AssetDatabase.AssetPathToGUID(path), Path = path, Name = asset.name, Kind = kind,
                    ContentHash = AssetDatabase.GetAssetDependencyHash(path),
                    AssetTypeName = asset.GetType().Name,
                    Protection = UmaUnusedAssetUtility.GetProtection(path)
                };
                Results.Add(result);
                byPath.Add(path, result);
                if (!byName.TryGetValue(asset.name, out var named))
                    byName.Add(asset.name, named = new List<UmaUnusedAssetResult>());
                named.Add(result);
                if (kind == UmaUnusedAssetKind.Shader)
                {
                    if (!byShaderName.TryGetValue(asset.name, out var shaders))
                        byShaderName.Add(asset.name, shaders = new List<UmaUnusedAssetResult>());
                    shaders.Add(result);
                }
                yield return "Candidates: " + path;
            }

            var owners = new List<string>();
            foreach (string path in AssetDatabase.GetAllAssetPaths())
                if ((path.StartsWith("Assets/", StringComparison.Ordinal) ||
                     path.StartsWith("Packages/", StringComparison.Ordinal)) && !AssetDatabase.IsValidFolder(path))
                    owners.Add(path);
            // Settings are not returned by GetAllAssetPaths. Use Unity serialization for them too.
            foreach (string file in Directory.GetFiles("ProjectSettings", "*.asset"))
                owners.Add(UmaUnusedAssetUtility.Normalize(file));
            Total = owners.Count;
            Processed = 0;
            foreach (string owner in owners)
            {
                Type type = AssetDatabase.GetMainAssetTypeAtPath(owner);
                bool isRegistry = type != null && typeof(UMAAssetIndexer).IsAssignableFrom(type);
                // Even a direct _SerializedItem reference is registration, not actual usage.
                if (!isRegistry)
                    foreach (string dependency in AssetDatabase.GetDependencies(owner, false))
                        Reference(dependency, owner);

                if (isRegistry)
                    InspectRegistry(AssetDatabase.LoadAssetAtPath<UMAAssetIndexer>(owner), owner);
                else if (type != null && typeof(OverlayDataAsset).IsAssignableFrom(type))
                    InspectOverlay(AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(owner), owner);
                else if ((type != null && type.FullName != null &&
                          type.FullName.StartsWith("UnityEditor.AddressableAssets.", StringComparison.Ordinal)) ||
                         owner.StartsWith("ProjectSettings/", StringComparison.Ordinal))
                {
                    // Addressables store GUID strings (including folder entries), not ordinary
                    // Unity references. Inspect without a dependency on the optional package.
                    foreach (Object settings in AssetDatabase.LoadAllAssetsAtPath(owner))
                        InspectSerializedReferences(settings, owner);
                }
                else if (owner.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && byShaderName.Count > 0)
                {
                    // C# source is text; Unity .asset/.mat/.prefab/native files are NEVER read this way.
                    foreach (Match match in ShaderFind.Matches(File.ReadAllText(ResolveSourceFile(owner))))
                        if (byShaderName.TryGetValue(match.Groups[1].Value, out var shaders))
                            foreach (var shader in shaders) Reference(shader.Path, owner + " (Shader.Find literal)");
                }
                Processed++;
                yield return owner;
            }

            var sceneRoots = new List<Object>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) sceneRoots.AddRange(scene.GetRootGameObjects());
            }
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.prefabContentsRoot != null)
                sceneRoots.Add(prefabStage.prefabContentsRoot);
            ProtectDependencies(sceneRoots.ToArray(), "Open scene / Prefab Mode reference");
            ProtectDependencies(PlayerSettings.GetPreloadedAssets(), "Player Settings: preloaded asset");

            var dirtyAssets = new List<Object>();
            foreach (ScriptableObject asset in Resources.FindObjectsOfTypeAll<ScriptableObject>())
                if (EditorUtility.IsPersistent(asset) && EditorUtility.IsDirty(asset)) dirtyAssets.Add(asset);
            foreach (Material asset in Resources.FindObjectsOfTypeAll<Material>())
                if (EditorUtility.IsPersistent(asset) && EditorUtility.IsDirty(asset)) dirtyAssets.Add(asset);
            ProtectDependencies(dirtyAssets.ToArray(), "Unsaved asset / unsaved reference");
            Results.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
            Complete = true;
        }

        private static string ResolveSourceFile(string path)
        {
            if (!path.StartsWith("Packages/", StringComparison.Ordinal)) return path;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            if (package == null) throw new IOException("Unable to locate package source: " + path);
            return System.IO.Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length + 1));
        }

        private void Reference(string path, string owner)
        {
            if (path == owner || !byPath.TryGetValue(path, out var result)) return;
            if (!result.References.Contains(owner)) result.References.Add(owner);
        }

        private void ProtectPathOrFolder(string path, string reason)
        {
            if (byPath.TryGetValue(path, out var result))
                result.Protection = reason;
            else if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                foreach (var candidate in Results)
                    if (candidate.Path.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))
                        candidate.Protection = reason;
        }

        private void InspectRegistry(UMAAssetIndexer registry, string path)
        {
            if (registry == null) throw new InvalidOperationException("Unable to load UMA registry: " + path);
            if (registry.SerializedItems == null) return;
            foreach (AssetItem item in registry.SerializedItems)
            {
                if (item == null) continue;
                string registeredPath = item._SerializedItem != null ? AssetDatabase.GetAssetPath(item._SerializedItem) :
                    !string.IsNullOrEmpty(item._Guid) ? AssetDatabase.GUIDToAssetPath(item._Guid) : item._Path;
                if (!string.IsNullOrEmpty(registeredPath) && byPath.TryGetValue(registeredPath, out var candidate))
                    Register(candidate, item, path);
                if (!string.IsNullOrEmpty(item._Name) && byName.TryGetValue(item._Name, out var named))
                    foreach (var match in named) Register(match, item, path);
            }
        }

        private static void Register(UmaUnusedAssetResult candidate, AssetItem item, string path)
        {
            if (UmaUnusedAssetUtility.RegistrationMatches(item, candidate) && !candidate.RegisteredIn.Contains(path))
                candidate.RegisteredIn.Add(path);
        }

        private void InspectOverlay(OverlayDataAsset overlay, string owner)
        {
            // Stripped overlays can use the library by name. That IS a consumer, unlike registration.
            if (overlay != null && overlay.material == null && !string.IsNullOrEmpty(overlay.materialName) &&
                byName.TryGetValue(overlay.materialName, out var named))
                foreach (var candidate in named)
                    if (candidate.Kind == UmaUnusedAssetKind.UMAMaterial)
                        Reference(candidate.Path, owner + " (overlay materialName)");
        }

        private void InspectSerializedReferences(Object settings, string owner)
        {
            if (settings == null) return;
            using (var serialized = new SerializedObject(settings))
            {
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null)
                        Reference(AssetDatabase.GetAssetPath(property.objectReferenceValue), owner);
                    else if (property.propertyType == SerializedPropertyType.String)
                    {
                        string value = property.stringValue;
                        if (value != null && value.Length == 32)
                            ProtectPathOrFolder(AssetDatabase.GUIDToAssetPath(value), "GUID registration: " + owner);
                    }
                }
            }
        }

        private void ProtectDependencies(Object[] roots, string reason)
        {
            // CollectDependencies is recursive and cannot stop at a registry boundary. Walking
            // live references prevents a scene/preloaded/dirty index from protecting all its entries.
            var pending = new Stack<Object>(roots.Where(root => root != null));
            var visited = new HashSet<Object>();
            while (pending.Count > 0)
            {
                Object current = pending.Pop();
                if (current == null || !visited.Add(current) || current is UMAAssetIndexer) continue;
                string path = AssetDatabase.GetAssetPath(current);
                ProtectPathOrFolder(path, reason);
                if (current is OverlayDataAsset overlay) InspectOverlay(overlay, string.IsNullOrEmpty(path) ? reason : path);
                // Saved asset-to-asset edges have already been checked project-wide above.
                if (EditorUtility.IsPersistent(current) && !EditorUtility.IsDirty(current)) continue;
                if (current is GameObject go)
                {
                    foreach (Component component in go.GetComponents<Component>()) if (component != null) pending.Push(component);
                    foreach (Transform child in go.transform) pending.Push(child.gameObject);
                    continue;
                }
                using (var serialized = new SerializedObject(current))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null)
                            pending.Push(property.objectReferenceValue);
                }
            }
        }
    }

    public static class UmaUnusedAssetUtility
    {
        public static string Normalize(string path) => (path ?? string.Empty).Replace('\\', '/');

        public static bool IsAssetsFolder(string path) =>
            (path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal)) &&
            !path.Contains("/../") && !path.EndsWith("/..", StringComparison.Ordinal) && AssetDatabase.IsValidFolder(path);

        public static bool TryGetKind(string path, out UmaUnusedAssetKind kind)
        {
            kind = default;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                path.Contains("/../") || AssetDatabase.IsValidFolder(path)) return false;
            Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (type != null && typeof(UMAMaterial).IsAssignableFrom(type) && extension == ".asset")
                kind = UmaUnusedAssetKind.UMAMaterial;
            else if (type == typeof(Material) && extension == ".mat") kind = UmaUnusedAssetKind.Material;
            else if (type == typeof(Shader) && (extension == ".shader" || extension == ".shadergraph"))
                kind = UmaUnusedAssetKind.Shader;
            else return false;
            return true;
        }

        public static string GetProtection(string path)
        {
            if (!TryGetKind(path, out _)) return "Not a standalone supported asset under Assets";
            string writeProtection = GetWriteProtection(path);
            if (!string.IsNullOrEmpty(writeProtection)) return writeProtection;
            if (path.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("/Editor Default Resources/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("/StreamingAssets/", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Runtime/editor loading folder";
            if (!string.IsNullOrEmpty(AssetDatabase.GetImplicitAssetBundleName(path))) return "AssetBundle entry";
            if (AssetDatabase.LoadAllAssetRepresentationsAtPath(path).Length > 0) return "Contains sub-assets; keep the entire file";
            if (EditorUtility.IsDirty(AssetDatabase.LoadMainAssetAtPath(path))) return "Unsaved changes";
            return string.Empty;
        }

        private static string GetWriteProtection(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("/../"))
                return "Not a writable project asset under Assets";
            // Never remove content reached through a symlink/junction: it may belong outside Assets.
            for (string location = path; !string.IsNullOrEmpty(location); location = Path.GetDirectoryName(location))
                if ((File.GetAttributes(location) & FileAttributes.ReparsePoint) != 0)
                    return "Linked asset or folder; manage the original location explicitly";
            if (!AssetDatabase.IsOpenForEdit(path, StatusQueryOptions.UseCachedIfPossible) ||
                !AssetDatabase.IsOpenForEdit(path + ".meta", StatusQueryOptions.UseCachedIfPossible))
                return "Read-only / not checked out";
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0 ||
                (File.GetAttributes(path + ".meta") & FileAttributes.ReadOnly) != 0)
                return "Read-only asset or metadata";
            return string.Empty;
        }

        /// <summary>Last identity/type/write-safety check. Call only after a complete fresh scan and user confirmation.</summary>
        public static bool CanTrash(UmaUnusedAssetResult result, out string reason)
        {
            reason = "The asset is referenced or protected.";
            if (result == null || !result.IsUnused) return false;
            if (AssetDatabase.GUIDToAssetPath(result.Guid) != result.Path ||
                !TryGetKind(result.Path, out var kind) || kind != result.Kind ||
                AssetDatabase.GetAssetDependencyHash(result.Path) != result.ContentHash)
            {
                reason = "Asset moved, changed, or was replaced since scanning. Scan again.";
                return false;
            }
            reason = GetProtection(result.Path);
            if (!string.IsNullOrEmpty(reason)) return false;
            foreach (string path in result.RegisteredIn)
            {
                var registry = AssetDatabase.LoadAssetAtPath<UMAAssetIndexer>(path);
                if (registry == null) { reason = "UMA index moved or missing: " + path; return false; }
                string protection = GetWriteProtection(path);
                if (string.IsNullOrEmpty(protection) && EditorUtility.IsDirty(registry)) protection = "Save the index's unsaved changes first";
                if (!string.IsNullOrEmpty(protection)) { reason = path + ": " + protection; return false; }
                if (registry.SerializedItems != null && registry.SerializedItems.Any(item =>
                    RegistrationMatches(item, result) && IsNameOnly(item)) && HasNameCollision(result))
                {
                    reason = "Ambiguous name-only UMA registration. Resolve same-name assets in the index before deleting: " + result.Name;
                    return false;
                }
            }
            return true;
        }

        internal static bool RegistrationMatches(AssetItem item, UmaUnusedAssetResult result)
        {
            if (item == null) return false;
            // Strong identity takes precedence; never remove a different asset just for sharing a name.
            if (item._SerializedItem != null)
                return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item._SerializedItem)) == result.Guid;
            if (!string.IsNullOrEmpty(item._Guid)) return string.Equals(item._Guid, result.Guid, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(item._Path)) return string.Equals(Normalize(item._Path), result.Path, StringComparison.OrdinalIgnoreCase);
            string type = (item._BaseTypeName ?? string.Empty).Split(',')[0];
            type = type.Substring(type.LastIndexOf('.') + 1);
            return item._Name == result.Name && (type == result.AssetTypeName || type == result.Kind.ToString());
        }

        private static bool IsNameOnly(AssetItem item) => item._SerializedItem == null &&
            string.IsNullOrEmpty(item._Guid) && string.IsNullOrEmpty(item._Path);

        private static bool HasNameCollision(UmaUnusedAssetResult result)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + result.Kind))
                if (guid != result.Guid)
                {
                    Object asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                    if (asset != null && asset.name == result.Name) return true;
                }
            return false;
        }

        /// <summary>Call only for checked assets after a fresh scan and user confirmation.</summary>
        public static bool TrashAndUnregister(UmaUnusedAssetResult result, out string reason) =>
            TrashAndUnregisterCore(result, AssetDatabase.MoveAssetToTrash, out reason);

        private static bool TrashAndUnregisterCore(UmaUnusedAssetResult result, Func<string, bool> moveToTrash, out string reason)
        {
            if (!CanTrash(result, out reason)) return false;
            // Snapshot only affected indexes. No singleton access, healing, or saving unrelated assets.
            var edits = result.RegisteredIn.Select(path => new RegistryEdit(
                AssetDatabase.LoadAssetAtPath<UMAAssetIndexer>(path), result)).ToList();
            try
            {
                foreach (var edit in edits) edit.Apply();
                foreach (var edit in edits) edit.Save();
                if (!moveToTrash(result.Path)) throw new IOException("Unity could not move the asset to the trash.");
                reason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                // If Unity removed the file before throwing, do not resurrect dangling registrations.
                if (!File.Exists(result.Path))
                {
                    reason = "Asset removed; Unity reported: " + exception.Message;
                    return true;
                }
                var failures = new List<string>();
                foreach (var edit in edits)
                    try { edit.Restore(); edit.Save(); }
                    catch (Exception restoreError) { failures.Add(edit.Path + ": " + restoreError.Message); }
                reason = exception.Message + (failures.Count == 0 ? " Asset kept; index entries restored." :
                    " Asset kept, but index restoration needs attention: " + string.Join("; ", failures));
                return false;
            }
        }

        private sealed class RegistryEdit
        {
            private readonly UMAAssetIndexer registry;
            private readonly UmaUnusedAssetResult target;
            private readonly List<AssetItem> original;
            private readonly int[] originalPositions;
            private readonly List<(Dictionary<string, AssetItem> dictionary, string key, AssetItem value)> removedLookups =
                new List<(Dictionary<string, AssetItem>, string, AssetItem)>();
            public string Path => AssetDatabase.GetAssetPath(registry);

            public RegistryEdit(UMAAssetIndexer registry, UmaUnusedAssetResult target)
            {
                this.registry = registry;
                this.target = target;
                original = registry.SerializedItems == null ? new List<AssetItem>() : new List<AssetItem>(registry.SerializedItems);
                originalPositions = original.Select(item => item == null ? -1 : item.Index).ToArray();
            }

            public void Apply()
            {
                registry.SerializedItems = original.Where(item => !RegistrationMatches(item, target)).ToList();
                for (int i = 0; i < registry.SerializedItems.Count; i++)
                    if (registry.SerializedItems[i] != null) registry.SerializedItems[i].Index = i;
                // Prune live name/GUID caches too, without RebuildIndex (which heals/renames unrelated entries).
                foreach (Type type in registry.GetIndexedTypeValues().Distinct()) Prune(registry.GetAssetDictionary(type));
                Prune(registry.GuidTypes);
                EditorUtility.SetDirty(registry);
            }

            private void Prune(Dictionary<string, AssetItem> dictionary)
            {
                foreach (var pair in dictionary.Where(pair => RegistrationMatches(pair.Value, target)).ToArray())
                {
                    removedLookups.Add((dictionary, pair.Key, pair.Value));
                    dictionary.Remove(pair.Key);
                }
            }

            public void Restore()
            {
                registry.SerializedItems = original;
                for (int i = 0; i < original.Count; i++) if (original[i] != null) original[i].Index = originalPositions[i];
                foreach (var lookup in removedLookups) lookup.dictionary[lookup.key] = lookup.value;
                EditorUtility.SetDirty(registry);
            }

            public void Save()
            {
                AssetDatabase.SaveAssetIfDirty(registry);
                if (EditorUtility.IsDirty(registry)) throw new IOException("Unable to save UMA index: " + Path);
            }
        }
    }
}
