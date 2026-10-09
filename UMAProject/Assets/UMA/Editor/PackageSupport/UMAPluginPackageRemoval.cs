using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.PackageSupport
{
    /// <summary>Removes manifest-owned plugin files using the confirmed policy; resumes after interrupted editor sessions.</summary>
    [InitializeOnLoad]
    public static class UMAPluginPackageRemoval
    {
        [Serializable] private sealed class Removal
        {
            public UMAContentKind kind;
            public UMAContentManifest manifest;
            public List<string> preserved = new();
        }
        [Serializable] private sealed class RemovalBatch
        {
            public int formatVersion = 1;
            public UMAContentKind kind;
            public List<Removal> removals = new();
            public int next;
            public bool removeModified;
            public bool allPlugins;
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
            int choice = EditorUtility.DisplayDialogComplex("Remove " + UMAContentCatalog.DisplayName(kind),
                "Remove " + unchanged + " unchanged package files from " + UMAContentCatalog.Root(kind) +
                "?\n\nContinue preserves modified files. Continue - Remove All also deletes modified package-owned files, their metadata, and empty folders. " +
                "Unowned files and user settings, documents, recovery, and exports are preserved with either choice.",
                "Continue", "Cancel", "Continue - Remove All");
            if (choice != 1)
            {
                if (!TryRemove(kind, out error, choice == 2)) Debug.LogError("[UMA Plugins] " + error);
            }
        }

        public static void RemoveAllInteractive(UMAContentKind kind)
        {
            if (!TryCreateBatch(kind, out var batch, out string error))
            {
                EditorUtility.DisplayDialog("Remove plugins", error, "OK"); return;
            }
            ConfirmRemovalBatch(batch, "Remove All — " + UMAContentCatalog.DisplayName(kind));
        }

        public static void RemoveAllInteractive()
        {
            if (!TryCreateAllPluginsBatch(out var batch, out string error))
            {
                EditorUtility.DisplayDialog("Remove plugins", error, "OK"); return;
            }
            ConfirmRemovalBatch(batch, "Remove all plugins");
        }

        private static void ConfirmRemovalBatch(RemovalBatch batch, string title)
        {
            string packages = string.Join("\n", batch.removals.Select(removal =>
                UMAContentCatalog.DisplayName(removal.kind)));
            int choice = EditorUtility.DisplayDialogComplex(title,
                "Remove these packages, in this order?\n\n" + packages +
                "\n\nContinue removes only unchanged package files. Continue - Remove All also deletes modified package-owned files, their metadata, and empty folders in every listed package. " +
                "Unowned files and user settings, documents, recovery, and exports are preserved with either choice.",
                "Continue", "Cancel", "Continue - Remove All");
            if (choice != 1)
            {
                batch.removeModified = choice == 2;
                if (!TryExecuteBatch(batch, out string error)) Debug.LogError("[UMA Plugins] " + error);
            }
        }

        public static bool CanRemoveAll(UMAContentKind kind, out string error) =>
            TryCreateBatch(kind, out _, out error);

        public static bool CanRemoveAll(out string error) =>
            TryCreateAllPluginsBatch(out _, out error);

        public static bool TryRemoveAll(UMAContentKind kind, out string error, bool removeModified = false)
        {
            if (!TryCreateBatch(kind, out var batch, out error)) return false;
            batch.removeModified = removeModified;
            return TryExecuteBatch(batch, out error);
        }

        public static bool TryRemoveAll(out string error, bool removeModified = false)
        {
            if (!TryCreateAllPluginsBatch(out var batch, out error)) return false;
            batch.removeModified = removeModified;
            return TryExecuteBatch(batch, out error);
        }

        private static IEnumerable<UMAContentKind> PluginRemovalOrder =>
            UMAContentCatalog.Plugins.Where(kind => !UMAContentCatalog.ParentPlugin(kind).HasValue)
                .SelectMany(kind => UMAContentCatalog.Companions(kind).Concat(new[] { kind }));

        private static bool HasPackageFiles(UMAContentKind kind) =>
            File.Exists(UMAContentCatalog.ManifestPath(kind)) ||
            (Directory.Exists(UMAContentCatalog.Root(kind)) &&
             Directory.EnumerateFiles(UMAContentCatalog.Root(kind), "*", SearchOption.AllDirectories)
                 .Any(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
                     !UMAContentCatalog.IsCompanionPath(kind, path.Replace('\\', '/'))));

        private static bool CanStartRemoval(UMAContentKind kind, out string error)
        {
            error = string.Empty;
            if (!UMAContentCatalog.IsPlugin(kind)) { error = "Only optional plugins can be removed here."; return false; }
            if (UMAContentPackageInstaller.IsInstallingAllPlugins || UMAPluginPackageDownload.IsActive ||
                File.Exists(Journal) || File.Exists("Library/UMA/ContentInstaller/pending.json"))
            { error = "A package transaction is already pending."; return false; }
            return true;
        }

        private static bool TryCreateBatch(UMAContentKind kind, out RemovalBatch batch, out string error)
        {
            batch = null;
            if (!CanStartRemoval(kind, out error)) return false;
            var candidate = new RemovalBatch { kind = kind };
            // Validate every package before removing any files. Companions with only
            // empty folders are absent; unmanaged payloads must be adopted first.
            foreach (var item in UMAContentCatalog.Companions(kind).Where(HasPackageFiles).Concat(new[] { kind }))
            {
                if (!UMAContentPackageArchiveValidator.TryReadInstalledManifest(item, out var manifest, out error))
                {
                    error = UMAContentCatalog.DisplayName(item) + ": " + error;
                    return false;
                }
                candidate.removals.Add(new Removal { kind = item, manifest = manifest });
            }
            batch = candidate;
            return true;
        }

        private static bool TryCreateAllPluginsBatch(out RemovalBatch batch, out string error)
        {
            batch = null;
            if (!CanStartRemoval(UMAContentKind.OverlayPainter, out error)) return false;
            var candidate = new RemovalBatch { allPlugins = true };
            // Build and validate one complete transaction before changing any package.
            // Absent parents do not prevent removal of their remaining companions.
            foreach (var item in PluginRemovalOrder.Where(HasPackageFiles))
            {
                if (!UMAContentPackageArchiveValidator.TryReadInstalledManifest(item, out var manifest, out error))
                {
                    error = UMAContentCatalog.DisplayName(item) + ": " + error;
                    return false;
                }
                candidate.removals.Add(new Removal { kind = item, manifest = manifest });
            }
            if (candidate.removals.Count == 0)
            {
                error = "No optional plugin packages are installed.";
                return false;
            }
            candidate.kind = candidate.removals.Last().kind;
            batch = candidate;
            return true;
        }

        public static bool CanRemove(UMAContentKind kind, out UMAContentManifest manifest, out string error)
        {
            manifest = null;
            if (!CanStartRemoval(kind, out error)) return false;
            var companions = UMAContentCatalog.Companions(kind).Where(HasPackageFiles).ToArray();
            if (companions.Length > 0)
            { error = "Remove " + string.Join(" and ", companions.Select(UMAContentCatalog.DisplayName)) + " before removing " + UMAContentCatalog.DisplayName(kind) + "."; return false; }
            return UMAContentPackageArchiveValidator.TryReadInstalledManifest(kind, out manifest, out error);
        }

        public static bool TryRemove(UMAContentKind kind, out string error, bool removeModified = false)
        {
            if (!CanRemove(kind, out var manifest, out error)) return false;
            var batch = new RemovalBatch { kind = kind, removeModified = removeModified };
            batch.removals.Add(new Removal { kind = kind, manifest = manifest });
            return TryExecuteBatch(batch, out error);
        }

        private static bool TryExecuteBatch(RemovalBatch batch, out string error)
        {
            error = string.Empty;
            // Recheck the transaction guard after an interactive confirmation.
            if (!CanStartRemoval(batch.kind, out error)) return false;
            try
            {
                ValidateBatch(batch);
                Directory.CreateDirectory(Path.GetDirectoryName(Journal));
                SaveBatch(batch);
                ExecuteBatch(batch);
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }

        private static void Resume()
        {
            if (!File.Exists(Journal)) return;
            try
            {
                string json = File.ReadAllText(Journal);
                ExecuteBatch(ReadBatch(json));
            }
            catch (Exception exception) { Debug.LogError("[UMA Plugins] Removal recovery stopped: " + exception.Message); }
        }

        private static RemovalBatch ReadBatch(string json)
        {
            var batch = JsonUtility.FromJson<RemovalBatch>(json);
            if (batch == null || batch.removals == null || batch.removals.Count == 0)
            {
                // Continue journals created by the previous single-package remover.
                var removal = JsonUtility.FromJson<Removal>(json);
                if (removal == null) throw new InvalidDataException("Invalid plugin removal journal.");
                batch = new RemovalBatch { kind = removal.kind };
                batch.removals.Add(removal);
            }
            ValidateBatch(batch);
            return batch;
        }

        private static void SaveBatch(RemovalBatch batch)
        {
            string temporary = Journal + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(batch, true));
            if (File.Exists(Journal)) File.Replace(temporary, Journal, null);
            else File.Move(temporary, Journal);
        }

        private static void ValidateBatch(RemovalBatch batch)
        {
            if (batch == null || batch.formatVersion != 1 || batch.removals == null ||
                batch.removals.Count == 0 || batch.next < 0 || batch.next > batch.removals.Count ||
                batch.removals.Last()?.kind != batch.kind)
                throw new InvalidDataException("Invalid plugin removal journal; no files were changed.");
            var allowed = new HashSet<UMAContentKind>(UMAContentCatalog.Companions(batch.kind)) { batch.kind };
            var order = batch.allPlugins ? PluginRemovalOrder.ToArray() : null;
            var seen = new HashSet<UMAContentKind>();
            int previousIndex = -1;
            foreach (var removal in batch.removals)
            {
                int index = removal == null || order == null ? -1 : Array.IndexOf(order, removal.kind);
                if (removal == null || !UMAContentCatalog.IsPlugin(removal.kind) ||
                    (batch.allPlugins ? index <= previousIndex : !allowed.Contains(removal.kind)) ||
                    !seen.Add(removal.kind) ||
                    !UMAContentPackageArchiveValidator.TryValidateManifestStructure(removal.manifest, removal.kind, out _) ||
                    !removal.manifest.ownedPaths.All(path => UMAContentCatalog.OwnsPluginPath(removal.kind, path)))
                    throw new InvalidDataException("Invalid plugin removal journal; no files were changed.");
                previousIndex = index;
            }
        }

        private static void ExecuteBatch(RemovalBatch batch)
        {
            ValidateBatch(batch);
            foreach (var removal in batch.removals.Skip(batch.next))
                ValidateTree(UMAContentCatalog.Root(removal.kind));
            // Hold compilation until all companions and their parent have been removed.
            EditorApplication.LockReloadAssemblies();
            AssetDatabase.StartAssetEditing();
            try
            {
                ProcessBatch(batch, index => Execute(batch.removals[index], () => SaveBatch(batch), batch.removeModified), () => SaveBatch(batch));
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Journal), "last-removal-batch.json"), JsonUtility.ToJson(batch, true));
                File.Delete(Journal);
            }
            finally
            {
                try { AssetDatabase.StopAssetEditing(); }
                finally { EditorApplication.UnlockReloadAssemblies(); AssetDatabase.Refresh(); }
            }
        }

        private static void ProcessBatch(RemovalBatch batch, Action<int> remove, Action checkpoint)
        {
            ValidateBatch(batch);
            while (batch.next < batch.removals.Count)
            {
                remove(batch.next);
                // If interrupted before checkpointing, recovery safely repeats the
                // current package. Completed companions are otherwise skipped.
                batch.next++;
                checkpoint();
            }
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

        private static void ValidateTree(string root)
        {
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
        }

        private static void Execute(Removal removal, Action saveProgress, bool removeModified)
        {
            string root = UMAContentCatalog.Root(removal.kind);
            RemoveOwnedFiles(removal, saveProgress, removeModified, AssetDatabase.DeleteAsset);
            string manifestPath = UMAContentCatalog.ManifestPath(removal.kind);
            if (!AssetDatabase.DeleteAsset(manifestPath) && File.Exists(manifestPath))
                throw new IOException("Could not remove " + manifestPath);
            UMAPluginDocumentationRegistry.Unregister(UMAContentCatalog.Id(removal.kind));
            if (removeModified)
                RemoveEmptyFolders(root, path => UMAContentCatalog.IsCompanionPath(removal.kind, path), AssetDatabase.DeleteAsset);
            else if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) AssetDatabase.DeleteAsset(root);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Journal), "last-removal.json"), JsonUtility.ToJson(removal, true));
            Debug.Log("[UMA Plugins] Removed " + (removeModified ? "all package-owned " : "unchanged ") +
                UMAContentCatalog.DisplayName(removal.kind) + " files. Preserved modified files: " + removal.preserved.Count + ".");
        }

        private static void RemoveEmptyFolders(string root, Func<string, bool> isProtected, Func<string, bool> deleteAsset)
        {
            // Empty leaf folders are not necessarily in the manifest. Prune children
            // first so their parents can become empty, stopping at this package root.
            // Never infer that a lone .meta belongs to a deleted folder: it could
            // describe a missing user asset and must be preserved.
            ValidateTree(root);
            Prune(root.Replace('\\', '/'));

            void Prune(string directory)
            {
                if (!Directory.Exists(directory) || isProtected(directory)) return;
                foreach (string child in Directory.GetDirectories(directory))
                    Prune(child.Replace('\\', '/'));
                if (Directory.EnumerateFileSystemEntries(directory).Any()) return;
                if (!deleteAsset(directory) || Directory.Exists(directory))
                    throw new IOException("Could not remove empty folder " + directory);
                // Ensure folder metadata cannot keep its parent alive when deletion
                // is batched or repeated during recovery.
                if (File.Exists(directory + ".meta")) File.Delete(directory + ".meta");
            }
        }

        private static void RemoveOwnedFiles(Removal removal, Action saveProgress, bool removeModified,
            Func<string, bool> deleteAsset)
        {
            // The validated manifest constrains every path to this feature root.
            foreach (var asset in removal.manifest.assets.Where(a => a.bytes > 0))
            {
                if (!File.Exists(asset.path) && !File.Exists(asset.path + ".meta")) continue;
                if (!removeModified && !Matches(asset))
                {
                    if (!removal.preserved.Contains(asset.path)) removal.preserved.Add(asset.path);
                    saveProgress();
                    continue;
                }
                if (Directory.Exists(asset.path)) throw new IOException("Expected a package file, but found a directory: " + asset.path);
                if (File.Exists(asset.path) && !deleteAsset(asset.path)) throw new IOException("Could not remove " + asset.path);
                // An interrupted removal or a locally deleted asset can leave only its metadata.
                if (File.Exists(asset.path + ".meta")) File.Delete(asset.path + ".meta");
            }
            foreach (var folder in removal.manifest.assets.Where(a => a.bytes == 0).OrderByDescending(a => a.path.Length))
                if (Directory.Exists(folder.path) && !Directory.EnumerateFileSystemEntries(folder.path).Any() &&
                    (removeModified || (File.Exists(folder.path + ".meta") && Hash(folder.path + ".meta") == folder.metaSha256)))
                    if (!deleteAsset(folder.path)) throw new IOException("Could not remove " + folder.path);
        }
    }
}
