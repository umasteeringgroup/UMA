using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.TexturePaint.Editor
{
    internal enum TexturePaintLayerPreviewKind { None, CachedLayer, Plugin, MaskPlugin, GarmentPath, GarmentProjection, Brush }

    public sealed partial class TexturePaintStageWindow
    {
        [NonSerialized] private TexturePaintSamplePreview cachedLayerPreview;

        internal static TexturePaintLayerPreviewKind LayerPreviewKind(TexturePaintLayer layer, bool mask, bool brush)
        {
            if (layer == null) return brush ? TexturePaintLayerPreviewKind.Brush : TexturePaintLayerPreviewKind.None;
            if (mask && !string.IsNullOrEmpty(layer.layerMask?.pluginId)) return TexturePaintLayerPreviewKind.MaskPlugin;
            if (brush) return TexturePaintLayerPreviewKind.Brush;
            if (mask) return TexturePaintLayerPreviewKind.CachedLayer;
            if (layer.kind == TexturePaintLayerKind.Plugin) return TexturePaintLayerPreviewKind.Plugin;
            if (layer.IsSplineLayer && (layer.splineSettings?.pathGenerator?.enabled == true || layer.splineSettings?.garment?.enabled == true || layer.splineSettings?.hemSeam?.enabled == true))
                return TexturePaintLayerPreviewKind.GarmentPath;
            if (layer.kind == TexturePaintLayerKind.Projection && layer.projectionSettings?.garment?.enabled == true)
                return TexturePaintLayerPreviewKind.GarmentProjection;
            return TexturePaintLayerPreviewKind.CachedLayer;
        }

        internal void DrawLayerPreviewPanel()
        {
            bool changed = GUI.changed;
            try
            {
                DrawLayerPreviewContent();
                DrawRegenerateStaleLayersButton();
                if (GUILayout.Button("Edit Preview Material", EditorStyles.miniButton))
                    TexturePaintMaterialWindow.ShowWindow();
            }
            finally { GUI.changed = changed; }
        }

        private void DrawLayerPreviewContent()
        {
            bool changed = GUI.changed;
            try
            {
                TextureSet set = ActiveTextureSet;
                TexturePaintLayer layer = set != null && (uint)set.activeLayerIndex < (uint)set.layers.Count ? set.layers[set.activeLayerIndex] : null;
                bool mask = set != null && IsLayerMaskMode(set);
                bool brush = tool == TexturePaintTool.Plugin && controller?.Plugins?.Brushes.Count > 0;
                switch (LayerPreviewKind(layer, mask, brush))
                {
                    case TexturePaintLayerPreviewKind.Brush:
                        RequestBrushPluginPreview(controller.Plugins.Brushes[Mathf.Clamp(selectedBrushPlugin, 0, controller.Plugins.Brushes.Count - 1)]);
                        brushPluginPreview.Draw(); return;
                    case TexturePaintLayerPreviewKind.Plugin:
                    case TexturePaintLayerPreviewKind.MaskPlugin:
                        var plugin = controller.Plugins.FindCommand(mask ? layer.layerMask.pluginId : layer.pluginId);
                        if (TexturePaintPluginPreview.Supports(plugin))
                        {
                            RequestPluginPreview(set, layer, plugin, mask ? layer.layerMask.pluginParameters : layer.pluginParameters, mask);
                            string progressKey = (mask ? "mask:" : string.Empty) +
                                (!string.IsNullOrEmpty(layer.logicalLayerId) ? layer.logicalLayerId : layer.id);
                            float? regenerationProgress = pluginLayerCancellation != null &&
                                string.Equals(runningPluginLayerId, progressKey, StringComparison.Ordinal)
                                ? pluginLayerProgress : (float?)null;
                            pluginPreview.Draw(() =>
                            {
                                if (mask) RegenerateLayerMaskPlugin(set, layer, plugin);
                                else RegeneratePluginLayer(set, layer, plugin);
                            }, pluginLayerCancellation == null && !IsPersistenceActive, regenerationProgress);
                            return;
                        }
                        break;
                    case TexturePaintLayerPreviewKind.GarmentPath:
                        float width = Mathf.Max(.0002f, layer.splineSettings.brushSize * 2);
                        RequestGarmentSectionPreview(set, layer, layer.splineSettings.garment, layer.splineSettings.hemSeam,
                            new Vector2(width, width * 3), layer.spline?.closed == true,layer.splineSettings.pathGenerator);
                        garmentPreview.Draw(layer.splineSettings.pathGenerator?.enabled==true ?
                            () => { QueueSplineReapply(set);ReapplyPendingSpline(); } : null, !IsPersistenceActive);
                        return;
                    case TexturePaintLayerPreviewKind.GarmentProjection:
                        RequestGarmentSectionPreview(set, layer, layer.projectionSettings.garment, null,
                            new Vector2(layer.projectionSettings.width, layer.projectionSettings.height), false);
                        garmentPreview.Draw(); return;
                }
                if (layer != null) { DrawCachedLayerPreview(set, layer, mask); return; }
                EditorGUILayout.HelpBox("Select a layer to preview its current output.", MessageType.None);
            }
            finally { GUI.changed = changed; }
        }

        private void DrawCachedLayerPreview(TextureSet set, TexturePaintLayer layer, bool mask)
        {
            var sources = new Dictionary<TexturePaintChannel, RenderTexture>();
            if (mask && layer.layerMask?.target?.Front != null) sources[TexturePaintChannel.Custom] = layer.layerMask.target.Front;
            else if (!mask)
                foreach (var pair in layer.channels)
                    if (pair.Value?.Front != null) sources[pair.Key] = pair.Value.Front;
            if (sources.Count == 0)
            {
                EditorGUILayout.HelpBox("This layer has no generated or painted output to preview.", MessageType.None); return;
            }
            cachedLayerPreview ??= new TexturePaintSamplePreview(RepaintAll);
            string key = set.persistentId + layer.id + mask + documentChangeVersion +
                string.Join(":", layer.channels.Select(p => p.Key + "=" + p.Value.Revision));
            cachedLayerPreview.Request(key, () => ReadLayerPreview(sources), "Current layer output. Generator previews show draft changes before regeneration.", mask);
            cachedLayerPreview.Draw();
        }

        private static Dictionary<TexturePaintChannel, Color[]> ReadLayerPreview(Dictionary<TexturePaintChannel, RenderTexture> sources)
        {
            var result = new Dictionary<TexturePaintChannel, Color[]>();
            var target = RenderTexture.GetTemporary(128, 128, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(128, 128, TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            try
            {
                foreach (var pair in sources)
                {
                    Graphics.Blit(pair.Value, target); RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); readback.Apply(false, false);
                    result[pair.Key] = readback.GetPixels();
                }
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(readback); }
            return result;
        }
    }

    [Overlay(typeof(SceneView), "Layer Preview", false, defaultDockZone = DockZone.Floating)]
    public sealed class TexturePaintLayerPreviewOverlay : Overlay
    {
        private static readonly HashSet<TexturePaintLayerPreviewOverlay> Instances = new HashSet<TexturePaintLayerPreviewOverlay>();
        private bool contextWasActive;
        public override VisualElement CreatePanelContent()
        {
            var panel = new IMGUIContainer(() => TexturePaintStageWindow.ActiveStage?.DrawLayerPreviewPanel());
            panel.style.width = 380; panel.style.paddingLeft = panel.style.paddingRight = 6;
            panel.style.paddingTop = panel.style.paddingBottom = 6;
            return panel;
        }
        public override void OnCreated()
        {
            base.OnCreated(); Instances.Add(this);
            contextWasActive = IsContextActive(); displayed = contextWasActive;
            EditorApplication.update += UpdateContextVisibility;
        }
        public override void OnWillBeDestroyed()
        {
            EditorApplication.update -= UpdateContextVisibility; Instances.Remove(this); base.OnWillBeDestroyed();
        }
        private bool IsContextActive()
        {
            var stage = TexturePaintStageWindow.ActiveStage;
            return stage != null && ReferenceEquals(StageUtility.GetCurrentStage(), stage) &&
                TexturePaintSceneOverlayVisibility.ShouldDisplayOn(containerWindow);
        }
        private void UpdateContextVisibility()
        {
            bool active = IsContextActive();
            if (active == contextWasActive) return;
            contextWasActive = active; displayed = active;
        }
        internal static void RefreshAllInstances() { foreach (var overlay in Instances) overlay.UpdateContextVisibility(); }
        internal static void HideAllInstances()
        { foreach (var overlay in Instances) { overlay.contextWasActive = false; overlay.displayed = false; } }
    }
}
