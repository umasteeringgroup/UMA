using System;
using System.Collections.Generic;
using System.IO;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Unity.Profiling;

namespace UMA.HairCards.Editor
{
    public sealed class HairCardStage : PreviewSceneStage
    {
        private const double RebuildDelay = 0.04d;
        private static readonly ProfilerMarker PreviewRefreshMarker = new ProfilerMarker("HairCards.RefreshPreview");
        private static readonly ProfilerMarker HighlightMarker = new ProfilerMarker("HairCards.RefreshHighlight");
        private static readonly ProfilerMarker PreviewValidationMarker = new ProfilerMarker("HairCards.ValidatePreview");
        private const double InteractiveGuideRefreshInterval = 1d / 30d;
        private const double AutosaveDelay = 30d;
        private const int SceneInputControlHint = 0x48414952;
        private const int BrushModifierControlHint = 0x48425253;
        private static bool eventsHooked;

        private readonly struct HairSurfaceHit
        {
            internal readonly int TriangleIndex;
            internal readonly Vector3 WorldPoint;
            internal readonly Vector3 WorldNormal;
            internal readonly Vector3 SourcePoint;
            internal readonly Vector3 SourceNormal;

            internal HairSurfaceHit(int triangleIndex, Vector3 worldPoint, Vector3 worldNormal,
                Vector3 sourcePoint, Vector3 sourceNormal)
            {
                TriangleIndex = triangleIndex;
                WorldPoint = worldPoint;
                WorldNormal = worldNormal;
                SourcePoint = sourcePoint;
                SourceNormal = sourceNormal;
            }
        }

        private sealed class CurveBrushEntry
        {
            internal HairGuide Guide;
            internal Matrix4x4 GuideToPose;
            internal Matrix4x4 PoseToGuide;
            internal Bounds PosedBounds;
            internal float PoseRadiusScale;
            internal Vector3[] SourcePoints = Array.Empty<Vector3>();
            internal Vector3[] PosedPoints = Array.Empty<Vector3>();
            internal Vector3[] ReferencePoints = Array.Empty<Vector3>();
            internal Vector3[] OriginalPoints = Array.Empty<Vector3>();
            internal Vector3[] TargetPoints = Array.Empty<Vector3>();
            internal float[] PointInfluences = Array.Empty<float>();
        }

        private struct MapStrokeSample
        {
            internal float initialValue;
            internal float maximumInfluence;
        }

        [SerializeField] private HairGroomAsset groom;
        [SerializeField] private DynamicCharacterAvatar sourceAvatar;
        [SerializeField] private HairWorkflowStep workflowStep;
        [SerializeField] private HairSceneTool sceneTool = HairSceneTool.Select;
        [SerializeField] private HairPreviewMode previewMode = HairPreviewMode.Cards;
        [SerializeField] private bool hasGeneratedCardPreview;
        [SerializeField] private HairSceneTool lastGrowthTool = HairSceneTool.PaintGrowth;
        [SerializeField] private HairSceneTool lastGuideTool = HairSceneTool.Select;
        [SerializeField] private HairSceneTool lastGroomTool = HairSceneTool.Comb;
        [SerializeField] private string activeGroupId;
        [SerializeField] private string activeMapId;
        [SerializeField] private string activeGuideId;
        [SerializeField] private string activeLayerId;
        [SerializeField] private string activeModifierId;
        [SerializeField] private string activeHelperId;
        [SerializeField] private int activeGuidePoint = -1;
        [SerializeField] private int lodLevel;
        [SerializeField] private float brushRadius = 0.075f;
        [SerializeField] private float brushHardness = HairBrushInteractionUtility.DefaultHardness;
        [SerializeField] private float brushStrength = 0.5f;
        [SerializeField] private float brushRootInfluence = 1f;
        [SerializeField] private bool affectThroughDepth = true;
        [SerializeField] private HairBrushScope brushScope;
        [SerializeField] private HairPreviewQuality previewQuality;
        [SerializeField] private List<string> selectedGuideIds = new List<string>();
        [SerializeField] private bool isolateSelectedGuides;
        [SerializeField] private string soloLayerId;
        [SerializeField] private bool gravityCollision;
        [SerializeField] private float gravityClearance = 0.002f;
        [SerializeField] private float gravityStrength = 2.5f;
        [SerializeField] private float gravitySeparation = 0.35f;
        [SerializeField] private float paintValue = 1f;
        // Destructive brush mode is session-only: never restore it from preferences or stage serialization.
        [NonSerialized] private bool paintErase;
        [NonSerialized] private bool shiftPaintErase;
        private GUIStyle sceneHelpStyle;
        [SerializeField] private bool mirrorPaintX;
        [SerializeField] private bool mirrorCutX;
        [SerializeField] private bool showScalp = true;
        [SerializeField] private bool showAvatar = true;
        [SerializeField] private bool showChildren = true;
        [SerializeField] private bool showChildSplines = true;
        [SerializeField] private bool showHelpers = true;
        [SerializeField] private bool showControlPoints = true;
        [SerializeField] private bool showGuideRoots = true;
        [SerializeField] private bool showGuideSplines = true;
        [SerializeField] private bool showCardWireframe = true;
        [SerializeField] private bool depthTestGuides = true;
        [SerializeField] private bool showFreezeMask;
        [SerializeField] private float rootHandleScale = 1f;
        [SerializeField] private List<string> hiddenAvatarSlots = new List<string>();
        [SerializeField] private HairGuideGenerationSettings guideGeneration = new HairGuideGenerationSettings();
        [SerializeField] private List<int> selectedVertices = new List<int>();
        [SerializeField] private List<UnityEngine.Object> editedResources = new List<UnityEngine.Object>();

