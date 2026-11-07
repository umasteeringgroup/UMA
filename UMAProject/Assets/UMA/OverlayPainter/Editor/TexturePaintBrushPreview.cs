using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    internal static class TexturePaintBrushPreview
    {
        internal static Dictionary<TexturePaintChannel, Color[]> Generate(ITexturePaintBrushV2 plugin,
            TexturePaintPluginParameterSet values, BrushPreset brush, TexturePaintChannel channel, Color color)
        {
            // A stroke can keep plugin state. Give the preview its own instance and parameter snapshot.
            var instance = plugin is ScriptableObject asset ? UnityEngine.Object.Instantiate(asset) as ITexturePaintBrushV2 :
                Activator.CreateInstance(plugin.GetType(), true) as ITexturePaintBrushV2;
            if (instance == null) throw new InvalidOperationException("The brush cannot create an isolated preview instance.");
            if (instance is ScriptableObject draftAsset) draftAsset.hideFlags = HideFlags.HideAndDontSave;
            Texture2D stamp = null;
            var parameters = values.Clone(); parameters.EnsureDefaults(plugin.Descriptor);
            var context = new TexturePaintBrushContextV2 { surfaceId = "brush-preview", channel = channel,
                parameters = parameters, cancellationToken = CancellationToken.None };
            bool started = false;
            try
            {
                int resolution = TexturePaintPreviewDisplay.Resolution;
                Color[] pixels = new Color[resolution * resolution];
                if (brush?.shape == BrushPreset.Shape.Stamp && brush.ResolvedStampTexture != null)
                    stamp = ReadStamp(brush.ResolvedStampTexture);
                instance.OnStrokeStart(context); started = true;
                float radius = .08f;
                float step = Mathf.Max(.002f, radius * Mathf.Max(.01f, brush?.spacing ?? .2f));
                int count = Mathf.Clamp(Mathf.CeilToInt(.85f / step), 2, 512);
                var random = new System.Random(719);
                Vector2 previous = new Vector2(.08f, .5f);
                for (int n = 0; n <= count; n++)
                {
                    float t = n / (float)count;
                    Vector2 uv = new Vector2(.08f + .84f * t, .5f + .16f * Mathf.Sin(t * Mathf.PI * 2));
                    float units = Mathf.Max(.0001f, brush?.size ?? .05f) / radius;
                    var sample = new StrokeSample(new Vector3(uv.x * units, uv.y * units, 0), Vector3.forward, uv, 0, 0)
                        { surfaceId = context.surfaceId, previousUV = previous, direction = new Vector3(uv.x - previous.x, uv.y - previous.y, 0),
                            pressure = 1, time = t, color = color, hasColor = true };
                    var output = new TexturePaintBrushSampleV2 { color = color, opacityMultiplier = 1, sizeMultiplier = 1 };
                    instance.EvaluateSample(context, sample, ref output);
                    previous = uv;
                    if (output.skip || !Finite(output)) continue;
                    float size = radius * Mathf.Clamp(output.sizeMultiplier, .001f, 8);
                    if (brush?.randomSizeVariation == true)
                        size *= Mathf.Lerp(1 - brush.randomSizeShrink, 1 + brush.randomSizeGrow, (float)random.NextDouble());
                    float opacity = Mathf.Clamp01((brush?.flow ?? 1) * output.opacityMultiplier);
                    if (brush?.splatter == true)
                    {
                        float angle = (float)random.NextDouble() * Mathf.PI * 2;
                        uv += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * brush.splatterDistance * (float)random.NextDouble();
                        if (brush.randomStrength) opacity *= (float)random.NextDouble();
                    }
                    if (brush?.fade == true) opacity *= 1 - t;
                    if (brush?.taper == true) size *= Mathf.Max(.001f, 1 - t);
                    float rotation = (brush?.rotation ?? 0) + output.rotationOffset;
                    if (brush?.alignToStroke == true) rotation += Mathf.Atan2(sample.direction.y, sample.direction.x) * Mathf.Rad2Deg;
                    if (brush?.randomRotation == true) rotation += (float)random.NextDouble() * 360;
                    Stamp(pixels, uv, size, rotation, opacity, TexturePaintChannelUtility.WorkingColor(channel, output.color), brush?.hardness ?? .75f,
                        brush?.shape ?? BrushPreset.Shape.Circle, stamp);
                }
                return new Dictionary<TexturePaintChannel, Color[]> { [channel] = pixels };
            }
            finally
            {
                try { if (started) instance.OnStrokeEnd(context, false); }
                finally
                {
                    if (instance is UnityEngine.Object owned) UnityEngine.Object.DestroyImmediate(owned);
                    else if (instance is IDisposable disposable) disposable.Dispose();
                    if (stamp != null) UnityEngine.Object.DestroyImmediate(stamp);
                }
            }
        }

        private static bool Finite(TexturePaintBrushSampleV2 sample) =>
            float.IsFinite(sample.opacityMultiplier) && float.IsFinite(sample.sizeMultiplier) &&
            float.IsFinite(sample.rotationOffset) && float.IsFinite(sample.color.r) && float.IsFinite(sample.color.g) &&
            float.IsFinite(sample.color.b) && float.IsFinite(sample.color.a);

        private static void Stamp(Color[] pixels, Vector2 uv, float radius, float rotation, float opacity,
            Color color, float hardness, BrushPreset.Shape shape, Texture2D stamp)
        {
            int resolution = TexturePaintPreviewDisplay.Resolution;
            float extent = radius * (shape == BrushPreset.Shape.Square ? 1.415f : 1);
            int x0 = Mathf.Max(0, Mathf.FloorToInt((uv.x - extent) * resolution));
            int x1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((uv.x + extent) * resolution));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((uv.y - extent) * resolution));
            int y1 = Mathf.Min(resolution - 1, Mathf.CeilToInt((uv.y + extent) * resolution));
            float angle = rotation * Mathf.Deg2Rad, cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = (new Vector2((x + .5f) / resolution, (y + .5f) / resolution) - uv) / Mathf.Max(.00001f, radius);
                p = new Vector2(p.x * cs + p.y * sn, -p.x * sn + p.y * cs);
                float distance = shape == BrushPreset.Shape.Square ? Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) : p.magnitude;
                if (distance > 1) continue;
                float coverage = distance <= hardness ? 1 : 1 - Mathf.Clamp01((distance - hardness) / Mathf.Max(1 - hardness, .00001f));
                if (shape == BrushPreset.Shape.Stamp && stamp != null) coverage *= stamp.GetPixelBilinear(p.x * .5f + .5f, p.y * .5f + .5f).a;
                Color source = color; source.a = Mathf.Clamp01(source.a * opacity * coverage);
                int i = y * resolution + x;
                // Store straight alpha so transparent samples do not darken the preview twice.
                Color from = pixels[i]; float alpha = source.a + from.a * (1 - source.a);
                if (alpha > 0) pixels[i] = new Color((source.r * source.a + from.r * from.a * (1 - source.a)) / alpha,
                    (source.g * source.a + from.g * from.a * (1 - source.a)) / alpha,
                    (source.b * source.a + from.b * from.a * (1 - source.a)) / alpha, alpha);
            }
        }

        private static Texture2D ReadStamp(Texture source)
        {
            RenderTexture target = RenderTexture.GetTemporary(128, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Texture2D result = null;
            try
            {
                Graphics.Blit(source, target); RenderTexture.active = target;
                result = new Texture2D(128, 128, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                result.ReadPixels(new Rect(0, 0, 128, 128), 0, 0, false); result.Apply(false, false); return result;
            }
            catch { if (result != null) UnityEngine.Object.DestroyImmediate(result); throw; }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }
    }
}
