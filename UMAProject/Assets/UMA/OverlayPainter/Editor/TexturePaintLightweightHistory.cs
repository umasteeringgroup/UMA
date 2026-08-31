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
            private Action redo;
            private readonly Action dispose;
            public readonly string coalesceKey;
            public double lastEditTime;

            public LightweightEditCommand(string label, Action undo, Action redo, Action dispose,
                string coalesceKey, double editTime)
            {
                this.label = label;
                this.undo = undo;
                this.redo = redo;
                this.dispose = dispose;
                this.coalesceKey = coalesceKey;
                lastEditTime = editTime;
            }

            public void Undo() => undo?.Invoke();
            public void Redo() => redo?.Invoke();
            public void ReplaceRedo(Action replacement, double editTime)
            { redo = replacement; lastEditTime = editTime; }
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

        private sealed class PluginLayerState
        {
            public string pluginId;
            public string pluginVersion;
            public TexturePaintPluginParameterSet parameters;
            public string parametersJson;
            public bool stale;
            public string lastError;
        }

        private sealed class MergedLayerState
        {
            public TextureSet set;
            public TexturePaintLayer lower;
            public TexturePaintLayer upper;
            public TexturePaintLayer merged;
            public int index;
        }

        private sealed class LayerGroupingState
        {
            public TextureSet set;
            public TexturePaintLayer layer;
            public TexturePaintLayer group;
            public string previousParentId;
            public int previousIndex;
        }

        private sealed class LayerChannelLocation
        {
            public TextureSet set;
            public TexturePaintLayer layer;
            public TexturePaintChannel channel;
            public EditableTextureTarget target;
            public TexturePaintLayerChannelSettings settings;
            public TexturePaintLayerChannelSettings previousSettings;
            public TexturePaintLayerEffects effectsBefore;
            public TexturePaintLayerEffects effectsAfter;
        }

        private sealed class LayerChannelSourceState
        {
            public TextureSet set;
            public TexturePaintLayer layer;
            public TexturePaintChannel channel;
            public TexturePaintChannelSourceSettings before;
            public TexturePaintChannelSourceSettings after;
        }

        private sealed class LayerMaskClipboardEntry
        {
            public int width;
            public int height;
            public Color32[] pixels;
            public float baseValue;
            public TexturePaintLayerMaskEffects effects;
            public TexturePaintChannelSourceSettings sourceSettings;
            public TexturePaintChannel sourceChannel;
            public string pluginId;
            public string pluginVersion;
            public string pluginParametersJson;
            public TexturePaintPluginParameterSet pluginParameters;
            public bool pluginStale;
            public string pluginLastError;
        }

        private sealed class LayerMaskClipboardData
        {
            public string sourceLayerName;
            public LayerMaskClipboardEntry fallback;
            public readonly Dictionary<string, LayerMaskClipboardEntry> entries =
                new Dictionary<string, LayerMaskClipboardEntry>(StringComparer.Ordinal);
        }

        private static LayerMaskClipboardData layerMaskClipboard;
        private static bool HasLayerMaskClipboard => layerMaskClipboard?.fallback != null;

        private bool CanUndoLightweight => lightweightUndo != null && lightweightUndo.Count > 0;
        private bool CanRedoLightweight => lightweightRedo != null && lightweightRedo.Count > 0;
        private string LightweightUndoLabel => CanUndoLightweight ? lightweightUndo[lightweightUndo.Count - 1].label : null;
        private string LightweightRedoLabel => CanRedoLightweight ? lightweightRedo[lightweightRedo.Count - 1].label : null;
        internal bool CanUndoPluginTransaction => string.Equals(LightweightUndoLabel, "Plugin Transaction",
            StringComparison.Ordinal);
        internal bool CanRedoPluginTransaction => string.Equals(LightweightRedoLabel, "Plugin Transaction",
            StringComparison.Ordinal);

        private void PushLightweightCommand(string label, Action undoAction, Action redoAction,
            Action disposeAction = null, string coalesceKey = null)
        {
            if (applyingLightweightHistory) return;
            lightweightUndo ??= new List<LightweightEditCommand>();
            lightweightRedo ??= new List<LightweightEditCommand>();
            controller?.Painting?.History?.ClearRedo();
            controller?.Plugins?.ClearRedo();
            DisposeCommands(lightweightRedo);
            lightweightRedo.Clear();
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (disposeAction == null && !string.IsNullOrEmpty(coalesceKey) &&
                lightweightUndo.Count > 0)
            {
                LightweightEditCommand previous = lightweightUndo[lightweightUndo.Count - 1];
                if (string.Equals(previous.coalesceKey, coalesceKey, StringComparison.Ordinal) &&
                    now - previous.lastEditTime <= 0.5d)
                {
                    previous.ReplaceRedo(redoAction, now);
                    return;
                }
            }
            lightweightUndo.Add(new LightweightEditCommand(label, undoAction, redoAction,
                disposeAction, coalesceKey, now));
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
                () => DisposeLayerIfDetached(set, layer));
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
                    for (int i = 0; i < recorded.Count; i++)
                        DisposeLayerIfDetached(recorded[i].set, recorded[i].layer);
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
            MarkDocumentDirty(peers);
        }

        private bool MoveLayerWithHistory(TextureSet set, int fromIndex, int toIndex)
        {
            if (set == null || (uint)fromIndex >= (uint)set.layers.Count || (uint)toIndex >= (uint)set.layers.Count)
                return false;
            TexturePaintLayer layer = set.layers[fromIndex];
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return false; }
            var oldIndexes = new Dictionary<TexturePaintLayer, int>();
            var newIndexes = new Dictionary<TexturePaintLayer, int>();
            for (int i = 0; i < peers.Count; i++) oldIndexes[peers[i].layer] = peers[i].textureSet.layers.IndexOf(peers[i].layer);
            for (int i = 0; i < peers.Count; i++)
            {
                int targetIndex = ConstrainLayerMoveTarget(peers[i].textureSet, peers[i].layer, toIndex);
                MoveLayerReference(peers[i].textureSet, peers[i].layer,
                    targetIndex);
                if (peers[i].layer.kind == TexturePaintLayerKind.Plugin)
                {
                    peers[i].layer.pluginStale = true;
                    peers[i].layer.pluginLastError = null;
                }
                newIndexes[peers[i].layer] = peers[i].textureSet.layers.IndexOf(peers[i].layer);
            }
            PushLightweightCommand("Reorder Texture Layer",
                () => { for (int i = 0; i < peers.Count; i++) MoveLayerReference(peers[i].textureSet, peers[i].layer, oldIndexes[peers[i].layer]); },
                () => { for (int i = 0; i < peers.Count; i++) MoveLayerReference(peers[i].textureSet, peers[i].layer, newIndexes[peers[i].layer]); });
            MarkDocumentDirtyAfterStructuralChange();
            return true;
        }

        private static int ConstrainLayerMoveTarget(TextureSet set, TexturePaintLayer layer,
            int requestedIndex)
        {
            if (set == null || layer == null) return requestedIndex;
            requestedIndex = Mathf.Clamp(requestedIndex, 0, set.layers.Count - 1);
            if (!string.IsNullOrEmpty(layer.parentId))
            {
                TexturePaintLayer parent = FindLayerById(set, layer.parentId);
                if (parent?.kind != TexturePaintLayerKind.Group) return requestedIndex;
                int firstChild = set.layers.Count;
                int groupIndex = set.layers.IndexOf(parent);
                for (int i = 0; i < set.layers.Count; i++)
                    if (IsDescendantOfGroup(set, set.layers[i], new HashSet<string> { parent.id }))
                        firstChild = Mathf.Min(firstChild, i);
                return firstChild < groupIndex
                    ? Mathf.Clamp(requestedIndex, firstChild, groupIndex - 1)
                    : requestedIndex;
            }

            // An ungrouped layer may not be inserted among another group's descendants. Put it
            // immediately above that folder instead, which keeps every child block contiguous.
            for (int i = 0; i < set.layers.Count; i++)
            {
                TexturePaintLayer group = set.layers[i];
                if (group?.kind != TexturePaintLayerKind.Group) continue;
                if (!IsDescendantOfGroup(set, set.layers[requestedIndex],
                        new HashSet<string> { group.id })) continue;
                return Mathf.Min(i + 1, set.layers.Count - 1);
            }
            return requestedIndex;
        }

        private bool MoveLayerIntoGroupWithHistory(TextureSet set, TexturePaintLayer layer,
            TexturePaintLayer group)
        {
            if (set == null || layer == null || group == null || ReferenceEquals(layer, group) ||
                group.kind != TexturePaintLayerKind.Group) return false;
            if (layer.kind == TexturePaintLayerKind.Group &&
                IsDescendantOfGroup(set, group, new HashSet<string> { layer.id }))
            { ShowWorkspaceStatus("A group cannot be moved into itself or one of its descendants."); return false; }
            if (!TryResolveLogicalPeers(set, layer,
                    out List<TexturePaintLogicalLayerMember> layerPeers, out string layerError))
            {
                ShowWorkspaceStatus(layerError);
                return false;
            }
            if (!TryResolveLogicalPeers(set, group,
                    out List<TexturePaintLogicalLayerMember> groupPeers, out string groupError))
            {
                ShowWorkspaceStatus(groupError);
                return false;
            }

            var groupsBySet = new Dictionary<TextureSet, TexturePaintLayer>();
            for (int i = 0; i < groupPeers.Count; i++)
                groupsBySet[groupPeers[i].textureSet] = groupPeers[i].layer;
            var states = new List<LayerGroupingState>(layerPeers.Count);
            bool changed = false;
            for (int i = 0; i < layerPeers.Count; i++)
            {
                TexturePaintLogicalLayerMember peer = layerPeers[i];
                if (!groupsBySet.TryGetValue(peer.textureSet, out TexturePaintLayer peerGroup))
                {
                    ShowWorkspaceStatus("The target folder is not available on every logical target member.");
                    return false;
                }
                int layerIndex = peer.textureSet.layers.IndexOf(peer.layer);
                int groupIndex = peer.textureSet.layers.IndexOf(peerGroup);
                if (layerIndex < 0 || groupIndex < 0) return false;
                states.Add(new LayerGroupingState
                {
                    set = peer.textureSet,
                    layer = peer.layer,
                    group = peerGroup,
                    previousParentId = peer.layer.parentId,
                    previousIndex = layerIndex
                });
                changed |= !string.Equals(peer.layer.parentId, peerGroup.id, StringComparison.Ordinal) ||
                    layerIndex != groupIndex - 1;
            }
            if (!changed) return false;

            for (int i = 0; i < states.Count; i++) PlaceLayerInGroup(states[i]);
            for (int i = 0; i < states.Count; i++)
                if (states[i].layer.kind == TexturePaintLayerKind.Plugin)
                { states[i].layer.pluginStale = true; states[i].layer.pluginLastError = null; }
            PushLightweightCommand("Move Layer Into Group",
                () =>
                {
                    for (int i = 0; i < states.Count; i++) RestoreLayerGrouping(states[i]);
                },
                () =>
                {
                    for (int i = 0; i < states.Count; i++) PlaceLayerInGroup(states[i]);
                });
            MarkDocumentDirtyAfterStructuralChange();
            return true;
        }

        private bool RemoveLayerFromGroupWithHistory(TextureSet set, TexturePaintLayer layer)
        {
            if (set == null || layer == null || string.IsNullOrEmpty(layer.parentId)) return false;
            if (!TryResolveLogicalPeers(set, layer,
                    out List<TexturePaintLogicalLayerMember> peers, out string error))
            {
                ShowWorkspaceStatus(error);
                return false;
            }
            var states = new List<LayerGroupingState>(peers.Count);
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLogicalLayerMember peer = peers[i];
                TexturePaintLayer group = FindLayerById(peer.textureSet, peer.layer.parentId);
                int previousIndex = peer.textureSet.layers.IndexOf(peer.layer);
                if (group == null || group.kind != TexturePaintLayerKind.Group || previousIndex < 0)
                {
                    ShowWorkspaceStatus("The layer's parent group is no longer available.");
                    return false;
                }
                states.Add(new LayerGroupingState
                {
                    set = peer.textureSet,
                    layer = peer.layer,
                    group = group,
                    previousParentId = peer.layer.parentId,
                    previousIndex = previousIndex
                });
            }
            for (int i = 0; i < states.Count; i++) PlaceLayerAboveGroup(states[i]);
            for (int i = 0; i < states.Count; i++)
                if (states[i].layer.kind == TexturePaintLayerKind.Plugin)
                { states[i].layer.pluginStale = true; states[i].layer.pluginLastError = null; }
            PushLightweightCommand("Remove Layer From Group",
                () =>
                {
                    for (int i = 0; i < states.Count; i++) RestoreLayerGrouping(states[i]);
                },
                () =>
                {
                    for (int i = 0; i < states.Count; i++) PlaceLayerAboveGroup(states[i]);
                });
            MarkDocumentDirtyAfterStructuralChange();
            return true;
        }

        private static void PlaceLayerInGroup(LayerGroupingState state)
        {
            if (state?.set == null || state.layer == null || state.group == null) return;
            if (state.set.layers.IndexOf(state.layer) < 0 || state.set.layers.IndexOf(state.group) < 0)
                return;
            state.layer.parentId = state.group.id;
            state.set.NormalizeLayerHierarchy();
            state.set.activeLayerIndex = state.set.layers.IndexOf(state.layer);
            state.set.BindPreviewTextures();
        }

        private static void RestoreLayerGrouping(LayerGroupingState state)
        {
            if (state?.set == null || state.layer == null) return;
            int currentIndex = state.set.layers.IndexOf(state.layer);
            if (currentIndex < 0) return;
            state.layer.parentId = state.previousParentId;
            state.set.NormalizeLayerHierarchy();
            currentIndex = state.set.layers.IndexOf(state.layer);
            if (currentIndex >= 0 && state.previousIndex >= 0 &&
                state.previousIndex < state.set.layers.Count && currentIndex != state.previousIndex)
                state.set.MoveLayer(currentIndex, state.previousIndex);
            state.set.activeLayerIndex = state.set.layers.IndexOf(state.layer);
            state.set.BindPreviewTextures();
        }

        // Layer rows are drawn in reverse list order. Placing an ungrouped layer immediately
        // after the folder in list order puts it visually above the group, keeping the remaining
        // children together directly below their folder.
        private static void PlaceLayerAboveGroup(LayerGroupingState state)
        {
            if (state?.set == null || state.layer == null || state.group == null) return;
            int layerIndex = state.set.layers.IndexOf(state.layer);
            if (layerIndex < 0 || state.set.layers.IndexOf(state.group) < 0) return;
            state.layer.parentId = null;
            state.set.NormalizeLayerHierarchy();
            var block = new List<TexturePaintLayer>();
            var roots = new HashSet<string> { state.layer.id };
            for (int i = 0; i < state.set.layers.Count; i++)
            {
                TexturePaintLayer candidate = state.set.layers[i];
                if (ReferenceEquals(candidate, state.layer) ||
                    IsDescendantOfGroup(state.set, candidate, roots)) block.Add(candidate);
            }
            for (int i = 0; i < block.Count; i++) state.set.layers.Remove(block[i]);
            int groupIndex = state.set.layers.IndexOf(state.group);
            if (groupIndex >= 0)
                state.set.layers.InsertRange(Mathf.Min(groupIndex + 1, state.set.layers.Count), block);
            state.set.NormalizeLayerHierarchy();
            state.set.activeLayerIndex = state.set.layers.IndexOf(state.layer);
            state.set.BindPreviewTextures();
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
                () => { for (int i = 0; i < peers.Count; i++) ApplyLayerMetadata(peers[i].textureSet, peers[i].layer, nextName, opacity, blendMode, null); },
                null, "layer-metadata:" + layer.id);
            MarkDocumentDirty(peers);
        }

        private static void ApplyLayerMetadata(TextureSet set, TexturePaintLayer layer, string name, float opacity,
            TexturePaintBlendMode blendMode,
            Dictionary<TexturePaintChannel, TexturePaintBlendMode> channelBlendOverrides)
        {
            TexturePaintBlendMode previousBlendMode = layer.blendMode;
            layer.name = name;
            layer.opacity = opacity;
            layer.blendMode = blendMode;
            foreach (KeyValuePair<TexturePaintChannel, TexturePaintLayerChannelSettings> pair in layer.channelSettings)
            {
                if (channelBlendOverrides != null)
                {
                    if (channelBlendOverrides.TryGetValue(pair.Key, out TexturePaintBlendMode old))
                        pair.Value.blendMode = old;
                }
                // A channel starts with the layer blend mode and continues to inherit it until the
                // user selects a different Channel Blend. Keep inherited channels synchronized while
                // preserving intentional per-channel overrides.
                else if (pair.Value.blendMode == previousBlendMode)
                {
                    pair.Value.blendMode = blendMode;
                }
            }
            set.BindPreviewTextures();
        }

        private void ChangePluginLayerDefinition(TextureSet set, TexturePaintLayer layer,
            ITexturePaintCommandExtensionV2 plugin)
        {
            if (set == null || layer?.kind != TexturePaintLayerKind.Plugin) return;
            if (!TryResolveLogicalPeers(set, layer,
                    out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            var before = new Dictionary<TexturePaintLayer, PluginLayerState>();
            for (int i = 0; i < peers.Count; i++) before[peers[i].layer] = CapturePluginLayerState(peers[i].layer);
            TexturePaintPluginParameterSet parameters = plugin != null
                ? controller.Plugins.CreateParameters(plugin) : new TexturePaintPluginParameterSet();
            PluginLayerState after = new PluginLayerState
            {
                pluginId = plugin?.Descriptor.id,
                pluginVersion = plugin?.Descriptor.pluginVersion,
                parameters = parameters.Clone(),
                parametersJson = JsonUtility.ToJson(parameters),
                stale = true,
                lastError = null
            };
            for (int i = 0; i < peers.Count; i++) ApplyPluginLayerState(peers[i].layer, after);
            PushLightweightCommand("Select Layer Plugin",
                () =>
                {
                    for (int i = 0; i < peers.Count; i++)
                        ApplyPluginLayerState(peers[i].layer, before[peers[i].layer]);
                },
                () =>
                {
                    for (int i = 0; i < peers.Count; i++) ApplyPluginLayerState(peers[i].layer, after);
                });
            MarkDocumentDirty(peers);
        }

        private void ChangePluginLayerParameters(TextureSet set, TexturePaintLayer layer,
            ITexturePaintCommandExtensionV2 plugin, TexturePaintPluginParameterSet parameters)
        {
            if (set == null || layer?.kind != TexturePaintLayerKind.Plugin || plugin == null ||
                parameters == null) return;
            if (!TryResolveLogicalPeers(set, layer,
                    out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            var before = new Dictionary<TexturePaintLayer, PluginLayerState>();
            for (int i = 0; i < peers.Count; i++) before[peers[i].layer] = CapturePluginLayerState(peers[i].layer);
            PluginLayerState after = new PluginLayerState
            {
                pluginId = plugin.Descriptor.id,
                pluginVersion = plugin.Descriptor.pluginVersion,
                parameters = parameters.Clone(),
                parametersJson = JsonUtility.ToJson(parameters),
                stale = true,
                lastError = null
            };
            for (int i = 0; i < peers.Count; i++) ApplyPluginLayerState(peers[i].layer, after);
            string key = !string.IsNullOrEmpty(layer.logicalLayerId) ? layer.logicalLayerId : layer.id;
            PushLightweightCommand("Edit Plugin Parameters",
                () =>
                {
                    for (int i = 0; i < peers.Count; i++)
                        ApplyPluginLayerState(peers[i].layer, before[peers[i].layer]);
                },
                () =>
                {
                    for (int i = 0; i < peers.Count; i++) ApplyPluginLayerState(peers[i].layer, after);
                }, null, "plugin-parameters:" + key);
            MarkDocumentDirty(peers);
        }

        private static PluginLayerState CapturePluginLayerState(TexturePaintLayer layer) =>
            new PluginLayerState
            {
                pluginId = layer.pluginId,
                pluginVersion = layer.pluginVersion,
                parameters = layer.pluginParameters?.Clone() ?? new TexturePaintPluginParameterSet(),
                parametersJson = layer.pluginParametersJson,
                stale = layer.pluginStale,
                lastError = layer.pluginLastError
            };

        private static void ApplyPluginLayerState(TexturePaintLayer layer, PluginLayerState state)
        {
            if (layer == null || state == null) return;
            layer.pluginId = state.pluginId;
            layer.pluginVersion = state.pluginVersion;
            layer.pluginParameters = state.parameters?.Clone() ?? new TexturePaintPluginParameterSet();
            layer.pluginParametersJson = state.parametersJson;
            layer.pluginStale = state.stale;
            layer.pluginLastError = state.lastError;
        }

        private void ChangeLayerMaskPluginDefinition(TextureSet set, TexturePaintLayer layer,
            ITexturePaintCommandExtensionV2 plugin)
        {
            if (!TryResolveLogicalPeers(set, layer,
                    out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            var before = new Dictionary<TexturePaintLayer, PluginLayerState>();
            for (int i = 0; i < peers.Count; i++)
                if (peers[i].layer.layerMask != null)
                    before[peers[i].layer] = CaptureMaskPluginState(peers[i].layer.layerMask);
            TexturePaintPluginParameterSet parameters = plugin != null
                ? controller.Plugins.CreateParameters(plugin) : new TexturePaintPluginParameterSet();
            var after = new PluginLayerState
            {
                pluginId = plugin?.Descriptor.id, pluginVersion = plugin?.Descriptor.pluginVersion,
                parameters = parameters.Clone(), parametersJson = JsonUtility.ToJson(parameters),
                stale = true
            };
            Action apply = () =>
            {
                for (int i = 0; i < peers.Count; i++)
                    ApplyMaskPluginState(peers[i].layer.layerMask, after);
            };
            Action restore = () =>
            {
                for (int i = 0; i < peers.Count; i++)
                    if (before.TryGetValue(peers[i].layer, out PluginLayerState state))
                        ApplyMaskPluginState(peers[i].layer.layerMask, state);
            };
            apply();
            PushLightweightCommand("Select Layer Mask Plugin", restore, apply);
            MarkDocumentDirty(peers);
        }

        private void ChangeLayerMaskPluginParameters(TextureSet set, TexturePaintLayer layer,
            ITexturePaintCommandExtensionV2 plugin, TexturePaintPluginParameterSet parameters)
        {
            if (plugin == null || parameters == null) return;
            if (!TryResolveLogicalPeers(set, layer,
                    out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            var before = new Dictionary<TexturePaintLayer, PluginLayerState>();
            for (int i = 0; i < peers.Count; i++)
                if (peers[i].layer.layerMask != null)
                    before[peers[i].layer] = CaptureMaskPluginState(peers[i].layer.layerMask);
            var after = new PluginLayerState
            {
                pluginId = plugin.Descriptor.id, pluginVersion = plugin.Descriptor.pluginVersion,
                parameters = parameters.Clone(), parametersJson = JsonUtility.ToJson(parameters),
                stale = true
            };
            Action apply = () =>
            {
                for (int i = 0; i < peers.Count; i++)
                    ApplyMaskPluginState(peers[i].layer.layerMask, after);
            };
            Action restore = () =>
            {
                for (int i = 0; i < peers.Count; i++)
                    if (before.TryGetValue(peers[i].layer, out PluginLayerState state))
                        ApplyMaskPluginState(peers[i].layer.layerMask, state);
            };
            apply();
            string key = !string.IsNullOrEmpty(layer.logicalLayerId) ? layer.logicalLayerId : layer.id;
            PushLightweightCommand("Edit Layer Mask Plugin Parameters", restore, apply, null,
                "mask-plugin-parameters:" + key);
            MarkDocumentDirty(peers);
        }

        private static PluginLayerState CaptureMaskPluginState(TexturePaintLayerMask mask) =>
            new PluginLayerState
            {
                pluginId = mask?.pluginId, pluginVersion = mask?.pluginVersion,
                parameters = mask?.pluginParameters?.Clone() ?? new TexturePaintPluginParameterSet(),
                parametersJson = mask?.pluginParametersJson, stale = mask?.pluginStale ?? true,
                lastError = mask?.pluginLastError
            };

        private static void ApplyMaskPluginState(TexturePaintLayerMask mask, PluginLayerState state)
        {
            if (mask == null || state == null) return;
            mask.pluginId = state.pluginId; mask.pluginVersion = state.pluginVersion;
            mask.pluginParameters = state.parameters?.Clone() ?? new TexturePaintPluginParameterSet();
            mask.pluginParametersJson = state.parametersJson; mask.pluginStale = state.stale;
            mask.pluginLastError = state.lastError;
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
            // Only ribbon-local effects require the expensive world-space path reprojection.
            // Conventional compositor effects (including Texture Overlay) update immediately
            // from the existing layer pixels and must not stall while editing tiling or opacity.
            bool rerenderRibbon = layer.IsSplineLayer &&
                layer.splineSettings?.pathMode == TexturePaintPathMode.Ribbon &&
                previous.TryGetValue(layer, out TexturePaintLayerEffects priorEffects) &&
                RibbonProjectionEffectsChanged(priorEffects, next);
            if (rerenderRibbon) ReapplyLayerEffectsPath(set, layer);
            PushLightweightCommand("Edit Layer Effects",
                () =>
                {
                    for (int i = 0; i < peers.Count; i++)
                        ApplyLayerEffects(peers[i].textureSet, peers[i].layer,
                            previous[peers[i].layer]);
                    if (rerenderRibbon) ReapplyLayerEffectsPath(set, layer);
                },
                () =>
                {
                    for (int i = 0; i < peers.Count; i++)
                        ApplyLayerEffects(peers[i].textureSet, peers[i].layer, next);
                    if (rerenderRibbon) ReapplyLayerEffectsPath(set, layer);
                }, null, "layer-effects:" + layer.id);
            MarkDocumentDirty(peers);
        }

        private bool AddLayerMaskWithHistory(TextureSet set, TexturePaintLayer layer, float baseValue)
        {
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers,
                out string error)) { ShowWorkspaceStatus(error); return false; }
            for (int i = 0; i < peers.Count; i++)
                if (peers[i].layer.layerMask != null)
                { ShowWorkspaceStatus("The selected layer already has a mask."); return false; }
            var masks = new Dictionary<TexturePaintLayer, TexturePaintLayerMask>();
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLogicalLayerMember peer = peers[i];
                TexturePaintLayerMask mask = peer.textureSet.AddLayerMask(peer.layer, baseValue);
                if (mask == null) continue;
                masks[peer.layer] = mask;
                peer.textureSet.BindPreviewTextures();
            }
            if (masks.Count == 0) return false;
            Action detach = () =>
            {
                for (int i = 0; i < peers.Count; i++)
                    if (masks.TryGetValue(peers[i].layer, out TexturePaintLayerMask mask) &&
                        ReferenceEquals(peers[i].layer.layerMask, mask))
                    { peers[i].layer.layerMask = null; peers[i].textureSet.BindPreviewTextures(); }
            };
            Action attach = () =>
            {
                for (int i = 0; i < peers.Count; i++)
                    if (masks.TryGetValue(peers[i].layer, out TexturePaintLayerMask mask))
                    { peers[i].layer.layerMask = mask; peers[i].textureSet.BindPreviewTextures(); }
            };
            PushLightweightCommand(baseValue < 0.5f ? "Add Black Layer Mask" : "Add White Layer Mask",
                detach, attach, () =>
                {
                    foreach (KeyValuePair<TexturePaintLayer, TexturePaintLayerMask> pair in masks)
                        if (!ReferenceEquals(pair.Key.layerMask, pair.Value)) pair.Value.Dispose();
                });
            MarkDocumentDirty(peers);
            return true;
        }

        private bool RemoveLayerMaskWithHistory(TextureSet set, TexturePaintLayer layer)
        {
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers,
                out string error)) { ShowWorkspaceStatus(error); return false; }
            var masks = new Dictionary<TexturePaintLayer, TexturePaintLayerMask>();
            for (int i = 0; i < peers.Count; i++)
                if (peers[i].layer.layerMask != null) masks[peers[i].layer] = peers[i].layer.layerMask;
            if (masks.Count == 0) return false;
            Action detach = () =>
            {
                for (int i = 0; i < peers.Count; i++)
                    if (masks.TryGetValue(peers[i].layer, out TexturePaintLayerMask mask) &&
                        ReferenceEquals(peers[i].layer.layerMask, mask))
                    { peers[i].layer.layerMask = null; peers[i].textureSet.BindPreviewTextures(); }
            };
            Action attach = () =>
            {
                for (int i = 0; i < peers.Count; i++)
                    if (masks.TryGetValue(peers[i].layer, out TexturePaintLayerMask mask))
                    { peers[i].layer.layerMask = mask; peers[i].textureSet.BindPreviewTextures(); }
            };
            detach();
            PushLightweightCommand("Remove Layer Mask", attach, detach, () =>
            {
                foreach (KeyValuePair<TexturePaintLayer, TexturePaintLayerMask> pair in masks)
                    if (!ReferenceEquals(pair.Key.layerMask, pair.Value)) pair.Value.Dispose();
            });
            MarkDocumentDirty(peers);
            return true;
        }

        private bool CopyLayerMaskToClipboard(TextureSet set, TexturePaintLayer layer)
        {
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers,
                out string error))
            {
                ShowWorkspaceStatus(error);
                return false;
            }
            var clipboard = new LayerMaskClipboardData { sourceLayerName = layer.name };
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLogicalLayerMember peer = peers[i];
                TexturePaintLayerMask mask = peer.layer?.layerMask;
                if (mask?.target?.Front == null) continue;
                LayerMaskClipboardEntry entry = CaptureLayerMaskClipboardEntry(mask);
                if (entry == null) continue;
                string key = LayerMaskClipboardKey(peer);
                if (!string.IsNullOrEmpty(key)) clipboard.entries[key] = entry;
                if (ReferenceEquals(peer.textureSet, set) && ReferenceEquals(peer.layer, layer))
                    clipboard.fallback = entry;
                clipboard.fallback ??= entry;
            }
            if (clipboard.fallback == null)
            {
                ShowWorkspaceStatus("The selected layer has no mask to copy.");
                return false;
            }
            layerMaskClipboard = clipboard;
            ShowWorkspaceStatus($"Copied mask from '{layer.name}'.");
            RepaintAll();
            return true;
        }

        private bool PasteLayerMaskFromClipboardWithHistory(TextureSet set, TexturePaintLayer layer)
        {
            if (!HasLayerMaskClipboard)
            {
                ShowWorkspaceStatus("Copy a layer mask before pasting.");
                return false;
            }
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers,
                out string error))
            {
                ShowWorkspaceStatus(error);
                return false;
            }
            var before = new Dictionary<TexturePaintLayer, TexturePaintLayerMask>();
            var after = new Dictionary<TexturePaintLayer, TexturePaintLayerMask>();
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLogicalLayerMember peer = peers[i];
                string key = LayerMaskClipboardKey(peer);
                LayerMaskClipboardEntry entry = !string.IsNullOrEmpty(key) &&
                    layerMaskClipboard.entries.TryGetValue(key, out LayerMaskClipboardEntry matching)
                        ? matching : layerMaskClipboard.fallback;
                TexturePaintLayerMask pasted = CreateLayerMaskFromClipboardEntry(
                    peer.textureSet, peer.layer, entry);
                if (pasted == null)
                {
                    foreach (TexturePaintLayerMask created in after.Values) created?.Dispose();
                    ShowWorkspaceStatus("The destination layer has no valid mask resolution.");
                    return false;
                }
                before[peer.layer] = peer.layer.layerMask;
                after[peer.layer] = pasted;
            }

            Action restore = () => ApplyLayerMaskClipboardState(peers, before);
            Action apply = () => ApplyLayerMaskClipboardState(peers, after);
            apply();
            PushLightweightCommand("Paste Layer Mask", restore, apply, () =>
            {
                for (int i = 0; i < peers.Count; i++)
                {
                    TexturePaintLayer peerLayer = peers[i].layer;
                    if (before.TryGetValue(peerLayer, out TexturePaintLayerMask oldMask) &&
                        oldMask != null && !ReferenceEquals(peerLayer.layerMask, oldMask))
                        oldMask.Dispose();
                    if (after.TryGetValue(peerLayer, out TexturePaintLayerMask pastedMask) &&
                        pastedMask != null && !ReferenceEquals(peerLayer.layerMask, pastedMask))
                        pastedMask.Dispose();
                }
            });
            MarkDocumentDirty(peers);
            ShowWorkspaceStatus($"Pasted mask from '{layerMaskClipboard.sourceLayerName}' onto '{layer.name}'.");
            return true;
        }

        private static LayerMaskClipboardEntry CaptureLayerMaskClipboardEntry(TexturePaintLayerMask mask)
        {
            RenderTexture source = mask?.target?.Front;
            if (source == null || source.width <= 0 || source.height <= 0) return null;
            Texture2D snapshot = new Texture2D(source.width, source.height,
                TextureFormat.RGBA32, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = source;
                snapshot.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
                snapshot.Apply(false, false);
                return new LayerMaskClipboardEntry
                {
                    width = source.width,
                    height = source.height,
                    pixels = snapshot.GetPixels32(),
                    baseValue = mask.baseValue,
                    effects = mask.effects?.Clone() ?? new TexturePaintLayerMaskEffects(),
                    sourceSettings = mask.sourceSettings?.Clone() ??
                        TexturePaintLayerMask.DefaultSourceSettings(),
                    sourceChannel = mask.sourceChannel,
                    pluginId = mask.pluginId,
                    pluginVersion = mask.pluginVersion,
                    pluginParametersJson = mask.pluginParametersJson,
                    pluginParameters = mask.pluginParameters?.Clone() ??
                        new TexturePaintPluginParameterSet(),
                    pluginStale = mask.pluginStale,
                    pluginLastError = mask.pluginLastError
                };
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(snapshot);
            }
        }

        private static TexturePaintLayerMask CreateLayerMaskFromClipboardEntry(TextureSet set,
            TexturePaintLayer layer, LayerMaskClipboardEntry entry)
        {
            if (set == null || layer == null || entry?.pixels == null || entry.width <= 0 ||
                entry.height <= 0 || entry.pixels.Length != entry.width * entry.height) return null;
            set.GetMaskResolution(out int width, out int height);
            if (width <= 0 || height <= 0) return null;
            Texture2D snapshot = new Texture2D(entry.width, entry.height,
                TextureFormat.RGBA32, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            try
            {
                snapshot.SetPixels32(entry.pixels);
                snapshot.Apply(false, false);
                return new TexturePaintLayerMask
                {
                    baseValue = Mathf.Clamp01(entry.baseValue),
                    effects = entry.effects?.Clone() ?? new TexturePaintLayerMaskEffects(),
                    sourceSettings = entry.sourceSettings?.Clone() ??
                        TexturePaintLayerMask.DefaultSourceSettings(),
                    sourceChannel = entry.sourceChannel,
                    pluginId = entry.pluginId,
                    pluginVersion = entry.pluginVersion,
                    pluginParametersJson = entry.pluginParametersJson,
                    pluginParameters = entry.pluginParameters?.Clone() ??
                        new TexturePaintPluginParameterSet(),
                    pluginStale = entry.pluginStale,
                    pluginLastError = entry.pluginLastError,
                    target = new EditableTextureTarget(layer.name + " Layer Mask", width, height,
                        RenderTextureFormat.ARGB32, snapshot, TextureSet.MaskColor(entry.baseValue))
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(snapshot);
            }
        }

        private static void ApplyLayerMaskClipboardState(List<TexturePaintLogicalLayerMember> peers,
            Dictionary<TexturePaintLayer, TexturePaintLayerMask> state)
        {
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLogicalLayerMember peer = peers[i];
                state.TryGetValue(peer.layer, out TexturePaintLayerMask mask);
                peer.layer.layerMask = mask;
                peer.textureSet.BindPreviewTextures();
            }
        }

        private static string LayerMaskClipboardKey(TexturePaintLogicalLayerMember peer)
        {
            if (!string.IsNullOrEmpty(peer?.textureSet?.persistentId))
                return "set:" + peer.textureSet.persistentId;
            if (peer?.targetMember == null) return null;
            return "member:" + (peer.targetMember.slotName ?? string.Empty) + ":" +
                peer.targetMember.udimTileNumber;
        }

        private void ChangeLayerMaskEffects(TextureSet set, TexturePaintLayer layer,
            TexturePaintLayerMaskEffects effects)
        {
            if (effects == null) return;
            if (!TryResolveLogicalPeers(set, layer,
                out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            TexturePaintLayerMaskEffects next = effects.Clone();
            next.Normalize();
            var previous = new Dictionary<TexturePaintLayer, TexturePaintLayerMaskEffects>();
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLayerMask mask = peers[i].layer.layerMask;
                if (mask == null) continue;
                previous[peers[i].layer] = mask.effects?.Clone() ?? new TexturePaintLayerMaskEffects();
                mask.effects = next.Clone();
                peers[i].textureSet.BindPreviewTextures();
            }
            if (previous.Count == 0) return;
            Action restore = () =>
            {
                for (int i = 0; i < peers.Count; i++)
                    if (peers[i].layer.layerMask != null && previous.TryGetValue(peers[i].layer,
                        out TexturePaintLayerMaskEffects value))
                    { peers[i].layer.layerMask.effects = value.Clone(); peers[i].textureSet.BindPreviewTextures(); }
            };
            Action apply = () =>
            {
                for (int i = 0; i < peers.Count; i++)
                    if (peers[i].layer.layerMask != null)
                    { peers[i].layer.layerMask.effects = next.Clone(); peers[i].textureSet.BindPreviewTextures(); }
            };
            PushLightweightCommand("Edit Layer Mask Effects", restore, apply, null,
                "layer-mask-effects:" + layer.id);
            MarkDocumentDirty(peers);
        }

        private void ChangeLayerMaskSource(TextureSet set, TexturePaintLayer layer,
            TexturePaintChannelSourceSettings source, TexturePaintChannel sourceChannel)
        {
            if (source == null) return;
            float nextValue = TexturePaintChannelUtility.ScalarValue(source.color);
            TexturePaintChannelSourceSettings next = TexturePaintLayerMask.DefaultSourceSettings();
            next.color = new Color(nextValue, nextValue, nextValue, 1f);
            sourceChannel = TexturePaintChannel.Albedo;
            if (!TryResolveLogicalPeers(set, layer,
                out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            var previousSources = new Dictionary<TexturePaintLayer, TexturePaintChannelSourceSettings>();
            var previousChannels = new Dictionary<TexturePaintLayer, TexturePaintChannel>();
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLayerMask mask = peers[i].layer.layerMask;
                if (mask == null) continue;
                mask.NormalizePaintSource();
                previousSources[peers[i].layer] = mask.sourceSettings?.Clone() ??
                    TexturePaintLayerMask.DefaultSourceSettings();
                previousChannels[peers[i].layer] = mask.sourceChannel;
                mask.sourceSettings = next.Clone();
                mask.sourceChannel = sourceChannel;
            }
            if (previousSources.Count == 0) return;
            Action restore = () =>
            {
                for (int i = 0; i < peers.Count; i++)
                    if (peers[i].layer.layerMask != null &&
                        previousSources.TryGetValue(peers[i].layer, out TexturePaintChannelSourceSettings prior))
                    {
                        peers[i].layer.layerMask.sourceSettings = prior.Clone();
                        peers[i].layer.layerMask.sourceChannel = previousChannels[peers[i].layer];
                    }
            };
            Action apply = () =>
            {
                for (int i = 0; i < peers.Count; i++)
                    if (peers[i].layer.layerMask != null)
                    {
                        peers[i].layer.layerMask.sourceSettings = next.Clone();
                        peers[i].layer.layerMask.sourceChannel = sourceChannel;
                    }
            };
            PushLightweightCommand("Edit Layer Mask Paint Source", restore, apply, null,
                "layer-mask-source:" + layer.id);
            MarkDocumentDirty(peers);
        }

        private static bool RibbonProjectionEffectsChanged(TexturePaintLayerEffects before,
            TexturePaintLayerEffects after)
        {
            before ??= new TexturePaintLayerEffects();
            after ??= new TexturePaintLayerEffects();
            before.Normalize();
            after.Normalize();
            return RibbonEffectSignature(before) != RibbonEffectSignature(after);
        }

        private static string RibbonEffectSignature(TexturePaintLayerEffects effects)
        {
            var signature = new System.Text.StringBuilder();
            for (int i = 0; i < effects.Stack.Count; i++)
            {
                TexturePaintLayerEffectSettings effect = effects.Stack[i];
                if (effect == null || TexturePaintLayerEffects.IsCompositeOnlyEffect(effect.kind)) continue;
                signature.Append(JsonUtility.ToJson(effect));
            }
            return signature.ToString();
        }

        private static void ApplyLayerEffects(TextureSet set, TexturePaintLayer layer,
            TexturePaintLayerEffects effects)
        {
            layer.effects = effects?.Clone() ?? new TexturePaintLayerEffects();
            layer.effects.Normalize();
            set.BindPreviewTextures();
        }

        private void ReapplyLayerEffectsPath(TextureSet set, TexturePaintLayer layer)
        {
            if (controller?.Textures == null || set == null || layer == null || !layer.IsSplineLayer ||
                !set.layers.Contains(layer)) return;
            int setIndex = -1;
            for (int i = 0; i < controller.Textures.Sets.Count; i++)
                if (ReferenceEquals(controller.Textures.Sets[i], set)) { setIndex = i; break; }
            if (setIndex < 0) return;
            selectedSurface = setIndex;
            set.activeLayerIndex = set.layers.IndexOf(layer);
            spline = layer.spline;
            splineMode = true;
            RestoreSplineSettings(layer.splineSettings);
            RequestSplineReapply(set, false);
        }

        private void ChangeLayerChannel(TextureSet set, TexturePaintLayer layer, TexturePaintChannel channel,
            bool enabled, bool locked, float contribution, float opacity, TexturePaintBlendMode blendMode)
        {
            if (set == null || layer == null) return;
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            var before = new Dictionary<TexturePaintLayer, TexturePaintLayerChannelSettings>();
            var after = new Dictionary<TexturePaintLayer, TexturePaintLayerChannelSettings>();
            for (int i = 0; i < peers.Count; i++)
                if (peers[i].textureSet.GetChannel(channel) == null)
                { ShowWorkspaceStatus($"Target member '{peers[i].targetMember?.slotName}' does not support {channel}."); return; }
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLayerChannelSettings settings = peers[i].layer.GetChannelSettings(channel);
                before[peers[i].layer] = settings.Clone();
                TexturePaintLayerChannelSettings updated = settings.Clone();
                updated.channel = channel;
                updated.enabled = enabled;
                updated.locked = locked;
                updated.contribution = contribution;
                updated.opacity = opacity;
                updated.blendMode = blendMode;
                after[peers[i].layer] = updated;
                ApplyChannelSettings(peers[i].textureSet, peers[i].layer, channel, updated);
            }
            PushLightweightCommand("Edit Layer Channel",
                () => { for (int i = 0; i < peers.Count; i++) if (before.TryGetValue(peers[i].layer, out TexturePaintLayerChannelSettings value)) ApplyChannelSettings(peers[i].textureSet, peers[i].layer, channel, value); },
                () => { for (int i = 0; i < peers.Count; i++) if (after.TryGetValue(peers[i].layer, out TexturePaintLayerChannelSettings value)) ApplyChannelSettings(peers[i].textureSet, peers[i].layer, channel, value); },
                null, "layer-channel:" + layer.id + ":" + channel);
            MarkDocumentDirty(peers);
        }

        private static void ApplyChannelSettings(TextureSet set, TexturePaintLayer layer, TexturePaintChannel channel,
            TexturePaintLayerChannelSettings value)
        {
            layer.channelSettings[channel] = value.Clone();
            set.BindPreviewTextures();
        }

        private void ChangeLayerNormalControlStrength(TextureSet set, TexturePaintLayer layer,
            float strength)
        {
            if (set == null || layer == null ||
                !layer.channels.ContainsKey(TexturePaintChannel.NormalControl)) return;
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers,
                    out string error))
            {
                ShowWorkspaceStatus(error);
                return;
            }
            strength = Mathf.Clamp(strength, 0f, 16f);
            var before = new Dictionary<TexturePaintLayer, TexturePaintLayerChannelSettings>();
            var after = new Dictionary<TexturePaintLayer, TexturePaintLayerChannelSettings>();
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLogicalLayerMember peer = peers[i];
                if (!peer.layer.channels.ContainsKey(TexturePaintChannel.NormalControl))
                {
                    ShowWorkspaceStatus($"Target member '{peer.targetMember?.slotName}' has no Normal Control channel.");
                    return;
                }
                TexturePaintLayerChannelSettings settings = peer.layer.GetChannelSettings(
                    TexturePaintChannel.NormalControl);
                before[peer.layer] = settings.Clone();
                TexturePaintLayerChannelSettings updated = settings.Clone();
                updated.hasNormalControlStrength = true;
                updated.normalControlStrength = strength;
                after[peer.layer] = updated;
            }

            void Apply(Dictionary<TexturePaintLayer, TexturePaintLayerChannelSettings> values)
            {
                for (int i = 0; i < peers.Count; i++)
                {
                    TexturePaintLogicalLayerMember peer = peers[i];
                    if (!values.TryGetValue(peer.layer, out TexturePaintLayerChannelSettings value)) continue;
                    peer.layer.channelSettings[TexturePaintChannel.NormalControl] = value.Clone();
                    peer.textureSet.BindPreviewTextures();
                }
            }

            Apply(after);
            PushLightweightCommand("Change Normal Control Height Strength",
                () => Apply(before), () => Apply(after), null,
                "normal-control-layer-strength:" + layer.id);
            MarkDocumentDirty(peers);
        }

        private void ChangeNormalControlSettings(TextureSet set, float strength, int radius, bool invert)
        {
            if (set == null || set.GetChannel(TexturePaintChannel.NormalControl) == null) return;
            float beforeStrength = set.normalControlStrength;
            int beforeRadius = set.normalControlRadius;
            bool beforeInvert = set.normalControlInvert;
            strength = Mathf.Clamp(strength, 0f, 16f);
            radius = Mathf.Clamp(radius, 1, 16);
            if (Mathf.Approximately(beforeStrength, strength) && beforeRadius == radius &&
                beforeInvert == invert) return;

            void Apply(float nextStrength, int nextRadius, bool nextInvert)
            {
                set.normalControlStrength = nextStrength;
                set.normalControlRadius = nextRadius;
                set.normalControlInvert = nextInvert;
                set.BindPreviewTextures(false);
            }

            Apply(strength, radius, invert);
            PushLightweightCommand("Edit Normal Control",
                () => Apply(beforeStrength, beforeRadius, beforeInvert),
                () => Apply(strength, radius, invert), null,
                "normal-control:" + set.persistentId);
            MarkDocumentDirtyAfterStructuralChange();
        }

        private void ChangeChannelAdjustments(TextureSet set, TexturePaintChannel channel,
            TexturePaintChannelAdjustments requested)
        {
            TextureChannelTarget target = set?.GetChannel(channel);
            if (target == null || requested == null || channel == TexturePaintChannel.Normal) return;
            TexturePaintChannelAdjustments before = target.adjustments?.Clone() ??
                new TexturePaintChannelAdjustments();
            TexturePaintChannelAdjustments after = requested.Clone();
            before.Normalize();
            after.Normalize();
            if (ChannelAdjustmentsEqual(before, after)) return;

            void Apply(TexturePaintChannelAdjustments value)
            {
                target.adjustments = value.Clone();
                set.CompositeChannel(channel);
                set.BindPreviewTextures(false);
            }

            Apply(after);
            PushLightweightCommand("Adjust " + TexturePaintChannelUtility.DisplayName(channel),
                () => Apply(before), () => Apply(after), null,
                "channel-adjustments:" + set.persistentId + ":" + channel);
            MarkDocumentDirtyAfterStructuralChange();
        }

        private static bool ChannelAdjustmentsEqual(TexturePaintChannelAdjustments left,
            TexturePaintChannelAdjustments right)
        {
            return Mathf.Approximately(left.brightness, right.brightness) &&
                Mathf.Approximately(left.contrast, right.contrast) &&
                Mathf.Approximately(left.hue, right.hue) &&
                Mathf.Approximately(left.vibrance, right.vibrance) &&
                Mathf.Approximately(left.saturation, right.saturation) &&
                left.colorBalance == right.colorBalance &&
                AnimationCurvesEqual(left.grayscaleAdjustmentCurve, right.grayscaleAdjustmentCurve);
        }

        private static bool AnimationCurvesEqual(AnimationCurve left, AnimationCurve right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.preWrapMode != right.preWrapMode ||
                left.postWrapMode != right.postWrapMode || left.length != right.length) return false;
            Keyframe[] leftKeys = left.keys;
            Keyframe[] rightKeys = right.keys;
            for (int i = 0; i < leftKeys.Length; i++)
                if (!leftKeys[i].Equals(rightKeys[i])) return false;
            return true;
        }

        private bool AddLayerChannelWithHistory(TextureSet set, TexturePaintLayer layer,
            TexturePaintChannel channel)
        {
            if (set == null || layer == null || layer.kind == TexturePaintLayerKind.Group) return false;
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers,
                    out string error))
            {
                ShowWorkspaceStatus(error);
                return false;
            }
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLogicalLayerMember peer = peers[i];
                if (peer.textureSet.GetChannel(channel) == null)
                {
                    ShowWorkspaceStatus($"Target member '{peer.targetMember?.slotName}' does not support {channel}.");
                    return false;
                }
                if (peer.layer.channels.ContainsKey(channel))
                {
                    ShowWorkspaceStatus($"{channel} is already present on this logical layer.");
                    return false;
                }
            }

            var added = new List<LayerChannelLocation>(peers.Count);
            try
            {
                for (int i = 0; i < peers.Count; i++)
                {
                    TexturePaintLogicalLayerMember peer = peers[i];
                    TextureChannelTarget baseChannel = peer.textureSet.GetChannel(channel);
                    var target = new EditableTextureTarget(peer.layer.name + " " + channel,
                        baseChannel.Texture.width, baseChannel.Texture.height, baseChannel.format,
                        null, Color.clear);
                    TexturePaintLayerChannelSettings previousSettings =
                        peer.layer.GetChannelSettings(channel, false)?.Clone();
                    TexturePaintLayerChannelSettings settings = previousSettings?.Clone() ??
                        new TexturePaintLayerChannelSettings
                        {
                            channel = channel,
                            enabled = true,
                            locked = false,
                            contribution = 1f,
                            opacity = 1f,
                            blendMode = peer.layer.blendMode
                        };
                    var location = new LayerChannelLocation
                    {
                        set = peer.textureSet,
                        layer = peer.layer,
                        channel = channel,
                        target = target,
                        settings = settings,
                        previousSettings = previousSettings
                    };
                    added.Add(location);
                    AttachLayerChannel(location);
                }
            }
            catch
            {
                for (int i = 0; i < added.Count; i++)
                {
                    DetachLayerChannel(added[i]);
                    added[i].target?.Dispose();
                }
                throw;
            }

            PushLightweightCommand("Add Layer Channel",
                () => { for (int i = 0; i < added.Count; i++) DetachLayerChannel(added[i]); },
                () => { for (int i = 0; i < added.Count; i++) AttachLayerChannel(added[i]); },
                () =>
                {
                    for (int i = 0; i < added.Count; i++)
                    {
                        LayerChannelLocation location = added[i];
                        if (!location.layer.channels.TryGetValue(location.channel,
                                out EditableTextureTarget attached) ||
                            !ReferenceEquals(attached, location.target))
                            location.target?.Dispose();
                    }
                });
            MarkDocumentDirty(peers);
            return true;
        }

        private bool RemoveLayerChannelWithHistory(TextureSet set, TexturePaintLayer layer,
            TexturePaintChannel channel)
        {
            if (set == null || layer == null || layer.kind == TexturePaintLayerKind.Group) return false;
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers,
                    out string error))
            { ShowWorkspaceStatus(error); return false; }
            var removed = new List<LayerChannelLocation>(peers.Count);
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLogicalLayerMember peer = peers[i];
                if (!peer.layer.channels.TryGetValue(channel, out EditableTextureTarget target))
                { ShowWorkspaceStatus($"{channel} is not present on every logical layer member."); return false; }
                TexturePaintLayerEffects beforeEffects = peer.layer.effects?.Clone() ??
                    new TexturePaintLayerEffects();
                TexturePaintLayerEffects afterEffects = beforeEffects.Clone();
                TexturePaintChannel replacement = FirstRemainingChannel(peer.layer, channel,
                    out bool hasReplacement);
                for (int effectIndex = 0; effectIndex < afterEffects.Stack.Count; effectIndex++)
                {
                    TexturePaintLayerEffectSettings effect = afterEffects.Stack[effectIndex];
                    if (effect == null || effect.channel != channel) continue;
                    if (hasReplacement) effect.channel = replacement; else effect.enabled = false;
                }
                removed.Add(new LayerChannelLocation
                {
                    set = peer.textureSet,
                    layer = peer.layer,
                    channel = channel,
                    target = target,
                    settings = peer.layer.GetChannelSettings(channel).Clone(),
                    effectsBefore = beforeEffects,
                    effectsAfter = afterEffects
                });
            }
            for (int i = 0; i < removed.Count; i++) DetachRemovedLayerChannel(removed[i]);
            PushLightweightCommand("Remove Layer Channel",
                () => { for (int i = 0; i < removed.Count; i++) AttachRemovedLayerChannel(removed[i]); },
                () => { for (int i = 0; i < removed.Count; i++) DetachRemovedLayerChannel(removed[i]); },
                () =>
                {
                    for (int i = 0; i < removed.Count; i++)
                        if (!removed[i].layer.channels.TryGetValue(channel, out EditableTextureTarget attached) ||
                            !ReferenceEquals(attached, removed[i].target)) removed[i].target?.Dispose();
                });
            MarkDocumentDirty(peers);
            return true;
        }

        private static TexturePaintChannel FirstRemainingChannel(TexturePaintLayer layer,
            TexturePaintChannel removed, out bool found)
        {
            foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                if (channel != removed && layer.channels.ContainsKey(channel))
                { found = true; return channel; }
            found = false;
            return TexturePaintChannel.Albedo;
        }

        private static void DetachRemovedLayerChannel(LayerChannelLocation location)
        {
            if (location?.set == null || location.layer == null) return;
            if (location.layer.channels.TryGetValue(location.channel, out EditableTextureTarget target) &&
                ReferenceEquals(target, location.target)) location.layer.channels.Remove(location.channel);
            location.layer.channelSettings.Remove(location.channel);
            location.layer.effects = location.effectsAfter.Clone();
            location.set.BindPreviewTextures();
        }

        private static void AttachRemovedLayerChannel(LayerChannelLocation location)
        {
            if (location?.set == null || location.layer == null || location.target == null) return;
            location.layer.channels[location.channel] = location.target;
            location.layer.channelSettings[location.channel] = location.settings.Clone();
            location.layer.effects = location.effectsBefore.Clone();
            location.set.BindPreviewTextures();
        }

        private static void AttachLayerChannel(LayerChannelLocation location)
        {
            if (location?.set == null || location.layer == null || location.target == null) return;
            location.layer.channels[location.channel] = location.target;
            location.layer.channelSettings[location.channel] = location.settings.Clone();
            location.set.BindPreviewTextures();
        }

        private static void DetachLayerChannel(LayerChannelLocation location)
        {
            if (location?.set == null || location.layer == null) return;
            if (location.layer.channels.TryGetValue(location.channel, out EditableTextureTarget target) &&
                ReferenceEquals(target, location.target))
                location.layer.channels.Remove(location.channel);
            if (location.previousSettings != null)
                location.layer.channelSettings[location.channel] = location.previousSettings.Clone();
            else
                location.layer.channelSettings.Remove(location.channel);
            location.set.BindPreviewTextures();
        }

        private bool ChangeLayerChannelSources(TextureSet set, TexturePaintLayer layer,
            IReadOnlyDictionary<TexturePaintChannel, TexturePaintChannelSourceSettings> sources)
        {
            if (set == null || layer == null || sources == null || sources.Count == 0) return false;
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers,
                    out string error))
            {
                ShowWorkspaceStatus(error);
                return false;
            }
            var states = new List<LayerChannelSourceState>(peers.Count * sources.Count);
            TexturePaintLogicalTarget logicalTarget = !string.IsNullOrEmpty(layer.paintTargetId)
                ? controller.LogicalTargets?.FindById(layer.paintTargetId)
                : controller.LogicalLayers?.FindTarget(set);
            foreach (TexturePaintLogicalLayerMember peer in peers)
            foreach (KeyValuePair<TexturePaintChannel, TexturePaintChannelSourceSettings> pair in sources)
            {
                if (!peer.layer.channels.ContainsKey(pair.Key))
                {
                    ShowWorkspaceStatus($"{pair.Key} was not added to every logical layer member.");
                    return false;
                }
                TexturePaintLayerChannelSettings settings = peer.layer.GetChannelSettings(pair.Key);
                TexturePaintChannelSourceSettings resolved = pair.Value?.Clone();
                if (resolved?.source == TexturePaintBrushSource.Overlay && resolved.sourceOverlay != null)
                {
                    resolved.sourceOverlay = TexturePaintLogicalLayerController.ResolveMemberOverlay(
                        logicalTarget, set, resolved.sourceOverlay, peer.textureSet);
                    if (resolved.sourceOverlay == null)
                    {
                        ShowWorkspaceStatus($"No matching overlay source exists for target member " +
                            $"'{peer.targetMember?.slotName}'.");
                        return false;
                    }
                }
                states.Add(new LayerChannelSourceState
                {
                    set = peer.textureSet,
                    layer = peer.layer,
                    channel = pair.Key,
                    before = settings.sourceSettings?.Clone(),
                    after = resolved
                });
            }
            ApplyLayerChannelSources(states, true);
            PushLightweightCommand(sources.Count > 1 ? "Assign Sprite Set" : "Edit Layer Channel Source",
                () => ApplyLayerChannelSources(states, false),
                () => ApplyLayerChannelSources(states, true), null,
                sources.Count == 1 ? "layer-channel-source:" + layer.id + ":" +
                    states[0].channel : null);
            MarkDocumentDirty(peers);
            return true;
        }

        private static void ApplyLayerChannelSources(List<LayerChannelSourceState> states, bool useAfter)
        {
            var changedSets = new HashSet<TextureSet>();
            var fillLayers = new HashSet<TexturePaintLayer>();
            for (int i = 0; i < states.Count; i++)
            {
                LayerChannelSourceState state = states[i];
                TexturePaintChannelSourceSettings source = useAfter ? state.after : state.before;
                state.layer.GetChannelSettings(state.channel).sourceSettings = source?.Clone();
                changedSets.Add(state.set);
                if (state.layer.kind == TexturePaintLayerKind.Fill) fillLayers.Add(state.layer);
            }
            foreach (TexturePaintLayer fillLayer in fillLayers)
            {
                TextureSet fillSet = null;
                for (int i = 0; i < states.Count; i++)
                    if (ReferenceEquals(states[i].layer, fillLayer)) { fillSet = states[i].set; break; }
                if (fillSet == null) continue;
                foreach (KeyValuePair<TexturePaintChannel, EditableTextureTarget> pair in fillLayer.channels)
                {
                    TexturePaintChannelSourceSettings source =
                        fillLayer.GetChannelSettings(pair.Key, false)?.sourceSettings;
                    if (source == null && pair.Key != fillLayer.fillChannel)
                        pair.Value.Reset(null, Color.clear);
                }
                fillSet.RegenerateFillLayer(fillLayer);
            }
            foreach (TextureSet changedSet in changedSets) changedSet.BindPreviewTextures();
        }

        private void ChangeFillLayer(TextureSet set, TexturePaintLayer layer, TexturePaintChannel channel,
            TexturePaintFillSettings settings)
        {
            if (set == null || layer == null || layer.kind != TexturePaintLayerKind.Fill || settings == null) return;
            if (settings.source == TexturePaintBrushSource.Texture && settings.sourceTexture == null &&
                settings.sourceSprite == null)
            {
                RestoreFillSourceControls(layer);
                ShowWorkspaceStatus("Select a source texture or sprite before changing this Fill layer.");
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
                ? controller?.LogicalTargets?.FindById(layer.paintTargetId)
                : controller?.LogicalLayers?.FindTarget(set);
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
                TexturePaintLayer previousLayer = peer.layer;
                TexturePaintLayer workingLayer = peer.textureSet.CloneLayer(previousLayer,
                    previousLayer.name, true);
                if (workingLayer == null)
                {
                    for (int rollback = 0; rollback < before.Count; rollback++)
                        SwapLayerSnapshot(before[rollback].set, after[rollback].layer, before[rollback].layer, before[rollback].index);
                    RestoreFillSourceControls(layer);
                    ShowWorkspaceStatus("The Fill layer could not be captured for history.");
                    return;
                }
                // Replace before mutating. The detached instance may already be the immutable redo
                // snapshot of an older command; editing it in place would corrupt that command.
                peer.textureSet.layers[index] = workingLayer;
                peer.textureSet.activeLayerIndex = index;
                if (!peer.textureSet.UpdateFillLayer(workingLayer, channel, resolvedSettings[i]))
                {
                    peer.textureSet.layers[index] = previousLayer;
                    workingLayer.Dispose();
                    for (int rollback = 0; rollback < before.Count; rollback++)
                        SwapLayerSnapshot(before[rollback].set, after[rollback].layer,
                            before[rollback].layer, before[rollback].index);
                    peer.textureSet.BindPreviewTextures();
                    RestoreFillSourceControls(layer);
                    ShowWorkspaceStatus("The Fill source could not be generated for every target member.");
                    return;
                }
                before.Add(new LayerLocation { set = peer.textureSet, layer = previousLayer, index = index });
                after.Add(new LayerLocation { set = peer.textureSet, layer = workingLayer, index = index });
            }
            PushLightweightCommand("Edit Fill Layer",
                () => { for (int i = 0; i < before.Count; i++) SwapLayerSnapshot(before[i].set, after[i].layer, before[i].layer, before[i].index); },
                () => { for (int i = 0; i < before.Count; i++) SwapLayerSnapshot(before[i].set, before[i].layer, after[i].layer, after[i].index); },
                () =>
                {
                    for (int i = 0; i < before.Count; i++)
                    {
                        DisposeLayerIfDetached(before[i].set, before[i].layer);
                        DisposeLayerIfDetached(after[i].set, after[i].layer);
                    }
                });
            MarkDocumentDirty(peers);
        }

        private bool RasterizeFillLayerWithHistory(TextureSet set, TexturePaintLayer layer)
        {
            if (set == null || layer?.kind != TexturePaintLayerKind.Fill) return false;
            if (!TryResolveLogicalPeers(set, layer, out List<TexturePaintLogicalLayerMember> peers,
                    out string error))
            {
                ShowWorkspaceStatus(error);
                return false;
            }

            var before = new List<LayerLocation>(peers.Count);
            var after = new List<LayerLocation>(peers.Count);
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLogicalLayerMember peer = peers[i];
                int index = peer.textureSet.layers.IndexOf(peer.layer);
                Texture maskPixels = peer.textureSet.GetLayerMaskPreview(peer.layer);
                TexturePaintLayer rasterized = peer.textureSet.CloneLayer(peer.layer,
                    peer.layer.name, true);
                if (rasterized == null)
                {
                    for (int dispose = 0; dispose < after.Count; dispose++)
                        after[dispose].layer.Dispose();
                    return false;
                }

                rasterized.kind = TexturePaintLayerKind.Paint;
                rasterized.fillSettings = null;
                rasterized.fillColor = Color.white;
                TexturePaintChannel paintChannel = rasterized.TryGetFirstAuthoredChannel(
                    out TexturePaintChannel firstChannel) ? firstChannel : TexturePaintChannel.Albedo;
                rasterized.paintSettings = new TexturePaintLayerSettings
                {
                    channel = paintChannel,
                    source = TexturePaintBrushSource.Color,
                    destination = TexturePaintSourceMode.SourceOverlay,
                    color = DefaultChannelSourceColor(paintChannel)
                };
                foreach (KeyValuePair<TexturePaintChannel, TexturePaintLayerChannelSettings> pair in
                         rasterized.channelSettings)
                {
                    pair.Value.sourceSettings = new TexturePaintChannelSourceSettings
                    {
                        source = TexturePaintBrushSource.Color,
                        color = DefaultChannelSourceColor(pair.Key),
                        normalConvention = normalConvention
                    };
                }
                if (rasterized.layerMask?.target != null && maskPixels != null)
                {
                    rasterized.layerMask.target.Reset(maskPixels, Color.white);
                    rasterized.layerMask.baseValue = 1f;
                    rasterized.layerMask.effects = new TexturePaintLayerMaskEffects();
                }
                rasterized.NormalizeKindPayload();
                before.Add(new LayerLocation { set = peer.textureSet, layer = peer.layer, index = index });
                after.Add(new LayerLocation { set = peer.textureSet, layer = rasterized, index = index });
            }

            for (int i = 0; i < before.Count; i++)
                SwapLayerSnapshot(before[i].set, before[i].layer, after[i].layer, before[i].index);
            PushLightweightCommand("Rasterize Fill Layer",
                () =>
                {
                    for (int i = 0; i < before.Count; i++)
                        SwapLayerSnapshot(before[i].set, after[i].layer, before[i].layer,
                            before[i].index);
                },
                () =>
                {
                    for (int i = 0; i < before.Count; i++)
                        SwapLayerSnapshot(before[i].set, before[i].layer, after[i].layer,
                            after[i].index);
                },
                () =>
                {
                    for (int i = 0; i < before.Count; i++)
                    {
                        DisposeLayerIfDetached(before[i].set, before[i].layer);
                        DisposeLayerIfDetached(after[i].set, after[i].layer);
                    }
                });
            MarkDocumentDirtyAfterStructuralChange();
            ShowWorkspaceStatus($"Rasterized '{layer.name}' to an editable Paint layer.");
            return true;
        }

        private void RestoreFillSourceControls(TexturePaintLayer layer)
        {
            layer?.NormalizeKindPayload();
            if (layer?.fillSettings == null) return;
            paintSource = layer.fillSettings.source;
            normalConvention = layer.fillSettings.normalConvention;
            RestorePaintSource(layer.fillSettings.sourceTexture, layer.fillSettings.sourceSprite);
            paintSourceOverlay = layer.fillSettings.sourceOverlay;
            paintColor = layer.fillSettings.color;
        }

        private void DuplicateLayerWithHistory(TextureSet set, int index)
        {
            if (set == null || (uint)index >= (uint)set.layers.Count) return;
            TexturePaintLayer source = set.layers[index];
            if (!TryResolveLogicalPeers(set, source, out List<TexturePaintLogicalLayerMember> peers, out string error))
            { ShowWorkspaceStatus(error); return; }
            string targetId = source.paintTargetId;
            var created = new List<LayerLocation>();
            var logicalIdsByOrdinal = new List<string>();
            for (int i = 0; i < peers.Count; i++)
            {
                TexturePaintLogicalLayerMember peer = peers[i];
                int peerIndex = peer.textureSet.layers.IndexOf(peer.layer);
                var existing = new HashSet<TexturePaintLayer>(peer.textureSet.layers);
                TexturePaintLayer copy = peer.textureSet.DuplicateLayerAt(peerIndex);
                if (copy == null)
                {
                    DetachLayerLocations(created);
                    for (int dispose = 0; dispose < created.Count; dispose++) created[dispose].layer.Dispose();
                    return;
                }
                var newBlock = new List<TexturePaintLayer>();
                for (int layerIndex = 0; layerIndex < peer.textureSet.layers.Count; layerIndex++)
                    if (!existing.Contains(peer.textureSet.layers[layerIndex]))
                        newBlock.Add(peer.textureSet.layers[layerIndex]);
                if (i == 0)
                    for (int ordinal = 0; ordinal < newBlock.Count; ordinal++)
                        logicalIdsByOrdinal.Add(Guid.NewGuid().ToString("N"));
                if (newBlock.Count != logicalIdsByOrdinal.Count)
                {
                    DetachLayerLocations(created);
                    for (int dispose = 0; dispose < created.Count; dispose++)
                        created[dispose].layer.Dispose();
                    for (int dispose = 0; dispose < newBlock.Count; dispose++)
                    { peer.textureSet.layers.Remove(newBlock[dispose]); newBlock[dispose].Dispose(); }
                    ShowWorkspaceStatus("The group subtree differs across logical target members.");
                    return;
                }
                for (int ordinal = 0; ordinal < newBlock.Count; ordinal++)
                {
                    TexturePaintLayer createdLayer = newBlock[ordinal];
                    createdLayer.logicalLayerId = peers.Count > 1 || !string.IsNullOrEmpty(targetId)
                        ? logicalIdsByOrdinal[ordinal] : null;
                    createdLayer.paintTargetId = targetId;
                    created.Add(new LayerLocation { set = peer.textureSet, layer = createdLayer,
                        index = peer.textureSet.layers.IndexOf(createdLayer) });
                }
                peer.textureSet.activeLayerIndex = peer.textureSet.layers.IndexOf(copy);
            }
            RegisterCreatedLayers(created, "Duplicate Texture Layer");
            MarkDocumentDirtyAfterStructuralChange();
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
                if (!upperPeer.textureSet.CanMergeLayerDown(peerUpperIndex, out string mergeReason))
                { ShowWorkspaceStatus(mergeReason); return false; }
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
                    {
                        DisposeLayerIfDetached(states[i].set, states[i].lower);
                        DisposeLayerIfDetached(states[i].set, states[i].upper);
                        DisposeLayerIfDetached(states[i].set, states[i].merged);
                    }
                });
            MarkDocumentDirtyAfterStructuralChange();
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

            // Logical targets own separate physical copies of a group, so collect the matching
            // group IDs for every texture set before removing anything. Child parentId values are
            // local to their texture set and cannot be compared directly with the primary group.
            var groupRootsBySet = new Dictionary<TextureSet, HashSet<string>>();
            for (int setIndex = 0; setIndex < controller.Textures.Sets.Count; setIndex++)
            {
                TextureSet set = controller.Textures.Sets[setIndex];
                for (int layerIndex = 0; layerIndex < set.layers.Count; layerIndex++)
                {
                    TexturePaintLayer candidate = set.layers[layerIndex];
                    bool logicalMatch = !string.IsNullOrEmpty(logicalLayerId) &&
                        string.Equals(candidate.logicalLayerId, logicalLayerId, StringComparison.Ordinal) &&
                        string.Equals(candidate.paintTargetId, paintTargetId, StringComparison.Ordinal);
                    bool remove = ReferenceEquals(candidate, primary) || logicalMatch || (!string.IsNullOrEmpty(groupKey) &&
                        string.Equals(candidate.proceduralGroupKey, groupKey, StringComparison.Ordinal));
                    if (!remove || candidate.kind != TexturePaintLayerKind.Group) continue;
                    if (!groupRootsBySet.TryGetValue(set, out HashSet<string> roots))
                    {
                        roots = new HashSet<string>();
                        groupRootsBySet.Add(set, roots);
                    }
                    roots.Add(candidate.id);
                }
            }
            for (int setIndex = 0; setIndex < controller.Textures.Sets.Count; setIndex++)
            {
                TextureSet set = controller.Textures.Sets[setIndex];
                groupRootsBySet.TryGetValue(set, out HashSet<string> groupRoots);
                var layersToRemove = new HashSet<TexturePaintLayer>();
                // Resolve the whole descendant tree before detaching the group. Detaching a
                // nested parent first would otherwise break the parentId walk for its children.
                for (int layerIndex = 0; layerIndex < set.layers.Count; layerIndex++)
                {
                    TexturePaintLayer candidate = set.layers[layerIndex];
                    bool logicalMatch = !string.IsNullOrEmpty(logicalLayerId) &&
                        string.Equals(candidate.logicalLayerId, logicalLayerId, StringComparison.Ordinal) &&
                        string.Equals(candidate.paintTargetId, paintTargetId, StringComparison.Ordinal);
                    bool remove = ReferenceEquals(candidate, primary) || logicalMatch || (!string.IsNullOrEmpty(groupKey) &&
                        string.Equals(candidate.proceduralGroupKey, groupKey, StringComparison.Ordinal));
                    if (remove || IsDescendantOfGroup(set, candidate, groupRoots))
                        layersToRemove.Add(candidate);
                }
                for (int layerIndex = set.layers.Count - 1; layerIndex >= 0; layerIndex--)
                {
                    TexturePaintLayer candidate = set.layers[layerIndex];
                    if (!layersToRemove.Contains(candidate)) continue;
                    removed.Add(new LayerLocation { set = set, layer = candidate, index = layerIndex });
                    DetachLayer(set, candidate, false);
                }
            }
            RefreshLayerLocationSets(removed);
            PushLightweightCommand("Delete Texture Layer",
                () => AttachLayerLocations(removed),
                () => DetachLayerLocations(removed),
                () =>
                {
                    for (int i = 0; i < removed.Count; i++)
                        DisposeLayerIfDetached(removed[i].set, removed[i].layer);
                });
            if (primary.spline != null) splineDisplayCache?.Remove(primary.spline);
            MarkDocumentDirtyAfterStructuralChange();
        }

        private static bool IsDescendantOfGroup(TextureSet set, TexturePaintLayer layer,
            HashSet<string> groupRoots)
        {
            if (set == null || layer == null || groupRoots == null || groupRoots.Count == 0) return false;
            string parentId = layer.parentId;
            int guard = 0;
            while (!string.IsNullOrEmpty(parentId) && guard++ < set.layers.Count)
            {
                if (groupRoots.Contains(parentId)) return true;
                TexturePaintLayer parent = FindLayerById(set, parentId);
                if (parent == null) break;
                parentId = parent.parentId;
            }
            return false;
        }

        private static string GetLayerDeletionConfirmation(TextureSet set, TexturePaintLayer layer)
        {
            if (layer == null) return "Delete this layer? You can restore it with Undo.";
            if (layer.kind != TexturePaintLayerKind.Group)
                return $"Delete '{layer.name}'? You can restore it with Undo.";
            int childCount = CountGroupDescendants(set, layer);
            if (childCount == 0)
                return $"Delete empty group '{layer.name}'? You can restore it with Undo.";
            return $"Delete group '{layer.name}' and all {childCount} child " +
                (childCount == 1 ? "layer" : "layers") + "? You can restore them with Undo.";
        }

        private static int CountGroupDescendants(TextureSet set, TexturePaintLayer group)
        {
            if (set == null || group == null || group.kind != TexturePaintLayerKind.Group) return 0;
            var groupRoots = new HashSet<string> { group.id };
            int children = 0;
            for (int i = 0; i < set.layers.Count; i++)
                if (!ReferenceEquals(set.layers[i], group) &&
                    IsDescendantOfGroup(set, set.layers[i], groupRoots)) children++;
            return children;
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

        private TextureSet FindContainingSet(TexturePaintLayer layer)
        {
            if (layer == null || controller?.Textures == null) return null;
            for (int i = 0; i < controller.Textures.Sets.Count; i++)
                if (controller.Textures.Sets[i].layers.Contains(layer)) return controller.Textures.Sets[i];
            return null;
        }

        private static void DisposeLayerIfDetached(TextureSet set, TexturePaintLayer layer)
        {
            if (layer != null && (set == null || !set.layers.Contains(layer))) layer.Dispose();
        }

        private static void DetachLayer(TextureSet set, TexturePaintLayer layer, bool refresh = true)
        {
            if (set == null || layer == null) return;
            int index = set.layers.IndexOf(layer);
            if (index < 0) return;
            set.layers.RemoveAt(index);
            if (set.layers.Count == 0) set.activeLayerIndex = -1;
            else set.activeLayerIndex = Mathf.Clamp(index - 1, 0, set.layers.Count - 1);
            if (refresh) set.BindPreviewTextures();
        }

        private static void AttachLayer(TextureSet set, TexturePaintLayer layer, int index,
            bool refresh = true)
        {
            if (set == null || layer == null || set.layers.Contains(layer)) return;
            int insert = Mathf.Clamp(index, 0, set.layers.Count);
            set.layers.Insert(insert, layer);
            set.activeLayerIndex = insert;
            if (refresh) set.BindPreviewTextures();
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
            if (index < 0) return;
            set.layers.RemoveAt(index);
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
                AttachLayer(locations[i].set, locations[i].layer, locations[i].index, false);
            RefreshLayerLocationSets(locations);
        }

        private static void DetachLayerLocations(List<LayerLocation> locations)
        {
            for (int i = locations.Count - 1; i >= 0; i--)
                DetachLayer(locations[i].set, locations[i].layer, false);
            RefreshLayerLocationSets(locations);
        }

        private static void RefreshLayerLocationSets(List<LayerLocation> locations)
        {
            var refreshed = new HashSet<TextureSet>();
            for (int i = 0; locations != null && i < locations.Count; i++)
                if (locations[i]?.set != null && refreshed.Add(locations[i].set))
                    locations[i].set.BindPreviewTextures();
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
                () => ApplyPathState(pending.set, pending.layer, pending.after), null,
                "path:" + pending.layer.id + ":" + pending.label);
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
            RequestSplineReapply(set, false);
        }

        private static TexturePaintSpline CloneSpline(TexturePaintSpline source)
        {
            return source == null ? null : JsonUtility.FromJson<TexturePaintSpline>(JsonUtility.ToJson(source));
        }
    }
}
