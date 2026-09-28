using System;
using UnityEngine;

namespace UMA
{
    /// <summary>A value snapshot, not an asset. Never changes shared source materials or profiles.</summary>
    public sealed class GeneratorQualityConfiguration
    {
        public bool fitAtlas = true;
        public bool SharperFitTextures = true;
        public UMAGeneratorBase.FitMethod AtlasOverflowFitMethod = UMAGeneratorBase.FitMethod.BestFitSquare;
        public float FitPercentageDecrease = 0.5f;
        public bool convertMipMaps = true;
        public int atlasResolution = 1024;
        public bool enableSourceUVCropping = false;
        public int sourceUVCropPadding = 8;
        public bool convertRenderTexture = false;
        public bool useAsyncConversion = true;
        public bool asyncMipRegen = true;
        public int MaxQueuedConversionsPerFrame = 8;
        public int IterationCount = 1;
        public int InterFrameDelay = 0;
        public float MaxMultiStepWorkMilliseconds = 2f;
        public bool collectGarbage = true;
        public int garbageCollectionRate = 0;
        public bool processAllPending = false;
        public bool applyInline = false;
        public int InitialScaleFactor = 1;
        public bool AutomaticScaling = false;
        public float ScaleGPUMemoryCutoffMB = 1024f;
        public float ScaleSystemMemoryCutoffMB = 16384f;
        public int editorAtlasResolution = 1024;
        public int editorInitialScaleFactor = 4;
        public bool SaveAndRestoreIgnoredItems = false;
        public bool CleanupUnusedBones;
        public bool MeasureBoneCleanup;
        public bool showInHierarchy = false;
        public UMARendererAsset defaultRendererAsset = null;
        public OverlayDataAsset defaultOverlayAsset = null;
        public bool alwaysRegenerateRenderers = false;
        public bool Use32BitBuffers = true;
        public TextureMerge textureMerge = null;
        public UMAMeshCombiner meshCombiner = null;
        public GeneratorTextureQuality textures;
        public GeneratorCharacterQuality characters;
        public GeneratorQualityConfiguration Clone() => (GeneratorQualityConfiguration)MemberwiseClone();
        public static GeneratorQualityConfiguration Capture(UMAGenerator generator) => new GeneratorQualityConfiguration
        {
            fitAtlas = generator.fitAtlas,
            SharperFitTextures = generator.SharperFitTextures,
            AtlasOverflowFitMethod = generator.AtlasOverflowFitMethod,
            FitPercentageDecrease = generator.FitPercentageDecrease,
            convertMipMaps = generator.convertMipMaps,
            atlasResolution = generator.atlasResolution,
            enableSourceUVCropping = generator.enableSourceUVCropping,
            sourceUVCropPadding = generator.sourceUVCropPadding,
            convertRenderTexture = generator.convertRenderTexture,
            useAsyncConversion = generator.useAsyncConversion,
            asyncMipRegen = generator.asyncMipRegen,
            MaxQueuedConversionsPerFrame = generator.MaxQueuedConversionsPerFrame,
            IterationCount = generator.IterationCount,
            InterFrameDelay = generator.InterFrameDelay,
            MaxMultiStepWorkMilliseconds = generator.MaxMultiStepWorkMilliseconds,
            collectGarbage = generator.collectGarbage,
            garbageCollectionRate = generator.garbageCollectionRate,
            processAllPending = generator.processAllPending,
            applyInline = generator.applyInline,
            InitialScaleFactor = generator.InitialScaleFactor,
            AutomaticScaling = generator.AutomaticScaling,
            ScaleGPUMemoryCutoffMB = generator.ScaleGPUMemoryCutoffMB,
            ScaleSystemMemoryCutoffMB = generator.ScaleSystemMemoryCutoffMB,
            editorAtlasResolution = generator.editorAtlasResolution,
            editorInitialScaleFactor = generator.editorInitialScaleFactor,
            SaveAndRestoreIgnoredItems = generator.SaveAndRestoreIgnoredItems,
            CleanupUnusedBones = generator.CleanupUnusedBones,
            MeasureBoneCleanup = generator.MeasureBoneCleanup,
            showInHierarchy = generator.showInHierarchy,
            defaultRendererAsset = generator.defaultRendererAsset,
            defaultOverlayAsset = generator.defaultOverlayAsset,
            alwaysRegenerateRenderers = generator.alwaysRegenerateRenderers,
            Use32BitBuffers = generator.Use32BitBuffers,
            textureMerge = generator.textureMerge,
            meshCombiner = generator.meshCombiner,
            textures = generator.qualityTextures
        };
        public void ApplyTo(UMAGenerator generator)
        {
            generator.fitAtlas = fitAtlas;
            generator.SharperFitTextures = SharperFitTextures;
            generator.AtlasOverflowFitMethod = AtlasOverflowFitMethod;
            generator.FitPercentageDecrease = FitPercentageDecrease;
            generator.convertMipMaps = convertMipMaps;
            generator.atlasResolution = atlasResolution;
            generator.enableSourceUVCropping = enableSourceUVCropping;
            generator.sourceUVCropPadding = sourceUVCropPadding;
            generator.convertRenderTexture = convertRenderTexture;
            generator.useAsyncConversion = useAsyncConversion;
            generator.asyncMipRegen = asyncMipRegen;
            generator.MaxQueuedConversionsPerFrame = MaxQueuedConversionsPerFrame;
            generator.IterationCount = IterationCount;
            generator.InterFrameDelay = InterFrameDelay;
            generator.MaxMultiStepWorkMilliseconds = MaxMultiStepWorkMilliseconds;
            generator.collectGarbage = collectGarbage;
            generator.garbageCollectionRate = garbageCollectionRate;
            generator.processAllPending = processAllPending;
            generator.applyInline = applyInline;
            generator.InitialScaleFactor = InitialScaleFactor;
            generator.AutomaticScaling = AutomaticScaling;
            generator.ScaleGPUMemoryCutoffMB = ScaleGPUMemoryCutoffMB;
            generator.ScaleSystemMemoryCutoffMB = ScaleSystemMemoryCutoffMB;
            generator.editorAtlasResolution = editorAtlasResolution;
            generator.editorInitialScaleFactor = editorInitialScaleFactor;
            generator.SaveAndRestoreIgnoredItems = SaveAndRestoreIgnoredItems;
            generator.CleanupUnusedBones = CleanupUnusedBones;
            generator.MeasureBoneCleanup = MeasureBoneCleanup;
            generator.showInHierarchy = showInHierarchy;
            generator.defaultRendererAsset = defaultRendererAsset;
            if (generator.defaultOverlayAsset != defaultOverlayAsset) generator.SetDefaultOverlayAsset(defaultOverlayAsset);
            generator.alwaysRegenerateRenderers = alwaysRegenerateRenderers;
            generator.Use32BitBuffers = Use32BitBuffers;
            generator.textureMerge = textureMerge;
            generator.meshCombiner = meshCombiner;
            generator.qualityTextures = textures;
        }
        public void ValidateAndResolveHardware(int systemMemoryMB, int graphicsMemoryMB)
        {
            atlasResolution = Mathf.Clamp(atlasResolution, 16, Mathf.Max(16, SystemInfo.maxTextureSize));
            editorAtlasResolution = Mathf.Clamp(editorAtlasResolution, 16, Mathf.Max(16, SystemInfo.maxTextureSize));
            InitialScaleFactor = Mathf.Clamp(InitialScaleFactor, 1, 16);
            editorInitialScaleFactor = Mathf.Clamp(editorInitialScaleFactor, 1, 16);
            sourceUVCropPadding = Mathf.Clamp(sourceUVCropPadding, 4, 256);
            FitPercentageDecrease = Finite(FitPercentageDecrease, .5f, .1f, .9f);
            MaxMultiStepWorkMilliseconds = Finite(MaxMultiStepWorkMilliseconds, 2, 0, 1000);
            MaxQueuedConversionsPerFrame = Mathf.Clamp(MaxQueuedConversionsPerFrame, 1, 1024);
            IterationCount = Mathf.Clamp(IterationCount, 1, 1024);
            InterFrameDelay = Mathf.Clamp(InterFrameDelay, 0, 10000);
            garbageCollectionRate = Mathf.Clamp(garbageCollectionRate, 0, 128);
            if (AutomaticScaling && ((systemMemoryMB > 0 && systemMemoryMB < ScaleSystemMemoryCutoffMB) ||
                (graphicsMemoryMB > 0 && graphicsMemoryMB < ScaleGPUMemoryCutoffMB)))
            {
                InitialScaleFactor = Mathf.Min(16, InitialScaleFactor * 2);
                atlasResolution = Mathf.Max(16, atlasResolution / 2);
            }
            AutomaticScaling = false; // resolved from the unmodified baseline on every selection
            if (useAsyncConversion && !SystemInfo.supportsAsyncGPUReadback) useAsyncConversion = false;
            textures.Validate();
            characters.Validate();
        }
        internal static float Finite(float value, float fallback, float min, float max) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
        public bool SameOutput(GeneratorQualityConfiguration other)
        {
            if (other == null) return false;
            return
                fitAtlas == other.fitAtlas &&
                SharperFitTextures == other.SharperFitTextures &&
                AtlasOverflowFitMethod == other.AtlasOverflowFitMethod &&
                FitPercentageDecrease == other.FitPercentageDecrease &&
                convertMipMaps == other.convertMipMaps &&
                atlasResolution == other.atlasResolution &&
                enableSourceUVCropping == other.enableSourceUVCropping &&
                sourceUVCropPadding == other.sourceUVCropPadding &&
                convertRenderTexture == other.convertRenderTexture &&
                useAsyncConversion == other.useAsyncConversion &&
                asyncMipRegen == other.asyncMipRegen &&
                InitialScaleFactor == other.InitialScaleFactor &&
                SaveAndRestoreIgnoredItems == other.SaveAndRestoreIgnoredItems &&
                CleanupUnusedBones == other.CleanupUnusedBones &&
                defaultRendererAsset == other.defaultRendererAsset &&
                defaultOverlayAsset == other.defaultOverlayAsset &&
                alwaysRegenerateRenderers == other.alwaysRegenerateRenderers &&
                Use32BitBuffers == other.Use32BitBuffers &&
                textureMerge == other.textureMerge &&
                meshCombiner == other.meshCombiner &&
                textures.Equals(other.textures) && characters.Equals(other.characters);
        }
    }
}
