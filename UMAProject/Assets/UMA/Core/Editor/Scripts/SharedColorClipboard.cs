using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    // No FilePath: this is a temporary Editor-session copy, including across Play mode reloads.
    // Store parameters as managed references rather than the recipe's lossy property strings.
    internal sealed class SharedColorClipboard : ScriptableSingleton<SharedColorClipboard>
    {
        [SerializeField] private bool hasColor;
        [SerializeField] private Color[] channelMask;
        [SerializeField] private Color[] channelAdditiveMask;
        [SerializeField] private Color displayColor;
        [SerializeField] private bool showDisplayColor;
        [SerializeField] private bool isBaseColor;
        [SerializeField] private bool hasPropertyBlock;
        [SerializeField] private bool alwaysUpdate;
        [SerializeField] private bool alwaysUpdateParms;
        [SerializeReference] private List<UMAProperty> properties;

        // Bulk copies retain names independently of the avatar's values-only clipboard.
        [SerializeField] private List<NamedColor> namedColors = new List<NamedColor>();

        [Serializable]
        private sealed class NamedColor
        {
            public OverlayColorData values;
            public bool hasPropertyBlock;
            public bool alwaysUpdate;
            public bool alwaysUpdateParms;
            // Keep parameters out of OverlayColorData's recipe serialization.
            [SerializeReference] public List<UMAProperty> properties;
        }

        internal bool HasColor => hasColor;
        internal int ChannelCount => Math.Max(channelMask?.Length ?? 0, channelAdditiveMask?.Length ?? 0);
        internal bool HasNamedColors => namedColors != null && namedColors.Count > 0;

        internal void CopyChecked(IEnumerable<OverlayColorData> colors)
        {
            var copies = new List<NamedColor>();
            if (colors != null)
                foreach (var color in colors)
                {
                    if (color == null || !color.isSelected) continue;
                    var values = new OverlayColorData { name = color.name };
                    CopyChannelsAndFlags(color, values);
                    copies.Add(new NamedColor
                    {
                        values = values,
                        hasPropertyBlock = color.PropertyBlock != null,
                        alwaysUpdate = color.PropertyBlock?.alwaysUpdate ?? false,
                        alwaysUpdateParms = color.PropertyBlock?.alwaysUpdateParms ?? false,
                        properties = CloneProperties(color.PropertyBlock?.shaderProperties)
                    });
                }
            namedColors = copies;
        }

        internal OverlayColorData[] GetNamedColors()
        {
            if (!HasNamedColors) return Array.Empty<OverlayColorData>();
            var result = new OverlayColorData[namedColors.Count];
            for (int i = 0; i < result.Length; i++)
            {
                var copy = namedColors[i];
                var color = new OverlayColorData { name = copy.values.name };
                CopyChannelsAndFlags(copy.values, color);
                color.PropertyBlock = copy.hasPropertyBlock ? new UMAMaterialPropertyBlock
                {
                    alwaysUpdate = copy.alwaysUpdate,
                    alwaysUpdateParms = copy.alwaysUpdateParms,
                    shaderProperties = CloneProperties(copy.properties)
                } : null;
                result[i] = color;
            }
            return result;
        }

        internal static void CopyValues(OverlayColorData source, OverlayColorData destination)
        {
            var block = source.PropertyBlock == null ? null : new UMAMaterialPropertyBlock
            {
                alwaysUpdate = source.PropertyBlock.alwaysUpdate,
                alwaysUpdateParms = source.PropertyBlock.alwaysUpdateParms,
                shaderProperties = CloneProperties(source.PropertyBlock.shaderProperties)
            };
            CopyChannelsAndFlags(source, destination);
            destination.PropertyBlock = block;
        }

        private static void CopyChannelsAndFlags(OverlayColorData source, OverlayColorData destination)
        {
            destination.channelMask = (Color[])source.channelMask?.Clone() ?? Array.Empty<Color>();
            destination.channelAdditiveMask = (Color[])source.channelAdditiveMask?.Clone() ?? Array.Empty<Color>();
            destination.displayColor = source.displayColor;
            destination.showDisplayColor = source.showDisplayColor;
            destination.isBaseColor = source.isBaseColor;
        }

        internal void Copy(OverlayColorData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            // Snapshot before replacing the clipboard, so subsequent source edits are independent.
            var copiedProperties = CloneProperties(source.PropertyBlock?.shaderProperties);
            channelMask = (Color[])source.channelMask?.Clone() ?? Array.Empty<Color>();
            channelAdditiveMask = (Color[])source.channelAdditiveMask?.Clone() ?? Array.Empty<Color>();
            displayColor = source.displayColor;
            showDisplayColor = source.showDisplayColor;
            isBaseColor = source.isBaseColor;
            hasPropertyBlock = source.PropertyBlock != null;
            alwaysUpdate = source.PropertyBlock?.alwaysUpdate ?? false;
            alwaysUpdateParms = source.PropertyBlock?.alwaysUpdateParms ?? false;
            properties = copiedProperties;
            hasColor = true;
        }

        internal void PasteValues(OverlayColorData destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (!hasColor) return;
            var block = hasPropertyBlock ? new UMAMaterialPropertyBlock
            {
                alwaysUpdate = alwaysUpdate,
                alwaysUpdateParms = alwaysUpdateParms,
                shaderProperties = CloneProperties(properties)
            } : null;
            destination.channelMask = (Color[])channelMask.Clone();
            destination.channelAdditiveMask = (Color[])channelAdditiveMask.Clone();
            destination.displayColor = displayColor;
            destination.showDisplayColor = showDisplayColor;
            destination.isBaseColor = isBaseColor;
            destination.PropertyBlock = block;
            // The color name and transient selection/foldout/delete state are never copied.
        }

        private static List<UMAProperty> CloneProperties(List<UMAProperty> source)
        {
            if (source == null) return null;
            var result = new List<UMAProperty>(source.Count);
            foreach (var property in source)
            {
                // These properties' runtime Clone implementations assume non-null arrays.
                if (property is UMAFloatArrayProperty floats && property.GetType() == typeof(UMAFloatArrayProperty))
                    result.Add(new UMAFloatArrayProperty { name = floats.name, Value = (float[])floats.Value?.Clone() });
                else if (property is UMAVectorArrayProperty vectors && property.GetType() == typeof(UMAVectorArrayProperty))
                    result.Add(new UMAVectorArrayProperty { name = vectors.name, Value = (Vector4[])vectors.Value?.Clone() });
                else if (property is UMAMatrixArrayProperty matrices && property.GetType() == typeof(UMAMatrixArrayProperty))
                    result.Add(new UMAMatrixArrayProperty { name = matrices.name, Value = (Matrix4x4[])matrices.Value?.Clone() });
                else
                    result.Add(property?.Clone());
            }
            return result;
        }
    }
}
