using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public static class TexturePaintStubblePresets
    {
        public const string Folder = "Assets/UMA/OverlayPainter/Presets/Stubble";

        [MenuItem("UMA/Overlay Painter/Create Missing Stubble Presets")]
        public static void CreateMissing()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            using var host = new PluginHost();
            host.Discover();
            for (int i = 0; i < StubbleMakerPresets.Names.Length; i++)
            {
                string path = Folder + "/" + StubbleMakerPresets.Names[i] + ".asset";
                if (!File.Exists(path)) AssetDatabase.CreateAsset(Build(i, host), path);
            }
            AssetDatabase.SaveAssets();
        }

        public static TexturePaintMaterialPreset Build(int index, PluginHost host)
        {
            var plugin = host.FindCommand("com.uma.texturepaint.stubble-maker")
                ?? throw new InvalidOperationException("Stubble Maker is unavailable.");
            var d = plugin.Descriptor;
            var p = host.CreateParameters(plugin);
            StubbleMakerPresets.Apply(p, index);
            var preset = ScriptableObject.CreateInstance<TexturePaintMaterialPreset>();
            preset.displayName = StubbleMakerPresets.Names[index];
            preset.thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + preset.displayName + "-Preview.png");
            preset.category = "Skin / Stubble";
            preset.author = "UMA";
            preset.includesWholeStack = false;
            preset.includesCachedPluginOutput = false;
            preset.description = "Natural curved stubble with coordinated roughness and subtle relief. Restrict to the beard/scalp using a layer or Control Mask. Facial growth follows UMA landmarks when available. Dimensions use a fixed 2K reference; changing output resolution preserves the groom.";
            preset.portability = TexturePaintPresetPortability.RequiresPlugin | TexturePaintPresetPortability.MeshDependent;
            var layer = new TexturePaintDocumentLayer
            {
                name = preset.displayName, kind = TexturePaintLayerKind.Plugin,
                pluginId = d.id, pluginVersion = d.pluginVersion, pluginParameters = p,
                pluginParametersJson = JsonUtility.ToJson(p)
            };
            foreach (var ch in new[] { TexturePaintChannel.Albedo, TexturePaintChannel.Roughness, TexturePaintChannel.NormalControl })
            {
                var saved = new TexturePaintDocumentLayerChannel { channel = ch };
                saved.settings.channel = ch;
                saved.settings.blendMode = ch == TexturePaintChannel.NormalControl ? TexturePaintBlendMode.Overlay : TexturePaintBlendMode.Normal;
                layer.channels.Add(saved);
                preset.channels.Add(new TexturePaintMaterialPresetChannel { channel = ch, required = true });
            }
            preset.layers.Add(layer);
            preset.plugins.Add(new TexturePaintMaterialPresetPlugin { pluginId = d.id, pluginVersion = d.pluginVersion,
                apiVersion = d.apiVersion, declaredChannels = d.declaredChannels, readChannels = d.readChannels,
                requiredMeshMaps = d.requiredMeshMaps, targets = d.supportedTargets });
            return preset;
        }
    }
}
