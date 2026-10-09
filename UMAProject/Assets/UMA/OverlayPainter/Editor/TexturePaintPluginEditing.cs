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

        private void ShowPluginLayerMenu(TextureSet set)
        {
            if (set == null) return;
            var menu = new GenericMenu();
            var plugins = controller?.Plugins;
            var cloth = plugins?.FindCommand("com.uma.texturepaint.cloth-texture");
            if (cloth != null)
            {
                menu.AddItem(new GUIContent("Fabric/Surface Detail (Keep Color)"), false,
                    () => AddPluginLayer(set, cloth, true));
                menu.AddItem(new GUIContent("Fabric/Cloth Texture (Color and Weave)"), false,
                    () => AddPluginLayer(set, cloth));
            }
            var textile = plugins?.FindCommand("com.uma.texturepaint.textile-surface");
            if (textile != null)
            {
                var modes = textile.Descriptor.parameters.Find(p => p.id == "mode").enumOptions;
                for (int i = 0; i < modes.Length; i++)
                {
                    int mode = i;
                    menu.AddItem(new GUIContent("Fabric/" + modes[i]), false,
                        () => AddPluginLayer(set, textile, textileMode: mode));
                }
            }
            if (plugins != null)
                foreach (var plugin in plugins.Commands)
                {
                    // Keep the legacy implementation available for saved layers, but create new scars as paths.
                    if (plugin.Descriptor.id == "com.uma.texturepaint.scar-wound") continue;
                    if ((plugin.Descriptor.supportedTargets & TexturePaintPluginTarget.LayerContent) == 0) continue;
                    var selected = plugin;
                    string category = plugin is ITexturePaintGeneratorV2 ? "Generators/" : "Filters/";
                    menu.AddItem(new GUIContent(category + plugin.Descriptor.displayName), false,
                        () => AddPluginLayer(set, selected));
                }
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Empty Plugin Layer"), false, () => AddPluginLayer(set));
            menu.ShowAsContext();
        }

        internal static void ConfigureFabricSurfaceDetail(TexturePaintLayer layer,
            TexturePaintPluginParameterSet parameters)
        {
            parameters.Get("outputAlbedo", true).boolean = false;
            parameters.Get("outputRoughness", true).boolean = true;
            parameters.Get("outputNormalControl", true).boolean = true;
            parameters.Get("roughness", true).number = .82f;
            parameters.Get("heightStrength", true).number = .035f;
            parameters.Get("fiberHeight", true).number = .012f;
            // Neutral gray leaves existing quilt/fold height intact; small weave variations
            // add relief around it instead of replacing the whole height field.
            layer.GetChannelSettings(TexturePaintChannel.NormalControl).blendMode = TexturePaintBlendMode.Overlay;
        }

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
            TexturePaintGarmentSettings garment, TexturePaintHemSeamSettings hem, Vector2 size, bool closed,
            TexturePaintPathGeneratorSettings pathGenerator = null)
        {
            if (controller?.Textures == null) return;
            garmentPreview ??= new TexturePaintSamplePreview(RepaintAll, TexturePaintPreviewView.LitSurface);
            var garmentSnapshot = garment?.Clone(); var hemSnapshot = hem?.Clone();
            var pathSnapshot=pathGenerator?.Clone();
            string key = set.persistentId + layer.id + documentChangeVersion +
                (garmentSnapshot == null ? "" : JsonUtility.ToJson(garmentSnapshot)) +
                (hemSnapshot == null ? "" : JsonUtility.ToJson(hemSnapshot)) +
                (pathSnapshot == null ? "" : JsonUtility.ToJson(pathSnapshot)) + size.ToString("R") + closed + JsonUtility.ToJson(layer.effects);
            garmentPreview.Request(key, () => TexturePaintGarmentPreview.Generate(garmentSnapshot, hemSnapshot, size,
                closed, set, controller.Textures.Sets, layer, pathSnapshot),
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
