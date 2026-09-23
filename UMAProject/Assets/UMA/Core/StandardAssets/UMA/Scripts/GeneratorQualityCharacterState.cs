using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.Examples;

namespace UMA
{
    /// <summary>Weak character tracking; only values this policy still owns are restored.</summary>
    internal sealed class GeneratorQualityCharacterState
    {
        private sealed class Value<T>
        {
            private T original, last;
            private bool applied;
            public T Apply(T current, GeneratorQualityValue<T> setting)
            {
                if (!applied || !EqualityComparer<T>.Default.Equals(current, last)) original = current;
                applied = true; last = setting.Resolve(original); return last;
            }
            public T Restore(T current)
            {
                if (!applied) return current;
                applied = false;
                return EqualityComparer<T>.Default.Equals(current, last) ? original : current;
            }
        }
        private readonly WeakReference<UMAData> target;
        private WeakReference<UMASimpleLOD> lodTarget;
        public int Revision = -1;
        public bool IsAlive => target.TryGetTarget(out var data) && data != null;
        public bool IsFor(UMAData data) => target.TryGetTarget(out var value) && value == data;
        public void MarkOutputDirtyForNextBuild()
        {
            if (target.TryGetTarget(out var data) && data != null && data.inheritGeneratorQuality)
                data.qualityNeedsFullBuild = data.isTextureDirty = data.isMeshDirty = true;
        }
        private readonly Value<UMARendererAsset> defaultRenderer = new Value<UMARendererAsset>();
        private bool usesDefaultRenderer;
        private readonly Value<bool> reuseMeshes = new Value<bool>();
        private readonly Value<bool> reuseTextures = new Value<bool>();
        private readonly Value<float> atlasScale = new Value<float>();
        private readonly Value<bool> markMeshNotReadable = new Value<bool>();
        private readonly Value<bool> ignoreBlendShapes = new Value<bool>();
        private readonly Value<bool> loadBlendShapeNormals = new Value<bool>();
        private readonly Value<bool> loadBlendShapeTangents = new Value<bool>();
        private readonly Value<bool> loadAllBlendShapeFrames = new Value<bool>();
        private readonly Value<float> lod_lodDistance = new Value<float>();
        private readonly Value<float> lod_lodDistanceMultiplier = new Value<float>();
        private readonly Value<int> lod_lodOffset = new Value<int>();
        private readonly Value<int> lod_maxLOD = new Value<int>();
        private readonly Value<bool> lod_slotDropping = new Value<bool>();
        private readonly Value<bool> lod_resizeLODTextures = new Value<bool>();
        private readonly Value<bool> lod_swapLODSlots = new Value<bool>();
        private readonly Value<bool> lod_internalMeshLOD = new Value<bool>();
        private readonly Value<int> lod_disableBoneAnimatorsAtLOD = new Value<int>();
        private readonly Value<int> lod_disableExpressionsAtLOD = new Value<int>();
        private readonly Value<int> lod_disableDynamicExpressionsAtLOD = new Value<int>();
        private readonly List<RendererState> renderers = new List<RendererState>();
        public GeneratorQualityCharacterState(UMAData data, UMARendererAsset baselineRenderer)
        { target = new WeakReference<UMAData>(data); usesDefaultRenderer = data.defaultRendererAsset == null || data.defaultRendererAsset == baselineRenderer; }
        public void Apply(GeneratorQualityConfiguration configuration, int revision)
        {
            if (!target.TryGetTarget(out var data) || data == null) return;
            var policy = configuration.characters;
            if (!data.inheritGeneratorReusePolicy) { policy.reuseMeshes.overrideValue = false; policy.reuseTextures.overrideValue = false; }
            data.reuseGeneratedMeshes = reuseMeshes.Apply(data.reuseGeneratedMeshes, policy.reuseMeshes);
            data.reuseGeneratedTextures = reuseTextures.Apply(data.reuseGeneratedTextures, policy.reuseTextures);
            data.atlasResolutionScale = atlasScale.Apply(data.atlasResolutionScale, policy.atlasScale);
            data.markNotReadable = markMeshNotReadable.Apply(data.markNotReadable, policy.markMeshNotReadable);
            data.blendShapeSettings.ignoreBlendShapes = ignoreBlendShapes.Apply(data.blendShapeSettings.ignoreBlendShapes, policy.ignoreBlendShapes);
            data.blendShapeSettings.loadNormals = loadBlendShapeNormals.Apply(data.blendShapeSettings.loadNormals, policy.loadBlendShapeNormals);
            data.blendShapeSettings.loadTangents = loadBlendShapeTangents.Apply(data.blendShapeSettings.loadTangents, policy.loadBlendShapeTangents);
            data.blendShapeSettings.loadAllFrames = loadAllBlendShapeFrames.Apply(data.blendShapeSettings.loadAllFrames, policy.loadAllBlendShapeFrames);
            if (usesDefaultRenderer) data.defaultRendererAsset = defaultRenderer.Apply(data.defaultRendererAsset,
                new GeneratorQualityValue<UMARendererAsset> { overrideValue = true, value = configuration.defaultRendererAsset });
            if (data.TryGetComponent<UMASimpleLOD>(out var lod))
            {
                lodTarget = new WeakReference<UMASimpleLOD>(lod);
                lod.lodDistance = lod_lodDistance.Apply(lod.lodDistance, policy.lodDistance);
                lod.distanceMultiplier = lod_lodDistanceMultiplier.Apply(lod.distanceMultiplier, policy.lodDistanceMultiplier);
                lod.lodOffset = lod_lodOffset.Apply(lod.lodOffset, policy.lodOffset);
                lod.maxLOD = lod_maxLOD.Apply(lod.maxLOD, policy.maxLOD);
                lod.useSlotDropping = lod_slotDropping.Apply(lod.useSlotDropping, policy.slotDropping);
                lod.useTextureResize = lod_resizeLODTextures.Apply(lod.useTextureResize, policy.resizeLODTextures);
                lod.swapSlots = lod_swapLODSlots.Apply(lod.swapSlots, policy.swapLODSlots);
                lod.useInternalMeshLOD = lod_internalMeshLOD.Apply(lod.useInternalMeshLOD, policy.internalMeshLOD);
                lod.disableBoneAnimatorsAtLOD = lod_disableBoneAnimatorsAtLOD.Apply(lod.disableBoneAnimatorsAtLOD, policy.disableBoneAnimatorsAtLOD);
                lod.disableUMAExpressionPlayerAtLOD = lod_disableExpressionsAtLOD.Apply(lod.disableUMAExpressionPlayerAtLOD, policy.disableExpressionsAtLOD);
                lod.disableDynamicExpressionPlayerAtLOD = lod_disableDynamicExpressionsAtLOD.Apply(lod.disableDynamicExpressionPlayerAtLOD, policy.disableDynamicExpressionsAtLOD);
            }
            Revision = revision;
        }
        public void ApplyRenderers(GeneratorQualityConfiguration configuration, UMAGenerator generator)
        {
            if (!target.TryGetTarget(out var data) || data == null || !data.inheritGeneratorQuality) return;
            renderers.RemoveAll(r => !r.IsAlive);
            var current = data.GetRenderers();
            if (current == null) return;
            for (int i = 0; i < current.Length; i++)
            {
                var renderer = current[i]; if (renderer == null) continue;
                var state = renderers.Find(s => s.IsFor(renderer));
                var asset = data.GetRendererAsset(i);
                if (!configuration.characters.overrideExplicitRendererSettings.Resolve(false) &&
                    asset != null && asset != generator.defaultRendererAsset)
                { state?.Restore(); continue; }
                if (state == null) { state = new RendererState(renderer); renderers.Add(state); }
                state.Apply(configuration.characters);
            }
        }
        public void Restore()
        {
            if (target.TryGetTarget(out var data) && data != null)
            {
                data.reuseGeneratedMeshes = reuseMeshes.Restore(data.reuseGeneratedMeshes);
                data.reuseGeneratedTextures = reuseTextures.Restore(data.reuseGeneratedTextures);
                data.atlasResolutionScale = atlasScale.Restore(data.atlasResolutionScale);
                data.markNotReadable = markMeshNotReadable.Restore(data.markNotReadable);
                data.blendShapeSettings.ignoreBlendShapes = ignoreBlendShapes.Restore(data.blendShapeSettings.ignoreBlendShapes);
                data.blendShapeSettings.loadNormals = loadBlendShapeNormals.Restore(data.blendShapeSettings.loadNormals);
                data.blendShapeSettings.loadTangents = loadBlendShapeTangents.Restore(data.blendShapeSettings.loadTangents);
                data.blendShapeSettings.loadAllFrames = loadAllBlendShapeFrames.Restore(data.blendShapeSettings.loadAllFrames);
                data.defaultRendererAsset = defaultRenderer.Restore(data.defaultRendererAsset);
            }
            if (lodTarget != null && lodTarget.TryGetTarget(out var lod) && lod != null)
            {
                lod.lodDistance = lod_lodDistance.Restore(lod.lodDistance);
                lod.distanceMultiplier = lod_lodDistanceMultiplier.Restore(lod.distanceMultiplier);
                lod.lodOffset = lod_lodOffset.Restore(lod.lodOffset);
                lod.maxLOD = lod_maxLOD.Restore(lod.maxLOD);
                lod.useSlotDropping = lod_slotDropping.Restore(lod.useSlotDropping);
                lod.useTextureResize = lod_resizeLODTextures.Restore(lod.useTextureResize);
                lod.swapSlots = lod_swapLODSlots.Restore(lod.swapSlots);
                lod.useInternalMeshLOD = lod_internalMeshLOD.Restore(lod.useInternalMeshLOD);
                lod.disableBoneAnimatorsAtLOD = lod_disableBoneAnimatorsAtLOD.Restore(lod.disableBoneAnimatorsAtLOD);
                lod.disableUMAExpressionPlayerAtLOD = lod_disableExpressionsAtLOD.Restore(lod.disableUMAExpressionPlayerAtLOD);
                lod.disableDynamicExpressionPlayerAtLOD = lod_disableDynamicExpressionsAtLOD.Restore(lod.disableDynamicExpressionPlayerAtLOD);
            }
            foreach (var renderer in renderers) renderer.Restore();
            Revision = -1;
        }
        private sealed class RendererState
        {
            private readonly WeakReference<SkinnedMeshRenderer> target;
            private readonly Value<UnityEngine.Rendering.ShadowCastingMode> shadows = new Value<UnityEngine.Rendering.ShadowCastingMode>();
            private readonly Value<bool> receive = new Value<bool>(), motion = new Value<bool>();
            private readonly Value<SkinQuality> weights = new Value<SkinQuality>();
            public bool IsAlive => target.TryGetTarget(out var r) && r != null;
            public bool IsFor(SkinnedMeshRenderer r) => target.TryGetTarget(out var value) && value == r;
            public RendererState(SkinnedMeshRenderer r) { target = new WeakReference<SkinnedMeshRenderer>(r); }
            public void Apply(GeneratorCharacterQuality policy)
            {
                if (!target.TryGetTarget(out var r) || r == null) return;
                r.shadowCastingMode = shadows.Apply(r.shadowCastingMode, policy.castShadows);
                r.receiveShadows = receive.Apply(r.receiveShadows, policy.receiveShadows);
                r.skinnedMotionVectors = motion.Apply(r.skinnedMotionVectors, policy.skinnedMotionVectors);
                r.quality = weights.Apply(r.quality, policy.skinWeights);
            }
            public void Restore()
            {
                if (!target.TryGetTarget(out var r) || r == null) return;
                r.shadowCastingMode = shadows.Restore(r.shadowCastingMode);
                r.receiveShadows = receive.Restore(r.receiveShadows);
                r.skinnedMotionVectors = motion.Restore(r.skinnedMotionVectors);
                r.quality = weights.Restore(r.quality);
            }
        }
    }
}
