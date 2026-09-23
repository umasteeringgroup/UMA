using System;
using UnityEngine;

namespace UMA
{
    // Explicit runtime operation, not TextureImporter compression. Defaults never enable it.
    public enum GeneratorRuntimeCompression { Disabled, Fast, HighQuality }
    [Serializable]
    public struct GeneratorTextureQuality : IEquatable<GeneratorTextureQuality>
    {
        public GeneratorQualityValue<int> maximumDimension;
        public GeneratorQualityValue<bool> mipMaps;
        public GeneratorQualityValue<FilterMode> filterMode;
        public GeneratorQualityValue<int> anisotropy;
        public GeneratorQualityValue<float> mipBias;
        public GeneratorQualityValue<GeneratorRuntimeCompression> compression;
        public void Overlay(GeneratorTextureQuality higher)
        {
            maximumDimension.Overlay(higher.maximumDimension);
            mipMaps.Overlay(higher.mipMaps);
            filterMode.Overlay(higher.filterMode);
            anisotropy.Overlay(higher.anisotropy);
            mipBias.Overlay(higher.mipBias);
            compression.Overlay(higher.compression);
        }
        public bool Equals(GeneratorTextureQuality other) =>
            maximumDimension.overrideValue == other.maximumDimension.overrideValue && (!maximumDimension.overrideValue || maximumDimension.value == other.maximumDimension.value) &&
            mipMaps.overrideValue == other.mipMaps.overrideValue && (!mipMaps.overrideValue || mipMaps.value == other.mipMaps.value) &&
            filterMode.overrideValue == other.filterMode.overrideValue && (!filterMode.overrideValue || filterMode.value == other.filterMode.value) &&
            anisotropy.overrideValue == other.anisotropy.overrideValue && (!anisotropy.overrideValue || anisotropy.value == other.anisotropy.value) &&
            mipBias.overrideValue == other.mipBias.overrideValue && (!mipBias.overrideValue || mipBias.value == other.mipBias.value) &&
            compression.overrideValue == other.compression.overrideValue && (!compression.overrideValue || compression.value == other.compression.value);
        public void Validate()
        {
            maximumDimension.value = Mathf.Clamp(maximumDimension.value, 16, Mathf.Max(16, SystemInfo.maxTextureSize));
            anisotropy.value = Mathf.Clamp(anisotropy.value, 0, 16);
            mipBias.value = GeneratorQualityConfiguration.Finite(mipBias.value, 0, -3, 3);
        }
        public void LimitSize(ref int width, ref int height)
        {
            if (!maximumDimension.overrideValue) return;
            int maximum = Mathf.Max(16, maximumDimension.value);
            float scale = Mathf.Min(1f, (float)maximum / Mathf.Max(1, Mathf.Max(width, height)));
            width = Mathf.Max(1, Mathf.FloorToInt(width * scale));
            height = Mathf.Max(1, Mathf.FloorToInt(height * scale));
        }
        public bool Mips(bool inherited) => mipMaps.Resolve(inherited);
        public int Anisotropy(UMAMaterial material) => anisotropy.Resolve(material.AnisoLevel);
        public float MipBias(UMAMaterial material) => mipBias.Resolve(material.MipMapBias);
        public FilterMode Filter(UMAMaterial material) => filterMode.Resolve(material.MatFilterMode);
        public GeneratorRuntimeCompression EffectiveCompression(bool converted, RenderTextureFormat format, int width, int height)
        {
            if (!converted || !compression.overrideValue || !SystemInfo.supportsAsyncGPUReadback || width % 4 != 0 || height % 4 != 0 ||
                (format != RenderTextureFormat.ARGB32 && format != RenderTextureFormat.Default)) return GeneratorRuntimeCompression.Disabled;
            // Texture2D.Compress chooses BC3 on desktop and ETC2 RGBA on these mobile platforms.
            bool mobile = Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer || Application.platform == RuntimePlatform.tvOS;
            if (!SystemInfo.SupportsTextureFormat(mobile ? TextureFormat.ETC2_RGBA8 : TextureFormat.DXT5)) return GeneratorRuntimeCompression.Disabled;
            return compression.value;
        }
        public void ApplySampling(Texture texture, UMAMaterial material)
        {
            texture.anisoLevel = Anisotropy(material);
            texture.mipMapBias = MipBias(material);
            texture.filterMode = Filter(material);
        }
        internal static void Compress(Texture2D texture, GeneratorRuntimeCompression mode)
        {
            if (mode == GeneratorRuntimeCompression.Disabled || texture == null || !texture.isReadable) return;
            texture.Compress(mode == GeneratorRuntimeCompression.HighQuality);
            texture.Apply(false, false);
        }
    }
    [Serializable]
    public struct GeneratorCharacterQuality : IEquatable<GeneratorCharacterQuality>
    {
        public GeneratorQualityValue<bool> reuseMeshes;
        public GeneratorQualityValue<bool> reuseTextures;
        public GeneratorQualityValue<float> atlasScale;
        public GeneratorQualityValue<float> lodDistance;
        public GeneratorQualityValue<float> lodDistanceMultiplier;
        public GeneratorQualityValue<int> lodOffset;
        public GeneratorQualityValue<int> maxLOD;
        public GeneratorQualityValue<bool> slotDropping;
        public GeneratorQualityValue<bool> resizeLODTextures;
        public GeneratorQualityValue<bool> swapLODSlots;
        public GeneratorQualityValue<bool> internalMeshLOD;
        public GeneratorQualityValue<int> disableBoneAnimatorsAtLOD;
        public GeneratorQualityValue<int> disableExpressionsAtLOD;
        public GeneratorQualityValue<int> disableDynamicExpressionsAtLOD;
        public GeneratorQualityValue<bool> markMeshNotReadable;
        public GeneratorQualityValue<bool> ignoreBlendShapes;
        public GeneratorQualityValue<bool> loadBlendShapeNormals;
        public GeneratorQualityValue<bool> loadBlendShapeTangents;
        public GeneratorQualityValue<bool> loadAllBlendShapeFrames;
        public GeneratorQualityValue<UnityEngine.Rendering.ShadowCastingMode> castShadows;
        public GeneratorQualityValue<bool> receiveShadows;
        public GeneratorQualityValue<bool> skinnedMotionVectors;
        public GeneratorQualityValue<SkinQuality> skinWeights;
        public GeneratorQualityValue<bool> overrideExplicitRendererSettings;
        public void Overlay(GeneratorCharacterQuality higher)
        {
            reuseMeshes.Overlay(higher.reuseMeshes);
            reuseTextures.Overlay(higher.reuseTextures);
            atlasScale.Overlay(higher.atlasScale);
            lodDistance.Overlay(higher.lodDistance);
            lodDistanceMultiplier.Overlay(higher.lodDistanceMultiplier);
            lodOffset.Overlay(higher.lodOffset);
            maxLOD.Overlay(higher.maxLOD);
            slotDropping.Overlay(higher.slotDropping);
            resizeLODTextures.Overlay(higher.resizeLODTextures);
            swapLODSlots.Overlay(higher.swapLODSlots);
            internalMeshLOD.Overlay(higher.internalMeshLOD);
            disableBoneAnimatorsAtLOD.Overlay(higher.disableBoneAnimatorsAtLOD);
            disableExpressionsAtLOD.Overlay(higher.disableExpressionsAtLOD);
            disableDynamicExpressionsAtLOD.Overlay(higher.disableDynamicExpressionsAtLOD);
            markMeshNotReadable.Overlay(higher.markMeshNotReadable);
            ignoreBlendShapes.Overlay(higher.ignoreBlendShapes);
            loadBlendShapeNormals.Overlay(higher.loadBlendShapeNormals);
            loadBlendShapeTangents.Overlay(higher.loadBlendShapeTangents);
            loadAllBlendShapeFrames.Overlay(higher.loadAllBlendShapeFrames);
            castShadows.Overlay(higher.castShadows);
            receiveShadows.Overlay(higher.receiveShadows);
            skinnedMotionVectors.Overlay(higher.skinnedMotionVectors);
            skinWeights.Overlay(higher.skinWeights);
            overrideExplicitRendererSettings.Overlay(higher.overrideExplicitRendererSettings);
        }
        public bool Equals(GeneratorCharacterQuality other) =>
            reuseMeshes.overrideValue == other.reuseMeshes.overrideValue && (!reuseMeshes.overrideValue || reuseMeshes.value == other.reuseMeshes.value) &&
            reuseTextures.overrideValue == other.reuseTextures.overrideValue && (!reuseTextures.overrideValue || reuseTextures.value == other.reuseTextures.value) &&
            atlasScale.overrideValue == other.atlasScale.overrideValue && (!atlasScale.overrideValue || atlasScale.value == other.atlasScale.value) &&
            lodDistance.overrideValue == other.lodDistance.overrideValue && (!lodDistance.overrideValue || lodDistance.value == other.lodDistance.value) &&
            lodDistanceMultiplier.overrideValue == other.lodDistanceMultiplier.overrideValue && (!lodDistanceMultiplier.overrideValue || lodDistanceMultiplier.value == other.lodDistanceMultiplier.value) &&
            lodOffset.overrideValue == other.lodOffset.overrideValue && (!lodOffset.overrideValue || lodOffset.value == other.lodOffset.value) &&
            maxLOD.overrideValue == other.maxLOD.overrideValue && (!maxLOD.overrideValue || maxLOD.value == other.maxLOD.value) &&
            slotDropping.overrideValue == other.slotDropping.overrideValue && (!slotDropping.overrideValue || slotDropping.value == other.slotDropping.value) &&
            resizeLODTextures.overrideValue == other.resizeLODTextures.overrideValue && (!resizeLODTextures.overrideValue || resizeLODTextures.value == other.resizeLODTextures.value) &&
            swapLODSlots.overrideValue == other.swapLODSlots.overrideValue && (!swapLODSlots.overrideValue || swapLODSlots.value == other.swapLODSlots.value) &&
            internalMeshLOD.overrideValue == other.internalMeshLOD.overrideValue && (!internalMeshLOD.overrideValue || internalMeshLOD.value == other.internalMeshLOD.value) &&
            disableBoneAnimatorsAtLOD.overrideValue == other.disableBoneAnimatorsAtLOD.overrideValue && (!disableBoneAnimatorsAtLOD.overrideValue || disableBoneAnimatorsAtLOD.value == other.disableBoneAnimatorsAtLOD.value) &&
            disableExpressionsAtLOD.overrideValue == other.disableExpressionsAtLOD.overrideValue && (!disableExpressionsAtLOD.overrideValue || disableExpressionsAtLOD.value == other.disableExpressionsAtLOD.value) &&
            disableDynamicExpressionsAtLOD.overrideValue == other.disableDynamicExpressionsAtLOD.overrideValue && (!disableDynamicExpressionsAtLOD.overrideValue || disableDynamicExpressionsAtLOD.value == other.disableDynamicExpressionsAtLOD.value) &&
            markMeshNotReadable.overrideValue == other.markMeshNotReadable.overrideValue && (!markMeshNotReadable.overrideValue || markMeshNotReadable.value == other.markMeshNotReadable.value) &&
            ignoreBlendShapes.overrideValue == other.ignoreBlendShapes.overrideValue && (!ignoreBlendShapes.overrideValue || ignoreBlendShapes.value == other.ignoreBlendShapes.value) &&
            loadBlendShapeNormals.overrideValue == other.loadBlendShapeNormals.overrideValue && (!loadBlendShapeNormals.overrideValue || loadBlendShapeNormals.value == other.loadBlendShapeNormals.value) &&
            loadBlendShapeTangents.overrideValue == other.loadBlendShapeTangents.overrideValue && (!loadBlendShapeTangents.overrideValue || loadBlendShapeTangents.value == other.loadBlendShapeTangents.value) &&
            loadAllBlendShapeFrames.overrideValue == other.loadAllBlendShapeFrames.overrideValue && (!loadAllBlendShapeFrames.overrideValue || loadAllBlendShapeFrames.value == other.loadAllBlendShapeFrames.value) &&
            castShadows.overrideValue == other.castShadows.overrideValue && (!castShadows.overrideValue || castShadows.value == other.castShadows.value) &&
            receiveShadows.overrideValue == other.receiveShadows.overrideValue && (!receiveShadows.overrideValue || receiveShadows.value == other.receiveShadows.value) &&
            skinnedMotionVectors.overrideValue == other.skinnedMotionVectors.overrideValue && (!skinnedMotionVectors.overrideValue || skinnedMotionVectors.value == other.skinnedMotionVectors.value) &&
            skinWeights.overrideValue == other.skinWeights.overrideValue && (!skinWeights.overrideValue || skinWeights.value == other.skinWeights.value) &&
            overrideExplicitRendererSettings.overrideValue == other.overrideExplicitRendererSettings.overrideValue && (!overrideExplicitRendererSettings.overrideValue || overrideExplicitRendererSettings.value == other.overrideExplicitRendererSettings.value);
        public void Validate()
        {
            atlasScale.value = GeneratorQualityConfiguration.Finite(atlasScale.value, 1, .0625f, 4);
            lodDistance.value = GeneratorQualityConfiguration.Finite(lodDistance.value, 5, .01f, 1000);
            lodDistanceMultiplier.value = GeneratorQualityConfiguration.Finite(lodDistanceMultiplier.value, 2, 1.01f, 10);
            maxLOD.value = Mathf.Clamp(maxLOD.value, 1, 16);
            lodOffset.value = Mathf.Clamp(lodOffset.value, -16, 16);
        }
    }
}
