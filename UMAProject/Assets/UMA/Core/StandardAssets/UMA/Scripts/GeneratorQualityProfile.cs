using System;
using UnityEngine;

namespace UMA
{
    [Serializable]
    public struct GeneratorQualityValue<T>
    {
        public bool overrideValue;
        public T value;
        public GeneratorQualityValue(T value) { this.value = value; overrideValue = false; }
        public T Resolve(T inherited) => overrideValue ? value : inherited;
        public void Overlay(GeneratorQualityValue<T> higher) { if (higher.overrideValue) this = higher; }
    }
    public enum GeneratorQualityRebuild { NewBuildsOnly, Gradual, Immediate }

    [CreateAssetMenu(menuName = "UMA/Quality/Generator Quality Profile")]
    public sealed class GeneratorQualityProfile : ScriptableObject
    {
        [Header("Atlas")]
        public GeneratorQualityValue<bool> fitAtlas = new GeneratorQualityValue<bool>(true);
        public GeneratorQualityValue<bool> SharperFitTextures = new GeneratorQualityValue<bool>(true);
        public GeneratorQualityValue<UMAGeneratorBase.FitMethod> AtlasOverflowFitMethod = new GeneratorQualityValue<UMAGeneratorBase.FitMethod>(UMAGeneratorBase.FitMethod.BestFitSquare);
        public GeneratorQualityValue<float> FitPercentageDecrease = new GeneratorQualityValue<float>(0.5f);
        public GeneratorQualityValue<bool> convertMipMaps = new GeneratorQualityValue<bool>(true);
        public GeneratorQualityValue<int> atlasResolution = new GeneratorQualityValue<int>(1024);
        public GeneratorQualityValue<bool> enableSourceUVCropping = new GeneratorQualityValue<bool>(false);
        public GeneratorQualityValue<int> sourceUVCropPadding = new GeneratorQualityValue<int>(8);
        [Header("Conversion")]
        public GeneratorQualityValue<bool> convertRenderTexture = new GeneratorQualityValue<bool>(false);
        public GeneratorQualityValue<bool> useAsyncConversion = new GeneratorQualityValue<bool>(true);
        public GeneratorQualityValue<bool> asyncMipRegen = new GeneratorQualityValue<bool>(true);
        [Header("Scheduling")]
        public GeneratorQualityValue<int> MaxQueuedConversionsPerFrame = new GeneratorQualityValue<int>(8);
        public GeneratorQualityValue<int> IterationCount = new GeneratorQualityValue<int>(1);
        public GeneratorQualityValue<int> InterFrameDelay = new GeneratorQualityValue<int>(0);
        public GeneratorQualityValue<float> MaxMultiStepWorkMilliseconds = new GeneratorQualityValue<float>(2f);
        public GeneratorQualityValue<bool> collectGarbage = new GeneratorQualityValue<bool>(true);
        public GeneratorQualityValue<int> garbageCollectionRate = new GeneratorQualityValue<int>(0);
        public GeneratorQualityValue<bool> processAllPending = new GeneratorQualityValue<bool>(false);
        public GeneratorQualityValue<bool> applyInline = new GeneratorQualityValue<bool>(false);
        [Header("Memory")]
        public GeneratorQualityValue<int> InitialScaleFactor = new GeneratorQualityValue<int>(1);
        public GeneratorQualityValue<bool> AutomaticScaling = new GeneratorQualityValue<bool>(false);
        public GeneratorQualityValue<float> ScaleGPUMemoryCutoffMB = new GeneratorQualityValue<float>(1024f);
        public GeneratorQualityValue<float> ScaleSystemMemoryCutoffMB = new GeneratorQualityValue<float>(16384f);
        [Header("Editor preview")]
        public GeneratorQualityValue<int> editorAtlasResolution = new GeneratorQualityValue<int>(1024);
        public GeneratorQualityValue<int> editorInitialScaleFactor = new GeneratorQualityValue<int>(4);
        [Header("Advanced")]
        public GeneratorQualityValue<bool> SaveAndRestoreIgnoredItems = new GeneratorQualityValue<bool>(false);
        public GeneratorQualityValue<bool> CleanupUnusedBones = new GeneratorQualityValue<bool>(false);
        public GeneratorQualityValue<bool> MeasureBoneCleanup = new GeneratorQualityValue<bool>(false);
        public GeneratorQualityValue<bool> showInHierarchy = new GeneratorQualityValue<bool>(false);
        public GeneratorQualityValue<UMARendererAsset> defaultRendererAsset = new GeneratorQualityValue<UMARendererAsset>(null);
        public GeneratorQualityValue<OverlayDataAsset> defaultOverlayAsset = new GeneratorQualityValue<OverlayDataAsset>(null);
        public GeneratorQualityValue<bool> alwaysRegenerateRenderers = new GeneratorQualityValue<bool>(false);
        public GeneratorQualityValue<bool> Use32BitBuffers = new GeneratorQualityValue<bool>(true);
        public GeneratorQualityValue<TextureMerge> textureMerge = new GeneratorQualityValue<TextureMerge>(null);
        public GeneratorQualityValue<UMAMeshCombiner> meshCombiner = new GeneratorQualityValue<UMAMeshCombiner>(null);
        [Header("Generated textures (never edits source assets)")]
        public GeneratorTextureQuality textures = new GeneratorTextureQuality
        {
            maximumDimension = new GeneratorQualityValue<int>(2048),
            mipMaps = new GeneratorQualityValue<bool>(true),
            filterMode = new GeneratorQualityValue<FilterMode>(FilterMode.Bilinear),
            anisotropy = new GeneratorQualityValue<int>(1)
        };
        [Header("Optional character policies")]
        public GeneratorCharacterQuality characters = new GeneratorCharacterQuality
        {
            atlasScale = new GeneratorQualityValue<float>(1),
            lodDistance = new GeneratorQualityValue<float>(5),
            lodDistanceMultiplier = new GeneratorQualityValue<float>(2),
            maxLOD = new GeneratorQualityValue<int>(5),
            disableBoneAnimatorsAtLOD = new GeneratorQualityValue<int>(-1),
            disableExpressionsAtLOD = new GeneratorQualityValue<int>(-1),
            disableDynamicExpressionsAtLOD = new GeneratorQualityValue<int>(-1),
            loadBlendShapeNormals = new GeneratorQualityValue<bool>(true),
            loadBlendShapeTangents = new GeneratorQualityValue<bool>(true),
            castShadows = new GeneratorQualityValue<UnityEngine.Rendering.ShadowCastingMode>(UnityEngine.Rendering.ShadowCastingMode.On),
            receiveShadows = new GeneratorQualityValue<bool>(true)
        };
        public void Overlay(GeneratorQualityConfiguration result)
        {
            result.fitAtlas = fitAtlas.Resolve(result.fitAtlas);
            result.SharperFitTextures = SharperFitTextures.Resolve(result.SharperFitTextures);
            result.AtlasOverflowFitMethod = AtlasOverflowFitMethod.Resolve(result.AtlasOverflowFitMethod);
            result.FitPercentageDecrease = FitPercentageDecrease.Resolve(result.FitPercentageDecrease);
            result.convertMipMaps = convertMipMaps.Resolve(result.convertMipMaps);
            result.atlasResolution = atlasResolution.Resolve(result.atlasResolution);
            result.enableSourceUVCropping = enableSourceUVCropping.Resolve(result.enableSourceUVCropping);
            result.sourceUVCropPadding = sourceUVCropPadding.Resolve(result.sourceUVCropPadding);
            result.convertRenderTexture = convertRenderTexture.Resolve(result.convertRenderTexture);
            result.useAsyncConversion = useAsyncConversion.Resolve(result.useAsyncConversion);
            result.asyncMipRegen = asyncMipRegen.Resolve(result.asyncMipRegen);
            result.MaxQueuedConversionsPerFrame = MaxQueuedConversionsPerFrame.Resolve(result.MaxQueuedConversionsPerFrame);
            result.IterationCount = IterationCount.Resolve(result.IterationCount);
            result.InterFrameDelay = InterFrameDelay.Resolve(result.InterFrameDelay);
            result.MaxMultiStepWorkMilliseconds = MaxMultiStepWorkMilliseconds.Resolve(result.MaxMultiStepWorkMilliseconds);
            result.collectGarbage = collectGarbage.Resolve(result.collectGarbage);
            result.garbageCollectionRate = garbageCollectionRate.Resolve(result.garbageCollectionRate);
            result.processAllPending = processAllPending.Resolve(result.processAllPending);
            result.applyInline = applyInline.Resolve(result.applyInline);
            result.InitialScaleFactor = InitialScaleFactor.Resolve(result.InitialScaleFactor);
            result.AutomaticScaling = AutomaticScaling.Resolve(result.AutomaticScaling);
            result.ScaleGPUMemoryCutoffMB = ScaleGPUMemoryCutoffMB.Resolve(result.ScaleGPUMemoryCutoffMB);
            result.ScaleSystemMemoryCutoffMB = ScaleSystemMemoryCutoffMB.Resolve(result.ScaleSystemMemoryCutoffMB);
            result.editorAtlasResolution = editorAtlasResolution.Resolve(result.editorAtlasResolution);
            result.editorInitialScaleFactor = editorInitialScaleFactor.Resolve(result.editorInitialScaleFactor);
            result.SaveAndRestoreIgnoredItems = SaveAndRestoreIgnoredItems.Resolve(result.SaveAndRestoreIgnoredItems);
            result.CleanupUnusedBones = CleanupUnusedBones.Resolve(result.CleanupUnusedBones);
            result.MeasureBoneCleanup = MeasureBoneCleanup.Resolve(result.MeasureBoneCleanup);
            result.showInHierarchy = showInHierarchy.Resolve(result.showInHierarchy);
            result.defaultRendererAsset = defaultRendererAsset.Resolve(result.defaultRendererAsset);
            result.defaultOverlayAsset = defaultOverlayAsset.Resolve(result.defaultOverlayAsset);
            result.alwaysRegenerateRenderers = alwaysRegenerateRenderers.Resolve(result.alwaysRegenerateRenderers);
            result.Use32BitBuffers = Use32BitBuffers.Resolve(result.Use32BitBuffers);
            result.textureMerge = textureMerge.Resolve(result.textureMerge);
            result.meshCombiner = meshCombiner.Resolve(result.meshCombiner);
            result.textures.Overlay(textures);
            result.characters.Overlay(characters);
        }
    }
}