        private GameObject sourceSpaceObject;
        private GameObject scalpObject;
        private GameObject hairObject;
        private MeshFilter scalpFilter;
        private MeshCollider scalpCollider;
        private MeshRenderer scalpRenderer;
        private GameObject growthOverlayObject;
        private MeshFilter growthOverlayFilter;
        private MeshRenderer growthOverlayRenderer;
        private Material growthOverlayMaterial;
        private Mesh growthOverlayMesh;
        private Mesh growthOverlaySourceMesh;
        private Color32[] growthOverlayColors = Array.Empty<Color32>();
        private MeshRenderer hairRenderer;
        private MeshFilter hairFilter;
        private GameObject lightingObject;
        private Material scalpMaterial;
        private Material fallbackHairMaterial;
        private readonly HairPreviewMaterialSet hairPreviewMaterials = new HairPreviewMaterialSet();
        private HairEvaluationResult evaluation;
        private HairCardMeshBuildResult meshBuild;
        private HairEvaluationResult posedEvaluation;
        private HairEvaluationResult interactiveEvaluation;
        private HairValidationReport validation;
        private HairValidationReport releaseValidation;
        private bool releaseValidationStale = true;
        private bool releaseValidationQueued;
        private int releaseValidationStamp;
        private HairGuideGenerationResult generationPreview;
        private bool rebuildQueued;
        private double rebuildNotBefore;
        private bool interactiveGuideRefreshQueued;
        private double nextInteractiveGuideRefresh;
        private double nextAutosave;
        private double nextPreferencesSave;
        internal bool IsEditing => strokeActive || workspaceInteractionActive;
        private bool strokeActive;
        private bool pointHandleStrokeActive;
        private bool saveRequested;
        private bool workspaceInteractionActive;
        private double lastWorkspaceInteraction;
        private double lastSavedAt;
        private HairPreviewChange pendingPreviewChanges = HairPreviewChange.All;
        private readonly HashSet<string> selectedGuideSet = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> brushHighlights = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> slicePreview = new Dictionary<string, float>(StringComparer.Ordinal);
        private HairMeshRaycaster gravityRaycaster;
        private HairMeshRaycaster cardRaycaster;
        private Mesh gravitySurface;
        private HairAtlasProfileAsset highlightedAtlas;
        private string highlightedUvId;
        private Vector3[] cardHighlightLines = Array.Empty<Vector3>();
        private readonly List<Vector3> highlightVertices = new List<Vector3>();
        private readonly List<List<int>> highlightSubmeshes = new List<List<int>>();
        private List<int> highlightTriangles = new List<int>(), nextHighlightTriangles = new List<int>();
        private readonly List<int> highlightEdgeIndices = new List<int>();
        private readonly HashSet<ulong> highlightEdges = new HashSet<ulong>();
        private Camera brushCamera;
        private int strokeUndoGroup = -1;
        private bool gravitySimulationActive;
        private double lastGravityUpdate;
        private double nextGravityStep;
        private Plane curveStrokePlane;
        private Vector3 previousCurvePlanePoint;
        private bool curveStrokePlaneValid;
        private float curveStrokePoseRadiusScale = 1f;
        private bool sliceCutActive;
        private Vector2 sliceStartMouse;
        private Vector2 sliceEndMouse;
        private bool modifierBrushDrag;
        private int modifierBrushHotControl;
        private Vector2 modifierBrushStartMouse;
        private Vector2 modifierBrushCurrentMouse;
        private float modifierBrushStartRadius;
        private float modifierBrushStartHardness;
        private bool modifierBrushCursorValid;
        private Vector3 modifierBrushWorldCenter;
        private Vector3 modifierBrushWorldNormal;
        private float modifierBrushPoseRadiusScale = 1f;
        private bool modifierBrushMirrorValid;
        private Vector3 modifierBrushMirrorCenter;
        private Vector3 modifierBrushMirrorNormal;
        private Vector3 previousStrokePosition;
        private bool hasPreviousStrokePosition;
        private int sceneInputHotControl;
        private Tool previousUnityTool;
        private bool previousToolsHidden;
        private bool unityToolStateCaptured;
        private bool needsFrame = true;
        private Bounds? pendingFocusBounds;
        private Vector3?[] authoringBonePositions;
        private Vector3? authoringHeadPosition;
        private Vector3? authoringNeckPosition;
        private Vector3? pendingFocusPoint;
        private bool closing;
        private HairVertexSpatialIndex vertexSpatialIndex;
        private HairAvatarPreview avatarPreview;
        private HairAuthoringPose authoringPose;
        private HairAvatarVisibilityCatalog visibilityCatalog;
        private HairSourceVisibility sourceVisibility;
        private HairMeshRaycaster surfaceRaycaster;
        private HairMeshRaycaster sourceSurfaceRaycaster;
        private Mesh raycastSurfaceMesh;
        private Mesh authoringSurfaceMesh;
        private Vector3[] sourceVertices = Array.Empty<Vector3>();
        private Vector3[] sourceNormals = Array.Empty<Vector3>();
        private string actionStatus = "Ready";
        private readonly HashSet<string> hiddenSlotSet = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<int> brushVertices = new List<int>();
        private readonly List<int> mirroredBrushVertices = new List<int>();
        private readonly HashSet<int> combinedBrushVertices = new HashSet<int>();
        private readonly Dictionary<int, MapStrokeSample> mapStrokeSamples = new Dictionary<int, MapStrokeSample>();
        private HairGrowthMap mapStrokeMap;
        private float[] mapStrokeValues;
        private float mapStrokeTarget;
        private readonly Dictionary<string, HairEvaluatedCurve> displayGuideCurves =
            new Dictionary<string, HairEvaluatedCurve>(StringComparer.Ordinal);
        private readonly Dictionary<string, Vector3[]> displayGuidePolylines =
            new Dictionary<string, Vector3[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, Matrix4x4> helperPoseMatrices =
            new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
        private readonly List<CurveBrushEntry> curveBrushEntries = new List<CurveBrushEntry>();
        private readonly Dictionary<string, CurveBrushEntry> retainedBrushEntries =
            new Dictionary<string, CurveBrushEntry>(StringComparer.Ordinal);
        private readonly List<string> staleGuideBufferIds = new List<string>();
        private readonly HashSet<string> activeBrushBufferIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HairEvaluationWorkspace evaluationWorkspace = new HairEvaluationWorkspace();
        private readonly HairMeshBuildWorkspace meshWorkspace = new HairMeshBuildWorkspace();
        private readonly HairGuideDepthRenderer guideDepthRenderer = new HairGuideDepthRenderer();
        private HairSculptLayer strokeSculptLayer;
        private readonly Dictionary<string, HairGuideDelta> strokeLayerDeltas =
            new Dictionary<string, HairGuideDelta>(StringComparer.Ordinal);

        public static HairCardStage ActiveStage { get; private set; }
        public HairGroomAsset Groom => groom;
        public DynamicCharacterAvatar SourceAvatar => sourceAvatar;
        public HairWorkflowStep WorkflowStep
        {
            get => workflowStep;
            set => ActivateWorkflowStep(value);
        }
        public HairSceneTool SceneTool
        {
            get => sceneTool;
            set
            {
                HairWorkflowStep targetStep = HairWorkflowState.StepForTool(value, workflowStep);
                if (sceneTool == value && targetStep == workflowStep &&
                    HairWorkflowState.IsToolAllowed(workflowStep, value)) return;
                EndGravitySimulation();
                ReleaseSceneInputCapture(true);
                if (targetStep != workflowStep)
                {
                    RememberToolForStep(workflowStep, sceneTool);
                    workflowStep = targetStep;
                    previewMode = DefaultPreviewForStep(targetStep);
                }
                sceneTool = value;
                RememberToolForStep(workflowStep, sceneTool);
                if (sceneTool == HairSceneTool.PaintGrowth) previewMode = HairPreviewMode.GrowthMap;
                actionStatus = ToolInstruction(sceneTool);
                CaptureUnityToolState();
                QueueRebuild();
                RepaintAll();
            }
        }
        public HairPreviewMode PreviewMode
        {
            get => previewMode;
            set
            {
                if (previewMode == value) return;
                bool sameGeometry = IsCardPreviewMode(previewMode) && IsCardPreviewMode(value);
                previewMode = value;
                QueuePreviewChange(sameGeometry ? HairPreviewChange.Display | HairPreviewChange.Materials : HairPreviewChange.Evaluation | HairPreviewChange.Display);
            }
        }
        public int LodLevel { get => lodLevel; set { value = Mathf.Max(0, value); if (lodLevel == value) return; lodLevel = value; QueueRebuild(); } }
        public float BrushRadius
        {
            get => brushRadius;
            set => brushRadius = Mathf.Clamp(value, HairBrushInteractionUtility.MinimumRadius,
                HairBrushInteractionUtility.MaximumRadius);
        }
        public float BrushHardness { get => brushHardness; set => brushHardness = Mathf.Clamp01(value); }
        public float BrushStrength { get => brushStrength; set => brushStrength = Mathf.Clamp01(value); }
        public float BrushRootInfluence { get => brushRootInfluence; set => brushRootInfluence = float.IsFinite(value) ? Mathf.Clamp01(value) : 0f; }
        public bool ShowCardWireframe
        {
            get => showCardWireframe;
            set
            {
                if (showCardWireframe == value) return;
                showCardWireframe = value;
                RefreshCardHighlight();
                UpdateHairPreviewVisibility();
                RepaintAll();
            }
        }
        public bool AffectThroughDepth
        {
            get => affectThroughDepth;
            set
            {
                if (affectThroughDepth == value) return;
                affectThroughDepth = value;
                brushScope = value ? HairBrushScope.ThroughDepth : HairBrushScope.DepthVolume;
                actionStatus = affectThroughDepth
                    ? "Groom brushes affect every displayed guide under the camera-facing brush circle."
                    : "Groom brushes use a local 3D volume for depth-isolated edits.";
                RepaintAll();
            }
        }
        public float GravityStrength
        {
            get => gravityStrength;
            set => gravityStrength = Mathf.Clamp(value, 0.1f, 8f);
        }
        public float GravitySeparation
        {
            get => gravitySeparation;
            set => gravitySeparation = Mathf.Clamp01(value);
        }
        public bool GravitySimulationActive => gravitySimulationActive;
        public bool GravityCollision { get => gravityCollision; set => gravityCollision = value; }
        public float GravityClearance { get => gravityClearance; set => gravityClearance = Mathf.Clamp(value, 0f, 0.05f); }
        public HairBrushScope BrushScope
        {
            get => brushScope;
            set { if (brushScope == value) return; brushScope = value; affectThroughDepth = value != HairBrushScope.DepthVolume; brushHighlights.Clear(); RepaintAll(); }
        }
        public HairPreviewQuality PreviewQuality
        {
            get => previewQuality;
            set { if (previewQuality == value) return; previewQuality = value; QueuePreviewChange(HairPreviewChange.Evaluation); }
        }
        public string PreviewStatus => rebuildQueued ? "Updating preview…" : previewQuality == HairPreviewQuality.Draft ? "Draft preview · release uses Full" : "Full preview";
        public string SaveStatus => saveRequested ? "Save queued" : HasUnsavedChanges ? "Unsaved · autosave when idle" : lastSavedAt > 0d ? "Saved " + DateTime.Now.AddSeconds(lastSavedAt - EditorApplication.timeSinceStartup).ToString("HH:mm:ss") : "Saved";
        public IReadOnlyList<string> SelectedGuideIds => selectedGuideIds;
        public int AffectedGuideCount => brushHighlights.Count;
        public int SlicePreviewCount => slicePreview.Count;
        public string SoloLayerId => soloLayerId;
        public bool IsolateSelectedGuides
        {
            get => isolateSelectedGuides;
            set { if (isolateSelectedGuides == value) return; isolateSelectedGuides = value; QueuePreviewChange(HairPreviewChange.Evaluation); }
        }
        public bool IsGuideSelected(string id) => selectedGuideSet.Contains(id);

        public void SetGuideSelection(IEnumerable<string> ids)
        {
            EndGravitySimulation();
            selectedGuideSet.Clear();
            if (ids != null) foreach (string id in ids)
                if (ActiveGroup?.guides.Exists(guide => guide != null && guide.Id == id) == true) selectedGuideSet.Add(id);
            selectedGuideIds = new List<string>(selectedGuideSet);
            if (!selectedGuideSet.Contains(activeGuideId))
            { activeGuideId = selectedGuideIds.Count > 0 ? selectedGuideIds[0] : null; activeGuidePoint = -1; }
            brushHighlights.Clear();
            if (isolateSelectedGuides) QueuePreviewChange(HairPreviewChange.Evaluation);
            RepaintAll();
        }

        public void ToggleGuideSelection(string id)
        {
            List<string> ids = new List<string>(selectedGuideIds);
            if (!ids.Remove(id)) ids.Add(id);
            SetGuideSelection(ids);
        }

        public void SoloLayer(string id)
        {
            EndGravitySimulation();
            soloLayerId = soloLayerId == id ? null : ActiveGroup?.sculptLayers.Find(layer => layer != null && layer.Id == id)?.Id;
            if (!string.IsNullOrEmpty(soloLayerId)) { activeLayerId = soloLayerId; activeModifierId = null; }
            QueuePreviewChange(HairPreviewChange.Evaluation);
        }

        internal void SetWorkspaceInteraction(bool active, bool changed = false)
        {
            if (active || changed || workspaceInteractionActive != active) lastWorkspaceInteraction = EditorApplication.timeSinceStartup;
            workspaceInteractionActive = active;
        }
        public float PaintValue { get => paintValue; set => paintValue = value; }
        public bool PaintErase { get => paintErase; set => paintErase = value; }
        public bool EffectivePaintErase => paintErase || (sceneTool == HairSceneTool.PaintGrowth && shiftPaintErase);

        internal bool UpdateTemporaryPaintErase(Event current)
        {
            bool held = current != null && sceneTool == HairSceneTool.PaintGrowth && current.shift && !current.alt;
            if (current != null)
            {
                bool shiftKey = current.keyCode == KeyCode.LeftShift || current.keyCode == KeyCode.RightShift;
                if (shiftKey && current.rawType == EventType.KeyDown && sceneTool == HairSceneTool.PaintGrowth && !current.alt) held = true;
                if ((shiftKey && current.rawType == EventType.KeyUp) || current.rawType == EventType.MouseLeaveWindow ||
                    current.rawType == EventType.Ignore || (current.rawType == EventType.KeyDown && current.keyCode == KeyCode.Escape)) held = false;
            }
            if (shiftPaintErase == held) return false;
            shiftPaintErase = held;
            return true;
        }

        internal string SceneHelpText => sceneTool == HairSceneTool.PaintGrowth
            ? $"{(EffectivePaintErase ? "ERASE" : "PAINT")} · {ActiveMap?.name ?? "Active map"}\n" +
              "Left-drag: paint • Hold Shift: erase • Release Shift: restore selected mode\n" +
              "M: mirror • [ / ]: radius • Shift + [ / ]: hardness\n" +
              "Shift + right-drag: radius / hardness • Alt: camera navigation"
            : ToolInstruction(sceneTool) + (SupportsBrushAdjustment()
                ? "\n[ / ]: radius • Shift + [ / ]: hardness\nShift + right-drag: radius / hardness • Alt: camera navigation"
                : "\nAlt: camera navigation" + (sceneTool == HairSceneTool.Cut ? " • M: mirror slice" : string.Empty));
        public bool MirrorPaintX
        {
            get => mirrorPaintX;
            set
            {
                if (mirrorPaintX == value) return;
                mirrorPaintX = value;
                actionStatus = mirrorPaintX
                    ? "Growth painting X mirror enabled. Both sides use the source mesh local X = 0 plane."
                    : "Growth painting X mirror disabled.";
                RepaintAll();
            }
        }
        public bool MirrorCutX
        {
            get => mirrorCutX;
            set
            {
                if (mirrorCutX == value) return;
                mirrorCutX = value;
                actionStatus = mirrorCutX
                    ? "Slice Cut X mirror enabled. The gesture trims both sides across source-local X = 0."
                    : "Slice Cut X mirror disabled.";
                RepaintAll();
            }
        }
        public bool ShowScalp { get => showScalp; set { if (showScalp == value) return; showScalp = value; if (scalpRenderer != null) scalpRenderer.enabled = value; RepaintAll(); } }
        public bool ShowAvatar { get => showAvatar; set { if (showAvatar == value) return; showAvatar = value; avatarPreview?.ApplyVisibility(value, hiddenSlotSet); RepaintAll(); } }
        public bool ShowChildren { get => showChildren; set { if (showChildren == value) return; showChildren = value; QueueRebuild(); } }
        public bool ShowChildSplines { get => showChildSplines; set { if (showChildSplines == value) return; showChildSplines = value; RepaintAll(); } }
        public bool ShowHelpers { get => showHelpers; set { if (showHelpers == value) return; showHelpers = value; RepaintAll(); } }
        public bool ShowControlPoints { get => showControlPoints; set { if (showControlPoints == value) return; showControlPoints = value; RepaintAll(); } }
        public bool ShowGuideRoots { get => showGuideRoots; set { if (showGuideRoots == value) return; showGuideRoots = value; RepaintAll(); } }
        public bool ShowGuideSplines { get => showGuideSplines; set { if (showGuideSplines == value) return; showGuideSplines = value; RepaintAll(); } }
        public bool DepthTestGuides { get => depthTestGuides; set { if (depthTestGuides == value) return; depthTestGuides = value; RepaintAll(); } }
        public bool ShowFreezeMask { get => showFreezeMask; set { if (showFreezeMask == value) return; showFreezeMask = value; RepaintAll(); } }
        public float RootHandleScale
        {
            get => rootHandleScale;
            set
            {
                float clamped = Mathf.Clamp(value, 0.1f, 4f);
                if (Mathf.Approximately(rootHandleScale, clamped)) return;
                rootHandleScale = clamped;
                RepaintAll();
            }
        }
        public HairGuideGenerationSettings GuideGeneration => guideGeneration;
        public HairEvaluationResult Evaluation => evaluation;
        public HairCardMeshBuildResult MeshBuild => meshBuild;
        public HairValidationReport Validation => validation;
        public HairValidationReport ReleaseValidation => releaseValidation;
        public bool ReleaseValidationIsCurrent => releaseValidation != null && !releaseValidationStale &&
            releaseValidationStamp == WorkspaceChangeStamp();
        public bool HasUnsavedChanges => WorkspaceAssets().Exists(asset =>
            asset != null && EditorUtility.IsDirty(asset));
        public HairGuideGenerationResult GenerationPreview => generationPreview;
        public string ActiveGroupId => activeGroupId;
        public string ActiveMapId => activeMapId;
        public string ActiveGuideId => activeGuideId;
        public string ActiveLayerId => activeLayerId;
        public HairSculptLayer ActiveLayer => ActiveGroup?.sculptLayers?.Find(layer => layer != null && layer.Id == activeLayerId);
        public HairModifierSettings ActiveModifier => ActiveLayer?.modifiers?.Find(modifier => modifier != null && modifier.Id == activeModifierId);
        public string ActiveModifierId => ActiveModifier?.Id;
        public string ActiveHelperId => activeHelperId;
        public int SelectedVertexCount => selectedVertices?.Count ?? 0;
        public int PaintableTriangleCount => surfaceRaycaster?.TriangleCount ?? 0;
        public string ActionStatus => actionStatus;
        public HairGroup ActiveGroup => groom?.FindGroup(activeGroupId) ?? FirstGroup();
        public HairGrowthMap ActiveMap => ActiveGroup?.maps?.Find(map => map != null && map.Id == activeMapId)
                                          ?? ActiveGroup?.FindMap(HairMapKind.GrowthArea);
        internal bool HasAvatarVisibility => visibilityCatalog != null && visibilityCatalog.SlotNames.Count > 0;
        internal IReadOnlyList<HairAvatarVisibilityGroup> RecipeVisibilityGroups =>
            visibilityCatalog?.RecipeGroups ?? Array.Empty<HairAvatarVisibilityGroup>();
        internal IReadOnlyList<HairAvatarVisibilityGroup> UdimVisibilityGroups =>
            visibilityCatalog?.UdimGroups ?? Array.Empty<HairAvatarVisibilityGroup>();
        internal IReadOnlyList<HairAvatarVisibilityGroup> SlotVisibilityGroups =>
            visibilityCatalog?.SlotGroups ?? Array.Empty<HairAvatarVisibilityGroup>();

        public static HairCardStage ShowStage(HairGroomAsset asset, DynamicCharacterAvatar avatar = null)
        {
            if (asset == null) return null;
            if (avatar != null && string.IsNullOrEmpty(asset.SourceObjectId))
                HairGroomSourcePersistence.RememberOrigin(asset, avatar);
            if (avatar == null) avatar = HairGroomSourcePersistence.ResolveAvatar(asset);
            HairCardStage stage = CreateInstance<HairCardStage>();
            stage.groom = asset;
            stage.sourceAvatar = avatar;
            stage.showScalp = avatar == null;
            stage.showAvatar = avatar != null;
            HairEditorPreferences.instance.Restore(stage, "stage", HairEditorPreferences.Context(asset));
            StageUtility.GoToStage(stage, true);
            return stage;
        }

        protected override GUIContent CreateHeaderContent()
        {
            return new GUIContent("Hair Cards", EditorGUIUtility.IconContent("Mesh Icon").image);
        }

        protected override bool OnOpenStage()
        {
            base.OnOpenStage();
            if (groom == null)
            {
                EditorUtility.DisplayDialog("Hair Card Stage", "No HairGroomAsset was supplied.", "OK");
                return false;
            }
            if (!HairGroomSourcePersistence.TryEnsureSource(groom, out string sourceProblem))
            {
                EditorUtility.DisplayDialog("Hair Card Stage",
                    sourceProblem, "OK");
                return false;
            }
            groom.EnsureIntegrity();

            EnsureEditorEvents();
            foreach (HairGroup existingGroup in groom.Groups)
                HairGroomCommands.ImportLegacyModifiers(groom, existingGroup);
            activeGroupId = groom.FindGroup(activeGroupId)?.Id ?? FirstGroup()?.Id;
            HairGroup group = ActiveGroup;
            activeMapId = group?.maps?.Find(map => map != null && map.Id == activeMapId)?.Id ??
                          group?.FindMap(HairMapKind.GrowthArea)?.Id;
            NormalizeStackSelection();
            NormalizeWorkflowState();

            CreatePreviewObjects();
            vertexSpatialIndex = new HairVertexSpatialIndex(groom.SourceMesh);
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += EditorUpdate;
            CaptureUnityToolState();
            ActiveStage = this;
            QueueRebuild(true);
            nextAutosave = EditorApplication.timeSinceStartup + AutosaveDelay;
            HairGroomWorkspace.OpenForActiveStage();
            return true;
        }

        private void ActivateWorkflowStep(HairWorkflowStep step)
        {
            bool validCurrentState = workflowStep == step && HairWorkflowState.IsToolAllowed(step, sceneTool) &&
                                     IsPreviewUsefulForStep(step, previewMode);
            if (validCurrentState) return;
            EndGravitySimulation();
            ReleaseSceneInputCapture(true);
            RememberToolForStep(workflowStep, sceneTool);
            workflowStep = step;
            sceneTool = RememberedToolForStep(step);
            previewMode = DefaultPreviewForStep(step);
            actionStatus = StepInstruction(step);
            CaptureUnityToolState();
            QueueRebuild();
            RepaintAll();
        }

        private void NormalizeWorkflowState()
        {
            if (HairWorkflowState.IsToolAllowed(workflowStep, sceneTool))
                RememberToolForStep(workflowStep, sceneTool);
            else
                sceneTool = RememberedToolForStep(workflowStep);
            if (workflowStep == HairWorkflowStep.Growth ||
                !IsPreviewUsefulForStep(workflowStep, previewMode))
                previewMode = DefaultPreviewForStep(workflowStep);
            actionStatus = StepInstruction(workflowStep);
        }

        private HairPreviewMode DefaultPreviewForStep(HairWorkflowStep step)
        {
            // After inspecting generated cards, returning to Groom should show the result of each
            // completed stroke. The Preview menu still allows an explicit guide-only view.
            return step == HairWorkflowStep.Groom && hasGeneratedCardPreview
                ? HairPreviewMode.Cards
                : HairWorkflowState.DefaultPreview(step);
        }

        private static bool IsCardPreviewMode(HairPreviewMode mode)
        {
            return mode == HairPreviewMode.Cards || mode == HairPreviewMode.CardGroups ||
                   mode == HairPreviewMode.Wireframe;
        }

        private HairSceneTool RememberedToolForStep(HairWorkflowStep step)
        {
            HairSceneTool remembered = step switch
            {
                HairWorkflowStep.Growth => lastGrowthTool,
                HairWorkflowStep.Guides => lastGuideTool,
                HairWorkflowStep.Groom => lastGroomTool,
                _ => HairWorkflowState.DefaultTool(step)
            };
            return HairWorkflowState.IsToolAllowed(step, remembered)
                ? remembered : HairWorkflowState.DefaultTool(step);
        }

        private void RememberToolForStep(HairWorkflowStep step, HairSceneTool tool)
        {
            if (!HairWorkflowState.IsToolAllowed(step, tool)) return;
            switch (step)
            {
                case HairWorkflowStep.Growth: lastGrowthTool = tool; break;
                case HairWorkflowStep.Guides: lastGuideTool = tool; break;
                case HairWorkflowStep.Groom: lastGroomTool = tool; break;
            }
        }

        private static bool IsPreviewUsefulForStep(HairWorkflowStep step, HairPreviewMode mode)
        {
            return step switch
            {
                HairWorkflowStep.Growth => mode == HairPreviewMode.GrowthMap,
                HairWorkflowStep.Guides => mode == HairPreviewMode.Guides ||
                                           mode == HairPreviewMode.GuidesAndChildren,
                HairWorkflowStep.Groom => mode == HairPreviewMode.Guides ||
                                          mode == HairPreviewMode.GuidesAndChildren ||
                                          mode == HairPreviewMode.Cards,
                _ => mode != HairPreviewMode.GrowthMap
            };
        }

        private static string StepInstruction(HairWorkflowStep step)
        {
            return step switch
            {
                HairWorkflowStep.Growth => "Paint or select the Growth Area.",
                HairWorkflowStep.Guides => "Generate a guide preview, accept it, or place guides manually.",
                HairWorkflowStep.Groom => "Choose a brush and drag across the guide curves to style them.",
                HairWorkflowStep.Cards => "Configure children and card geometry, then rebuild the preview.",
                HairWorkflowStep.Optimize => "Inspect LOD and geometry budgets.",
                HairWorkflowStep.ValidateAndBake => "Validate the groom, run a dry run, then bake.",
                _ => "Confirm the source and preview setup."
            };
        }

        private static string ToolInstruction(HairSceneTool tool)
        {
            return tool switch
            {
                HairSceneTool.PaintGrowth => "Drag on the visible source surface to paint the active map.",
                HairSceneTool.PlaceGuide => "Click the source surface to place a guide along its normal.",
                HairSceneTool.DrawGuide => "Drag on the source surface to draw an anchored guide.",
                HairSceneTool.Select => "Select guides/control points or source vertices for the current step.",
                HairSceneTool.Comb => "Drag in screen space to comb. Roots and every guide segment stay locked in length.",
                HairSceneTool.Grab => "Drag nearby points while preserving guide segment lengths.",
                HairSceneTool.Smooth => "Relax nearby curvature without shrinking the guides.",
                HairSceneTool.Cut => "Drag a line across the view to slice intersecting guides and remove their tip side.",
                _ => $"Drag across guide curves with {ObjectNames.NicifyVariableName(tool.ToString())}."
            };
        }

        protected override void OnCloseStage()
        {
            closing = true;
            EndGravitySimulation();
            ReleaseSceneInputCapture(true);
            workspaceInteractionActive = false;
            SaveNow(false);
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= EditorUpdate;
            RestoreUnityToolState();
            if (ActiveStage == this) ActiveStage = null;
            DisposeBuild();
            sourceVisibility?.Dispose();
            sourceVisibility = null;
            surfaceRaycaster = null;
            sourceSurfaceRaycaster = null;
            gravityRaycaster = null;
            gravitySurface = null;
            raycastSurfaceMesh = null;
            avatarPreview?.Dispose();
            avatarPreview = null;
            authoringPose = null;
            DestroyPreviewObject(growthOverlayMesh);
            growthOverlayMesh = null;
            DestroyPreviewObject(growthOverlayMaterial);
            growthOverlayMaterial = null;
            DestroyPreviewObject(authoringSurfaceMesh);
            authoringSurfaceMesh = null;
            DestroyPreviewObject(scalpMaterial);
            DestroyPreviewObject(fallbackHairMaterial);
            DestroyPreviewObject(sourceSpaceObject);
            base.OnCloseStage();
        }

        protected override void OnFirstTimeOpenStageInSceneView(SceneView sceneView)
        {
            if (sceneView == null) return;
            sceneView.wantsMouseMove = true;
            sceneView.wantsMouseEnterLeaveWindow = true;
            CaptureUnityToolState();
        }

        public void SetActiveGroup(string groupId)
        {
            HairGroup group = groom?.FindGroup(groupId);
            if (group == null) return;
            EndGravitySimulation();
            activeGroupId = group.Id;
            activeMapId = group.FindMap(HairMapKind.GrowthArea)?.Id;
            activeGuideId = string.Empty;
            selectedGuideIds.Clear();
            selectedGuideSet.Clear();
            soloLayerId = null;
            activeGuidePoint = -1;
            activeLayerId = group.sculptLayers.Count > 0 ? group.sculptLayers[group.sculptLayers.Count - 1].Id : string.Empty;
            activeModifierId = null;
            generationPreview = null;
            RebuildCurveBrushCache();
            QueueRebuild();
        }

        public void SetActiveMap(string mapId)
        {
            HairGrowthMap map = ActiveGroup?.maps?.Find(candidate => candidate != null && candidate.Id == mapId);
            if (map == null) return;
            activeMapId = map.Id;
            previewMode = HairPreviewMode.GrowthMap;
            actionStatus = $"Painting {map.name}. Drag on the visible source surface.";
            UpdateGrowthOverlay(raycastSurfaceMesh, true);
            RepaintAll();
        }

        public void SetActiveGuide(string guideId, int pointIndex = -1)
        {
            HairGroup owner = null;
            HairGuide guide = groom != null ? groom.FindGuide(guideId, out owner) : null;
            if (guide == null) return;
            if (owner != null && owner.Id != activeGroupId) SetActiveGroup(owner.Id);
            activeGuideId = guide.Id;
            activeGuidePoint = pointIndex;
            SetGuideSelection(new[] { guide.Id });
            RepaintAll();
        }

        public void SetActiveLayer(string layerId)
        {
            EndGravitySimulation();
            activeLayerId = ActiveGroup?.sculptLayers?.Find(layer => layer != null && layer.Id == layerId)?.Id;
            activeModifierId = null;
            if (!string.IsNullOrEmpty(soloLayerId)) { soloLayerId = activeLayerId; QueuePreviewChange(HairPreviewChange.Evaluation); }
            RepaintAll();
        }

        public void SetActiveModifier(string layerId, string modifierId)
        {
            HairSculptLayer owner = ActiveGroup?.sculptLayers?.Find(layer => layer != null && layer.Id == layerId);
            HairModifierSettings modifier = owner?.modifiers?.Find(item => item != null && item.Id == modifierId);
            if (modifier == null) return;
            SetActiveLayer(owner.Id);
            activeModifierId = modifier.Id;
            RepaintAll();
        }

        private void NormalizeStackSelection()
        {
            activeLayerId = ActiveLayer?.Id ?? ActiveGroup?.sculptLayers?.FindLast(layer => layer != null)?.Id;
            activeModifierId = ActiveModifier?.Id;
            if (ActiveGroup?.sculptLayers?.Exists(layer => layer != null && layer.Id == soloLayerId) != true)
                soloLayerId = null;
        }

        public void SetGravityHeld(bool held)
        {
            if (held)
            {
                if (gravitySimulationActive) return;
                HairGroup group = ActiveGroup;
                if (workflowStep != HairWorkflowStep.Groom || group == null || group.locked ||
                    group.guides == null || group.guides.Count == 0)
                {
                    actionStatus = group?.locked == true
                        ? "Unlock the active group before applying gravity."
                        : "Gravity is available in Groom when the active group has guides.";
                    RepaintAll();
                    return;
                }
                ReleaseSceneInputCapture(true);
                gravitySimulationActive = true;
                lastGravityUpdate = EditorApplication.timeSinceStartup;
                nextGravityStep = lastGravityUpdate;
                BeginStroke("Settle Hair with Gravity");
                actionStatus = "Gravity settling: roots and segment lengths are locked; separation keeps cards fanned apart.";
                RepaintAll();
            }
            else
            {
                EndGravitySimulation();
            }
        }

        public void RepairActiveGroupLengths()
        {
            HairGroup group = ActiveGroup;
            if (group == null || group.locked || group.guides == null || curveBrushEntries.Count == 0)
            {
                actionStatus = group?.locked == true
                    ? "Unlock the active group before repairing stretched guides."
                    : "There are no active guides to repair.";
                RepaintAll();
                return;
            }

            EndGravitySimulation();
            ReleaseSceneInputCapture(true);
            List<HairGuideDelta> repairs = new List<HairGuideDelta>();
            for (int entryIndex = 0; entryIndex < curveBrushEntries.Count; entryIndex++)
            {
                CurveBrushEntry entry = curveBrushEntries[entryIndex];
                HairGuide guide = entry.Guide;
                if (guide?.points == null || guide.points.Count < 2 ||
                    entry.SourcePoints.Length != guide.points.Count) continue;

                EnsurePointBuffers(entry, guide.points.Count);
                Array.Copy(entry.SourcePoints, entry.OriginalPoints, guide.points.Count);
                for (int pointIndex = 0; pointIndex < guide.points.Count; pointIndex++)
                    entry.ReferencePoints[pointIndex] = guide.points[pointIndex].position;
                HairGuideShapeUtility.RestoreReferenceSegmentLengths(
                    entry.ReferencePoints, entry.OriginalPoints, entry.TargetPoints);
                bool changed = false;
                for (int pointIndex = 1; pointIndex < guide.points.Count; pointIndex++)
                {
                    changed |= (entry.TargetPoints[pointIndex] -
                                entry.OriginalPoints[pointIndex]).sqrMagnitude > 1e-12f;
                }
                if (!changed) continue;

                HairGuideDelta repair = new HairGuideDelta
                {
                    guideId = guide.Id,
                    positionOffsets = new Vector3[guide.points.Count],
                    widthOffsets = new float[guide.points.Count],
                    rollOffsets = new float[guide.points.Count]
                };
                for (int pointIndex = 1; pointIndex < guide.points.Count; pointIndex++)
                    repair.positionOffsets[pointIndex] = entry.TargetPoints[pointIndex] -
                                                         entry.OriginalPoints[pointIndex];
                repairs.Add(repair);
            }

            if (repairs.Count == 0)
            {
                actionStatus = "All active guides already match their authored segment lengths.";
                RepaintAll();
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            const string undoName = "Repair Stretched Hair Guides";
            Undo.SetCurrentGroupName(undoName);
            Undo.RegisterCompleteObjectUndo(groom, undoName);
            HairSculptLayer repairLayer = new HairSculptLayer { name = "Length Repair" };
            repairLayer.EnsureIntegrity();
            repairLayer.deltas.AddRange(repairs);
            group.sculptLayers.Add(repairLayer);
            activeLayerId = repairLayer.Id;
            activeModifierId = null;
            groom.EnsureIntegrity();
            EditorUtility.SetDirty(groom);
            Undo.CollapseUndoOperations(undoGroup);
            actionStatus = $"Repaired {repairs.Count:N0} stretched guides on a new Length Repair layer.";
            QueueRebuild(true);
        }

        private void EndGravitySimulation()
        {
            if (!gravitySimulationActive) return;
            gravitySimulationActive = false;
            EndStroke();
            if (!closing)
            {
                actionStatus = "Gravity settle complete. Hold the button again to continue relaxing the groom.";
                RepaintAll();
            }
        }

        public void SetActiveHelper(string helperId)
        {
            activeHelperId = groom?.FindHelper(helperId)?.Id;
            RepaintAll();
        }

        public void QueueRebuild(bool immediate = false)
        {
            QueuePreviewChange(HairPreviewChange.All, immediate);
        }

        public void QueuePreviewChange(HairPreviewChange change, bool immediate = false)
        {
            if (closing) return;
            if ((change & ~HairPreviewChange.Display) != 0) releaseValidationStale = true;
            pendingPreviewChanges |= change;
            rebuildQueued = true;
            rebuildNotBefore = immediate ? 0d : EditorApplication.timeSinceStartup + RebuildDelay;
            RepaintAll();
        }

        public void ValidateReleaseNow()
        {
            if (strokeActive || workspaceInteractionActive)
            {
                releaseValidationQueued = true;
                actionStatus = "Release validation queued until the edit finishes.";
                return;
            }
            releaseValidationQueued = false;
            SyncExternalHelpers();
            SetReleaseValidation(HairBakePipeline.ValidateRelease(groom));
            actionStatus = releaseValidation.CanBake ? "Release validation passed for all exported LODs." :
                $"Release validation found {releaseValidation.ErrorCount} blockers. Open Validate & Bake for actions.";
            RepaintAll();
        }

        internal void SetReleaseValidation(HairValidationReport report)
        {
            releaseValidation = report;
            releaseValidationStale = false;
            releaseValidationStamp = WorkspaceChangeStamp();
        }

        private int WorkspaceChangeStamp()
        {
            unchecked
            {
                int stamp = 17;
                foreach (UnityEngine.Object asset in WorkspaceAssets())
                    stamp = stamp * 31 + EditorUtility.GetDirtyCount(asset);
                return stamp;
            }
        }

        internal void NavigateToIssue(HairValidationIssue issue, bool selectGuide = false)
        {
            if (issue == null) return;
            if (!string.IsNullOrEmpty(issue.groupId)) SetActiveGroup(issue.groupId);
            if (selectGuide && !string.IsNullOrEmpty(issue.guideId))
            {
                WorkflowStep = HairWorkflowStep.Guides;
                SetActiveGuide(issue.guideId);
                FrameGroom();
            }
            else
            {
                WorkflowStep = issue.fixId switch
                {
                    "assign-profile" or "assign-atlas" or "assign-atlas-region" => HairWorkflowStep.Cards,
                    "rebind-source" => HairWorkflowStep.Setup,
                    "repair-helper-reference" => HairWorkflowStep.Groom,
                    "edit-lods" => HairWorkflowStep.Optimize,
                    "inspect-groups" => HairWorkflowStep.Cards,
                    _ => !string.IsNullOrEmpty(issue.guideId) ? HairWorkflowStep.Guides : HairWorkflowStep.Optimize
                };
                if (issue.lodLevel >= 0) LodLevel = issue.lodLevel;
                if (!string.IsNullOrEmpty(issue.helperId)) activeHelperId = issue.helperId;
            }
            actionStatus = issue.message;
            RepaintAll();
        }

        internal bool FixMissingResource(HairValidationIssue issue)
        {
            HairGroup group = groom?.FindGroup(issue?.groupId);
            if (group == null || group.locked) return false;
            if (issue.fixId == "assign-profile" && group.profile == null)
            {
                Undo.RecordObject(groom, "Repair Missing Hair Profile");
                group.profile = HairCardMenu.CreateDefaultProfileNear(groom);
                TrackResourceEdit(group.profile);
            }
            else if (issue.fixId == "assign-atlas" && group.atlas == null)
            {
                Undo.RecordObject(groom, "Repair Missing Hair Atlas");
                group.atlas = HairCardMenu.CreateDefaultAtlasNear(groom);
                TrackResourceEdit(group.atlas);
            }
            else return false;
            HairGroomCommands.Commit(groom);
            ValidateReleaseNow();
            return true;
        }

        public void RebuildNow()
        {
            if (closing || groom == null) return;
            using var refreshScope = PreviewRefreshMarker.Auto();
            if (strokeActive)
            {
                // Even explicit rebuild requests wait for release. Only the lightweight guide
                // evaluation runs while combing, cutting, or holding Gravity.
                QueueRebuild(true);
                return;
            }
            // A direct caller requests a full refresh; queued callers can reuse unaffected work.
            if (!rebuildQueued) pendingPreviewChanges |= HairPreviewChange.All;
            rebuildQueued = false;
            interactiveGuideRefreshQueued = false;
            HairPreviewChange changes = pendingPreviewChanges;
            pendingPreviewChanges = HairPreviewChange.None;
            if (evaluation == null) changes |= HairPreviewChange.Evaluation;
            if ((changes & HairPreviewChange.Evaluation) != 0)
            {
                SyncExternalHelpers();
                RefreshHelperPoseMatrices();
                selectedGuideSet.Clear();
                selectedGuideIds.RemoveAll(id => groom.FindGuide(id, out _) == null);
                selectedGuideSet.UnionWith(selectedGuideIds);
                evaluation = HairGroomEvaluator.EvaluateInto(groom, PreviewOptions(), evaluationWorkspace, evaluation);
                RefreshDisplayGuideCurves(evaluation);
                changes |= HairPreviewChange.Geometry | HairPreviewChange.Validation;
            }
            if ((changes & (HairPreviewChange.Geometry | HairPreviewChange.Uvs)) != 0) RefreshEvaluationResources();
            if ((changes & HairPreviewChange.Geometry) != 0)
            {
                if (IsCardPreviewMode(previewMode))
                {
                    HairEvaluationResult meshEvaluation = evaluation;
                    if (authoringPose?.IsActive == true)
                        meshEvaluation = posedEvaluation = authoringPose.TransformEvaluation(groom, evaluation, posedEvaluation);
                    meshBuild = HairCardMeshGenerator.Update(
                        meshEvaluation, groom.name + " Preview",
                        meshBuild, meshWorkspace);
                }
                else { meshBuild?.Dispose(); meshBuild = null; posedEvaluation = null; }
                cardRaycaster = null; // Build the picking BVH lazily, only when a card is clicked.
                if (hairFilter != null) hairFilter.sharedMesh = meshBuild?.mesh;
                changes |= HairPreviewChange.Materials;
            }
            else if ((changes & HairPreviewChange.Uvs) != 0)
                HairCardMeshGenerator.RefreshAtlasUvs(meshBuild, groom);
            if ((changes & (HairPreviewChange.Uvs | HairPreviewChange.Geometry)) != 0) RefreshCardHighlight();
            if (IsCardPreviewMode(previewMode) && meshBuild?.cardCount > 0)
                hasGeneratedCardPreview = true;
            if ((changes & (HairPreviewChange.Validation | HairPreviewChange.Uvs | HairPreviewChange.Geometry)) != 0)
            using (PreviewValidationMarker.Auto())
                validation = HairValidator.Validate(groom, evaluation, meshBuild, new HairValidationOptions
            {
                triangleBudget = groom.BakeSettings.triangleBudget,
                cardBudget = groom.BakeSettings.cardBudget,
                requireAtlas = groom.BakeSettings.requireAtlas
            });
            for (int helperIndex = 0; (changes & HairPreviewChange.Evaluation) != 0 && helperIndex < groom.SharedHelpers.Count; helperIndex++)
            {
                HairHelper helper = groom.SharedHelpers[helperIndex];
                if (helper != null && !helper.embedded && !TryResolveExternalHelperObject(helper, out _))
                    validation.Add(HairValidationSeverity.Error, HairValidationCode.MissingHelper,
                        $"External helper '{helper.name}' is not available in an open scene.",
                        helperId: helper.Id, fixId: "repair-helper-reference");
            }
            if ((changes & HairPreviewChange.Materials) != 0) ApplyHairMaterials();
            if ((changes & HairPreviewChange.Display) != 0) UpdateGrowthOverlay(raycastSurfaceMesh, true);
            UpdateHairPreviewVisibility();
            RepaintAll();
        }

        private HairEvaluationOptions PreviewOptions(bool guidesOnly = false) => ConfigureGravityEvaluation(new HairEvaluationOptions
        {
            lodLevel = lodLevel,
            includeChildren = !guidesOnly && showChildren && (IsCardPreviewMode(previewMode) || previewMode == HairPreviewMode.GuidesAndChildren),
            includeGuideCards = !guidesOnly && IsCardPreviewMode(previewMode),
            applySculptLayers = true, applyModifiers = true, applyConstraints = true,
            includeHiddenGroups = false, evaluateSurfaceAnchors = !guidesOnly,
            includedGuideIds = isolateSelectedGuides ? selectedGuideSet : null,
            soloLayerId = soloLayerId,
            interactiveSampleLimit = previewQuality == HairPreviewQuality.Draft ? 1500 : 0,
            previewSampleCount = previewQuality == HairPreviewQuality.Draft ? 8 : 0
        });

        internal HairEvaluationOptions ConfigureGravityEvaluation(HairEvaluationOptions options)
        {
            authoringPose?.RefreshGuideMatrices(groom);
            options.sourceToWorld = SourceToStageMatrix;
            options.guideToSourcePose = GuideToGravityPose;
            options.gravityCollisionMesh = authoringSurfaceMesh != null ? authoringSurfaceMesh : groom?.SourceMesh;
            return options;
        }

        private Matrix4x4 GuideToGravityPose(string guideId) =>
            authoringPose?.MatrixForGuide(groom, guideId) ?? Matrix4x4.identity;

        private void RefreshEvaluationResources()
        {
            if (evaluation == null) return;
            Dictionary<string, HairGroup> groups = new Dictionary<string, HairGroup>();
            Dictionary<string, string[]> regionIds = new Dictionary<string, string[]>();
            foreach (HairGroup group in groom.Groups)
                if (group != null) { groups[group.Id] = group; regionIds[group.Id] = group.atlasRegionIds.ToArray(); }
            foreach (HairEvaluatedCurve curve in evaluation.curves)
                if (groups.TryGetValue(curve.groupId, out HairGroup group))
                { curve.atlas = group.atlas; curve.profile = group.profile; curve.atlasRegionSelection = group.atlasRegionSelection; curve.atlasRegionIds = regionIds[group.Id]; }
        }

        private void QueueInteractiveGuideRefresh()
        {
            if (closing) return;
            interactiveGuideRefreshQueued = true;
            if (nextInteractiveGuideRefresh < EditorApplication.timeSinceStartup)
                nextInteractiveGuideRefresh = EditorApplication.timeSinceStartup;
        }

        private void RebuildInteractiveGuidePreview(double now)
        {
            interactiveGuideRefreshQueued = false;
            interactiveEvaluation = HairGroomEvaluator.EvaluateInto(groom, PreviewOptions(true), evaluationWorkspace, interactiveEvaluation);
            RefreshDisplayGuideCurves(interactiveEvaluation);
            nextInteractiveGuideRefresh = now + InteractiveGuideRefreshInterval;
            SceneView.RepaintAll();
        }

        private void RefreshDisplayGuideCurves(HairEvaluationResult sourceEvaluation)
        {
            authoringPose?.RefreshGuideMatrices(groom);
            displayGuideCurves.Clear();
            if (sourceEvaluation?.evaluatedGuides != null)
            {
                for (int curveIndex = 0; curveIndex < sourceEvaluation.evaluatedGuides.Count; curveIndex++)
                {
                    HairEvaluatedCurve curve = sourceEvaluation.evaluatedGuides[curveIndex];
                    if (curve == null || string.IsNullOrEmpty(curve.parentGuideId) ||
                        displayGuideCurves.ContainsKey(curve.parentGuideId)) continue;
                    displayGuideCurves.Add(curve.parentGuideId, curve);
                    if (!displayGuidePolylines.TryGetValue(curve.parentGuideId, out Vector3[] polyline) ||
                        polyline.Length != curve.points.Count)
                        displayGuidePolylines[curve.parentGuideId] = polyline = new Vector3[curve.points.Count];
                    for (int pointIndex = 0; pointIndex < polyline.Length; pointIndex++)
                        polyline[pointIndex] = curve.points[pointIndex].position;
                }
            }
            staleGuideBufferIds.Clear();
            foreach (string id in displayGuidePolylines.Keys)
                if (!displayGuideCurves.ContainsKey(id)) staleGuideBufferIds.Add(id);
            foreach (string id in staleGuideBufferIds) displayGuidePolylines.Remove(id);
            staleGuideBufferIds.Clear();
            RebuildCurveBrushCache();
        }

        private void RebuildCurveBrushCache()
        {
            curveBrushEntries.Clear();
            activeBrushBufferIds.Clear();
            HairGroup group = ActiveGroup;
            if (group?.guides == null) { retainedBrushEntries.Clear(); return; }
            for (int guideIndex = 0; guideIndex < group.guides.Count; guideIndex++)
            {
                HairGuide guide = group.guides[guideIndex];
                if (guide == null || !guide.enabled ||
                    !displayGuideCurves.TryGetValue(guide.Id, out HairEvaluatedCurve curve) ||
                    curve.points.Count < 2) continue;
                Matrix4x4 guideToPose = GuidePoseMatrix(guide.Id);
                float poseScaleX = guideToPose.MultiplyVector(Vector3.right).magnitude;
                float poseScaleY = guideToPose.MultiplyVector(Vector3.up).magnitude;
                float poseScaleZ = guideToPose.MultiplyVector(Vector3.forward).magnitude;
                float poseRadiusScale = Mathf.Max(poseScaleX, Mathf.Max(poseScaleY, poseScaleZ));
                if (!retainedBrushEntries.TryGetValue(guide.Id, out CurveBrushEntry entry))
                    retainedBrushEntries.Add(guide.Id, entry = new CurveBrushEntry());
                activeBrushBufferIds.Add(guide.Id);
                entry.Guide = guide;
                entry.GuideToPose = guideToPose;
                entry.PoseToGuide = guideToPose.inverse;
                entry.PoseRadiusScale = Mathf.Max(0.0001f, poseRadiusScale);
                if (entry.SourcePoints.Length != curve.points.Count) entry.SourcePoints = new Vector3[curve.points.Count];
                for (int pointIndex = 0; pointIndex < curve.points.Count; pointIndex++)
                    entry.SourcePoints[pointIndex] = curve.points[pointIndex].position;
                RefreshCurveBrushEntry(entry);
                curveBrushEntries.Add(entry);
            }
            staleGuideBufferIds.Clear();
            foreach (string id in retainedBrushEntries.Keys)
                if (!activeBrushBufferIds.Contains(id)) staleGuideBufferIds.Add(id);
            foreach (string id in staleGuideBufferIds) retainedBrushEntries.Remove(id);
            staleGuideBufferIds.Clear();
        }

        private static void RefreshCurveBrushEntry(CurveBrushEntry entry)
        {
            if (entry?.SourcePoints == null || entry.SourcePoints.Length == 0) return;
            if (entry.PosedPoints.Length != entry.SourcePoints.Length)
                entry.PosedPoints = new Vector3[entry.SourcePoints.Length];
            Vector3 first = entry.GuideToPose.MultiplyPoint3x4(entry.SourcePoints[0]);
            entry.PosedPoints[0] = first;
            Bounds bounds = new Bounds(first, Vector3.zero);
            for (int pointIndex = 1; pointIndex < entry.SourcePoints.Length; pointIndex++)
            {
                Vector3 posed = entry.GuideToPose.MultiplyPoint3x4(entry.SourcePoints[pointIndex]);
                entry.PosedPoints[pointIndex] = posed;
                bounds.Encapsulate(posed);
            }
            entry.PosedBounds = bounds;
        }

        public void GenerateGuidePreview()
        {
            ReleaseSceneInputCapture(true);
            workflowStep = HairWorkflowStep.Guides;
            sceneTool = HairSceneTool.Select;
            lastGuideTool = sceneTool;
            previewMode = HairPreviewMode.Guides;
            generationPreview = HairGuideGenerator.Generate(groom, ActiveGroup, guideGeneration);
            actionStatus = generationPreview.guides.Count > 0
                ? $"Previewed {generationPreview.guides.Count:N0} guides. Accept the preview to make them editable."
                : generationPreview.warnings.Count > 0
                    ? generationPreview.warnings[0]
                    : "No guides were generated. Paint a non-zero Growth Area and try again.";
            RepaintAll();
        }

        public void AcceptGuidePreview(bool replaceGenerated = false, bool replaceAll = false)
        {
            if (generationPreview == null) return;
            int added = HairGroomCommands.AddGeneratedGuides(groom, ActiveGroup, generationPreview.guides,
                replaceGenerated, replaceAll);
            if (added <= 0)
            {
                actionStatus = ActiveGroup?.locked == true
                    ? "No guides were accepted because the active group is locked."
                    : "The preview contained no guides to accept.";
                RepaintAll();
                return;
            }
            generationPreview = null;
            HairGroup group = ActiveGroup;
            HairGuide last = group != null && group.guides.Count > 0 ? group.guides[group.guides.Count - 1] : null;
            activeGuideId = last?.Id;
            activeGuidePoint = -1;
            sceneTool = HairSceneTool.Select;
            lastGuideTool = sceneTool;
            previewMode = HairPreviewMode.GuidesAndChildren;
            actionStatus = $"Accepted {added:N0} authored guides. Continue to Groom to style them.";
            QueueRebuild(true);
        }

        public void CancelGuidePreview()
        {
            generationPreview = null;
            actionStatus = "Guide preview cancelled; authored guides were not changed.";
            RepaintAll();
        }

        public void GetGrowthAreaStatistics(out int nonZeroVertices, out int totalVertices,
            out float maximumValue)
        {
            nonZeroVertices = 0;
            maximumValue = 0f;
            HairGrowthMap growth = ActiveGroup?.FindMap(HairMapKind.GrowthArea);
            totalVertices = growth?.values?.Length ?? 0;
            if (growth?.values == null) return;
            for (int vertex = 0; vertex < growth.values.Length; vertex++)
            {
                float value = growth.values[vertex];
                if (value > growth.valueRange.x + 0.0001f) nonZeroVertices++;
                maximumValue = Mathf.Max(maximumValue, value);
            }
        }

        public void FillVisibleActiveMap(float value)
        {
            HairGrowthMap map = ActiveMap;
            if (map == null || map.locked) return;
            Undo.RecordObject(groom, $"Fill Visible {map.name}");
            float target = Mathf.Clamp(value, map.valueRange.x, map.valueRange.y);
            for (int vertex = 0; vertex < map.values.Length; vertex++)
                if (IsSourceVertexVisible(vertex)) map.values[vertex] = target;
            actionStatus = $"Filled the visible portion of {map.name} with {target:0.###}.";
            HairGroomCommands.Commit(groom);
        }

        public void SaveNow(bool createRecovery = true)
        {
            SaveEditorPreferences();
            HairGroomWorkspace.SaveOpenPreferences();
            nextAutosave = EditorApplication.timeSinceStartup + AutosaveDelay;
            if (groom == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(groom))) return;
            if (strokeActive || workspaceInteractionActive)
            {
                saveRequested = true;
                actionStatus = "Save queued until the current edit finishes.";
                return;
            }
            saveRequested = false;
            // Upgrade already-open, older grooms while their generated source is still alive.
            HairGroomSourcePersistence.EnsurePersistent(groom);
            if (!HasUnsavedChanges) return;
            foreach (UnityEngine.Object asset in WorkspaceAssets())
                if (asset != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset)))
                    AssetDatabase.SaveAssetIfDirty(asset);
            HairEditorPreferences.instance.RememberSetup(groom, ActiveGroup);
            if (createRecovery) HairGroomRecovery.SaveSnapshot(groom);
            editedResources.RemoveAll(asset => asset == null || !EditorUtility.IsDirty(asset));
            lastSavedAt = EditorApplication.timeSinceStartup;
            actionStatus = HasUnsavedChanges ? "Some setup resources are unsaved; check their asset paths." :
                "Saved groom, card profiles, and atlas UV settings.";
            RepaintAll();
        }

