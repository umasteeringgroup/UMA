using System;
using UnityEngine;

namespace UMA.TexturePaint
{
    public sealed partial class TextureLayerCompositor
    {
        internal static bool HasSpatialMasks(TextureSet set)
        {
            if (set == null) return false;
            foreach (var layer in set.layers)
                if (layer?.visible == true && layer.layerMask?.effects?.stack != null)
                    foreach (var effect in layer.layerMask.effects.stack)
                        if (effect?.enabled == true && effect.opacity > 0 && effect.kind >= TexturePaintMaskEffectKind.Blur &&
                            effect.kind <= TexturePaintMaskEffectKind.Warp) return true;
            return false;
        }

        private void EvaluateMaskStack(TextureSet set, TexturePaintLayerMask mask, RenderTexture destination)
        {
            var effects = mask.effects;
            if (effects.stack.Count == 0 && effects.startFromPaint) return;
            if (!shader.HasKernel("CSMaskStack")) throw new InvalidOperationException("The mask stack shader is unavailable.");
            int evaluate = shader.FindKernel("CSMaskStack"), spatial = shader.FindKernel("CSMaskSpatial");
            int width = destination.width, height = destination.height;
            var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBHalf, 0)
                { enableRandomWrite = true, sRGB = false };
            RenderTexture ping = RenderTexture.GetTemporary(descriptor), filtered = RenderTexture.GetTemporary(descriptor), scratch = RenderTexture.GetTemporary(descriptor);
            RenderTexture seedsA = null, seedsB = null;
            try
            {
                foreach (var effect in effects.stack)
                {
                    if (!effect.enabled || effect.opacity <= 0) continue;
                    Texture source = effect.texture;
                    if (effect.kind == TexturePaintMaskEffectKind.LayerReference)
                    { if (!mask.referenceOutputs.TryGetValue(effect.id, out var referenced) || referenced == null) continue; source = referenced; }
                    if (effect.kind == TexturePaintMaskEffectKind.Texture && source == null) continue;
                    if (effect.kind >= TexturePaintMaskEffectKind.Curvature && source == null)
                    {
                        if (set?.surface?.mesh == null) continue;
                        var maps = set.GetProceduralMeshMaps();
                        source = effect.kind switch
                        {
                            TexturePaintMaskEffectKind.Curvature or TexturePaintMaskEffectKind.EdgeWear => maps.curvature,
                            TexturePaintMaskEffectKind.AmbientOcclusion or TexturePaintMaskEffectKind.CavityDirt => maps.ambientOcclusion,
                            TexturePaintMaskEffectKind.Thickness => maps.thickness,
                            TexturePaintMaskEffectKind.WorldNormal or TexturePaintMaskEffectKind.Dust => maps.worldNormal,
                            TexturePaintMaskEffectKind.WorldPosition => maps.position,
                            _ => maps.id
                        };
                    }
                    shader.SetInts("_TextureSize", width, height);
                    shader.SetInt("_StackKind", (int)effect.kind); shader.SetInt("_StackBlend", (int)effect.blend);
                    shader.SetInt("_StackChannel", effect.kind == TexturePaintMaskEffectKind.LayerReference ? 1 : (int)effect.channel);
                    shader.SetInt("_StackInvert", effect.invert ? 1 : 0); shader.SetFloat("_StackOpacity", effect.opacity);
                    shader.SetFloat("_StackAngle", effect.rotation * Mathf.Deg2Rad);
                    shader.SetVector("_StackParams", new Vector4(effect.value, effect.amount, effect.radius, effect.threshold));
                    shader.SetVector("_StackRange", new Vector4(effect.inputMin, effect.inputMax, effect.outputMin, effect.outputMax));
                    shader.SetVector("_StackMisc", new Vector4(effect.gamma, effect.softness, effect.steps, effect.repeat ? 1 : 0));
                    shader.SetVector("_StackTransform", new Vector4(effect.tiling.x, effect.tiling.y, effect.offset.x, effect.offset.y));
                    shader.SetVector("_StackDirection", effect.direction);
                    shader.SetInt("_MaskNoiseSeed", effect.seed); shader.SetInt("_MaskNoiseOctaves", effect.octaves);
                    bool spatialEffect = effect.kind >= TexturePaintMaskEffectKind.Blur && effect.kind <= TexturePaintMaskEffectKind.Outline;
                    if (spatialEffect)
                    {
                        bool directional = effect.kind == TexturePaintMaskEffectKind.DirectionalBlur;
                        shader.SetVector("_StackAxis", directional ? new Vector4(Mathf.Cos(effect.rotation * Mathf.Deg2Rad), Mathf.Sin(effect.rotation * Mathf.Deg2Rad), 0, 0) : new Vector4(1, 0, 0, 0));
                        shader.SetTexture(spatial, "_StackInput", destination); shader.SetTexture(spatial, "_StackOutput", directional ? filtered : scratch);
                        DispatchFull(spatial, width, height);
                        if (!directional)
                        {
                            shader.SetVector("_StackAxis", new Vector4(0, 1, 0, 0)); shader.SetTexture(spatial, "_StackInput", scratch);
                            shader.SetTexture(spatial, "_StackOutput", filtered); DispatchFull(spatial, width, height);
                        }
                    }
                    if (effect.kind == TexturePaintMaskEffectKind.Distance || effect.kind == TexturePaintMaskEffectKind.Feather)
                    {
                        var seedDescriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.RGFloat, 0) { enableRandomWrite = true, sRGB = false };
                        seedsA ??= RenderTexture.GetTemporary(seedDescriptor); seedsB ??= RenderTexture.GetTemporary(seedDescriptor);
                        int prepare = shader.FindKernel("CSMaskSeeds"), jump = shader.FindKernel("CSMaskJump");
                        shader.SetTexture(prepare, "_StackInput", destination); shader.SetTexture(prepare, "_StackSeedsWrite", seedsA); DispatchFull(prepare, width, height);
                        for (int step = Mathf.NextPowerOfTwo(Mathf.Max(width, height)) / 2; step >= 1; step /= 2)
                        {
                            shader.SetInt("_StackJump", step); shader.SetTexture(jump, "_StackSeedsRead", seedsA); shader.SetTexture(jump, "_StackSeedsWrite", seedsB);
                            DispatchFull(jump, width, height); (seedsA, seedsB) = (seedsB, seedsA);
                        }
                    }
                    shader.SetTexture(evaluate, "_StackInput", destination);
                    shader.SetTexture(evaluate, "_StackFiltered", spatialEffect ? filtered : destination);
                    shader.SetTexture(evaluate, "_StackSource", source != null ? source : Texture2D.whiteTexture);
                    shader.SetTexture(evaluate, "_StackPaint", mask.target.Front);
                    shader.SetTexture(evaluate, "_StackCurve", GetCurveTexture(effect.curve));
                    shader.SetTexture(evaluate, "_StackSeedsRead", seedsA != null ? (Texture)seedsA : Texture2D.blackTexture);
                    shader.SetTexture(evaluate, "_StackOutput", ping); DispatchFull(evaluate, width, height);
                    Graphics.CopyTexture(ping, destination);
                }
            }
            finally
            {
                RenderTexture.ReleaseTemporary(ping); RenderTexture.ReleaseTemporary(filtered); RenderTexture.ReleaseTemporary(scratch);
                if (seedsA != null) RenderTexture.ReleaseTemporary(seedsA); if (seedsB != null) RenderTexture.ReleaseTemporary(seedsB);
            }
        }
    }
}
