using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    internal static class TexturePaintRecoveryStore
    {
        private const string DefaultFolder = UMAPathUtility.OverlayPainterRecoveryRoot;
        private const string AssetName = "painter_recovery.asset";
        private const string DataFolderName = "painter_recovery Data";

        internal static string RecoveryFolderOverride { get; set; }
        internal static string RecoveryFolder => NormalizeFolder(string.IsNullOrEmpty(RecoveryFolderOverride)
            ? UMASettings.TexturePaintRecoveryFolder
            : RecoveryFolderOverride);
        internal static string RecoveryAssetPath => RecoveryFolder + "/" + AssetName;
        internal static string RecoveryDataFolder => RecoveryFolder + "/" + DataFolderName;

        public static string GetContextKey(DynamicCharacterAvatar avatar)
        {
            string identity = avatar != null ? GlobalObjectId.GetGlobalObjectIdSlow(avatar).ToString() : "standalone";
            return Hash128.Compute(identity).ToString();
        }

        public static string GetContextKey(TexturePaintLaunchContext context)
        {
            if (context == null || !context.IsStandalone) return GetContextKey((DynamicCharacterAvatar)null);
            List<string> identities = new List<string>();
            for (int i = 0; context.members != null && i < context.members.Count; i++)
                identities.Add(context.members[i]?.slotGuid ?? string.Empty);
            identities.Sort(StringComparer.Ordinal);
            string orientation = string.Empty;
            if (context.fixupRotations)
            {
                orientation = "|fixup-rotations";
                if ((context.slotRotationEuler - MeshReconstructor.DefaultStandaloneSlotRotationEuler).sqrMagnitude > 0.000001f)
                {
                    orientation += "|" + context.slotRotationEuler.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                        context.slotRotationEuler.y.ToString("R", CultureInfo.InvariantCulture) + "," +
                        context.slotRotationEuler.z.ToString("R", CultureInfo.InvariantCulture);
                }
            }
            return Hash128.Compute("slot|" + context.sourceMode + "|" + context.umaMaterialGuid + "|" + context.udimGroupId + "|" +
                string.Join(",", identities) + orientation).ToString();
        }

        public static bool HasRecovery(string contextKey)
        {
            if (string.IsNullOrEmpty(contextKey)) return false;
            TexturePaintDocument recovery = AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(RecoveryAssetPath);
            if (recovery == null)
                return File.Exists(Path.GetFullPath(RecoveryAssetPath));
            return recovery.recoverySnapshot &&
                string.Equals(recovery.recoveryContextKey, contextKey, StringComparison.Ordinal);
        }

        public static bool TryLoad(string contextKey, out TexturePaintDocument document, out string error)
        {
            document = null;
            error = null;
            try
            {
                TexturePaintDocument recovery = AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(RecoveryAssetPath);
                if (recovery == null)
                    throw new InvalidDataException("The Overlay Painter recovery asset could not be loaded: " +
                        RecoveryAssetPath);
                if (!recovery.recoverySnapshot)
                    throw new InvalidDataException("The configured recovery asset is not a recovery snapshot.");
                if (!string.Equals(recovery.recoveryContextKey, contextKey, StringComparison.Ordinal))
                    throw new InvalidDataException("The recovery asset belongs to a different Overlay Painter session.");
                document = UnityEngine.Object.Instantiate(recovery);
                document.hideFlags = HideFlags.HideAndDontSave;
                document.Migrate();
                foreach (TexturePaintPixelData pixels in TexturePaintDocumentBlobUtility.EnumeratePixels(document))
                {
                    if (pixels == null) continue;
                    if (pixels.dataAsset == null)
                    {
                        if (pixels.width > 0 && pixels.height > 0)
                            throw new FileNotFoundException("A recovery texture data asset is missing: " +
                                pixels.storageKey);
                        continue;
                    }
                    byte[] bytes = pixels.dataAsset.bytes;
                    if (!TexturePaintDocumentBlobUtility.VerifyChecksum(bytes, pixels.checksum))
                        throw new InvalidDataException("A recovery texture blob failed its checksum: " + pixels.storageKey);
                    pixels.compressedBytes = bytes;
                    pixels.dataAsset = null;
                    pixels.recoveryBlobKey = null;
                }
                return true;
            }
            catch (Exception exception)
            {
                if (document != null) UnityEngine.Object.DestroyImmediate(document);
                document = null;
                error = exception.Message;
                return false;
            }
        }

        public static TexturePaintRecoveryWriteOperation BeginSave(TexturePaintDocument snapshot, string contextKey)
        {
            return new TexturePaintRecoveryWriteOperation(snapshot, contextKey, RecoveryAssetPath,
                RecoveryDataFolder);
        }

        public static void SaveImmediate(TexturePaintDocument snapshot, string contextKey)
        {
            using TexturePaintRecoveryWriteOperation operation = BeginSave(snapshot, contextKey);
            operation.CompleteSynchronously();
            if (operation.HasError) throw new IOException(operation.Error);
        }

        public static void Delete(string contextKey)
        {
            if (string.IsNullOrEmpty(contextKey)) return;
            TexturePaintDocument recovery = AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(RecoveryAssetPath);
            if (recovery != null && !string.Equals(recovery.recoveryContextKey, contextKey, StringComparison.Ordinal))
                return;
            if (AssetDatabase.LoadMainAssetAtPath(RecoveryAssetPath) != null ||
                File.Exists(Path.GetFullPath(RecoveryAssetPath)))
                AssetDatabase.DeleteAsset(RecoveryAssetPath);
            if (AssetDatabase.IsValidFolder(RecoveryDataFolder)) AssetDatabase.DeleteAsset(RecoveryDataFolder);
        }

        internal static void EnsureAssetFolder(string folder)
        {
            string normalized = NormalizeFolder(folder);
            string[] parts = normalized.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string NormalizeFolder(string folder)
        {
            string normalized = string.IsNullOrWhiteSpace(folder) ? DefaultFolder :
                folder.Trim().Replace('\\', '/').TrimEnd('/');
            string[] parts = normalized.Split('/');
            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || parts.Length < 2)
                return DefaultFolder;
            for (int i = 0; i < parts.Length; i++)
                if (string.IsNullOrWhiteSpace(parts[i]) || parts[i] == "." || parts[i] == "..")
                    return DefaultFolder;
            return normalized;
        }
    }

    internal sealed class TexturePaintRecoveryWriteOperation : IDisposable
    {
        private readonly TexturePaintDocument metadata;
        private readonly string assetPath;
        private readonly string dataFolder;
        private readonly string stagingFolder;
        private readonly List<BlobWrite> writes = new List<BlobWrite>();
        private readonly HashSet<string> referencedBlobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> createdAssetPaths = new List<string>();
        private readonly TexturePaintDocument existing;
        private readonly TexturePaintDocument existingBackup;
        private Task writeTask;
        private bool metadataCommitted;

        public bool IsDone { get; private set; }
        public bool HasError => !string.IsNullOrEmpty(Error);
        public string Error { get; private set; }
        public float Progress => IsDone ? 1f : writeTask == null ? 0f : writeTask.IsCompleted ? 0.85f : 0.35f;

        internal TexturePaintRecoveryWriteOperation(TexturePaintDocument snapshot, string contextKey,
            string assetPath, string dataFolder)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (string.IsNullOrEmpty(contextKey)) throw new ArgumentException("A recovery context key is required.", nameof(contextKey));
            this.assetPath = assetPath;
            this.dataFolder = dataFolder;
            TexturePaintRecoveryStore.EnsureAssetFolder(dataFolder);
            stagingFolder = Path.GetFullPath(Path.Combine("Library/UMA/TextureModifications/RecoveryStaging",
                Guid.NewGuid().ToString("N")));
            existing = AssetDatabase.LoadAssetAtPath<TexturePaintDocument>(assetPath);
            if (existing == null && AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                throw new IOException("The configured recovery path is occupied by a different asset: " + assetPath);
            if (existing != null && !existing.recoverySnapshot)
                throw new IOException("The configured recovery path contains a non-recovery Overlay Painter document: " +
                    assetPath);
            existingBackup = existing != null ? UnityEngine.Object.Instantiate(existing) : null;
            metadata = UnityEngine.Object.Instantiate(snapshot);
            metadata.name = snapshot.name;
            metadata.hideFlags = HideFlags.HideAndDontSave;
            metadata.recoverySnapshot = true;
            metadata.recoveryContextKey = contextKey;

            foreach (TexturePaintPixelData pixels in TexturePaintDocumentBlobUtility.EnumeratePixels(metadata))
            {
                byte[] bytes = pixels?.GetCompressedBytes();
                if (bytes == null || bytes.Length == 0) continue;
                pixels.checksum = TexturePaintDocumentBlobUtility.ComputeChecksum(bytes);
                string blobPath = dataFolder + "/" + pixels.checksum + ".bytes";
                bool firstReference = referencedBlobs.Add(blobPath);
                TextAsset blob = AssetDatabase.LoadAssetAtPath<TextAsset>(blobPath);
                if (blob != null)
                {
                    if (!TexturePaintDocumentBlobUtility.VerifyChecksum(blob.bytes, pixels.checksum))
                        throw new InvalidDataException("A recovery data file has unexpected contents: " + blobPath);
                    pixels.dataAsset = blob;
                }
                else
                {
                    if (firstReference)
                        writes.Add(new BlobWrite(blobPath,
                            Path.Combine(stagingFolder, pixels.checksum + ".bytes"), bytes));
                    pixels.dataAsset = null;
                }
                pixels.recoveryBlobKey = null;
                pixels.compressedBytes = null;
            }
            StartWrites();
        }

        public void Tick()
        {
            if (IsDone || writeTask == null || !writeTask.IsCompleted) return;
            try
            {
                writeTask.GetAwaiter().GetResult();
                CommitAsset();
                IsDone = true;
            }
            catch (Exception exception)
            {
                Error = "Recovery write failed: " + exception.Message;
                RollBack();
                IsDone = true;
            }
            finally { TryDeleteStaging(); }
        }

        public void CompleteSynchronously()
        {
            if (IsDone) return;
            try
            {
                writeTask?.GetAwaiter().GetResult();
                CommitAsset();
            }
            catch (Exception exception)
            {
                Error = "Recovery write failed: " + exception.Message;
                RollBack();
            }
            IsDone = true;
            TryDeleteStaging();
        }

        public void Dispose()
        {
            if (metadata != null) UnityEngine.Object.DestroyImmediate(metadata);
            if (existingBackup != null) UnityEngine.Object.DestroyImmediate(existingBackup);
            TryDeleteStaging();
        }

        private void StartWrites()
        {
            writeTask = Task.Run(() =>
            {
                Directory.CreateDirectory(stagingFolder);
                for (int i = 0; i < writes.Count; i++)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(writes[i].stagingPath));
                    File.WriteAllBytes(writes[i].stagingPath, writes[i].bytes);
                }
            });
        }

        private void CommitAsset()
        {
            if (metadataCommitted) return;
            ImportBlobs();
            TexturePaintDocument target = existing;
            if (target == null)
            {
                target = ScriptableObject.CreateInstance<TexturePaintDocument>();
                target.name = Path.GetFileNameWithoutExtension(assetPath);
                AssetDatabase.CreateAsset(target, assetPath);
                createdAssetPaths.Add(assetPath);
            }
            EditorUtility.CopySerialized(metadata, target);
            target.hideFlags = HideFlags.None;
            target.name = Path.GetFileNameWithoutExtension(assetPath);
            target.recoverySnapshot = true;
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
            metadataCommitted = true;
            CleanupUnreferencedBlobs();
        }

        private void ImportBlobs()
        {
            for (int i = 0; i < writes.Count; i++)
            {
                BlobWrite write = writes[i];
                string absolutePath = Path.GetFullPath(write.assetPath);
                if (File.Exists(absolutePath))
                    throw new IOException("A recovery data asset already exists but is not imported: " + write.assetPath);
                File.Move(write.stagingPath, absolutePath);
                createdAssetPaths.Add(write.assetPath);
                AssetDatabase.ImportAsset(write.assetPath, ImportAssetOptions.ForceSynchronousImport);
                TextAsset blob = AssetDatabase.LoadAssetAtPath<TextAsset>(write.assetPath);
                if (blob == null) throw new IOException("Unity did not import recovery data: " + write.assetPath);
                AssignBlob(write.assetPath, blob);
            }
        }

        private void AssignBlob(string path, TextAsset blob)
        {
            foreach (TexturePaintPixelData pixels in TexturePaintDocumentBlobUtility.EnumeratePixels(metadata))
                if (pixels != null && pixels.dataAsset == null &&
                    string.Equals(dataFolder + "/" + pixels.checksum + ".bytes", path,
                        StringComparison.OrdinalIgnoreCase))
                    pixels.dataAsset = blob;
        }

        private void CleanupUnreferencedBlobs()
        {
            string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { dataFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!referencedBlobs.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
        }

        private void RollBack()
        {
            if (existing != null && existingBackup != null)
            {
                try
                {
                    EditorUtility.CopySerialized(existingBackup, existing);
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssetIfDirty(existing);
                }
                catch (Exception exception)
                {
                    Debug.LogError("Overlay Painter could not restore the previous recovery asset: " +
                        exception.Message);
                }
            }
            for (int i = createdAssetPaths.Count - 1; i >= 0; i--)
                if (!string.IsNullOrEmpty(createdAssetPaths[i])) AssetDatabase.DeleteAsset(createdAssetPaths[i]);
        }

        private void TryDeleteStaging()
        {
            try
            {
                if (!string.IsNullOrEmpty(stagingFolder) && Directory.Exists(stagingFolder))
                    Directory.Delete(stagingFolder, true);
            }
            catch { }
        }

        private readonly struct BlobWrite
        {
            public readonly string assetPath;
            public readonly string stagingPath;
            public readonly byte[] bytes;
            public BlobWrite(string assetPath, string stagingPath, byte[] bytes)
            {
                this.assetPath = assetPath;
                this.stagingPath = stagingPath;
                this.bytes = bytes;
            }
        }
    }

    internal sealed class TexturePaintProjectSaveOperation : IDisposable
    {
        private readonly TexturePaintDocument snapshot;
        private readonly TexturePaintDocument existingDocument;
        private readonly TexturePaintDocument existingBackup;
        private readonly string documentPath;
        private readonly string dataFolder;
        private readonly List<ProjectBlobWrite> writes = new List<ProjectBlobWrite>();
        private readonly List<string> createdAssetPaths = new List<string>();
        private Task stagingTask;
        private string stagingFolder;

        public bool IsDone { get; private set; }
        public bool HasError => !string.IsNullOrEmpty(Error);
        public string Error { get; private set; }
        public float Progress => IsDone ? 1f : stagingTask == null ? 0f : stagingTask.IsCompleted ? 0.65f : 0.3f;
        public TexturePaintDocument SavedDocument { get; private set; }

        public TexturePaintProjectSaveOperation(TexturePaintDocument snapshot, TexturePaintDocument existingDocument,
            string documentPath)
        {
            this.snapshot = snapshot != null ? snapshot : throw new ArgumentNullException(nameof(snapshot));
            this.existingDocument = existingDocument;
            existingBackup = existingDocument != null ? UnityEngine.Object.Instantiate(existingDocument) : null;
            this.documentPath = documentPath?.Replace('\\', '/');
            if (string.IsNullOrEmpty(this.documentPath) || !this.documentPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Overlay Painter documents must be saved below Assets.", nameof(documentPath));
            UnityEngine.Object occupied = AssetDatabase.LoadMainAssetAtPath(this.documentPath);
            if (occupied != null && !ReferenceEquals(occupied, existingDocument))
                throw new IOException("An asset already exists at " + this.documentPath);

            string parent = Path.GetDirectoryName(this.documentPath)?.Replace('\\', '/');
            string stem = Path.GetFileNameWithoutExtension(this.documentPath);
            dataFolder = parent + "/" + stem + " Data";
            EnsureAssetFolder(dataFolder);
            stagingFolder = Path.GetFullPath(Path.Combine("Library/UMA/TextureModifications/ProjectStaging",
                Guid.NewGuid().ToString("N")));
            string revisionSuffix = string.IsNullOrEmpty(snapshot.revisionId) ? Guid.NewGuid().ToString("N").Substring(0, 8)
                : snapshot.revisionId.Substring(0, Mathf.Min(8, snapshot.revisionId.Length));
            bool copyAllData = existingDocument == null ||
                !string.Equals(AssetDatabase.GetAssetPath(existingDocument), this.documentPath,
                    StringComparison.OrdinalIgnoreCase);
            foreach (TexturePaintPixelData pixels in TexturePaintDocumentBlobUtility.EnumeratePixels(snapshot))
            {
                if (copyAllData && (pixels?.compressedBytes == null || pixels.compressedBytes.Length == 0) &&
                    pixels?.dataAsset != null)
                {
                    pixels.compressedBytes = pixels.dataAsset.bytes;
                    pixels.dataAsset = null;
                }
                if (pixels?.compressedBytes == null || pixels.compressedBytes.Length == 0) continue;
                string fileName = TexturePaintDocumentBlobUtility.BlobName(pixels.storageKey) + "_" + revisionSuffix + ".bytes";
                writes.Add(new ProjectBlobWrite
                {
                    pixels = pixels,
                    stagingPath = Path.Combine(stagingFolder, fileName),
                    assetPath = dataFolder + "/" + fileName,
                    bytes = pixels.compressedBytes
                });
            }
            stagingTask = Task.Run(StageBlobs);
        }

        public void Tick()
        {
            if (IsDone || stagingTask == null || !stagingTask.IsCompleted) return;
            try
            {
                stagingTask.GetAwaiter().GetResult();
                ImportBlobs();
                CommitDocument();
                try { CleanupUnreferencedBlobs(); }
                catch (Exception cleanupException) { Debug.LogWarning("Overlay Painter document cleanup: " + cleanupException.Message); }
                IsDone = true;
            }
            catch (Exception exception)
            {
                Error = "Document save failed: " + exception.Message;
                RollBackCreatedAssets();
                IsDone = true;
            }
            finally
            {
                TryDeleteStaging();
            }
        }

        public void Dispose()
        {
            TryDeleteStaging();
            if (existingBackup != null) UnityEngine.Object.DestroyImmediate(existingBackup);
        }

        private void StageBlobs()
        {
            Directory.CreateDirectory(stagingFolder);
            for (int i = 0; i < writes.Count; i++) File.WriteAllBytes(writes[i].stagingPath, writes[i].bytes);
        }

        private void ImportBlobs()
        {
            for (int i = 0; i < writes.Count; i++)
            {
                ProjectBlobWrite write = writes[i];
                string absolute = Path.GetFullPath(write.assetPath);
                if (File.Exists(absolute)) throw new IOException("A generated document blob already exists: " + write.assetPath);
                File.Move(write.stagingPath, absolute);
                createdAssetPaths.Add(write.assetPath);
                AssetDatabase.ImportAsset(write.assetPath, ImportAssetOptions.ForceSynchronousImport);
                TextAsset data = AssetDatabase.LoadAssetAtPath<TextAsset>(write.assetPath);
                if (data == null) throw new IOException("Unity did not import document data: " + write.assetPath);
                write.pixels.dataAsset = data;
                write.pixels.compressedBytes = null;
                write.pixels.recoveryBlobKey = null;
            }
        }

        private void CommitDocument()
        {
            TexturePaintDocument target = existingDocument;
            if (target == null)
            {
                target = ScriptableObject.CreateInstance<TexturePaintDocument>();
                target.name = Path.GetFileNameWithoutExtension(documentPath);
                AssetDatabase.CreateAsset(target, documentPath);
                createdAssetPaths.Add(documentPath);
            }
            EditorUtility.CopySerialized(snapshot, target);
            target.hideFlags = HideFlags.None;
            target.name = Path.GetFileNameWithoutExtension(documentPath);
            target.recoverySnapshot = false;
            target.recoveryContextKey = null;
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
            SavedDocument = target;
        }

        private void CleanupUnreferencedBlobs()
        {
            HashSet<string> referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TexturePaintPixelData pixels in TexturePaintDocumentBlobUtility.EnumeratePixels(SavedDocument))
            {
                string path = pixels?.dataAsset != null ? AssetDatabase.GetAssetPath(pixels.dataAsset) : null;
                if (!string.IsNullOrEmpty(path)) referenced.Add(path);
            }
            string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { dataFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!referenced.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
        }

        private void RollBackCreatedAssets()
        {
            if (existingDocument != null && existingBackup != null)
            {
                try
                {
                    EditorUtility.CopySerialized(existingBackup, existingDocument);
                    EditorUtility.SetDirty(existingDocument);
                    AssetDatabase.SaveAssetIfDirty(existingDocument);
                }
                catch (Exception exception)
                {
                    Debug.LogError("Overlay Painter could not restore the previous document after a save failure: " + exception.Message);
                }
            }
            for (int i = createdAssetPaths.Count - 1; i >= 0; i--)
                if (!string.IsNullOrEmpty(createdAssetPaths[i])) AssetDatabase.DeleteAsset(createdAssetPaths[i]);
        }

        private void TryDeleteStaging()
        {
            try { if (!string.IsNullOrEmpty(stagingFolder) && Directory.Exists(stagingFolder)) Directory.Delete(stagingFolder, true); }
            catch { }
        }

        private static void EnsureAssetFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private sealed class ProjectBlobWrite
        {
            public TexturePaintPixelData pixels;
            public string stagingPath;
            public string assetPath;
            public byte[] bytes;
        }
    }

    internal static class TexturePaintDocumentBlobUtility
    {
        public static IEnumerable<TexturePaintPixelData> EnumeratePixels(TexturePaintDocument document)
        {
            if (document?.surfaces == null) yield break;
            for (int surfaceIndex = 0; surfaceIndex < document.surfaces.Count; surfaceIndex++)
            {
                TexturePaintDocumentSurface surface = document.surfaces[surfaceIndex];
                if (surface == null) continue;
                if (surface.baseChannels != null)
                    for (int channelIndex = 0; channelIndex < surface.baseChannels.Count; channelIndex++)
                        if (surface.baseChannels[channelIndex]?.pixels != null)
                            yield return surface.baseChannels[channelIndex].pixels;
                if (surface.layers == null) continue;
                for (int layerIndex = 0; layerIndex < surface.layers.Count; layerIndex++)
                {
                    TexturePaintDocumentLayer layer = surface.layers[layerIndex];
                    if (layer == null) continue;
                    if (layer.maskPixels != null) yield return layer.maskPixels;
                    if (layer.channels != null)
                        for (int channelIndex = 0; channelIndex < layer.channels.Count; channelIndex++)
                            if (layer.channels[channelIndex]?.pixels != null)
                                yield return layer.channels[channelIndex].pixels;
                }
            }
        }

        public static string BlobName(string storageKey)
        {
            return Hash128.Compute(string.IsNullOrEmpty(storageKey) ? Guid.NewGuid().ToString("N") : storageKey).ToString();
        }

        public static bool VerifyChecksum(byte[] bytes, string expected)
        {
            if (bytes == null || bytes.Length == 0) return false;
            if (string.IsNullOrEmpty(expected)) return true;
            string actual = ComputeChecksum(bytes);
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        public static string ComputeChecksum(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes ?? Array.Empty<byte>())).Replace("-", string.Empty)
                .ToLowerInvariant();
        }
    }
}