        internal void TrackResourceEdit(UnityEngine.Object asset)
        {
            if (asset != null && !editedResources.Contains(asset)) editedResources.Add(asset);
        }

        internal void SaveEditorPreferences()
        {
            if (HairEditorPreferences.Suspended || groom == null || !EditorUtility.IsPersistent(groom)) return;
            HairEditorPreferences.instance.Remember(this, "stage", HairEditorPreferences.Context(groom));
            HairEditorPreferences.instance.RememberSetup(groom, ActiveGroup);
        }

        internal void ResetEditorPreferences()
        {
            EndGravitySimulation();
            ReleaseSceneInputCapture(true);
            HairEditorPreferences.instance.ClearEditorSettings();
            HairPreferenceCodec.Reset(this);
            paintErase = false;
            showScalp = sourceAvatar == null;
            showAvatar = sourceAvatar != null;
            activeGroupId = FirstGroup()?.Id;
            activeMapId = ActiveGroup?.FindMap(HairMapKind.GrowthArea)?.Id;
            selectedGuideSet.Clear();
            generationPreview = null;
            NormalizeWorkflowState();
            ApplyVisibility();
            QueueRebuild(true);
            actionStatus = "Editor options reset. Authored guides, paint, and card setup were kept.";
            RepaintAll();
        }

        private List<UnityEngine.Object> WorkspaceAssets()
        {
            List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
            if (groom != null) assets.Add(groom);
            foreach (UnityEngine.Object asset in editedResources)
                if (asset != null && !assets.Contains(asset)) assets.Add(asset);
            if (groom?.Groups != null)
                foreach (HairGroup group in groom.Groups)
                {
                    if (group?.profile != null && !assets.Contains(group.profile)) assets.Add(group.profile);
                    if (group?.atlas != null && !assets.Contains(group.atlas)) assets.Add(group.atlas);
                    if (group?.atlas?.material != null && !assets.Contains(group.atlas.material)) assets.Add(group.atlas.material);
                    if (group?.atlas?.secondPassMaterial != null && !assets.Contains(group.atlas.secondPassMaterial)) assets.Add(group.atlas.secondPassMaterial);
                }
            return assets;
        }

        public void FrameGroom()
        {
            pendingFocusBounds = null;
            pendingFocusPoint = null;
            needsFrame = true;
            SceneView.RepaintAll();
        }

        internal bool TryGetCurrentAreaBounds(out Bounds bounds, out int boneCount)
        {
            return HairPaintedAreaBounds.TryCalculate(groom?.SourceMesh,
                ActiveGroup?.FindMap(HairMapKind.GrowthArea)?.values, authoringPose,
                authoringBonePositions, SourceToStageMatrix, out bounds, out boneCount);
        }

        public bool FocusCurrentArea()
        {
            if (!TryGetCurrentAreaBounds(out Bounds bounds, out int boneCount))
            {
                actionStatus = "No painted Growth Area in the current group. Paint an area first.";
                RepaintAll();
                return false;
            }
            pendingFocusBounds = bounds;
            pendingFocusPoint = null;
            needsFrame = true;
            actionStatus = boneCount > 0
                ? $"Focused current painted area and {boneCount} influencing bone(s)."
                : "Focused current painted area (no usable bone influences).";
            RepaintAll();
            return true;
        }

        internal bool CanFocusBone(HumanBodyBones bone) => GetFocusBonePosition(bone).HasValue;

        private Vector3? GetFocusBonePosition(HumanBodyBones bone) => bone == HumanBodyBones.Head
            ? authoringHeadPosition : bone == HumanBodyBones.Neck ? authoringNeckPosition : null;

        public bool FocusBone(HumanBodyBones bone)
        {
            Vector3? position = GetFocusBonePosition(bone);
#if !NoFudge
            position += new Vector3(0, 0.05f, 0);
#endif
            if (!position.HasValue)
            {
                actionStatus = $"No {bone} bone is available in the character preview.";
                RepaintAll();
                return false;            }
            pendingFocusBounds = null;
            pendingFocusPoint = position;
            needsFrame = true;
            actionStatus = $"Focused {bone} at 0.45 meters.";
            RepaintAll();
            return true;
        }

        private void ApplyPendingFocus(SceneView sceneView)
        {
            if (!needsFrame || sceneView == null) return;
            needsFrame = false;
            if (pendingFocusPoint.HasValue)
            {
                // SceneView size is the framed sphere radius, not camera distance. Use the
                // aspect-neutral FOV (not Camera.fieldOfView, which changes in tall windows).
                const float distance = 0.45f;
                bool orthographic = sceneView.orthographic;
                sceneView.orthographic = orthographic; // Finish any projection transition.
                float size = orthographic ? distance * 0.5f
                    : distance * Mathf.Sin(sceneView.cameraSettings.fieldOfView * 0.5f * Mathf.Deg2Rad);
                sceneView.LookAtDirect(pendingFocusPoint.Value, sceneView.rotation, size);
                sceneView.Repaint();
            }
            else if (pendingFocusBounds.HasValue) sceneView.Frame(pendingFocusBounds.Value, false);
            else if (TryGetPreviewBounds(out Bounds bounds)) sceneView.Frame(bounds, false);
            pendingFocusBounds = null;
            pendingFocusPoint = null;
        }

        internal HairVisibilityState GetVisibilityState(HairAvatarVisibilityGroup group)
        {
            if (group == null || group.SlotNames.Count == 0) return HairVisibilityState.Hidden;
            RefreshHiddenSlotSet();
            int visible = 0;
            for (int i = 0; i < group.SlotNames.Count; i++)
                if (!hiddenSlotSet.Contains(group.SlotNames[i])) visible++;
            if (visible == 0) return HairVisibilityState.Hidden;
            return visible == group.SlotNames.Count ? HairVisibilityState.Visible : HairVisibilityState.Mixed;
        }

        internal void SetVisibility(HairAvatarVisibilityGroup group, bool visible)
        {
            if (group == null) return;
            RefreshHiddenSlotSet();
            for (int i = 0; i < group.SlotNames.Count; i++)
            {
                if (visible) hiddenSlotSet.Remove(group.SlotNames[i]);
                else hiddenSlotSet.Add(group.SlotNames[i]);
            }
            CommitHiddenSlotSet();
        }

        internal void IsolateVisibility(HairAvatarVisibilityGroup group)
        {
            if (group == null || visibilityCatalog == null) return;
            hiddenSlotSet.Clear();
            foreach (string slotName in visibilityCatalog.SlotNames)
                if (!group.SlotNames.Contains(slotName)) hiddenSlotSet.Add(slotName);
            CommitHiddenSlotSet();
        }

        internal void ShowAllAvatarSlots()
        {
            hiddenAvatarSlots.Clear();
            hiddenSlotSet.Clear();
            ApplyVisibility();
        }

        internal void HideAllAvatarSlots()
        {
            hiddenSlotSet.Clear();
            if (visibilityCatalog != null)
                foreach (string slotName in visibilityCatalog.SlotNames) hiddenSlotSet.Add(slotName);
            CommitHiddenSlotSet();
        }

        internal void InvertAvatarSlots()
        {
            RefreshHiddenSlotSet();
            HashSet<string> inverted = new HashSet<string>(StringComparer.Ordinal);
            if (visibilityCatalog != null)
                foreach (string slotName in visibilityCatalog.SlotNames)
                    if (!hiddenSlotSet.Contains(slotName)) inverted.Add(slotName);
            hiddenSlotSet.Clear();
            hiddenSlotSet.UnionWith(inverted);
            CommitHiddenSlotSet();
        }

        private void RefreshHiddenSlotSet()
        {
            hiddenSlotSet.Clear();
            if (hiddenAvatarSlots == null) hiddenAvatarSlots = new List<string>();
            for (int i = 0; i < hiddenAvatarSlots.Count; i++)
                if (!string.IsNullOrEmpty(hiddenAvatarSlots[i])) hiddenSlotSet.Add(hiddenAvatarSlots[i]);
        }

        private void CommitHiddenSlotSet()
        {
            hiddenAvatarSlots.Clear();
            hiddenAvatarSlots.AddRange(hiddenSlotSet);
            hiddenAvatarSlots.Sort(StringComparer.OrdinalIgnoreCase);
            ApplyVisibility();
        }

