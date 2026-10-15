using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        private void DrawOriginalOverlayRow(TextureSet set)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                bool visible = !set.hideOriginalOverlay;
                bool next = GUILayout.Toggle(visible, visible ? "ON" : "OFF",
                    EditorStyles.miniButton, GUILayout.Width(38f));
                GUILayout.Label(new GUIContent("Original Overlay (base)",
                    "The reconstructed original overlays and any direct Base Texture edits. Turn off to start on transparency; source assets are preserved."));
                if (next != visible) ChangeOriginalOverlayVisibility(set, next);
            }
        }

        private void ChangeOriginalOverlayVisibility(TextureSet activeSet, bool visible)
        {
            if (activeSet == null || activeSet.hideOriginalOverlay == !visible) return;
            var previous = new Dictionary<TextureSet, bool>();
            // A logical UDIM target uses one authoring decision for all its member surfaces.
            if (controller?.Textures != null)
                foreach (var set in controller.Textures.Sets)
                    if (IsSurfaceSelected(set.surface)) previous[set] = set.hideOriginalOverlay;
            if (previous.Count == 0) previous[activeSet] = activeSet.hideOriginalOverlay;
            void Apply(bool restore)
            {
                foreach (var pair in previous)
                {
                    pair.Key.hideOriginalOverlay = restore ? pair.Value : !visible;
                    foreach (var layer in pair.Key.layers)
                        if (layer.kind == TexturePaintLayerKind.Plugin) layer.pluginStale = true;
                    pair.Key.BindPreviewTextures();
                }
                ApplyWorkspaceDisplay();
                RepaintAll();
            }
            Apply(false);
            PushLightweightCommand("Toggle Original Overlay", () => Apply(true), () => Apply(false));
            SetDocumentDirtyFlags();
        }
    }
}
