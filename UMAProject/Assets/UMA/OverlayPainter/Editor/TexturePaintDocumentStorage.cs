using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.TexturePaint.Editor
{
    internal static class TexturePaintDocumentStorage
    {
        internal readonly struct RestoreReport
        {
            public readonly int restoredSurfaces;
            public readonly int restoredLayers;
            public readonly int unboundSurfaces;
            public readonly int unboundLayers;

            public bool HasUnboundLayers => unboundLayers > 0;

            public RestoreReport(int restoredSurfaces, int restoredLayers,
                int unboundSurfaces, int unboundLayers)
            {
                this.restoredSurfaces = restoredSurfaces;
                this.restoredLayers = restoredLayers;
                this.unboundSurfaces = unboundSurfaces;
                this.unboundLayers = unboundLayers;
            }
        }

        internal sealed class CaptureOperation
        {
            private readonly List<PixelCaptureWork> work;
            private int nextWork;
            private PixelCaptureWork active;
            private Task<CapturedPixels> compression;
            private bool gpuRequestPending;
            private bool canceled;

            public TexturePaintDocument Snapshot { get; }
            public Dictionary<EditableTextureTarget, long> CapturedRevisions { get; } =
                new Dictionary<EditableTextureTarget, long>();
            public bool IsDone { get; private set; }
            public bool HasError => !string.IsNullOrEmpty(Error);
            public string Error { get; private set; }
            public float Progress => work.Count == 0 ? 1f : Mathf.Clamp01((float)nextWork / work.Count);

            internal CaptureOperation(TexturePaintDocument source, TextureStore store,
                IReadOnlyDictionary<EditableTextureTarget, long> persistedRevisions, bool recoverySnapshot)
            {
                Snapshot = BuildSnapshot(source, store, persistedRevisions, recoverySnapshot, out work,
                    CapturedRevisions);
                if (work.Count == 0) IsDone = true;
            }

            public void Tick()
            {
                if (IsDone || canceled) return;
                if (compression != null)
                {
                    if (!compression.IsCompleted) return;
                    try
                    {
                        CapturedPixels result = compression.GetAwaiter().GetResult();
                        active.destination.width = active.source.width;
                        active.destination.height = active.source.height;
                        active.destination.textureFormat = active.textureFormat;
                        active.destination.linear = !active.source.sRGB;
                        active.destination.uncompressedByteCount = result.uncompressedByteCount;
                        active.destination.compressedBytes = result.bytes;
                        active.destination.checksum = result.checksum;
                        CapturedRevisions[active.target] = active.revision;
                    }
                    catch (Exception exception)
                    {
                        Fail("Texture-paint document compression failed: " + exception.Message);
                        return;
                    }
                    finally
                    {
                        compression = null;
                        active = null;
                    }
                }
                if (gpuRequestPending || compression != null) return;
                if (nextWork >= work.Count)
                {
                    IsDone = true;
                    return;
                }

                active = work[nextWork++];
                if (active.source == null || !active.source.IsCreated())
                {
                    Fail("A texture target was released while the document was being captured.");
                    return;
                }
                gpuRequestPending = true;
                try
                {
                    AsyncGPUReadback.Request(active.source, 0, active.textureFormat, OnReadbackComplete);
                }
                catch (Exception exception)
                {
                    gpuRequestPending = false;
                    Fail("Unable to start texture-paint document readback: " + exception.Message);
                }
            }

            public void Cancel()
            {
                canceled = true;
                Error = "Canceled";
                IsDone = true;
            }

            private void OnReadbackComplete(AsyncGPUReadbackRequest request)
            {
                gpuRequestPending = false;
                if (canceled || IsDone) return;
                if (request.hasError)
                {
                    Fail("The GPU could not read a texture target for document persistence.");
                    return;
                }
                byte[] raw;
                try { raw = request.GetData<byte>().ToArray(); }
                catch (Exception exception)
                {
                    Fail("Unable to copy texture-paint readback data: " + exception.Message);
                    return;
                }
                compression = Task.Run(() => CompressPixels(raw));
            }

            private void Fail(string message)
            {
                Error = message;
                IsDone = true;
            }
        }

        private sealed class PixelCaptureWork
        {
            public EditableTextureTarget target;
            public RenderTexture source;
            public TextureFormat textureFormat;
            public TexturePaintPixelData destination;
            public long revision;
        }

        private readonly struct CapturedPixels
        {
            public readonly byte[] bytes;
            public readonly int uncompressedByteCount;
            public readonly string checksum;

            public CapturedPixels(byte[] bytes, int uncompressedByteCount, string checksum)
            {
                this.bytes = bytes;
                this.uncompressedByteCount = uncompressedByteCount;
                this.checksum = checksum;
            }
        }

        public static TexturePaintDocument CreateTransient(DynamicCharacterAvatar avatar,
            TexturePaintLaunchContext launchContext = null)
        {
            TexturePaintDocument document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            document.name = avatar != null ? avatar.name + " Overlay Painter (Temporary)" : "Overlay Painter (Temporary)";
            document.hideFlags = HideFlags.HideAndDontSave;
            document.avatarName = avatar != null ? avatar.name : "Avatar";
            document.avatarGlobalObjectId = avatar != null
                ? GlobalObjectId.GetGlobalObjectIdSlow(avatar).ToString() : string.Empty;
            document.launchContext = launchContext?.Clone();
            document.createdUtc = DateTime.UtcNow.ToString("O");
            document.Migrate();
            return document;
        }

        public static CaptureOperation BeginCapture(TexturePaintDocument source, TextureStore store,
            IReadOnlyDictionary<EditableTextureTarget, long> persistedRevisions,
            bool recoverySnapshot)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            AssignStableSurfaceIds(store);
            return new CaptureOperation(source, store, persistedRevisions, recoverySnapshot);
        }

        public static void RecordCurrentRevisions(TextureStore store,
            IDictionary<EditableTextureTarget, long> destination)
        {
            destination.Clear();
            if (store == null) return;
            for (int setIndex = 0; setIndex < store.Sets.Count; setIndex++)
            {
                TextureSet set = store.Sets[setIndex];
                foreach (TextureChannelTarget channel in set.channels.Values)
                    if (channel?.editable != null) destination[channel.editable] = channel.editable.Revision;
                for (int layerIndex = 0; layerIndex < set.layers.Count; layerIndex++)
                {
                    TexturePaintLayer layer = set.layers[layerIndex];
                    foreach (EditableTextureTarget target in layer.channels.Values)
                        if (target != null) destination[target] = target.Revision;
                    if (layer.layerMask?.target != null)
                        destination[layer.layerMask.target] = layer.layerMask.target.Revision;
                }
            }
        }

        public static void Save(TexturePaintDocument document, TextureStore store,
            bool recoverySnapshot = false)
        {
            if (document == null || store == null) return;
            document.Migrate();
            document.revisionId = Guid.NewGuid().ToString("N");
            document.recoverySnapshot = recoverySnapshot;
            document.lastSavedUtc = DateTime.UtcNow.ToString("O");
            List<TexturePaintDocumentSurface> previousSurfaces = new List<TexturePaintDocumentSurface>(document.surfaces);
            HashSet<TexturePaintDocumentSurface> matchedPrevious = new HashSet<TexturePaintDocumentSurface>();
            for (int setIndex = 0; setIndex < store.Sets.Count; setIndex++)
            {
                TextureSet set = store.Sets[setIndex];
                TexturePaintDocumentSurface previous = document.FindSurface(set.persistentId) ?? FindFallback(document, set);
                if (previous != null)
                {
                    TexturePaintSurfaceFingerprint current = TexturePaintSurfaceFingerprintUtility.Compute(set.surface?.mesh);
                    bool uvChanged = !string.IsNullOrEmpty(previous.uvSignature) && previous.uvSignature != current.uv;
                    if (!uvChanged) matchedPrevious.Add(previous);
                }
            }
            document.surfaces.Clear();
            for (int setIndex = 0; setIndex < store.Sets.Count; setIndex++)
            {
                TextureSet set = store.Sets[setIndex];
                TexturePaintSurfaceFingerprint fingerprint = TexturePaintSurfaceFingerprintUtility.Compute(set.surface?.mesh);
                TexturePaintDocumentSurface surface = new TexturePaintDocumentSurface
                {
                    stableId = set.persistentId,
                    materialName = set.Name,
                    umaMaterialGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(set.umaMaterial)),
                    meshSignature = fingerprint.geometry,
                    topologySignature = fingerprint.topology,
                    uvSignature = fingerprint.uv,
                    materialSignature = MaterialSignature(set),
                    fallbackRendererIndex = set.surface?.rendererIndex ?? -1,
                    fallbackSubmeshIndex = set.surface?.sourceSubmeshIndex ?? -1,
                    activeLayer = set.activeLayerIndex,
                    normalControlStrength = set.normalControlStrength,
                    normalControlRadius = set.normalControlRadius,
                    normalControlInvert = set.normalControlInvert,
                    baseStrokes = CloneStrokes(set.baseStrokes),
                    slotNames = set.surface != null ? new List<string>(set.surface.slotNames) : new List<string>()
                };
                foreach (KeyValuePair<TexturePaintChannel, TextureChannelTarget> pair in set.channels)
                {
                    TextureChannelTarget channel = pair.Value;
                    surface.baseChannels.Add(new TexturePaintDocumentChannel
                    {
                        channel = pair.Key,
                        materialProperty = channel.materialProperty,
                        sourceKeyword = channel.sourceKeyword,
                        umaChannelIndex = channel.umaChannelIndex,
                        renderTextureFormat = channel.format,
                        sRGB = channel.sRGB,
                        adjustments = channel.adjustments?.Clone() ??
                            new TexturePaintChannelAdjustments(),
                        // Documents persist the painter's linear working pixels. The channel's
                        // sRGB flag is output metadata and must not reinterpret those bytes.
                        pixels = Capture(channel.editable.Front, channel.editable.Front.sRGB)
                    });
                }
                for (int layerIndex = 0; layerIndex < set.layers.Count; layerIndex++)
                    surface.layers.Add(CaptureLayer(set.layers[layerIndex]));
                document.surfaces.Add(surface);
            }
            for (int i = 0; i < previousSurfaces.Count; i++)
            {
                TexturePaintDocumentSurface previous = previousSurfaces[i];
                if (previous == null || matchedPrevious.Contains(previous)) continue;
                if (!previous.orphaned)
                {
                    previous.orphaned = true;
                    previous.orphanReason = "No exact UV-compatible reconstructed surface existed when the document was saved.";
                }
                document.surfaces.Add(previous);
            }
            EditorUtility.SetDirty(document);
            AssetDatabase.SaveAssetIfDirty(document);
        }

        public static RestoreReport Restore(TexturePaintDocument document, TextureStore store)
        {
            if (document == null || store == null) return default;
            var matched = new HashSet<TexturePaintDocumentSurface>();
            int restoredSurfaces = 0;
            int restoredLayers = 0;
            AssignStableSurfaceIds(store);
            for (int setIndex = 0; setIndex < store.Sets.Count; setIndex++)
            {
                TextureSet set = store.Sets[setIndex];
                TexturePaintDocumentSurface saved = document.FindSurface(set.persistentId) ?? FindFallback(document, set);
                if (saved == null) continue;
                matched.Add(saved);
                restoredSurfaces++;
                restoredLayers += saved.layers?.Count ?? 0;
                set.normalControlStrength = Mathf.Clamp(saved.normalControlStrength, 0f, 16f);
                set.normalControlRadius = Mathf.Clamp(saved.normalControlRadius, 1, 16);
                set.normalControlInvert = saved.normalControlInvert;
                RestoreChannelAdjustments(saved, set);
                TexturePaintSurfaceFingerprint current = TexturePaintSurfaceFingerprintUtility.Compute(set.surface?.mesh);
                bool uvChanged = !string.IsNullOrEmpty(saved.uvSignature) &&
                    !string.Equals(saved.uvSignature, current.uv, StringComparison.Ordinal);
                if (uvChanged)
                {
                    RestoreReprojectableContent(saved, set);
                    set.BindPreviewTextures();
                    continue;
                }
                RestoreBaseChannels(saved, set);
                set.baseStrokes.Clear();
                set.baseStrokes.AddRange(CloneStrokes(saved.baseStrokes));
                for (int i = 0; i < set.layers.Count; i++) set.layers[i].Dispose();
                set.layers.Clear();
                set.activeLayerIndex = -1;
                for (int layerIndex = 0; layerIndex < saved.layers.Count; layerIndex++)
                    RestoreLayer(saved.layers[layerIndex], set);
                set.activeLayerIndex = Mathf.Clamp(saved.activeLayer, -1, set.layers.Count - 1);
                set.BindPreviewTextures();
            }
            int unboundSurfaces = 0;
            int unboundLayers = 0;
            for (int i = 0; i < document.surfaces.Count; i++)
            {
                TexturePaintDocumentSurface saved = document.surfaces[i];
                if (saved == null || saved.orphaned || matched.Contains(saved) ||
                    saved.layers == null || saved.layers.Count == 0) continue;
                unboundSurfaces++;
                unboundLayers += saved.layers.Count;
            }
            if (unboundLayers > 0)
                Debug.LogWarning($"Overlay Painter restored {restoredLayers} saved layer member" +
                    (restoredLayers == 1 ? string.Empty : "s") + $", but {unboundLayers} layer member" +
                    (unboundLayers == 1 ? string.Empty : "s") + " could not be rebound to the current " +
                    "character surfaces. The unmatched content remains in the document.", document);
            return new RestoreReport(restoredSurfaces, restoredLayers, unboundSurfaces, unboundLayers);
        }

        public static List<TexturePaintBindingReport> AnalyzeBindings(TexturePaintDocument document, TextureStore store)
        {
            List<TexturePaintBindingReport> reports = new List<TexturePaintBindingReport>();
            if (document == null || store == null) return reports;
            HashSet<TexturePaintDocumentSurface> matched = new HashSet<TexturePaintDocumentSurface>();
            AssignStableSurfaceIds(store);
            for (int i = 0; i < store.Sets.Count; i++)
            {
                TextureSet set = store.Sets[i];
                TexturePaintDocumentSurface saved = document.FindSurface(set.persistentId);
                TexturePaintBindingStatus status = TexturePaintBindingStatus.Exact;
                string message = "Stable surface identity and mesh data match.";
                if (saved == null)
                {
                    saved = FindFallback(document, set);
                    if (saved == null)
                    {
                        reports.Add(new TexturePaintBindingReport
                        {
                            currentSurfaceId = set.persistentId,
                            materialName = set.Name,
                            status = TexturePaintBindingStatus.Orphaned,
                            message = "No saved surface has compatible slots, material, or topology."
                        });
                        continue;
                    }
                    TexturePaintSurfaceFingerprint current = TexturePaintSurfaceFingerprintUtility.Compute(set.surface?.mesh);
                    if (!string.IsNullOrEmpty(saved.uvSignature) && saved.uvSignature != current.uv)
                    {
                        status = TexturePaintBindingStatus.Reprojectable;
                        message = "The surface was rebound but its UVs changed. Surface-anchored strokes and paths were retained; layer-mask pixels reset to their base values and other raster pixels require rerasterization.";
                    }
                    else
                    {
                        status = TexturePaintBindingStatus.Rebound;
                        message = saved.materialSignature == MaterialSignature(set)
                            ? "Surface was rebound by compatible material, slots, topology, and UVs."
                            : "Material changed; compatible topology and UVs were rebound to the current material.";
                    }
                }
                matched.Add(saved);
                reports.Add(new TexturePaintBindingReport
                {
                    savedSurfaceId = saved.stableId,
                    currentSurfaceId = set.persistentId,
                    materialName = set.Name,
                    status = status,
                    message = message
                });
            }
            for (int i = 0; i < document.surfaces.Count; i++)
            {
                TexturePaintDocumentSurface saved = document.surfaces[i];
                if (saved == null || matched.Contains(saved)) continue;
                reports.Add(new TexturePaintBindingReport
                {
                    savedSurfaceId = saved.stableId,
                    materialName = saved.materialName,
                    status = TexturePaintBindingStatus.Orphaned,
                    message = "Saved content no longer maps to a reconstructed surface."
                });
            }
            return reports;
        }

        public static void AssignStableSurfaceIds(TextureStore store)
        {
            if (store == null) return;
            for (int i = 0; i < store.Sets.Count; i++)
            {
                TextureSet set = store.Sets[i];
                string umaGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(set.umaMaterial));
                string materialGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(set.surface?.sourceMaterial));
                List<string> slots = set.surface != null ? new List<string>(set.surface.slotNames) : new List<string>();
                slots.Sort(StringComparer.Ordinal);
                string identity = string.Join("|", new[]
                {
                    umaGuid,
                    materialGuid,
                    StableMaterialName(set.Name),
                    string.Join(",", slots),
                    MeshSignature(set.surface?.mesh)
                });
                set.persistentId = Hash128.Compute(identity).ToString();
            }
        }

        private static TexturePaintDocument BuildSnapshot(TexturePaintDocument source, TextureStore store,
            IReadOnlyDictionary<EditableTextureTarget, long> persistedRevisions,
            bool recoverySnapshot, out List<PixelCaptureWork> work,
            IDictionary<EditableTextureTarget, long> capturedRevisions)
        {
            source ??= ScriptableObject.CreateInstance<TexturePaintDocument>();
            source.Migrate();
            TexturePaintDocument snapshot = ScriptableObject.CreateInstance<TexturePaintDocument>();
            snapshot.hideFlags = HideFlags.HideAndDontSave;
            snapshot.schemaVersion = TexturePaintDocument.CurrentSchemaVersion;
            snapshot.documentId = source.documentId;
            snapshot.revisionId = Guid.NewGuid().ToString("N");
            snapshot.avatarName = source.avatarName;
            snapshot.avatarGlobalObjectId = source.avatarGlobalObjectId;
            snapshot.launchContext = source.launchContext?.Clone();
            snapshot.createdUtc = string.IsNullOrEmpty(source.createdUtc) ? DateTime.UtcNow.ToString("O") : source.createdUtc;
            snapshot.lastSavedUtc = DateTime.UtcNow.ToString("O");
            snapshot.recoverySnapshot = recoverySnapshot;
            snapshot.editorStateJson = source.editorStateJson;
            work = new List<PixelCaptureWork>();

            List<TexturePaintDocumentSurface> previousSurfaces = source.surfaces != null
                ? new List<TexturePaintDocumentSurface>(source.surfaces)
                : new List<TexturePaintDocumentSurface>();
            HashSet<TexturePaintDocumentSurface> matchedPrevious = new HashSet<TexturePaintDocumentSurface>();
            for (int setIndex = 0; setIndex < store.Sets.Count; setIndex++)
            {
                TextureSet set = store.Sets[setIndex];
                TexturePaintDocumentSurface previous = source.FindSurface(set.persistentId) ?? FindFallback(source, set);
                TexturePaintSurfaceFingerprint fingerprint = TexturePaintSurfaceFingerprintUtility.Compute(set.surface?.mesh);
                bool previousCompatible = previous != null && (string.IsNullOrEmpty(previous.uvSignature) ||
                    string.Equals(previous.uvSignature, fingerprint.uv, StringComparison.Ordinal));
                if (previousCompatible) matchedPrevious.Add(previous);

                TexturePaintDocumentSurface surface = new TexturePaintDocumentSurface
                {
                    stableId = set.persistentId,
                    materialName = set.Name,
                    umaMaterialGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(set.umaMaterial)),
                    meshSignature = fingerprint.geometry,
                    topologySignature = fingerprint.topology,
                    uvSignature = fingerprint.uv,
                    materialSignature = MaterialSignature(set),
                    fallbackRendererIndex = set.surface?.rendererIndex ?? -1,
                    fallbackSubmeshIndex = set.surface?.sourceSubmeshIndex ?? -1,
                    activeLayer = set.activeLayerIndex,
                    normalControlStrength = set.normalControlStrength,
                    normalControlRadius = set.normalControlRadius,
                    normalControlInvert = set.normalControlInvert,
                    baseStrokes = CloneStrokes(set.baseStrokes),
                    slotNames = set.surface != null ? new List<string>(set.surface.slotNames) : new List<string>()
                };

                foreach (KeyValuePair<TexturePaintChannel, TextureChannelTarget> pair in set.channels)
                {
                    TextureChannelTarget current = pair.Value;
                    string key = PixelStorageKey(set.persistentId, null, pair.Key);
                    TexturePaintPixelData previousPixels = previousCompatible
                        ? FindBasePixels(previous, pair.Key) : null;
                    TexturePaintPixelData pixels = PreparePixels(current.editable, previousPixels, key,
                        persistedRevisions, capturedRevisions, work);
                    surface.baseChannels.Add(new TexturePaintDocumentChannel
                    {
                        channel = pair.Key,
                        materialProperty = current.materialProperty,
                        sourceKeyword = current.sourceKeyword,
                        umaChannelIndex = current.umaChannelIndex,
                        renderTextureFormat = current.format,
                        sRGB = current.sRGB,
                        adjustments = current.adjustments?.Clone() ??
                            new TexturePaintChannelAdjustments(),
                        pixels = pixels
                    });
                }

                for (int layerIndex = 0; layerIndex < set.layers.Count; layerIndex++)
                {
                    TexturePaintLayer layer = set.layers[layerIndex];
                    TexturePaintDocumentLayer savedLayer = CaptureLayerMetadata(layer);
                    TexturePaintDocumentLayer previousLayer = previousCompatible ? FindLayer(previous, layer.id) : null;
                    foreach (KeyValuePair<TexturePaintChannel, EditableTextureTarget> pair in layer.channels)
                    {
                        TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(pair.Key, false) ??
                            new TexturePaintLayerChannelSettings
                            {
                                channel = pair.Key,
                                opacity = 1f,
                                blendMode = layer.blendMode
                            };
                        string key = PixelStorageKey(set.persistentId, layer.id, pair.Key);
                        TexturePaintPixelData previousPixels = FindLayerPixels(previousLayer, pair.Key);
                        var savedChannel = new TexturePaintDocumentLayerChannel
                        {
                            channel = pair.Key,
                            settings = settings.Clone(),
                            pixels = PreparePixels(pair.Value, previousPixels, key, persistedRevisions,
                                capturedRevisions, work)
                        };
                        savedChannel.SetSourceSettings(settings.sourceSettings);
                        savedLayer.channels.Add(savedChannel);
                    }
                    if (layer.layerMask?.target != null)
                    {
                        TexturePaintPixelData previousPixels = previousLayer?.maskPixels;
                        savedLayer.maskPixels = PreparePixels(layer.layerMask.target, previousPixels,
                            MaskPixelStorageKey(set.persistentId, layer.id), persistedRevisions,
                            capturedRevisions, work);
                    }
                    surface.layers.Add(savedLayer);
                }
                snapshot.surfaces.Add(surface);
            }

            for (int i = 0; i < previousSurfaces.Count; i++)
            {
                TexturePaintDocumentSurface previous = previousSurfaces[i];
                if (previous == null || matchedPrevious.Contains(previous)) continue;
                TexturePaintDocumentSurface orphan = CloneSurface(previous);
                orphan.orphaned = true;
                orphan.orphanReason = "No exact UV-compatible reconstructed surface existed when the document was saved.";
                snapshot.surfaces.Add(orphan);
            }
            return snapshot;
        }

        private static TexturePaintPixelData PreparePixels(EditableTextureTarget target,
            TexturePaintPixelData previous, string key,
            IReadOnlyDictionary<EditableTextureTarget, long> persistedRevisions,
            IDictionary<EditableTextureTarget, long> capturedRevisions, ICollection<PixelCaptureWork> work)
        {
            if (target != null && previous != null && previous.HasData && persistedRevisions != null &&
                persistedRevisions.TryGetValue(target, out long revision) && revision == target.Revision)
            {
                capturedRevisions[target] = revision;
                TexturePaintPixelData reused = ClonePixels(previous);
                reused.storageKey = key;
                return reused;
            }

            TexturePaintPixelData destination = new TexturePaintPixelData { storageKey = key };
            if (target?.Front != null)
            {
                work.Add(new PixelCaptureWork
                {
                    target = target,
                    source = target.Front,
                    textureFormat = ToTextureFormat(target.Front.format),
                    destination = destination,
                    revision = target.Revision
                });
            }
            return destination;
        }

        private static TexturePaintDocumentLayer CaptureLayerMetadata(TexturePaintLayer layer)
        {
            layer.layerMask?.NormalizePaintSource();
            return new TexturePaintDocumentLayer
            {
                id = layer.id,
                logicalLayerId = layer.logicalLayerId,
                paintTargetId = layer.paintTargetId,
                parentId = layer.parentId,
                name = layer.name,
                kind = layer.kind,
                visible = layer.visible,
                opacity = layer.opacity,
                blendMode = layer.blendMode,
                effects = layer.effects?.Clone() ?? new TexturePaintLayerEffects(),
                fillChannel = layer.fillChannel,
                fillColor = layer.fillColor,
                fillSettings = layer.fillSettings?.Clone(),
                paintSettings = layer.paintSettings?.Clone(),
                spline = layer.IsSplineLayer ? CloneSpline(layer.spline) : null,
                splineSettings = layer.IsSplineLayer ? CloneSplineSettings(layer.splineSettings) : null,
                pluginId = layer.pluginId,
                pluginVersion = layer.pluginVersion,
                pluginParametersJson = layer.pluginParametersJson,
                pluginParameters = layer.pluginParameters?.Clone() ?? new TexturePaintPluginParameterSet(),
                pluginStale = layer.pluginStale,
                pluginLastError = layer.pluginLastError,
                proceduralGroupKey = layer.proceduralGroupKey,
                sourceMaterialPresetId = layer.sourceMaterialPresetId,
                sourceMaterialPresetRevision = layer.sourceMaterialPresetRevision,
                sourceMaterialPresetLayerId = layer.sourceMaterialPresetLayerId,
                hasMask = layer.layerMask?.target != null,
                maskBaseValue = layer.layerMask?.baseValue ?? 1f,
                maskEffects = layer.layerMask?.effects?.Clone() ?? new TexturePaintLayerMaskEffects(),
                maskSourceSettings = layer.layerMask?.sourceSettings?.Clone() ??
                    TexturePaintLayerMask.DefaultSourceSettings(),
                maskSourceChannel = layer.layerMask?.sourceChannel ?? TexturePaintChannel.Albedo,
                maskPluginId = layer.layerMask?.pluginId,
                maskPluginVersion = layer.layerMask?.pluginVersion,
                maskPluginParametersJson = layer.layerMask?.pluginParametersJson,
                maskPluginParameters = layer.layerMask?.pluginParameters?.Clone() ??
                    new TexturePaintPluginParameterSet(),
                maskPluginStale = layer.layerMask?.pluginStale ?? true,
                maskPluginLastError = layer.layerMask?.pluginLastError,
                strokes = CloneStrokes(layer.strokes)
            };
        }

        private static TexturePaintDocumentSurface CloneSurface(TexturePaintDocumentSurface source)
        {
            if (source == null) return null;
            TexturePaintDocumentSurface clone = new TexturePaintDocumentSurface
            {
                stableId = source.stableId,
                materialName = source.materialName,
                umaMaterialGuid = source.umaMaterialGuid,
                meshSignature = source.meshSignature,
                topologySignature = source.topologySignature,
                uvSignature = source.uvSignature,
                materialSignature = source.materialSignature,
                orphaned = source.orphaned,
                orphanReason = source.orphanReason,
                slotNames = source.slotNames != null ? new List<string>(source.slotNames) : new List<string>(),
                fallbackRendererIndex = source.fallbackRendererIndex,
                fallbackSubmeshIndex = source.fallbackSubmeshIndex,
                activeLayer = source.activeLayer,
                normalControlStrength = source.normalControlStrength,
                normalControlRadius = source.normalControlRadius,
                normalControlInvert = source.normalControlInvert,
                baseStrokes = CloneStrokes(source.baseStrokes)
            };
            if (source.baseChannels != null)
                for (int i = 0; i < source.baseChannels.Count; i++)
                {
                    TexturePaintDocumentChannel channel = source.baseChannels[i];
                    if (channel == null) continue;
                    clone.baseChannels.Add(new TexturePaintDocumentChannel
                    {
                        channel = channel.channel,
                        materialProperty = channel.materialProperty,
                        sourceKeyword = channel.sourceKeyword,
                        umaChannelIndex = channel.umaChannelIndex,
                        renderTextureFormat = channel.renderTextureFormat,
                        sRGB = channel.sRGB,
                        adjustments = channel.adjustments?.Clone() ??
                            new TexturePaintChannelAdjustments(),
                        pixels = ClonePixels(channel.pixels)
                    });
                }
            if (source.layers != null)
                for (int i = 0; i < source.layers.Count; i++) clone.layers.Add(CloneDocumentLayer(source.layers[i]));
            return clone;
        }

        private static TexturePaintDocumentLayer CloneDocumentLayer(TexturePaintDocumentLayer source)
        {
            if (source == null) return null;
            TexturePaintDocumentLayer clone = new TexturePaintDocumentLayer
            {
                id = source.id,
                logicalLayerId = source.logicalLayerId,
                paintTargetId = source.paintTargetId,
                parentId = source.parentId,
                name = source.name,
                kind = source.kind,
                visible = source.visible,
                opacity = source.opacity,
                blendMode = source.blendMode,
                effects = source.effects?.Clone() ?? new TexturePaintLayerEffects(),
                fillChannel = source.fillChannel,
                fillColor = source.fillColor,
                fillSettings = source.fillSettings?.Clone(),
                paintSettings = source.paintSettings?.Clone(),
                spline = CloneSpline(source.spline),
                splineSettings = CloneSplineSettings(source.splineSettings),
                pluginId = source.pluginId,
                pluginVersion = source.pluginVersion,
                pluginParametersJson = source.pluginParametersJson,
                pluginParameters = source.pluginParameters?.Clone() ?? new TexturePaintPluginParameterSet(),
                pluginStale = source.pluginStale,
                pluginLastError = source.pluginLastError,
                proceduralGroupKey = source.proceduralGroupKey,
                sourceMaterialPresetId = source.sourceMaterialPresetId,
                sourceMaterialPresetRevision = source.sourceMaterialPresetRevision,
                sourceMaterialPresetLayerId = source.sourceMaterialPresetLayerId,
                hasMask = source.hasMask,
                maskBaseValue = source.maskBaseValue,
                maskEffects = source.maskEffects?.Clone() ?? new TexturePaintLayerMaskEffects(),
                maskSourceSettings = source.maskSourceSettings?.Clone() ??
                    TexturePaintLayerMask.DefaultSourceSettings(),
                maskSourceChannel = source.maskSourceChannel,
                maskPluginId = source.maskPluginId,
                maskPluginVersion = source.maskPluginVersion,
                maskPluginParametersJson = source.maskPluginParametersJson,
                maskPluginParameters = source.maskPluginParameters?.Clone() ??
                    new TexturePaintPluginParameterSet(),
                maskPluginStale = source.maskPluginStale,
                maskPluginLastError = source.maskPluginLastError,
                maskPixels = ClonePixels(source.maskPixels),
                strokes = CloneStrokes(source.strokes)
            };
            if (source.channels != null)
                for (int i = 0; i < source.channels.Count; i++)
                {
                    TexturePaintDocumentLayerChannel channel = source.channels[i];
                    if (channel == null) continue;
                    var clonedChannel = new TexturePaintDocumentLayerChannel
                    {
                        channel = channel.channel,
                        settings = channel.settings?.Clone(),
                        pixels = ClonePixels(channel.pixels)
                    };
                    clonedChannel.SetSourceSettings(channel.GetSourceSettings());
                    clone.channels.Add(clonedChannel);
                }
            return clone;
        }

        private static TexturePaintPixelData ClonePixels(TexturePaintPixelData source)
        {
            if (source == null) return new TexturePaintPixelData();
            return new TexturePaintPixelData
            {
                width = source.width,
                height = source.height,
                textureFormat = source.textureFormat,
                linear = source.linear,
                uncompressedByteCount = source.uncompressedByteCount,
                storageKey = source.storageKey,
                checksum = source.checksum,
                recoveryBlobKey = source.recoveryBlobKey,
                dataAsset = source.dataAsset,
                compressedBytes = source.compressedBytes
            };
        }

        private static TexturePaintPixelData FindBasePixels(TexturePaintDocumentSurface surface, TexturePaintChannel channel)
        {
            if (surface?.baseChannels == null) return null;
            for (int i = 0; i < surface.baseChannels.Count; i++)
                if (surface.baseChannels[i] != null && surface.baseChannels[i].channel == channel)
                    return surface.baseChannels[i].pixels;
            return null;
        }

        private static TexturePaintDocumentLayer FindLayer(TexturePaintDocumentSurface surface, string id)
        {
            if (surface?.layers == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < surface.layers.Count; i++)
                if (surface.layers[i] != null && string.Equals(surface.layers[i].id, id, StringComparison.Ordinal))
                    return surface.layers[i];
            return null;
        }

        private static TexturePaintPixelData FindLayerPixels(TexturePaintDocumentLayer layer, TexturePaintChannel channel)
        {
            if (layer?.channels == null) return null;
            for (int i = 0; i < layer.channels.Count; i++)
                if (layer.channels[i] != null && layer.channels[i].channel == channel)
                    return layer.channels[i].pixels;
            return null;
        }

        private static string PixelStorageKey(string surfaceId, string layerId, TexturePaintChannel channel)
        {
            return string.IsNullOrEmpty(layerId)
                ? $"{surfaceId}/base/{channel}"
                : $"{surfaceId}/layer/{layerId}/{channel}";
        }

        private static string MaskPixelStorageKey(string surfaceId, string layerId)
            => $"{surfaceId}/layer/{layerId}/mask";

        private static CapturedPixels CompressPixels(byte[] raw)
        {
            byte[] compressed = Compress(raw, System.IO.Compression.CompressionLevel.Fastest);
            using SHA256 sha = SHA256.Create();
            string checksum = BitConverter.ToString(sha.ComputeHash(compressed)).Replace("-", string.Empty).ToLowerInvariant();
            return new CapturedPixels(compressed, raw?.Length ?? 0, checksum);
        }

        private static TexturePaintDocumentLayer CaptureLayer(TexturePaintLayer layer)
        {
            TexturePaintDocumentLayer saved = CaptureLayerMetadata(layer);
            foreach (KeyValuePair<TexturePaintChannel, EditableTextureTarget> pair in layer.channels)
            {
                TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(pair.Key, false) ??
                    new TexturePaintLayerChannelSettings
                    {
                        channel = pair.Key,
                        opacity = 1f,
                        blendMode = layer.blendMode
                    };
                var savedChannel = new TexturePaintDocumentLayerChannel
                {
                    channel = pair.Key,
                    settings = settings.Clone(),
                    pixels = Capture(pair.Value.Front, pair.Value.Front.sRGB)
                };
                savedChannel.SetSourceSettings(settings.sourceSettings);
                saved.channels.Add(savedChannel);
            }
            if (layer.layerMask?.target?.Front != null)
                saved.maskPixels = Capture(layer.layerMask.target.Front, false);
            return saved;
        }

        private static void RestoreBaseChannels(TexturePaintDocumentSurface saved, TextureSet set)
        {
            for (int i = 0; i < saved.baseChannels.Count; i++)
            {
                TexturePaintDocumentChannel channel = saved.baseChannels[i];
                TextureChannelTarget target = set.GetChannel(channel.channel);
                if (target == null) continue;
                Restore(channel.pixels, target.editable);
            }
        }

        private static void RestoreChannelAdjustments(TexturePaintDocumentSurface saved, TextureSet set)
        {
            if (saved?.baseChannels == null || set == null) return;
            for (int i = 0; i < saved.baseChannels.Count; i++)
            {
                TexturePaintDocumentChannel channel = saved.baseChannels[i];
                TextureChannelTarget target = channel != null ? set.GetChannel(channel.channel) : null;
                if (target == null) continue;
                target.adjustments = channel.adjustments?.Clone() ??
                    new TexturePaintChannelAdjustments();
                target.adjustments.Normalize();
            }
        }

        private static void RestoreLayer(TexturePaintDocumentLayer saved, TextureSet set)
        {
            TexturePaintLayer layer = set.AddLayer(saved.name);
            layer.id = string.IsNullOrEmpty(saved.id) ? Guid.NewGuid().ToString("N") : saved.id;
            layer.logicalLayerId = saved.logicalLayerId;
            layer.paintTargetId = saved.paintTargetId;
            layer.parentId = saved.parentId;
            layer.kind = saved.kind;
            layer.visible = saved.visible;
            layer.opacity = saved.opacity;
            layer.blendMode = saved.blendMode;
            layer.effects = saved.effects?.Clone() ?? new TexturePaintLayerEffects();
            layer.fillChannel = saved.fillChannel;
            layer.fillColor = saved.fillColor;
            layer.fillSettings = saved.fillSettings?.Clone();
            layer.paintSettings = saved.paintSettings?.Clone();
            layer.spline = layer.IsSplineLayer ? CloneSpline(saved.spline) : null;
            layer.splineSettings = layer.IsSplineLayer ? CloneSplineSettings(saved.splineSettings) : null;
            layer.pluginId = saved.pluginId;
            layer.pluginVersion = saved.pluginVersion;
            layer.pluginParametersJson = saved.pluginParametersJson;
            layer.pluginParameters = saved.pluginParameters?.Clone() ??
                (!string.IsNullOrEmpty(saved.pluginParametersJson)
                    ? JsonUtility.FromJson<TexturePaintPluginParameterSet>(saved.pluginParametersJson)
                    : new TexturePaintPluginParameterSet());
            layer.pluginStale = saved.pluginStale;
            layer.pluginLastError = saved.pluginLastError;
            layer.proceduralGroupKey = saved.proceduralGroupKey;
            layer.sourceMaterialPresetId = saved.sourceMaterialPresetId;
            layer.sourceMaterialPresetRevision = saved.sourceMaterialPresetRevision;
            layer.sourceMaterialPresetLayerId = saved.sourceMaterialPresetLayerId;
            layer.NormalizeKindPayload();
            layer.strokes.AddRange(CloneStrokes(saved.strokes));
            for (int i = 0; i < saved.channels.Count; i++)
            {
                TexturePaintDocumentLayerChannel savedChannel = saved.channels[i];
                TextureChannelTarget baseChannel = set.GetChannel(savedChannel.channel);
                if (baseChannel == null) continue;
                EditableTextureTarget target = new EditableTextureTarget(layer.name + " " + savedChannel.channel,
                    baseChannel.Texture.width, baseChannel.Texture.height, baseChannel.format, null, Color.clear);
                Restore(savedChannel.pixels, target);
                layer.channels[savedChannel.channel] = target;
                TexturePaintLayerChannelSettings settings = savedChannel.settings?.Clone() ??
                    new TexturePaintLayerChannelSettings { channel = savedChannel.channel };
                settings.channel = savedChannel.channel;
                settings.sourceSettings = savedChannel.GetSourceSettings();
                layer.channelSettings[savedChannel.channel] = settings;
            }
            if (saved.hasMask)
            {
                TexturePaintLayerMask mask = set.AddLayerMask(layer, saved.maskBaseValue);
                if (mask != null)
                {
                    mask.effects = saved.maskEffects?.Clone() ?? new TexturePaintLayerMaskEffects();
                    mask.sourceSettings = saved.maskSourceSettings?.Clone() ??
                        TexturePaintLayerMask.DefaultSourceSettings();
                    mask.sourceChannel = saved.maskSourceChannel;
                    mask.pluginId = saved.maskPluginId;
                    mask.pluginVersion = saved.maskPluginVersion;
                    mask.pluginParametersJson = saved.maskPluginParametersJson;
                    mask.pluginParameters = saved.maskPluginParameters?.Clone() ??
                        new TexturePaintPluginParameterSet();
                    mask.pluginStale = saved.maskPluginStale;
                    mask.pluginLastError = saved.maskPluginLastError;
                    mask.NormalizePaintSource();
                    Restore(saved.maskPixels, mask.target);
                }
            }
        }

        private static TexturePaintDocumentSurface FindFallback(TexturePaintDocument document, TextureSet set)
        {
            if (document?.surfaces == null || set == null) return null;
            TexturePaintSurfaceFingerprint current = TexturePaintSurfaceFingerprintUtility.Compute(set.surface?.mesh);
            string currentMaterialSignature = MaterialSignature(set);
            string currentUmaMaterialGuid = AssetDatabase.AssetPathToGUID(
                AssetDatabase.GetAssetPath(set.umaMaterial));
            string currentMaterialName = StableMaterialName(set.Name);
            int currentRendererIndex = set.surface?.rendererIndex ?? -1;
            int currentSubmeshIndex = set.surface?.sourceSubmeshIndex ?? -1;
            TexturePaintDocumentSurface best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < document.surfaces.Count; i++)
            {
                TexturePaintDocumentSurface candidate = document.surfaces[i];
                if (candidate == null || candidate.orphaned) continue;
                if (!SlotsOverlap(candidate.slotNames, set.surface?.slotNames)) continue;

                bool topologyMatches = !string.IsNullOrEmpty(candidate.topologySignature) &&
                    string.Equals(candidate.topologySignature, current.topology, StringComparison.Ordinal);
                bool uvMatches = !string.IsNullOrEmpty(candidate.uvSignature) &&
                    string.Equals(candidate.uvSignature, current.uv, StringComparison.Ordinal);
                bool geometryMatches = !string.IsNullOrEmpty(candidate.meshSignature) &&
                    string.Equals(candidate.meshSignature, current.geometry, StringComparison.Ordinal);
                bool materialMatches = !string.IsNullOrEmpty(candidate.materialSignature) &&
                    string.Equals(candidate.materialSignature, currentMaterialSignature, StringComparison.Ordinal);
                bool umaMaterialMatches = !string.IsNullOrEmpty(candidate.umaMaterialGuid) &&
                    !string.IsNullOrEmpty(currentUmaMaterialGuid) &&
                    string.Equals(candidate.umaMaterialGuid, currentUmaMaterialGuid, StringComparison.Ordinal);
                bool materialNameMatches = string.Equals(StableMaterialName(candidate.materialName),
                    currentMaterialName, StringComparison.Ordinal);
                bool locationMatches = candidate.fallbackRendererIndex >= 0 &&
                    candidate.fallbackSubmeshIndex >= 0 && currentRendererIndex >= 0 &&
                    currentSubmeshIndex >= 0 && candidate.fallbackRendererIndex == currentRendererIndex &&
                    candidate.fallbackSubmeshIndex == currentSubmeshIndex;

                // Raster pixels are safe when the UV sequence is unchanged, even if regenerated
                // triangle ordering changed. A topology match remains eligible for the existing
                // reprojectable-content path. Renderer/submesh is only accepted when material
                // evidence also agrees, preventing an index shift from binding unrelated content.
                bool eligible = topologyMatches || uvMatches ||
                    (locationMatches && (materialMatches || umaMaterialMatches || materialNameMatches)) ||
                    (string.IsNullOrEmpty(candidate.topologySignature) && materialNameMatches);
                if (!eligible) continue;

                int score = 0;
                if (topologyMatches && uvMatches) score += 10000;
                else
                {
                    if (uvMatches) score += 4000;
                    if (topologyMatches) score += 3000;
                }
                if (locationMatches) score += 1000;
                if (umaMaterialMatches) score += 500;
                if (materialMatches) score += 250;
                if (materialNameMatches) score += 125;
                if (SlotSetsEqual(candidate.slotNames, set.surface?.slotNames)) score += 80;
                if (geometryMatches) score += 40;
                if (score <= bestScore) continue;
                best = candidate;
                bestScore = score;
            }
            return best;
        }

        private static void RestoreReprojectableContent(TexturePaintDocumentSurface saved, TextureSet set)
        {
            int resetLayerMasks = 0;
            set.baseStrokes.Clear();
            set.baseStrokes.AddRange(CloneStrokes(saved.baseStrokes));
            for (int i = 0; i < set.layers.Count; i++) set.layers[i].Dispose();
            set.layers.Clear();
            set.activeLayerIndex = -1;
            for (int i = 0; i < saved.layers.Count; i++)
            {
                TexturePaintDocumentLayer source = saved.layers[i];
                TexturePaintLayer layer = source.kind == TexturePaintLayerKind.Fill
                    ? set.AddFillLayer(source.name, source.fillChannel, source.fillSettings ??
                        new TexturePaintFillSettings
                        {
                            source = TexturePaintBrushSource.Color,
                            color = source.fillColor
                        })
                    : set.AddLayer(source.name);
                if (layer == null) continue;
                layer.id = source.id; layer.logicalLayerId = source.logicalLayerId;
                layer.paintTargetId = source.paintTargetId; layer.parentId = source.parentId; layer.kind = source.kind;
                layer.visible = source.visible; layer.opacity = source.opacity; layer.blendMode = source.blendMode;
                layer.effects = source.effects?.Clone() ?? new TexturePaintLayerEffects();
                layer.fillChannel = source.fillChannel; layer.fillColor = source.fillColor;
                layer.fillSettings = source.fillSettings?.Clone();
                layer.paintSettings = source.paintSettings?.Clone();
                layer.spline = layer.IsSplineLayer ? CloneSpline(source.spline) : null;
                layer.splineSettings = layer.IsSplineLayer ? CloneSplineSettings(source.splineSettings) : null;
                layer.pluginId = source.pluginId; layer.pluginVersion = source.pluginVersion;
                layer.pluginParametersJson = source.pluginParametersJson;
                layer.pluginParameters = source.pluginParameters?.Clone() ??
                    new TexturePaintPluginParameterSet();
                // Cached procedural pixels are tied to the saved UV layout and are deliberately
                // not restored by this reprojectable-content path. Preserve the definition, but
                // require Plugin layers to regenerate against the new layout.
                layer.pluginStale = source.kind == TexturePaintLayerKind.Plugin || source.pluginStale;
                layer.pluginLastError = source.kind == TexturePaintLayerKind.Plugin
                    ? null : source.pluginLastError;
                layer.proceduralGroupKey = source.proceduralGroupKey;
                layer.sourceMaterialPresetId = source.sourceMaterialPresetId;
                layer.sourceMaterialPresetRevision = source.sourceMaterialPresetRevision;
                layer.sourceMaterialPresetLayerId = source.sourceMaterialPresetLayerId;
                layer.NormalizeKindPayload();
                layer.strokes.AddRange(CloneStrokes(source.strokes));
                if (source.hasMask)
                {
                    TexturePaintLayerMask mask = set.AddLayerMask(layer, source.maskBaseValue);
                    if (mask != null)
                    {
                        mask.effects = source.maskEffects?.Clone() ?? new TexturePaintLayerMaskEffects();
                        mask.sourceSettings = source.maskSourceSettings?.Clone() ??
                            TexturePaintLayerMask.DefaultSourceSettings();
                        mask.sourceChannel = source.maskSourceChannel;
                        mask.pluginId = source.maskPluginId;
                        mask.pluginVersion = source.maskPluginVersion;
                        mask.pluginParametersJson = source.maskPluginParametersJson;
                        mask.pluginParameters = source.maskPluginParameters?.Clone() ??
                            new TexturePaintPluginParameterSet();
                        mask.pluginStale = true;
                        mask.pluginLastError = null;
                        mask.NormalizePaintSource();
                        // Pixel-space masks are not valid after a UV-layout change. Keep the
                        // authored base value and procedural effects, but reset editable pixels
                        // instead of silently applying them to unrelated texels.
                        float value = Mathf.Clamp01(source.maskBaseValue);
                        mask.target.Reset(null, new Color(value, value, value, 1f));
                        resetLayerMasks++;
                    }
                }
            }
            set.activeLayerIndex = Mathf.Clamp(saved.activeLayer, -1, set.layers.Count - 1);
            if (resetLayerMasks > 0)
                Debug.LogWarning($"Overlay Painter reset {resetLayerMasks} editable layer mask" +
                    (resetLayerMasks == 1 ? string.Empty : "s") +
                    $" to its base value on '{set.Name}' because the UV layout changed.");
        }

        private static bool SlotsOverlap(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a == null || b == null) return false;
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < b.Count; j++)
                    if (string.Equals(a[i], b[j], StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool SlotSetsEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < b.Count; j++)
                    if (string.Equals(a[i], b[j], StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                if (!found) return false;
            }
            return true;
        }

        internal static string StableMaterialName(string materialName)
        {
            if (string.IsNullOrEmpty(materialName)) return string.Empty;
            const string marker = "_Genb_";
            int searchIndex = 0;
            while (searchIndex < materialName.Length)
            {
                int markerIndex = materialName.IndexOf(marker, searchIndex, StringComparison.Ordinal);
                if (markerIndex < 0) break;
                int digitsStart = markerIndex + marker.Length;
                int digitsEnd = digitsStart;
                while (digitsEnd < materialName.Length && char.IsDigit(materialName[digitsEnd])) digitsEnd++;
                if (digitsEnd == digitsStart)
                {
                    searchIndex = digitsStart;
                    continue;
                }
                // Keep the semantic "_Genb" marker but discard the random number and its
                // separator. Any stable suffix appended by reconstruction remains part of the id.
                materialName = materialName.Remove(markerIndex + "_Genb".Length,
                    digitsEnd - (markerIndex + "_Genb".Length));
                searchIndex = markerIndex + "_Genb".Length;
            }
            return materialName;
        }

        private static string MaterialSignature(TextureSet set)
        {
            string umaGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(set?.umaMaterial));
            string materialGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(set?.surface?.sourceMaterial));
            string shader = set?.surface?.sourceMaterial?.shader?.name ?? string.Empty;
            return Hash128.Compute(umaGuid + "|" + materialGuid + "|" + shader).ToString();
        }

        private static TexturePaintPixelData Capture(RenderTexture source, bool sRGB)
        {
            if (source == null) return new TexturePaintPixelData();
            TextureFormat format = ToTextureFormat(source.format);
            bool linear = !sRGB;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            Texture2D readback = new Texture2D(source.width, source.height, format, false, linear);
            readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            readback.Apply(false, false);
            byte[] raw = readback.GetRawTextureData<byte>().ToArray();
            UnityEngine.Object.DestroyImmediate(readback);
            RenderTexture.active = previous;
            return new TexturePaintPixelData
            {
                width = source.width,
                height = source.height,
                textureFormat = format,
                linear = linear,
                uncompressedByteCount = raw.Length,
                compressedBytes = Compress(raw)
            };
        }

        private static void Restore(TexturePaintPixelData pixels, EditableTextureTarget destination)
        {
            if (pixels == null || !pixels.HasData || destination == null) return;
            byte[] compressed = pixels.GetCompressedBytes();
            if (!TexturePaintDocumentBlobUtility.VerifyChecksum(compressed, pixels.checksum))
                throw new InvalidDataException("Overlay Painter document data failed its checksum: " + pixels.storageKey);
            byte[] raw = Decompress(compressed, pixels.uncompressedByteCount);
            if (raw == null || raw.Length == 0) return;
            Texture2D texture = new Texture2D(pixels.width, pixels.height, pixels.textureFormat, false, pixels.linear);
            texture.LoadRawTextureData(raw);
            texture.Apply(false, false);
            destination.Reset(texture, Color.clear);
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static byte[] Compress(byte[] raw)
        {
            return Compress(raw, System.IO.Compression.CompressionLevel.Optimal);
        }

        private static byte[] Compress(byte[] raw, System.IO.Compression.CompressionLevel level)
        {
            using MemoryStream output = new MemoryStream();
            using (DeflateStream stream = new DeflateStream(output, level, true))
                stream.Write(raw, 0, raw.Length);
            return output.ToArray();
        }

        private static byte[] Decompress(byte[] compressed, int expectedLength)
        {
            using MemoryStream input = new MemoryStream(compressed);
            using DeflateStream stream = new DeflateStream(input, CompressionMode.Decompress);
            using MemoryStream output = expectedLength > 0 ? new MemoryStream(expectedLength) : new MemoryStream();
            stream.CopyTo(output);
            return output.ToArray();
        }

        private static TextureFormat ToTextureFormat(RenderTextureFormat format)
        {
            switch (format)
            {
                case RenderTextureFormat.ARGBHalf: return TextureFormat.RGBAHalf;
                case RenderTextureFormat.ARGBFloat: return TextureFormat.RGBAFloat;
                case RenderTextureFormat.RHalf: return TextureFormat.RHalf;
                case RenderTextureFormat.RFloat: return TextureFormat.RFloat;
                default: return TextureFormat.RGBA32;
            }
        }

        private static TexturePaintSpline CloneSpline(TexturePaintSpline source)
        {
            return source == null ? null : JsonUtility.FromJson<TexturePaintSpline>(JsonUtility.ToJson(source));
        }

        private static TexturePaintSplineSettings CloneSplineSettings(TexturePaintSplineSettings source)
        {
            return source?.Clone();
        }

        private static List<TexturePaintStrokeRecord> CloneStrokes(IReadOnlyList<TexturePaintStrokeRecord> source)
        {
            List<TexturePaintStrokeRecord> result = new List<TexturePaintStrokeRecord>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                TexturePaintStrokeRecord stroke = source[i];
                if (stroke == null) continue;
                result.Add(new TexturePaintStrokeRecord
                {
                    id = stroke.id,
                    createdUtc = stroke.createdUtc,
                    historyGroupKey = stroke.historyGroupKey,
                    tool = stroke.tool,
                    channel = stroke.channel,
                    samples = stroke.samples != null ? new List<StrokeSample>(stroke.samples) : new List<StrokeSample>()
                });
            }
            return result;
        }

        private static string MeshSignature(Mesh mesh) => TexturePaintSurfaceFingerprintUtility.Compute(mesh).geometry;

    }
}
