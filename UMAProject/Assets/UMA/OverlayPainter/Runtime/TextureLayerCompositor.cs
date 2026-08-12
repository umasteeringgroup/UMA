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
        private readonly int evaluateLayerMaskKernel = -1;
        private readonly int applyGroupMaskKernel = -1;
        private readonly int applyIsolatedLayerKernel = -1;
        private readonly int unassociateAlphaKernel = -1;
        private readonly int clearKernel = -1;
        private readonly Dictionary<string, LayerMaskCacheEntry> maskCache =
            new Dictionary<string, LayerMaskCacheEntry>();
        private readonly Dictionary<EditableTextureTarget, EffectDistanceCacheEntry> effectDistanceCache =
            new Dictionary<EditableTextureTarget, EffectDistanceCacheEntry>();
        private readonly Dictionary<int, Texture2D> curveTextureCache = new Dictionary<int, Texture2D>();
        private RenderTexture effectSeedA;
        private RenderTexture effectSeedB;
        private readonly List<CompositeScratch> compositeScratch = new List<CompositeScratch>();
        // Retained as diagnostic aliases for the first scratch level used by existing tooling.
        private RenderTexture groupOriginal;
        private RenderTexture groupResult;
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

        private sealed class LayerMaskCacheEntry
        {
            public EditableTextureTarget target;
            public RenderTexture texture;
            public long revision = -1;
            public int effectSignature;
        }

        private sealed class CompositeScratch
        {
            public RenderTexture original;
            public RenderTexture result;
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
            if (shader.HasKernel("CSEvaluateLayerMask"))
                evaluateLayerMaskKernel = shader.FindKernel("CSEvaluateLayerMask");
            if (shader.HasKernel("CSApplyGroupMask"))
                applyGroupMaskKernel = shader.FindKernel("CSApplyGroupMask");
            if (shader.HasKernel("CSApplyIsolatedLayer"))
                applyIsolatedLayerKernel = shader.FindKernel("CSApplyIsolatedLayer");
            if (shader.HasKernel("CSUnassociateAlpha"))
                unassociateAlphaKernel = shader.FindKernel("CSUnassociateAlpha");
            if (shader.HasKernel("CSClear")) clearKernel = shader.FindKernel("CSClear");
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

            CopyInto(baseChannel.editable.Front, baseChannel.composite, rect);

            for (int i = 0; i < set.layers.Count; i++)
            {
                TexturePaintLayer layer = set.layers[i];
                if (layer == null || TryGetParentGroup(set, layer, out _)) continue;
                if (layer.kind == TexturePaintLayerKind.Group)
                    CompositeGroupChildren(baseChannel.composite, set, layer, channel, rect, 0);
                else
                    CompositeAuthoredLayer(baseChannel.composite, set, layer, channel, rect, 0);
            }
            PruneEffectDistanceCache();
            PruneLayerMaskCache();
        }

        internal bool ComposeAuthoredLayers(TextureSet set, TexturePaintChannel channel,
            RenderTexture destination)
        {
            if (set == null || destination == null) return false;
            RectInt rect = new RectInt(0, 0, destination.width, destination.height);
            if (!IsAvailable)
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = destination;
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = previous;
                return false;
            }

            ClearInto(destination, rect);
            for (int i = 0; i < set.layers.Count; i++)
            {
                TexturePaintLayer layer = set.layers[i];
                if (layer == null || TryGetParentGroup(set, layer, out _)) continue;
                if (layer.kind == TexturePaintLayerKind.Group)
                    CompositeGroupChildren(destination, set, layer, channel, rect, 0);
                else
                    CompositeAuthoredLayer(destination, set, layer, channel, rect, 0);
            }
            // Compositing against transparent storage produces associated RGB. UMA expects
            // straight-alpha overlay textures, so recover straight color before export.
            UnassociateAlpha(destination, rect);
            PruneEffectDistanceCache();
            PruneLayerMaskCache();
            return true;
        }

        internal bool ComposeBelowLayer(TextureSet set, TexturePaintChannel channel,
            TexturePaintLayer boundaryLayer, RenderTexture destination)
        {
            TextureChannelTarget baseChannel = set?.GetChannel(channel);
            int boundary = set?.layers.IndexOf(boundaryLayer) ?? -1;
            if (baseChannel?.editable?.Front == null || destination == null || boundary < 0)
                return false;
            RectInt rect = new RectInt(0, 0, destination.width, destination.height);
            if (!IsAvailable)
            {
                Graphics.Blit(baseChannel.editable.Front, destination);
                return false;
            }

            CopyInto(baseChannel.editable.Front, destination, rect);
            for (int i = 0; i < set.layers.Count; i++)
            {
                TexturePaintLayer layer = set.layers[i];
                if (layer == null || TryGetParentGroup(set, layer, out _)) continue;
                if (layer.kind == TexturePaintLayerKind.Group)
                {
                    if (i < boundary || HasDescendantBefore(set, layer, boundary))
                        CompositeGroupChildren(destination, set, layer, channel, rect, 0, boundary);
                }
                else if (i < boundary)
                    CompositeAuthoredLayer(destination, set, layer, channel, rect, 0);
            }
            PruneEffectDistanceCache();
            PruneLayerMaskCache();
            return true;
        }

        internal void ComposeGroupPreview(TextureSet set, TexturePaintLayer group,
            TexturePaintChannel channel, RenderTexture destination)
        {
            TextureChannelTarget baseChannel = set?.GetChannel(channel);
            if (baseChannel?.editable?.Front == null || destination == null) return;
            RectInt rect = new RectInt(0, 0, destination.width, destination.height);
            if (!IsAvailable)
            {
                Graphics.Blit(baseChannel.editable.Front, destination);
                return;
            }
            CopyInto(baseChannel.editable.Front, destination, rect);
            if (group != null && group.kind == TexturePaintLayerKind.Group)
                CompositeGroupChildren(destination, set, group, channel, rect, 0);
        }

        private void CopyInto(Texture source, RenderTexture destination, RectInt rect)
        {
            shader.SetInts("_TextureSize", destination.width, destination.height);
            shader.SetInts("_TileOffset", rect.x, rect.y);
            shader.SetTexture(copyKernel, "_Base", source);
            shader.SetTexture(copyKernel, "_Composite", destination);
            Dispatch(copyKernel, rect);
        }

        private void CompositeGroupChildren(RenderTexture destination, TextureSet set,
            TexturePaintLayer group, TexturePaintChannel channel, RectInt rect, int depth,
            int maximumLayerIndexExclusive = int.MaxValue)
        {
            if (group?.visible != true || group.opacity <= 0f) return;
            if (applyGroupMaskKernel < 0 || clearKernel < 0)
            {
                CompositeGroupChildrenUnmasked(destination, set, group, channel, rect, depth,
                    maximumLayerIndexExclusive);
                return;
            }
            CompositeScratch scratch = GetCompositeScratch(depth, destination);
            CopyInto(destination, scratch.original, rect);
            ClearInto(scratch.result, rect);
            CompositeGroupChildrenUnmasked(scratch.result, set, group, channel, rect, depth + 1,
                maximumLayerIndexExclusive);
            Texture mask = group.layerMask != null && evaluateLayerMaskKernel >= 0
                ? GetEffectiveLayerMask(group, destination.width, destination.height) : null;
            shader.SetInts("_TextureSize", destination.width, destination.height);
            shader.SetInts("_TileOffset", rect.x, rect.y);
            shader.SetInt("_HasGroupMask", mask != null ? 1 : 0);
            shader.SetFloat("_GroupOpacity", Mathf.Clamp01(group.opacity));
            shader.SetInt("_GroupBlendMode", (int)group.blendMode);
            shader.SetTexture(applyGroupMaskKernel, "_GroupOriginal", scratch.original);
            shader.SetTexture(applyGroupMaskKernel, "_GroupResult", scratch.result);
            shader.SetTexture(applyGroupMaskKernel, "_LayerMask",
                mask != null ? mask : Texture2D.whiteTexture);
            shader.SetTexture(applyGroupMaskKernel, "_Composite", destination);
            Dispatch(applyGroupMaskKernel, rect);
        }

        private void CompositeGroupChildrenUnmasked(RenderTexture destination, TextureSet set,
            TexturePaintLayer group, TexturePaintChannel channel, RectInt rect, int depth,
            int maximumLayerIndexExclusive = int.MaxValue)
        {
            for (int i = 0; i < set.layers.Count; i++)
            {
                TexturePaintLayer child = set.layers[i];
                if (child == null ||
                    !string.Equals(child.parentId, group.id, System.StringComparison.Ordinal)) continue;
                if (child.kind == TexturePaintLayerKind.Group)
                {
                    if (i < maximumLayerIndexExclusive ||
                        HasDescendantBefore(set, child, maximumLayerIndexExclusive))
                        CompositeGroupChildren(destination, set, child, channel, rect, depth,
                            maximumLayerIndexExclusive);
                }
                else if (i < maximumLayerIndexExclusive)
                    CompositeAuthoredLayer(destination, set, child, channel, rect, depth);
            }
        }

        private static bool HasDescendantBefore(TextureSet set, TexturePaintLayer group,
            int maximumLayerIndexExclusive)
        {
            if (set == null || group == null) return false;
            int limit = Mathf.Min(maximumLayerIndexExclusive, set.layers.Count);
            for (int i = 0; i < limit; i++)
            {
                TexturePaintLayer candidate = set.layers[i];
                string parentId = candidate?.parentId;
                int guard = 0;
                while (!string.IsNullOrEmpty(parentId) && guard++ < set.layers.Count)
                {
                    if (string.Equals(parentId, group.id, System.StringComparison.Ordinal)) return true;
                    TexturePaintLayer parent = null;
                    for (int parentIndex = 0; parentIndex < set.layers.Count; parentIndex++)
                        if (string.Equals(set.layers[parentIndex]?.id, parentId,
                                System.StringComparison.Ordinal))
                        { parent = set.layers[parentIndex]; break; }
                    parentId = parent?.parentId;
                }
            }
            return false;
        }

        private void ClearInto(RenderTexture destination, RectInt rect)
        {
            shader.SetInts("_TextureSize", destination.width, destination.height);
            shader.SetInts("_TileOffset", rect.x, rect.y);
            shader.SetTexture(clearKernel, "_Composite", destination);
            Dispatch(clearKernel, rect);
        }

        private CompositeScratch GetCompositeScratch(int depth, RenderTexture template)
        {
            while (compositeScratch.Count <= depth) compositeScratch.Add(new CompositeScratch());
            CompositeScratch scratch = compositeScratch[depth];
            if (scratch.original != null && scratch.original.width == template.width &&
                scratch.original.height == template.height && scratch.original.format == template.format &&
                scratch.result != null && scratch.result.width == template.width &&
                scratch.result.height == template.height && scratch.result.format == template.format)
                return scratch;
            Destroy(scratch.original);
            Destroy(scratch.result);
            scratch.original = CreateEffectTexture("Overlay Painter Composite Original " + depth,
                template.width, template.height, template.format, FilterMode.Bilinear);
            scratch.result = CreateEffectTexture("Overlay Painter Composite Result " + depth,
                template.width, template.height, template.format, FilterMode.Bilinear);
            if (depth == 0)
            {
                groupOriginal = scratch.original;
                groupResult = scratch.result;
            }
            return scratch;
        }

        private void CompositeAuthoredLayer(RenderTexture destination, TextureSet set,
            TexturePaintLayer layer, TexturePaintChannel channel, RectInt rect, int depth)
        {
            if (layer?.visible != true || !layer.channels.TryGetValue(channel,
                    out EditableTextureTarget layerTarget)) return;
            TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(channel, false);
            if (settings != null && !settings.enabled) return;
            float opacity = layer.opacity * (settings != null ? settings.opacity : 1f);
            if (opacity <= 0f) return;
            TexturePaintBlendMode blendMode = settings != null ? settings.blendMode : layer.blendMode;
            CompositeLayerInto(destination, set, layer, layerTarget, channel, opacity, blendMode,
                rect, depth);
        }

        private static bool TryGetParentGroup(TextureSet set, TexturePaintLayer layer,
            out TexturePaintLayer parent)
        {
            parent = null;
            if (set == null || layer == null || string.IsNullOrEmpty(layer.parentId)) return false;
            for (int i = 0; i < set.layers.Count; i++)
            {
                TexturePaintLayer candidate = set.layers[i];
                if (candidate?.kind != TexturePaintLayerKind.Group ||
                    !string.Equals(candidate.id, layer.parentId, System.StringComparison.Ordinal)) continue;
                parent = candidate;
                return true;
            }
            return false;
        }

        internal bool CompositeLayerInto(RenderTexture destination, TextureSet set,
            TexturePaintLayer layer, EditableTextureTarget layerTarget, TexturePaintChannel channel,
            float opacity, TexturePaintBlendMode blendMode, RectInt requestedRect = default)
            => CompositeLayerInto(destination, set, layer, layerTarget, channel, opacity, blendMode,
                requestedRect, 0);

        private bool CompositeLayerInto(RenderTexture destination, TextureSet set,
            TexturePaintLayer layer, EditableTextureTarget layerTarget, TexturePaintChannel channel,
            float opacity, TexturePaintBlendMode blendMode, RectInt requestedRect, int depth)
        {
            if (!IsAvailable || destination == null || set == null || layer == null ||
                layerTarget?.Front == null) return false;
            RectInt rect = ClampRect(requestedRect, destination.width, destination.height);
            Texture layerMask = GetEffectiveLayerMask(layer, destination.width, destination.height);
            TexturePaintLayerEffects effects = layer.effects ??= new TexturePaintLayerEffects();
            effects.Normalize();
            bool ribbonLocal = layer.IsSplineLayer &&
                layer.splineSettings?.pathMode == TexturePaintPathMode.Ribbon;
            // Ribbon strokes are evaluated during ribbon projection from its intrinsic long-edge
            // coordinates. Building a distance field from the rasterized layer would instead use
            // the expanded alpha of any outer shadow/glow as the stroke boundary.
            bool requiresDistance = !ribbonLocal && effects.RequiresDistanceField(channel);
            RenderTexture distance = requiresDistance
                ? GetEffectDistance(layerTarget, layerMask, LayerMaskSignature(layer)) : null;
            if (!requiresDistance) ReleaseEffectDistance(layerTarget);

            // Build the complete layer contribution at full strength against a private copy of the
            // backdrop. Applying layer/channel opacity only once prevents masks and translucent
            // opacity from being compounded by every effect pass.
            CompositeScratch scratch = GetCompositeScratch(depth, destination);
            CopyInto(destination, scratch.original, rect);
            CopyInto(destination, scratch.result, rect);
            if (!CompositeInto(scratch.result, layerTarget.Front, 1f, blendMode, rect, layerMask))
                return false;
            for (int i = 0; i < effects.Stack.Count; i++)
            {
                TexturePaintLayerEffectSettings effect = effects.Stack[i];
                if (!IsCompositeEffect(effect, ribbonLocal)) continue;
                CompositeEffect(scratch.result, scratch.original, layerTarget.Front, layerMask, distance,
                    effect, 1f, channel, rect);
            }
            if (applyIsolatedLayerKernel < 0)
            {
                CopyInto(scratch.result, destination, rect);
                return true;
            }
            shader.SetInts("_TextureSize", destination.width, destination.height);
            shader.SetInts("_TileOffset", rect.x, rect.y);
            shader.SetFloat("_LayerOpacity", Mathf.Clamp01(opacity));
            shader.SetFloat("_LayerValueScale", channel == TexturePaintChannel.NormalControl
                ? set.ResolveNormalControlLayerScale(layer.GetChannelSettings(channel, false)) : 1f);
            shader.SetTexture(applyIsolatedLayerKernel, "_GroupOriginal", scratch.original);
            shader.SetTexture(applyIsolatedLayerKernel, "_GroupResult", scratch.result);
            shader.SetTexture(applyIsolatedLayerKernel, "_Composite", destination);
            Dispatch(applyIsolatedLayerKernel, rect);
            return true;
        }

        private static bool IsCompositeEffect(TexturePaintLayerEffectSettings effect,
            bool ribbonLocal)
        {
            if (effect?.enabled != true) return false;
            if (TexturePaintLayerEffects.IsCompositeOnlyEffect(effect.kind)) return true;
            if (ribbonLocal) return false;
            return TexturePaintLayerEffects.IsDistanceEffect(effect.kind);
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

        internal bool UnassociateAlpha(RenderTexture target, RectInt requestedRect = default)
        {
            if (!IsAvailable || unassociateAlphaKernel < 0 || target == null) return false;
            RectInt rect = ClampRect(requestedRect, target.width, target.height);
            shader.SetInts("_TextureSize", target.width, target.height);
            shader.SetInts("_TileOffset", rect.x, rect.y);
            shader.SetTexture(unassociateAlphaKernel, "_Composite", target);
            Dispatch(unassociateAlphaKernel, rect);
            return true;
        }

        private void CompositeEffect(RenderTexture destination, Texture original, Texture layerTexture,
            Texture layerMask, RenderTexture distance, TexturePaintLayerEffectSettings effect,
            float layerOpacity, TexturePaintChannel channel, RectInt rect)
        {
            if (!EffectsAvailable || !TexturePaintLayerEffects.EnabledFor(effect, channel) ||
                destination == null || layerTexture == null) return;
            bool requiresDistance = !TexturePaintLayerEffects.IsCompositeOnlyEffect(effect.kind);
            if (requiresDistance && distance == null) return;
            shader.SetInts("_TextureSize", destination.width, destination.height);
            shader.SetInts("_TileOffset", rect.x, rect.y);
            shader.SetFloat("_LayerOpacity", Mathf.Clamp01(layerOpacity));
            shader.SetInt("_HasLayerMask", layerMask != null ? 1 : 0);
            shader.SetInt("_EffectType", (int)effect.kind);
            shader.SetInt("_GrayscaleChannel", TexturePaintChannelUtility.IsGrayscale(channel) ? 1 : 0);
            shader.SetVector("_EffectColor", TexturePaintChannelUtility.ConstrainColor(channel, effect.color));
            shader.SetFloat("_EffectWidth", effect.width);
            shader.SetFloat("_EffectSmoothness", effect.smoothness);
            shader.SetVector("_EffectOffset", new Vector4(effect.offset.x, effect.offset.y, 0f, 0f));
            shader.SetFloat("_EffectLevel", effect.level);
            shader.SetFloat("_EffectSaturation", effect.saturation);
            shader.SetFloat("_EffectBrightness", effect.brightness);
            shader.SetFloat("_EffectContrast", effect.contrast);
            shader.SetFloat("_EffectHue", effect.hue);
            shader.SetInt("_EffectBlendMode", effect.kind == TexturePaintLayerEffectKind.ColorOverlay
                ? (int)effect.blendMode : (int)TexturePaintBlendMode.Normal);
            shader.SetInt("_HasEffectTexture1", effect.texture1 != null ? 1 : 0);
            shader.SetInt("_HasEffectTexture2", effect.texture2 != null ? 1 : 0);
            shader.SetTexture(compositeLayerEffectKernel, "_EffectTexture1",
                effect.texture1 != null ? effect.texture1 : Texture2D.blackTexture);
            shader.SetTexture(compositeLayerEffectKernel, "_EffectTexture2",
                effect.texture2 != null ? effect.texture2 : Texture2D.blackTexture);
            shader.SetVector("_EffectTextureTiling1",
                new Vector4(effect.textureTiling1.x, effect.textureTiling1.y, 0f, 0f));
            shader.SetVector("_EffectTextureTiling2",
                new Vector4(effect.textureTiling2.x, effect.textureTiling2.y, 0f, 0f));
            shader.SetVector("_EffectTextureOffset1", effect.textureOffset1);
            shader.SetVector("_EffectTextureOffset2", effect.textureOffset2);
            shader.SetFloat("_EffectTextureRotation1", effect.textureRotation1);
            shader.SetFloat("_EffectTextureRotation2", effect.textureRotation2);
            shader.SetFloat("_EffectTextureOpacity1", effect.textureOpacity1);
            shader.SetFloat("_EffectTextureOpacity2", effect.textureOpacity2);
            shader.SetVector("_EffectTextureColor1", TexturePaintChannelUtility.ConstrainColor(channel, effect.color));
            shader.SetVector("_EffectTextureColor2", TexturePaintChannelUtility.ConstrainColor(channel, effect.secondaryColor));
            shader.SetInt("_EffectTextureBlendMode1", (int)effect.blendMode);
            shader.SetInt("_EffectTextureBlendMode2", (int)effect.secondaryBlendMode);
            shader.SetTexture(compositeLayerEffectKernel, "_Layer", layerTexture);
            shader.SetTexture(compositeLayerEffectKernel, "_LayerMask",
                layerMask != null ? layerMask : Texture2D.whiteTexture);
            shader.SetTexture(compositeLayerEffectKernel, "_EffectDistanceRead",
                distance != null ? distance : Texture2D.blackTexture);
            shader.SetTexture(compositeLayerEffectKernel, "_EffectCurveTexture",
                GetCurveTexture(effect.curve));
            shader.SetTexture(compositeLayerEffectKernel, "_GroupOriginal", original);
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
            if (layer?.layerMask?.target == null) return 0;
            unchecked
            {
                return layer.layerMask.target.Revision.GetHashCode() * 397 ^
                    MaskEffectSignature(layer.layerMask.effects);
            }
        }

        internal Texture GetEffectiveLayerMask(TexturePaintLayer layer, int width, int height)
        {
            TexturePaintLayerMask mask = layer?.layerMask;
            if (mask?.target?.Front == null || evaluateLayerMaskKernel < 0 || width <= 0 || height <= 0)
                return null;
            mask.effects ??= new TexturePaintLayerMaskEffects();
            mask.effects.Normalize();
            int signature = MaskEffectSignature(mask.effects);
            string key = mask.target.GetHashCode() + "|" + width + "|" + height;
            if (!maskCache.TryGetValue(key, out LayerMaskCacheEntry entry))
            {
                entry = new LayerMaskCacheEntry { target = mask.target };
                maskCache.Add(key, entry);
            }
            if (entry.texture == null || entry.texture.width != width || entry.texture.height != height)
            {
                Destroy(entry.texture);
                entry.texture = CreateEffectTexture("Overlay Painter Effective Layer Mask", width, height,
                    RenderTextureFormat.ARGB32, FilterMode.Bilinear);
                entry.revision = -1;
            }
            if (entry.revision == mask.target.Revision && entry.effectSignature == signature)
                return entry.texture;

            TexturePaintLayerMaskNoiseSettings noise = mask.effects.noise;
            TexturePaintLayerMaskTextureOverlaySettings overlay = mask.effects.textureOverlay;
            shader.SetInts("_TextureSize", width, height);
            shader.SetInts("_MaskBaseSize", mask.target.Width, mask.target.Height);
            shader.SetInt("_MaskNoiseEnabled", noise.enabled ? 1 : 0);
            shader.SetInt("_MaskNoiseSeed", noise.seed);
            shader.SetVector("_MaskNoiseTiling", noise.tiling);
            shader.SetVector("_MaskNoiseOffset", noise.offset);
            shader.SetInt("_MaskNoiseOctaves", noise.octaves);
            shader.SetFloat("_MaskNoiseBalance", noise.balance);
            shader.SetFloat("_MaskNoiseContrast", noise.contrast);
            shader.SetInt("_MaskNoiseInvert", noise.invert ? 1 : 0);
            shader.SetFloat("_MaskNoiseOpacity", noise.opacity);
            shader.SetInt("_MaskNoiseCombine", (int)noise.combine);
            bool hasOverlay = overlay.enabled && overlay.texture != null;
            shader.SetInt("_MaskOverlayEnabled", hasOverlay ? 1 : 0);
            shader.SetInt("_MaskOverlayChannel", (int)overlay.sourceChannel);
            shader.SetVector("_MaskOverlayTiling", overlay.tiling);
            shader.SetVector("_MaskOverlayOffset", overlay.offset);
            shader.SetFloat("_MaskOverlayRotation", overlay.rotation);
            shader.SetInt("_MaskOverlayInvert", overlay.invert ? 1 : 0);
            shader.SetFloat("_MaskOverlayOpacity", overlay.opacity);
            shader.SetInt("_MaskOverlayCombine", (int)overlay.combine);
            shader.SetTexture(evaluateLayerMaskKernel, "_MaskBase", mask.target.Front);
            shader.SetTexture(evaluateLayerMaskKernel, "_MaskOverlay",
                hasOverlay ? overlay.texture : Texture2D.whiteTexture);
            shader.SetTexture(evaluateLayerMaskKernel, "_MaskResult", entry.texture);
            DispatchFull(evaluateLayerMaskKernel, width, height);
            entry.revision = mask.target.Revision;
            entry.effectSignature = signature;
            return entry.texture;
        }

        private static int MaskEffectSignature(TexturePaintLayerMaskEffects effects)
        {
            if (effects == null) return 0;
            effects.Normalize();
            TexturePaintLayerMaskNoiseSettings noise = effects.noise;
            TexturePaintLayerMaskTextureOverlaySettings overlay = effects.textureOverlay;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + noise.enabled.GetHashCode();
                hash = hash * 31 + noise.seed;
                hash = hash * 31 + noise.tiling.GetHashCode();
                hash = hash * 31 + noise.offset.GetHashCode();
                hash = hash * 31 + noise.octaves;
                hash = hash * 31 + noise.balance.GetHashCode();
                hash = hash * 31 + noise.contrast.GetHashCode();
                hash = hash * 31 + noise.invert.GetHashCode();
                hash = hash * 31 + noise.opacity.GetHashCode();
                hash = hash * 31 + (int)noise.combine;
                hash = hash * 31 + overlay.enabled.GetHashCode();
                hash = hash * 31 + (overlay.texture != null
                    ? overlay.texture.GetEntityId().GetHashCode() : 0);
                hash = hash * 31 + (int)overlay.sourceChannel;
                hash = hash * 31 + overlay.tiling.GetHashCode();
                hash = hash * 31 + overlay.offset.GetHashCode();
                hash = hash * 31 + overlay.rotation.GetHashCode();
                hash = hash * 31 + overlay.invert.GetHashCode();
                hash = hash * 31 + overlay.opacity.GetHashCode();
                hash = hash * 31 + (int)overlay.combine;
                return hash;
            }
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
                bool ribbonLocal = layer.IsSplineLayer &&
                    layer.splineSettings?.pathMode == TexturePaintPathMode.Ribbon;
                foreach (KeyValuePair<TexturePaintChannel, EditableTextureTarget> pair in layer.channels)
                {
                    TexturePaintLayerChannelSettings settings = layer.GetChannelSettings(pair.Key, false);
                    if (settings != null && !settings.enabled) continue;
                    if (!ribbonLocal && layer.effects.RequiresDistanceField(pair.Key)) return true;
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

        private void PruneLayerMaskCache()
        {
            List<string> dead = null;
            foreach (KeyValuePair<string, LayerMaskCacheEntry> pair in maskCache)
            {
                if (pair.Value?.target?.Front != null) continue;
                dead ??= new List<string>();
                dead.Add(pair.Key);
            }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++)
            {
                LayerMaskCacheEntry entry = maskCache[dead[i]];
                Destroy(entry.texture);
                maskCache.Remove(dead[i]);
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
            foreach (LayerMaskCacheEntry mask in maskCache.Values)
                Destroy(mask.texture);
            maskCache.Clear();
            foreach (EffectDistanceCacheEntry entry in effectDistanceCache.Values)
                Destroy(entry.distance);
            effectDistanceCache.Clear();
            foreach (Texture2D curve in curveTextureCache.Values) Destroy(curve);
            curveTextureCache.Clear();
            Destroy(effectSeedA);
            Destroy(effectSeedB);
            for (int i = 0; i < compositeScratch.Count; i++)
            {
                Destroy(compositeScratch[i].original);
                Destroy(compositeScratch[i].result);
            }
            compositeScratch.Clear();
            effectSeedA = null;
            effectSeedB = null;
            groupOriginal = null;
            groupResult = null;
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
