using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        private const int MaterialPresetPickerControlId = 0x54505052;
        [NonSerialized] private bool materialPresetPickerOpen;
        [NonSerialized] private TextureSet materialPresetDestination;
        [NonSerialized] private CancellationTokenSource materialPresetCancellation;

        private sealed class MaterialPresetProgress : IProgress<float>
        {
            private readonly Action<float> report;
            public MaterialPresetProgress(Action<float> report) => this.report = report;
            public void Report(float value) => report?.Invoke(value);
        }

        private void SaveWholeStackAsMaterialPreset(TextureSet set)
        {
            if (set == null || set.layers.Count == 0)
            {
                ShowWorkspaceStatus("There are no layers to save as a Material Preset.");
                return;
            }
            SaveMaterialPreset(set, new List<TexturePaintLayer>(set.layers), true);
        }

        private void SaveLayerAsMaterialPreset(TextureSet set, TexturePaintLayer root)
        {
            if (set == null || root == null || !set.layers.Contains(root)) return;
            List<TexturePaintLayer> layers = root.kind == TexturePaintLayerKind.Group
                ? CollectMaterialPresetSubtree(set, root)
                : new List<TexturePaintLayer> { root };
            SaveMaterialPreset(set, layers, false);
        }

        private static List<TexturePaintLayer> CollectMaterialPresetSubtree(TextureSet set,
            TexturePaintLayer root)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(root?.id)) ids.Add(root.id);
            bool expanded;
            do
            {
                expanded = false;
                for (int i = 0; i < set.layers.Count; i++)
                {
                    TexturePaintLayer candidate = set.layers[i];
                    if (candidate == null || string.IsNullOrEmpty(candidate.parentId) ||
                        !ids.Contains(candidate.parentId) || string.IsNullOrEmpty(candidate.id) ||
                        !ids.Add(candidate.id)) continue;
                    expanded = true;
                }
            } while (expanded);
            return set.layers.Where(layer => ReferenceEquals(layer, root) ||
                (!string.IsNullOrEmpty(layer?.id) && ids.Contains(layer.id))).ToList();
        }

        private void SaveMaterialPreset(TextureSet set, IReadOnlyList<TexturePaintLayer> layers,
            bool wholeStack)
        {
            string suggested = wholeStack ? set.Name + " Material Preset" :
                layers.Count > 0 ? layers[layers.Count - 1].name + " Material Preset" :
                "Overlay Painter Material Preset";
            foreach (char invalid in Path.GetInvalidFileNameChars()) suggested = suggested.Replace(invalid, '_');
            string path = EditorUtility.SaveFilePanelInProject("Save Overlay Painter Material Preset",
                suggested, "asset", "Choose where to save the reusable layer stack.", "Assets");
            if (string.IsNullOrEmpty(path)) return;

            TexturePaintMaterialPreset preset =
                AssetDatabase.LoadAssetAtPath<TexturePaintMaterialPreset>(path);
            UnityEngine.Object occupied = AssetDatabase.LoadMainAssetAtPath(path);
            if (occupied != null && preset == null)
            {
                EditorUtility.DisplayDialog("Save Material Preset",
                    "Another asset already exists at that path.", "OK");
                return;
            }
            bool created = preset == null;
            if (!created && !EditorUtility.DisplayDialog("Update Material Preset",
                    $"Replace the saved stack in '{preset.name}' with the current selection?",
                    "Update", "Cancel")) return;

            try
            {
                if (created)
                {
                    preset = ScriptableObject.CreateInstance<TexturePaintMaterialPreset>();
                    preset.name = Path.GetFileNameWithoutExtension(path);
                    preset.displayName = preset.name;
                }
                else Undo.RecordObject(preset, "Update Material Preset");
                TexturePaintMaterialPresetStorage.Capture(preset, set, layers, wholeStack,
                    controller?.Plugins, true);
                if (created) AssetDatabase.CreateAsset(preset, path);
                EditorUtility.SetDirty(preset);
                AssetDatabase.SaveAssetIfDirty(preset);
                Selection.activeObject = preset;
                EditorGUIUtility.PingObject(preset);
                ShowWorkspaceStatus($"Saved Material Preset '{preset.displayName}' with {preset.layers.Count} layers");
            }
            catch (Exception exception)
            {
                if (created && preset != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(preset)))
                    DestroyImmediate(preset);
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Save Material Preset", exception.Message, "OK");
            }
        }

        private void OpenMaterialPresetPicker(TextureSet destination)
        {
            if (destination == null || materialPresetCancellation != null) return;
            materialPresetDestination = destination;
            materialPresetPickerOpen = true;
            EditorGUIUtility.ShowObjectPicker<TexturePaintMaterialPreset>(null, false, string.Empty,
                MaterialPresetPickerControlId);
        }

        private void HandleMaterialPresetPickerEvent(Event current)
        {
            if (!materialPresetPickerOpen || current == null) return;
            if (!IsObjectPickerCompletionEvent(current)) return;
            materialPresetPickerOpen = false;
            TexturePaintMaterialPreset preset =
                EditorGUIUtility.GetObjectPickerObject() as TexturePaintMaterialPreset;
            TextureSet destination = materialPresetDestination;
            materialPresetDestination = null;
            if (preset == null) ShowWorkspaceStatus("No Material Preset was selected");
            else ApplyMaterialPreset(destination, preset);
            current.Use();
        }

        private async void ApplyMaterialPreset(TextureSet destination,
            TexturePaintMaterialPreset preset)
        {
            if (destination == null || preset == null || controller?.Textures == null ||
                materialPresetCancellation != null) return;
            TexturePaintLogicalTarget target = ActiveLogicalTarget ??
                controller.LogicalLayers?.FindTarget(destination);
            List<TextureSet> sets = target != null
                ? controller.LogicalLayers.GetTextureSets(target)
                : new List<TextureSet> { destination };
            if (!sets.Contains(destination)) sets.Insert(0, destination);

            TexturePaintMaterialPresetCompatibility compatibility =
                TexturePaintMaterialPresetStorage.Evaluate(preset, sets, controller.Plugins);
            if (!compatibility.CanApply)
            {
                EditorUtility.DisplayDialog("Cannot Apply Material Preset", compatibility.Summary(), "OK");
                return;
            }
            if (compatibility.issues.Count > 0 && !EditorUtility.DisplayDialog(
                    "Apply Material Preset with Warnings",
                    compatibility.Summary() + "\n\nCompatible content will still be applied.",
                    "Apply", "Cancel")) return;

            materialPresetCancellation = new CancellationTokenSource();
            try
            {
                var progress = new MaterialPresetProgress(value =>
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Applying Material Preset",
                            $"Building {preset.displayName ?? preset.name}", Mathf.Clamp01(value)))
                        materialPresetCancellation?.Cancel();
                    RepaintAll();
                });
                TexturePaintMaterialPresetApplyResult result =
                    await TexturePaintMaterialPresetStorage.ApplyAsync(preset, controller.Textures,
                        sets, controller.Plugins, target,
                        new TexturePaintMaterialPresetApplyOptions
                        {
                            wrapInGroup = true,
                            strictChannels = false,
                            strictPlugins = false
                        }, progress, materialPresetCancellation.Token);
                var history = new List<LayerLocation>(result.created.Count);
                for (int i = 0; i < result.created.Count; i++)
                {
                    TexturePaintMaterialPresetCreatedLayer item = result.created[i];
                    if (item?.set == null || item.layer == null || !item.set.layers.Contains(item.layer))
                        continue;
                    history.Add(new LayerLocation
                    {
                        set = item.set,
                        layer = item.layer,
                        index = item.set.layers.IndexOf(item.layer)
                    });
                }
                RegisterCreatedLayers(history, "Apply Material Preset");
                MarkDocumentDirtyAfterStructuralChange();
                SyncActiveLayerSelection(destination);
                string status = $"Applied '{preset.displayName ?? preset.name}' ({preset.layers.Count} layers)";
                if (result.warnings.Count > 0) status += $" with {result.warnings.Count} warning(s)";
                ShowWorkspaceStatus(status);
            }
            catch (OperationCanceledException)
            {
                ShowWorkspaceStatus("Material Preset application cancelled; no layers were added");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Apply Material Preset",
                    "The preset could not be applied. No layers were added.\n\n" + exception.Message,
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                materialPresetCancellation?.Dispose();
                materialPresetCancellation = null;
                RepaintAll();
            }
        }

        private static void DrawMaterialPresetProvenance(TexturePaintLayer layer)
        {
            if (layer == null || string.IsNullOrEmpty(layer.sourceMaterialPresetId)) return;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Material Preset",
                $"Revision {layer.sourceMaterialPresetRevision}");
            if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(54f)))
            {
                string[] guids = AssetDatabase.FindAssets("t:TexturePaintMaterialPreset");
                for (int i = 0; i < guids.Length; i++)
                {
                    TexturePaintMaterialPreset preset =
                        AssetDatabase.LoadAssetAtPath<TexturePaintMaterialPreset>(
                            AssetDatabase.GUIDToAssetPath(guids[i]));
                    if (preset == null || !string.Equals(preset.presetId,
                            layer.sourceMaterialPresetId, StringComparison.Ordinal)) continue;
                    Selection.activeObject = preset;
                    EditorGUIUtility.PingObject(preset);
                    break;
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
