using System;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Temporarily overrides the inspector-visible settings of the active UMAGenerator.
    /// The original values are restored when this component is disabled or destroyed.
    /// </summary>
    [AddComponentMenu("UMA/UMAGenerator Override")]
    [DefaultExecutionOrder(-20200)]
    [DisallowMultipleComponent]
    public class UMAGeneratorOverride : MonoBehaviour
    {
        [Tooltip("Optional quality profile. When assigned, its checked values replace the legacy fields below; unchecked values inherit the lower-priority profile or generator baseline.")]
        public GeneratorQualityProfile qualityProfile;
        [Tooltip("Higher priorities win. Equal priorities use activation order. Quality controllers default to 0; scene overrides default to 100.")]
        public int qualityPriority = 100;
        public GeneratorQualityRebuild qualityRebuild = GeneratorQualityRebuild.NewBuildsOnly;
        public bool overrideSourceUVCropping;
        public bool enableSourceUVCropping;
        [Min(4)] public int sourceUVCropPadding = 8;
        // Atlas Settings
        public bool fitAtlas = true;
        public bool SharperFitTextures = true;
        public UMAGeneratorBase.FitMethod AtlasOverflowFitMethod = UMAGeneratorBase.FitMethod.BestFitSquare;
        [Range(0.1f, 0.9f)]
        public float FitPercentageDecrease = 0.5f;
        public bool convertMipMaps = true;
        public int atlasResolution = 1024;

        // Conversion Settings
        [Tooltip("Convert generated RenderTextures to Texture2D. Disable this on mobile or unified-memory devices.")]
#if UNITY_ANDROID || UNITY_IOS
        public bool convertRenderTexture = false;
#else
        public bool convertRenderTexture = true;
#endif
        [Tooltip("Use asynchronous RenderTexture conversion to avoid GPU stalls.")]
        public bool useAsyncConversion = true;
        [Tooltip("Regenerate mipmaps after asynchronous conversion.")]
        public bool asyncMipRegen = true;

        // Generation Settings
        public int MaxQueuedConversionsPerFrame = 8;
        [Range(1, 16)]
        public int InitialScaleFactor = 1;
        [Tooltip("Number of iterations to process each frame.")]
        public int IterationCount = 1;
        [Min(0)]
        [Tooltip("Number of complete frames to wait before processing the next UMA. Values above zero limit generation to one UMA per eligible frame.")]
        public int InterFrameDelay;
        [Min(0f)]
        [Tooltip("Soft main-thread budget, in milliseconds, for incremental mesh-combiner work during one generator update. Zero means unlimited.")]
        public float MaxMultiStepWorkMilliseconds = 2f;
        public bool collectGarbage = true;
        [Range(0, 128)]
        public int garbageCollectionRate;
        public bool processAllPending;
        public bool SaveAndRestoreIgnoredItems;
        public bool CleanupUnusedBones;
        public bool MeasureBoneCleanup;
        public bool showInHierarchy;

        // Runtime Tuning Settings
        public bool AutomaticScaling;
        public float ScaleGPUMemoryCutoffMB = 1024f;
        public float ScaleSystemMemoryCutoffMB = 16384f;

        // Edit Time Settings
        public int editorAtlasResolution = 1024;
        [Range(1, 16)]
        public int editorInitialScaleFactor = 4;

        // Advanced Settings
        public bool applyInline;
        [Tooltip("Leave null to keep the generator's current Default Renderer Asset.")]
        public UMARendererAsset defaultRendererAsset;
        [Tooltip("Leave null to keep the generator's current Default Overlay Asset.")]
        public OverlayDataAsset defaultOverlayAsset;
        public bool alwaysRegenerateRenderers;
        public bool Use32BitBuffers = true;
        [Tooltip("Leave null to keep the generator's current Texture Merge asset.")]
        public TextureMerge textureMerge;
        [Tooltip("Leave null to keep the generator's current Mesh Combiner.")]
        public UMAMeshCombiner meshCombiner;

        [NonSerialized]
        private UMAGenerator overriddenGenerator;

        private void Awake()
        {
            if (isActiveAndEnabled) ApplyOverride();
        }

        private void OnEnable()
        {
            ApplyOverride();
        }

        private void OnDisable()
        {
            RestoreGenerator();
        }

        private void OnDestroy()
        {
            RestoreGenerator();
        }

        private void Update()
        {
            if (overriddenGenerator == null || !GeneratorQualityRuntime.HasOwner(overriddenGenerator, this)) ApplyOverride();
        }

        public void ApplySettings() { if (isActiveAndEnabled) ApplyOverride(); }

        private void Reset()
        {
            // References do not have useful universal defaults. Reuse the current
            // generator's assets when the component is first added in the editor.
            UMAGenerator currentGenerator = FindCurrentGenerator(false);
            if (currentGenerator == null)
            {
                return;
            }

            defaultRendererAsset = currentGenerator.defaultRendererAsset;
            defaultOverlayAsset = currentGenerator.defaultOverlayAsset;
            textureMerge = currentGenerator.textureMerge;
            meshCombiner = currentGenerator.meshCombiner;
        }

        private void OnValidate()
        {
            FitPercentageDecrease = Mathf.Clamp(FitPercentageDecrease, 0.1f, 0.9f);
            atlasResolution = Mathf.Max(1, atlasResolution);
            MaxQueuedConversionsPerFrame = Mathf.Max(1, MaxQueuedConversionsPerFrame);
            InitialScaleFactor = Mathf.Clamp(InitialScaleFactor, 1, 16);
            IterationCount = Mathf.Max(1, IterationCount);
            InterFrameDelay = Mathf.Max(0, InterFrameDelay);
            if (float.IsNaN(MaxMultiStepWorkMilliseconds) ||
                float.IsInfinity(MaxMultiStepWorkMilliseconds))
            {
                MaxMultiStepWorkMilliseconds = 2f;
            }
            MaxMultiStepWorkMilliseconds = Mathf.Max(0f, MaxMultiStepWorkMilliseconds);
            garbageCollectionRate = Mathf.Clamp(garbageCollectionRate, 0, 128);
            ScaleGPUMemoryCutoffMB = Mathf.Max(0f, ScaleGPUMemoryCutoffMB);
            ScaleSystemMemoryCutoffMB = Mathf.Max(0f, ScaleSystemMemoryCutoffMB);
            editorAtlasResolution = Mathf.Max(1, editorAtlasResolution);
            editorInitialScaleFactor = Mathf.Clamp(editorInitialScaleFactor, 1, 16);
            sourceUVCropPadding = Mathf.Clamp(sourceUVCropPadding, 4, 256);
        }

        private void ApplyOverride()
        {
            var next = FindCurrentGenerator(true);
            if (overriddenGenerator != next) GeneratorQualityRuntime.Remove(overriddenGenerator, this);
            overriddenGenerator = next;
            if (overriddenGenerator == null)
            {
                return;
            }

            if (qualityProfile != null)
                GeneratorQualityRuntime.SetProfile(overriddenGenerator, this, qualityProfile, qualityPriority, qualityRebuild);
            else
                GeneratorQualityRuntime.Set(overriddenGenerator, this, OverlayLegacySettings, qualityPriority, qualityRebuild, 1);
        }

        private void RestoreGenerator()
        {
            GeneratorQualityRuntime.Remove(overriddenGenerator, this);
            overriddenGenerator = null;
        }

        private void OverlayLegacySettings(GeneratorQualityConfiguration state)
        {
            state.fitAtlas = fitAtlas;
            state.SharperFitTextures = SharperFitTextures;
            state.AtlasOverflowFitMethod = AtlasOverflowFitMethod;
            state.FitPercentageDecrease = FitPercentageDecrease;
            state.convertMipMaps = convertMipMaps;
            state.atlasResolution = atlasResolution;
            state.convertRenderTexture = convertRenderTexture;
            state.useAsyncConversion = useAsyncConversion;
            state.asyncMipRegen = asyncMipRegen;
            state.MaxQueuedConversionsPerFrame = MaxQueuedConversionsPerFrame;
            state.IterationCount = IterationCount;
            state.InterFrameDelay = InterFrameDelay;
            state.MaxMultiStepWorkMilliseconds = MaxMultiStepWorkMilliseconds;
            state.collectGarbage = collectGarbage;
            state.garbageCollectionRate = garbageCollectionRate;
            state.processAllPending = processAllPending;
            state.applyInline = applyInline;
            state.InitialScaleFactor = InitialScaleFactor;
            state.AutomaticScaling = AutomaticScaling;
            state.ScaleGPUMemoryCutoffMB = ScaleGPUMemoryCutoffMB;
            state.ScaleSystemMemoryCutoffMB = ScaleSystemMemoryCutoffMB;
            state.editorAtlasResolution = editorAtlasResolution;
            state.editorInitialScaleFactor = editorInitialScaleFactor;
            state.SaveAndRestoreIgnoredItems = SaveAndRestoreIgnoredItems;
            state.CleanupUnusedBones = CleanupUnusedBones;
            state.MeasureBoneCleanup = MeasureBoneCleanup;
            state.showInHierarchy = showInHierarchy;
            if (defaultRendererAsset != null) state.defaultRendererAsset = defaultRendererAsset;
            if (defaultOverlayAsset != null) state.defaultOverlayAsset = defaultOverlayAsset;
            state.alwaysRegenerateRenderers = alwaysRegenerateRenderers;
            state.Use32BitBuffers = Use32BitBuffers;
            if (textureMerge != null) state.textureMerge = textureMerge;
            if (meshCombiner != null) state.meshCombiner = meshCombiner;
            if (overrideSourceUVCropping) { state.enableSourceUVCropping = enableSourceUVCropping; state.sourceUVCropPadding = sourceUVCropPadding; }
        }

        // Kept as a small adapter for existing editor integrations.
        private void ApplyTo(UMAGenerator generator)
        {
            var settings = GeneratorQualityConfiguration.Capture(generator);
            if (qualityProfile != null) qualityProfile.Overlay(settings);
            else OverlayLegacySettings(settings);
            settings.ApplyTo(generator);
        }

        private static UMAGenerator FindCurrentGenerator(bool createIfMissing)
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.bareInstance;
            if (indexer != null && indexer.bareGenerator != null)
            {
                return indexer.bareGenerator;
            }

            UMAGenerator generator = FindAnyObjectByType<UMAGenerator>(FindObjectsInactive.Exclude);
            if (generator != null || !createIfMissing)
            {
                return generator;
            }

            indexer = UMAAssetIndexer.Instance;
            return indexer != null ? indexer.Generator : null;
        }

        private sealed class GeneratorState
        {
            private GeneratorQualityConfiguration settings;
            public static GeneratorState Capture(UMAGenerator generator) =>
                new GeneratorState { settings = GeneratorQualityConfiguration.Capture(generator) };
            public void ApplyTo(UMAGenerator generator) => settings.ApplyTo(generator);
        }
    }
}
