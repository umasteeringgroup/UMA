using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.TexturePaint.Examples
{
    /// <summary>API v2 generator example. It reads immutable AO snapshots and submits validated tile commands.</summary>
    public sealed class ExampleModelPlugin : ScriptableObject, ITexturePaintGeneratorV2
    {
        private static readonly TexturePaintPluginDescriptor descriptor = new TexturePaintPluginDescriptor
        {
            id = "com.uma.texturepaint.ao-variation",
            displayName = "AO Variation Generator",
            description = "Adds deterministic fine AO variation through a masked, undoable command transaction.",
            pluginVersion = "2.0.0",
            capabilities = TexturePaintPluginCapability.Generator | TexturePaintPluginCapability.LongRunning,
            declaredChannels = TexturePaintChannelMask.AmbientOcclusion,
            parameters = new List<TexturePaintPluginParameterDefinition>
            {
                new TexturePaintPluginParameterDefinition { id = "strength", displayName = "Strength", type = TexturePaintPluginParameterType.Float, minimum = 0f, maximum = 1f, defaultNumber = 0.25f },
                new TexturePaintPluginParameterDefinition { id = "frequency", displayName = "Frequency", type = TexturePaintPluginParameterType.Float, minimum = 1f, maximum = 256f, defaultNumber = 80f }
            }
        };

        public TexturePaintPluginDescriptor Descriptor => descriptor;

        public Task ExecuteAsync(TexturePaintCommandContextV2 context)
        {
            return Task.Run(() => Execute(context), context.cancellationToken);
        }

        private static void Execute(TexturePaintCommandContextV2 context)
        {
            float strength = context.parameters.Float("strength", 0.25f);
            float frequency = context.parameters.Float("frequency", 80f);
            for (int surfaceIndex = 0; surfaceIndex < context.source.surfaceIds.Count; surfaceIndex++)
            {
                context.cancellationToken.ThrowIfCancellationRequested();
                string surfaceId = context.source.surfaceIds[surfaceIndex];
                TexturePaintReadOnlyImage source = context.source.Get(surfaceId, TexturePaintChannel.AmbientOcclusion);
                if (source == null) continue;
                Color32[] pixels = new Color32[source.width * source.height];
                Parallel.For(0, source.height, new ParallelOptions
                    { CancellationToken = context.cancellationToken }, y =>
                {
                    for (int x = 0; x < source.width; x++)
                    {
                        float noise = Mathf.PerlinNoise((x + 0.5f) / source.width * frequency,
                            (y + 0.5f) / source.height * frequency);
                        float value = Mathf.Lerp(1f, noise, strength);
                        byte compact = (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
                        pixels[y * source.width + x] = new Color32(compact, compact, compact, 255);
                    }
                });
                context.WriteTileCompactOwned(surfaceId, TexturePaintChannel.AmbientOcclusion,
                    new RectInt(0, 0, source.width, source.height), pixels,
                    TexturePaintPluginColorSpace.Data, TexturePaintPluginBlend.Multiply, 1f);
            }
            context.progress?.Report(1f);
        }
    }
}
