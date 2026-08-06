using UnityEngine;
using System.Collections.Generic;

namespace UMA.TexturePaint
{
    public sealed class TextureLayerCompositor
    {
        private readonly ComputeShader shader;
        private readonly int copyKernel = -1;
        private readonly int compositeKernel = -1;
        private readonly int prepareEffectSeedsKernel = -1;
        private readonly int jumpFloodEffectSeedsKernel = -1;
        private readonly int resolveEffectDistanceKernel = -1;
        private readonly int compositeLayerEffectKernel = -1;
        private readonly Dictionary<string, Texture2D> maskCache = new Dictionary<string, Texture2D>();
        private readonly Dictionary<EditableTextureTarget, EffectDistanceCacheEntry> effectDistanceCache =
            new Dictionary<EditableTextureTarget, EffectDistanceCacheEntry>();
        private readonly Dictionary<int, Texture2D> curveTextureCache = new Dictionary<int, Texture2D>();
        private RenderTexture effectSeedA;
        private RenderTexture effectSeedB;
        private int interactiveEditDepth;

        public bool IsAvailable => shader != null && copyKernel >= 0 && compositeKernel >= 0 && SystemInfo.supportsComputeShaders;
        public bool EffectsAvailable => IsAvailable && prepareEffectSeedsKernel >= 0 &&
            jumpFloodEffectSeedsKernel >= 0 && resolveEffectDistanceKernel >= 0 &&
            compositeLayerEffectKernel >= 0 &&
            SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGFloat) &&
            SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat);

        private sealed class EffectDistanceCacheEntry
        {
            public RenderTexture distance;
            public long revision = -1;
            public int maskSignature;
        }

        public TextureLayerCompositor(ComputeShader shader)
        {
            this.shader = shader;
            if (shader == null) return;
            if (shader.HasKernel("CSCopyBase")) copyKernel = shader.FindKernel("CSCopyBase");
            if (shader.HasKernel("CSCompositeLayer")) compositeKernel = shader.FindKernel("CSCompositeLayer");
            if (shader.HasKernel("CSPrepareEffectSeeds"))
                prepareEffectSeedsKernel = shader.FindKernel("CSPrepareEffectSeeds");
            if (shader.HasKernel("CSJumpFloodEffectSeeds"))
                jumpFloodEffectSeedsKernel = shader.FindKernel("CSJumpFloodEffectSeeds");
            if (shader.HasKernel("CSResolveEffectDistance"))
                resolveEffectDistanceKernel = shader.FindKernel("CSResolveEffectDistance");
            if (shader.HasKernel("CSCompositeLayerEffect"))
                compositeLayerEffectKernel = shader.FindKernel("CSCompositeLayerEffect");
        }

        public void Compose(TextureSet set, TexturePaintChannel channel, RectInt requestedRect)
        {
            TextureChannelTarget baseChannel = set?.GetChannel(channel);
            if (baseChannel?.editable?.Front == null || baseChannel.composite == null) return;
            RectInt rect = ClampRect(requestedRect, baseChannel.composite.width, baseChannel.composite.height);
            int effectReach = EffectsAvailable ? MaximumEffectReach(set, channel) : 0;
            if (effectReach > 0)
                rect = ExpandRect(rect, effectReach, baseChannel.composite.width, baseChannel.composite.height);
            if (rect.width <= 0 || rect.height <= 0) return;

            if (!IsAvailable)
            {
                Graphics.Blit(baseChannel.editable.Front, baseChannel.composite);
                return;
            }

            shader.SetInts("_TextureSize", baseChannel.composite.width, baseChannel.composite.height);
            shader.SetInts("_TileOffset", rect.x, rect.y);
            shader.SetTexture(copyKernel, "_Base", baseChannel.editable.Front);
            shader.SetTexture(copyKernel, "_Composite", baseChannel.composite);
            Dispatch(copyKernel, rect);

            for (int i = 0; i < set.layers.Count; i++)
            {
                TexturePaintLayer layer = set.layers[i];
                if (layer == null || !IsLayerVisible(set, layer, out float parentOpacity) || layer.kind == TexturePaintLayerKind.Group ||
                    !layer.channels.TryGetValue(channel, out EditableTextureTarget layerTarget)) continue;
                TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(channel, false);
                if (settings != null && !settings.enabled) continue;
                float opacity = layer.opacity * (settings != null ? settings.opacity : 1f) * parentOpacity;
                TexturePaintBlendMode blendMode = settings != null ? settings.blendMode : layer.blendMode;
                if (opacity <= 0f) continue;
                CompositeLayerInto(baseChannel.composite, set, layer, layerTarget, channel,
                    opacity, blendMode, rect);
            }
            PruneEffectDistanceCache();
        }

        internal bool CompositeLayerInto(RenderTexture destination, TextureSet set,
            TexturePaintLayer layer, EditableTextureTarget layerTarget, TexturePaintChannel channel,
            float opacity, TexturePaintBlendMode blendMode, RectInt requestedRect = default)
        {
            if (!IsAvailable || destination == null || set == null || layer == null ||
                layerTarget?.Front == null) return false;
            RectInt rect = ClampRect(requestedRect, destination.width, destination.height);
            Texture2D layerMask = GetLayerMask(set, layer, destination.width, destination.height);
            TexturePaintLayerEffects effects = layer.effects ??= new TexturePaintLayerEffects();
            effects.Normalize();
            bool requiresDistance = effects.RequiresDistanceField(channel);
            RenderTexture distance = requiresDistance
                ? GetEffectDistance(layerTarget, layerMask, LayerMaskSignature(layer)) : null;
            if (!requiresDistance) ReleaseEffectDistance(layerTarget);
            CompositeEffect(destination, layerTarget.Front, layerMask, distance,
                effects.outerShadow, opacity, channel, rect);
            CompositeEffect(destination, layerTarget.Front, layerMask, distance,
                effects.outerGlow, opacity, channel, rect);
            CompositeEffect(destination, layerTarget.Front, layerMask, distance,
                effects.stroke, opacity, channel, rect);
            if (!CompositeInto(destination, layerTarget.Front, opacity, blendMode, rect, layerMask))
                return false;
            CompositeEffect(destination, layerTarget.Front, layerMask, distance,
                effects.colorOverlay, opacity, channel, rect);
            CompositeEffect(destination, layerTarget.Front, layerMask, distance,
                effects.innerShadow, opacity, channel, rect);
            CompositeEffect(destination, layerTarget.Front, layerMask, distance,
                effects.innerGlow, opacity, channel, rect);
            return true;
        }

        internal bool CompositeInto(RenderTexture destination, Texture source, float opacity,
            TexturePaintBlendMode blendMode, RectInt requestedRect = default, Texture layerMask = null)
        {
            if (!IsAvailable || destination == null || source == null) return false;
            RectInt rect = ClampRect(requestedRect, destination.width, destination.height);
            shader.SetInts("_TextureSize", destination.width, destination.height);
            shader.SetInts("_TileOffset", rect.x, rect.y);
            shader.SetFloat("_LayerOpacity", Mathf.Clamp01(opacity));
            shader.SetInt("_BlendMode", (int)blendMode);
            shader.SetInt("_HasLayerMask", layerMask != null ? 1 : 0);
            shader.SetTexture(compositeKernel, "_Layer", source);
            shader.SetTexture(compositeKernel, "_LayerMask", layerMask != null ? layerMask : Texture2D.whiteTexture);
            shader.SetTexture(compositeKernel, "_Composite", destination);
            Dispatch(compositeKernel, rect);
            return true;
        }

        private void CompositeEffect(RenderTexture destination, Texture layerTexture, Texture layerMask,
            RenderTexture distance, TexturePaintLayerEffectSettings effect, float layerOpacity,
            TexturePaintChannel channel, RectInt rect)
        {
            if (!EffectsAvailable || !TexturePaintLayerEffects.EnabledFor(effect, channel) ||
                destination == null || layerTexture == null) return;
            bool requiresDistance = effect.kind != TexturePaintLayerEffectKind.ColorOverlay;
            if (requiresDistance && distance == null) return;
            shader.SetInts("_TextureSize", destination.width, destination.height);
            shader.SetInts("_TileOffset", rect.x, rect.y);
            shader.SetFloat("_LayerOpacity", Mathf.Clamp01(layerOpacity));
            shader.SetInt("_HasLayerMask", layerMask != null ? 1 : 0);
            shader.SetInt("_EffectType", (int)effect.kind);
            shader.SetVector("_EffectColor", effect.color);
            shader.SetFloat("_EffectWidth", effect.width);
            shader.SetFloat("_EffectSmoothness", effect.smoothness);
            shader.SetVector("_EffectOffset", new Vector4(effect.offset.x, effect.offset.y, 0f, 0f));
            shader.SetFloat("_EffectLevel", effect.kind == TexturePaintLayerEffectKind.ColorOverlay
                ? effect.level : 1f);
            shader.SetInt("_EffectBlendMode", effect.kind == TexturePaintLayerEffectKind.ColorOverlay
                ? (int)effect.blendMode : (int)TexturePaintBlendMode.Normal);
            shader.SetTexture(compositeLayerEffectKernel, "_Layer", layerTexture);
            shader.SetTexture(compositeLayerEffectKernel, "_LayerMask",
                layerMask != null ? layerMask : Texture2D.whiteTexture);
            shader.SetTexture(compositeLayerEffectKernel, "_EffectDistanceRead",
                distance != null ? distance : Texture2D.blackTexture);
            shader.SetTexture(compositeLayerEffectKernel, "_EffectCurveTexture",
                GetCurveTexture(effect.curve));
            shader.SetTexture(compositeLayerEffectKernel, "_Composite", destination);
            Dispatch(compositeLayerEffectKernel, rect);
        }

        private RenderTexture GetEffectDistance(EditableTextureTarget target, Texture layerMask,
            int maskSignature)
        {
            if (!EffectsAvailable || target?.Front == null) return null;
            if (interactiveEditDepth > 0)
            {
                if (!effectDistanceCache.TryGetValue(target, out EffectDistanceCacheEntry cached) ||
                    cached.distance == null || cached.distance.width != target.Width ||
                    cached.distance.height != target.Height || cached.revision < 0 ||
                    cached.maskSignature != maskSignature) return null;
                return cached.distance;
            }
            if (!effectDistanceCache.TryGetValue(target, out EffectDistanceCacheEntry entry))
            {
                entry = new EffectDistanceCacheEntry();
                effectDistanceCache.Add(target, entry);
            }
            int width = target.Width;
            int height = target.Height;
            if (entry.distance == null || entry.distance.width != width || entry.distance.height != height)
            {
                Destroy(entry.distance);
                entry.distance = CreateEffectTexture("Overlay Painter Layer Effect Distance", width, height,
                    RenderTextureFormat.RFloat, FilterMode.Point);
                entry.revision = -1;
            }
            if (entry.revision == target.Revision && entry.maskSignature == maskSignature)
                return entry.distance;

            EnsureEffectSeedTextures(width, height);
            shader.SetInts("_TextureSize", width, height);
            shader.SetInt("_HasLayerMask", layerMask != null ? 1 : 0);
            shader.SetTexture(prepareEffectSeedsKernel, "_Layer", target.Front);
            shader.SetTexture(prepareEffectSeedsKernel, "_LayerMask",
                layerMask != null ? layerMask : Texture2D.whiteTexture);
            shader.SetTexture(prepareEffectSeedsKernel, "_EffectSeedsWrite", effectSeedA);
            DispatchFull(prepareEffectSeedsKernel, width, height);

            RenderTexture read = effectSeedA;
            RenderTexture write = effectSeedB;
            for (int step = Mathf.NextPowerOfTwo(Mathf.Max(width, height)) / 2;
                step >= 1; step /= 2)
            {
                shader.SetInt("_JumpStep", step);
                shader.SetTexture(jumpFloodEffectSeedsKernel, "_EffectSeedsRead", read);
                shader.SetTexture(jumpFloodEffectSeedsKernel, "_EffectSeedsWrite", write);
                DispatchFull(jumpFloodEffectSeedsKernel, width, height);
                (read, write) = (write, read);
            }

            shader.SetTexture(resolveEffectDistanceKernel, "_Layer", target.Front);
            shader.SetTexture(resolveEffectDistanceKernel, "_LayerMask",
                layerMask != null ? layerMask : Texture2D.whiteTexture);
            shader.SetTexture(resolveEffectDistanceKernel, "_EffectSeedsRead", read);
            shader.SetTexture(resolveEffectDistanceKernel, "_EffectDistanceWrite", entry.distance);
            DispatchFull(resolveEffectDistanceKernel, width, height);
            entry.revision = target.Revision;
            entry.maskSignature = maskSignature;
            return entry.distance;
        }

        internal void BeginInteractiveEdit()
        {
            interactiveEditDepth++;
        }

        internal void EndInteractiveEdit()
        {
            interactiveEditDepth = Mathf.Max(0, interactiveEditDepth - 1);
        }

        private void EnsureEffectSeedTextures(int width, int height)
        {
            if (effectSeedA != null && effectSeedA.width == width && effectSeedA.height == height &&
                effectSeedB != null && effectSeedB.width == width && effectSeedB.height == height) return;
            Destroy(effectSeedA);
            Destroy(effectSeedB);
            effectSeedA = CreateEffectTexture("Overlay Painter Effect Seeds A", width, height,
                RenderTextureFormat.RGFloat, FilterMode.Point);
            effectSeedB = CreateEffectTexture("Overlay Painter Effect Seeds B", width, height,
                RenderTextureFormat.RGFloat, FilterMode.Point);
        }

        private Texture2D GetCurveTexture(AnimationCurve curve)
        {
            curve ??= TexturePaintLayerEffectSettings.DefaultCurve();
            int signature = CurveSignature(curve);
            if (curveTextureCache.TryGetValue(signature, out Texture2D cached) && cached != null)
                return cached;
            if (curveTextureCache.Count >= 64)
            {
                foreach (Texture2D old in curveTextureCache.Values) Destroy(old);
                curveTextureCache.Clear();
            }
            Texture2D texture = new Texture2D(256, 1, TextureFormat.RFloat, false, true)
            {
                name = "Overlay Painter Effect Curve",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color[] pixels = new Color[256];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(Mathf.Clamp01(curve.Evaluate(i / 255f)), 0f, 0f, 1f);
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            curveTextureCache[signature] = texture;
            return texture;
        }

        private static int CurveSignature(AnimationCurve curve)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (int)curve.preWrapMode;
                hash = hash * 31 + (int)curve.postWrapMode;
                Keyframe[] keys = curve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    Keyframe key = keys[i];
                    hash = hash * 31 + key.time.GetHashCode();
                    hash = hash * 31 + key.value.GetHashCode();
                    hash = hash * 31 + key.inTangent.GetHashCode();
                    hash = hash * 31 + key.outTangent.GetHashCode();
                    hash = hash * 31 + key.inWeight.GetHashCode();
                    hash = hash * 31 + key.outWeight.GetHashCode();
                    hash = hash * 31 + (int)key.weightedMode;
                }
                return hash;
            }
        }

        private static int LayerMaskSignature(TexturePaintLayer layer)
        {
            return layer?.masks == null || layer.masks.Count == 0
                ? 0 : new TexturePaintMaskStack(layer.masks).Signature;
        }

        private Texture2D GetLayerMask(TextureSet set, TexturePaintLayer layer, int width, int height)
        {
            if (layer.masks.Count == 0) return null;
            TexturePaintMaskStack stack = new TexturePaintMaskStack(layer.masks);
            string key = layer.id + "|" + width + "|" + height + "|" + stack.Signature;
            if (maskCache.TryGetValue(key, out Texture2D cached)) return cached;
            Texture2D mask = TexturePaintGeometryMask.Build(set.surface, width, height, null, -1, stack);
            maskCache[key] = mask;
            return mask;
        }

        private static int MaximumEffectReach(TextureSet set, TexturePaintChannel channel)
        {
            int reach = 0;
            if (set == null) return reach;
            for (int i = 0; i < set.layers.Count; i++)
            {
                TexturePaintLayer layer = set.layers[i];
                if (layer == null || layer.kind == TexturePaintLayerKind.Group ||
                    !IsLayerVisible(set, layer, out _) || !layer.channels.ContainsKey(channel)) continue;
                TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(channel, false);
                if (settings != null && !settings.enabled) continue;
                layer.effects ??= new TexturePaintLayerEffects();
                reach = Mathf.Max(reach, layer.effects.MaximumReach(channel));
            }
            return reach;
        }

        internal static bool HasDistanceEffects(TextureSet set)
        {
            if (set == null) return false;
            for (int layerIndex = 0; layerIndex < set.layers.Count; layerIndex++)
            {
                TexturePaintLayer layer = set.layers[layerIndex];
                if (layer == null || layer.kind == TexturePaintLayerKind.Group || !layer.visible ||
                    layer.effects == null) continue;
                foreach (KeyValuePair<TexturePaintChannel, EditableTextureTarget> pair in layer.channels)
                {
                    TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(pair.Key, false);
                    if (settings != null && !settings.enabled) continue;
                    if (layer.effects.RequiresDistanceField(pair.Key)) return true;
                }
            }
            return false;
        }

        private void PruneEffectDistanceCache()
        {
            List<EditableTextureTarget> dead = null;
            foreach (KeyValuePair<EditableTextureTarget, EffectDistanceCacheEntry> pair in effectDistanceCache)
            {
                if (pair.Key?.Front != null) continue;
                dead ??= new List<EditableTextureTarget>();
                dead.Add(pair.Key);
            }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++)
            {
                EffectDistanceCacheEntry entry = effectDistanceCache[dead[i]];
                Destroy(entry.distance);
                effectDistanceCache.Remove(dead[i]);
            }
        }

        private void ReleaseEffectDistance(EditableTextureTarget target)
        {
            if (target == null || !effectDistanceCache.TryGetValue(target,
                out EffectDistanceCacheEntry entry)) return;
            Destroy(entry.distance);
            effectDistanceCache.Remove(target);
        }

        public void Dispose()
        {
            foreach (Texture2D mask in maskCache.Values)
                Destroy(mask);
            maskCache.Clear();
            foreach (EffectDistanceCacheEntry entry in effectDistanceCache.Values)
                Destroy(entry.distance);
            effectDistanceCache.Clear();
            foreach (Texture2D curve in curveTextureCache.Values) Destroy(curve);
            curveTextureCache.Clear();
            Destroy(effectSeedA);
            Destroy(effectSeedB);
            effectSeedA = null;
            effectSeedB = null;
        }

        private static bool IsLayerVisible(TextureSet set, TexturePaintLayer layer, out float parentOpacity)
        {
            parentOpacity = 1f;
            if (!layer.visible) return false;
            string parentId = layer.parentId;
            int guard = 0;
            while (!string.IsNullOrEmpty(parentId) && guard++ < set.layers.Count)
            {
                TexturePaintLayer parent = null;
                for (int i = 0; i < set.layers.Count; i++)
                    if (set.layers[i].id == parentId) { parent = set.layers[i]; break; }
                if (parent == null) break;
                if (!parent.visible) return false;
                parentOpacity *= Mathf.Clamp01(parent.opacity);
                parentId = parent.parentId;
            }
            return true;
        }

        private void Dispatch(int kernel, RectInt rect)
        {
            shader.SetInts("_DispatchSize", rect.width, rect.height);
            shader.Dispatch(kernel, Mathf.CeilToInt(rect.width / 16f), Mathf.CeilToInt(rect.height / 16f), 1);
        }

        private void DispatchFull(int kernel, int width, int height)
        {
            shader.Dispatch(kernel, Mathf.CeilToInt(width / 16f), Mathf.CeilToInt(height / 16f), 1);
        }

        private static RenderTexture CreateEffectTexture(string name, int width, int height,
            RenderTextureFormat format, FilterMode filterMode)
        {
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(width, height, format, 0)
            {
                enableRandomWrite = true,
                sRGB = false,
                useMipMap = false,
                autoGenerateMips = false,
                msaaSamples = 1
            };
            RenderTexture texture = new RenderTexture(descriptor)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = filterMode
            };
            texture.Create();
            return texture;
        }

        private static void Destroy(Object target)
        {
            if (target == null) return;
            if (target is RenderTexture renderTexture)
            {
                if (RenderTexture.active == renderTexture) RenderTexture.active = null;
                renderTexture.Release();
            }
            if (Application.isPlaying) Object.Destroy(target); else Object.DestroyImmediate(target);
        }

        private static RectInt ClampRect(RectInt rect, int width, int height)
        {
            if (rect.width <= 0 || rect.height <= 0) return new RectInt(0, 0, width, height);
            int xMin = Mathf.Clamp(rect.xMin, 0, width);
            int yMin = Mathf.Clamp(rect.yMin, 0, height);
            int xMax = Mathf.Clamp(rect.xMax, xMin, width);
            int yMax = Mathf.Clamp(rect.yMax, yMin, height);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private static RectInt ExpandRect(RectInt rect, int padding, int width, int height)
        {
            if (padding <= 0) return rect;
            int xMin = Mathf.Max(0, rect.xMin - padding);
            int yMin = Mathf.Max(0, rect.yMin - padding);
            int xMax = Mathf.Min(width, rect.xMax + padding);
            int yMax = Mathf.Min(height, rect.yMax + padding);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }
    }
}