        private void CreatePreviewObjects()
        {
            SkinnedMeshRenderer sourceRenderer = ResolveSourceRenderer();
            sourceVertices = groom.SourceMesh.vertices;
            sourceNormals = groom.SourceMesh.normals;
            sourceSpaceObject = new GameObject("Hair Card Source Space");
            sourceSpaceObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            sourceSpaceObject.transform.localScale = Vector3.one;
            if (sourceAvatar != null && sourceRenderer != null)
            {
                Matrix4x4 sourceToAvatar = sourceAvatar.transform.worldToLocalMatrix *
                                           sourceRenderer.transform.localToWorldMatrix;
                sourceSpaceObject.transform.SetPositionAndRotation(sourceToAvatar.GetPosition(), sourceToAvatar.rotation);
                sourceSpaceObject.transform.localScale = sourceToAvatar.lossyScale;
            }

            scalpObject = new GameObject("Hair Scalp Surface");
            scalpObject.transform.SetParent(sourceSpaceObject.transform, false);
            scalpFilter = scalpObject.AddComponent<MeshFilter>();
            Mesh paintSurface = CreateAuthoringSurfaceMesh(sourceRenderer);
            authoringPose = new HairAuthoringPose(groom.SourceMesh, paintSurface);
            authoringBonePositions = authoringPose.IsActive ? HairPaintedAreaBounds.CaptureBones(sourceRenderer) : null;
            authoringHeadPosition = HairBoneFocus.Capture(sourceAvatar, HumanBodyBones.Head);
            authoringNeckPosition = HairBoneFocus.Capture(sourceAvatar, HumanBodyBones.Neck);
            sourceSurfaceRaycaster = new HairMeshRaycaster(groom.SourceMesh);
            scalpFilter.sharedMesh = paintSurface;
            scalpRenderer = scalpObject.AddComponent<MeshRenderer>();
            scalpMaterial = CreateMaterial("Hair Scalp Preview", new Color(0.42f, 0.43f, 0.46f, 1f));
            scalpRenderer.sharedMaterial = scalpMaterial;
            scalpCollider = scalpObject.AddComponent<MeshCollider>();
            scalpCollider.sharedMesh = paintSurface;

            growthOverlayObject = new GameObject("Hair Growth Map Overlay");
            growthOverlayObject.transform.SetParent(sourceSpaceObject.transform, false);
            growthOverlayFilter = growthOverlayObject.AddComponent<MeshFilter>();
            growthOverlayRenderer = growthOverlayObject.AddComponent<MeshRenderer>();
            growthOverlayMaterial = CreateGrowthOverlayMaterial();
            growthOverlayRenderer.sharedMaterial = growthOverlayMaterial;
            growthOverlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            growthOverlayRenderer.receiveShadows = false;
            growthOverlayRenderer.lightProbeUsage = LightProbeUsage.Off;
            growthOverlayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            hairObject = new GameObject("Generated Hair Cards");
            hairObject.transform.SetParent(sourceSpaceObject.transform, false);
            hairFilter = hairObject.AddComponent<MeshFilter>();
            hairRenderer = hairObject.AddComponent<MeshRenderer>();
            fallbackHairMaterial = CreateMaterial("Hair Card Preview", new Color(0.12f, 0.055f, 0.025f, 1f));

            if (sourceAvatar?.umaData != null && sourceRenderer != null)
            {
                try
                {
                    avatarPreview = HairAvatarPreview.Build(sourceAvatar);
                    if (avatarPreview?.Root != null) SceneManager.MoveGameObjectToScene(avatarPreview.Root, scene);
                    visibilityCatalog = HairAvatarVisibilityCatalog.Build(sourceAvatar, avatarPreview?.RenderedSlots);
                    sourceVisibility = new HairSourceVisibility(groom.SourceMesh, paintSurface,
                        sourceAvatar.umaData, sourceRenderer, avatarPreview?.RenderedSlots);
                }
                catch (Exception exception)
                {
                    avatarPreview?.Dispose();
                    avatarPreview = null;
                    visibilityCatalog = null;
                    sourceVisibility?.Dispose();
                    sourceVisibility = null;
                    Debug.LogWarning($"[UMA Hair Cards] The character preview could not be reconstructed. " +
                                     $"Hair authoring remains available on the source surface. {exception.Message}");
                }
            }

            lightingObject = new GameObject("Hair Card Lighting");
            Light light = lightingObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightingObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            SceneManager.MoveGameObjectToScene(sourceSpaceObject, scene);
            SceneManager.MoveGameObjectToScene(lightingObject, scene);
            ApplyVisibility();
        }

        private Mesh CreateAuthoringSurfaceMesh(SkinnedMeshRenderer sourceRenderer)
        {
            if (sourceRenderer == null || sourceRenderer.sharedMesh == null) return groom.SourceMesh;
            Mesh baked = new Mesh
            {
                name = sourceRenderer.name + " Hair Card Authoring Surface",
                indexFormat = sourceRenderer.sharedMesh.indexFormat,
                hideFlags = HideFlags.HideAndDontSave
            };
            try
            {
                sourceRenderer.BakeMesh(baked);
                if (baked.vertexCount != groom.SourceMesh.vertexCount ||
                    !string.Equals(HairMeshUtility.ComputeTopologySignature(baked),
                        HairMeshUtility.ComputeTopologySignature(groom.SourceMesh), StringComparison.Ordinal))
                {
                    Debug.LogWarning("[UMA Hair Cards] The posed character surface did not retain the groom " +
                                     "source topology. Painting will use the undeformed source surface.");
                    DestroyImmediate(baked);
                    return groom.SourceMesh;
                }
                authoringSurfaceMesh = baked;
                return authoringSurfaceMesh;
            }
            catch (Exception exception)
            {
                DestroyImmediate(baked);
                Debug.LogWarning($"[UMA Hair Cards] The posed character surface could not be prepared for " +
                                 $"painting. The undeformed source surface will be used. {exception.Message}");
                return groom.SourceMesh;
            }
        }

