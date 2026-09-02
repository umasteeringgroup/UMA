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

namespace UMA.HairCards.Editor
{
    public sealed class HairCardStage : PreviewSceneStage
    {
        private const double RebuildDelay = 0.04d;
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

        [SerializeField] private HairGroomAsset groom;
        [SerializeField] private DynamicCharacterAvatar sourceAvatar;
        [SerializeField] private HairWorkflowStep workflowStep;
        [SerializeField] private HairSceneTool sceneTool = HairSceneTool.Select;
        [SerializeField] private HairPreviewMode previewMode = HairPreviewMode.Cards;
        [SerializeField] private HairSceneTool lastGrowthTool = HairSceneTool.PaintGrowth;
        [SerializeField] private HairSceneTool lastGuideTool = HairSceneTool.Select;
        [SerializeField] private HairSceneTool lastGroomTool = HairSceneTool.Comb;
        [SerializeField] private string activeGroupId;
        [SerializeField] private string activeMapId;
        [SerializeField] private string activeGuideId;
        [SerializeField] private string activeLayerId;
        [SerializeField] private string activeHelperId;
        [SerializeField] private int activeGuidePoint = -1;
        [SerializeField] private int lodLevel;
        [SerializeField] private float brushRadius = 0.075f;
        [SerializeField] private float brushHardness = HairBrushInteractionUtility.DefaultHardness;
        [SerializeField] private float brushStrength = 0.5f;
        [SerializeField] private float paintValue = 1f;
        [SerializeField] private bool paintErase;
        [SerializeField] private bool mirrorPaintX;
        [SerializeField] private bool showScalp = true;
        [SerializeField] private bool showAvatar = true;
        [SerializeField] private bool showChildren = true;
        [SerializeField] private bool showHelpers = true;
        [SerializeField] private bool showControlPoints = true;
        [SerializeField] private List<string> hiddenAvatarSlots = new List<string>();
        [SerializeField] private HairGuideGenerationSettings guideGeneration = new HairGuideGenerationSettings();
        [SerializeField] private List<int> selectedVertices = new List<int>();

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
        private HairEvaluationResult evaluation;
        private HairCardMeshBuildResult meshBuild;
        private HairValidationReport validation;
        private HairGuideGenerationResult generationPreview;
        private bool rebuildQueued;
        private double rebuildNotBefore;
        private double nextAutosave;
        private bool strokeActive;
        private bool modifierBrushDrag;
        private int modifierBrushHotControl;
        private Vector2 modifierBrushStartMouse;
        private Vector2 modifierBrushCurrentMouse;
        private float modifierBrushStartRadius;
        private float modifierBrushStartHardness;
        private Vector3 previousStrokePosition;
        private bool hasPreviousStrokePosition;
        private int sceneInputHotControl;
        private Tool previousUnityTool;
        private bool previousToolsHidden;
        private bool unityToolStateCaptured;
        private bool needsFrame = true;
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
        private readonly Dictionary<string, HairEvaluatedCurve> displayGuideCurves =
            new Dictionary<string, HairEvaluatedCurve>(StringComparer.Ordinal);
        private readonly Dictionary<string, Matrix4x4> helperPoseMatrices =
            new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);

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
                ReleaseSceneInputCapture(true);
                if (targetStep != workflowStep)
                {
                    RememberToolForStep(workflowStep, sceneTool);
                    workflowStep = targetStep;
                    previewMode = HairWorkflowState.DefaultPreview(targetStep);
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
                previewMode = value;
                QueueRebuild();
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
        public float PaintValue { get => paintValue; set => paintValue = value; }
        public bool PaintErase { get => paintErase; set => paintErase = value; }
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
        public bool ShowScalp { get => showScalp; set { if (showScalp == value) return; showScalp = value; ApplyVisibility(); } }
        public bool ShowAvatar { get => showAvatar; set { if (showAvatar == value) return; showAvatar = value; ApplyVisibility(); } }
        public bool ShowChildren { get => showChildren; set { if (showChildren == value) return; showChildren = value; QueueRebuild(); } }
        public bool ShowHelpers { get => showHelpers; set { if (showHelpers == value) return; showHelpers = value; RepaintAll(); } }
        public bool ShowControlPoints { get => showControlPoints; set { if (showControlPoints == value) return; showControlPoints = value; RepaintAll(); } }
        public HairGuideGenerationSettings GuideGeneration => guideGeneration;
        public HairEvaluationResult Evaluation => evaluation;
        public HairCardMeshBuildResult MeshBuild => meshBuild;
        public HairValidationReport Validation => validation;
        public HairGuideGenerationResult GenerationPreview => generationPreview;
        public string ActiveGroupId => activeGroupId;
        public string ActiveMapId => activeMapId;
        public string ActiveGuideId => activeGuideId;
        public string ActiveLayerId => activeLayerId;
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
            HairCardStage stage = CreateInstance<HairCardStage>();
            stage.groom = asset;
            stage.sourceAvatar = avatar;
            stage.showScalp = avatar == null;
            stage.showAvatar = avatar != null;
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
            groom.EnsureIntegrity();
            if (groom.SourceMesh == null)
            {
                EditorUtility.DisplayDialog("Hair Card Stage",
                    "Assign a readable source scalp mesh before opening the Hair Card Stage.", "OK");
                return false;
            }

            EnsureEditorEvents();
            activeGroupId = groom.FindGroup(activeGroupId)?.Id ?? FirstGroup()?.Id;
            HairGroup group = ActiveGroup;
            activeMapId = group?.maps?.Find(map => map != null && map.Id == activeMapId)?.Id ??
                          group?.FindMap(HairMapKind.GrowthArea)?.Id;
            activeLayerId = group?.sculptLayers?.Find(layer => layer != null && layer.Id == activeLayerId)?.Id;
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
            ReleaseSceneInputCapture(true);
            RememberToolForStep(workflowStep, sceneTool);
            workflowStep = step;
            sceneTool = RememberedToolForStep(step);
            previewMode = HairWorkflowState.DefaultPreview(step);
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
                previewMode = HairWorkflowState.DefaultPreview(workflowStep);
            actionStatus = StepInstruction(workflowStep);
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
                HairSceneTool.Comb => "Drag across guide curves in the desired flow direction.",
                HairSceneTool.Grab => "Drag across guide curves to move nearby points.",
                HairSceneTool.Smooth => "Drag across guide curves to relax their shape.",
                _ => $"Drag across guide curves with {ObjectNames.NicifyVariableName(tool.ToString())}."
            };
        }

        protected override void OnCloseStage()
        {
            closing = true;
            ReleaseSceneInputCapture(true);
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
            activeGroupId = group.Id;
            activeMapId = group.FindMap(HairMapKind.GrowthArea)?.Id;
            activeGuideId = string.Empty;
            activeGuidePoint = -1;
            activeLayerId = group.sculptLayers.Count > 0 ? group.sculptLayers[group.sculptLayers.Count - 1].Id : string.Empty;
            generationPreview = null;
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
            RepaintAll();
        }

        public void SetActiveLayer(string layerId)
        {
            activeLayerId = ActiveGroup?.sculptLayers?.Find(layer => layer != null && layer.Id == layerId)?.Id;
            RepaintAll();
        }

        public void SetActiveHelper(string helperId)
        {
            activeHelperId = groom?.FindHelper(helperId)?.Id;
            RepaintAll();
        }

        public void QueueRebuild(bool immediate = false)
        {
            if (closing) return;
            rebuildQueued = true;
            rebuildNotBefore = immediate ? 0d : EditorApplication.timeSinceStartup + RebuildDelay;
            RepaintAll();
        }

        public void RebuildNow()
        {
            rebuildQueued = false;
            SyncExternalHelpers();
            RefreshHelperPoseMatrices();
            DisposeBuild();
            evaluation = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions
            {
                lodLevel = lodLevel,
                includeChildren = showChildren && previewMode != HairPreviewMode.Guides,
                includeGuideCards = true,
                applySculptLayers = true,
                applyModifiers = true,
                applyConstraints = true,
                includeHiddenGroups = false,
                interactiveSampleLimit = strokeActive ? 5000 : 0
            });
            displayGuideCurves.Clear();
            for (int curveIndex = 0; curveIndex < evaluation.evaluatedGuides.Count; curveIndex++)
            {
                HairEvaluatedCurve curve = evaluation.evaluatedGuides[curveIndex];
                if (curve == null || string.IsNullOrEmpty(curve.parentGuideId) ||
                    displayGuideCurves.ContainsKey(curve.parentGuideId)) continue;
                displayGuideCurves.Add(curve.parentGuideId, curve);
            }
            HairEvaluationResult previewEvaluation = authoringPose?.TransformEvaluation(groom, evaluation) ?? evaluation;
            meshBuild = HairCardMeshGenerator.Build(previewEvaluation, groom.name + " Preview");
            validation = HairValidator.Validate(groom, evaluation, meshBuild, new HairValidationOptions
            {
                triangleBudget = groom.BakeSettings.triangleBudget,
                cardBudget = groom.BakeSettings.cardBudget,
                requireAtlas = groom.BakeSettings.requireAtlas
            });
            for (int helperIndex = 0; helperIndex < groom.SharedHelpers.Count; helperIndex++)
            {
                HairHelper helper = groom.SharedHelpers[helperIndex];
                if (helper != null && !helper.embedded && !TryResolveExternalHelperObject(helper, out _))
                    validation.Add(HairValidationSeverity.Error, HairValidationCode.MissingHelper,
                        $"External helper '{helper.name}' is not available in an open scene.",
                        helperId: helper.Id, fixId: "repair-helper-reference");
            }
            if (hairFilter != null) hairFilter.sharedMesh = meshBuild.mesh;
            ApplyHairMaterials();
            ApplyVisibility();
            RepaintAll();
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
            if (groom == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(groom))) return;
            EditorUtility.SetDirty(groom);
            AssetDatabase.SaveAssetIfDirty(groom);
            if (createRecovery) HairGroomRecovery.SaveSnapshot(groom);
            nextAutosave = EditorApplication.timeSinceStartup + AutosaveDelay;
        }

        public void FrameGroom()
        {
            needsFrame = true;
            SceneView.RepaintAll();
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
            int count = Mathf.Max(1, meshBuild.mesh != null ? meshBuild.mesh.subMeshCount : 1);
            Material[] materials = new Material[count];
            for (int i = 0; i < count; i++)
            {
                materials[i] = i < meshBuild.materials.Count && meshBuild.materials[i] != null
                    ? meshBuild.materials[i]
                    : fallbackHairMaterial;
            }
            hairRenderer.sharedMaterials = materials;
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
            if (hairRenderer != null)
            {
                hairRenderer.enabled = previewMode == HairPreviewMode.Cards ||
                                       previewMode == HairPreviewMode.CardGroups ||
                                        previewMode == HairPreviewMode.Wireframe;
            }
            RepaintAll();
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
            if (rebuildQueued && EditorApplication.timeSinceStartup >= rebuildNotBefore) RebuildNow();
            if (EditorApplication.timeSinceStartup >= nextAutosave) SaveNow();
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
            QueueRebuild(true);
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (sceneView == null || groom == null) return;
            DrawSceneToolbar(sceneView);
            using (new Handles.DrawingScope(SourceToStageMatrix))
            {
                DrawGuides();
                DrawEvaluatedChildren();
                if (showHelpers) DrawHelpers();
                DrawGenerationPreview();
                DrawVertexSelection();
            }
            HandleSceneInput(sceneView);
            if (needsFrame)
            {
                needsFrame = false;
                if (TryGetPreviewBounds(out Bounds bounds)) sceneView.Frame(bounds, false);
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

        private float WorldBrushRadiusForPose(Matrix4x4 sourceToPose)
        {
            Matrix4x4 sourceToWorld = SourceToStageMatrix * sourceToPose;
            float x = sourceToWorld.MultiplyVector(Vector3.right).magnitude;
            float y = sourceToWorld.MultiplyVector(Vector3.up).magnitude;
            float z = sourceToWorld.MultiplyVector(Vector3.forward).magnitude;
            return brushRadius * Mathf.Max(x, y, z);
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
            HairPreviewMode toolbarPreview = (HairPreviewMode)EditorGUILayout.EnumPopup(previewMode,
                EditorStyles.toolbarPopup, GUILayout.Width(145f));
            if (toolbarPreview != previewMode) PreviewMode = toolbarPreview;
            GUILayout.Label("Radius", GUILayout.Width(42f));
            brushRadius = GUILayout.HorizontalSlider(brushRadius, HairBrushInteractionUtility.MinimumRadius,
                HairBrushInteractionUtility.MaximumRadius, GUILayout.Width(82f));
            GUILayout.Label("Hard", GUILayout.Width(31f));
            brushHardness = GUILayout.HorizontalSlider(brushHardness, 0f, 1f, GUILayout.Width(70f));
            GUILayout.Label("Strength", GUILayout.Width(52f));
            brushStrength = GUILayout.HorizontalSlider(brushStrength, 0.01f, 1f, GUILayout.Width(80f));
            if (sceneTool == HairSceneTool.PaintGrowth)
            {
                bool mirrored = GUILayout.Toggle(mirrorPaintX, "Mirror X", EditorStyles.toolbarButton,
                    GUILayout.Width(66f));
                if (mirrored != mirrorPaintX) MirrorPaintX = mirrored;
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
            GUILayout.EndArea();
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
                    if (guide == null || !guide.enabled || guide.points == null || guide.points.Count < 2) continue;
                    Matrix4x4 oldMatrix = Handles.matrix;
                    Handles.matrix = oldMatrix * GuidePoseMatrix(guide.Id);
                    try
                    {
                        bool selected = guide.Id == activeGuideId;
                        Handles.color = selected ? Color.yellow : group.color;
                        IReadOnlyList<HairCurvePoint> displayPoints = displayGuideCurves.TryGetValue(guide.Id,
                            out HairEvaluatedCurve displayCurve) ? displayCurve.points : null;
                        if (displayPoints != null && displayPoints.Count > 1)
                        {
                            for (int pointIndex = 1; pointIndex < displayPoints.Count; pointIndex++)
                                Handles.DrawAAPolyLine(selected ? 4f : 2f,
                                    displayPoints[pointIndex - 1].position, displayPoints[pointIndex].position);
                        }
                        else
                        {
                            for (int pointIndex = 1; pointIndex < guide.points.Count; pointIndex++)
                                Handles.DrawAAPolyLine(selected ? 4f : 2f,
                                    guide.points[pointIndex - 1].position, guide.points[pointIndex].position);
                        }
                        Vector3 rootPosition = displayPoints != null && displayPoints.Count > 0
                            ? displayPoints[0].position
                            : guide.points[0].position;
                        float rootSize = LocalHandleSize(rootPosition) * 0.035f;
                        if (Handles.Button(rootPosition, Quaternion.identity, rootSize, rootSize,
                                Handles.DotHandleCap))
                            SetActiveGuide(guide.Id, 0);
                        if (selected && showControlPoints)
                        {
                            for (int pointIndex = 0; pointIndex < guide.points.Count; pointIndex++)
                            {
                                HairGuidePoint point = guide.points[pointIndex];
                                float size = LocalHandleSize(point.position) * 0.025f;
                                Handles.color = pointIndex == activeGuidePoint ? Color.white : Color.yellow;
                                if (Handles.Button(point.position, Quaternion.identity, size, size,
                                        Handles.SphereHandleCap))
                                    activeGuidePoint = pointIndex;
                            }
                            DrawSelectedPointHandle(guide);
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
            if (!showChildren || previewMode != HairPreviewMode.GuidesAndChildren || evaluation?.curves == null)
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
            if (guide == null || activeGuidePoint < 0 || activeGuidePoint >= guide.points.Count) return;
            HairGuidePoint point = guide.points[activeGuidePoint];
            EditorGUI.BeginChangeCheck();
            Vector3 position = Handles.PositionHandle(point.position, Quaternion.identity);
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(groom, "Move Hair Guide Point");
            if (activeGuidePoint == 0)
            {
                Vector3 target = position;
                if (HairMeshUtility.TryFindClosestSurface(groom.SourceMesh, groom.SourceMeshId, position,
                        out HairSurfaceAnchor rootAnchor))
                {
                    target = rootAnchor.CachedLocalPosition;
                    guide.root = rootAnchor;
                }
                Vector3 delta = target - point.position;
                for (int i = 0; i < guide.points.Count; i++) guide.points[i].position += delta;
            }
            else point.position = position;
            HairGroomCommands.Commit(groom);
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

        private void HandleSceneInput(SceneView sceneView)
        {
            Event current = Event.current;
            if (current == null) return;

            int controlId = GUIUtility.GetControlID(SceneInputControlHint, FocusType.Passive);
            int brushModifierControlId = GUIUtility.GetControlID(BrushModifierControlHint, FocusType.Passive);
            if (current.type == EventType.Layout) HandleUtility.AddDefaultControl(controlId);
            if (current.type == EventType.MouseMove || current.type == EventType.MouseDrag)
                sceneView.Repaint();

            if (current.type == EventType.MouseLeaveWindow)
            {
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

            if (sceneTool == HairSceneTool.Select && workflowStep == HairWorkflowStep.Growth &&
                HandleVertexSelectionEvent(current, controlId))
            {
                return;
            }
            bool interactiveTool = sceneTool != HairSceneTool.Select && sceneTool != HairSceneTool.Helper;
            if (!interactiveTool) return;
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (IsGroomTool(sceneTool) && TryGetCurveBrushCenter(ray, out Vector3 curveCenter,
                    out Vector3 curveWorldCenter, out Matrix4x4 poseToSource))
            {
                HandleCurveBrushEvent(sceneView, current, curveCenter, curveWorldCenter,
                    poseToSource, controlId);
                return;
            }
            if (!TryRaycastSourceSurface(ray, out HairSurfaceHit hit))
            {
                ConsumeMissedSurfaceGesture(current, controlId);
                return;
            }

            Color brushColor = paintErase ? new Color(1f, 0.2f, 0.2f, 0.9f) :
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
            SceneView.RepaintAll();

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
                    hit = new HairSurfaceHit(surfaceHit.TriangleIndex, SourceToStagePoint(surfaceHit.Point),
                        SourceToStageNormal(surfaceHit.Normal), sourcePoint, sourceNormal);
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

        private void HandleCurveBrushEvent(SceneView sceneView, Event current, Vector3 curveCenter,
            Vector3 curveWorldCenter, Matrix4x4 poseToSource, int controlId)
        {
            Vector3 worldViewNormal = sceneView.camera != null ? -sceneView.camera.transform.forward : Vector3.up;
            Vector3 viewNormal = poseToSource.MultiplyVector(StageToSourceDirection(worldViewNormal)).normalized;
            Color brushColor = paintErase ? new Color(1f, 0.2f, 0.2f, 0.9f) :
                new Color(0.2f, 0.9f, 1f, 0.9f);
            DrawBrushCursor(curveWorldCenter, worldViewNormal,
                WorldBrushRadiusForPose(poseToSource.inverse), brushColor);
            if (current.type == EventType.MouseDown)
            {
                if (!CanCaptureSceneInput(controlId)) return;
                CaptureSceneInput(controlId);
                BeginStroke();
                SculptAt(curveCenter, viewNormal, Vector3.zero);
                previousStrokePosition = curveCenter;
                hasPreviousStrokePosition = true;
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && OwnsSceneInput(controlId) && strokeActive)
            {
                Vector3 delta = hasPreviousStrokePosition ? curveCenter - previousStrokePosition : Vector3.zero;
                SculptAt(curveCenter, viewNormal, delta);
                previousStrokePosition = curveCenter;
                hasPreviousStrokePosition = true;
                EditorUtility.SetDirty(groom);
                QueueRebuild();
                current.Use();
            }
            else if (current.type == EventType.MouseUp && OwnsSceneInput(controlId))
            {
                ReleaseSceneInputCapture(true);
                current.Use();
            }
            SceneView.RepaintAll();
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
            if (finishStroke) EndStroke();
            if (sceneInputHotControl != 0 && GUIUtility.hotControl == sceneInputHotControl)
                GUIUtility.hotControl = 0;
            sceneInputHotControl = 0;
            ReleaseBrushModifierCapture();
        }

        private bool HandleBrushShortcuts(Event current, SceneView sceneView)
        {
            if (current == null || current.type != EventType.KeyDown || EditorGUIUtility.editingTextField ||
                !SupportsBrushAdjustment() || current.control || current.command || current.alt ||
                Tools.viewToolActive)
                return false;
            if (!current.shift && current.keyCode == KeyCode.M && sceneTool == HairSceneTool.PaintGrowth)
            {
                MirrorPaintX = !mirrorPaintX;
                current.Use();
                sceneView.Repaint();
                HairGroomWorkspace.RepaintOpenWindows();
                return true;
            }
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
                DrawBrushAdjustmentHud(modifierBrushCurrentMouse);
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
        }

        private bool SupportsBrushAdjustment()
        {
            return sceneTool == HairSceneTool.PaintGrowth || IsGroomTool(sceneTool);
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

        private void DrawBrushAdjustmentHud(Vector2 mousePosition)
        {
            Handles.BeginGUI();
            Rect rectangle = new Rect(mousePosition.x + 18f, mousePosition.y + 18f, 215f, 42f);
            GUI.Box(rectangle, $"Radius  {brushRadius:0.000}\nHardness  {brushHardness:0.00}",
                EditorStyles.helpBox);
            Handles.EndGUI();
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

        private void BeginStroke()
        {
            strokeActive = true;
            hasPreviousStrokePosition = false;
            Undo.RegisterCompleteObjectUndo(groom, SceneToolUndoName());
        }

        private bool TryGetCurveBrushCenter(Ray ray, out Vector3 center, out Vector3 worldCenter,
            out Matrix4x4 poseToSource)
        {
            center = Vector3.zero;
            worldCenter = Vector3.zero;
            poseToSource = Matrix4x4.identity;
            HairGroup group = ActiveGroup;
            if (group?.guides == null) return false;
            Ray posedRay = new Ray(StageToSourcePoint(ray.origin), StageToSourceDirection(ray.direction).normalized);
            float bestSquare = float.MaxValue;
            Vector3 bestPoint = Vector3.zero;
            Matrix4x4 bestGuideToPose = Matrix4x4.identity;
            for (int guideIndex = 0; guideIndex < group.guides.Count; guideIndex++)
            {
                HairGuide guide = group.guides[guideIndex];
                IReadOnlyList<HairCurvePoint> points = guide != null &&
                                                       displayGuideCurves.TryGetValue(guide.Id,
                                                           out HairEvaluatedCurve display)
                    ? display.points
                    : null;
                if (points == null || points.Count < 2) continue;
                Matrix4x4 guideToPose = GuidePoseMatrix(guide.Id);
                Matrix4x4 inversePose = guideToPose.inverse;
                Ray guideRay = new Ray(inversePose.MultiplyPoint3x4(posedRay.origin),
                    inversePose.MultiplyVector(posedRay.direction).normalized);
                for (int pointIndex = 1; pointIndex < points.Count; pointIndex++)
                {
                    if (!HairCurveBrushUtility.TryClosestPoint(guideRay,
                            points[pointIndex - 1].position, points[pointIndex].position,
                            out Vector3 point, out float square)) continue;
                    if (square >= bestSquare) continue;
                    bestSquare = square;
                    bestPoint = point;
                    bestGuideToPose = guideToPose;
                }
            }
            float pickRadius = brushRadius * 1.5f;
            if (bestSquare > pickRadius * pickRadius) return false;
            center = bestPoint;
            poseToSource = bestGuideToPose.inverse;
            worldCenter = SourceToStagePoint(bestGuideToPose.MultiplyPoint3x4(bestPoint));
            return true;
        }

        private static bool IsGroomTool(HairSceneTool tool)
        {
            return tool >= HairSceneTool.Comb && tool <= HairSceneTool.Freeze;
        }

        private void EndStroke()
        {
            if (!strokeActive) return;
            strokeActive = false;
            hasPreviousStrokePosition = false;
            groom.EnsureIntegrity();
            EditorUtility.SetDirty(groom);
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            QueueRebuild();
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
            QueueRebuild();
        }

        private void PaintMapAt(Vector3 localCenter)
        {
            HairGrowthMap map = ActiveMap;
            if (map == null || map.locked || groom.SourceMesh == null) return;
            Vector3[] vertices = groom.SourceMesh.vertices;
            float target = paintErase ? map.valueRange.x : Mathf.Clamp(paintValue, map.valueRange.x, map.valueRange.y);
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
                map.values[i] = Mathf.Lerp(map.values[i], target, falloff * brushStrength);
                if ((uint)i < (uint)growthOverlayColors.Length)
                {
                    growthOverlayColors[i] = GrowthOverlayColor(map, i);
                    overlayChanged = true;
                }
            }
            if (overlayChanged && growthOverlayMesh != null)
                growthOverlayMesh.colors32 = growthOverlayColors;
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
                guide.points[i].stiffness = 1f - t;
            }
            activeGuidePoint = guide.points.Count - 1;
        }

        private void SculptAt(Vector3 center, Vector3 surfaceNormal, Vector3 strokeDelta)
        {
            HairGroup group = ActiveGroup;
            if (group == null || group.locked || group.guides == null) return;
            HairSculptLayer layer = ResolveSculptLayer(group);
            float squareRadius = brushRadius * brushRadius;
            Vector3 average = CalculateNearbyAverage(group, center, squareRadius);
            for (int guideIndex = 0; guideIndex < group.guides.Count; guideIndex++)
            {
                HairGuide guide = group.guides[guideIndex];
                if (guide == null || !guide.enabled || guide.points == null) continue;
                HairGuideDelta delta = layer != null ? ResolveDelta(layer, guide) : null;
                for (int pointIndex = 1; pointIndex < guide.points.Count; pointIndex++)
                {
                    HairGuidePoint point = guide.points[pointIndex];
                    Vector3 actual = point.position + (delta != null ? delta.positionOffsets[pointIndex] : Vector3.zero);
                    float squareDistance = (actual - center).sqrMagnitude;
                    if (squareDistance > squareRadius || point.freeze >= 0.999f) continue;
                    float falloff = HairBrushInteractionUtility.EvaluateFalloff(
                        Mathf.Sqrt(squareDistance), brushRadius, brushHardness) *
                        brushStrength * (1f - point.freeze);
                    Vector3 displacement = Vector3.zero;
                    switch (sceneTool)
                    {
                        case HairSceneTool.Comb:
                            displacement = strokeDelta.sqrMagnitude > 1e-10f
                                ? strokeDelta.normalized * (strokeDelta.magnitude + brushRadius * 0.025f) *
                                  falloff * (pointIndex / (guide.points.Count - 1f))
                                : Vector3.zero;
                            break;
                        case HairSceneTool.Grab:
                            displacement = strokeDelta * falloff;
                            break;
                        case HairSceneTool.Smooth:
                        {
                            Vector3 neighborAverage = pointIndex < guide.points.Count - 1
                                ? (guide.points[pointIndex - 1].position + guide.points[pointIndex + 1].position) * 0.5f
                                : guide.points[pointIndex - 1].position +
                                  (guide.points[pointIndex].position - guide.points[pointIndex - 1].position).normalized *
                                  Vector3.Distance(guide.points[pointIndex].position, guide.points[pointIndex - 1].position);
                            displacement = (neighborAverage - actual) * falloff;
                            break;
                        }
                        case HairSceneTool.Clump:
                            displacement = (average - actual) * falloff;
                            break;
                        case HairSceneTool.Part:
                            displacement = Vector3.ProjectOnPlane(actual - center, surfaceNormal).normalized *
                                           (brushRadius * 0.08f * falloff);
                            break;
                        case HairSceneTool.Width:
                            if (delta != null) delta.widthOffsets[pointIndex] +=
                                (paintErase ? -1f : 1f) * point.width * 0.15f * falloff;
                            else point.width = Mathf.Max(0f, point.width * (1f + (paintErase ? -0.15f : 0.15f) * falloff));
                            continue;
                        case HairSceneTool.Freeze:
                            point.freeze = Mathf.Clamp01(point.freeze + (paintErase ? -1f : 1f) * falloff);
                            continue;
                        case HairSceneTool.Length:
                            ScaleGuideLength(guide, delta, 1f + (paintErase ? -0.04f : 0.04f) * falloff);
                            pointIndex = guide.points.Count;
                            continue;
                        case HairSceneTool.Cut:
                            if (pointIndex >= 2 && pointIndex < guide.points.Count - 1)
                            {
                                guide.points.RemoveRange(pointIndex + 1, guide.points.Count - pointIndex - 1);
                                ResizeDelta(delta, guide.points.Count);
                            }
                            return;
                    }
                    if (delta != null) delta.positionOffsets[pointIndex] += displacement;
                    else point.position += displacement;
                }
            }
        }

        private HairSculptLayer ResolveSculptLayer(HairGroup group)
        {
            HairSculptLayer layer = group.sculptLayers.Find(candidate => candidate != null && candidate.Id == activeLayerId);
            if (layer != null && !layer.locked && layer.visible) return layer;
            layer = new HairSculptLayer { name = $"Sculpt Layer {group.sculptLayers.Count + 1}" };
            layer.EnsureIntegrity();
            group.sculptLayers.Add(layer);
            activeLayerId = layer.Id;
            actionStatus = $"Created {layer.name} as the visible destination for grooming strokes.";
            return layer;
        }

        private static HairGuideDelta ResolveDelta(HairSculptLayer layer, HairGuide guide)
        {
            HairGuideDelta delta = layer.deltas.Find(candidate => candidate != null && candidate.guideId == guide.Id);
            if (delta == null)
            {
                delta = new HairGuideDelta { guideId = guide.Id };
                layer.deltas.Add(delta);
            }
            ResizeDelta(delta, guide.points.Count);
            return delta;
        }

        private static void ResizeDelta(HairGuideDelta delta, int count)
        {
            if (delta == null) return;
            Array.Resize(ref delta.positionOffsets, count);
            Array.Resize(ref delta.widthOffsets, count);
            Array.Resize(ref delta.rollOffsets, count);
        }

        private static void ScaleGuideLength(HairGuide guide, HairGuideDelta delta, float scale)
        {
            if (guide?.points == null || guide.points.Count < 2) return;
            Vector3 root = guide.points[0].position +
                           (delta != null && delta.positionOffsets.Length > 0 ? delta.positionOffsets[0] : Vector3.zero);
            for (int i = 1; i < guide.points.Count; i++)
            {
                Vector3 basePosition = guide.points[i].position;
                Vector3 actual = basePosition +
                                 (delta != null && i < delta.positionOffsets.Length
                                     ? delta.positionOffsets[i]
                                     : Vector3.zero);
                Vector3 target = root + (actual - root) * Mathf.Max(0.01f, scale);
                if (delta != null) delta.positionOffsets[i] = target - basePosition;
                else guide.points[i].position = target;
            }
        }

        private static Vector3 CalculateNearbyAverage(HairGroup group, Vector3 center, float squareRadius)
        {
            Vector3 total = Vector3.zero;
            int count = 0;
            for (int guideIndex = 0; guideIndex < group.guides.Count; guideIndex++)
            {
                HairGuide guide = group.guides[guideIndex];
                if (guide?.points == null) continue;
                for (int pointIndex = 1; pointIndex < guide.points.Count; pointIndex++)
                {
                    Vector3 position = guide.points[pointIndex].position;
                    if ((position - center).sqrMagnitude > squareRadius) continue;
                    total += position;
                    count++;
                }
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
            if (hairFilter != null) hairFilter.sharedMesh = null;
            meshBuild?.Dispose();
            meshBuild = null;
            evaluation = null;
            displayGuideCurves.Clear();
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
