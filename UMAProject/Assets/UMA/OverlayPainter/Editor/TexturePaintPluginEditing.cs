using System;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow
    {
        [NonSerialized] private TexturePaintPluginPreview pluginPreview;
        [NonSerialized] private TexturePaintSamplePreview garmentPreview, brushPluginPreview;
        internal TextureSet PluginPreviewSet => ActiveTextureSet;
        internal long PluginPreviewVersion => documentChangeVersion;
        internal BrushPreset PluginPreviewBrush => ActiveBrush;
        internal bool UsesPluginPreviewController(TexturePaintStageController value) => ReferenceEquals(controller, value);

        private void RequestBrushPluginPreview(ITexturePaintBrushV2 plugin)
        {
            brushPluginPreview ??= new TexturePaintSamplePreview(RepaintAll);
            var parameters = controller.Plugins.GetParameters(plugin).Clone();
            BrushPreset preset = ActiveBrush;
            var channel = selectedChannel;
            Color color = paintColor;
            string key = plugin.Descriptor.id + JsonUtility.ToJson(parameters) + JsonUtility.ToJson(preset) + channel + color;
            brushPluginPreview.Request(key, () => TexturePaintBrushPreview.Generate(plugin, parameters, preset, channel, color),
                "Sample stroke framed to brush size. Shows spacing, coverage and variation.");
        }

        private void RequestGarmentSectionPreview(TextureSet set, TexturePaintLayer layer,
            TexturePaintGarmentSettings garment, TexturePaintHemSeamSettings hem, Vector2 size, bool closed)
        {
            if (controller?.Textures == null) return;
            garmentPreview ??= new TexturePaintSamplePreview(RepaintAll, TexturePaintPreviewView.LitSurface);
            var garmentSnapshot = garment?.Clone(); var hemSnapshot = hem?.Clone();
            string key = set.persistentId + layer.id + documentChangeVersion +
                (garmentSnapshot == null ? "" : JsonUtility.ToJson(garmentSnapshot)) +
                (hemSnapshot == null ? "" : JsonUtility.ToJson(hemSnapshot)) + size.ToString("R") + closed;
            garmentPreview.Request(key, () => TexturePaintGarmentPreview.Generate(garmentSnapshot, hemSnapshot, size,
                closed, set, controller.Textures.Sets, layer),
                "Straight sample section. Scene view shows the path's curvature and placement.");
        }

        private void RequestPluginPreview(TextureSet set, TexturePaintLayer layer,
            ITexturePaintCommandExtensionV2 plugin, TexturePaintPluginParameterSet parameters, bool mask = false)
        {
            if (!TexturePaintPluginPreview.Supports(plugin)) return;
            pluginPreview ??= new TexturePaintPluginPreview(RepaintAll);
            pluginPreview.Request(controller.Textures, set, layer, plugin, parameters ?? new TexturePaintPluginParameterSet(), mask, documentChangeVersion);
        }

        internal static TexturePaintPluginParameterDefinition PluginFillMappingParameter(TexturePaintPluginDescriptor descriptor)
        {
            // Only promote a mapping parameter the plugin actually implements. Image
            // filters and UV-only generators cannot inherit a Fill layer's projection.
            return descriptor?.parameters?.Find(parameter => parameter != null && parameter.id == "projection" &&
                parameter.type == TexturePaintPluginParameterType.Enum && parameter.enumOptions != null &&
                Array.Exists(parameter.enumOptions, option => option != null &&
                    option.IndexOf("triplanar", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static void DrawPluginFillMapping(TexturePaintPluginParameterSet parameters,
            TexturePaintPluginParameterDefinition mapping)
        {
            if (mapping == null) return;
            var value = parameters.Get(mapping.id, true);
            var names = new string[mapping.enumOptions.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string option = mapping.enumOptions[i] ?? string.Empty;
                names[i] = string.Equals(option, "UV", StringComparison.OrdinalIgnoreCase)
                    ? "Flat (UV)" : option.IndexOf("triplanar", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "Triplanar (World)" : option;
            }
            value.number = EditorGUILayout.Popup(new GUIContent("Fill Type",
                "Flat follows the mesh UVs. Triplanar uses world coordinates. Click Regenerate after changing the mapping."),
                Mathf.Clamp(Mathf.RoundToInt(value.number), 0, names.Length - 1), names);
        }
    }
}
