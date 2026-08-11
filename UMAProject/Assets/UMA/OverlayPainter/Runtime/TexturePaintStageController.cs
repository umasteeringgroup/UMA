using System;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA.TexturePaint
{
    public sealed class TexturePaintStageController : IDisposable
    {
        public DynamicCharacterAvatar Avatar { get; private set; }
        public MeshReconstructionResult Reconstruction { get; private set; } 
        public TextureStore Textures { get; private set; }
        public PaintingEngine Painting { get; private set; }
        public PluginHost Plugins { get; private set; }
        public TexturePaintLogicalLayerController LogicalLayers { get; private set; }
        public TexturePaintDocument Document { get; private set; }
        public TexturePaintLogicalTargetCatalog LogicalTargets => Reconstruction?.logicalTargets;
        public void Initialize(DynamicCharacterAvatar avatar, ComputeShader stroke, ComputeShader blur, ComputeShader normal,
            ComputeShader layerComposite = null, ComputeShader channelPack = null, int resolution = 2048,
            Shader fillShader = null, Shader ribbonShader = null)
        {
            Dispose();
            Avatar = avatar != null ? avatar : throw new ArgumentNullException(nameof(avatar));
            InitializeCore(MeshReconstructor.Reconstruct(avatar), stroke, blur, normal, layerComposite,
                channelPack, resolution, fillShader, ribbonShader, false);
        }

        public void InitializeStandalone(TexturePaintLaunchContext context, ComputeShader stroke, ComputeShader blur,
            ComputeShader normal, ComputeShader layerComposite = null, ComputeShader channelPack = null,
            int resolution = 2048, Shader fillShader = null, Shader ribbonShader = null)
        {
            Dispose();
            if (context == null || !context.IsStandalone)
                throw new ArgumentException("A standalone slot launch context is required.", nameof(context));
            InitializeCore(MeshReconstructor.ReconstructSlotGroup(context), stroke, blur, normal,
                layerComposite, channelPack, resolution, fillShader, ribbonShader,
                context.sourceMode == TexturePaintStandaloneSourceMode.UMAMaterial);
        }

        private void InitializeCore(MeshReconstructionResult reconstruction, ComputeShader stroke,
            ComputeShader blur, ComputeShader normal, ComputeShader layerComposite, ComputeShader channelPack,
            int resolution, Shader fillShader, Shader ribbonShader, bool addDefaultWhite)
        {
            Reconstruction = reconstruction ?? throw new ArgumentNullException(nameof(reconstruction));
            Textures = new TextureStore();
            Textures.Initialize(Reconstruction, resolution, layerComposite, channelPack, fillShader);
#if UNITY_EDITOR
            System.Collections.Generic.List<string> materialFailures =
                new System.Collections.Generic.List<string>();
            for (int setIndex = 0; setIndex < Textures.Sets.Count; setIndex++)
            {
                TextureSet set = Textures.Sets[setIndex];
                if (set.materialCapability == null)
                {
                    materialFailures.Add($"{set.Name}: no compiled UMAMaterial channel descriptor is available.");
                    continue;
                }
                if (!set.materialCapability.IsSupported)
                    materialFailures.Add($"{set.Name}:\n{set.materialCapability.FailureSummary()}");
            }
            if (materialFailures.Count > 0)
                throw new InvalidOperationException("Overlay Painter material preflight failed:\n\n" +
                    string.Join("\n\n", materialFailures));
#endif
            Reconstruction.logicalTargets.BindTextureSets(Textures.Sets);
            LogicalLayers = new TexturePaintLogicalLayerController(Reconstruction.logicalTargets);
            if (addDefaultWhite) AddDefaultWhiteLayer();
            Painting = new PaintingEngine(stroke, blur, normal, ribbonShader);
            Plugins = new PluginHost();
            Plugins.LogicalLayers = LogicalLayers;
            Plugins.Discover();
        }

        private void AddDefaultWhiteLayer()
        {
            if (Textures?.Sets == null || Textures.Sets.Count == 0 ||
                Reconstruction?.logicalTargets?.Targets.Count == 0) return;
            TextureSet primary = Textures.Sets[0];
            TexturePaintChannel channel = TexturePaintChannel.Albedo;
#if UNITY_EDITOR
            TexturePaintMaterialChannelCapability first = primary.materialCapability?.GetChannel(0);
            if (first != null && first.LogicalChannels.Count > 0)
            {
                channel = first.LogicalChannels[0];
                for (int i = 0; i < first.LogicalChannels.Count; i++)
                    if (first.LogicalChannels[i] == TexturePaintChannel.Albedo) { channel = TexturePaintChannel.Albedo; break; }
                if (channel != TexturePaintChannel.Albedo)
                    Debug.LogWarning($"Overlay Painter created Default White on {channel} because the first " +
                        $"physical channel of '{primary.umaMaterial.name}' is not an albedo/color channel.");
            }
#endif
            if (primary.GetChannel(channel) == null)
                throw new InvalidOperationException("The first physical UMAMaterial channel has no editable logical component for Default White.");
            TexturePaintLayer layer = primary.AddFillLayer("Default White", channel, Color.white);
            if (layer == null) throw new InvalidOperationException("Default White could not be created for the selected material.");
            TexturePaintLogicalTarget target = LogicalLayers.FindTarget(primary);
            if (target == null || !LogicalLayers.LinkAndRepair(target, primary, layer, null, out _))
                throw new InvalidOperationException("Default White could not be linked across the standalone slot group.");
        }

        public void AttachDocument(TexturePaintDocument document)
        {
            Document = document;
            Document?.Migrate();
        }

        public void SaveRecipeState(TexturePaintStageState state)
        {
            if (Avatar?.umaData?.umaRecipe == null) return;
            var field = Avatar.umaData.umaRecipe.GetType().GetField("texturePaintStageState");
            field?.SetValue(Avatar.umaData.umaRecipe, JsonUtility.ToJson(state ?? CaptureState()));
        }

        public TexturePaintStageState LoadRecipeState()
        {
            object recipe = Avatar?.umaData?.umaRecipe;
            var field = recipe?.GetType().GetField("texturePaintStageState");
            string json = field?.GetValue(recipe) as string;
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<TexturePaintStageState>(json);
        }

        public TexturePaintStageState CaptureState()
        {
            TexturePaintStageState state = new TexturePaintStageState();
            if (Textures == null) return state;
            for (int i = 0; i < Textures.Sets.Count; i++)
            {
                TextureSet set = Textures.Sets[i];
                TexturePaintMaterialState material = new TexturePaintMaterialState { materialName = set.Name, surfaceIndex = set.surface.index, activeLayer = set.activeLayerIndex };
                for (int layerIndex = 0; layerIndex < set.layers.Count; layerIndex++)
                {
                    TexturePaintLayer layer = set.layers[layerIndex];
                    material.layers.Add(new TexturePaintLayerState
                    {
                        name = layer.name,
                        visible = layer.visible,
                        opacity = layer.opacity,
                        blendMode = layer.blendMode,
                        effects = layer.effects?.Clone() ?? new TexturePaintLayerEffects(),
                        isSplineLayer = layer.IsSplineLayer,
                        spline = layer.spline
                    });
                }
                state.materials.Add(material);
            }
            return state;
        }

        public void Dispose()
        {
            Plugins?.Dispose(); Painting?.Dispose(); Textures?.Dispose(); Reconstruction?.Dispose();
            Plugins = null; Painting = null; Textures = null; Reconstruction = null; LogicalLayers = null;
            Avatar = null; Document = null;
        }
    }

    [Serializable]
    public sealed class TexturePaintStageState
    {
        public const int CurrentVersion = 16;
        public int version = CurrentVersion;
        public string documentGuid;
        public int selectedSurface;
        public System.Collections.Generic.List<string> selectedSlots = new System.Collections.Generic.List<string>();
        public TexturePaintChannel selectedChannel = TexturePaintChannel.Albedo;
        public TexturePaintSourceMode sourceMode = TexturePaintSourceMode.SourceOverlay;
        public TexturePaintTool tool;
        public TexturePaintBrushSource paintSource = TexturePaintBrushSource.Color;
        public string sourceTextureGuid;
        public string sourceSpriteGlobalId;
        public string sourceOverlayGuid;
        public Color sourceColor = Color.white;
        public bool mirrorX;
        public bool limitStrokeCoverage;
        public TexturePaintNormalConvention normalConvention = TexturePaintNormalConvention.OpenGL;
        public float strokeStabilization;
        public float directionSmoothing = 0.35f;
        public float projectionDepth = 0.5f;
        public float normalAngleLimit = 90f;
        public bool paintBackfaces;
        public bool pressureAffectsFlow = true;
        public bool pressureAffectsSize;
        public int historyBudgetMB = 256;
        public int coverageBudgetMB = 128;
        public string brushAssetGuid;
        public string brushLibraryGuid;
        public string exportFolder = UMAPathUtility.OverlayPainterGeneratedRoot;
        public string exportTemplateGuid;
        public System.Collections.Generic.List<TexturePaintMaterialState> materials = new System.Collections.Generic.List<TexturePaintMaterialState>();
        public System.Collections.Generic.List<string> exportedTexturePaths = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<TexturePaintExportRecord> exportRecords = new System.Collections.Generic.List<TexturePaintExportRecord>();
        public System.Collections.Generic.List<TexturePaintPluginProfile> pluginProfiles = new System.Collections.Generic.List<TexturePaintPluginProfile>();
        public float workspaceLeftWidth = 238f;
        public float workspaceRightWidth = 318f;
        public float workspaceShelfHeight = 178f;
        public bool workspaceShowToolRail = true;
        public bool workspaceShowTargets = true;
        public bool workspaceShowLayers = true;
        public bool workspaceShowProperties = true;
        public bool workspaceShowAssetShelf = true;
        public bool workspaceShowUV = true;
        public int workspaceLeftTab;
        public int workspaceRightTab;
        public Vector2 workspaceUVPan;
        public float workspaceUVZoom = 1f;
        public bool channelSolo;
        public bool previewBefore;
        public bool uvPreviewBefore;
        public bool layerMaskMode;
        public bool soloLayerMask;
        public float layerMaskPaintValue = 1f;
        public bool isolateSelectedSlots;
        public bool wireframe;
        public string assetShelfSearch;
        public string assetShelfFolder;
        public bool assetShelfFavoritesOnly;
        public bool assetShelfRecentOnly;
        public System.Collections.Generic.List<string> favoriteBrushGuids = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> recentBrushGuids = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> brushOrderGuids = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> collapsedLayerGroupIds = new System.Collections.Generic.List<string>();
        public System.Collections.Generic.List<string> collapsedPropertySectionIds = new System.Collections.Generic.List<string>();
    }

    [Serializable]
    public sealed class TexturePaintMaterialState
    {
        public string materialName;
        public int surfaceIndex;
        public int activeLayer = -1;
        public System.Collections.Generic.List<TexturePaintLayerState> layers = new System.Collections.Generic.List<TexturePaintLayerState>();
    }

    [Serializable]
    public sealed class TexturePaintLayerState
    {
        public string name;
        public bool visible = true;
        public float opacity = 1f;
        public TexturePaintBlendMode blendMode;
        public TexturePaintLayerEffects effects = new TexturePaintLayerEffects();
        public bool isSplineLayer;
        public TexturePaintSpline spline;
    }
}
