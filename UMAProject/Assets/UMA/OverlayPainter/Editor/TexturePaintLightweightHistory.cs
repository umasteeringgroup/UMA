using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        private const int LightweightHistoryCapacity = 64;

        [NonSerialized] private List<LightweightEditCommand> lightweightUndo = new List<LightweightEditCommand>();
        [NonSerialized] private List<LightweightEditCommand> lightweightRedo = new List<LightweightEditCommand>();
        [NonSerialized] private string pendingLayerCreationLabel;
        [NonSerialized] private PendingPathEdit pendingPathEdit;
        [NonSerialized] private bool applyingLightweightHistory;
        [NonSerialized] private bool suppressLogicalLayerRepair;
        [NonSerialized] private List<LayerLocation> strokeCreatedLayers = new List<LayerLocation>();

        private sealed class LightweightEditCommand : IDisposable
        {
            public readonly string label;
            private readonly Action undo;
            private readonly Action redo;
            private readonly Action dispose;

            public LightweightEditCommand(string label, Action undo, Action redo, Action dispose)
            {
                this.label = label;
                this.undo = undo;
                this.redo = redo;
                this.dispose = dispose;
            }

            public void Undo() => undo?.Invoke();
            public void Redo() => redo?.Invoke();
            public void Dispose() => dispose?.Invoke();
        }

        private sealed class PendingPathEdit
        {
            public string label;
            public TextureSet set;
            public TexturePaintLayer layer;
            public PathEditState before;
            public PathEditState after;
            public bool deferred;
        }

        private sealed class PathEditState
        {
            public TexturePaintSpline spline;
            public TexturePaintSplineSettings settings;
            public int selectedPoint;
        }

        private sealed class LayerLocation
        {
            public TextureSet set;
            public TexturePaintLayer layer;
            public int index;
        }

        private sealed class LayerMetadataState
        {
            public string name;
            public float opacity;
            public TexturePaintBlendMode blendMode;
            public Dictionary<TexturePaintChannel, TexturePaintBlendMode> channelBlends;
        }

        private sealed class MergedLayerState
        {
            public TextureSet set;
            public TexturePaintLayer lower;
            public TexturePaintLayer upper;
            public TexturePaintLayer merged;
            public int index;
        }

        private bool CanUndoLightweight => lightweightUndo != null && lightweightUndo.Count > 0;
        private bool CanRedoLightweight => lightweightRedo != null && lightweightRedo.Count > 0;
        private string LightweightUndoLabel => CanUndoLightweight ? lightweightUndo[lightweightUndo.Count - 1].label : null;
        private string LightweightRedoLabel => CanRedoLightweight ? lightweightRedo[lightweightRedo.Count - 1].label : null;
        internal bool CanUndoPluginTransaction => string.Equals(LightweightUndoLabel, "Plugin Transaction",
            StringComparison.Ordinal);
        internal bool CanRedoPluginTransaction => string.Equals(LightweightRedoLabel, "Plugin Transaction",
            StringComparison.Ordinal);

        private void PushLightweightCommand(string label, Action undoAction, Action redoAction,
            Action disposeAction = null)
        {
            if (applyingLightweightHistory) return;
            lightweightUndo ??= new List<LightweightEditCommand>();
            lightweightRedo ??= new List<LightweightEditCommand>();
            controller?.Painting?.History?.ClearRedo();
            controller?.Plugins?.ClearRedo();
            DisposeCommands(lightweightRedo);
            lightweightRedo.Clear();
            lightweightUndo.Add(new LightweightEditCommand(label, undoAction, redoAction, disposeAction));
            while (lightweightUndo.Count > LightweightHistoryCapacity)
            {
                lightweightUndo[0].Dispose();
                lightweightUndo.RemoveAt(0);
            }
        }

        private bool UndoLightweight()
        {
            if (!CanUndoLightweight) return false;
            LightweightEditCommand command = lightweightUndo[lightweightUndo.Count - 1];
            lightweightUndo.RemoveAt(lightweightUndo.Count - 1);
            applyingLightweightHistory = true;
            try { command.Undo(); }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { applyingLightweightHistory = false; }
            lightweightRedo.Add(command);
            FinishLightweightHistoryChange(command.label);
            return true;
        }

        private bool RedoLightweight()
        {
            if (!CanRedoLightweight) return false;
            LightweightEditCommand command = lightweightRedo[lightweightRedo.Count - 1];
            lightweightRedo.RemoveAt(lightweightRedo.Count - 1);
            applyingLightweightHistory = true;
            try { command.Redo(); }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { applyingLightweightHistory = false; }
            lightweightUndo.Add(command);
            FinishLightweightHistoryChange(command.label);
            return true;
        }

        private void FinishLightweightHistoryChange(string label)
        {
            if (controller?.Textures != null)
            {
                selectedSurface = Mathf.Clamp(selectedSurface, 0, controller.Textures.Sets.Count - 1);
                if (controller.Textures.Sets.Count > 0)
                {
                    suppressLogicalLayerRepair = true;
                    try { SyncActiveLayerSelection(controller.Textures.Sets[selectedSurface]); }
                    finally { suppressLogicalLayerRepair = false; }
                }
            }
            MarkDocumentDirty();
            ShowWorkspaceStatus(label);
            ApplyWorkspaceDisplay();
            RepaintAll();
        }

        private void ClearLightweightHistory()
        {
            pendingLayerCreationLabel = null;
            pendingPathEdit = null;
            strokeCreatedLayers?.Clear();
            if (lightweightUndo != null) { DisposeCommands(lightweightUndo); lightweightUndo.Clear(); }
            if (lightweightRedo != null) { DisposeCommands(lightweightRedo); lightweightRedo.Clear(); }
        }

        private static void DisposeCommands(List<LightweightEditCommand> commands)
        {
            if (commands == null) return;
            for (int i = 0; i < commands.Count; i++) commands[i]?.Dispose();
        }

        private void RegisterCreatedLayer(TexturePaintLayer layer, string label)
        {
            if (layer == null || controller?.Textures == null) return;
            TextureSet set = FindContainingSet(layer);
            if (set == null) return;
            int index = set.layers.IndexOf(layer);
            PushLightweightCommand(label,
                () => DetachLayer(set, layer),
                () => AttachLayer(set, layer, index),
                () => DisposeLayerIfDetached(layer));
        }

        private void RegisterCreatedLayers(List<LayerLocation> layers, string label)
        {
            if (layers == null || layers.Count == 0) return;
            var recorded = new List<LayerLocation>(layers);
            PushLightweightCommand(label,
                () => DetachLayerLocations(recorded),
                () => AttachLayerLocations(recorded),
                () =>
                {
                    for (int i = 0; i < recorded.Count; i++) DisposeLayerIfDetached(recorded[i].layer);
                });
        }

        private bool TryResolveLogicalPeers(TextureSet set, TexturePaintLayer layer,
            out List<TexturePaintLogicalLayerMember> peers, out string error)
        {
            peers = new List<TexturePaintLogicalLayerMember>();
            error = null;
            if (set == null || layer == null) { error = "No texture layer is selected."; return false; }
            if (string.IsNullOrEmpty(layer.logicalLayerId) || string.IsNullOrEmpty(layer.paintTargetId) ||
                controller?.LogicalLayers == null)
            {
                peers.Add(new TexturePaintLogicalLayerMember { textureSet = set, layer = layer });
                return true;
            }
            TexturePaintLogicalTarget target = controller.LogicalTargets?.FindById(layer.paintTargetId);
            TexturePaintLogicalLayerBinding binding = controller.LogicalLayers.Resolve(target, layer.logicalLayerId);
            if (!binding.complete) { error = binding.error; return false; }
            peers.AddRange(binding.members);
            return true;
        }

        private void ChangeLayerVisibility(TextureSet set, TexturePaintLayer layer, bool visible)
        {
            if (set == null || layer == null || layer.visible == visible) return;
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            var previous = new Dictionary<TexturePaintLayer, bool>();
            for (int i = 0; i < peers.Count; i++)
            {
                previous[peers[i].layer] = peers[i].layer.visible;
                peers[i].layer.visible = visible;
                peers[i].textureSet.BindPreviewTextures();
            }
            PushLightweightCommand("Toggle Texture Layer",
                () => { for (int i = 0; i < peers.Count; i++) { peers[i].layer.visible = previous[peers[i].layer]; peers[i].textureSet.BindPreviewTextures(); } },
                () => { for (int i = 0; i < peers.Count; i++) { peers[i].layer.visible = visible; peers[i].textureSet.BindPreviewTextures(); } });
            MarkDocumentDirty();
        }

        private bool MoveLayerWithHistory(TextureSet set, int fromIndex, int toIndex)
        {
            if (set == null || (uint)fromIndex >= (uint)set.layers.Count || (uint)toIndex >= (uint)set.layers.Count)
                return false;
            TexturePaintLayer layer = set.layers[fromIndex];
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return false; }
            var oldIndexes = new Dictionary<TexturePaintLayer, int>();
            for (int i = 0; i < peers.Count; i++) oldIndexes[peers[i].layer] = peers[i].textureSet.layers.IndexOf(peers[i].layer);
            for (int i = 0; i < peers.Count; i++)
                MoveLayerReference(peers[i].textureSet, peers[i].layer,
                    Mathf.Clamp(toIndex, 0, peers[i].textureSet.layers.Count - 1));
            PushLightweightCommand("Reorder Texture Layer",
                () => { for (int i = 0; i < peers.Count; i++) MoveLayerReference(peers[i].textureSet, peers[i].layer, oldIndexes[peers[i].layer]); },
                () => { for (int i = 0; i < peers.Count; i++) MoveLayerReference(peers[i].textureSet, peers[i].layer, Mathf.Clamp(toIndex, 0, peers[i].textureSet.layers.Count - 1)); });
            MarkDocumentDirty();
            return true;
        }

        private void ChangeLayerMetadata(TextureSet set, TexturePaintLayer layer, string name, float opacity,
            TexturePaintBlendMode blendMode)
        {
            if (set == null || layer == null) return;
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            string nextName = string.IsNullOrWhiteSpace(name) ? layer.name : name.Trim();
            var previous = new Dictionary<TexturePaintLayer, LayerMetadataState>();
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLayer peer = peers[i].layer;
                var state = new LayerMetadataState { name = peer.name, opacity = peer.opacity, blendMode = peer.blendMode,
                    channelBlends = new Dictionary<TexturePaintChannel, TexturePaintBlendMode>() };
                foreach (KeyValuePair<TexturePaintChannel, TexturePaintLayerChannelSettings> pair in peer.channelSettings)
                    state.channelBlends[pair.Key] = pair.Value.blendMode;
                previous[peer] = state;
                ApplyLayerMetadata(peers[i].textureSet, peer, nextName, opacity, blendMode, null);
            }
            PushLightweightCommand("Edit Texture Layer",
                () => { for (int i = 0; i < peers.Count; i++) { LayerMetadataState state = previous[peers[i].layer]; ApplyLayerMetadata(peers[i].textureSet, peers[i].layer, state.name, state.opacity, state.blendMode, state.channelBlends); } },
                () => { for (int i = 0; i < peers.Count; i++) ApplyLayerMetadata(peers[i].textureSet, peers[i].layer, nextName, opacity, blendMode, null); });
            MarkDocumentDirty();
        }

        private static void ApplyLayerMetadata(TextureSet set, TexturePaintLayer layer, string name, float opacity,
            TexturePaintBlendMode blendMode,
            Dictionary<TexturePaintChannel, TexturePaintBlendMode> channelBlendOverrides)
        {
            layer.name = name;
            layer.opacity = opacity;
            layer.blendMode = blendMode;
            foreach (KeyValuePair<TexturePaintChannel, TexturePaintLayerChannelSettings> pair in layer.channelSettings)
                pair.Value.blendMode = channelBlendOverrides != null && channelBlendOverrides.TryGetValue(pair.Key, out TexturePaintBlendMode old)
                    ? old : blendMode;
            set.BindPreviewTextures();
        }

        private void ChangeLayerEffects(TextureSet set, TexturePaintLayer layer,
            TexturePaintLayerEffects effects)
        {
            if (set == null || layer == null || effects == null) return;
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers,
                out string error))
            { ShowWorkspaceStatus(error); return; }
            TexturePaintLayerEffects next = effects.Clone();
            next.Normalize();
            var previous = new Dictionary<TexturePaintLayer, TexturePaintLayerEffects>();
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLayer peer = peers[i].layer;
                previous[peer] = peer.effects?.Clone() ?? new TexturePaintLayerEffects();
                ApplyLayerEffects(peers[i].textureSet, peer, next);
            }
            PushLightweightCommand("Edit Layer Effects",
                () =>
                {
                    for (int i = 0; i < peers.Count; i++)
                        ApplyLayerEffects(peers[i].textureSet, peers[i].layer,
                            previous[peers[i].layer]);
                },
                () =>
                {
                    for (int i = 0; i < peers.Count; i++)
                        ApplyLayerEffects(peers[i].textureSet, peers[i].layer, next);
                });
            MarkDocumentDirty();
        }

        private static void ApplyLayerEffects(TextureSet set, TexturePaintLayer layer,
            TexturePaintLayerEffects effects)
        {
            layer.effects = effects?.Clone() ?? new TexturePaintLayerEffects();
            layer.effects.Normalize();
            set.BindPreviewTextures();
        }

        private void ChangeLayerChannel(TextureSet set, TexturePaintLayer layer, TexturePaintChannel channel,
            bool enabled, bool locked, float contribution, float opacity, TexturePaintBlendMode blendMode)
        {
            if (set == null || layer == null) return;
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            var before = new Dictionary<TexturePaintLayer, TexturePaintLayerChannelSettings>();
            TexturePaintLayerChannelSettings after = new TexturePaintLayerChannelSettings
            {
                channel = channel,
                enabled = enabled,
                locked = locked,
                contribution = contribution,
                opacity = opacity,
                blendMode = blendMode
            };
            for (int i = 0; i < peers.Count; i++)
                if (peers[i].textureSet.GetChannel(channel) == null)
                { ShowWorkspaceStatus($"Target member '{peers[i].targetMember?.slotName}' does not support {channel}."); return; }
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLayerChannelSettings settings = peers[i].layer.GetChannelSettings(channel);
                before[peers[i].layer] = settings.Clone();
                ApplyChannelSettings(peers[i].textureSet, peers[i].layer, channel, after);
            }
            PushLightweightCommand("Edit Layer Channel",
                () => { for (int i = 0; i < peers.Count; i++) if (before.TryGetValue(peers[i].layer, out TexturePaintLayerChannelSettings value)) ApplyChannelSettings(peers[i].textureSet, peers[i].layer, channel, value); },
                () => { for (int i = 0; i < peers.Count; i++) if (before.ContainsKey(peers[i].layer)) ApplyChannelSettings(peers[i].textureSet, peers[i].layer, channel, after); });
            MarkDocumentDirty();
        }

        private static void ApplyChannelSettings(TextureSet set, TexturePaintLayer layer, TexturePaintChannel channel,
            TexturePaintLayerChannelSettings value)
        {
            layer.channelSettings[channel] = value.Clone();
            set.BindPreviewTextures();
        }

        private void ChangeFillLayer(TextureSet set, TexturePaintLayer layer, TexturePaintChannel channel,
            TexturePaintFillSettings settings)
        {
            if (set == null || layer == null || layer.kind != TexturePaintLayerKind.Fill || settings == null) return;
            if (settings.source == TexturePaintBrushSource.Texture && settings.sourceTexture == null)
            {
                RestoreFillSourceControls(layer);
                ShowWorkspaceStatus("Select a source texture before changing this Fill layer.");
                return;
            }
            if (settings.source == TexturePaintBrushSource.Overlay && settings.sourceOverlay == null)
            {
                RestoreFillSourceControls(layer);
                ShowWorkspaceStatus("Select an OverlayData source before changing this Fill layer.");
                return;
            }
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            for (int i = 0; i < peers.Count; i++)
                if (peers[i].textureSet.GetChannel(channel) == null)
                { ShowWorkspaceStatus($"Target member '{peers[i].targetMember?.slotName}' does not support {channel}."); return; }
            TexturePaintLogicalTarget target = !string.IsNullOrEmpty(layer.paintTargetId)
                ? controller.LogicalTargets?.FindById(layer.paintTargetId)
                : controller.LogicalLayers?.FindTarget(set);
            var resolvedSettings = new List<TexturePaintFillSettings>(peers.Count);
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintFillSettings peerSettings = settings.Clone();
                if (peerSettings.source == TexturePaintBrushSource.Overlay)
                {
                    peerSettings.sourceOverlay = TexturePaintLogicalLayerController.ResolveMemberOverlay(target,
                        set, settings.sourceOverlay, peers[i].textureSet);
                    if (peerSettings.sourceOverlay == null)
                    {
                        ShowWorkspaceStatus($"No matching overlay source exists for target member '{peers[i].targetMember?.slotName}'.");
                        return;
                    }
                }
                resolvedSettings.Add(peerSettings);
            }
            var before = new List<LayerLocation>();
            var after = new List<LayerLocation>();
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLogicalLayerMember peer = peers[i];
                int index = peer.textureSet.layers.IndexOf(peer.layer);
                TexturePaintLayer snapshot = peer.textureSet.CloneLayer(peer.layer, peer.layer.name, true);
                if (snapshot == null || !peer.textureSet.UpdateFillLayer(peer.layer, channel, resolvedSettings[i]))
                {
                    snapshot?.Dispose();
                    for (int rollback = 0; rollback < before.Count; rollback++)
                        SwapLayerSnapshot(before[rollback].set, after[rollback].layer, before[rollback].layer, before[rollback].index);
                    RestoreFillSourceControls(layer);
                    ShowWorkspaceStatus("The Fill source could not be generated for every target member.");
                    return;
                }
                before.Add(new LayerLocation { set = peer.textureSet, layer = snapshot, index = index });
                after.Add(new LayerLocation { set = peer.textureSet, layer = peer.layer, index = index });
            }
            PushLightweightCommand("Edit Fill Layer",
                () => { for (int i = 0; i < before.Count; i++) SwapLayerSnapshot(before[i].set, after[i].layer, before[i].layer, before[i].index); },
                () => { for (int i = 0; i < before.Count; i++) SwapLayerSnapshot(before[i].set, before[i].layer, after[i].layer, after[i].index); },
                () => { for (int i = 0; i < before.Count; i++) { DisposeLayerIfDetached(before[i].layer); DisposeLayerIfDetached(after[i].layer); } });
            MarkDocumentDirty();
        }

        private void RestoreFillSourceControls(TexturePaintLayer layer)
        {
            layer?.NormalizeKindPayload();
            if (layer?.fillSettings == null) return;
            paintSource = layer.fillSettings.source;
            paintSourceTexture = layer.fillSettings.sourceTexture;
            paintSourceOverlay = layer.fillSettings.sourceOverlay;
            paintColor = layer.fillSettings.color;
        }

        private void DuplicateLayerWithHistory(TextureSet set, int index)
        {
            if (set == null || (uint)index >= (uint)set.layers.Count) return;
            TexturePaintLayer source = set.layers[index];
            if (!TryResolveLogicalPeers(set, source, out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            string logicalId = Guid.NewGuid().ToString("N");
            string targetId = source.paintTargetId;
            var created = new List<LayerLocation>();
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLogicalLayerMember peer = peers[i];
                int peerIndex = peer.textureSet.layers.IndexOf(peer.layer);
                TexturePaintLayer copy = peer.textureSet.DuplicateLayerAt(peerIndex);
                if (copy == null)
                {
                    DetachLayerLocations(created);
                    for (int dispose = 0; dispose < created.Count; dispose++) created[dispose].layer.Dispose();
                    return;
                }
                copy.logicalLayerId = peers.Count > 1 || !string.IsNullOrEmpty(targetId) ? logicalId : null;
                copy.paintTargetId = targetId;
                created.Add(new LayerLocation { set = peer.textureSet, layer = copy,
                    index = peer.textureSet.layers.IndexOf(copy) });
            }
            RegisterCreatedLayers(created, "Duplicate Texture Layer");
            MarkDocumentDirty();
        }

        private bool MergeLayerWithHistory(TextureSet set, int upperIndex)
        {
            if (set == null || upperIndex <= 0 || upperIndex >= set.layers.Count) return false;
            TexturePaintLayer lower = set.layers[upperIndex - 1];
            TexturePaintLayer upper = set.layers[upperIndex];
            if (!TryResolveLogicalPeers(set, upper, out List<TexturePaintLogicalLayerMember> upperPeers, out string error) ||
                !TryResolveLogicalPeers(set, lower, out List<TexturePaintLogicalLayerMember> lowerPeers, out error))
            { ShowWorkspaceStatus(error); return false; }
            var lowerBySet = new Dictionary<TextureSet, TexturePaintLayer>();
            for (int i = 0; i < lowerPeers.Count; i++) lowerBySet[lowerPeers[i].textureSet] = lowerPeers[i].layer;
            var states = new List<MergedLayerState>();
            for (int i = 0; i < upperPeers.Count; i++)
            {
                TexturePaintLogicalLayerMember upperPeer = upperPeers[i];
                if (!lowerBySet.TryGetValue(upperPeer.textureSet, out TexturePaintLayer lowerPeer))
                { ShowWorkspaceStatus("Merge requires matching logical layers on every target member."); return false; }
                int peerUpperIndex = upperPeer.textureSet.layers.IndexOf(upperPeer.layer);
                int peerLowerIndex = upperPeer.textureSet.layers.IndexOf(lowerPeer);
                if (peerUpperIndex != peerLowerIndex + 1)
                { ShowWorkspaceStatus("Merge requires adjacent logical layers on every target member."); return false; }
                TexturePaintLayer merged = upperPeer.textureSet.CreateMergedLayer(peerUpperIndex);
                if (merged == null)
                {
                    for (int dispose = 0; dispose < states.Count; dispose++) states[dispose].merged.Dispose();
                    return false;
                }
                merged.logicalLayerId = lowerPeer.logicalLayerId;
                merged.paintTargetId = lowerPeer.paintTargetId;
                states.Add(new MergedLayerState { set = upperPeer.textureSet, lower = lowerPeer,
                    upper = upperPeer.layer, merged = merged, index = peerLowerIndex });
            }
            for (int i = 0; i < states.Count; i++)
                ReplaceSourcesWithMerged(states[i].set, states[i].lower, states[i].upper, states[i].merged, states[i].index);
            PushLightweightCommand("Merge Texture Layers",
                () => { for (int i = 0; i < states.Count; i++) ReplaceMergedWithSources(states[i].set, states[i].merged, states[i].lower, states[i].upper, states[i].index); },
                () => { for (int i = 0; i < states.Count; i++) ReplaceSourcesWithMerged(states[i].set, states[i].lower, states[i].upper, states[i].merged, states[i].index); },
                () =>
                {
                    for (int i = 0; i < states.Count; i++)
                    { DisposeLayerIfDetached(states[i].lower); DisposeLayerIfDetached(states[i].upper); DisposeLayerIfDetached(states[i].merged); }
                });
            MarkDocumentDirty();
            return true;
        }

        private void DeleteLayerWithHistory(TextureSet primarySet, int index)
        {
            if (primarySet == null || (uint)index >= (uint)primarySet.layers.Count) return;
            TexturePaintLayer primary = primarySet.layers[index];
            string groupKey = primary.proceduralGroupKey;
            string logicalLayerId = primary.logicalLayerId;
            string paintTargetId = primary.paintTargetId;
            if (string.IsNullOrEmpty(groupKey) && primary.IsSplineLayer) groupKey = "texture-paint-spline:" + primary.id;
            List<LayerLocation> removed = new List<LayerLocation>();
            if (controller.Painting.IsPainting) { controller.Painting.EndStroke(false); strokeActive = false; }
            DiscardPaintStrokeHistory();
            for (int setIndex = 0; setIndex < controller.Textures.Sets.Count; setIndex++)
            {
                TextureSet set = controller.Textures.Sets[setIndex];
                for (int layerIndex = set.layers.Count - 1; layerIndex >= 0; layerIndex--)
                {
                    TexturePaintLayer candidate = set.layers[layerIndex];
                    bool logicalMatch = !string.IsNullOrEmpty(logicalLayerId) &&
                        string.Equals(candidate.logicalLayerId, logicalLayerId, StringComparison.Ordinal) &&
                        string.Equals(candidate.paintTargetId, paintTargetId, StringComparison.Ordinal);
                    bool remove = ReferenceEquals(candidate, primary) || logicalMatch || (!string.IsNullOrEmpty(groupKey) &&
                        string.Equals(candidate.proceduralGroupKey, groupKey, StringComparison.Ordinal));
                    if (!remove) continue;
                    removed.Add(new LayerLocation { set = set, layer = candidate, index = layerIndex });
                    DetachLayer(set, candidate);
                }
            }
            PushLightweightCommand("Delete Texture Layer",
                () => AttachLayerLocations(removed),
                () => DetachLayerLocations(removed),
                () => { for (int i = 0; i < removed.Count; i++) DisposeLayerIfDetached(removed[i].layer); });
            if (primary.spline != null) splineDisplayCache?.Remove(primary.spline);
            MarkDocumentDirty();
        }

        private void DiscardPaintStrokeHistory()
        {
            controller?.Painting?.History?.Clear();
            RemoveCommandsWithLabel(lightweightUndo, "Paint Stroke");
            RemoveCommandsWithLabel(lightweightRedo, "Paint Stroke");
        }

        private static void RemoveCommandsWithLabel(List<LightweightEditCommand> commands, string label)
        {
            if (commands == null) return;
            for (int i = commands.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(commands[i].label, label, StringComparison.Ordinal)) continue;
                commands[i].Dispose();
                commands.RemoveAt(i);
            }
        }

        private void RenameLayerWithHistory(TextureSet set, TexturePaintLayer layer, string newName)
        {
            if (set == null || layer == null || string.IsNullOrWhiteSpace(newName)) return;
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            var previous = new Dictionary<TexturePaintLayer, string>();
            string next = newName.Trim();
            if (layer.name == next) return;
            for (int i = 0; i < peers.Count; i++) { previous[peers[i].layer] = peers[i].layer.name; peers[i].layer.name = next; }
            PushLightweightCommand("Rename Texture Layer",
                () => { for (int i = 0; i < peers.Count; i++) peers[i].layer.name = previous[peers[i].layer]; },
                () => { for (int i = 0; i < peers.Count; i++) peers[i].layer.name = next; });
            MarkDocumentDirty();
        }

        internal void RecordMaskChange(TextureSet set, TexturePaintLayer layer,
            IReadOnlyList<TexturePaintMask> before, IReadOnlyList<TexturePaintMask> after)
        {
            List<TexturePaintLogicalLayerMember> peers;
            if (set != null && layer != null)
            {
                if (!TryResolveLogicalPeers(set, layer, out peers, out string error))
                { ShowWorkspaceStatus(error); return; }
            }
            else peers = new List<TexturePaintLogicalLayerMember>
                { new TexturePaintLogicalLayerMember { textureSet = set, layer = layer } };
            var beforeByLayer = new Dictionary<TexturePaintLayer, List<TexturePaintMask>>();
            for (int i = 0; i < peers.Count; i++)
                if (peers[i].layer != null)
                    beforeByLayer[peers[i].layer] = ReferenceEquals(peers[i].layer, layer)
                        ? CloneMasksForHistory(before) : CloneMasksForHistory(peers[i].layer.masks);
            List<TexturePaintMask> afterCopy = CloneMasksForHistory(after);
            for (int i = 0; i < peers.Count; i++) ApplyMaskSnapshot(peers[i].textureSet, peers[i].layer, afterCopy);
            PushLightweightCommand("Edit Overlay Painter Masks",
                () => { for (int i = 0; i < peers.Count; i++) ApplyMaskSnapshot(peers[i].textureSet, peers[i].layer,
                    peers[i].layer != null ? beforeByLayer[peers[i].layer] : before); },
                () => { for (int i = 0; i < peers.Count; i++) ApplyMaskSnapshot(peers[i].textureSet, peers[i].layer, afterCopy); });
            MarkDocumentDirty();
        }

        internal void RecordBrushLibraryChange(BrushLibrary library, BrushPreset preset, int index, bool added)
        {
            if (library == null || preset == null) return;
            Action add = () =>
            {
                library.Insert(index, preset);
                UnityEditor.EditorUtility.SetDirty(library);
            };
            Action remove = () =>
            {
                library.Remove(preset);
                UnityEditor.EditorUtility.SetDirty(library);
            };
            PushLightweightCommand(added ? "Add Overlay Painter Brush" : "Remove Overlay Painter Brush",
                added ? remove : add, added ? add : remove);
        }

        private void ApplyMaskSnapshot(TextureSet set, TexturePaintLayer layer,
            IReadOnlyList<TexturePaintMask> masks)
        {
            if (layer != null)
            {
                layer.masks.Clear();
                layer.masks.AddRange(CloneMasksForHistory(masks));
                set?.BindPreviewTextures();
            }
            else controller?.Masks?.ReplaceWith(CloneMasksForHistory(masks));
            if (TryGetActivePathLayer(ActiveTextureSet, out _))
            {
                QueueSplineReapply(ActiveTextureSet);
                ScheduleSplineReapply();
            }
        }

        internal static List<TexturePaintMask> CloneMasksForHistory(IReadOnlyList<TexturePaintMask> masks)
        {
            List<TexturePaintMask> result = new List<TexturePaintMask>();
            if (masks == null) return result;
            for (int i = 0; i < masks.Count; i++)
            {
                TexturePaintMask source = masks[i];
                if (source == null) continue;
                result.Add(new TexturePaintMask
                {
                    id = source.id,
                    ownerLayerId = source.ownerLayerId,
                    ownerSurfaceId = source.ownerSurfaceId,
                    name = source.name,
                    enabled = source.enabled,
                    kind = source.kind,
                    operation = source.operation,
                    grayscaleTexture = source.grayscaleTexture,
                    surfaceIndex = source.surfaceIndex,
                    triangleIndices = new List<int>(source.triangleIndices),
                    uvIslandIndices = new List<int>(source.uvIslandIndices),
                    proceduralPluginId = source.proceduralPluginId,
                    invert = source.invert,
                    threshold = source.threshold,
                    inputMin = source.inputMin,
                    inputMax = source.inputMax,
                    gamma = source.gamma,
                    feather = source.feather,
                    blurRadius = source.blurRadius,
                    idValue = source.idValue,
                    contentRevision = source.contentRevision
                });
            }
            return result;
        }

        private TextureSet FindContainingSet(TexturePaintLayer layer)
        {
            if (layer == null || controller?.Textures == null) return null;
            for (int i = 0; i < controller.Textures.Sets.Count; i++)
                if (controller.Textures.Sets[i].layers.Contains(layer)) return controller.Textures.Sets[i];
            return null;
        }

        private bool IsLayerAttached(TexturePaintLayer layer) => FindContainingSet(layer) != null;

        private void DisposeLayerIfDetached(TexturePaintLayer layer)
        {
            if (layer != null && !IsLayerAttached(layer)) layer.Dispose();
        }

        private static void DetachLayer(TextureSet set, TexturePaintLayer layer)
        {
            if (set == null || layer == null) return;
            int index = set.layers.IndexOf(layer);
            if (index < 0) return;
            set.layers.RemoveAt(index);
            if (set.layers.Count == 0) set.activeLayerIndex = -1;
            else set.activeLayerIndex = Mathf.Clamp(index - 1, 0, set.layers.Count - 1);
            set.BindPreviewTextures();
        }

        private static void AttachLayer(TextureSet set, TexturePaintLayer layer, int index)
        {
            if (set == null || layer == null || set.layers.Contains(layer)) return;
            int insert = Mathf.Clamp(index, 0, set.layers.Count);
            set.layers.Insert(insert, layer);
            set.activeLayerIndex = insert;
            set.BindPreviewTextures();
        }

        private static void MoveLayerReference(TextureSet set, TexturePaintLayer layer, int targetIndex)
        {
            int current = set?.layers.IndexOf(layer) ?? -1;
            if (current < 0) return;
            set.MoveLayer(current, Mathf.Clamp(targetIndex, 0, set.layers.Count - 1));
        }

        private static void SwapLayerSnapshot(TextureSet set, TexturePaintLayer expected,
            TexturePaintLayer replacement, int indexHint)
        {
            if (set == null || replacement == null) return;
            int index = set.layers.IndexOf(expected);
            if (index < 0) index = Mathf.Clamp(indexHint, 0, set.layers.Count);
            else set.layers.RemoveAt(index);
            if (!set.layers.Contains(replacement)) set.layers.Insert(Mathf.Clamp(index, 0, set.layers.Count), replacement);
            set.activeLayerIndex = set.layers.IndexOf(replacement);
            set.BindPreviewTextures();
        }

        private static void ReplaceMergedWithSources(TextureSet set, TexturePaintLayer merged,
            TexturePaintLayer lower, TexturePaintLayer upper, int index)
        {
            set.layers.Remove(merged);
            int insert = Mathf.Clamp(index, 0, set.layers.Count);
            if (!set.layers.Contains(lower)) set.layers.Insert(insert, lower);
            if (!set.layers.Contains(upper)) set.layers.Insert(Mathf.Min(insert + 1, set.layers.Count), upper);
            set.activeLayerIndex = set.layers.IndexOf(upper);
            set.BindPreviewTextures();
        }

        private static void ReplaceSourcesWithMerged(TextureSet set, TexturePaintLayer lower,
            TexturePaintLayer upper, TexturePaintLayer merged, int index)
        {
            set.layers.Remove(upper);
            set.layers.Remove(lower);
            if (!set.layers.Contains(merged)) set.layers.Insert(Mathf.Clamp(index, 0, set.layers.Count), merged);
            set.activeLayerIndex = set.layers.IndexOf(merged);
            set.BindPreviewTextures();
        }

        private static void AttachLayerLocations(List<LayerLocation> locations)
        {
            locations.Sort((a, b) => a.index.CompareTo(b.index));
            for (int i = 0; i < locations.Count; i++)
                AttachLayer(locations[i].set, locations[i].layer, locations[i].index);
        }

        private static void DetachLayerLocations(List<LayerLocation> locations)
        {
            for (int i = locations.Count - 1; i >= 0; i--)
                DetachLayer(locations[i].set, locations[i].layer);
        }

        private void BeginCustomPathEdit(TextureSet set, string label, TexturePaintSplineSettings settingsOverride = null)
        {
            if (pendingPathEdit != null || !TryGetActivePathLayer(set, out TexturePaintLayer layer)) return;
            pendingPathEdit = new PendingPathEdit
            {
                label = label,
                set = set,
                layer = layer,
                before = CapturePathState(layer, settingsOverride),
                after = null
            };
            pathEditRecordedThisGUI = true;
        }

        private void CompleteCustomPathEdit(TextureSet set, bool deferUntilMouseUp)
        {
            if (pendingPathEdit == null || !ReferenceEquals(pendingPathEdit.set, set) ||
                !TryGetActivePathLayer(set, out TexturePaintLayer layer)) return;
            pendingPathEdit.after = CapturePathState(layer, null);
            pendingPathEdit.deferred |= deferUntilMouseUp;
            if (!deferUntilMouseUp) CommitPendingPathEdit();
        }

        private void CommitPendingPathEdit()
        {
            PendingPathEdit pending = pendingPathEdit;
            pendingPathEdit = null;
            if (pending?.before == null || pending.after == null || pending.layer == null) return;
            PushLightweightCommand(pending.label,
                () => ApplyPathState(pending.set, pending.layer, pending.before),
                () => ApplyPathState(pending.set, pending.layer, pending.after));
        }

        private PathEditState CapturePathState(TexturePaintLayer layer, TexturePaintSplineSettings settingsOverride)
        {
            return new PathEditState
            {
                spline = CloneSpline(layer?.spline),
                settings = (settingsOverride ?? layer?.splineSettings ?? CreateSplineSettings())?.Clone(),
                selectedPoint = selectedSplinePoint
            };
        }

        private void ApplyPathState(TextureSet set, TexturePaintLayer layer, PathEditState state)
        {
            if (set == null || layer == null || !layer.IsSplineLayer || state == null || !set.layers.Contains(layer)) return;
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers, out _))
                peers = new List<TexturePaintLogicalLayerMember>
                    { new TexturePaintLogicalLayerMember { textureSet = set, layer = layer } };
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLayer peer = peers[i].layer;
                if (!peer.IsSplineLayer) continue;
                TexturePaintSpline previous = peer.spline;
                peer.spline = CloneSpline(state.spline);
                peer.splineSettings = state.settings?.Clone() ?? new TexturePaintSplineSettings();
                if (previous != null) splineDisplayCache?.Remove(previous);
                peers[i].textureSet.activeLayerIndex = peers[i].textureSet.layers.IndexOf(peer);
            }
            spline = layer.spline;
            splineMode = true;
            selectedSplinePoint = Mathf.Clamp(state.selectedPoint, -1, spline?.PointCount - 1 ?? -1);
            selectedSplinePoints?.Clear();
            RestoreSplineSettings(layer.splineSettings);
            QueueSplineReapply(set);
            ReapplyPendingSpline();
        }

        private static TexturePaintSpline CloneSpline(TexturePaintSpline source)
        {
            return source == null ? null : JsonUtility.FromJson<TexturePaintSpline>(JsonUtility.ToJson(source));
        }
    }
}