        private static Material CreateMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ??
                            Shader.Find("Hidden/Internal-Colored");
            Material material = new Material(shader) { name = materialName, hideFlags = HideFlags.HideAndDontSave };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            material.enableInstancing = true;
            return material;
        }

        private static Material CreateGrowthOverlayMaterial()
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Unlit/Color");
            Material material = new Material(shader)
            {
                name = "Hair Growth Map Overlay",
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = (int)RenderQueue.Overlay
            };
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            return material;
        }

        private void ApplyHairMaterials()
        {
            if (hairRenderer == null || meshBuild == null) return;
            hairRenderer.sharedMaterials = hairPreviewMaterials.Update(meshBuild, fallbackHairMaterial);
        }

        private void ApplyVisibility()
        {
            RefreshHiddenSlotSet();
            avatarPreview?.ApplyVisibility(showAvatar, hiddenSlotSet);
            Mesh paintSurface = authoringSurfaceMesh != null ? authoringSurfaceMesh : groom.SourceMesh;
            if (sourceVisibility != null)
            {
                paintSurface = sourceVisibility.Rebuild(hiddenSlotSet);
                selectedVertices.RemoveAll(vertex => !IsSourceVertexVisible(vertex));
            }
            if (scalpFilter != null && scalpFilter.sharedMesh != paintSurface)
                scalpFilter.sharedMesh = paintSurface;
            if (scalpCollider != null && scalpCollider.sharedMesh != paintSurface)
            {
                scalpCollider.sharedMesh = null;
                scalpCollider.sharedMesh = paintSurface;
            }
            EnsureSurfaceRaycaster(paintSurface);
            UpdateGrowthOverlay(paintSurface, true);
            if (scalpRenderer != null) scalpRenderer.enabled = showScalp;
            UpdateHairPreviewVisibility();
            RepaintAll();
        }

        private void UpdateHairPreviewVisibility()
        {
            if (hairRenderer != null)
            {
                hairRenderer.enabled = !strokeActive && IsCardPreviewMode(previewMode) &&
                                       meshBuild?.mesh != null;
                EditorUtility.SetSelectedRenderState(hairRenderer, showCardWireframe
                    ? EditorSelectedRenderState.Highlight | EditorSelectedRenderState.Wireframe
                    : EditorSelectedRenderState.Hidden);
            }
        }

        private void UpdateGrowthOverlay(Mesh paintSurface, bool refreshAllColors)
        {
            if (growthOverlayFilter == null || growthOverlayRenderer == null) return;
            HairGrowthMap map = ActiveMap;
            bool shouldShow = previewMode == HairPreviewMode.GrowthMap && map != null && map.visible;
            growthOverlayRenderer.enabled = shouldShow;
            if (!shouldShow || paintSurface == null) return;

            if (growthOverlayMesh == null || growthOverlaySourceMesh != paintSurface)
            {
                DestroyPreviewObject(growthOverlayMesh);
                growthOverlaySourceMesh = paintSurface;
                growthOverlayMesh = Instantiate(paintSurface);
                growthOverlayMesh.name = paintSurface.name + " Growth Map Overlay";
                growthOverlayMesh.hideFlags = HideFlags.HideAndDontSave;
                Vector3[] vertices = growthOverlayMesh.vertices;
                Vector3[] normals = growthOverlayMesh.normals;
                if (normals.Length != vertices.Length)
                {
                    growthOverlayMesh.RecalculateNormals();
                    normals = growthOverlayMesh.normals;
                }
                float offset = Mathf.Max(0.00001f, paintSurface.bounds.size.magnitude * 0.00015f);
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                    vertices[vertex] += normals[vertex] * offset;
                growthOverlayMesh.vertices = vertices;
                growthOverlayMesh.RecalculateBounds();
                growthOverlayFilter.sharedMesh = growthOverlayMesh;
                growthOverlayColors = new Color32[growthOverlayMesh.vertexCount];
                refreshAllColors = true;
            }

            if (refreshAllColors) RefreshAllGrowthOverlayColors(map);
        }

        private void RefreshAllGrowthOverlayColors(HairGrowthMap map)
        {
            if (growthOverlayMesh == null || map?.values == null ||
                growthOverlayColors.Length != growthOverlayMesh.vertexCount) return;
            for (int vertex = 0; vertex < growthOverlayColors.Length; vertex++)
                growthOverlayColors[vertex] = GrowthOverlayColor(map, vertex);
            growthOverlayMesh.colors32 = growthOverlayColors;
        }

        private static Color32 GrowthOverlayColor(HairGrowthMap map, int vertex)
        {
            float value = (uint)vertex < (uint)map.values.Length ? map.values[vertex] : map.valueRange.x;
            float normalized = Mathf.InverseLerp(map.valueRange.x, map.valueRange.y, value);
            Color low = new Color(0.02f, 0.18f, 0.85f, 0.42f);
            Color high = new Color(1f, 0.18f, 0.015f, 0.88f);
            return Color.Lerp(low, high, normalized);
        }

        private void EnsureSurfaceRaycaster(Mesh surface)
        {
            if (surfaceRaycaster != null && raycastSurfaceMesh == surface) return;
            raycastSurfaceMesh = surface;
            surfaceRaycaster = new HairMeshRaycaster(surface);
        }

        private bool IsSourceVertexVisible(int vertex)
        {
            return sourceVisibility == null || sourceVisibility.IsVertexVisible(vertex, hiddenSlotSet);
        }

        private void EditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (!strokeActive && !workspaceInteractionActive && now >= nextPreferencesSave)
            {
                nextPreferencesSave = now + 2d;
                SaveEditorPreferences();
            }
            if (gravitySimulationActive && now >= nextGravityStep)
            {
                float deltaTime = Mathf.Clamp((float)(now - lastGravityUpdate), 1f / 120f, 1f / 20f);
                lastGravityUpdate = now;
                nextGravityStep = now + 1d / 60d;
                ApplyGravityStep(deltaTime);
                EditorUtility.SetDirty(groom);
                RepaintAll();
            }
            if (strokeActive && interactiveGuideRefreshQueued && now >= nextInteractiveGuideRefresh)
                RebuildInteractiveGuidePreview(now);
            if (!strokeActive && !workspaceInteractionActive && rebuildQueued && now >= rebuildNotBefore) RebuildNow();
            if (!strokeActive && !workspaceInteractionActive && meshBuild != null &&
                (pendingPreviewChanges & HairPreviewChange.Geometry) == 0 && !meshBuild.MaterialPassLayoutMatches())
                QueuePreviewChange(HairPreviewChange.Geometry);
            if (!strokeActive && !workspaceInteractionActive && meshBuild != null && hairRenderer != null &&
                hairPreviewMaterials.SourcesChanged(meshBuild, fallbackHairMaterial))
            {
                // Material Inspector edits must remain live even though previews use private instances.
                ApplyHairMaterials();
                RepaintAll();
            }
            if (!strokeActive && !workspaceInteractionActive && releaseValidationQueued) ValidateReleaseNow();
            if (!strokeActive && !workspaceInteractionActive && !rebuildQueued &&
                now - lastWorkspaceInteraction >= 1.5d && (saveRequested || now >= nextAutosave))
            {
                if (saveRequested || HasUnsavedChanges) SaveNow();
                else nextAutosave = now + AutosaveDelay;
            }
        }

        private void SyncExternalHelpers()
        {
            if (groom?.SharedHelpers == null) return;
            Transform sourceTransform = ResolveSourceRenderer()?.transform;
            for (int helperIndex = 0; helperIndex < groom.SharedHelpers.Count; helperIndex++)
            {
                HairHelper helper = groom.SharedHelpers[helperIndex];
                if (helper == null || helper.embedded) continue;
                if (!TryResolveExternalHelperObject(helper, out UnityEngine.Object resolved)) continue;
                Transform target = resolved is GameObject gameObject ? gameObject.transform :
                    resolved is Component component ? component.transform : null;
                if (target == null) continue;
                Vector3 posedPosition = sourceTransform != null
                    ? sourceTransform.InverseTransformPoint(target.position) : target.position;
                Matrix4x4 sourceToPose = authoringPose?.MatrixNearPosedPoint(groom.SourceMeshId,
                    posedPosition) ?? Matrix4x4.identity;
                Matrix4x4 poseToSource = sourceToPose.inverse;
                helper.position = poseToSource.MultiplyPoint3x4(posedPosition);
                Vector3 posedForward = sourceTransform != null
                    ? sourceTransform.InverseTransformDirection(target.forward) : target.forward;
                Vector3 posedUp = sourceTransform != null
                    ? sourceTransform.InverseTransformDirection(target.up) : target.up;
                Vector3 sourceForward = poseToSource.MultiplyVector(posedForward).normalized;
                Vector3 sourceUp = poseToSource.MultiplyVector(posedUp).normalized;
                helper.rotation = sourceForward.sqrMagnitude > 1e-8f && sourceUp.sqrMagnitude > 1e-8f
                    ? Quaternion.LookRotation(sourceForward, sourceUp)
                    : Quaternion.identity;
                helper.scale = target.lossyScale;
                helper.points.Clear();
                helper.points.Add(helper.position);
                for (int child = 0; child < target.childCount; child++)
                {
                    Vector3 posedPoint = sourceTransform != null
                        ? sourceTransform.InverseTransformPoint(target.GetChild(child).position)
                        : target.GetChild(child).position;
                    helper.points.Add(authoringPose?.SourcePointFromPose(groom.SourceMeshId, posedPoint) ??
                                      posedPoint);
                }
                if (helper.points.Count == 1)
                    helper.points.Add(helper.position + helper.rotation * Vector3.up * 0.2f);
            }
        }

        private void RefreshHelperPoseMatrices()
        {
            helperPoseMatrices.Clear();
            if (authoringPose == null || groom?.SharedHelpers == null) return;
            for (int helperIndex = 0; helperIndex < groom.SharedHelpers.Count; helperIndex++)
            {
                HairHelper helper = groom.SharedHelpers[helperIndex];
                if (helper == null || string.IsNullOrEmpty(helper.Id)) continue;
                helperPoseMatrices[helper.Id] = authoringPose.MatrixNearSourcePoint(groom.SourceMeshId,
                    helper.position);
            }
        }

        internal static bool IsExternalHelperAvailable(HairHelper helper) => TryResolveExternalHelperObject(helper, out _);

        private static bool TryResolveExternalHelperObject(HairHelper helper, out UnityEngine.Object resolved)
        {
            resolved = null;
            if (helper == null || helper.embedded) return helper != null;
            if (!string.IsNullOrEmpty(helper.externalGlobalId) &&
                GlobalObjectId.TryParse(helper.externalGlobalId, out GlobalObjectId globalId))
                resolved = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId);
            if (resolved != null) return true;
            if (string.IsNullOrEmpty(helper.externalHelperId)) return false;
            UMA.HairCards.Runtime.HairHelperId[] helperIds =
                Resources.FindObjectsOfTypeAll<UMA.HairCards.Runtime.HairHelperId>();
            for (int candidate = 0; candidate < helperIds.Length; candidate++)
            {
                if (helperIds[candidate].Id != helper.externalHelperId) continue;
                resolved = helperIds[candidate].gameObject;
                return true;
            }
            return false;
        }

        private SkinnedMeshRenderer ResolveSourceRenderer()
        {
            if (sourceAvatar?.umaData != null)
            {
                SkinnedMeshRenderer renderer = sourceAvatar.umaData.GetRenderer(0);
                if (renderer != null) return renderer;
            }
            return sourceAvatar != null ? sourceAvatar.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        }

        private void OnUndoRedo()
        {
            groom?.EnsureIntegrity();
            activeGroupId = groom?.FindGroup(activeGroupId)?.Id ?? FirstGroup()?.Id;
            NormalizeStackSelection();
            QueueRebuild(true);
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (sceneView == null || groom == null) return;
            // Reserve these before any conditional handle drawing. Guide/root handles are hidden
            // during a stroke; allocating afterward changes Unity's control-ID sequence between
            // MouseDown and MouseDrag and causes the stage to lose ownership of every other stroke.
            int sceneInputControlId = GUIUtility.GetControlID(SceneInputControlHint, FocusType.Passive);
            int brushModifierControlId = GUIUtility.GetControlID(BrushModifierControlHint, FocusType.Passive);
            if (UpdateTemporaryPaintErase(Event.current))
            {
                sceneView.Repaint();
                HairGroomWorkspace.RepaintOpenWindows();
            }
            UpdateBrushHighlight(sceneView);
            DrawSceneToolbar(sceneView);
            if (depthTestGuides && Event.current.type == EventType.Repaint)
            {
                // Re-establish occluder depth in the actual handle render target, after Begin/EndGUI.
                // Do not redraw color or fill transparent/cutout cards with solid depth.
                guideDepthRenderer.Draw(scalpRenderer, scalpFilter != null ? scalpFilter.sharedMesh : null);
                avatarPreview?.DrawGuideOccluderDepth(guideDepthRenderer);
            }
            DrawGuideOverlays();
            if (pointHandleStrokeActive && (Event.current.rawType == EventType.MouseUp || GUIUtility.hotControl == 0))
                EndStroke();
            if (!pointHandleStrokeActive) HandleSceneInput(sceneView, sceneInputControlId, brushModifierControlId);
            ApplyPendingFocus(sceneView);
        }

        private void DrawGuideOverlays()
        {
            using (new Handles.DrawingScope(SourceToStageMatrix))
            {
                CompareFunction previousZTest = Handles.zTest;
                try
                {
                    Handles.zTest = depthTestGuides ? CompareFunction.LessEqual : CompareFunction.Always;
                    DrawGuides();
                    DrawEvaluatedChildren();
                    // Candidate guides are still guides: their dotted preview must use the same
                    // depth toggle as accepted guides and children, before restoring handle state.
                    DrawGenerationPreview();
                    if (showCardWireframe && !strokeActive && IsCardPreviewMode(previewMode) && cardHighlightLines.Length > 0)
                    { Handles.color = new Color(1f, 0.75f, 0.1f, 1f); Handles.DrawLines(cardHighlightLines); }
                }
                finally
                {
                    Handles.zTest = previousZTest;
                }
                if (showHelpers) DrawHelpers();
                DrawVertexSelection();
            }
        }

        private Matrix4x4 SourceToStageMatrix => sourceSpaceObject != null
            ? sourceSpaceObject.transform.localToWorldMatrix : Matrix4x4.identity;

        private Matrix4x4 GuidePoseMatrix(string guideId)
        {
            return authoringPose?.MatrixForGuide(groom, guideId) ?? Matrix4x4.identity;
        }

        private static float LocalHandleSize(Vector3 localPoint)
        {
            return HandleUtility.GetHandleSize(Handles.matrix.MultiplyPoint3x4(localPoint));
        }

        private Vector3 SourceToStagePoint(Vector3 point)
        {
            return sourceSpaceObject != null ? sourceSpaceObject.transform.TransformPoint(point) : point;
        }

        private Vector3 StageToSourcePoint(Vector3 point)
        {
            return sourceSpaceObject != null ? sourceSpaceObject.transform.InverseTransformPoint(point) : point;
        }

        private Vector3 StageToSourceDirection(Vector3 direction)
        {
            return SourceToStageMatrix.inverse.MultiplyVector(direction);
        }

        private Vector3 StageToSourceNormal(Vector3 normal)
        {
            return SourceToStageMatrix.transpose.MultiplyVector(normal).normalized;
        }

        private Vector3 SourceToStageNormal(Vector3 normal)
        {
            return SourceToStageMatrix.inverse.transpose.MultiplyVector(normal).normalized;
        }

        private float WorldBrushRadius
        {
            get
            {
                float x = SourceToStageMatrix.MultiplyVector(Vector3.right).magnitude;
                float y = SourceToStageMatrix.MultiplyVector(Vector3.up).magnitude;
                float z = SourceToStageMatrix.MultiplyVector(Vector3.forward).magnitude;
                return brushRadius * Mathf.Max(x, y, z);
            }
        }

        private bool TryGetPreviewBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = avatarPreview != null && avatarPreview.TryGetVisibleBounds(out bounds);
            EncapsulateVisibleRenderer(scalpRenderer, ref bounds, ref found);
            EncapsulateVisibleRenderer(hairRenderer, ref bounds, ref found);
            if (found) return true;

            Bounds sourceBounds = groom.SourceMesh.bounds;
            Vector3 center = SourceToStagePoint(sourceBounds.center);
            Vector3 extents = sourceBounds.extents;
            bounds = new Bounds(center, Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                bounds.Encapsulate(SourceToStagePoint(sourceBounds.center + Vector3.Scale(extents,
                    new Vector3(x, y, z))));
            return true;
        }

        private static void EncapsulateVisibleRenderer(Renderer renderer, ref Bounds bounds, ref bool found)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) return;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }

        private void DrawSceneToolbar(SceneView sceneView)
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(12f, 12f, Mathf.Min(980f, sceneView.position.width - 24f), 48f),
                GUIContent.none, EditorStyles.toolbar);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Workspace", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                HairGroomWorkspace.OpenForActiveStage();
            HairSceneTool toolbarTool = (HairSceneTool)EditorGUILayout.EnumPopup(sceneTool,
                EditorStyles.toolbarPopup, GUILayout.Width(110f));
            if (toolbarTool != sceneTool) SceneTool = toolbarTool;
            if (SupportsBrushAdjustment())
            {
                GUILayout.Label("Radius", GUILayout.Width(42f));
                brushRadius = GUILayout.HorizontalSlider(brushRadius, HairBrushInteractionUtility.MinimumRadius,
                    HairBrushInteractionUtility.MaximumRadius, GUILayout.Width(82f));
                GUILayout.Label("Hard", GUILayout.Width(31f));
                brushHardness = GUILayout.HorizontalSlider(brushHardness, 0f, 1f, GUILayout.Width(70f));
                GUILayout.Label("Strength", GUILayout.Width(52f));
                brushStrength = GUILayout.HorizontalSlider(brushStrength, 0.01f, 1f, GUILayout.Width(80f));
                if (IsGroomTool(sceneTool) && sceneTool != HairSceneTool.Cut)
                {
                    BrushScope = (HairBrushScope)EditorGUILayout.EnumPopup(brushScope,
                        EditorStyles.toolbarPopup, GUILayout.Width(130f));
                }
            }
            else if (sceneTool == HairSceneTool.Cut)
            {
                GUILayout.Label("Drag a slice line in the Scene view", EditorStyles.miniLabel,
                    GUILayout.Width(190f));
            }
            if (sceneTool == HairSceneTool.PaintGrowth)
            {
                bool mirrored = GUILayout.Toggle(mirrorPaintX, "Mirror X", EditorStyles.toolbarButton,
                    GUILayout.Width(66f));
                if (mirrored != mirrorPaintX) MirrorPaintX = mirrored;
            }
            else if (sceneTool == HairSceneTool.Cut)
            {
                bool mirrored = GUILayout.Toggle(mirrorCutX, "Mirror X", EditorStyles.toolbarButton,
                    GUILayout.Width(66f));
                if (mirrored != mirrorCutX) MirrorCutX = mirrored;
            }
            GUILayout.FlexibleSpace();
            int paintableTriangles = PaintableTriangleCount;
            GUILayout.Label(paintableTriangles > 0 ? $"Surface {paintableTriangles:N0} tris" : "No paint surface",
                EditorStyles.miniLabel);
            string badge = validation == null ? "Waiting" : $"{validation.ErrorCount} errors, {validation.WarningCount} warnings";
            GUILayout.Label(badge, EditorStyles.miniLabel);
            if (GUILayout.Button("Rebuild", EditorStyles.toolbarButton, GUILayout.Width(60f))) QueueRebuild(true);
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(46f))) SaveNow();
            GUILayout.EndHorizontal();
            GUILayout.Label($"{ActiveGroup?.name} / {ActiveGroup?.sculptLayers.Find(layer => layer != null && layer.Id == activeLayerId)?.name ?? "New sculpt layer"}" +
                $"    ·    {AffectedGuideCount:N0} affected    ·    {PreviewStatus}", EditorStyles.miniLabel);
            GUILayout.EndArea();
            sceneHelpStyle ??= new GUIStyle(EditorStyles.helpBox) { fontSize = 11, wordWrap = true };
            float helpWidth = Mathf.Max(1f, Mathf.Min(480f, sceneView.position.width - 24f));
            GUIContent help = new GUIContent(SceneHelpText);
            GUI.Label(new Rect(12f, 66f, helpWidth, sceneHelpStyle.CalcHeight(help, helpWidth)), help, sceneHelpStyle);
            Handles.EndGUI();
        }

        private void DrawGuides()
        {
            if (groom.Groups == null) return;
            for (int groupIndex = 0; groupIndex < groom.Groups.Count; groupIndex++)
            {
                HairGroup group = groom.Groups[groupIndex];
                if (group == null || !group.visible || !group.enabled || group.guides == null) continue;
                for (int guideIndex = 0; guideIndex < group.guides.Count; guideIndex++)
                {
                    HairGuide guide = group.guides[guideIndex];
                    if (guide == null || !guide.enabled || guide.points == null || guide.points.Count < 2 ||
                        (isolateSelectedGuides && !IsGuideSelected(guide.Id))) continue;
                    Matrix4x4 oldMatrix = Handles.matrix;
                    Handles.matrix = oldMatrix * GuidePoseMatrix(guide.Id);
                    try
                    {
                        bool selected = IsGuideSelected(guide.Id);
                        bool affected = brushHighlights.TryGetValue(guide.Id, out float influence);
                        Handles.color = affected ? Color.Lerp(new Color(0.2f, 1f, 0.8f), new Color(1f, 0.5f, 0.05f), influence) : selected ? Color.yellow : group.color;
                        IReadOnlyList<HairCurvePoint> displayPoints = displayGuideCurves.TryGetValue(guide.Id,
                            out HairEvaluatedCurve displayCurve) ? displayCurve.points : null;
                        if (showGuideSplines &&
                            displayGuidePolylines.TryGetValue(guide.Id, out Vector3[] polyline) &&
                            polyline.Length > 1)
                        {
                            if (showFreezeMask || sceneTool == HairSceneTool.Freeze)
                            {
                                if (affected) Handles.DrawAAPolyLine(6f, polyline);
                                for (int sample = 1; sample < polyline.Length; sample++)
                                {
                                    float control = sample / (polyline.Length - 1f) * (guide.points.Count - 1);
                                    int left = Mathf.Min(Mathf.FloorToInt(control), guide.points.Count - 1);
                                    int right = Mathf.Min(left + 1, guide.points.Count - 1);
                                    float frozen = Mathf.Lerp(guide.points[left].freeze, guide.points[right].freeze, control - left);
                                    Handles.color = Color.Lerp(new Color(0.15f, 0.85f, 1f), new Color(1f, 0.25f, 0.65f), frozen);
                                    Handles.DrawAAPolyLine(selected ? 4f : 3f, polyline[sample - 1], polyline[sample]);
                                }
                            }
                            else Handles.DrawAAPolyLine(selected ? 4f : 2f, polyline);
                        }
                        else if (showGuideSplines)
                        {
                            for (int pointIndex = 1; pointIndex < guide.points.Count; pointIndex++)
                                Handles.DrawAAPolyLine(selected ? 4f : 2f,
                                    guide.points[pointIndex - 1].position, guide.points[pointIndex].position);
                        }
                        if (!strokeActive || pointHandleStrokeActive)
                        {
                            Vector3 rootPosition = displayPoints != null && displayPoints.Count > 0
                                ? displayPoints[0].position
                                : guide.points[0].position;
                            if (showGuideRoots)
                            {
                                float rootSize = LocalHandleSize(rootPosition) * 0.035f * rootHandleScale;
                                if (Handles.Button(rootPosition, Quaternion.identity, rootSize, rootSize,
                                        Handles.DotHandleCap))
                                {
                                    if (Event.current.shift || Event.current.control || Event.current.command) ToggleGuideSelection(guide.Id);
                                    else SetActiveGuide(guide.Id, 0);
                                }
                            }
                            if (guide.Id == activeGuideId && showControlPoints)
                            {
                                for (int pointIndex = 0; pointIndex < guide.points.Count; pointIndex++)
                                {
                                    HairGuidePoint point = guide.points[pointIndex];
                                    Vector3 displayPosition = DisplayedControlPoint(guide, pointIndex);
                                    float size = LocalHandleSize(displayPosition) * 0.025f;
                                    Handles.color = pointIndex == activeGuidePoint ? Color.white : Color.yellow;
                                    if (Handles.Button(displayPosition, Quaternion.identity, size, size,
                                            Handles.SphereHandleCap))
                                        activeGuidePoint = pointIndex;
                                }
                                DrawSelectedPointHandle(guide);
                            }
                        }
                    }
                    finally
                    {
                        Handles.matrix = oldMatrix;
                    }
                }
            }
        }

        private void DrawEvaluatedChildren()
        {
            if (strokeActive || !showChildren || !showChildSplines || previewMode != HairPreviewMode.GuidesAndChildren ||
                evaluation?.curves == null)
                return;
            int childCount = evaluation.childCurveCount;
            int stride = Mathf.Max(1, childCount / 2500);
            int encountered = 0;
            for (int curveIndex = 0; curveIndex < evaluation.curves.Count; curveIndex++)
            {
                HairEvaluatedCurve curve = evaluation.curves[curveIndex];
                if (curve == null || !curve.isChild || curve.points.Count < 2) continue;
                if (encountered++ % stride != 0) continue;
                Matrix4x4 oldMatrix = Handles.matrix;
                Handles.matrix = oldMatrix * GuidePoseMatrix(curve.parentGuideId);
                Color color = curve.groupColor;
                color.a = 0.38f;
                Handles.color = color;
                for (int pointIndex = 1; pointIndex < curve.points.Count; pointIndex++)
                    Handles.DrawDottedLine(curve.points[pointIndex - 1].position,
                        curve.points[pointIndex].position, 3f);
                Handles.matrix = oldMatrix;
            }
        }

        private void DrawSelectedPointHandle(HairGuide guide)
        {
            if (guide == null || activeGuidePoint <= 0 || activeGuidePoint >= guide.points.Count ||
                ActiveGroup == null || ActiveGroup.locked || guide.points[activeGuidePoint].freeze >= 0.999f) return;
            EditorGUI.BeginChangeCheck();
            Vector3 position = Handles.PositionHandle(DisplayedControlPoint(guide, activeGuidePoint), Quaternion.identity);
            if (!EditorGUI.EndChangeCheck()) return;
            MoveGuidePoint(guide.Id, activeGuidePoint, position);
        }

        internal Vector3 DisplayedControlPoint(HairGuide guide, int index)
        {
            if (displayGuidePolylines.TryGetValue(guide.Id, out Vector3[] points) && points.Length > 1)
                return HairCurveBrushUtility.SamplePolyline(points, index / (guide.points.Count - 1f));
            return guide.points[index].position;
        }

        internal bool MoveGuidePoint(string guideId, int index, Vector3 target)
        {
            HairGroup group = ActiveGroup;
            CurveBrushEntry entry = curveBrushEntries.Find(item => item.Guide.Id == guideId);
            HairGuide guide = entry?.Guide;
            if (group == null || group.locked || !group.visible || !group.enabled || guide == null ||
                !guide.enabled || index <= 0 || index >= guide.points.Count || guide.points[index].freeze >= 0.999f ||
                !float.IsFinite(target.x) || !float.IsFinite(target.y) || !float.IsFinite(target.z)) return false;
            EnsurePointBuffers(entry, guide.points.Count);
            for (int i = 0; i < guide.points.Count; i++)
                entry.TargetPoints[i] = entry.OriginalPoints[i] = HairCurveBrushUtility.SamplePolyline(
                    entry.SourcePoints, i / (guide.points.Count - 1f));
            entry.TargetPoints[index] = Vector3.Lerp(entry.OriginalPoints[index], target, 1f - guide.points[index].freeze);
            HairGuideShapeUtility.PreserveSegmentLengths(entry.OriginalPoints, entry.TargetPoints, guide.points);
            bool changed = false;
            for (int i = 1; i < guide.points.Count; i++)
                changed |= (entry.TargetPoints[i] - entry.OriginalPoints[i]).sqrMagnitude > 1e-16f;
            if (!changed) return false;
            pointHandleStrokeActive = true;
            BeginStroke("Sculpt Hair Control Point");
            HairSculptLayer layer = ResolveSculptLayer(group);
            HairGuideDelta delta = null;
            ApplyGuideTarget(entry, guide, layer, ref delta);
            EditorUtility.SetDirty(groom);
            QueueInteractiveGuideRefresh();
            return true;
        }

        private void DrawHelpers()
        {
            if (groom.SharedHelpers == null) return;
            for (int i = 0; i < groom.SharedHelpers.Count; i++)
            {
                HairHelper helper = groom.SharedHelpers[i];
                if (helper == null || !helper.visible) continue;
                bool selected = helper.Id == activeHelperId;
                Handles.color = selected ? Color.cyan : new Color(0.2f, 0.8f, 0.9f, 0.7f);
                Matrix4x4 outerMatrix = Handles.matrix;
                Matrix4x4 helperPose = helperPoseMatrices.TryGetValue(helper.Id, out Matrix4x4 cachedPose)
                    ? cachedPose : Matrix4x4.identity;
                Matrix4x4 posedSourceMatrix = outerMatrix * helperPose;
                Handles.matrix = posedSourceMatrix * Matrix4x4.TRS(helper.position, helper.rotation, helper.scale);
                try
                {
                    switch (helper.type)
                    {
                        case HairHelperType.Sphere:
                        case HairHelperType.Attractor:
                        case HairHelperType.Repulsor:
                            Handles.DrawWireDisc(Vector3.zero, Vector3.up, helper.radius);
                            Handles.DrawWireDisc(Vector3.zero, Vector3.right, helper.radius);
                            break;
                        case HairHelperType.Box:
                        case HairHelperType.SculptCage:
                        case HairHelperType.VolumeTarget:
                            Handles.DrawWireCube(Vector3.zero, helper.size);
                            break;
                        default:
                            if (helper.points != null && helper.points.Count > 1)
                            {
                                Handles.matrix = posedSourceMatrix;
                                for (int p = 1; p < helper.points.Count; p++)
                                    Handles.DrawAAPolyLine(selected ? 4f : 2f,
                                        helper.points[p - 1], helper.points[p]);
                            }
                            else Handles.DrawWireDisc(Vector3.zero, Vector3.up,
                                Mathf.Max(0.01f, helper.radius));
                            break;
                    }
                    Handles.matrix = posedSourceMatrix;
                    float size = LocalHandleSize(helper.position) * 0.04f;
                    if (Handles.Button(helper.position, Quaternion.identity, size, size,
                            Handles.RectangleHandleCap))
                        SetActiveHelper(helper.Id);
                    if (selected && !helper.locked)
                    {
                        EditorGUI.BeginChangeCheck();
                        Vector3 position = Handles.PositionHandle(helper.position, helper.rotation);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(groom, "Move Hair Helper");
                            Vector3 delta = position - helper.position;
                            helper.position = position;
                            if (helper.points != null && helper.points.Count > 0)
                                for (int pointIndex = 0; pointIndex < helper.points.Count; pointIndex++)
                                    helper.points[pointIndex] += delta;
                            HairGroomCommands.Commit(groom);
                        }
                    }
                }
                finally
                {
                    Handles.matrix = outerMatrix;
                }
            }
        }

        private void DrawGrowthMap()
        {
            HairGrowthMap map = ActiveMap;
            Mesh mesh = groom.SourceMesh;
            if (map == null || mesh == null || map.values == null) return;
            Vector3[] vertices = mesh.vertices;
            int stride = Mathf.Max(1, vertices.Length / 3000);
            for (int i = 0; i < vertices.Length && i < map.values.Length; i += stride)
            {
                if (!IsSourceVertexVisible(i)) continue;
                float normalized = Mathf.InverseLerp(map.valueRange.x, map.valueRange.y, map.values[i]);
                Handles.color = Color.Lerp(new Color(0.03f, 0.08f, 0.2f, 0.35f),
                    new Color(1f, 0.25f, 0.03f, 0.9f), normalized);
                Vector3 displayVertex = authoringPose?.PosedVertex(i) ?? vertices[i];
                float size = LocalHandleSize(displayVertex) * 0.008f;
                Handles.DotHandleCap(0, displayVertex, Quaternion.identity, size, EventType.Repaint);
            }
        }

        private void DrawGenerationPreview()
        {
            if (generationPreview?.guides == null) return;
            for (int guideIndex = 0; guideIndex < generationPreview.guides.Count; guideIndex++)
            {
                HairGuide guide = generationPreview.guides[guideIndex];
                if (guide == null) continue;
                Matrix4x4 oldMatrix = Handles.matrix;
                if (authoringPose != null && authoringPose.TryGetMatrix(guide.root, out Matrix4x4 guidePose))
                    Handles.matrix = oldMatrix * guidePose;
                Handles.color = new Color(0.1f, 1f, 0.8f, 0.9f);
                for (int pointIndex = 1; pointIndex < guide.points.Count; pointIndex++)
                    Handles.DrawDottedLine(guide.points[pointIndex - 1].position,
                        guide.points[pointIndex].position, 4f);
                Handles.matrix = oldMatrix;
            }
        }

        private void HandleSceneInput(SceneView sceneView, int controlId, int brushModifierControlId)
        {
            Event current = Event.current;
            if (current == null) return;
            if (current.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlId);
                return;
            }
            if (current.type == EventType.MouseMove)
            {
                sceneView.Repaint();
                return;
            }
            if (current.type == EventType.MouseDrag) sceneView.Repaint();

            if (current.type == EventType.MouseLeaveWindow)
            {
                brushHighlights.Clear();
                ReleaseSceneInputCapture(true);
                return;
            }
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                ReleaseSceneInputCapture(true);
                current.Use();
                return;
            }
            if (HandleBrushShortcuts(current, sceneView)) return;
            if (HandleBrushModifierDrag(current, sceneView, brushModifierControlId)) return;
            if (current.alt)
            {
                ReleaseSceneInputCapture(true);
                return;
            }
            if (current.type == EventType.Used || current.button != 0) return;
            if (!HairWorkflowState.IsToolAllowed(workflowStep, sceneTool)) return;

            if (sceneTool == HairSceneTool.Select && IsCardPreviewMode(previewMode) &&
                current.type == EventType.MouseDown && CanCaptureSceneInput(controlId) &&
                PickCard(HandleUtility.GUIPointToWorldRay(current.mousePosition)))
            { current.Use(); return; }

            if (sceneTool == HairSceneTool.Select && workflowStep == HairWorkflowStep.Growth &&
                HandleVertexSelectionEvent(current, controlId))
            {
                return;
            }
            bool interactiveTool = sceneTool != HairSceneTool.Select && sceneTool != HairSceneTool.Helper;
            if (!interactiveTool) return;
            if (ActiveGroup == null || ActiveGroup.locked || !ActiveGroup.visible || !ActiveGroup.enabled)
            {
                actionStatus = "Choose a visible, enabled, unlocked group before editing.";
                if (current.type == EventType.MouseDown || current.type == EventType.MouseDrag) current.Use();
                return;
            }
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (sceneTool == HairSceneTool.Cut)
            {
                HandleSliceCutEvent(sceneView, current, controlId);
                return;
            }
            if (IsGroomTool(sceneTool))
            {
                if (strokeActive && OwnsSceneInput(controlId))
                {
                    HandleActiveCurveBrushEvent(sceneView, current, ray, controlId);
                }
                else if (TryGetCurveBrushCenter(ray, out Vector3 curveCenter,
                             out Vector3 curveWorldCenter, out float poseRadiusScale))
                {
                    HandleCurveBrushEvent(sceneView, current, ray, curveCenter,
                        curveWorldCenter, poseRadiusScale, controlId);
                }
                else
                {
                    ConsumeMissedSurfaceGesture(current, controlId);
                }
                return;
            }
            if (!TryRaycastSourceSurface(ray, out HairSurfaceHit hit))
            {
                ConsumeMissedSurfaceGesture(current, controlId);
                return;
            }

            Color brushColor = EffectivePaintErase ? new Color(1f, 0.2f, 0.2f, 0.9f) :
                new Color(0.2f, 0.9f, 1f, 0.9f);
            DrawBrushCursor(hit.WorldPoint, hit.WorldNormal, WorldBrushRadius, brushColor);
            if (sceneTool == HairSceneTool.PaintGrowth && mirrorPaintX &&
                Mathf.Abs(hit.SourcePoint.x) > 0.00001f &&
                TryGetMirroredBrushCursor(hit, out Vector3 mirroredWorldPoint,
                    out Vector3 mirroredWorldNormal))
            {
                Color mirroredColor = brushColor;
                mirroredColor.a *= 0.72f;
                DrawBrushCursor(mirroredWorldPoint, mirroredWorldNormal, WorldBrushRadius, mirroredColor);
            }
            if (current.type == EventType.MouseDown)
            {
                if (!CanCaptureSceneInput(controlId)) return;
                CaptureSceneInput(controlId);
                BeginStroke();
                ApplySceneTool(hit, Vector3.zero);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && OwnsSceneInput(controlId))
            {
                if (strokeActive)
                {
                    Vector3 delta = hasPreviousStrokePosition
                        ? hit.SourcePoint - previousStrokePosition : Vector3.zero;
                    ApplySceneTool(hit, delta);
                }
                current.Use();
            }
            else if (current.type == EventType.MouseUp && OwnsSceneInput(controlId))
            {
                ReleaseSceneInputCapture(true);
                current.Use();
            }
        }

        private bool HandleVertexSelectionEvent(Event current, int controlId)
        {
            if (current.type == EventType.MouseDown)
            {
                if (!CanCaptureSceneInput(controlId)) return false;
                CaptureSceneInput(controlId);
                SelectTriangleUnderCursor(current);
                current.Use();
                return true;
            }
            if (current.type == EventType.MouseDrag && OwnsSceneInput(controlId))
            {
                SelectTriangleUnderCursor(current);
                current.Use();
                return true;
            }
            if (current.type == EventType.MouseUp && OwnsSceneInput(controlId))
            {
                ReleaseSceneInputCapture(false);
                current.Use();
                return true;
            }
            return false;
        }

        private void SelectTriangleUnderCursor(Event current)
        {
            Ray selectionRay = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (TryRaycastSourceSurface(selectionRay, out HairSurfaceHit selectionHit))
            {
                SelectTriangle(selectionHit.TriangleIndex, current.shift,
                    current.control || current.command);
            }
        }

        private bool TryRaycastSourceSurface(Ray worldRay, out HairSurfaceHit hit)
        {
            hit = default;
            if (surfaceRaycaster != null)
            {
                Ray sourceRay = new Ray(StageToSourcePoint(worldRay.origin),
                    StageToSourceDirection(worldRay.direction).normalized);
                if (surfaceRaycaster.Raycast(sourceRay, out HairMeshRaycastHit surfaceHit))
                {
                    Vector3 sourcePoint = StageToSourcePoint(SourceToStagePoint(surfaceHit.Point));
                    Vector3 sourceNormal = StageToSourceNormal(SourceToStageNormal(surfaceHit.Normal));
                    if (TryResolveTriangle(surfaceHit.TriangleIndex, out _, out _,
                            out int a, out int b, out int c) &&
                        (uint)a < (uint)sourceVertices.Length && (uint)b < (uint)sourceVertices.Length &&
                        (uint)c < (uint)sourceVertices.Length)
                    {
                        Vector3 barycentric = surfaceHit.Barycentric;
                        sourcePoint = sourceVertices[a] * barycentric.x +
                                      sourceVertices[b] * barycentric.y +
                                      sourceVertices[c] * barycentric.z;
                        if (sourceNormals.Length == sourceVertices.Length)
                        {
                            sourceNormal = sourceNormals[a] * barycentric.x +
                                           sourceNormals[b] * barycentric.y +
                                           sourceNormals[c] * barycentric.z;
                        }
                        else
                        {
                            sourceNormal = Vector3.Cross(sourceVertices[b] - sourceVertices[a],
                                sourceVertices[c] - sourceVertices[a]);
                        }
                        sourceNormal = sourceNormal.sqrMagnitude > 0.0000001f
                            ? sourceNormal.normalized : Vector3.up;
                    }
                    // Use the visible/posed surface's interpolated normal, just as the mirrored
                    // cursor does. Source-space normals are only for painting and mirror probes.
                    Vector3 brushNormal = surfaceRaycaster.TryGetSurfaceNormal(surfaceHit.TriangleIndex,
                        surfaceHit.Barycentric, out Vector3 surfaceNormal) ? surfaceNormal : surfaceHit.Normal;
                    hit = new HairSurfaceHit(surfaceHit.TriangleIndex, SourceToStagePoint(surfaceHit.Point),
                        SourceToStageNormal(brushNormal), sourcePoint, sourceNormal);
                    return true;
                }
            }

            // Retain the collider as a last-resort path for unusual meshes that cannot be read back.
            if (scalpCollider == null || !scalpCollider.Raycast(worldRay, out RaycastHit colliderHit, 10000f))
                return false;
            hit = new HairSurfaceHit(colliderHit.triangleIndex, colliderHit.point, colliderHit.normal,
                StageToSourcePoint(colliderHit.point), StageToSourceNormal(colliderHit.normal));
            return true;
        }

        private bool TryGetMirroredBrushCursor(HairSurfaceHit sourceHit, out Vector3 worldPoint,
            out Vector3 worldNormal)
        {
            worldPoint = Vector3.zero;
            worldNormal = Vector3.up;
            if (sourceSurfaceRaycaster == null || authoringPose == null || groom?.SourceMesh == null)
                return false;

            Vector3 target = HairBrushInteractionUtility.MirrorX(sourceHit.SourcePoint);
            Vector3 targetNormal = HairBrushInteractionUtility.MirrorX(sourceHit.SourceNormal);
            targetNormal = targetNormal.sqrMagnitude > 0.0000001f ? targetNormal.normalized : Vector3.up;
            float probeDistance = Mathf.Max(brushRadius * 4f,
                groom.SourceMesh.bounds.size.magnitude * 2f, 0.01f);
            bool found = TryRaycastVisibleMirrorProbe(
                new Ray(target + targetNormal * probeDistance, -targetNormal),
                out HairMeshRaycastHit bestHit);
            if (TryRaycastVisibleMirrorProbe(
                    new Ray(target - targetNormal * probeDistance, targetNormal),
                    out HairMeshRaycastHit reverseHit) &&
                (!found || (reverseHit.Point - target).sqrMagnitude < (bestHit.Point - target).sqrMagnitude))
            {
                bestHit = reverseHit;
                found = true;
            }
            if (!found || !authoringPose.TryPoseTrianglePoint(bestHit.TriangleIndex, bestHit.Barycentric,
                    out Vector3 posedPoint, out Vector3 posedNormal)) return false;
            worldPoint = SourceToStagePoint(posedPoint);
            worldNormal = SourceToStageNormal(posedNormal);
            return true;
        }

        private bool TryRaycastVisibleMirrorProbe(Ray ray, out HairMeshRaycastHit hit)
        {
            hit = default;
            if (sourceSurfaceRaycaster == null || !sourceSurfaceRaycaster.Raycast(ray, out HairMeshRaycastHit candidate) ||
                !sourceSurfaceRaycaster.TryGetTriangleVertices(candidate.TriangleIndex,
                    out int a, out int b, out int c) ||
                !IsSourceVertexVisible(a) || !IsSourceVertexVisible(b) || !IsSourceVertexVisible(c)) return false;
            hit = candidate;
            return true;
        }

        private void ConsumeMissedSurfaceGesture(Event current, int controlId)
        {
            if (current.type == EventType.MouseDown)
            {
                if (!CanCaptureSceneInput(controlId)) return;
                CaptureSceneInput(controlId);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && OwnsSceneInput(controlId))
            {
                current.Use();
            }
            else if (current.type == EventType.MouseUp && OwnsSceneInput(controlId))
            {
                ReleaseSceneInputCapture(true);
                current.Use();
            }
        }

        private void HandleSliceCutEvent(SceneView sceneView, Event current, int controlId)
        {
            if (sliceCutActive && current.type == EventType.Repaint)
                DrawSliceCutOverlay();

            if (current.type == EventType.MouseDown)
            {
                if (GUIUtility.hotControl != 0 && GUIUtility.hotControl != controlId) return;
                EndGravitySimulation();
                CaptureSceneInput(controlId);
                BeginStroke("Slice Cut Hair");
                sliceCutActive = true;
                sliceStartMouse = current.mousePosition;
                sliceEndMouse = current.mousePosition;
                actionStatus = mirrorCutX
                    ? "Drag the slice line. Release to cut the active group and its X mirror."
                    : "Drag the slice line. Release to cut the intersecting guide tips.";
                current.Use();
                sceneView.Repaint();
            }
            else if (current.type == EventType.MouseDrag && OwnsSceneInput(controlId) && sliceCutActive)
            {
                sliceEndMouse = current.mousePosition;
                current.Use();
                sceneView.Repaint();
            }
            else if (current.type == EventType.MouseUp && OwnsSceneInput(controlId))
            {
                sliceEndMouse = current.mousePosition;
                int cutCount = sliceCutActive ? ApplySliceCut() : 0;
                sliceCutActive = false;
                actionStatus = cutCount > 0
                    ? $"Slice cut {cutCount:N0} guides; the tip side was removed."
                    : Vector2.Distance(sliceStartMouse, sliceEndMouse) < HairSliceUtility.MinimumGesturePixels
                        ? "Slice cancelled: drag a longer line."
                        : "The slice did not cross any active guide.";
                ReleaseSceneInputCapture(true);
                if (cutCount > 0) QueueRebuild(true);
                current.Use();
            }
        }

        private void DrawSliceCutOverlay()
        {
            UpdateSlicePreview();
            using (new Handles.DrawingScope(new Color(1f, 0.2f, 0.1f, 0.95f), SourceToStageMatrix))
            {
                foreach (CurveBrushEntry entry in curveBrushEntries)
                {
                    if (!slicePreview.TryGetValue(entry.Guide.Id, out float cut)) continue;
                    Vector3 intersection = HairCurveBrushUtility.SamplePolyline(entry.PosedPoints, cut);
                    Handles.SphereHandleCap(0, intersection, Quaternion.identity,
                        LocalHandleSize(intersection) * 0.035f, EventType.Repaint);
                    int start = Mathf.Clamp(Mathf.CeilToInt(cut * (entry.PosedPoints.Length - 1)), 1, entry.PosedPoints.Length - 1);
                    Handles.DrawAAPolyLine(5f, intersection, entry.PosedPoints[start]);
                    for (int i = start + 1; i < entry.PosedPoints.Length; i++)
                        Handles.DrawAAPolyLine(5f, entry.PosedPoints[i - 1], entry.PosedPoints[i]);
                }
            }
            Handles.BeginGUI();
            Color oldColor = Handles.color;
            Handles.color = new Color(1f, 0.32f, 0.12f, 0.98f);
            Handles.DrawAAPolyLine(5f,
                new Vector3(sliceStartMouse.x, sliceStartMouse.y),
                new Vector3(sliceEndMouse.x, sliceEndMouse.y));
            Handles.color = Color.white;
            Handles.DrawAAPolyLine(1.5f,
                new Vector3(sliceStartMouse.x, sliceStartMouse.y),
                new Vector3(sliceEndMouse.x, sliceEndMouse.y));
            Vector2 midpoint = (sliceStartMouse + sliceEndMouse) * 0.5f;
            GUI.Label(new Rect(midpoint.x + 12f, midpoint.y + 10f, 285f, 38f),
                $"{slicePreview.Count:N0} guides to cut{(mirrorCutX ? " · Mirror X included" : "")}\nRed = removed tips · Esc cancels",
                EditorStyles.helpBox);
            Handles.color = oldColor;
            Handles.EndGUI();
        }

        private int ApplySliceCut()
        {
            UpdateSlicePreview();
            int count = 0;
            foreach (CurveBrushEntry entry in curveBrushEntries)
                if (slicePreview.TryGetValue(entry.Guide.Id, out float cut) &&
                    HairSliceUtility.TruncateGuide(ActiveGroup, entry.Guide, cut))
                {
                    count++;
                    if (entry.Guide.Id == activeGuideId) activeGuidePoint = Mathf.Min(activeGuidePoint, entry.Guide.points.Count - 1);
                }
            slicePreview.Clear();
            return count;
        }

        private void UpdateSlicePreview()
        {
            slicePreview.Clear();
            if ((sliceEndMouse - sliceStartMouse).sqrMagnitude <
                HairSliceUtility.MinimumGesturePixels * HairSliceUtility.MinimumGesturePixels)
                return;
            Ray startRay = WorldToPosedRay(HandleUtility.GUIPointToWorldRay(sliceStartMouse));
            Ray endRay = WorldToPosedRay(HandleUtility.GUIPointToWorldRay(sliceEndMouse));
            if (!HairSliceUtility.TryCreateCameraPlane(startRay, endRay, out Plane slicePlane))
                return;

            CollectSlicePreview(slicePlane, planePoint => HairSliceUtility.IsOnFiniteGesture(
                sliceStartMouse, sliceEndMouse, HandleUtility.WorldToGUIPoint(SourceToStagePoint(planePoint))));
        }

        private void CollectSlicePreview(Plane slicePlane, Predicate<Vector3> withinGesture)
        {
            slicePreview.Clear();
            HairGroup group = ActiveGroup;
            if (group == null || group.locked) return;
            for (int entryIndex = 0; entryIndex < curveBrushEntries.Count; entryIndex++)
            {
                CurveBrushEntry entry = curveBrushEntries[entryIndex];
                HairGuide guide = entry.Guide;
                if (guide?.points == null || guide.points.Count < 2 || !GuideInEditScope(guide)) continue;
                float earliest = float.MaxValue;
                if (TryGetSliceCurvePosition(entry, slicePlane, false, withinGesture, out float primary))
                    earliest = primary;
                if (mirrorCutX && TryGetSliceCurvePosition(entry, slicePlane, true, withinGesture, out float mirrored))
                    earliest = Mathf.Min(earliest, mirrored);
                if (earliest == float.MaxValue) continue;
                int end = Mathf.Clamp(Mathf.CeilToInt(earliest * (guide.points.Count - 1)), 1, guide.points.Count - 1);
                bool frozen = false;
                for (int i = end; i < guide.points.Count; i++) frozen |= guide.points[i].freeze >= 0.999f;
                if (!frozen && (brushScope != HairBrushScope.VisibleHair || IsPosedPointVisible(
                    HairCurveBrushUtility.SamplePolyline(entry.PosedPoints, earliest)))) slicePreview[guide.Id] = earliest;
            }
        }

        private bool TryGetSliceCurvePosition(CurveBrushEntry entry, Plane slicePlane,
            bool mirrored, Predicate<Vector3> withinGesture, out float curvePosition)
        {
            curvePosition = 0f;
            if (!HairSliceUtility.TryFindRootFirstIntersection(entry.PosedPoints, slicePlane,
                    mirrored, withinGesture,
                    out HairSliceIntersection intersection)) return false;
            curvePosition = (intersection.SegmentEndIndex - 1f + intersection.SegmentT) /
                            (entry.PosedPoints.Length - 1f);
            return curvePosition < 0.99999f;
        }

        private void HandleCurveBrushEvent(SceneView sceneView, Event current, Ray worldRay,
            Vector3 curveCenter, Vector3 curveWorldCenter, float poseRadiusScale, int controlId)
        {
            Vector3 worldViewNormal = sceneView.camera != null ? -sceneView.camera.transform.forward : Vector3.up;
            Vector3 viewNormal = StageToSourceDirection(worldViewNormal).normalized;
            Color brushColor = paintErase ? new Color(1f, 0.2f, 0.2f, 0.9f) :
                new Color(0.2f, 0.9f, 1f, 0.9f);
            DrawBrushCursor(curveWorldCenter, worldViewNormal,
                WorldBrushRadius * poseRadiusScale, brushColor);
            if (current.type == EventType.MouseDown)
            {
                if (!CanCaptureSceneInput(controlId)) return;
                EndGravitySimulation();
                CaptureSceneInput(controlId);
                BeginStroke();
                curveStrokePlane = new Plane(viewNormal, curveCenter);
                curveStrokePoseRadiusScale = poseRadiusScale;
                Ray posedRay = WorldToPosedRay(worldRay);
                curveStrokePlaneValid = curveStrokePlane.Raycast(posedRay, out float enter);
                previousCurvePlanePoint = curveStrokePlaneValid ? posedRay.GetPoint(enter) : curveCenter;
                SculptAt(curveCenter, viewNormal, Vector3.zero);
                current.Use();
            }
        }

        private void HandleActiveCurveBrushEvent(SceneView sceneView, Event current, Ray worldRay,
            int controlId)
        {
            Vector3 worldViewNormal = sceneView.camera != null ? -sceneView.camera.transform.forward : Vector3.up;
            Vector3 posedViewNormal = StageToSourceDirection(worldViewNormal).normalized;
            Ray posedRay = WorldToPosedRay(worldRay);
            if (curveStrokePlaneValid && curveStrokePlane.Raycast(posedRay, out float enter))
            {
                Vector3 center = posedRay.GetPoint(enter);
                DrawBrushCursor(SourceToStagePoint(center), worldViewNormal,
                    WorldBrushRadius * curveStrokePoseRadiusScale,
                    paintErase ? new Color(1f, 0.2f, 0.2f, 0.9f) : new Color(0.2f, 0.9f, 1f, 0.9f));
                if (current.type == EventType.MouseDrag)
                {
                    Vector3 delta = HairBrushInteractionUtility.ClampStrokeDelta(
                        center - previousCurvePlanePoint, brushRadius * curveStrokePoseRadiusScale);
                    SculptAt(center, posedViewNormal, delta);
                    previousCurvePlanePoint = center;
                    EditorUtility.SetDirty(groom);
                    current.Use();
                }
            }
            if (current.type == EventType.MouseUp && OwnsSceneInput(controlId))
            {
                ReleaseSceneInputCapture(true);
                current.Use();
            }
        }

        private Ray WorldToPosedRay(Ray worldRay)
        {
            return new Ray(StageToSourcePoint(worldRay.origin),
                StageToSourceDirection(worldRay.direction).normalized);
        }

        private bool CanCaptureSceneInput(int controlId)
        {
            return (GUIUtility.hotControl == 0 || GUIUtility.hotControl == controlId) &&
                   (HandleUtility.nearestControl <= 0 || HandleUtility.nearestControl == controlId);
        }

        private void CaptureSceneInput(int controlId)
        {
            sceneInputHotControl = controlId;
            GUIUtility.hotControl = controlId;
        }

        private bool OwnsSceneInput(int controlId)
        {
            return sceneInputHotControl == controlId && GUIUtility.hotControl == controlId;
        }

        private void ReleaseSceneInputCapture(bool finishStroke)
        {
            shiftPaintErase = false;
            if (finishStroke) EndStroke();
            if (sceneInputHotControl != 0 && GUIUtility.hotControl == sceneInputHotControl)
                GUIUtility.hotControl = 0;
            sceneInputHotControl = 0;
            ReleaseBrushModifierCapture();
        }

        private bool HandleBrushShortcuts(Event current, SceneView sceneView)
        {
            if (current == null || current.type != EventType.KeyDown || EditorGUIUtility.editingTextField ||
                current.control || current.command || current.alt || Tools.viewToolActive)
                return false;
            if (!current.shift && current.keyCode == KeyCode.M &&
                (sceneTool == HairSceneTool.PaintGrowth || sceneTool == HairSceneTool.Cut))
            {
                if (sceneTool == HairSceneTool.Cut) MirrorCutX = !mirrorCutX;
                else MirrorPaintX = !mirrorPaintX;
                current.Use();
                sceneView.Repaint();
                HairGroomWorkspace.RepaintOpenWindows();
                return true;
            }
            if (!SupportsBrushAdjustment()) return false;
            if (current.keyCode != KeyCode.LeftBracket && current.keyCode != KeyCode.RightBracket)
                return false;

            float direction = current.keyCode == KeyCode.RightBracket ? 1f : -1f;
            if (current.shift)
            {
                brushHardness = HairBrushInteractionUtility.StepHardness(brushHardness, direction);
                actionStatus = $"Brush hardness {brushHardness:0.00}. Shift+right-drag vertically also adjusts hardness.";
            }
            else
            {
                brushRadius = HairBrushInteractionUtility.StepRadius(brushRadius, direction);
                actionStatus = $"Brush radius {brushRadius:0.000}. Shift+right-drag horizontally also adjusts size.";
            }
            current.Use();
            sceneView.Repaint();
            HairGroomWorkspace.RepaintOpenWindows();
            return true;
        }

        private bool HandleBrushModifierDrag(Event current, SceneView sceneView, int controlId)
        {
            if (current == null || !SupportsBrushAdjustment()) return false;
            bool altMouseGesture = (current.alt ||
                                    (current.modifiers & EventModifiers.Alt) != EventModifiers.None) &&
                                   (current.type == EventType.MouseDown || current.type == EventType.MouseDrag ||
                                    current.rawType == EventType.MouseDown || current.rawType == EventType.MouseDrag ||
                                    current.rawType == EventType.MouseUp);
            if (altMouseGesture)
            {
                ReleaseBrushModifierCapture();
                return false;
            }
            if (modifierBrushHotControl != 0 && GUIUtility.hotControl != modifierBrushHotControl)
            {
                ReleaseBrushModifierCapture();
                return false;
            }
            if (current.type == EventType.MouseDown && current.button == 1 && current.shift && !current.alt)
            {
                modifierBrushDrag = true;
                modifierBrushStartMouse = current.mousePosition;
                modifierBrushCurrentMouse = current.mousePosition;
                modifierBrushStartRadius = brushRadius;
                modifierBrushStartHardness = brushHardness;
                CaptureBrushAdjustmentCursor(HandleUtility.GUIPointToWorldRay(current.mousePosition),
                    sceneView.camera != null ? -sceneView.camera.transform.forward : Vector3.up, sceneView.pivot);
                modifierBrushHotControl = controlId;
                GUIUtility.hotControl = modifierBrushHotControl;
                current.Use();
                sceneView.Repaint();
                return true;
            }
            if (modifierBrushDrag && current.type == EventType.MouseDrag)
            {
                modifierBrushCurrentMouse = current.mousePosition;
                Vector2 delta = modifierBrushCurrentMouse - modifierBrushStartMouse;
                brushRadius = HairBrushInteractionUtility.RadiusFromModifierDrag(
                    modifierBrushStartRadius, delta.x);
                brushHardness = HairBrushInteractionUtility.HardnessFromModifierDrag(
                    modifierBrushStartHardness, delta.y);
                actionStatus = $"Brush radius {brushRadius:0.000} | hardness {brushHardness:0.00}";
                current.Use();
                sceneView.Repaint();
                HairGroomWorkspace.RepaintOpenWindows();
                return true;
            }
            if (modifierBrushDrag && current.type == EventType.Repaint)
            {
                DrawBrushAdjustmentCursor();
                return true;
            }
            if (modifierBrushDrag && (current.rawType == EventType.MouseUp ||
                                      current.type == EventType.MouseLeaveWindow))
            {
                ReleaseBrushModifierCapture();
                current.Use();
                sceneView.Repaint();
                return true;
            }
            return false;
        }

        private void ReleaseBrushModifierCapture()
        {
            int ownedControl = modifierBrushHotControl;
            modifierBrushHotControl = 0;
            if (ownedControl != 0 && GUIUtility.hotControl == ownedControl) GUIUtility.hotControl = 0;
            modifierBrushDrag = false;
            modifierBrushCursorValid = false;
            modifierBrushMirrorValid = false;
        }

        private bool SupportsBrushAdjustment()
        {
            return sceneTool == HairSceneTool.PaintGrowth ||
                   (IsGroomTool(sceneTool) && sceneTool != HairSceneTool.Cut);
        }

        private void DrawBrushCursor(Vector3 center, Vector3 normal, float radius, Color color)
        {
            Handles.color = Color.black;
            Handles.DrawWireDisc(center, normal, radius * 1.04f);
            Handles.color = color;
            Handles.DrawWireDisc(center, normal, radius);
            if (brushHardness > 0.0001f)
            {
                Handles.color = new Color(1f, 1f, 1f, 0.72f);
                Handles.DrawWireDisc(center, normal, radius * brushHardness);
            }
        }

        internal void CaptureBrushAdjustmentCursor(Ray ray, Vector3 viewNormal, Vector3 fallbackCenter)
        {
            modifierBrushCursorValid = false;
            modifierBrushMirrorValid = false;
            modifierBrushPoseRadiusScale = 1f;
            modifierBrushWorldNormal = viewNormal.sqrMagnitude > 1e-8f ? viewNormal.normalized : -ray.direction;
            // Resolve once at gesture start. Changing radius must not pick a different guide/depth,
            // or move the preview off the head as the mouse drags away to resize the brush.
            if (IsGroomTool(sceneTool) && TryGetCurveBrushCenter(ray, out _, out Vector3 curveCenter,
                    out float poseScale))
            {
                modifierBrushWorldCenter = curveCenter;
                modifierBrushPoseRadiusScale = poseScale;
                modifierBrushCursorValid = true;
                return;
            }
            if (TryRaycastSourceSurface(ray, out HairSurfaceHit hit))
            {
                modifierBrushWorldCenter = hit.WorldPoint;
                if (sceneTool == HairSceneTool.PaintGrowth)
                {
                    modifierBrushWorldNormal = hit.WorldNormal;
                    modifierBrushMirrorValid = mirrorPaintX && Mathf.Abs(hit.SourcePoint.x) > 0.00001f &&
                        TryGetMirroredBrushCursor(hit, out modifierBrushMirrorCenter, out modifierBrushMirrorNormal);
                }
                modifierBrushCursorValid = true;
                return;
            }
            // Empty-space adjustment still has a meaningful world-size preview at the view pivot.
            Plane plane = new Plane(modifierBrushWorldNormal, fallbackCenter);
            if (plane.Raycast(ray, out float enter))
            {
                modifierBrushWorldCenter = ray.GetPoint(enter);
                modifierBrushCursorValid = true;
            }
        }

        internal bool TryGetBrushAdjustmentCursor(out Vector3 center, out Vector3 normal, out float radius)
        {
            center = modifierBrushWorldCenter;
            normal = modifierBrushWorldNormal;
            radius = WorldBrushRadius * modifierBrushPoseRadiusScale;
            return modifierBrushCursorValid;
        }

        private void DrawBrushAdjustmentCursor()
        {
            if (!TryGetBrushAdjustmentCursor(out Vector3 center, out Vector3 normal, out float radius)) return;
            Color color = EffectivePaintErase ? new Color(1f, 0.2f, 0.2f, 0.9f) : new Color(0.2f, 0.9f, 1f, 0.9f);
            // This is the same outer-radius/falloff rendering used by the painting cursor, not a HUD.
            using (new Handles.DrawingScope(Matrix4x4.identity))
            {
                DrawBrushCursor(center, normal, radius, color);
                if (modifierBrushMirrorValid)
                {
                    color.a *= 0.72f;
                    DrawBrushCursor(modifierBrushMirrorCenter, modifierBrushMirrorNormal, radius, color);
                }
            }
        }

        private void CaptureUnityToolState()
        {
            if (!unityToolStateCaptured)
            {
                previousUnityTool = Tools.current;
                previousToolsHidden = Tools.hidden;
                unityToolStateCaptured = true;
            }
            Tools.current = Tool.None;
            Tools.hidden = true;
        }

        private void RestoreUnityToolState()
        {
            if (!unityToolStateCaptured) return;
            Tools.current = previousUnityTool;
            Tools.hidden = previousToolsHidden;
            unityToolStateCaptured = false;
        }

        private void BeginStroke(string undoName = null)
        {
            if (strokeActive) return;
            ResetMapPaintStroke();
            strokeActive = true;
            // Stale cards would obscure the moving guides, especially with depth testing enabled.
            // RebuildNow restores their visibility once the completed stroke has been evaluated.
            UpdateHairPreviewVisibility();
            curveStrokePlaneValid = false;
            hasPreviousStrokePosition = false;
            strokeSculptLayer = null;
            strokeLayerDeltas.Clear();
            Undo.IncrementCurrentGroup();
            strokeUndoGroup = Undo.GetCurrentGroup();
            string operationName = string.IsNullOrWhiteSpace(undoName) ? SceneToolUndoName() : undoName;
            Undo.SetCurrentGroupName(operationName);
            Undo.RegisterCompleteObjectUndo(groom, operationName);
        }

        private bool TryGetCurveBrushCenter(Ray ray, out Vector3 center, out Vector3 worldCenter,
            out float poseRadiusScale)
        {
            center = Vector3.zero;
            worldCenter = Vector3.zero;
            poseRadiusScale = 1f;
            if (curveBrushEntries.Count == 0) return false;
            Ray posedRay = WorldToPosedRay(ray);
            float bestNormalizedSquare = float.MaxValue;
            Vector3 bestPoint = Vector3.zero;
            CurveBrushEntry bestEntry = null;
            for (int entryIndex = 0; entryIndex < curveBrushEntries.Count; entryIndex++)
            {
                CurveBrushEntry entry = curveBrushEntries[entryIndex];
                float pickRadius = brushRadius * 1.5f * entry.PoseRadiusScale;
                if (!GuideInEditScope(entry.Guide)) continue;
                Bounds broadPhase = entry.PosedBounds;
                broadPhase.Expand(pickRadius * 2f);
                if (!broadPhase.IntersectRay(posedRay)) continue;
                for (int pointIndex = 1; pointIndex < entry.PosedPoints.Length; pointIndex++)
                {
                    if (!HairCurveBrushUtility.TryClosestPoint(posedRay,
                            entry.PosedPoints[pointIndex - 1], entry.PosedPoints[pointIndex],
                            out Vector3 point, out float square)) continue;
                    float normalizedSquare = square / Mathf.Max(1e-12f, pickRadius * pickRadius);
                    if (normalizedSquare >= bestNormalizedSquare) continue;
                    if (brushScope == HairBrushScope.VisibleHair && !IsPosedPointVisible(point)) continue;
                    bestNormalizedSquare = normalizedSquare;
                    bestPoint = point;
                    bestEntry = entry;
                }
            }
            if (bestEntry == null || bestNormalizedSquare > 1f) return false;
            center = bestPoint;
            worldCenter = SourceToStagePoint(bestPoint);
            poseRadiusScale = bestEntry.PoseRadiusScale;
            return true;
        }

        private static bool IsGroomTool(HairSceneTool tool)
        {
            return tool >= HairSceneTool.Comb && tool <= HairSceneTool.Freeze;
        }

        private void EndStroke()
        {
            ResetMapPaintStroke();
            sliceCutActive = false;
            slicePreview.Clear();
            pointHandleStrokeActive = false;
            if (!strokeActive) return;
            strokeActive = false;
            curveStrokePlaneValid = false;
            curveStrokePoseRadiusScale = 1f;
            hasPreviousStrokePosition = false;
            interactiveGuideRefreshQueued = false;
            strokeSculptLayer = null;
            strokeLayerDeltas.Clear();
            groom.EnsureIntegrity();
            EditorUtility.SetDirty(groom);
            if (strokeUndoGroup >= 0) Undo.CollapseUndoOperations(strokeUndoGroup);
            strokeUndoGroup = -1;
            lastWorkspaceInteraction = EditorApplication.timeSinceStartup;
            QueuePreviewChange(sceneTool == HairSceneTool.PaintGrowth ? HairPreviewChange.All : HairPreviewChange.Evaluation, true);
        }

        private void ApplySceneTool(HairSurfaceHit hit, Vector3 strokeDelta)
        {
            Vector3 localPoint = hit.SourcePoint;
            Vector3 localNormal = hit.SourceNormal;
            switch (sceneTool)
            {
                case HairSceneTool.PaintGrowth:
                    PaintMapAt(localPoint);
                    break;
                case HairSceneTool.PlaceGuide:
                    if (!hasPreviousStrokePosition) PlaceGuideAt(hit.TriangleIndex, localPoint, localNormal, false);
                    break;
                case HairSceneTool.DrawGuide:
                    if (!hasPreviousStrokePosition) PlaceGuideAt(hit.TriangleIndex, localPoint, localNormal, true);
                    else ExtendDrawGuide(localPoint, localNormal);
                    break;
                default:
                    SculptAt(localPoint, localNormal, strokeDelta);
                    break;
            }
            previousStrokePosition = localPoint;
            hasPreviousStrokePosition = true;
            EditorUtility.SetDirty(groom);
            if (sceneTool != HairSceneTool.PaintGrowth) QueueInteractiveGuideRefresh();
        }

        private void PaintMapAt(Vector3 localCenter)
        {
            HairGrowthMap map = ActiveMap;
            if (map == null || map.locked || groom.SourceMesh == null) return;
            Vector3[] vertices = groom.SourceMesh.vertices;
            float target = EffectivePaintErase ? map.valueRange.x : Mathf.Clamp(paintValue, map.valueRange.x, map.valueRange.y);
            if (!strokeActive || mapStrokeMap != map || !ReferenceEquals(mapStrokeValues, map.values) || mapStrokeTarget != target)
            {
                ResetMapPaintStroke();
                mapStrokeMap = map;
                mapStrokeValues = map.values;
                mapStrokeTarget = target;
            }
            vertexSpatialIndex.QuerySphere(localCenter, brushRadius, brushVertices);
            combinedBrushVertices.Clear();
            for (int candidate = 0; candidate < brushVertices.Count; candidate++)
                combinedBrushVertices.Add(brushVertices[candidate]);
            if (mirrorPaintX)
            {
                Vector3 mirroredCenter = HairBrushInteractionUtility.MirrorX(localCenter);
                vertexSpatialIndex.QuerySphere(mirroredCenter, brushRadius, mirroredBrushVertices);
                for (int candidate = 0; candidate < mirroredBrushVertices.Count; candidate++)
                    combinedBrushVertices.Add(mirroredBrushVertices[candidate]);
            }
            bool overlayChanged = false;
            foreach (int i in combinedBrushVertices)
            {
                if ((uint)i >= (uint)map.values.Length || !IsSourceVertexVisible(i)) continue;
                float falloff = HairBrushInteractionUtility.EvaluateMirroredFalloff(
                    vertices[i], localCenter, brushRadius, brushHardness, mirrorPaintX);
                float influence = Mathf.Clamp01(falloff * brushStrength);
                if (influence <= 0f) continue;
                if (!mapStrokeSamples.TryGetValue(i, out MapStrokeSample sample))
                    sample = new MapStrokeSample { initialValue = map.values[i] };
                if (influence <= sample.maximumInfluence) continue;
                sample.maximumInfluence = influence;
                mapStrokeSamples[i] = sample;
                // Blend from this stroke's original value, not its previous dab. Otherwise mouse
                // event frequency repeatedly accumulates even tiny edge weights toward full paint.
                float value = Mathf.Lerp(sample.initialValue, target, influence);
                if (map.values[i] == value) continue;
                map.values[i] = value;
                if ((uint)i < (uint)growthOverlayColors.Length)
                {
                    growthOverlayColors[i] = GrowthOverlayColor(map, i);
                    overlayChanged = true;
                }
            }
            if (overlayChanged && growthOverlayMesh != null)
                growthOverlayMesh.colors32 = growthOverlayColors;
        }

        private void ResetMapPaintStroke()
        {
            mapStrokeSamples.Clear();
            mapStrokeMap = null;
            mapStrokeValues = null;
        }

        public void ApplySelectionToActiveMap(float value)
        {
            HairGrowthMap map = ActiveMap;
            if (map == null || map.locked || selectedVertices == null) return;
            Undo.RecordObject(groom, "Apply Hair Vertex Selection");
            float target = Mathf.Clamp(value, map.valueRange.x, map.valueRange.y);
            for (int i = 0; i < selectedVertices.Count; i++)
            {
                int vertex = selectedVertices[i];
                if ((uint)vertex < (uint)map.values.Length && IsSourceVertexVisible(vertex))
                    map.values[vertex] = target;
            }
            HairGroomCommands.Commit(groom);
        }

        public void SelectFromActiveMap(float threshold = 0.5f)
        {
            HairGrowthMap map = ActiveMap;
            if (map == null) return;
            selectedVertices.Clear();
            for (int i = 0; i < map.values.Length; i++)
                if (map.values[i] >= threshold && IsSourceVertexVisible(i)) selectedVertices.Add(i);
            RepaintAll();
        }

        public void InvertVertexSelection()
        {
            HashSet<int> current = new HashSet<int>(selectedVertices);
            selectedVertices.Clear();
            for (int i = 0; i < groom.SourceVertexCount; i++)
                if (IsSourceVertexVisible(i) && !current.Contains(i)) selectedVertices.Add(i);
            RepaintAll();
        }

        public void ClearVertexSelection()
        {
            selectedVertices.Clear();
            RepaintAll();
        }

        public void GrowVertexSelection(bool shrink)
        {
            Mesh mesh = groom.SourceMesh;
            if (mesh == null || selectedVertices.Count == 0) return;
            HashSet<int> selected = new HashSet<int>(selectedVertices);
            HashSet<int> boundary = new HashSet<int>();
            int[] triangles = mesh.triangles;
            for (int index = 0; index + 2 < triangles.Length; index += 3)
            {
                int a = triangles[index];
                int b = triangles[index + 1];
                int c = triangles[index + 2];
                int selectedCount = (selected.Contains(a) ? 1 : 0) + (selected.Contains(b) ? 1 : 0) +
                                    (selected.Contains(c) ? 1 : 0);
                if (!shrink && selectedCount > 0)
                {
                    boundary.Add(a); boundary.Add(b); boundary.Add(c);
                }
                else if (shrink && selectedCount > 0 && selectedCount < 3)
                {
                    if (selected.Contains(a)) boundary.Add(a);
                    if (selected.Contains(b)) boundary.Add(b);
                    if (selected.Contains(c)) boundary.Add(c);
                }
            }
            if (shrink) selected.ExceptWith(boundary);
            else selected.UnionWith(boundary);
            selected.RemoveWhere(vertex => !IsSourceVertexVisible(vertex));
            selectedVertices.Clear();
            selectedVertices.AddRange(selected);
            RepaintAll();
        }

        private void SelectTriangle(int combinedTriangle, bool add, bool subtract)
        {
            if (!TryResolveTriangle(combinedTriangle, out _, out _, out int a, out int b, out int c)) return;
            if (!add && !subtract) selectedVertices.Clear();
            if (subtract)
            {
                selectedVertices.Remove(a);
                selectedVertices.Remove(b);
                selectedVertices.Remove(c);
            }
            else
            {
                AddSelectedVertex(a);
                AddSelectedVertex(b);
                AddSelectedVertex(c);
            }
            RepaintAll();
        }

        private void AddSelectedVertex(int vertex)
        {
            if (!selectedVertices.Contains(vertex)) selectedVertices.Add(vertex);
        }

        private void DrawVertexSelection()
        {
            if (selectedVertices == null || selectedVertices.Count == 0 || groom.SourceMesh == null) return;
            Vector3[] vertices = groom.SourceMesh.vertices;
            Handles.color = Color.yellow;
            int stride = Mathf.Max(1, selectedVertices.Count / 4000);
            for (int i = 0; i < selectedVertices.Count; i += stride)
            {
                int vertex = selectedVertices[i];
                if ((uint)vertex >= (uint)vertices.Length || !IsSourceVertexVisible(vertex)) continue;
                Vector3 displayVertex = authoringPose?.PosedVertex(vertex) ?? vertices[vertex];
                float size = LocalHandleSize(displayVertex) * 0.012f;
                Handles.DotHandleCap(0, displayVertex, Quaternion.identity, size, EventType.Repaint);
            }
        }

        private void PlaceGuideAt(int visibleTriangle, Vector3 localPoint, Vector3 localNormal, bool beginDraw)
        {
            HairGroup group = ActiveGroup;
            if (group == null || group.locked) return;
            if (!TryResolveTriangle(visibleTriangle, out int submesh, out int triangle, out int a, out int b, out int c))
                return;
            Vector3[] vertices = groom.SourceMesh.vertices;
            Vector3 barycentric = HairMeshUtility.Barycentric(localPoint, vertices[a], vertices[b], vertices[c]);
            HairSurfaceAnchor anchor = HairSurfaceAnchor.Create(groom.SourceMeshId, submesh, triangle,
                barycentric, 0f, localPoint, localNormal);
            HairGuide guide = new HairGuide
            {
                name = $"Guide {group.guides.Count + 1:000}",
                root = anchor,
                seed = group.children.seed + group.guides.Count * 3571
            };
            float width = group.profile != null ? group.profile.DefaultWidth : 0.012f;
            int points = beginDraw ? 2 : 6;
            for (int i = 0; i < points; i++)
            {
                float t = i / (points - 1f);
                guide.points.Add(new HairGuidePoint
                {
                    position = localPoint + localNormal * ((beginDraw ? 0.005f : 0.18f) * t),
                    width = width * (1f - t),
                    widthBaseline = width * (1f - t),
                    stiffness = 1f - t
                });
            }
            guide.EnsureIntegrity(width);
            group.guides.Add(guide);
            activeGuideId = guide.Id;
            activeGuidePoint = guide.points.Count - 1;
            actionStatus = $"Placed {guide.name}. Select/Edit it or add more guides.";
        }

        private void ExtendDrawGuide(Vector3 localPoint, Vector3 localNormal)
        {
            HairGuide guide = groom.FindGuide(activeGuideId, out _);
            if (guide?.points == null || guide.points.Count < 2) return;
            Vector3 position = localPoint + localNormal * 0.005f;
            HairGuidePoint last = guide.points[guide.points.Count - 1];
            float spacing = Mathf.Max(0.002f, brushRadius * 0.15f);
            if (Vector3.Distance(last.position, position) < spacing)
            {
                last.position = position;
                return;
            }
            float rootWidth = guide.points[0].width;
            guide.points.Add(new HairGuidePoint { position = position, width = rootWidth * 0.5f });
            for (int i = 0; i < guide.points.Count; i++)
            {
                float t = i / (guide.points.Count - 1f);
                guide.points[i].width = rootWidth * (1f - t);
                guide.points[i].widthBaseline = guide.points[i].width;
                guide.points[i].stiffness = 1f - t;
            }
            activeGuidePoint = guide.points.Count - 1;
        }

        private void ApplyGravityStep(float deltaTime)
        {
            HairGroup group = ActiveGroup;
            if (!gravitySimulationActive || group == null || group.locked ||
                group.guides == null || curveBrushEntries.Count == 0) return;

            HairSculptLayer layer = ResolveSculptLayer(group);
            if (gravityCollision) EnsureGravitySurface();
            Vector3 gravity = Physics.gravity;
            if (!float.IsFinite(gravity.sqrMagnitude) || gravity.sqrMagnitude <= 1e-12f) return;
            for (int entryIndex = 0; entryIndex < curveBrushEntries.Count; entryIndex++)
            {
                CurveBrushEntry entry = curveBrushEntries[entryIndex];
                HairGuide guide = entry.Guide;
                if (guide == null || !guide.enabled || guide.points == null || guide.points.Count < 2 || !GuideInEditScope(guide))
                    continue;

                HairGuideDelta delta = null;
                if (strokeLayerDeltas.TryGetValue(guide.Id, out delta))
                    ResizeDelta(delta, guide.points.Count);
                EnsurePointBuffers(entry, guide.points.Count);
                bool directDisplayMapping = entry.SourcePoints.Length == guide.points.Count;
                for (int pointIndex = 0; pointIndex < guide.points.Count; pointIndex++)
                {
                    Vector3 sourcePoint = directDisplayMapping
                        ? entry.SourcePoints[pointIndex]
                        : guide.points[pointIndex].position +
                          (delta != null ? delta.positionOffsets[pointIndex] : Vector3.zero);
                    entry.OriginalPoints[pointIndex] = sourcePoint;
                    entry.TargetPoints[pointIndex] = sourcePoint;
                }

                Matrix4x4 toWorld = SourceToStageMatrix * entry.GuideToPose;
                if (!float.IsFinite(toWorld.determinant) || Mathf.Abs(toWorld.determinant) < 1e-12f) continue;
                Matrix4x4 fromWorld = toWorld.inverse;
                Vector3 posedRootNormal = fromWorld.transpose.MultiplyVector(guide.root.CachedLocalNormal).normalized;
                Vector3 targetDirection = HairGuideShapeUtility.StableGravityDirection(
                    gravity, posedRootNormal, guide.seed, gravitySeparation);
                bool changed = false;
                for (int pointIndex = 1; pointIndex < guide.points.Count; pointIndex++)
                {
                    HairGuidePoint point = guide.points[pointIndex];
                    if (point.freeze >= 0.999f || (brushScope == HairBrushScope.VisibleHair &&
                        !IsPosedPointVisible(entry.GuideToPose.MultiplyPoint3x4(entry.OriginalPoints[pointIndex]))))
                    {
                        entry.TargetPoints[pointIndex] = entry.OriginalPoints[pointIndex];
                        continue;
                    }

                    Vector3 sourceSegment = entry.OriginalPoints[pointIndex] -
                                            entry.OriginalPoints[pointIndex - 1];
                    float segmentLength = sourceSegment.magnitude;
                    if (segmentLength <= 1e-8f) continue;
                    float t = pointIndex / (guide.points.Count - 1f);
                    float settle = HairGuideShapeUtility.GravityResponse(t, brushRootInfluence,
                        point.stiffness, point.freeze, gravityStrength, deltaTime);
                    Vector3 settledSourceDirection = HairGuideShapeUtility.SettleSegment(sourceSegment,
                        targetDirection, settle, toWorld, fromWorld);
                    if (settledSourceDirection.sqrMagnitude <= 1e-12f) continue;
                    entry.TargetPoints[pointIndex] = entry.TargetPoints[pointIndex - 1] +
                        settledSourceDirection;
                    changed |= settle > 1e-7f;
                }

                if (!changed) continue;
                HairGuideShapeUtility.PreserveSegmentLengths(entry.OriginalPoints, entry.TargetPoints, guide.points);
                if (gravityCollision) ConstrainGravityToSurface(entry);
                ApplyGuideTarget(entry, guide, layer, ref delta);
            }
        }

        private void EnsureGravitySurface()
        {
            Mesh surface = authoringSurfaceMesh != null ? authoringSurfaceMesh : groom?.SourceMesh;
            if (gravitySurface == surface && gravityRaycaster != null) return;
            gravitySurface = surface;
            gravityRaycaster = surface != null && surface.isReadable ? new HairMeshRaycaster(surface) : null;
        }

        private void ConstrainGravityToSurface(CurveBrushEntry entry)
            => HairGuideShapeUtility.ConstrainToSurface(entry.OriginalPoints, entry.TargetPoints,
                entry.Guide.points, gravityRaycaster, gravityClearance, entry.GuideToPose, entry.PoseToGuide);

        internal void HighlightUvSet(HairAtlasProfileAsset atlas, string id)
        {
            if (highlightedAtlas == atlas && highlightedUvId == id) return;
            highlightedAtlas = atlas; highlightedUvId = id;
            RefreshCardHighlight();
            RepaintAll();
        }

        private void RefreshCardHighlight()
        {
            using var highlightScope = HighlightMarker.Auto();
            if (!showCardWireframe || meshBuild?.mesh == null || highlightedAtlas == null || string.IsNullOrEmpty(highlightedUvId))
            { cardHighlightLines = Array.Empty<Vector3>(); return; }
            Mesh mesh = meshBuild.mesh;
            mesh.GetVertices(highlightVertices);
            while (highlightSubmeshes.Count < mesh.subMeshCount) highlightSubmeshes.Add(new List<int>());
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                mesh.GetTriangles(highlightSubmeshes[submesh], submesh);
            nextHighlightTriangles.Clear();
            foreach (HairCardSpan span in meshBuild.cards)
            {
                if (span.curve.atlas != highlightedAtlas || span.uvSetId != highlightedUvId) continue;
                List<int> indices = highlightSubmeshes[span.submesh];
                for (int i = span.triangleStart * 3; i < (span.triangleStart + span.triangleCount) * 3; i++)
                    nextHighlightTriangles.Add(indices[i]);
            }
            bool sameTopology = nextHighlightTriangles.Count == highlightTriangles.Count;
            for (int i = 0; sameTopology && i < nextHighlightTriangles.Count; i++)
                sameTopology = nextHighlightTriangles[i] == highlightTriangles[i];
            if (!sameTopology)
            {
                highlightEdges.Clear(); highlightEdgeIndices.Clear();
                for (int i = 0; i < nextHighlightTriangles.Count; i += 3)
                    for (int edge = 0; edge < 3; edge++)
                    {
                        int a = nextHighlightTriangles[i + edge], b = nextHighlightTriangles[i + (edge + 1) % 3];
                        ulong key = ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                        if (highlightEdges.Add(key)) { highlightEdgeIndices.Add(a); highlightEdgeIndices.Add(b); }
                    }
            }
            (highlightTriangles, nextHighlightTriangles) = (nextHighlightTriangles, highlightTriangles);
            if (cardHighlightLines.Length != highlightEdgeIndices.Count) cardHighlightLines = new Vector3[highlightEdgeIndices.Count];
            for (int i = 0; i < highlightEdgeIndices.Count; i++) cardHighlightLines[i] = highlightVertices[highlightEdgeIndices[i]];
        }

        internal HairCardSpan CardAtTriangle(int triangle)
        {
            if (meshBuild?.mesh == null || triangle < 0) return null;
            for (int submesh = 0; submesh < meshBuild.mesh.subMeshCount; submesh++)
            {
                int count = (int)meshBuild.mesh.GetIndexCount(submesh) / 3;
                if (triangle < count)
                {
                    if (submesh < meshBuild.secondPasses.Count && meshBuild.secondPasses[submesh])
                    {
                        HairAtlasProfileAsset atlas = meshBuild.atlases[submesh];
                        submesh = meshBuild.atlases.IndexOf(atlas);
                    }
                    return meshBuild.cards.Find(span => span.submesh == submesh && triangle >= span.triangleStart && triangle < span.triangleStart + span.triangleCount);
                }
                triangle -= count;
            }
            return null;
        }

        private bool PickCard(Ray ray)
        {
            if (meshBuild?.mesh == null) return false;
            cardRaycaster ??= new HairMeshRaycaster(meshBuild.mesh);
            if (!cardRaycaster.Raycast(WorldToPosedRay(ray), out HairMeshRaycastHit hit)) return false;
            if (surfaceRaycaster != null && surfaceRaycaster.Raycast(WorldToPosedRay(ray), out HairMeshRaycastHit surface) && surface.Distance < hit.Distance - 0.0005f) return false;
            HairCardSpan card = CardAtTriangle(hit.TriangleIndex);
            if (card == null) return false;
            if (card.curve.groupId != activeGroupId) SetActiveGroup(card.curve.groupId);
            HighlightUvSet(card.curve.atlas, card.uvSetId);
            HairGroomWorkspace.SelectUvSet(card.curve.atlas, card.uvSetId);
            HairAtlasRegion region = card.curve.atlas?.regions.Find(item => item != null && item.Id == card.uvSetId);
            actionStatus = $"Card: {ActiveGroup?.name} / {region?.name ?? "No UV set"}";
            RepaintAll();
            return true;
        }

        private void SculptAt(Vector3 center, Vector3 surfaceNormal, Vector3 strokeDelta)
        {
            HairGroup group = ActiveGroup;
            if (group == null || group.locked || group.guides == null) return;
            HairSculptLayer layer = null;
            Vector3 clumpAverage = sceneTool == HairSceneTool.Clump
                ? CalculateNearbyAverage(center, surfaceNormal)
                : center;

            for (int entryIndex = 0; entryIndex < curveBrushEntries.Count; entryIndex++)
            {
                CurveBrushEntry entry = curveBrushEntries[entryIndex];
                HairGuide guide = entry.Guide;
                if (guide == null || !guide.enabled || guide.points == null || guide.points.Count < 2 || !GuideInEditScope(guide))
                    continue;

                float posedRadius = brushRadius * entry.PoseRadiusScale;
                if (!BrushBoundsOverlap(entry.PosedBounds, center, posedRadius, surfaceNormal)) continue;

                EnsurePointBuffers(entry, guide.points.Count);
                if (!FillBrushInfluences(entry, center, surfaceNormal)) continue;

                if (sceneTool != HairSceneTool.Freeze)
                    layer ??= ResolveSculptLayer(group);
                HairGuideDelta delta = null;
                if (layer != null && strokeLayerDeltas.TryGetValue(guide.Id, out delta))
                    ResizeDelta(delta, guide.points.Count);
                for (int pointIndex = 0; pointIndex < guide.points.Count; pointIndex++)
                {
                    float normalizedPosition = pointIndex / (guide.points.Count - 1f);
                    Vector3 sourcePoint = HairCurveBrushUtility.SamplePolyline(
                        entry.SourcePoints, normalizedPosition);
                    entry.OriginalPoints[pointIndex] = sourcePoint;
                    entry.TargetPoints[pointIndex] = sourcePoint;
                }

                bool positionChanged = false;
                bool intentionallyChangesLength = false;
                for (int pointIndex = 1; pointIndex < guide.points.Count; pointIndex++)
                {
                    HairGuidePoint point = guide.points[pointIndex];
                    Vector3 actual = entry.OriginalPoints[pointIndex];
                    Vector3 posedActual = HairCurveBrushUtility.SamplePolyline(entry.PosedPoints,
                        pointIndex / (guide.points.Count - 1f));
                    if (sceneTool == HairSceneTool.Freeze)
                    {
                        // Unfreeze must reach fully frozen points; do not attenuate the mask brush by its own mask.
                        point.freeze = Mathf.Clamp01(point.freeze + (paintErase ? -1f : 1f) *
                            entry.PointInfluences[pointIndex] * brushStrength);
                        continue;
                    }
                    if (point.freeze >= 0.999f) continue;
                    float falloff = entry.PointInfluences[pointIndex] * brushStrength *
                                    (1f - point.freeze);
                    if (falloff <= 0f) continue;

                    Vector3 sourceDisplacement = Vector3.zero;
                    switch (sceneTool)
                    {
                        case HairSceneTool.Comb:
                            sourceDisplacement = entry.PoseToGuide.MultiplyVector(strokeDelta *
                                (falloff * (pointIndex / (guide.points.Count - 1f))));
                            break;
                        case HairSceneTool.Grab:
                            sourceDisplacement = entry.PoseToGuide.MultiplyVector(strokeDelta * falloff);
                            break;
                        case HairSceneTool.Smooth:
                        {
                            Vector3 neighborAverage = pointIndex < guide.points.Count - 1
                                ? (entry.OriginalPoints[pointIndex - 1] +
                                   entry.OriginalPoints[pointIndex + 1]) * 0.5f
                                : entry.OriginalPoints[pointIndex - 1] +
                                  (actual - entry.OriginalPoints[pointIndex - 1]).normalized *
                                  Vector3.Distance(actual, entry.OriginalPoints[pointIndex - 1]);
                            sourceDisplacement = (neighborAverage - actual) * falloff;
                            break;
                        }
                        case HairSceneTool.Clump:
                            sourceDisplacement = entry.PoseToGuide.MultiplyVector(
                                (clumpAverage - posedActual) * falloff);
                            break;
                        case HairSceneTool.Part:
                            sourceDisplacement = entry.PoseToGuide.MultiplyVector(
                                Vector3.ProjectOnPlane(posedActual - center, surfaceNormal).normalized *
                                (posedRadius * 0.08f * falloff));
                            break;
                        case HairSceneTool.Width:
                            delta ??= ResolveDelta(layer, guide);
                            float brushWidth = HairCardMeshGenerator.ResolveCardWidth(new HairCurvePoint(
                                point.position, point.width, point.roll, guide.GetWidthBaseline(pointIndex), point.profileScale),
                                group.profile, pointIndex / (guide.points.Count - 1f));
                            delta.widthOffsets[pointIndex] +=
                                (paintErase ? -1f : 1f) * Mathf.Max(0.0001f, brushWidth) * 0.15f * falloff /
                                Mathf.Max(0.0001f, CurrentSculptLayerGain(group, layer, guide.Id));
                            continue;
                        case HairSceneTool.Length:
                        {
                            float scale = 1f + (paintErase ? -0.04f : 0.04f) * falloff;
                            Vector3 root = entry.OriginalPoints[0];
                            for (int targetIndex = 1; targetIndex < guide.points.Count; targetIndex++)
                            {
                                float mobility = guide.points[targetIndex].freeze >= 0.999f ? 0f :
                                    1f - guide.points[targetIndex].freeze;
                                entry.TargetPoints[targetIndex] = Vector3.Lerp(entry.OriginalPoints[targetIndex],
                                    root + (entry.OriginalPoints[targetIndex] - root) * Mathf.Max(0.01f, scale), mobility);
                            }
                            intentionallyChangesLength = true;
                            positionChanged = true;
                            pointIndex = guide.points.Count;
                            continue;
                        }
                    }
                    sourceDisplacement *= HairGuideShapeUtility.RootInfluence(
                        pointIndex / (guide.points.Count - 1f), brushRootInfluence);
                    if (sourceDisplacement.sqrMagnitude <= 1e-16f) continue;
                    entry.TargetPoints[pointIndex] += sourceDisplacement;
                    positionChanged = true;
                }

                if (!positionChanged) continue;
                if (!intentionallyChangesLength)
                    HairGuideShapeUtility.PreserveSegmentLengths(entry.OriginalPoints, entry.TargetPoints, guide.points);
                ApplyGuideTarget(entry, guide, layer, ref delta);
            }
        }

        private static bool HasEditablePointInfluence(IReadOnlyList<float> influences)
        {
            if (influences == null) return false;
            for (int pointIndex = 1; pointIndex < influences.Count; pointIndex++)
                if (influences[pointIndex] > 0f) return true;
            return false;
        }

        private bool GuideInEditScope(HairGuide guide) => guide != null &&
            ((!isolateSelectedGuides && brushScope != HairBrushScope.SelectedGuidesOnly) || IsGuideSelected(guide.Id));

        private bool IsPosedPointVisible(Vector3 point)
        {
            if (brushCamera == null) return true;
            Vector3 worldPoint = SourceToStagePoint(point);
            Vector3 viewport = brushCamera.WorldToViewportPoint(worldPoint);
            if (viewport.z <= 0f) return false;
            Ray ray = WorldToPosedRay(brushCamera.ViewportPointToRay(viewport));
            float distance = Vector3.Dot(point - ray.origin, ray.direction);
            return surfaceRaycaster == null || !surfaceRaycaster.Raycast(ray, out HairMeshRaycastHit hit) ||
                hit.Distance >= distance - 0.0005f;
        }

        private bool FillBrushInfluences(CurveBrushEntry entry, Vector3 center, Vector3 normal)
        {
            EnsurePointBuffers(entry, entry.Guide.points.Count);
            if (!GuideInEditScope(entry.Guide) || !HairCurveBrushUtility.FillControlPointInfluences(
                entry.PosedPoints, center, brushRadius * entry.PoseRadiusScale, brushHardness, normal,
                affectThroughDepth, entry.PointInfluences)) return false;
            for (int i = 1; i < entry.PointInfluences.Length; i++)
            {
                if ((sceneTool != HairSceneTool.Freeze && entry.Guide.points[i].freeze >= 0.999f) ||
                    (brushScope == HairBrushScope.VisibleHair && entry.PointInfluences[i] > 0f &&
                     !IsPosedPointVisible(HairCurveBrushUtility.SamplePolyline(entry.PosedPoints, i / (entry.PointInfluences.Length - 1f)))))
                    entry.PointInfluences[i] = 0f;
            }
            return brushStrength > 0f && HasEditablePointInfluence(entry.PointInfluences);
        }

        private void UpdateBrushHighlight(SceneView sceneView)
        {
            brushCamera = sceneView.camera;
            if (Event.current.type != EventType.Repaint) return;
            int oldCount = brushHighlights.Count;
            brushHighlights.Clear();
            if (workflowStep == HairWorkflowStep.Groom && IsGroomTool(sceneTool) && sceneTool != HairSceneTool.Cut &&
                !gravitySimulationActive && ActiveGroup is { locked: false, visible: true, enabled: true })
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
                bool found = TryGetCurveBrushCenter(ray, out Vector3 center, out _, out _);
                if (strokeActive && curveStrokePlaneValid && curveStrokePlane.Raycast(WorldToPosedRay(ray), out float enter))
                { center = WorldToPosedRay(ray).GetPoint(enter); found = true; }
                if (found)
                {
                    Vector3 normal = StageToSourceDirection(-sceneView.camera.transform.forward).normalized;
                    foreach (CurveBrushEntry entry in curveBrushEntries)
                        if (BrushBoundsOverlap(entry.PosedBounds, center, brushRadius * entry.PoseRadiusScale, normal) && FillBrushInfluences(entry, center, normal))
                        {
                            float maximum = 0f;
                            for (int i = 1; i < entry.PointInfluences.Length; i++)
                                maximum = Mathf.Max(maximum, entry.PointInfluences[i] * brushStrength *
                                    (sceneTool == HairSceneTool.Freeze ? 1f : 1f - entry.Guide.points[i].freeze));
                            brushHighlights[entry.Guide.Id] = maximum;
                        }
                }
            }
            if (oldCount != brushHighlights.Count) HairGroomWorkspace.RepaintOpenWindows();
        }

        private bool BrushBoundsOverlap(Bounds bounds, Vector3 center, float radius,
            Vector3 projectionNormal)
        {
            if (!affectThroughDepth || projectionNormal.sqrMagnitude <= 1e-12f)
                return bounds.SqrDistance(center) <= radius * radius;
            Vector3 projectedOffset = Vector3.ProjectOnPlane(
                bounds.center - center, projectionNormal.normalized);
            float conservativeProjectedRadius = bounds.extents.magnitude;
            float maximumDistance = radius + conservativeProjectedRadius;
            return projectedOffset.sqrMagnitude <= maximumDistance * maximumDistance;
        }

        private static void EnsurePointBuffers(CurveBrushEntry entry, int pointCount)
        {
            if (entry.ReferencePoints.Length != pointCount)
                entry.ReferencePoints = new Vector3[pointCount];
            if (entry.OriginalPoints.Length != pointCount)
                entry.OriginalPoints = new Vector3[pointCount];
            if (entry.TargetPoints.Length != pointCount)
                entry.TargetPoints = new Vector3[pointCount];
            if (entry.PointInfluences.Length != pointCount)
                entry.PointInfluences = new float[pointCount];
        }

        private void ApplyGuideTarget(CurveBrushEntry entry, HairGuide guide, HairSculptLayer layer,
            ref HairGuideDelta delta)
        {
            delta ??= ResolveDelta(layer, guide);
            float layerGain = 1f / Mathf.Max(0.0001f, CurrentSculptLayerGain(ActiveGroup, layer, guide.Id));
            for (int pointIndex = 1; pointIndex < guide.points.Count; pointIndex++)
                delta.positionOffsets[pointIndex] +=
                    (entry.TargetPoints[pointIndex] - entry.OriginalPoints[pointIndex]) * layerGain;

            if (entry.SourcePoints.Length == guide.points.Count)
            {
                for (int pointIndex = 1; pointIndex < entry.SourcePoints.Length; pointIndex++)
                    entry.SourcePoints[pointIndex] = entry.TargetPoints[pointIndex];
            }
            else
            {
                for (int sampleIndex = 1; sampleIndex < entry.SourcePoints.Length; sampleIndex++)
                {
                    float t = sampleIndex / (entry.SourcePoints.Length - 1f);
                    float control = t * (guide.points.Count - 1f);
                    int left = Mathf.Min(Mathf.FloorToInt(control), guide.points.Count - 1);
                    int right = Mathf.Min(left + 1, guide.points.Count - 1);
                    float blend = control - left;
                    Vector3 leftShift = entry.TargetPoints[left] - entry.OriginalPoints[left];
                    Vector3 rightShift = entry.TargetPoints[right] - entry.OriginalPoints[right];
                    entry.SourcePoints[sampleIndex] += Vector3.LerpUnclamped(leftShift, rightShift, blend);
                }
            }

            if (displayGuidePolylines.TryGetValue(guide.Id, out Vector3[] polyline) &&
                polyline.Length == entry.SourcePoints.Length)
                Array.Copy(entry.SourcePoints, polyline, polyline.Length);
            RefreshCurveBrushEntry(entry);
        }

        private HairSculptLayer ResolveSculptLayer(HairGroup group)
        {
            HairSculptLayer layer = group.sculptLayers.Find(candidate => candidate != null && candidate.Id == activeLayerId);
            if (layer == null || layer.locked || (!layer.visible && layer.Id != soloLayerId) || CurrentSculptLayerGain(group, layer) <= 0.0001f)
            {
                layer = new HairSculptLayer { name = $"Sculpt Layer {group.sculptLayers.Count + 1}" };
                layer.EnsureIntegrity();
                group.sculptLayers.Add(layer);
                activeLayerId = layer.Id;
                activeModifierId = null;
                if (!string.IsNullOrEmpty(soloLayerId)) soloLayerId = layer.Id;
                actionStatus = $"Created {layer.name} as the visible destination for grooming strokes.";
            }
            PrepareStrokeDeltaLookup(layer);
            return layer;
        }

        private float CurrentSculptLayerGain(HairGroup group, HairSculptLayer layer, string guideId = null) =>
            !string.IsNullOrEmpty(soloLayerId) && layer?.Id == soloLayerId ? layer.opacity : SculptLayerGain(group, layer, guideId);

        private static float SculptLayerGain(HairGroup group, HairSculptLayer layer, string guideId = null)
        {
            if (group == null || layer == null) return 0f;
            float gain = layer.opacity;
            int index = group.sculptLayers.IndexOf(layer);
            for (int i = index + 1; i < group.sculptLayers.Count; i++)
            {
                HairSculptLayer above = group.sculptLayers[i];
                if (above != null && above.visible && above.blendMode == HairSculptBlendMode.Override &&
                    above.deltas != null && (guideId == null ? above.deltas.Count > 0 :
                        above.deltas.Exists(delta => delta != null && delta.guideId == guideId)))
                    gain *= 1f - above.opacity;
            }
            return gain;
        }

        private void PrepareStrokeDeltaLookup(HairSculptLayer layer)
        {
            if (ReferenceEquals(strokeSculptLayer, layer)) return;
            strokeSculptLayer = layer;
            strokeLayerDeltas.Clear();
            if (layer?.deltas == null) return;
            for (int index = 0; index < layer.deltas.Count; index++)
            {
                HairGuideDelta delta = layer.deltas[index];
                if (delta == null || string.IsNullOrEmpty(delta.guideId) ||
                    strokeLayerDeltas.ContainsKey(delta.guideId)) continue;
                strokeLayerDeltas.Add(delta.guideId, delta);
            }
        }

        private HairGuideDelta ResolveDelta(HairSculptLayer layer, HairGuide guide)
        {
            PrepareStrokeDeltaLookup(layer);
            if (!strokeLayerDeltas.TryGetValue(guide.Id, out HairGuideDelta delta))
            {
                delta = new HairGuideDelta { guideId = guide.Id };
                layer.deltas.Add(delta);
                strokeLayerDeltas.Add(guide.Id, delta);
            }
            ResizeDelta(delta, guide.points.Count);
            return delta;
        }

        private static void ResizeDelta(HairGuideDelta delta, int count)
        {
            if (delta == null) return;
            if (delta.positionOffsets == null || delta.positionOffsets.Length != count)
                Array.Resize(ref delta.positionOffsets, count);
            if (delta.widthOffsets == null || delta.widthOffsets.Length != count)
                Array.Resize(ref delta.widthOffsets, count);
            if (delta.rollOffsets == null || delta.rollOffsets.Length != count)
                Array.Resize(ref delta.rollOffsets, count);
        }

        private Vector3 CalculateNearbyAverage(Vector3 center, Vector3 projectionNormal)
        {
            Vector3 total = Vector3.zero;
            int count = 0;
            for (int entryIndex = 0; entryIndex < curveBrushEntries.Count; entryIndex++)
            {
                CurveBrushEntry entry = curveBrushEntries[entryIndex];
                float radius = brushRadius * entry.PoseRadiusScale;
                if (!FillBrushInfluences(entry, center, projectionNormal)) continue;
                float squareRadius = radius * radius;
                if (!BrushBoundsOverlap(entry.PosedBounds, center, radius, projectionNormal)) continue;
                Vector3 nearest = Vector3.zero;
                float nearestSquare = float.MaxValue;
                for (int pointIndex = 1; pointIndex < entry.PosedPoints.Length; pointIndex++)
                {
                    Vector3 position = HairCurveBrushUtility.ClosestPointForBrush(center,
                        entry.PosedPoints[pointIndex - 1], entry.PosedPoints[pointIndex],
                        projectionNormal, affectThroughDepth, out _, out float squareDistance);
                    if (squareDistance >= nearestSquare) continue;
                    nearest = position;
                    nearestSquare = squareDistance;
                }
                if (nearestSquare > squareRadius) continue;
                total += nearest;
                count++;
            }
            return count > 0 ? total / count : center;
        }

        private bool TryResolveTriangle(int combinedTriangle, out int submesh, out int triangle,
            out int a, out int b, out int c)
        {
            submesh = triangle = a = b = c = -1;
            if (sourceVisibility != null)
            {
                if (sourceVisibility.TryResolveVisibleTriangle(combinedTriangle,
                        out HairSourceVisibility.TriangleReference visible))
                {
                    submesh = visible.Submesh;
                    triangle = visible.Triangle;
                    a = visible.A;
                    b = visible.B;
                    c = visible.C;
                    return true;
                }
                return false;
            }
            int offset = combinedTriangle;
            for (int candidate = 0; candidate < groom.SourceMesh.subMeshCount; candidate++)
            {
                int[] triangles = groom.SourceMesh.GetTriangles(candidate, true);
                int count = triangles.Length / 3;
                if (offset >= count)
                {
                    offset -= count;
                    continue;
                }
                int index = offset * 3;
                submesh = candidate;
                triangle = offset;
                a = triangles[index];
                b = triangles[index + 1];
                c = triangles[index + 2];
                return true;
            }
            return false;
        }

        private string SceneToolUndoName()
        {
            return ObjectNames.NicifyVariableName(sceneTool.ToString()) + " Hair";
        }

        private HairGroup FirstGroup()
        {
            return groom?.Groups != null && groom.Groups.Count > 0 ? groom.Groups[0] : null;
        }

        private void DisposeBuild()
        {
            posedEvaluation = interactiveEvaluation = null;
            pendingFocusBounds = null;
            pendingFocusPoint = null;
            authoringHeadPosition = null;
            authoringNeckPosition = null;
            authoringBonePositions = null;
            ResetMapPaintStroke();
            mapStrokeSamples.TrimExcess();
            cardRaycaster = null;
            cardHighlightLines = Array.Empty<Vector3>();
            highlightVertices.Clear(); highlightVertices.Capacity = 0;
            highlightSubmeshes.Clear(); highlightSubmeshes.Capacity = 0;
            highlightTriangles.Clear(); highlightTriangles.Capacity = 0;
            nextHighlightTriangles.Clear(); nextHighlightTriangles.Capacity = 0;
            highlightEdgeIndices.Clear(); highlightEdgeIndices.Capacity = 0;
            highlightEdges.Clear(); highlightEdges.TrimExcess();
            if (hairFilter != null) hairFilter.sharedMesh = null;
            meshBuild?.Dispose();
            meshBuild = null;
            evaluation = null;
            displayGuideCurves.Clear();
            displayGuidePolylines.Clear();
            curveBrushEntries.Clear();
            retainedBrushEntries.Clear();
            activeBrushBufferIds.Clear();
            staleGuideBufferIds.Clear();
            evaluationWorkspace.Clear();
            meshWorkspace.Clear();
            guideDepthRenderer.Dispose();
            if (hairRenderer != null) hairRenderer.sharedMaterials = Array.Empty<Material>();
            hairPreviewMaterials.Dispose();
        }

        private static void DestroyPreviewObject(UnityEngine.Object target)
        {
            if (target != null) DestroyImmediate(target);
        }

        private void RepaintAll()
        {
            SceneView.RepaintAll();
            HairGroomWorkspace.RepaintOpenWindows();
        }

        private static void EnsureEditorEvents()
        {
            if (eventsHooked) return;
            eventsHooked = true;
            AssemblyReloadEvents.beforeAssemblyReload += ExitStageIfActive;
            CompilationPipeline.compilationStarted += _ => ExitStageIfActive();
            EditorApplication.playModeStateChanged += _ => ExitStageIfActive();
        }

        private static void ExitStageIfActive()
        {
            try
            {
                if (StageUtility.GetCurrentStage() is HairCardStage) StageUtility.GoBackToPreviousStage();
            }
            catch (Exception)
            {
                // Stage shutdown is best effort during compilation and domain reload.
            }
        }
    }

    internal static class HairGroomRecovery
    {
        private const string RecoveryRoot = "Assets/UMAProjectData/HairCards/Recovery";

        [InitializeOnLoadMethod]
        private static void QueueRecoveryNameRepair()
        {
            EditorApplication.delayCall -= RepairRecoveryNamesAfterReload;
            EditorApplication.delayCall += RepairRecoveryNamesAfterReload;
        }

        private static void RepairRecoveryNamesAfterReload()
        {
            RepairSnapshotNames();
        }

        public static void SaveSnapshot(HairGroomAsset groom)
        {
            if (groom == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(groom))) return;
            EnsureFolder(RecoveryRoot);
            string recoveryPath = $"{RecoveryRoot}/{groom.GroomId}.asset";
            HairGroomAsset snapshot = AssetDatabase.LoadAssetAtPath<HairGroomAsset>(recoveryPath);
            bool createSnapshot = snapshot == null;
            if (createSnapshot && AssetDatabase.LoadMainAssetAtPath(recoveryPath) != null)
                throw new InvalidOperationException(
                    $"Hair groom recovery path is occupied by an unexpected asset: '{recoveryPath}'.");
            if (createSnapshot) snapshot = ScriptableObject.CreateInstance<HairGroomAsset>();
            try
            {
                EditorUtility.CopySerialized(groom, snapshot);
                snapshot.name = Path.GetFileNameWithoutExtension(recoveryPath);
                snapshot.hideFlags = HideFlags.NotEditable;
                if (createSnapshot) AssetDatabase.CreateAsset(snapshot, recoveryPath);
                else EditorUtility.SetDirty(snapshot);
                AssetDatabase.SaveAssetIfDirty(snapshot);
            }
            catch
            {
                if (createSnapshot && snapshot != null &&
                    string.IsNullOrEmpty(AssetDatabase.GetAssetPath(snapshot)))
                    UnityEngine.Object.DestroyImmediate(snapshot);
                throw;
            }
        }

        public static bool TryRestoreSnapshot(HairGroomAsset groom)
        {
            if (groom == null) return false;
            string recoveryPath = $"{RecoveryRoot}/{groom.GroomId}.asset";
            HairGroomAsset snapshot = AssetDatabase.LoadAssetAtPath<HairGroomAsset>(recoveryPath);
            if (snapshot == null) return false;
            if (!EditorUtility.DisplayDialog("Restore Hair Groom Recovery",
                    $"Restore the last automatic recovery snapshot for '{groom.name}'?", "Restore", "Cancel"))
                return false;
            Undo.RecordObject(groom, "Restore Hair Groom Recovery");
            RestoreSnapshotData(groom, snapshot);
            return true;
        }

        internal static void RestoreSnapshotData(HairGroomAsset groom, HairGroomAsset snapshot)
        {
            if (groom == null) throw new ArgumentNullException(nameof(groom));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            string groomPath = AssetDatabase.GetAssetPath(groom);
            string groomAssetName = string.IsNullOrEmpty(groomPath)
                ? groom.name
                : Path.GetFileNameWithoutExtension(groomPath);
            HideFlags groomHideFlags = groom.hideFlags;
            EditorUtility.CopySerialized(snapshot, groom);
            groom.name = groomAssetName;
            groom.hideFlags = groomHideFlags;
            groom.EnsureIntegrity();
            EditorUtility.SetDirty(groom);
            if (!string.IsNullOrEmpty(groomPath)) AssetDatabase.SaveAssetIfDirty(groom);
        }

        internal static int RepairSnapshotNames()
        {
            if (!AssetDatabase.IsValidFolder(RecoveryRoot)) return 0;

            int repaired = 0;
            string[] assetGuids = AssetDatabase.FindAssets(
                "t:HairGroomAsset", new[] { RecoveryRoot });
            for (int index = 0; index < assetGuids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(assetGuids[index]);
                HairGroomAsset snapshot = AssetDatabase.LoadAssetAtPath<HairGroomAsset>(path);
                if (snapshot == null) continue;

                string expectedName = Path.GetFileNameWithoutExtension(path);
                if (string.Equals(snapshot.name, expectedName, StringComparison.Ordinal)) continue;

                snapshot.name = expectedName;
                snapshot.hideFlags = HideFlags.NotEditable;
                EditorUtility.SetDirty(snapshot);
                AssetDatabase.SaveAssetIfDirty(snapshot);
                repaired++;
            }
            return repaired;
        }

        private static void EnsureFolder(string assetFolder)
        {
            string[] parts = assetFolder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
