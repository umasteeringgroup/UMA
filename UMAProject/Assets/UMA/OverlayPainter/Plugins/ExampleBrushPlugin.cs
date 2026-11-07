using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    /// <summary>API v2 brush example. It can only modulate validated samples; it never receives a writable texture.</summary>
    public sealed class ExampleBrushPlugin : ScriptableObject, ITexturePaintBrushV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor = new TexturePaintPluginDescriptor
        {
            id = "com.uma.texturepaint.noise-brush",
            displayName = "Procedural Noise Brush",
            description = "Modulates standard masked brush coverage with deterministic UV-space Perlin noise.",
            pluginVersion = "2.0.0",
            capabilities = TexturePaintPluginCapability.Brush,
            declaredChannels = TexturePaintChannelMask.All,
            parameters = new List<TexturePaintPluginParameterDefinition>
            {
                new TexturePaintPluginParameterDefinition { id = "frequency", displayName = "Frequency", type = TexturePaintPluginParameterType.Float, minimum = 1f, maximum = 128f, defaultNumber = 24f },
                new TexturePaintPluginParameterDefinition { id = "minimum", displayName = "Minimum Coverage", type = TexturePaintPluginParameterType.Float, minimum = 0f, maximum = 1f, defaultNumber = 0.15f }
            }
        };

        public TexturePaintPluginDescriptor Descriptor => descriptor;
        public void OnStrokeStart(TexturePaintBrushContextV2 context) { }

        public void EvaluateSample(TexturePaintBrushContextV2 context, StrokeSample input, ref TexturePaintBrushSampleV2 output)
        {
            float frequency = context.parameters.Float("frequency", 24f);
            float minimum = context.parameters.Float("minimum", 0.15f);
            float noise = Mathf.PerlinNoise(input.uv.x * frequency + 17.31f, input.uv.y * frequency + 41.73f);
            output.opacityMultiplier *= Mathf.Lerp(minimum, 1f, noise);
        }

        public void OnStrokeEnd(TexturePaintBrushContextV2 context, bool committed) { }
    }
}
