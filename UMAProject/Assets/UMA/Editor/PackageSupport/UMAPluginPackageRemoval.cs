using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.PackageSupport
{
    /// <summary>Removes only unchanged manifest-owned plugin files; resumes after interrupted editor sessions.</summary>
    [InitializeOnLoad]
    public static class UMAPluginPackageRemoval
    {
        [Serializable] private sealed class Removal
        {
            public UMAContentKind kind;
            public UMAContentManifest manifest;
            public List<string> preserved = new();
        }
        private static string Journal => Path.GetFullPath("Library/UMA/PluginRemoval/pending.json");
        static UMAPluginPackageRemoval() { EditorApplication.delayCall += Resume; }

        public static void RemoveInteractive(UMAContentKind kind)
        {
            if (!CanRemove(kind, out var manifest, out string error))
            {
                EditorUtility.DisplayDialog("Remove plugin", error, "OK"); return;
            }
            int unchanged = manifest.assets.Count(a => a.bytes > 0 && Matches(a));
            if (EditorUtility.DisplayDialog("Remove " + UMAContentCatalog.DisplayName(kind),
                "Remove " + unchanged + " unchanged package files from " + UMAContentCatalog.Root(kind) +
                "? Modified and unowned files, settings, documents, recovery, and exports are preserved.", "Remove", "Cancel"))
            {
                if (!TryRemove(kind, out error)) Debug.LogError("[UMA Plugins] " + error);
            }
        }

        public static bool CanRemove(UMAContentKind kind, out UMAContentManifest manifest, out string error)
        {
            manifest = null;
            if (!UMAContentCatalog.IsPlugin(kind)) { error = "Only optional plugins can be removed here."; return false; }
            if (File.Exists(Journal) || File.Exists("Library/UMA/ContentInstaller/pending.json"))
            { error = "A package transaction is already pending."; return false; }
            var companions = UMAContentCatalog.Companions(kind).Where(child => Directory.Exists(UMAContentCatalog.Root(child)) &&
                Directory.EnumerateFiles(UMAContentCatalog.Root(child), "*", SearchOption.AllDirectories)
                    .Any(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))).ToArray();
            if (companions.Length > 0)
            { error = "Remove " + string.Join(" and ", companions.Select(UMAContentCatalog.DisplayName)) + " before removing " + UMAContentCatalog.DisplayName(kind) + "."; return false; }
            return UMAContentPackageArchiveValidator.TryReadInstalledManifest(kind, out manifest, out error);
        }

        public static bool TryRemove(UMAContentKind kind, out string error)
        {
            if (!CanRemove(kind, out var manifest, out error)) return false;
            var removal = new Removal { kind = kind, manifest = manifest };
            Directory.CreateDirectory(Path.GetDirectoryName(Journal));
            File.WriteAllText(Journal, JsonUtility.ToJson(removal, true));
            try { Execute(removal); return true; }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        private static void Resume()
        {
            if (!File.Exists(Journal)) return;
            try
            {
                var removal = JsonUtility.FromJson<Removal>(File.ReadAllText(Journal));
                if (removal == null || !UMAContentCatalog.IsPlugin(removal.kind) ||
                    !UMAContentPackageArchiveValidator.TryValidateManifestStructure(removal.manifest, removal.kind, out _))
                    throw new InvalidDataException("Invalid plugin removal journal; no files were changed.");
                Execute(removal);
            }
            catch (Exception exception) { Debug.LogError("[UMA Plugins] Removal recovery stopped: " + exception.Message); }
        }

        private static bool Matches(UMAContentManifestAsset asset) =>
            File.Exists(asset.path) && File.Exists(asset.path + ".meta") &&
            string.Equals(Hash(asset.path), asset.sha256, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Hash(asset.path + ".meta"), asset.metaSha256, StringComparison.OrdinalIgnoreCase);

        private static string Hash(string path)
        {
            using var algorithm = SHA256.Create(); using var stream = File.OpenRead(path);
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static void Execute(Removal removal)
        {
            string root = UMAContentCatalog.Root(removal.kind);
            // The validated manifest constrains every path to this feature root.
            var directories = new Stack<string>();
            if (Directory.Exists(root)) directories.Push(root);
            while (directories.Count > 0)
            {
                string directory = directories.Pop();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Plugin removal does not follow junctions or symbolic links: " + directory);
                foreach (string path in Directory.GetFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Plugin removal does not follow junctions or symbolic links: " + path);
                    if ((attributes & FileAttributes.Directory) != 0) directories.Push(path);
                }
            }
            EditorApplication.LockReloadAssemblies();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var asset in removal.manifest.assets.Where(a => a.bytes > 0))
                {
                    if (!File.Exists(asset.path) && !File.Exists(asset.path + ".meta")) continue;
                    if (!Matches(asset))
                    {
                        if (!removal.preserved.Contains(asset.path)) removal.preserved.Add(asset.path);
                        File.WriteAllText(Journal, JsonUtility.ToJson(removal, true));
                        continue;
                    }
                    if (!AssetDatabase.DeleteAsset(asset.path)) throw new IOException("Could not remove " + asset.path);
                }
                foreach (var folder in removal.manifest.assets.Where(a => a.bytes == 0).OrderByDescending(a => a.path.Length))
                    if (Directory.Exists(folder.path) && !Directory.EnumerateFileSystemEntries(folder.path).Any() &&
                        File.Exists(folder.path + ".meta") && Hash(folder.path + ".meta") == folder.metaSha256)
                        AssetDatabase.DeleteAsset(folder.path);
                AssetDatabase.DeleteAsset(UMAContentCatalog.ManifestPath(removal.kind));
                UMAPluginDocumentationRegistry.Unregister(UMAContentCatalog.Id(removal.kind));
                if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) AssetDatabase.DeleteAsset(root);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Journal), "last-removal.json"), JsonUtility.ToJson(removal, true));
                File.Delete(Journal);
            }
            finally { AssetDatabase.StopAssetEditing(); EditorApplication.UnlockReloadAssemblies(); AssetDatabase.Refresh(); }
            Debug.Log("[UMA Plugins] Removed unchanged " + UMAContentCatalog.DisplayName(removal.kind) +
                " files. Preserved modified files: " + removal.preserved.Count + ".");
        }
    }
}
