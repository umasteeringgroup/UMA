using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    [Flags]
    public enum TexturePaintPresetPortability
    {
        Portable = 0,
        UVDependent = 1 << 0,
        MeshDependent = 1 << 1,
        RequiresPlugin = 1 << 2
    }

    [Serializable]
    public sealed class TexturePaintMaterialPresetChannel
    {
        public TexturePaintChannel channel;
        public bool required;
        public TexturePaintPresetPortability portability;
    }

    [Serializable]
    public sealed class TexturePaintMaterialPresetPlugin
    {
        public string pluginId;
        public string pluginVersion;
        public int apiVersion;
        public TexturePaintChannelMask declaredChannels;
        public TexturePaintChannelMask readChannels;
        public TexturePaintMeshMapMask requiredMeshMaps;
        public TexturePaintPluginTarget targets;
    }

    [Serializable]
    public sealed class TexturePaintMaterialPresetPackagedDependency
    {
        public string name;
        public string type;
        public string sourceGuid;
        public long sourceLocalId;
    }

    /// <summary>
    /// A portable Overlay Painter layer-stack template. Layer ids in this asset are template ids;
    /// applying the preset always creates new physical and logical identities.
    /// </summary>
    [CreateAssetMenu(menuName = "UMA/Overlay Painter/Material Preset",
        fileName = "Overlay Painter Material Preset")]
    public sealed class TexturePaintMaterialPreset : ScriptableObject
    {
        public const int CurrentSchemaVersion = 3;

        public int schemaVersion = CurrentSchemaVersion;
        public string presetId = Guid.NewGuid().ToString("N");
        public int revision = 1;
        public string displayName;
        [TextArea] public string description;
        public string category;
        public List<string> tags = new List<string>();
        public Texture2D thumbnail;
        public string author;
        public string createdUtc;
        public string modifiedUtc;

        public string sourceMaterialName;
        public string sourceMeshSignature;
        public string sourceTopologySignature;
        public string sourceUVSignature;
        public List<string> sourceSlotNames = new List<string>();
        public TexturePaintPresetPortability portability;
        public bool includesWholeStack = true;
        public bool includesCachedPluginOutput = true;
        public bool packaged;
        public string packagedFromPresetId;
        public string packagedUtc;
        public List<TexturePaintMaterialPresetPackagedDependency> packagedDependencies =
            new List<TexturePaintMaterialPresetPackagedDependency>();
        public List<string> packagedExternalDependencies = new List<string>();

        public List<TexturePaintMaterialPresetChannel> channels =
            new List<TexturePaintMaterialPresetChannel>();
        public List<TexturePaintMaterialPresetPlugin> plugins =
            new List<TexturePaintMaterialPresetPlugin>();
        public List<TexturePaintDocumentLayer> layers = new List<TexturePaintDocumentLayer>();

        public void Migrate()
        {
            if (schemaVersion <= 0) schemaVersion = 1;
            if (string.IsNullOrEmpty(presetId)) presetId = Guid.NewGuid().ToString("N");
            if (revision < 1) revision = 1;
            tags ??= new List<string>();
            sourceSlotNames ??= new List<string>();
            channels ??= new List<TexturePaintMaterialPresetChannel>();
            plugins ??= new List<TexturePaintMaterialPresetPlugin>();
            layers ??= new List<TexturePaintDocumentLayer>();
            packagedDependencies ??= new List<TexturePaintMaterialPresetPackagedDependency>();
            packagedExternalDependencies ??= new List<string>();

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < layers.Count; i++)
            {
                TexturePaintDocumentLayer layer = layers[i];
                if (layer == null) continue;
                if (string.IsNullOrEmpty(layer.id) || !ids.Add(layer.id))
                {
                    layer.id = Guid.NewGuid().ToString("N");
                    ids.Add(layer.id);
                }
                layer.logicalLayerId = null;
                layer.paintTargetId = null;
                layer.effects ??= new TexturePaintLayerEffects();
                layer.effects.Normalize();
                layer.pluginParameters ??= new TexturePaintPluginParameterSet();
                layer.maskEffects ??= new TexturePaintLayerMaskEffects();
                layer.maskSourceSettings ??= TexturePaintLayerMask.DefaultSourceSettings();
                layer.maskPluginParameters ??= new TexturePaintPluginParameterSet();
                layer.splineSettings?.MigrateEditorSettings();
                layer.channels ??= new List<TexturePaintDocumentLayerChannel>();
                layer.strokes ??= new List<TexturePaintStrokeRecord>();
                for (int channelIndex = 0; channelIndex < layer.channels.Count; channelIndex++)
                {
                    TexturePaintDocumentLayerChannel channel = layer.channels[channelIndex];
                    if (channel == null) continue;
                    channel.settings ??= new TexturePaintLayerChannelSettings
                    {
                        channel = channel.channel
                    };
                    channel.settings.channel = channel.channel;
                    channel.MigrateLegacySourceSettings();
                    channel.pixels ??= new TexturePaintPixelData();
                }
                layer.maskPixels ??= new TexturePaintPixelData();
            }
            schemaVersion = CurrentSchemaVersion;
        }

        public bool ContainsChannel(TexturePaintChannel channel)
        {
            if (channels == null) return false;
            for (int i = 0; i < channels.Count; i++)
                if (channels[i] != null && channels[i].channel == channel) return true;
            return false;
        }

        private void OnValidate() => Migrate();
    }
}
