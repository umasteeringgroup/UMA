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
        public bool collectGarbage = true;
        [Range(0, 128)]
        public int garbageCollectionRate;
        public bool processAllPending;
        public bool SaveAndRestoreIgnoredItems;
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
        [NonSerialized]
        private GeneratorState previousState;

        private void Awake()
        {
            ApplyOverride();
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
            garbageCollectionRate = Mathf.Clamp(garbageCollectionRate, 0, 128);
            ScaleGPUMemoryCutoffMB = Mathf.Max(0f, ScaleGPUMemoryCutoffMB);
            ScaleSystemMemoryCutoffMB = Mathf.Max(0f, ScaleSystemMemoryCutoffMB);
            editorAtlasResolution = Mathf.Max(1, editorAtlasResolution);
            editorInitialScaleFactor = Mathf.Clamp(editorInitialScaleFactor, 1, 16);
        }

        private void ApplyOverride()
        {
            if (overriddenGenerator != null || previousState != null)
            {
                return;
            }

            overriddenGenerator = FindCurrentGenerator(true);
            if (overriddenGenerator == null)
            {
                return;
            }

            previousState = GeneratorState.Capture(overriddenGenerator);
            ApplyTo(overriddenGenerator);
        }

        private void RestoreGenerator()
        {
            if (previousState != null && overriddenGenerator != null)
            {
                previousState.ApplyTo(overriddenGenerator);
            }

            previousState = null;
            overriddenGenerator = null;
        }

        private void ApplyTo(UMAGenerator generator)
        {
            generator.fitAtlas = fitAtlas;
            generator.SharperFitTextures = SharperFitTextures;
            generator.AtlasOverflowFitMethod = AtlasOverflowFitMethod;
            generator.FitPercentageDecrease = FitPercentageDecrease;
            generator.convertMipMaps = convertMipMaps;
            generator.atlasResolution = atlasResolution;

            generator.convertRenderTexture = convertRenderTexture;
            generator.useAsyncConversion = useAsyncConversion;
            generator.asyncMipRegen = asyncMipRegen;

            generator.MaxQueuedConversionsPerFrame = MaxQueuedConversionsPerFrame;
            generator.InitialScaleFactor = InitialScaleFactor;
            generator.IterationCount = IterationCount;
            generator.InterFrameDelay = InterFrameDelay;
            generator.collectGarbage = collectGarbage;
            generator.garbageCollectionRate = garbageCollectionRate;
            generator.processAllPending = processAllPending;
            generator.SaveAndRestoreIgnoredItems = SaveAndRestoreIgnoredItems;
            generator.showInHierarchy = showInHierarchy;

            generator.AutomaticScaling = AutomaticScaling;
            generator.ScaleGPUMemoryCutoffMB = ScaleGPUMemoryCutoffMB;
            generator.ScaleSystemMemoryCutoffMB = ScaleSystemMemoryCutoffMB;

            generator.editorAtlasResolution = editorAtlasResolution;
            generator.editorInitialScaleFactor = editorInitialScaleFactor;

            generator.applyInline = applyInline;
            if (defaultRendererAsset != null)
            {
                generator.defaultRendererAsset = defaultRendererAsset;
            }
            if (defaultOverlayAsset != null)
            {
                generator.SetDefaultOverlayAsset(defaultOverlayAsset);
            }
            generator.alwaysRegenerateRenderers = alwaysRegenerateRenderers;
            generator.Use32BitBuffers = Use32BitBuffers;
            if (textureMerge != null)
            {
                generator.textureMerge = textureMerge;
            }
            if (meshCombiner != null)
            {
                generator.meshCombiner = meshCombiner;
            }
        }

        private static UMAGenerator FindCurrentGenerator(bool createIfMissing)
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.bareInstance;
            if (indexer != null && indexer.bareGenerator != null)
            {
                return indexer.bareGenerator;
            }

            UMAGenerator generator = FindFirstObjectByType<UMAGenerator>(FindObjectsInactive.Exclude);
            if (generator != null || !createIfMissing)
            {
                return generator;
            }

            indexer = UMAAssetIndexer.Instance;
            return indexer != null ? indexer.Generator : null;
        }

        private sealed class GeneratorState
        {
            private bool fitAtlas;
            private bool sharperFitTextures;
            private UMAGeneratorBase.FitMethod atlasOverflowFitMethod;
            private float fitPercentageDecrease;
            private bool convertMipMaps;
            private int atlasResolution;
            private bool convertRenderTexture;
            private bool useAsyncConversion;
            private bool asyncMipRegen;
            private int maxQueuedConversionsPerFrame;
            private int initialScaleFactor;
            private int iterationCount;
            private int interFrameDelay;
            private bool collectGarbage;
            private int garbageCollectionRate;
            private bool processAllPending;
            private bool saveAndRestoreIgnoredItems;
            private bool showInHierarchy;
            private bool automaticScaling;
            private float scaleGPUMemoryCutoffMB;
            private float scaleSystemMemoryCutoffMB;
            private int editorAtlasResolution;
            private int editorInitialScaleFactor;
            private bool applyInline;
            private UMARendererAsset defaultRendererAsset;
            private OverlayDataAsset defaultOverlayAsset;
            private bool alwaysRegenerateRenderers;
            private bool use32BitBuffers;
            private TextureMerge textureMerge;
            private UMAMeshCombiner meshCombiner;

            public static GeneratorState Capture(UMAGenerator generator)
            {
                return new GeneratorState
                {
                    fitAtlas = generator.fitAtlas,
                    sharperFitTextures = generator.SharperFitTextures,
                    atlasOverflowFitMethod = generator.AtlasOverflowFitMethod,
                    fitPercentageDecrease = generator.FitPercentageDecrease,
                    convertMipMaps = generator.convertMipMaps,
                    atlasResolution = generator.atlasResolution,
                    convertRenderTexture = generator.convertRenderTexture,
                    useAsyncConversion = generator.useAsyncConversion,
                    asyncMipRegen = generator.asyncMipRegen,
                    maxQueuedConversionsPerFrame = generator.MaxQueuedConversionsPerFrame,
                    initialScaleFactor = generator.InitialScaleFactor,
                    iterationCount = generator.IterationCount,
                    interFrameDelay = generator.InterFrameDelay,
                    collectGarbage = generator.collectGarbage,
                    garbageCollectionRate = generator.garbageCollectionRate,
                    processAllPending = generator.processAllPending,
                    saveAndRestoreIgnoredItems = generator.SaveAndRestoreIgnoredItems,
                    showInHierarchy = generator.showInHierarchy,
                    automaticScaling = generator.AutomaticScaling,
                    scaleGPUMemoryCutoffMB = generator.ScaleGPUMemoryCutoffMB,
                    scaleSystemMemoryCutoffMB = generator.ScaleSystemMemoryCutoffMB,
                    editorAtlasResolution = generator.editorAtlasResolution,
                    editorInitialScaleFactor = generator.editorInitialScaleFactor,
                    applyInline = generator.applyInline,
                    defaultRendererAsset = generator.defaultRendererAsset,
                    defaultOverlayAsset = generator.defaultOverlayAsset,
                    alwaysRegenerateRenderers = generator.alwaysRegenerateRenderers,
                    use32BitBuffers = generator.Use32BitBuffers,
                    textureMerge = generator.textureMerge,
                    meshCombiner = generator.meshCombiner
                };
            }

            public void ApplyTo(UMAGenerator generator)
            {
                generator.fitAtlas = fitAtlas;
                generator.SharperFitTextures = sharperFitTextures;
                generator.AtlasOverflowFitMethod = atlasOverflowFitMethod;
                generator.FitPercentageDecrease = fitPercentageDecrease;
                generator.convertMipMaps = convertMipMaps;
                generator.atlasResolution = atlasResolution;
                generator.convertRenderTexture = convertRenderTexture;
                generator.useAsyncConversion = useAsyncConversion;
                generator.asyncMipRegen = asyncMipRegen;
                generator.MaxQueuedConversionsPerFrame = maxQueuedConversionsPerFrame;
                generator.InitialScaleFactor = initialScaleFactor;
                generator.IterationCount = iterationCount;
                generator.InterFrameDelay = interFrameDelay;
                generator.collectGarbage = collectGarbage;
                generator.garbageCollectionRate = garbageCollectionRate;
                generator.processAllPending = processAllPending;
                generator.SaveAndRestoreIgnoredItems = saveAndRestoreIgnoredItems;
                generator.showInHierarchy = showInHierarchy;
                generator.AutomaticScaling = automaticScaling;
                generator.ScaleGPUMemoryCutoffMB = scaleGPUMemoryCutoffMB;
                generator.ScaleSystemMemoryCutoffMB = scaleSystemMemoryCutoffMB;
                generator.editorAtlasResolution = editorAtlasResolution;
                generator.editorInitialScaleFactor = editorInitialScaleFactor;
                generator.applyInline = applyInline;
                generator.defaultRendererAsset = defaultRendererAsset;
                generator.SetDefaultOverlayAsset(defaultOverlayAsset);
                generator.alwaysRegenerateRenderers = alwaysRegenerateRenderers;
                generator.Use32BitBuffers = use32BitBuffers;
                generator.textureMerge = textureMerge;
                generator.meshCombiner = meshCombiner;
            }
        }
    }
}
