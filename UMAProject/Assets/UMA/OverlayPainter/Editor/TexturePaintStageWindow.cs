using System;
using System.Collections.Generic;
using System.IO;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA.TexturePaint.Editor
{
    public sealed partial class TexturePaintStageWindow : PreviewSceneStage
    {
        private static string ShaderRoot => UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders") + "/";
        private const float SplineInsertTolerancePixels = 8f;
        private static bool eventsHooked;
        private static readonly int[] RibbonRotationValues = { -180, -90, 0, 90, 180 };
        private static readonly string[] RibbonRotationLabels =
            { "-180\u00b0", "-90\u00b0", "0\u00b0", "90\u00b0", "180\u00b0" };
        private static readonly string[] SplineSceneHints =
        {
            "\u2022 Shift-Click: Add spline point",
            "\u2022 Ctrl-Click: Insert point on spline",
            "\u2022 Click: Select/edit point",
            "\u2022 Drag: Move point",
            "\u2022 Green handles: Adjust curve",
            "\u2022 Blue handle: Adjust point width"
        };
        private static readonly string[] Spline2DSceneHints =
        {
            "2D SPLINE POINT",
            "\u2022 Orange handle: Position point",
            "\u2022 Green handles: Adjust curve",
            "\u2022 Blue handle: Adjust point width"
        };
        private static readonly string[] Spline2DNoSelectionSceneHints =
        {
            "2D SPLINE",
            "\u2022 Select a point in the 2D view"
        };
        private static readonly string[] PaintSceneHints =
        {
            "\u2022 Click/Drag: Paint",
            "\u2022 Shift + Right-Drag: Size/hardness"
        };
        private static readonly string[] ClonePaintSceneHints =
        {
            "\u2022 Click/Drag: Paint",
            "\u2022 Ctrl-Click: Set clone source",
            "\u2022 Shift + Right-Drag: Size/hardness"
        };
        private static readonly string[] MaskSceneHints =
        {
            "LAYER MASK MODE",
            "\u2022 Click/Drag: Paint mask",
            "\u2022 Shift + Right-Drag: Size/hardness"
        };
        private static readonly string[] PolygonFillSceneHints =
        {
            "POLYGON FILL",
            "\u2022 Click: Fill polygon",
            "\u2022 Esc: Cancel"
        };
        private static readonly string[] UVIslandFillSceneHints =
        {
            "UV ISLAND FILL",
            "\u2022 Click: Fill UV island",
            "\u2022 Esc: Cancel"
        };
        internal static TexturePaintStageWindow ActiveStage { get; private set; }

        [SerializeField] private DynamicCharacterAvatar avatar;
        [SerializeField] private TexturePaintLaunchContext launchContext;
        [SerializeField] private TexturePaintDocument launchDocument;
        [SerializeField] private int selectedSurface;
        [SerializeField] private string selectedTargetId;
        [SerializeField] private List<string> selectedSlots = new List<string>();
        [SerializeField] private bool slotTargetsExpanded = true;
        [SerializeField] private TexturePaintChannel selectedChannel = TexturePaintChannel.Albedo;
        [SerializeField] private TexturePaintNormalConvention normalConvention = TexturePaintNormalConvention.OpenGL;
        [SerializeField] private TexturePaintSourceMode sourceMode = TexturePaintSourceMode.SourceOverlay;
        [SerializeField] private TexturePaintTool tool;
        [SerializeField] private BrushPreset brush;
        [SerializeField] private BrushLibrary currentBrushLibrary;
        [SerializeField] private bool mirrorX;
        [SerializeField] private Color paintColor = Color.white;
        [SerializeField] private float strength = 1f;
        [SerializeField] private bool limitStrokeCoverage;
        [SerializeField, Range(0f, 1f)] private float strokeStabilization;
        [SerializeField, Range(0f, 1f)] private float directionSmoothing = 0.35f;
        [SerializeField, Range(0.05f, 2f)] private float projectionDepth = 0.5f;
        [SerializeField, Range(0f, 180f)] private float normalAngleLimit = 90f;
        [SerializeField] private bool paintBackfaces;
        [SerializeField] private bool pressureAffectsFlow = true;
        [SerializeField] private bool pressureAffectsSize;
        [SerializeField] private TexturePaintBrushSource paintSource = TexturePaintBrushSource.Color;
        [SerializeField] private Texture2D paintSourceTexture;
        [SerializeField] private Sprite paintSourceSprite;
        [SerializeField] private OverlayDataAsset paintSourceOverlay;
        [SerializeField] private Vector2 cloneSourceUV;
        [SerializeField] private int selectedBrushPlugin;
        [SerializeField] private bool splineMode;
        [SerializeField] private TexturePaintSpline spline;
        [SerializeField] private int selectedSplinePoint = -1;
        [SerializeField] private TexturePaintPathMode pathMode = TexturePaintPathMode.Ribbon;
        [SerializeField] private TexturePaintPathOrientation pathOrientation = TexturePaintPathOrientation.FollowPath;
        [SerializeField] private TexturePaintPathCap pathStartCap = TexturePaintPathCap.Round;
        [SerializeField] private TexturePaintPathCap pathEndCap = TexturePaintPathCap.Round;
        [SerializeField, Range(1, 16)] private int radialSymmetry = 1;
        [SerializeField] private Vector3 radialSymmetryAxis = Vector3.up;
        [SerializeField] private Texture2D ribbonBeginningTexture;
        [SerializeField] private Sprite ribbonBeginningSprite;
        [SerializeField] private Texture2D ribbonEndTexture;
        [SerializeField] private Sprite ribbonEndSprite;
        [SerializeField] private bool performanceExpanded;
        [SerializeField, Range(16, 1024)] private int historyBudgetMB = 256;
        [SerializeField, Range(16, 512)] private int coverageBudgetMB = 128;
        [SerializeField] private string exportFolder = UMAPathUtility.OverlayPainterGeneratedRoot;

        private TexturePaintStageController controller;
        private TexturePaintDocument document;
        private BrushPreset transientBrush;
        private GameObject lightObject;
        private bool needsFrame = true;
        private bool strokeActive;
        [NonSerialized] private bool syncingLogicalLayerSelection;
        private bool paintGestureActive;
        private bool initialFollowStampPending;
        private bool hasInitialFollowStamp;
        private StrokeSample initialFollowStamp;
        [NonSerialized] private int paintRandomSeed;
        [NonSerialized] private int paintRandomStampIndex;
        [NonSerialized] private float paintStrokeWorldDistance;
        [NonSerialized] private bool hasPaintStrokeDistanceSample;
        private long paintHistoryVersionAtStrokeStart;
        private bool applyingSpline;
        [NonSerialized] private BrushPreset activeSplineBrush;
        private long observedPluginCommitVersion;
        [NonSerialized] private System.Threading.CancellationTokenSource pluginLayerCancellation;
        [NonSerialized] private string runningPluginLayerId;
        [NonSerialized] private float pluginLayerProgress;
        [NonSerialized] private bool splineReapplyPending;
        [NonSerialized] private bool textureWindowRepaintPending;
        [NonSerialized] private TextureSet pendingSplineSet;
        [NonSerialized] private TexturePaintLayer pendingSplineLayer;
        private readonly WorldSpaceStrokeSampler strokeSampler = new WorldSpaceStrokeSampler();
        private readonly List<StrokeSample> sampledStrokePoints = new List<StrokeSample>();
        private readonly List<StrokeDispatchSample> splineDispatchSamples = new List<StrokeDispatchSample>();
        private readonly Dictionary<StrokeContactKey, StrokeSample> previousContactSamples =
            new Dictionary<StrokeContactKey, StrokeSample>();
        private ReconstructedSurface hoverSurface;
        private RaycastHit hoverHit;
        private Vector3 hoverTangent;
        private bool hasHover;
        private bool documentDirty;
        private string documentRevision;
        private double nextAutosaveTime;
        private const double AutosaveIntervalSeconds = 30d;
        private Vector2 scroll;
        private readonly List<TextureSet> strokeTextureSets = new List<TextureSet>();
        private TexturePaintLogicalTarget strokeLogicalTarget;
        private readonly List<SurfaceBrushContact> brushContacts = new List<SurfaceBrushContact>();
        [NonSerialized] private Dictionary<TexturePaintSpline, SplineDisplayCache> splineDisplayCache =
            new Dictionary<TexturePaintSpline, SplineDisplayCache>();
        [NonSerialized] private HashSet<int> selectedSplinePoints = new HashSet<int>();
        private static string splineClipboard;
        private TexturePaintResourceSnapshot resourceBaseline;
        private bool hasResourceBaseline;
        private string resourceCheckResult;
        [NonSerialized] private bool pathEditRecordedThisGUI;
        [NonSerialized] private bool splineReapplyDelayScheduled;
        [NonSerialized] private int splineHandleHotControl;
        [NonSerialized] private TextureSet splineHandleEditSet;
        [NonSerialized] private string splineHandleEditLabel;
        [NonSerialized] private bool splineHandleUndoStarted;

        private sealed class SplineDisplayCache
        {
            public int signature;
            public Vector3[] points;
        }

        private enum SplineSurfaceHandleEvent
        {
            None,
            Pressed,
            Dragged,
            ContextRequested
        }

        private enum SplineEditingSpace
        {
            Texture2D,
            Surface3D
        }

        private const int SplineAnchorHandleHint = 0x51A100;
        private const int SplineIncomingHandleHint = 0x51A200;
        private const int SplineOutgoingHandleHint = 0x51A300;
        private const int SplineWidthHandleHint = 0x51A400;

        private readonly struct StrokeContactKey : IEquatable<StrokeContactKey>
        {
            private readonly int surface;
            private readonly int island;
            private readonly int triangle;
            private readonly string slot;
            private readonly int variant;

            public StrokeContactKey(int surface, int island, int triangle, string slot, int variant)
            {
                this.surface = surface;
                this.island = island;
                this.triangle = triangle;
                this.slot = slot ?? string.Empty;
                this.variant = variant;
            }

            public bool Equals(StrokeContactKey other) => surface == other.surface && island == other.island && triangle == other.triangle &&
                variant == other.variant && string.Equals(slot, other.slot, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is StrokeContactKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked { return ((((surface * 397) ^ island) * 397 ^ triangle) * 397 ^ slot.GetHashCode()) * 397 ^ variant; }
            }
        }

        public TexturePaintStageController Controller => controller;

        public static TexturePaintStageWindow ShowStage(DynamicCharacterAvatar avatar)
        {
            return ShowStage(avatar, null);
        }

        public static TexturePaintStageWindow ShowStage(DynamicCharacterAvatar avatar,
            TexturePaintDocument document)
        {
            if (avatar == null) return null;
            if (PrefabStageUtility.GetPrefabStage(avatar.gameObject) != null)
            {
                EditorUtility.DisplayDialog("Overlay Painter Unavailable",
                    "Overlay Painter requires a generated DynamicCharacterAvatar in an open scene. Exit Prefab Mode and try again.", "OK");
                return null;
            }
            TexturePaintStageWindow stage = CreateInstance<TexturePaintStageWindow>();
            stage.avatar = avatar;
            stage.launchDocument = document;
            StageUtility.GoToStage(stage, true);
            return stage;
        }

        public static TexturePaintStageWindow ShowStage(TexturePaintLaunchContext context)
        {
            return ShowStage(context, null);
        }

        public static TexturePaintStageWindow ShowStage(TexturePaintLaunchContext context,
            TexturePaintDocument document)
        {
            if (context == null || !context.IsStandalone) return null;
            TexturePaintStageWindow stage = CreateInstance<TexturePaintStageWindow>();
            stage.launchContext = context;
            stage.launchDocument = document;
            StageUtility.GoToStage(stage, true);
            return stage;
        }

        internal static bool OpenDocumentAsset(TexturePaintDocument document)
        {
            if (document == null) return false;
            if (ActiveStage?.controller != null)
            {
                ActiveStage.LoadWorkspaceDocument(document);
                TexturePaintDockWindow.ShowDockable();
                return true;
            }
            if (document.launchContext?.IsStandalone == true)
                return ShowStage(document.launchContext.Clone(), document) != null;

            DynamicCharacterAvatar documentAvatar = null;
            if (!string.IsNullOrEmpty(document.avatarGlobalObjectId) &&
                GlobalObjectId.TryParse(document.avatarGlobalObjectId, out GlobalObjectId avatarId))
                documentAvatar = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(avatarId) as DynamicCharacterAvatar;
            if (documentAvatar != null) return ShowStage(documentAvatar, document) != null;

            EditorUtility.DisplayDialog("Overlay Painter Document",
                "This document was created from a scene avatar that is not currently available. Open Overlay Painter from the original generated avatar, then use File > Load Document.",
                "OK");
            return true;
        }

        protected override GUIContent CreateHeaderContent() => new GUIContent("Overlay Painter", EditorGUIUtility.IconContent("Texture Icon").image);

        protected override bool OnOpenStage()
        {
            base.OnOpenStage();
            EnsureEvents();
            bool standalone = launchContext != null && launchContext.IsStandalone;
            if (!standalone && (avatar == null || avatar.umaData == null))
            {
                EditorUtility.DisplayDialog("Overlay Painter", "Generate the DynamicCharacterAvatar before opening this stage.", "OK");
                return false;
            }
            try
            {
                transientBrush = CreateInstance<BrushPreset>();
                transientBrush.name = "Session Brush";
                transientBrush.hideFlags = HideFlags.HideAndDontSave;
                ComputeShader stroke = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "StrokeRasterize.compute");
                ComputeShader blur = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "Blur.compute");
                ComputeShader normal = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "NormalTouchup.compute");
                ComputeShader composite = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "LayerComposite.compute");
                ComputeShader channelPack = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "ChannelPack.compute");
                Shader fill = AssetDatabase.LoadAssetAtPath<Shader>(ShaderRoot + "FillLayer.shader");
                Shader ribbon = AssetDatabase.LoadAssetAtPath<Shader>(ShaderRoot + "RibbonProjection.shader");
                controller = new TexturePaintStageController();
                if (standalone)
                    controller.InitializeStandalone(launchContext, stroke, blur, normal, composite, channelPack,
                        launchContext.resolution, fill, ribbon);
                else controller.Initialize(avatar, stroke, blur, normal, composite, channelPack, 2048, fill, ribbon);
                ShowReconstructionWarnings(controller.Reconstruction);
                SceneManager.MoveGameObjectToScene(controller.Reconstruction.root, scene);
                lightObject = new GameObject("Overlay Painter Lighting");
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.2f;
                lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                SceneManager.MoveGameObjectToScene(lightObject, scene);
                TexturePaintStageState savedState = standalone ? null : controller.LoadRecipeState();
                if (!InitializeDocumentSession())
                {
                    controller.Dispose();
                    controller = null;
                    if (lightObject != null) DestroyImmediate(lightObject);
                    if (transientBrush != null) DestroyImmediate(transientBrush);
                    return false;
                }
                observedPluginCommitVersion = controller.Plugins.CommitVersion;
                RestoreState(LoadDocumentEditorState() ?? savedState, false);
                EnsureInitialSlotSelection();
                if (controller.Textures.Sets.Count > 0)
                {
                    suppressLogicalLayerRepair = true;
                    try { SyncActiveLayerSelection(controller.Textures.Sets[selectedSurface]); }
                    finally { suppressLogicalLayerRepair = false; }
                }
                InitializeWorkspaceUI();
                controller.Painting.TextureChanged += OnTextureChanged;
                controller.Plugins.Changed += OnPluginChanged;
                EditorApplication.update += PersistenceUpdate;
                nextAutosaveTime = EditorApplication.timeSinceStartup + AutosaveIntervalSeconds;
                SceneView.duringSceneGui += OnSceneGUI;
                Tools.hidden = true;
                ActiveStage = this;
                TexturePaintDockWindow.ShowDockable();
                TexturePaintUVWindow.ShowDockable();
                return true;
            }
            catch (Exception exception)
            {
                DisposeWorkspaceUI();
                EditorApplication.update -= PersistenceUpdate;
                if (controller?.Painting != null) controller.Painting.TextureChanged -= OnTextureChanged;
                if (controller?.Plugins != null) controller.Plugins.Changed -= OnPluginChanged;
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Overlay Painter", exception.Message, "OK");
                controller?.Dispose(); controller = null;
                TexturePaintSpriteSource.ClearCache();
                if (document != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(document))) DestroyImmediate(document);
                document = null;
                if (ActiveStage == this) ActiveStage = null;
                return false;
            }
        }

        private static void ShowReconstructionWarnings(MeshReconstructionResult reconstruction)
        {
            if (reconstruction?.warnings == null || reconstruction.warnings.Count == 0) return;
            for (int warningIndex = 0; warningIndex < reconstruction.warnings.Count; warningIndex++)
                Debug.LogWarning("Overlay Painter: " + reconstruction.warnings[warningIndex]);
            EditorUtility.DisplayDialog("Overlay Painter - Material Source Warning",
                string.Join("\n\n", reconstruction.warnings), "Continue");
        }

        protected override void OnCloseStage()
        {
            pluginLayerCancellation?.Cancel();
            pluginLayerCancellation?.Dispose();
            pluginLayerCancellation = null;
            ReleaseSplineHandleCapture();
            ReleaseModifierBrushCapture(false);
            DisposeWorkspaceUI();
            if (controller != null)
            {
                DisposeDocumentSession();
                if (controller.Painting != null) controller.Painting.TextureChanged -= OnTextureChanged;
                if (controller.Plugins != null) controller.Plugins.Changed -= OnPluginChanged;
                controller.Dispose(); controller = null;
            }
            TexturePaintSpriteSource.ClearCache();
            EditorApplication.update -= PersistenceUpdate;
            EditorApplication.delayCall -= ReapplySplineAfterGUI;
            EditorApplication.delayCall -= RepaintTextureWindows;
            textureWindowRepaintPending = false;
            SceneView.duringSceneGui -= OnSceneGUI;
            Tools.hidden = false;
            ClearLightweightHistory();
            if (lightObject != null) DestroyImmediate(lightObject);
            if (transientBrush != null) DestroyImmediate(transientBrush);
            if (ActiveStage == this) ActiveStage = null;
            documentRevision = null;
            TexturePaintDockWindow.RepaintOpenWindows();
            TexturePaintUVWindow.RepaintOpenWindows();
            base.OnCloseStage();
        }

        private static void EnsureEvents()
        {
            if (eventsHooked) return;
            eventsHooked = true;
            AssemblyReloadEvents.beforeAssemblyReload += ExitIfActive;
            CompilationPipeline.compilationStarted += _ => ExitIfActive();
            EditorApplication.playModeStateChanged += _ => ExitIfActive();
        }

        private static void ExitIfActive()
        {
            try { if (StageUtility.GetCurrentStage() is TexturePaintStageWindow) StageUtility.GoBackToPreviousStage(); }
            catch { }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (controller?.Reconstruction == null) return;
            if (closeAfterSave && IsPersistenceActive) return;
            if (needsFrame && Event.current.type == EventType.Repaint)
            {
                needsFrame = false;
                Bounds bounds = CalculateBounds();
                sceneView.Frame(bounds, false);
            }
            Event current = Event.current;
            EventType inputEventType = current.type;
            ApplySceneViewDisplay(sceneView);
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            hasHover = controller.Reconstruction.Raycast(ray, out hoverSurface, out hoverHit);
            if (hasHover) hoverTangent = CalculateTangent(hoverSurface, hoverHit.triangleIndex);
            bool targetHover = hasHover && IsSelectedSlotHit(hoverSurface, hoverHit.triangleIndex);
            TexturePaintLayer sceneSplineLayer = null;
            bool activeSplineLayer = !IsLayerMaskMode(ActiveTextureSet) &&
                TryGetActivePathLayer(ActiveTextureSet, out sceneSplineLayer);
            bool authoringSplineLayer = activeSplineLayer && sceneSplineLayer.spline?.worldSpace == true;
            bool twoDimensionalSplineLayer = activeSplineLayer && sceneSplineLayer.spline?.worldSpace == false;
            bool positioningTwoDimensionalPoint = twoDimensionalSplineLayer && selectedSplinePoint >= 0 &&
                selectedSplinePoint < sceneSplineLayer.spline.PointCount;
            bool sceneSplineHandlesActive = authoringSplineLayer || positioningTwoDimensionalPoint;
            splineMode = authoringSplineLayer;
            if (ShouldYieldToSceneNavigation(current))
                ReleaseSplineHandleCapture(true, false);
            else if (splineHandleHotControl != 0 && GUIUtility.hotControl != splineHandleHotControl)
                ReleaseSplineHandleCapture(true, false);
            else if (!sceneSplineHandlesActive && splineHandleHotControl != 0)
                ReleaseSplineHandleCapture(true, false);
            DrawSceneModeHints(sceneView, authoringSplineLayer, twoDimensionalSplineLayer);
            if (HandleWorkspaceShortcuts(current, true))
            {
                sceneView.Repaint();
                return;
            }
            if (HandleBrushModifierDrag(current, true))
            {
                sceneView.Repaint();
                return;
            }
            if (geometryFillMode != 0 && HandleGeometryFill(current, targetHover))
            {
                sceneView.Repaint();
                return;
            }
            if (sceneSplineHandlesActive && current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            TextureSet hoverSet = targetHover ? controller.Textures.FindSet(hoverSurface.index) : null;
            if (targetHover && !authoringSplineLayer && CanStartFreehandPaint(hoverSet)) DrawCursor();
            DrawVisibleSplines();

            if (!current.alt && current.button == 0)
            {
                if (current.type == EventType.MouseDown && authoringSplineLayer)
                {
                    if (current.shift && targetHover) AddSplinePoint();
                    else if (current.control || current.command)
                        TryInsertSplinePointAt(current.mousePosition);
                    current.Use();
                }
                else if (current.type == EventType.MouseDown && twoDimensionalSplineLayer)
                {
                    ShowWorkspaceStatus("This is a 2D spline. Edit it in the 2D view.");
                    current.Use();
                }
                else if (current.type == EventType.MouseDown && targetHover)
                {
                    if (!authoringSplineLayer && CanStartFreehandPaint(hoverSet) &&
                        tool == TexturePaintTool.Clone && current.control)
                    {
                        cloneSourceUV = hoverHit.textureCoord;
                    }
                    else if (CanStartFreehandPaint(hoverSet))
                    {
                        paintGestureActive = true;
                        BeginPaint();
                    }
                    else ShowPaintLayerRequiredStatus(hoverSet);
                    current.Use();
                }
                else if (current.type == EventType.MouseDrag && paintGestureActive)
                {
                    if (targetHover)
                    {
                        if (strokeActive) ContinuePaint();
                        else BeginPaint();
                    }
                    else EndPaint();
                    current.Use();
                }
                else if (current.type == EventType.MouseUp && paintGestureActive)
                {
                    if (strokeActive) EndPaint();
                    paintGestureActive = false;
                    current.Use();
                }
                else if (current.type == EventType.MouseUp && splineReapplyPending)
                {
                    if (pendingPathEdit != null && pendingPathEdit.deferred) CommitPendingPathEdit();
                    ReapplyPendingSpline();
                    current.Use();
                }
                else if (current.type == EventType.MouseUp && pendingPathEdit != null && pendingPathEdit.deferred)
                {
                    CommitPendingPathEdit();
                    current.Use();
                }
            }
            if (current.type == EventType.MouseLeaveWindow)
            {
                if (strokeActive) EndPaint();
                if (pendingPathEdit != null && pendingPathEdit.deferred) CommitPendingPathEdit();
                ReleaseSplineHandleCapture(true, false);
                ReleaseModifierBrushCapture(true);
                paintGestureActive = false;
            }

            if (strokeActive || inputEventType == EventType.MouseMove || inputEventType == EventType.MouseDrag)
                sceneView.Repaint();
        }

        private void DrawSceneModeHints(SceneView sceneView, bool authoringSplineLayer,
            bool twoDimensionalSplineLayer)
        {
            // Scene View GUI is driven for multiple event types; only draw during repaint so the
            // help panel remains passive and cannot consume painting or spline-edit input.
            if (Event.current.type != EventType.Repaint || sceneView == null) return;

            string[] hints;
            bool maskMode = IsLayerMaskMode(ActiveTextureSet);
            if (geometryFillMode == 1) hints = PolygonFillSceneHints;
            else if (geometryFillMode == 2) hints = UVIslandFillSceneHints;
            else if (maskMode) hints = MaskSceneHints;
            else if (authoringSplineLayer) hints = SplineSceneHints;
            else if (twoDimensionalSplineLayer)
                hints = selectedSplinePoint >= 0 ? Spline2DSceneHints : Spline2DNoSelectionSceneHints;
            else if (CanStartFreehandPaint(ActiveTextureSet))
                hints = tool == TexturePaintTool.Clone ? ClonePaintSceneHints : PaintSceneHints;
            else return;

            const float margin = 40f;
            const float width = 245f;
            const float lineHeight = 18f;
            const float padding = 7f;
            float height = hints.Length * lineHeight + padding * 2f;
            Rect panel = new Rect(Mathf.Max(margin, sceneView.position.width - width - margin), margin,
                width, height);

            Handles.BeginGUI();
            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.92f);
            GUI.Box(panel, GUIContent.none, EditorStyles.helpBox);
            GUI.color = previousColor;
            for (int i = 0; i < hints.Length; i++)
            {
                GUI.Label(new Rect(panel.x + padding, panel.y + padding + i * lineHeight,
                    panel.width - padding * 2f, lineHeight), hints[i],
                    (maskMode || geometryFillMode != 0) && i == 0
                        ? EditorStyles.boldLabel : EditorStyles.miniLabel);
            }
            Handles.EndGUI();
        }

        internal void DrawToolsPanel()
        {
            if (controller?.Textures == null) return;
            bool changedBefore = GUI.changed;
            scroll = GUILayout.BeginScrollView(scroll);
            IReadOnlyList<TextureSet> sets = controller.Textures.Sets;
            DrawSlotTargetSelector(sets);
            EnsureActiveSurfaceMatchesSlots(sets);
            TextureSet set = sets[Mathf.Clamp(selectedSurface, 0, sets.Count - 1)];
            pathEditRecordedThisGUI = false;
            bool hadPathRenderState = TryCapturePathRenderState(out TextureSet pathSetBefore,
                out TexturePaintLayer pathLayerBefore, out TexturePaintSplineSettings pathSettingsBefore,
                out int pathSignatureBefore);

            TexturePaintLayer destinationLayer = (uint)set.activeLayerIndex < (uint)set.layers.Count
                ? set.layers[set.activeLayerIndex] : null;
            bool maskMode = IsLayerMaskMode(set);
            if (maskMode)
            {
                sourceMode = TexturePaintSourceMode.SourceOverlay;
                EditorGUILayout.LabelField("Paint Target", "Layer Mask");
                EditorGUILayout.HelpBox("Mask painting is grayscale only and does not use material channels, texture sources, sprites, or overlays.",
                    MessageType.Info);
                DrawLayerMaskSource(set, destinationLayer);
            }
            else if (destinationLayer != null &&
                (destinationLayer.kind == TexturePaintLayerKind.Paint || destinationLayer.IsSplineLayer))
            {
                sourceMode = TexturePaintSourceMode.SourceOverlay;
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.EnumPopup("Paint Target", TexturePaintSourceMode.SourceOverlay);
            }
            else sourceMode = (TexturePaintSourceMode)EditorGUILayout.EnumPopup("Paint Target", sourceMode);
            bool layerOwnsSources = maskMode || destinationLayer != null &&
                destinationLayer.kind != TexturePaintLayerKind.Group;
            if (!layerOwnsSources)
            {
                EditorGUILayout.Space(2f);
                GUILayout.Label("Paint Source", EditorStyles.boldLabel);
                if (TexturePaintChannelUtility.IsAuxiliary(selectedChannel))
                {
                    int sourceIndex = paintSource == TexturePaintBrushSource.Texture ? 0 : 1;
                    sourceIndex = GUILayout.Toolbar(sourceIndex, new[] { "Texture", "Color" });
                    paintSource = sourceIndex == 0 ? TexturePaintBrushSource.Texture :
                        TexturePaintBrushSource.Color;
                }
                else paintSource = (TexturePaintBrushSource)GUILayout.Toolbar((int)paintSource,
                    new[] { "Texture", "Overlay", "Color" });
                switch (paintSource)
                {
                    case TexturePaintBrushSource.Texture:
                        DrawTextureOrSpriteSourceFields();
                        if (paintSourceTexture == null)
                            EditorGUILayout.HelpBox("Assign a texture or sprite before painting.", MessageType.Warning);
                        break;
                    case TexturePaintBrushSource.Overlay:
                        DrawOverlayDataSelector(sets);
                        if (paintSourceOverlay == null)
                            EditorGUILayout.HelpBox("Select OverlayData before painting.", MessageType.Warning);
                        break;
                    case TexturePaintBrushSource.Color:
                        if (TexturePaintChannelUtility.IsGrayscale(selectedChannel))
                        {
                            float value = EditorGUILayout.Slider("Value",
                                TexturePaintChannelUtility.ScalarValue(paintColor), 0f, 1f);
                            paintColor = new Color(value, value, value, paintColor.a);
                        }
                        else paintColor = EditorGUILayout.ColorField("Color", paintColor);
                        break;
                }
                if (TexturePaintChannelUtility.IsAuxiliary(selectedChannel))
                    EditorGUILayout.HelpBox("Normal Control has no OverlayData material source. Use a texture, sprite, or grayscale value.", MessageType.None);
            }
            if (!maskMode)
            {
                EditorGUI.BeginChangeCheck();
                selectedChannel = DrawAvailableChannelPopup(set, new GUIContent("Channel"), selectedChannel);
                if (selectedChannel == TexturePaintChannel.Normal)
                    normalConvention = (TexturePaintNormalConvention)EditorGUILayout.EnumPopup("Normal Convention", normalConvention);
                if (EditorGUI.EndChangeCheck())
                {
                    paintColor = TexturePaintChannelUtility.ConstrainColor(selectedChannel, paintColor);
                    RefreshPaintSourceForChannel();
                }
                if (set.GetChannel(selectedChannel) == null) EditorGUILayout.HelpBox("The active slot material does not expose the selected channel.", MessageType.Warning);
                if (selectedChannel == TexturePaintChannel.NormalControl)
                {
                    EditorGUILayout.HelpBox("Neutral gray leaves normals unchanged; dark recesses and light raises the generated normal.", MessageType.None);
                    TexturePaintLayer activeLayer = (uint)set.activeLayerIndex < (uint)set.layers.Count
                        ? set.layers[set.activeLayerIndex] : null;
                    TexturePaintLayerChannelSettings activeSettings = activeLayer?.GetChannelSettings(
                        TexturePaintChannel.NormalControl, false);
                    if (activeSettings != null && activeLayer.channels.ContainsKey(TexturePaintChannel.NormalControl))
                    {
                        EditorGUI.BeginChangeCheck();
                        float layerStrength = EditorGUILayout.Slider(new GUIContent("Height Strength",
                            "Slope intensity generated only by this layer's grayscale height field."),
                            set.ResolveNormalControlStrength(activeSettings), 0f, 16f);
                        if (EditorGUI.EndChangeCheck())
                            ChangeLayerNormalControlStrength(set, activeLayer, layerStrength);
                    }
                    else EditorGUILayout.HelpBox(
                        "Select a layer with a Normal Control channel to edit its Height Strength.",
                        MessageType.Info);
                    EditorGUI.BeginChangeCheck();
                    int controlRadius = EditorGUILayout.IntSlider("Sample Radius (px)", set.normalControlRadius, 1, 16);
                    bool controlInvert = EditorGUILayout.Toggle("Invert Height", set.normalControlInvert);
                    if (EditorGUI.EndChangeCheck())
                        ChangeNormalControlSettings(set, set.normalControlStrength, controlRadius, controlInvert);
                }
            }
            else
            {
                selectedChannel = TexturePaintChannel.Albedo;
                if (tool == TexturePaintTool.NormalTouchup) tool = TexturePaintTool.Paint;
            }

            EditorGUILayout.Space(4f);
            tool = (TexturePaintTool)EditorGUILayout.EnumPopup("Tool", tool);
            if (tool == TexturePaintTool.NormalTouchup && selectedChannel != TexturePaintChannel.Normal)
            {
                selectedChannel = TexturePaintChannel.Normal;
                RefreshPaintSourceForChannel();
                EditorGUILayout.HelpBox("Normal Touchup always targets the normal channel.", MessageType.Info);
            }
            if (tool == TexturePaintTool.Clone)
                EditorGUILayout.HelpBox("Control-click the model to set the clone source, then paint normally.", MessageType.None);
            if (tool == TexturePaintTool.Plugin && controller.Plugins.Brushes.Count > 0)
            {
                string[] pluginNames = new string[controller.Plugins.Brushes.Count];
                for (int i = 0; i < pluginNames.Length; i++) pluginNames[i] = controller.Plugins.Brushes[i].Descriptor.displayName;
                selectedBrushPlugin = EditorGUILayout.Popup("Brush Plugin", Mathf.Clamp(selectedBrushPlugin, 0, pluginNames.Length - 1), pluginNames);
                ITexturePaintBrushV2 selectedPlugin = controller.Plugins.Brushes[Mathf.Clamp(selectedBrushPlugin, 0, controller.Plugins.Brushes.Count - 1)];
                PluginManagerWindow.DrawParameters(selectedPlugin.Descriptor, controller.Plugins.GetParameters(selectedPlugin));
            }
            BrushPreset selectedPreset = (BrushPreset)EditorGUILayout.ObjectField(
                "Brush Preset", brush, typeof(BrushPreset), false);
            if (selectedPreset != brush) SelectBrushPreset(selectedPreset);
            BrushPreset active = ActiveBrush;
            EditorGUI.BeginChangeCheck();
            active.shape = (BrushPreset.Shape)EditorGUILayout.EnumPopup("Shape", active.shape);
            active.size = EditorGUILayout.Slider(new GUIContent("Brush Size",
                "World-space radius in the 3D view; normalized-UV radius in the 2D view."),
                active.size, 0.001f, 0.5f);
            active.hardness = EditorGUILayout.Slider("Hardness", active.hardness, 0f, 1f);
            active.flow = EditorGUILayout.Slider("Flow", active.flow, 0f, 1f);
            active.spacing = EditorGUILayout.Slider(new GUIContent("Stroke Spacing",
                "Center-to-center stamp spacing measured in brush diameters."), active.spacing, 0.01f, 10f);
            active.rotation = DrawBrushRotation(active.rotation);
            active.blendMode = (TexturePaintBlendMode)EditorGUILayout.EnumPopup(
                "Brush Blend", active.blendMode);
            active.mirrorStroke = EditorGUILayout.Toggle("Mirror Stroke", active.mirrorStroke);
            active.alignToStroke = EditorGUILayout.Toggle("Follow Stroke", active.alignToStroke);
            if (active.shape == BrushPreset.Shape.Stamp)
                BrushPresetInspectorUtility.DrawStampSource(active);
            if (!splineMode || IsLayerMaskMode(ActiveTextureSet))
            {
                BrushPresetInspectorUtility.DrawRandomization(active);
                BrushPresetInspectorUtility.DrawStrokeEvolution(active);
            }
            strength = EditorGUILayout.Slider("Strength", strength, 0f, 1f);
            limitStrokeCoverage = EditorGUILayout.Toggle(
                new GUIContent("Cap Update Per Stroke", "Accumulates toward the coverage allowed by the brush falloff. Hardness shapes the soft edge; Flow controls how quickly it fills."),
                limitStrokeCoverage);
            mirrorX = EditorGUILayout.Toggle("Mirror Global X", mirrorX);
            strokeStabilization = EditorGUILayout.Slider(new GUIContent("Stabilization", "Smooths pointer movement before distance-based stroke sampling."),
                strokeStabilization, 0f, 1f);
            directionSmoothing = EditorGUILayout.Slider(new GUIContent("Direction Smoothing", "Filters stamp direction used by smear and Follow Stroke rotation."),
                directionSmoothing, 0f, 1f);
            pressureAffectsFlow = EditorGUILayout.Toggle("Pressure Affects Flow", pressureAffectsFlow);
            pressureAffectsSize = EditorGUILayout.Toggle("Pressure Affects Size", pressureAffectsSize);
            projectionDepth = EditorGUILayout.Slider(new GUIContent("Projection Depth", "Maximum depth from the stroke surface, as a multiple of brush size."),
                projectionDepth, 0.05f, 2f);
            normalAngleLimit = EditorGUILayout.Slider(new GUIContent("Normal Angle", "Maximum surface-normal deviation accepted by the projected brush."),
                normalAngleLimit, 0f, 180f);
            paintBackfaces = EditorGUILayout.Toggle("Paint Backfaces", paintBackfaces);
            EditorGUI.EndChangeCheck();
            DrawBrushAssetActions();

            EditorGUILayout.Space(5f);
            TexturePaintLogicalTarget layerTarget = ActiveLogicalTarget;
            string layerHeading = layerTarget != null
                ? $"Layers · {layerTarget.displayName}" + (layerTarget.isUdim ? $" ({layerTarget.members.Count} UDIM tiles)" : string.Empty)
                : "Layers";
            GUILayout.Label(layerHeading, EditorStyles.boldLabel);
            int moveFrom = -1;
            int moveTo = -1;
            int deleteLayer = -1;
            for (int i = set.layers.Count - 1; i >= 0; i--)
            {
                TexturePaintLayer layer = set.layers[i];
                GUILayout.BeginHorizontal();
                bool activeLayer = set.activeLayerIndex == i;
                string kindPrefix = layer.kind == TexturePaintLayerKind.Spline ? "Spline: " :
                    layer.kind == TexturePaintLayerKind.Fill ? "Fill: " :
                    layer.kind == TexturePaintLayerKind.Group ? "Group: " :
                    layer.kind == TexturePaintLayerKind.Plugin ? "Plugin: " : string.Empty;
                string channelLabel = LayerChannelSummary(layer);
                string layerLabel = (string.IsNullOrEmpty(layer.parentId) ? string.Empty : "    ") + kindPrefix +
                    layer.name + (string.IsNullOrEmpty(channelLabel) ? string.Empty : ": " + channelLabel);
                if (GUILayout.Toggle(activeLayer, layerLabel, "Button") && !activeLayer)
                {
                    set.activeLayerIndex = i;
                    SyncActiveLayerSelection(set);
                }
                bool visible = GUILayout.Toggle(layer.visible, new GUIContent(layer.visible ? "ON" : "OFF",
                    layer.visible ? "Layer is visible; click to hide it" : "Layer is hidden; click to show it"),
                    GUILayout.Width(38f));
                if (visible != layer.visible)
                {
                    ChangeLayerVisibility(set, layer, visible);
                }
                using (new EditorGUI.DisabledScope(i == set.layers.Count - 1))
                    if (GUILayout.Button(new GUIContent("\u25B2", "Move layer up"), GUILayout.Width(22f))) { moveFrom = i; moveTo = i + 1; }
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUILayout.Button(new GUIContent("\u25BC", "Move layer down"), GUILayout.Width(22f))) { moveFrom = i; moveTo = i - 1; }
                if (GUILayout.Button(new GUIContent("X", "Delete layer"), GUILayout.Width(22f))) deleteLayer = i;
                GUILayout.EndHorizontal();
            }
            if (moveFrom >= 0)
            {
                if (MoveLayerWithHistory(set, moveFrom, moveTo)) SyncActiveLayerSelection(set);
            }
            if (deleteLayer >= 0)
            {
                TexturePaintLayer layer = set.layers[deleteLayer];
                if (EditorUtility.DisplayDialog("Delete Texture Layer",
                    GetLayerDeletionConfirmation(set, layer), "Delete", "Cancel"))
                {
                    DeleteLayerWithHistory(set, deleteLayer);
                    SyncActiveLayerSelection(set);
                }
            }

            if ((uint)set.activeLayerIndex < (uint)set.layers.Count)
            {
                TexturePaintLayer activeLayer = set.layers[set.activeLayerIndex];
                EditorGUI.BeginChangeCheck();
                string layerName = EditorGUILayout.TextField("Name", activeLayer.name);
                float layerOpacity = EditorGUILayout.Slider("Layer Opacity", activeLayer.opacity, 0f, 1f);
                TexturePaintBlendMode layerBlend = (TexturePaintBlendMode)EditorGUILayout.EnumPopup("Layer Blend", activeLayer.blendMode);
                if (EditorGUI.EndChangeCheck())
                {
                    ChangeLayerMetadata(set, activeLayer, layerName, layerOpacity, layerBlend);
                }

                if (activeLayer.kind == TexturePaintLayerKind.Fill)
                {
                    DrawFillLayerProperties(set, activeLayer);
                }
                if (activeLayer.kind == TexturePaintLayerKind.Plugin)
                    DrawPluginLayerProperties(set, activeLayer);

                if (activeLayer.kind != TexturePaintLayerKind.Group &&
                    activeLayer.kind != TexturePaintLayerKind.Plugin &&
                    EditorGUILayout.DropdownButton(new GUIContent("Add from Sprite Set",
                        "Assign one sprite-set material to this layer's supported channels."),
                        FocusType.Keyboard))
                {
                    TexturePaintLayer targetLayer = activeLayer;
                    OverlayPainterSpriteSetPickerWindow.Show((spriteSet, spriteIndex, tiling) =>
                        AddFromSpriteSet(set, targetLayer, spriteSet, spriteIndex, tiling));
                }

                DrawLayerChannelProperties(set, activeLayer,
                    activeLayer.kind != TexturePaintLayerKind.Plugin);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Paint"))
            {
                AddPaintLayer(set);
            }
            if (GUILayout.Button("+ Fill"))
            {
                AddFillLayer(set);
            }
            if (GUILayout.Button("+ Plugin")) AddPluginLayer(set);
            if (GUILayout.Button("+ Group"))
            {
                BeginLayerCreationUndo("Add Layer Group");
                TexturePaintLayer created = set.AddGroup("Group " + (set.layers.Count + 1));
                CompleteLayerCreationUndo(created);
                SyncActiveLayerSelection(set);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope((uint)set.activeLayerIndex >= (uint)set.layers.Count))
            {
                if (GUILayout.Button("Duplicate"))
                {
                    DuplicateLayerWithHistory(set, set.activeLayerIndex);
                    SyncActiveLayerSelection(set);
                }
                using (new EditorGUI.DisabledScope(set.activeLayerIndex <= 0))
                    if (GUILayout.Button("Merge Down"))
                    {
                        MergeLayerWithHistory(set, set.activeLayerIndex);
                        SyncActiveLayerSelection(set);
                    }
            }
            GUILayout.EndHorizontal();

            performanceExpanded = EditorGUILayout.Foldout(performanceExpanded, "Performance & Memory", true);
            if (performanceExpanded)
            {
                EditorGUI.indentLevel++;
                historyBudgetMB = EditorGUILayout.IntSlider("Undo Budget (MB)", historyBudgetMB, 16, 1024);
                coverageBudgetMB = EditorGUILayout.IntSlider("Stroke Budget (MB)", coverageBudgetMB, 16, 512);
                controller.Painting.History.MemoryBudgetBytes = historyBudgetMB * 1024L * 1024L;
                controller.Painting.CoverageMemoryBudgetBytes = coverageBudgetMB * 1024L * 1024L;
                TexturePaintPerformanceMetrics metrics = controller.Painting.Performance;
                EditorGUILayout.LabelField("Preview p95", metrics.PreviewP95Milliseconds.ToString("0.00") + " ms");
                EditorGUILayout.LabelField("Preview maximum", metrics.MaximumPreviewMilliseconds.ToString("0.00") + " ms");
                EditorGUILayout.LabelField("Undo memory", EditorUtility.FormatBytes(controller.Painting.History.EstimatedMemoryBytes));
                EditorGUILayout.LabelField("Active stroke memory", EditorUtility.FormatBytes(controller.Painting.ActiveCoverageMemoryBytes));
                EditorGUILayout.LabelField("Compute / CPU", metrics.computeDispatches + " / " + metrics.cpuFallbacks);
                EditorGUILayout.LabelField("Geometry masks built", metrics.geometryMaskBuilds.ToString());
                if (GUILayout.Button("Reset Performance Counters")) metrics.Reset();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Capture Resource Baseline"))
                {
                    resourceBaseline = TexturePaintResourceDiagnostics.Capture();
                    hasResourceBaseline = true;
                    resourceCheckResult = "Baseline captured";
                }
                using (new EditorGUI.DisabledScope(!hasResourceBaseline))
                if (GUILayout.Button("Check Resource Delta"))
                {
                    TexturePaintResourceSnapshot current = TexturePaintResourceDiagnostics.Capture();
                    resourceCheckResult = "RenderTexture delta " + current.RenderTextureDelta(resourceBaseline) +
                        ", Texture2D delta " + current.TextureDelta(resourceBaseline);
                }
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(resourceCheckResult)) EditorGUILayout.HelpBox(resourceCheckResult, MessageType.None);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(5f);
            if (GUILayout.Button("Create Spline Layer"))
            {
                CreateSplineLayerWithUndo(set);
            }
            splineMode = TryGetActivePathLayer(set, out TexturePaintLayer toolsPathLayer) &&
                toolsPathLayer.spline?.worldSpace == true;
            using (new EditorGUI.DisabledScope(true))
                GUILayout.Toggle(TryGetActivePathLayer(set, out _), "Spline Authoring", "Button");
            if (TryGetActivePathLayer(set, out _))
            {
                DrawSplineSpaceProperty(set);
                EditorGUILayout.HelpBox(spline.worldSpace
                    ? "Edit this path only in the 3D Scene view. Its texture destinations are resolved from the surface path."
                    : "Edit this path only in the 2D view. It rasterizes directly in texture space and never projects through the model.",
                    MessageType.None);
                EditorGUI.BeginChangeCheck();
                bool useBezier = EditorGUILayout.Toggle("Bezier Curves", spline.useBezier);
                bool closed = EditorGUILayout.Toggle("Closed Loop", spline.closed);
                bool showControls = EditorGUILayout.Toggle("Show Controls", spline.showControls);
                EditorGUILayout.LabelField("Tangents", "Per-point Corner / Smooth / Broken / Custom / Straight");
                TexturePaintPathMode nextPathMode = (TexturePaintPathMode)EditorGUILayout.EnumPopup("Path Mode", pathMode);
                TexturePaintPathOrientation nextOrientation;
                using (new EditorGUI.DisabledScope(nextPathMode == TexturePaintPathMode.Ribbon))
                    nextOrientation = (TexturePaintPathOrientation)EditorGUILayout.EnumPopup("Orientation",
                        nextPathMode == TexturePaintPathMode.Ribbon
                            ? TexturePaintPathOrientation.FollowPath : pathOrientation);
                if (nextPathMode == TexturePaintPathMode.Ribbon)
                    nextOrientation = TexturePaintPathOrientation.FollowPath;
                TexturePaintPathCap nextStartCap = pathStartCap;
                TexturePaintPathCap nextEndCap = pathEndCap;
                using (new EditorGUI.DisabledScope(nextPathMode == TexturePaintPathMode.Ribbon))
                {
                    nextStartCap = (TexturePaintPathCap)EditorGUILayout.EnumPopup("Start Cap", pathStartCap);
                    nextEndCap = (TexturePaintPathCap)EditorGUILayout.EnumPopup("End Cap", pathEndCap);
                }
                int nextRadialSymmetry = EditorGUILayout.IntSlider("Radial Symmetry", radialSymmetry, 1, 16);
                if (EditorGUI.EndChangeCheck())
                {
                    BeginLightweightPathUndo(set, "Edit Path Parameters");
                    spline.useBezier = useBezier; spline.closed = closed; spline.showControls = showControls;
                    pathMode = nextPathMode; pathOrientation = nextOrientation;
                    pathStartCap = nextStartCap; pathEndCap = nextEndCap; radialSymmetry = nextRadialSymmetry;
                    CompleteLightweightPathEdit(set, false);
                }
                DrawPathStampSpacingControl();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply Spline")) ApplySpline();
                if (GUILayout.Button("Clear") && spline != null)
                {
                    BeginLightweightPathUndo(set, "Clear Spline");
                    spline.Clear(); selectedSplinePoint = -1;
                    CompleteLightweightPathEdit(set, false);
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(selectedSplinePoint < 0 || selectedSplinePoint >= spline.PointCount))
                {
                    if (GUILayout.Button("Insert Point"))
                    {
                        BeginLightweightPathUndo(set, "Insert Spline Point");
                        selectedSplinePoint = spline.InsertPointAfter(selectedSplinePoint);
                        UpdateSplineAnchorFromCurrentDomain(set, spline, selectedSplinePoint);
                        CompleteLightweightPathEdit(set, false);
                    }
                    if (GUILayout.Button("Delete Point")) DeleteSelectedSplinePoints(set);
                }
                if (GUILayout.Button("Reverse"))
                {
                    BeginLightweightPathUndo(set, "Reverse Spline");
                    spline.Reverse();
                    if (selectedSplinePoint >= 0) selectedSplinePoint = spline.PointCount - 1 - selectedSplinePoint;
                    CompleteLightweightPathEdit(set, false);
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Select All Points"))
                {
                    selectedSplinePoints ??= new HashSet<int>();
                    selectedSplinePoints.Clear();
                    for (int point = 0; point < spline.PointCount; point++) selectedSplinePoints.Add(point);
                    selectedSplinePoint = spline.PointCount > 0 ? 0 : -1;
                }
                if (GUILayout.Button("Copy Path")) splineClipboard = JsonUtility.ToJson(spline);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(splineClipboard)))
                    if (GUILayout.Button("Paste Path"))
                    {
                        BeginLayerCreationUndo("Paste Texture Path");
                        TexturePaintLayer pasted = set.AddSplineLayer("Pasted Path");
                        pasted.spline = JsonUtility.FromJson<TexturePaintSpline>(splineClipboard);
                        pathMode = TexturePaintPathMode.Ribbon;
                        pasted.splineSettings = CreateSplineSettings();
                        spline = pasted.spline;
                        selectedSplinePoint = -1;
                        CompleteLayerCreationUndo(pasted);
                    }
                GUILayout.EndHorizontal();
                if (selectedSplinePoint >= 0 && selectedSplinePoint < spline.PointCount)
                {
                    spline.EnsureControlPoints();
                    EditorGUILayout.LabelField("Selected Point Dynamics", EditorStyles.boldLabel);
                    if (GUILayout.Button(new GUIContent("Straight Handles", "Force the selected point handles onto straight, linear segments")))
                        StraightenSelectedSplinePoints(set);
                    EditorGUI.BeginChangeCheck();
                    TexturePaintTangentMode tangentMode = (TexturePaintTangentMode)EditorGUILayout.EnumPopup("Tangent", spline.tangentModes[selectedSplinePoint]);
                    float pointPressure = EditorGUILayout.Slider("Pressure", spline.pressures[selectedSplinePoint], 0f, 1f);
                    float pointWidthPercent = EditorGUILayout.Slider(new GUIContent("Width (%)", "Brush width at this point as a percentage of the path width"),
                        spline.widths[selectedSplinePoint] * 100f, 5f, 400f);
                    float pointWidth = pointWidthPercent * 0.01f;
                    float pointFlow = EditorGUILayout.Slider("Flow", spline.flows[selectedSplinePoint], 0f, 2f);
                    float pointRoll = EditorGUILayout.Slider("Roll", spline.rolls[selectedSplinePoint], -180f, 180f);
                    float pointOffset = spline.offsets[selectedSplinePoint];
                    using (new EditorGUI.DisabledScope(!spline.worldSpace))
                        pointOffset = EditorGUILayout.Slider("Surface Offset", pointOffset, -0.1f, 0.1f);
                    Color pointColor = EditorGUILayout.ColorField("Color", spline.colors[selectedSplinePoint]);
                    IReadOnlyCollection<int> selected = selectedSplinePoints != null && selectedSplinePoints.Count > 0
                        ? selectedSplinePoints : new[] { selectedSplinePoint };
                    if (EditorGUI.EndChangeCheck())
                    {
                        BeginLightweightPathUndo(set, "Edit Path Point Dynamics");
                        foreach (int point in selected)
                        {
                            if ((uint)point >= (uint)spline.PointCount) continue;
                            spline.SetTangentMode(point, tangentMode);
                            spline.pressures[point] = pointPressure; spline.widths[point] = pointWidth;
                            spline.flows[point] = pointFlow; spline.rolls[point] = pointRoll;
                            spline.offsets[point] = pointOffset; spline.colors[point] = pointColor;
                        }
                        CompleteLightweightPathEdit(set, false);
                    }
                }
            }

            GUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!CanUndoLightweight && !controller.Painting.History.CanUndo &&
                !controller.Plugins.CanUndo)) if (GUILayout.Button("Undo")) PerformWorkspaceUndo();
            using (new EditorGUI.DisabledScope(!CanRedoLightweight && !controller.Painting.History.CanRedo &&
                !controller.Plugins.CanRedo)) if (GUILayout.Button("Redo")) PerformWorkspaceRedo();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Brush Library")) BrushEditor.Open(currentBrushLibrary);
            if (GUILayout.Button("Plugins")) PluginManagerWindow.Open(controller);
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(5f);
            if (GUILayout.Button("Export Textures & UMA Assets..."))
            {
                TexturePaintExportWindow.Open(controller, avatar, set, BuildState(), document);
            }
            if (GUILayout.Button(IsDocumentTemporary ? "Save Document As..." : "Save Document")) SaveWorkspace();
            if (GUILayout.Button(new GUIContent("Clear All Overlay Painting...",
                "Restore every slot to its source textures and remove all layers, paths, masks, and paint history.")))
                ClearAllTexturePaintData(true);
            GUILayout.EndScrollView();
            HandlePathRenderParameterChanges(hadPathRenderState, pathSetBefore, pathLayerBefore,
                pathSettingsBefore, pathSignatureBefore);
            if (GUI.changed && !changedBefore) MarkDocumentDirty();
        }

        private void DrawSplineSpaceProperty(TextureSet set)
        {
            if (spline == null) return;
            int currentSpace = spline.worldSpace ? (int)SplineEditingSpace.Surface3D :
                (int)SplineEditingSpace.Texture2D;
            int nextSpace = EditorGUILayout.Popup(new GUIContent("Spline Space",
                    "2D edits and rasterizes only in normalized texture space. 3D edits in the Scene view and projects the path onto the model."),
                currentSpace, new[] { "2D Texture", "3D Surface" });
            if (nextSpace == currentSpace) return;
            bool worldSpace = nextSpace == (int)SplineEditingSpace.Surface3D;
            if (worldSpace && !CanConvertSplineToSurface(set, spline))
            {
                ShowWorkspaceStatus("The 2D path cannot be converted because one or more points are outside this surface's UVs.");
                return;
            }

            BeginLightweightPathUndo(set, "Change Spline Space");
            ConvertSplineSpace(set, spline, worldSpace);
            selectedSplinePoint = -1;
            selectedSplinePoints?.Clear();
            uvDraggingSplinePoint = -1;
            uvDraggingSplineHandle = UVSplineHandleKind.None;
            uvSplineHandleUndoStarted = false;
            ReleaseSplineHandleCapture(false, false);
            splineDisplayCache?.Remove(spline);
            CompleteLightweightPathEdit(set, false);
            SceneView.RepaintAll();
            TexturePaintUVWindow.RepaintOpenWindows();
            ShowWorkspaceStatus(worldSpace
                ? "Spline is now 3D-only; edit it in the Scene view."
                : "Spline is now 2D-only; edit it in the 2D view.");
        }

        private static bool CanConvertSplineToSurface(TextureSet set, TexturePaintSpline targetSpline)
        {
            if (set?.surface == null || targetSpline == null) return false;
            targetSpline.EnsureControlPoints();
            for (int point = 0; point < targetSpline.PointCount; point++)
            {
                int preferred = point < targetSpline.triangleIndices.Count
                    ? targetSpline.triangleIndices[point] : -1;
                if (!set.surface.TryUVToWorld(targetSpline.uvPoints[point], preferred,
                    out _, out _, out preferred, out _)) return false;
                if (!set.surface.TryUVToWorld(targetSpline.uvInControls[point], preferred,
                    out _, out _, out _, out _)) return false;
                if (!set.surface.TryUVToWorld(targetSpline.uvOutControls[point], preferred,
                    out _, out _, out _, out _)) return false;
            }
            return true;
        }

        private static void ConvertSplineSpace(TextureSet set, TexturePaintSpline targetSpline,
            bool worldSpace)
        {
            if (targetSpline == null || targetSpline.worldSpace == worldSpace) return;
            targetSpline.EnsureControlPoints();
            if (!worldSpace)
            {
                targetSpline.worldSpace = false;
                for (int point = 0; point < targetSpline.PointCount; point++)
                    NormalizeTwoDimensionalSplinePoint(set, targetSpline, point);
            }
            else
            {
                // Keep the spline in its UV domain while deriving the complete surface model.
                // Setting worldSpace early would make the control conversion skip itself.
                targetSpline.worldSpace = false;
                for (int point = 0; point < targetSpline.PointCount; point++)
                {
                    UpdateSplineAnchorFromUV(set, targetSpline, point);
                    ProjectSplineControlToSurface(set, targetSpline, point, true);
                    ProjectSplineControlToSurface(set, targetSpline, point, false);
                }
                targetSpline.worldSpace = true;
            }
            targetSpline.worldCurveVersion = TexturePaintSpline.CurrentWorldCurveVersion;
        }

        private static void NormalizeTwoDimensionalSplinePoint(TextureSet set,
            TexturePaintSpline targetSpline, int point)
        {
            if (targetSpline == null || (uint)point >= (uint)targetSpline.PointCount) return;
            targetSpline.EnsureControlPoints();
            Vector2 uv = targetSpline.uvPoints[point];
            Vector2 incoming = targetSpline.uvInControls[point];
            Vector2 outgoing = targetSpline.uvOutControls[point];
            targetSpline.worldPoints[point] = new Vector3(uv.x, uv.y, 0f);
            targetSpline.worldInControls[point] = new Vector3(incoming.x, incoming.y, 0f);
            targetSpline.worldOutControls[point] = new Vector3(outgoing.x, outgoing.y, 0f);
            targetSpline.worldNormals[point] = Vector3.forward;
            targetSpline.surfaceIndices[point] = set?.surface?.index ?? 0;
            targetSpline.triangleIndices[point] = -1;
            targetSpline.anchors[point] = new TexturePaintSurfaceAnchor
            {
                surfaceId = set?.persistentId,
                surfaceIndex = set?.surface?.index ?? 0,
                triangleIndex = -1,
                normal = Vector3.forward
            };
        }

        private void DrawSlotTargetSelector(IReadOnlyList<TextureSet> sets)
        {
            IReadOnlyList<TexturePaintLogicalTarget> targets = controller.LogicalTargets?.Targets;
            slotTargetsExpanded = EditorGUILayout.Foldout(slotTargetsExpanded, "Paint Target", true);
            if (slotTargetsExpanded)
            {
                if (targets == null || targets.Count == 0)
                {
                    EditorGUILayout.HelpBox("No logical paint targets were reconstructed.", MessageType.Warning);
                }
                else for (int i = 0; i < targets.Count; i++)
                {
                    TexturePaintLogicalTarget target = targets[i];
                    string type = target.isUdim ? $"UDIM · {target.members.Count} tiles" : "Single slot";
                    bool selected = string.Equals(selectedTargetId, target.id, StringComparison.Ordinal);
                    if (GUILayout.Toggle(selected, $"{target.displayName}    {type}", "Button") && !selected)
                        SelectLogicalTarget(target, sets, true);
                    if (!selected || !target.isUdim) continue;
                    for (int memberIndex = 0; memberIndex < target.members.Count; memberIndex++)
                    {
                        TexturePaintLogicalTargetMember member = target.members[memberIndex];
                        EditorGUILayout.LabelField($"    {member.udimTileNumber}  {member.slotName}", EditorStyles.miniLabel);
                    }
                }
            }

            if (selectedSlots.Count == 0)
                EditorGUILayout.HelpBox("Select a paint target to enable painting.", MessageType.Warning);
        }

        private void DrawOverlayDataSelector(IReadOnlyList<TextureSet> sets)
        {
            List<TextureSourceBinding> overlays = new List<TextureSourceBinding>();
            List<string> labels = new List<string> { "None" };
            List<OverlayDataAsset> seen = new List<OverlayDataAsset>();
            for (int setIndex = 0; setIndex < sets.Count; setIndex++)
            {
                TextureSet set = sets[setIndex];
                if (!IsSurfaceSelected(set.surface)) continue;
                for (int sourceIndex = 0; sourceIndex < set.sources.Count; sourceIndex++)
                {
                    TextureSourceBinding source = set.sources[sourceIndex];
                    OverlayDataAsset asset = source.overlay?.asset;
                    if (asset == null || seen.Contains(asset) || !BindingMatchesSelectedSlots(source)) continue;
                    seen.Add(asset);
                    overlays.Add(source);
                    labels.Add(source.slotNames.Count > 0
                        ? $"{source.name} ({string.Join(", ", source.slotNames)})"
                        : source.name);
                }
            }
            int current = 0;
            for (int i = 0; i < overlays.Count; i++)
            {
                if (overlays[i].overlay.asset != paintSourceOverlay) continue;
                current = i + 1;
                break;
            }
            if (current == 0 && paintSourceOverlay != null) paintSourceOverlay = null;
            int next = EditorGUILayout.Popup("OverlayData", current, labels.ToArray());
            paintSourceOverlay = next > 0 && next <= overlays.Count ? overlays[next - 1].overlay.asset : null;
        }

        private bool BindingMatchesSelectedSlots(TextureSourceBinding binding)
        {
            if (binding.slotNames.Count == 0) return true;
            for (int i = 0; i < binding.slotNames.Count; i++)
                if (selectedSlots.Contains(binding.slotNames[i])) return true;
            return false;
        }

        private static List<string> CollectSlotNames(IReadOnlyList<TextureSet> sets)
        {
            List<string> result = new List<string>();
            for (int setIndex = 0; setIndex < sets.Count; setIndex++)
            {
                ReconstructedSurface surface = sets[setIndex].surface;
                if (surface == null) continue;
                for (int slotIndex = 0; slotIndex < surface.slotNames.Count; slotIndex++)
                {
                    string slot = surface.slotNames[slotIndex];
                    if (!string.IsNullOrEmpty(slot) && !result.Contains(slot)) result.Add(slot);
                }
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private void EnsureInitialSlotSelection()
        {
            if (selectedSlots == null) selectedSlots = new List<string>();
            IReadOnlyList<TextureSet> sets = controller.Textures.Sets;
            TexturePaintLogicalTargetCatalog catalog = controller.LogicalTargets;
            TexturePaintLogicalTarget target = catalog?.FindById(selectedTargetId);
            if (target == null)
            {
                for (int i = 0; i < selectedSlots.Count && target == null; i++) target = catalog?.FindBySlot(selectedSlots[i]);
            }
            if (target == null && sets.Count > 0)
            {
                TextureSet preferred = sets[Mathf.Clamp(selectedSurface, 0, sets.Count - 1)];
                if (preferred.surface.slotNames.Count > 0) target = catalog?.FindBySlot(preferred.surface.slotNames[0]);
            }
            if (target == null && catalog?.Targets.Count > 0) target = catalog.Targets[0];
            if (target != null) SelectLogicalTarget(target, sets);
            else { selectedTargetId = null; selectedSlots.Clear(); }
            EnsureActiveSurfaceMatchesSlots(sets);
        }

        private TexturePaintLogicalTarget ActiveLogicalTarget => controller?.LogicalTargets?.FindById(selectedTargetId);

        private void SelectLogicalTarget(TexturePaintLogicalTarget target, IReadOnlyList<TextureSet> sets = null,
            bool frameSceneView = false)
        {
            selectedTargetId = target?.id;
            selectedSlots ??= new List<string>();
            if (target != null) target.ExpandSlotNames(selectedSlots); else selectedSlots.Clear();
            EnsureActiveSurfaceMatchesSlots(sets ?? controller?.Textures?.Sets);
            ApplyWorkspaceDisplay();
            if (frameSceneView) FrameActiveTarget();
        }

        private void EnsureActiveSurfaceMatchesSlots(IReadOnlyList<TextureSet> sets)
        {
            if (sets == null || sets.Count == 0) return;
            int current = Mathf.Clamp(selectedSurface, 0, sets.Count - 1);
            if (IsSurfaceSelected(sets[current].surface)) { selectedSurface = current; return; }
            for (int i = 0; i < sets.Count; i++)
            {
                if (!IsSurfaceSelected(sets[i].surface)) continue;
                selectedSurface = i;
                SyncActiveLayerSelection(sets[i]);
                return;
            }
        }

        private bool IsSurfaceSelected(ReconstructedSurface surface)
        {
            if (surface == null || selectedSlots == null || selectedSlots.Count == 0) return false;
            for (int i = 0; i < selectedSlots.Count; i++) if (surface.ContainsSlot(selectedSlots[i])) return true;
            return false;
        }

        private bool IsSelectedSlotHit(ReconstructedSurface surface, int triangleIndex)
        {
            if (!IsSurfaceSelected(surface)) return false;
            string triangleSlot = surface.GetTriangleSlotName(triangleIndex);
            return string.IsNullOrEmpty(triangleSlot) || selectedSlots.Contains(triangleSlot);
        }

        private TextureSet ActivateSurfaceForSpline(ReconstructedSurface surface)
        {
            if (surface == null) return null;
            IReadOnlyList<TextureSet> sets = controller.Textures.Sets;
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i].surface != surface) continue;
                if (selectedSurface != i)
                {
                    selectedSurface = i;
                    SyncActiveLayerSelection(sets[i]);
                }
                return sets[i];
            }
            return null;
        }

        private void FollowPaintedSurface(TextureSet set)
        {
            if (set?.surface == null) return;
            IReadOnlyList<TextureSet> sets = controller.Textures.Sets;
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i] != set || selectedSurface == i) continue;
                selectedSurface = i;
                spline = (uint)set.activeLayerIndex < (uint)set.layers.Count ? set.layers[set.activeLayerIndex].spline : null;
                selectedSplinePoint = -1;
                TexturePaintDockWindow.RepaintOpenWindows();
                TexturePaintUVWindow.RepaintOpenWindows();
                return;
            }
        }

        // Presets are templates. Painting always edits the transient copy so changing controls in
        // the workspace cannot silently modify a shared project asset.
        private BrushPreset ActiveBrush => transientBrush;

        private void SelectBrushPreset(BrushPreset preset)
        {
            brush = preset;
            if (preset != null && transientBrush != null)
                transientBrush.CopyPaintSettingsFrom(preset);
        }

        private void UpdateSelectedBrushAsset()
        {
            if (brush == null || transientBrush == null) return;
            if (!EditorUtility.DisplayDialog("Update Brush Asset?",
                    $"This will overwrite the paint settings stored in '{brush.name}'. Future uses of this preset will start with the updated settings. Existing layer snapshots will remain unchanged.",
                    "Update Brush Asset", "Cancel")) return;

            Undo.RecordObject(brush, "Update Overlay Painter Brush Asset");
            brush.CopyPaintSettingsFrom(transientBrush);
            EditorUtility.SetDirty(brush);
            AssetDatabase.SaveAssetIfDirty(brush);
            ShowWorkspaceStatus("Updated brush asset: " + brush.name);
        }

        internal void SetCurrentBrushLibrary(BrushLibrary library)
        {
            currentBrushLibrary = library;
        }

        private void DrawBrushAssetActions()
        {
            BrushLibrary nextLibrary = (BrushLibrary)EditorGUILayout.ObjectField(
                new GUIContent("Brush Library", "New brushes are added to this library and saved beside its asset."),
                currentBrushLibrary, typeof(BrushLibrary), false);
            if (nextLibrary != currentBrushLibrary) SetCurrentBrushLibrary(nextLibrary);

            if (GUILayout.Button("Save Current Settings to New Brush..."))
                PromptToSaveCurrentBrush();
            if (brush != null && GUILayout.Button("Update Brush Asset with Current Settings..."))
                UpdateSelectedBrushAsset();
        }

        private void PromptToSaveCurrentBrush()
        {
            if (currentBrushLibrary == null)
            {
                EditorUtility.DisplayDialog("No Current Brush Library",
                    "Assign a Brush Library first. The new brush asset will be added to that library and saved in the same folder.",
                    "OK");
                return;
            }

            string defaultName = brush != null ? brush.name + " Copy" : "New Overlay Painter Brush";
            BrushNamePromptWindow.Show(defaultName, SaveCurrentBrushAsNewAsset);
        }

        private void SaveCurrentBrushAsNewAsset(string requestedName)
        {
            if (this == null || currentBrushLibrary == null || transientBrush == null) return;
            BrushPreset created = CreateBrushAssetFromCurrentSettings(currentBrushLibrary,
                transientBrush, requestedName, out string assetPath, out string error);
            if (created == null)
            {
                EditorUtility.DisplayDialog("Unable to Save Brush", error, "OK");
                return;
            }

            int index = currentBrushLibrary.Brushes.Count - 1;
            RecordBrushLibraryChange(currentBrushLibrary, created, index, true);
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (!brushOrderGuids.Contains(guid)) brushOrderGuids.Add(guid);
            recentBrushGuids.Remove(guid);
            recentBrushGuids.Insert(0, guid);
            workspaceBrushesDirty = true;
            assetShelfFolder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? "Assets";
            SelectBrushPreset(created);
            workspaceRenameBrush = created.name;
            ShowWorkspaceStatus("Saved brush: " + assetPath);
            EditorGUIUtility.PingObject(created);
            EditorUtility.DisplayDialog("Brush Saved",
                $"'{created.name}' was added to '{currentBrushLibrary.name}' and saved at:\n\n{assetPath}",
                "OK");
        }

        internal static BrushPreset CreateBrushAssetFromCurrentSettings(BrushLibrary library,
            BrushPreset currentSettings, string requestedName, out string assetPath, out string error)
        {
            assetPath = null;
            error = null;
            if (library == null)
            {
                error = "A Brush Library is required.";
                return null;
            }
            if (currentSettings == null)
            {
                error = "There are no current brush settings to save.";
                return null;
            }

            string libraryPath = AssetDatabase.GetAssetPath(library);
            string folder = Path.GetDirectoryName(libraryPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(libraryPath) || string.IsNullOrEmpty(folder) ||
                !AssetDatabase.IsValidFolder(folder))
            {
                error = "The current Brush Library must be a saved project asset.";
                return null;
            }

            string brushName = SanitizeBrushAssetName(requestedName);
            assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + brushName + ".asset");
            BrushPreset preset = CreateInstance<BrushPreset>();
            preset.name = brushName;
            preset.CopyPaintSettingsFrom(currentSettings);
            try
            {
                AssetDatabase.CreateAsset(preset, assetPath);
                Undo.RegisterCreatedObjectUndo(preset, "Create Overlay Painter Brush");
                library.Add(preset);
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets();
                return preset;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (preset != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(preset)))
                    DestroyImmediate(preset);
                assetPath = null;
                return null;
            }
        }

        private static string SanitizeBrushAssetName(string requestedName)
        {
            string value = string.IsNullOrWhiteSpace(requestedName)
                ? "New Overlay Painter Brush" : requestedName.Trim();
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] characters = value.ToCharArray();
            for (int i = 0; i < characters.Length; i++)
                if (Array.IndexOf(invalid, characters[i]) >= 0 || characters[i] == '/' || characters[i] == '\\')
                    characters[i] = '_';
            value = new string(characters).Trim().TrimEnd('.');
            return string.IsNullOrEmpty(value) ? "New Overlay Painter Brush" : value;
        }

        private float DrawBrushRotation(float rotation)
        {
            bool ribbonLayer = pathMode == TexturePaintPathMode.Ribbon &&
                TryGetActivePathLayer(ActiveTextureSet, out _);
            if (!ribbonLayer)
                return EditorGUILayout.Slider("Rotation", rotation, -180f, 180f);
            int snapped = SnapRibbonRotation(rotation);
            return EditorGUILayout.IntPopup("Ribbon Rotation", snapped,
                RibbonRotationLabels, RibbonRotationValues);
        }

        internal static int SnapRibbonRotation(float rotation)
        {
            int nearest = RibbonRotationValues[0];
            float nearestDistance = Mathf.Abs(rotation - nearest);
            for (int i = 1; i < RibbonRotationValues.Length; i++)
            {
                float distance = Mathf.Abs(rotation - RibbonRotationValues[i]);
                if (distance >= nearestDistance) continue;
                nearest = RibbonRotationValues[i];
                nearestDistance = distance;
            }
            return nearest;
        }
        private BrushPreset FootprintBrush => applyingSpline && activeSplineBrush != null
            ? activeSplineBrush : ActiveBrush;

        private void SyncActiveLayerSelection(TextureSet set)
        {
            if (syncingLogicalLayerSelection) return;
            syncingLogicalLayerSelection = true;
            try
            {
                if (strokeActive && !CanStartFreehandPaint(set)) EndPaint();
                if (set != null && (uint)set.activeLayerIndex < (uint)set.layers.Count)
                {
                    TexturePaintLayer activeLayer = set.layers[set.activeLayerIndex];
                    TexturePaintLogicalTarget target = controller?.LogicalLayers?.FindTarget(set);
                    if (target != null && !suppressLogicalLayerRepair)
                    {
                        string oldLogicalId = activeLayer.logicalLayerId;
                        string oldTargetId = activeLayer.paintTargetId;
                        var repaired = new List<TexturePaintLogicalLayerMember>();
                        if (!controller.LogicalLayers.LinkAndRepair(target, set, activeLayer, repaired,
                            out TexturePaintLogicalLayerBinding binding))
                        {
                            ShowWorkspaceStatus(binding?.error ?? "The selected logical layer could not be repaired.");
                        }
                        else
                        {
                            controller.LogicalLayers.Activate(binding);
                            if (repaired.Count > 0 || string.IsNullOrEmpty(oldLogicalId) ||
                                !string.Equals(oldTargetId, target.id, StringComparison.Ordinal))
                            {
                                string newLogicalId = activeLayer.logicalLayerId;
                                string newTargetId = activeLayer.paintTargetId;
                                var locations = new List<LayerLocation>();
                                for (int i = 0; i < repaired.Count; i++)
                                    locations.Add(new LayerLocation { set = repaired[i].textureSet, layer = repaired[i].layer,
                                        index = repaired[i].textureSet.layers.IndexOf(repaired[i].layer) });
                                PushLightweightCommand("Repair Logical Texture Layer",
                                    () =>
                                    {
                                        DetachLayerLocations(locations);
                                        activeLayer.logicalLayerId = oldLogicalId;
                                        activeLayer.paintTargetId = oldTargetId;
                                    },
                                    () =>
                                    {
                                        activeLayer.logicalLayerId = newLogicalId;
                                        activeLayer.paintTargetId = newTargetId;
                                        AttachLayerLocations(locations);
                                    },
                                    () =>
                                    {
                                        for (int i = 0; i < locations.Count; i++)
                                            DisposeLayerIfDetached(locations[i].set, locations[i].layer);
                                    });
                                MarkDocumentDirty();
                                ShowWorkspaceStatus($"Repaired logical layer across {binding.members.Count} target member(s)");
                            }
                        }
                    }
                    else if (target != null && !string.IsNullOrEmpty(activeLayer.logicalLayerId))
                    {
                        TexturePaintLogicalLayerBinding binding = controller.LogicalLayers.Resolve(target,
                            activeLayer.logicalLayerId);
                        if (binding.complete) controller.LogicalLayers.Activate(binding);
                    }
                    activeLayer.NormalizeKindPayload();
                    if (activeLayer.kind == TexturePaintLayerKind.Paint || activeLayer.IsSplineLayer)
                        sourceMode = TexturePaintSourceMode.SourceOverlay;
                    spline = activeLayer.spline;
                    splineMode = activeLayer.IsSplineLayer && activeLayer.spline?.worldSpace == true;
                    if (activeLayer.kind == TexturePaintLayerKind.Fill)
                    {
                        selectedChannel = activeLayer.fillChannel;
                        paintSource = activeLayer.fillSettings.source;
                        normalConvention = activeLayer.fillSettings.normalConvention;
                        RestorePaintSource(activeLayer.fillSettings.sourceTexture,
                            activeLayer.fillSettings.sourceSprite);
                        paintSourceOverlay = activeLayer.fillSettings.sourceOverlay;
                        paintColor = activeLayer.fillSettings.color;
                    }
                    else if (activeLayer.kind == TexturePaintLayerKind.Paint)
                    {
                        activeLayer.paintSettings ??= CreatePaintLayerSettings();
                        RestorePaintLayerSettings(activeLayer.paintSettings);
                    }
                    if (activeLayer.IsSplineLayer) uvColorSamplerArmed = false;
                    if (activeLayer.IsSplineLayer && activeLayer.splineSettings != null)
                        RestoreSplineSettings(activeLayer.splineSettings);
                }
                else
                {
                    spline = null;
                    splineMode = false;
                }
                if (layerMaskMode && (set == null || (uint)set.activeLayerIndex >= (uint)set.layers.Count ||
                    set.layers[set.activeLayerIndex]?.layerMask?.target == null))
                {
                    layerMaskMode = false;
                    soloLayerMask = false;
                }
                if (geometryFillMode != 0 && !CanStartFreehandPaint(set)) geometryFillMode = 0;
                selectedSplinePoint = -1;
                selectedSplinePoints?.Clear();
                uvDraggingSplinePoint = -1;
                uvDraggingSplineHandle = UVSplineHandleKind.None;
                uvSplineHandleUndoStarted = false;
                SceneView.RepaintAll();
            }
            finally { syncingLogicalLayerSelection = false; }
        }

        internal static bool IsActiveSplineAuthoringLayer(TextureSet set, int layerIndex)
        {
            return set != null && set.activeLayerIndex == layerIndex &&
                (uint)layerIndex < (uint)set.layers.Count && set.layers[layerIndex].IsSplineLayer;
        }

        private static bool IsActivePaintLayer(TextureSet set)
        {
            if (set == null || (uint)set.activeLayerIndex >= (uint)set.layers.Count) return false;
            TexturePaintLayer layer = set.layers[set.activeLayerIndex];
            return layer != null && layer.kind == TexturePaintLayerKind.Paint && !layer.IsSplineLayer;
        }

        private bool CanStartFreehandPaint(TextureSet set)
        {
            if (set == null) return false;
            if (IsLayerMaskMode(set))
            {
                TexturePaintLayer maskLayer = set.layers[set.activeLayerIndex];
                if (maskLayer?.layerMask?.target == null) return false;
                TexturePaintLogicalTarget maskTarget = controller?.LogicalLayers?.FindTarget(set);
                if (maskTarget == null || string.IsNullOrEmpty(maskLayer.logicalLayerId)) return true;
                TexturePaintLogicalLayerBinding maskBinding = controller.LogicalLayers.Resolve(maskTarget,
                    maskLayer.logicalLayerId);
                if (!maskBinding.complete) return false;
                for (int i = 0; i < maskBinding.members.Count; i++)
                    if (maskBinding.members[i].layer?.layerMask?.target == null) return false;
                return true;
            }
            TexturePaintLogicalTarget target = controller?.LogicalLayers?.FindTarget(set);
            if (target == null) return set.layers.Count == 0 || IsActivePaintLayer(set);
            List<TextureSet> sets = controller.LogicalLayers.GetTextureSets(target);
            bool allEmpty = sets.Count > 0;
            for (int i = 0; i < sets.Count; i++) allEmpty &= sets[i].layers.Count == 0;
            if (allEmpty) return true;
            if (!IsActivePaintLayer(set)) return false;
            TexturePaintLayer layer = set.layers[set.activeLayerIndex];
            TexturePaintLogicalLayerBinding binding = controller.LogicalLayers.Resolve(target, layer.logicalLayerId);
            return binding.complete && controller.LogicalLayers.ValidatePaintBinding(binding, selectedChannel, out _);
        }

        private void ShowPaintLayerRequiredStatus(TextureSet set)
        {
            if (layerMaskMode)
            {
                ShowWorkspaceStatus("Select a layer mask thumbnail before painting in Mask Mode");
                return;
            }
            string activeType = set != null && (uint)set.activeLayerIndex < (uint)set.layers.Count
                ? set.layers[set.activeLayerIndex].kind.ToString() : "no active";
            ShowWorkspaceStatus($"Freehand tools require a Paint layer ({activeType} layer selected)");
        }

        private void BeginPaint()
        {
            if (!IsSelectedSlotHit(hoverSurface, hoverHit.triangleIndex)) return;
            TextureSet set = controller.Textures.FindSet(hoverSurface.index);
            if (set == null) return;
            BeginPaintAt(set, MakeSample(hoverSurface, hoverHit, hoverHit.textureCoord));
        }

        private void BeginPaintAt(TextureSet set, StrokeSample initialSample, bool applyInitialStroke = true,
            bool directUV = false)
        {
            if (set == null || !IsSurfaceSelected(set.surface)) return;
            if (IsLayerMaskMode(set))
            {
                BeginLayerMaskPaintAt(set, initialSample, applyInitialStroke, directUV);
                return;
            }
            TexturePaintLogicalTarget target = ActiveLogicalTarget ?? controller.LogicalLayers?.FindTarget(set);
            if (target == null || controller.LogicalLayers.FindMember(target, set) == null)
            { ShowWorkspaceStatus("The painted surface is not part of the selected paint target."); return; }
            List<TextureSet> targetSets = controller.LogicalLayers.GetTextureSets(target);
            if (targetSets.Count == 0) { ShowWorkspaceStatus("The selected paint target has no texture sets."); return; }
            Dictionary<TextureSet, HashSet<TexturePaintLayer>> layerBaseline =
                new Dictionary<TextureSet, HashSet<TexturePaintLayer>>();
            for (int setIndex = 0; setIndex < targetSets.Count; setIndex++)
                layerBaseline[targetSets[setIndex]] = new HashSet<TexturePaintLayer>(targetSets[setIndex].layers);
            bool allEmpty = true;
            for (int i = 0; i < targetSets.Count; i++) allEmpty &= targetSets[i].layers.Count == 0;
            TexturePaintLayer activeLayer;
            if (allEmpty)
            {
                SetSelectedChannelAndRefreshSource(TexturePaintChannel.Albedo);
                activeLayer = set.AddLayer("Paint Layer 1");
                activeLayer.visible = true;
            }
            else if ((uint)set.activeLayerIndex < (uint)set.layers.Count) activeLayer = set.layers[set.activeLayerIndex];
            else { ShowPaintLayerRequiredStatus(set); return; }
            sourceMode = TexturePaintSourceMode.SourceOverlay;
            activeLayer.paintSettings ??= CreatePaintLayerSettings();
            activeLayer.paintSettings.destination = TexturePaintSourceMode.SourceOverlay;
            string previousLogicalId = activeLayer.logicalLayerId;
            string previousTargetId = activeLayer.paintTargetId;
            var repaired = new List<TexturePaintLogicalLayerMember>();
            if (!controller.LogicalLayers.LinkAndRepair(target, set, activeLayer, repaired,
                out TexturePaintLogicalLayerBinding binding))
            {
                RollbackLayersAddedSince(layerBaseline);
                activeLayer.logicalLayerId = previousLogicalId;
                activeLayer.paintTargetId = previousTargetId;
                ShowWorkspaceStatus(binding?.error ?? "The logical layer binding is invalid.");
                return;
            }
            controller.LogicalLayers.Activate(binding);
            if (!controller.LogicalLayers.ValidatePaintBinding(binding, selectedChannel, out string bindingError))
            {
                RollbackLayersAddedSince(layerBaseline);
                activeLayer.logicalLayerId = previousLogicalId;
                activeLayer.paintTargetId = previousTargetId;
                ShowWorkspaceStatus(bindingError);
                return;
            }
            FollowPaintedSurface(set);
            StrokeContext context = new StrokeContext
            {
                textures = set, geometrySelection = BuildGeometrySelection(), directUV = directUV,
                brush = ActiveBrush, tool = tool, channel = selectedChannel,
                mirrorEnabled = mirrorX || ActiveBrush.mirrorStroke, color = paintColor, strength = strength,
                limitStrokeCoverage = limitStrokeCoverage,
                pressureAffectsFlow = pressureAffectsFlow, pressureAffectsSize = pressureAffectsSize,
                projectionDepth = projectionDepth, normalAngleLimit = normalAngleLimit, paintBackfaces = paintBackfaces,
                paintSource = paintSource, sourceTexture = paintSourceTexture, sourceOverlay = paintSourceOverlay,
                sourceSprite = paintSourceSprite, normalConvention = normalConvention,
                modelToWorld = controller.Reconstruction.root.transform.localToWorldMatrix,
                cloneSourceUV = cloneSourceUV,
                historyGroupKey = "texture-paint-target-stroke:" + target.id + ":" + Guid.NewGuid().ToString("N"),
                pluginHost = controller.Plugins,
                brushPlugin = tool == TexturePaintTool.Plugin && controller.Plugins.Brushes.Count > 0
                    ? controller.Plugins.Brushes[Mathf.Clamp(selectedBrushPlugin, 0, controller.Plugins.Brushes.Count - 1)] : null
            };
            PopulateLayerChannelSources(context, activeLayer);
            if (context.brushPlugin != null) context.brushPluginParameters = controller.Plugins.GetParameters(context.brushPlugin);
            if ((tool == TexturePaintTool.Paint || tool == TexturePaintTool.Plugin) &&
                context.channelSources.Count == 0 && paintSource == TexturePaintBrushSource.Overlay &&
                !BuildMemberOverlayBindings(context, target, targetSets, out string overlayError))
            {
                RollbackLayersAddedSince(layerBaseline);
                activeLayer.logicalLayerId = previousLogicalId;
                activeLayer.paintTargetId = previousTargetId;
                ShowWorkspaceStatus(overlayError);
                return;
            }
            paintHistoryVersionAtStrokeStart = controller.Painting.History.CommitVersion;
            strokeTextureSets.Clear();
            strokeTextureSets.AddRange(targetSets);
            strokeActive = controller.Painting.BeginStroke(context, sourceMode, strokeTextureSets);
            if (!strokeActive)
            {
                directUVStroke = false;
                RollbackLayersAddedSince(layerBaseline);
                activeLayer.logicalLayerId = previousLogicalId;
                activeLayer.paintTargetId = previousTargetId;
                ShowWorkspaceStatus("The stroke could not start for the complete paint target.");
                return;
            }
            directUVStroke = directUV;
            strokeLogicalTarget = target;
            strokeCreatedLayers ??= new List<LayerLocation>();
            strokeCreatedLayers.Clear();
            foreach (KeyValuePair<TextureSet, HashSet<TexturePaintLayer>> pair in layerBaseline)
                for (int layerIndex = 0; layerIndex < pair.Key.layers.Count; layerIndex++)
                    if (!pair.Value.Contains(pair.Key.layers[layerIndex]))
                        strokeCreatedLayers.Add(new LayerLocation
                        {
                            set = pair.Key,
                            layer = pair.Key.layers[layerIndex],
                            index = layerIndex
                        });
            strokeSampler.Reset();
            ResetPaintRandomization();
            strokeSampler.Spacing = ActiveBrush.StampSpacing;
            strokeSampler.Stabilization = strokeStabilization;
            strokeSampler.DirectionSmoothing = directionSmoothing;
            previousContactSamples.Clear();
            initialFollowStampPending = ActiveBrush.alignToStroke && context.brushPlugin == null;
            hasInitialFollowStamp = false;
            initialFollowStamp = default;
            sampledStrokePoints.Clear();
            if (applyInitialStroke)
            {
                strokeSampler.Add(initialSample, sampledStrokePoints);
                ApplySampledStrokePoints();
            }
        }

        private void BeginLayerMaskPaintAt(TextureSet set, StrokeSample initialSample,
            bool applyInitialStroke, bool directUV)
        {
            if (set == null || (uint)set.activeLayerIndex >= (uint)set.layers.Count) return;
            TexturePaintLayer layer = set.layers[set.activeLayerIndex];
            if (layer?.layerMask?.target == null)
            { ShowWorkspaceStatus("The active layer has no editable mask."); return; }

            var targetSets = new List<TextureSet> { set };
            TexturePaintLogicalTarget target = controller?.LogicalLayers?.FindTarget(set);
            if (target != null && !string.IsNullOrEmpty(layer.logicalLayerId))
            {
                TexturePaintLogicalLayerBinding binding = controller.LogicalLayers.Resolve(target,
                    layer.logicalLayerId);
                if (!binding.complete)
                { ShowWorkspaceStatus(binding.error); return; }
                for (int i = 0; i < binding.members.Count; i++)
                    if (binding.members[i].layer?.layerMask?.target == null)
                    { ShowWorkspaceStatus("The logical layer mask is missing from one or more target members."); return; }
                controller.LogicalLayers.Activate(binding);
                targetSets.Clear();
                for (int i = 0; i < binding.members.Count; i++)
                    if (!targetSets.Contains(binding.members[i].textureSet))
                        targetSets.Add(binding.members[i].textureSet);
            }

            FollowPaintedSurface(set);
            layer.layerMask.NormalizePaintSource();
            float value = layer.layerMask.PaintValue;
            layerMaskPaintValue = value;
            StrokeContext context = new StrokeContext
            {
                textures = set,
                geometrySelection = BuildGeometrySelection(),
                directUV = directUV,
                editLayerMask = true,
                maskValue = value,
                brush = ActiveBrush,
                tool = tool == TexturePaintTool.NormalTouchup ? TexturePaintTool.Paint : tool,
                channel = TexturePaintChannel.Albedo,
                maskSourceChannel = TexturePaintChannel.Albedo,
                mirrorEnabled = mirrorX || ActiveBrush.mirrorStroke,
                color = new Color(value, value, value, 1f),
                strength = strength,
                limitStrokeCoverage = limitStrokeCoverage,
                pressureAffectsFlow = pressureAffectsFlow,
                pressureAffectsSize = pressureAffectsSize,
                projectionDepth = projectionDepth,
                normalAngleLimit = normalAngleLimit,
                paintBackfaces = paintBackfaces,
                paintSource = TexturePaintBrushSource.Color,
                sourceTexture = null,
                sourceSprite = null,
                sourceOverlay = null,
                sourceInvert = false,
                normalConvention = TexturePaintNormalConvention.OpenGL,
                modelToWorld = controller.Reconstruction.root.transform.localToWorldMatrix,
                cloneSourceUV = cloneSourceUV,
                historyGroupKey = "texture-paint-layer-mask-stroke:" +
                    (layer.logicalLayerId ?? layer.id) + ":" + Guid.NewGuid().ToString("N"),
                pluginHost = controller.Plugins,
                brushPlugin = tool == TexturePaintTool.Plugin && controller.Plugins.Brushes.Count > 0
                    ? controller.Plugins.Brushes[Mathf.Clamp(selectedBrushPlugin, 0,
                        controller.Plugins.Brushes.Count - 1)] : null
            };
            if (context.brushPlugin != null)
                context.brushPluginParameters = controller.Plugins.GetParameters(context.brushPlugin);

            paintHistoryVersionAtStrokeStart = controller.Painting.History.CommitVersion;
            strokeTextureSets.Clear();
            strokeTextureSets.AddRange(targetSets);
            strokeActive = controller.Painting.BeginStroke(context,
                TexturePaintSourceMode.SourceOverlay, strokeTextureSets);
            if (!strokeActive)
            { directUVStroke = false; ShowWorkspaceStatus("The layer-mask stroke could not start."); return; }
            directUVStroke = directUV;
            strokeLogicalTarget = target;
            strokeCreatedLayers ??= new List<LayerLocation>();
            strokeCreatedLayers.Clear();
            strokeSampler.Reset();
            ResetPaintRandomization();
            strokeSampler.Spacing = ActiveBrush.StampSpacing;
            strokeSampler.Stabilization = strokeStabilization;
            strokeSampler.DirectionSmoothing = directionSmoothing;
            previousContactSamples.Clear();
            initialFollowStampPending = ActiveBrush.alignToStroke && context.brushPlugin == null;
            hasInitialFollowStamp = false;
            initialFollowStamp = default;
            sampledStrokePoints.Clear();
            if (applyInitialStroke)
            {
                strokeSampler.Add(initialSample, sampledStrokePoints);
                ApplySampledStrokePoints();
            }
        }

        private static void RollbackLayersAddedSince(Dictionary<TextureSet, HashSet<TexturePaintLayer>> baseline)
        {
            foreach (KeyValuePair<TextureSet, HashSet<TexturePaintLayer>> pair in baseline)
            {
                for (int i = pair.Key.layers.Count - 1; i >= 0; i--)
                {
                    TexturePaintLayer layer = pair.Key.layers[i];
                    if (pair.Value.Contains(layer)) continue;
                    pair.Key.layers.RemoveAt(i);
                    layer.Dispose();
                }
                pair.Key.activeLayerIndex = Mathf.Clamp(pair.Key.activeLayerIndex, -1, pair.Key.layers.Count - 1);
                pair.Key.BindPreviewTextures();
            }
        }

        private bool BuildMemberOverlayBindings(StrokeContext context, TexturePaintLogicalTarget target,
            IReadOnlyList<TextureSet> targetSets, out string error)
            => BuildMemberOverlayBindings(context, target, targetSets, paintSourceOverlay, out error);

        private bool BuildMemberOverlayBindings(StrokeContext context, TexturePaintLogicalTarget target,
            IReadOnlyList<TextureSet> targetSets, OverlayDataAsset requestedOverlay, out string error)
        {
            error = null;
            if (requestedOverlay == null) { error = "Select OverlayData before painting."; return false; }
            int sourceOrdinal = -1;
            string sourceName = requestedOverlay.overlayName;
            for (int memberIndex = 0; memberIndex < target.members.Count && sourceOrdinal < 0; memberIndex++)
            for (int overlayIndex = 0; overlayIndex < target.members[memberIndex].sourceOverlays.Count; overlayIndex++)
                if (target.members[memberIndex].sourceOverlays[overlayIndex]?.asset == requestedOverlay)
                { sourceOrdinal = overlayIndex; break; }
            var resolved = new Dictionary<TextureSet, OverlayDataAsset>();
            for (int memberIndex = 0; memberIndex < target.members.Count; memberIndex++)
            {
                TexturePaintLogicalTargetMember member = target.members[memberIndex];
                OverlayDataAsset asset = null;
                for (int overlayIndex = 0; overlayIndex < member.sourceOverlays.Count; overlayIndex++)
                    if (member.sourceOverlays[overlayIndex]?.asset == requestedOverlay)
                    { asset = requestedOverlay; break; }
                if (asset == null && sourceOrdinal >= 0 && sourceOrdinal < member.sourceOverlays.Count)
                    asset = member.sourceOverlays[sourceOrdinal]?.asset;
                if (asset == null && !string.IsNullOrEmpty(sourceName))
                    for (int overlayIndex = 0; overlayIndex < member.sourceOverlays.Count; overlayIndex++)
                        if (string.Equals(member.sourceOverlays[overlayIndex]?.asset?.overlayName, sourceName,
                            StringComparison.Ordinal)) { asset = member.sourceOverlays[overlayIndex].asset; break; }
                if (asset == null)
                { error = $"No matching overlay source exists for target member '{member.slotName}'."; return false; }
                for (int setIndex = 0; setIndex < member.textureSets.Count; setIndex++)
                {
                    TextureSet memberSet = member.textureSets[setIndex];
                    if (resolved.TryGetValue(memberSet, out OverlayDataAsset existing) && existing != asset)
                    { error = $"Texture set '{memberSet.Name}' maps to conflicting member overlay sources."; return false; }
                    resolved[memberSet] = asset;
                }
            }
            for (int i = 0; i < targetSets.Count; i++)
            {
                TextureSet memberSet = targetSets[i];
                if (!resolved.TryGetValue(memberSet, out OverlayDataAsset asset))
                { error = $"Texture set '{memberSet.Name}' has no member overlay source."; return false; }
                string key = !string.IsNullOrEmpty(memberSet.persistentId)
                    ? memberSet.persistentId : memberSet.surface?.index.ToString();
                if (string.IsNullOrEmpty(key)) { error = "A target member has no stable surface identity."; return false; }
                context.sourceOverlaysBySurfaceId[key] = asset;
            }
            context.sourceOverlay = null;
            return true;
        }

        private static void PopulateLayerChannelSources(StrokeContext context, TexturePaintLayer layer)
        {
            if (context == null || layer == null) return;
            foreach (KeyValuePair<TexturePaintChannel, TexturePaintLayerChannelSettings> pair in
                layer.channelSettings)
            {
                TexturePaintChannelSourceSettings source = pair.Value?.sourceSettings;
                if (source == null ||
                    (source.source == TexturePaintBrushSource.Texture &&
                        source.sourceTexture == null && source.sourceSprite == null) ||
                    (source.source == TexturePaintBrushSource.Overlay && source.sourceOverlay == null))
                    continue;
                context.channelSources[pair.Key] = source;
            }
            if (!context.channelSources.ContainsKey(context.channel) &&
                (context.paintSource == TexturePaintBrushSource.Color ||
                    context.paintSource == TexturePaintBrushSource.Texture &&
                        (context.sourceTexture != null || context.sourceSprite != null) ||
                    context.paintSource == TexturePaintBrushSource.Overlay && context.sourceOverlay != null))
            {
                var source = new TexturePaintChannelSourceSettings
                {
                    source = context.paintSource,
                    sourceTexture = context.sourceTexture,
                    sourceSprite = context.sourceSprite,
                    sourceOverlay = context.sourceOverlay,
                    color = context.color,
                    normalConvention = context.normalConvention
                };
                layer.GetChannelSettings(context.channel).sourceSettings = source.Clone();
                context.channelSources[context.channel] = source;
            }
            if (context.channelSources.Count == 0) return;
            foreach (TexturePaintChannelSourceSettings source in context.channelSources.Values)
            {
                context.paintSource = source.source;
                context.sourceTexture = source.sourceTexture;
                context.sourceSprite = source.sourceSprite;
                context.sourceOverlay = source.sourceOverlay;
                context.color = source.color;
                context.normalConvention = source.normalConvention;
                break;
            }
        }

        private void ContinuePaint()
        {
            ContinuePaintAt(MakeSample(hoverSurface, hoverHit, hoverHit.textureCoord));
        }

        private void ContinuePaintAt(StrokeSample sample)
        {
            sampledStrokePoints.Clear();
            strokeSampler.Add(sample, sampledStrokePoints);
            ApplySampledStrokePoints();
        }

        private void ApplySampledStrokePoints()
        {
            RestampInitialFollowStampIfDirectionKnown();
            for (int i = 0; i < sampledStrokePoints.Count; i++)
            {
                StrokeSample sample = sampledStrokePoints[i];
                AccumulateAndApplyStrokeEvolution(ref sample);
                if ((ActiveBrush.fade && sample.flowMultiplier <= 0f) ||
                    (ActiveBrush.taper && sample.sizeMultiplier <= 0f))
                    continue;
                if (!directUVStroke) ProjectStrokeSampleToSurface(ref sample);
                ApplyPaintRandomVariation(ref sample, ActiveBrush, paintRandomSeed,
                    paintRandomStampIndex++, pressureAffectsSize, directUVStroke);
                if (initialFollowStampPending && !hasInitialFollowStamp)
                {
                    initialFollowStamp = sample;
                    hasInitialFollowStamp = true;
                }
                if (directUVStroke) ApplyDirectUVBrushFootprint(sample);
                else ApplyBrushFootprint(sample);
            }
        }

        private void ResetPaintRandomization()
        {
            unchecked
            {
                paintRandomSeed = (int)System.DateTime.UtcNow.Ticks ^
                    GetEntityId().GetHashCode();
            }
            paintRandomStampIndex = 0;
            paintStrokeWorldDistance = 0f;
            hasPaintStrokeDistanceSample = false;
        }

        private void AccumulateAndApplyStrokeEvolution(ref StrokeSample sample)
        {
            if (hasPaintStrokeDistanceSample)
                paintStrokeWorldDistance += Vector3.Distance(
                    sample.previousWorldPosition, sample.worldPosition);
            else hasPaintStrokeDistanceSample = true;
            ApplyStrokeEvolution(ref sample, ActiveBrush, paintStrokeWorldDistance);
        }

        internal static void ApplyStrokeEvolution(ref StrokeSample sample, BrushPreset paintBrush,
            float worldDistance)
        {
            if (paintBrush == null || (!paintBrush.fade && !paintBrush.taper)) return;
            float factor = 1f - Mathf.Clamp01(Mathf.Max(0f, worldDistance) /
                paintBrush.ResolvedFadeTaperLength);
            if (paintBrush.fade) sample.flowMultiplier *= factor;
            if (paintBrush.taper) sample.sizeMultiplier *= factor;
        }

        internal static void ApplyPaintRandomVariation(ref StrokeSample sample, BrushPreset paintBrush,
            int strokeSeed, int stampIndex, bool usePressure = false, bool uvSpace = false)
        {
            if (paintBrush == null) return;
            if (paintBrush.randomRotation && !paintBrush.alignToStroke)
                sample.rotation += PaintRandom01(strokeSeed, stampIndex, 0xA511E9B3u) * 360f;
            if (paintBrush.randomSizeVariation)
            {
                float shrink = Mathf.Clamp01(paintBrush.randomSizeShrink);
                float grow = Mathf.Clamp01(paintBrush.randomSizeGrow);
                float sizeScale = Mathf.Lerp(1f - shrink, 1f + grow,
                    PaintRandom01(strokeSeed, stampIndex, 0x63D83595u));
                float authoredSizeMultiplier = sample.sizeMultiplier > 0.000001f
                    ? sample.sizeMultiplier : 1f;
                sample.sizeMultiplier = authoredSizeMultiplier * Mathf.Max(0.001f, sizeScale);
            }

            if (!paintBrush.splatter) return;
            if (paintBrush.randomStrength)
                sample.flowMultiplier *= PaintRandom01(strokeSeed, stampIndex, 0x9E3779B9u);
            float maximumDistance = CalculateEffectiveWorldBrushSize(paintBrush, sample, usePressure) *
                Mathf.Clamp(paintBrush.splatterDistance, 0.01f, 2f);
            float angle = PaintRandom01(strokeSeed, stampIndex, 0xC2B2AE35u) * Mathf.PI * 2f;
            // sqrt produces uniform area density instead of clustering stamps at the center.
            float radius = Mathf.Sqrt(PaintRandom01(strokeSeed, stampIndex, 0x27D4EB2Fu)) *
                maximumDistance;
            Vector3 tangent = Vector3.zero;
            Vector3 bitangent = Vector3.zero;
            EnsureWorldProjectionFrame(sample.worldNormal, sample.direction, ref tangent, ref bitangent);
            Vector3 offset = (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * radius;
            sample.worldPosition += offset;
            if (uvSpace)
            {
                Vector2 uvOffset = new Vector2(offset.x, offset.y);
                sample.uv += uvOffset;
                sample.previousUV += uvOffset;
                sample.previousWorldPosition += offset;
            }
        }

        private static float PaintRandom01(int strokeSeed, int stampIndex, uint salt)
        {
            unchecked
            {
                uint value = (uint)strokeSeed ^ ((uint)stampIndex * 0x9E3779B9u) ^ salt;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return (value & 0x00FFFFFFu) / 16777216f;
            }
        }

        private void RestampInitialFollowStampIfDirectionKnown()
        {
            if (!initialFollowStampPending || !hasInitialFollowStamp) return;
            Vector3 direction = Vector3.zero;
            for (int i = 0; i < sampledStrokePoints.Count; i++)
            {
                if (sampledStrokePoints[i].direction.sqrMagnitude <= 0.00000001f) continue;
                direction = sampledStrokePoints[i].direction.normalized;
                break;
            }
            if (direction.sqrMagnitude <= 0.00000001f) return;

            controller.Painting.RewindActiveStroke();
            previousContactSamples.Clear();
            StrokeSample corrected = initialFollowStamp;
            corrected.direction = direction;
            corrected.previousWorldPosition = corrected.worldPosition - direction * ActiveBrush.StampSpacing;
            if (directUVStroke) ApplyDirectUVBrushFootprint(corrected);
            else ApplyBrushFootprint(corrected);
            initialFollowStampPending = false;
        }

        private void ApplyDirectUVBrushFootprint(StrokeSample sample)
        {
            float radius = CalculateEffectiveWorldBrushSize(FootprintBrush, sample, pressureAffectsSize);
            if (FootprintBrush.alignToStroke && sample.direction.sqrMagnitude > 0.00000001f)
                sample.rotation += Mathf.Atan2(sample.direction.y, sample.direction.x) * Mathf.Rad2Deg;
            controller.Painting.ApplySample(sample, radius);
            if (!mirrorX && !FootprintBrush.mirrorStroke) return;
            StrokeSample mirrored = sample;
            mirrored.uv.x = 1f - sample.uv.x;
            mirrored.previousUV.x = 1f - sample.previousUV.x;
            mirrored.worldPosition.x = mirrored.uv.x;
            mirrored.previousWorldPosition.x = mirrored.previousUV.x;
            mirrored.rotation = -sample.rotation;
            controller.Painting.ApplySample(mirrored, radius);
        }

        private void QueueDirectUVSplineFootprint(StrokeSample sample)
        {
            float radius = CalculateEffectiveWorldBrushSize(FootprintBrush, sample, pressureAffectsSize);
            if (FootprintBrush.alignToStroke && sample.direction.sqrMagnitude > 0.00000001f)
                sample.rotation += Mathf.Atan2(sample.direction.y, sample.direction.x) * Mathf.Rad2Deg;
            int copies = Mathf.Clamp(radialSymmetry, 1, 16);
            for (int copy = 0; copy < copies; copy++)
            {
                float angle = copy * 360f / copies;
                Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.forward);
                StrokeSample rotated = sample;
                Vector3 centered = sample.worldPosition - new Vector3(0.5f, 0.5f, 0f);
                rotated.worldPosition = new Vector3(0.5f, 0.5f, 0f) + rotation * centered;
                rotated.uv = new Vector2(rotated.worldPosition.x, rotated.worldPosition.y);
                rotated.previousWorldPosition = rotated.worldPosition;
                rotated.previousUV = rotated.uv;
                rotated.direction = rotation * sample.direction;
                rotated.rotation += angle;
                splineDispatchSamples.Add(new StrokeDispatchSample(rotated, radius, default));
                if (!mirrorX && !FootprintBrush.mirrorStroke) continue;
                StrokeSample mirrored = rotated;
                mirrored.uv.x = 1f - rotated.uv.x;
                mirrored.previousUV.x = mirrored.uv.x;
                mirrored.worldPosition.x = mirrored.uv.x;
                mirrored.previousWorldPosition.x = mirrored.uv.x;
                mirrored.direction.x = -mirrored.direction.x;
                mirrored.rotation = -rotated.rotation;
                splineDispatchSamples.Add(new StrokeDispatchSample(mirrored, radius, default));
            }
        }

        private void ProjectStrokeSampleToSurface(ref StrokeSample sample)
        {
            Vector3 normal = sample.worldNormal.sqrMagnitude > 0.000001f ? sample.worldNormal.normalized : Vector3.up;
            float offset = Mathf.Max(ActiveBrush.size * 4f, 0.01f);
            bool found = false;
            float bestDistance = float.MaxValue;
            ReconstructedSurface bestSurface = null;
            RaycastHit bestHit = default;
            if (controller.Reconstruction.Raycast(new Ray(sample.worldPosition + normal * offset, -normal),
                out ReconstructedSurface frontSurface, out RaycastHit frontHit) && IsSelectedSlotHit(frontSurface, frontHit.triangleIndex))
            {
                found = true;
                bestDistance = Vector3.Distance(sample.worldPosition, frontHit.point);
                bestSurface = frontSurface;
                bestHit = frontHit;
            }
            if (controller.Reconstruction.Raycast(new Ray(sample.worldPosition - normal * offset, normal),
                out ReconstructedSurface backSurface, out RaycastHit backHit) && IsSelectedSlotHit(backSurface, backHit.triangleIndex))
            {
                float distance = Vector3.Distance(sample.worldPosition, backHit.point);
                if (!found || distance < bestDistance)
                {
                    found = true;
                    bestSurface = backSurface;
                    bestHit = backHit;
                }
            }
            if (!found) return;
            sample.worldPosition = bestHit.point;
            sample.worldNormal = bestHit.normal;
            sample.projectionDirection = -bestHit.normal;
            sample.uv = bestHit.textureCoord;
            sample.surfaceIndex = bestSurface.index;
            sample.triangleIndex = bestHit.triangleIndex;
            sample.barycentric = bestHit.barycentricCoordinate;
            sample.uvIsland = bestSurface.triangleIslands != null &&
                (uint)bestHit.triangleIndex < (uint)bestSurface.triangleIslands.Length
                    ? bestSurface.triangleIslands[bestHit.triangleIndex] : -1;
            sample.slotName = bestSurface.GetTriangleSlotName(bestHit.triangleIndex);
        }

        private void ApplyBrushFootprint(StrokeSample centerSample)
        {
            BrushPreset footprintBrush = FootprintBrush;
            int copies = applyingSpline ? Mathf.Clamp(radialSymmetry, 1, 16) : 1;
            Vector3 pivot = controller.Reconstruction.root.transform.position;
            BrushProjection centerProjection = default;
            TextureSet centerSet = controller.Textures.FindSet(centerSample.surfaceIndex);
            if (centerSet != null)
                centerProjection = centerSet.surface.CalculateBrushProjection(centerSample.triangleIndex, 1f);
            Vector3 centerTangent = centerProjection.worldTangent;
            Vector3 centerBitangent = centerProjection.worldBitangent;
            EnsureWorldProjectionFrame(centerSample.worldNormal, centerSample.direction,
                ref centerTangent, ref centerBitangent);
            for (int copy = 0; copy < copies; copy++)
            {
                Quaternion rotation = Quaternion.AngleAxis(copy * 360f / copies, Vector3.up);
                StrokeSample rotated = centerSample;
                rotated.worldNormal = rotation * centerSample.worldNormal;
                rotated.direction = rotation * centerSample.direction;
                rotated.projectionDirection = rotation * centerSample.projectionDirection;
                Vector3 world = pivot + rotation * (centerSample.worldPosition - pivot);
                Vector3 sharedTangent = rotation * centerTangent;
                Vector3 sharedBitangent = rotation * centerBitangent;
                ApplyBrushFootprintAt(rotated, world, copy * 2, sharedTangent, sharedBitangent);
                if (mirrorX || footprintBrush.mirrorStroke)
                {
                    rotated.worldNormal = TexturePaintMath.MirrorDirectionAcrossGlobalX(rotated.worldNormal);
                    rotated.direction = TexturePaintMath.MirrorDirectionAcrossGlobalX(rotated.direction);
                    rotated.projectionDirection = TexturePaintMath.MirrorDirectionAcrossGlobalX(rotated.projectionDirection);
                    sharedTangent = TexturePaintMath.MirrorDirectionAcrossGlobalX(sharedTangent);
                    sharedBitangent = TexturePaintMath.MirrorDirectionAcrossGlobalX(sharedBitangent);
                    ApplyBrushFootprintAt(rotated, TexturePaintMath.MirrorAcrossGlobalX(world), copy * 2 + 1,
                        sharedTangent, sharedBitangent);
                }
            }
        }

        private void ApplyBrushFootprintAt(StrokeSample centerSample, Vector3 worldCenter, int variant,
            Vector3 sharedWorldTangent, Vector3 sharedWorldBitangent)
        {
            BrushPreset footprintBrush = FootprintBrush;
            float brushSize = CalculateEffectiveWorldBrushSize(footprintBrush, centerSample,
                pressureAffectsSize);
            float queryRadius = footprintBrush.shape == BrushPreset.Shape.Square ? brushSize * 1.41421356f : brushSize;
            Vector2 footprintScale = centerSample.footprintScale;
            float footprintRadiusScale = Mathf.Max(
                Mathf.Abs(footprintScale.x) <= 0.000001f ? 1f : Mathf.Abs(footprintScale.x),
                Mathf.Abs(footprintScale.y) <= 0.000001f ? 1f : Mathf.Abs(footprintScale.y));
            queryRadius *= footprintRadiusScale;
            for (int setIndex = 0; setIndex < strokeTextureSets.Count; setIndex++)
            {
                TextureSet set = strokeTextureSets[setIndex];
                brushContacts.Clear();
                set.surface.CollectBrushContacts(worldCenter, queryRadius, selectedSlots, brushContacts,
                    centerSample.worldNormal, brushSize * projectionDepth, normalAngleLimit, paintBackfaces,
                    sharedWorldTangent, sharedWorldBitangent);
                for (int contactIndex = 0; contactIndex < brushContacts.Count; contactIndex++)
                {
                    SurfaceBrushContact contact = brushContacts[contactIndex];
                    TexturePaintLogicalTarget target = strokeLogicalTarget;
                    TexturePaintLogicalTargetMember member = !string.IsNullOrEmpty(contact.slotName)
                        ? controller.LogicalLayers?.FindMember(target, contact.slotName)
                        : target?.members.Count == 1 ? controller.LogicalLayers?.FindMember(target, set) : null;
                    if (target != null && (member == null || !member.textureSets.Contains(set))) continue;
                    StrokeContactKey key = new StrokeContactKey(set.surface.index, contact.uvIsland,
                        contact.triangleIndex, contact.slotName, variant);
                    StrokeSample sample = centerSample;
                    sample.surfaceId = set.persistentId;
                    sample.worldPosition = contact.worldPoint;
                    sample.worldNormal = contact.worldNormal;
                    sample.uv = contact.brushCenterUV;
                    sample.surfaceIndex = set.surface.index;
                    sample.triangleIndex = contact.triangleIndex;
                    sample.uvIsland = contact.uvIsland;
                    sample.slotName = contact.slotName;
                    if (previousContactSamples.TryGetValue(key, out StrokeSample previousContact))
                    {
                        sample.previousUV = previousContact.uv;
                        sample.previousWorldPosition = previousContact.worldPosition;
                    }
                    else
                    {
                        sample.previousUV = sample.uv;
                        sample.previousWorldPosition = sample.worldPosition;
                    }
                    float uvRadius = set.surface.CalculateUVRadius(contact.triangleIndex, brushSize);
                    BrushProjection projection = set.surface.CalculateBrushProjection(contact.triangleIndex, brushSize,
                        sharedWorldTangent, sharedWorldBitangent, true);
                    // A triangle-restricted contact must retain the canonical world projector. The
                    // generic UV-radius fallback represents a different, locally centered brush and
                    // causes dense or grazing polygons to fan outward as independent stamps.
                    if (!projection.valid) continue;
                    if (footprintBrush.alignToStroke || (applyingSpline && pathOrientation == TexturePaintPathOrientation.FollowPath))
                    {
                        Vector2 brushMotion = ResolveFollowStrokeMotion(sample, projection);
                        if (brushMotion.sqrMagnitude > 0.00000001f)
                            sample.rotation += Mathf.Atan2(brushMotion.y, brushMotion.x) * Mathf.Rad2Deg;
                    }
                    if (applyingSpline) splineDispatchSamples.Add(new StrokeDispatchSample(sample, uvRadius, projection));
                    else controller.Painting.ApplySample(sample, uvRadius, projection);
                    previousContactSamples[key] = sample;
                }
            }
        }

        internal static float CalculateEffectiveWorldBrushSize(BrushPreset paintBrush,
            StrokeSample sample, bool usePressure)
        {
            if (paintBrush == null) return 0f;
            float pressureScale = usePressure
                ? Mathf.Clamp01(sample.pressure) : 1f;
            return paintBrush.size * pressureScale * Mathf.Max(0.01f, sample.sizeMultiplier);
        }

        private static void EnsureWorldProjectionFrame(Vector3 normal, Vector3 direction,
            ref Vector3 tangent, ref Vector3 bitangent)
        {
            if (tangent.sqrMagnitude > 0.00000001f && bitangent.sqrMagnitude > 0.00000001f)
            {
                tangent.Normalize();
                bitangent.Normalize();
                return;
            }
            normal = normal.sqrMagnitude > 0.00000001f ? normal.normalized : Vector3.forward;
            tangent = direction - normal * Vector3.Dot(direction, normal);
            if (tangent.sqrMagnitude <= 0.00000001f)
            {
                Vector3 reference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.95f
                    ? Vector3.up : Vector3.right;
                tangent = Vector3.Cross(reference, normal);
            }
            tangent.Normalize();
            bitangent = Vector3.Cross(normal, tangent).normalized;
        }

        private void EndPaint()
        {
            sampledStrokePoints.Clear();
            strokeSampler.Flush(sampledStrokePoints);
            ApplySampledStrokePoints();
            controller.Painting.EndStroke(true);
            if (controller.Painting.History.CommitVersion != paintHistoryVersionAtStrokeStart)
            {
                List<LayerLocation> createdLayers = new List<LayerLocation>(strokeCreatedLayers);
                PushLightweightCommand("Paint Stroke",
                    () =>
                    {
                        controller.Painting.Undo();
                        DetachLayerLocations(createdLayers);
                    },
                    () =>
                    {
                        AttachLayerLocations(createdLayers);
                        controller.Painting.Redo();
                    },
                    () =>
                    {
                        for (int i = 0; i < createdLayers.Count; i++)
                            DisposeLayerIfDetached(createdLayers[i].set, createdLayers[i].layer);
                    });
            }
            else
            {
                for (int i = 0; i < strokeCreatedLayers.Count; i++)
                {
                    LayerLocation created = strokeCreatedLayers[i];
                    DetachLayer(created.set, created.layer);
                    created.layer.Dispose();
                }
            }
            strokeCreatedLayers.Clear();
            MarkDocumentDirty();
            strokeActive = false;
            directUVStroke = false;
            strokeLogicalTarget = null;
            strokeTextureSets.Clear();
            previousContactSamples.Clear();
            initialFollowStampPending = false;
            hasInitialFollowStamp = false;
        }

        internal static Vector2 ResolveFollowStrokeMotion(StrokeSample sample, BrushProjection projection)
        {
            Vector2 uvMotion = sample.uv - sample.previousUV;
            Vector2 brushMotion = new Vector2(
                uvMotion.x * projection.uvToBrush.x + uvMotion.y * projection.uvToBrush.y,
                uvMotion.x * projection.uvToBrush.z + uvMotion.y * projection.uvToBrush.w);
            if (brushMotion.sqrMagnitude > 0.00000001f || sample.direction.sqrMagnitude <= 0.00000001f)
                return brushMotion;
            Vector3 direction = Vector3.ProjectOnPlane(sample.direction, sample.worldNormal);
            if (direction.sqrMagnitude <= 0.00000001f) return Vector2.zero;
            direction.Normalize();
            return new Vector2(Vector3.Dot(direction, projection.worldTangent),
                Vector3.Dot(direction, projection.worldBitangent));
        }

        private void OnTextureChanged(TextureSet changedSet, TexturePaintChannel changedChannel)
        {
            if (changedSet == null && controller?.Textures != null)
            {
                for (int i = 0; i < controller.Textures.Sets.Count; i++)
                    controller.Textures.Sets[i].BindPreviewTextures();
            }
            MarkDocumentDirty();
            if (textureWindowRepaintPending) return;
            textureWindowRepaintPending = true;
            EditorApplication.delayCall += RepaintTextureWindows;
        }

        private void RepaintTextureWindows()
        {
            textureWindowRepaintPending = false;
            TexturePaintDockWindow.RepaintOpenWindows();
            TexturePaintUVWindow.RepaintOpenWindows();
        }

        private void OnPluginChanged()
        {
            if (!applyingLightweightHistory && controller?.Plugins != null &&
                observedPluginCommitVersion != controller.Plugins.CommitVersion)
            {
                observedPluginCommitVersion = controller.Plugins.CommitVersion;
                PushLightweightCommand("Plugin Transaction", () => controller.Plugins.Undo(),
                    () => controller.Plugins.Redo());
            }
            if (controller?.Textures != null)
                for (int i = 0; i < controller.Textures.Sets.Count; i++) controller.Textures.Sets[i].BindPreviewTextures();
            TextureSet activeSet = ActiveTextureSet;
            if (TryGetActivePathLayer(activeSet, out _))
            {
                QueueSplineReapply(activeSet);
                ScheduleSplineReapply();
            }
            MarkDocumentDirty();
            SceneView.RepaintAll();
        }

        internal void MarkDocumentDirty()
        {
            MarkPluginLayersAboveActiveLayerStale();
            SetDocumentDirtyFlags();
        }

        private void MarkDocumentDirty(IReadOnlyList<TexturePaintLogicalLayerMember> changedMembers)
        {
            if (changedMembers != null)
                for (int i = 0; i < changedMembers.Count; i++)
                    MarkPluginLayersAffectedByLayer(changedMembers[i]?.textureSet,
                        changedMembers[i]?.layer);
            SetDocumentDirtyFlags();
        }

        private void MarkDocumentDirtyAfterStructuralChange()
        {
            if (controller?.Textures != null)
                for (int setIndex = 0; setIndex < controller.Textures.Sets.Count; setIndex++)
                {
                    TextureSet set = controller.Textures.Sets[setIndex];
                    for (int layerIndex = 0; layerIndex < set.layers.Count; layerIndex++)
                    {
                        TexturePaintLayer layer = set.layers[layerIndex];
                        if (layer?.kind != TexturePaintLayerKind.Plugin) continue;
                        layer.pluginStale = true;
                        layer.pluginLastError = null;
                    }
                }
            SetDocumentDirtyFlags();
        }

        private void SetDocumentDirtyFlags()
        {
            documentDirty = true;
            recoveryDirty = true;
            documentChangeVersion++;
            // Debounce recovery capture from the most recent edit. Timing from the first stamp made
            // long strokes immediately trigger a full document readback when the mouse was released.
            nextAutosaveTime = EditorApplication.timeSinceStartup + AutosaveIntervalSeconds;
        }

        private void MarkPluginLayersAboveActiveLayerStale()
        {
            if (controller?.Textures == null) return;
            for (int setIndex = 0; setIndex < controller.Textures.Sets.Count; setIndex++)
            {
                TextureSet set = controller.Textures.Sets[setIndex];
                TexturePaintLayer changedLayer = (uint)set.activeLayerIndex < (uint)set.layers.Count
                    ? set.layers[set.activeLayerIndex] : null;
                MarkPluginLayersAffectedByLayer(set, changedLayer);
            }
        }

        private static void MarkPluginLayersAffectedByLayer(TextureSet set,
            TexturePaintLayer changedLayer)
        {
            if (set == null) return;
            int changedIndex = changedLayer != null ? set.layers.IndexOf(changedLayer) : -1;
            if (changedIndex < -1) changedIndex = -1;
            for (int layerIndex = changedIndex + 1; layerIndex < set.layers.Count; layerIndex++)
            {
                TexturePaintLayer candidate = set.layers[layerIndex];
                if (candidate?.kind != TexturePaintLayerKind.Plugin) continue;
                candidate.pluginStale = true;
                candidate.pluginLastError = null;
            }
            // A group is stored after its descendants in the bottom-to-top stack. Its mask,
            // opacity, or blend can still affect a descendant Plugin layer's below-layer
            // snapshot, even though the Plugin layer has a lower numeric index.
            if (changedLayer?.kind != TexturePaintLayerKind.Group) return;
            for (int layerIndex = 0; layerIndex <= changedIndex && layerIndex < set.layers.Count;
                layerIndex++)
            {
                TexturePaintLayer candidate = set.layers[layerIndex];
                if (candidate?.kind != TexturePaintLayerKind.Plugin ||
                    !IsDescendantOfGroup(set, candidate, changedLayer.id)) continue;
                candidate.pluginStale = true;
                candidate.pluginLastError = null;
            }
        }

        private static bool IsDescendantOfGroup(TextureSet set, TexturePaintLayer layer,
            string groupId)
        {
            if (set == null || layer == null || string.IsNullOrEmpty(groupId)) return false;
            string parentId = layer.parentId;
            int guard = 0;
            while (!string.IsNullOrEmpty(parentId) && guard++ < set.layers.Count)
            {
                if (string.Equals(parentId, groupId, StringComparison.Ordinal)) return true;
                TexturePaintLayer parent = null;
                for (int i = 0; i < set.layers.Count; i++)
                    if (string.Equals(set.layers[i]?.id, parentId, StringComparison.Ordinal))
                    { parent = set.layers[i]; break; }
                if (parent?.kind != TexturePaintLayerKind.Group) return false;
                parentId = parent.parentId;
            }
            return false;
        }

        private void AutosaveUpdate()
        {
            PersistenceUpdate();
        }

        private void SaveDocument(bool recoverySnapshot)
        {
            if (document == null || controller?.Textures == null) return;
            if (recoverySnapshot)
                BeginPersistence(IsDocumentTemporary ? PersistenceIntent.Recovery : PersistenceIntent.ProjectSave);
            else if (!IsDocumentTemporary)
                BeginPersistence(PersistenceIntent.ProjectSave, AssetDatabase.GetAssetPath(document));
        }

        private void ClearAllTexturePaintData(bool confirm)
        {
            if (controller?.Textures == null) return;
            if (confirm && !EditorUtility.DisplayDialog("Clear All Overlay Painting",
                "This restores every slot to its original source textures and removes all paint layers, paths, " +
                "direct Base Texture edits, masks, and texture-paint history.\n\nThis cannot be undone.",
                "Clear Everything", "Cancel")) return;

            ResetTexturePaintRuntimeState();

            MarkDocumentDirty();
            selectedSurface = Mathf.Clamp(selectedSurface, 0, controller.Textures.Sets.Count - 1);
            if (controller.Textures.Sets.Count > 0) SyncActiveLayerSelection(controller.Textures.Sets[selectedSurface]);
            ApplyWorkspaceDisplay();
            ShowWorkspaceStatus("All Overlay Painter changes cleared");
            RepaintAll();
        }

        private void ResetTexturePaintRuntimeState()
        {
            EditorApplication.delayCall -= ReapplySplineAfterGUI;
            splineReapplyDelayScheduled = false;
            splineReapplyPending = false;
            pendingSplineSet = null;
            pendingSplineLayer = null;
            if (controller.Painting.IsPainting) controller.Painting.EndStroke(false);
            strokeActive = false;
            paintGestureActive = false;
            initialFollowStampPending = false;
            hasInitialFollowStamp = false;
            applyingSpline = false;
            controller.Painting.History.Clear();
            controller.Plugins?.ClearHistory();
            observedPluginCommitVersion = controller.Plugins?.CommitVersion ?? 0L;
            ClearLightweightHistory();
            controller.Textures.ClearModifications();

            spline = null;
            splineMode = false;
            selectedSplinePoint = -1;
            selectedSplinePoints?.Clear();
            splineDisplayCache?.Clear();
            layerMaskMode = false;
            soloLayerMask = false;
            geometryFillMode = 0;
            uvDraggingSplinePoint = -1;
            uvDraggingSplineHandle = UVSplineHandleKind.None;
            uvSplineHandleUndoStarted = false;
            uvStrokeActive = false;
            workspaceRenameLayerId = null;
        }

        private void BeginLayerCreationUndo(string label)
        {
            if (controller?.Textures == null) return;
            pendingLayerCreationLabel = label;
        }

        private void CompleteLayerCreationUndo(TexturePaintLayer layer)
        {
            if (layer == null || controller?.Textures == null) return;
            string label = string.IsNullOrEmpty(pendingLayerCreationLabel) ? "Add Texture Layer" : pendingLayerCreationLabel;
            pendingLayerCreationLabel = null;
            TextureSet set = FindContainingSet(layer);
            TexturePaintLayer parentGroup = FindLayerById(set, layer.parentId);
            if (parentGroup?.kind == TexturePaintLayerKind.Group)
                SetGroupExpanded(parentGroup, true);
            TexturePaintLogicalTarget target = controller.LogicalLayers?.FindTarget(set);
            if (set == null || target == null)
            {
                RegisterCreatedLayer(layer, label);
                MarkDocumentDirtyAfterStructuralChange();
                return;
            }
            var created = new List<TexturePaintLogicalLayerMember>();
            if (!controller.LogicalLayers.LinkAndRepair(target, set, layer, created,
                out TexturePaintLogicalLayerBinding binding))
            {
                DetachLayer(set, layer);
                layer.Dispose();
                ShowWorkspaceStatus(binding?.error ?? "Could not create the logical layer on every target member.");
                return;
            }
            var locations = new List<LayerLocation>
            {
                new LayerLocation { set = set, layer = layer, index = set.layers.IndexOf(layer) }
            };
            for (int i = 0; i < created.Count; i++)
                locations.Add(new LayerLocation { set = created[i].textureSet, layer = created[i].layer,
                    index = created[i].textureSet.layers.IndexOf(created[i].layer) });
            controller.LogicalLayers.Activate(binding);
            RegisterCreatedLayers(locations, label);
            MarkDocumentDirtyAfterStructuralChange();
        }

        private void BeginLightweightPathUndo(TextureSet set, string label)
        {
            BeginCustomPathEdit(set, label);
        }

        private void RecordLightweightPathParameterUndo(TextureSet set, TexturePaintSplineSettings previousSettings,
            string label)
        {
            if (previousSettings == null) return;
            BeginCustomPathEdit(set, label, previousSettings);
            CompleteLightweightPathEdit(set, false);
        }

        private void CompleteLightweightPathEdit(TextureSet set, bool deferUntilMouseUp)
        {
            if (!TryGetActivePathLayer(set, out TexturePaintLayer layer)) return;
            CaptureSplineSettings(layer);
            CompleteCustomPathEdit(set, deferUntilMouseUp);
            QueueSplineReapply(set);
            if (!deferUntilMouseUp) ScheduleSplineReapply();
            MarkDocumentDirty();
        }

        private void SelectSingleSplinePoint(int pointIndex)
        {
            selectedSplinePoint = pointIndex;
            selectedSplinePoints ??= new HashSet<int>();
            selectedSplinePoints.Clear();
            if (pointIndex >= 0) selectedSplinePoints.Add(pointIndex);
            RepaintAll();
        }

        private IReadOnlyCollection<int> GetSelectedSplinePointIndices(TexturePaintSpline targetSpline)
        {
            if (selectedSplinePoints != null && selectedSplinePoints.Count > 0)
                return selectedSplinePoints;
            return selectedSplinePoint >= 0 && selectedSplinePoint < targetSpline.PointCount
                ? new[] { selectedSplinePoint }
                : Array.Empty<int>();
        }

        private void StraightenSelectedSplinePoints(TextureSet set)
        {
            if (!TryGetActivePathLayer(set, out TexturePaintLayer layer)) return;
            IReadOnlyCollection<int> selection = GetSelectedSplinePointIndices(layer.spline);
            if (selection.Count == 0) return;
            BeginLightweightPathUndo(set, "Straighten Spline Point Handles");
            foreach (int pointIndex in selection)
                if ((uint)pointIndex < (uint)layer.spline.PointCount)
                    layer.spline.SetTangentMode(pointIndex, TexturePaintTangentMode.Straight);
            CompleteLightweightPathEdit(set, false);
            RepaintAll();
        }

        private void DeleteSelectedSplinePoints(TextureSet set)
        {
            if (!TryGetActivePathLayer(set, out TexturePaintLayer layer)) return;
            IReadOnlyCollection<int> selection = GetSelectedSplinePointIndices(layer.spline);
            if (selection.Count == 0) return;
            var points = new List<int>(selection);
            points.Sort((left, right) => right.CompareTo(left));
            int nextSelection = points[points.Count - 1];
            BeginLightweightPathUndo(set, points.Count == 1 ? "Delete Spline Point" : "Delete Spline Points");
            for (int i = 0; i < points.Count; i++) layer.spline.RemovePoint(points[i]);
            nextSelection = layer.spline.PointCount > 0
                ? Mathf.Clamp(nextSelection, 0, layer.spline.PointCount - 1)
                : -1;
            SelectSingleSplinePoint(nextSelection);
            CompleteLightweightPathEdit(set, false);
        }

        private void SetSplinePointWidth(TextureSet set, int pointIndex, float widthMultiplier)
        {
            if (!TryGetActivePathLayer(set, out TexturePaintLayer layer) ||
                (uint)pointIndex >= (uint)layer.spline.PointCount) return;
            BeginLightweightPathUndo(set, "Change Spline Point Width");
            layer.spline.widths[pointIndex] = Mathf.Clamp(widthMultiplier, 0.05f, 4f);
            CompleteLightweightPathEdit(set, false);
            RepaintAll();
        }

        private void ShowSplinePointContextMenu(TextureSet set, int pointIndex)
        {
            if (!TryGetActivePathLayer(set, out TexturePaintLayer layer) ||
                (uint)pointIndex >= (uint)layer.spline.PointCount) return;
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Straight Handles (Linear)"), false,
                () => StraightenSelectedSplinePoints(set));
            menu.AddItem(new GUIContent("Delete Point"), false,
                () => DeleteSelectedSplinePoints(set));
            menu.AddSeparator(string.Empty);
            int[] widthPercentages = { 25, 50, 75, 100, 125, 150, 200, 300, 400 };
            float currentWidth = layer.spline.widths[pointIndex];
            for (int i = 0; i < widthPercentages.Length; i++)
            {
                int percentage = widthPercentages[i];
                float multiplier = percentage * 0.01f;
                menu.AddItem(new GUIContent($"Width/{percentage}%"),
                    Mathf.Approximately(currentWidth, multiplier),
                    () => SetSplinePointWidth(set, pointIndex, multiplier));
            }
            menu.ShowAsContext();
        }

        private static bool TryGetActivePathLayer(TextureSet set, out TexturePaintLayer layer)
        {
            layer = null;
            if (set == null || (uint)set.activeLayerIndex >= (uint)set.layers.Count) return false;
            layer = set.layers[set.activeLayerIndex];
            return layer != null && layer.IsSplineLayer && layer.spline != null;
        }

        private void ScheduleSplineReapply()
        {
            if (!splineReapplyPending || splineReapplyDelayScheduled) return;
            splineReapplyDelayScheduled = true;
            EditorApplication.delayCall += ReapplySplineAfterGUI;
        }

        private void ReapplySplineAfterGUI()
        {
            splineReapplyDelayScheduled = false;
            if (controller == null || !splineReapplyPending) return;
            ReapplyPendingSpline();
            RepaintAll();
        }

        private static StrokeSample MakeSample(ReconstructedSurface surface, RaycastHit hit, Vector2 uv)
        {
            float eventPressure = Event.current != null && Event.current.pressure > 0f ? Event.current.pressure : 1f;
            StrokeSample sample = new StrokeSample(hit.point, hit.normal, uv, surface.index, hit.triangleIndex)
            {
                surfaceId = surface.index.ToString(),
                barycentric = hit.barycentricCoordinate,
                projectionDirection = -hit.normal,
                pressure = Mathf.Clamp01(eventPressure),
                uvIsland = surface.triangleIslands != null && (uint)hit.triangleIndex < (uint)surface.triangleIslands.Length
                    ? surface.triangleIslands[hit.triangleIndex] : -1,
                slotName = surface.GetTriangleSlotName(hit.triangleIndex)
            };
            return sample;
        }

        private void AddSplinePoint()
        {
            if (!IsSelectedSlotHit(hoverSurface, hoverHit.triangleIndex)) return;
            TextureSet set = ActivateSurfaceForSpline(hoverSurface);
            if (set == null) return;
            EnsureSplineLayer(set);
            if (spline?.worldSpace != true)
            {
                ShowWorkspaceStatus("This is a 2D spline. Edit it in the 2D view or change Spline Space in Properties.");
                return;
            }
            BeginLightweightPathUndo(set, "Add Spline Point");
            spline.UpgradeWorldCurve();
            spline.AddPoint(hoverHit.point, hoverHit.textureCoord, hoverSurface.index, hoverHit.triangleIndex, hoverHit.normal);
            selectedSplinePoint = spline.PointCount - 1;
            TexturePaintSurfaceAnchor anchor = spline.anchors[selectedSplinePoint];
            anchor.surfaceId = set.persistentId;
            anchor.barycentric = hoverHit.barycentricCoordinate;
            anchor.normal = hoverHit.normal;
            spline.anchors[selectedSplinePoint] = anchor;
            CompleteLightweightPathEdit(set, true);
        }

        private bool TryInsertSplinePointAt(Vector2 guiPoint)
        {
            TextureSet set = ActiveTextureSet;
            if (set?.surface == null || !TryGetActivePathLayer(set, out TexturePaintLayer layer) ||
                layer.spline?.worldSpace != true || layer.spline.SegmentCount == 0 ||
                !TryFindNearestWorldSplineSegment(set, layer.spline, guiPoint,
                    out int segment, out float segmentT)) return false;

            spline = layer.spline;
            BeginLightweightPathUndo(set, "Insert Spline Point");
            int inserted = spline.InsertPointAfter(segment, segmentT);
            if (inserted < 0) return false;
            selectedSplinePoint = inserted;
            selectedSplinePoints?.Clear();
            selectedSplinePoints?.Add(inserted);
            UpdateInsertedWorldSplineAnchor(set, spline, inserted);
            CompleteLightweightPathEdit(set, true);
            SceneView.RepaintAll();
            return true;
        }

        private bool TryFindNearestWorldSplineSegment(TextureSet set, TexturePaintSpline targetSpline,
            Vector2 guiPoint, out int bestSegment, out float bestT)
        {
            bestSegment = -1;
            bestT = 0f;
            float bestDistanceSquared = SplineInsertTolerancePixels * SplineInsertTolerancePixels;
            targetSpline.EnsureControlPoints();
            IReadOnlyList<TextureSet> projectionSets = GetSplineProjectionSets(set.surface);
            const int subdivisions = 48;
            for (int segment = 0; segment < targetSpline.SegmentCount; segment++)
            {
                int next = (segment + 1) % targetSpline.PointCount;
                int preferredSurface = segment < targetSpline.surfaceIndices.Count
                    ? targetSpline.surfaceIndices[segment] : set.surface.index;
                int preferredTriangle = segment < targetSpline.triangleIndices.Count
                    ? targetSpline.triangleIndices[segment] : -1;
                bool hasPrevious = false;
                Vector2 previousScreen = default;
                float previousT = 0f;
                for (int step = 0; step <= subdivisions; step++)
                {
                    float t = step / (float)subdivisions;
                    targetSpline.EvaluateSegment(segment, next, t, out Vector3 world, out _);
                    Vector3 hint = Vector3.Slerp(targetSpline.worldNormals[segment],
                        targetSpline.worldNormals[next], t);
                    if (!TryProjectWorldPathPoint(projectionSets, world, hint, preferredSurface,
                        preferredTriangle, out TextureSet projectedSet, out Vector3 surfacePoint,
                        out _, out _, out int triangle, out _))
                    {
                        hasPrevious = false;
                        continue;
                    }
                    preferredSurface = projectedSet.surface.index;
                    preferredTriangle = triangle;
                    Vector2 screen = HandleUtility.WorldToGUIPoint(surfacePoint);
                    if (hasPrevious)
                        AccumulateSplineInsertionCandidate(guiPoint, previousScreen, screen, segment,
                            previousT, t, ref bestSegment, ref bestT, ref bestDistanceSquared);
                    previousScreen = screen;
                    previousT = t;
                    hasPrevious = true;
                }
            }
            return bestSegment >= 0;
        }

        private void UpdateInsertedWorldSplineAnchor(TextureSet ownerSet, TexturePaintSpline targetSpline,
            int point)
        {
            if (ownerSet?.surface == null || (uint)point >= (uint)targetSpline.PointCount) return;
            int preferredSurface = targetSpline.surfaceIndices[point];
            int preferredTriangle = targetSpline.triangleIndices[point];
            Vector3 normalHint = targetSpline.worldNormals[point];
            if (!TryProjectWorldPathPoint(GetSplineProjectionSets(ownerSet.surface),
                targetSpline.worldPoints[point], normalHint, preferredSurface, preferredTriangle,
                out TextureSet projectedSet, out Vector3 world, out Vector3 normal, out Vector2 uv,
                out int triangle, out Vector3 barycentric)) return;

            Vector3 delta = world - targetSpline.worldPoints[point];
            targetSpline.worldPoints[point] = world;
            targetSpline.worldInControls[point] += delta;
            targetSpline.worldOutControls[point] += delta;
            targetSpline.uvPoints[point] = uv;
            targetSpline.worldNormals[point] = normal;
            targetSpline.surfaceIndices[point] = projectedSet.surface.index;
            targetSpline.triangleIndices[point] = triangle;
            targetSpline.anchors[point] = new TexturePaintSurfaceAnchor
            {
                surfaceId = projectedSet.persistentId,
                surfaceIndex = projectedSet.surface.index,
                triangleIndex = triangle,
                barycentric = barycentric,
                normal = normal
            };
        }

        private static void AccumulateSplineInsertionCandidate(Vector2 query, Vector2 from, Vector2 to,
            int segment, float fromT, float toT, ref int bestSegment, ref float bestT,
            ref float bestDistanceSquared)
        {
            Vector2 edge = to - from;
            float edgeLengthSquared = edge.sqrMagnitude;
            float edgeT = edgeLengthSquared > 0.0001f
                ? Mathf.Clamp01(Vector2.Dot(query - from, edge) / edgeLengthSquared) : 0f;
            float distanceSquared = (query - Vector2.Lerp(from, to, edgeT)).sqrMagnitude;
            if (distanceSquared > bestDistanceSquared) return;
            bestDistanceSquared = distanceSquared;
            bestSegment = segment;
            bestT = Mathf.Lerp(fromT, toT, edgeT);
        }

        private void QueueSplineReapply(TextureSet set)
        {
            if (set == null || (uint)set.activeLayerIndex >= (uint)set.layers.Count) return;
            TexturePaintLayer layer = set.layers[set.activeLayerIndex];
            if (!layer.IsSplineLayer) return;
            pendingSplineSet = set;
            pendingSplineLayer = layer;
            splineReapplyPending = true;
        }

        private void ReapplyPendingSpline()
        {
            if (!splineReapplyPending) return;
            TextureSet set = pendingSplineSet;
            TexturePaintLayer layer = pendingSplineLayer;
            splineReapplyPending = false;
            pendingSplineSet = null;
            pendingSplineLayer = null;
            if (controller?.Textures == null || set == null || layer == null) return;
            int setIndex = -1;
            for (int i = 0; i < controller.Textures.Sets.Count; i++)
                if (ReferenceEquals(controller.Textures.Sets[i], set)) { setIndex = i; break; }
            int layerIndex = set.layers.IndexOf(layer);
            if (setIndex < 0 || layerIndex < 0 || !layer.IsSplineLayer) return;
            selectedSurface = setIndex;
            set.activeLayerIndex = layerIndex;
            spline = layer.spline;
            splineMode = spline?.worldSpace == true;
            ApplySpline();
        }

        private void ApplySpline()
        {
            splineReapplyPending = false;
            pendingSplineSet = null;
            pendingSplineLayer = null;
            if (spline == null || controller?.Textures == null || controller.Textures.Sets.Count == 0) return;
            if (pathMode == TexturePaintPathMode.Ribbon)
            {
                Shader ribbonShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderRoot + "RibbonProjection.shader");
                if (ribbonShader == null || ShaderUtil.ShaderHasError(ribbonShader))
                {
                    ShowWorkspaceStatus("Ribbon projection was not applied because its shader has compilation errors. " +
                        "The existing layer result was left unchanged.");
                    return;
                }
            }
            TextureSet set = controller.Textures.Sets[Mathf.Clamp(selectedSurface, 0, controller.Textures.Sets.Count - 1)];
            EnsureSplineLayer(set);
            TexturePaintLayer splineLayer = set.layers[set.activeLayerIndex];
            TexturePaintLogicalTarget logicalTarget = controller.LogicalLayers?.FindTarget(set);
            TexturePaintLogicalLayerBinding logicalBinding = controller.LogicalLayers?.Resolve(logicalTarget,
                splineLayer.logicalLayerId);
            if (logicalTarget == null || logicalBinding == null || !logicalBinding.complete)
            { ShowWorkspaceStatus(logicalBinding?.error ?? "The spline layer is not bound to a complete paint target."); return; }
            string splineHistoryKey = "texture-paint-spline:" + splineLayer.id;
            splineLayer.proceduralGroupKey = splineHistoryKey;
            spline.UpgradeWorldCurve();
            CaptureSplineSettings(splineLayer);
            for (int i = 0; i < logicalBinding.members.Count; i++)
            {
                TexturePaintLayer peer = logicalBinding.members[i].layer;
                SynchronizeSplinePeer(splineLayer, peer, splineHistoryKey);
            }
            spline = splineLayer.spline;
            List<TextureSet> logicalSets = spline.worldSpace
                ? controller.LogicalLayers.GetTextureSets(logicalTarget)
                : new List<TextureSet> { set };
            if (spline.uvPoints.Count == 0)
            {
                controller.Painting.ClearProceduralResult(splineHistoryKey, splineLayer, logicalSets);
                MarkDocumentDirty();
                return;
            }
            bool ribbonMode = pathMode == TexturePaintPathMode.Ribbon;
            splineLayer.effects ??= new TexturePaintLayerEffects();
            splineLayer.effects.Normalize();
            TexturePaintLayerEffectSettings edgeFade = null;
            for (int effectIndex = 0; effectIndex < splineLayer.effects.Stack.Count; effectIndex++)
            {
                TexturePaintLayerEffectSettings candidate = splineLayer.effects.Stack[effectIndex];
                if (candidate?.kind == TexturePaintLayerEffectKind.EdgeFade && candidate.enabled &&
                    candidate.channel == selectedChannel) { edgeFade = candidate; break; }
            }
            BrushPreset splineBrush = ActiveBrush;
            bool ownsSplineBrush = false;
            if (ribbonMode)
            {
                // A ribbon tile represents the entire source image. A round or soft interactive
                // brush mask would crop every image before the next tile is placed, leaving gaps at
                // the corners and turning dense deposits back into the old narrow strip behavior.
                splineBrush = Instantiate(ActiveBrush);
                splineBrush.name = ActiveBrush.name + " (Ribbon Tile)";
                splineBrush.hideFlags = HideFlags.HideAndDontSave;
                splineBrush.shape = BrushPreset.Shape.Square;
                splineBrush.hardness = 1f;
                ownsSplineBrush = true;
            }
            StrokeContext context = new StrokeContext
            {
                textures = set, geometrySelection = BuildGeometrySelection(), directUV = !spline.worldSpace,
                brush = splineBrush, tool = tool, channel = selectedChannel,
                color = paintColor, strength = strength, paintSource = paintSource,
                limitStrokeCoverage = limitStrokeCoverage,
                pressureAffectsFlow = pressureAffectsFlow, pressureAffectsSize = pressureAffectsSize,
                projectionDepth = projectionDepth, normalAngleLimit = normalAngleLimit, paintBackfaces = paintBackfaces,
                ribbonEdgeFadeEnabled = ribbonMode && edgeFade?.enabled == true,
                ribbonEdgeFadeStart = edgeFade?.edgeFadeStart ?? 0.75f,
                ribbonEdgeFadeSize = edgeFade?.edgeFadeSize ?? 1f,
                ribbonBeginningTexture = TexturePaintSpriteSource.Resolve(
                    ribbonBeginningTexture, ribbonBeginningSprite, selectedChannel, normalConvention),
                ribbonEndTexture = TexturePaintSpriteSource.Resolve(ribbonEndTexture, ribbonEndSprite,
                    selectedChannel, normalConvention),
                ribbonEffects = ribbonMode ? splineLayer.effects.Clone() : null,
                sourceTexture = paintSourceSprite == null ? paintSourceTexture : null,
                sourceSprite = paintSourceSprite, sourceOverlay = paintSourceOverlay,
                normalConvention = normalConvention,
                historyGroupKey = splineHistoryKey, replaceLayer = splineLayer, replaceHistoryGroup = true,
                derivedLayerRaster = true
            };
            PopulateLayerChannelSources(context, splineLayer);
            if ((tool == TexturePaintTool.Paint || tool == TexturePaintTool.Plugin) &&
                context.channelSources.Count == 0 && paintSource == TexturePaintBrushSource.Overlay &&
                !BuildMemberOverlayBindings(context, logicalTarget, logicalSets, out string overlayError))
            {
                if (ownsSplineBrush) DestroyImmediate(splineBrush);
                ShowWorkspaceStatus(overlayError);
                return;
            }
            strokeTextureSets.Clear();
            strokeTextureSets.AddRange(logicalSets);
            Dictionary<TextureSet, int> previousActiveLayers = ActivateSplineResultLayers(
                set, splineLayer, splineHistoryKey, strokeTextureSets);
            bool beganStroke;
            try { beganStroke = controller.Painting.BeginStroke(context, sourceMode, strokeTextureSets); }
            finally
            {
                foreach (KeyValuePair<TextureSet, int> pair in previousActiveLayers)
                    pair.Key.activeLayerIndex = ReferenceEquals(pair.Key, set)
                        ? pair.Key.layers.IndexOf(splineLayer)
                        : Mathf.Clamp(pair.Value, -1, pair.Key.layers.Count - 1);
            }
            if (!beganStroke)
            {
                if (ownsSplineBrush) DestroyImmediate(splineBrush);
                return;
            }
            strokeLogicalTarget = logicalTarget;
            previousContactSamples.Clear();
            spline.EnsureControlPoints();
            if (spline.worldSpace) ReprojectSplineAnchors(spline);
            applyingSpline = true;
            directUVStroke = !spline.worldSpace;
            activeSplineBrush = splineBrush;
            splineDispatchSamples.Clear();
            float pathSpacing = pathMode == TexturePaintPathMode.Stamps
                ? splineBrush.StampSpacing
                : Mathf.Min(splineBrush.StampSpacing, splineBrush.size * 0.2f);
            List<StrokeSample> samples;
            bool sourceAlongY = false;
            bool reverseSourceAxis = false;
            if (ribbonMode)
            {
                float sourceRotation = splineBrush.rotation * Mathf.Deg2Rad;
                float localPathX = Mathf.Cos(sourceRotation);
                float localPathY = -Mathf.Sin(sourceRotation);
                sourceAlongY = Mathf.Abs(localPathY) > Mathf.Abs(localPathX);
                reverseSourceAxis = sourceAlongY ? localPathY < 0f : localPathX < 0f;
                // Both domains use a dense centerline to construct one continuous ribbon. The 3D
                // renderer projects it through the reconstructed mesh; the 2D renderer evaluates
                // the same intrinsic across/along coordinates on a normalized-UV fullscreen quad.
                samples = spline.Sample(Mathf.Max(0.0001f, splineBrush.size * 0.2f),
                    set.surface.index);
            }
            else samples = spline.Sample(pathSpacing, set.surface.index);
            for (int i = 0; i < samples.Count; i++)
            {
                StrokeSample sample = samples[i];
                if (!spline.worldSpace)
                {
                    sample.worldPosition = new Vector3(sample.uv.x, sample.uv.y, 0f);
                    sample.worldNormal = Vector3.forward;
                    sample.projectionDirection = Vector3.back;
                    sample.surfaceIndex = set.surface.index;
                    sample.surfaceId = set.persistentId;
                    sample.triangleIndex = -1;
                    sample.barycentric = Vector3.zero;
                    sample.uvIsland = -1;
                    sample.slotName = string.Empty;
                    samples[i] = sample;
                    continue;
                }
                TextureSet sampleSet = controller.Textures.FindSet(sample.surfaceIndex) ?? set;
                Vector3 world, normal, barycentric;
                Vector2 projectedUV = sample.uv;
                int projectedTriangle;
                bool projected;
                Vector3 query = sample.worldPosition - sample.worldNormal * sample.surfaceOffset;
                projected = TryProjectWorldPathPoint(strokeTextureSets, query, sample.worldNormal,
                    sample.surfaceIndex, sample.triangleIndex, out sampleSet, out world, out normal,
                    out projectedUV, out projectedTriangle, out barycentric);
                if (projected)
                {
                    sample.worldPosition = world + normal * sample.surfaceOffset;
                    sample.worldNormal = normal;
                    sample.uv = projectedUV;
                    sample.triangleIndex = projectedTriangle;
                    sample.barycentric = barycentric;
                    sample.surfaceIndex = sampleSet.surface.index;
                    sample.surfaceId = sampleSet.persistentId;
                    sample.uvIsland = sampleSet.surface.triangleIslands != null &&
                        (uint)projectedTriangle < (uint)sampleSet.surface.triangleIslands.Length
                            ? sampleSet.surface.triangleIslands[projectedTriangle] : -1;
                    sample.slotName = sampleSet.surface.GetTriangleSlotName(projectedTriangle);
                    samples[i] = sample;
                }
            }
            if (spline.worldSpace) RefreshProjectedSplineDirections(samples);
            if (!ribbonMode && !spline.closed && samples.Count > 1)
            {
                if (pathStartCap == TexturePaintPathCap.Square)
                    samples[0] = ExtendPathCap(samples[0], samples[1], -splineBrush.size);
                else if (pathStartCap == TexturePaintPathCap.Butt)
                    samples[0] = ExtendPathCap(samples[0], samples[1], splineBrush.size);
                if (pathEndCap == TexturePaintPathCap.Square)
                    samples[samples.Count - 1] = ExtendPathCap(samples[samples.Count - 1], samples[samples.Count - 2], -splineBrush.size);
                else if (pathEndCap == TexturePaintPathCap.Butt)
                    samples[samples.Count - 1] = ExtendPathCap(samples[samples.Count - 1], samples[samples.Count - 2], splineBrush.size);
            }
            if (pathMode == TexturePaintPathMode.Filled && spline.closed)
                ApplyFilledPath(set, samples);
            bool applied = true;
            if (ribbonMode)
            {
                List<TexturePaintRibbonSegment> ribbonSegments = BuildRibbonSegments(samples,
                    splineBrush.size, splineBrush.size * 2f, spline.closed);
                ribbonSegments = ExpandRibbonCopies(ribbonSegments,
                    controller.Reconstruction.root.transform.position,
                    Mathf.Clamp(radialSymmetry, 1, 16), mirrorX || splineBrush.mirrorStroke,
                    radialSymmetryAxis);
                applied = controller.Painting.ApplyRibbon(ribbonSegments, samples,
                    sourceAlongY, reverseSourceAxis, spline.closed, !spline.worldSpace);
            }
            else
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    if (spline.worldSpace) ApplyBrushFootprint(samples[i]);
                    else QueueDirectUVSplineFootprint(samples[i]);
                }
                applied = controller.Painting.ApplySamples(splineDispatchSamples);
            }
            splineDispatchSamples.Clear();
            applyingSpline = false;
            directUVStroke = false;
            activeSplineBrush = null;
            controller.Painting.EndStroke(applied);
            if (ownsSplineBrush) DestroyImmediate(splineBrush);
            strokeLogicalTarget = null;
            strokeTextureSets.Clear();
            previousContactSamples.Clear();
            if (applied) MarkDocumentDirty();
            else
            {
                // The derived target was cleared before regeneration. If projection fails, refresh
                // the preview now so it cannot continue displaying stale pixels from the old path.
                for (int i = 0; i < logicalSets.Count; i++) logicalSets[i]?.BindPreviewTextures();
                ShowWorkspaceStatus(ribbonMode
                    ? "The continuous ribbon projection shader is unavailable or the current tool is unsupported."
                    : "The spline did not produce a paint result.");
            }
        }

        internal static List<TexturePaintRibbonSegment> BuildRibbonSegments(
            IReadOnlyList<StrokeSample> sourceSamples, float baseHalfWidth, float nominalTileLength,
            bool closed = false)
        {
            List<StrokeSample> samples = new List<StrokeSample>();
            if (sourceSamples == null) return new List<TexturePaintRibbonSegment>();
            for (int i = 0; i < sourceSamples.Count; i++)
            {
                StrokeSample sample = sourceSamples[i];
                if (!float.IsFinite(sample.worldPosition.x) || !float.IsFinite(sample.worldPosition.y) ||
                    !float.IsFinite(sample.worldPosition.z)) continue;
                if (samples.Count > 0 && Vector3.Distance(samples[samples.Count - 1].worldPosition,
                    sample.worldPosition) <= 0.000001f) continue;
                samples.Add(sample);
            }
            if (samples.Count < 2) return new List<TexturePaintRibbonSegment>();

            float[] distances = new float[samples.Count];
            for (int i = 1; i < samples.Count; i++)
                distances[i] = distances[i - 1] + Vector3.Distance(samples[i - 1].worldPosition,
                    samples[i].worldPosition);
            float totalLength = distances[distances.Length - 1] + (closed
                ? Vector3.Distance(samples[samples.Count - 1].worldPosition, samples[0].worldPosition)
                : 0f);
            if (totalLength <= 0.000001f) return new List<TexturePaintRibbonSegment>();
            float requestedTileLength = Mathf.Max(0.0001f, nominalTileLength);
            int tileCount = Mathf.Max(1, Mathf.RoundToInt(totalLength / requestedTileLength));
            float fittedTileLength = totalLength / tileCount;

            int pathSegmentCount = closed ? samples.Count : samples.Count - 1;
            Vector3[] pathDirections = new Vector3[pathSegmentCount];
            float[] pathMidpoints = new float[pathSegmentCount];
            for (int segment = 0; segment < pathSegmentCount; segment++)
            {
                int next = (segment + 1) % samples.Count;
                pathDirections[segment] = (samples[next].worldPosition - samples[segment].worldPosition).normalized;
                float endDistance = next == 0 ? totalLength : distances[next];
                pathMidpoints[segment] = (distances[segment] + endDistance) * 0.5f;
            }

            Vector3[] left = new Vector3[samples.Count];
            Vector3[] right = new Vector3[samples.Count];
            Vector3[] normals = new Vector3[samples.Count];
            Vector3 previousSide = Vector3.zero;
            for (int i = 0; i < samples.Count; i++)
            {
                // Spread a turn over the three closest fitted tiles. A triangular arc-length
                // weight gives the nearest cross section the largest share and tapers the
                // elongation/compression toward its two neighbors instead of forcing one quad to
                // absorb the complete corner.
                float bendRadius = Mathf.Max(0.0001f, fittedTileLength * 1.5f);
                float sampleDistance = distances[i];
                Vector3 tangent = Vector3.zero;
                float tangentWeight = 0f;
                for (int segment = 0; segment < pathSegmentCount; segment++)
                {
                    float delta = Mathf.Abs(pathMidpoints[segment] - sampleDistance);
                    if (closed) delta = Mathf.Min(delta, totalLength - delta);
                    if (delta >= bendRadius) continue;
                    float weight = 1f - delta / bendRadius;
                    tangent += pathDirections[segment] * weight;
                    tangentWeight += weight;
                }
                if (tangentWeight > 0f) tangent /= tangentWeight;
                if (tangent.sqrMagnitude <= 0.00000001f)
                {
                    int fallbackSegment = Mathf.Clamp(i, 0, pathSegmentCount - 1);
                    tangent = pathDirections[fallbackSegment];
                }
                tangent.Normalize();
                Vector3 normal = samples[i].worldNormal.sqrMagnitude > 0.00000001f
                    ? samples[i].worldNormal.normalized : Vector3.up;
                Vector3 side = Vector3.Cross(normal, tangent);
                if (side.sqrMagnitude <= 0.00000001f)
                {
                    Vector3 reference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.95f
                        ? Vector3.up : Vector3.right;
                    side = Vector3.Cross(normal, reference);
                }
                side.Normalize();
                if (previousSide.sqrMagnitude > 0f && Vector3.Dot(side, previousSide) < 0f) side = -side;
                if (Mathf.Abs(samples[i].rotation) > 0.0001f)
                    side = Quaternion.AngleAxis(samples[i].rotation, normal) * side;
                previousSide = side;
                float halfWidth = Mathf.Max(0.0001f, baseHalfWidth) *
                    Mathf.Max(0.01f, samples[i].sizeMultiplier);
                left[i] = samples[i].worldPosition - side * halfWidth;
                right[i] = samples[i].worldPosition + side * halfWidth;
                normals[i] = normal;
            }

            int segmentCount = closed ? samples.Count : samples.Count - 1;
            List<TexturePaintRibbonSegment> result = new List<TexturePaintRibbonSegment>(segmentCount);
            for (int i = 0; i < segmentCount; i++)
            {
                int endIndex = (i + 1) % samples.Count;
                StrokeSample start = samples[i];
                StrokeSample end = samples[endIndex];
                float endDistance = endIndex == 0 ? totalLength : distances[endIndex];
                result.Add(new TexturePaintRibbonSegment
                {
                    leftStartAlong = new Vector4(left[i].x, left[i].y, left[i].z,
                        distances[i] / fittedTileLength),
                    rightStartFlow = new Vector4(right[i].x, right[i].y, right[i].z,
                        Mathf.Max(0f, start.flowMultiplier)),
                    leftEndAlong = new Vector4(left[endIndex].x, left[endIndex].y, left[endIndex].z,
                        endDistance / fittedTileLength),
                    rightEndFlow = new Vector4(right[endIndex].x, right[endIndex].y, right[endIndex].z,
                        Mathf.Max(0f, end.flowMultiplier)),
                    normalStartPressure = new Vector4(normals[i].x, normals[i].y, normals[i].z,
                        Mathf.Clamp01(start.pressure)),
                    normalEndPressure = new Vector4(normals[endIndex].x, normals[endIndex].y, normals[endIndex].z,
                        Mathf.Clamp01(end.pressure)),
                    colorStart = start.hasColor ? (Vector4)start.color : Vector4.one,
                    colorEnd = end.hasColor ? (Vector4)end.color : Vector4.one
                });
            }
            return result;
        }

        private static List<TexturePaintRibbonSegment> ExpandRibbonCopies(
            IReadOnlyList<TexturePaintRibbonSegment> source, Vector3 pivot, int radialCopies, bool mirror,
            Vector3 symmetryAxis)
        {
            List<TexturePaintRibbonSegment> result = new List<TexturePaintRibbonSegment>(
                (source?.Count ?? 0) * Mathf.Max(1, radialCopies) * (mirror ? 2 : 1));
            if (source == null) return result;
            int copies = Mathf.Max(1, radialCopies);
            for (int copy = 0; copy < copies; copy++)
            {
                Vector3 axis = symmetryAxis.sqrMagnitude > 0.000001f ? symmetryAxis.normalized : Vector3.up;
                Quaternion rotation = Quaternion.AngleAxis(copy * 360f / copies, axis);
                for (int i = 0; i < source.Count; i++)
                {
                    TexturePaintRibbonSegment transformed = TransformRibbonSegment(source[i], pivot, rotation, false);
                    result.Add(transformed);
                    if (mirror) result.Add(TransformRibbonSegment(transformed, Vector3.zero, Quaternion.identity, true));
                }
            }
            return result;
        }

        private static TexturePaintRibbonSegment TransformRibbonSegment(TexturePaintRibbonSegment source,
            Vector3 pivot, Quaternion rotation, bool mirrorX)
        {
            source.leftStartAlong = TransformRibbonPoint(source.leftStartAlong, pivot, rotation, mirrorX);
            source.rightStartFlow = TransformRibbonPoint(source.rightStartFlow, pivot, rotation, mirrorX);
            source.leftEndAlong = TransformRibbonPoint(source.leftEndAlong, pivot, rotation, mirrorX);
            source.rightEndFlow = TransformRibbonPoint(source.rightEndFlow, pivot, rotation, mirrorX);
            source.normalStartPressure = TransformRibbonDirection(source.normalStartPressure, rotation, mirrorX);
            source.normalEndPressure = TransformRibbonDirection(source.normalEndPressure, rotation, mirrorX);
            return source;
        }

        private static Vector4 TransformRibbonPoint(Vector4 source, Vector3 pivot,
            Quaternion rotation, bool mirrorX)
        {
            Vector3 point = pivot + rotation * (new Vector3(source.x, source.y, source.z) - pivot);
            if (mirrorX) point = TexturePaintMath.MirrorAcrossGlobalX(point);
            return new Vector4(point.x, point.y, point.z, source.w);
        }

        private static Vector4 TransformRibbonDirection(Vector4 source, Quaternion rotation, bool mirrorX)
        {
            Vector3 direction = rotation * new Vector3(source.x, source.y, source.z);
            if (mirrorX) direction = TexturePaintMath.MirrorDirectionAcrossGlobalX(direction);
            return new Vector4(direction.x, direction.y, direction.z, source.w);
        }

        internal static Dictionary<TextureSet, int> ActivateSplineResultLayers(TextureSet primarySet,
            TexturePaintLayer primaryLayer, string groupKey, IReadOnlyList<TextureSet> targetSets)
        {
            var previous = new Dictionary<TextureSet, int>();
            if (primarySet == null || primaryLayer == null || string.IsNullOrEmpty(groupKey) || targetSets == null)
                return previous;
            primaryLayer.proceduralGroupKey = groupKey;
            for (int setIndex = 0; setIndex < targetSets.Count; setIndex++)
            {
                TextureSet targetSet = targetSets[setIndex];
                if (targetSet == null || previous.ContainsKey(targetSet)) continue;
                previous[targetSet] = targetSet.activeLayerIndex;
                if (ReferenceEquals(targetSet, primarySet))
                {
                    targetSet.activeLayerIndex = targetSet.layers.IndexOf(primaryLayer);
                    continue;
                }
                TexturePaintLayer linked = !string.IsNullOrEmpty(primaryLayer.logicalLayerId)
                    ? TexturePaintLogicalLayerController.FindLayer(targetSet, primaryLayer.logicalLayerId) : null;
                for (int layerIndex = 0; layerIndex < targetSet.layers.Count; layerIndex++)
                {
                    if (linked != null) break;
                    if (string.Equals(targetSet.layers[layerIndex].proceduralGroupKey, groupKey,
                        StringComparison.Ordinal))
                    { linked = targetSet.layers[layerIndex]; break; }
                }
                if (linked == null)
                {
                    linked = targetSet.AddLayer(primaryLayer.name + " · Linked Path Result");
                    linked.proceduralGroupKey = groupKey;
                }
                linked.logicalLayerId = primaryLayer.logicalLayerId;
                linked.paintTargetId = primaryLayer.paintTargetId;
                linked.proceduralGroupKey = groupKey;
                linked.visible = primaryLayer.visible;
                linked.opacity = primaryLayer.opacity;
                linked.blendMode = primaryLayer.blendMode;
                targetSet.activeLayerIndex = targetSet.layers.IndexOf(linked);
            }
            return previous;
        }

        private void ReprojectSplineAnchors(TexturePaintSpline targetSpline)
        {
            targetSpline.EnsureControlPoints();
            for (int point = 0; point < targetSpline.PointCount; point++)
            {
                TexturePaintSurfaceAnchor anchor = targetSpline.anchors[point];
                TextureSet anchorSet = null;
                for (int i = 0; i < controller.Textures.Sets.Count; i++)
                {
                    TextureSet candidate = controller.Textures.Sets[i];
                    if ((!string.IsNullOrEmpty(anchor.surfaceId) && candidate.persistentId == anchor.surfaceId) ||
                        (string.IsNullOrEmpty(anchor.surfaceId) && candidate.surface.index == anchor.surfaceIndex))
                    { anchorSet = candidate; break; }
                }
                Mesh mesh = anchorSet?.surface?.mesh;
                if (mesh == null || anchor.triangleIndex < 0) continue;
                int[] triangles = mesh.triangles;
                int offset = anchor.triangleIndex * 3;
                if (offset + 2 >= triangles.Length) continue;
                Vector3[] vertices = mesh.vertices; Vector3[] normals = mesh.normals; Vector2[] uv = mesh.uv;
                int a = triangles[offset], b = triangles[offset + 1], c = triangles[offset + 2];
                Vector3 local = vertices[a] * anchor.barycentric.x + vertices[b] * anchor.barycentric.y + vertices[c] * anchor.barycentric.z;
                Vector3 localNormal = normals.Length == vertices.Length
                    ? normals[a] * anchor.barycentric.x + normals[b] * anchor.barycentric.y + normals[c] * anchor.barycentric.z
                    : Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                Vector3 worldNormal = anchorSet.surface.gameObject.transform.TransformDirection(localNormal).normalized;
                Vector3 reprojected = anchorSet.surface.gameObject.transform.TransformPoint(local) + worldNormal * anchor.normalOffset;
                Vector3 delta = reprojected - targetSpline.worldPoints[point];
                targetSpline.worldPoints[point] = reprojected;
                targetSpline.worldInControls[point] += delta;
                targetSpline.worldOutControls[point] += delta;
                targetSpline.worldNormals[point] = worldNormal;
                if (uv.Length == vertices.Length)
                    targetSpline.uvPoints[point] = uv[a] * anchor.barycentric.x + uv[b] * anchor.barycentric.y + uv[c] * anchor.barycentric.z;
                targetSpline.surfaceIndices[point] = anchorSet.surface.index;
                targetSpline.triangleIndices[point] = anchor.triangleIndex;
            }
        }

        private static void UpdateSplineAnchorFromUV(TextureSet set, TexturePaintSpline targetSpline, int point)
        {
            if (set?.surface == null || (uint)point >= (uint)targetSpline.PointCount) return;
            int preferred = targetSpline.triangleIndices[point];
            if (!set.surface.TryUVToWorld(targetSpline.uvPoints[point], preferred, out Vector3 world,
                out Vector3 normal, out int triangle, out Vector3 barycentric)) return;
            Vector3 delta = world - targetSpline.worldPoints[point];
            targetSpline.worldPoints[point] = world;
            targetSpline.worldInControls[point] += delta;
            targetSpline.worldOutControls[point] += delta;
            targetSpline.worldNormals[point] = normal;
            targetSpline.triangleIndices[point] = triangle;
            targetSpline.surfaceIndices[point] = set.surface.index;
            targetSpline.anchors[point] = new TexturePaintSurfaceAnchor
            {
                surfaceId = set.persistentId,
                surfaceIndex = set.surface.index,
                triangleIndex = triangle,
                barycentric = barycentric,
                normal = normal
            };
        }

        private static void UpdateSplineAnchorFromCurrentDomain(TextureSet set,
            TexturePaintSpline targetSpline, int point)
        {
            if (targetSpline?.worldSpace == true) UpdateSplineAnchorFromWorld(set, targetSpline, point);
            else NormalizeTwoDimensionalSplinePoint(set, targetSpline, point);
        }

        private static void UpdateSplineAnchorFromWorld(TextureSet set, TexturePaintSpline targetSpline, int point)
        {
            if (set?.surface == null || targetSpline == null || (uint)point >= (uint)targetSpline.PointCount) return;
            int preferred = point < targetSpline.triangleIndices.Count ? targetSpline.triangleIndices[point] : -1;
            Vector3 hint = point < targetSpline.worldNormals.Count ? targetSpline.worldNormals[point] : Vector3.zero;
            if (!set.surface.TryClosestSurfacePoint(targetSpline.worldPoints[point], hint, preferred,
                out Vector3 world, out Vector3 normal, out Vector2 uv, out int triangle,
                out Vector3 barycentric)) return;
            Vector3 delta = world - targetSpline.worldPoints[point];
            targetSpline.worldPoints[point] = world;
            targetSpline.worldInControls[point] += delta;
            targetSpline.worldOutControls[point] += delta;
            targetSpline.uvPoints[point] = uv;
            targetSpline.worldNormals[point] = normal;
            targetSpline.triangleIndices[point] = triangle;
            targetSpline.surfaceIndices[point] = set.surface.index;
            targetSpline.anchors[point] = new TexturePaintSurfaceAnchor
            {
                surfaceId = set.persistentId,
                surfaceIndex = set.surface.index,
                triangleIndex = triangle,
                barycentric = barycentric,
                normal = normal
            };
        }

        private static void RefreshProjectedSplineDirections(List<StrokeSample> samples)
        {
            for (int i = 0; i < samples.Count; i++)
            {
                StrokeSample sample = samples[i];
                StrokeSample neighbor = i + 1 < samples.Count ? samples[i + 1]
                    : i > 0 ? samples[i - 1] : sample;
                Vector3 direction = i + 1 < samples.Count
                    ? neighbor.worldPosition - sample.worldPosition
                    : sample.worldPosition - neighbor.worldPosition;
                sample.previousWorldPosition = i > 0 ? samples[i - 1].worldPosition : sample.worldPosition;
                sample.previousUV = i > 0 ? samples[i - 1].uv : sample.uv;
                sample.direction = direction.sqrMagnitude > 0.00000001f ? direction.normalized : Vector3.zero;
                sample.projectionDirection = -sample.worldNormal;
                samples[i] = sample;
            }
        }

        /// <summary>
        /// Resolves a canonical world-space curve point against the union of the logical paint
        /// target. Within one reconstructed surface, the control point's polygon component is a
        /// continuity constraint: nearby but disconnected layers must not steal the curve. Separate
        /// surfaces remain eligible so slot and UDIM boundaries can still be crossed.
        /// </summary>
        private bool TryProjectWorldPathPoint(IReadOnlyList<TextureSet> candidateSets,
            Vector3 worldPoint, Vector3 normalHint, int preferredSurfaceIndex, int preferredTriangle,
            out TextureSet projectedSet, out Vector3 surfacePoint, out Vector3 surfaceNormal,
            out Vector2 surfaceUV, out int triangleIndex, out Vector3 barycentric)
        {
            projectedSet = null;
            surfacePoint = Vector3.zero;
            surfaceNormal = Vector3.up;
            surfaceUV = Vector2.zero;
            triangleIndex = -1;
            barycentric = Vector3.zero;
            if (candidateSets == null) return false;

            float bestDistance = float.MaxValue;
            bool bestIsPreferred = false;
            for (int setIndex = 0; setIndex < candidateSets.Count; setIndex++)
            {
                TextureSet candidateSet = candidateSets[setIndex];
                ReconstructedSurface candidateSurface = candidateSet?.surface;
                if (candidateSurface == null || !IsSurfaceSelected(candidateSurface)) continue;
                bool isPreferred = candidateSurface.index == preferredSurfaceIndex;
                int candidatePreferredTriangle = isPreferred ? preferredTriangle : -1;
                if (!candidateSurface.TryClosestSurfacePoint(worldPoint, normalHint,
                    candidatePreferredTriangle, selectedSlots, out Vector3 candidatePoint,
                    out Vector3 candidateNormal, out Vector2 candidateUV, out int candidateTriangle,
                    out Vector3 candidateBarycentric)) continue;

                float distance = (candidatePoint - worldPoint).sqrMagnitude;
                bool tied = Mathf.Abs(distance - bestDistance) <= 0.0000000001f;
                if (distance > bestDistance && !tied || tied && bestIsPreferred && !isPreferred) continue;
                bestDistance = distance;
                bestIsPreferred = isPreferred;
                projectedSet = candidateSet;
                surfacePoint = candidatePoint;
                surfaceNormal = candidateNormal;
                surfaceUV = candidateUV;
                triangleIndex = candidateTriangle;
                barycentric = candidateBarycentric;
            }
            if (projectedSet == null) return false;

            // Nearest-point projection alone is ambiguous for layered clothing: the world curve
            // between two belt/strap controls may pass closer to a disconnected polygon layer
            // underneath. If that competing triangle belongs to the same reconstructed mesh,
            // resolve the sample against the control's connected polygon strip instead.
            TextureSet continuitySet = null;
            if (preferredTriangle >= 0)
            {
                for (int setIndex = 0; setIndex < candidateSets.Count; setIndex++)
                {
                    TextureSet candidateSet = candidateSets[setIndex];
                    if (candidateSet?.surface?.index == preferredSurfaceIndex)
                    {
                        continuitySet = candidateSet;
                        break;
                    }
                }
            }
            bool constrainDirectedProjection = continuitySet?.surface != null &&
                ReferenceEquals(projectedSet, continuitySet);
            if (constrainDirectedProjection &&
                !continuitySet.surface.AreTrianglesTopologyConnected(preferredTriangle, triangleIndex))
            {
                if (continuitySet.surface.TryClosestConnectedSurfacePoint(worldPoint, normalHint,
                    preferredTriangle, selectedSlots, out Vector3 connectedPoint,
                    out Vector3 connectedNormal, out Vector2 connectedUV, out int connectedTriangle,
                    out Vector3 connectedBarycentric))
                {
                    projectedSet = continuitySet;
                    surfacePoint = connectedPoint;
                    surfaceNormal = connectedNormal;
                    surfaceUV = connectedUV;
                    triangleIndex = connectedTriangle;
                    barycentric = connectedBarycentric;
                    bestDistance = (connectedPoint - worldPoint).sqrMagnitude;
                }
                else constrainDirectedProjection = false;
            }

            // Nearest-point projection is robust fallback behavior, but its tangent coordinates can
            // jump when two duplicated UV-boundary triangles are almost equally near. Prefer a hit
            // along the spline's interpolated normal: this changes depth only, so UV/slot topology
            // cannot introduce a sideways kink into a world-authored curve.
            float nearestDistance = Mathf.Sqrt(bestDistance);
            float maximumDirectedDistance = nearestDistance * 2f + 0.001f;
            TextureSet directedSet = null;
            Vector3 directedPoint = Vector3.zero, directedNormal = Vector3.up, directedBarycentric = Vector3.zero;
            Vector2 directedUV = Vector2.zero;
            int directedTriangle = -1;
            float directedDistance = float.MaxValue;
            bool directedIsPreferred = false;
            for (int setIndex = 0; setIndex < candidateSets.Count; setIndex++)
            {
                TextureSet candidateSet = candidateSets[setIndex];
                ReconstructedSurface candidateSurface = candidateSet?.surface;
                if (candidateSurface == null || !IsSurfaceSelected(candidateSurface)) continue;
                if (constrainDirectedProjection && !ReferenceEquals(candidateSet, continuitySet)) continue;
                if (!candidateSurface.TryProjectAlongNormal(worldPoint, normalHint, selectedSlots,
                    out Vector3 candidatePoint, out Vector3 candidateNormal, out Vector2 candidateUV,
                    out int candidateTriangle, out Vector3 candidateBarycentric)) continue;

                if (constrainDirectedProjection && ReferenceEquals(candidateSet, continuitySet) &&
                    !candidateSurface.AreTrianglesTopologyConnected(preferredTriangle, candidateTriangle))
                    continue;

                float distance = Vector3.Distance(candidatePoint, worldPoint);
                if (distance > maximumDirectedDistance) continue;
                bool isPreferred = candidateSurface.index == preferredSurfaceIndex;
                bool tied = Mathf.Abs(distance - directedDistance) <= 0.000001f;
                if (distance > directedDistance && !tied || tied && directedIsPreferred && !isPreferred) continue;
                directedDistance = distance;
                directedIsPreferred = isPreferred;
                directedSet = candidateSet;
                directedPoint = candidatePoint;
                directedNormal = candidateNormal;
                directedUV = candidateUV;
                directedTriangle = candidateTriangle;
                directedBarycentric = candidateBarycentric;
            }
            if (directedSet == null) return true;
            projectedSet = directedSet;
            surfacePoint = directedPoint;
            surfaceNormal = directedNormal;
            surfaceUV = directedUV;
            triangleIndex = directedTriangle;
            barycentric = directedBarycentric;
            return true;
        }

        private IReadOnlyList<TextureSet> GetSplineProjectionSets(ReconstructedSurface fallbackSurface)
        {
            TexturePaintLogicalTarget target = ActiveLogicalTarget;
            if (target != null)
            {
                List<TextureSet> targetSets = controller?.LogicalLayers?.GetTextureSets(target);
                if (targetSets != null && targetSets.Count > 0) return targetSets;
            }
            TextureSet fallback = fallbackSurface != null
                ? controller?.Textures?.FindSet(fallbackSurface.index) : null;
            return fallback != null ? new[] { fallback } : Array.Empty<TextureSet>();
        }

        private static StrokeSample ExtendPathCap(StrokeSample endpoint, StrokeSample neighbor, float distance)
        {
            Vector3 direction = (neighbor.worldPosition - endpoint.worldPosition).normalized;
            endpoint.worldPosition += direction * distance;
            endpoint.previousWorldPosition = endpoint.worldPosition;
            return endpoint;
        }

        private void ApplyFilledPath(TextureSet set, List<StrokeSample> boundary)
        {
            if (boundary == null || boundary.Count < 3) return;
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            for (int i = 0; i < boundary.Count; i++)
            {
                Vector2 uv = boundary[i].uv;
                xMin = Mathf.Min(xMin, uv.x); yMin = Mathf.Min(yMin, uv.y);
                xMax = Mathf.Max(xMax, uv.x); yMax = Mathf.Max(yMax, uv.y);
            }
            int preferredTriangle = boundary[0].triangleIndex;
            float uvStep = spline?.worldSpace == false
                ? Mathf.Max(0.0002f, ActiveBrush.size * 0.75f)
                : Mathf.Max(0.0002f,
                    set.surface.CalculateUVRadius(preferredTriangle, ActiveBrush.size) * 0.75f);
            for (float y = yMin; y <= yMax; y += uvStep)
            for (float x = xMin; x <= xMax; x += uvStep)
            {
                Vector2 uv = new Vector2(x, y);
                if (!PointInPolygon(boundary, uv)) continue;
                if (spline?.worldSpace == false)
                {
                    QueueDirectUVSplineFootprint(new StrokeSample(new Vector3(uv.x, uv.y, 0f),
                        Vector3.forward, uv, set.surface.index, -1));
                    continue;
                }
                if (!set.surface.TryUVToWorld(uv, preferredTriangle, out Vector3 world,
                    out Vector3 normal, out int triangle, out Vector3 barycentric)) continue;
                StrokeSample fill = new StrokeSample(world, normal, uv, set.surface.index, triangle)
                { barycentric = barycentric, sizeMultiplier = 1f, flowMultiplier = 1f };
                ApplyBrushFootprint(fill);
            }
        }

        private static bool PointInPolygon(List<StrokeSample> polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[i].uv, b = polygon[j].uv;
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        internal bool TryGetActiveLayer(out TextureSet set, out TexturePaintLayer layer)
        {
            set = null; layer = null;
            if (controller?.Textures == null || controller.Textures.Sets.Count == 0) return false;
            set = controller.Textures.Sets[Mathf.Clamp(selectedSurface, 0, controller.Textures.Sets.Count - 1)];
            if ((uint)set.activeLayerIndex >= (uint)set.layers.Count) return false;
            layer = set.layers[set.activeLayerIndex];
            return true;
        }

        private bool HandleGeometryFill(Event current, bool targetHover)
        {
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                geometryFillMode = 0;
                ShowWorkspaceStatus("Geometry fill cancelled");
                current.Use();
                return true;
            }
            if (current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (current.type != EventType.MouseDown || current.button != 0 || current.alt) return false;
            if (!targetHover) return true;
            TextureSet set = controller.Textures.FindSet(hoverSurface.index);
            if (!CanStartFreehandPaint(set))
            {
                ShowPaintLayerRequiredStatus(set);
                current.Use();
                return true;
            }
            ApplyGeometryFill(set, MakeSample(hoverSurface, hoverHit, hoverHit.textureCoord));
            current.Use();
            return true;
        }

        private void ApplyGeometryFill(TextureSet set, StrokeSample sample)
        {
            if (set == null || geometryFillMode == 0) return;
            BeginPaintAt(set, sample, false);
            if (!strokeActive) return;
            bool filled = controller.Painting.FillActiveGeometry(sample, geometryFillMode == 2);
            EndPaint();
            if (filled)
                ShowWorkspaceStatus(geometryFillMode == 2
                    ? "Filled UV island" : "Filled polygon");
        }

        private TexturePaintGeometrySelection BuildGeometrySelection()
        {
            // Geometry clipping remains internal to individual paint operations. Layer masking is
            // now an editable per-layer texture and is evaluated by the layer compositor.
            return new TexturePaintGeometrySelection();
        }

        private void CaptureSplineSettings(TexturePaintLayer layer)
        {
            if (layer == null) return;
            TexturePaintSplineSettings next = CreateSplineSettings();
            layer.splineSettings = next;
            if (controller?.Textures == null || string.IsNullOrEmpty(layer.logicalLayerId)) return;
            for (int setIndex = 0; setIndex < controller.Textures.Sets.Count; setIndex++)
            {
                TextureSet peerSet = controller.Textures.Sets[setIndex];
                for (int layerIndex = 0; layerIndex < peerSet.layers.Count; layerIndex++)
                {
                    TexturePaintLayer peer = peerSet.layers[layerIndex];
                    if (ReferenceEquals(peer, layer) || !peer.IsSplineLayer ||
                        !string.Equals(peer.logicalLayerId, layer.logicalLayerId, StringComparison.Ordinal) ||
                        !string.Equals(peer.paintTargetId, layer.paintTargetId, StringComparison.Ordinal)) continue;
                    peer.splineSettings = next.Clone();
                }
            }
        }

        private TexturePaintLayerSettings CreatePaintLayerSettings()
        {
            BrushPreset active = ActiveBrush;
            return new TexturePaintLayerSettings
            {
                tool = tool,
                channel = selectedChannel,
                source = paintSource,
                destination = TexturePaintSourceMode.SourceOverlay,
                brush = brush,
                brushShape = active.shape,
                brushSize = active.size,
                brushHardness = active.hardness,
                brushFlow = active.flow,
                brushSpacing = active.spacing,
                brushRotation = active.rotation,
                brushBlendMode = active.blendMode,
                brushMirrorStroke = active.mirrorStroke,
                brushAlignToStroke = active.alignToStroke,
                brushStamp = active.stampSprite == null ? active.stampTexture : null,
                brushStampSprite = active.stampSprite,
                brushRandomizationVersion = 1,
                brushRandomRotation = active.randomRotation,
                brushRandomSizeVariation = active.randomSizeVariation,
                brushRandomSizeShrink = active.randomSizeShrink,
                brushRandomSizeGrow = active.randomSizeGrow,
                brushSplatter = active.splatter,
                brushSplatterDistance = active.splatterDistance,
                brushRandomStrength = active.randomStrength,
                brushFade = active.fade,
                brushTaper = active.taper,
                brushFadeTaperLength = active.fadeTaperLength,
                sourceTexture = paintSourceSprite == null ? paintSourceTexture : null,
                sourceSprite = paintSourceSprite,
                sourceOverlay = paintSourceOverlay,
                color = paintColor,
                normalConvention = normalConvention,
                strength = strength,
                limitStrokeCoverage = limitStrokeCoverage,
                mirrorX = mirrorX,
                stabilization = strokeStabilization,
                directionSmoothing = directionSmoothing,
                projectionDepth = projectionDepth,
                normalAngleLimit = normalAngleLimit,
                paintBackfaces = paintBackfaces,
                pressureAffectsFlow = pressureAffectsFlow,
                pressureAffectsSize = pressureAffectsSize
            };
        }

        private void RestorePaintLayerSettings(TexturePaintLayerSettings settings)
        {
            if (settings == null) return;
            tool = settings.tool;
            selectedChannel = settings.channel;
            paintSource = settings.source;
            normalConvention = settings.normalConvention;
            // Paint-layer settings from older documents may contain SourceTexture. A layer's
            // pixels must remain non-destructive and owned by that layer.
            settings.destination = TexturePaintSourceMode.SourceOverlay;
            sourceMode = TexturePaintSourceMode.SourceOverlay;
            RestorePaintSource(settings.sourceTexture, settings.sourceSprite);
            paintSourceOverlay = settings.sourceOverlay;
            paintColor = settings.color;
            strength = settings.strength;
            limitStrokeCoverage = settings.limitStrokeCoverage;
            mirrorX = settings.mirrorX;
            strokeStabilization = settings.stabilization;
            directionSmoothing = settings.directionSmoothing;
            projectionDepth = settings.projectionDepth;
            normalAngleLimit = settings.normalAngleLimit;
            paintBackfaces = settings.paintBackfaces;
            pressureAffectsFlow = settings.pressureAffectsFlow;
            pressureAffectsSize = settings.pressureAffectsSize;
            // The embedded values are the per-layer snapshot. Retain the originating preset only
            // as an explicit update target; it must not override the saved layer state.
            brush = settings.brush;
            BrushPreset target = transientBrush;
            target.shape = settings.brushShape;
            target.size = Mathf.Max(0.001f, settings.brushSize);
            target.hardness = settings.brushHardness;
            target.flow = settings.brushFlow;
            target.spacing = Mathf.Max(0.01f, settings.brushSpacing);
            target.rotation = settings.brushRotation;
            target.blendMode = settings.brushBlendMode;
            target.mirrorStroke = settings.brushMirrorStroke;
            target.alignToStroke = settings.brushAlignToStroke;
            target.stampSprite = settings.brushStampSprite;
            target.stampTexture = settings.brushStampSprite == null ? settings.brushStamp : null;
            bool hasRandomization = settings.brushRandomizationVersion > 0;
            target.randomRotation = hasRandomization && settings.brushRandomRotation;
            target.randomSizeVariation = hasRandomization && settings.brushRandomSizeVariation;
            target.randomSizeShrink = hasRandomization
                ? Mathf.Clamp01(settings.brushRandomSizeShrink) : 0.3f;
            target.randomSizeGrow = hasRandomization
                ? Mathf.Clamp01(settings.brushRandomSizeGrow) : 0.3f;
            target.splatter = settings.brushSplatter;
            target.splatterDistance = Mathf.Clamp(settings.brushSplatterDistance, 0.01f, 2f);
            target.randomStrength = settings.brushRandomStrength;
            target.fade = settings.brushFade;
            target.taper = settings.brushTaper;
            target.fadeTaperLength = Mathf.Max(0f, settings.brushFadeTaperLength);
        }

        private void CaptureActivePaintLayerSettings()
        {
            TextureSet set = ActiveTextureSet;
            if (set == null || (uint)set.activeLayerIndex >= (uint)set.layers.Count) return;
            // Mask Mode owns an independent grayscale paint value. Its temporary UI state must
            // never overwrite the selected layer's ordinary multi-channel paint sources.
            if (IsLayerMaskMode(set)) return;
            TexturePaintLayer layer = set.layers[set.activeLayerIndex];
            if (layer?.kind != TexturePaintLayerKind.Paint) return;
            TexturePaintLayerSettings next = CreatePaintLayerSettings();
            if (PaintLayerSettingsEqual(layer.paintSettings, next)) return;
            layer.paintSettings = next;
            if (controller?.Textures != null && !string.IsNullOrEmpty(layer.logicalLayerId))
            {
                for (int setIndex = 0; setIndex < controller.Textures.Sets.Count; setIndex++)
                {
                    TextureSet peerSet = controller.Textures.Sets[setIndex];
                    for (int layerIndex = 0; layerIndex < peerSet.layers.Count; layerIndex++)
                    {
                        TexturePaintLayer peer = peerSet.layers[layerIndex];
                        if (ReferenceEquals(peer, layer) || peer.kind != TexturePaintLayerKind.Paint ||
                            !string.Equals(peer.logicalLayerId, layer.logicalLayerId, StringComparison.Ordinal) ||
                            !string.Equals(peer.paintTargetId, layer.paintTargetId, StringComparison.Ordinal)) continue;
                        peer.paintSettings = next.Clone();
                    }
                }
            }
            MarkDocumentDirty();
        }

        private static bool PaintLayerSettingsEqual(TexturePaintLayerSettings a, TexturePaintLayerSettings b)
        {
            return a != null && b != null && a.tool == b.tool && a.channel == b.channel &&
                a.source == b.source && a.destination == b.destination && a.brush == b.brush &&
                a.brushShape == b.brushShape && a.brushSize == b.brushSize &&
                a.brushHardness == b.brushHardness && a.brushFlow == b.brushFlow &&
                a.brushSpacing == b.brushSpacing && a.brushRotation == b.brushRotation &&
                a.brushBlendMode == b.brushBlendMode && a.brushMirrorStroke == b.brushMirrorStroke &&
                a.brushAlignToStroke == b.brushAlignToStroke && a.brushStamp == b.brushStamp &&
                a.brushStampSprite == b.brushStampSprite &&
                a.brushRandomizationVersion == b.brushRandomizationVersion &&
                a.brushRandomRotation == b.brushRandomRotation &&
                a.brushRandomSizeVariation == b.brushRandomSizeVariation &&
                a.brushRandomSizeShrink == b.brushRandomSizeShrink &&
                a.brushRandomSizeGrow == b.brushRandomSizeGrow &&
                a.brushSplatter == b.brushSplatter &&
                a.brushSplatterDistance == b.brushSplatterDistance &&
                a.brushRandomStrength == b.brushRandomStrength &&
                a.brushFade == b.brushFade && a.brushTaper == b.brushTaper &&
                a.brushFadeTaperLength == b.brushFadeTaperLength &&
                a.sourceTexture == b.sourceTexture && a.sourceSprite == b.sourceSprite &&
                a.sourceOverlay == b.sourceOverlay && a.color == b.color &&
                a.normalConvention == b.normalConvention &&
                a.strength == b.strength && a.limitStrokeCoverage == b.limitStrokeCoverage &&
                a.mirrorX == b.mirrorX && a.stabilization == b.stabilization &&
                a.directionSmoothing == b.directionSmoothing && a.projectionDepth == b.projectionDepth &&
                a.normalAngleLimit == b.normalAngleLimit && a.paintBackfaces == b.paintBackfaces &&
                a.pressureAffectsFlow == b.pressureAffectsFlow && a.pressureAffectsSize == b.pressureAffectsSize;
        }

        private TexturePaintSplineSettings CreateSplineSettings()
        {
            BrushPreset active = ActiveBrush;
            return new TexturePaintSplineSettings
            {
                tool = tool,
                channel = selectedChannel,
                source = paintSource,
                destination = TexturePaintSourceMode.SourceOverlay,
                brush = brush,
                brushShape = active.shape,
                brushSize = active.size,
                brushHardness = active.hardness,
                brushFlow = active.flow,
                brushSpacing = active.spacing,
                brushRotation = active.rotation,
                brushBlendMode = active.blendMode,
                brushMirrorStroke = active.mirrorStroke,
                brushAlignToStroke = active.alignToStroke,
                brushStamp = active.stampSprite == null ? active.stampTexture : null,
                brushStampSprite = active.stampSprite,
                brushRandomizationVersion = 1,
                brushRandomRotation = active.randomRotation,
                brushRandomSizeVariation = active.randomSizeVariation,
                brushRandomSizeShrink = active.randomSizeShrink,
                brushRandomSizeGrow = active.randomSizeGrow,
                brushSplatter = active.splatter,
                brushSplatterDistance = active.splatterDistance,
                brushRandomStrength = active.randomStrength,
                brushFade = active.fade,
                brushTaper = active.taper,
                brushFadeTaperLength = active.fadeTaperLength,
                sourceTexture = paintSourceSprite == null ? paintSourceTexture : null,
                sourceSprite = paintSourceSprite,
                ribbonBeginningTexture = ribbonBeginningSprite == null ? ribbonBeginningTexture : null,
                ribbonBeginningSprite = ribbonBeginningSprite,
                ribbonEndTexture = ribbonEndSprite == null ? ribbonEndTexture : null,
                ribbonEndSprite = ribbonEndSprite,
                sourceOverlay = paintSourceOverlay,
                color = paintColor,
                normalConvention = normalConvention,
                strength = strength,
                limitStrokeCoverage = limitStrokeCoverage,
                mirrorX = mirrorX,
                pathMode = pathMode,
                orientation = pathOrientation,
                startCap = pathStartCap,
                endCap = pathEndCap,
                radialSymmetry = radialSymmetry,
                symmetryAxis = radialSymmetryAxis,
                stabilization = strokeStabilization,
                directionSmoothing = directionSmoothing,
                projectionDepth = projectionDepth,
                normalAngleLimit = normalAngleLimit,
                paintBackfaces = paintBackfaces,
                pressureAffectsFlow = pressureAffectsFlow,
                pressureAffectsSize = pressureAffectsSize
            };
        }

        private void DrawPathStampSpacingControl()
        {
            BrushPreset active = ActiveBrush;
            if (pathMode == TexturePaintPathMode.Ribbon)
            {
                EditorGUILayout.LabelField(new GUIContent("Ribbon Tile Length",
                    "Nominal path length occupied by one complete source image. The final integer tile count is fitted slightly so both path ends meet complete tile edges."),
                    new GUIContent((active.size * 2f).ToString("0.#####") + " world units"));
                EditorGUILayout.HelpBox("Ribbon constructs one continuous world-space strip and projects it through the character mesh into every affected UV/UDIM texture. The complete source image repeats without internal stamp edges, and bend deformation is distributed across the three nearest tiles. Brush Size controls the nominal tile width and length.",
                    MessageType.None);
                return;
            }
            bool enabled = pathMode == TexturePaintPathMode.Stamps;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                EditorGUI.BeginChangeCheck();
                float nextSpacing = EditorGUILayout.Slider(new GUIContent("Stamp Spacing",
                    "Center-to-center distance measured in brush diameters. 1.0 places adjacent stamps edge-to-edge; values above 1.0 leave gaps."),
                    active.spacing, 0.05f, 10f);
                if (EditorGUI.EndChangeCheck())
                {
                    active.spacing = nextSpacing;
                }
                EditorGUILayout.LabelField(new GUIContent("Center Distance",
                    "The resulting center-to-center distance in model world units."),
                    new GUIContent(active.StampSpacing.ToString("0.#####") + " world units"));
            }
            if (!enabled)
                EditorGUILayout.HelpBox("Choose Stamps to place separated texture stamps, or Ribbon to repeat complete source tiles edge-to-edge.",
                    MessageType.None);
        }

        private bool TryCapturePathRenderState(out TextureSet set, out TexturePaintLayer layer,
            out TexturePaintSplineSettings settings, out int signature)
        {
            set = null;
            layer = null;
            settings = null;
            signature = 0;
            if (controller?.Textures == null || (uint)selectedSurface >= (uint)controller.Textures.Sets.Count)
                return false;
            set = controller.Textures.Sets[selectedSurface];
            if (IsLayerMaskMode(set)) return false;
            if (!TryGetActivePathLayer(set, out layer)) return false;
            settings = CreateSplineSettings();
            signature = GetPathRenderSignature(set, layer, settings);
            return true;
        }

        private void HandlePathRenderParameterChanges(bool hadPathState, TextureSet previousSet,
            TexturePaintLayer previousLayer, TexturePaintSplineSettings previousSettings, int previousSignature)
        {
            CommitPathRenderParameterChange(hadPathState, previousSet, previousLayer, previousSettings,
                previousSignature, "Edit Path Parameters");
            Event current = Event.current;
            if (splineReapplyPending && current != null && current.rawType == EventType.MouseUp && current.button == 0)
                ScheduleSplineReapply();
            if (pendingPathEdit != null && pendingPathEdit.deferred && current != null &&
                current.rawType == EventType.MouseUp) CommitPendingPathEdit();
        }

        private void CommitPathRenderParameterChange(bool hadPathState, TextureSet previousSet,
            TexturePaintLayer previousLayer, TexturePaintSplineSettings previousSettings, int previousSignature,
            string undoLabel)
        {
            if (IsLayerMaskMode(previousSet)) return;
            if (!hadPathState || !TryGetActivePathLayer(previousSet, out TexturePaintLayer currentLayer) ||
                !ReferenceEquals(currentLayer, previousLayer)) return;
            int currentSignature = GetPathRenderSignature(previousSet, currentLayer, CreateSplineSettings());
            if (currentSignature == previousSignature) return;
            if (!pathEditRecordedThisGUI)
                RecordLightweightPathParameterUndo(previousSet, previousSettings, undoLabel);
            else
            {
                QueueSplineReapply(previousSet);
                ScheduleSplineReapply();
            }
        }

        private int GetPathRenderSignature(TextureSet set, TexturePaintLayer layer,
            TexturePaintSplineSettings settings)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + GetSplineSignature(set?.surface, layer?.spline);
                hash = hash * 31 + (int)settings.tool;
                hash = hash * 31 + (int)settings.channel;
                hash = hash * 31 + (int)settings.source;
                hash = hash * 31 + (int)settings.destination;
                hash = hash * 31 + ObjectIdentityHash(settings.brush);
                hash = hash * 31 + (int)settings.brushShape;
                hash = hash * 31 + settings.brushSize.GetHashCode();
                hash = hash * 31 + settings.brushHardness.GetHashCode();
                hash = hash * 31 + settings.brushFlow.GetHashCode();
                hash = hash * 31 + settings.brushSpacing.GetHashCode();
                hash = hash * 31 + settings.brushRotation.GetHashCode();
                hash = hash * 31 + (int)settings.brushBlendMode;
                hash = hash * 31 + (settings.brushMirrorStroke ? 1 : 0);
                hash = hash * 31 + (settings.brushAlignToStroke ? 1 : 0);
                hash = hash * 31 + ObjectIdentityHash(settings.brushStamp);
                hash = hash * 31 + ObjectIdentityHash(settings.brushStampSprite);
                hash = hash * 31 + settings.brushRandomizationVersion;
                hash = hash * 31 + (settings.brushRandomRotation ? 1 : 0);
                hash = hash * 31 + (settings.brushRandomSizeVariation ? 1 : 0);
                hash = hash * 31 + settings.brushRandomSizeShrink.GetHashCode();
                hash = hash * 31 + settings.brushRandomSizeGrow.GetHashCode();
                hash = hash * 31 + (settings.brushSplatter ? 1 : 0);
                hash = hash * 31 + settings.brushSplatterDistance.GetHashCode();
                hash = hash * 31 + (settings.brushRandomStrength ? 1 : 0);
                hash = hash * 31 + (settings.brushFade ? 1 : 0);
                hash = hash * 31 + (settings.brushTaper ? 1 : 0);
                hash = hash * 31 + settings.brushFadeTaperLength.GetHashCode();
                hash = hash * 31 + ObjectIdentityHash(settings.sourceTexture);
                hash = hash * 31 + ObjectIdentityHash(settings.sourceSprite);
                hash = hash * 31 + (int)settings.normalConvention;
                hash = hash * 31 + ObjectIdentityHash(settings.ribbonBeginningTexture);
                hash = hash * 31 + ObjectIdentityHash(settings.ribbonBeginningSprite);
                hash = hash * 31 + ObjectIdentityHash(settings.ribbonEndTexture);
                hash = hash * 31 + ObjectIdentityHash(settings.ribbonEndSprite);
                hash = hash * 31 + ObjectIdentityHash(settings.sourceOverlay);
                hash = hash * 31 + settings.color.GetHashCode();
                hash = hash * 31 + settings.strength.GetHashCode();
                hash = hash * 31 + (settings.limitStrokeCoverage ? 1 : 0);
                hash = hash * 31 + (settings.mirrorX ? 1 : 0);
                hash = hash * 31 + settings.stabilization.GetHashCode();
                hash = hash * 31 + settings.directionSmoothing.GetHashCode();
                hash = hash * 31 + settings.projectionDepth.GetHashCode();
                hash = hash * 31 + settings.normalAngleLimit.GetHashCode();
                hash = hash * 31 + (settings.paintBackfaces ? 1 : 0);
                hash = hash * 31 + (settings.pressureAffectsFlow ? 1 : 0);
                hash = hash * 31 + (settings.pressureAffectsSize ? 1 : 0);
                hash = hash * 31 + (int)settings.pathMode;
                hash = hash * 31 + (int)settings.orientation;
                hash = hash * 31 + (int)settings.startCap;
                hash = hash * 31 + (int)settings.endCap;
                hash = hash * 31 + settings.radialSymmetry;
                hash = hash * 31 + settings.symmetryAxis.GetHashCode();
                hash = hash * 31 + selectedBrushPlugin;
                if (settings.tool == TexturePaintTool.Plugin && controller?.Plugins != null &&
                    (uint)selectedBrushPlugin < (uint)controller.Plugins.Brushes.Count)
                {
                    ITexturePaintBrushV2 plugin = controller.Plugins.Brushes[selectedBrushPlugin];
                    TexturePaintPluginParameterSet parameters = controller.Plugins.GetParameters(plugin);
                    if (parameters?.values != null)
                        for (int i = 0; i < parameters.values.Count; i++)
                        {
                            TexturePaintPluginParameterValue value = parameters.values[i];
                            if (value == null) continue;
                            hash = hash * 31 + (value.id?.GetHashCode() ?? 0);
                            hash = hash * 31 + value.number.GetHashCode();
                            hash = hash * 31 + (value.boolean ? 1 : 0);
                            hash = hash * 31 + value.color.GetHashCode();
                            hash = hash * 31 + (value.text?.GetHashCode() ?? 0);
                            hash = hash * 31 + ObjectIdentityHash(value.texture);
                        }
                }
                if (layer?.spline != null)
                {
                    for (int i = 0; i < layer.spline.PointCount; i++)
                    {
                        hash = hash * 31 + layer.spline.pressures[i].GetHashCode();
                        hash = hash * 31 + layer.spline.widths[i].GetHashCode();
                        hash = hash * 31 + layer.spline.flows[i].GetHashCode();
                        hash = hash * 31 + layer.spline.rolls[i].GetHashCode();
                        hash = hash * 31 + layer.spline.colors[i].GetHashCode();
                        hash = hash * 31 + layer.spline.offsets[i].GetHashCode();
                        hash = hash * 31 + (int)layer.spline.tangentModes[i];
                    }
                }
                return hash;
            }
        }

        private static int ObjectIdentityHash(UnityEngine.Object value) =>
            value != null ? value.GetEntityId().GetHashCode() : 0;

        private void RestoreSplineSettings(TexturePaintSplineSettings settings)
        {
            tool = settings.tool;
            selectedChannel = settings.channel;
            paintSource = settings.source;
            normalConvention = settings.normalConvention;
            settings.destination = TexturePaintSourceMode.SourceOverlay;
            sourceMode = TexturePaintSourceMode.SourceOverlay;
            RestorePaintSource(settings.sourceTexture, settings.sourceSprite);
            ribbonBeginningTexture = settings.ribbonBeginningTexture;
            ribbonBeginningSprite = settings.ribbonBeginningSprite;
            ribbonEndTexture = settings.ribbonEndTexture;
            ribbonEndSprite = settings.ribbonEndSprite;
            paintSourceOverlay = settings.sourceOverlay;
            paintColor = settings.color;
            strength = settings.strength;
            limitStrokeCoverage = settings.limitStrokeCoverage;
            mirrorX = settings.mirrorX;
            pathMode = settings.pathMode;
            pathOrientation = settings.orientation;
            pathStartCap = settings.startCap;
            pathEndCap = settings.endCap;
            radialSymmetry = Mathf.Clamp(settings.radialSymmetry, 1, 16);
            radialSymmetryAxis = settings.symmetryAxis.sqrMagnitude > 0.000001f
                ? settings.symmetryAxis.normalized : Vector3.up;
            strokeStabilization = settings.stabilization;
            directionSmoothing = settings.directionSmoothing;
            projectionDepth = settings.projectionDepth;
            normalAngleLimit = settings.normalAngleLimit;
            paintBackfaces = settings.paintBackfaces;
            pressureAffectsFlow = settings.pressureAffectsFlow;
            pressureAffectsSize = settings.pressureAffectsSize;
            // Use the embedded values so editing a spline never mutates a shared brush-library
            // asset, while retaining that preset as an explicit update target.
            brush = settings.brush;
            BrushPreset target = transientBrush;
            target.shape = settings.brushShape;
            target.size = Mathf.Max(0.001f, settings.brushSize);
            target.hardness = settings.brushHardness;
            target.flow = settings.brushFlow;
            target.spacing = Mathf.Max(0.01f, settings.brushSpacing);
            target.rotation = settings.brushRotation;
            target.blendMode = settings.brushBlendMode;
            target.mirrorStroke = settings.brushMirrorStroke;
            target.alignToStroke = settings.brushAlignToStroke;
            target.stampSprite = settings.brushStampSprite;
            target.stampTexture = settings.brushStampSprite == null ? settings.brushStamp : null;
            bool hasRandomization = settings.brushRandomizationVersion > 0;
            target.randomRotation = hasRandomization && settings.brushRandomRotation;
            target.randomSizeVariation = hasRandomization && settings.brushRandomSizeVariation;
            target.randomSizeShrink = hasRandomization
                ? Mathf.Clamp01(settings.brushRandomSizeShrink) : 0.3f;
            target.randomSizeGrow = hasRandomization
                ? Mathf.Clamp01(settings.brushRandomSizeGrow) : 0.3f;
            target.splatter = settings.brushSplatter;
            target.splatterDistance = Mathf.Clamp(settings.brushSplatterDistance, 0.01f, 2f);
            target.randomStrength = settings.brushRandomStrength;
            target.fade = settings.brushFade;
            target.taper = settings.brushTaper;
            target.fadeTaperLength = Mathf.Max(0f, settings.brushFadeTaperLength);
        }

        private void DrawCursor()
        {
            Color color = tool == TexturePaintTool.NormalTouchup ? Color.cyan : paintColor;
            DrawCursorAt(hoverHit.point, hoverHit.normal, hoverTangent, color, false);
            if (mirrorX || ActiveBrush.mirrorStroke)
            {
                Vector3 mirroredPoint = TexturePaintMath.MirrorAcrossGlobalX(hoverHit.point);
                Vector3 mirroredNormal = new Vector3(-hoverHit.normal.x, hoverHit.normal.y, hoverHit.normal.z);
                Vector3 mirroredTangent = new Vector3(-hoverTangent.x, hoverTangent.y, hoverTangent.z);
                DrawCursorAt(mirroredPoint, mirroredNormal, mirroredTangent, new Color(color.r, color.g, color.b, 0.7f), true);
            }
        }

        private void DrawCursorAt(Vector3 point, Vector3 normal, Vector3 tangent, Color color, bool mirrored)
        {
            Handles.color = Color.black;
            DrawCursorShapeAt(point, normal, tangent, ActiveBrush.size * 1.04f);
            Handles.color = color;
            DrawCursorShapeAt(point, normal, tangent, ActiveBrush.size);
            Handles.color = new Color(1f, 1f, 1f, mirrored ? 0.4f : 0.72f);
            DrawCursorShapeAt(point, normal, tangent, ActiveBrush.size * ActiveBrush.hardness);
            Vector3 direction = Quaternion.AngleAxis((mirrored ? -1f : 1f) * ActiveBrush.rotation, normal) * tangent.normalized;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector3.Cross(normal, Vector3.up);
                if (direction.sqrMagnitude < 0.0001f) direction = Vector3.Cross(normal, Vector3.right);
                direction.Normalize();
            }
            Handles.DrawAAPolyLine(2f, point, point + direction * ActiveBrush.size);
            Texture2D stamp = ActiveBrush.ResolvedStampTexture;
            if (ActiveBrush.shape == BrushPreset.Shape.Stamp && stamp != null)
            {
                Handles.BeginGUI();
                Vector2 screen = HandleUtility.WorldToGUIPoint(point);
                Color previous = GUI.color; GUI.color = new Color(1f, 1f, 1f, mirrored ? 0.42f : 0.72f);
                GUI.DrawTexture(new Rect(screen.x - 18f, screen.y - 18f, 36f, 36f), stamp, ScaleMode.ScaleToFit, true);
                GUI.color = previous;
                Handles.EndGUI();
            }
        }

        internal static void SynchronizeSplinePeer(TexturePaintLayer source, TexturePaintLayer peer,
            string proceduralGroupKey)
        {
            if (source == null || peer == null) return;
            // The active layer owns the spline instance being edited by both the Scene and UV
            // views. Replacing it here leaves the UV editor holding a stale reference after every
            // render, so its point hit-testing and dragging stop working. Only logical peers need
            // an independent clone.
            if (!ReferenceEquals(peer, source)) peer.spline = CloneSpline(source.spline);
            peer.splineSettings = source.splineSettings?.Clone() ?? new TexturePaintSplineSettings();
            peer.proceduralGroupKey = proceduralGroupKey;
        }

        private TexturePaintLayer CreateSplineLayer(TextureSet set)
        {
            // Apply mode belongs to the path layer. A newly authored path starts as a ribbon even
            // when the previously selected path used Stamps, Continuous, or Filled mode.
            SetSelectedChannelAndRefreshSource(TexturePaintChannel.Albedo);
            pathMode = TexturePaintPathMode.Ribbon;
            TexturePaintLayer layer = set.AddSplineLayer();
            layer.visible = true;
            layer.splineSettings = CreateSplineSettings();
            spline = layer.spline;
            splineMode = true;
            selectedSplinePoint = -1;
            SceneView.RepaintAll();
            return layer;
        }

        private TexturePaintLayer CreateSplineLayerWithUndo(TextureSet set)
        {
            if (set == null) return null;
            BeginLayerCreationUndo("Create Spline Layer");
            TexturePaintLayer layer = CreateSplineLayer(set);
            CompleteLayerCreationUndo(layer);
            return layer;
        }

        private void EnsureSplineLayer(TextureSet set)
        {
            if (set.activeLayerIndex >= 0 && set.activeLayerIndex < set.layers.Count && set.layers[set.activeLayerIndex].IsSplineLayer)
            {
                // Applying an edit rerasterizes the existing path through this method. Keep the
                // point selection intact so its curve and width handles remain available after
                // moving an anchor or either handle.
                spline = set.layers[set.activeLayerIndex].spline;
                RepairSplinePointSelection(spline);
                return;
            }
            CreateSplineLayerWithUndo(set);
        }

        private void RepairSplinePointSelection(TexturePaintSpline targetSpline)
        {
            int pointCount = targetSpline?.PointCount ?? 0;
            selectedSplinePoints ??= new HashSet<int>();
            selectedSplinePoints.RemoveWhere(point => point < 0 || point >= pointCount);
            if (pointCount == 0)
            {
                selectedSplinePoint = -1;
                selectedSplinePoints.Clear();
                return;
            }
            if (selectedSplinePoint >= pointCount)
                selectedSplinePoint = pointCount - 1;
            else if (selectedSplinePoint < -1)
                selectedSplinePoint = -1;
            if (selectedSplinePoint >= 0)
                selectedSplinePoints.Add(selectedSplinePoint);
        }

        private void DrawVisibleSplines()
        {
            if (controller?.Textures == null) return;
            if ((uint)selectedSurface >= (uint)controller.Textures.Sets.Count) return;
            TextureSet set = controller.Textures.Sets[selectedSurface];
            int layerIndex = set.activeLayerIndex;
            if (!IsActiveSplineAuthoringLayer(set, layerIndex)) return;
            TexturePaintSpline layerSpline = set.layers[layerIndex].spline;
            if (layerSpline == null || layerSpline.PointCount == 0) return;
            layerSpline.EnsureControlPoints();
            if (!layerSpline.worldSpace)
            {
                DrawSelectedTwoDimensionalSplineHandles(set, layerSpline);
                return;
            }

            Color previous = Handles.color;
            Handles.color = Color.yellow;
            Vector3[] curvePoints = GetSurfaceHuggingCurve(set.surface, layerSpline);
            if (curvePoints.Length > 1) Handles.DrawAAPolyLine(4f, curvePoints);
            for (int pointIndex = 0; pointIndex < layerSpline.PointCount; pointIndex++)
            {
                Vector3 point = layerSpline.worldPoints[pointIndex];
                float nodeSize = HandleUtility.GetHandleSize(point) * 0.045f;
                bool pointSelected = pointIndex == selectedSplinePoint ||
                    (selectedSplinePoints != null && selectedSplinePoints.Contains(pointIndex));
                Color pointColor = pointSelected
                    ? new Color(1f, 0.42f, 0.04f, 1f) : new Color(0.92f, 0.05f, 0.04f, 1f);
                if (splineMode)
                {
                    int controlId = GUIUtility.GetControlID(SplineAnchorHandleHint + pointIndex, FocusType.Passive);
                    SplineSurfaceHandleEvent interaction = DoSurfaceProjectedHandle(controlId, point,
                        nodeSize, pointColor, Handles.SphereHandleCap,
                        out ReconstructedSurface hitSurface, out RaycastHit hit);
                    if (interaction == SplineSurfaceHandleEvent.Pressed)
                    {
                        PrepareSplineHandleUndo(set, "Move Spline Point");
                        selectedSplinePoints ??= new HashSet<int>();
                        if (Event.current.control || Event.current.command)
                        {
                            if (!selectedSplinePoints.Add(pointIndex)) selectedSplinePoints.Remove(pointIndex);
                            selectedSplinePoint = selectedSplinePoints.Count > 0 ? pointIndex : -1;
                        }
                        else
                        {
                            selectedSplinePoints.Clear();
                            selectedSplinePoints.Add(pointIndex);
                            selectedSplinePoint = pointIndex;
                        }
                    }
                    else if (interaction == SplineSurfaceHandleEvent.ContextRequested)
                    {
                        SelectSingleSplinePoint(pointIndex);
                        ShowSplinePointContextMenu(set, pointIndex);
                    }
                    else if (interaction == SplineSurfaceHandleEvent.Dragged)
                    {
                        BeginPreparedSplineHandleUndo();
                        TextureSet hitSet = controller.Textures.FindSet(hitSurface.index);
                        MoveSplinePointToSurfaceHit(hitSet, layerSpline, pointIndex, hit);
                        CompleteLightweightPathEdit(set, true);
                    }
                }
                else
                {
                    Handles.color = pointColor;
                    Handles.SphereHandleCap(0, point, Quaternion.identity, nodeSize, EventType.Repaint);
                }
            }
            if (splineMode && selectedSplinePoint >= 0 &&
                selectedSplinePoint < layerSpline.PointCount)
            {
                if (layerSpline.useBezier && layerSpline.showControls)
                    DrawSplineControlHandles(set, layerSpline, selectedSplinePoint);
                DrawSplineWidthHandle(set, layerSpline, selectedSplinePoint);
            }
            Handles.color = previous;
        }

        private void DrawSelectedTwoDimensionalSplineHandles(TextureSet fallbackSet,
            TexturePaintSpline targetSpline)
        {
            int pointIndex = selectedSplinePoint;
            if ((uint)pointIndex >= (uint)targetSpline.PointCount) return;
            TextureSet projectionSet = ResolveTwoDimensionalSplineProjectionSet(fallbackSet,
                targetSpline, pointIndex);
            if (!TryProjectTwoDimensionalSplineUV(projectionSet, targetSpline, pointIndex,
                targetSpline.uvPoints[pointIndex], out Vector3 point, out Vector3 normal,
                out int triangle, out _)) return;

            float surfaceOffset = Mathf.Max(0.0001f,
                projectionSet.surface.mesh.bounds.size.magnitude * 0.00025f);
            point += normal * surfaceOffset;
            Color previous = Handles.color;
            Color pointColor = new Color(1f, 0.42f, 0.04f, 1f);
            float nodeSize = HandleUtility.GetHandleSize(point) * 0.045f;
            int controlId = GUIUtility.GetControlID(SplineAnchorHandleHint + pointIndex,
                FocusType.Passive);
            SplineSurfaceHandleEvent interaction = DoSurfaceProjectedHandle(controlId, point,
                nodeSize, pointColor, Handles.SphereHandleCap,
                out ReconstructedSurface hitSurface, out RaycastHit hit);
            if (interaction == SplineSurfaceHandleEvent.Pressed)
                PrepareSplineHandleUndo(fallbackSet, "Position 2D Spline Point on Surface");
            else if (interaction == SplineSurfaceHandleEvent.ContextRequested)
                ShowSplinePointContextMenu(fallbackSet, pointIndex);
            else if (interaction == SplineSurfaceHandleEvent.Dragged)
            {
                BeginPreparedSplineHandleUndo();
                TextureSet hitSet = controller.Textures.FindSet(hitSurface.index);
                MoveTwoDimensionalSplinePoint(hitSet, targetSpline, pointIndex, hit.textureCoord,
                    hit.normal, hit.triangleIndex, hit.barycentricCoordinate);
                CompleteLightweightPathEdit(fallbackSet, true);
            }

            Vector3 tangent = CalculateTwoDimensionalSplineWorldTangent(projectionSet,
                targetSpline, pointIndex, triangle, point, normal);
            if (targetSpline.useBezier && targetSpline.showControls)
                DrawTwoDimensionalSplineControlHandles(fallbackSet, projectionSet, targetSpline,
                    pointIndex, point, surfaceOffset);
            DrawSplineWidthHandle(fallbackSet, targetSpline, pointIndex, point, tangent, normal);
            Handles.color = previous;
        }

        private TextureSet ResolveTwoDimensionalSplineProjectionSet(TextureSet fallbackSet,
            TexturePaintSpline targetSpline, int pointIndex)
        {
            if (controller?.Textures == null || targetSpline == null ||
                (uint)pointIndex >= (uint)targetSpline.PointCount) return fallbackSet;
            if (pointIndex < targetSpline.anchors.Count)
            {
                TexturePaintSurfaceAnchor anchor = targetSpline.anchors[pointIndex];
                for (int i = 0; i < controller.Textures.Sets.Count; i++)
                {
                    TextureSet candidate = controller.Textures.Sets[i];
                    if ((!string.IsNullOrEmpty(anchor.surfaceId) &&
                         string.Equals(candidate.persistentId, anchor.surfaceId, StringComparison.Ordinal)) ||
                        (string.IsNullOrEmpty(anchor.surfaceId) &&
                         candidate.surface.index == anchor.surfaceIndex))
                        return candidate;
                }
            }
            return fallbackSet;
        }

        private static bool TryProjectTwoDimensionalSplineUV(TextureSet set,
            TexturePaintSpline targetSpline, int pointIndex, Vector2 uv, out Vector3 world,
            out Vector3 normal, out int triangle, out Vector3 barycentric)
        {
            world = Vector3.zero;
            normal = Vector3.up;
            triangle = -1;
            barycentric = Vector3.zero;
            if (set?.surface == null || targetSpline == null ||
                (uint)pointIndex >= (uint)targetSpline.PointCount) return false;
            int preferred = pointIndex < targetSpline.triangleIndices.Count
                ? targetSpline.triangleIndices[pointIndex] : -1;
            return set.surface.TryUVToWorld(uv, preferred, out world, out normal,
                out triangle, out barycentric);
        }

        private static Vector3 CalculateTwoDimensionalSplineWorldTangent(TextureSet set,
            TexturePaintSpline targetSpline, int pointIndex, int preferredTriangle,
            Vector3 point, Vector3 normal)
        {
            Vector2 uvTangent = targetSpline.uvOutControls[pointIndex] -
                targetSpline.uvInControls[pointIndex];
            if (uvTangent.sqrMagnitude < 0.000001f)
            {
                int previous = pointIndex > 0 ? pointIndex - 1 : targetSpline.closed &&
                    targetSpline.PointCount > 1 ? targetSpline.PointCount - 1 : pointIndex;
                int next = pointIndex + 1 < targetSpline.PointCount ? pointIndex + 1 :
                    targetSpline.closed && targetSpline.PointCount > 1 ? 0 : pointIndex;
                uvTangent = targetSpline.uvPoints[next] - targetSpline.uvPoints[previous];
            }
            if (uvTangent.sqrMagnitude > 0.000001f)
            {
                float step = 0.002f;
                Vector2 probeUV = targetSpline.uvPoints[pointIndex] + uvTangent.normalized * step;
                if (set?.surface != null && set.surface.TryUVToWorld(probeUV, preferredTriangle,
                    out Vector3 probe, out _, out _, out _))
                {
                    Vector3 tangent = Vector3.ProjectOnPlane(probe - point, normal);
                    if (tangent.sqrMagnitude > 0.000001f) return tangent.normalized;
                }
            }
            Vector3 fallback = set?.surface != null
                ? CalculateTangent(set.surface, preferredTriangle) : Vector3.right;
            fallback = Vector3.ProjectOnPlane(fallback, normal);
            return fallback.sqrMagnitude > 0.000001f ? fallback.normalized : Vector3.right;
        }

        private void DrawTwoDimensionalSplineControlHandles(TextureSet ownerSet,
            TextureSet projectionSet, TexturePaintSpline targetSpline, int pointIndex,
            Vector3 point, float surfaceOffset)
        {
            Color controlColor = new Color(0.2f, 1f, 0.32f, 1f);
            if (TryProjectTwoDimensionalSplineUV(projectionSet, targetSpline, pointIndex,
                targetSpline.uvInControls[pointIndex], out Vector3 incoming,
                out Vector3 incomingNormal, out _, out _))
            {
                incoming += incomingNormal * surfaceOffset;
                Handles.color = new Color(controlColor.r, controlColor.g, controlColor.b, 0.72f);
                Handles.DrawLine(point, incoming, 2f);
                int incomingId = GUIUtility.GetControlID(SplineIncomingHandleHint + pointIndex,
                    FocusType.Passive);
                SplineSurfaceHandleEvent incomingInteraction = DoSurfaceProjectedHandle(incomingId,
                    incoming, HandleUtility.GetHandleSize(incoming) * 0.035f, controlColor,
                    Handles.DotHandleCap, out ReconstructedSurface incomingSurface, out RaycastHit incomingHit);
                if (incomingInteraction == SplineSurfaceHandleEvent.Pressed)
                    PrepareSplineHandleUndo(ownerSet, "Adjust 2D Spline Curve on Surface");
                else if (incomingInteraction == SplineSurfaceHandleEvent.Dragged)
                {
                    BeginPreparedSplineHandleUndo();
                    SetTwoDimensionalSplineControl(targetSpline, pointIndex, true,
                        incomingHit.textureCoord);
                    CompleteLightweightPathEdit(ownerSet, true);
                }
            }

            if (TryProjectTwoDimensionalSplineUV(projectionSet, targetSpline, pointIndex,
                targetSpline.uvOutControls[pointIndex], out Vector3 outgoing,
                out Vector3 outgoingNormal, out _, out _))
            {
                outgoing += outgoingNormal * surfaceOffset;
                Handles.color = new Color(controlColor.r, controlColor.g, controlColor.b, 0.72f);
                Handles.DrawLine(point, outgoing, 2f);
                int outgoingId = GUIUtility.GetControlID(SplineOutgoingHandleHint + pointIndex,
                    FocusType.Passive);
                SplineSurfaceHandleEvent outgoingInteraction = DoSurfaceProjectedHandle(outgoingId,
                    outgoing, HandleUtility.GetHandleSize(outgoing) * 0.035f, controlColor,
                    Handles.DotHandleCap, out ReconstructedSurface outgoingSurface, out RaycastHit outgoingHit);
                if (outgoingInteraction == SplineSurfaceHandleEvent.Pressed)
                    PrepareSplineHandleUndo(ownerSet, "Adjust 2D Spline Curve on Surface");
                else if (outgoingInteraction == SplineSurfaceHandleEvent.Dragged)
                {
                    BeginPreparedSplineHandleUndo();
                    SetTwoDimensionalSplineControl(targetSpline, pointIndex, false,
                        outgoingHit.textureCoord);
                    CompleteLightweightPathEdit(ownerSet, true);
                }
            }
        }

        private void DrawSplineWidthHandle(TextureSet set, TexturePaintSpline targetSpline,
            int pointIndex)
        {
            Vector3 point = targetSpline.worldPoints[pointIndex];
            Vector3 tangent = targetSpline.worldOutControls[pointIndex] -
                targetSpline.worldInControls[pointIndex];
            if (tangent.sqrMagnitude < 0.000001f)
            {
                int previous = pointIndex > 0 ? pointIndex - 1 : targetSpline.closed &&
                    targetSpline.PointCount > 1 ? targetSpline.PointCount - 1 : pointIndex;
                int next = pointIndex + 1 < targetSpline.PointCount ? pointIndex + 1 :
                    targetSpline.closed && targetSpline.PointCount > 1 ? 0 : pointIndex;
                tangent = targetSpline.worldPoints[next] - targetSpline.worldPoints[previous];
            }
            if (tangent.sqrMagnitude < 0.000001f) tangent = Vector3.right;
            Vector3 normal = pointIndex < targetSpline.worldNormals.Count
                ? targetSpline.worldNormals[pointIndex] : Vector3.up;
            if (normal.sqrMagnitude < 0.000001f) normal = Vector3.up;
            DrawSplineWidthHandle(set, targetSpline, pointIndex, point, tangent, normal);
        }

        private void DrawSplineWidthHandle(TextureSet set, TexturePaintSpline targetSpline,
            int pointIndex, Vector3 point, Vector3 tangent, Vector3 normal)
        {
            Vector3 widthDirection = Vector3.Cross(normal.normalized, tangent.normalized).normalized;
            if (widthDirection.sqrMagnitude < 0.000001f) widthDirection = Vector3.right;

            float handleSize = HandleUtility.GetHandleSize(point);
            float authoredBrushSize = TryGetActivePathLayer(set, out TexturePaintLayer layer) &&
                layer.spline == targetSpline ? Mathf.Max(0.0001f, layer.splineSettings?.brushSize ?? ActiveBrush.size)
                : Mathf.Max(0.0001f, ActiveBrush.size);
            float displayScale = Mathf.Clamp(authoredBrushSize, handleSize * 0.08f,
                handleSize * 0.35f);
            Vector2 projectedWidth = HandleUtility.WorldToGUIPoint(point +
                widthDirection * displayScale) - HandleUtility.WorldToGUIPoint(point);
            if (projectedWidth.sqrMagnitude < 16f && SceneView.currentDrawingSceneView?.camera != null)
            {
                Camera camera = SceneView.currentDrawingSceneView.camera;
                Vector2 tangentGUI = HandleUtility.WorldToGUIPoint(point + tangent.normalized * displayScale) -
                    HandleUtility.WorldToGUIPoint(point);
                if (tangentGUI.sqrMagnitude < 0.0001f) tangentGUI = Vector2.right;
                tangentGUI.Normalize();
                Vector2 screenNormal = new Vector2(-tangentGUI.y, tangentGUI.x);
                widthDirection = (camera.transform.right * screenNormal.x -
                    camera.transform.up * screenNormal.y).normalized;
            }
            float width = Mathf.Clamp(targetSpline.widths[pointIndex], 0.05f, 4f);
            Vector3 widthPosition = point + widthDirection * displayScale * width;
            Color blue = new Color(0.12f, 0.55f, 1f, 1f);
            Handles.color = new Color(blue.r, blue.g, blue.b, 0.75f);
            Handles.DrawLine(point, widthPosition, 2.5f);
            int controlId = GUIUtility.GetControlID(SplineWidthHandleHint + pointIndex,
                FocusType.Passive);
            SplineSurfaceHandleEvent interaction = DoSplineScalarHandle(controlId, point,
                widthPosition, displayScale, blue, out float nextWidth);
            if (interaction == SplineSurfaceHandleEvent.Pressed)
                PrepareSplineHandleUndo(set, "Adjust Spline Point Width");
            else if (interaction == SplineSurfaceHandleEvent.Dragged)
            {
                BeginPreparedSplineHandleUndo();
                targetSpline.widths[pointIndex] = Mathf.Clamp(nextWidth, 0.05f, 4f);
                CompleteLightweightPathEdit(set, true);
            }
        }

        private SplineSurfaceHandleEvent DoSplineScalarHandle(int controlId, Vector3 center,
            Vector3 position, float worldScale, Color color, out float value)
        {
            value = 1f;
            float size = HandleUtility.GetHandleSize(position) * 0.045f;
            Event current = Event.current;
            switch (current.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    HandleUtility.AddControl(controlId, HandleUtility.DistanceToCircle(position, size));
                    break;
                case EventType.Repaint:
                    Color previous = Handles.color;
                    Handles.color = GUIUtility.hotControl == controlId
                        ? Color.Lerp(color, Color.white, 0.25f) : color;
                    Handles.DotHandleCap(controlId, position, Quaternion.identity, size,
                        EventType.Repaint);
                    Handles.color = previous;
                    break;
                case EventType.MouseDown:
                    if (current.button == 0 && !current.alt &&
                        HandleUtility.nearestControl == controlId)
                    {
                        GUIUtility.hotControl = controlId;
                        splineHandleHotControl = controlId;
                        current.Use();
                        return SplineSurfaceHandleEvent.Pressed;
                    }
                    break;
                case EventType.MouseDrag:
                    if (splineHandleHotControl == controlId && GUIUtility.hotControl == controlId)
                    {
                        Vector2 centerGUI = HandleUtility.WorldToGUIPoint(center);
                        Vector2 scaleGUI = HandleUtility.WorldToGUIPoint(center +
                            (position - center).normalized * worldScale);
                        Vector2 axis = scaleGUI - centerGUI;
                        float pixelScale = axis.magnitude;
                        if (pixelScale > 0.001f)
                        {
                            axis /= pixelScale;
                            value = Vector2.Dot(current.mousePosition - centerGUI, axis) / pixelScale;
                        }
                        current.Use();
                        SceneView.RepaintAll();
                        return SplineSurfaceHandleEvent.Dragged;
                    }
                    break;
                case EventType.MouseUp:
                    if (splineHandleHotControl == controlId && current.button == 0)
                    {
                        ReleaseSplineHandleCapture(true, true);
                        current.Use();
                        SceneView.RepaintAll();
                    }
                    break;
            }
            return SplineSurfaceHandleEvent.None;
        }

        private void DrawSplineControlHandles(TextureSet set, TexturePaintSpline targetSpline, int pointIndex)
        {
            Vector3 point = targetSpline.worldPoints[pointIndex];
            Vector3 incoming = targetSpline.worldInControls[pointIndex];
            Vector3 outgoing = targetSpline.worldOutControls[pointIndex];
            Color controlColor = new Color(0.2f, 1f, 0.32f, 1f);
            Handles.color = new Color(controlColor.r, controlColor.g, controlColor.b, 0.72f);
            Handles.DrawLine(point, incoming, 2f);
            Handles.DrawLine(point, outgoing, 2f);

            float incomingSize = HandleUtility.GetHandleSize(incoming) * 0.035f;
            int incomingId = GUIUtility.GetControlID(SplineIncomingHandleHint + pointIndex, FocusType.Passive);
            SplineSurfaceHandleEvent incomingInteraction = DoSurfaceProjectedHandle(incomingId, incoming,
                incomingSize, controlColor, Handles.DotHandleCap, out ReconstructedSurface incomingSurface,
                out RaycastHit incomingHit);
            if (incomingInteraction == SplineSurfaceHandleEvent.Pressed)
                PrepareSplineHandleUndo(set, "Adjust Spline Curve");
            else if (incomingInteraction == SplineSurfaceHandleEvent.Dragged)
            {
                BeginPreparedSplineHandleUndo();
                SetSplineControlFromSurfaceHit(controller.Textures.FindSet(incomingSurface.index), targetSpline,
                    pointIndex, true, incomingHit);
                CompleteLightweightPathEdit(set, true);
            }

            float outgoingSize = HandleUtility.GetHandleSize(outgoing) * 0.035f;
            int outgoingId = GUIUtility.GetControlID(SplineOutgoingHandleHint + pointIndex, FocusType.Passive);
            SplineSurfaceHandleEvent outgoingInteraction = DoSurfaceProjectedHandle(outgoingId, outgoing,
                outgoingSize, controlColor, Handles.DotHandleCap, out ReconstructedSurface outgoingSurface,
                out RaycastHit outgoingHit);
            if (outgoingInteraction == SplineSurfaceHandleEvent.Pressed)
                PrepareSplineHandleUndo(set, "Adjust Spline Curve");
            else if (outgoingInteraction == SplineSurfaceHandleEvent.Dragged)
            {
                BeginPreparedSplineHandleUndo();
                SetSplineControlFromSurfaceHit(controller.Textures.FindSet(outgoingSurface.index), targetSpline,
                    pointIndex, false, outgoingHit);
                CompleteLightweightPathEdit(set, true);
            }
        }

        private SplineSurfaceHandleEvent DoSurfaceProjectedHandle(int controlId, Vector3 position,
            float size, Color color, Handles.CapFunction cap, out ReconstructedSurface projectedSurface,
            out RaycastHit projectedHit)
        {
            projectedSurface = null;
            projectedHit = default;
            Event current = Event.current;
            switch (current.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    HandleUtility.AddControl(controlId, HandleUtility.DistanceToCircle(position, size));
                    break;
                case EventType.Repaint:
                    Color previous = Handles.color;
                    Handles.color = GUIUtility.hotControl == controlId
                        ? Color.Lerp(color, Color.white, 0.25f) : color;
                    cap(controlId, position, Quaternion.identity, size, EventType.Repaint);
                    Handles.color = previous;
                    break;
                case EventType.MouseDown:
                    if (current.button == 1 && !current.alt && HandleUtility.nearestControl == controlId)
                    {
                        current.Use();
                        return SplineSurfaceHandleEvent.ContextRequested;
                    }
                    if (current.button == 0 && !current.alt && HandleUtility.nearestControl == controlId)
                    {
                        GUIUtility.hotControl = controlId;
                        splineHandleHotControl = controlId;
                        current.Use();
                        return SplineSurfaceHandleEvent.Pressed;
                    }
                    break;
                case EventType.MouseDrag:
                    if (splineHandleHotControl == controlId && GUIUtility.hotControl == controlId)
                    {
                        Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
                        if (controller?.Reconstruction != null &&
                            controller.Reconstruction.Raycast(ray, out projectedSurface, out projectedHit) &&
                            IsSelectedSlotHit(projectedSurface, projectedHit.triangleIndex))
                        {
                            current.Use();
                            SceneView.RepaintAll();
                            return SplineSurfaceHandleEvent.Dragged;
                        }
                        current.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (splineHandleHotControl == controlId && current.button == 0)
                    {
                        ReleaseSplineHandleCapture(true, true);
                        current.Use();
                        SceneView.RepaintAll();
                    }
                    break;
            }
            return SplineSurfaceHandleEvent.None;
        }

        internal static bool ShouldYieldToSceneNavigation(Event current)
        {
            if (current == null) return false;
            bool altHeld = current.alt ||
                (current.modifiers & EventModifiers.Alt) != EventModifiers.None;
            if (!altHeld) return false;
            return current.type == EventType.MouseDown || current.type == EventType.MouseDrag ||
                current.rawType == EventType.MouseDown || current.rawType == EventType.MouseDrag ||
                current.rawType == EventType.MouseUp;
        }

        private void PrepareSplineHandleUndo(TextureSet set, string label)
        {
            splineHandleEditSet = set;
            splineHandleEditLabel = label;
            splineHandleUndoStarted = false;
        }

        private void BeginPreparedSplineHandleUndo()
        {
            if (splineHandleUndoStarted || splineHandleEditSet == null) return;
            BeginLightweightPathUndo(splineHandleEditSet,
                string.IsNullOrEmpty(splineHandleEditLabel) ? "Edit Spline" : splineHandleEditLabel);
            splineHandleUndoStarted = true;
        }

        private void ReleaseSplineHandleCapture(bool completePathEdit = false,
            bool reapplyImmediately = false)
        {
            if (splineHandleHotControl == 0) return;
            int ownedControl = splineHandleHotControl;
            splineHandleHotControl = 0;
            if (GUIUtility.hotControl == ownedControl) GUIUtility.hotControl = 0;
            bool completedEdit = splineHandleUndoStarted;
            splineHandleEditSet = null;
            splineHandleEditLabel = null;
            splineHandleUndoStarted = false;
            if (!completePathEdit || !completedEdit) return;
            if (pendingPathEdit != null && pendingPathEdit.deferred) CommitPendingPathEdit();
            if (!splineReapplyPending) return;
            if (reapplyImmediately) ReapplyPendingSpline();
            else ScheduleSplineReapply();
        }

        private void MoveSplinePointToSurfaceHit(TextureSet set, TexturePaintSpline targetSpline,
            int pointIndex, RaycastHit hit)
        {
            if (set?.surface == null || targetSpline == null || (uint)pointIndex >= (uint)targetSpline.PointCount) return;
            if (!targetSpline.worldSpace) return;
            targetSpline.UpgradeWorldCurve();
            targetSpline.EnsureControlPoints();
            Vector3 worldDelta = hit.point - targetSpline.worldPoints[pointIndex];
            Vector2 uvDelta = hit.textureCoord - targetSpline.uvPoints[pointIndex];
            targetSpline.worldPoints[pointIndex] = hit.point;
            targetSpline.uvPoints[pointIndex] = hit.textureCoord;
            targetSpline.worldInControls[pointIndex] += worldDelta;
            targetSpline.worldOutControls[pointIndex] += worldDelta;
            targetSpline.uvInControls[pointIndex] += uvDelta;
            targetSpline.uvOutControls[pointIndex] += uvDelta;
            targetSpline.RefreshStraightTangents();
            targetSpline.worldNormals[pointIndex] = hit.normal;
            targetSpline.surfaceIndices[pointIndex] = set.surface.index;
            targetSpline.triangleIndices[pointIndex] = hit.triangleIndex;
            targetSpline.anchors[pointIndex] = new TexturePaintSurfaceAnchor
            {
                surfaceId = set.persistentId,
                surfaceIndex = set.surface.index,
                triangleIndex = hit.triangleIndex,
                barycentric = hit.barycentricCoordinate,
                normal = hit.normal,
                normalOffset = 0f
            };
        }

        internal static bool MoveTwoDimensionalSplinePoint(TextureSet set,
            TexturePaintSpline targetSpline, int pointIndex, Vector2 uv, Vector3 normal,
            int triangleIndex, Vector3 barycentric)
        {
            if (set?.surface == null || targetSpline == null || targetSpline.worldSpace ||
                (uint)pointIndex >= (uint)targetSpline.PointCount) return false;
            targetSpline.EnsureControlPoints();
            Vector2 uvDelta = uv - targetSpline.uvPoints[pointIndex];
            targetSpline.uvPoints[pointIndex] = uv;
            targetSpline.uvInControls[pointIndex] += uvDelta;
            targetSpline.uvOutControls[pointIndex] += uvDelta;
            targetSpline.worldPoints[pointIndex] = new Vector3(uv.x, uv.y, 0f);
            targetSpline.worldInControls[pointIndex] = new Vector3(
                targetSpline.uvInControls[pointIndex].x, targetSpline.uvInControls[pointIndex].y, 0f);
            targetSpline.worldOutControls[pointIndex] = new Vector3(
                targetSpline.uvOutControls[pointIndex].x, targetSpline.uvOutControls[pointIndex].y, 0f);
            targetSpline.RefreshStraightTangents();
            targetSpline.worldNormals[pointIndex] = Vector3.forward;
            targetSpline.surfaceIndices[pointIndex] = set.surface.index;
            targetSpline.triangleIndices[pointIndex] = triangleIndex;
            targetSpline.anchors[pointIndex] = new TexturePaintSurfaceAnchor
            {
                surfaceId = set.persistentId,
                surfaceIndex = set.surface.index,
                triangleIndex = triangleIndex,
                barycentric = barycentric,
                normal = normal,
                normalOffset = 0f
            };
            return true;
        }

        internal static bool SetTwoDimensionalSplineControl(TexturePaintSpline targetSpline,
            int pointIndex, bool incoming, Vector2 uv)
        {
            if (targetSpline == null || targetSpline.worldSpace ||
                (uint)pointIndex >= (uint)targetSpline.PointCount) return false;
            targetSpline.SetWorldControl(pointIndex, incoming,
                new Vector3(uv.x, uv.y, 0f), uv);
            return true;
        }

        private void SetSplineControlFromSurfaceHit(TextureSet set, TexturePaintSpline targetSpline,
            int pointIndex, bool incoming, RaycastHit hit)
        {
            if (set?.surface == null || targetSpline == null || (uint)pointIndex >= (uint)targetSpline.PointCount) return;
            if (!targetSpline.worldSpace) return;
            targetSpline.UpgradeWorldCurve();
            targetSpline.EnsureControlPoints();
            float displayOffset = Mathf.Max(0.0001f, set.surface.mesh.bounds.size.magnitude * 0.00025f);
            Vector3 surfacePoint = hit.point + hit.normal * displayOffset;
            targetSpline.SetWorldControl(pointIndex, incoming, surfacePoint, hit.textureCoord);
            if (incoming) targetSpline.worldInControls[pointIndex] = surfacePoint;
            else targetSpline.worldOutControls[pointIndex] = surfacePoint;
        }

        // UV-authored paths keep a derived world representation for the Scene preview. This is the
        // only control projection that remains; world-authored controls never enter this method.
        private static void ProjectSplineControlToSurface(TextureSet set, TexturePaintSpline targetSpline,
            int pointIndex, bool incoming)
        {
            if (set?.surface == null || targetSpline == null || targetSpline.worldSpace ||
                (uint)pointIndex >= (uint)targetSpline.PointCount) return;
            Vector2 controlUV = incoming
                ? targetSpline.uvInControls[pointIndex] : targetSpline.uvOutControls[pointIndex];
            int preferredTriangle = pointIndex < targetSpline.triangleIndices.Count
                ? targetSpline.triangleIndices[pointIndex] : -1;
            if (!set.surface.TryUVToWorld(controlUV, preferredTriangle, out Vector3 world,
                out Vector3 normal, out _, out _)) return;
            world += normal * Mathf.Max(0.0001f, set.surface.mesh.bounds.size.magnitude * 0.00025f);
            if (incoming) targetSpline.worldInControls[pointIndex] = world;
            else targetSpline.worldOutControls[pointIndex] = world;
        }

        private Vector3[] GetSurfaceHuggingCurve(ReconstructedSurface surface, TexturePaintSpline targetSpline)
        {
            if (splineDisplayCache == null)
                splineDisplayCache = new Dictionary<TexturePaintSpline, SplineDisplayCache>();
            int signature = GetSplineSignature(surface, targetSpline);
            if (splineDisplayCache.TryGetValue(targetSpline, out SplineDisplayCache cached) && cached.signature == signature)
                return cached.points;
            Vector3[] points = BuildSurfaceHuggingCurve(GetSplineProjectionSets(surface), surface, targetSpline);
            splineDisplayCache[targetSpline] = new SplineDisplayCache { signature = signature, points = points };
            return points;
        }

        private static int GetSplineSignature(ReconstructedSurface surface, TexturePaintSpline targetSpline)
        {
            unchecked
            {
                int hash = surface?.mesh != null
                    ? surface.mesh.GetEntityId().GetHashCode() : 0;
                hash = hash * 31 + targetSpline.PointCount;
                hash = hash * 31 + (targetSpline.worldSpace ? 1 : 0);
                hash = hash * 31 + (targetSpline.useBezier ? 1 : 0);
                hash = hash * 31 + (targetSpline.closed ? 1 : 0);
                for (int i = 0; i < targetSpline.PointCount; i++)
                {
                    hash = hash * 31 + targetSpline.worldPoints[i].GetHashCode();
                    hash = hash * 31 + targetSpline.uvPoints[i].GetHashCode();
                    if (i < targetSpline.worldNormals.Count)
                        hash = hash * 31 + targetSpline.worldNormals[i].GetHashCode();
                    if (i < targetSpline.worldInControls.Count) hash = hash * 31 + targetSpline.worldInControls[i].GetHashCode();
                    if (i < targetSpline.worldOutControls.Count) hash = hash * 31 + targetSpline.worldOutControls[i].GetHashCode();
                    if (i < targetSpline.uvInControls.Count) hash = hash * 31 + targetSpline.uvInControls[i].GetHashCode();
                    if (i < targetSpline.uvOutControls.Count) hash = hash * 31 + targetSpline.uvOutControls[i].GetHashCode();
                    if (i < targetSpline.surfaceIndices.Count) hash = hash * 31 + targetSpline.surfaceIndices[i];
                    if (i < targetSpline.triangleIndices.Count) hash = hash * 31 + targetSpline.triangleIndices[i];
                }
                return hash;
            }
        }

        private Vector3[] BuildSurfaceHuggingCurve(IReadOnlyList<TextureSet> projectionSets,
            ReconstructedSurface surface, TexturePaintSpline targetSpline, int subdivisionsPerSegment = 24)
        {
            targetSpline.EnsureControlPoints();
            targetSpline.UpgradeWorldCurve();
            if (targetSpline.PointCount == 0) return Array.Empty<Vector3>();
            if (targetSpline.PointCount == 1) return new[] { targetSpline.worldPoints[0] };
            List<Vector3> points = new List<Vector3>(targetSpline.SegmentCount * subdivisionsPerSegment + 1);
            for (int segment = 0; segment < targetSpline.SegmentCount; segment++)
            {
                int next = (segment + 1) % targetSpline.PointCount;
                int preferredTriangle = segment < targetSpline.triangleIndices.Count ? targetSpline.triangleIndices[segment] : -1;
                int preferredSurface = segment < targetSpline.surfaceIndices.Count
                    ? targetSpline.surfaceIndices[segment] : surface.index;
                for (int step = 0; step < subdivisionsPerSegment; step++)
                {
                    float t = step / (float)subdivisionsPerSegment;
                    targetSpline.EvaluateSegment(segment, next, t,
                        out Vector3 fallbackWorld, out Vector2 uv);
                    Vector3 normalHint = Vector3.Slerp(targetSpline.worldNormals[segment],
                        targetSpline.worldNormals[next], t).normalized;
                    TextureSet projectedSet = null;
                    bool found = targetSpline.worldSpace
                        ? TryProjectWorldPathPoint(projectionSets, fallbackWorld,
                            normalHint,
                            preferredSurface, preferredTriangle, out projectedSet, out Vector3 projected,
                            out Vector3 normal, out _, out int projectedTriangle, out _)
                        : surface.TryUVToWorld(uv, preferredTriangle, out projected, out normal,
                            out projectedTriangle, out _);
                    if (found)
                    {
                        ReconstructedSurface projectedSurface = projectedSet?.surface ?? surface;
                        Vector3 displayNormal = targetSpline.worldSpace && normalHint.sqrMagnitude > 0.000001f
                            ? normalHint : normal;
                        points.Add(projected + displayNormal * Mathf.Max(0.0001f,
                            projectedSurface.mesh.bounds.size.magnitude * 0.00025f));
                        if (targetSpline.worldSpace)
                        {
                            preferredSurface = projectedSurface.index;
                            preferredTriangle = projectedTriangle;
                        }
                    }
                    else points.Add(fallbackWorld);
                }
            }
            int finalFrom = targetSpline.SegmentCount - 1;
            int finalTo = targetSpline.closed ? 0 : targetSpline.PointCount - 1;
            targetSpline.EvaluateSegment(finalFrom, finalTo, 1f, out Vector3 finalFallback, out Vector2 finalUV);
            int finalPreferred = finalTo < targetSpline.triangleIndices.Count
                ? targetSpline.triangleIndices[finalTo] : -1;
            int finalPreferredSurface = finalTo < targetSpline.surfaceIndices.Count
                ? targetSpline.surfaceIndices[finalTo] : surface.index;
            TextureSet finalSet = null;
            bool finalFound = targetSpline.worldSpace
                ? TryProjectWorldPathPoint(projectionSets, finalFallback, targetSpline.worldNormals[finalTo],
                    finalPreferredSurface, finalPreferred, out finalSet, out Vector3 finalProjected,
                    out Vector3 finalNormal, out _, out _, out _)
                : surface.TryUVToWorld(finalUV, finalPreferred, out finalProjected,
                    out finalNormal, out _, out _);
            if (finalFound)
            {
                ReconstructedSurface finalSurface = finalSet?.surface ?? surface;
                Vector3 displayNormal = targetSpline.worldSpace && targetSpline.worldNormals[finalTo].sqrMagnitude > 0.000001f
                    ? targetSpline.worldNormals[finalTo].normalized : finalNormal;
                points.Add(finalProjected + displayNormal * Mathf.Max(0.0001f,
                    finalSurface.mesh.bounds.size.magnitude * 0.00025f));
            }
            else points.Add(finalFallback);
            return points.ToArray();
        }

        private void DrawCursorShapeAt(Vector3 point, Vector3 normal, Vector3 surfaceTangent, float size)
        {
            if (ActiveBrush.shape == BrushPreset.Shape.Circle) Handles.DrawWireDisc(point, normal, size);
            else
            {
                Vector3 tangent = surfaceTangent.normalized * size;
                Vector3 bitangent = Vector3.Cross(normal, tangent.normalized) * size;
                Vector3[] points = { point - tangent - bitangent, point + tangent - bitangent, point + tangent + bitangent, point - tangent + bitangent, point - tangent - bitangent };
                Handles.DrawAAPolyLine(2f, points);
            }
        }

        private static Vector3 CalculateTangent(ReconstructedSurface surface, int triangleIndex)
        {
            Vector4[] tangents = surface.mesh.tangents; int[] triangles = surface.mesh.triangles;
            int offset = triangleIndex * 3;
            if (tangents.Length == surface.mesh.vertexCount && offset + 2 < triangles.Length)
            {
                Vector4 t = tangents[triangles[offset]];
                return new Vector3(t.x, t.y, t.z);
            }
            return Vector3.right;
        }

        private Bounds CalculateBounds()
        {
            Bounds bounds = controller.Reconstruction.surfaces[0].collider.bounds;
            for (int i = 1; i < controller.Reconstruction.surfaces.Count; i++) bounds.Encapsulate(controller.Reconstruction.surfaces[i].collider.bounds);
            return bounds;
        }

        private TexturePaintStageState BuildState()
        {
            TexturePaintStageState state = controller.CaptureState();
            TexturePaintStageState previous = controller.LoadRecipeState();
            if (previous != null)
            {
                state.exportTemplateGuid = previous.exportTemplateGuid;
                state.exportedTexturePaths = previous.exportedTexturePaths != null
                    ? new List<string>(previous.exportedTexturePaths) : new List<string>();
                state.exportRecords = previous.exportRecords != null
                    ? new List<TexturePaintExportRecord>(previous.exportRecords) : new List<TexturePaintExportRecord>();
            }
            state.selectedSurface = selectedSurface; state.selectedChannel = selectedChannel; state.sourceMode = sourceMode; state.tool = tool;
            state.paintSource = paintSource; state.sourceColor = paintColor; state.mirrorX = mirrorX; state.exportFolder = exportFolder;
            state.limitStrokeCoverage = limitStrokeCoverage;
            state.normalConvention = normalConvention;
            state.strokeStabilization = strokeStabilization;
            state.directionSmoothing = directionSmoothing;
            state.projectionDepth = projectionDepth;
            state.normalAngleLimit = normalAngleLimit;
            state.paintBackfaces = paintBackfaces;
            state.pressureAffectsFlow = pressureAffectsFlow;
            state.pressureAffectsSize = pressureAffectsSize;
            state.historyBudgetMB = historyBudgetMB;
            state.coverageBudgetMB = coverageBudgetMB;
            state.pluginProfiles = controller.Plugins.CaptureProfiles();
            state.workspaceLeftWidth = workspaceLeftWidth; state.workspaceRightWidth = workspaceRightWidth;
            state.workspaceShelfHeight = workspaceShelfHeight; state.workspaceShowToolRail = workspaceShowToolRail;
            state.workspaceShowTargets = workspaceShowTargets; state.workspaceShowLayers = workspaceShowLayers;
            state.workspaceShowProperties = workspaceShowProperties; state.workspaceShowAssetShelf = workspaceShowAssetShelf;
            state.workspaceShowUV = workspaceShowUV; state.workspaceLeftTab = workspaceLeftTab; state.workspaceRightTab = workspaceRightTab;
            state.workspaceUVPan = workspaceUVPan; state.workspaceUVZoom = workspaceUVZoom;
            state.channelSolo = channelSolo; state.previewBefore = previewBefore; state.uvPreviewBefore = uvPreviewBefore;
            state.layerMaskMode = layerMaskMode; state.soloLayerMask = soloLayerMask;
            state.layerMaskPaintValue = layerMaskPaintValue;
            state.isolateSelectedSlots = isolateSelectedSlots; state.wireframe = wireframe;
            state.assetShelfSearch = assetShelfSearch; state.assetShelfFolder = assetShelfFolder;
            state.assetShelfFavoritesOnly = assetShelfFavoritesOnly; state.assetShelfRecentOnly = assetShelfRecentOnly;
            state.favoriteBrushGuids = new List<string>(favoriteBrushGuids ?? new List<string>());
            state.recentBrushGuids = new List<string>(recentBrushGuids ?? new List<string>());
            state.brushOrderGuids = new List<string>(brushOrderGuids ?? new List<string>());
            state.collapsedLayerGroupIds = new List<string>(workspaceCollapsedLayerGroupIds ??
                new List<string>());
            state.collapsedPropertySectionIds = new List<string>(workspaceCollapsedPropertySectionIds ??
                new List<string>());
            if (document != null)
                state.documentGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(document));
            state.selectedSlots = new List<string>(selectedSlots);
            if (paintSourceSprite != null)
                state.sourceSpriteGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(paintSourceSprite).ToString();
            else if (paintSourceTexture != null)
                state.sourceTextureGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(paintSourceTexture));
            if (paintSourceOverlay != null) state.sourceOverlayGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(paintSourceOverlay));
            if (brush != null) state.brushAssetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(brush));
            if (currentBrushLibrary != null)
                state.brushLibraryGuid = AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(currentBrushLibrary));
            return state;
        }

        private void ExportMaterial(TextureSet set, bool addressable)
        {
            TexturePaintStageState state = BuildState();
            TexturePaintExporter.Export(set, exportFolder, addressable, state);
        }

        private void ExportAll(bool addressable)
        {
            TexturePaintStageState state = BuildState();
            TexturePaintExporter.ExportAll(controller.Textures, exportFolder, addressable, state);
        }

        private void RestoreState(TexturePaintStageState state, bool restoreLegacyLayers)
        {
            if (state == null) return;
            selectedSurface = Mathf.Clamp(state.selectedSurface, 0, controller.Textures.Sets.Count - 1);
            selectedChannel = state.selectedChannel; sourceMode = state.sourceMode; paintSource = state.paintSource; tool = state.tool;
            paintColor = state.sourceColor; mirrorX = state.mirrorX;
            limitStrokeCoverage = state.limitStrokeCoverage;
            normalConvention = state.normalConvention;
            strokeStabilization = state.strokeStabilization;
            directionSmoothing = state.directionSmoothing;
            projectionDepth = state.version < 6 ? 0.5f : state.projectionDepth;
            normalAngleLimit = state.version < 6 ? 90f : state.normalAngleLimit;
            paintBackfaces = state.paintBackfaces;
            pressureAffectsFlow = state.version < 6 || state.pressureAffectsFlow;
            pressureAffectsSize = state.pressureAffectsSize;
            historyBudgetMB = Mathf.Clamp(state.historyBudgetMB <= 0 ? 256 : state.historyBudgetMB, 16, 1024);
            coverageBudgetMB = Mathf.Clamp(state.coverageBudgetMB <= 0 ? 128 : state.coverageBudgetMB, 16, 512);
            controller.Plugins.RestoreProfiles(state.pluginProfiles);
            if (state.version >= 10)
            {
                workspaceLeftWidth = Mathf.Clamp(state.workspaceLeftWidth, 180f, 360f);
                workspaceRightWidth = Mathf.Clamp(state.workspaceRightWidth, 210f, 440f);
                workspaceShelfHeight = Mathf.Clamp(state.workspaceShelfHeight, 112f, 400f);
                workspaceShowToolRail = state.workspaceShowToolRail; workspaceShowTargets = state.workspaceShowTargets;
                workspaceShowLayers = state.workspaceShowLayers; workspaceShowProperties = state.workspaceShowProperties;
                workspaceShowAssetShelf = state.workspaceShowAssetShelf; workspaceShowUV = state.workspaceShowUV;
                workspaceLeftTab = Mathf.Clamp(state.workspaceLeftTab, 0, 1); workspaceRightTab = Mathf.Clamp(state.workspaceRightTab, 0, 1);
                workspaceUVPan = state.workspaceUVPan; workspaceUVZoom = Mathf.Clamp(state.workspaceUVZoom, 0.2f, 8f);
                channelSolo = state.channelSolo; previewBefore = state.previewBefore;
                if (previewBefore) channelSolo = false;
                uvPreviewBefore = state.version >= 11 && state.uvPreviewBefore;
                layerMaskMode = state.version >= 14 && state.layerMaskMode;
                soloLayerMask = state.version >= 14 && state.soloLayerMask;
                layerMaskPaintValue = state.version >= 14 ? Mathf.Clamp01(state.layerMaskPaintValue) : 1f;
                isolateSelectedSlots = state.isolateSelectedSlots; wireframe = state.wireframe;
                assetShelfSearch = state.assetShelfSearch; assetShelfFolder = state.assetShelfFolder;
                assetShelfFavoritesOnly = state.assetShelfFavoritesOnly; assetShelfRecentOnly = state.assetShelfRecentOnly;
                favoriteBrushGuids = state.favoriteBrushGuids != null ? new List<string>(state.favoriteBrushGuids) : new List<string>();
                recentBrushGuids = state.recentBrushGuids != null ? new List<string>(state.recentBrushGuids) : new List<string>();
                brushOrderGuids = state.brushOrderGuids != null ? new List<string>(state.brushOrderGuids) : new List<string>();
                workspaceCollapsedLayerGroupIds = state.collapsedLayerGroupIds != null
                    ? new List<string>(state.collapsedLayerGroupIds)
                    : new List<string>();
                workspaceCollapsedPropertySectionIds = state.version >= 15 &&
                    state.collapsedPropertySectionIds != null
                        ? new List<string>(state.collapsedPropertySectionIds)
                        : new List<string>();
            }
            selectedSlots = state.selectedSlots != null ? new List<string>(state.selectedSlots) : new List<string>();
            Texture2D restoredSourceTexture = !string.IsNullOrEmpty(state.sourceTextureGuid)
                ? AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(state.sourceTextureGuid)) : null;
            Sprite restoredSourceSprite = null;
            if (!string.IsNullOrEmpty(state.sourceSpriteGlobalId) &&
                GlobalObjectId.TryParse(state.sourceSpriteGlobalId, out GlobalObjectId spriteId))
                restoredSourceSprite = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(spriteId) as Sprite;
            RestorePaintSource(restoredSourceTexture, restoredSourceSprite);
            if (!string.IsNullOrEmpty(state.sourceOverlayGuid)) paintSourceOverlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(AssetDatabase.GUIDToAssetPath(state.sourceOverlayGuid));
            if (!string.IsNullOrEmpty(state.exportFolder))
            {
                exportFolder = state.exportFolder == "Assets/UMA/TexturePaintStage/Generated" ||
                    state.exportFolder == "Assets/UMA/OverlayPainter/Generated"
                    ? UMAPathUtility.OverlayPainterGeneratedRoot
                    : state.exportFolder;
            }
            if (!string.IsNullOrEmpty(state.brushAssetGuid))
                SelectBrushPreset(AssetDatabase.LoadAssetAtPath<BrushPreset>(
                    AssetDatabase.GUIDToAssetPath(state.brushAssetGuid)));
            if (!string.IsNullOrEmpty(state.brushLibraryGuid))
                currentBrushLibrary = AssetDatabase.LoadAssetAtPath<BrushLibrary>(
                    AssetDatabase.GUIDToAssetPath(state.brushLibraryGuid));
            if (restoreLegacyLayers)
            for (int materialIndex = 0; materialIndex < state.materials.Count; materialIndex++)
            {
                TexturePaintMaterialState savedMaterial = state.materials[materialIndex];
                TextureSet set = controller.Textures.FindSet(savedMaterial.surfaceIndex);
                if (set == null) continue;
                for (int layerIndex = 0; layerIndex < savedMaterial.layers.Count; layerIndex++)
                {
                    TexturePaintLayerState savedLayer = savedMaterial.layers[layerIndex];
                    TexturePaintLayer layer = savedLayer.isSplineLayer
                        ? set.AddSplineLayer(savedLayer.name)
                        : set.AddLayer(savedLayer.name);
                    layer.visible = savedLayer.visible;
                    layer.opacity = savedLayer.opacity;
                    layer.blendMode = savedLayer.blendMode;
                    layer.effects = savedLayer.effects?.Clone() ?? new TexturePaintLayerEffects();
                    if (savedLayer.isSplineLayer && savedLayer.spline != null) layer.spline = savedLayer.spline;
                }
                set.activeLayerIndex = Mathf.Clamp(savedMaterial.activeLayer, -1, set.layers.Count - 1);
            }
            TextureSet activeSet = controller.Textures.Sets[selectedSurface];
            if (activeSet.activeLayerIndex >= 0 && activeSet.activeLayerIndex < activeSet.layers.Count)
            {
                TexturePaintLayer activeLayer = activeSet.layers[activeSet.activeLayerIndex];
                activeLayer.NormalizeKindPayload();
                spline = activeLayer.IsSplineLayer ? activeLayer.spline : null;
                splineMode = activeLayer.IsSplineLayer && activeLayer.spline?.worldSpace == true;
            }
        }

        private static void RepaintAll()
        {
            SceneView.RepaintAll();
            TexturePaintDockWindow.RepaintOpenWindows();
            TexturePaintUVWindow.RepaintOpenWindows();
        }
    }

    public sealed class TexturePaintDockWindow : EditorWindow
    {
        private static readonly HashSet<TexturePaintDockWindow> openWindows = new HashSet<TexturePaintDockWindow>();
        private static bool closeDecisionPending;
        private static bool editorQuitting;
        private static bool domainReloading;

        [InitializeOnLoadMethod]
        private static void InitializeCloseHandling()
        {
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        [MenuItem("Window/UMA/Overlay Painter")]
        public static void ShowDockable()
        {
            TexturePaintDockWindow window = GetWindow<TexturePaintDockWindow>();
            window.titleContent = new GUIContent("Overlay Painter", EditorGUIUtility.IconContent("Texture Icon").image);
            window.minSize = new Vector2(500f, 360f);
            window.Show();
            window.Focus();
        }

        internal static void RepaintOpenWindows()
        {
            foreach (TexturePaintDockWindow window in openWindows)
                if (window != null) window.Repaint();
        }

        private void OnEnable()
        {
            openWindows.Add(this);
            titleContent = new GUIContent("Overlay Painter", EditorGUIUtility.IconContent("Texture Icon").image);
            minSize = new Vector2(500f, 360f);
        }

        private void OnDisable()
        {
            openWindows.Remove(this);
        }

        private void OnDestroy()
        {
            TexturePaintStageWindow stage = ResolveStage();
            if (editorQuitting || domainReloading || EditorApplication.isCompiling || closeDecisionPending ||
                stage == null || stage.Controller == null || HasAnotherOpenWindow()) return;

            closeDecisionPending = true;
            EditorApplication.delayCall += () => CloseStageAfterWindow(stage);
        }

        private void OnGUI()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            if (stage == null) stage = StageUtility.GetCurrentStage() as TexturePaintStageWindow;
            if (stage == null || stage.Controller == null)
            {
                EditorGUILayout.HelpBox(
                    "Open Overlay Painter from a SlotDataAssets Inspector, or a generated DynamicCharacterAvatar's Utilities section.",
                    MessageType.Info);
                return;
            }
            stage.DrawWorkspace(position);
        }

        private static void OnEditorQuitting()
        {
            editorQuitting = true;
        }

        private static void OnBeforeAssemblyReload()
        {
            domainReloading = true;
        }

        private bool HasAnotherOpenWindow()
        {
            foreach (TexturePaintDockWindow window in openWindows)
            {
                if (window != null && window != this) return true;
            }
            return false;
        }

        private static TexturePaintStageWindow ResolveStage()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            return stage ?? StageUtility.GetCurrentStage() as TexturePaintStageWindow;
        }

        private static void CloseStageAfterWindow(TexturePaintStageWindow requestedStage)
        {
            closeDecisionPending = false;
            TexturePaintStageWindow currentStage = StageUtility.GetCurrentStage() as TexturePaintStageWindow;
            if (currentStage == null || currentStage.Controller == null) return;

            if (!ReferenceEquals(currentStage, requestedStage))
            {
                ShowDockable();
                return;
            }

            try
            {
                if (!currentStage.RequestCloseStage()) ShowDockable();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowDockable();
            }
        }

    }

    /// <summary>
    /// Dockable host for the stage-owned 2D texture canvas. All selection, painting,
    /// history, and texture state remains on the active TexturePaintStageWindow.
    /// </summary>
    public sealed class TexturePaintUVWindow : EditorWindow
    {
        private static readonly HashSet<TexturePaintUVWindow> openWindows = new HashSet<TexturePaintUVWindow>();

        [MenuItem("Window/UMA/Overlay Painter 2D")]
        public static void ShowDockable()
        {
            TexturePaintUVWindow window = GetWindow<TexturePaintUVWindow>();
            window.Configure();
            window.Show();
            window.Focus();
        }

        internal static void RepaintOpenWindows()
        {
            foreach (TexturePaintUVWindow window in openWindows)
                if (window != null) window.Repaint();
        }

        private void OnEnable()
        {
            openWindows.Add(this);
            Configure();
            wantsMouseMove = true;
        }

        private void OnLostFocus()
        {
            ResolveStage()?.EndUVWindowInteraction();
        }

        private void OnDisable()
        {
            openWindows.Remove(this);
            ResolveStage()?.EndUVWindowInteraction();
        }

        private void OnGUI()
        {
            TexturePaintStageWindow stage = ResolveStage();
            if (stage == null || stage.Controller == null)
            {
                EditorGUILayout.HelpBox(
                    "Open Overlay Painter from a SlotDataAssets Inspector, or a generated DynamicCharacterAvatar's Utilities section.",
                    MessageType.Info);
                return;
            }
            stage.DrawUVWorkspace(new Rect(0f, 0f, position.width, position.height));
            if (Event.current.type == EventType.MouseMove) Repaint();
        }

        private void Configure()
        {
            titleContent = new GUIContent("Overlay Painter 2D", EditorGUIUtility.IconContent("Texture Icon").image);
            minSize = new Vector2(360f, 300f);
        }

        private static TexturePaintStageWindow ResolveStage()
        {
            TexturePaintStageWindow stage = TexturePaintStageWindow.ActiveStage;
            return stage ?? StageUtility.GetCurrentStage() as TexturePaintStageWindow;
        }
    }

    internal static class TexturePaintDocumentAssetOpen
    {
        [UnityEditor.Callbacks.OnOpenAsset(0)]
        public static bool OpenDocument(int instanceId, int line)
        {
            _ = line;
            TexturePaintDocument document = EditorUtility.EntityIdToObject(instanceId) as TexturePaintDocument;
            return document != null && TexturePaintStageWindow.OpenDocumentAsset(document);
        }
    }
}
