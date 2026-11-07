using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        [NonSerialized] private bool regeneratingStaleLayers;
        [NonSerialized] private int regeneratedStaleLayerCount;

        internal static bool FindStaleGenerator(IReadOnlyList<TextureSet> sets,
            out TextureSet set, out TexturePaintLayer layer, out bool mask)
        {
            set = null; layer = null; mask = false;
            if (sets == null) return false;
            foreach (var candidateSet in sets)
            {
                if (candidateSet == null) continue;
                // Layer storage is bottom-to-top, including hidden layers. A commit can replace
                // layer objects and invalidate higher generators, so always inspect live state.
                foreach (var candidate in candidateSet.layers)
                {
                    if (candidate == null) continue;
                    bool content = candidate.kind == TexturePaintLayerKind.Plugin && candidate.pluginStale &&
                        !string.IsNullOrEmpty(candidate.pluginId);
                    bool generatedMask = candidate.layerMask?.pluginStale == true &&
                        !string.IsNullOrEmpty(candidate.layerMask.pluginId);
                    if (!content && !generatedMask) continue;
                    set = candidateSet; layer = candidate; mask = !content;
                    return true;
                }
            }
            return false;
        }

        internal static async Task<int> RegenerateStaleGeneratorsAsync(IReadOnlyList<TextureSet> sets,
            Func<TextureSet, TexturePaintLayer, bool, Task<bool>> regenerate, Func<bool> canContinue)
        {
            int count = 0;
            while (canContinue() && FindStaleGenerator(sets, out var set, out var layer, out bool mask))
            {
                if (!await regenerate(set, layer, mask)) break;
                count++;
                // Guard against a failed/no-op plugin reporting success without clearing stale.
                var live = set.layers.Find(value => value.id == layer.id);
                if (live != null && (mask ? live.layerMask?.pluginStale == true : live.pluginStale)) break;
            }
            return count;
        }

        private void DrawRegenerateStaleLayersButton()
        {
            bool enabled = !regeneratingStaleLayers && !IsPersistenceActive && pluginLayerCancellation == null &&
                !strokeActive && !uvStrokeActive && controller?.Plugins != null &&
                FindStaleGenerator(controller.Textures?.Sets, out _, out _, out _);
            using (new EditorGUI.DisabledScope(!enabled))
                if (GUILayout.Button(new GUIContent("Regenerate all stale layers",
                    "Regenerate all stale plugin layers and generated masks in this document, including hidden layers."), EditorStyles.miniButton))
                    RegenerateAllStaleLayers();
            if (!regeneratingStaleLayers) return;
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 18f), Mathf.Clamp01(pluginLayerProgress),
                $"Regenerating — {regeneratedStaleLayerCount} completed");
            if (GUILayout.Button("Cancel Regeneration", EditorStyles.miniButton))
                pluginLayerCancellation?.Cancel();
        }

        private async void RegenerateAllStaleLayers()
        {
            if (regeneratingStaleLayers || IsPersistenceActive || pluginLayerCancellation != null ||
                controller?.Textures == null || controller.Plugins == null) return;
            var owner = controller;
            regeneratingStaleLayers = true;
            regeneratedStaleLayerCount = 0;
            try
            {
                await RegenerateStaleGeneratorsAsync(owner.Textures.Sets, async (set, layer, mask) =>
                {
                    var id = mask ? layer.layerMask.pluginId : layer.pluginId;
                    var plugin = owner.Plugins.FindCommand(id);
                    if (plugin == null)
                    {
                        ShowWorkspaceStatus("Cannot regenerate " + layer.name + ": plugin is unavailable (" + id + ")");
                        return false;
                    }
                    bool succeeded = mask ? await RegenerateLayerMaskPluginAsync(set, layer, plugin)
                        : await RegeneratePluginLayerAsync(set, layer, plugin);
                    if (succeeded) regeneratedStaleLayerCount++;
                    return succeeded;
                }, () => this != null && ReferenceEquals(controller, owner) && !IsPersistenceActive);
                if (ReferenceEquals(controller, owner) && !FindStaleGenerator(owner.Textures.Sets, out _, out _, out _))
                    ShowWorkspaceStatus($"Regenerated {regeneratedStaleLayerCount} stale layer outputs");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowWorkspaceStatus("Stale-layer regeneration stopped; remaining layers are still stale");
            }
            finally
            {
                regeneratingStaleLayers = false;
                RepaintAll();
            }
        }
    }
}
