using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UMA;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UMA_BURSTCOMPILE
using Unity.Burst;
#endif

namespace UMA
{
    public class VertexEditorStage : PreviewSceneStage
    {
        private static bool eventsHooked;

        private static void EnsureEditorEvents()
        {
#if UNITY_EDITOR
            if (eventsHooked)
            {
                return;
            }
            eventsHooked = true;
            AssemblyReloadEvents.beforeAssemblyReload += ExitStageIfActive;
            CompilationPipeline.compilationStarted += _ => ExitStageIfActive();
            EditorApplication.playModeStateChanged += _ => ExitStageIfActive();
#endif
        }

        private static void ExitStageIfActive()
        {
#if UNITY_EDITOR
            try
            {
                if (StageUtility.GetCurrentStage() is VertexEditorStage)
                {
                    StageUtility.GoBackToPreviousStage();
                }
            }
            catch
            {
            }
#endif
        }
        public PreviewWindow ownerWindow;
        public GUIContent titleContent;
        public SceneView openedSceneView;
        public GameObject selectedObject;
        public GameObject VertexObject;
        public GameObject cameraAnchor;
        GameObject lightingObject = null;
        public bool NeedsCameraSetup = false;
        public bool closing = false;
        public bool hasSaved = false;
        public DynamicCharacterAvatar thisDCA;
        public Mesh BakedMesh;
        [SerializeField]
        private List<VertexSelection> SelectedVertexes = new List<VertexSelection>();
        private readonly HashSet<VertexSelectionKey> savedSelectionSnapshot = new HashSet<VertexSelectionKey>();
        PhysicsScene phyScene;

        private Vector3[] bakedVertices;
        private Vector3[] bakedNormals;
        private int[] bakedTriangles;

        private void RefreshBakedMeshCaches()
        {
            if (BakedMesh == null)
            {
                bakedVertices = null;
                bakedNormals = null;
                bakedTriangles = null;
                return;
            }

            bakedVertices = BakedMesh.vertices;
            bakedNormals = BakedMesh.normals;
            bakedTriangles = BakedMesh.triangles;
        }

        // Edit Options
        float HandlesSize = 0.003f;
        float weightSmoothAmount = 0.5f;
        public Color ActiveColor = new Color32(0, 210, 0, 255);
        public Color InactiveColor = new Color32(235, 0, 0, 255);
        bool selectObscured = false;
        bool selectFacingAway = false;
        private GUIStyle centeredLabel;
        [SerializeField]
        private int currentSelected = -1;
        public int CurrentSelected
        {
            get { return currentSelected; }
            set
            {
                if (editorMode == MeshModifierEditor.EditorMode.VertexAdjustments)
                {
                    currentSelected = value;
                    VertexSelection vs = GetSelectedVertex();
                    RepaintLinkedEditors();
                }
            }
       }

        private void RepaintLinkedEditors()
        {
            if (modifierEditor != null)
            {
                modifierEditor.Repaint();
            }
            if (slotWeightEditorWindow != null)
            {
                slotWeightEditorWindow.Repaint();
            }
            if (touchupWeightsWindow != null)
            {
                touchupWeightsWindow.Repaint();
            }
        }

        private void RefreshVisibleSlotListsIfNeeded()
        {
            // Keep dropdowns in sync with current slot suppression changes while the stage is running.
            RefreshVisibleSlotLists();
        }
        float blinkSpeed = 0.2f;

        enum selectMode { Add, Remove, InvertSelection, Activate, Deactivate, ToggleState };

        enum DefineMode { DefineVertexSet, DefineVertexState };

        enum SelectionBrushShape { Point, Circle };

        private enum SceneToolMode { Select, SelectionBrush, Sculpt, VertexPaint }
        private enum TouchupWeightTool { Select, Paint }
        private enum TouchupWeightPaintMode { Replace, Add, Remove, Smooth, Smear }

        [Serializable]
        private struct TouchupUndoBoneWeight
        {
            public int boneIndex;
            public float weight;
        }

        [Serializable]
        private class TouchupUndoVertexWeights
        {
            public int vertexIndex;
            public List<TouchupUndoBoneWeight> weights = new List<TouchupUndoBoneWeight>();
        }
        private enum SculptTool { Add, Remove, Smooth, Grab, Crease, Pinch, Plane, Boundary, ElasticDeform }
        private enum SculptPlaneMode { Flatten, Fill, Scrape }
        private enum SculptBoundaryMode { Grab, Bend, Expand, Inflate, Twist, Smooth }
        private enum SculptElasticMode { Grab, Scale, Twist }
        private enum SculptFalloff { Constant, Linear, Smooth, EaseIn, EaseOut, EaseInOut, Sharp, UserDefined }
        private enum SculptMaskTool { None, Paint, Erase }
        private enum AutosculptAxis { X, Y, Z }
        private enum VertexPaintTool { Paint, Erase, Smear, Blur, Burn, Dodge, Noise, Clear, Smooth }
        private enum VertexPaintBrushShape { Circle, Square, Bitmap }

        private sealed class SculptSlotEditState
        {
            public SlotData slot;
            public int start;
            public int count;
            public Vector3[] originalVertices;
            public Vector3[] originalNormals;
            public bool hasOriginalNormals;
            public float[] mask;
        }

        private struct SculptSeamVertex
        {
            public SlotData slot;
            public int localIndex;
            public int bakedIndex;
        }

        private struct AutosculptTriangle
        {
            public Vector3 a;
            public Vector3 b;
            public Vector3 c;
        }

        private sealed class AutosculptOccluderMap
        {
            public AutosculptAxis axis;
            public Vector2 origin;
            public float cellSize;
            public readonly Dictionary<Vector2Int, List<AutosculptTriangle>> cells =
                new Dictionary<Vector2Int, List<AutosculptTriangle>>();
            public readonly List<AutosculptTriangle> largeTriangles = new List<AutosculptTriangle>();
        }

        [SerializeField] private SceneToolMode sceneToolMode = SceneToolMode.Select;
        [SerializeField] private SculptTool sculptTool = SculptTool.Add;
        [SerializeField] private SculptFalloff sculptFalloff = SculptFalloff.Smooth;
        [SerializeField] private SculptMaskTool sculptMaskTool = SculptMaskTool.None;
        [SerializeField] private float sculptRadius = 0.05f;
        [SerializeField] private float sculptStrengthPercent = 25f;
        [SerializeField] private float sculptCreaseDepth = -0.35f;
        [SerializeField] private float sculptPinchStrength = 0.65f;
        [SerializeField] private SculptPlaneMode sculptPlaneMode = SculptPlaneMode.Flatten;
        [SerializeField] private SculptBoundaryMode sculptBoundaryMode = SculptBoundaryMode.Grab;
        [SerializeField] private SculptElasticMode sculptElasticMode = SculptElasticMode.Grab;
        [SerializeField] private float sculptElasticVolumePreservation = 0.65f;
        [SerializeField] private bool sculptSymmetryX = false;
        [SerializeField] private bool sculptConnectedOnly = false;
        [SerializeField] private bool sculptUpdateNormalsWhileSculpting = false;
        [SerializeField] private AnimationCurve sculptCustomFalloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        [SerializeField] private int sculptSlotIndex = 0;
        [SerializeField] private bool sculptDefaultSlotChosen = false;
        [SerializeField] private string sculptModifierName = string.Empty;
        [SerializeField] private string sculptBlendshapeName = string.Empty;
        [SerializeField] private string sculptBlendshapeStatusMessage = string.Empty;
        [SerializeField] private MessageType sculptBlendshapeStatusType = MessageType.Info;
        [SerializeField] private string sculptNewSlotName = string.Empty;
        private readonly List<SlotData> sculptSlots = new List<SlotData>();
        private readonly List<string> sculptSlotNames = new List<string>();
        private readonly Dictionary<SlotData, SculptSlotEditState> sculptSlotStates = new Dictionary<SlotData, SculptSlotEditState>();
        private readonly Dictionary<int, List<SculptSeamVertex>> sculptCrossSlotSeams = new Dictionary<int, List<SculptSeamVertex>>();
        private readonly List<List<SculptSeamVertex>> sculptCrossSlotSeamGroups = new List<List<SculptSeamVertex>>();
        private bool sculptCrossSlotSeamsBuilt;
        private SculptSlotEditState activeSculptState;
        private SlotData sculptSlot;
        private int sculptSlotStart = -1;
        private int sculptSlotVertexCount;
        private Vector3[] sculptOriginalVertices;
        private Vector3[] sculptOriginalNormals;
        private List<int>[] sculptNeighbors;
        private HashSet<int> sculptBoundaryVertices;
        private List<int>[] sculptCoincidentVertices;
        private int[] sculptConnectedComponents;
        private int sculptHoverConnectedComponent = -1;
        [SerializeField] private float[] sculptMask;
        private float[] sculptStrokeApplied;
        private float[] sculptStrokeLimit;
        private bool sculpting;
        private bool sculptHoverValid;
        private Vector3 sculptHoverPoint;
        private Vector3 sculptHoverNormal = Vector3.up;
        private Vector3 sculptHoverTangent = Vector3.right;
        private Vector3 sculptLastSamplePoint;
        private bool sculptHasLastSample;
        private int sculptUndoGroup = -1;
        private bool sculptGrabInitialized;
        private Plane sculptGrabPlane;
        private Vector3 sculptGrabStartPlanePoint;
        private Vector3 sculptGrabStartBrushPoint;
        private Vector3[] sculptGrabStartVertices;
        private float[] sculptGrabWeights;
        private float[] sculptGrabMirroredWeights;
        private Vector2 sculptGrabStartGuiPoint;
        private Vector3 sculptGrabStartLocalPoint;
        private Vector3 sculptGrabStartLocalNormal;
        private Vector3 sculptGrabStartLocalTangent;
        private Vector3 sculptStrokePlanePoint;
        private Vector3 sculptStrokePlaneNormal;
        [SerializeField] private bool showAutosculpt = false;
        [SerializeField] private string autosculptOccluderSlotName = string.Empty;
        [SerializeField] private AutosculptAxis autosculptAxis = AutosculptAxis.Y;
        [SerializeField] private bool autosculptClothify = false;
        [SerializeField] private float autosculptClothEffect = 0.25f;
        [SerializeField] private string autosculptStatusMessage = string.Empty;
        [SerializeField] private MessageType autosculptStatusType = MessageType.Info;

        [SerializeField] private VertexPaintTool vertexPaintTool = VertexPaintTool.Paint;
        [SerializeField] private VertexPaintBrushShape vertexPaintBrushShape = VertexPaintBrushShape.Circle;
        [SerializeField] private Color vertexPaintBrushColor = new Color(0.8f, 0.25f, 0.2f, 1f);
        [SerializeField] private float vertexPaintRadius = 0.05f;
        [SerializeField] private float vertexPaintStrengthPercent = 35f;
        [SerializeField] private SculptFalloff vertexPaintFalloff = SculptFalloff.Smooth;
        [SerializeField] private AnimationCurve vertexPaintCustomFalloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        [SerializeField] private Texture2D vertexPaintBrushTexture;
        [SerializeField] private float vertexPaintSmoothRadius = 0.03f;
        [SerializeField] private float vertexPaintGeneralSmoothPercent = 50f;
        [SerializeField] private float vertexPaintGeneralSmoothRadius = 0.03f;
        [SerializeField] private float vertexPaintGeneralSmoothCutoffPercent = 0.05f;
        [SerializeField] private float vertexPaintNoiseScale = 12f;
        [SerializeField] private float vertexPaintNoiseAmount = 0.15f;
        [SerializeField] private int vertexPaintNoiseSeed;
        [SerializeField] private bool vertexPaintAffectAlpha = true;
        [SerializeField] private bool vertexPaintSymmetryX = false;
        [SerializeField] private int vertexPaintSlotIndex;
        [SerializeField] private bool vertexPaintDefaultSlotChosen;
        [SerializeField] private string vertexPaintModifierName = string.Empty;
        private readonly List<SlotData> vertexPaintSlots = new List<SlotData>();
        private readonly List<string> vertexPaintSlotNames = new List<string>();
        private SlotData vertexPaintSlot;
        private int vertexPaintSlotStart = -1;
        private int vertexPaintSlotVertexCount;
        [SerializeField] private Color32[] vertexPaintBaseColors;
        [SerializeField] private Color32[] vertexPaintColors;
        private List<int>[] vertexPaintNeighbors;
        private List<int>[] vertexPaintCoincidentVertices;
        private Dictionary<Vector3Int, List<int>> vertexPaintWorldSmoothingCells;
        private Vector3[] vertexPaintWorldSmoothingPositions;
        private Vector3[] vertexPaintWorldSmoothingNormals;
        private float vertexPaintWorldSmoothingCellSize;
        private float[] vertexPaintStrokeApplied;
        private bool vertexPaintPreviewInitialized;
        private bool vertexPaintDirty;
        private readonly HashSet<string> vertexPaintDirtySlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool vertexPainting;
        private bool vertexPaintHoverValid;
        private Vector3 vertexPaintHoverPoint;
        private Vector3 vertexPaintHoverNormal = Vector3.up;
        private Vector3 vertexPaintHoverTangent = Vector3.right;
        private Vector3 vertexPaintLastSamplePoint;
        private bool vertexPaintHasLastSample;
        private bool vertexPaintStrokeChanged;
        private int vertexPaintUndoGroup = -1;
        private Texture2D vertexPaintReadableBrush;
        private Texture2D vertexPaintCachedBrushSource;
        private Material vertexColorPreviewMaterial;

        private const int SculptSlotSpecialOptionCount = 2;
        private bool IsSculptAllSlotsMode => sculptSlotIndex == 0;
        private bool IsSculptAllNonBaseSlotsMode => sculptSlotIndex == 1;
        private bool IsSculptMultiSlotMode => IsSculptAllSlotsMode || IsSculptAllNonBaseSlotsMode;
        private bool IsVertexPaintAllSlotsMode => vertexPaintSlotIndex == 0;

        string[] selectFrom = new string[] { "All Slots" };
        int selectionSlot = 0; // 0 is all slots
        string[] visibleSelectFrom = new string[] { "All Slots" };

        private enum RaycastSelectDirection
        {
            Outward,
            Inward
        }

        private enum RaycastHitFaceFilter
        {
            TowardVertex,
            AwayFromVertex,
            All
        }

        [SerializeField]
        private bool showRaycastSelection = false;
        [SerializeField]
        private int raycastSelectionSlot = 0;
        [SerializeField]
        private RaycastSelectDirection raycastDirection = RaycastSelectDirection.Outward;
        [SerializeField]
        private bool raycastAddToSelection = false;
        [SerializeField]
        private float raycastLength = 0.25f;
        [SerializeField]
        private RaycastHitFaceFilter raycastHitFaceFilter = RaycastHitFaceFilter.TowardVertex;
        [SerializeField]
        private string raycastStatusMessage;
        [SerializeField]
        private MessageType raycastStatusType = MessageType.Info;
        [SerializeField]
        private bool raycastDrawDebugRays = false;
        [SerializeField]
        private int raycastDebugRayLimit = 64;
        private readonly List<DebugRay> raycastDebugRays = new List<DebugRay>(64);

        private struct DebugRay
        {
            public Vector3 origin;
            public Vector3 direction;
            public float length;
            public bool hitOtherSlot;
           public float time;
        }

        [SerializeField]
        private float raycastDebugRayLifetime = 25f;

        selectMode currentMode = selectMode.Add;
        DefineMode currentDefineMode = DefineMode.DefineVertexSet;
        private bool replaceSelectionOnRectSelect = false;
        private bool selectionBrushModeSet = false;
        private bool selectionBrushModeState = false;
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("paintBrushMode")]
        private SelectionBrushShape selectionBrushShape = SelectionBrushShape.Point;
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("paintBrushRadiusPixels")]
        private int selectionBrushRadiusPixels = 24;
        private readonly HashSet<string> selectionBrushedVerticesThisStroke = new HashSet<string>();
        // End Options

        private const int MinSelectionBrushRadiusPixels = 1;
        private const int MaxSelectionBrushRadiusPixels = 256;

        const int VertexEditorToolsWindowID = 0x1234;
        const int VisibleWearablesID = 0x1235;
        private const float LeftPanelWidthMin = 320f;
        private const float LeftPanelWidthMax = 460f;
        private const float LeftPanelPadding = 6f;
        private const float LeftPanelHeaderHeight = 18f;

        public Rect VertexEditorToolsWindow = new Rect(10, 10, 300, 300);


        public Vector2 VisibleWearablesLocation = Vector2.zero;
        public Rect VisibleWearablesWindow = new Rect(10, 310, 250, 300);
        private Rect leftPanelRect;
        private Vector2 lastSceneViewSize = Vector2.zero;
        private float cachedVisibilityHeight = -1f;

        private MeshModifierEditor modifierEditor;
        private UmaSlotWeightEditorWindow slotWeightEditorWindow;
        private UmaTouchupWeightsWindow touchupWeightsWindow;
        private bool slotWeightEditorMode;
        private bool slotWeightEditorReadOnly;
        private bool touchupWeightsMode;
        private bool ownsSlotWeightPreviewAvatar;
        private SlotDataAsset slotWeightEditorSlotAsset;
        private RaceData slotWeightEditorRace;
        private SlotData touchupWeightSlot;
        private int touchupWeightBoneHash;
        private List<VertexWeightEntry> touchupPreviewWeights;
        private readonly HashSet<int> touchupPreviewVertexIndices = new HashSet<int>();
        private Color32[] touchupBaseColors;
        private Color32[] touchupDisplayColors;
        private List<BoneWeight1>[] touchupAssetWeights;
        private List<BoneWeight1>[] touchupWorkingWeights;
        private int[][] touchupConnectedVertices;
        private TouchupWeightTool touchupWeightTool;
        private TouchupWeightPaintMode touchupWeightPaintMode;
        private float touchupPaintAmount = 0.1f;
        [SerializeField] private int touchupSelectionSmoothPercentage = 50;
        private bool touchupPaintSelectedVerticesOnly;
        private bool touchupSmoothSelectedBoneOnly;
        private bool touchupAutoMaskConnectedVertices = true;
        [SerializeField] private bool touchupLiveUpdate;
        private HashSet<int> touchupCrossSlotMaskedVertices;
        private bool touchupPaintStrokeActive;
        private bool touchupPaintStrokeBlocked;
        private bool touchupSmearHasPreviousSample;
        private Vector2 touchupSmearPreviousMousePosition;
        private readonly List<int> touchupSmearPreviousCandidates = new List<int>();
        private readonly Dictionary<int, List<BoneWeight1>> touchupPaintStrokeWeights =
            new Dictionary<int, List<BoneWeight1>>();
        private readonly Dictionary<int, List<BoneWeight1>> touchupPendingPaintWeights =
            new Dictionary<int, List<BoneWeight1>>();
        [SerializeField]
        private List<TouchupUndoVertexWeights> touchupPendingPaintUndoState =
            new List<TouchupUndoVertexWeights>();
        private readonly HashSet<int> touchupSavedPositionVertexIndices = new HashSet<int>();
        private readonly HashSet<int> touchupLivePositionVertexIndices = new HashSet<int>();
        private readonly Dictionary<int, Vector3> touchupSkinningPositionOffsets =
            new Dictionary<int, Vector3>();
        private Vector3[] touchupSkinningSourceVertices;
        private int touchupSkinningSourceMeshId;
        private string touchupPaintStatusMessage = string.Empty;
        private MessageType touchupPaintStatusType = MessageType.Info;
        private int touchupWeightsRevision;
        private readonly Matrix4x4[] touchupHandleBatch = new Matrix4x4[1023];
        private SkinnedMeshRenderer stageSkinnedMeshRenderer;
        private bool stageSkinnedMeshRendererWasEnabled;
        [SerializeField] private bool showOriginalMaterials = false;
        [SerializeField] private bool showVertexWireframe = true;
        private Material[] originalVertexMaterials;
        private Material[] pastelVertexMaterials;
        public bool rectSelect = false;
        public bool selectionBrushing = false;
        private bool pendingStateClickAction = false;
        private Vector2 pendingStateClickStart = Vector2.zero;
        private bool touchupEmptyClickCandidate;
        private bool touchupReplaceSelectionOnNextCandidates;
        private Vector2 touchupEmptyClickStart;
        public Vector2 RectStart = Vector2.zero;
        public MeshModifier Currentmodifier;
        public Type[] ModifierTypes;



     [SerializeReference]
        private List<VertexAdjustment> _adjustments = new List<VertexAdjustment>();

        private bool selectionUndoArmed = false;

        private bool IsSelectionBrushModeEnabled
        {
            get { return sceneToolMode == SceneToolMode.SelectionBrush; }
        }

        private bool IsVertexPaintModeEnabled
        {
            get { return sceneToolMode == SceneToolMode.VertexPaint; }
        }

        internal bool IsSculptAuthoringMode => sceneToolMode == SceneToolMode.Sculpt;
        internal bool IsVertexPaintAuthoringMode => sceneToolMode == SceneToolMode.VertexPaint;

        internal void ActivateSelectionAuthoringMode()
        {
            ChangeSceneToolMode(SceneToolMode.Select);
        }

        internal void ActivateSculptAuthoringMode()
        {
            ChangeSceneToolMode(SceneToolMode.Sculpt);
        }

        internal void ActivateVertexPaintAuthoringMode()
        {
            ChangeSceneToolMode(SceneToolMode.VertexPaint);
        }

        private static bool PassFaceFilter(RaycastHit hit, Vector3 rayDirection, RaycastHitFaceFilter filter)
        {
            if (filter == RaycastHitFaceFilter.All)
            {
                return true;
            }

            float dot = Vector3.Dot(hit.normal, rayDirection);
            // Toward: surface normal opposes the ray direction.
            if (filter == RaycastHitFaceFilter.TowardVertex)
            {
                return dot <= 0f;
            }

            // Away: surface normal points (roughly) along the ray direction.
            return dot >= 0f;
        }

        private SlotData GetSlotForTriangle(int triangleIndex)
        {
            if (triangleIndex < 0 || BakedMesh == null)
            {
                return null;
            }

            int triBase = triangleIndex * 3;
            var tris = BakedMesh.triangles;
            if (triBase + 2 >= tris.Length)
            {
                return null;
            }

            SlotData slot0;
            SlotData slot1;
            SlotData slot2;
            int unused;
            if (!TryGetSlotForBakedVertex(tris[triBase], out slot0, out unused))
            {
                slot0 = null;
            }
            if (!TryGetSlotForBakedVertex(tris[triBase + 1], out slot1, out unused))
            {
                slot1 = null;
            }
            if (!TryGetSlotForBakedVertex(tris[triBase + 2], out slot2, out unused))
            {
                slot2 = null;
            }

            if (slot0 == null && slot1 == null && slot2 == null)
            {
                return null;
            }

            // Majority vote: this is resilient against seam triangles where one vertex maps oddly.
            if (slot0 != null && slot1 != null && slot0.slotName == slot1.slotName)
            {
                return slot0;
            }
            if (slot0 != null && slot2 != null && slot0.slotName == slot2.slotName)
            {
                return slot0;
            }
            if (slot1 != null && slot2 != null && slot1.slotName == slot2.slotName)
            {
                return slot1;
            }

            // No majority: fall back to the first non-null.
            if (slot0 != null)
            {
                return slot0;
            }
            if (slot1 != null)
            {
                return slot1;
            }
            return slot2;
        }

        private bool TryGetNearestHitDifferentSlot(Ray ray, float maxDistance, SlotData sourceSlot, out RaycastHit bestHit, ref int hitsSameSlot, ref int hitsOtherSlot)
        {
            bestHit = default;
            if (!phyScene.IsValid())
            {
                return false;
            }

            var tempHits = new RaycastHit[32];
            int hitCount = phyScene.Raycast(ray.origin, ray.direction, tempHits, maxDistance);
            if (hitCount <= 0)
            {
                return false;
            }

         Array.Sort(tempHits, 0, hitCount, RaycastHitDistanceComparer.Instance);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = tempHits[i];
                if (hit.collider == null)
                {
                    continue;
                }
                if (hit.collider.gameObject != VertexObject)
                {
                    continue;
                }

                if (!PassFaceFilter(hit, ray.direction, raycastHitFaceFilter))
                {
                    continue;
                }

                SlotData hitSlot = GetSlotForTriangle(hit.triangleIndex);
                if (hitSlot == null)
                {
                    continue;
                }

                if (hitSlot.slotName == sourceSlot.slotName)
                {
                    hitsSameSlot++;
                    continue;
                }

                hitsOtherSlot++;
                bestHit = hit;
                return true;
            }

            return false;
        }

        private bool TryGetNearestHitSameSlot(Ray ray, float maxDistance, SlotData sourceSlot, out RaycastHit bestHit)
        {
            bestHit = default;
            if (!phyScene.IsValid())
            {
                return false;
            }

            var tempHits = new RaycastHit[32];
            int hitCount = phyScene.Raycast(ray.origin, ray.direction, tempHits, maxDistance);
            if (hitCount <= 0)
            {
                return false;
            }

            Array.Sort(tempHits, 0, hitCount, RaycastHitDistanceComparer.Instance);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = tempHits[i];
                if (hit.collider == null)
                {
                    continue;
                }
                if (!PassFaceFilter(hit, ray.direction, raycastHitFaceFilter))
                {
                    continue;
                }
                SlotData hitSlot = GetSlotForTriangle(hit.triangleIndex);
                if (hitSlot == null)
                {
                    continue;
                }
                if (hitSlot.slotName != sourceSlot.slotName)
                {
                    continue;
                }
                bestHit = hit;
                return true;
            }

            return false;
        }

        private sealed class RaycastHitDistanceComparer : IComparer<RaycastHit>
        {
            public static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();
            public int Compare(RaycastHit x, RaycastHit y)
            {
                return x.distance.CompareTo(y.distance);
            }
        }

        private void DrawRaycastDebugRays()
        {
            if (!raycastDrawDebugRays || raycastDebugRays.Count == 0)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            for (int i = raycastDebugRays.Count - 1; i >= 0; i--)
            {
                if (raycastDebugRayLifetime > 0f && (now - raycastDebugRays[i].time) > raycastDebugRayLifetime)
                {
                    raycastDebugRays.RemoveAt(i);
                }
            }

            Gizmos.matrix = Matrix4x4.identity;
            for (int i = 0; i < raycastDebugRays.Count; i++)
            {
                DebugRay dr = raycastDebugRays[i];
                float len;
                if (float.IsPositiveInfinity(dr.length))
                {
                    len = 0.25f;
                }
                else
                {
                    len = dr.length;
                }
                Vector3 end = dr.origin + (dr.direction.normalized * len);
                Gizmos.color = dr.hitOtherSlot ? Color.green : Color.red;
                Gizmos.DrawLine(dr.origin, end);
            }
        }

        private void DrawRaycastDebugRaysHandles()
        {
            if (!raycastDrawDebugRays || raycastDebugRays.Count == 0)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            for (int i = raycastDebugRays.Count - 1; i >= 0; i--)
            {
                if (raycastDebugRayLifetime > 0f && (now - raycastDebugRays[i].time) > raycastDebugRayLifetime)
                {
                    raycastDebugRays.RemoveAt(i);
                }
            }

            if (raycastDebugRays.Count == 0)
            {
                return;
            }

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            float capSize = Mathf.Max(0.002f, HandlesSize * 0.5f);
            for (int i = 0; i < raycastDebugRays.Count; i++)
            {
                DebugRay dr = raycastDebugRays[i];
                float len = float.IsPositiveInfinity(dr.length) ? 0.25f : dr.length;
                Vector3 dir = dr.direction.sqrMagnitude > 1e-8f ? dr.direction.normalized : Vector3.up;
                Vector3 end = dr.origin + (dir * len);
                Color c = dr.hitOtherSlot ? new Color(0.1f, 1f, 0.1f, 1f) : new Color(1f, 0.15f, 0.15f, 1f);
                using (new Handles.DrawingScope(c))
                {
                    Handles.DrawAAPolyLine(3f, dr.origin, end);
                    Handles.SphereHandleCap(0, dr.origin, Quaternion.identity, capSize, EventType.Repaint);
                    Handles.SphereHandleCap(0, end, Quaternion.identity, capSize, EventType.Repaint);
                }
            }
        }

        private void OnDrawGizmos()
        {
            DrawRaycastDebugRays();
        }

        private void SelectByRaycast()
        {
            raycastDebugRays.Clear();

            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null)
            {
                raycastStatusType = MessageType.Warning;
                raycastStatusMessage = "Raycast skipped: character data is not available.";
                return;
            }

            if (VertexObject == null || BakedMesh == null || BakedMesh.vertexCount == 0)
            {
                raycastStatusType = MessageType.Warning;
                raycastStatusMessage = "Raycast skipped: baked mesh is not available.";
                return;
            }

            if (raycastSelectionSlot <= 0 || raycastSelectionSlot >= visibleSelectFrom.Length)
            {
                raycastStatusType = MessageType.Info;
                raycastStatusMessage = "Select a visible slot to raycast from.";
                return;
            }

            SlotData sourceSlot = thisDCA.umaData.umaRecipe.GetSlot(visibleSelectFrom[raycastSelectionSlot]);
            if (sourceSlot == null || !IsSelectableSlot(sourceSlot))
            {
                raycastStatusType = MessageType.Warning;
                raycastStatusMessage = "Raycast skipped: source slot is not selectable.";
                return;
            }

            int sourceVertexCount = sourceSlot.asset != null && !UMAMeshData.IsNullOrEmptyMeshData(sourceSlot.asset.meshData)
                ? sourceSlot.asset.meshData.vertexCount
                : 0;

            if (sourceVertexCount <= 0)
            {
                raycastStatusType = MessageType.Warning;
                raycastStatusMessage = "Raycast skipped: source slot has no vertices.";
                return;
            }

            // Use pure CPU ray-triangle intersection - no Unity physics, 100% synchronous
            SelectByRaycastCPU(sourceSlot, sourceVertexCount);
        }

        /// <summary>
        /// Pure CPU ray-triangle intersection. No Unity physics, completely synchronous.
        /// Builds triangle list from all slots EXCEPT sourceSlot, then tests rays against it.
        /// </summary>
        private void SelectByRaycastCPU(SlotData sourceSlot, int sourceVertexCount)
        {
            RefreshBakedMeshCaches();
            var verts = bakedVertices;
            var normals = bakedNormals;
            var tris = bakedTriangles;
            if (verts == null || normals == null || tris == null)
            {
                raycastStatusType = MessageType.Warning;
                raycastStatusMessage = "Raycast skipped: baked mesh data is not available.";
                return;
            }
            float maxDistance = raycastLength > 0f ? raycastLength : float.MaxValue;

            // Step 1: Build list of triangles that do NOT belong to sourceSlot
            var otherTriangles = new List<(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 normal)>();
            int triangleCount = tris.Length / 3;

            for (int t = 0; t < triangleCount; t++)
            {
                int i0 = tris[t * 3];
                int i1 = tris[t * 3 + 1];
                int i2 = tris[t * 3 + 2];

                // Check if this triangle belongs to the source slot
                if (TryGetSlotForBakedVertex(i0, out SlotData slot0, out _) && slot0.slotName == sourceSlot.slotName)
                {
                    continue; // Skip triangles from source slot
                }

                Vector3 v0 = VertexObject.transform.TransformPoint(verts[i0]);
                Vector3 v1 = VertexObject.transform.TransformPoint(verts[i1]);
                Vector3 v2 = VertexObject.transform.TransformPoint(verts[i2]);

                // Calculate face normal
                Vector3 edge1 = v1 - v0;
                Vector3 edge2 = v2 - v0;
                Vector3 faceNormal = Vector3.Cross(edge1, edge2).normalized;

                otherTriangles.Add((v0, v1, v2, faceNormal));
            }

            if (otherTriangles.Count == 0)
            {
                raycastStatusType = MessageType.Warning;
                raycastStatusMessage = "Raycast skipped: no other geometry to test against.";
                return;
            }

            // Step 2: Gather ray origins and directions from source slot vertices
            var rayData = new List<(int slotVertexIndex, Vector3 origin, Vector3 direction)>();
            for (int slotVertexIndex = 0; slotVertexIndex < sourceVertexCount; slotVertexIndex++)
            {
                if (!TryGetVisibleBakedVertexIndex(sourceSlot, slotVertexIndex, out int bakedIndex))
                {
                    continue;
                }
                if (bakedIndex < 0 || bakedIndex >= verts.Length)
                {
                    continue;
                }

                Vector3 originWorld = VertexObject.transform.TransformPoint(verts[bakedIndex]);
                Vector3 dirWorld = Vector3.up;
                if (bakedIndex < normals.Length)
                {
                    Vector3 nWorld = VertexObject.transform.TransformDirection(normals[bakedIndex]).normalized;
                    dirWorld = raycastDirection == RaycastSelectDirection.Inward ? -nWorld : nWorld;
                }
                if (dirWorld.sqrMagnitude < 1e-8f)
                {
                    continue;
                }

                // Offset slightly to avoid self-intersection
                Vector3 origin = originWorld + (dirWorld * 0.0005f);
                rayData.Add((slotVertexIndex, origin, dirWorld));
            }

            if (rayData.Count == 0)
            {
                raycastStatusType = MessageType.Warning;
                raycastStatusMessage = "Raycast skipped: no valid vertices found on source slot.";
                return;
            }

            // Step 3: For each ray, test against all triangles (CPU ray-triangle intersection)
            if (!raycastAddToSelection)
            {
                SelectedVertexes.Clear();
            }

            int added = 0;
            int hits = 0;
            int misses = 0;
            int totalCasts = rayData.Count;

            foreach (var (slotVertexIndex, origin, direction) in rayData)
            {
                bool hitSomething = false;
                float closestT = maxDistance;

                // Test against all "other" triangles
                foreach (var (v0, v1, v2, faceNormal) in otherTriangles)
                {
                    if (RayTriangleIntersect(origin, direction, v0, v1, v2, out float t))
                    {
                        if (t > 0.0001f && t < closestT)
                        {
                            // Apply face filter
                            if (raycastHitFaceFilter == RaycastHitFaceFilter.All)
                            {
                                hitSomething = true;
                                closestT = t;
                            }
                            else
                            {
                                float dot = Vector3.Dot(faceNormal, direction);
                                bool passFaceFilter = raycastHitFaceFilter == RaycastHitFaceFilter.TowardVertex
                                    ? dot <= 0f
                                    : dot >= 0f;
                                if (passFaceFilter)
                                {
                                    hitSomething = true;
                                    closestT = t;
                                }
                            }
                        }
                    }
                }

                if (raycastDrawDebugRays && raycastDebugRays.Count < Mathf.Max(0, raycastDebugRayLimit))
                {
                    raycastDebugRays.Add(new DebugRay()
                    {
                        origin = origin,
                        direction = direction,
                        length = maxDistance,
                        hitOtherSlot = hitSomething,
                        time = Time.realtimeSinceStartup
                    });
                }

                if (hitSomething)
                {
                    hits++;
                    if (GetSelectionIndex(sourceSlot, slotVertexIndex) < 0)
                    {
                        SelectedVertexes.Add(new VertexSelection()
                        {
                            vertexIndexOnSlot = slotVertexIndex,
                            slot = sourceSlot,
                            WorldPosition = origin,
                            isActive = (currentNewVertexState == (int)newVertexState.Active)
                        });
                        added++;
                    }
                }
                else
                {
                    misses++;
                }
            }

            if (added > 0)
            {
                UpdateSelections();
            }

            raycastStatusType = MessageType.Info;
            raycastStatusMessage = $"CPU Raycast complete\nTriangles tested: {otherTriangles.Count}\nRays: {totalCasts}\nHits (occluded): {hits}\nMisses: {misses}\nSelected: {added}";
        }

        /// <summary>
        /// M�ller�Trumbore ray-triangle intersection algorithm.
        /// Returns true if ray intersects triangle, with t = distance along ray.
        /// </summary>
        private static bool RayTriangleIntersect(Vector3 rayOrigin, Vector3 rayDir, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            t = 0f;
            const float EPSILON = 1e-8f;

            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(rayDir, edge2);
            float a = Vector3.Dot(edge1, h);

            if (a > -EPSILON && a < EPSILON)
            {
                return false; // Ray is parallel to triangle
            }

            float f = 1.0f / a;
            Vector3 s = rayOrigin - v0;
            float u = f * Vector3.Dot(s, h);

            if (u < 0.0f || u > 1.0f)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(rayDir, q);

            if (v < 0.0f || u + v > 1.0f)
            {
                return false;
            }

            t = f * Vector3.Dot(edge2, q);
            return t > EPSILON;
        }

        private void BeginSelectionUndoSnapshot(string actionName)
        {
            if (selectionUndoArmed)
            {
                return;
            }
            Undo.RegisterCompleteObjectUndo(this, actionName);
            selectionUndoArmed = true;
        }

        private void EndSelectionUndoSnapshot()
        {
            selectionUndoArmed = false;
        }

        public List<VertexAdjustment> Adjustments
        {
            get
            {
                return _adjustments;
            }
            set
            {
                _adjustments = value;
            }
        }

        private enum vertexState
        {
            unKnown,
            Active,
            Inactive,
            AddingOnly
        }

        private enum newVertexState
        {
            Inactive,
            Active
        }

        int currentNewVertexState = 1;


        GUIStyle HelpBoxStyle;
        private struct VertexSelectionKey : IEquatable<VertexSelectionKey>
        {
            public readonly string slotName;
            public readonly int vertexIndex;
            public readonly bool isActive;

            public VertexSelectionKey(string slotName, int vertexIndex, bool isActive)
            {
                this.slotName = slotName;
                this.vertexIndex = vertexIndex;
                this.isActive = isActive;
            }

            public bool Equals(VertexSelectionKey other)
            {
                return vertexIndex == other.vertexIndex &&
                       isActive == other.isActive &&
                       string.Equals(slotName, other.slotName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is VertexSelectionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = slotName != null ? slotName.GetHashCode() : 0;
                    hash = (hash * 397) ^ vertexIndex;
                    hash = (hash * 397) ^ (isActive ? 1 : 0);
                    return hash;
                }
            }
        }

        [Serializable]
        public class VertexSelection
        {
            public int vertexIndexOnSlot;
            public SlotData slot;
            public Vector3 WorldPosition;
            public bool isActive;
            public bool suppressed;
        }

        [System.Serializable]
        private class SerializedSelection
        {
            public string slotName;
            public int vertexIndex;
            public bool isActive;
        }

        [System.Serializable]
        private class SerializedSelections
        {
            public List<SerializedSelection> selections = new List<SerializedSelection>();
            public static SerializedSelections FromSelections(List<VertexSelection> selections)
            {
                SerializedSelections ss = new SerializedSelections();
                foreach (var selection in selections)
                {
                    ss.selections.Add(new SerializedSelection()
                    {
                        slotName = selection.slot.slotName,
                        vertexIndex = selection.vertexIndexOnSlot,
                        isActive = selection.isActive
                    });
                }
                return ss;
            }

            public List<VertexSelection> ToSelections(DynamicCharacterAvatar DCA, VertexEditorStage stage)
            {
                List<VertexSelection> newSelections = new List<VertexSelection>();
                foreach (var selection in selections)
                {
                    SlotData slot = DCA.umaData.umaRecipe.GetSlot(selection.slotName);
                    if (slot != null)
                    {
                        newSelections.Add(new VertexSelection()
                        {
                            slot = slot,
                            vertexIndexOnSlot = selection.vertexIndex,
                            isActive = selection.isActive,
                            WorldPosition = stage.GetWorldPosition(slot, selection.vertexIndex)
                        });
                    }
                }
                return newSelections;
            }
        }

        private int GetSelectionIndex(SlotData slot, int vertexIndexOnSlot)
        {
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                if (SelectedVertexes[i].slot.slotName == slot.slotName && SelectedVertexes[i].vertexIndexOnSlot == vertexIndexOnSlot)
                {
                    return i;
                }
            }
            return -1;
        }

        public Vector3 GetWorldPosition(SlotData slot, int vertexIndex)
        {
            int bakedIndex = GetVisibleBakedVertexIndex(slot, vertexIndex);
            RefreshBakedMeshCaches();
            if (bakedVertices == null || bakedIndex < 0 || bakedIndex >= bakedVertices.Length)
            {
                return Vector3.zero;
            }
            return VertexObject.transform.TransformPoint(bakedVertices[bakedIndex]);
        }

        private bool IsSelectableSlot(SlotData slot)
        {
            if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData) || slot.Suppressed || slot.asset.isUtilitySlot)
            {
                return false;
            }

            if (slotWeightEditorMode && slotWeightEditorSlotAsset != null)
            {
                return ReferenceEquals(slot.asset, slotWeightEditorSlotAsset) || SlotMatchesAssetSource(slot, slotWeightEditorSlotAsset);
            }

            return true;
        }

        private bool TryGetSlotForBakedVertex(int bakedVertexIndex, out SlotData foundSlot, out int slotVertexIndex)
        {
            foundSlot = null;
            slotVertexIndex = -1;

            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null)
            {
                return false;
            }

            if (touchupWeightsMode)
            {
                var touchupSlots = thisDCA.umaData.umaRecipe.slotDataList;
                for (int i = 0; i < touchupSlots.Length; i++)
                {
                    SlotData slot = touchupSlots[i];
                    if (!IsSelectableSlot(slot) || !IsSlotOnStageRenderer(slot))
                    {
                        continue;
                    }

                    int slotStart = slot.vertexOffset;
                    int slotVertexCount = slot.asset.meshData.vertexCount;
                    if (slotStart >= 0 &&
                        bakedVertexIndex >= slotStart &&
                        bakedVertexIndex < slotStart + slotVertexCount)
                    {
                        foundSlot = slot;
                        slotVertexIndex = bakedVertexIndex - slotStart;
                        return true;
                    }
                }

                return false;
            }

            int runningOffset = 0;
            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                SlotData slot = slots[i];
                if (!IsSelectableSlot(slot))
                {
                    continue;
                }

                int slotVertexCount = slot.asset.meshData.vertexCount;
                if (bakedVertexIndex >= runningOffset && bakedVertexIndex < runningOffset + slotVertexCount)
                {
                    foundSlot = slot;
                    slotVertexIndex = bakedVertexIndex - runningOffset;
                    return true;
                }

                runningOffset += slotVertexCount;
            }

            return false;
        }

        private int GetVisibleBakedVertexIndex(SlotData slot, int slotVertexIndex)
        {
            if (!IsSelectableSlot(slot) || slotVertexIndex < 0)
            {
                return -1;
            }

            if (touchupWeightsMode)
            {
                if (!IsSlotOnStageRenderer(slot) || slotVertexIndex >= slot.asset.meshData.vertexCount)
                {
                    return -1;
                }

                int bakedVertexIndex = slot.vertexOffset + slotVertexIndex;
                return BakedMesh != null && bakedVertexIndex >= 0 && bakedVertexIndex < BakedMesh.vertexCount
                    ? bakedVertexIndex
                    : -1;
            }

            int runningOffset = 0;
            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                SlotData recipeSlot = slots[i];
                if (!IsSelectableSlot(recipeSlot))
                {
                    continue;
                }

                int slotVertexCount = recipeSlot.asset.meshData.vertexCount;
                if (ReferenceEquals(recipeSlot, slot) || recipeSlot.slotName == slot.slotName)
                {
                    if (slotVertexIndex >= slotVertexCount)
                    {
                        return -1;
                    }
                    return runningOffset + slotVertexIndex;
                }
                runningOffset += slotVertexCount;
            }

            return -1;
        }

        public bool TryGetVisibleBakedVertexIndex(SlotData slot, int slotVertexIndex, out int bakedVertexIndex)
        {
            bakedVertexIndex = GetVisibleBakedVertexIndex(slot, slotVertexIndex);
            return bakedVertexIndex >= 0 && bakedVertexIndex < BakedMesh.vertexCount;
        }


        public HashSet<int> flippedVertexes = new HashSet<int>();

        public static VertexEditorStage ShowStage(DynamicCharacterAvatar DCA, MeshModifier modifier)
        {
            if (!CanOpenForDynamicCharacterAvatar(DCA))
            {
                return null;
            }

            VertexEditorStage stage = ScriptableObject.CreateInstance<VertexEditorStage>();
            stage.titleContent = new GUIContent();
            stage.titleContent.text = "Mesh Modifier Editor";
            stage.titleContent.image = EditorGUIUtility.IconContent("GameObject Icon").image;
            stage.thisDCA = DCA;
            stage.Currentmodifier = modifier;
            stage.sceneToolMode = SceneToolMode.Sculpt;
            stage.editorMode = MeshModifierEditor.EditorMode.MeshModifiers;
            CaptureSceneViewBeforeOpening(stage);
            StageUtility.GoToStage(stage, true);
            return stage;
        }

        private static bool CanOpenForDynamicCharacterAvatar(DynamicCharacterAvatar dca)
        {
            if (dca == null || PrefabStageUtility.GetPrefabStage(dca.gameObject) == null)
            {
                return true;
            }

            EditorUtility.DisplayDialog(
                "UMA Mesh Editors Are Unavailable in Prefab Mode",
                "The Face Editor and Vertex Editor work from a generated DynamicCharacterAvatar in an open scene. They cannot edit a DynamicCharacterAvatar while its prefab is open in Prefab Mode.\n\nExit Prefab Mode, then select a DynamicCharacterAvatar in the Scene Hierarchy and open this tool again.",
                "OK");
            return false;
        }

        public static void OpenSlotWeightEditor(SlotDataAsset slotAsset)
        {
            if (slotAsset == null)
            {
                EditorUtility.DisplayDialog("View and Edit weights", "Select a SlotDataAsset asset in the Project window.", "OK");
                return;
            }

            if (slotAsset.isUtilitySlot || UMAMeshData.IsNullOrEmptyMeshData(slotAsset.meshData))
            {
                EditorUtility.DisplayDialog("View and Edit weights", "The selected slot does not have editable mesh data.", "OK");
                return;
            }

            List<RaceData> races = GetCompatibleRacesForSlotWeightEditor(slotAsset);
            if (races.Count == 0)
            {
                races = GetAllPreviewRacesForSlotWeightEditor();
                if (races.Count == 0)
                {
                    EditorUtility.DisplayDialog("View and Edit weights", "No race assets are available to use as a slot-weight preview.", "OK");
                    return;
                }

                UmaSlotWeightEditorRacePickerWindow.Open(
                    slotAsset,
                    races,
                    "This slot is not part of a race base recipe. Choose a race to supply the preview skeleton; the selected slot will be added temporarily for weight editing.");
                return;
            }

            if (races.Count == 1)
            {
                ShowSlotWeightEditorStage(slotAsset, races[0]);
                return;
            }

            UmaSlotWeightEditorRacePickerWindow.Open(slotAsset, races);
        }

        internal static void ShowSlotWeightEditorStage(SlotDataAsset slotAsset, RaceData race)
        {
            if (slotAsset == null || race == null)
            {
                EditorUtility.DisplayDialog("View and Edit weights", "Select a slot and race before opening the weight editor.", "OK");
                return;
            }

            string errorMessage;
            DynamicCharacterAvatar previewAvatar;
            if (!TryCreateSlotWeightPreviewAvatar(slotAsset, race, out previewAvatar, out errorMessage))
            {
                EditorUtility.DisplayDialog("View and Edit weights", errorMessage, "OK");
                return;
            }

            VertexEditorStage stage = ScriptableObject.CreateInstance<VertexEditorStage>();
            stage.titleContent = new GUIContent();
            stage.titleContent.text = "Slot Weight Editor";
            stage.titleContent.image = EditorGUIUtility.IconContent("SkinnedMeshRenderer Icon").image;
            stage.thisDCA = previewAvatar;
            stage.Currentmodifier = null;
            stage.slotWeightEditorMode = true;
            stage.slotWeightEditorReadOnly = false;
            stage.ownsSlotWeightPreviewAvatar = true;
            stage.slotWeightEditorSlotAsset = slotAsset;
            stage.slotWeightEditorRace = race;
            CaptureSceneViewBeforeOpening(stage);
            StageUtility.GoToStage(stage, true);
        }

        public static void OpenCurrentCharacterWeightViewer(DynamicCharacterAvatar avatar)
        {
            if (!TryValidateCurrentCharacterWeightAvatar(avatar, out string errorMessage))
            {
                EditorUtility.DisplayDialog("View Current Character Weights", errorMessage, "OK");
                return;
            }

            VertexEditorStage stage = ScriptableObject.CreateInstance<VertexEditorStage>();
            stage.titleContent = new GUIContent();
            stage.titleContent.text = "Current Character Weights";
            stage.titleContent.image = EditorGUIUtility.IconContent("SkinnedMeshRenderer Icon").image;
            stage.thisDCA = avatar;
            stage.Currentmodifier = null;
            stage.slotWeightEditorMode = true;
            stage.slotWeightEditorReadOnly = true;
            stage.ownsSlotWeightPreviewAvatar = false;
            stage.slotWeightEditorSlotAsset = null;
            stage.slotWeightEditorRace = GetAvatarRaceData(avatar);
            CaptureSceneViewBeforeOpening(stage);
            StageUtility.GoToStage(stage, true);
        }

        public static void OpenTouchupWeights(DynamicCharacterAvatar avatar)
        {
            if (!TryValidateCurrentCharacterWeightAvatar(avatar, out string errorMessage))
            {
                EditorUtility.DisplayDialog("Touchup Weights", errorMessage, "OK");
                return;
            }

            VertexEditorStage stage = ScriptableObject.CreateInstance<VertexEditorStage>();
            stage.titleContent = new GUIContent
            {
                text = "Touchup Weights",
                image = EditorGUIUtility.IconContent("SkinnedMeshRenderer Icon").image
            };
            stage.thisDCA = avatar;
            stage.Currentmodifier = null;
            stage.slotWeightEditorMode = true;
            stage.slotWeightEditorReadOnly = false;
            stage.touchupWeightsMode = true;
            stage.ownsSlotWeightPreviewAvatar = false;
            stage.slotWeightEditorRace = GetAvatarRaceData(avatar);
            stage.sceneToolMode = SceneToolMode.SelectionBrush;
            stage.selectionBrushShape = SelectionBrushShape.Circle;
            stage.currentDefineMode = DefineMode.DefineVertexSet;
            stage.currentMode = selectMode.Add;
            CaptureSceneViewBeforeOpening(stage);
            StageUtility.GoToStage(stage, true);
        }

        private static void CaptureSceneViewBeforeOpening(VertexEditorStage stage)
        {
            if (stage == null) return;
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return;
            }
            stage.openedSceneView = sceneView;
        }

        private static bool TryValidateCurrentCharacterWeightAvatar(DynamicCharacterAvatar avatar, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (avatar == null)
            {
                errorMessage = "Select a DynamicCharacterAvatar, or one of its children, in the Hierarchy.";
                return false;
            }

            if (avatar.umaData == null || avatar.umaData.umaRecipe == null || avatar.umaData.umaRecipe.slotDataList == null)
            {
                errorMessage = "The selected DynamicCharacterAvatar has not built a usable UMA recipe yet.";
                return false;
            }

            SkinnedMeshRenderer renderer = GetSkinnedMeshRenderer(avatar);
            if (renderer == null || renderer.sharedMesh == null)
            {
                errorMessage = "The selected DynamicCharacterAvatar does not have a generated SkinnedMeshRenderer mesh.";
                return false;
            }

            SlotData[] slots = avatar.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                SlotData slot = slots[i];
                if (slot != null && slot.asset != null && !slot.Suppressed && !slot.asset.isUtilitySlot && !UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                {
                    return true;
                }
            }

            errorMessage = "The selected DynamicCharacterAvatar does not have any visible weighted slots to inspect.";
            return false;
        }

        private static RaceData GetAvatarRaceData(DynamicCharacterAvatar avatar)
        {
            if (avatar == null || avatar.activeRace == null)
            {
                return null;
            }

            return avatar.activeRace.racedata != null ? avatar.activeRace.racedata : avatar.activeRace.data;
        }

        internal SlotDataAsset SlotWeightEditorSlotAsset
        {
            get { return slotWeightEditorSlotAsset; }
        }

        internal RaceData SlotWeightEditorRace
        {
            get { return slotWeightEditorRace; }
        }

        internal bool IsSlotWeightEditorMode
        {
            get { return slotWeightEditorMode; }
        }

        internal bool IsSlotWeightEditorReadOnly
        {
            get { return slotWeightEditorReadOnly; }
        }

        internal bool IsTouchupWeightsMode
        {
            get { return touchupWeightsMode; }
        }

        internal List<SlotData> GetTouchupWeightSlots()
        {
            List<SlotData> result = new List<SlotData>();
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null ||
                thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return result;
            }

            SlotData[] slots = thisDCA.umaData.umaRecipe.slotDataList;
            HashSet<SlotDataAsset> seenAssets = new HashSet<SlotDataAsset>();
            for (int i = 0; i < slots.Length; i++)
            {
                SlotData slot = slots[i];
                if (IsSelectableSlot(slot) && IsSlotOnStageRenderer(slot) && seenAssets.Add(slot.asset))
                {
                    result.Add(slot);
                }
            }
            return result;
        }

        private bool IsSlotOnStageRenderer(SlotData slot)
        {
            if (slot == null || thisDCA == null || thisDCA.umaData == null ||
                thisDCA.umaData.RendererCount <= 1)
            {
                return slot != null;
            }

            SkinnedMeshRenderer renderer = stageSkinnedMeshRenderer != null
                ? stageSkinnedMeshRenderer
                : GetCurrentSkinnedMeshRenderer();
            int rendererIndex = thisDCA.umaData.GetRendererIndex(renderer);
            return rendererIndex >= 0 &&
                   ReferenceEquals(slot.rendererAsset, thisDCA.umaData.GetRendererAsset(rendererIndex));
        }

        internal SlotData TouchupWeightSlot
        {
            get { return touchupWeightSlot; }
        }

        internal int TouchupWeightBoneHash
        {
            get { return touchupWeightBoneHash; }
            set
            {
                if (touchupWeightBoneHash == value)
                {
                    return;
                }
                touchupWeightBoneHash = value;
                RefreshTouchupWeightVisualization();
            }
        }

        internal int TouchupSelectionCount
        {
            get { return GetTouchupSelectedVertices().Count; }
        }

        internal int TouchupWeightsRevision
        {
            get { return touchupWeightsRevision; }
        }

        internal bool HasPendingTouchupPaintWeights
        {
            get { return touchupPendingPaintWeights.Count > 0; }
        }

        internal void SetTouchupWeightSlot(SlotData slot)
        {
            if (!touchupWeightsMode || ReferenceEquals(touchupWeightSlot, slot))
            {
                return;
            }

            if (touchupPreviewWeights != null && touchupPreviewVertexIndices.Count > 0)
            {
                List<int> previewVertexIndices = new List<int>(touchupPreviewVertexIndices);
                touchupPreviewWeights = null;
                touchupPreviewVertexIndices.Clear();
                RecalculateTouchupVertexPositions(previewVertexIndices);
            }

            touchupWeightSlot = slot;
            SelectedVertexes.Clear();
            currentSelected = -1;
            touchupPreviewWeights = null;
            touchupPreviewVertexIndices.Clear();
            touchupPendingPaintWeights.Clear();
            SyncTouchupPendingPaintUndoState();
            touchupSavedPositionVertexIndices.Clear();
            touchupLivePositionVertexIndices.Clear();
            touchupSkinningPositionOffsets.Clear();
            ResetTouchupPaintData();
            RefreshVisibleSlotLists();
            selectionSlot = 0;
            if (slot != null)
            {
                for (int i = 1; i < selectFrom.Length; i++)
                {
                    if (string.Equals(selectFrom[i], slot.slotName, StringComparison.Ordinal))
                    {
                        selectionSlot = i;
                        break;
                    }
                }
            }

            touchupWeightBoneHash = FindDominantTouchupBoneHash(slot);
            EnsureTouchupPaintData(out _);
            RefreshTouchupWeightVisualization();
            RepaintLinkedEditors();
            SceneView.RepaintAll();
        }

        internal List<VertexSelection> GetTouchupSelectedVertices()
        {
            List<VertexSelection> result = new List<VertexSelection>();
            if (touchupWeightSlot == null || SelectedVertexes == null)
            {
                return result;
            }

            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                VertexSelection selection = SelectedVertexes[i];
                if (selection != null && SelectionMatchesTouchupSlot(selection))
                {
                    result.Add(selection);
                }
            }
            return result;
        }

        internal VertexSelection GetFirstTouchupSelectedVertex()
        {
            if (touchupWeightSlot == null || SelectedVertexes == null)
            {
                return null;
            }

            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                if (SelectionMatchesTouchupSlot(SelectedVertexes[i]))
                {
                    return SelectedVertexes[i];
                }
            }
            return null;
        }

        internal List<BoneOption> GetTouchupBoneOptions()
        {
            if (touchupWeightSlot == null)
            {
                return new List<BoneOption>();
            }

            return GetEditableBoneOptions(new VertexSelection
            {
                slot = touchupWeightSlot,
                vertexIndexOnSlot = 0
            });
        }

        internal void ClearTouchupSelection()
        {
            ClearTouchupSelection(true);
        }

        private bool ClearTouchupSelection(bool recordUndo)
        {
            if (SelectedVertexes == null || SelectedVertexes.Count == 0)
            {
                return false;
            }
            if (recordUndo)
            {
                Undo.RegisterCompleteObjectUndo(this, "Clear Touchup Weight Selection");
            }

            List<int> previewVertexIndices = new List<int>(touchupPreviewVertexIndices);
            touchupPreviewWeights = null;
            touchupPreviewVertexIndices.Clear();
            RecalculateTouchupVertexPositions(previewVertexIndices);
            SelectedVertexes.Clear();
            currentSelected = -1;
            RefreshTouchupWeightVisualization();
            RepaintLinkedEditors();
            SceneView.RepaintAll();
            return true;
        }

        internal void SetTouchupWeightPreview(List<VertexWeightEntry> weights)
        {
            List<VertexSelection> selectedVertices = GetTouchupSelectedVertices();
            if (selectedVertices.Count == 0 || touchupWeightSlot == null ||
                touchupWeightSlot.asset == null)
            {
                return;
            }

            if (!TryGetSlotMeshData(
                    touchupWeightSlot,
                    out UMAMeshData meshData,
                    out string statusMessage) ||
                !EnsureTouchupPaintData(out statusMessage))
            {
                touchupPaintStatusType = MessageType.Error;
                touchupPaintStatusMessage = statusMessage;
                return;
            }

            List<VertexWeightEntry> normalizedWeights =
                NormalizeTouchupWeights(weights, out statusMessage);
            if (normalizedWeights == null)
            {
                touchupPaintStatusType = MessageType.Error;
                touchupPaintStatusMessage = statusMessage;
                return;
            }

            Undo.RegisterCompleteObjectUndo(this, "Edit Touchup Vertex Weights");
            bool needsNewBinding = false;
            for (int i = 0; i < normalizedWeights.Count; i++)
            {
                if (GetBoundBoneIndex(meshData, normalizedWeights[i].boneHash) < 0)
                {
                    needsNewBinding = true;
                    break;
                }
            }
            if (needsNewBinding)
            {
                Undo.RecordObject(touchupWeightSlot.asset, "Bind Touchup Weight Bone");
            }
            if (!EnsureEditedWeightBonesAreBound(
                    meshData,
                    normalizedWeights,
                    out statusMessage))
            {
                touchupPaintStatusType = MessageType.Error;
                touchupPaintStatusMessage = statusMessage;
                return;
            }

            List<BoneWeight1> targetWeights =
                BuildTargetBoneWeights(meshData, normalizedWeights, out statusMessage);
            if (targetWeights == null)
            {
                touchupPaintStatusType = MessageType.Error;
                touchupPaintStatusMessage = statusMessage;
                return;
            }

            List<int> changedVertexIndices = new List<int>();
            for (int i = 0; i < selectedVertices.Count; i++)
            {
                int vertexIndex = selectedVertices[i].vertexIndexOnSlot;
                if (vertexIndex < 0 || vertexIndex >= touchupWorkingWeights.Length)
                {
                    continue;
                }
                List<BoneWeight1> stagedWeights = CloneBoneWeightList(targetWeights);
                touchupWorkingWeights[vertexIndex] = stagedWeights;
                touchupPendingPaintWeights[vertexIndex] =
                    CloneBoneWeightList(stagedWeights);
                changedVertexIndices.Add(vertexIndex);
            }

            touchupPreviewWeights = null;
            touchupPreviewVertexIndices.Clear();
            SyncTouchupPendingPaintUndoState();
            touchupPaintStatusType = MessageType.Info;
            touchupPaintStatusMessage =
                "Staged numeric weight changes for " +
                changedVertexIndices.Count + " vertex(es).";
            RecalculateTouchupVertexPositions(changedVertexIndices);
            RefreshTouchupWeightVisualization();
            RepaintLinkedEditors();
        }

        internal void ClearTouchupWeightPreview()
        {
            List<int> previewVertexIndices = new List<int>(touchupPreviewVertexIndices);
            touchupPreviewWeights = null;
            touchupPreviewVertexIndices.Clear();
            RecalculateTouchupVertexPositions(previewVertexIndices);
            RefreshTouchupWeightVisualization();
        }

        internal bool TrySavePendingTouchupPaintWeights(out string statusMessage)
        {
            statusMessage = string.Empty;
            if (touchupPendingPaintWeights.Count == 0)
            {
                statusMessage = "There are no pending weight changes to save.";
                return false;
            }
            if (touchupWeightSlot == null || touchupWeightSlot.asset == null)
            {
                statusMessage = "The edited slot is no longer available.";
                return false;
            }

            if (!TryRewriteSlotAssetVertexWeights(
                    touchupWeightSlot,
                    touchupPendingPaintWeights,
                    "Paint Vertex Weights",
                    out statusMessage))
            {
                return false;
            }

            int savedVertexCount = touchupPendingPaintWeights.Count;
            List<int> savedVertexIndices = new List<int>(touchupPendingPaintWeights.Keys);
            AssetDatabase.SaveAssetIfDirty(touchupWeightSlot.asset);
            touchupPendingPaintWeights.Clear();
            SyncTouchupPendingPaintUndoState();
            touchupSavedPositionVertexIndices.UnionWith(savedVertexIndices);
            touchupWeightsRevision++;
            touchupPaintStatusType = MessageType.Info;
            touchupPaintStatusMessage =
                "Saved weights for " + savedVertexCount + " vertex(es).";
            statusMessage = touchupPaintStatusMessage;
            RecalculateTouchupVertexPositions(savedVertexIndices);
            RefreshTouchupWeightVisualization();
            RepaintLinkedEditors();
            return true;
        }

        internal void RevertPendingTouchupPaintWeights()
        {
            if (touchupPendingPaintWeights.Count == 0)
            {
                return;
            }

            List<int> revertedVertexIndices = new List<int>(touchupPendingPaintWeights.Keys);
            Undo.RegisterCompleteObjectUndo(this, "Revert Touchup Weights");
            touchupPendingPaintWeights.Clear();
            SyncTouchupPendingPaintUndoState();
            ResetTouchupPaintData();
            EnsureTouchupPaintData(out _);
            RecalculateTouchupVertexPositions(revertedVertexIndices);
            touchupWeightsRevision++;
            touchupPaintStatusType = MessageType.Info;
            touchupPaintStatusMessage = "Reverted unsaved weight changes.";
            RefreshTouchupWeightVisualization();
            RepaintLinkedEditors();
        }

        internal bool TrySaveTouchupWeights(List<VertexWeightEntry> editedWeights, out string statusMessage)
        {
            statusMessage = string.Empty;
            List<VertexSelection> selectedVertices = GetTouchupSelectedVertices();
            if (touchupWeightSlot == null || touchupWeightSlot.asset == null)
            {
                statusMessage = "Choose a slot before saving weights.";
                return false;
            }
            if (selectedVertices.Count == 0)
            {
                statusMessage = "Select at least one vertex on the current slot.";
                return false;
            }
            if (!TryGetSlotMeshData(touchupWeightSlot, out UMAMeshData meshData, out statusMessage))
            {
                return false;
            }
            if (!HasValidManagedBoneWeights(meshData) && !HasValidLegacyBoneWeights(meshData))
            {
                statusMessage = "The SlotDataAsset has no valid managed or legacy weights to preserve.";
                return false;
            }

            List<VertexWeightEntry> normalizedWeights = NormalizeTouchupWeights(editedWeights, out statusMessage);
            if (normalizedWeights == null)
            {
                return false;
            }
            for (int i = 0; i < normalizedWeights.Count; i++)
            {
                VertexWeightEntry weight = normalizedWeights[i];
                if (GetBoundBoneIndex(meshData, weight.boneHash) >= 0)
                {
                    continue;
                }
                if (weight.boneHash == 0)
                {
                    statusMessage = "A weight references a bone without a usable UMA hash.";
                    return false;
                }
                if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.skeleton == null ||
                    thisDCA.umaData.skeleton.GetBoneTransform(weight.boneHash) == null)
                {
                    statusMessage = "Bone '" + weight.boneName + "' is not available on the current character skeleton.";
                    return false;
                }
            }

            Undo.RecordObject(touchupWeightSlot.asset, "Touchup Slot Weights");
            if (!EnsureEditedWeightBonesAreBound(meshData, normalizedWeights, out statusMessage))
            {
                return false;
            }

            List<BoneWeight1> targetWeights = BuildTargetBoneWeights(meshData, normalizedWeights, out statusMessage);
            if (targetWeights == null)
            {
                return false;
            }

            Dictionary<int, List<BoneWeight1>> weightsByVertex = new Dictionary<int, List<BoneWeight1>>();
            for (int i = 0; i < selectedVertices.Count; i++)
            {
                int vertexIndex = selectedVertices[i].vertexIndexOnSlot;
                if (!weightsByVertex.ContainsKey(vertexIndex))
                {
                    weightsByVertex.Add(vertexIndex, new List<BoneWeight1>(targetWeights));
                }
            }

            if (!TryRewriteSlotAssetVertexWeights(
                    touchupWeightSlot,
                    weightsByVertex,
                    selectedVertices.Count == 1 ? "Touchup Vertex Weights" : "Touchup Vertex Weights",
                    out statusMessage))
            {
                return false;
            }

            AssetDatabase.SaveAssetIfDirty(touchupWeightSlot.asset);
            List<int> savedVertexIndices = new List<int>(weightsByVertex.Keys);
            touchupSavedPositionVertexIndices.UnionWith(savedVertexIndices);
            touchupPreviewWeights = null;
            touchupPreviewVertexIndices.Clear();
            ResetTouchupPaintData();
            EnsureTouchupPaintData(out _);
            RecalculateTouchupVertexPositions(savedVertexIndices);
            touchupWeightsRevision++;
            statusMessage = "Saved weights for " + weightsByVertex.Count + " " +
                            (weightsByVertex.Count == 1 ? "vertex" : "vertices") +
                            " to " + touchupWeightSlot.asset.slotName + ".";
            RefreshTouchupWeightVisualization();
            RepaintLinkedEditors();
            SceneView.RepaintAll();
            return true;
        }

        private List<VertexWeightEntry> NormalizeTouchupWeights(List<VertexWeightEntry> editedWeights, out string statusMessage)
        {
            statusMessage = string.Empty;
            if (editedWeights == null || editedWeights.Count == 0)
            {
                statusMessage = "Add at least one bone weight before saving.";
                return null;
            }

            List<VertexWeightEntry> result = new List<VertexWeightEntry>();
            float total = 0f;
            for (int i = 0; i < editedWeights.Count; i++)
            {
                VertexWeightEntry source = editedWeights[i];
                if (source == null)
                {
                    continue;
                }

                float weight = Mathf.Clamp01(source.weight);
                if (weight <= 0f)
                {
                    continue;
                }

                VertexWeightEntry copy = source.Clone();
                copy.weight = weight;
                result.Add(copy);
                total += weight;
            }

            if (result.Count == 0 || total <= Mathf.Epsilon)
            {
                statusMessage = "At least one bone weight must be greater than zero.";
                return null;
            }

            for (int i = 0; i < result.Count; i++)
            {
                result[i].weight /= total;
            }
            return result;
        }

        private void ResetTouchupPaintData()
        {
            touchupAssetWeights = null;
            touchupWorkingWeights = null;
            touchupConnectedVertices = null;
            touchupCrossSlotMaskedVertices = null;
            touchupDisplayColors = null;
            touchupPaintStrokeWeights.Clear();
            touchupPaintStrokeActive = false;
            touchupPaintStrokeBlocked = false;
            touchupSmearHasPreviousSample = false;
            touchupSmearPreviousCandidates.Clear();
        }

        private bool EnsureTouchupPaintData(out string statusMessage)
        {
            statusMessage = string.Empty;
            if (!TryGetSlotMeshData(touchupWeightSlot, out UMAMeshData meshData, out statusMessage))
            {
                return false;
            }

            if (touchupAssetWeights != null && touchupAssetWeights.Length == meshData.vertexCount &&
                touchupWorkingWeights != null && touchupWorkingWeights.Length == meshData.vertexCount &&
                touchupConnectedVertices != null && touchupConnectedVertices.Length == meshData.vertexCount &&
                touchupCrossSlotMaskedVertices != null)
            {
                return true;
            }

            bool hasManagedWeights = HasValidManagedBoneWeights(meshData);
            bool hasLegacyWeights = HasValidLegacyBoneWeights(meshData);
            if (!hasManagedWeights && !hasLegacyWeights)
            {
                statusMessage = "The selected slot has no usable bone weights.";
                return false;
            }

            touchupAssetWeights = new List<BoneWeight1>[meshData.vertexCount];
            touchupWorkingWeights = new List<BoneWeight1>[meshData.vertexCount];
            int managedOffset = 0;
            for (int vertexIndex = 0; vertexIndex < meshData.vertexCount; vertexIndex++)
            {
                List<BoneWeight1> weights;
                if (hasManagedWeights)
                {
                    int weightCount = meshData.ManagedBonesPerVertex[vertexIndex];
                    weights = new List<BoneWeight1>(weightCount);
                    for (int weightIndex = 0; weightIndex < weightCount; weightIndex++)
                    {
                        weights.Add(meshData.ManagedBoneWeights[managedOffset + weightIndex]);
                    }
                    managedOffset += weightCount;
                }
                else
                {
                    TryGetLegacyWeightsForVertex(meshData, vertexIndex, out weights);
                }

                touchupAssetWeights[vertexIndex] =
                    weights != null ? CloneBoneWeightList(weights) : new List<BoneWeight1>();
                touchupWorkingWeights[vertexIndex] =
                    CloneBoneWeightList(touchupAssetWeights[vertexIndex]);
            }

            foreach (KeyValuePair<int, List<BoneWeight1>> pendingWeight in touchupPendingPaintWeights)
            {
                if (pendingWeight.Key >= 0 && pendingWeight.Key < touchupWorkingWeights.Length)
                {
                    touchupWorkingWeights[pendingWeight.Key] =
                        CloneBoneWeightList(pendingWeight.Value);
                }
            }

            touchupConnectedVertices = BuildTouchupConnectedVertices(meshData);
            touchupCrossSlotMaskedVertices = BuildTouchupCrossSlotVertexMask();
            return true;
        }

        private int[][] BuildTouchupConnectedVertices(UMAMeshData meshData)
        {
            HashSet<int>[] connected = new HashSet<int>[meshData.vertexCount];
            for (int i = 0; i < connected.Length; i++)
            {
                connected[i] = new HashSet<int>();
            }

            if (meshData.submeshes != null)
            {
                for (int submeshIndex = 0; submeshIndex < meshData.submeshes.Length; submeshIndex++)
                {
                    SubMeshTriangles submesh = meshData.submeshes[submeshIndex];
                    if (submesh == null)
                    {
                        continue;
                    }

                    int[] triangles = submesh.getManagedTriangles(0);
                    if (triangles == null)
                    {
                        continue;
                    }

                    for (int triangleIndex = 0; triangleIndex + 2 < triangles.Length; triangleIndex += 3)
                    {
                        int vertex0 = triangles[triangleIndex];
                        int vertex1 = triangles[triangleIndex + 1];
                        int vertex2 = triangles[triangleIndex + 2];
                        AddTouchupConnection(connected, vertex0, vertex1);
                        AddTouchupConnection(connected, vertex0, vertex2);
                        AddTouchupConnection(connected, vertex1, vertex2);
                    }
                }
            }

            int[][] result = new int[connected.Length][];
            for (int i = 0; i < connected.Length; i++)
            {
                result[i] = new int[connected[i].Count];
                connected[i].CopyTo(result[i]);
            }
            return result;
        }

        private HashSet<int> BuildTouchupCrossSlotVertexMask()
        {
            HashSet<int> result = new HashSet<int>();
            if (touchupWeightSlot == null || touchupWeightSlot.asset == null ||
                BakedMesh == null || thisDCA == null || thisDCA.umaData == null ||
                thisDCA.umaData.umaRecipe == null)
            {
                return result;
            }

            if (bakedVertices == null || bakedVertices.Length != BakedMesh.vertexCount)
            {
                RefreshBakedMeshCaches();
            }
            if (bakedVertices == null)
            {
                return result;
            }

            float tolerance = Mathf.Max(0.00001f, BakedMesh.bounds.size.magnitude * 0.00001f);
            float toleranceSqr = tolerance * tolerance;
            float inverseTolerance = 1f / tolerance;
            Dictionary<Vector3Int, List<Vector3>> otherSlotPositions =
                new Dictionary<Vector3Int, List<Vector3>>();

            SlotData[] slots = thisDCA.umaData.umaRecipe.slotDataList;
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                SlotData slot = slots[slotIndex];
                if (slot == null || ReferenceEquals(slot, touchupWeightSlot) ||
                    !IsSelectableSlot(slot) || !IsSlotOnStageRenderer(slot))
                {
                    continue;
                }

                int bakedStart = slot.vertexOffset;
                int vertexCount = slot.asset.meshData.vertexCount;
                if (bakedStart < 0 || bakedStart + vertexCount > bakedVertices.Length)
                {
                    continue;
                }

                for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                {
                    Vector3 position = bakedVertices[bakedStart + vertexIndex];
                    Vector3Int key = GetTouchupPositionCell(position, inverseTolerance);
                    if (!otherSlotPositions.TryGetValue(key, out List<Vector3> positions))
                    {
                        positions = new List<Vector3>();
                        otherSlotPositions.Add(key, positions);
                    }
                    positions.Add(position);
                }
            }

            int activeStart = touchupWeightSlot.vertexOffset;
            int activeCount = touchupWeightSlot.asset.meshData.vertexCount;
            if (activeStart < 0 || activeStart + activeCount > bakedVertices.Length)
            {
                return result;
            }

            for (int vertexIndex = 0; vertexIndex < activeCount; vertexIndex++)
            {
                Vector3 position = bakedVertices[activeStart + vertexIndex];
                Vector3Int cell = GetTouchupPositionCell(position, inverseTolerance);
                bool matched = false;
                for (int x = -1; x <= 1 && !matched; x++)
                {
                    for (int y = -1; y <= 1 && !matched; y++)
                    {
                        for (int z = -1; z <= 1 && !matched; z++)
                        {
                            Vector3Int neighborCell = new Vector3Int(
                                cell.x + x,
                                cell.y + y,
                                cell.z + z);
                            if (!otherSlotPositions.TryGetValue(neighborCell, out List<Vector3> positions))
                            {
                                continue;
                            }

                            for (int positionIndex = 0; positionIndex < positions.Count; positionIndex++)
                            {
                                if ((position - positions[positionIndex]).sqrMagnitude <= toleranceSqr)
                                {
                                    result.Add(vertexIndex);
                                    matched = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        private static Vector3Int GetTouchupPositionCell(Vector3 position, float inverseTolerance)
        {
            return new Vector3Int(
                Mathf.FloorToInt(position.x * inverseTolerance),
                Mathf.FloorToInt(position.y * inverseTolerance),
                Mathf.FloorToInt(position.z * inverseTolerance));
        }

        private static void AddTouchupConnection(HashSet<int>[] connected, int vertex0, int vertex1)
        {
            if (vertex0 < 0 || vertex1 < 0 || vertex0 >= connected.Length || vertex1 >= connected.Length ||
                vertex0 == vertex1)
            {
                return;
            }

            connected[vertex0].Add(vertex1);
            connected[vertex1].Add(vertex0);
        }

        private bool ApplyTouchupWeightPaintBrush(Event currentEvent)
        {
            if (touchupPaintStrokeBlocked)
            {
                return false;
            }

            if (touchupPreviewWeights != null)
            {
                touchupPaintStrokeBlocked = true;
                touchupPaintStatusType = MessageType.Warning;
                touchupPaintStatusMessage =
                    "Save or revert the numeric weight edits in the Touchup Weights window before painting.";
                return false;
            }

            if (!EnsureTouchupPaintData(out string statusMessage))
            {
                touchupPaintStrokeBlocked = true;
                touchupPaintStatusType = MessageType.Error;
                touchupPaintStatusMessage = statusMessage;
                return false;
            }

            Vector2 smearDelta = Vector2.zero;
            bool smearNeedsInitialSample = false;
            if (touchupWeightPaintMode == TouchupWeightPaintMode.Smear)
            {
                if (!touchupSmearHasPreviousSample)
                {
                    touchupSmearHasPreviousSample = true;
                    touchupSmearPreviousMousePosition = currentEvent.mousePosition;
                    smearNeedsInitialSample = true;
                }
                else
                {
                    smearDelta =
                        currentEvent.mousePosition - touchupSmearPreviousMousePosition;
                    touchupSmearPreviousMousePosition = currentEvent.mousePosition;
                    if (smearDelta.sqrMagnitude < 0.01f)
                    {
                        return false;
                    }
                }
            }

            List<int> candidates = GetTouchupBrushVertexIndices(
                currentEvent,
                touchupPaintSelectedVerticesOnly);
            List<int> smearCurrentSourceCandidates =
                touchupWeightPaintMode == TouchupWeightPaintMode.Smear
                    ? new List<int>(candidates)
                    : null;
            if (touchupAutoMaskConnectedVertices &&
                touchupCrossSlotMaskedVertices != null &&
                touchupCrossSlotMaskedVertices.Count > 0)
            {
                int unmaskedCandidateCount = candidates.Count;
                candidates.RemoveAll(vertexIndex =>
                    touchupCrossSlotMaskedVertices.Contains(vertexIndex));
                if (unmaskedCandidateCount > 0 && candidates.Count == 0)
                {
                    touchupPaintStatusType = MessageType.Info;
                    touchupPaintStatusMessage =
                        "All vertices under the brush are protected by Auto-mask Connected Vertices.";
                }
            }
            if (candidates.Count == 0)
            {
                if (touchupWeightPaintMode == TouchupWeightPaintMode.Smear)
                {
                    touchupSmearPreviousCandidates.Clear();
                    touchupSmearPreviousCandidates.AddRange(smearCurrentSourceCandidates);
                }
                return false;
            }
            if (touchupWeightPaintMode == TouchupWeightPaintMode.Smear && smearNeedsInitialSample)
            {
                touchupSmearPreviousCandidates.Clear();
                touchupSmearPreviousCandidates.AddRange(smearCurrentSourceCandidates);
                return false;
            }

            UMAMeshData meshData = touchupWeightSlot.asset.meshData;
            int selectedBoneIndex = GetBoundBoneIndex(meshData, touchupWeightBoneHash);
            bool operationCanAddBone =
                 (touchupWeightPaintMode == TouchupWeightPaintMode.Replace ||
                 touchupWeightPaintMode == TouchupWeightPaintMode.Add) &&
                touchupPaintAmount > BoneWeightMismatchTolerance;
            if (selectedBoneIndex < 0 && operationCanAddBone)
            {
                Undo.RecordObject(touchupWeightSlot.asset, "Bind Weight Paint Bone");
                VertexWeightEntry newBinding = new VertexWeightEntry
                {
                    boneIndex = -1,
                    boneHash = touchupWeightBoneHash,
                    boneName = GetBoneDisplayName(touchupWeightBoneHash, -1),
                    weight = 1f
                };
                if (!EnsureEditedWeightBonesAreBound(
                        meshData,
                        new List<VertexWeightEntry> { newBinding },
                        out statusMessage))
                {
                    touchupPaintStrokeBlocked = true;
                    touchupPaintStatusType = MessageType.Error;
                    touchupPaintStatusMessage = statusMessage;
                    return false;
                }
                selectedBoneIndex = newBinding.boneIndex;
            }

            Dictionary<int, float> smearTargetWeights = null;
            if (touchupWeightPaintMode == TouchupWeightPaintMode.Smear)
            {
                if (selectedBoneIndex < 0)
                {
                    touchupSmearPreviousCandidates.Clear();
                    touchupSmearPreviousCandidates.AddRange(smearCurrentSourceCandidates);
                    return false;
                }

                smearTargetWeights = BuildTouchupSmearTargetWeights(
                    candidates,
                    touchupSmearPreviousCandidates,
                    smearDelta,
                    selectedBoneIndex);
                touchupSmearPreviousCandidates.Clear();
                touchupSmearPreviousCandidates.AddRange(smearCurrentSourceCandidates);
                if (smearTargetWeights.Count == 0)
                {
                    return false;
                }
            }

            if (!touchupPaintStrokeActive)
            {
                touchupPaintStrokeActive = true;
                touchupPaintStrokeWeights.Clear();
            }

            List<int> changedVertices = new List<int>();
            for (int i = 0; i < candidates.Count; i++)
            {
                int vertexIndex = candidates[i];
                if (touchupWeightPaintMode != TouchupWeightPaintMode.Smear)
                {
                    string strokeKey = "weight:" + vertexIndex;
                    if (selectionBrushedVerticesThisStroke.Contains(strokeKey))
                    {
                        continue;
                    }
                    selectionBrushedVerticesThisStroke.Add(strokeKey);
                }

                List<BoneWeight1> paintedWeights;
                if (touchupWeightPaintMode == TouchupWeightPaintMode.Smear)
                {
                    if (!smearTargetWeights.TryGetValue(vertexIndex, out float sourceWeight))
                    {
                        continue;
                    }
                    paintedWeights = BuildSmearedTouchupWeights(
                        vertexIndex,
                        selectedBoneIndex,
                        sourceWeight);
                }
                else
                {
                    paintedWeights = BuildPaintedTouchupWeights(
                        vertexIndex,
                        selectedBoneIndex);
                }
                if (paintedWeights == null ||
                    BoneWeightListsApproximatelyEqual(touchupWorkingWeights[vertexIndex], paintedWeights))
                {
                    continue;
                }

                touchupWorkingWeights[vertexIndex] = paintedWeights;
                touchupPaintStrokeWeights[vertexIndex] = CloneBoneWeightList(paintedWeights);
                changedVertices.Add(vertexIndex);
            }

            if (changedVertices.Count == 0)
            {
                return false;
            }

            UpdateTouchupWeightVisualization(changedVertices);
            RepaintLinkedEditors();
            return true;
        }

        private Dictionary<int, float> BuildTouchupSmearTargetWeights(
            List<int> targetVertices,
            List<int> sourceVertices,
            Vector2 mouseDelta,
            int selectedBoneIndex)
        {
            Dictionary<int, float> result = new Dictionary<int, float>();
            if (sourceVertices == null || sourceVertices.Count == 0)
            {
                return result;
            }

            List<Vector2> sourcePositions = new List<Vector2>(sourceVertices.Count);
            List<float> sourceWeights = new List<float>(sourceVertices.Count);
            for (int sourceIndex = 0; sourceIndex < sourceVertices.Count; sourceIndex++)
            {
                int vertexIndex = sourceVertices[sourceIndex];
                if (!TryGetTouchupVertexScreenPosition(vertexIndex, out Vector2 screenPosition))
                {
                    continue;
                }

                sourcePositions.Add(screenPosition);
                sourceWeights.Add(GetWeightFromList(
                    touchupWorkingWeights[vertexIndex],
                    selectedBoneIndex));
            }

            if (sourcePositions.Count == 0)
            {
                return result;
            }

            for (int targetIndex = 0; targetIndex < targetVertices.Count; targetIndex++)
            {
                int vertexIndex = targetVertices[targetIndex];
                if (!TryGetTouchupVertexScreenPosition(vertexIndex, out Vector2 targetPosition))
                {
                    continue;
                }

                Vector2 samplePosition = targetPosition - mouseDelta;
                float nearestDistance0 = float.MaxValue;
                float nearestDistance1 = float.MaxValue;
                float nearestDistance2 = float.MaxValue;
                float nearestWeight0 = 0f;
                float nearestWeight1 = 0f;
                float nearestWeight2 = 0f;

                for (int sourceIndex = 0; sourceIndex < sourcePositions.Count; sourceIndex++)
                {
                    float distance = (sourcePositions[sourceIndex] - samplePosition).sqrMagnitude;
                    float sourceWeight = sourceWeights[sourceIndex];
                    if (distance < nearestDistance0)
                    {
                        nearestDistance2 = nearestDistance1;
                        nearestWeight2 = nearestWeight1;
                        nearestDistance1 = nearestDistance0;
                        nearestWeight1 = nearestWeight0;
                        nearestDistance0 = distance;
                        nearestWeight0 = sourceWeight;
                    }
                    else if (distance < nearestDistance1)
                    {
                        nearestDistance2 = nearestDistance1;
                        nearestWeight2 = nearestWeight1;
                        nearestDistance1 = distance;
                        nearestWeight1 = sourceWeight;
                    }
                    else if (distance < nearestDistance2)
                    {
                        nearestDistance2 = distance;
                        nearestWeight2 = sourceWeight;
                    }
                }

                float weightedTotal = 0f;
                float influenceTotal = 0f;
                AddTouchupSmearSample(
                    nearestDistance0,
                    nearestWeight0,
                    ref weightedTotal,
                    ref influenceTotal);
                AddTouchupSmearSample(
                    nearestDistance1,
                    nearestWeight1,
                    ref weightedTotal,
                    ref influenceTotal);
                AddTouchupSmearSample(
                    nearestDistance2,
                    nearestWeight2,
                    ref weightedTotal,
                    ref influenceTotal);
                if (influenceTotal > Mathf.Epsilon)
                {
                    result[vertexIndex] = weightedTotal / influenceTotal;
                }
            }

            return result;
        }

        private static void AddTouchupSmearSample(
            float squaredDistance,
            float boneWeight,
            ref float weightedTotal,
            ref float influenceTotal)
        {
            if (squaredDistance == float.MaxValue)
            {
                return;
            }

            // Shepard interpolation keeps the smear continuous when the mouse moves less
            // than the screen-space distance between vertices.
            float influence = 1f / Mathf.Sqrt(squaredDistance + 0.25f);
            weightedTotal += boneWeight * influence;
            influenceTotal += influence;
        }

        private List<BoneWeight1> BuildSmearedTouchupWeights(
            int vertexIndex,
            int selectedBoneIndex,
            float sourceWeight)
        {
            List<BoneWeight1> currentWeights =
                CloneBoneWeightList(touchupWorkingWeights[vertexIndex]);
            float currentWeight = GetWeightFromList(currentWeights, selectedBoneIndex);
            float smearedWeight = Mathf.Lerp(
                currentWeight,
                Mathf.Clamp01(sourceWeight),
                Mathf.Clamp01(touchupPaintAmount));
            return SetTouchupBoneWeight(currentWeights, selectedBoneIndex, smearedWeight);
        }

        private List<BoneWeight1> BuildPaintedTouchupWeights(int vertexIndex, int selectedBoneIndex)
        {
            List<BoneWeight1> currentWeights = CloneBoneWeightList(touchupWorkingWeights[vertexIndex]);
            float amount = Mathf.Clamp01(touchupPaintAmount);

            switch (touchupWeightPaintMode)
            {
                case TouchupWeightPaintMode.Replace:
                    if (selectedBoneIndex < 0)
                    {
                        return currentWeights;
                    }
                    return SetTouchupBoneWeight(currentWeights, selectedBoneIndex, amount);

                case TouchupWeightPaintMode.Add:
                    if (selectedBoneIndex < 0)
                    {
                        return currentWeights;
                    }
                    SetWeightInMap(currentWeights, selectedBoneIndex,
                        GetWeightFromList(currentWeights, selectedBoneIndex) + amount);
                    return NormalizeTouchupBoneWeightList(currentWeights, touchupWorkingWeights[vertexIndex]);

                case TouchupWeightPaintMode.Remove:
                    if (selectedBoneIndex < 0)
                    {
                        return currentWeights;
                    }
                    SetWeightInMap(currentWeights, selectedBoneIndex,
                        Mathf.Max(0f, GetWeightFromList(currentWeights, selectedBoneIndex) - amount));
                    return NormalizeTouchupBoneWeightList(currentWeights, touchupWorkingWeights[vertexIndex]);

                case TouchupWeightPaintMode.Smooth:
                    return touchupSmoothSelectedBoneOnly
                        ? SmoothTouchupSelectedBone(vertexIndex, currentWeights, selectedBoneIndex, amount)
                        : SmoothAllTouchupWeights(vertexIndex, currentWeights, amount);
            }

            return currentWeights;
        }

        private List<BoneWeight1> SmoothTouchupSelectedBone(
            int vertexIndex,
            List<BoneWeight1> currentWeights,
            int selectedBoneIndex,
            float amount)
        {
            if (selectedBoneIndex < 0 || touchupConnectedVertices == null ||
                vertexIndex < 0 || vertexIndex >= touchupConnectedVertices.Length)
            {
                return currentWeights;
            }

            int[] connected = touchupConnectedVertices[vertexIndex];
            if (connected == null || connected.Length == 0)
            {
                return currentWeights;
            }

            float connectedWeight = 0f;
            for (int i = 0; i < connected.Length; i++)
            {
                connectedWeight += GetWeightFromList(
                    touchupWorkingWeights[connected[i]],
                    selectedBoneIndex);
            }
            float averageWeight = connectedWeight / connected.Length;
            float smoothedWeight = Mathf.Lerp(
                GetWeightFromList(currentWeights, selectedBoneIndex),
                averageWeight,
                amount);
            return SetTouchupBoneWeight(currentWeights, selectedBoneIndex, smoothedWeight);
        }

        private List<BoneWeight1> SmoothAllTouchupWeights(
            int vertexIndex,
            List<BoneWeight1> currentWeights,
            float amount)
        {
            if (touchupConnectedVertices == null ||
                vertexIndex < 0 || vertexIndex >= touchupConnectedVertices.Length)
            {
                return currentWeights;
            }

            int[] connected = touchupConnectedVertices[vertexIndex];
            if (connected == null || connected.Length == 0)
            {
                return currentWeights;
            }

            Dictionary<int, float> currentMap = BuildWeightMap(currentWeights);
            Dictionary<int, float> averageMap = new Dictionary<int, float>();
            for (int i = 0; i < connected.Length; i++)
            {
                AddWeightsToWeightMap(averageMap, touchupWorkingWeights[connected[i]], 1f / connected.Length);
            }

            HashSet<int> boneIndices = new HashSet<int>(currentMap.Keys);
            boneIndices.UnionWith(averageMap.Keys);
            List<BoneWeight1> result = new List<BoneWeight1>(boneIndices.Count);
            foreach (int boneIndex in boneIndices)
            {
                currentMap.TryGetValue(boneIndex, out float currentWeight);
                averageMap.TryGetValue(boneIndex, out float averageWeight);
                result.Add(new BoneWeight1
                {
                    boneIndex = boneIndex,
                    weight = Mathf.Lerp(currentWeight, averageWeight, amount)
                });
            }
            return NormalizeTouchupBoneWeightList(result, currentWeights);
        }

        private void SmoothSelectedTouchupVertexWeights()
        {
            List<VertexSelection> selectedVertices = GetTouchupSelectedVertices();
            if (selectedVertices.Count == 0)
            {
                touchupPaintStatusType = MessageType.Warning;
                touchupPaintStatusMessage = "Select at least one vertex to smooth.";
                return;
            }

            if (!EnsureTouchupPaintData(out string statusMessage))
            {
                touchupPaintStatusType = MessageType.Error;
                touchupPaintStatusMessage = statusMessage;
                return;
            }

            float amount = Mathf.Clamp01(touchupSelectionSmoothPercentage / 100f);
            if (amount <= Mathf.Epsilon)
            {
                touchupPaintStatusType = MessageType.Info;
                touchupPaintStatusMessage = "The smoothing percentage is 0%, so no weights changed.";
                return;
            }

            HashSet<int> targetVertices = new HashSet<int>();
            int protectedVertexCount = 0;
            for (int selectionIndex = 0;
                 selectionIndex < selectedVertices.Count;
                 selectionIndex++)
            {
                int vertexIndex = selectedVertices[selectionIndex].vertexIndexOnSlot;
                if (vertexIndex < 0 || vertexIndex >= touchupWorkingWeights.Length)
                {
                    continue;
                }
                if (touchupAutoMaskConnectedVertices &&
                    touchupCrossSlotMaskedVertices != null &&
                    touchupCrossSlotMaskedVertices.Contains(vertexIndex))
                {
                    protectedVertexCount++;
                    continue;
                }
                targetVertices.Add(vertexIndex);
            }

            // Calculate the complete batch before changing touchupWorkingWeights so every
            // result is sampled from the same pre-operation state.
            Dictionary<int, List<BoneWeight1>> smoothedWeights =
                new Dictionary<int, List<BoneWeight1>>();
            foreach (int vertexIndex in targetVertices)
            {
                List<BoneWeight1> currentWeights =
                    CloneBoneWeightList(touchupWorkingWeights[vertexIndex]);
                List<BoneWeight1> result =
                    SmoothAllTouchupWeights(vertexIndex, currentWeights, amount);
                if (!BoneWeightListsApproximatelyEqual(currentWeights, result))
                {
                    smoothedWeights[vertexIndex] = result;
                }
            }

            if (smoothedWeights.Count == 0)
            {
                touchupPaintStatusType = MessageType.Info;
                touchupPaintStatusMessage = protectedVertexCount > 0
                    ? "No weights changed. Auto-mask protected " +
                      protectedVertexCount + " selected vertex(es)."
                    : "The selected vertex weights are already smooth at this percentage.";
                return;
            }

            Undo.RegisterCompleteObjectUndo(this, "Smooth Selected Vertex Weights");
            List<int> changedVertexIndices = new List<int>(smoothedWeights.Count);
            foreach (KeyValuePair<int, List<BoneWeight1>> smoothedVertex in smoothedWeights)
            {
                touchupWorkingWeights[smoothedVertex.Key] =
                    CloneBoneWeightList(smoothedVertex.Value);
                touchupPendingPaintWeights[smoothedVertex.Key] =
                    CloneBoneWeightList(smoothedVertex.Value);
                changedVertexIndices.Add(smoothedVertex.Key);
            }

            touchupPreviewWeights = null;
            touchupPreviewVertexIndices.Clear();
            SyncTouchupPendingPaintUndoState();
            touchupWeightsRevision++;
            touchupPaintStatusType = MessageType.Info;
            touchupPaintStatusMessage =
                "Smoothed weights on " + changedVertexIndices.Count +
                " selected vertex(es) at " + touchupSelectionSmoothPercentage + "%." +
                (protectedVertexCount > 0
                    ? " Auto-mask protected " + protectedVertexCount + " vertex(es)."
                    : string.Empty) +
                " Use Save Weights to write the pending changes.";
            RecalculateTouchupVertexPositions(changedVertexIndices);
            RefreshTouchupWeightVisualization();
            RepaintLinkedEditors();
            SceneView.RepaintAll();
        }

        private List<BoneWeight1> SetTouchupBoneWeight(
            List<BoneWeight1> currentWeights,
            int selectedBoneIndex,
            float targetWeight)
        {
            targetWeight = Mathf.Clamp01(targetWeight);
            float otherWeightTotal = 0f;
            for (int i = 0; i < currentWeights.Count; i++)
            {
                if (currentWeights[i].boneIndex != selectedBoneIndex)
                {
                    otherWeightTotal += Mathf.Max(0f, currentWeights[i].weight);
                }
            }

            if (otherWeightTotal <= Mathf.Epsilon)
            {
                return new List<BoneWeight1>
                {
                    new BoneWeight1 { boneIndex = selectedBoneIndex, weight = 1f }
                };
            }

            List<BoneWeight1> result = new List<BoneWeight1>(currentWeights.Count + 1);
            float remainingWeight = 1f - targetWeight;
            for (int i = 0; i < currentWeights.Count; i++)
            {
                BoneWeight1 weight = currentWeights[i];
                if (weight.boneIndex == selectedBoneIndex)
                {
                    continue;
                }

                float scaledWeight = Mathf.Max(0f, weight.weight) / otherWeightTotal * remainingWeight;
                if (scaledWeight > BoneWeightMismatchTolerance)
                {
                    result.Add(new BoneWeight1
                    {
                        boneIndex = weight.boneIndex,
                        weight = scaledWeight
                    });
                }
            }
            if (targetWeight > BoneWeightMismatchTolerance)
            {
                result.Add(new BoneWeight1
                {
                    boneIndex = selectedBoneIndex,
                    weight = targetWeight
                });
            }
            result.Sort((left, right) => right.weight.CompareTo(left.weight));
            return result;
        }

        private static void SetWeightInMap(List<BoneWeight1> weights, int boneIndex, float value)
        {
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i].boneIndex != boneIndex)
                {
                    continue;
                }

                weights[i] = new BoneWeight1 { boneIndex = boneIndex, weight = value };
                return;
            }
            weights.Add(new BoneWeight1 { boneIndex = boneIndex, weight = value });
        }

        private List<BoneWeight1> NormalizeTouchupBoneWeightList(
            List<BoneWeight1> weights,
            List<BoneWeight1> fallback)
        {
            Dictionary<int, float> totals = new Dictionary<int, float>();
            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                float value = Mathf.Max(0f, weights[i].weight);
                if (value <= BoneWeightMismatchTolerance)
                {
                    continue;
                }

                totals.TryGetValue(weights[i].boneIndex, out float existing);
                totals[weights[i].boneIndex] = existing + value;
                total += value;
            }

            if (total <= Mathf.Epsilon)
            {
                return CloneBoneWeightList(fallback);
            }

            List<BoneWeight1> result = new List<BoneWeight1>(totals.Count);
            foreach (KeyValuePair<int, float> pair in totals)
            {
                result.Add(new BoneWeight1
                {
                    boneIndex = pair.Key,
                    weight = pair.Value / total
                });
            }
            result.Sort((left, right) => right.weight.CompareTo(left.weight));
            return result;
        }

        private static float GetWeightFromList(List<BoneWeight1> weights, int boneIndex)
        {
            if (weights == null)
            {
                return 0f;
            }

            float result = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i].boneIndex == boneIndex)
                {
                    result += weights[i].weight;
                }
            }
            return result;
        }

        private static List<BoneWeight1> CloneBoneWeightList(List<BoneWeight1> weights)
        {
            return weights != null ? new List<BoneWeight1>(weights) : new List<BoneWeight1>();
        }

        private static bool BoneWeightListsApproximatelyEqual(
            List<BoneWeight1> left,
            List<BoneWeight1> right)
        {
            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                BoneWeight1 weight = left[i];
                if (Mathf.Abs(weight.weight - GetWeightFromList(right, weight.boneIndex)) >
                    BoneWeightMismatchTolerance)
                {
                    return false;
                }
            }
            return true;
        }

        private void EndTouchupWeightPaintStroke(bool commit)
        {
            touchupSmearHasPreviousSample = false;
            touchupSmearPreviousCandidates.Clear();
            if (!touchupPaintStrokeActive)
            {
                touchupPaintStrokeBlocked = false;
                return;
            }

            touchupPaintStrokeActive = false;
            touchupPaintStrokeBlocked = false;
            if (touchupPaintStrokeWeights.Count == 0)
            {
                return;
            }

            List<int> strokeVertexIndices = new List<int>(touchupPaintStrokeWeights.Keys);
            if (commit)
            {
                Undo.RegisterCompleteObjectUndo(this, "Paint Vertex Weights");
                foreach (KeyValuePair<int, List<BoneWeight1>> paintedVertex in touchupPaintStrokeWeights)
                {
                    touchupPendingPaintWeights[paintedVertex.Key] =
                        CloneBoneWeightList(paintedVertex.Value);
                }
                SyncTouchupPendingPaintUndoState();
                touchupPaintStatusType = MessageType.Info;
                touchupPaintStatusMessage =
                    "Painted " + touchupPaintStrokeWeights.Count +
                    " vertex(es). Use Save Weights to write the pending changes.";
            }
            else
            {
                ResetTouchupPaintData();
                EnsureTouchupPaintData(out _);
                touchupPaintStatusType = MessageType.Info;
                touchupPaintStatusMessage = "Discarded the current weight-paint stroke.";
            }

            RecalculateTouchupVertexPositions(strokeVertexIndices);
            touchupPaintStrokeWeights.Clear();
            RepaintLinkedEditors();
        }

        private bool SelectionMatchesTouchupSlot(VertexSelection selection)
        {
            return selection != null && selection.slot != null && touchupWeightSlot != null &&
                   (ReferenceEquals(selection.slot, touchupWeightSlot) ||
                    string.Equals(selection.slot.slotName, touchupWeightSlot.slotName, StringComparison.Ordinal));
        }

        private void SyncTouchupPendingPaintUndoState()
        {
            if (touchupPendingPaintUndoState == null)
            {
                touchupPendingPaintUndoState = new List<TouchupUndoVertexWeights>();
            }
            touchupPendingPaintUndoState.Clear();

            List<int> vertexIndices = new List<int>(touchupPendingPaintWeights.Keys);
            vertexIndices.Sort();
            for (int vertexIndexIndex = 0; vertexIndexIndex < vertexIndices.Count; vertexIndexIndex++)
            {
                int vertexIndex = vertexIndices[vertexIndexIndex];
                TouchupUndoVertexWeights vertexState = new TouchupUndoVertexWeights
                {
                    vertexIndex = vertexIndex
                };
                List<BoneWeight1> weights = touchupPendingPaintWeights[vertexIndex];
                for (int weightIndex = 0; weightIndex < weights.Count; weightIndex++)
                {
                    BoneWeight1 weight = weights[weightIndex];
                    vertexState.weights.Add(new TouchupUndoBoneWeight
                    {
                        boneIndex = weight.boneIndex,
                        weight = weight.weight
                    });
                }
                touchupPendingPaintUndoState.Add(vertexState);
            }
            EditorUtility.SetDirty(this);
        }

        private void RestoreTouchupPendingPaintWeightsFromUndoState()
        {
            touchupPendingPaintWeights.Clear();
            if (touchupPendingPaintUndoState == null)
            {
                return;
            }

            for (int vertexStateIndex = 0;
                 vertexStateIndex < touchupPendingPaintUndoState.Count;
                 vertexStateIndex++)
            {
                TouchupUndoVertexWeights vertexState =
                    touchupPendingPaintUndoState[vertexStateIndex];
                if (vertexState == null || vertexState.vertexIndex < 0 ||
                    vertexState.weights == null)
                {
                    continue;
                }

                List<BoneWeight1> weights =
                    new List<BoneWeight1>(vertexState.weights.Count);
                for (int weightIndex = 0;
                     weightIndex < vertexState.weights.Count;
                     weightIndex++)
                {
                    TouchupUndoBoneWeight weight = vertexState.weights[weightIndex];
                    weights.Add(new BoneWeight1
                    {
                        boneIndex = weight.boneIndex,
                        weight = weight.weight
                    });
                }
                touchupPendingPaintWeights[vertexState.vertexIndex] = weights;
            }
        }

        private void InitializeTouchupWeights()
        {
            List<SlotData> slots = GetTouchupWeightSlots();
            if (slots.Count > 0)
            {
                SetTouchupWeightSlot(slots[0]);
            }
            else
            {
                RefreshTouchupWeightVisualization();
            }
        }

        private int FindDominantTouchupBoneHash(SlotData slot)
        {
            if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
            {
                return 0;
            }

            UMAMeshData meshData = slot.asset.meshData;
            Dictionary<int, float> totalsByBoneIndex = new Dictionary<int, float>();
            if (HasValidManagedBoneWeights(meshData))
            {
                for (int i = 0; i < meshData.ManagedBoneWeights.Length; i++)
                {
                    BoneWeight1 weight = meshData.ManagedBoneWeights[i];
                    totalsByBoneIndex.TryGetValue(weight.boneIndex, out float total);
                    totalsByBoneIndex[weight.boneIndex] = total + weight.weight;
                }
            }
            else if (HasValidLegacyBoneWeights(meshData))
            {
                for (int vertexIndex = 0; vertexIndex < meshData.vertexCount; vertexIndex++)
                {
                    if (!TryGetLegacyWeightsForVertex(meshData, vertexIndex, out List<BoneWeight1> weights))
                    {
                        continue;
                    }
                    for (int i = 0; i < weights.Count; i++)
                    {
                        BoneWeight1 weight = weights[i];
                        totalsByBoneIndex.TryGetValue(weight.boneIndex, out float total);
                        totalsByBoneIndex[weight.boneIndex] = total + weight.weight;
                    }
                }
            }

            int bestBoneIndex = -1;
            float bestTotal = float.MinValue;
            foreach (KeyValuePair<int, float> pair in totalsByBoneIndex)
            {
                if (pair.Value > bestTotal)
                {
                    bestBoneIndex = pair.Key;
                    bestTotal = pair.Value;
                }
            }
            int bestBoneHash = GetSlotBoneHash(meshData, bestBoneIndex);
            if (bestBoneHash != 0)
            {
                return bestBoneHash;
            }
            if (meshData.boneNameHashes != null)
            {
                for (int i = 0; i < meshData.boneNameHashes.Length; i++)
                {
                    if (meshData.boneNameHashes[i] != 0)
                    {
                        return meshData.boneNameHashes[i];
                    }
                }
            }
            return 0;
        }

        internal void RefreshTouchupWeightVisualization()
        {
            if (!touchupWeightsMode || BakedMesh == null)
            {
                return;
            }

            CaptureTouchupBaseColors();
            Color32[] colors = (Color32[])touchupBaseColors.Clone();

            if (touchupWeightSlot != null && touchupWeightSlot.asset != null &&
                !UMAMeshData.IsNullOrEmptyMeshData(touchupWeightSlot.asset.meshData) &&
                TryGetVisibleBakedVertexIndex(touchupWeightSlot, 0, out int bakedStart))
            {
                UMAMeshData meshData = touchupWeightSlot.asset.meshData;
                int boneIndex = GetBoundBoneIndex(meshData, touchupWeightBoneHash);
                int vertexCount = Mathf.Min(meshData.vertexCount, colors.Length - bakedStart);
                if (touchupWorkingWeights != null &&
                    touchupWorkingWeights.Length == meshData.vertexCount)
                {
                    for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                    {
                        colors[bakedStart + vertexIndex] = BlenderWeightColor(
                            GetWeightFromList(touchupWorkingWeights[vertexIndex], boneIndex));
                    }
                }
                else if (boneIndex >= 0 && HasValidManagedBoneWeights(meshData))
                {
                    int weightOffset = 0;
                    for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                    {
                        int weightCount = meshData.ManagedBonesPerVertex[vertexIndex];
                        float displayedWeight = 0f;
                        for (int weightIndex = 0; weightIndex < weightCount; weightIndex++)
                        {
                            BoneWeight1 weight = meshData.ManagedBoneWeights[weightOffset + weightIndex];
                            if (weight.boneIndex == boneIndex)
                            {
                                displayedWeight += weight.weight;
                            }
                        }
                        colors[bakedStart + vertexIndex] = BlenderWeightColor(displayedWeight);
                        weightOffset += weightCount;
                    }
                }
                else
                {
                    for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                    {
                        float displayedWeight = 0f;
                        if (boneIndex >= 0 &&
                            TryGetLegacyWeightsForVertex(meshData, vertexIndex, out List<BoneWeight1> weights))
                        {
                            for (int i = 0; i < weights.Count; i++)
                            {
                                if (weights[i].boneIndex == boneIndex)
                                {
                                    displayedWeight += weights[i].weight;
                                }
                            }
                        }
                        colors[bakedStart + vertexIndex] = BlenderWeightColor(displayedWeight);
                    }
                }

                if (touchupPreviewWeights != null && touchupPreviewVertexIndices.Count > 0)
                {
                    float total = 0f;
                    float selectedBoneWeight = 0f;
                    for (int i = 0; i < touchupPreviewWeights.Count; i++)
                    {
                        VertexWeightEntry entry = touchupPreviewWeights[i];
                        float value = Mathf.Clamp01(entry.weight);
                        total += value;
                        if (entry.boneHash == touchupWeightBoneHash)
                        {
                            selectedBoneWeight += value;
                        }
                    }
                    float displayedWeight = total > Mathf.Epsilon ? selectedBoneWeight / total : 0f;
                    Color32 previewColor = BlenderWeightColor(displayedWeight);
                    foreach (int vertexIndex in touchupPreviewVertexIndices)
                    {
                        if (vertexIndex >= 0 && vertexIndex < vertexCount)
                        {
                            colors[bakedStart + vertexIndex] = previewColor;
                        }
                    }
                }
            }

            touchupDisplayColors = colors;
            BakedMesh.colors32 = colors;
            ApplyVertexDisplayOptions();
            SceneView.RepaintAll();
        }

        private void UpdateTouchupWeightVisualization(List<int> vertexIndices)
        {
            if (BakedMesh == null || touchupWeightSlot == null || touchupWorkingWeights == null ||
                vertexIndices == null || vertexIndices.Count == 0)
            {
                return;
            }

            if (touchupDisplayColors == null || touchupDisplayColors.Length != BakedMesh.vertexCount)
            {
                RefreshTouchupWeightVisualization();
                return;
            }

            int boneIndex = GetBoundBoneIndex(touchupWeightSlot.asset.meshData, touchupWeightBoneHash);
            int bakedStart = touchupWeightSlot.vertexOffset;
            for (int i = 0; i < vertexIndices.Count; i++)
            {
                int vertexIndex = vertexIndices[i];
                int bakedIndex = bakedStart + vertexIndex;
                if (vertexIndex < 0 || vertexIndex >= touchupWorkingWeights.Length ||
                    bakedIndex < 0 || bakedIndex >= touchupDisplayColors.Length)
                {
                    continue;
                }

                touchupDisplayColors[bakedIndex] = BlenderWeightColor(
                    GetWeightFromList(touchupWorkingWeights[vertexIndex], boneIndex));
            }

            BakedMesh.colors32 = touchupDisplayColors;
            SceneView.RepaintAll();
        }

        private void OnTouchupLiveUpdate()
        {
            if (closing || !touchupWeightsMode || !touchupLiveUpdate)
            {
                return;
            }

            touchupLivePositionVertexIndices.Clear();
            touchupLivePositionVertexIndices.UnionWith(touchupSavedPositionVertexIndices);
            touchupLivePositionVertexIndices.UnionWith(touchupPendingPaintWeights.Keys);
            touchupLivePositionVertexIndices.UnionWith(touchupPaintStrokeWeights.Keys);
            touchupLivePositionVertexIndices.UnionWith(touchupPreviewVertexIndices);
            if (touchupLivePositionVertexIndices.Count == 0)
            {
                return;
            }

            RecalculateTouchupVertexPositions(touchupLivePositionVertexIndices);
            SceneView.RepaintAll();
        }

        private void RecalculateTouchupVertexPositions(IEnumerable<int> vertexIndices)
        {
            if (vertexIndices == null || BakedMesh == null || VertexObject == null ||
                touchupWeightSlot == null || touchupWeightSlot.asset == null ||
                !EnsureTouchupPaintData(out _))
            {
                return;
            }

            SkinnedMeshRenderer renderer = stageSkinnedMeshRenderer != null
                ? stageSkinnedMeshRenderer
                : GetCurrentSkinnedMeshRenderer();
            Mesh sourceMesh = renderer != null ? renderer.sharedMesh : null;
            UMAMeshData meshData = touchupWeightSlot.asset.meshData;
            if (renderer == null || sourceMesh == null || meshData == null ||
                thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.skeleton == null)
            {
                return;
            }

            int sourceMeshId = sourceMesh.GetInstanceID();
            if (touchupSkinningSourceVertices == null ||
                touchupSkinningSourceMeshId != sourceMeshId ||
                touchupSkinningSourceVertices.Length != sourceMesh.vertexCount)
            {
                touchupSkinningSourceVertices = sourceMesh.vertices;
                touchupSkinningSourceMeshId = sourceMeshId;
                touchupSkinningPositionOffsets.Clear();
            }

            Vector3[] previewVertices = BakedMesh.vertices;
            int bakedStart = touchupWeightSlot.vertexOffset;
            bool changed = false;
            foreach (int vertexIndex in vertexIndices)
            {
                int bakedIndex = bakedStart + vertexIndex;
                if (vertexIndex < 0 || vertexIndex >= meshData.vertexCount ||
                    vertexIndex >= touchupWorkingWeights.Length ||
                    vertexIndex >= touchupAssetWeights.Length ||
                    bakedIndex < 0 || bakedIndex >= previewVertices.Length ||
                    bakedIndex >= touchupSkinningSourceVertices.Length)
                {
                    continue;
                }

                Vector3 sourcePosition = touchupSkinningSourceVertices[bakedIndex];
                if (!touchupSkinningPositionOffsets.TryGetValue(bakedIndex, out Vector3 positionOffset))
                {
                    if (!TrySkinTouchupVertex(
                            renderer,
                            meshData,
                            sourcePosition,
                            touchupAssetWeights[vertexIndex],
                            out Vector3 originalPosition))
                    {
                        continue;
                    }
                    positionOffset = previewVertices[bakedIndex] - originalPosition;
                    touchupSkinningPositionOffsets.Add(bakedIndex, positionOffset);
                }

                bool hasPreviewWeights =
                    touchupPreviewWeights != null &&
                    touchupPreviewVertexIndices.Contains(vertexIndex);
                bool skinned = hasPreviewWeights
                    ? TrySkinTouchupVertex(
                        renderer,
                        meshData,
                        sourcePosition,
                        touchupPreviewWeights,
                        out Vector3 skinnedPosition)
                    : TrySkinTouchupVertex(
                        renderer,
                        meshData,
                        sourcePosition,
                        touchupWorkingWeights[vertexIndex],
                        out skinnedPosition);
                if (!skinned)
                {
                    continue;
                }

                previewVertices[bakedIndex] = skinnedPosition + positionOffset;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            BakedMesh.vertices = previewVertices;
            BakedMesh.RecalculateBounds();
            RefreshBakedMeshCaches();
            RefreshTouchupSelectionPositions();

            MeshCollider collider = VertexObject.GetComponent<MeshCollider>();
            if (collider != null)
            {
                collider.sharedMesh = null;
                collider.sharedMesh = BakedMesh;
            }
            SceneView.RepaintAll();
        }

        private bool TrySkinTouchupVertex(
            SkinnedMeshRenderer renderer,
            UMAMeshData meshData,
            Vector3 sourcePosition,
            List<BoneWeight1> weights,
            out Vector3 skinnedPosition)
        {
            skinnedPosition = Vector3.zero;
            if (weights == null || weights.Count == 0)
            {
                return false;
            }

            float totalWeight = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                BoneWeight1 weight = weights[i];
                float value = Mathf.Max(0f, weight.weight);
                if (value <= BoneWeightMismatchTolerance)
                {
                    continue;
                }
                if (!TryGetTouchupSkinMatrix(
                        renderer,
                        meshData,
                        weight.boneIndex,
                        GetSlotBoneHash(meshData, weight.boneIndex),
                        out Matrix4x4 skinMatrix))
                {
                    return false;
                }

                skinnedPosition += skinMatrix.MultiplyPoint3x4(sourcePosition) * value;
                totalWeight += value;
            }

            if (totalWeight <= Mathf.Epsilon)
            {
                return false;
            }
            skinnedPosition /= totalWeight;
            return true;
        }

        private bool TrySkinTouchupVertex(
            SkinnedMeshRenderer renderer,
            UMAMeshData meshData,
            Vector3 sourcePosition,
            List<VertexWeightEntry> weights,
            out Vector3 skinnedPosition)
        {
            skinnedPosition = Vector3.zero;
            if (weights == null || weights.Count == 0)
            {
                return false;
            }

            float totalWeight = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                VertexWeightEntry weight = weights[i];
                if (weight == null)
                {
                    continue;
                }

                float value = Mathf.Clamp01(weight.weight);
                int boneIndex = GetBoundBoneIndex(meshData, weight.boneHash);
                if (value <= BoneWeightMismatchTolerance)
                {
                    continue;
                }
                if (!TryGetTouchupSkinMatrix(
                        renderer,
                        meshData,
                        boneIndex,
                        weight.boneHash,
                        out Matrix4x4 skinMatrix))
                {
                    return false;
                }

                skinnedPosition += skinMatrix.MultiplyPoint3x4(sourcePosition) * value;
                totalWeight += value;
            }

            if (totalWeight <= Mathf.Epsilon)
            {
                return false;
            }
            skinnedPosition /= totalWeight;
            return true;
        }

        private bool TryGetTouchupSkinMatrix(
            SkinnedMeshRenderer renderer,
            UMAMeshData meshData,
            int boneIndex,
            int boneHash,
            out Matrix4x4 skinMatrix)
        {
            skinMatrix = Matrix4x4.identity;
            if (renderer == null || meshData == null ||
                thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.skeleton == null)
            {
                return false;
            }

            if (boneHash == 0)
            {
                boneHash = GetSlotBoneHash(meshData, boneIndex);
            }
            Transform boneTransform = boneHash != 0
                ? thisDCA.umaData.skeleton.GetBoneTransform(boneHash)
                : null;
            if (boneTransform == null)
            {
                return false;
            }

            Matrix4x4 bindPose =
                meshData.bindPoses != null &&
                boneIndex >= 0 &&
                boneIndex < meshData.bindPoses.Length
                    ? meshData.bindPoses[boneIndex]
                    : ResolveBindPoseForBone(boneHash, boneTransform);
            skinMatrix =
                renderer.transform.worldToLocalMatrix *
                boneTransform.localToWorldMatrix *
                bindPose;
            return true;
        }

        private void RefreshTouchupSelectionPositions()
        {
            if (SelectedVertexes == null || bakedVertices == null || VertexObject == null)
            {
                return;
            }

            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                VertexSelection selection = SelectedVertexes[i];
                if (!SelectionMatchesTouchupSlot(selection) ||
                    !TryGetVisibleBakedVertexIndex(
                        selection.slot,
                        selection.vertexIndexOnSlot,
                        out int bakedIndex) ||
                    bakedIndex < 0 ||
                    bakedIndex >= bakedVertices.Length)
                {
                    continue;
                }
                selection.WorldPosition =
                    VertexObject.transform.TransformPoint(bakedVertices[bakedIndex]);
            }
        }

        private void CaptureTouchupBaseColors()
        {
            if (touchupBaseColors != null && touchupBaseColors.Length == BakedMesh.vertexCount)
            {
                return;
            }

            Color32[] bakedColors = BakedMesh.colors32;
            touchupBaseColors = new Color32[BakedMesh.vertexCount];
            if (bakedColors != null && bakedColors.Length == touchupBaseColors.Length)
            {
                Array.Copy(bakedColors, touchupBaseColors, bakedColors.Length);
                return;
            }

            Color32 white = new Color32(255, 255, 255, 255);
            for (int i = 0; i < touchupBaseColors.Length; i++)
            {
                touchupBaseColors[i] = white;
            }
        }

        private static Color32 BlenderWeightColor(float weight)
        {
            float value = Mathf.Clamp01(weight);
            Color color;
            if (value <= 0.25f)
            {
                color = Color.Lerp(new Color(0f, 0f, 1f), new Color(0f, 1f, 1f), value * 4f);
            }
            else if (value <= 0.5f)
            {
                color = Color.Lerp(new Color(0f, 1f, 1f), new Color(0f, 1f, 0f), (value - 0.25f) * 4f);
            }
            else if (value <= 0.75f)
            {
                color = Color.Lerp(new Color(0f, 1f, 0f), new Color(1f, 1f, 0f), (value - 0.5f) * 4f);
            }
            else
            {
                color = Color.Lerp(new Color(1f, 1f, 0f), new Color(1f, 0f, 0f), (value - 0.75f) * 4f);
            }
            color.a = 1f;
            return color;
        }

        private static List<RaceData> GetCompatibleRacesForSlotWeightEditor(SlotDataAsset slotAsset)
        {
            List<RaceData> races = new List<RaceData>();
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null || slotAsset == null)
            {
                return races;
            }

            if (slotAsset.Races != null && slotAsset.Races.Length > 0)
            {
                for (int i = 0; i < slotAsset.Races.Length; i++)
                {
                    if (string.IsNullOrEmpty(slotAsset.Races[i]))
                    {
                        continue;
                    }
                    RaceData race = indexer.GetRace(slotAsset.Races[i]);
                    if (race != null && RaceBaseRecipeContainsSlot(race, slotAsset))
                    {
                        AddUniqueRace(races, race);
                    }
                }

                if (races.Count > 0)
                {
                    return races;
                }
            }

            List<RaceData> allRaces = GetAllPreviewRacesForSlotWeightEditor();
            for (int i = 0; i < allRaces.Count; i++)
            {
                RaceData race = allRaces[i];

                if (RaceBaseRecipeContainsSlot(race, slotAsset))
                {
                    AddUniqueRace(races, race);
                }
            }

            return races;
        }

        private static List<RaceData> GetAllPreviewRacesForSlotWeightEditor()
        {
            List<RaceData> races = new List<RaceData>();
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null)
            {
                return races;
            }

            RaceData[] allRaces = indexer.GetAllRaces();
            if (allRaces == null)
            {
                return races;
            }

            for (int i = 0; i < allRaces.Length; i++)
            {
                RaceData race = allRaces[i];
                if (race == null || string.Equals(race.raceName, "RaceDataPlaceholder", StringComparison.Ordinal))
                {
                    continue;
                }

                AddUniqueRace(races, race);
            }

            return races;
        }

        private static void AddUniqueRace(List<RaceData> races, RaceData race)
        {
            if (race == null)
            {
                return;
            }

            for (int i = 0; i < races.Count; i++)
            {
                if (races[i] == race || string.Equals(races[i].raceName, race.raceName, StringComparison.Ordinal))
                {
                    return;
                }
            }

            races.Add(race);
        }

        private static bool RaceBaseRecipeContainsSlot(RaceData race, SlotDataAsset slotAsset)
        {
            if (race == null || race.baseRaceRecipe == null || slotAsset == null)
            {
                return false;
            }

            try
            {
                UMAData.UMARecipe recipe = new UMAData.UMARecipe();
                race.baseRaceRecipe.Load(recipe);
                if (recipe.slotDataList == null)
                {
                    return false;
                }

                for (int i = 0; i < recipe.slotDataList.Length; i++)
                {
                    if (SlotMatchesAssetSource(recipe.slotDataList[i], slotAsset))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryCreateSlotWeightPreviewAvatar(SlotDataAsset slotAsset, RaceData race, out DynamicCharacterAvatar previewAvatar, out string errorMessage)
        {
            previewAvatar = null;
            errorMessage = string.Empty;

            GameObject previewObject = new GameObject("UMA Slot Weight Preview - " + slotAsset.name);
            previewObject.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                previewAvatar = previewObject.AddComponent<DynamicCharacterAvatar>();
                previewAvatar.editorTimeGeneration = true;
                previewAvatar.ignoreMeshHideAssets = true;
                previewAvatar.activeRace.name = race.raceName;
                previewAvatar.activeRace.data = race;
                previewAvatar.GenerateNow();

                if (!InstallSlotWeightEditorSlot(previewAvatar, slotAsset, out errorMessage))
                {
                    DestroyImmediate(previewObject);
                    previewAvatar = null;
                    return false;
                }

                RegeneratePreviewAvatar(previewAvatar);
                SkinnedMeshRenderer renderer = previewAvatar.gameObject.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (renderer == null || renderer.sharedMesh == null)
                {
                    DestroyImmediate(previewObject);
                    previewAvatar = null;
                    errorMessage = "The preview avatar did not generate a SkinnedMeshRenderer for the selected slot.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                DestroyImmediate(previewObject);
                previewAvatar = null;
                errorMessage = "Unable to create the temporary slot weight preview avatar.\n" + ex.Message;
                return false;
            }
        }

        private static bool InstallSlotWeightEditorSlot(DynamicCharacterAvatar previewAvatar, SlotDataAsset slotAsset, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (previewAvatar == null || previewAvatar.umaData == null || previewAvatar.umaData.umaRecipe == null || previewAvatar.umaData.umaRecipe.slotDataList == null)
            {
                errorMessage = "The preview avatar did not build a usable UMA recipe.";
                return false;
            }

            SlotData targetSlot = null;
            SlotData[] slots = previewAvatar.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                if (SlotMatchesAssetSource(slots[i], slotAsset))
                {
                    targetSlot = slots[i];
                    break;
                }
            }

            if (targetSlot == null)
            {
                targetSlot = new SlotData(slotAsset);
                SlotData[] expandedSlots = new SlotData[slots.Length + 1];
                Array.Copy(slots, expandedSlots, slots.Length);
                expandedSlots[expandedSlots.Length - 1] = targetSlot;
                previewAvatar.umaData.umaRecipe.slotDataList = expandedSlots;
                slots = expandedSlots;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                {
                    slots[i].Suppressed = !ReferenceEquals(slots[i], targetSlot);
                }
            }

            targetSlot.asset = slotAsset;
            targetSlot.UpdateFromAsset(slotAsset);
            if (targetSlot.OverlayCount == 0)
            {
                UMAGenerator previewGenerator = previewAvatar.umaData.umaGenerator;
                OverlayData defaultOverlay = previewGenerator != null
                    ? previewGenerator.defaultOverlaydata
                    : null;
                if (defaultOverlay != null)
                {
                    targetSlot.AddOverlay(defaultOverlay.Duplicate());
                }
                else if (previewGenerator != null && previewGenerator.defaultOverlayAsset != null)
                {
                    // The generator can have a configured asset before Awake creates
                    // its transient OverlayData instance in an editor preview.
                    targetSlot.AddOverlay(new OverlayData(previewGenerator.defaultOverlayAsset));
                }
                else
                {
                    errorMessage = "The selected generator has no default overlay for rendering the temporary slot preview.";
                    return false;
                }
            }
            targetSlot.Suppressed = false;
            return true;
        }

        private static void RegeneratePreviewAvatar(DynamicCharacterAvatar previewAvatar)
        {
            if (previewAvatar == null || previewAvatar.umaData == null || previewAvatar.umaData.umaGenerator == null)
            {
                return;
            }

            previewAvatar.umaData.Dirty(true, true, true);
            previewAvatar.umaData.umaGenerator.GenerateSingleUMA(previewAvatar.umaData, true);
            previewAvatar.umaData.umaGenerator.Clear();
        }

        private static bool SlotMatchesAssetSource(SlotData recipeSlot, SlotDataAsset slotAsset)
        {
            if (recipeSlot == null || slotAsset == null)
            {
                return false;
            }

            string targetSource = GetSlotSourceKey(slotAsset);
            if (recipeSlot.asset != null)
            {
                string recipeSource = GetSlotSourceKey(recipeSlot.asset);
                if (StringEqualsSlotKey(recipeSource, targetSource) || StringEqualsSlotKey(recipeSlot.asset.slotName, targetSource) || StringEqualsSlotKey(recipeSource, slotAsset.slotName))
                {
                    return true;
                }
            }

            return StringEqualsSlotKey(recipeSlot.slotName, targetSource) || StringEqualsSlotKey(recipeSlot.slotName, slotAsset.slotName);
        }

        private static string GetSlotSourceKey(SlotDataAsset slotAsset)
        {
            if (slotAsset == null)
            {
                return string.Empty;
            }

            return string.IsNullOrEmpty(slotAsset.sourceSlot) ? slotAsset.slotName : slotAsset.sourceSlot;
        }

        private static bool StringEqualsSlotKey(string left, string right)
        {
            return !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        public VertexSelection GetSelectedVertex()
        {
            if (currentSelected >= 0 && currentSelected < SelectedVertexes.Count)
            {
                return SelectedVertexes[currentSelected];
            }
            return null;
        }

        private const float BoneWeightMismatchTolerance = 0.0001f;

        internal class VertexWeightEntry
        {
            public int boneIndex;
            public int boneHash;
            public string boneName;
            public float weight;

            public VertexWeightEntry Clone()
            {
                return new VertexWeightEntry()
                {
                    boneIndex = boneIndex,
                    boneHash = boneHash,
                    boneName = boneName,
                    weight = weight
                };
            }
        }

        internal class BoneOption
        {
            public int boneIndex;
            public int boneHash;
            public string boneName;
            public string displayName;
            public bool isBound;
        }

        internal class VertexWeightComparison
        {
            public int boneHash;
            public string boneName;
            public float slotWeight;
            public float skinnedWeight;
            public bool mismatch;
        }

        internal VertexSelection GetVertexForWeightPopup()
        {
            VertexSelection selectedVertex = GetSelectedVertex();
            if (selectedVertex != null)
            {
                return selectedVertex;
            }

            if (SelectedVertexes != null && SelectedVertexes.Count > 0)
            {
                return SelectedVertexes[0];
            }

            return null;
        }

        private void ShowCurrentVertexWeightsPopup()
        {
            VertexSelection selectedVertex = GetVertexForWeightPopup();
            if (selectedVertex == null)
            {
                EditorUtility.DisplayDialog("Vertex Weights", "No current or selected vertex is available.", "OK");
                return;
            }

            VertexWeightEditorWindow.Open(this, selectedVertex);
        }

        internal List<VertexWeightEntry> GetSlotAssetVertexWeights(VertexSelection selectedVertex, out string statusMessage)
        {
            List<VertexWeightEntry> weights = new List<VertexWeightEntry>();
            statusMessage = string.Empty;

            if (!TryGetSelectionMeshData(selectedVertex, out UMAMeshData meshData, out statusMessage))
            {
                return weights;
            }

            int vertexIndex = selectedVertex.vertexIndexOnSlot;
            if (touchupWeightsMode && SelectionMatchesTouchupSlot(selectedVertex) &&
                EnsureTouchupPaintData(out _) &&
                touchupWorkingWeights != null &&
                vertexIndex >= 0 &&
                vertexIndex < touchupWorkingWeights.Length)
            {
                List<BoneWeight1> workingWeights = touchupWorkingWeights[vertexIndex];
                for (int i = 0; i < workingWeights.Count; i++)
                {
                    BoneWeight1 weight = workingWeights[i];
                    weights.Add(
                        CreateSlotWeightEntry(
                            meshData,
                            weight.boneIndex,
                            weight.weight));
                }
                statusMessage = touchupPendingPaintWeights.ContainsKey(vertexIndex)
                    ? "Using staged weight changes for this vertex."
                    : string.Empty;
                return weights;
            }

            if (TryGetManagedWeightsForVertex(meshData, vertexIndex, out List<BoneWeight1> managedWeights))
            {
                for (int i = 0; i < managedWeights.Count; i++)
                {
                    BoneWeight1 weight = managedWeights[i];
                    weights.Add(CreateSlotWeightEntry(meshData, weight.boneIndex, weight.weight));
                }
                statusMessage = weights.Count == 0 ? "SlotDataAsset has no weights for this vertex." : string.Empty;
                return weights;
            }

            if (TryGetLegacyWeightsForVertex(meshData, vertexIndex, out List<BoneWeight1> legacyWeights))
            {
                for (int i = 0; i < legacyWeights.Count; i++)
                {
                    BoneWeight1 weight = legacyWeights[i];
                    weights.Add(CreateSlotWeightEntry(meshData, weight.boneIndex, weight.weight));
                }
                statusMessage = weights.Count == 0 ? "SlotDataAsset has no legacy weights for this vertex." : "Using legacy SlotDataAsset weights.";
                return weights;
            }

            statusMessage = "SlotDataAsset has no usable weight data for this vertex.";
            return weights;
        }

        internal List<VertexWeightEntry> GetSkinnedMeshVertexWeights(VertexSelection selectedVertex, out string statusMessage)
        {
            List<VertexWeightEntry> weights = new List<VertexWeightEntry>();
            statusMessage = string.Empty;

            if (selectedVertex == null || selectedVertex.slot == null)
            {
                statusMessage = "No vertex is selected.";
                return weights;
            }

            if (!TryGetVisibleBakedVertexIndex(selectedVertex.slot, selectedVertex.vertexIndexOnSlot, out int skinnedVertexIndex))
            {
                statusMessage = "The selected vertex is not visible in the current generated mesh.";
                return weights;
            }

            SkinnedMeshRenderer renderer = GetCurrentSkinnedMeshRenderer();
            if (renderer == null || renderer.sharedMesh == null)
            {
                statusMessage = "No generated SkinnedMeshRenderer mesh is available.";
                return weights;
            }

            Mesh skinnedMesh = renderer.sharedMesh;
            var bonesPerVertex = skinnedMesh.GetBonesPerVertex();
            var allWeights = skinnedMesh.GetAllBoneWeights();
            try
            {
                if (bonesPerVertex.Length <= skinnedVertexIndex)
                {
                    statusMessage = "The generated SkinnedMesh vertex index is outside the bone weight data.";
                    return weights;
                }

                int weightOffset = 0;
                for (int i = 0; i < skinnedVertexIndex; i++)
                {
                    weightOffset += bonesPerVertex[i];
                }

                int weightCount = bonesPerVertex[skinnedVertexIndex];
                if (weightOffset + weightCount > allWeights.Length)
                {
                    statusMessage = "Generated SkinnedMesh bone weight data is not valid for this vertex.";
                    return weights;
                }

                for (int i = 0; i < weightCount; i++)
                {
                    BoneWeight1 boneWeight = allWeights[weightOffset + i];
                    weights.Add(CreateSkinnedWeightEntry(renderer, boneWeight.boneIndex, boneWeight.weight));
                }
            }
            finally
            {
                if (bonesPerVertex.IsCreated)
                {
                    bonesPerVertex.Dispose();
                }
                if (allWeights.IsCreated)
                {
                    allWeights.Dispose();
                }
            }

            statusMessage = weights.Count == 0 ? "Generated SkinnedMesh has no weights for this vertex." : string.Empty;
            return weights;
        }

        internal bool TryApplySlotAssetVertexWeights(VertexSelection selectedVertex, List<VertexWeightEntry> editedWeights, out string statusMessage)
        {
            statusMessage = string.Empty;
            if (!TryGetSelectionMeshData(selectedVertex, out UMAMeshData meshData, out statusMessage))
            {
                return false;
            }

            Undo.RecordObject(selectedVertex.slot.asset, "Edit Vertex Weights");

            if (!EnsureEditedWeightBonesAreBound(meshData, editedWeights, out statusMessage))
            {
                return false;
            }

            List<BoneWeight1> targetWeights = BuildTargetBoneWeights(meshData, editedWeights, out statusMessage);
            if (targetWeights == null)
            {
                return false;
            }

            bool hasManagedWeights = HasValidManagedBoneWeights(meshData);
            bool hasLegacyWeights = HasValidLegacyBoneWeights(meshData);
            if (!hasManagedWeights && !hasLegacyWeights)
            {
                statusMessage = "Cannot rewrite the SlotDataAsset because the existing mesh data has no valid managed or legacy weights to preserve for the other vertices.";
                return false;
            }

            byte[] newBonesPerVertex = new byte[meshData.vertexCount];
            List<BoneWeight1> newBoneWeights = new List<BoneWeight1>(meshData.ManagedBoneWeights != null ? meshData.ManagedBoneWeights.Length : meshData.vertexCount * 4);
            int managedOffset = 0;
            for (int vertexIndex = 0; vertexIndex < meshData.vertexCount; vertexIndex++)
            {
                List<BoneWeight1> vertexWeights;
                if (vertexIndex == selectedVertex.vertexIndexOnSlot)
                {
                    vertexWeights = targetWeights;
                }
                else if (hasManagedWeights)
                {
                    int count = meshData.ManagedBonesPerVertex[vertexIndex];
                    vertexWeights = new List<BoneWeight1>(count);
                    for (int weightIndex = 0; weightIndex < count; weightIndex++)
                    {
                        vertexWeights.Add(meshData.ManagedBoneWeights[managedOffset + weightIndex]);
                    }
                }
                else
                {
                    TryGetLegacyWeightsForVertex(meshData, vertexIndex, out vertexWeights);
                }

                if (hasManagedWeights)
                {
                    managedOffset += meshData.ManagedBonesPerVertex[vertexIndex];
                }

                if (vertexWeights.Count > byte.MaxValue)
                {
                    statusMessage = "A vertex cannot store more than 255 bone weights.";
                    return false;
                }

                newBonesPerVertex[vertexIndex] = (byte)vertexWeights.Count;
                newBoneWeights.AddRange(vertexWeights);
            }

            meshData.ManagedBonesPerVertex = newBonesPerVertex;
            meshData.ManagedBoneWeights = newBoneWeights.ToArray();
            UpdateLegacyVertexWeights(meshData, selectedVertex.vertexIndexOnSlot, targetWeights);
            meshData.LoadedBoneweights = false;

            EditorUtility.SetDirty(selectedVertex.slot.asset);
            AssetDatabase.SaveAssets();
            statusMessage = "SlotDataAsset weights updated.";
            return true;
        }

        private bool EnsureEditedWeightBonesAreBound(UMAMeshData meshData, List<VertexWeightEntry> editedWeights, out string statusMessage)
        {
            statusMessage = string.Empty;
            if (meshData == null || editedWeights == null)
            {
                statusMessage = "No editable bone weights are available.";
                return false;
            }

            for (int i = 0; i < editedWeights.Count; i++)
            {
                VertexWeightEntry weight = editedWeights[i];
                if (weight == null)
                {
                    continue;
                }
                if (Mathf.Clamp01(weight.weight) <= 0f)
                {
                    continue;
                }

                int existingIndex = GetBoundBoneIndex(meshData, weight.boneHash);
                if (existingIndex >= 0)
                {
                    weight.boneIndex = existingIndex;
                    continue;
                }

                if (weight.boneHash == 0)
                {
                    statusMessage = "Weight references a bone without a usable UMA hash.";
                    return false;
                }

                if (!AppendSlotBoneBinding(meshData, weight.boneHash, out int newBoneIndex, out statusMessage))
                {
                    return false;
                }

                weight.boneIndex = newBoneIndex;
            }

            return true;
        }

        private int GetBoundBoneIndex(UMAMeshData meshData, int boneHash)
        {
            if (meshData == null || meshData.boneNameHashes == null || boneHash == 0)
            {
                return -1;
            }

            for (int i = 0; i < meshData.boneNameHashes.Length; i++)
            {
                if (meshData.boneNameHashes[i] == boneHash)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool AppendSlotBoneBinding(UMAMeshData meshData, int boneHash, out int boneIndex, out string statusMessage)
        {
            boneIndex = -1;
            statusMessage = string.Empty;
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.skeleton == null)
            {
                statusMessage = "No preview skeleton is available for the new bone binding.";
                return false;
            }

            Transform boneTransform = thisDCA.umaData.skeleton.GetBoneTransform(boneHash);
            if (boneTransform == null)
            {
                statusMessage = "The selected bone is not present in the preview skeleton.";
                return false;
            }

            int oldCount = meshData.boneNameHashes != null ? meshData.boneNameHashes.Length : 0;
            boneIndex = oldCount;

            int[] newHashes = new int[oldCount + 1];
            if (meshData.boneNameHashes != null)
            {
                Array.Copy(meshData.boneNameHashes, newHashes, meshData.boneNameHashes.Length);
            }
            newHashes[oldCount] = boneHash;
            meshData.boneNameHashes = newHashes;

            Matrix4x4[] newBindPoses = new Matrix4x4[oldCount + 1];
            if (meshData.bindPoses != null)
            {
                Array.Copy(meshData.bindPoses, newBindPoses, Mathf.Min(meshData.bindPoses.Length, oldCount));
            }
            newBindPoses[oldCount] = ResolveBindPoseForBone(boneHash, boneTransform);
            meshData.bindPoses = newBindPoses;

            UMATransform[] newUmaBones = new UMATransform[oldCount + 1];
            if (meshData.umaBones != null)
            {
                Array.Copy(meshData.umaBones, newUmaBones, Mathf.Min(meshData.umaBones.Length, oldCount));
            }

            int parentHash = boneTransform.parent != null ? UMAUtils.StringToHash(boneTransform.parent.name) : 0;
            newUmaBones[oldCount] = new UMATransform(boneTransform, boneHash, parentHash);
            meshData.umaBones = newUmaBones;
            meshData.umaBoneCount = newUmaBones.Length;
            return true;
        }

        private Matrix4x4 ResolveBindPoseForBone(int boneHash, Transform boneTransform)
        {
            SkinnedMeshRenderer renderer = GetCurrentSkinnedMeshRenderer();
            if (renderer != null && renderer.bones != null && renderer.sharedMesh != null && renderer.sharedMesh.bindposes != null)
            {
                Matrix4x4[] bindPoses = renderer.sharedMesh.bindposes;
                for (int i = 0; i < renderer.bones.Length && i < bindPoses.Length; i++)
                {
                    Transform rendererBone = renderer.bones[i];
                    if (rendererBone != null && UMAUtils.StringToHash(rendererBone.name) == boneHash)
                    {
                        return bindPoses[i];
                    }
                }
            }

            Transform rootTransform = renderer != null && renderer.rootBone != null ? renderer.rootBone : null;
            if (rootTransform == null && thisDCA != null && thisDCA.umaData != null)
            {
                rootTransform = thisDCA.umaData.GetGlobalTransform();
            }

            if (boneTransform != null && rootTransform != null)
            {
                return boneTransform.worldToLocalMatrix * rootTransform.localToWorldMatrix;
            }

            return Matrix4x4.identity;
        }

        private void SmoothSelectedVertexWeights(float smoothAmount)
        {
            if (TrySmoothSelectedVertexWeights(smoothAmount, out string statusMessage))
            {
                RebuildMesh(true);
            }

            EditorUtility.DisplayDialog("Smooth Vertex Weights", statusMessage, "OK");
        }

        private bool TrySmoothSelectedVertexWeights(float smoothAmount, out string statusMessage)
        {
            statusMessage = string.Empty;
            smoothAmount = Mathf.Clamp01(smoothAmount);
            if (smoothAmount <= 0f)
            {
                statusMessage = "Smooth amount is 0. No weights were changed.";
                return false;
            }

            if (SelectedVertexes == null || SelectedVertexes.Count == 0)
            {
                statusMessage = "No selected vertices are available to smooth.";
                return false;
            }

            Dictionary<SlotData, HashSet<int>> selectedVertexIndicesBySlot = GetSelectedVertexIndicesBySlot(out int skippedSelections);
            if (selectedVertexIndicesBySlot.Count == 0)
            {
                statusMessage = "No valid selected vertices are available to smooth.";
                return false;
            }

            int updatedSlotCount = 0;
            int updatedVertexCount = 0;
            int skippedVertexCount = skippedSelections;
            List<string> errors = new List<string>();

            foreach (KeyValuePair<SlotData, HashSet<int>> slotSelection in selectedVertexIndicesBySlot)
            {
                SlotData slot = slotSelection.Key;
                if (!TryGetSlotMeshData(slot, out UMAMeshData meshData, out string meshStatusMessage))
                {
                    errors.Add(slot != null ? slot.slotName + ": " + meshStatusMessage : meshStatusMessage);
                    skippedVertexCount += slotSelection.Value.Count;
                    continue;
                }

                Dictionary<int, HashSet<int>> connectedVerticesBySelection = BuildConnectedVertexLookup(meshData, slotSelection.Value);
                Dictionary<int, List<BoneWeight1>> smoothedWeightsByVertex = new Dictionary<int, List<BoneWeight1>>();
                foreach (int vertexIndex in slotSelection.Value)
                {
                    if (!connectedVerticesBySelection.TryGetValue(vertexIndex, out HashSet<int> connectedVertices) || connectedVertices.Count == 0)
                    {
                        skippedVertexCount++;
                        continue;
                    }

                    if (!TryGetVertexBoneWeights(meshData, vertexIndex, out List<BoneWeight1> currentWeights))
                    {
                        skippedVertexCount++;
                        continue;
                    }

                    Dictionary<int, float> connectedWeightTotals = new Dictionary<int, float>();
                    int connectedWeightVertexCount = 0;
                    foreach (int connectedVertexIndex in connectedVertices)
                    {
                        if (connectedVertexIndex < 0 || connectedVertexIndex >= meshData.vertexCount)
                        {
                            continue;
                        }

                        if (!TryGetVertexBoneWeights(meshData, connectedVertexIndex, out List<BoneWeight1> connectedWeights))
                        {
                            continue;
                        }

                        AddWeightsToWeightMap(connectedWeightTotals, connectedWeights, 1f);
                        connectedWeightVertexCount++;
                    }

                    if (connectedWeightVertexCount == 0)
                    {
                        skippedVertexCount++;
                        continue;
                    }

                    Dictionary<int, float> currentWeightMap = BuildWeightMap(currentWeights);
                    Dictionary<int, float> connectedAverageWeightMap = DivideWeightMap(connectedWeightTotals, connectedWeightVertexCount);
                    List<BoneWeight1> smoothedWeights = BuildSmoothedBoneWeights(meshData, currentWeightMap, connectedAverageWeightMap, smoothAmount, out string smoothStatusMessage);
                    if (smoothedWeights == null)
                    {
                        errors.Add(slot.slotName + " vertex " + vertexIndex + ": " + smoothStatusMessage);
                        skippedVertexCount++;
                        continue;
                    }

                    smoothedWeightsByVertex.Add(vertexIndex, smoothedWeights);
                }

                if (smoothedWeightsByVertex.Count == 0)
                {
                    continue;
                }

                if (!TryRewriteSlotAssetVertexWeights(slot, smoothedWeightsByVertex, "Smooth Vertex Weights", out string rewriteStatusMessage))
                {
                    errors.Add(slot.slotName + ": " + rewriteStatusMessage);
                    skippedVertexCount += smoothedWeightsByVertex.Count;
                    continue;
                }

                updatedSlotCount++;
                updatedVertexCount += smoothedWeightsByVertex.Count;
            }

            if (updatedVertexCount > 0)
            {
                AssetDatabase.SaveAssets();
                statusMessage = "Smoothed weights for " + updatedVertexCount + " selected vertex(es) across " + updatedSlotCount + " slot(s).";
                if (skippedVertexCount > 0)
                {
                    statusMessage += "\nSkipped " + skippedVertexCount + " selected vertex(es) with no usable connected weights.";
                }
                if (errors.Count > 0)
                {
                    statusMessage += "\n" + string.Join("\n", errors);
                }
                return true;
            }

            statusMessage = "No selected vertex weights were smoothed.";
            if (skippedVertexCount > 0)
            {
                statusMessage += "\nSkipped " + skippedVertexCount + " selected vertex(es) with no usable connected weights.";
            }
            if (errors.Count > 0)
            {
                statusMessage += "\n" + string.Join("\n", errors);
            }
            return false;
        }

        private Dictionary<SlotData, HashSet<int>> GetSelectedVertexIndicesBySlot(out int skippedSelections)
        {
            skippedSelections = 0;
            Dictionary<SlotData, HashSet<int>> selectedVertexIndicesBySlot = new Dictionary<SlotData, HashSet<int>>();
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                VertexSelection selectedVertex = SelectedVertexes[i];
                if (selectedVertex == null || selectedVertex.slot == null || selectedVertex.suppressed)
                {
                    skippedSelections++;
                    continue;
                }

                if (!TryGetSlotMeshData(selectedVertex.slot, out UMAMeshData meshData, out _)
                    || selectedVertex.vertexIndexOnSlot < 0
                    || selectedVertex.vertexIndexOnSlot >= meshData.vertexCount)
                {
                    skippedSelections++;
                    continue;
                }

                if (!selectedVertexIndicesBySlot.TryGetValue(selectedVertex.slot, out HashSet<int> vertexIndices))
                {
                    vertexIndices = new HashSet<int>();
                    selectedVertexIndicesBySlot.Add(selectedVertex.slot, vertexIndices);
                }
                vertexIndices.Add(selectedVertex.vertexIndexOnSlot);
            }
            return selectedVertexIndicesBySlot;
        }

        private Dictionary<int, HashSet<int>> BuildConnectedVertexLookup(UMAMeshData meshData, HashSet<int> selectedVertexIndices)
        {
            Dictionary<int, HashSet<int>> connectedVerticesBySelection = new Dictionary<int, HashSet<int>>();
            foreach (int vertexIndex in selectedVertexIndices)
            {
                connectedVerticesBySelection.Add(vertexIndex, new HashSet<int>());
            }

            if (meshData == null || meshData.submeshes == null)
            {
                return connectedVerticesBySelection;
            }

            for (int submeshIndex = 0; submeshIndex < meshData.submeshes.Length; submeshIndex++)
            {
                SubMeshTriangles submesh = meshData.submeshes[submeshIndex];
                if (submesh == null)
                {
                    continue;
                }

                int[] triangles = submesh.getManagedTriangles(0);
                if (triangles == null)
                {
                    continue;
                }

                for (int triangleIndex = 0; triangleIndex + 2 < triangles.Length; triangleIndex += 3)
                {
                    int vertex0 = triangles[triangleIndex];
                    int vertex1 = triangles[triangleIndex + 1];
                    int vertex2 = triangles[triangleIndex + 2];
                    AddConnectedTriangleVertices(connectedVerticesBySelection, selectedVertexIndices, vertex0, vertex1, vertex2, meshData.vertexCount);
                }
            }

            return connectedVerticesBySelection;
        }

        private void AddConnectedTriangleVertices(Dictionary<int, HashSet<int>> connectedVerticesBySelection, HashSet<int> selectedVertexIndices, int vertex0, int vertex1, int vertex2, int vertexCount)
        {
            AddConnectedVertices(connectedVerticesBySelection, selectedVertexIndices, vertex0, vertex1, vertex2, vertexCount);
            AddConnectedVertices(connectedVerticesBySelection, selectedVertexIndices, vertex1, vertex0, vertex2, vertexCount);
            AddConnectedVertices(connectedVerticesBySelection, selectedVertexIndices, vertex2, vertex0, vertex1, vertexCount);
        }

        private void AddConnectedVertices(Dictionary<int, HashSet<int>> connectedVerticesBySelection, HashSet<int> selectedVertexIndices, int selectedCandidate, int connectedVertex0, int connectedVertex1, int vertexCount)
        {
            if (!selectedVertexIndices.Contains(selectedCandidate) || !connectedVerticesBySelection.TryGetValue(selectedCandidate, out HashSet<int> connectedVertices))
            {
                return;
            }

            if (connectedVertex0 >= 0 && connectedVertex0 < vertexCount && connectedVertex0 != selectedCandidate)
            {
                connectedVertices.Add(connectedVertex0);
            }
            if (connectedVertex1 >= 0 && connectedVertex1 < vertexCount && connectedVertex1 != selectedCandidate)
            {
                connectedVertices.Add(connectedVertex1);
            }
        }

        private Dictionary<int, float> BuildWeightMap(List<BoneWeight1> weights)
        {
            Dictionary<int, float> weightMap = new Dictionary<int, float>();
            AddWeightsToWeightMap(weightMap, weights, 1f);
            return weightMap;
        }

        private void AddWeightsToWeightMap(Dictionary<int, float> weightMap, List<BoneWeight1> weights, float multiplier)
        {
            for (int i = 0; i < weights.Count; i++)
            {
                BoneWeight1 weight = weights[i];
                float weightedValue = weight.weight * multiplier;
                if (weightMap.ContainsKey(weight.boneIndex))
                {
                    weightMap[weight.boneIndex] += weightedValue;
                }
                else
                {
                    weightMap.Add(weight.boneIndex, weightedValue);
                }
            }
        }

        private Dictionary<int, float> DivideWeightMap(Dictionary<int, float> weightMap, int divisor)
        {
            Dictionary<int, float> dividedWeightMap = new Dictionary<int, float>();
            if (divisor <= 0)
            {
                return dividedWeightMap;
            }

            foreach (KeyValuePair<int, float> weight in weightMap)
            {
                dividedWeightMap.Add(weight.Key, weight.Value / divisor);
            }
            return dividedWeightMap;
        }

        private List<BoneWeight1> BuildSmoothedBoneWeights(UMAMeshData meshData, Dictionary<int, float> currentWeightMap, Dictionary<int, float> connectedAverageWeightMap, float smoothAmount, out string statusMessage)
        {
            statusMessage = string.Empty;
            Dictionary<int, float> smoothedWeightMap = new Dictionary<int, float>();
            foreach (KeyValuePair<int, float> currentWeight in currentWeightMap)
            {
                connectedAverageWeightMap.TryGetValue(currentWeight.Key, out float connectedAverageWeight);
                smoothedWeightMap.Add(currentWeight.Key, Mathf.Lerp(currentWeight.Value, connectedAverageWeight, smoothAmount));
            }

            foreach (KeyValuePair<int, float> connectedAverageWeight in connectedAverageWeightMap)
            {
                if (smoothedWeightMap.ContainsKey(connectedAverageWeight.Key))
                {
                    continue;
                }
                smoothedWeightMap.Add(connectedAverageWeight.Key, Mathf.Lerp(0f, connectedAverageWeight.Value, smoothAmount));
            }

            List<BoneWeight1> smoothedWeights = new List<BoneWeight1>(smoothedWeightMap.Count);
            foreach (KeyValuePair<int, float> smoothedWeight in smoothedWeightMap)
            {
                smoothedWeights.Add(new BoneWeight1()
                {
                    boneIndex = smoothedWeight.Key,
                    weight = smoothedWeight.Value
                });
            }
            return BuildTargetBoneWeightsFromBoneWeights(meshData, smoothedWeights, out statusMessage);
        }

        private bool TryRewriteSlotAssetVertexWeights(SlotData slot, Dictionary<int, List<BoneWeight1>> targetWeightsByVertexIndex, string undoName, out string statusMessage)
        {
            statusMessage = string.Empty;
            if (!TryGetSlotMeshData(slot, out UMAMeshData meshData, out statusMessage))
            {
                return false;
            }

            if (targetWeightsByVertexIndex == null || targetWeightsByVertexIndex.Count == 0)
            {
                statusMessage = "No target vertex weights were supplied.";
                return false;
            }

            bool hasManagedWeights = HasValidManagedBoneWeights(meshData);
            bool hasLegacyWeights = HasValidLegacyBoneWeights(meshData);
            if (!hasManagedWeights && !hasLegacyWeights)
            {
                statusMessage = "Cannot rewrite the SlotDataAsset because the existing mesh data has no valid managed or legacy weights to preserve for the other vertices.";
                return false;
            }

            Dictionary<int, List<BoneWeight1>> validatedTargetWeights = new Dictionary<int, List<BoneWeight1>>();
            foreach (KeyValuePair<int, List<BoneWeight1>> targetWeights in targetWeightsByVertexIndex)
            {
                if (targetWeights.Key < 0 || targetWeights.Key >= meshData.vertexCount)
                {
                    statusMessage = "Target vertex index is outside the slot mesh data.";
                    return false;
                }

                List<BoneWeight1> validatedWeights = BuildTargetBoneWeightsFromBoneWeights(meshData, targetWeights.Value, out statusMessage);
                if (validatedWeights == null)
                {
                    return false;
                }
                if (validatedWeights.Count > byte.MaxValue)
                {
                    statusMessage = "A vertex cannot store more than 255 bone weights.";
                    return false;
                }
                validatedTargetWeights.Add(targetWeights.Key, validatedWeights);
            }

            byte[] newBonesPerVertex = new byte[meshData.vertexCount];
            List<BoneWeight1> newBoneWeights = new List<BoneWeight1>(meshData.ManagedBoneWeights != null ? meshData.ManagedBoneWeights.Length : meshData.vertexCount * 4);
            int managedOffset = 0;
            for (int vertexIndex = 0; vertexIndex < meshData.vertexCount; vertexIndex++)
            {
                List<BoneWeight1> vertexWeights;
                if (validatedTargetWeights.TryGetValue(vertexIndex, out List<BoneWeight1> targetWeights))
                {
                    vertexWeights = targetWeights;
                }
                else if (hasManagedWeights)
                {
                    int count = meshData.ManagedBonesPerVertex[vertexIndex];
                    vertexWeights = new List<BoneWeight1>(count);
                    for (int weightIndex = 0; weightIndex < count; weightIndex++)
                    {
                        vertexWeights.Add(meshData.ManagedBoneWeights[managedOffset + weightIndex]);
                    }
                }
                else
                {
                    TryGetLegacyWeightsForVertex(meshData, vertexIndex, out vertexWeights);
                }

                if (hasManagedWeights)
                {
                    managedOffset += meshData.ManagedBonesPerVertex[vertexIndex];
                }

                if (vertexWeights.Count > byte.MaxValue)
                {
                    statusMessage = "A vertex cannot store more than 255 bone weights.";
                    return false;
                }

                newBonesPerVertex[vertexIndex] = (byte)vertexWeights.Count;
                newBoneWeights.AddRange(vertexWeights);
            }

            Undo.RecordObject(slot.asset, undoName);
            meshData.ManagedBonesPerVertex = newBonesPerVertex;
            meshData.ManagedBoneWeights = newBoneWeights.ToArray();
            foreach (KeyValuePair<int, List<BoneWeight1>> targetWeights in validatedTargetWeights)
            {
                UpdateLegacyVertexWeights(meshData, targetWeights.Key, targetWeights.Value);
            }
            meshData.LoadedBoneweights = false;

            EditorUtility.SetDirty(slot.asset);
            return true;
        }

        private bool TryGetSlotMeshData(SlotData slot, out UMAMeshData meshData, out string statusMessage)
        {
            meshData = null;
            statusMessage = string.Empty;
            if (slot == null)
            {
                statusMessage = "No slot is available.";
                return false;
            }

            if (slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
            {
                statusMessage = "The selected slot has no mesh data.";
                return false;
            }

            meshData = slot.asset.meshData;
            return true;
        }

        private bool TryGetVertexBoneWeights(UMAMeshData meshData, int vertexIndex, out List<BoneWeight1> weights)
        {
            if (TryGetManagedWeightsForVertex(meshData, vertexIndex, out weights))
            {
                return true;
            }

            if (TryGetLegacyWeightsForVertex(meshData, vertexIndex, out weights))
            {
                return true;
            }

            weights = new List<BoneWeight1>();
            return false;
        }

        private List<BoneWeight1> BuildTargetBoneWeightsFromBoneWeights(UMAMeshData meshData, List<BoneWeight1> editedWeights, out string statusMessage)
        {
            statusMessage = string.Empty;
            if (editedWeights == null || editedWeights.Count == 0)
            {
                statusMessage = "Add at least one weight before applying.";
                return null;
            }

            Dictionary<int, float> weightsByBoneIndex = new Dictionary<int, float>();
            for (int i = 0; i < editedWeights.Count; i++)
            {
                BoneWeight1 editedWeight = editedWeights[i];
                if (editedWeight.boneIndex < 0 || meshData.boneNameHashes == null || editedWeight.boneIndex >= meshData.boneNameHashes.Length)
                {
                    statusMessage = "Weight references an invalid SlotDataAsset bone index.";
                    return null;
                }

                float weight = Mathf.Clamp01(editedWeight.weight);
                if (weight <= 0f)
                {
                    continue;
                }

                if (weightsByBoneIndex.ContainsKey(editedWeight.boneIndex))
                {
                    weightsByBoneIndex[editedWeight.boneIndex] += weight;
                }
                else
                {
                    weightsByBoneIndex.Add(editedWeight.boneIndex, weight);
                }
            }

            if (weightsByBoneIndex.Count == 0)
            {
                statusMessage = "At least one weight must be greater than zero.";
                return null;
            }

            List<BoneWeight1> targetWeights = new List<BoneWeight1>(weightsByBoneIndex.Count);
            foreach (KeyValuePair<int, float> pair in weightsByBoneIndex)
            {
                targetWeights.Add(new BoneWeight1()
                {
                    boneIndex = pair.Key,
                    weight = pair.Value
                });
            }
            targetWeights.Sort((left, right) => right.weight.CompareTo(left.weight));
            return targetWeights;
        }

        private bool TryGetSelectionMeshData(VertexSelection selectedVertex, out UMAMeshData meshData, out string statusMessage)
        {
            meshData = null;
            statusMessage = string.Empty;

            if (selectedVertex == null || selectedVertex.slot == null)
            {
                statusMessage = "No vertex is selected.";
                return false;
            }

            if (selectedVertex.slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(selectedVertex.slot.asset.meshData))
            {
                statusMessage = "The selected slot has no mesh data.";
                return false;
            }

            meshData = selectedVertex.slot.asset.meshData;
            if (selectedVertex.vertexIndexOnSlot < 0 || selectedVertex.vertexIndexOnSlot >= meshData.vertexCount)
            {
                statusMessage = "The selected vertex index is outside the slot mesh data.";
                return false;
            }

            return true;
        }

        private bool TryGetManagedWeightsForVertex(UMAMeshData meshData, int vertexIndex, out List<BoneWeight1> weights)
        {
            weights = new List<BoneWeight1>();
            if (!HasValidManagedBoneWeights(meshData))
            {
                return false;
            }

            int weightOffset = 0;
            for (int i = 0; i < vertexIndex; i++)
            {
                weightOffset += meshData.ManagedBonesPerVertex[i];
            }

            int weightCount = meshData.ManagedBonesPerVertex[vertexIndex];
            for (int i = 0; i < weightCount; i++)
            {
                weights.Add(meshData.ManagedBoneWeights[weightOffset + i]);
            }
            return true;
        }

        private bool TryGetLegacyWeightsForVertex(UMAMeshData meshData, int vertexIndex, out List<BoneWeight1> weights)
        {
            weights = new List<BoneWeight1>();
            if (!HasValidLegacyBoneWeights(meshData) || vertexIndex < 0 || vertexIndex >= meshData.boneWeights.Length)
            {
                return false;
            }

            UMABoneWeight legacyWeight = meshData.boneWeights[vertexIndex];
            AddLegacyWeight(weights, legacyWeight.boneIndex0, legacyWeight.weight0);
            AddLegacyWeight(weights, legacyWeight.boneIndex1, legacyWeight.weight1);
            AddLegacyWeight(weights, legacyWeight.boneIndex2, legacyWeight.weight2);
            AddLegacyWeight(weights, legacyWeight.boneIndex3, legacyWeight.weight3);
            return true;
        }

        private void AddLegacyWeight(List<BoneWeight1> weights, int boneIndex, float weight)
        {
            if (weight <= 0f)
            {
                return;
            }

            weights.Add(new BoneWeight1()
            {
                boneIndex = boneIndex,
                weight = weight
            });
        }

        private bool HasValidManagedBoneWeights(UMAMeshData meshData)
        {
            if (meshData == null || meshData.ManagedBonesPerVertex == null || meshData.ManagedBonesPerVertex.Length != meshData.vertexCount || meshData.ManagedBoneWeights == null)
            {
                return false;
            }

            int weightCount = 0;
            for (int i = 0; i < meshData.ManagedBonesPerVertex.Length; i++)
            {
                weightCount += meshData.ManagedBonesPerVertex[i];
                if (weightCount > meshData.ManagedBoneWeights.Length)
                {
                    return false;
                }
            }
            return true;
        }

        private bool HasValidLegacyBoneWeights(UMAMeshData meshData)
        {
            return meshData != null && meshData.boneWeights != null && meshData.boneWeights.Length == meshData.vertexCount;
        }

        private VertexWeightEntry CreateSlotWeightEntry(UMAMeshData meshData, int boneIndex, float weight)
        {
            int boneHash = GetSlotBoneHash(meshData, boneIndex);
            return new VertexWeightEntry()
            {
                boneIndex = boneIndex,
                boneHash = boneHash,
                boneName = GetBoneDisplayName(boneHash, boneIndex),
                weight = weight
            };
        }

        private VertexWeightEntry CreateSkinnedWeightEntry(SkinnedMeshRenderer renderer, int boneIndex, float weight)
        {
            string boneName = "Renderer Bone Index " + boneIndex;
            int boneHash = 0;
            if (renderer != null && renderer.bones != null && boneIndex >= 0 && boneIndex < renderer.bones.Length && renderer.bones[boneIndex] != null)
            {
                boneName = renderer.bones[boneIndex].name;
                boneHash = UMAUtils.StringToHash(boneName);
            }

            return new VertexWeightEntry()
            {
                boneIndex = boneIndex,
                boneHash = boneHash,
                boneName = boneName,
                weight = weight
            };
        }

        private int GetSlotBoneHash(UMAMeshData meshData, int boneIndex)
        {
            if (meshData == null || meshData.boneNameHashes == null || boneIndex < 0 || boneIndex >= meshData.boneNameHashes.Length)
            {
                return 0;
            }
            return meshData.boneNameHashes[boneIndex];
        }

        internal string GetBoneDisplayName(int boneHash, int boneIndex)
        {
            Transform boneTransform = thisDCA != null && thisDCA.umaData != null && thisDCA.umaData.skeleton != null
                ? thisDCA.umaData.skeleton.GetBoneTransform(boneHash)
                : null;
            if (boneTransform != null)
            {
                return boneTransform.name;
            }

            return boneHash != 0 ? "Hash " + boneHash : "Bone Index " + boneIndex;
        }

        private static SkinnedMeshRenderer GetSkinnedMeshRenderer(DynamicCharacterAvatar avatar)
        {
            if (avatar != null && avatar.umaData != null)
            {
                SkinnedMeshRenderer renderer = avatar.umaData.GetRenderer(0);
                if (renderer != null)
                {
                    return renderer;
                }
            }

            return avatar != null ? avatar.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        }

        private SkinnedMeshRenderer GetCurrentSkinnedMeshRenderer()
        {
            return GetSkinnedMeshRenderer(thisDCA);
        }

        internal List<BoneOption> GetSlotBoneOptions(VertexSelection selectedVertex)
        {
            List<BoneOption> options = new List<BoneOption>();
            if (!TryGetSelectionMeshData(selectedVertex, out UMAMeshData meshData, out _))
            {
                return options;
            }

            if (meshData.boneNameHashes == null)
            {
                return options;
            }

            for (int i = 0; i < meshData.boneNameHashes.Length; i++)
            {
                int boneHash = meshData.boneNameHashes[i];
                string boneName = GetBoneDisplayName(boneHash, i);
                options.Add(new BoneOption()
                {
                    boneIndex = i,
                    boneHash = boneHash,
                    boneName = boneName,
                    displayName = boneName + " (index " + i + ", hash " + boneHash + ")",
                    isBound = true
                });
            }

            return options;
        }

        internal List<BoneOption> GetEditableBoneOptions(VertexSelection selectedVertex)
        {
            List<BoneOption> options = GetSlotBoneOptions(selectedVertex);
            HashSet<int> seenHashes = new HashSet<int>();
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].boneHash != 0)
                {
                    seenHashes.Add(options[i].boneHash);
                }
            }

            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.skeleton == null)
            {
                return options;
            }

            List<int> skeletonHashes = new List<int>(thisDCA.umaData.skeleton.boneHashData.Keys);
            skeletonHashes.Sort();
            for (int i = 0; i < skeletonHashes.Count; i++)
            {
                int boneHash = skeletonHashes[i];
                if (seenHashes.Contains(boneHash))
                {
                    continue;
                }

                Transform boneTransform = thisDCA.umaData.skeleton.GetBoneTransform(boneHash);
                string boneName = boneTransform != null ? boneTransform.name : "Hash " + boneHash;
                options.Add(new BoneOption()
                {
                    boneIndex = -1,
                    boneHash = boneHash,
                    boneName = boneName,
                    displayName = boneName + " (new binding, hash " + boneHash + ")",
                    isBound = false
                });
                seenHashes.Add(boneHash);
            }

            return options;
        }

        internal List<VertexWeightComparison> BuildWeightComparisons(List<VertexWeightEntry> slotWeights, List<VertexWeightEntry> skinnedWeights)
        {
            Dictionary<int, VertexWeightComparison> comparisonsByHash = new Dictionary<int, VertexWeightComparison>();
            AddWeightsToComparisons(comparisonsByHash, slotWeights, true);
            AddWeightsToComparisons(comparisonsByHash, skinnedWeights, false);

            List<VertexWeightComparison> comparisons = new List<VertexWeightComparison>(comparisonsByHash.Values);
            comparisons.Sort((left, right) => string.Compare(left.boneName, right.boneName, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < comparisons.Count; i++)
            {
                comparisons[i].mismatch = Mathf.Abs(comparisons[i].slotWeight - comparisons[i].skinnedWeight) > BoneWeightMismatchTolerance;
            }
            return comparisons;
        }

        private void AddWeightsToComparisons(Dictionary<int, VertexWeightComparison> comparisonsByHash, List<VertexWeightEntry> weights, bool isSlotWeight)
        {
            for (int i = 0; i < weights.Count; i++)
            {
                VertexWeightEntry weight = weights[i];
                int key = weight.boneHash != 0 ? weight.boneHash : int.MinValue + weight.boneIndex;
                if (!comparisonsByHash.TryGetValue(key, out VertexWeightComparison comparison))
                {
                    comparison = new VertexWeightComparison()
                    {
                        boneHash = weight.boneHash,
                        boneName = weight.boneName
                    };
                    comparisonsByHash.Add(key, comparison);
                }

                if (isSlotWeight)
                {
                    comparison.slotWeight += weight.weight;
                }
                else
                {
                    comparison.skinnedWeight += weight.weight;
                    if (string.IsNullOrEmpty(comparison.boneName) || comparison.boneName.StartsWith("Hash ", StringComparison.Ordinal))
                    {
                        comparison.boneName = weight.boneName;
                    }
                }
            }
        }

        private List<BoneWeight1> BuildTargetBoneWeights(UMAMeshData meshData, List<VertexWeightEntry> editedWeights, out string statusMessage)
        {
            statusMessage = string.Empty;
            if (editedWeights == null || editedWeights.Count == 0)
            {
                statusMessage = "Add at least one weight before applying.";
                return null;
            }

            Dictionary<int, float> weightsByBoneIndex = new Dictionary<int, float>();
            for (int i = 0; i < editedWeights.Count; i++)
            {
                VertexWeightEntry editedWeight = editedWeights[i];
                if (editedWeight.boneIndex < 0 || meshData.boneNameHashes == null || editedWeight.boneIndex >= meshData.boneNameHashes.Length)
                {
                    statusMessage = "Weight references an invalid SlotDataAsset bone index.";
                    return null;
                }

                float weight = Mathf.Clamp01(editedWeight.weight);
                if (weight <= 0f)
                {
                    continue;
                }

                if (weightsByBoneIndex.ContainsKey(editedWeight.boneIndex))
                {
                    weightsByBoneIndex[editedWeight.boneIndex] += weight;
                }
                else
                {
                    weightsByBoneIndex.Add(editedWeight.boneIndex, weight);
                }
            }

            if (weightsByBoneIndex.Count == 0)
            {
                statusMessage = "At least one weight must be greater than zero.";
                return null;
            }

            List<BoneWeight1> targetWeights = new List<BoneWeight1>(weightsByBoneIndex.Count);
            foreach (KeyValuePair<int, float> pair in weightsByBoneIndex)
            {
                targetWeights.Add(new BoneWeight1()
                {
                    boneIndex = pair.Key,
                    weight = pair.Value
                });
            }
            targetWeights.Sort((left, right) => right.weight.CompareTo(left.weight));
            return targetWeights;
        }

        private void UpdateLegacyVertexWeights(UMAMeshData meshData, int vertexIndex, List<BoneWeight1> targetWeights)
        {
            if (!HasValidLegacyBoneWeights(meshData) || vertexIndex < 0 || vertexIndex >= meshData.boneWeights.Length)
            {
                return;
            }

            UMABoneWeight legacyWeight = new UMABoneWeight();
            if (targetWeights.Count > 0)
            {
                legacyWeight.boneIndex0 = targetWeights[0].boneIndex;
                legacyWeight.weight0 = targetWeights[0].weight;
            }
            if (targetWeights.Count > 1)
            {
                legacyWeight.boneIndex1 = targetWeights[1].boneIndex;
                legacyWeight.weight1 = targetWeights[1].weight;
            }
            if (targetWeights.Count > 2)
            {
                legacyWeight.boneIndex2 = targetWeights[2].boneIndex;
                legacyWeight.weight2 = targetWeights[2].weight;
            }
            if (targetWeights.Count > 3)
            {
                legacyWeight.boneIndex3 = targetWeights[3].boneIndex;
                legacyWeight.weight3 = targetWeights[3].weight;
            }

            meshData.boneWeights[vertexIndex] = legacyWeight;
        }

        private class VertexWeightEditorWindow : EditorWindow
        {
            private VertexEditorStage stage;
            private VertexSelection selectedVertex;
            private List<VertexWeightEntry> editableSlotWeights = new List<VertexWeightEntry>();
            private List<VertexWeightEntry> skinnedWeights = new List<VertexWeightEntry>();
            private List<BoneOption> boneOptions = new List<BoneOption>();
            private Vector2 scrollPosition;
            private string slotStatusMessage;
            private string skinnedStatusMessage;
            private string actionStatusMessage;
            private string boneFilter = string.Empty;
            private int filteredBoneIndex;
            private float newBoneWeight = 0f;

            public static void Open(VertexEditorStage stage, VertexSelection selectedVertex)
            {
                VertexWeightEditorWindow window = CreateInstance<VertexWeightEditorWindow>();
                window.titleContent = new GUIContent("Vertex Weights");
                window.minSize = new Vector2(560f, 480f);
                window.Initialize(stage, selectedVertex);
                window.ShowUtility();
                window.Focus();
            }

            private void Initialize(VertexEditorStage stage, VertexSelection selectedVertex)
            {
                this.stage = stage;
                this.selectedVertex = selectedVertex;
                RefreshData();
            }

            private void RefreshData()
            {
                if (stage == null || selectedVertex == null)
                {
                    return;
                }

                List<VertexWeightEntry> slotWeights = stage.GetSlotAssetVertexWeights(selectedVertex, out slotStatusMessage);
                editableSlotWeights = new List<VertexWeightEntry>(slotWeights.Count);
                for (int i = 0; i < slotWeights.Count; i++)
                {
                    editableSlotWeights.Add(slotWeights[i].Clone());
                }

                skinnedWeights = stage.GetSkinnedMeshVertexWeights(selectedVertex, out skinnedStatusMessage);
                boneOptions = stage.GetSlotBoneOptions(selectedVertex);
                actionStatusMessage = string.Empty;
            }

            private void OnGUI()
            {
                if (stage == null || selectedVertex == null || selectedVertex.slot == null)
                {
                    EditorGUILayout.HelpBox("The selected vertex is no longer available.", MessageType.Warning);
                    if (GUILayout.Button("Close"))
                    {
                        Close();
                    }
                    return;
                }

                EditorGUILayout.LabelField("Slot", selectedVertex.slot.slotName);
                EditorGUILayout.LabelField("Vertex", selectedVertex.vertexIndexOnSlot.ToString());

                List<VertexWeightComparison> comparisons = stage.BuildWeightComparisons(editableSlotWeights, skinnedWeights);
                bool hasMismatch = HasMismatch(comparisons);
                EditorGUILayout.HelpBox(hasMismatch ? "Mismatch: SlotDataAsset weights do not match the current SkinnedMesh weights." : "OK: SlotDataAsset weights match the current SkinnedMesh weights.", hasMismatch ? MessageType.Warning : MessageType.Info);

                if (!string.IsNullOrEmpty(slotStatusMessage))
                {
                    EditorGUILayout.HelpBox(slotStatusMessage, MessageType.Info);
                }
                if (!string.IsNullOrEmpty(skinnedStatusMessage))
                {
                    EditorGUILayout.HelpBox(skinnedStatusMessage, MessageType.Info);
                }
                if (!string.IsNullOrEmpty(actionStatusMessage))
                {
                    EditorGUILayout.HelpBox(actionStatusMessage, MessageType.Info);
                }

                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                DrawComparison(comparisons);
                EditorGUILayout.Space();
                DrawEditableWeights();
                EditorGUILayout.Space();
                DrawAddWeight();
                EditorGUILayout.EndScrollView();

                DrawFooterButtons();
            }

            private bool HasMismatch(List<VertexWeightComparison> comparisons)
            {
                for (int i = 0; i < comparisons.Count; i++)
                {
                    if (comparisons[i].mismatch)
                    {
                        return true;
                    }
                }
                return false;
            }

            private void DrawComparison(List<VertexWeightComparison> comparisons)
            {
                EditorGUILayout.LabelField("SlotDataAsset vs SkinnedMesh", EditorStyles.boldLabel);
                if (comparisons.Count == 0)
                {
                    EditorGUILayout.HelpBox("No weights are available to compare.", MessageType.Info);
                    return;
                }

                for (int i = 0; i < comparisons.Count; i++)
                {
                    VertexWeightComparison comparison = comparisons[i];
                    string message = (comparison.mismatch ? "Mismatch" : "OK") + " - " + comparison.boneName + " | SlotDataAsset: " + FormatWeight(comparison.slotWeight) + " | SkinnedMesh: " + FormatWeight(comparison.skinnedWeight);
                    EditorGUILayout.HelpBox(message, comparison.mismatch ? MessageType.Warning : MessageType.None);
                }
            }

            private void DrawEditableWeights()
            {
                EditorGUILayout.LabelField("Edit SlotDataAsset Weights", EditorStyles.boldLabel);
                if (editableSlotWeights.Count == 0)
                {
                    EditorGUILayout.HelpBox("No SlotDataAsset weights are currently assigned to this vertex.", MessageType.Info);
                }

                for (int i = 0; i < editableSlotWeights.Count; i++)
                {
                    VertexWeightEntry weight = editableSlotWeights[i];
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(weight.boneName + " (index " + weight.boneIndex + ")", GUILayout.MinWidth(220f));
                    weight.weight = Mathf.Clamp01(EditorGUILayout.FloatField(weight.weight, GUILayout.Width(72f)));
                    bool removeWeight = false;
                    if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                    {
                        removeWeight = true;
                    }
                    EditorGUILayout.EndHorizontal();

                    if (removeWeight)
                    {
                        editableSlotWeights.RemoveAt(i);
                        GUIUtility.ExitGUI();
                    }
                }

                float total = GetEditableWeightTotal();
                EditorGUILayout.LabelField("Total", FormatWeight(total));
                EditorGUI.BeginDisabledGroup(total <= 0f);
                if (GUILayout.Button("Normalize"))
                {
                    NormalizeEditableWeights(total);
                }
                EditorGUI.EndDisabledGroup();
            }

            private void DrawAddWeight()
            {
                EditorGUILayout.LabelField("Add Bone Weight", EditorStyles.boldLabel);
                boneFilter = EditorGUILayout.TextField("Bone Filter", boneFilter);
                List<BoneOption> filteredOptions = GetFilteredBoneOptions();
                if (filteredOptions.Count == 0)
                {
                    EditorGUILayout.HelpBox("No matching bones are available to add.", MessageType.Info);
                    return;
                }

                if (filteredBoneIndex >= filteredOptions.Count)
                {
                    filteredBoneIndex = 0;
                }

                string[] optionNames = new string[filteredOptions.Count];
                for (int i = 0; i < filteredOptions.Count; i++)
                {
                    optionNames[i] = filteredOptions[i].displayName;
                }

                filteredBoneIndex = EditorGUILayout.Popup("Bone", filteredBoneIndex, optionNames);
                newBoneWeight = Mathf.Clamp01(EditorGUILayout.FloatField("Weight", newBoneWeight));
                if (GUILayout.Button("Add"))
                {
                    BoneOption option = filteredOptions[filteredBoneIndex];
                    editableSlotWeights.Add(new VertexWeightEntry()
                    {
                        boneIndex = option.boneIndex,
                        boneHash = option.boneHash,
                        boneName = option.boneName,
                        weight = newBoneWeight
                    });
                    newBoneWeight = 0f;
                }
            }

            private void DrawFooterButtons()
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Refresh"))
                {
                    RefreshData();
                }
                if (GUILayout.Button("Apply to SlotDataAsset"))
                {
                    if (stage.TryApplySlotAssetVertexWeights(selectedVertex, editableSlotWeights, out actionStatusMessage))
                    {
                        string applyStatusMessage = actionStatusMessage;
                        stage.RebuildMesh(true);
                        RefreshData();
                        actionStatusMessage = applyStatusMessage;
                    }
                }
                if (GUILayout.Button("Close"))
                {
                    Close();
                }
                EditorGUILayout.EndHorizontal();
            }

            private List<BoneOption> GetFilteredBoneOptions()
            {
                List<BoneOption> filteredOptions = new List<BoneOption>();
                string normalizedFilter = string.IsNullOrWhiteSpace(boneFilter) ? string.Empty : boneFilter.Trim();
                for (int i = 0; i < boneOptions.Count; i++)
                {
                    BoneOption option = boneOptions[i];
                    if (HasEditableBone(option.boneIndex))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(normalizedFilter) && option.displayName.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    filteredOptions.Add(option);
                }
                return filteredOptions;
            }

            private bool HasEditableBone(int boneIndex)
            {
                for (int i = 0; i < editableSlotWeights.Count; i++)
                {
                    if (editableSlotWeights[i].boneIndex == boneIndex)
                    {
                        return true;
                    }
                }
                return false;
            }

            private float GetEditableWeightTotal()
            {
                float total = 0f;
                for (int i = 0; i < editableSlotWeights.Count; i++)
                {
                    total += editableSlotWeights[i].weight;
                }
                return total;
            }

            private void NormalizeEditableWeights(float total)
            {
                if (total <= 0f)
                {
                    return;
                }

                for (int i = 0; i < editableSlotWeights.Count; i++)
                {
                    editableSlotWeights[i].weight /= total;
                }
            }

            private string FormatWeight(float weight)
            {
                return weight.ToString("0.######");
            }
        }

        public VertexSelection GetInternalSelection(VertexAdjustment va)
        {
            if (va == null)
            {
                return null;
            }
            var result = GetSelectedVertex();
            if (result != null)
            {
                return result;
            }
            VertexSelection vs = new VertexSelection();
            vs.slot = FindSlotBySourceSlotOrName(va.slotName);
            if (vs.slot == null)
            {
                return null;
            }
            vs.isActive = true;
            vs.suppressed = false;
            vs.vertexIndexOnSlot = va.vertexIndex;
            vs.WorldPosition = GetWorldPosition(vs.slot, vs.vertexIndexOnSlot);
            return vs;
        }


        public List<VertexSelection> GetVertexSelections()
        {
            return SelectedVertexes;
        }

        public void SetVertexSelections(List<VertexSelection> selections)
        {
            SelectedVertexes = selections;
        }

        public List<VertexSelection> GetActiveSelectedVertexes()
        {
            List<VertexSelection> active = new List<VertexSelection>();
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                if (SelectedVertexes[i].isActive)
                {
                    active.Add(SelectedVertexes[i]);
                }
            }
            return active;
        }
        public List<VertexSelection> GetAllVertexes()
        {
            List<VertexSelection> active = new List<VertexSelection>();
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                if (SelectedVertexes[i].isActive)
                {
                    active.Add(SelectedVertexes[i]);
                }
            }
            return active;
        }

        public int GetSelectedVertexCount()
        {
            return SelectedVertexes.Count;
        }

        public int GetActiveSelectedVertexCount()
        {
            int count = 0;
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                if (SelectedVertexes[i].isActive)
                {
                    count++;
                }
            }
            return count;
        }

        public void AddVertexAdjustment(VertexAdjustment adjustment)
        {
            Adjustments.Add(adjustment);
        }

        public List<VertexAdjustment> GetVertexAdjustments()
        {
            return Adjustments;
        }

        protected override bool OnOpenStage()
        {
            base.OnOpenStage();

            EnsureEditorEvents();
            if (touchupWeightsMode)
            {
                Undo.undoRedoPerformed += OnTouchupWeightUndoRedo;
                EditorApplication.update += OnTouchupLiveUpdate;
            }
            //scene = EditorSceneManager.NewPreviewScene();

            if (touchupWeightsMode)
            {
                touchupWeightsWindow = UmaTouchupWeightsWindow.Open(this);
            }
            else if (slotWeightEditorMode)
            {
                slotWeightEditorWindow = UmaSlotWeightEditorWindow.Open(this, slotWeightEditorSlotAsset);
            }
            else
            {
                modifierEditor = MeshModifierEditor.GetOrCreateWindowFromModifier(Currentmodifier, thisDCA, this);
                if (Currentmodifier != null)
                {
                    // Note: Setup() in MeshModifierEditor already creates a safe copy of EditorModifiers.
                    // Do NOT reassign modifierEditor.Modifiers here as it would replace the copy with a direct reference.
                    foreach (var newMod in modifierEditor.Modifiers)
                    {
                        // get the type of the VertexAdjustment for this collection
                        // no-op: adjustments are persisted directly via SerializeReference
                        /*
                        Type adjType = Type.GetType(newMod.AdjustmentType);
                        Type colType = Type.GetType(newMod.CollectionType);
                        newMod.adjustments = (VertexAdjustmentCollection)Activator.CreateInstance(colType);
                        newMod.TemplateAdjustment = (VertexAdjustment)Activator.CreateInstance(adjType);
                        foreach(string json in newMod.JsonAdjustments)
                        {
                            VertexAdjustment va = VertexAdjustment.FromJSON(json);
                            if (va != null)
                            {
                                newMod.adjustments.Add(va);
                            }
                        } */
                    }

                    // Debug: Check adjustments count after foreach loop
                    int totalAfterLoop = 0;
                    foreach (var mod in modifierEditor.Modifiers)
                    {
                        if (mod != null && mod.adjustments != null && mod.adjustments.vertexAdjustments != null)
                        {
                            totalAfterLoop += mod.adjustments.vertexAdjustments.Count;
                        }
                    }
                    Debug.Log($"[OnOpenStage] After foreach loop: Modifiers.Count={modifierEditor.Modifiers.Count}, TotalAdjustments={totalAfterLoop}");
                }
                else
                {
                    modifierEditor.Modifiers = new List<MeshModifier.Modifier>();
                }
            }
            lightingObject = new GameObject("Directional Light");
            lightingObject.transform.rotation = Quaternion.Euler(50, 330, 0);
            lightingObject.AddComponent<Light>().type = LightType.Directional;

            SkinnedMeshRenderer smr = GetCurrentSkinnedMeshRenderer();
            if (smr == null)
            {
                EditorUtility.DisplayDialog("UMA Vertex Editing", "No SkinnedMeshRenderer was available for the vertex editor stage.", "OK");
                return false;
            }

            stageSkinnedMeshRenderer = smr;
            stageSkinnedMeshRendererWasEnabled = smr.enabled;
            CaptureOriginalVertexMaterials(smr);

            BakedMesh = new Mesh();
            BakedMesh.name = "BakedMesh";
            smr.BakeMesh(BakedMesh, true);
            RefreshBakedMeshCaches();
            GameObject go = new GameObject("VertexEditor");
            go.AddComponent<MeshFilter>().sharedMesh = BakedMesh;
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = go.AddComponent<MeshRenderer>();
            }
            // Material sharedMaterial = UMAUtils.GetDefaultDiffuseMaterial();
            renderer.sharedMaterials = new Material[BakedMesh.subMeshCount];
            //go.transform.parent = thisDCA.gameObject.transform;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            MeshCollider mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = BakedMesh;

            go.SetActive(true);
            smr.enabled = false;
            VertexObject = go;
            SetVertexMaterialColors(go);
            ApplyVertexDisplayOptions();
            cameraAnchor = new GameObject("CameraAnchor");
            cameraAnchor.transform.position = new Vector3(0, 1, 2.5f);
            cameraAnchor.transform.rotation = Quaternion.Euler(0, 180, 0);

            SceneManager.MoveGameObjectToScene(VertexObject, scene);
            SceneManager.MoveGameObjectToScene(lightingObject, scene);
            SceneManager.MoveGameObjectToScene(cameraAnchor, scene);
            Tools.hidden = true;
            SceneView.duringSceneGui += OnSceneGUI;
            NeedsCameraSetup = true;
            // The caller generates the UMA synchronously before opening this stage.
            // Do not regenerate here: editor-time DCA generation destroys and replaces
            // the generated materials/atlas textures after this preview has captured them.
            cachedVisibilityHeight = -1f;
            CaptureSavedSelectionSnapshot();
            if (touchupWeightsMode)
            {
                InitializeTouchupWeights();
            }

            return true;
        }

        private List<Type> LoadTypes(Type baseType)
        {
            List<Type> theTypes = new List<Type>();
            System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var asm in assemblies)
            {
                if (asm.IsDynamic)
                {
                    continue;
                }

                var Types = asm.GetExportedTypes();
                foreach (var t in Types)
                {
                    if (typeof(VertexAdjustmentCollection).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                    {
                        theTypes.Add(t);
                    }
                }
            }
            return theTypes;
        }



        protected override void OnCloseStage()
        {
            closing = true;
            Undo.undoRedoPerformed -= OnTouchupWeightUndoRedo;
            EditorApplication.update -= OnTouchupLiveUpdate;
            EndSculptStroke(true);
            EndVertexPaintStroke(true);
            if (thisDCA != null && thisDCA.umaData != null)
                thisDCA.umaData.CharacterUpdated.RemoveAction(BuildCollisionMesh);
            Tools.hidden = false;
            if (VertexObject != null)
            {
                DestroyImmediate(VertexObject);
            }
            if (lightingObject != null)
            {
                DestroyImmediate(lightingObject);
            }
            if (cameraAnchor != null)
            {
                DestroyImmediate(cameraAnchor);
            }
            RefreshBakedMeshCaches();
            SceneView.duringSceneGui -= OnSceneGUI;
            if (!ownsSlotWeightPreviewAvatar && stageSkinnedMeshRenderer != null)
            {
                stageSkinnedMeshRenderer.enabled = stageSkinnedMeshRendererWasEnabled;
            }
            if (thisDCA != null && !slotWeightEditorReadOnly && !touchupWeightsMode)
            {
                var wearables = thisDCA.GetVisibleWearables();
                foreach (var wearable in wearables)
                {
                    wearable.disabled = false;
                }
                thisDCA.umaData.ManualMeshModifiers = new List<MeshModifier.Modifier>();
                if (!ownsSlotWeightPreviewAvatar && thisDCA.editorTimeGeneration)
                {
                    thisDCA.ignoreMeshHideAssets = false;
                    thisDCA.GenerateSingleUMA();
                }
            }
            if (modifierEditor != null)
            {
                modifierEditor.Close();
            }
            if (slotWeightEditorWindow != null)
            {
                slotWeightEditorWindow.Close();
                slotWeightEditorWindow = null;
            }
            if (touchupWeightsWindow != null)
            {
                touchupWeightsWindow.Close();
                touchupWeightsWindow = null;
            }
            if (vertexMaterial != null)
            {
                DestroyImmediate(vertexMaterial);
            }
            if (vertexMesh != null)
            {
                DestroyImmediate(vertexMesh);
            }
            DestroyVertexPaintBrushCache();
            if (vertexColorPreviewMaterial != null)
            {
                DestroyImmediate(vertexColorPreviewMaterial);
                vertexColorPreviewMaterial = null;
            }
            DestroyPastelVertexMaterials();
            DestroyOriginalVertexMaterialCopies();
            if (ownsSlotWeightPreviewAvatar && thisDCA != null && thisDCA.gameObject != null)
            {
                DestroyImmediate(thisDCA.gameObject);
            }
            base.OnCloseStage();
        }

        private void OnTouchupWeightUndoRedo()
        {
            if (!touchupWeightsMode || closing)
            {
                return;
            }

            touchupLivePositionVertexIndices.Clear();
            touchupLivePositionVertexIndices.UnionWith(touchupSavedPositionVertexIndices);
            touchupLivePositionVertexIndices.UnionWith(touchupPendingPaintWeights.Keys);
            touchupLivePositionVertexIndices.UnionWith(touchupPaintStrokeWeights.Keys);
            touchupLivePositionVertexIndices.UnionWith(touchupPreviewVertexIndices);
            RestoreTouchupPendingPaintWeightsFromUndoState();
            ResetTouchupPaintData();
            EnsureTouchupPaintData(out _);
            touchupLivePositionVertexIndices.UnionWith(touchupSavedPositionVertexIndices);
            touchupLivePositionVertexIndices.UnionWith(touchupPendingPaintWeights.Keys);
            touchupLivePositionVertexIndices.UnionWith(touchupPreviewVertexIndices);
            RecalculateTouchupVertexPositions(touchupLivePositionVertexIndices);
            touchupWeightsRevision++;
            touchupPaintStatusType = MessageType.Info;
            touchupPaintStatusMessage = touchupPendingPaintWeights.Count > 0
                ? "Undo/redo restored pending weights for " +
                  touchupPendingPaintWeights.Count + " vertex(es)."
                : "Undo/redo restored the original weights.";
            RefreshTouchupWeightVisualization();
            RepaintLinkedEditors();
        }

        private IEnumerator RegenerateUMA()
        {
            yield return null;
            thisDCA.GenerateSingleUMA();
        }

        Int64 FloatToFixed5(float f)
        {
            return (Int64)(f * 100000);
        }

        public Int64 GetPositionKey(Vector3 inVector)
        {
            // convert position to 6 digit fixed point.
            // and then pack it into an Int64
            Int64 posKey = 0;
            Int64 x = FloatToFixed5(inVector.x);
            Int64 y = FloatToFixed5(inVector.y);
            Int64 z = FloatToFixed5(inVector.z);

            posKey = x + (y * 1000000) + (z * 1000000000000);
            return posKey;
        }


#if UMA_BURSTCOMPILE
        [BurstCompile(CompileSynchronously = true)]
#endif
        public void RecalculateNormals()
        {
#if TRUE
            BakedMesh.RecalculateNormals();
            BakedMesh.RecalculateTangents();

            return;
#else
            // now go through and average the normals for any duplicate vertexes to smooth the mesh at the seams.
            Dictionary<Int64, List<Vector3>> normals = new Dictionary<long, List<Vector3>>();
            Vector3[] verts = BakedMesh.vertices;
            Vector3[] norms = BakedMesh.normals;

            for (int i = 0; i < verts.Length; i++)
            {
                Int64 posKey = GetPositionKey(verts[i]);
                if (!normals.ContainsKey(posKey))
                {
                    normals.Add(posKey, new List<Vector3>());
                }
                normals[posKey].Add(norms[i]);
            }

            for (int i = 0; i < verts.Length; i++)
            {
                Int64 posKey = GetPositionKey(verts[i]);
                List<Vector3> normList = normals[posKey];
                Vector3 avg = Vector3.zero;
                foreach (Vector3 norm in normList)
                {
                    avg += norm;
                }
                avg /= normList.Count;
                norms[i] = avg;
            }
            BakedMesh.normals = norms;
#endif
        }

        private void OnSceneGUI(SceneView view)
        {
            if (NeedsCameraSetup)
            {
                InitialSetup(view);
            }
            DrawRaycastDebugRaysHandles();
         if (raycastDrawDebugRays && raycastDebugRays.Count > 0)
            {
                SceneView.RepaintAll();
            }
            AdjustWindowRects();
            DoSceneGUI(view);
        }

        public void AdjustWindowRects()
        {
         Rect r = SceneView.lastActiveSceneView.position;
            float panelWidth = Mathf.Clamp(r.width * 0.33f, LeftPanelWidthMin, LeftPanelWidthMax);
            leftPanelRect = new Rect(LeftPanelPadding, LeftPanelPadding, panelWidth, r.height - (LeftPanelPadding * 2f));
        }

        Quaternion test = Quaternion.identity;

        private void DoSceneGUI(SceneView sceneView)
        {

            Event currentEvent = Event.current;
            Vector2 mousePos = currentEvent.mousePosition;
         bool mouseOverAnyWindow = leftPanelRect.Contains(mousePos);
            // Let SceneView handle built-in focus shortcut (F) when the cursor is not over our UI.
            if (!mouseOverAnyWindow && currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.F)
            {
                return;
            }

            Handles.SetCamera(sceneView.camera);
            if (!slotWeightEditorMode && sceneToolMode == SceneToolMode.Sculpt)
            {
                HandleSculptSceneGUI(sceneView, currentEvent, mouseOverAnyWindow);
                DrawGUIWindows(sceneView);
                return;
            }
            if (!slotWeightEditorMode && sceneToolMode == SceneToolMode.VertexPaint)
            {
                HandleVertexPaintSceneGUI(sceneView, currentEvent, mouseOverAnyWindow);
                DrawGUIWindows(sceneView);
                return;
            }
            if (!rectSelect && Event.current.alt)
            {
                DrawHandles(SelectedVertexes);
                return;
            }

            if (editAdjustment != null && editAdjustment.Gizmo != VertexAdjustmentGizmo.None)
            {
                bool changed = DoGizmoInput();
                if (changed)
                {
                    RepaintLinkedEditors();

                    if (modifierEditor != null && modifierEditor.RebuildOnChanges)
                    {
                        modifierEditor.DoCharacterRebuild();
                    }
                }
            }

            Handles.BeginGUI();
            EnsureGUIStyles();

            if (isEditing == false)
            {

                string vals = $"Shift {currentEvent.shift}\nControl{currentEvent.control}\n,Alt{currentEvent.alt},Command{currentEvent.command}";

             if (Event.current.type == EventType.Layout && !mouseOverAnyWindow)
                {
                    HandleUtility.AddDefaultControl(GUIUtility.GetControlID(GetHashCode(), FocusType.Passive));
                }

                if (currentEvent.type == EventType.Repaint)
                {
                    if (currentMode == selectMode.Remove)
                    {
                        EditorGUIUtility.AddCursorRect(new Rect(0, 0, sceneView.position.width, sceneView.position.height), MouseCursor.ArrowMinus);
                    }
                    else
                    {
                        EditorGUIUtility.AddCursorRect(new Rect(0, 0, sceneView.position.width, sceneView.position.height), MouseCursor.Arrow);
                    }
                }

               if (currentEvent.type == EventType.MouseDown && !mouseOverAnyWindow)
                {
                    flippedVertexes.Clear();
                    selectionBrushedVerticesThisStroke.Clear();
                    //Debug.Log("Currentevent.button = "+ currentEvent.button);
                    if (currentEvent.button == 0)
                    {
                        if (!touchupWeightsMode || touchupWeightTool != TouchupWeightTool.Paint)
                        {
                            BeginSelectionUndoSnapshot(
                                currentDefineMode == DefineMode.DefineVertexSet
                                    ? "Modify Vertex Set"
                                    : "Modify Vertex State");
                        }
                        touchupEmptyClickCandidate =
                            touchupWeightsMode &&
                            touchupWeightTool == TouchupWeightTool.Select &&
                            !currentEvent.shift &&
                            !currentEvent.control &&
                            !IsPointerOverTouchupCharacter(currentEvent.mousePosition);
                        touchupReplaceSelectionOnNextCandidates =
                            touchupWeightsMode &&
                            touchupWeightTool == TouchupWeightTool.Select &&
                            !currentEvent.shift &&
                            !currentEvent.control;
                        touchupEmptyClickStart = currentEvent.mousePosition;

                        if (currentDefineMode == DefineMode.DefineVertexSet)
                        {
                            if (IsSelectionBrushModeEnabled)
                            {
                                replaceSelectionOnRectSelect = false;
                                rectSelect = false;
                                selectionBrushing = true;
                            }
                            else
                            {
                                replaceSelectionOnRectSelect = GetEffectiveSelectMode(currentEvent) == selectMode.Add && !currentEvent.shift && !currentEvent.control;
                                rectSelect = true;
                                selectionBrushing = false;
                                RectStart = currentEvent.mousePosition - currentEvent.delta;
                            }
                        }
                        else
                        {
                            if (IsSelectionBrushModeEnabled)
                            {
                                pendingStateClickAction = false;
                                replaceSelectionOnRectSelect = false;
                                rectSelect = false;
                                selectionBrushing = true;
                                ApplySelectionBrush(currentEvent);
                            }
                            else
                            {
                                pendingStateClickAction = true;
                                pendingStateClickStart = currentEvent.mousePosition;
                                replaceSelectionOnRectSelect = false;
                                rectSelect = true;
                                selectionBrushing = false;
                                RectStart = currentEvent.mousePosition - currentEvent.delta;
                            }
                        }

                        if (currentDefineMode == DefineMode.DefineVertexSet)
                        {
                            if (IsSelectionBrushModeEnabled)
                            {
                                ApplySelectionBrush(currentEvent);
                            }
                            else
                            {
                                // Defer click selection until MouseUp so a click doesn't get treated as a zero-size rect drag.
                                pendingStateClickAction = true;
                                pendingStateClickStart = currentEvent.mousePosition;
                            }
                        }
                    }
                    else if (currentEvent.button == 1)
                    {
                        touchupEmptyClickCandidate = false;
                        touchupReplaceSelectionOnNextCandidates = false;
                        pendingStateClickAction = false;
                        replaceSelectionOnRectSelect = false;
                        selectionBrushedVerticesThisStroke.Clear();
                        rectSelect = false;
                    }
                }



            // This is to prevent the scene view from capturing the selection and doing it's own routines.
            // But we must not eat events intended for our own IMGUI windows/scrollviews.
           if (currentEvent.type == EventType.MouseDrag)
            {
                if (touchupEmptyClickCandidate &&
                    Vector2.Distance(touchupEmptyClickStart, currentEvent.mousePosition) > 2f)
                {
                    touchupEmptyClickCandidate = false;
                }
                if (pendingStateClickAction)
                {
                    float dragDistance = Vector2.Distance(pendingStateClickStart, currentEvent.mousePosition);
                    if (dragDistance > 2f)
                    {
                        rectSelect = true;
                    }
                }

                if (!mouseOverAnyWindow)
                {
                    currentEvent.Use();
                    sceneView.Repaint();
                }
            }


                if (currentEvent.type == EventType.MouseUp)// && currentEvent.button == 0)
                {
                    if (touchupWeightsMode && touchupWeightTool == TouchupWeightTool.Paint)
                    {
                        EndTouchupWeightPaintStroke(true);
                    }
                    if (touchupEmptyClickCandidate)
                    {
                        ClearTouchupSelection(false);
                    }
                    touchupEmptyClickCandidate = false;
                    touchupReplaceSelectionOnNextCandidates = false;
                    EndSelectionUndoSnapshot();
                    selectionBrushing = false;

                    if (pendingStateClickAction)
                    {
                        float dragDistance = Vector2.Distance(pendingStateClickStart, currentEvent.mousePosition);
                        if (dragDistance <= 2f)
                        {
                            SingleSelect(currentEvent);
                        }
                        else
                        {
                            Vector2 RectEnd = currentEvent.mousePosition;
                            Rect MinMax = GetMinMax(RectStart, RectEnd);
                            RectangleSelect(currentEvent, MinMax);
                        }
                        pendingStateClickAction = false;
                        rectSelect = false;
                    }
                    else if (rectSelect)
                    {
                        // Do the rectangle selection
                        Vector2 RectEnd = currentEvent.mousePosition;
                        Rect MinMax = GetMinMax(RectStart, RectEnd);
                        RectangleSelect(currentEvent, MinMax);
                        rectSelect = false;
                    }
                    replaceSelectionOnRectSelect = false;
                    selectionBrushedVerticesThisStroke.Clear();
                }



                if (currentEvent.type == EventType.MouseLeaveWindow)
                {
                    touchupEmptyClickCandidate = false;
                    touchupReplaceSelectionOnNextCandidates = false;
                    if (rectSelect)
                    {
                        Vector2 RectEnd = currentEvent.mousePosition;
                        Rect MinMax = GetMinMax(RectStart, RectEnd);
                        RectangleSelect(currentEvent, MinMax);
                        rectSelect = false;
                        selectionBrushing = false;
                        sceneView.Repaint();
                    }
                    pendingStateClickAction = false;
                    replaceSelectionOnRectSelect = false;
                    selectionBrushedVerticesThisStroke.Clear();
                    if (touchupWeightsMode && touchupWeightTool == TouchupWeightTool.Paint)
                    {
                        EndTouchupWeightPaintStroke(true);
                    }
                    EndSelectionUndoSnapshot();
                }

                if (rectSelect && (currentEvent.mousePosition.x < 0 || currentEvent.mousePosition.y < 0 || currentEvent.mousePosition.x > sceneView.position.width || currentEvent.mousePosition.y > sceneView.position.height))
                {
                    rectSelect = false;
                }
                if (rectSelect)
                {
                    GUI.Box(new Rect(RectStart.x, RectStart.y, currentEvent.mousePosition.x - RectStart.x, currentEvent.mousePosition.y - RectStart.y), "");
                }
            }

            DrawSelectionBrushCircle(sceneView, currentEvent, mouseOverAnyWindow);

            if (!touchupWeightsMode && isEditing)
            {
                Rect topCenter = new Rect(0, 25, sceneView.position.width, 20);
                GUI.Label(topCenter, "** Edit Mode **", centeredLabel);
            }
            else if (!touchupWeightsMode)
            {
                Rect topCenter = new Rect(0, 25, sceneView.position.width, 20);
                string modeText = currentDefineMode == DefineMode.DefineVertexSet ? "** Define Vertex Set Mode **" : "** Define Vertex State Mode **";
                GUI.Label(topCenter, modeText, centeredLabel);
            }


            Handles.EndGUI();


            DrawHandles(SelectedVertexes);

            // Your custom GUI logic here
            DrawGUIWindows(sceneView);

            // Repaint the scene view only when necessary
            if (currentEvent.type == EventType.Repaint)
            {
                if (selectionBrushing)
                {
                    ApplySelectionBrush(currentEvent);
                }
                SceneView.RepaintAll();
            }
        }

        private void HandleSculptSceneGUI(SceneView sceneView, Event currentEvent, bool mouseOverAnyWindow)
        {
            EnsureSculptSession();
            if (Event.current.type == EventType.Layout && !mouseOverAnyWindow && !currentEvent.alt)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(GetHashCode() ^ 0x51C017, FocusType.Passive));

            bool wasSculpting = sculpting;
            bool currentHitValid = false;
            if (sculpting && sculptGrabInitialized)
            {
                // A Grab stroke is anchored to its initial slot and view plane. Re-raycasting
                // the deformed surface here would make the brush jump or switch slots mid-drag.
                currentHitValid = sculptHoverValid;
            }
            else
            {
                sculptHoverValid = !mouseOverAnyWindow && !currentEvent.alt &&
                    TryGetSculptHit(currentEvent.mousePosition, out sculptHoverPoint, out sculptHoverNormal, out sculptHoverTangent);
                currentHitValid = sculptHoverValid;
            }
            if (wasSculpting && !sculpting && sculptHoverValid && currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
            {
                BeginSculptStroke();
            }
            if (currentEvent.type == EventType.Repaint)
            {
                if (sculptHoverValid) DrawSculptBrush();
                DrawSculptMaskVisualization(sceneView);
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && sculptHoverValid)
            {
                BeginSculptStroke();
                if (sculpting && IsSculptDragMode)
                {
                    if (!InitializeSculptGrab(sceneView, currentEvent.mousePosition))
                        EndSculptStroke(false);
                }
                else
                {
                    ApplySculptSample(sculptHoverPoint, sculptHoverNormal);
                }
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0 && sculpting)
            {
                if (sculptGrabInitialized)
                {
                    if (!mouseOverAnyWindow && !currentEvent.alt)
                        ApplySculptGrab(currentEvent.mousePosition);
                }
                else if (currentHitValid)
                {
                    ApplyInterpolatedSculptSample(sculptHoverPoint, sculptHoverNormal);
                }
                currentEvent.Use();
            }
            else if ((currentEvent.rawType == EventType.MouseUp || currentEvent.type == EventType.MouseUp) && sculpting)
            {
                EndSculptStroke(true);
                currentEvent.Use();
            }
            else if ((currentEvent.type == EventType.MouseLeaveWindow || currentEvent.type == EventType.Ignore) && sculpting)
            {
                EndSculptStroke(true);
            }
            if (sculpting || sculptHoverValid) sceneView.Repaint();
        }

        private bool TryGetSculptHit(Vector2 guiPoint, out Vector3 point, out Vector3 normal, out Vector3 tangent)
        {
            point = Vector3.zero; normal = Vector3.up; tangent = sculptHoverTangent;
            sculptHoverConnectedComponent = -1;
            if (sculptSlot == null || VertexObject == null || BakedMesh == null || !phyScene.IsValid()) return false;
            Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            RaycastHit[] hits = new RaycastHit[32];
            int count = phyScene.Raycast(ray.origin, ray.direction, hits, 10000f);
            Array.Sort(hits, 0, count, RaycastHitDistanceComparer.Instance);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null || hit.collider.gameObject != VertexObject) continue;
                SlotData hitSlot = GetSlotForTriangle(hit.triangleIndex);
                if (hitSlot == null) continue;
                if (!IsSculptSlotIncludedInTarget(hitSlot)) continue;
                if (!IsSculptMultiSlotMode && (sculptSlot == null || hitSlot.slotName != sculptSlot.slotName)) continue;
                if (IsSculptMultiSlotMode && !ReferenceEquals(hitSlot, sculptSlot))
                {
                    EndSculptStroke(true);
                    ActivateSculptSlot(hitSlot);
                }
                if (sculptSlot == null || sculptOriginalVertices == null) continue;
                point = hit.point;
                Vector3 localPoint = VertexObject.transform.InverseTransformPoint(point);
                Vector3[] vertices = BakedMesh.vertices;
                Vector3[] normals = BakedMesh.normals;
                Vector3 average = Vector3.zero; float total = 0f;
                for (int v = 0; v < sculptSlotVertexCount; v++)
                {
                    float d = Vector3.Distance(vertices[sculptSlotStart + v], localPoint);
                    if (d > sculptRadius) continue;
                    float w = EvaluateSculptFalloff(d / sculptRadius);
                    average += normals[sculptSlotStart + v] * w; total += w;
                }
                Vector3 localNormal = total > 1e-6f ? (average / total).normalized : VertexObject.transform.InverseTransformDirection(hit.normal).normalized;
                normal = VertexObject.transform.TransformDirection(localNormal).normalized;
                int[] tris = BakedMesh.triangles; int ti = hit.triangleIndex * 3;
                if (ti >= 0 && ti < tris.Length)
                {
                    int localHitVertex = tris[ti] - sculptSlotStart;
                    if (sculptConnectedComponents != null && localHitVertex >= 0 && localHitVertex < sculptConnectedComponents.Length)
                        sculptHoverConnectedComponent = sculptConnectedComponents[localHitVertex];
                }
                Vector3 edge = ti + 1 < tris.Length ? vertices[tris[ti + 1]] - vertices[tris[ti]] : Vector3.right;
                Vector3 worldEdge = VertexObject.transform.TransformVector(edge);
                tangent = Vector3.ProjectOnPlane(worldEdge, normal).normalized;
                if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.ProjectOnPlane(sculptHoverTangent, normal).normalized;
                if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.Cross(normal, Vector3.up).normalized;
                if (Vector3.Dot(tangent, sculptHoverTangent) < 0f) tangent = -tangent;
                return true;
            }
            return false;
        }

        private static Vector3 ReflectLocalX(Vector3 value)
        {
            value.x = -value.x;
            return value;
        }

        private Vector3 MirrorWorldPointAcrossLocalX(Vector3 worldPoint)
        {
            Vector3 localPoint = VertexObject.transform.InverseTransformPoint(worldPoint);
            return VertexObject.transform.TransformPoint(ReflectLocalX(localPoint));
        }

        private Vector3 MirrorWorldDirectionAcrossLocalX(Vector3 worldDirection)
        {
            Vector3 localDirection = VertexObject.transform.InverseTransformDirection(worldDirection);
            return VertexObject.transform.TransformDirection(ReflectLocalX(localDirection)).normalized;
        }

        private void DrawSculptBrush()
        {
            Vector3 bitangent = Vector3.Cross(sculptHoverNormal, sculptHoverTangent).normalized;
            Color color = new Color(1f, .08f, .05f, 1f);
            UnityEngine.Rendering.CompareFunction previousZTest = Handles.zTest;
            try
            {
                using (new Handles.DrawingScope(color))
                {
                    // Keep the surface-oriented brush readable even where neighboring
                    // triangles or overlapping clothing would otherwise depth-clip it.
                    Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                    DrawSculptBrushAt(sculptHoverPoint, sculptHoverNormal, sculptHoverTangent, bitangent);
                    if (sculptSymmetryX)
                    {
                        Vector3 mirroredPoint = MirrorWorldPointAcrossLocalX(sculptHoverPoint);
                        if ((mirroredPoint - sculptHoverPoint).sqrMagnitude > 0.0000000001f)
                        {
                            DrawSculptBrushAt(
                                mirroredPoint,
                                MirrorWorldDirectionAcrossLocalX(sculptHoverNormal),
                                MirrorWorldDirectionAcrossLocalX(sculptHoverTangent),
                                MirrorWorldDirectionAcrossLocalX(bitangent));
                        }
                    }
                }
            }
            finally
            {
                Handles.zTest = previousZTest;
            }
        }

        private void DrawSculptBrushAt(Vector3 point, Vector3 normal, Vector3 tangent, Vector3 bitangent)
        {
            Vector3[] ring = new Vector3[65];
            for (int i = 0; i < ring.Length; i++)
            {
                float a = i / 64f * Mathf.PI * 2f;
                ring[i] = point + (tangent * Mathf.Cos(a) + bitangent * Mathf.Sin(a)) * sculptRadius;
            }
            Handles.DrawAAPolyLine(5f, ring);
            Handles.DrawAAPolyLine(3.5f, point, point + normal * sculptRadius * .45f);
        }

        private void DrawSculptMaskVisualization(SceneView sceneView)
        {
            if (sculptMask == null || BakedMesh == null || VertexObject == null || sceneView == null || sceneView.camera == null || sculptSlotStart < 0)
                return;
            Vector3[] vertices = BakedMesh.vertices;
            if (sculptSlotStart + sculptMask.Length > vertices.Length) return;

            Handles.BeginGUI();
            for (int i = 0; i < sculptMask.Length; i++)
            {
                float mask = sculptMask[i];
                if (mask <= .01f) continue;
                Vector3 world = VertexObject.transform.TransformPoint(vertices[sculptSlotStart + i]);
                Vector3 gui = HandleUtility.WorldToGUIPointWithDepth(world);
                if (gui.z <= 0f || gui.x < 0f || gui.y < 0f || gui.x > sceneView.position.width || gui.y > sceneView.position.height) continue;

                float size = Mathf.Lerp(4f, 7f, mask);
                Rect outline = new Rect(gui.x - (size + 2f) * .5f, gui.y - (size + 2f) * .5f, size + 2f, size + 2f);
                Rect square = new Rect(gui.x - size * .5f, gui.y - size * .5f, size, size);
                EditorGUI.DrawRect(outline, new Color(0.12f, 0f, 0f, Mathf.Lerp(.35f, .9f, mask)));
                EditorGUI.DrawRect(square, new Color(1f, .03f, .02f, Mathf.Lerp(.35f, 1f, mask)));
            }
            Handles.EndGUI();
        }

        private void BeginSculptStroke()
        {
            if (sculptOriginalVertices == null) return;
            ClearSculptGrabState();
            Undo.IncrementCurrentGroup();
            sculptUndoGroup = Undo.GetCurrentGroup();
            string undoName = sculptMaskTool != SculptMaskTool.None
                ? "Paint Sculpt Mask"
                : IsSculptDragMode ? ObjectNames.NicifyVariableName(sculptTool.ToString()) + " Mesh" : "Sculpt Mesh";
            Undo.SetCurrentGroupName(undoName);
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { this, BakedMesh }, undoName);
            Array.Clear(sculptStrokeApplied, 0, sculptStrokeApplied.Length);
            Array.Clear(sculptStrokeLimit, 0, sculptStrokeLimit.Length);
            sculptStrokePlanePoint = VertexObject.transform.InverseTransformPoint(sculptHoverPoint);
            sculptStrokePlaneNormal = VertexObject.transform.InverseTransformDirection(sculptHoverNormal).normalized;
            sculpting = true; sculptHasLastSample = false;
        }

        // Cloth can join this anchored-drag path later without changing the stroke lifecycle.
        private bool IsSculptDragMode => sculptMaskTool == SculptMaskTool.None &&
            (sculptTool == SculptTool.Grab || sculptTool == SculptTool.Boundary || sculptTool == SculptTool.ElasticDeform);

        private bool InitializeSculptGrab(SceneView sceneView, Vector2 guiPoint)
        {
            if (!sculpting || sceneView == null || sceneView.camera == null || VertexObject == null ||
                BakedMesh == null || sculptSlotStart < 0 || sculptSlotVertexCount <= 0 ||
                sculptSlotStart + sculptSlotVertexCount > BakedMesh.vertexCount)
            {
                return false;
            }

            Vector3 planeNormal = sceneView.camera.transform.forward;
            if (planeNormal.sqrMagnitude <= 0.000000000001f)
            {
                return false;
            }

            sculptGrabPlane = new Plane(planeNormal.normalized, sculptHoverPoint);
            Ray startRay = HandleUtility.GUIPointToWorldRay(guiPoint);
            if (!sculptGrabPlane.Raycast(startRay, out float enter))
            {
                return false;
            }

            sculptGrabStartPlanePoint = startRay.GetPoint(enter);
            sculptGrabStartBrushPoint = sculptHoverPoint;
            sculptGrabStartGuiPoint = guiPoint;
            sculptGrabStartLocalPoint = VertexObject.transform.InverseTransformPoint(sculptGrabStartBrushPoint);
            sculptGrabStartLocalNormal = VertexObject.transform.InverseTransformDirection(sculptHoverNormal).normalized;
            sculptGrabStartLocalTangent = VertexObject.transform.InverseTransformDirection(sculptHoverTangent).normalized;
            sculptGrabStartVertices = new Vector3[sculptSlotVertexCount];
            Vector3[] vertices = BakedMesh.vertices;
            Array.Copy(vertices, sculptSlotStart, sculptGrabStartVertices, 0, sculptSlotVertexCount);

            sculptGrabWeights = new float[sculptSlotVertexCount];
            sculptGrabMirroredWeights = sculptSymmetryX ? new float[sculptSlotVertexCount] : null;
            Vector3 localPoint = sculptGrabStartLocalPoint;
            Vector3 mirroredPoint = ReflectLocalX(localPoint);
            int connectedComponent = sculptConnectedOnly ? sculptHoverConnectedComponent : -1;
            int mirroredComponent = sculptConnectedOnly && sculptGrabMirroredWeights != null
                ? FindSculptConnectedComponentAtPoint(mirroredPoint)
                : -1;
            float strength = sculptStrengthPercent * 0.01f;

            if (sculptTool == SculptTool.Boundary)
            {
                if (!BuildSculptBoundaryWeights(localPoint, connectedComponent, strength, sculptGrabWeights))
                {
                    return false;
                }
                if (sculptGrabMirroredWeights != null)
                    BuildSculptBoundaryWeights(mirroredPoint, mirroredComponent, strength, sculptGrabMirroredWeights);
                sculptGrabInitialized = true;
                return true;
            }

            for (int i = 0; i < sculptSlotVertexCount; i++)
            {
                float unmaskedStrength = strength * (1f - sculptMask[i]);
                bool primaryConnected = !sculptConnectedOnly ||
                    (connectedComponent >= 0 && sculptConnectedComponents != null &&
                     sculptConnectedComponents[i] == connectedComponent);
                float distance = Vector3.Distance(sculptGrabStartVertices[i], localPoint);
                if (primaryConnected && distance <= sculptRadius)
                {
                    sculptGrabWeights[i] = EvaluateSculptFalloff(distance / sculptRadius) * unmaskedStrength;
                }
                if (sculptGrabMirroredWeights != null)
                {
                    float mirroredDistance = Vector3.Distance(sculptGrabStartVertices[i], mirroredPoint);
                    bool mirroredConnected = !sculptConnectedOnly ||
                        (mirroredComponent >= 0 && sculptConnectedComponents != null &&
                         sculptConnectedComponents[i] == mirroredComponent);
                    if (mirroredConnected && mirroredDistance <= sculptRadius)
                    {
                        sculptGrabMirroredWeights[i] = EvaluateSculptFalloff(mirroredDistance / sculptRadius) * unmaskedStrength;
                    }
                }
            }

            sculptGrabInitialized = true;
            return true;
        }

        private bool BuildSculptBoundaryWeights(Vector3 localPoint, int connectedComponent, float strength, float[] weights)
        {
            if (weights == null || sculptBoundaryVertices == null || sculptBoundaryVertices.Count == 0 ||
                sculptGrabStartVertices == null || sculptNeighbors == null)
            {
                return false;
            }

            int origin = -1;
            float closestDistance = float.PositiveInfinity;
            foreach (int boundaryVertex in sculptBoundaryVertices)
            {
                if (boundaryVertex < 0 || boundaryVertex >= sculptSlotVertexCount) continue;
                if (IsSculptMultiSlotMode && sculptCrossSlotSeamsBuilt &&
                    sculptCrossSlotSeams.ContainsKey(sculptSlotStart + boundaryVertex))
                {
                    // A per-slot open edge welded to another visible slot is not an exposed garment boundary.
                    continue;
                }
                if (sculptConnectedOnly &&
                    (connectedComponent < 0 || sculptConnectedComponents == null ||
                     sculptConnectedComponents[boundaryVertex] != connectedComponent))
                {
                    continue;
                }
                float distance = Vector3.Distance(sculptGrabStartVertices[boundaryVertex], localPoint);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    origin = boundaryVertex;
                }
            }
            if (origin < 0 || closestDistance > sculptRadius * 1.5f) return false;

            float[] distances = new float[sculptSlotVertexCount];
            bool[] queued = new bool[sculptSlotVertexCount];
            for (int i = 0; i < distances.Length; i++) distances[i] = float.PositiveInfinity;
            Queue<int> queue = new Queue<int>();
            distances[origin] = 0f;
            queue.Enqueue(origin);
            queued[origin] = true;

            while (queue.Count > 0)
            {
                int vertex = queue.Dequeue();
                queued[vertex] = false;
                List<int> neighbors = sculptNeighbors[vertex];
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int neighbor = neighbors[i];
                    if (sculptConnectedOnly && sculptConnectedComponents != null &&
                        sculptConnectedComponents[neighbor] != connectedComponent)
                    {
                        continue;
                    }
                    float candidate = distances[vertex] +
                        Vector3.Distance(sculptGrabStartVertices[vertex], sculptGrabStartVertices[neighbor]);
                    if (candidate >= distances[neighbor] || candidate > sculptRadius) continue;
                    distances[neighbor] = candidate;
                    if (!queued[neighbor])
                    {
                        queue.Enqueue(neighbor);
                        queued[neighbor] = true;
                    }
                }
            }

            bool hasWeight = false;
            for (int i = 0; i < distances.Length; i++)
            {
                if (float.IsInfinity(distances[i]) || distances[i] > sculptRadius) continue;
                weights[i] = EvaluateSculptFalloff(distances[i] / Mathf.Max(sculptRadius, 0.000001f)) *
                    strength * (1f - sculptMask[i]);
                hasWeight |= weights[i] > 0f;
            }
            return hasWeight;
        }

        private void ApplySculptGrab(Vector2 guiPoint)
        {
            if (!sculpting || !sculptGrabInitialized || VertexObject == null || BakedMesh == null ||
                sculptGrabStartVertices == null || sculptGrabWeights == null)
            {
                return;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            if (!sculptGrabPlane.Raycast(ray, out float enter))
            {
                return;
            }

            Vector3 currentPlanePoint = ray.GetPoint(enter);
            Vector3 worldDelta = currentPlanePoint - sculptGrabStartPlanePoint;
            Vector3 localDelta = VertexObject.transform.InverseTransformVector(worldDelta);
            Vector2 guiDelta = guiPoint - sculptGrabStartGuiPoint;
            Vector3[] vertices = BakedMesh.vertices;
            bool[] processed = new bool[sculptSlotVertexCount];

            for (int i = 0; i < sculptSlotVertexCount; i++)
            {
                if (processed[i]) continue;
                List<int> coincident = sculptCoincidentVertices != null && sculptCoincidentVertices[i] != null
                    ? sculptCoincidentVertices[i]
                    : new List<int> { i };
                float primaryWeight = 0f;
                float mirroredWeight = 0f;
                for (int coincidentIndex = 0; coincidentIndex < coincident.Count; coincidentIndex++)
                {
                    int localIndex = coincident[coincidentIndex];
                    if (localIndex < 0 || localIndex >= sculptSlotVertexCount) continue;
                    processed[localIndex] = true;
                    primaryWeight = Mathf.Max(primaryWeight, sculptGrabWeights[localIndex]);
                    if (sculptGrabMirroredWeights != null)
                        mirroredWeight = Mathf.Max(mirroredWeight, sculptGrabMirroredWeights[localIndex]);
                }

                float totalWeight = primaryWeight + mirroredWeight;
                if (totalWeight <= 0f) continue;

                int referenceIndex = coincident[0];
                Vector3 startPosition = sculptGrabStartVertices[referenceIndex];
                Vector3 grabbedPosition;
                if (primaryWeight > 0f && mirroredWeight > 0f)
                {
                    Vector3 primaryTarget = EvaluateSculptDragTarget(
                        referenceIndex, startPosition, primaryWeight, localDelta, guiDelta, false);
                    Vector3 mirroredTarget = EvaluateSculptDragTarget(
                        referenceIndex, startPosition, mirroredWeight, localDelta, guiDelta, true);
                    grabbedPosition = Vector3.Lerp(primaryTarget, mirroredTarget, mirroredWeight / totalWeight);
                }
                else if (primaryWeight > 0f)
                {
                    grabbedPosition = EvaluateSculptDragTarget(
                        referenceIndex, startPosition, primaryWeight, localDelta, guiDelta, false);
                }
                else
                {
                    grabbedPosition = EvaluateSculptDragTarget(
                        referenceIndex, startPosition, mirroredWeight, localDelta, guiDelta, true);
                }

                for (int coincidentIndex = 0; coincidentIndex < coincident.Count; coincidentIndex++)
                {
                    int localIndex = coincident[coincidentIndex];
                    if (localIndex >= 0 && localIndex < sculptSlotVertexCount)
                        vertices[sculptSlotStart + localIndex] = grabbedPosition;
                }
                SynchronizeCrossSlotSculptPosition(coincident, grabbedPosition, vertices);
            }

            BakedMesh.vertices = vertices;
            BakedMesh.RecalculateBounds();
            if (sculptUpdateNormalsWhileSculpting)
            {
                BakedMesh.RecalculateNormals();
                SynchronizeCrossSlotSculptNormals();
            }
            RefreshBakedMeshCaches();
            RefreshSculptCollider();
            sculptHoverPoint = sculptGrabStartBrushPoint + worldDelta;
            EditorUtility.SetDirty(BakedMesh);
            EditorUtility.SetDirty(this);
        }

        private Vector3 EvaluateSculptDragTarget(
            int localIndex,
            Vector3 startPosition,
            float weight,
            Vector3 primaryLocalDelta,
            Vector2 guiDelta,
            bool mirrored)
        {
            Vector3 localDelta = mirrored ? ReflectLocalX(primaryLocalDelta) : primaryLocalDelta;
            Vector3 origin = mirrored ? ReflectLocalX(sculptGrabStartLocalPoint) : sculptGrabStartLocalPoint;
            Vector3 normal = mirrored ? ReflectLocalX(sculptGrabStartLocalNormal).normalized : sculptGrabStartLocalNormal;
            Vector3 tangent = mirrored ? ReflectLocalX(sculptGrabStartLocalTangent).normalized : sculptGrabStartLocalTangent;
            Vector3 radial = startPosition - origin;
            float signedDrag = Vector3.Dot(localDelta, tangent) / Mathf.Max(sculptRadius, 0.000001f);
            if (Mathf.Abs(signedDrag) < 0.0001f)
                signedDrag = (guiDelta.x - guiDelta.y) * 0.0025f;
            float mirroredRotationSign = mirrored ? -1f : 1f;

            if (sculptTool == SculptTool.Grab)
                return startPosition + localDelta * weight;

            if (sculptTool == SculptTool.ElasticDeform)
            {
                switch (sculptElasticMode)
                {
                    case SculptElasticMode.Scale:
                        return origin + radial * Mathf.Max(0.05f, 1f + signedDrag * weight);
                    case SculptElasticMode.Twist:
                        return origin + Quaternion.AngleAxis(signedDrag * 120f * weight * mirroredRotationSign, normal) * radial;
                    default:
                    {
                        Vector3 target = startPosition + localDelta * weight;
                        if (sculptElasticVolumePreservation > 0f && localDelta.sqrMagnitude > 0.000000000001f)
                        {
                            Vector3 deltaDirection = localDelta.normalized;
                            Vector3 perpendicular = radial - Vector3.Project(radial, deltaDirection);
                            if (perpendicular.sqrMagnitude > 0.000000000001f)
                            {
                                target += perpendicular.normalized * localDelta.magnitude * weight *
                                    (1f - Mathf.Clamp01(weight)) * sculptElasticVolumePreservation * 0.35f;
                            }
                        }
                        return target;
                    }
                }
            }

            if (sculptTool == SculptTool.Boundary)
            {
                switch (sculptBoundaryMode)
                {
                    case SculptBoundaryMode.Bend:
                        return origin + Quaternion.AngleAxis(signedDrag * 90f * weight * mirroredRotationSign, tangent) * radial;
                    case SculptBoundaryMode.Expand:
                    {
                        Vector3 planarRadial = Vector3.ProjectOnPlane(radial, normal);
                        Vector3 normalRadial = radial - planarRadial;
                        return origin + normalRadial + planarRadial * Mathf.Max(0.05f, 1f + signedDrag * weight);
                    }
                    case SculptBoundaryMode.Inflate:
                        return startPosition + normal * signedDrag * sculptRadius * weight;
                    case SculptBoundaryMode.Twist:
                        return origin + Quaternion.AngleAxis(signedDrag * 120f * weight * mirroredRotationSign, normal) * radial;
                    case SculptBoundaryMode.Smooth:
                    {
                        List<int> neighbors = sculptNeighbors != null && localIndex >= 0 && localIndex < sculptNeighbors.Length
                            ? sculptNeighbors[localIndex]
                            : null;
                        if (neighbors == null || neighbors.Count == 0) return startPosition;
                        Vector3 average = Vector3.zero;
                        for (int i = 0; i < neighbors.Count; i++) average += sculptGrabStartVertices[neighbors[i]];
                        average /= neighbors.Count;
                        float blend = Mathf.Clamp01((localDelta.magnitude / Mathf.Max(sculptRadius, 0.000001f) +
                            guiDelta.magnitude * 0.0025f) * weight);
                        return Vector3.Lerp(startPosition, average, blend);
                    }
                    default:
                        return startPosition + localDelta * weight;
                }
            }

            return startPosition;
        }

        private void ClearSculptGrabState()
        {
            sculptGrabInitialized = false;
            sculptGrabStartVertices = null;
            sculptGrabWeights = null;
            sculptGrabMirroredWeights = null;
        }

        private void ApplyInterpolatedSculptSample(Vector3 point, Vector3 normal)
        {
            float spacing = Mathf.Max(.0001f, sculptRadius * .2f);
            if (!sculptHasLastSample) { ApplySculptSample(point, normal); return; }
            float distance = Vector3.Distance(sculptLastSamplePoint, point);
            int steps = Mathf.Clamp(Mathf.CeilToInt(distance / spacing), 1, 32);
            Vector3 start = sculptLastSamplePoint;
            for (int i = 1; i <= steps; i++) ApplySculptSample(Vector3.Lerp(start, point, i / (float)steps), normal);
        }

        private void ApplySculptSample(Vector3 worldPoint, Vector3 worldNormal)
        {
            if (!sculpting || sculptOriginalVertices == null || IsSculptDragMode) return;
            Vector3[] vertices = BakedMesh.vertices;
            Vector3 localPoint = VertexObject.transform.InverseTransformPoint(worldPoint);
            Vector3 localNormal = VertexObject.transform.InverseTransformDirection(worldNormal).normalized;
            float strength = sculptStrengthPercent * .01f;
            float maxEffect = sculptRadius * strength;
            Vector3[] before = (Vector3[])vertices.Clone();
            int connectedComponent = sculptConnectedOnly ? sculptHoverConnectedComponent : -1;
            Vector3 planePoint = sculptTool == SculptTool.Plane ? sculptStrokePlanePoint : localPoint;
            Vector3 planeNormal = sculptTool == SculptTool.Plane ? sculptStrokePlaneNormal : localNormal;
            ApplySculptBrushPass(
                localPoint, localNormal, maxEffect, vertices, before, connectedComponent, planePoint, planeNormal);
            if (sculptSymmetryX)
            {
                Vector3 mirroredPoint = ReflectLocalX(localPoint);
                if ((mirroredPoint - localPoint).sqrMagnitude > 0.0000000001f)
                {
                    int mirroredComponent = sculptConnectedOnly
                        ? FindSculptConnectedComponentAtPoint(mirroredPoint)
                        : -1;
                    ApplySculptBrushPass(
                        mirroredPoint,
                        ReflectLocalX(localNormal).normalized,
                        maxEffect,
                        vertices,
                        before,
                        mirroredComponent,
                        ReflectLocalX(planePoint),
                        ReflectLocalX(planeNormal).normalized);
                }
            }
            BakedMesh.vertices = vertices;
            BakedMesh.RecalculateBounds();
            if (sculptUpdateNormalsWhileSculpting)
            {
                BakedMesh.RecalculateNormals();
                SynchronizeCrossSlotSculptNormals();
            }
            RefreshBakedMeshCaches();
            RefreshSculptCollider();
            sculptLastSamplePoint = worldPoint; sculptHasLastSample = true;
            EditorUtility.SetDirty(BakedMesh); EditorUtility.SetDirty(this);
        }

        private void ApplySculptBrushPass(
            Vector3 localPoint,
            Vector3 localNormal,
            float maxEffect,
            Vector3[] vertices,
            Vector3[] samplePositions,
            int connectedComponent,
            Vector3 deformationPoint,
            Vector3 deformationNormal)
        {
            if (sculptConnectedOnly && connectedComponent < 0) return;
            for (int i = 0; i < sculptSlotVertexCount; i++)
            {
                if (sculptConnectedOnly &&
                    (sculptConnectedComponents == null || sculptConnectedComponents[i] != connectedComponent))
                {
                    continue;
                }
                int baked = sculptSlotStart + i;
                float distance = Vector3.Distance(samplePositions[baked], localPoint);
                if (distance > sculptRadius) continue;
                float falloff = EvaluateSculptFalloff(distance / sculptRadius);
                ApplySculptVertex(
                    i, falloff, maxEffect, localPoint, localNormal, deformationPoint, deformationNormal,
                    vertices, samplePositions);
            }
        }

        private void ApplySculptVertex(
            int index,
            float falloff,
            float maxEffect,
            Vector3 brushPoint,
            Vector3 brushNormal,
            Vector3 deformationPoint,
            Vector3 deformationNormal,
            Vector3[] vertices,
            Vector3[] before)
        {
            if (index < 0 || index >= sculptSlotVertexCount) return;
            List<int> coincident = sculptCoincidentVertices != null && sculptCoincidentVertices[index] != null
                ? sculptCoincidentVertices[index]
                : new List<int> { index };
            float desiredLimit = Mathf.Max(0f, falloff) * maxEffect;
            if (sculptMaskTool == SculptMaskTool.None) desiredLimit *= 1f - sculptMask[index];
            float applied = 0f;
            for (int i = 0; i < coincident.Count; i++)
            {
                int weldedIndex = coincident[i];
                sculptStrokeLimit[weldedIndex] = Mathf.Max(sculptStrokeLimit[weldedIndex], desiredLimit);
                applied = Mathf.Max(applied, sculptStrokeApplied[weldedIndex]);
            }
            float amount = Mathf.Max(0f, desiredLimit - applied);
            if (amount <= 0f) return;
            if (sculptMaskTool != SculptMaskTool.None)
            {
                float delta = maxEffect > 1e-7f ? amount / maxEffect : 0f;
                for (int i = 0; i < coincident.Count; i++)
                {
                    int weldedIndex = coincident[i];
                    sculptMask[weldedIndex] = Mathf.Clamp01(sculptMask[weldedIndex] + (sculptMaskTool == SculptMaskTool.Paint ? delta : -delta));
                    sculptStrokeApplied[weldedIndex] += amount;
                }
                SynchronizeCrossSlotSculptMask(coincident, sculptMask[coincident[0]]);
                return;
            }
            if (sculptTool == SculptTool.Smooth)
            {
                HashSet<int> neighborSet = new HashSet<int>();
                for (int i = 0; i < coincident.Count; i++)
                {
                    List<int> neighbors = sculptNeighbors[coincident[i]];
                    for (int n = 0; n < neighbors.Count; n++)
                        if (!coincident.Contains(neighbors[n])) neighborSet.Add(neighbors[n]);
                }
                if (neighborSet.Count == 0) return;
                Vector3 average = Vector3.zero;
                foreach (int neighbor in neighborSet) average += before[sculptSlotStart + neighbor];
                average /= neighborSet.Count;
                float blend = Mathf.Clamp01(amount / Mathf.Max(sculptRadius, 1e-6f));
                Vector3 smoothedPosition = Vector3.Lerp(before[sculptSlotStart + coincident[0]], average, blend);
                for (int i = 0; i < coincident.Count; i++) vertices[sculptSlotStart + coincident[i]] = smoothedPosition;
                SynchronizeCrossSlotSculptPosition(coincident, smoothedPosition, vertices);
            }
            else if (sculptTool == SculptTool.Pinch || sculptTool == SculptTool.Crease)
            {
                Vector3 sourcePosition = vertices[sculptSlotStart + coincident[0]];
                Vector3 towardCenter = Vector3.ProjectOnPlane(brushPoint - sourcePosition, brushNormal);
                Vector3 displacement = towardCenter.sqrMagnitude > 0.000000000001f
                    ? towardCenter.normalized * amount * sculptPinchStrength
                    : Vector3.zero;
                if (sculptTool == SculptTool.Crease)
                    displacement += brushNormal * amount * sculptCreaseDepth;
                Vector3 pinchedPosition = sourcePosition + displacement;
                for (int i = 0; i < coincident.Count; i++) vertices[sculptSlotStart + coincident[i]] = pinchedPosition;
                SynchronizeCrossSlotSculptPosition(coincident, pinchedPosition, vertices);
            }
            else if (sculptTool == SculptTool.Plane)
            {
                Vector3 sourcePosition = vertices[sculptSlotStart + coincident[0]];
                Vector3 planeNormal = deformationNormal.sqrMagnitude > 0.000000000001f
                    ? deformationNormal.normalized
                    : brushNormal;
                float signedDistance = Vector3.Dot(sourcePosition - deformationPoint, planeNormal);
                if ((sculptPlaneMode == SculptPlaneMode.Fill && signedDistance >= 0f) ||
                    (sculptPlaneMode == SculptPlaneMode.Scrape && signedDistance <= 0f))
                {
                    return;
                }
                Vector3 moveToPlane = -planeNormal * signedDistance;
                if (moveToPlane.magnitude > amount) moveToPlane = moveToPlane.normalized * amount;
                Vector3 planarPosition = sourcePosition + moveToPlane;
                for (int i = 0; i < coincident.Count; i++) vertices[sculptSlotStart + coincident[i]] = planarPosition;
                SynchronizeCrossSlotSculptPosition(coincident, planarPosition, vertices);
            }
            else
            {
                Vector3 displacement = brushNormal * amount * (sculptTool == SculptTool.Add ? 1f : -1f);
                Vector3 weldedPosition = vertices[sculptSlotStart + coincident[0]] + displacement;
                for (int i = 0; i < coincident.Count; i++) vertices[sculptSlotStart + coincident[i]] = weldedPosition;
                SynchronizeCrossSlotSculptPosition(coincident, weldedPosition, vertices);
            }
            for (int i = 0; i < coincident.Count; i++) sculptStrokeApplied[coincident[i]] += amount;
        }

        private void RefreshSculptCollider()
        {
            MeshCollider collider = VertexObject != null ? VertexObject.GetComponent<MeshCollider>() : null;
            if (collider != null) { collider.sharedMesh = null; collider.sharedMesh = BakedMesh; }
        }

        private void EndSculptStroke(bool finalize)
        {
            if (!sculpting)
            {
                ClearSculptGrabState();
                return;
            }
            sculpting = false; sculptHasLastSample = false;
            if (finalize && BakedMesh != null)
            {
                BakedMesh.RecalculateNormals();
                SynchronizeCrossSlotSculptNormals();
                BakedMesh.RecalculateBounds();
                RefreshBakedMeshCaches();
                RefreshSculptCollider();
                RepaintLinkedEditors(); SceneView.RepaintAll();
            }
            if (sculptUndoGroup >= 0) Undo.CollapseUndoOperations(sculptUndoGroup);
            sculptUndoGroup = -1;
            ClearSculptGrabState();
        }

        private void HandleVertexPaintSceneGUI(SceneView sceneView, Event currentEvent, bool mouseOverAnyWindow)
        {
            EnsureVertexPaintSession();
            if (currentEvent.type == EventType.Layout && !mouseOverAnyWindow && !currentEvent.alt)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(GetHashCode() ^ 0x47A11, FocusType.Passive));
            }

            bool wasVertexPainting = vertexPainting;
            bool currentHitValid = false;
            if (!mouseOverAnyWindow && !currentEvent.alt &&
                TryGetVertexPaintHit(currentEvent.mousePosition, out Vector3 hitPoint, out Vector3 hitNormal, out Vector3 hitTangent))
            {
                vertexPaintHoverPoint = hitPoint;
                vertexPaintHoverNormal = hitNormal;
                vertexPaintHoverTangent = hitTangent;
                vertexPaintHoverValid = true;
                currentHitValid = true;
            }
            else if (!vertexPainting)
            {
                vertexPaintHoverValid = false;
            }

            if (wasVertexPainting && !vertexPainting && currentHitValid && currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
            {
                BeginVertexPaintStroke();
            }

            if (currentEvent.type == EventType.Repaint && vertexPaintHoverValid)
            {
                DrawVertexPaintBrush();
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && currentHitValid)
            {
                BeginVertexPaintStroke();
                ApplyVertexPaintSample(vertexPaintHoverPoint, vertexPaintHoverNormal, vertexPaintHoverTangent);
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0 && vertexPainting)
            {
                if (currentHitValid)
                {
                    ApplyInterpolatedVertexPaintSample(vertexPaintHoverPoint, vertexPaintHoverNormal, vertexPaintHoverTangent);
                }
                currentEvent.Use();
            }
            else if ((currentEvent.rawType == EventType.MouseUp || currentEvent.type == EventType.MouseUp) && vertexPainting)
            {
                EndVertexPaintStroke(true);
                currentEvent.Use();
            }
            else if ((currentEvent.type == EventType.MouseLeaveWindow || currentEvent.type == EventType.Ignore) && vertexPainting)
            {
                EndVertexPaintStroke(true);
            }

            if (vertexPainting || vertexPaintHoverValid)
            {
                sceneView.Repaint();
            }
        }

        private bool TryGetVertexPaintHit(Vector2 guiPoint, out Vector3 point, out Vector3 normal, out Vector3 tangent)
        {
            point = Vector3.zero;
            normal = Vector3.up;
            tangent = vertexPaintHoverTangent;
            if (vertexPaintSlot == null || VertexObject == null || BakedMesh == null || !phyScene.IsValid())
            {
                return false;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            RaycastHit[] hits = new RaycastHit[32];
            int count = phyScene.Raycast(ray.origin, ray.direction, hits, 10000f);
            Array.Sort(hits, 0, count, RaycastHitDistanceComparer.Instance);
            for (int hitIndex = 0; hitIndex < count; hitIndex++)
            {
                RaycastHit hit = hits[hitIndex];
                if (hit.collider == null || hit.collider.gameObject != VertexObject)
                {
                    continue;
                }

                SlotData hitSlot = GetSlotForTriangle(hit.triangleIndex);
                if (hitSlot == null)
                {
                    continue;
                }
                if (!IsVertexPaintAllSlotsMode && (vertexPaintSlot == null || hitSlot.slotName != vertexPaintSlot.slotName))
                {
                    continue;
                }
                if (IsVertexPaintAllSlotsMode && !ReferenceEquals(hitSlot, vertexPaintSlot))
                {
                    EndVertexPaintStroke(true);
                    ActivateVertexPaintSlot(hitSlot);
                }
                if (vertexPaintSlot == null || vertexPaintColors == null)
                {
                    continue;
                }

                point = hit.point;
                Vector3 localPoint = VertexObject.transform.InverseTransformPoint(point);
                Vector3[] vertices = BakedMesh.vertices;
                Vector3[] normals = BakedMesh.normals;
                Vector3 averageNormal = Vector3.zero;
                float totalWeight = 0f;
                for (int vertexIndex = 0; vertexIndex < vertexPaintSlotVertexCount; vertexIndex++)
                {
                    int bakedIndex = vertexPaintSlotStart + vertexIndex;
                    float distance = Vector3.Distance(vertices[bakedIndex], localPoint);
                    if (distance > vertexPaintRadius)
                    {
                        continue;
                    }

                    float weight = EvaluateVertexPaintFalloff(distance / Mathf.Max(vertexPaintRadius, 0.000001f));
                    averageNormal += normals[bakedIndex] * weight;
                    totalWeight += weight;
                }

                Vector3 localNormal = totalWeight > 0.000001f
                    ? (averageNormal / totalWeight).normalized
                    : VertexObject.transform.InverseTransformDirection(hit.normal).normalized;
                normal = VertexObject.transform.TransformDirection(localNormal).normalized;

                int[] triangles = BakedMesh.triangles;
                int triangleStart = hit.triangleIndex * 3;
                Vector3 edge = triangleStart + 1 < triangles.Length
                    ? vertices[triangles[triangleStart + 1]] - vertices[triangles[triangleStart]]
                    : Vector3.right;
                Vector3 worldEdge = VertexObject.transform.TransformVector(edge);
                tangent = Vector3.ProjectOnPlane(worldEdge, normal).normalized;
                if (tangent.sqrMagnitude < 0.000001f)
                {
                    tangent = Vector3.ProjectOnPlane(vertexPaintHoverTangent, normal).normalized;
                }
                if (tangent.sqrMagnitude < 0.000001f)
                {
                    tangent = Vector3.Cross(normal, Vector3.up).normalized;
                }
                if (Vector3.Dot(tangent, vertexPaintHoverTangent) < 0f)
                {
                    tangent = -tangent;
                }
                return true;
            }

            return false;
        }

        private void DrawVertexPaintBrush()
        {
            Vector3 bitangent = Vector3.Cross(vertexPaintHoverNormal, vertexPaintHoverTangent).normalized;
            Color toolColor = GetVertexPaintCursorColor();
            toolColor.a = 1f;
            UnityEngine.Rendering.CompareFunction previousZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            try
            {
                DrawVertexPaintBrushAt(
                    vertexPaintHoverPoint,
                    vertexPaintHoverNormal,
                    vertexPaintHoverTangent,
                    bitangent,
                    toolColor);
                if (vertexPaintSymmetryX)
                {
                    Vector3 mirroredPoint = MirrorWorldPointAcrossLocalX(vertexPaintHoverPoint);
                    if ((mirroredPoint - vertexPaintHoverPoint).sqrMagnitude > 0.0000000001f)
                    {
                        DrawVertexPaintBrushAt(
                            mirroredPoint,
                            MirrorWorldDirectionAcrossLocalX(vertexPaintHoverNormal),
                            MirrorWorldDirectionAcrossLocalX(vertexPaintHoverTangent),
                            MirrorWorldDirectionAcrossLocalX(bitangent),
                            toolColor);
                    }
                }
            }
            finally
            {
                Handles.zTest = previousZTest;
            }
        }

        private void DrawVertexPaintBrushAt(Vector3 point, Vector3 normal, Vector3 tangent, Vector3 bitangent, Color toolColor)
        {
            Vector3[] outline;
            if (vertexPaintBrushShape == VertexPaintBrushShape.Circle)
            {
                outline = new Vector3[65];
                for (int i = 0; i < outline.Length; i++)
                {
                    float angle = i / 64f * Mathf.PI * 2f;
                    outline[i] = point +
                        (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * vertexPaintRadius;
                }
            }
            else
            {
                outline = new[]
                {
                    point + (-tangent - bitangent) * vertexPaintRadius,
                    point + ( tangent - bitangent) * vertexPaintRadius,
                    point + ( tangent + bitangent) * vertexPaintRadius,
                    point + (-tangent + bitangent) * vertexPaintRadius,
                    point + (-tangent - bitangent) * vertexPaintRadius
                };
            }

            // A black/white halo keeps the cursor readable over every possible
            // vertex color, while the inner line still identifies the active tool.
            DrawVertexPaintBrushStroke(outline, point, normal, Color.black, 8f, 6f);
            DrawVertexPaintBrushStroke(outline, point, normal, Color.white, 5f, 4f);
            DrawVertexPaintBrushStroke(outline, point, normal, toolColor, 2.5f, 2f);
        }

        private void DrawVertexPaintBrushStroke(Vector3[] outline, Vector3 point, Vector3 normal, Color color, float outlineWidth, float normalWidth)
        {
            using (new Handles.DrawingScope(color))
            {
                Handles.DrawAAPolyLine(outlineWidth, outline);
                Handles.DrawAAPolyLine(
                    normalWidth,
                    point,
                    point + normal * vertexPaintRadius * 0.35f);
            }
        }

        private Color GetVertexPaintCursorColor()
        {
            switch (vertexPaintTool)
            {
                case VertexPaintTool.Paint: return vertexPaintBrushColor;
                case VertexPaintTool.Erase:
                case VertexPaintTool.Clear: return Color.white;
                case VertexPaintTool.Smear: return new Color(0.1f, 0.9f, 0.9f, 1f);
                case VertexPaintTool.Blur: return new Color(0.2f, 0.55f, 1f, 1f);
                case VertexPaintTool.Smooth: return new Color(0.2f, 0.85f, 0.45f, 1f);
                case VertexPaintTool.Burn: return new Color(1f, 0.25f, 0.05f, 1f);
                case VertexPaintTool.Dodge: return new Color(1f, 0.9f, 0.15f, 1f);
                case VertexPaintTool.Noise: return new Color(0.9f, 0.2f, 1f, 1f);
                default: return Color.white;
            }
        }

        private void BeginVertexPaintStroke()
        {
            if (vertexPaintColors == null || vertexPaintColors.Length == 0 || BakedMesh == null)
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            vertexPaintUndoGroup = Undo.GetCurrentGroup();
            string undoName = "Vertex Paint " + ObjectNames.NicifyVariableName(vertexPaintTool.ToString());
            Undo.SetCurrentGroupName(undoName);
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { this, BakedMesh }, undoName);
            Array.Clear(vertexPaintStrokeApplied, 0, vertexPaintStrokeApplied.Length);
            if (vertexPaintTool == VertexPaintTool.Smooth)
            {
                BuildVertexPaintWorldSmoothingMap(BakedMesh.vertices, BakedMesh.normals, vertexPaintSmoothRadius);
            }
            vertexPaintStrokeChanged = false;
            vertexPainting = true;
            vertexPaintHasLastSample = false;
        }

        private void ApplyInterpolatedVertexPaintSample(Vector3 point, Vector3 normal, Vector3 tangent)
        {
            float spacing = Mathf.Max(0.0001f, vertexPaintRadius * 0.18f);
            if (!vertexPaintHasLastSample)
            {
                ApplyVertexPaintSample(point, normal, tangent);
                return;
            }

            Vector3 start = vertexPaintLastSamplePoint;
            float distance = Vector3.Distance(start, point);
            int steps = Mathf.Clamp(Mathf.CeilToInt(distance / spacing), 1, 48);
            for (int step = 1; step <= steps; step++)
            {
                float t = step / (float)steps;
                ApplyVertexPaintSample(Vector3.Lerp(start, point, t), normal, tangent);
            }
        }

        private void ApplyVertexPaintSample(Vector3 worldPoint, Vector3 worldNormal, Vector3 worldTangent)
        {
            if (!vertexPainting || vertexPaintColors == null || vertexPaintSlot == null || BakedMesh == null)
            {
                return;
            }

            Vector3[] vertices = BakedMesh.vertices;
            Vector3[] normals = BakedMesh.normals;
            Vector3 localPoint = VertexObject.transform.InverseTransformPoint(worldPoint);
            Vector3 localNormal = VertexObject.transform.InverseTransformDirection(worldNormal).normalized;
            Vector3 localTangent = VertexObject.transform.InverseTransformDirection(worldTangent).normalized;
            localTangent = Vector3.ProjectOnPlane(localTangent, localNormal).normalized;
            if (localTangent.sqrMagnitude < 0.000001f)
            {
                localTangent = Vector3.Cross(localNormal, Vector3.up).normalized;
            }
            Vector3 localBitangent = Vector3.Cross(localNormal, localTangent).normalized;
            Vector3 strokeDirection = vertexPaintHasLastSample
                ? VertexObject.transform.InverseTransformVector(worldPoint - vertexPaintLastSamplePoint).normalized
                : Vector3.zero;

            Color32[] before = (Color32[])vertexPaintColors.Clone();
            float brushStrength = Mathf.Clamp01(vertexPaintStrengthPercent * 0.01f);
            bool anyChanged = ApplyVertexPaintBrushPass(
                localPoint,
                localNormal,
                localTangent,
                localBitangent,
                strokeDirection,
                brushStrength,
                before,
                vertices,
                normals);

            if (vertexPaintSymmetryX)
            {
                anyChanged |= ApplyVertexPaintBrushPass(
                    ReflectLocalX(localPoint),
                    ReflectLocalX(localNormal).normalized,
                    ReflectLocalX(localTangent).normalized,
                    ReflectLocalX(localBitangent).normalized,
                    ReflectLocalX(strokeDirection).normalized,
                    brushStrength,
                    before,
                    vertices,
                    normals);
            }

            if (anyChanged)
            {
                ApplyVertexPaintColorsToPreview(vertexPaintSlotStart, vertexPaintColors);
                EditorUtility.SetDirty(BakedMesh);
                EditorUtility.SetDirty(this);
                vertexPaintStrokeChanged = true;
            }

            vertexPaintLastSamplePoint = worldPoint;
            vertexPaintHasLastSample = true;
        }

        private bool ApplyVertexPaintBrushPass(
            Vector3 localPoint,
            Vector3 localNormal,
            Vector3 localTangent,
            Vector3 localBitangent,
            Vector3 strokeDirection,
            float brushStrength,
            Color32[] before,
            Vector3[] vertices,
            Vector3[] normals)
        {
            bool anyChanged = false;
            for (int vertexIndex = 0; vertexIndex < vertexPaintSlotVertexCount; vertexIndex++)
            {
                int bakedIndex = vertexPaintSlotStart + vertexIndex;
                Vector3 offset = vertices[bakedIndex] - localPoint;
                float u = Vector3.Dot(offset, localTangent);
                float v = Vector3.Dot(offset, localBitangent);
                float depth = Mathf.Abs(Vector3.Dot(offset, localNormal));
                if (depth > vertexPaintRadius)
                {
                    continue;
                }

                float normalizedDistance = vertexPaintBrushShape == VertexPaintBrushShape.Circle
                    ? Mathf.Sqrt(u * u + v * v) / Mathf.Max(vertexPaintRadius, 0.000001f)
                    : Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) / Mathf.Max(vertexPaintRadius, 0.000001f);
                if (normalizedDistance > 1f)
                {
                    continue;
                }

                if (normals != null && bakedIndex < normals.Length && Vector3.Dot(normals[bakedIndex], localNormal) < -0.1f)
                {
                    continue;
                }

                float mask = EvaluateVertexPaintBrushMask(u, v);
                if (mask <= 0.0001f)
                {
                    continue;
                }

                float falloff = EvaluateVertexPaintFalloff(normalizedDistance);
                float desired = Mathf.Clamp01(mask * falloff * brushStrength);
                if (vertexPaintTool == VertexPaintTool.Clear)
                {
                    desired = mask * falloff > 0.0001f ? 1f : 0f;
                }
                if (desired <= 0.00001f)
                {
                    continue;
                }

                if (ApplyVertexPaintVertex(vertexIndex, desired, before, strokeDirection, vertices))
                {
                    anyChanged = true;
                }
            }
            return anyChanged;
        }

        private bool ApplyVertexPaintVertex(int vertexIndex, float amount, Color32[] before, Vector3 strokeDirection, Vector3[] allVertices)
        {
            if (vertexIndex < 0 || vertexIndex >= vertexPaintSlotVertexCount)
            {
                return false;
            }

            List<int> coincident = vertexPaintCoincidentVertices != null && vertexPaintCoincidentVertices[vertexIndex] != null
                ? vertexPaintCoincidentVertices[vertexIndex]
                : null;
            if (coincident == null || coincident.Count == 0)
            {
                coincident = new List<int> { vertexIndex };
            }

            float alreadyApplied = 0f;
            for (int i = 0; i < coincident.Count; i++)
            {
                alreadyApplied = Mathf.Max(alreadyApplied, vertexPaintStrokeApplied[coincident[i]]);
            }
            amount = Mathf.Max(0f, amount - alreadyApplied);
            if (amount <= 0.00001f)
            {
                return false;
            }

            Color current = before[coincident[0]];
            Color target = current;
            switch (vertexPaintTool)
            {
                case VertexPaintTool.Paint:
                    target = new Color(
                        current.r * vertexPaintBrushColor.r,
                        current.g * vertexPaintBrushColor.g,
                        current.b * vertexPaintBrushColor.b,
                        vertexPaintAffectAlpha ? current.a * vertexPaintBrushColor.a : current.a);
                    break;
                case VertexPaintTool.Erase:
                    target = new Color(1f, 1f, 1f, vertexPaintAffectAlpha ? 1f : current.a);
                    break;
                case VertexPaintTool.Smear:
                    target = GetVertexPaintSmearColor(vertexIndex, before, strokeDirection, allVertices);
                    if (!vertexPaintAffectAlpha) target.a = current.a;
                    break;
                case VertexPaintTool.Blur:
                    target = GetVertexPaintNeighborAverage(vertexIndex, before);
                    if (!vertexPaintAffectAlpha) target.a = current.a;
                    break;
                case VertexPaintTool.Smooth:
                    target = GetVertexPaintWorldAverage(vertexIndex, before, vertexPaintSmoothRadius);
                    if (!vertexPaintAffectAlpha) target.a = current.a;
                    break;
                case VertexPaintTool.Burn:
                    target = new Color(current.r * 0.5f, current.g * 0.5f, current.b * 0.5f, current.a);
                    break;
                case VertexPaintTool.Dodge:
                    target = new Color(1f, 1f, 1f, current.a);
                    break;
                case VertexPaintTool.Noise:
                    target = GetVertexPaintNoiseColor(vertexIndex, current, allVertices);
                    break;
                case VertexPaintTool.Clear:
                    target = new Color(1f, 1f, 1f, vertexPaintAffectAlpha ? 1f : current.a);
                    break;
            }

            Color result = vertexPaintTool == VertexPaintTool.Clear ? target : Color.Lerp(current, target, amount);
            Color32 result32 = result;
            bool changed = false;
            for (int i = 0; i < coincident.Count; i++)
            {
                int coincidentIndex = coincident[i];
                if (!ColorsEqual(vertexPaintColors[coincidentIndex], result32))
                {
                    vertexPaintColors[coincidentIndex] = result32;
                    changed = true;
                }
                vertexPaintStrokeApplied[coincidentIndex] = Mathf.Clamp01(vertexPaintStrokeApplied[coincidentIndex] + amount);
            }
            return changed;
        }

        private Color GetVertexPaintSmearColor(int vertexIndex, Color32[] source, Vector3 strokeDirection, Vector3[] allVertices)
        {
            if (strokeDirection.sqrMagnitude < 0.000001f || vertexPaintNeighbors == null || vertexPaintNeighbors[vertexIndex] == null)
            {
                return GetVertexPaintNeighborAverage(vertexIndex, source);
            }

            Vector3 currentPosition = allVertices[vertexPaintSlotStart + vertexIndex];
            float bestScore = 0f;
            int bestIndex = -1;
            List<int> neighbors = vertexPaintNeighbors[vertexIndex];
            for (int i = 0; i < neighbors.Count; i++)
            {
                int neighbor = neighbors[i];
                Vector3 fromNeighbor = currentPosition - allVertices[vertexPaintSlotStart + neighbor];
                float score = Vector3.Dot(fromNeighbor, strokeDirection);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = neighbor;
                }
            }
            return bestIndex >= 0 ? (Color)source[bestIndex] : GetVertexPaintNeighborAverage(vertexIndex, source);
        }

        private Color GetVertexPaintNeighborAverage(int vertexIndex, Color32[] source)
        {
            if (vertexPaintNeighbors == null || vertexPaintNeighbors[vertexIndex] == null || vertexPaintNeighbors[vertexIndex].Count == 0)
            {
                return source[vertexIndex];
            }

            Color sum = source[vertexIndex];
            int count = 1;
            List<int> neighbors = vertexPaintNeighbors[vertexIndex];
            for (int i = 0; i < neighbors.Count; i++)
            {
                sum += (Color)source[neighbors[i]];
                count++;
            }
            return sum / count;
        }

        private void BuildVertexPaintWorldSmoothingMap(Vector3[] allVertices, Vector3[] allNormals, float radius)
        {
            if (VertexObject == null || allVertices == null || vertexPaintSlotStart < 0 ||
                vertexPaintSlotStart + vertexPaintSlotVertexCount > allVertices.Length)
            {
                vertexPaintWorldSmoothingCells = null;
                vertexPaintWorldSmoothingPositions = null;
                vertexPaintWorldSmoothingNormals = null;
                return;
            }

            vertexPaintWorldSmoothingCellSize = Mathf.Max(0.000001f, radius);
            float inverseCellSize = 1f / vertexPaintWorldSmoothingCellSize;
            vertexPaintWorldSmoothingCells = new Dictionary<Vector3Int, List<int>>(vertexPaintSlotVertexCount);
            vertexPaintWorldSmoothingPositions = new Vector3[vertexPaintSlotVertexCount];

            bool hasNormals = allNormals != null && vertexPaintSlotStart >= 0 &&
                vertexPaintSlotStart + vertexPaintSlotVertexCount <= allNormals.Length;
            vertexPaintWorldSmoothingNormals = hasNormals ? new Vector3[vertexPaintSlotVertexCount] : null;
            Transform vertexTransform = VertexObject.transform;
            Matrix4x4 worldNormalMatrix = vertexTransform.localToWorldMatrix.inverse.transpose;

            for (int vertexIndex = 0; vertexIndex < vertexPaintSlotVertexCount; vertexIndex++)
            {
                int bakedIndex = vertexPaintSlotStart + vertexIndex;
                Vector3 worldPosition = vertexTransform.TransformPoint(allVertices[bakedIndex]);
                vertexPaintWorldSmoothingPositions[vertexIndex] = worldPosition;
                if (hasNormals)
                {
                    vertexPaintWorldSmoothingNormals[vertexIndex] =
                        worldNormalMatrix.MultiplyVector(allNormals[bakedIndex]).normalized;
                }

                Vector3Int cell = GetVertexPaintWorldSmoothingCell(worldPosition, inverseCellSize);
                if (!vertexPaintWorldSmoothingCells.TryGetValue(cell, out List<int> cellVertices))
                {
                    cellVertices = new List<int>();
                    vertexPaintWorldSmoothingCells.Add(cell, cellVertices);
                }
                cellVertices.Add(vertexIndex);
            }
        }

        private Color GetVertexPaintWorldAverage(int vertexIndex, Color32[] source, float radius)
        {
            if (vertexPaintWorldSmoothingCells == null || vertexPaintWorldSmoothingPositions == null ||
                vertexIndex < 0 || vertexIndex >= vertexPaintWorldSmoothingPositions.Length)
            {
                return source[vertexIndex];
            }

            radius = Mathf.Max(0.000001f, radius);
            float radiusSquared = radius * radius;
            float inverseCellSize = 1f / Mathf.Max(0.000001f, vertexPaintWorldSmoothingCellSize);
            Vector3 centerPosition = vertexPaintWorldSmoothingPositions[vertexIndex];
            Vector3 centerNormal = vertexPaintWorldSmoothingNormals != null
                ? vertexPaintWorldSmoothingNormals[vertexIndex]
                : Vector3.zero;
            Vector3Int centerCell = GetVertexPaintWorldSmoothingCell(centerPosition, inverseCellSize);
            Color weightedColor = Color.clear;
            float totalWeight = 0f;

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        Vector3Int cell = centerCell + new Vector3Int(x, y, z);
                        if (!vertexPaintWorldSmoothingCells.TryGetValue(cell, out List<int> candidates))
                        {
                            continue;
                        }

                        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                        {
                            int neighbor = candidates[candidateIndex];
                            Vector3 offset = vertexPaintWorldSmoothingPositions[neighbor] - centerPosition;
                            float distanceSquared = offset.sqrMagnitude;
                            if (distanceSquared > radiusSquared)
                            {
                                continue;
                            }

                            float normalWeight = 1f;
                            if (vertexPaintWorldSmoothingNormals != null)
                            {
                                float normalAlignment = Vector3.Dot(centerNormal, vertexPaintWorldSmoothingNormals[neighbor]);
                                if (normalAlignment <= 0f)
                                {
                                    continue;
                                }
                                normalWeight = Mathf.Lerp(0.15f, 1f, normalAlignment * normalAlignment);
                            }

                            float spatialWeight = 1f - distanceSquared / radiusSquared;
                            spatialWeight *= spatialWeight;
                            float weight = spatialWeight * normalWeight;
                            weightedColor += (Color)source[neighbor] * weight;
                            totalWeight += weight;
                        }
                    }
                }
            }

            return totalWeight > 0.000001f ? weightedColor / totalWeight : source[vertexIndex];
        }

        private static Vector3Int GetVertexPaintWorldSmoothingCell(Vector3 worldPosition, float inverseCellSize)
        {
            return new Vector3Int(
                Mathf.FloorToInt(worldPosition.x * inverseCellSize),
                Mathf.FloorToInt(worldPosition.y * inverseCellSize),
                Mathf.FloorToInt(worldPosition.z * inverseCellSize));
        }

        private Color GetVertexPaintNoiseColor(int vertexIndex, Color current, Vector3[] allVertices)
        {
            Vector3 position = allVertices[vertexPaintSlotStart + vertexIndex] * Mathf.Max(0.0001f, vertexPaintNoiseScale);
            float seed = vertexPaintNoiseSeed * 0.173f;
            float r = Mathf.PerlinNoise(position.x + seed + 11.3f, position.y + position.z + 7.7f) * 2f - 1f;
            float g = Mathf.PerlinNoise(position.y + seed + 31.9f, position.z + position.x + 19.1f) * 2f - 1f;
            float b = Mathf.PerlinNoise(position.z + seed + 53.7f, position.x + position.y + 37.3f) * 2f - 1f;
            float a = Mathf.PerlinNoise(position.x + position.z + seed + 71.9f, position.y + 43.1f) * 2f - 1f;
            return new Color(
                Mathf.Clamp01(current.r + r * vertexPaintNoiseAmount),
                Mathf.Clamp01(current.g + g * vertexPaintNoiseAmount),
                Mathf.Clamp01(current.b + b * vertexPaintNoiseAmount),
                vertexPaintAffectAlpha ? Mathf.Clamp01(current.a + a * vertexPaintNoiseAmount) : current.a);
        }

        private void EndVertexPaintStroke(bool finalize)
        {
            if (!vertexPainting)
            {
                return;
            }

            vertexPainting = false;
            vertexPaintHasLastSample = false;
            if (finalize && vertexPaintStrokeChanged)
            {
                SyncVertexPaintAdjustments();
                vertexPaintDirtySlots.Add(GetVertexPaintSlotKey());
                vertexPaintDirty = true;
                RepaintLinkedEditors();
                SceneView.RepaintAll();
            }
            if (vertexPaintUndoGroup >= 0)
            {
                Undo.CollapseUndoOperations(vertexPaintUndoGroup);
            }
            vertexPaintUndoGroup = -1;
            vertexPaintStrokeChanged = false;
            vertexPaintWorldSmoothingCells = null;
            vertexPaintWorldSmoothingPositions = null;
            vertexPaintWorldSmoothingNormals = null;
        }

        private VertexAdjustment editAdjustment;
        private VertexSelection editSelection;

        public void SetActive(VertexAdjustment va)
        {
            editSelection = GetInternalSelection(va);
            editAdjustment = va;
        }

        public bool isEditing
        {
            get
            {
                return editAdjustment != null;
            }
        }

        private void DrawHandles(List<VertexSelection> vertexes)
        {
            Color LastColor = Color.black;
            if (EventType.Repaint != Event.current.type)
            {
                return;
            }
            Color saveColor = Handles.color;
            Mesh mesh = GetVertexMesh();
            Material mat = GetVertexMaterial(Color.red);

            if (!touchupWeightsMode || bakedNormals == null ||
                BakedMesh == null || bakedNormals.Length != BakedMesh.vertexCount)
            {
                RefreshBakedMeshCaches();
            }
            Vector3[] normals = bakedNormals;
            if (normals == null)
            {
                return;
            }

            if (touchupWeightsMode)
            {
                DrawTouchupSelectionHandles(vertexes, mesh, mat, normals);
                return;
            }

            HashSet<string> VisibleSlots = new HashSet<string>();
            for (int i = 0; i < thisDCA.umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData slot = thisDCA.umaData.umaRecipe.slotDataList[i];
                if (!slot.Suppressed)
                {
                    VisibleSlots.Add(slot.slotName);
                }
            }



            for (int i = 0; i < vertexes.Count; i++)
            {
                VertexSelection vs = vertexes[i];
                if (vs.suppressed) continue;
                if (!VisibleSlots.Contains(vs.slot.slotName))
                {
                    continue;
                }

                if (!TryGetVisibleBakedVertexIndex(vs.slot, vs.vertexIndexOnSlot, out int bakedIndex))
                {
                    continue;
                }

                Vector3 bakedNormal = normals[bakedIndex];
                if (Vector3.Dot(bakedNormal, Camera.current.transform.forward) > 0)
                {
                    continue;
                }

                Matrix4x4 matrix = Matrix4x4.TRS(vs.WorldPosition, Quaternion.identity, Vector3.one * HandlesSize);

                Color newColor = InactiveColor;

                if (vs.isActive)
                {
                    newColor = ActiveColor;
                }

                if (i == currentSelected && editAdjustment == null && editorMode == MeshModifierEditor.EditorMode.VertexAdjustments)
                {
                    // do nothing right now
                    AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
                    float time = Time.fixedTime / blinkSpeed;
                    float val = curve.Evaluate(time % 1.0f);
                    newColor = Color.Lerp(Color.cyan, Color.white, val);
                    mat.SetColor("_Color", newColor);
                    mat.SetPass(0);
                    Graphics.DrawMeshNow(mesh, matrix);
                    LastColor = newColor;
                }
                else
                {
                    if (newColor != LastColor)
                    {
                        LastColor = newColor;
                        mat.SetColor("_Color", newColor);
                        mat.SetPass(0);
                    }
                    Graphics.DrawMeshNow(mesh, matrix);
                }
            }

            if (editAdjustment != null && editorMode == MeshModifierEditor.EditorMode.VertexAdjustments && editSelection != null)
            {
                AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
                float time = Time.fixedTime / blinkSpeed;
                float val = curve.Evaluate(time % 1.0f);
                Color newColor = Color.Lerp(Color.cyan, Color.white, val);
                Matrix4x4 matrix = Matrix4x4.TRS(editSelection.WorldPosition, Quaternion.identity, Vector3.one * HandlesSize);
                mat.SetColor("_Color", newColor);
                mat.SetPass(0);
                Graphics.DrawMeshNow(mesh, matrix);
            }
        }

        private void DrawTouchupSelectionHandles(
            List<VertexSelection> vertexes,
            Mesh mesh,
            Material material,
            Vector3[] normals)
        {
            if (vertexes == null || vertexes.Count == 0 || Camera.current == null)
            {
                return;
            }

            material.enableInstancing = true;
            material.SetColor("_Color", InactiveColor);
            int batchCount = 0;
            for (int i = 0; i < vertexes.Count; i++)
            {
                VertexSelection selection = vertexes[i];
                if (!SelectionMatchesTouchupSlot(selection) ||
                    !TryGetVisibleBakedVertexIndex(
                        selection.slot,
                        selection.vertexIndexOnSlot,
                        out int bakedIndex) ||
                    bakedIndex < 0 ||
                    bakedIndex >= normals.Length ||
                    Vector3.Dot(normals[bakedIndex], Camera.current.transform.forward) > 0f)
                {
                    continue;
                }

                touchupHandleBatch[batchCount++] = Matrix4x4.TRS(
                    selection.WorldPosition,
                    Quaternion.identity,
                    Vector3.one * HandlesSize);
                if (batchCount < touchupHandleBatch.Length)
                {
                    continue;
                }

                Graphics.DrawMeshInstanced(
                    mesh,
                    0,
                    material,
                    touchupHandleBatch,
                    batchCount,
                    null,
                    UnityEngine.Rendering.ShadowCastingMode.Off,
                    false,
                    0,
                    Camera.current);
                batchCount = 0;
            }

            if (batchCount > 0)
            {
                Graphics.DrawMeshInstanced(
                    mesh,
                    0,
                    material,
                    touchupHandleBatch,
                    batchCount,
                    null,
                    UnityEngine.Rendering.ShadowCastingMode.Off,
                    false,
                    0,
                    Camera.current);
            }
        }

        private bool DoGizmoInput()
        {
            bool changed = false;
            VertexAdjustmentGizmo gizmo = editAdjustment.Gizmo;

            switch (gizmo)
            {
                case VertexAdjustmentGizmo.Rotate:
                    changed = DoRotationGizmo();
                    break;
                case VertexAdjustmentGizmo.Scale:
                    changed = DoScaleGizmo();
                    break;
                case VertexAdjustmentGizmo.Move:
                    changed = DoTranslateGizmo();
                    break;
            }
            return changed;
        }

        private bool DoRotationGizmo()
        {
            bool changed = false;
            // show an arrow gizmo at the editSelection.WorldPosition, pointing in the direction of the normal
            // when the user clicks on the gizmo, show a rotation handle
            // when the user clicks on the rotation handle, rotate the vertex around the normal
            VertexNormalAdjustment van = editAdjustment as VertexNormalAdjustment;

            if (van != null)
            {
                RefreshBakedMeshCaches();
                if (bakedVertices == null || bakedNormals == null)
                {
                    return false;
                }

                if (!TryGetVisibleBakedVertexIndex(editSelection.slot, editSelection.vertexIndexOnSlot, out int bakedIndex))
                {
                    return false;
                }

                if (van.bakedNormalSet == false)
                {
                    van.bakedNormal = bakedNormals[bakedIndex];
                    van.bakedNormalSet = true;
                }

                editSelection.WorldPosition = VertexObject.transform.TransformPoint(bakedVertices[bakedIndex]);
                // show an arrow gizmo at the editSelection.WorldPosition, pointing in the direction of the normal
                Handles.color = Color.red;
                Vector3 normal = van.bakedNormal;
                Vector3 worldRotation = VertexObject.transform.TransformVector(normal/*BakedMesh.normals[editSelection.slot.vertexOffset + editSelection.vertexIndexOnSlot]*/);
                Quaternion quaternion = Quaternion.LookRotation(worldRotation) * van.rotation;
                Handles.ArrowHandleCap(0, editSelection.WorldPosition, quaternion, 0.1f, EventType.Repaint);
                //            Handles.ArrowHandleCap(0, editSelection.WorldPosition, Quaternion.LookRotation(worldRotation), 0.1f, EventType.Repaint);

                // show a rotation handle at the editSelection.WorldPosition for van.normal
                Quaternion q = Handles.RotationHandle(van.rotation, editSelection.WorldPosition);
                if (q != van.rotation)
                {
                    van.rotation = q;
                    changed = true;
                }
                //van.SetRotation(Handles.RotationHandle(van.rotation, editSelection.WorldPosition));
            }
            return changed;
        }

        private bool DoScaleGizmo()
        {
            VertexScaleAdjustment vas = editAdjustment as VertexScaleAdjustment;
            if (vas != null)
            {
                RefreshBakedMeshCaches();
                if (bakedNormals == null)
                {
                    return false;
                }

                if (!TryGetVisibleBakedVertexIndex(editSelection.slot, editSelection.vertexIndexOnSlot, out int bakedIndex))
                {
                    return false;
                }

                UMAData umaData = thisDCA.umaData;
                SlotData slot = FindSlotBySourceSlotOrName(vas.slotName);

                if (slot == null) return false;

                if (!vas.basePosSet)
                {
                    vas.basePos = slot.asset.meshData.vertices[editSelection.vertexIndexOnSlot];
                    vas.basePosSet = true;
                }
                Vector3 basenormal = slot.asset.meshData.normals[editSelection.vertexIndexOnSlot];

                // show an arrow gizmo at the editSelection.WorldPosition, pointing in the direction of the normal
                Handles.color = Color.red;
                //Vector3 normal = vas.bakedNormal;
                Vector3 worldRotation = VertexObject.transform.TransformVector(bakedNormals[bakedIndex]);
                Quaternion quaternion = Quaternion.LookRotation(worldRotation);
                // Handles.ArrowHandleCap(0, editSelection.WorldPosition, quaternion, 0.1f, EventType.Repaint);
                //Handles.ArrowHandleCap(0, editSelection.WorldPosition, Quaternion.LookRotation(worldRotation), 0.1f, EventType.Repaint);

                Vector3 Scale = Vector3.one * vas.scale;
                Vector3 newScale = Handles.ScaleHandle(Scale, editSelection.WorldPosition, Quaternion.identity, 0.1f);
                if (Scale != newScale)
                {
                    vas.scale = newScale.z;
                    return true;
                }
            }
            return false;
        }

        private bool DoTranslateGizmo()
        {
            return false;
        }

        private int GetVisibleSlotCount()
        {
            int visibleCount = 0;
            foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
            {
                if (slot != null && !slot.Suppressed)
                {
                    visibleCount++;
                }
            }
            return visibleCount;
        }

        private bool EnsureAtLeastOneVisibleSlot()
        {
            if (GetVisibleSlotCount() > 0)
            {
                return false;
            }

            foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
            {
                if (slot != null)
                {
                    slot.Suppressed = false;
                    return true;
                }
            }
            return false;
        }

        private void DrawGUIWindows(SceneView sceneView)
        {
            Handles.BeginGUI();
            EnsureGUIStyles();
            DrawStageCloseButton(sceneView);
            GUILayout.BeginArea(leftPanelRect, EditorStyles.helpBox);
            {
                if (slotWeightEditorMode)
                {
                    DoToolsPanel();
                    GUILayout.EndArea();
                    Handles.EndGUI();
                    return;
                }

                float availableHeight = leftPanelRect.height - (LeftPanelPadding * 2f);
                float maxVisibilityHeight = Mathf.Max(50f, availableHeight * 0.5f);
               if (cachedVisibilityHeight < 0f)
                {
                    cachedVisibilityHeight = GetVisibilitySectionHeightEstimate(maxVisibilityHeight);
                }
                float visibilityHeight = Mathf.Min(cachedVisibilityHeight, maxVisibilityHeight);
                float toolsHeight = Mathf.Max(50f, availableHeight - visibilityHeight);

                Rect toolsRect = new Rect(0f, 0f, leftPanelRect.width, toolsHeight);
                Rect visRect = new Rect(0f, toolsHeight, leftPanelRect.width, visibilityHeight);

                GUILayout.BeginArea(toolsRect);
                {
                    DoToolsPanel();
                }
                GUILayout.EndArea();

                GUILayout.BeginArea(visRect);
                {
                    DrawVisibilityPanel(visRect.height);
                }
                GUILayout.EndArea();
            }
            GUILayout.EndArea();


            Handles.EndGUI();
        }

        private void DrawStageCloseButton(SceneView sceneView)
        {
            if (sceneView == null || closing)
            {
                return;
            }

            const float buttonWidth = 86f;
            const float buttonHeight = 26f;
            const float margin = 8f;
            Rect buttonRect = new Rect(sceneView.position.width - buttonWidth - margin, 28f, buttonWidth, buttonHeight);

            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.82f, 0.25f, 0.2f, 1f);
            bool pressed = GUI.Button(buttonRect, "Close", EditorStyles.miniButton);
            GUI.backgroundColor = previousBackground;
            if (pressed)
            {
                RequestClose();
                Event.current.Use();
            }
        }

        private void CaptureSavedSelectionSnapshot()
        {
            savedSelectionSnapshot.Clear();
            if (SelectedVertexes == null)
            {
                return;
            }

            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                VertexSelection selection = SelectedVertexes[i];
                if (selection == null || selection.slot == null)
                {
                    continue;
                }

                savedSelectionSnapshot.Add(new VertexSelectionKey(
                    selection.slot.slotName,
                    selection.vertexIndexOnSlot,
                    selection.isActive));
            }
        }

        private bool HasUnsavedSelectionChanges()
        {
            if (SelectedVertexes == null)
            {
                return savedSelectionSnapshot.Count != 0;
            }

            HashSet<VertexSelectionKey> current = new HashSet<VertexSelectionKey>();
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                VertexSelection selection = SelectedVertexes[i];
                if (selection == null || selection.slot == null)
                {
                    continue;
                }

                current.Add(new VertexSelectionKey(
                    selection.slot.slotName,
                    selection.vertexIndexOnSlot,
                    selection.isActive));
            }

            return !savedSelectionSnapshot.SetEquals(current);
        }

        private bool TrySaveSelectionsForClose()
        {
            hasSaved = false;
            SaveSelections();
            return hasSaved;
        }

        private void RequestClose()
        {
            if (closing)
            {
                return;
            }

            if (touchupWeightsMode)
            {
                if (touchupWeightsWindow != null &&
                    !touchupWeightsWindow.TryResolveUnsavedChangesBeforeClose())
                {
                    return;
                }
                StageUtility.GoBackToPreviousStage();
                return;
            }

            bool selectionChanged = HasUnsavedSelectionChanges();
            bool sculptChanged = HasSculptChanges();
            bool vertexPaintChanged = vertexPaintDirty;
            if (selectionChanged || sculptChanged || vertexPaintChanged)
            {
                string message = "The Vertex Editor has unsaved ";
                List<string> changedItems = new List<string>();
                if (selectionChanged) changedItems.Add("vertex selections");
                if (sculptChanged) changedItems.Add("sculpt changes");
                if (vertexPaintChanged) changedItems.Add("vertex colors");
                message += string.Join(", ", changedItems) + ". Save before closing?";

                int choice = EditorUtility.DisplayDialogComplex(
                    "Vertex Editor",
                    message,
                    "Save",
                    "Discard",
                    "Cancel");

                if (choice == 2)
                {
                    return;
                }

                if (choice == 0)
                {
                    if (selectionChanged && !TrySaveSelectionsForClose())
                    {
                        return;
                    }

                    if ((sculptChanged || vertexPaintChanged) && !SaveSculptModifier())
                    {
                        return;
                    }
                }
            }

            StageUtility.GoBackToPreviousStage();
        }

        private float GetVisibilitySectionHeightEstimate(float maxHeight)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float vSpacing = 2f;
            float header = Mathf.Max(LeftPanelHeaderHeight, line);

            int wearableCount = 0;
            int slotCount = 0;
            if (thisDCA != null && thisDCA.umaData != null && thisDCA.umaData.umaRecipe != null)
            {
                var wearables = thisDCA.GetVisibleWearables();
                wearableCount = wearables != null ? wearables.Length : 0;
                var slots = thisDCA.umaData.umaRecipe.slotDataList;
                slotCount = slots != null ? slots.Length : 0;
            }

            // Approximate height for:
            // - "Visibility" header
            // - scroll view containing wearables + slots + button + optional help box
            float scrollContentLines = 0f;
            scrollContentLines += 1f; // "Visible Wearables" header
            scrollContentLines += wearableCount;
            scrollContentLines += 1f; // "Visible Slots" header
            scrollContentLines += slotCount;
            scrollContentLines += 1.5f; // spacing + invert button
            scrollContentLines += 2f; // safety margin / possible helpbox

            float estimated = header + ((scrollContentLines * (line + vSpacing)) + (LeftPanelPadding * 2f)) + 8f;
            return Mathf.Clamp(estimated, 50f, maxHeight);
        }

        private void DoToolsPanel()
        {
            string panelTitle = touchupWeightsMode
                ? "Weight Touchup"
                : slotWeightEditorMode ? "Vertex Selection" : "Authoring Workflow";
            GUILayout.Label(panelTitle, EditorStyles.boldLabel);
            DoToolsWindow(VertexEditorToolsWindowID);
        }

        private void DrawVisibilityPanel(float availableHeight)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Visibility", EditorStyles.boldLabel);

            // Fill the remainder of the visibility section with the scroll view.
            float headerHeight = Mathf.Max(LeftPanelHeaderHeight, EditorGUIUtility.singleLineHeight);
            float scrollHeight = Mathf.Max(0f, availableHeight - headerHeight - 8f);
            VisibleWearablesLocation = GUILayout.BeginScrollView(VisibleWearablesLocation, GUILayout.Height(scrollHeight));
            bool wasChanged = false;
            bool wasRecipeChanged = false;
            bool blockedHideAllSlots = false;
            var wearables = thisDCA.GetVisibleWearables();

            if (EnsureAtLeastOneVisibleSlot())
            {
                wasChanged = true;
            }

            GUILayout.Label("Visible Wearables", EditorStyles.boldLabel);
            foreach (var wearable in wearables)
            {
                GUILayout.BeginHorizontal();
                bool wasVisible = wearable.disabled;
                wearable.disabled = !GUILayout.Toggle(!wearable.disabled, string.Empty, GUILayout.Width(24));
                if (wasVisible != wearable.disabled)
                {
                    wasRecipeChanged = true;
                }
                GUILayout.Label(wearable.name);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            GUILayout.Label("Visible Slots", EditorStyles.boldLabel);
            int visibleSlotCount = GetVisibleSlotCount();
            foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
            {
                if (slot == null)
                {
                    continue;
                }
                GUILayout.BeginHorizontal();
                bool wasDisabled = slot.Suppressed;
                bool desiredVisible = GUILayout.Toggle(!slot.Suppressed, string.Empty, GUILayout.Width(24));
                bool desiredSuppressed = !desiredVisible;

                if (desiredSuppressed && !slot.Suppressed && visibleSlotCount <= 1)
                {
                    desiredSuppressed = false;
                    blockedHideAllSlots = true;
                }

                slot.Suppressed = desiredSuppressed;
                if (slot.Suppressed != wasDisabled)
                {
                    wasChanged = true;
                    visibleSlotCount += slot.Suppressed ? -1 : 1;
                }
                if (slot.Suppressed && editAdjustment != null && editAdjustment.slotName == slot.slotName)
                {
                    editAdjustment = null;
                }
                string label = slot.slotName;
                if (label.Length > 27)
                {
                    label = label.Substring(0, 24) + "...";
                }
                if (GUILayout.Button(label, EditorStyles.label))
                {
                    slot.Suppressed = !slot.Suppressed;
                    wasChanged = true;
                    visibleSlotCount += slot.Suppressed ? -1 : 1;
                    if (slot.Suppressed && editAdjustment != null && editAdjustment.slotName == slot.slotName)
                    {
                        editAdjustment = null;
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            if (GUILayout.Button("Invert Visiblity", EditorStyles.miniButton))
            {
                foreach (var wearable in wearables)
                {
                    wearable.disabled = !wearable.disabled;
                    wasRecipeChanged = true;
                }

                int totalSlots = 0;
                foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
                {
                    if (slot != null)
                    {
                        totalSlots++;
                    }
                }

                if (totalSlots > 0 && (totalSlots - visibleSlotCount) == 0)
                {
                    blockedHideAllSlots = true;
                }

                foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
                {
                    if (slot == null)
                    {
                        continue;
                    }
                    if (blockedHideAllSlots)
                    {
                        continue;
                    }
                    slot.Suppressed = !slot.Suppressed;
                    wasChanged = true;
                    if (slot.Suppressed && editAdjustment != null && editAdjustment.slotName == slot.slotName)
                    {
                        editAdjustment = null;
                    }
                }
            }

            if (blockedHideAllSlots)
            {
                EditorGUILayout.HelpBox("At least one slot must remain visible.", MessageType.Warning);
            }

            GUILayout.EndScrollView();

            if (wasChanged)
            {
                RebuildMesh(false);
                RepaintLinkedEditors();
            }
            if (wasRecipeChanged)
            {
                RebuildMesh(true);
                RepaintLinkedEditors();
            }
            GUILayout.EndVertical();
        }

        private Vector2 ToolsPos = new Vector2(0, 0);
        private const float ToolsPanelRightPadding = 10f;
        private GUIStyle smallButtonStyle;
        private GUIStyle threeButtonStyle;
        bool doneButton = false;
        public float ToolWindowAreaHeight = 0.0f;
        public MeshModifierEditor.EditorMode editorMode = MeshModifierEditor.EditorMode.VertexAdjustments;

        private void EnsureGUIStyles()
        {
            if (centeredLabel == null)
            {
                centeredLabel = new GUIStyle(EditorStyles.boldLabel);
                centeredLabel.alignment = TextAnchor.MiddleCenter;
            }

            if (HelpBoxStyle == null)
            {
                HelpBoxStyle = new GUIStyle(EditorStyles.miniLabel);
                HelpBoxStyle.wordWrap = true;
            }
        }

        private void ChangeSceneToolMode(SceneToolMode newMode)
        {
            if (newMode == sceneToolMode)
            {
                return;
            }

            CancelInteraction();
            EndSculptStroke(true);
            EndVertexPaintStroke(true);
            sceneToolMode = newMode;
            selectionBrushModeSet = sceneToolMode == SceneToolMode.SelectionBrush;
            selectionBrushModeState = sceneToolMode == SceneToolMode.SelectionBrush;
            sculptHoverValid = false;
            vertexPaintHoverValid = false;
            if (sceneToolMode == SceneToolMode.VertexPaint)
            {
                EnsureVertexPaintSession();
            }
            ApplyVertexDisplayOptions();
            RepaintLinkedEditors();
            SceneView.RepaintAll();
        }

        private int GetAuthoringWorkflowIndex()
        {
            if (sceneToolMode == SceneToolMode.Sculpt)
            {
                return 0;
            }
            if (sceneToolMode == SceneToolMode.VertexPaint)
            {
                return 1;
            }
            if (modifierEditor != null && modifierEditor.IsBlendshapeExtractionWorkflow)
            {
                return 3;
            }
            return 2;
        }

        private void DrawAuthoringWorkflowToolbar()
        {
            int currentWorkflow = GetAuthoringWorkflowIndex();
            int requestedWorkflow = GUILayout.Toolbar(
                currentWorkflow,
                new[] { "Sculpt", "Vertex Paint", "Advanced", "Blendshapes" });
            if (requestedWorkflow == currentWorkflow)
            {
                return;
            }

            switch (requestedWorkflow)
            {
                case 0:
                    if (modifierEditor != null) modifierEditor.SelectSculptWorkflowFromStage();
                    else ChangeSceneToolMode(SceneToolMode.Sculpt);
                    break;
                case 1:
                    if (modifierEditor != null) modifierEditor.SelectVertexPaintWorkflowFromStage();
                    else ChangeSceneToolMode(SceneToolMode.VertexPaint);
                    break;
                case 2:
                    if (modifierEditor != null) modifierEditor.SelectAdvancedWorkflowFromStage();
                    else ChangeSceneToolMode(SceneToolMode.Select);
                    break;
                case 3:
                    if (modifierEditor != null) modifierEditor.SelectBlendshapeWorkflowFromStage();
                    break;
            }
        }

        private void DrawAdvancedSelectionToolbar()
        {
            if (sceneToolMode != SceneToolMode.Select && sceneToolMode != SceneToolMode.SelectionBrush)
            {
                ChangeSceneToolMode(SceneToolMode.Select);
            }

            int currentTool = sceneToolMode == SceneToolMode.SelectionBrush ? 1 : 0;
            int requestedTool = GUILayout.Toolbar(currentTool, new[] { "Select", "Select Brush" });
            SceneToolMode requestedMode = requestedTool == 0 ? SceneToolMode.Select : SceneToolMode.SelectionBrush;
            if (requestedMode != sceneToolMode)
            {
                ChangeSceneToolMode(requestedMode);
            }
        }

        public void DoToolsWindow(int ID)
        {
            if (!doneButton)
            {
                smallButtonStyle = new GUIStyle(EditorStyles.miniButton);
                threeButtonStyle = new GUIStyle(EditorStyles.miniButton);
                smallButtonStyle.fontSize = 9;
                smallButtonStyle.fixedWidth = 82;
                threeButtonStyle.fontSize = 9;
                threeButtonStyle.fixedWidth = 54;
                doneButton = true;
                ToolWindowAreaHeight = VertexEditorToolsWindow.height;
            }
            ToolsPos = GUILayout.BeginScrollView(ToolsPos);
            GUIStyle verticalScrollbar = GUI.skin.verticalScrollbar;
            float scrollbarWidth = verticalScrollbar != null && verticalScrollbar.fixedWidth > 0f
                ? verticalScrollbar.fixedWidth
                : 16f;
            float toolsContentWidth = Mathf.Max(
                120f,
                leftPanelRect.width - scrollbarWidth - ToolsPanelRightPadding);
            GUILayout.BeginArea(new Rect(0, 0, toolsContentWidth, ToolsPos.y + ToolWindowAreaHeight));
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (touchupWeightsMode)
            {
                DrawTouchupWeightsTools(sceneView);
                if (Event.current.type == EventType.Repaint)
                {
                    ToolWindowAreaHeight = Mathf.Max(180f, GUILayoutUtility.GetLastRect().yMax + 20f);
                }
                GUILayout.EndArea();
                GUILayout.Space(ToolWindowAreaHeight + 10);
                GUILayout.EndScrollView();
                return;
            }

            if (slotWeightEditorMode)
            {
                DrawAdvancedSelectionToolbar();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(3f);
                DrawAuthoringWorkflowToolbar();
                GUILayout.EndHorizontal();
                if (GetAuthoringWorkflowIndex() == 2)
                {
                    GUILayout.Label("Advanced Selection", EditorStyles.miniBoldLabel);
                    DrawAdvancedSelectionToolbar();
                }
            }
            selectionBrushModeSet = sceneToolMode == SceneToolMode.SelectionBrush;
            selectionBrushModeState = sceneToolMode == SceneToolMode.SelectionBrush;
            EditorGUI.BeginChangeCheck();
            if (touchupWeightsMode)
            {
                EditorGUILayout.HelpBox("The mesh is shown in Blender-style weight colors for the bone selected in the Touchup Weights window.", MessageType.Info);
            }
            else
            {
                showOriginalMaterials = EditorGUILayout.Toggle(new GUIContent("Original Materials", "Display the materials from the generated UMA renderer instead of the pastel editor materials."), showOriginalMaterials);
            }
            if (EditorGUI.EndChangeCheck())
            {
                ApplyVertexDisplayOptions();
                SceneView.RepaintAll();
            }
            if (sceneToolMode == SceneToolMode.Sculpt)
            {
                GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
                DrawSculptOptions();
                GUIHelper.EndVerticalPadded(5);
                if (GUILayout.Button("Reset Camera") && sceneView != null)
                {
                    Selection.activeObject = VertexObject;
                    sceneView.AlignViewToObject(cameraAnchor.transform);
                    sceneView.FrameSelected(true);
                }
                if (Event.current.type == EventType.Repaint)
                    ToolWindowAreaHeight = Mathf.Max(240f, GUILayoutUtility.GetLastRect().yMax + 20f);
                GUILayout.EndArea();
                GUILayout.Space(ToolWindowAreaHeight + 10);
                GUILayout.EndScrollView();
                return;
            }
            if (sceneToolMode == SceneToolMode.VertexPaint)
            {
                GUIHelper.BeginVerticalPadded(5, new Color(0.82f, 0.9f, 0.82f), EditorStyles.helpBox);
                DrawVertexPaintOptions();
                GUIHelper.EndVerticalPadded(5);
                if (GUILayout.Button("Reset Camera") && sceneView != null)
                {
                    Selection.activeObject = VertexObject;
                    sceneView.AlignViewToObject(cameraAnchor.transform);
                    sceneView.FrameSelected(true);
                }
                if (Event.current.type == EventType.Repaint)
                    ToolWindowAreaHeight = Mathf.Max(320f, GUILayoutUtility.GetLastRect().yMax + 20f);
                GUILayout.EndArea();
                GUILayout.Space(ToolWindowAreaHeight + 10);
                GUILayout.EndScrollView();
                return;
            }
            if (!slotWeightEditorMode && modifierEditor != null && modifierEditor.IsBlendshapeExtractionWorkflow)
            {
                GUIHelper.BeginVerticalPadded(5, new Color(0.88f, 0.84f, 0.94f), EditorStyles.helpBox);
                GUILayout.Label("Blendshape Extraction", centeredLabel);
                EditorGUILayout.HelpBox(
                    "Choose the source blendshape, DNA, and target slots in the Mesh Modifiers window. Blendshape extraction does not use Scene vertex selection tools.",
                    MessageType.Info);
                if (GUILayout.Button("Focus Mesh Modifiers Window"))
                {
                    modifierEditor.Focus();
                }
                GUIHelper.EndVerticalPadded(5);
                if (GUILayout.Button("Reset Camera") && sceneView != null)
                {
                    Selection.activeObject = VertexObject;
                    sceneView.AlignViewToObject(cameraAnchor.transform);
                    sceneView.FrameSelected(true);
                }
                if (Event.current.type == EventType.Repaint)
                {
                    ToolWindowAreaHeight = Mathf.Max(180f, GUILayoutUtility.GetLastRect().yMax + 20f);
                }
                GUILayout.EndArea();
                GUILayout.Space(ToolWindowAreaHeight + 10);
                GUILayout.EndScrollView();
                return;
            }
            #region Editor Options
            GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
            GUILayout.Label("Editor Options", centeredLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Handle Size", GUILayout.Width(82));
            HandlesSize = EditorGUILayout.Slider(HandlesSize, 0.0f, 0.04f);
            GUILayout.EndHorizontal();
            GUILayout.Label("Vertex Colors", centeredLabel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Active", GUILayout.Width(82));
            ActiveColor = EditorGUILayout.ColorField(ActiveColor, GUILayout.Width(90));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Inactive", GUILayout.Width(82));
            InactiveColor = EditorGUILayout.ColorField(InactiveColor, GUILayout.Width(90));
            GUILayout.EndHorizontal();
            if (slotWeightEditorReadOnly)
            {
                EditorGUILayout.HelpBox("Current character mode is read-only. Select vertices to inspect generated weights.", MessageType.Info);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Smooth Amount", GUILayout.Width(82));
                weightSmoothAmount = EditorGUILayout.Slider(weightSmoothAmount, 0.0f, 1.0f);
                GUILayout.EndHorizontal();
                EditorGUI.BeginDisabledGroup(SelectedVertexes == null || SelectedVertexes.Count == 0);
                if (GUILayout.Button("Smooth Selected Weights"))
                {
                    SmoothSelectedVertexWeights(weightSmoothAmount);
                }
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("View/Edit Vertex Weights"))
                {
                    ShowCurrentVertexWeightsPopup();
                }
            }
            GUIHelper.EndVerticalPadded(5);
            #endregion
            #region Selection Options
            GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
            GUILayout.Label("Selection Options", centeredLabel);

            if (IsSelectionBrushModeEnabled)
            {
                EditorGUILayout.HelpBox(
                    "Select Brush changes the selected vertex set or its Active/Inactive state. It does not paint mesh vertex colors; use Vertex Paint for color data.",
                    MessageType.Info);
            }

            GUILayout.BeginHorizontal();
            DefineMode newDefineMode = (DefineMode)GUILayout.Toolbar((int)currentDefineMode, new string[] { "Define Set", "Define State" });
            GUILayout.EndHorizontal();
            if (newDefineMode != currentDefineMode)
            {
                CancelInteraction();
                currentDefineMode = newDefineMode;
                currentMode = currentDefineMode == DefineMode.DefineVertexSet ? selectMode.Add : selectMode.ToggleState;
            }

            GUILayout.Label(currentDefineMode == DefineMode.DefineVertexSet ? "Current Mode: Define Vertex Set" : "Current Mode: Define Vertex State", EditorStyles.miniBoldLabel);

            GUILayout.BeginHorizontal();
            selectObscured = EditorGUILayout.Toggle("Obscured", selectObscured);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!selectObscured);
            selectFacingAway = EditorGUILayout.Toggle("Backfacing", selectFacingAway);
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Default State", GUILayout.Width(92));
            currentNewVertexState = EditorGUILayout.Popup(currentNewVertexState, new string[] { "Inactive", "Active" });
            GUILayout.EndHorizontal();

            if (currentDefineMode == DefineMode.DefineVertexSet)
            {
                DrawSelectionBrushOptions();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Selection Action", GUILayout.Width(92));
                int setAction = currentMode == selectMode.Remove ? 1 : currentMode == selectMode.InvertSelection ? 2 : 0;
                int newSetAction = GUILayout.Toolbar(setAction, new string[] { "Add", "Remove", "Invert" });
                currentMode = newSetAction == 1 ? selectMode.Remove : newSetAction == 2 ? selectMode.InvertSelection : selectMode.Add;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Slot Filter", GUILayout.Width(92));

                if (touchupWeightsMode)
                {
                    EditorGUILayout.LabelField(touchupWeightSlot != null ? touchupWeightSlot.slotName : "No slot selected");
                }
                else
                {
                    if (selectionSlot >= selectFrom.Length)
                    {
                        selectionSlot = 0;
                    }
                    selectionSlot = EditorGUILayout.Popup(selectionSlot, selectFrom);
                }
                GUILayout.EndHorizontal();

                if (sceneToolMode == SceneToolMode.Select)
                {
                    GUILayout.BeginHorizontal();
                    showRaycastSelection = EditorGUILayout.Foldout(showRaycastSelection, "Select by raycasting", true);
                    GUILayout.EndHorizontal();
                }

                if (sceneToolMode == SceneToolMode.Select && showRaycastSelection)
                {
                 RefreshVisibleSlotListsIfNeeded();
                    GUIHelper.BeginVerticalPadded(5, new Color(0.92f, 0.92f, 0.97f), EditorStyles.helpBox);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Selection Slot", GUILayout.Width(92));
                    if (raycastSelectionSlot >= visibleSelectFrom.Length)
                    {
                        raycastSelectionSlot = 0;
                    }
                    raycastSelectionSlot = EditorGUILayout.Popup(raycastSelectionSlot, visibleSelectFrom);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Direction", GUILayout.Width(92));
                    raycastDirection = (RaycastSelectDirection)EditorGUILayout.EnumPopup(raycastDirection);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Hit Faces", GUILayout.Width(92));
                    raycastHitFaceFilter = (RaycastHitFaceFilter)EditorGUILayout.EnumPopup(raycastHitFaceFilter);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Raycast Length", GUILayout.Width(92));
                    raycastLength = EditorGUILayout.Slider(raycastLength,0.01f,1.0f);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    raycastDrawDebugRays = EditorGUILayout.Toggle("Debug Rays", raycastDrawDebugRays);
                    GUILayout.EndHorizontal();
                    EditorGUI.BeginDisabledGroup(!raycastDrawDebugRays);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Debug Ray Limit", GUILayout.Width(92));
                    raycastDebugRayLimit = EditorGUILayout.IntSlider(raycastDebugRayLimit, 0, 1024);
                    GUILayout.EndHorizontal();
                    EditorGUI.EndDisabledGroup();

                    raycastAddToSelection = GUILayout.Toggle(raycastAddToSelection, "Add to selection (otherwise replace)");

                    EditorGUI.BeginDisabledGroup(raycastSelectionSlot <= 0);
                    if (GUILayout.Button("Select by raycast"))
                    {
                        Undo.RegisterCompleteObjectUndo(this, "Select Vertexes By Raycast");
                        SelectByRaycast();
                        RepaintLinkedEditors();
                        SceneView.RepaintAll();
                    }
                    EditorGUI.EndDisabledGroup();

                    if (!string.IsNullOrEmpty(raycastStatusMessage))
                    {
                       GUILayout.Label("Result (copy/paste):", EditorStyles.miniBoldLabel);
                        float line = EditorGUIUtility.singleLineHeight;
                        EditorGUILayout.HelpBox(raycastStatusMessage, raycastStatusType, true);
                    }

                    GUIHelper.EndVerticalPadded(5);
                }
            }
            else
            {
                DrawSelectionBrushOptions();

                GUILayout.BeginHorizontal();
                GUILayout.Label("State Action", GUILayout.Width(92));
                int stateAction = currentMode == selectMode.Activate ? 0 : currentMode == selectMode.Deactivate ? 1 : 2;
                int newStateAction = GUILayout.Toolbar(stateAction, new string[] { "Activate", "Deactivate", "Toggle" });
                currentMode = newStateAction == 0 ? selectMode.Activate : newStateAction == 1 ? selectMode.Deactivate : selectMode.ToggleState;
                GUILayout.EndHorizontal();
            }


            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save", threeButtonStyle))
            {
                // Save the vertex selections
                SaveSelections();
            }

            if (GUILayout.Button("Load", threeButtonStyle))
            {
                // Load the vertex selections
                Undo.RegisterCompleteObjectUndo(this, "Load Vertex Selection");
                SelectedVertexes.Clear();
                LoadSelections();
                RepaintLinkedEditors();
            }
            if (GUILayout.Button("Append", threeButtonStyle))
            {
                // Append the vertex selections
                Undo.RegisterCompleteObjectUndo(this, "Append Vertex Selection");
                LoadSelections();
                RepaintLinkedEditors();
            }
            GUILayout.EndHorizontal();

            if (currentDefineMode == DefineMode.DefineVertexSet)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Invert Selection", smallButtonStyle))
                {
                    Undo.RegisterCompleteObjectUndo(this, "Invert Vertex Selection");
                    InvertSelection();
                    RepaintLinkedEditors();
                }
                if (GUILayout.Button("Select All", smallButtonStyle))
                {
                    Undo.RegisterCompleteObjectUndo(this, "Select All Vertexes");
                    SelectAll();
                    RepaintLinkedEditors();
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Clear Selection", smallButtonStyle))
                {
                    Undo.RegisterCompleteObjectUndo(this, "Clear Vertex Selection");
                    SelectedVertexes.Clear();
                    RepaintLinkedEditors();
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Invert State", smallButtonStyle))
                {
                    Undo.RegisterCompleteObjectUndo(this, "Invert Vertex State");
                    for (int i = 0; i < SelectedVertexes.Count; i++)
                    {
                        SelectedVertexes[i].isActive = !SelectedVertexes[i].isActive;
                    }
                    RepaintLinkedEditors();
                }
                if (GUILayout.Button("Activate all", smallButtonStyle))
                {
                    Undo.RegisterCompleteObjectUndo(this, "Activate Vertex State");
                    for (int i = 0; i < SelectedVertexes.Count; i++)
                    {
                        SelectedVertexes[i].isActive = true;
                    }
                    RepaintLinkedEditors();
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Deactivate all", smallButtonStyle))
                {
                    Undo.RegisterCompleteObjectUndo(this, "Deactivate Vertex State");
                    for (int i = 0; i < SelectedVertexes.Count; i++)
                    {
                        SelectedVertexes[i].isActive = false;
                    }
                        RepaintLinkedEditors();
                }
                GUILayout.EndHorizontal();
            }
            GUIHelper.EndVerticalPadded(5);
            #endregion

            //GUILayout.Label("camera: " + sceneView.camera.transform.position.ToString());
            if (GUILayout.Button("Reset Camera"))
            {
                Selection.activeObject = VertexObject;
                sceneView.AlignViewToObject(cameraAnchor.transform);
                sceneView.FrameSelected(true);
                sceneView.AlignViewToObject(cameraAnchor.transform);
            }
            GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.85f, 1f));
            if (currentDefineMode == DefineMode.DefineVertexSet)
            {
                if (selectionBrushModeSet)
                {
                    GUILayout.TextArea("Define Vertex Set mode\nSelect Brush enabled: click-drag applies the Selection Action to vertices under the cursor\nSelection Action is Add / Remove / Invert\nEach vertex is processed only once per stroke\n\nThis edits selection membership, not vertex colors. Hold Alt and use mouse buttons/wheel to navigate.", HelpBoxStyle);
                }
                else
                {
                    GUILayout.TextArea("Define Vertex Set mode\nClick a vertex, or click-drag a rectangle, to change the selection\nSelection Action is Add / Remove / Invert\n\nHold Alt and use mouse buttons/wheel to navigate.", HelpBoxStyle);
                }
            }
            else
            {
                if (selectionBrushModeState)
                {
                    GUILayout.TextArea("Define Vertex State mode\nOnly affects already selected vertices\nSelect Brush enabled: click-drag applies the State Action\nState Action is Toggle / Activate / Deactivate\nEach vertex is processed only once per stroke\n\nThis edits selection state, not vertex colors. Hold Alt and use mouse buttons/wheel to navigate.", HelpBoxStyle);
                }
                else
                {
                    GUILayout.TextArea("Define Vertex State mode\nOnly affects already selected vertices\nClick a selected vertex, or click-drag a rectangle, to apply State Action\nState Action is selected from Toggle / Activate / Deactivate\n\nHold Alt and use mouse buttons/wheel to navigate.", HelpBoxStyle);
                }
            }
            if (Event.current.type == EventType.Repaint)
            {
                Rect lastRect = GUILayoutUtility.GetLastRect();
                ToolWindowAreaHeight = Mathf.Max(50f, lastRect.yMax + 10f);
            }
            GUIHelper.EndVerticalPadded(5);
            GUILayout.EndArea();
            GUILayout.Space(ToolWindowAreaHeight + 10);
            GUILayout.EndScrollView();
            // Define a small drag area so the rest of the window is NOT draggable
        }

        private void DrawTouchupWeightsTools(SceneView sceneView)
        {
            sceneToolMode = SceneToolMode.SelectionBrush;
            currentDefineMode = DefineMode.DefineVertexSet;
            selectionBrushShape = SelectionBrushShape.Circle;
            currentMode = selectMode.Add;
            selectionBrushModeSet = true;
            selectionBrushModeState = true;

            EditorGUI.BeginChangeCheck();
            TouchupWeightTool requestedTool = (TouchupWeightTool)GUILayout.Toolbar(
                (int)touchupWeightTool,
                new[] { "Select Mode", "Paint Mode" });
            if (EditorGUI.EndChangeCheck() && requestedTool != touchupWeightTool)
            {
                CancelInteraction();
                touchupWeightTool = requestedTool;
                selectionBrushedVerticesThisStroke.Clear();
                touchupPaintStatusMessage = string.Empty;
            }

            if (touchupWeightTool == TouchupWeightTool.Select)
            {
                EditorGUILayout.HelpBox(
                    "Drag to select vertices on the current slot. Hold Shift to add and Ctrl to remove.",
                    MessageType.Info);

                touchupSelectionSmoothPercentage = EditorGUILayout.IntSlider(
                    new GUIContent(
                        "Smooth Percentage",
                        "Blend each selected vertex's complete weight set toward the average of its connected vertices."),
                    touchupSelectionSmoothPercentage,
                    0,
                    100);
                using (new EditorGUI.DisabledScope(
                           TouchupSelectionCount == 0 ||
                           touchupSelectionSmoothPercentage <= 0))
                {
                    if (GUILayout.Button("Smooth Vertex Weights"))
                    {
                        SmoothSelectedTouchupVertexWeights();
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Drag over the current slot to paint the bone selected in the Touchup Weights window. " +
                    "Every painted result is normalized; use Save Weights in that window to write the changes.",
                    MessageType.Info);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Bone", GUILayout.Width(72f));
                EditorGUILayout.LabelField(
                    GetBoneDisplayName(
                        touchupWeightBoneHash,
                        touchupWeightSlot != null && touchupWeightSlot.asset != null
                            ? GetBoundBoneIndex(touchupWeightSlot.asset.meshData, touchupWeightBoneHash)
                            : -1),
                    EditorStyles.miniBoldLabel);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Amount", GUILayout.Width(72f));
                touchupPaintAmount = EditorGUILayout.Slider(touchupPaintAmount, 0f, 1f);
                GUILayout.EndHorizontal();

                GUILayout.Label("Paint Operation", EditorStyles.miniBoldLabel);
                touchupWeightPaintMode = (TouchupWeightPaintMode)GUILayout.Toolbar(
                    (int)touchupWeightPaintMode,
                    new[] { "Replace", "Add", "Remove", "Smooth", "Smear" });

                touchupPaintSelectedVerticesOnly = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Selected Vertices Only",
                        "Restrict painting to vertices already selected in Select Mode."),
                    touchupPaintSelectedVerticesOnly);
                if (touchupPaintSelectedVerticesOnly && TouchupSelectionCount == 0)
                {
                    EditorGUILayout.HelpBox(
                        "Select at least one vertex in Select Mode before painting with this restriction.",
                        MessageType.Warning);
                }

                touchupAutoMaskConnectedVertices = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Auto-mask Connected Vertices",
                        "Protect vertices co-located with vertices on another visible slot so weight changes cannot open gaps between slots."),
                    touchupAutoMaskConnectedVertices);
                if (touchupAutoMaskConnectedVertices &&
                    touchupCrossSlotMaskedVertices != null &&
                    touchupCrossSlotMaskedVertices.Count > 0)
                {
                    EditorGUILayout.LabelField(
                        touchupCrossSlotMaskedVertices.Count + " cross-slot vertices protected.",
                        EditorStyles.miniLabel);
                }

                if (touchupWeightPaintMode == TouchupWeightPaintMode.Smooth)
                {
                    touchupSmoothSelectedBoneOnly = EditorGUILayout.Toggle(
                        new GUIContent(
                            "Selected Bone Only",
                            "Smooth only the selected bone. When disabled, all influences are smoothed together."),
                        touchupSmoothSelectedBoneOnly);
                }

                string operationHelp;
                switch (touchupWeightPaintMode)
                {
                    case TouchupWeightPaintMode.Replace:
                        operationHelp = "Replace sets the selected bone to Amount and proportionally scales the other influences.";
                        break;
                    case TouchupWeightPaintMode.Add:
                        operationHelp = "Add increases the selected bone by Amount, then normalizes all influences.";
                        break;
                    case TouchupWeightPaintMode.Remove:
                        operationHelp = "Remove subtracts Amount from the selected bone, then normalizes all influences.";
                        break;
                    case TouchupWeightPaintMode.Smooth:
                        operationHelp = touchupSmoothSelectedBoneOnly
                            ? "Smooth moves the selected bone toward the average of connected vertices by Amount."
                            : "Smooth moves all influences toward the average of connected vertices by Amount.";
                        break;
                    default:
                        operationHelp =
                            "Smear pulls the selected bone's weights from the previous brush position in the stroke direction by Amount, then normalizes the other influences.";
                        break;
                }
                EditorGUILayout.HelpBox(operationHelp, MessageType.None);

            }

            if (!string.IsNullOrEmpty(touchupPaintStatusMessage))
            {
                EditorGUILayout.HelpBox(touchupPaintStatusMessage, touchupPaintStatusType);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Slot", GUILayout.Width(72f));
            EditorGUILayout.LabelField(
                touchupWeightSlot != null ? touchupWeightSlot.slotName : "No slot selected",
                EditorStyles.miniBoldLabel);
            GUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(TouchupSelectionCount == 0))
            {
                if (GUILayout.Button("Clear Selection"))
                {
                    ClearTouchupSelection();
                }
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Brush Radius", GUILayout.Width(88f));
            selectionBrushRadiusPixels = EditorGUILayout.IntSlider(
                selectionBrushRadiusPixels,
                MinSelectionBrushRadiusPixels,
                MaxSelectionBrushRadiusPixels);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Handle Size", GUILayout.Width(88f));
            HandlesSize = EditorGUILayout.Slider(HandlesSize, 0f, 0.04f);
            GUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            touchupLiveUpdate = EditorGUILayout.Toggle(
                new GUIContent(
                    "Live update",
                    "Recalculate edited vertex positions from the current skeleton and preview weights every editor frame."),
                touchupLiveUpdate);
            if (EditorGUI.EndChangeCheck() && touchupLiveUpdate)
            {
                OnTouchupLiveUpdate();
            }

            selectObscured = EditorGUILayout.Toggle(
                new GUIContent("Select Obscured", "Allow the brush to select vertices hidden behind the visible surface."),
                selectObscured);
            using (new EditorGUI.DisabledScope(!selectObscured))
            {
                selectFacingAway = EditorGUILayout.Toggle(
                    new GUIContent("Select Backfacing", "Allow the brush to select vertices facing away from the camera."),
                    selectFacingAway);
            }

            if (GUILayout.Button("Reset Camera") && sceneView != null)
            {
                FrameTouchupWeightSlot(sceneView);
            }
        }

        private void FrameTouchupWeightSlot(SceneView sceneView)
        {
            if (sceneView == null || touchupWeightSlot == null || touchupWeightSlot.asset == null ||
                UMAMeshData.IsNullOrEmptyMeshData(touchupWeightSlot.asset.meshData) ||
                BakedMesh == null || VertexObject == null)
            {
                return;
            }

            int vertexStart = touchupWeightSlot.vertexOffset;
            int vertexCount = touchupWeightSlot.asset.meshData.vertexCount;
            Vector3[] vertices = BakedMesh.vertices;
            if (vertexStart < 0 || vertexCount <= 0 || vertexStart + vertexCount > vertices.Length)
            {
                return;
            }

            Bounds bounds = new Bounds(
                VertexObject.transform.TransformPoint(vertices[vertexStart]),
                Vector3.zero);
            for (int vertexIndex = 1; vertexIndex < vertexCount; vertexIndex++)
            {
                bounds.Encapsulate(
                    VertexObject.transform.TransformPoint(vertices[vertexStart + vertexIndex]));
            }
            bounds.Expand(Mathf.Max(0.001f, bounds.size.magnitude * 0.01f));
            sceneView.Frame(bounds, false);
            sceneView.Repaint();
        }

        private void DrawSelectionBrushOptions()
        {
            if (!IsSelectionBrushModeEnabled)
            {
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Select Brush", GUILayout.Width(92));
            selectionBrushShape = (SelectionBrushShape)GUILayout.Toolbar((int)selectionBrushShape, new string[] { "Point", "Circle" });
            GUILayout.EndHorizontal();

            if (selectionBrushShape == SelectionBrushShape.Circle)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Radius", GUILayout.Width(92));
                selectionBrushRadiusPixels = EditorGUILayout.IntSlider(selectionBrushRadiusPixels, MinSelectionBrushRadiusPixels, MaxSelectionBrushRadiusPixels);
                GUILayout.EndHorizontal();
            }
        }

        private void DrawVertexPaintOptions()
        {
            EnsureVertexPaintSession();
            GUILayout.Label("Vertex Paint", centeredLabel);
            EditorGUILayout.HelpBox(
                "Paint the selected slot's existing vertex colors. Slots without color data begin white. " +
                "Painting creates the existing VertexColorAdjustment data used by MeshModifiers.",
                MessageType.Info);

            if (vertexPaintSlots.Count == 0 || vertexPaintSlot == null)
            {
                EditorGUILayout.HelpBox("No visible editable slot is available.", MessageType.Warning);
                return;
            }

            int oldSlotIndex = vertexPaintSlotIndex;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Slot", GUILayout.Width(92));
            vertexPaintSlotIndex = EditorGUILayout.Popup(vertexPaintSlotIndex, vertexPaintSlotNames.ToArray());
            bool frameSlot = GUILayout.Button(new GUIContent("F", "Frame the selected vertex-paint slot in the Scene view."), EditorStyles.miniButton, GUILayout.Width(26));
            GUILayout.EndHorizontal();
            if (oldSlotIndex != vertexPaintSlotIndex)
            {
                EndVertexPaintStroke(true);
                vertexPaintModifierName = string.Empty;
                EnsureVertexPaintSession(true);
            }
            if (frameSlot)
            {
                if (IsVertexPaintAllSlotsMode) FrameAllEditableSlots();
                else FrameSelectedVertexPaintSlot();
            }
            if (IsVertexPaintAllSlotsMode)
            {
                EditorGUILayout.HelpBox(
                    $"All Slots is active. The brush follows the slot under the pointer. Current target: {(vertexPaintSlot != null ? vertexPaintSlot.slotName : "None")}.",
                    MessageType.None);
            }

            vertexPaintTool = (VertexPaintTool)EditorGUILayout.EnumPopup(
                new GUIContent("Behavior", "How the brush changes vertex colors."),
                vertexPaintTool);
            if (vertexPaintTool == VertexPaintTool.Paint)
            {
                vertexPaintBrushColor = EditorGUILayout.ColorField(
                    new GUIContent("Brush Color", "Paint multiplies the existing vertex color by this color."),
                    vertexPaintBrushColor);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Brush", GUILayout.Width(92));
            vertexPaintBrushShape = (VertexPaintBrushShape)GUILayout.Toolbar(
                (int)vertexPaintBrushShape,
                new[]
                {
                    new GUIContent("Circle", "Circular radial brush."),
                    new GUIContent("Square", "Square planar brush."),
                    new GUIContent("Bitmap", "Use a texture's alpha and grayscale as the brush mask.")
                });
            GUILayout.EndHorizontal();

            if (vertexPaintBrushShape == VertexPaintBrushShape.Bitmap)
            {
                EditorGUI.BeginChangeCheck();
                vertexPaintBrushTexture = (Texture2D)EditorGUILayout.ObjectField(
                    new GUIContent("Brush Bitmap", "A user-provided bitmap. Alpha and luminance define brush strength."),
                    vertexPaintBrushTexture,
                    typeof(Texture2D),
                    false);
                if (EditorGUI.EndChangeCheck())
                {
                    DestroyVertexPaintBrushCache();
                }
                if (vertexPaintBrushTexture == null)
                {
                    EditorGUILayout.HelpBox("Assign a bitmap before painting with the Bitmap brush.", MessageType.Warning);
                }
            }

            vertexPaintRadius = EditorGUILayout.Slider(
                new GUIContent("Size", "World-space brush radius."),
                vertexPaintRadius,
                0.001f,
                0.5f);
            if (vertexPaintTool != VertexPaintTool.Clear)
            {
                GUIContent effectLabel = vertexPaintTool == VertexPaintTool.Smooth
                    ? new GUIContent("Smooth Amount %", "How strongly each stroke moves colors toward their world-space neighborhood average.")
                    : new GUIContent("Effect %", "Maximum effect a vertex can receive during one stroke.");
                vertexPaintStrengthPercent = EditorGUILayout.Slider(
                    effectLabel,
                    vertexPaintStrengthPercent,
                    0f,
                    100f);
            }
            if (vertexPaintTool == VertexPaintTool.Smooth)
            {
                vertexPaintSmoothRadius = EditorGUILayout.Slider(
                    new GUIContent(
                        "Smooth Radius",
                        "World-space radius sampled around each affected vertex. Larger values blend broader color transitions and cross UV seams."),
                    vertexPaintSmoothRadius,
                    0.001f,
                    0.5f);
            }
            vertexPaintFalloff = (SculptFalloff)EditorGUILayout.EnumPopup("Falloff", vertexPaintFalloff);
            if (vertexPaintFalloff == SculptFalloff.UserDefined)
            {
                vertexPaintCustomFalloff = EditorGUILayout.CurveField(
                    "Curve",
                    vertexPaintCustomFalloff,
                    Color.green,
                    new Rect(0f, 0f, 1f, 1f));
            }
            vertexPaintSymmetryX = EditorGUILayout.Toggle(
                new GUIContent(
                    "X Symmetry",
                    "Repeat every brush sample across the character's local X axis, including square/bitmap orientation and stroke direction."),
                vertexPaintSymmetryX);
            vertexPaintAffectAlpha = EditorGUILayout.Toggle(
                new GUIContent("Affect Alpha", "Allow Paint, Erase, Smear, Blur, Smooth, Noise, and Clear to modify vertex alpha."),
                vertexPaintAffectAlpha);

            if (vertexPaintTool == VertexPaintTool.Noise)
            {
                vertexPaintNoiseScale = EditorGUILayout.Slider(
                    new GUIContent("Noise Scale", "Spatial frequency of the procedural noise."),
                    vertexPaintNoiseScale,
                    0.1f,
                    100f);
                vertexPaintNoiseAmount = EditorGUILayout.Slider(
                    new GUIContent("Noise Amount", "Maximum color variation introduced by Noise."),
                    vertexPaintNoiseAmount,
                    0f,
                    1f);
                vertexPaintNoiseSeed = EditorGUILayout.IntField("Noise Seed", vertexPaintNoiseSeed);
            }

            string behaviorDescription;
            switch (vertexPaintTool)
            {
                case VertexPaintTool.Paint: behaviorDescription = "Paint multiplies the current color by the brush color."; break;
                case VertexPaintTool.Erase: behaviorDescription = "Erase moves painted colors back toward white."; break;
                case VertexPaintTool.Smear: behaviorDescription = "Smear pulls nearby color in the direction of the stroke."; break;
                case VertexPaintTool.Blur: behaviorDescription = "Blur averages connected neighboring vertex colors."; break;
                case VertexPaintTool.Smooth: behaviorDescription = "Smooth blends a distance-weighted world-space neighborhood, including seams and nearby disconnected vertices while avoiding oppositely facing surfaces."; break;
                case VertexPaintTool.Burn: behaviorDescription = "Burn darkens the existing RGB values."; break;
                case VertexPaintTool.Dodge: behaviorDescription = "Dodge lightens the existing RGB values."; break;
                case VertexPaintTool.Noise: behaviorDescription = "Noise adds repeatable procedural color variation."; break;
                default: behaviorDescription = "Clear resets every affected channel to white."; break;
            }
            EditorGUILayout.HelpBox(behaviorDescription, MessageType.None);

            EditorGUILayout.Space(6f);
            GUILayout.Label("General Smooth", EditorStyles.miniBoldLabel);
            vertexPaintGeneralSmoothPercent = EditorGUILayout.Slider(
                new GUIContent(
                    "Amount %",
                    "How strongly every affected vertex moves toward its world-space neighborhood average."),
                vertexPaintGeneralSmoothPercent,
                0f,
                100f);
            vertexPaintGeneralSmoothRadius = EditorGUILayout.Slider(
                new GUIContent(
                    "Radius",
                    "World-space neighborhood sampled around every vertex in the current slot or all editable slots."),
                vertexPaintGeneralSmoothRadius,
                0.001f,
                0.5f);
            vertexPaintGeneralSmoothCutoffPercent = EditorGUILayout.Slider(
                new GUIContent(
                    "Cutoff %",
                    "Return a vertex to its unmodified slot color when its largest resulting channel difference is below this percentage, preventing imperceptible smoothing from spreading into the MeshModifier."),
                vertexPaintGeneralSmoothCutoffPercent,
                0f,
                10f);

            string generalSmoothTarget = IsVertexPaintAllSlotsMode ? "All Slots" : "Current Slot";
            using (new EditorGUI.DisabledScope(vertexPaintGeneralSmoothPercent <= 0f))
            {
                if (GUILayout.Button(new GUIContent(
                        "Smooth " + generalSmoothTarget,
                        "Apply one simultaneous world-space color smoothing pass to " + generalSmoothTarget.ToLowerInvariant() + ".")))
                {
                    SmoothVertexPaintSlots();
                }
            }
            EditorGUILayout.HelpBox(
                "Only vertices whose resulting colors differ from the unmodified slot by at least Cutoff % are retained as modifier adjustments.",
                MessageType.None);

            EditorGUI.BeginDisabledGroup(IsVertexPaintAllSlotsMode);
            if (GUILayout.Button(new GUIContent("Clear Entire Slot to White", "Reset every vertex color on the selected slot to white. Select a specific slot to use this command.")))
            {
                ClearVertexPaintSlotToWhite();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(6f);
            GUILayout.Label("Save Painted Colors", EditorStyles.miniBoldLabel);
            vertexPaintModifierName = EditorGUILayout.TextField("Modifier Name", vertexPaintModifierName);
            EditorGUI.BeginDisabledGroup(!vertexPaintDirty && !HasSculptChanges());
            if (GUILayout.Button(new GUIContent("Save as MeshModifier", "Save vertex-color adjustments together with the current modifier and sculpt work.")))
            {
                SaveSculptModifier();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!vertexPaintDirty || IsVertexPaintAllSlotsMode);
            if (GUILayout.Button(new GUIContent("Save Colors to SlotDataAsset", "Bake the previewed colors directly into the selected SlotDataAsset's mesh data.")))
            {
                SaveVertexPaintToSlotDataAsset();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.HelpBox(
                "MeshModifier keeps all painted slots non-destructive and portable. To write colors into one SlotDataAsset, select that specific slot first.",
                MessageType.Info);
        }

        private void RefreshVertexPaintSlots(SlotData preferredSlot = null)
        {
            bool preserveAllSlots = vertexPaintSlotIndex == 0;
            if (preferredSlot == null)
            {
                preferredSlot = vertexPaintSlot;
            }

            vertexPaintSlots.Clear();
            vertexPaintSlotNames.Clear();
            vertexPaintSlotNames.Add("All Slots");
            if (thisDCA != null && thisDCA.umaData != null && thisDCA.umaData.umaRecipe != null)
            {
                foreach (SlotData slot in thisDCA.umaData.umaRecipe.slotDataList)
                {
                    if (!IsSelectableSlot(slot))
                    {
                        continue;
                    }
                    vertexPaintSlots.Add(slot);
                    vertexPaintSlotNames.Add(slot.slotName);
                }
            }

            vertexPaintSlotIndex = Mathf.Clamp(vertexPaintSlotIndex, 0, vertexPaintSlots.Count);
            if (preserveAllSlots)
            {
                vertexPaintSlotIndex = 0;
            }
            else if (preferredSlot != null)
            {
                int found = vertexPaintSlots.FindIndex(slot => ReferenceEquals(slot, preferredSlot));
                if (found < 0)
                {
                    found = vertexPaintSlots.FindIndex(slot => slot != null && slot.slotName == preferredSlot.slotName);
                }
                if (found >= 0)
                {
                    vertexPaintSlotIndex = found + 1;
                }
            }
        }

        private void EnsureVertexPaintSession(bool force = false)
        {
            SlotData preferredSlot = vertexPaintSlotIndex > 0 && vertexPaintSlotIndex <= vertexPaintSlots.Count
                ? vertexPaintSlots[vertexPaintSlotIndex - 1]
                : vertexPaintSlot;
            RefreshVertexPaintSlots(preferredSlot);
            if (!vertexPaintDefaultSlotChosen)
            {
                vertexPaintSlotIndex = 0;
                vertexPaintDefaultSlotChosen = true;
            }

            if (!vertexPaintPreviewInitialized)
            {
                InitializeVertexPaintPreview();
            }

            SlotData requested = null;
            if (vertexPaintSlots.Count > 0)
            {
                requested = IsVertexPaintAllSlotsMode
                    ? (vertexPaintSlots.Contains(vertexPaintSlot) ? vertexPaintSlot : vertexPaintSlots[0])
                    : vertexPaintSlots[Mathf.Clamp(vertexPaintSlotIndex - 1, 0, vertexPaintSlots.Count - 1)];
            }
            ActivateVertexPaintSlot(requested, force);
        }

        private void ActivateVertexPaintSlot(SlotData requested, bool force = false)
        {
            if (!force && ReferenceEquals(requested, vertexPaintSlot) && vertexPaintColors != null &&
                requested != null && vertexPaintSlotVertexCount == requested.asset.meshData.vertexCount)
            {
                ApplyVertexDisplayOptions();
                return;
            }

            EndVertexPaintStroke(true);
            vertexPaintSlot = requested;
            vertexPaintSlotStart = -1;
            vertexPaintSlotVertexCount = 0;
            vertexPaintBaseColors = null;
            vertexPaintColors = null;
            vertexPaintNeighbors = null;
            vertexPaintCoincidentVertices = null;
            vertexPaintStrokeApplied = null;
            if (vertexPaintSlot == null || vertexPaintSlot.asset == null || BakedMesh == null)
            {
                return;
            }

            vertexPaintSlotVertexCount = vertexPaintSlot.asset.meshData.vertexCount;
            vertexPaintSlotStart = GetVisibleBakedVertexIndex(vertexPaintSlot, 0);
            if (vertexPaintSlotStart < 0 || vertexPaintSlotStart + vertexPaintSlotVertexCount > BakedMesh.vertexCount)
            {
                vertexPaintSlotVertexCount = 0;
                return;
            }

            vertexPaintBaseColors = BuildVertexPaintColors(vertexPaintSlot, false);
            vertexPaintColors = BuildVertexPaintColors(vertexPaintSlot, true);
            vertexPaintStrokeApplied = new float[vertexPaintSlotVertexCount];
            BuildVertexPaintTopology();
            ApplyVertexPaintColorsToPreview(vertexPaintSlotStart, vertexPaintColors);
            if (string.IsNullOrEmpty(vertexPaintModifierName))
            {
                vertexPaintModifierName = IsVertexPaintAllSlotsMode
                    ? "All Slots Vertex Paint"
                    : GetVertexPaintSlotKey() + " Vertex Paint";
            }
            ApplyVertexDisplayOptions();
        }

        private void InitializeVertexPaintPreview()
        {
            if (BakedMesh == null)
            {
                return;
            }

            Color32[] allColors = new Color32[BakedMesh.vertexCount];
            FillWhite(allColors);
            RefreshVertexPaintSlots();
            for (int slotIndex = 0; slotIndex < vertexPaintSlots.Count; slotIndex++)
            {
                SlotData slot = vertexPaintSlots[slotIndex];
                if (slot == null || slot.asset == null)
                {
                    continue;
                }

                int start = GetVisibleBakedVertexIndex(slot, 0);
                int count = slot.asset.meshData.vertexCount;
                if (start < 0 || start + count > allColors.Length)
                {
                    continue;
                }

                Color32[] slotColors = BuildVertexPaintColors(slot, true);
                Array.Copy(slotColors, 0, allColors, start, Mathf.Min(count, slotColors.Length));
            }

            BakedMesh.colors32 = allColors;
            vertexPaintPreviewInitialized = true;
            EditorUtility.SetDirty(BakedMesh);
        }

        private Color32[] BuildVertexPaintColors(SlotData slot, bool includeAdHoc)
        {
            int count = slot != null && slot.asset != null ? slot.asset.meshData.vertexCount : 0;
            Color32[] colors = new Color32[count];
            if (count == 0)
            {
                return colors;
            }

            Color32[] sourceColors = slot.asset.meshData.colors32;
            if (sourceColors != null && sourceColors.Length == count)
            {
                Array.Copy(sourceColors, colors, count);
            }
            else
            {
                FillWhite(colors);
            }

            string slotKey = GetVertexPaintSlotKey(slot);
            if (modifierEditor != null && modifierEditor.Modifiers != null)
            {
                for (int modifierIndex = 0; modifierIndex < modifierEditor.Modifiers.Count; modifierIndex++)
                {
                    MeshModifier.Modifier modifier = modifierEditor.Modifiers[modifierIndex];
                    if (modifier == null || !(modifier.adjustments is VertexColorAdjustmentCollection) ||
                        modifier.adjustments.vertexAdjustments == null)
                    {
                        continue;
                    }

                    for (int adjustmentIndex = 0; adjustmentIndex < modifier.adjustments.vertexAdjustments.Count; adjustmentIndex++)
                    {
                        VertexColorAdjustment adjustment = modifier.adjustments.vertexAdjustments[adjustmentIndex] as VertexColorAdjustment;
                        if (adjustment == null || !VertexPaintAdjustmentMatchesSlot(adjustment, modifier.SlotName, slot, slotKey))
                        {
                            continue;
                        }
                        ApplyVertexColorAdjustment(colors, adjustment, modifier.Scale);
                    }
                }
            }

            if (includeAdHoc && Adjustments != null)
            {
                for (int adjustmentIndex = 0; adjustmentIndex < Adjustments.Count; adjustmentIndex++)
                {
                    VertexColorAdjustment adjustment = Adjustments[adjustmentIndex] as VertexColorAdjustment;
                    if (adjustment == null || !VertexPaintAdjustmentMatchesSlot(adjustment, null, slot, slotKey))
                    {
                        continue;
                    }
                    ApplyVertexColorAdjustment(colors, adjustment, 1f);
                }
            }

            return colors;
        }

        private static void ApplyVertexColorAdjustment(Color32[] colors, VertexColorAdjustment adjustment, float modifierScale)
        {
            if (colors == null || adjustment == null || adjustment.vertexIndex < 0 || adjustment.vertexIndex >= colors.Length)
            {
                return;
            }

            float scale = Mathf.Clamp01(modifierScale * adjustment.weight);
            colors[adjustment.vertexIndex] = (Color32)Color.Lerp(colors[adjustment.vertexIndex], adjustment.color, scale);
        }

        private static bool VertexPaintAdjustmentMatchesSlot(VertexAdjustment adjustment, string modifierSlotName, SlotData slot, string slotKey)
        {
            if (adjustment == null || slot == null)
            {
                return false;
            }

            string adjustmentSlot = string.IsNullOrEmpty(adjustment.slotName) ? modifierSlotName : adjustment.slotName;
            return string.Equals(adjustmentSlot, slotKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(adjustmentSlot, slot.slotName, StringComparison.OrdinalIgnoreCase);
        }

        private static void FillWhite(Color32[] colors)
        {
            if (colors == null)
            {
                return;
            }
            Color32 white = new Color32(255, 255, 255, 255);
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = white;
            }
        }

        private int FindDefaultVertexPaintSlotIndex()
        {
            if (vertexPaintSlots.Count == 0)
            {
                return 0;
            }

            HashSet<string> baseSlotNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                RaceData race = thisDCA != null && thisDCA.activeRace != null ? thisDCA.activeRace.data : null;
                UMARecipeBase baseRecipeAsset = race != null ? race.baseRaceRecipe : null;
                UMAData.UMARecipe baseRecipe = baseRecipeAsset != null ? baseRecipeAsset.GetCachedRecipe() : null;
                if (baseRecipe != null && baseRecipe.slotDataList != null)
                {
                    for (int i = 0; i < baseRecipe.slotDataList.Length; i++)
                    {
                        SlotData baseSlot = baseRecipe.slotDataList[i];
                        if (baseSlot != null && !string.IsNullOrEmpty(baseSlot.slotName))
                        {
                            baseSlotNames.Add(baseSlot.slotName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Unable to determine the default non-base vertex paint slot: " + ex.Message);
            }

            for (int i = 0; i < vertexPaintSlots.Count; i++)
            {
                SlotData slot = vertexPaintSlots[i];
                if (slot != null && !baseSlotNames.Contains(slot.slotName))
                {
                    return i;
                }
            }
            return 0;
        }

        private void BuildVertexPaintTopology()
        {
            vertexPaintNeighbors = new List<int>[vertexPaintSlotVertexCount];
            for (int i = 0; i < vertexPaintSlotVertexCount; i++)
            {
                vertexPaintNeighbors[i] = new List<int>(6);
            }

            int[] triangles = BakedMesh.triangles;
            for (int triangleIndex = 0; triangleIndex + 2 < triangles.Length; triangleIndex += 3)
            {
                int a = triangles[triangleIndex] - vertexPaintSlotStart;
                int b = triangles[triangleIndex + 1] - vertexPaintSlotStart;
                int c = triangles[triangleIndex + 2] - vertexPaintSlotStart;
                if (a < 0 || b < 0 || c < 0 || a >= vertexPaintSlotVertexCount ||
                    b >= vertexPaintSlotVertexCount || c >= vertexPaintSlotVertexCount)
                {
                    continue;
                }
                AddVertexPaintEdge(a, b);
                AddVertexPaintEdge(b, c);
                AddVertexPaintEdge(c, a);
            }

            BuildVertexPaintCoincidentMap();
        }

        private void AddVertexPaintEdge(int a, int b)
        {
            if (!vertexPaintNeighbors[a].Contains(b)) vertexPaintNeighbors[a].Add(b);
            if (!vertexPaintNeighbors[b].Contains(a)) vertexPaintNeighbors[b].Add(a);
        }

        private void BuildVertexPaintCoincidentMap()
        {
            vertexPaintCoincidentVertices = new List<int>[vertexPaintSlotVertexCount];
            if (BakedMesh == null || vertexPaintSlotStart < 0)
            {
                return;
            }

            Vector3[] vertices = BakedMesh.vertices;
            float tolerance = Mathf.Max(0.000001f, BakedMesh.bounds.size.magnitude * 0.000001f);
            float inverseTolerance = 1f / tolerance;
            Dictionary<Vector3Int, List<int>> groups = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < vertexPaintSlotVertexCount; i++)
            {
                Vector3 position = vertices[vertexPaintSlotStart + i];
                Vector3Int key = new Vector3Int(
                    Mathf.RoundToInt(position.x * inverseTolerance),
                    Mathf.RoundToInt(position.y * inverseTolerance),
                    Mathf.RoundToInt(position.z * inverseTolerance));
                if (!groups.TryGetValue(key, out List<int> group))
                {
                    group = new List<int>();
                    groups.Add(key, group);
                }
                group.Add(i);
            }

            foreach (List<int> group in groups.Values)
            {
                for (int i = 0; i < group.Count; i++)
                {
                    vertexPaintCoincidentVertices[group[i]] = group;
                }
            }
        }

        private float EvaluateVertexPaintFalloff(float normalizedDistance)
        {
            float x = Mathf.Clamp01(normalizedDistance);
            switch (vertexPaintFalloff)
            {
                case SculptFalloff.Constant: return 1f;
                case SculptFalloff.Linear: return 1f - x;
                case SculptFalloff.EaseIn: return 1f - x * x;
                case SculptFalloff.EaseOut: { float y = 1f - x; return y * y; }
                case SculptFalloff.EaseInOut: return Mathf.SmoothStep(1f, 0f, x);
                case SculptFalloff.Sharp: return Mathf.Pow(1f - x, 4f);
                case SculptFalloff.UserDefined: return Mathf.Clamp01(vertexPaintCustomFalloff.Evaluate(x));
                default: { float y = 1f - x; return y * y * (3f - 2f * y); }
            }
        }

        private float EvaluateVertexPaintBrushMask(float u, float v)
        {
            if (vertexPaintBrushShape != VertexPaintBrushShape.Bitmap)
            {
                return 1f;
            }

            EnsureVertexPaintBrushCache();
            if (vertexPaintReadableBrush == null)
            {
                return 0f;
            }

            float diameter = Mathf.Max(0.000001f, vertexPaintRadius * 2f);
            float textureU = u / diameter + 0.5f;
            float textureV = v / diameter + 0.5f;
            if (textureU < 0f || textureU > 1f || textureV < 0f || textureV > 1f)
            {
                return 0f;
            }

            Color sample = vertexPaintReadableBrush.GetPixelBilinear(textureU, textureV);
            return Mathf.Clamp01(sample.a * sample.grayscale);
        }

        private void EnsureVertexPaintBrushCache()
        {
            if (vertexPaintBrushTexture == vertexPaintCachedBrushSource && vertexPaintReadableBrush != null)
            {
                return;
            }

            DestroyVertexPaintBrushCache();
            vertexPaintCachedBrushSource = vertexPaintBrushTexture;
            if (vertexPaintBrushTexture == null)
            {
                return;
            }

            int sourceWidth = Mathf.Max(1, vertexPaintBrushTexture.width);
            int sourceHeight = Mathf.Max(1, vertexPaintBrushTexture.height);
            float downScale = Mathf.Min(1f, 512f / Mathf.Max(sourceWidth, sourceHeight));
            int width = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * downScale));
            int height = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * downScale));
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(vertexPaintBrushTexture, temporary);
                RenderTexture.active = temporary;
                vertexPaintReadableBrush = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                {
                    name = vertexPaintBrushTexture.name + " Vertex Paint Cache",
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                vertexPaintReadableBrush.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                vertexPaintReadableBrush.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private void DestroyVertexPaintBrushCache()
        {
            if (vertexPaintReadableBrush != null)
            {
                DestroyImmediate(vertexPaintReadableBrush);
                vertexPaintReadableBrush = null;
            }
            vertexPaintCachedBrushSource = null;
        }

        private void SyncVertexPaintAdjustments()
        {
            if (vertexPaintSlot == null || vertexPaintColors == null || vertexPaintBaseColors == null)
            {
                return;
            }

            string slotKey = GetVertexPaintSlotKey();
            for (int i = Adjustments.Count - 1; i >= 0; i--)
            {
                VertexColorAdjustment adjustment = Adjustments[i] as VertexColorAdjustment;
                if (adjustment != null && VertexPaintAdjustmentMatchesSlot(adjustment, null, vertexPaintSlot, slotKey))
                {
                    Adjustments.RemoveAt(i);
                }
            }

            int count = Mathf.Min(vertexPaintColors.Length, vertexPaintBaseColors.Length);
            for (int i = 0; i < count; i++)
            {
                if (ColorsEqual(vertexPaintColors[i], vertexPaintBaseColors[i]))
                {
                    continue;
                }
                Adjustments.Add(new VertexColorAdjustment
                {
                    vertexIndex = i,
                    slotName = slotKey,
                    weight = 1f,
                    color = vertexPaintColors[i]
                });
            }
            EditorUtility.SetDirty(this);
        }

        private static bool ColorsEqual(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        private string GetVertexPaintSlotKey()
        {
            return GetVertexPaintSlotKey(vertexPaintSlot);
        }

        private static string GetVertexPaintSlotKey(SlotData slot)
        {
            if (slot == null)
            {
                return string.Empty;
            }
            return slot.asset != null && !string.IsNullOrEmpty(slot.asset.sourceSlot)
                ? slot.asset.sourceSlot
                : slot.slotName;
        }

        private void ApplyVertexPaintColorsToPreview(int start, Color32[] colors)
        {
            if (BakedMesh == null || colors == null || start < 0 || start + colors.Length > BakedMesh.vertexCount)
            {
                return;
            }

            Color32[] allColors = BakedMesh.colors32;
            if (allColors == null || allColors.Length != BakedMesh.vertexCount)
            {
                allColors = new Color32[BakedMesh.vertexCount];
                FillWhite(allColors);
            }
            Array.Copy(colors, 0, allColors, start, colors.Length);
            BakedMesh.colors32 = allColors;
            EditorUtility.SetDirty(BakedMesh);
        }

        private void SmoothVertexPaintSlots()
        {
            EnsureVertexPaintSession();
            EndVertexPaintStroke(true);
            if (BakedMesh == null || vertexPaintSlot == null || vertexPaintGeneralSmoothPercent <= 0f)
            {
                return;
            }

            SlotData originalSlot = vertexPaintSlot;
            List<SlotData> targetSlots = IsVertexPaintAllSlotsMode
                ? new List<SlotData>(vertexPaintSlots)
                : new List<SlotData> { vertexPaintSlot };
            float smoothAmount = Mathf.Clamp01(vertexPaintGeneralSmoothPercent * 0.01f);
            float cutoffPercent = Mathf.Max(0f, vertexPaintGeneralSmoothCutoffPercent);
            bool anyChanged = false;
            bool undoRegistered = false;
            int undoGroup = -1;

            Vector3[] vertices = BakedMesh.vertices;
            Vector3[] normals = BakedMesh.normals;
            for (int slotIndex = 0; slotIndex < targetSlots.Count; slotIndex++)
            {
                SlotData targetSlot = targetSlots[slotIndex];
                ActivateVertexPaintSlot(targetSlot, true);
                if (vertexPaintColors == null || vertexPaintColors.Length == 0 ||
                    vertexPaintBaseColors == null || vertexPaintBaseColors.Length != vertexPaintColors.Length ||
                    vertexPaintSlotStart < 0 || vertexPaintSlotVertexCount != vertexPaintColors.Length)
                {
                    continue;
                }

                Color32[] source = (Color32[])vertexPaintColors.Clone();
                Color32[] smoothed = (Color32[])source.Clone();
                BuildVertexPaintWorldSmoothingMap(
                    vertices,
                    normals,
                    vertexPaintGeneralSmoothRadius);

                bool slotChanged = false;
                for (int vertexIndex = 0; vertexIndex < source.Length; vertexIndex++)
                {
                    Color current = source[vertexIndex];
                    Color target = GetVertexPaintWorldAverage(
                        vertexIndex,
                        source,
                        vertexPaintGeneralSmoothRadius);
                    if (!vertexPaintAffectAlpha)
                    {
                        target.a = current.a;
                    }

                    Color result = Color.Lerp(current, target, smoothAmount);
                    Color baseColor = vertexPaintBaseColors[vertexIndex];
                    Color32 result32;
                    if (GetVertexPaintColorDifferencePercent(
                            baseColor,
                            result,
                            vertexPaintAffectAlpha) < cutoffPercent)
                    {
                        Color cutoffResult = baseColor;
                        if (!vertexPaintAffectAlpha)
                        {
                            cutoffResult.a = current.a;
                        }
                        result32 = cutoffResult;
                    }
                    else
                    {
                        result32 = result;
                    }
                    if (ColorsEqual(source[vertexIndex], result32))
                    {
                        continue;
                    }

                    smoothed[vertexIndex] = result32;
                    slotChanged = true;
                }

                if (!slotChanged)
                {
                    continue;
                }

                if (!undoRegistered)
                {
                    Undo.IncrementCurrentGroup();
                    undoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Smooth Vertex Colors");
                    Undo.RegisterCompleteObjectUndo(
                        new UnityEngine.Object[] { this, BakedMesh },
                        "Smooth Vertex Colors");
                    undoRegistered = true;
                }

                vertexPaintColors = smoothed;
                ApplyVertexPaintColorsToPreview(vertexPaintSlotStart, vertexPaintColors);
                SyncVertexPaintAdjustments();
                vertexPaintDirtySlots.Add(GetVertexPaintSlotKey());
                anyChanged = true;
            }

            vertexPaintWorldSmoothingCells = null;
            vertexPaintWorldSmoothingPositions = null;
            vertexPaintWorldSmoothingNormals = null;
            ActivateVertexPaintSlot(originalSlot, true);

            if (undoRegistered)
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
            if (anyChanged)
            {
                vertexPaintDirty = true;
                EditorUtility.SetDirty(BakedMesh);
                EditorUtility.SetDirty(this);
                RepaintLinkedEditors();
                SceneView.RepaintAll();
            }
        }

        private static float GetVertexPaintColorDifferencePercent(Color a, Color b, bool includeAlpha)
        {
            float difference = Mathf.Max(
                Mathf.Abs(a.r - b.r),
                Mathf.Abs(a.g - b.g),
                Mathf.Abs(a.b - b.b));
            if (includeAlpha)
            {
                difference = Mathf.Max(difference, Mathf.Abs(a.a - b.a));
            }
            return difference * 100f;
        }

        private void ClearVertexPaintSlotToWhite()
        {
            EnsureVertexPaintSession();
            if (vertexPaintColors == null)
            {
                return;
            }

            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { this, BakedMesh }, "Clear Vertex Colors");
            FillWhite(vertexPaintColors);
            ApplyVertexPaintColorsToPreview(vertexPaintSlotStart, vertexPaintColors);
            SyncVertexPaintAdjustments();
            vertexPaintDirtySlots.Add(GetVertexPaintSlotKey());
            vertexPaintDirty = true;
            RepaintLinkedEditors();
            SceneView.RepaintAll();
        }

        private void SaveVertexPaintToSlotDataAsset()
        {
            EnsureVertexPaintSession();
            if (vertexPaintSlot == null || vertexPaintSlot.asset == null || vertexPaintColors == null ||
                UMAMeshData.IsNullOrEmptyMeshData(vertexPaintSlot.asset.meshData))
            {
                EditorUtility.DisplayDialog("Save Vertex Colors", "The selected slot does not have editable mesh data.", "OK");
                return;
            }

            SlotDataAsset target = vertexPaintSlot.asset;
            if (target.meshData.vertexCount != vertexPaintColors.Length)
            {
                EditorUtility.DisplayDialog("Save Vertex Colors", "The selected slot topology no longer matches the vertex paint preview.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                "Save Vertex Colors to SlotDataAsset",
                $"Overwrite the vertex colors on '{target.name}'? Vertex positions and all other mesh data will be left unchanged. " +
                "The baked color adjustments for this slot will be removed from the current MeshModifier editing session so they are not applied twice.",
                "Save Colors",
                "Cancel"))
            {
                return;
            }

            List<UnityEngine.Object> undoTargets = new List<UnityEngine.Object> { target, this, BakedMesh };
            if (modifierEditor != null)
            {
                undoTargets.Add(modifierEditor);
            }
            Undo.RegisterCompleteObjectUndo(undoTargets.ToArray(), "Save Vertex Colors to SlotDataAsset");
            target.meshData.colors32 = (Color32[])vertexPaintColors.Clone();
            RemoveBakedVertexColorAdjustmentsForSlot();
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
            vertexPaintBaseColors = (Color32[])vertexPaintColors.Clone();
            ApplyVertexPaintColorsToPreview(vertexPaintSlotStart, vertexPaintColors);
            vertexPaintDirtySlots.Remove(GetVertexPaintSlotKey());
            vertexPaintDirty = vertexPaintDirtySlots.Count > 0;
            RepaintLinkedEditors();
            SceneView.RepaintAll();
            Selection.activeObject = target;
        }

        private void RemoveBakedVertexColorAdjustmentsForSlot()
        {
            string slotKey = GetVertexPaintSlotKey();
            for (int i = Adjustments.Count - 1; i >= 0; i--)
            {
                VertexColorAdjustment adjustment = Adjustments[i] as VertexColorAdjustment;
                if (adjustment != null && VertexPaintAdjustmentMatchesSlot(adjustment, null, vertexPaintSlot, slotKey))
                {
                    Adjustments.RemoveAt(i);
                }
            }

            if (modifierEditor == null || modifierEditor.Modifiers == null)
            {
                return;
            }

            for (int modifierIndex = modifierEditor.Modifiers.Count - 1; modifierIndex >= 0; modifierIndex--)
            {
                MeshModifier.Modifier modifier = modifierEditor.Modifiers[modifierIndex];
                if (modifier == null || !(modifier.adjustments is VertexColorAdjustmentCollection) ||
                    modifier.adjustments.vertexAdjustments == null)
                {
                    continue;
                }

                for (int adjustmentIndex = modifier.adjustments.vertexAdjustments.Count - 1; adjustmentIndex >= 0; adjustmentIndex--)
                {
                    VertexColorAdjustment adjustment = modifier.adjustments.vertexAdjustments[adjustmentIndex] as VertexColorAdjustment;
                    if (adjustment != null && VertexPaintAdjustmentMatchesSlot(adjustment, modifier.SlotName, vertexPaintSlot, slotKey))
                    {
                        modifier.adjustments.vertexAdjustments.RemoveAt(adjustmentIndex);
                    }
                }

                if (modifier.adjustments.vertexAdjustments.Count == 0)
                {
                    modifierEditor.Modifiers.RemoveAt(modifierIndex);
                }
            }
            EditorUtility.SetDirty(modifierEditor);
        }

        internal void PrepareVertexPaintAdjustmentsForSave()
        {
            EndVertexPaintStroke(true);
            if (vertexPaintPreviewInitialized)
            {
                SyncVertexPaintAdjustments();
            }
        }

        internal void MarkVertexPaintSaved()
        {
            vertexPaintDirtySlots.Clear();
            vertexPaintDirty = false;
        }

        internal void AppendPendingSculptModifier(List<MeshModifier.Modifier> snapshot)
        {
            if (snapshot == null)
            {
                return;
            }
            List<SculptSlotEditState> changedStates = GetChangedSculptStates();
            bool useSlotSuffix = changedStates.Count > 1 || IsSculptMultiSlotMode;
            for (int i = 0; i < changedStates.Count; i++)
            {
                MeshModifier.Modifier sculptModifier = CreateSculptModifierStack(changedStates[i], useSlotSuffix);
                if (sculptModifier != null)
                {
                    snapshot.Add(sculptModifier);
                }
            }
        }

        private MeshModifier.Modifier CreateSculptModifierStack(SculptSlotEditState state, bool useSlotSuffix)
        {
            if (!HasSculptChanges(state) || state.slot == null)
            {
                return null;
            }

            string slotKey = GetSculptSlotKey(state.slot);
            string baseName = string.IsNullOrWhiteSpace(sculptModifierName) ? "Sculpt" : sculptModifierName;
            MeshModifier.Modifier modifier = new MeshModifier.Modifier
            {
                ModifierName = useSlotSuffix ? baseName + " - " + state.slot.slotName : baseName,
                SlotName = slotKey,
                Scale = 1f,
                DNAName = string.Empty,
                manuallyModified = true,
                adjustments = new VertexDeltaAdjustmentCollection()
            };
            Vector3[] current = BakedMesh.vertices;
            for (int i = 0; i < state.count; i++)
            {
                Vector3 delta = current[state.start + i] - state.originalVertices[i];
                if (delta.sqrMagnitude <= 0.000000000001f)
                {
                    continue;
                }
                modifier.adjustments.vertexAdjustments.Add(new VertexDeltaAdjustment
                {
                    vertexIndex = i,
                    slotName = slotKey,
                    weight = 1f,
                    delta = delta
                });
            }
            return modifier.adjustments.vertexAdjustments.Count > 0 ? modifier : null;
        }

        private void FrameSelectedVertexPaintSlot()
        {
            if (vertexPaintSlot == null || BakedMesh == null || VertexObject == null ||
                vertexPaintSlotStart < 0 || vertexPaintSlotVertexCount <= 0)
            {
                return;
            }

            Vector3[] vertices = BakedMesh.vertices;
            Vector3 first = VertexObject.transform.TransformPoint(vertices[vertexPaintSlotStart]);
            Bounds bounds = new Bounds(first, Vector3.zero);
            for (int i = 1; i < vertexPaintSlotVertexCount; i++)
            {
                bounds.Encapsulate(VertexObject.transform.TransformPoint(vertices[vertexPaintSlotStart + i]));
            }
            bounds.Expand(Mathf.Max(0.001f, bounds.size.magnitude * 0.01f));
            SceneView sceneView = openedSceneView != null ? openedSceneView : SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                sceneView.Frame(bounds, false);
                sceneView.Repaint();
            }
        }

        private void FrameAllEditableSlots()
        {
            if (VertexObject == null || BakedMesh == null || BakedMesh.vertexCount == 0)
            {
                return;
            }

            Vector3[] vertices = BakedMesh.vertices;
            Bounds bounds = new Bounds(VertexObject.transform.TransformPoint(vertices[0]), Vector3.zero);
            for (int i = 1; i < vertices.Length; i++)
            {
                bounds.Encapsulate(VertexObject.transform.TransformPoint(vertices[i]));
            }
            bounds.Expand(Mathf.Max(0.001f, bounds.size.magnitude * 0.01f));
            SceneView sceneView = openedSceneView != null ? openedSceneView : SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                sceneView.Frame(bounds, false);
                sceneView.Repaint();
            }
        }

        private void FrameAllNonBaseSculptSlots()
        {
            if (VertexObject == null || BakedMesh == null || BakedMesh.vertexCount == 0)
            {
                return;
            }

            Vector3[] vertices = BakedMesh.vertices;
            bool hasBounds = false;
            Bounds bounds = new Bounds();
            for (int slotIndex = 0; slotIndex < sculptSlots.Count; slotIndex++)
            {
                SlotData slot = sculptSlots[slotIndex];
                if (!IsSculptSlotIncludedInTarget(slot) || slot.asset == null)
                {
                    continue;
                }

                int start = GetVisibleBakedVertexIndex(slot, 0);
                int count = slot.asset.meshData.vertexCount;
                if (start < 0 || start + count > vertices.Length)
                {
                    continue;
                }

                for (int vertexIndex = start; vertexIndex < start + count; vertexIndex++)
                {
                    Vector3 point = VertexObject.transform.TransformPoint(vertices[vertexIndex]);
                    if (!hasBounds)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            if (!hasBounds)
            {
                return;
            }

            bounds.Expand(Mathf.Max(0.001f, bounds.size.magnitude * 0.01f));
            SceneView sceneView = openedSceneView != null ? openedSceneView : SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                sceneView.Frame(bounds, false);
                sceneView.Repaint();
            }
        }

        private void RefreshSculptSlots(SlotData preferredSlot = null)
        {
            bool preserveMultiSlotTarget = IsSculptMultiSlotMode;
            if (preferredSlot == null) preferredSlot = sculptSlot;
            sculptSlots.Clear();
            sculptSlotNames.Clear();
            sculptSlotNames.Add("All Slots");
            sculptSlotNames.Add("All non-base slots");
            if (thisDCA != null && thisDCA.umaData != null && thisDCA.umaData.umaRecipe != null)
            {
                foreach (SlotData slot in thisDCA.umaData.umaRecipe.slotDataList)
                {
                    if (IsSelectableSlot(slot))
                    {
                        sculptSlots.Add(slot);
                        sculptSlotNames.Add(slot.slotName);
                    }
                }
            }
            sculptSlotIndex = Mathf.Clamp(sculptSlotIndex, 0, sculptSlots.Count + SculptSlotSpecialOptionCount - 1);
            if (preserveMultiSlotTarget)
            {
                sculptSlotIndex = IsSculptAllSlotsMode ? 0 : 1;
            }
            else if (preferredSlot != null)
            {
                int found = sculptSlots.FindIndex(slot => ReferenceEquals(slot, preferredSlot));
                if (found < 0) found = sculptSlots.FindIndex(slot => slot != null && slot.slotName == preferredSlot.slotName);
                if (found >= 0) sculptSlotIndex = found + SculptSlotSpecialOptionCount;
            }
        }

        private void EnsureSculptSession(bool force = false)
        {
            SlotData preferredSlot = sculptSlotIndex >= SculptSlotSpecialOptionCount &&
                                     sculptSlotIndex < sculptSlots.Count + SculptSlotSpecialOptionCount
                ? sculptSlots[sculptSlotIndex - SculptSlotSpecialOptionCount]
                : sculptSlot;
            RefreshSculptSlots(preferredSlot);
            if (!sculptDefaultSlotChosen)
            {
                sculptSlotIndex = FindDefaultSculptSlotIndex() + SculptSlotSpecialOptionCount;
                sculptDefaultSlotChosen = true;
            }
            SlotData requested = null;
            if (sculptSlots.Count > 0)
            {
                requested = IsSculptMultiSlotMode
                    ? (sculptSlots.Contains(sculptSlot) && IsSculptSlotIncludedInTarget(sculptSlot)
                        ? sculptSlot
                        : FindFirstSculptTargetSlot())
                    : sculptSlots[Mathf.Clamp(sculptSlotIndex - SculptSlotSpecialOptionCount, 0, sculptSlots.Count - 1)];
            }
            ActivateSculptSlot(requested, force);
            if (IsSculptMultiSlotMode)
            {
                EnsureCrossSlotSculptSeams();
            }
            else
            {
                ClearCrossSlotSculptSeams();
            }
        }

        private SculptSlotEditState GetOrCreateSculptSlotState(SlotData slot)
        {
            if (slot == null || slot.asset == null || BakedMesh == null ||
                UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
            {
                return null;
            }

            int count = slot.asset.meshData.vertexCount;
            int start = GetVisibleBakedVertexIndex(slot, 0);
            if (start < 0 || start + count > BakedMesh.vertexCount)
            {
                return null;
            }

            if (sculptSlotStates.TryGetValue(slot, out SculptSlotEditState state) && state.count == count)
            {
                state.start = start;
                return state;
            }

            state = new SculptSlotEditState
            {
                slot = slot,
                start = start,
                count = count,
                originalVertices = new Vector3[count],
                originalNormals = new Vector3[count],
                mask = new float[count]
            };
            Vector3[] allVertices = BakedMesh.vertices;
            Array.Copy(allVertices, start, state.originalVertices, 0, count);
            Vector3[] allNormals = BakedMesh.normals;
            if (allNormals != null && start + count <= allNormals.Length)
            {
                Array.Copy(allNormals, start, state.originalNormals, 0, count);
                state.hasOriginalNormals = true;
            }
            sculptSlotStates[slot] = state;
            return state;
        }

        private void ActivateSculptSlot(SlotData requested, bool force = false)
        {
            if (!force && ReferenceEquals(requested, sculptSlot) && sculptOriginalVertices != null &&
                requested != null && sculptSlotVertexCount == requested.asset.meshData.vertexCount)
            {
                return;
            }
            EndSculptStroke(false);
            if (activeSculptState != null && sculptMask != null)
            {
                activeSculptState.mask = sculptMask;
            }
            sculptSlot = requested;
            activeSculptState = null;
            sculptSlotStart = -1;
            sculptSlotVertexCount = 0;
            sculptOriginalVertices = null;
            sculptOriginalNormals = null;
            sculptNeighbors = null;
            sculptBoundaryVertices = null;
            sculptCoincidentVertices = null;
            sculptConnectedComponents = null;
            sculptHoverConnectedComponent = -1;
            sculptMask = null;
            if (sculptSlot == null || sculptSlot.asset == null || BakedMesh == null ||
                UMAMeshData.IsNullOrEmptyMeshData(sculptSlot.asset.meshData)) return;
            sculptSlotVertexCount = sculptSlot.asset.meshData.vertexCount;
            sculptSlotStart = GetVisibleBakedVertexIndex(sculptSlot, 0);
            SculptSlotEditState state = GetOrCreateSculptSlotState(sculptSlot);
            if (state == null) return;

            activeSculptState = state;
            sculptOriginalVertices = state.originalVertices;
            sculptOriginalNormals = state.originalNormals;
            sculptMask = state.mask ?? (state.mask = new float[sculptSlotVertexCount]);
            sculptStrokeApplied = new float[sculptSlotVertexCount];
            sculptStrokeLimit = new float[sculptSlotVertexCount];
            sculptNeighbors = new List<int>[sculptSlotVertexCount];
            for (int i = 0; i < sculptSlotVertexCount; i++) sculptNeighbors[i] = new List<int>(6);
            int[] tris = BakedMesh.triangles;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = tris[i] - sculptSlotStart, b = tris[i + 1] - sculptSlotStart, c = tris[i + 2] - sculptSlotStart;
                if (a < 0 || b < 0 || c < 0 || a >= sculptSlotVertexCount || b >= sculptSlotVertexCount || c >= sculptSlotVertexCount) continue;
                AddSculptEdge(a, b); AddSculptEdge(b, c); AddSculptEdge(c, a);
            }
            sculptBoundaryVertices = GetSculptSlotBoundaryVertices(sculptSlotStart, sculptSlotVertexCount);
            BuildSculptCoincidentVertexMap();
            BuildSculptConnectedComponents();
            if (string.IsNullOrEmpty(sculptModifierName))
                sculptModifierName = IsSculptAllSlotsMode
                    ? "All Slots Sculpt"
                    : IsSculptAllNonBaseSlotsMode
                        ? "All Non-Base Slots Sculpt"
                        : GetSculptSlotKey() + " Sculpt";
            if (string.IsNullOrEmpty(sculptBlendshapeName))
                sculptBlendshapeName = sculptModifierName;
            if (string.IsNullOrEmpty(sculptNewSlotName)) sculptNewSlotName = sculptSlot.slotName + "_modified";
        }

        private void AddSculptEdge(int a, int b)
        {
            if (!sculptNeighbors[a].Contains(b)) sculptNeighbors[a].Add(b);
            if (!sculptNeighbors[b].Contains(a)) sculptNeighbors[b].Add(a);
        }

        private void ClearCrossSlotSculptSeams()
        {
            sculptCrossSlotSeams.Clear();
            sculptCrossSlotSeamGroups.Clear();
            sculptCrossSlotSeamsBuilt = false;
        }

        private void EnsureCrossSlotSculptSeams()
        {
            if (sculptCrossSlotSeamsBuilt)
            {
                return;
            }

            sculptCrossSlotSeams.Clear();
            sculptCrossSlotSeamGroups.Clear();
            sculptCrossSlotSeamsBuilt = true;
            if (!IsSculptMultiSlotMode || BakedMesh == null || sculptSlots.Count < 2)
            {
                return;
            }

            Vector3[] vertices = BakedMesh.vertices;
            float tolerance = Mathf.Max(0.00001f, BakedMesh.bounds.size.magnitude * 0.00001f);
            float inverseTolerance = 1f / tolerance;
            Dictionary<Vector3Int, List<SculptSeamVertex>> candidates = new Dictionary<Vector3Int, List<SculptSeamVertex>>();

            for (int slotIndex = 0; slotIndex < sculptSlots.Count; slotIndex++)
            {
                SlotData slot = sculptSlots[slotIndex];
                if (!IsSculptSlotIncludedInTarget(slot) || slot == null || slot.asset == null ||
                    UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                {
                    continue;
                }

                int count = slot.asset.meshData.vertexCount;
                int start = GetVisibleBakedVertexIndex(slot, 0);
                if (start < 0 || start + count > vertices.Length)
                {
                    continue;
                }

                HashSet<int> boundaryVertices = GetSculptSlotBoundaryVertices(start, count);
                sculptSlotStates.TryGetValue(slot, out SculptSlotEditState existingState);
                foreach (int localIndex in boundaryVertices)
                {
                    int bakedIndex = start + localIndex;
                    Vector3 referencePosition = existingState != null && existingState.originalVertices != null &&
                                                localIndex < existingState.originalVertices.Length
                        ? existingState.originalVertices[localIndex]
                        : vertices[bakedIndex];
                    Vector3Int key = new Vector3Int(
                        Mathf.RoundToInt(referencePosition.x * inverseTolerance),
                        Mathf.RoundToInt(referencePosition.y * inverseTolerance),
                        Mathf.RoundToInt(referencePosition.z * inverseTolerance));
                    if (!candidates.TryGetValue(key, out List<SculptSeamVertex> group))
                    {
                        group = new List<SculptSeamVertex>();
                        candidates.Add(key, group);
                    }
                    group.Add(new SculptSeamVertex
                    {
                        slot = slot,
                        localIndex = localIndex,
                        bakedIndex = bakedIndex
                    });
                }
            }

            foreach (List<SculptSeamVertex> candidateGroup in candidates.Values)
            {
                HashSet<SlotData> connectedSlots = new HashSet<SlotData>();
                for (int i = 0; i < candidateGroup.Count; i++)
                {
                    connectedSlots.Add(candidateGroup[i].slot);
                }
                if (connectedSlots.Count < 2)
                {
                    continue;
                }

                List<SculptSeamVertex> seamGroup = new List<SculptSeamVertex>(candidateGroup);
                sculptCrossSlotSeamGroups.Add(seamGroup);
                for (int i = 0; i < seamGroup.Count; i++)
                {
                    sculptCrossSlotSeams[seamGroup[i].bakedIndex] = seamGroup;
                }
            }
        }

        private HashSet<int> GetSculptSlotBoundaryVertices(int start, int count)
        {
            Dictionary<Vector2Int, int> edgeUseCounts = new Dictionary<Vector2Int, int>();
            int[] triangles = BakedMesh.triangles;
            for (int triangleIndex = 0; triangleIndex + 2 < triangles.Length; triangleIndex += 3)
            {
                int a = triangles[triangleIndex] - start;
                int b = triangles[triangleIndex + 1] - start;
                int c = triangles[triangleIndex + 2] - start;
                if (a < 0 || b < 0 || c < 0 || a >= count || b >= count || c >= count)
                {
                    continue;
                }
                CountSculptSlotEdge(edgeUseCounts, a, b);
                CountSculptSlotEdge(edgeUseCounts, b, c);
                CountSculptSlotEdge(edgeUseCounts, c, a);
            }

            HashSet<int> boundaryVertices = new HashSet<int>();
            foreach (KeyValuePair<Vector2Int, int> edge in edgeUseCounts)
            {
                if (edge.Value == 1)
                {
                    boundaryVertices.Add(edge.Key.x);
                    boundaryVertices.Add(edge.Key.y);
                }
            }
            return boundaryVertices;
        }

        private static void CountSculptSlotEdge(Dictionary<Vector2Int, int> edgeUseCounts, int a, int b)
        {
            Vector2Int edge = a < b ? new Vector2Int(a, b) : new Vector2Int(b, a);
            edgeUseCounts.TryGetValue(edge, out int count);
            edgeUseCounts[edge] = count + 1;
        }

        private void SynchronizeCrossSlotSculptPosition(List<int> localIndices, Vector3 targetPosition, Vector3[] vertices)
        {
            if (!IsSculptMultiSlotMode || !sculptCrossSlotSeamsBuilt || localIndices == null)
            {
                return;
            }

            HashSet<int> updatedVertices = new HashSet<int>();
            for (int i = 0; i < localIndices.Count; i++)
            {
                int bakedIndex = sculptSlotStart + localIndices[i];
                if (!sculptCrossSlotSeams.TryGetValue(bakedIndex, out List<SculptSeamVertex> seamGroup))
                {
                    continue;
                }
                for (int seamIndex = 0; seamIndex < seamGroup.Count; seamIndex++)
                {
                    SculptSeamVertex seamVertex = seamGroup[seamIndex];
                    if (!updatedVertices.Add(seamVertex.bakedIndex))
                    {
                        continue;
                    }
                    if (GetOrCreateSculptSlotState(seamVertex.slot) == null)
                    {
                        continue;
                    }
                    vertices[seamVertex.bakedIndex] = targetPosition;
                }
            }
        }

        private void SynchronizeCrossSlotSculptMask(List<int> localIndices, float targetMask)
        {
            if (!IsSculptMultiSlotMode || !sculptCrossSlotSeamsBuilt || localIndices == null)
            {
                return;
            }

            HashSet<int> updatedVertices = new HashSet<int>();
            for (int i = 0; i < localIndices.Count; i++)
            {
                int bakedIndex = sculptSlotStart + localIndices[i];
                if (!sculptCrossSlotSeams.TryGetValue(bakedIndex, out List<SculptSeamVertex> seamGroup))
                {
                    continue;
                }
                for (int seamIndex = 0; seamIndex < seamGroup.Count; seamIndex++)
                {
                    SculptSeamVertex seamVertex = seamGroup[seamIndex];
                    if (!updatedVertices.Add(seamVertex.bakedIndex))
                    {
                        continue;
                    }
                    SculptSlotEditState state = GetOrCreateSculptSlotState(seamVertex.slot);
                    if (state != null && state.mask != null && seamVertex.localIndex < state.mask.Length)
                    {
                        state.mask[seamVertex.localIndex] = targetMask;
                    }
                }
            }
        }

        private void SynchronizeCrossSlotSculptNormals()
        {
            if (!IsSculptMultiSlotMode || sculptCrossSlotSeamGroups.Count == 0 || BakedMesh == null)
            {
                return;
            }

            Vector3[] normals = BakedMesh.normals;
            if (normals == null || normals.Length != BakedMesh.vertexCount)
            {
                return;
            }

            for (int groupIndex = 0; groupIndex < sculptCrossSlotSeamGroups.Count; groupIndex++)
            {
                List<SculptSeamVertex> seamGroup = sculptCrossSlotSeamGroups[groupIndex];
                Vector3 average = Vector3.zero;
                for (int i = 0; i < seamGroup.Count; i++)
                {
                    average += normals[seamGroup[i].bakedIndex];
                }
                if (average.sqrMagnitude <= 0.000000000001f)
                {
                    continue;
                }
                average.Normalize();
                for (int i = 0; i < seamGroup.Count; i++)
                {
                    normals[seamGroup[i].bakedIndex] = average;
                }
            }
            BakedMesh.normals = normals;
        }

        private int FindDefaultSculptSlotIndex()
        {
            if (sculptSlots.Count == 0) return 0;
            for (int i = 0; i < sculptSlots.Count; i++)
            {
                SlotData slot = sculptSlots[i];
                if (!IsBaseSculptSlot(slot)) return i;
            }
            return 0;
        }

        private SlotData FindFirstSculptTargetSlot()
        {
            for (int i = 0; i < sculptSlots.Count; i++)
            {
                SlotData slot = sculptSlots[i];
                if (IsSculptSlotIncludedInTarget(slot)) return slot;
            }
            return null;
        }

        private bool IsSculptSlotIncludedInTarget(SlotData slot)
        {
            return slot != null && (!IsSculptAllNonBaseSlotsMode || !IsBaseSculptSlot(slot));
        }

        private bool IsBaseSculptSlot(SlotData slot)
        {
            RaceData race = thisDCA != null && thisDCA.activeRace != null ? thisDCA.activeRace.data : null;
            return slot != null && RaceBaseRecipeContainsSlot(race, slot.asset);
        }

        private void BuildSculptCoincidentVertexMap()
        {
            sculptCoincidentVertices = new List<int>[sculptSlotVertexCount];
            if (sculptOriginalVertices == null) return;

            // Quantize at a very small scale-relative tolerance. Vertices duplicated for
            // UV islands, hard normals, or material boundaries then share one sculpt state.
            float tolerance = Mathf.Max(0.000001f, BakedMesh.bounds.size.magnitude * 0.000001f);
            float inverseTolerance = 1f / tolerance;
            Dictionary<Vector3Int, List<int>> groups = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < sculptOriginalVertices.Length; i++)
            {
                Vector3 position = sculptOriginalVertices[i];
                Vector3Int key = new Vector3Int(
                    Mathf.RoundToInt(position.x * inverseTolerance),
                    Mathf.RoundToInt(position.y * inverseTolerance),
                    Mathf.RoundToInt(position.z * inverseTolerance));
                if (!groups.TryGetValue(key, out List<int> group))
                {
                    group = new List<int>();
                    groups.Add(key, group);
                }
                group.Add(i);
            }
            foreach (List<int> group in groups.Values)
                for (int i = 0; i < group.Count; i++) sculptCoincidentVertices[group[i]] = group;
        }

        private void BuildSculptConnectedComponents()
        {
            sculptConnectedComponents = new int[sculptSlotVertexCount];
            for (int i = 0; i < sculptConnectedComponents.Length; i++)
                sculptConnectedComponents[i] = -1;

            Queue<int> pending = new Queue<int>();
            int component = 0;
            for (int start = 0; start < sculptSlotVertexCount; start++)
            {
                if (sculptConnectedComponents[start] >= 0) continue;
                sculptConnectedComponents[start] = component;
                pending.Enqueue(start);
                while (pending.Count > 0)
                {
                    int vertex = pending.Dequeue();
                    List<int> neighbors = sculptNeighbors != null ? sculptNeighbors[vertex] : null;
                    if (neighbors != null)
                    {
                        for (int i = 0; i < neighbors.Count; i++)
                            AddSculptConnectedVertex(neighbors[i], component, pending);
                    }

                    List<int> coincident = sculptCoincidentVertices != null ? sculptCoincidentVertices[vertex] : null;
                    if (coincident != null)
                    {
                        for (int i = 0; i < coincident.Count; i++)
                            AddSculptConnectedVertex(coincident[i], component, pending);
                    }
                }
                component++;
            }
        }

        private void AddSculptConnectedVertex(int vertex, int component, Queue<int> pending)
        {
            if (vertex < 0 || vertex >= sculptConnectedComponents.Length || sculptConnectedComponents[vertex] >= 0)
                return;
            sculptConnectedComponents[vertex] = component;
            pending.Enqueue(vertex);
        }

        private string GetSculptSlotKey()
        {
            return GetSculptSlotKey(sculptSlot);
        }

        private static string GetSculptSlotKey(SlotData slot)
        {
            if (slot == null) return string.Empty;
            return slot.asset != null && !string.IsNullOrEmpty(slot.asset.sourceSlot) ? slot.asset.sourceSlot : slot.slotName;
        }

        private int FindSculptConnectedComponentAtPoint(Vector3 localPoint)
        {
            if (BakedMesh == null || sculptConnectedComponents == null || sculptSlotStart < 0)
                return -1;

            Vector3[] vertices = BakedMesh.vertices;
            int[] triangles = BakedMesh.triangles;
            float closestDistance = float.PositiveInfinity;
            int closestComponent = -1;
            int slotEnd = sculptSlotStart + sculptSlotVertexCount;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int bakedA = triangles[i];
                int bakedB = triangles[i + 1];
                int bakedC = triangles[i + 2];
                if (bakedA < sculptSlotStart || bakedA >= slotEnd ||
                    bakedB < sculptSlotStart || bakedB >= slotEnd ||
                    bakedC < sculptSlotStart || bakedC >= slotEnd)
                {
                    continue;
                }

                Vector3 closestPoint = ClosestPointOnTriangle(
                    localPoint,
                    vertices[bakedA],
                    vertices[bakedB],
                    vertices[bakedC]);
                float distance = (closestPoint - localPoint).sqrMagnitude;
                if (distance >= closestDistance) continue;
                closestDistance = distance;
                closestComponent = sculptConnectedComponents[bakedA - sculptSlotStart];
            }
            return closestComponent;
        }

        private static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            if (Vector3.Cross(ab, ac).sqrMagnitude <= 0.000000000001f)
                return ClosestPointOnTriangleEdges(point, a, b, c);
            Vector3 ap = point - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + w * (c - b);
            }

            float denominator = va + vb + vc;
            if (Mathf.Abs(denominator) <= 0.000000000001f)
                return ClosestPointOnTriangleEdges(point, a, b, c);

            float inverseDenominator = 1f / denominator;
            float barycentricV = vb * inverseDenominator;
            float barycentricW = vc * inverseDenominator;
            return a + ab * barycentricV + ac * barycentricW;
        }

        private static Vector3 ClosestPointOnTriangleEdges(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 closestAB = ClosestPointOnSegment(point, a, b);
            Vector3 closestAC = ClosestPointOnSegment(point, a, c);
            Vector3 closestBC = ClosestPointOnSegment(point, b, c);
            float distanceAB = (point - closestAB).sqrMagnitude;
            float distanceAC = (point - closestAC).sqrMagnitude;
            float distanceBC = (point - closestBC).sqrMagnitude;
            return distanceAB <= distanceAC && distanceAB <= distanceBC
                ? closestAB
                : distanceAC <= distanceBC ? closestAC : closestBC;
        }

        private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 segment = b - a;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.000000000001f) return a;
            return a + segment * Mathf.Clamp01(Vector3.Dot(point - a, segment) / lengthSquared);
        }

        private float EvaluateSculptFalloff(float normalizedDistance)
        {
            float x = Mathf.Clamp01(normalizedDistance);
            switch (sculptFalloff)
            {
                case SculptFalloff.Constant: return 1f;
                case SculptFalloff.Linear: return 1f - x;
                case SculptFalloff.EaseIn: return 1f - x * x;
                case SculptFalloff.EaseOut: { float y = 1f - x; return y * y; }
                case SculptFalloff.EaseInOut: return Mathf.SmoothStep(1f, 0f, x);
                case SculptFalloff.Sharp: return Mathf.Pow(1f - x, 4f);
                case SculptFalloff.UserDefined: return Mathf.Clamp01(sculptCustomFalloff.Evaluate(x));
                default: { float y = 1f - x; return y * y * (3f - 2f * y); }
            }
        }

        private SlotData GetAutosculptOccluderSlot()
        {
            if (string.IsNullOrEmpty(autosculptOccluderSlotName)) return null;
            for (int i = 0; i < sculptSlots.Count; i++)
            {
                SlotData slot = sculptSlots[i];
                if (slot != null && string.Equals(slot.slotName, autosculptOccluderSlotName, StringComparison.Ordinal))
                    return slot;
            }
            return null;
        }

        private void DrawAutosculptOptions()
        {
            EditorGUILayout.Space(6f);
            showAutosculpt = EditorGUILayout.Foldout(showAutosculpt, "Autosculpt", true);
            if (!showAutosculpt) return;

            GUIHelper.BeginVerticalPadded(5, new Color(0.9f, 0.94f, 1f), EditorStyles.helpBox);
            EditorGUILayout.HelpBox(
                "Choose the slot that should remain unchanged. Target vertices cast outward along the selected local axis toward that occluder, then move inward using the current Radius, Effect %, Falloff, mask, and X Symmetry settings.",
                MessageType.Info);

            List<string> occluderNames = new List<string>(sculptSlots.Count + 1) { "Select Occluder..." };
            int selectedOccluder = 0;
            for (int i = 0; i < sculptSlots.Count; i++)
            {
                SlotData slot = sculptSlots[i];
                if (slot == null) continue;
                occluderNames.Add(slot.slotName);
                if (string.Equals(slot.slotName, autosculptOccluderSlotName, StringComparison.Ordinal))
                    selectedOccluder = occluderNames.Count - 1;
            }

            int requestedOccluder = EditorGUILayout.Popup("Source / Occluder", selectedOccluder, occluderNames.ToArray());
            if (requestedOccluder != selectedOccluder)
            {
                autosculptOccluderSlotName = requestedOccluder > 0
                    ? occluderNames[requestedOccluder]
                    : string.Empty;
                autosculptStatusMessage = string.Empty;
            }
            else if (selectedOccluder == 0 && !string.IsNullOrEmpty(autosculptOccluderSlotName))
            {
                autosculptOccluderSlotName = string.Empty;
            }

            autosculptAxis = (AutosculptAxis)EditorGUILayout.EnumPopup(
                new GUIContent("Axis", "Local model axis used to cast toward the occluder and pull target vertices toward the model center."),
                autosculptAxis);
            autosculptClothify = EditorGUILayout.Toggle(
                new GUIContent("Clothify", "Vary the inward displacement with deterministic surface noise to create small cloth-like wrinkles."),
                autosculptClothify);
            EditorGUI.BeginDisabledGroup(!autosculptClothify);
            autosculptClothEffect = EditorGUILayout.Slider(
                new GUIContent("Cloth Effect", "Wrinkle intensity applied only to vertices moved by Autosculpt."),
                autosculptClothEffect,
                0f,
                1f);
            EditorGUI.EndDisabledGroup();

            SlotData occluder = GetAutosculptOccluderSlot();
            bool currentIsOccluder = occluder != null && sculptSlot != null &&
                string.Equals(occluder.slotName, sculptSlot.slotName, StringComparison.Ordinal);
            EditorGUI.BeginDisabledGroup(occluder == null || sculptSlot == null || currentIsOccluder);
            if (GUILayout.Button(new GUIContent(
                    "Autosculpt Current Slot",
                    "Autosculpt only the current slot, leaving the selected occluder unchanged.")))
            {
                ApplyAutosculpt(false);
            }
            EditorGUI.EndDisabledGroup();

            bool hasAllSlotTarget = false;
            if (occluder != null)
            {
                for (int i = 0; i < sculptSlots.Count; i++)
                {
                    if (sculptSlots[i] != null &&
                        !string.Equals(sculptSlots[i].slotName, occluder.slotName, StringComparison.Ordinal))
                    {
                        hasAllSlotTarget = true;
                        break;
                    }
                }
            }
            EditorGUI.BeginDisabledGroup(occluder == null || !hasAllSlotTarget);
            if (GUILayout.Button(new GUIContent(
                    "Autosculpt All Slots",
                    "Autosculpt every visible editable slot except the selected occluder.")))
            {
                ApplyAutosculpt(true);
            }
            EditorGUI.EndDisabledGroup();

            if (currentIsOccluder)
            {
                EditorGUILayout.HelpBox(
                    "The current slot is the occluder, so use Autosculpt All Slots or select a different current slot.",
                    MessageType.None);
            }
            if (!string.IsNullOrEmpty(autosculptStatusMessage))
                EditorGUILayout.HelpBox(autosculptStatusMessage, autosculptStatusType);
            GUIHelper.EndVerticalPadded(5);
        }

        private void ApplyAutosculpt(bool allSlots)
        {
            EndSculptStroke(true);
            EnsureSculptSession();
            SlotData occluder = GetAutosculptOccluderSlot();
            if (occluder == null || BakedMesh == null || VertexObject == null)
            {
                autosculptStatusType = MessageType.Warning;
                autosculptStatusMessage = "Autosculpt skipped: select a valid visible occluder slot.";
                return;
            }

            List<SlotData> targets = new List<SlotData>();
            if (allSlots)
            {
                for (int i = 0; i < sculptSlots.Count; i++)
                {
                    SlotData candidate = sculptSlots[i];
                    if (candidate != null &&
                        !string.Equals(candidate.slotName, occluder.slotName, StringComparison.Ordinal))
                    {
                        targets.Add(candidate);
                    }
                }
            }
            else if (sculptSlot != null &&
                     !string.Equals(sculptSlot.slotName, occluder.slotName, StringComparison.Ordinal))
            {
                targets.Add(sculptSlot);
            }

            if (targets.Count == 0)
            {
                autosculptStatusType = MessageType.Warning;
                autosculptStatusMessage = "Autosculpt skipped: there are no valid target slots.";
                return;
            }

            RefreshBakedMeshCaches();
            AutosculptOccluderMap occluderMap = BuildAutosculptOccluderMap(occluder);
            if (occluderMap == null)
            {
                autosculptStatusType = MessageType.Warning;
                autosculptStatusMessage = "Autosculpt skipped: the selected occluder has no visible triangles.";
                return;
            }

            Vector3[] vertices = BakedMesh.vertices;
            Vector3[] before = (Vector3[])vertices.Clone();
            Vector3 modelCenter = BakedMesh.bounds.center;
            Vector3 axis = GetAutosculptAxisVector(autosculptAxis);
            float searchDistance = Mathf.Max(0.0001f, sculptRadius);
            float maximumPull = searchDistance * Mathf.Clamp01(sculptStrengthPercent * 0.01f);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Autosculpt Mesh");
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { this, BakedMesh }, "Autosculpt Mesh");

            int movedVertices = 0;
            int affectedSlots = 0;
            try
            {
                for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                {
                    SlotData target = targets[targetIndex];
                    SculptSlotEditState state = GetOrCreateSculptSlotState(target);
                    if (state == null) continue;
                    bool slotChanged = false;
                    EditorUtility.DisplayProgressBar(
                        "Autosculpt",
                        "Projecting " + target.slotName + " against " + occluder.slotName,
                        targetIndex / (float)Mathf.Max(1, targets.Count));

                    for (int localIndex = 0; localIndex < state.count; localIndex++)
                    {
                        int bakedIndex = state.start + localIndex;
                        if (bakedIndex < 0 || bakedIndex >= before.Length) continue;
                        Vector3 position = before[bakedIndex];
                        if (!TryGetAutosculptPull(
                                position,
                                modelCenter,
                                axis,
                                occluderMap,
                                searchDistance,
                                out Vector3 inwardDirection,
                                out float hitDistance))
                        {
                            continue;
                        }

                        float falloff = EvaluateSculptFalloff(hitDistance / searchDistance);
                        float mask = state.mask != null && localIndex < state.mask.Length ? state.mask[localIndex] : 0f;
                        float pull = maximumPull * falloff * (1f - mask);
                        if (autosculptClothify && autosculptClothEffect > 0f)
                        {
                            float wrinkle = EvaluateAutosculptNoise(position);
                            pull *= Mathf.Max(0f, 1f + wrinkle * Mathf.Clamp01(autosculptClothEffect) * 0.35f);
                        }
                        if (pull <= 0.0000001f) continue;

                        Vector3 newPosition = position + inwardDirection * pull;
                        if ((newPosition - vertices[bakedIndex]).sqrMagnitude <= 0.000000000001f) continue;
                        vertices[bakedIndex] = newPosition;
                        movedVertices++;
                        slotChanged = true;
                    }
                    if (slotChanged) affectedSlots++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (movedVertices > 0)
            {
                BakedMesh.vertices = vertices;
                BakedMesh.RecalculateNormals();
                BakedMesh.RecalculateBounds();
                RefreshBakedMeshCaches();
                RefreshSculptCollider();
                sculptHoverValid = false;
                EditorUtility.SetDirty(BakedMesh);
                EditorUtility.SetDirty(this);
                RepaintLinkedEditors();
                SceneView.RepaintAll();
                autosculptStatusType = MessageType.Info;
                autosculptStatusMessage =
                    $"Autosculpt moved {movedVertices} vertices across {affectedSlots} slot(s). Continue sculpting or save through the normal Sculpt workflow.";
            }
            else
            {
                autosculptStatusType = MessageType.Info;
                autosculptStatusMessage =
                    "Autosculpt found no target vertices beneath the occluder within the current Sculpt Radius.";
            }
            Undo.CollapseUndoOperations(undoGroup);
        }

        private AutosculptOccluderMap BuildAutosculptOccluderMap(SlotData occluder)
        {
            if (occluder == null || bakedVertices == null || bakedTriangles == null) return null;
            List<AutosculptTriangle> triangles = new List<AutosculptTriangle>();
            for (int triangleIndex = 0; triangleIndex + 2 < bakedTriangles.Length; triangleIndex += 3)
            {
                int bakedA = bakedTriangles[triangleIndex];
                int bakedB = bakedTriangles[triangleIndex + 1];
                int bakedC = bakedTriangles[triangleIndex + 2];
                if (bakedA < 0 || bakedB < 0 || bakedC < 0 ||
                    bakedA >= bakedVertices.Length || bakedB >= bakedVertices.Length || bakedC >= bakedVertices.Length)
                {
                    continue;
                }
                if (!TryGetSlotForBakedVertex(bakedA, out SlotData triangleSlot, out _) ||
                    triangleSlot == null ||
                    !string.Equals(triangleSlot.slotName, occluder.slotName, StringComparison.Ordinal))
                {
                    continue;
                }

                AutosculptTriangle triangle = new AutosculptTriangle
                {
                    a = bakedVertices[bakedA],
                    b = bakedVertices[bakedB],
                    c = bakedVertices[bakedC]
                };
                triangles.Add(triangle);
                if (sculptSymmetryX)
                {
                    triangles.Add(new AutosculptTriangle
                    {
                        a = ReflectLocalX(triangle.a),
                        b = ReflectLocalX(triangle.b),
                        c = ReflectLocalX(triangle.c)
                    });
                }
            }
            if (triangles.Count == 0) return null;

            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < triangles.Count; i++)
            {
                AutosculptTriangle triangle = triangles[i];
                minimum = Vector2.Min(minimum, ProjectAutosculptPoint(triangle.a, autosculptAxis));
                minimum = Vector2.Min(minimum, ProjectAutosculptPoint(triangle.b, autosculptAxis));
                minimum = Vector2.Min(minimum, ProjectAutosculptPoint(triangle.c, autosculptAxis));
                maximum = Vector2.Max(maximum, ProjectAutosculptPoint(triangle.a, autosculptAxis));
                maximum = Vector2.Max(maximum, ProjectAutosculptPoint(triangle.b, autosculptAxis));
                maximum = Vector2.Max(maximum, ProjectAutosculptPoint(triangle.c, autosculptAxis));
            }

            AutosculptOccluderMap map = new AutosculptOccluderMap
            {
                axis = autosculptAxis,
                origin = minimum,
                cellSize = Mathf.Max(0.0001f, Mathf.Max(maximum.x - minimum.x, maximum.y - minimum.y) / 64f)
            };
            for (int i = 0; i < triangles.Count; i++) AddAutosculptTriangleToMap(map, triangles[i]);
            return map;
        }

        private static void AddAutosculptTriangleToMap(AutosculptOccluderMap map, AutosculptTriangle triangle)
        {
            Vector2 a = ProjectAutosculptPoint(triangle.a, map.axis);
            Vector2 b = ProjectAutosculptPoint(triangle.b, map.axis);
            Vector2 c = ProjectAutosculptPoint(triangle.c, map.axis);
            Vector2 minimum = Vector2.Min(a, Vector2.Min(b, c));
            Vector2 maximum = Vector2.Max(a, Vector2.Max(b, c));
            Vector2Int minCell = GetAutosculptCell(map, minimum);
            Vector2Int maxCell = GetAutosculptCell(map, maximum);
            long cellCount = (long)(maxCell.x - minCell.x + 1) * (maxCell.y - minCell.y + 1);
            if (cellCount > 4096)
            {
                map.largeTriangles.Add(triangle);
                return;
            }

            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    Vector2Int key = new Vector2Int(x, y);
                    if (!map.cells.TryGetValue(key, out List<AutosculptTriangle> cell))
                    {
                        cell = new List<AutosculptTriangle>();
                        map.cells.Add(key, cell);
                    }
                    cell.Add(triangle);
                }
            }
        }

        private bool TryGetAutosculptPull(
            Vector3 position,
            Vector3 modelCenter,
            Vector3 axis,
            AutosculptOccluderMap map,
            float maxDistance,
            out Vector3 inwardDirection,
            out float hitDistance)
        {
            float side = Vector3.Dot(position - modelCenter, axis);
            if (Mathf.Abs(side) > 0.000001f)
            {
                Vector3 outward = side > 0f ? axis : -axis;
                if (TryGetAutosculptHitDistance(position, outward, map, maxDistance, out hitDistance))
                {
                    inwardDirection = -outward;
                    return true;
                }
                inwardDirection = Vector3.zero;
                return false;
            }

            bool positiveHit = TryGetAutosculptHitDistance(position, axis, map, maxDistance, out float positiveDistance);
            bool negativeHit = TryGetAutosculptHitDistance(position, -axis, map, maxDistance, out float negativeDistance);
            if (!positiveHit && !negativeHit)
            {
                inwardDirection = Vector3.zero;
                hitDistance = 0f;
                return false;
            }
            bool usePositive = positiveHit && (!negativeHit || positiveDistance <= negativeDistance);
            inwardDirection = usePositive ? -axis : axis;
            hitDistance = usePositive ? positiveDistance : negativeDistance;
            return true;
        }

        private static bool TryGetAutosculptHitDistance(
            Vector3 position,
            Vector3 outwardDirection,
            AutosculptOccluderMap map,
            float maxDistance,
            out float hitDistance)
        {
            float originOffset = Mathf.Min(0.0001f, maxDistance * 0.01f);
            Vector3 rayOrigin = position - outwardDirection * originOffset;
            float closest = maxDistance + originOffset;
            bool found = false;
            Vector2Int cellKey = GetAutosculptCell(map, ProjectAutosculptPoint(position, map.axis));
            if (map.cells.TryGetValue(cellKey, out List<AutosculptTriangle> cell))
                TestAutosculptTriangles(rayOrigin, outwardDirection, cell, ref closest, ref found);
            TestAutosculptTriangles(rayOrigin, outwardDirection, map.largeTriangles, ref closest, ref found);
            hitDistance = found ? Mathf.Max(0f, closest - originOffset) : 0f;
            return found && hitDistance <= maxDistance;
        }

        private static void TestAutosculptTriangles(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            List<AutosculptTriangle> triangles,
            ref float closest,
            ref bool found)
        {
            for (int i = 0; i < triangles.Count; i++)
            {
                AutosculptTriangle triangle = triangles[i];
                if (RayTriangleIntersect(rayOrigin, rayDirection, triangle.a, triangle.b, triangle.c, out float distance) &&
                    distance < closest)
                {
                    closest = distance;
                    found = true;
                }
            }
        }

        private float EvaluateAutosculptNoise(Vector3 position)
        {
            if (sculptSymmetryX) position.x = Mathf.Abs(position.x);
            float frequency = 1f / Mathf.Max(0.002f, sculptRadius * 0.35f);
            position *= frequency;
            float noise = (
                Mathf.PerlinNoise(position.x + 13.71f, position.y + 31.17f) +
                Mathf.PerlinNoise(position.y + 47.53f, position.z + 7.91f) +
                Mathf.PerlinNoise(position.z + 23.29f, position.x + 59.83f)) / 3f;
            return noise * 2f - 1f;
        }

        private static Vector3 GetAutosculptAxisVector(AutosculptAxis axis)
        {
            switch (axis)
            {
                case AutosculptAxis.X: return Vector3.right;
                case AutosculptAxis.Z: return Vector3.forward;
                default: return Vector3.up;
            }
        }

        private static Vector2 ProjectAutosculptPoint(Vector3 point, AutosculptAxis axis)
        {
            switch (axis)
            {
                case AutosculptAxis.X: return new Vector2(point.y, point.z);
                case AutosculptAxis.Z: return new Vector2(point.x, point.y);
                default: return new Vector2(point.x, point.z);
            }
        }

        private static Vector2Int GetAutosculptCell(AutosculptOccluderMap map, Vector2 point)
        {
            return new Vector2Int(
                Mathf.FloorToInt((point.x - map.origin.x) / map.cellSize),
                Mathf.FloorToInt((point.y - map.origin.y) / map.cellSize));
        }

        private void DrawSculptOptions()
        {
            EnsureSculptSession();
            GUILayout.Label("Sculpt", centeredLabel);
            if (sculptSlots.Count == 0)
            {
                EditorGUILayout.HelpBox("No visible editable slot is available.", MessageType.Warning);
                return;
            }
            int oldSlot = sculptSlotIndex;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Slot", GUILayout.Width(92));
            sculptSlotIndex = EditorGUILayout.Popup(sculptSlotIndex, sculptSlotNames.ToArray());
            bool frameSlot = GUILayout.Button(new GUIContent("◎", "Frame the selected sculpt slot in the Scene view."), EditorStyles.miniButton, GUILayout.Width(26));
            GUILayout.EndHorizontal();
            if (oldSlot != sculptSlotIndex)
            {
                EndSculptStroke(true);
                sculptNewSlotName = string.Empty;
                EnsureSculptSession(true);
            }
            if (frameSlot)
            {
                if (IsSculptAllSlotsMode) FrameAllEditableSlots();
                else if (IsSculptAllNonBaseSlotsMode) FrameAllNonBaseSculptSlots();
                else FrameSelectedSculptSlot();
            }
            if (IsSculptMultiSlotMode)
            {
                string targetDescription = IsSculptAllNonBaseSlotsMode
                    ? "All non-base slots is active. Each stroke targets the non-base slot under the pointer; base-race slots are ignored."
                    : "All Slots is active. Each stroke targets the slot under the pointer.";
                EditorGUILayout.HelpBox(
                    $"{targetDescription} Current target: {(sculptSlot != null ? sculptSlot.slotName : "None")}. Co-located boundary vertices are welded across targeted slots and every affected slot is retained.",
                    MessageType.None);
            }
            if (sculptSlot == null || sculptMask == null)
            {
                EditorGUILayout.HelpBox(
                    IsSculptAllNonBaseSlotsMode
                        ? "No visible editable non-base slot is available for this avatar. Select a specific slot or All Slots."
                        : "No visible editable slot is available.",
                    MessageType.Warning);
                return;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Tool", GUILayout.Width(92));
            GUIContent[] tools = {
                new GUIContent("Add", "Add: move vertices outward along the averaged surface normal."),
                new GUIContent("Remove", "Remove: move vertices inward along the averaged surface normal."),
                new GUIContent("Smooth", "Smooth: relax vertices toward their connected neighbors."),
                new GUIContent("Grab", "Grab: drag vertices parallel to the Scene camera's view plane."),
                new GUIContent("Crease", "Crease: pinch vertices toward the stroke while adding an adjustable ridge or indentation."),
                new GUIContent("Pinch", "Pinch: draw vertices together across the surface without adding depth."),
                new GUIContent("Plane", "Plane: flatten, fill, or scrape vertices toward the plane captured at stroke start."),
                new GUIContent("Boundary", "Boundary: deform the nearest open edge and propagate the effect into the garment."),
                new GUIContent("Elastic", "Elastic Deform: broad grab, scale, or twist deformation with smooth volume-aware falloff.") };
            sculptTool = (SculptTool)GUILayout.SelectionGrid((int)sculptTool, tools, 3, EditorStyles.miniButton);
            GUILayout.EndHorizontal();
            sculptRadius = EditorGUILayout.Slider(new GUIContent("Radius", "World-space brush radius."), sculptRadius, 0.001f, 0.5f);
            sculptStrengthPercent = EditorGUILayout.Slider(new GUIContent("Effect %", "Maximum effect a vertex can receive during one stroke. For Grab, this controls how closely the center follows the cursor."), sculptStrengthPercent, 0f, 100f);
            sculptFalloff = (SculptFalloff)EditorGUILayout.EnumPopup("Falloff", sculptFalloff);
            if (sculptFalloff == SculptFalloff.UserDefined) sculptCustomFalloff = EditorGUILayout.CurveField("Curve", sculptCustomFalloff, Color.green, new Rect(0, 0, 1, 1));
            if (sculptTool == SculptTool.Crease)
            {
                sculptPinchStrength = EditorGUILayout.Slider(new GUIContent("Pinch", "How strongly vertices converge toward the stroke."), sculptPinchStrength, 0f, 2f);
                sculptCreaseDepth = EditorGUILayout.Slider(new GUIContent("Depth", "Negative values cut a crease; positive values raise a ridge."), sculptCreaseDepth, -1f, 1f);
            }
            else if (sculptTool == SculptTool.Pinch)
            {
                sculptPinchStrength = EditorGUILayout.Slider(new GUIContent("Pinch", "How strongly vertices converge toward the stroke."), sculptPinchStrength, 0f, 2f);
            }
            else if (sculptTool == SculptTool.Plane)
            {
                sculptPlaneMode = (SculptPlaneMode)EditorGUILayout.EnumPopup("Plane Mode", sculptPlaneMode);
                EditorGUILayout.HelpBox(
                    "The first point of each stroke fixes the working plane. Flatten affects both sides, Fill raises only recessed vertices, and Scrape lowers only protruding vertices.",
                    MessageType.None);
            }
            else if (sculptTool == SculptTool.Boundary)
            {
                sculptBoundaryMode = (SculptBoundaryMode)EditorGUILayout.EnumPopup("Boundary Mode", sculptBoundaryMode);
                if (sculptBoundaryVertices == null || sculptBoundaryVertices.Count == 0)
                    EditorGUILayout.HelpBox("The selected slot has no open mesh boundary.", MessageType.Warning);
                else
                    EditorGUILayout.HelpBox("Start the drag near an open hem, cuff, collar, sleeve, or skirt edge. Radius controls how far the deformation propagates across the surface.", MessageType.None);
            }
            else if (sculptTool == SculptTool.ElasticDeform)
            {
                sculptElasticMode = (SculptElasticMode)EditorGUILayout.EnumPopup("Elastic Mode", sculptElasticMode);
                if (sculptElasticMode == SculptElasticMode.Grab)
                    sculptElasticVolumePreservation = EditorGUILayout.Slider(new GUIContent("Preserve Volume", "Adds a small perpendicular bulge through the transition area to reduce visible volume loss."), sculptElasticVolumePreservation, 0f, 1f);
                EditorGUILayout.HelpBox("Drag across the view plane. Scale and Twist use the drag direction along the surface tangent to determine their sign and amount.", MessageType.None);
            }
            sculptSymmetryX = EditorGUILayout.Toggle(
                new GUIContent(
                    "X Symmetry",
                    "Repeat every brush sample from the reflected position and surface direction across the character's local X axis."),
                sculptSymmetryX);
            sculptConnectedOnly = EditorGUILayout.Toggle(
                new GUIContent(
                    "Connected Only",
                    "Restrict the brush to the connected surface under its center. This prevents nearby disconnected layers in the same slot, such as the fabric behind a pocket, from being modified."),
                sculptConnectedOnly);
            sculptUpdateNormalsWhileSculpting = EditorGUILayout.Toggle(new GUIContent("Live Normal Updates", "Recalculate mesh normals after every brush sample. This improves live surface feedback but costs more CPU time."), sculptUpdateNormalsWhileSculpting);
            if (IsSculptDragMode)
            {
                EditorGUILayout.HelpBox(
                    "Press on the surface, then drag in the Scene view. The affected area and target slot are frozen until release; multi-slot targets keep connected slot boundaries welded.",
                    MessageType.None);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mask", GUILayout.Width(92));
            sculptMaskTool = (SculptMaskTool)GUILayout.Toolbar((int)sculptMaskTool, new[] { new GUIContent("Off", "Sculpt normally."), new GUIContent("Paint", "Protect vertices under the brush."), new GUIContent("Erase", "Remove protection under the brush.") });
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear Mask")) Array.Clear(sculptMask, 0, sculptMask.Length);
            if (GUILayout.Button("Invert Mask")) for (int i = 0; i < sculptMask.Length; i++) sculptMask[i] = 1f - sculptMask[i];
            GUILayout.EndHorizontal();
            DrawAutosculptOptions();
            sculptModifierName = EditorGUILayout.TextField("Modifier Name", sculptModifierName);
            EditorGUI.BeginDisabledGroup(!HasSculptChanges());
            if (GUILayout.Button("Save MeshModifier")) SaveSculptModifier();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(6f);
            GUILayout.Label("Save Sculpt as Blendshape", EditorStyles.miniBoldLabel);
            sculptBlendshapeName = EditorGUILayout.TextField("Blendshape Name", sculptBlendshapeName);
            EditorGUI.BeginDisabledGroup(!HasSculptChanges() || string.IsNullOrWhiteSpace(sculptBlendshapeName));
            if (GUILayout.Button(new GUIContent(
                    "Save Sculpt as Blendshape",
                    "Add or replace a 100%-weight blendshape on every SlotDataAsset changed by this sculpt session.")))
            {
                SaveSculptAsBlendshape();
            }
            EditorGUI.EndDisabledGroup();
            if (!string.IsNullOrEmpty(sculptBlendshapeStatusMessage))
                EditorGUILayout.HelpBox(sculptBlendshapeStatusMessage, sculptBlendshapeStatusType);

            EditorGUILayout.Space(6f);
            GUILayout.Label("Save Slot Mesh", EditorStyles.miniBoldLabel);
            EditorGUI.BeginDisabledGroup(IsSculptMultiSlotMode || !HasCurrentSculptChanges());
            if (GUILayout.Button(new GUIContent("Save slot modifications to base slot", "Overwrite the selected SlotDataAsset's MeshData with the sculpted vertex positions.")))
                SaveSculptToBaseSlot();
            sculptNewSlotName = EditorGUILayout.TextField("New Slot Name", sculptNewSlotName);
            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(sculptNewSlotName));
            if (GUILayout.Button(new GUIContent("Save slot modifications to a new slot", "Create a new SlotDataAsset with copied settings and sculpted MeshData.")))
                SaveSculptToNewSlot();
            EditorGUI.EndDisabledGroup();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.HelpBox(
                IsSculptMultiSlotMode
                    ? "Drag over any targetable slot to sculpt it. Connected target-slot boundaries remain welded, and Save MeshModifier includes every directly or indirectly affected slot. Select a specific slot to write or create a SlotDataAsset."
                    : "Drag on the selected slot to sculpt. Edits on other slots remain available for MeshModifier saving.",
                MessageType.Info);
        }

        private void FrameSelectedSculptSlot()
        {
            if (sculptSlot == null || BakedMesh == null || VertexObject == null || sculptSlotStart < 0 || sculptSlotVertexCount <= 0) return;
            Vector3[] vertices = BakedMesh.vertices;
            if (sculptSlotStart + sculptSlotVertexCount > vertices.Length) return;

            Vector3 first = VertexObject.transform.TransformPoint(vertices[sculptSlotStart]);
            Bounds bounds = new Bounds(first, Vector3.zero);
            for (int i = 1; i < sculptSlotVertexCount; i++)
                bounds.Encapsulate(VertexObject.transform.TransformPoint(vertices[sculptSlotStart + i]));

            // Give zero-thickness or very small slots enough extent for SceneView.Frame.
            float minimumExtent = Mathf.Max(0.001f, bounds.size.magnitude * 0.01f);
            bounds.Expand(minimumExtent);
            SceneView sceneView = openedSceneView != null ? openedSceneView : SceneView.lastActiveSceneView;
            if (sceneView == null) return;
            sceneView.Frame(bounds, false);
            sceneView.Repaint();
        }

        private bool HasSculptChanges()
        {
            foreach (SculptSlotEditState state in sculptSlotStates.Values)
                if (HasSculptChanges(state)) return true;
            return false;
        }

        private bool HasCurrentSculptChanges()
        {
            return HasSculptChanges(activeSculptState);
        }

        private bool HasSculptChanges(SculptSlotEditState state)
        {
            if (state == null || state.originalVertices == null || BakedMesh == null ||
                state.start < 0 || state.start + state.count > BakedMesh.vertexCount) return false;
            Vector3[] vertices = BakedMesh.vertices;
            for (int i = 0; i < state.count; i++)
                if ((vertices[state.start + i] - state.originalVertices[i]).sqrMagnitude > 1e-12f) return true;
            return false;
        }

        private List<SculptSlotEditState> GetChangedSculptStates()
        {
            List<SculptSlotEditState> changed = new List<SculptSlotEditState>();
            foreach (SculptSlotEditState state in sculptSlotStates.Values)
            {
                if (HasSculptChanges(state))
                    changed.Add(state);
            }
            changed.Sort((left, right) => string.Compare(
                left != null && left.slot != null ? left.slot.slotName : string.Empty,
                right != null && right.slot != null ? right.slot.slotName : string.Empty,
                StringComparison.Ordinal));
            return changed;
        }

        private void RestoreSculptPreview()
        {
            if (sculptOriginalVertices == null || BakedMesh == null || sculptSlotStart < 0) return;
            Vector3[] vertices = BakedMesh.vertices;
            if (sculptSlotStart + sculptOriginalVertices.Length > vertices.Length) return;
            Array.Copy(sculptOriginalVertices, 0, vertices, sculptSlotStart, sculptOriginalVertices.Length);
            BakedMesh.vertices = vertices;
            BakedMesh.RecalculateNormals();
            BakedMesh.RecalculateBounds();
            RefreshBakedMeshCaches();
            RefreshSculptCollider();
        }

        private bool SaveSculptModifier()
        {
            bool hasSculptChanges = HasSculptChanges();
            if (!hasSculptChanges && !vertexPaintDirty)
            {
                return false;
            }
            if (modifierEditor == null)
            {
                EditorUtility.DisplayDialog("Save MeshModifier", "The Mesh Modifier editor is not available.", "OK");
                return false;
            }

            string defaultName = hasSculptChanges
                ? (string.IsNullOrWhiteSpace(sculptModifierName) ? "SculptModifier" : sculptModifierName)
                : (string.IsNullOrWhiteSpace(vertexPaintModifierName) ? "VertexPaintModifier" : vertexPaintModifierName);
            return modifierEditor.SaveToAsset(defaultName, "Save MeshModifier");
        }

        private void SaveSculptAsBlendshape()
        {
            EndSculptStroke(true);
            string blendshapeName = string.IsNullOrWhiteSpace(sculptBlendshapeName)
                ? string.Empty
                : sculptBlendshapeName.Trim();
            if (string.IsNullOrEmpty(blendshapeName))
            {
                sculptBlendshapeStatusType = MessageType.Warning;
                sculptBlendshapeStatusMessage = "Enter a blendshape name before saving.";
                return;
            }

            List<SculptSlotEditState> changedStates = GetChangedSculptStates();
            if (changedStates.Count == 0)
            {
                sculptBlendshapeStatusType = MessageType.Warning;
                sculptBlendshapeStatusMessage = "Sculpt at least one slot before creating a blendshape.";
                return;
            }

            List<SlotDataAsset> targets = new List<SlotDataAsset>();
            Dictionary<SlotDataAsset, UMABlendShape> shapes = new Dictionary<SlotDataAsset, UMABlendShape>();
            Dictionary<SlotDataAsset, string> targetSlotNames = new Dictionary<SlotDataAsset, string>();
            List<string> errors = new List<string>();
            int replacementCount = 0;

            for (int i = 0; i < changedStates.Count; i++)
            {
                SculptSlotEditState state = changedStates[i];
                string slotName = state != null && state.slot != null ? state.slot.slotName : "Unknown";
                SlotDataAsset target = state != null && state.slot != null ? state.slot.asset : null;
                if (target == null)
                {
                    errors.Add("Slot '" + slotName + "' has no SlotDataAsset.");
                    continue;
                }
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(target)))
                {
                    errors.Add("Slot '" + slotName + "' does not reference a saved project asset.");
                    continue;
                }
                if (shapes.ContainsKey(target))
                {
                    errors.Add(
                        "Slots '" + targetSlotNames[target] + "' and '" + slotName +
                        "' share SlotDataAsset '" + target.name + "'. Give them independent slot assets before saving different sculpt results as blendshapes.");
                    continue;
                }
                if (!TryBuildSculptBlendshape(state, blendshapeName, out UMABlendShape shape, out string error))
                {
                    errors.Add("Slot '" + slotName + "': " + error);
                    continue;
                }

                targets.Add(target);
                shapes.Add(target, shape);
                targetSlotNames.Add(target, slotName);
                if (HasBlendshape(target.meshData, blendshapeName)) replacementCount++;
            }

            if (errors.Count > 0)
            {
                sculptBlendshapeStatusType = MessageType.Error;
                sculptBlendshapeStatusMessage = "Blendshape save failed. " + errors[0];
                EditorUtility.DisplayDialog(
                    "Unable to Save Sculpt as Blendshape",
                    string.Join("\n\n", errors),
                    "OK");
                return;
            }

            if (replacementCount > 0 && !EditorUtility.DisplayDialog(
                    "Replace Sculpt Blendshape",
                    "A blendshape named '" + blendshapeName + "' already exists on " + replacementCount +
                    " of the " + targets.Count + " sculpted SlotDataAsset(s). Replace the existing blendshape data?",
                    "Replace",
                    "Cancel"))
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Save Sculpt as Blendshape");
            UnityEngine.Object[] undoTargets = new UnityEngine.Object[targets.Count];
            for (int i = 0; i < targets.Count; i++) undoTargets[i] = targets[i];
            Undo.RegisterCompleteObjectUndo(undoTargets, "Save Sculpt as Blendshape");

            for (int i = 0; i < targets.Count; i++)
            {
                SlotDataAsset target = targets[i];
                AddOrReplaceBlendshape(target.meshData, shapes[target]);
                EditorUtility.SetDirty(target);
            }
            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(undoGroup);

            sculptBlendshapeName = blendshapeName;
            sculptBlendshapeStatusType = MessageType.Info;
            sculptBlendshapeStatusMessage =
                "Saved blendshape '" + blendshapeName + "' on " + targets.Count +
                " sculpted SlotDataAsset(s). Regenerate the avatar to load the saved blendshape.";
            RepaintLinkedEditors();
        }

        private bool TryBuildSculptBlendshape(
            SculptSlotEditState state,
            string blendshapeName,
            out UMABlendShape shape,
            out string error)
        {
            shape = null;
            error = string.Empty;
            if (state == null || state.slot == null || state.slot.asset == null ||
                UMAMeshData.IsNullOrEmptyMeshData(state.slot.asset.meshData))
            {
                error = "The source mesh data is unavailable.";
                return false;
            }
            if (BakedMesh == null || state.originalVertices == null || state.originalVertices.Length != state.count ||
                state.start < 0 || state.start + state.count > BakedMesh.vertexCount ||
                state.slot.asset.meshData.vertexCount != state.count)
            {
                error = "The source topology no longer matches the sculpt preview.";
                return false;
            }

            Vector3[] currentVertices = BakedMesh.vertices;
            Vector3[] deltaVertices = new Vector3[state.count];
            for (int i = 0; i < state.count; i++)
                deltaVertices[i] = currentVertices[state.start + i] - state.originalVertices[i];

            Vector3[] deltaNormals = new Vector3[0];
            Vector3[] currentNormals = BakedMesh.normals;
            if (state.hasOriginalNormals && state.originalNormals != null &&
                state.originalNormals.Length == state.count && currentNormals != null &&
                state.start + state.count <= currentNormals.Length)
            {
                deltaNormals = new Vector3[state.count];
                for (int i = 0; i < state.count; i++)
                    deltaNormals[i] = currentNormals[state.start + i] - state.originalNormals[i];
            }

            shape = new UMABlendShape
            {
                shapeName = blendshapeName,
                frames = new[]
                {
                    new UMABlendFrame
                    {
                        frameWeight = 100f,
                        deltaVertices = deltaVertices,
                        deltaNormals = deltaNormals,
                        deltaTangents = new Vector3[0]
                    }
                }
            };
            return true;
        }

        private static bool HasBlendshape(UMAMeshData meshData, string blendshapeName)
        {
            if (meshData == null || meshData.blendShapes == null) return false;
            for (int i = 0; i < meshData.blendShapes.Length; i++)
            {
                UMABlendShape shape = meshData.blendShapes[i];
                if (shape != null && string.Equals(shape.shapeName, blendshapeName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void AddOrReplaceBlendshape(UMAMeshData meshData, UMABlendShape shape)
        {
            List<UMABlendShape> shapes = meshData.blendShapes != null
                ? new List<UMABlendShape>(meshData.blendShapes)
                : new List<UMABlendShape>();
            int existing = shapes.FindIndex(item => item != null &&
                string.Equals(item.shapeName, shape.shapeName, StringComparison.Ordinal));
            if (existing >= 0) shapes[existing] = shape;
            else shapes.Add(shape);
            meshData.blendShapes = shapes.ToArray();
        }

        private UMAMeshData BuildSculptedSlotMeshData()
        {
            if (!HasCurrentSculptChanges() || sculptSlot == null || sculptSlot.asset == null ||
                UMAMeshData.IsNullOrEmptyMeshData(sculptSlot.asset.meshData))
                return null;

            UMAMeshData meshData = sculptSlot.asset.meshData.DeepCopy();
            if (meshData == null || meshData.vertices == null || meshData.vertices.Length != sculptSlotVertexCount)
                return null;

            Vector3[] currentVertices = BakedMesh.vertices;
            Vector3[] currentNormals = BakedMesh.normals;
            for (int i = 0; i < sculptSlotVertexCount; i++)
            {
                meshData.vertices[i] += currentVertices[sculptSlotStart + i] - sculptOriginalVertices[i];
                if (meshData.normals != null && i < meshData.normals.Length && sculptOriginalNormals != null &&
                    i < sculptOriginalNormals.Length && currentNormals != null && sculptSlotStart + i < currentNormals.Length)
                {
                    Vector3 adjustedNormal = meshData.normals[i] + currentNormals[sculptSlotStart + i] - sculptOriginalNormals[i];
                    if (adjustedNormal.sqrMagnitude > 1e-12f) meshData.normals[i] = adjustedNormal.normalized;
                }
            }
            return meshData;
        }

        private void SaveSculptToBaseSlot()
        {
            if (sculptSlot == null || sculptSlot.asset == null) return;
            if (!EditorUtility.DisplayDialog(
                "Warning",
                "Warning, this will overwrite the MeshData on the slot with the new values!",
                "Overwrite MeshData",
                "Cancel"))
                return;

            UMAMeshData modifiedMeshData = BuildSculptedSlotMeshData();
            if (modifiedMeshData == null)
            {
                EditorUtility.DisplayDialog("Unable to Save Slot", "The selected slot's MeshData is unavailable or its topology no longer matches the sculpt preview.", "OK");
                return;
            }

            SlotDataAsset target = sculptSlot.asset;
            Undo.RecordObject(target, "Overwrite Slot MeshData From Sculpt");
            target.meshData = modifiedMeshData;
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
            Selection.activeObject = target;
            StageUtility.GoBackToPreviousStage();
        }

        private void SaveSculptToNewSlot()
        {
            if (sculptSlot == null || sculptSlot.asset == null || string.IsNullOrWhiteSpace(sculptNewSlotName)) return;
            UMAMeshData modifiedMeshData = BuildSculptedSlotMeshData();
            if (modifiedMeshData == null)
            {
                EditorUtility.DisplayDialog("Unable to Save Slot", "The selected slot's MeshData is unavailable or its topology no longer matches the sculpt preview.", "OK");
                return;
            }

            string cleanName = sculptNewSlotName.Trim();
            string lastFolder = EditorPrefs.GetString("UMA_SculptSlotSaveFolder_" + Application.dataPath.GetHashCode(), "Assets");
            if (!AssetDatabase.IsValidFolder(lastFolder)) lastFolder = "Assets";
            string path = EditorUtility.SaveFilePanelInProject("Save Sculpted SlotDataAsset", cleanName, "asset", "Create a new SlotDataAsset containing the sculpted mesh.", lastFolder);
            if (string.IsNullOrEmpty(path)) return;
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            EditorPrefs.SetString("UMA_SculptSlotSaveFolder_" + Application.dataPath.GetHashCode(), System.IO.Path.GetDirectoryName(path));

            SlotDataAsset newSlot = Instantiate(sculptSlot.asset);
            newSlot.name = cleanName;
            newSlot._oldSlotName = cleanName;
            newSlot.meshData = modifiedMeshData;
            newSlot.meshData.SlotName = cleanName;
            newSlot.hideFlags = HideFlags.None;
            SerializedObject serializedSlot = new SerializedObject(newSlot);
            SerializedProperty sourceSlotName = serializedSlot.FindProperty("_sourceSlotName");
            if (sourceSlotName != null) sourceSlotName.stringValue = string.Empty;
            serializedSlot.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(newSlot, "Create Sculpted SlotDataAsset");
            AssetDatabase.CreateAsset(newSlot, path);
            EditorUtility.SetDirty(newSlot);
            AssetDatabase.SaveAssetIfDirty(newSlot);
            if (UMAAssetIndexer.Instance != null) UMAAssetIndexer.Instance.ProcessNewItem(newSlot, false, false);
            Selection.activeObject = newSlot;
            StageUtility.GoBackToPreviousStage();
        }

        private void CancelInteraction()
        {
            if (touchupWeightsMode && touchupPaintStrokeActive)
            {
                EndTouchupWeightPaintStroke(true);
            }
            pendingStateClickAction = false;
            touchupEmptyClickCandidate = false;
            touchupReplaceSelectionOnNextCandidates = false;
            replaceSelectionOnRectSelect = false;
            selectionBrushedVerticesThisStroke.Clear();
            rectSelect = false;
            selectionBrushing = false;
            EndSelectionUndoSnapshot();
        }

        private selectMode GetEffectiveSelectMode(Event currentEvent)
        {
            if (currentDefineMode == DefineMode.DefineVertexSet && currentEvent != null)
            {
                if (currentEvent.control)
                {
                    return selectMode.Remove;
                }

                if (currentEvent.shift)
                {
                    return selectMode.Add;
                }
            }

            return currentMode;
        }

        private bool IsAllowedBySelectionSlot(SlotData slot)
        {
            if (slot == null)
            {
                return false;
            }
            if (touchupWeightsMode)
            {
                return touchupWeightSlot != null &&
                       (ReferenceEquals(slot, touchupWeightSlot) ||
                        string.Equals(slot.slotName, touchupWeightSlot.slotName, StringComparison.Ordinal));
            }
            return selectionSlot <= 0 || (selectionSlot < selectFrom.Length &&
                                          string.Equals(slot.slotName, selectFrom[selectionSlot], StringComparison.Ordinal));
        }


        public void SelectAll()
        {
            SelectedVertexes.Clear();
            var vertexes = BakedMesh.vertices;
            for (int i = 0; i < vertexes.Length; i++)
            {
                if (TryGetSlotForBakedVertex(i, out SlotData foundSlot, out int foundVert))
                {
                    if (!IsAllowedBySelectionSlot(foundSlot))
                    {
                        continue;
                    }
                    SelectedVertexes.Add(new VertexSelection()
                    {
                        vertexIndexOnSlot = foundVert,
                        slot = foundSlot,
                        WorldPosition = VertexObject.transform.TransformPoint(vertexes[i]),
                        isActive = (currentNewVertexState == (int)newVertexState.Active)
                    });
                }
            }
        }

        private void LoadSelections()
        {
            // clear the progress bar
            EditorUtility.ClearProgressBar();
            // load the selections, and then add them to the SelectedVertexes if they don't already exist
            string path = EditorUtility.OpenFilePanel("Load Selections", "Assets", "json");
            if (path.Length > 0)
            {
                string json = File.ReadAllText(path);
                SerializedSelections ss = JsonUtility.FromJson<SerializedSelections>(json);
                List<VertexSelection> newSelections = ss.ToSelections(thisDCA, this);
                for (int i = 0; i < newSelections.Count; i++)
                {
                    bool found = false;
                    for (int j = 0; j < SelectedVertexes.Count; j++)
                    {
                        if (SelectedVertexes[j].slot.slotName == newSelections[i].slot.slotName && SelectedVertexes[j].vertexIndexOnSlot == newSelections[i].vertexIndexOnSlot)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        SelectedVertexes.Add(newSelections[i]);
                    }
                    // update the progress bar
                    EditorUtility.DisplayProgressBar("Loading Selections", "Processing vertex " + i.ToString(), (float)i / (float)newSelections.Count);
                }
            }
            // close the progress bar
            EditorUtility.ClearProgressBar();
        }

        private void SaveSelections()
        {
            // save the selections to disk
            string path = EditorUtility.SaveFilePanel("Save Selections", "Assets", "Selections", "json");
            if (path.Length > 0)
            {
                SerializedSelections ss = SerializedSelections.FromSelections(SelectedVertexes);
                string json = JsonUtility.ToJson(ss);
                File.WriteAllText(path, json);
                hasSaved = true;
                CaptureSavedSelectionSnapshot();
            }
        }

        private void InvertSelection()
        {
            EditorUtility.ClearProgressBar();
            List<VertexSelection> newSelection = new List<VertexSelection>();
            try
            {
                for (int i = 0; i < BakedMesh.vertices.Length; i++)
                {
                    EditorUtility.DisplayProgressBar("Inverting Selection", "Processing vertex " + i.ToString(), (float)i / (float)BakedMesh.vertices.Length);
                    if (TryGetSlotForBakedVertex(i, out SlotData foundSlot, out int foundVert))
                    {
                        if (!IsAllowedBySelectionSlot(foundSlot))
                        {
                            continue;
                        }
                        bool found = false;
                        for (int j = 0; j < SelectedVertexes.Count; j++)
                        {
                            if (SelectedVertexes[j].slot.slotName == foundSlot.slotName && SelectedVertexes[j].vertexIndexOnSlot == foundVert)
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            newSelection.Add(new VertexSelection()
                            {
                                vertexIndexOnSlot = foundVert,
                                slot = foundSlot,
                                WorldPosition = VertexObject.transform.TransformPoint(BakedMesh.vertices[i]),
                                isActive = (currentNewVertexState == (int)newVertexState.Active)
                            });
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            SelectedVertexes = newSelection;
        }

        private Rect GetMinMax(Vector2 rectStart, Vector2 rectEnd)
        {
            float xMin = Mathf.Min(rectStart.x, rectEnd.x);
            float xMax = Mathf.Max(rectStart.x, rectEnd.x);
            float yMin = Mathf.Min(rectStart.y, rectEnd.y);
            float yMax = Mathf.Max(rectStart.y, rectEnd.y);

            Rect MinMax = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);

            return MinMax;
        }

        private void RectangleSelect(Event currentEvent, Rect ScreenArea)
        {
            EditorUtility.ClearProgressBar();

            selectMode effectiveMode = GetEffectiveSelectMode(currentEvent);
            if (replaceSelectionOnRectSelect && currentDefineMode == DefineMode.DefineVertexSet && effectiveMode == selectMode.Add)
            {
                SelectedVertexes.Clear();
            }

            var vertexes = BakedMesh.vertices;
            var normals = BakedMesh.normals;
            for (int i = 0; i < vertexes.Length; i++)
            {
                if (i % 100 == 0)
                {
                    EditorUtility.DisplayProgressBar("Selecting Vertices", "Processing vertex " + i.ToString(), (float)i / (float)vertexes.Length);
                }
                Vector3 screenPos = HandleUtility.WorldToGUIPoint(VertexObject.transform.TransformPoint(vertexes[i]));
                if (ScreenArea.Contains(screenPos))
                {
                    bool blocked = false;

                    if (!selectFacingAway)
                    {
                        Vector3 Normal = normals[i];
                        // if the normal is not facing the camera
                        if (Vector3.Dot(Normal, Camera.current.transform.forward) > 0)
                        {
                            continue;
                        }
                    }


                    if (!selectObscured)
                    {
                        // do a raycast here from the camera to the vertex (expanded by the normal * 1.001)
                        Ray ray = HandleUtility.GUIPointToWorldRay(screenPos);
                        if (phyScene.Raycast(ray.origin, ray.direction, out RaycastHit hit))
                        {
                            if (hit.transform != null && hit.transform.gameObject == VertexObject)
                            {
                                float dist = Mathf.Abs(Vector3.Distance(VertexObject.transform.TransformPoint(vertexes[i]), hit.point));
                                if (dist > 0.001f)
                                {
                                    blocked = true;
                                }
                            }
                        }
                    }

                    if (!blocked)
                    {
                        if (!TryGetSlotForBakedVertex(i, out SlotData foundSlot, out int foundVert))
                        {
                            continue;
                        }

                        if (currentDefineMode == DefineMode.DefineVertexSet && !IsAllowedBySelectionSlot(foundSlot))
                        {
                            continue;
                        }

                        if (foundSlot != null)
                        {
                            switch (effectiveMode)
                            {
                                case selectMode.Add:
                                    AddVertex(foundSlot, foundVert);
                                    break;
                                case selectMode.Remove:
                                    RemoveVertex(foundSlot, foundVert);
                                    break;
                                case selectMode.InvertSelection:
                                    InvertVertex(foundSlot, foundVert);
                                    break;
                                case selectMode.Activate:
                                    ActivateVertex(foundSlot, foundVert);
                                    break;
                                case selectMode.Deactivate:
                                    DeactivateVertex(foundSlot, foundVert);
                                    break;
                                case selectMode.ToggleState:
                                    int stateIndex = GetSelectionIndex(foundSlot, foundVert);
                                    if (stateIndex >= 0)
                                    {
                                        SelectedVertexes[stateIndex].isActive = !SelectedVertexes[stateIndex].isActive;
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
            EditorUtility.ClearProgressBar();
            RepaintLinkedEditors();
        }

        private bool ApplySelectionBrush(Event currentEvent)
        {
            if (touchupWeightsMode && touchupWeightTool == TouchupWeightTool.Paint)
            {
                return ApplyTouchupWeightPaintBrush(currentEvent);
            }

            if (selectionBrushShape == SelectionBrushShape.Circle)
            {
                return ApplyCircleSelectionBrush(currentEvent);
            }

            return SingleSelect(currentEvent);
        }

        private bool ApplyCircleSelectionBrush(Event currentEvent)
        {
            if (BakedMesh == null || VertexObject == null)
            {
                return false;
            }

            if (touchupWeightsMode)
            {
                List<int> touchupCandidates = GetTouchupBrushVertexIndices(currentEvent, false);
                HashSet<int> strokeCandidates = new HashSet<int>();
                for (int i = 0; i < touchupCandidates.Count; i++)
                {
                    int vertexIndex = touchupCandidates[i];
                    string selectionKey = touchupWeightSlot.slotName + ":" + vertexIndex;
                    if (selectionBrushedVerticesThisStroke.Contains(selectionKey))
                    {
                        continue;
                    }

                    selectionBrushedVerticesThisStroke.Add(selectionKey);
                    strokeCandidates.Add(vertexIndex);
                }

                bool touchupChanged = ApplyTouchupSelectionCandidates(
                    strokeCandidates,
                    GetEffectiveSelectMode(currentEvent));
                if (touchupChanged)
                {
                    RepaintLinkedEditors();
                    SceneView.RepaintAll();
                }
                return touchupChanged;
            }

            RefreshBakedMeshCaches();
            Vector3[] vertexes = bakedVertices;
            Vector3[] normals = bakedNormals;
            if (vertexes == null || vertexes.Length == 0)
            {
                return false;
            }

            bool changed = false;
            selectMode effectiveMode = GetEffectiveSelectMode(currentEvent);
            float radius = Mathf.Clamp(selectionBrushRadiusPixels, MinSelectionBrushRadiusPixels, MaxSelectionBrushRadiusPixels);
            float radiusSqr = radius * radius;
            Vector2 brushCenter = currentEvent.mousePosition;

            for (int i = 0; i < vertexes.Length; i++)
            {
                if (!TryGetSlotForBakedVertex(i, out SlotData foundSlot, out int foundVert) ||
                    foundSlot == null ||
                    (currentDefineMode == DefineMode.DefineVertexSet && !IsAllowedBySelectionSlot(foundSlot)))
                {
                    continue;
                }

                Vector3 screenPos3 = HandleUtility.WorldToGUIPoint(VertexObject.transform.TransformPoint(vertexes[i]));
                Vector2 screenPos = new Vector2(screenPos3.x, screenPos3.y);
                if ((screenPos - brushCenter).sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                if (!IsPaintCandidateVisible(i, screenPos, vertexes, normals))
                {
                    continue;
                }

                string selectionKey = foundSlot.slotName + ":" + foundVert;
                if (selectionBrushedVerticesThisStroke.Contains(selectionKey))
                {
                    continue;
                }

                if (ApplySelectionModeToVertex(foundSlot, foundVert, effectiveMode))
                {
                    changed = true;
                }

                selectionBrushedVerticesThisStroke.Add(selectionKey);
            }

            if (changed)
            {
                RepaintLinkedEditors();
                SceneView.RepaintAll();
            }

            return changed;
        }

        private bool ApplyTouchupSelectionCandidates(HashSet<int> candidates, selectMode mode)
        {
            if (candidates == null || candidates.Count == 0 || touchupWeightSlot == null)
            {
                return false;
            }

            bool changed = false;
            if (mode == selectMode.Add && touchupReplaceSelectionOnNextCandidates)
            {
                int removed = SelectedVertexes.RemoveAll(SelectionMatchesTouchupSlot);
                changed = removed > 0;
                touchupReplaceSelectionOnNextCandidates = false;
            }

            HashSet<int> selectedIndices = new HashSet<int>();
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                VertexSelection selection = SelectedVertexes[i];
                if (SelectionMatchesTouchupSlot(selection))
                {
                    selectedIndices.Add(selection.vertexIndexOnSlot);
                }
            }

            if (mode == selectMode.Remove || mode == selectMode.InvertSelection)
            {
                int removed = SelectedVertexes.RemoveAll(selection =>
                    SelectionMatchesTouchupSlot(selection) &&
                    candidates.Contains(selection.vertexIndexOnSlot));
                changed = removed > 0;
            }

            if (mode == selectMode.Add || mode == selectMode.InvertSelection)
            {
                if (bakedVertices == null || BakedMesh == null ||
                    bakedVertices.Length != BakedMesh.vertexCount)
                {
                    RefreshBakedMeshCaches();
                }
                foreach (int vertexIndex in candidates)
                {
                    if (selectedIndices.Contains(vertexIndex))
                    {
                        continue;
                    }

                    int bakedIndex = touchupWeightSlot.vertexOffset + vertexIndex;
                    if (bakedVertices == null || bakedIndex < 0 || bakedIndex >= bakedVertices.Length)
                    {
                        continue;
                    }

                    SelectedVertexes.Add(new VertexSelection
                    {
                        vertexIndexOnSlot = vertexIndex,
                        slot = touchupWeightSlot,
                        WorldPosition = VertexObject.transform.TransformPoint(bakedVertices[bakedIndex]),
                        isActive = currentNewVertexState == (int)newVertexState.Active
                    });
                    changed = true;
                }
            }

            if (changed)
            {
                CurrentSelected = -1;
                SetActive(null);
            }
            return changed;
        }

        private List<int> GetTouchupBrushVertexIndices(Event currentEvent, bool selectedVerticesOnly)
        {
            return GetTouchupBrushVertexIndices(
                currentEvent.mousePosition,
                selectedVerticesOnly);
        }

        private List<int> GetTouchupBrushVertexIndices(
            Vector2 brushCenter,
            bool selectedVerticesOnly)
        {
            List<int> result = new List<int>();
            if (touchupWeightSlot == null || touchupWeightSlot.asset == null ||
                BakedMesh == null || VertexObject == null)
            {
                return result;
            }

            if (bakedVertices == null || bakedVertices.Length != BakedMesh.vertexCount)
            {
                RefreshBakedMeshCaches();
            }
            if (bakedVertices == null || bakedVertices.Length == 0)
            {
                return result;
            }

            HashSet<int> selectedIndices = null;
            if (selectedVerticesOnly)
            {
                selectedIndices = new HashSet<int>();
                List<VertexSelection> selectedVertices = GetTouchupSelectedVertices();
                for (int i = 0; i < selectedVertices.Count; i++)
                {
                    selectedIndices.Add(selectedVertices[i].vertexIndexOnSlot);
                }
                if (selectedIndices.Count == 0)
                {
                    return result;
                }
            }

            float radius = Mathf.Clamp(
                selectionBrushRadiusPixels,
                MinSelectionBrushRadiusPixels,
                MaxSelectionBrushRadiusPixels);
            float radiusSqr = radius * radius;
            int bakedStart = touchupWeightSlot.vertexOffset;
            int vertexCount = Mathf.Min(
                touchupWeightSlot.asset.meshData.vertexCount,
                bakedVertices.Length - bakedStart);
            if (bakedStart < 0 || vertexCount <= 0)
            {
                return result;
            }

            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                if (selectedIndices != null && !selectedIndices.Contains(vertexIndex))
                {
                    continue;
                }

                int bakedIndex = bakedStart + vertexIndex;
                Vector3 screenPosition3 = HandleUtility.WorldToGUIPoint(
                    VertexObject.transform.TransformPoint(bakedVertices[bakedIndex]));
                Vector2 screenPosition = new Vector2(screenPosition3.x, screenPosition3.y);
                if ((screenPosition - brushCenter).sqrMagnitude > radiusSqr ||
                    !IsPaintCandidateVisible(
                        bakedIndex,
                        screenPosition,
                        bakedVertices,
                        bakedNormals))
                {
                    continue;
                }

                result.Add(vertexIndex);
            }

            return result;
        }

        private bool TryGetTouchupVertexScreenPosition(
            int vertexIndex,
            out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;
            if (touchupWeightSlot == null || BakedMesh == null || VertexObject == null ||
                bakedVertices == null)
            {
                return false;
            }

            int bakedIndex = touchupWeightSlot.vertexOffset + vertexIndex;
            if (vertexIndex < 0 || bakedIndex < 0 || bakedIndex >= bakedVertices.Length)
            {
                return false;
            }

            Vector3 guiPosition = HandleUtility.WorldToGUIPoint(
                VertexObject.transform.TransformPoint(bakedVertices[bakedIndex]));
            screenPosition = new Vector2(guiPosition.x, guiPosition.y);
            return true;
        }

        private bool IsPaintCandidateVisible(int bakedVertexIndex, Vector2 screenPos, Vector3[] vertexes, Vector3[] normals)
        {
            if (!selectFacingAway && normals != null && bakedVertexIndex < normals.Length && Camera.current != null)
            {
                Vector3 normal = normals[bakedVertexIndex];
                if (Vector3.Dot(normal, Camera.current.transform.forward) > 0)
                {
                    return false;
                }
            }

            if (!selectObscured)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(screenPos);
                if (phyScene.Raycast(ray.origin, ray.direction, out RaycastHit hit))
                {
                    if (hit.transform != null && hit.transform.gameObject == VertexObject)
                    {
                        float dist = Mathf.Abs(Vector3.Distance(VertexObject.transform.TransformPoint(vertexes[bakedVertexIndex]), hit.point));
                        if (dist > 0.001f)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private bool ApplySelectionModeToVertex(SlotData foundSlot, int foundVert, selectMode effectiveMode)
        {
            int existingIndex = GetSelectionIndex(foundSlot, foundVert);
            switch (effectiveMode)
            {
                case selectMode.Add:
                    if (existingIndex >= 0)
                    {
                        return false;
                    }
                    AddVertex(foundSlot, foundVert);
                    CurrentSelected = SelectedVertexes.Count - 1;
                    SetActive(null);
                    return true;
                case selectMode.Remove:
                    if (existingIndex < 0)
                    {
                        return false;
                    }
                    RemoveVertex(foundSlot, foundVert);
                    if (CurrentSelected == existingIndex)
                    {
                        CurrentSelected = -1;
                        SetActive(null);
                    }
                    else if (CurrentSelected > existingIndex)
                    {
                        CurrentSelected--;
                    }
                    return true;
                case selectMode.InvertSelection:
                    InvertVertex(foundSlot, foundVert);
                    CurrentSelected = GetSelectionIndex(foundSlot, foundVert);
                    SetActive(null);
                    return true;
                case selectMode.Activate:
                    if (existingIndex < 0 || SelectedVertexes[existingIndex].isActive)
                    {
                        return false;
                    }
                    ActivateVertex(foundSlot, foundVert);
                    CurrentSelected = existingIndex;
                    SetActive(null);
                    return true;
                case selectMode.Deactivate:
                    if (existingIndex < 0 || !SelectedVertexes[existingIndex].isActive)
                    {
                        return false;
                    }
                    DeactivateVertex(foundSlot, foundVert);
                    CurrentSelected = existingIndex;
                    SetActive(null);
                    return true;
                case selectMode.ToggleState:
                    if (existingIndex < 0)
                    {
                        return false;
                    }
                    SelectedVertexes[existingIndex].isActive = !SelectedVertexes[existingIndex].isActive;
                    CurrentSelected = existingIndex;
                    SetActive(null);
                    return true;
            }

            return false;
        }

        void ActivateVertex(SlotData foundSlot, int foundVert)
        {
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                if (SelectedVertexes[i].slot.slotName == foundSlot.slotName && SelectedVertexes[i].vertexIndexOnSlot == foundVert)
                {
                    SelectedVertexes[i].isActive = true;
                    return;
                }
            }
        }

        void DeactivateVertex(SlotData foundSlot, int foundVert)
        {
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                if (SelectedVertexes[i].slot.slotName == foundSlot.slotName && SelectedVertexes[i].vertexIndexOnSlot == foundVert)
                {
                    SelectedVertexes[i].isActive = false;
                    return;
                }
            }
        }


        void AddVertex(SlotData foundSlot, int foundVert)
        {
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                if (SelectedVertexes[i].slot.slotName == foundSlot.slotName && SelectedVertexes[i].vertexIndexOnSlot == foundVert)
                {
                    return;
                }
            }
            SelectedVertexes.Add(new VertexSelection()
            {
                vertexIndexOnSlot = foundVert,
                slot = foundSlot,
                WorldPosition = GetWorldPosition(foundSlot, foundVert),
                isActive = (currentNewVertexState == (int)newVertexState.Active)
            });
        }

        void RemoveVertex(SlotData foundSlot, int foundVert)
        {
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                if (SelectedVertexes[i].slot.slotName == foundSlot.slotName && SelectedVertexes[i].vertexIndexOnSlot == foundVert)
                {
                    SelectedVertexes.RemoveAt(i);
                    return;
                }
            }
        }

        void InvertVertex(SlotData foundSlot, int foundVert)
        {
            for (int i = 0; i < SelectedVertexes.Count; i++)
            {
                if (SelectedVertexes[i].slot.slotName == foundSlot.slotName && SelectedVertexes[i].vertexIndexOnSlot == foundVert)
                {
                    SelectedVertexes.RemoveAt(i);
                    return;
                }
            }
            SelectedVertexes.Add(new VertexSelection()
            {
                vertexIndexOnSlot = foundVert,
                slot = foundSlot,
                WorldPosition = GetWorldPosition(foundSlot, foundVert)
            });
        }


        public void SelectVertexes(VertexAdjustmentCollection unsortedAdjustments)
        {
            SelectedVertexes.Clear();
            VertexAdjustmentCollection vac = unsortedAdjustments;
            for (int j = 0; j < vac.vertexAdjustments.Count; j++)
            {
                VertexAdjustment va = vac.vertexAdjustments[j];
                SlotData slot = FindSlotBySourceSlotOrName(va.slotName);
                if (slot != null)
                {
                    SelectedVertexes.Add(new VertexSelection()
                    {
                        vertexIndexOnSlot = va.vertexIndex,
                        slot = slot,
                        WorldPosition = GetWorldPosition(slot, va.vertexIndex),
                        isActive = true
                    });
                }
            }
        }

        private SlotData FindSlotBySourceSlotOrName(string slotKey)
        {
            if (string.IsNullOrEmpty(slotKey) || thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return null;
            }

            SlotData legacySlot = null;
            SlotData[] slots = thisDCA.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                SlotData slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                if (slot.asset != null && string.Equals(slot.asset.sourceSlot, slotKey, StringComparison.OrdinalIgnoreCase))
                {
                    return slot;
                }

                if (legacySlot == null && string.Equals(slot.slotName, slotKey, StringComparison.OrdinalIgnoreCase))
                {
                    legacySlot = slot;
                }
            }

            return legacySlot;
        }

        private bool SingleSelect(Event currentEvent)
        {
            bool found = false;
            selectMode effectiveMode = GetEffectiveSelectMode(currentEvent);
            bool replaceSelectionOnAdd = currentDefineMode == DefineMode.DefineVertexSet &&
                                         effectiveMode == selectMode.Add &&
                                         !currentEvent.shift &&
                                         !currentEvent.control &&
                                         !(IsSelectionBrushModeEnabled && selectionBrushing);

            //Debug.Log("Doing single select");
            Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
            if (phyScene.Raycast(ray.origin, ray.direction, out RaycastHit hit))
            {
                if (hit.transform != null && hit.transform.gameObject == VertexObject)
                {
                    VertexSelection vs = FindVertex(hit, BakedMesh, VertexObject);
                    if (vs != null)
                    {
                        bool trackSelectionBrushStroke = IsSelectionBrushModeEnabled && selectionBrushing;
                        string selectionKey = vs.slot.slotName + ":" + vs.vertexIndexOnSlot;
                        if (trackSelectionBrushStroke && selectionBrushedVerticesThisStroke.Contains(selectionKey))
                        {
                            return false;
                        }

                        if (currentDefineMode == DefineMode.DefineVertexSet)
                        {
                            if (!IsAllowedBySelectionSlot(vs.slot))
                            {
                                return false;
                            }
                        }

                        for (int i = 0; i < SelectedVertexes.Count; i++)
                        {
                            if (SelectedVertexes[i].slot.slotName == vs.slot.slotName && SelectedVertexes[i].vertexIndexOnSlot == vs.vertexIndexOnSlot)
                            {
                                found = true;
                                int selectedVertex = i;

                                if (effectiveMode == selectMode.Add && replaceSelectionOnAdd)
                                {
                                    bool previousActive = SelectedVertexes[i].isActive;
                                    SelectedVertexes.Clear();
                                    vs.isActive = previousActive;
                                    SelectedVertexes.Add(vs);
                                    CurrentSelected = 0;
                                    SetActive(null);
                                    found = true;
                                }
                                else if (effectiveMode == selectMode.Remove)
                                {
                                    SelectedVertexes.RemoveAt(selectedVertex);
                                    if (CurrentSelected == selectedVertex)
                                    {
                                        CurrentSelected = -1;
                                        SetActive(null);
                                    }
                                    else if (CurrentSelected > selectedVertex)
                                    {
                                        CurrentSelected--;
                                    }
                                    found = true;
                                }
                                else if (effectiveMode == selectMode.InvertSelection)
                                {
                                    SelectedVertexes.RemoveAt(selectedVertex);
                                    if (CurrentSelected == selectedVertex)
                                    {
                                        CurrentSelected = -1;
                                        SetActive(null);
                                    }
                                    else if (CurrentSelected > selectedVertex)
                                    {
                                        CurrentSelected--;
                                    }
                                    found = true;
                                }
                                else if (effectiveMode == selectMode.ToggleState)
                                {
                                    SelectedVertexes[i].isActive = !SelectedVertexes[i].isActive;
                                    CurrentSelected = i;
                                    SetActive(null);
                                    found = true;
                                }
                                else if (effectiveMode == selectMode.Activate)
                                {
                                    SelectedVertexes[i].isActive = true;
                                    CurrentSelected = i;
                                    SetActive(null);
                                    found = true;
                                }
                                else if (effectiveMode == selectMode.Deactivate)
                                {
                                    SelectedVertexes[i].isActive = false;
                                    CurrentSelected = i;
                                    SetActive(null);
                                    found = true;
                                }
                                else
                                {
                                    CurrentSelected = i;
                                    SetActive(null);
                                }
                                break;
                            }
                        }

                        if (trackSelectionBrushStroke)
                        {
                            selectionBrushedVerticesThisStroke.Add(selectionKey);
                        }

                        if (!found)
                        {
                            if (effectiveMode == selectMode.Add || effectiveMode == selectMode.InvertSelection)
                            {
                                if (replaceSelectionOnAdd)
                                {
                                    SelectedVertexes.Clear();
                                }
                                SelectedVertexes.Add(vs);
                                CurrentSelected = SelectedVertexes.Count - 1;
                                SetActive(null);
                                found = true;
                            }
                        }
                    }
                }
            }
            RepaintLinkedEditors();
            return found;
        }

        private bool IsPointerOverTouchupCharacter(Vector2 mousePosition)
        {
            if (VertexObject == null)
            {
                return false;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            return phyScene.Raycast(ray.origin, ray.direction, out RaycastHit hit) &&
                   hit.transform != null &&
                   hit.transform.gameObject == VertexObject;
        }

        private void DrawSelectionBrushCircle(SceneView sceneView, Event currentEvent, bool mouseOverAnyWindow)
        {
            if (currentEvent.type != EventType.Repaint || mouseOverAnyWindow || !IsSelectionBrushModeEnabled || selectionBrushShape != SelectionBrushShape.Circle)
            {
                return;
            }

            Vector2 mousePosition = currentEvent.mousePosition;
            if (mousePosition.x < 0f || mousePosition.y < 0f || mousePosition.x > sceneView.position.width || mousePosition.y > sceneView.position.height)
            {
                return;
            }

            const int segments = 64;
            float radius = Mathf.Clamp(selectionBrushRadiusPixels, MinSelectionBrushRadiusPixels, MaxSelectionBrushRadiusPixels);
            Vector3[] points = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                points[i] = new Vector3(mousePosition.x + Mathf.Cos(angle) * radius, mousePosition.y + Mathf.Sin(angle) * radius, 0f);
            }

            Color previousColor = Handles.color;
            Handles.color = new Color(0f, 0f, 0f, 0.7f);
            Handles.DrawAAPolyLine(3f, points);
            Handles.color = currentMode == selectMode.Remove || currentMode == selectMode.Deactivate
                ? new Color(1f, 0.35f, 0.25f, 0.9f)
                : new Color(0.25f, 0.75f, 1f, 0.9f);
            Handles.DrawAAPolyLine(1.5f, points);
            Handles.color = previousColor;
        }

        public void CloseStage()
        {

            // This is only called from the MeshModifierEditor being closed
            // so we need to null this out so we don't try to close it again
            thisDCA.umaData.CharacterUpdated.RemoveAllListeners();
            thisDCA.umaData.ManualMeshModifiers.Clear();
            modifierEditor.DoCharacterRebuild(false, false);
            this.modifierEditor = null;

            StageUtility.GoBackToPreviousStage();
            SceneView.RepaintAll();

        }



        public void RebuildMesh(bool RecipeChanged, bool buildCollisionMesh = true)
        {
            UMAGeneratorBuiltin gb = thisDCA.umaData.umaGenerator as UMAGeneratorBuiltin;
            if (buildCollisionMesh)
            {
                thisDCA.umaData.CharacterUpdated.AddAction(BuildCollisionMesh);
            }
            if (gb != null)
            {
                gb.Clear();
                if (slotWeightEditorMode && slotWeightEditorSlotAsset != null)
                {
                    InstallSlotWeightEditorSlot(thisDCA, slotWeightEditorSlotAsset, out _);
                    thisDCA.umaData.Dirty(true, true, true);
                    gb.GenerateSingleUMA(thisDCA.umaData, true);
                    gb.Clear();
                    return;
                }

                if (RecipeChanged)
                {
                    var suppressed = SaveSuppressedSlots();
                    thisDCA.BuildCharacter(true, true);
                    //gb.GenerateSingleUMA(thisDCA.umaData, true);
                    RestoreSuppressedSlots(suppressed);
                }
                // always have to rebuild because the slots are regenerated
                thisDCA.umaData.Dirty(false, true, true); // have to rebuild materials and mesh if we drop out slots
                gb.GenerateSingleUMA(thisDCA.umaData, true);
            }
        }

        public List<string> SaveSuppressedSlots()
        {
            List<string> suppressed = new List<string>();
            foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
            {
                if (slot.Suppressed)
                {
                    suppressed.Add(slot.slotName);
                }
            }
            return suppressed;
        }

        public void RestoreSuppressedSlots(List<string> suppressed)
        {
            foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
            {
                if (suppressed.Contains(slot.slotName))
                {
                    slot.Suppressed = true;
                }
            }
        }

        public void BuildCollisionMesh(UMAData umaData)
        {
            thisDCA.umaData.CharacterUpdated.RemoveAction(BuildCollisionMesh);
            if (closing || VertexObject == null) return;
            SkinnedMeshRenderer smr = GetCurrentSkinnedMeshRenderer();
            if (smr == null)
            {
                Debug.LogError("No SkinnedMeshRenderer found");
                return;
            }
            if (BakedMesh != null) GameObject.DestroyImmediate(BakedMesh);
            sculptSlotStates.Clear();
            ClearCrossSlotSculptSeams();
            activeSculptState = null;
            sculptSlot = null;
            sculptOriginalVertices = null;
            sculptOriginalNormals = null;
            vertexPaintPreviewInitialized = false;
            vertexPaintSlot = null;
            vertexPaintBaseColors = null;
            vertexPaintColors = null;
            touchupBaseColors = null;
            touchupSkinningSourceVertices = null;
            touchupSkinningSourceMeshId = 0;
            touchupSkinningPositionOffsets.Clear();
            touchupSavedPositionVertexIndices.Clear();
            touchupLivePositionVertexIndices.Clear();
            ResetTouchupPaintData();
            stageSkinnedMeshRenderer = smr;
            CaptureOriginalVertexMaterials(smr);
            BakedMesh = new Mesh();
            BakedMesh.name = "BakedMesh";
            smr.BakeMesh(BakedMesh, true);
            smr.enabled = false;
            RefreshBakedMeshCaches();
            VertexObject.GetComponent<MeshFilter>().sharedMesh = BakedMesh;
            MeshCollider mc = VertexObject.GetComponent<MeshCollider>();
            mc.sharedMesh = BakedMesh;
            MeshRenderer previewRenderer = VertexObject.GetComponent<MeshRenderer>();
            if (previewRenderer != null)
            {
                previewRenderer.sharedMaterials = new Material[BakedMesh.subMeshCount];
                DestroyPastelVertexMaterials();
                SetVertexMaterialColors(VertexObject);
            }
            ApplyVertexDisplayOptions();
            UpdateSelections();
            EnsureSculptSession(true);
            if (IsVertexPaintModeEnabled)
            {
                EnsureVertexPaintSession(true);
            }
            if (touchupWeightsMode)
            {
                RefreshTouchupWeightVisualization();
            }
        }

        public void UpdateSelections()
        {
            Dictionary<string, SlotData> slotDict = new Dictionary<string, SlotData>();

            foreach (SlotData sd in thisDCA.umaData.umaRecipe.slotDataList)
            {
                if (!sd.Suppressed && !sd.asset.isUtilitySlot)
                {
                    slotDict.Add(sd.slotName, sd);
                }
            }


            foreach (VertexSelection vs in SelectedVertexes)
            {
                if (slotDict.ContainsKey(vs.slot.slotName))
                {
                    vs.slot = slotDict[vs.slot.slotName];
                    vs.suppressed = false;
                }
                else
                {
                    vs.suppressed = true;
                }
            }
        }

        public Vector3 InverseTransform(Vector3 point)
        {
            return VertexObject.transform.InverseTransformPoint(point);
        }

        private VertexSelection FindVertex(RaycastHit hit, Mesh mesh, GameObject go)
        {
            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            int triangle = hit.triangleIndex;

            var tris = mesh.triangles;
            var verts = mesh.vertices;

            int i0 = tris[triangle * 3];
            Vector3 local = go.transform.InverseTransformPoint(hit.point);

            int foundVert = tris[triangle * 3];
            float maxDist = MathF.Abs(Vector3.Distance(local, verts[foundVert])); //?? Why would this ever be negative? Yet it is!!!

            for (int i = 0; i < 3; i++)
            {
                Vector3 vert = verts[tris[triangle * 3 + i]];
                float dist = Vector3.Distance(local, vert);
                if (dist < maxDist)
                {
                    maxDist = dist;
                    foundVert = tris[triangle * 3 + i];
                }
            }

            if (TryGetSlotForBakedVertex(foundVert, out SlotData foundSlot, out int slotVertexIndex))
            {
                return new VertexSelection()
                {
                    vertexIndexOnSlot = slotVertexIndex,
                    slot = foundSlot,
                    WorldPosition = go.transform.TransformPoint(verts[foundVert]),
                    isActive = (currentNewVertexState == (int)newVertexState.Active)
                };
            }
            throw new Exception("Vertex not found on slots!");
        }


        protected override GUIContent CreateHeaderContent()
        {
            GUIContent headerContent = new GUIContent();
            headerContent.text = "UMA Vertex Editing";
            headerContent.image = titleContent.image;
            return headerContent;
        }

        protected void InitialSetup(SceneView sceneView)
        {
            NeedsCameraSetup = false;
            openedSceneView = sceneView;

            Tools.current = Tool.None;
            Tools.hidden = true;

            // Setup Scene view state
            sceneView.sceneViewState.showFlares = false;
            sceneView.sceneViewState.alwaysRefresh = false;
            sceneView.sceneViewState.showFog = false;
            sceneView.sceneViewState.showSkybox = false;
            sceneView.sceneViewState.showImageEffects = false;
            sceneView.sceneViewState.showParticleSystems = false;
            sceneView.sceneLighting = false;
            ApplyVertexDisplayOptions(true);
            sceneView.wantsMouseMove = true;
            sceneView.wantsMouseEnterLeaveWindow = true;


            RefreshVisibleSlotLists();



            // this doesn't work in 2021.3
            Tools.hidden = true;

            SceneView.lastActiveSceneView.pivot = new Vector3(0, 1, 2.5f);
            Selection.activeObject = VertexObject;
            sceneView.AlignViewToObject(cameraAnchor.transform);
            sceneView.FrameSelected();
            phyScene = PhysicsSceneExtensions.GetPhysicsScene(scene);
        }

        private void RefreshVisibleSlotLists()
        {
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null)
            {
                selectFrom = new string[] { "All Slots" };
                visibleSelectFrom = new string[] { "All Slots" };
                selectionSlot = 0;
                raycastSelectionSlot = 0;
                return;
            }

            List<string> all = new List<string>();
            List<string> visible = new List<string>();
            all.Add("All Slots");
            visible.Add("All Slots");
            foreach (var slot in thisDCA.umaData.umaRecipe.slotDataList)
            {
                if (slot == null)
                {
                    continue;
                }
                all.Add(slot.slotName);
                if (IsSelectableSlot(slot))
                {
                    visible.Add(slot.slotName);
                }
            }
            selectFrom = all.ToArray();
            visibleSelectFrom = visible.ToArray();

            if (selectionSlot >= selectFrom.Length)
            {
                selectionSlot = 0;
            }
            if (raycastSelectionSlot >= visibleSelectFrom.Length)
            {
                raycastSelectionSlot = 0;
            }
        }

        public Mesh GetBakedMesh()
        {
            return BakedMesh;
        }

        private Color[] defaultColors = new Color[]
    {
            new Color(1.0f, 0.9f, 0.9f, 1.0f),
            new Color(0.9f, 1.0f, 0.9f, 1.0f),
            new Color(0.9f, 0.9f, 1.0f, 1.0f),
            new Color(1.0f, 1.0f, 0.9f, 1.0f),
            new Color(0.9f, 1.0f, 1.0f, 1.0f),
            new Color(1.0f, 0.9f, 1.0f, 1.0f)
    };

        private Material vertexMaterial = null;
        private Material GetVertexMaterial(Color col)
        {
            if (vertexMaterial != null)
            {
                vertexMaterial.SetColor("_Color", col);
                return vertexMaterial;
            }
            Material M = UMAUtils.GetDefaultDiffuseMaterial();
            M.shader = Shader.Find("UMA/UnlitInstanced");
            M.SetColor("_Color", col);
            vertexMaterial = M;
            return vertexMaterial;
        }

        private Mesh vertexMesh = null;

        private Mesh GetVertexMesh()
        {
            if (vertexMesh == null)
            {
                GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                vertexMesh = Instantiate(obj.GetComponent<MeshFilter>().sharedMesh);
                DestroyImmediate(obj);
            }
            return vertexMesh;
        }

        private void SetVertexMaterialColors(GameObject VertexObject)
        {
            MeshRenderer mr = VertexObject.GetComponent<MeshRenderer>();
            List<Material> newMaterials = new List<Material>();

            if (mr != null)
            {
                for (int i = 0; i < mr.sharedMaterials.Length; i++)
                {
                    int colorNo = i % defaultColors.Length;
                    if (mr.sharedMaterials[i] == null)
                    {
                        Material M = UMAUtils.GetDefaultDiffuseMaterial();
                        if (M != null)
                        {
                            M.SetColor("_Color", defaultColors[colorNo]);
                            newMaterials.Add(M);
                        }
                        else
                        {
                            Debug.LogError("No Default Material found");
                        }
                    }
                }
                pastelVertexMaterials = newMaterials.ToArray();
                mr.sharedMaterials = pastelVertexMaterials;
            }
            else
            {
                Debug.LogError("No MeshRenderer found");
            }
        }

        private void DestroyPastelVertexMaterials()
        {
            if (pastelVertexMaterials == null) return;
            for (int i = 0; i < pastelVertexMaterials.Length; i++)
                if (pastelVertexMaterials[i] != null) DestroyImmediate(pastelVertexMaterials[i]);
            pastelVertexMaterials = null;
        }

        private void CaptureOriginalVertexMaterials(SkinnedMeshRenderer sourceRenderer)
        {
            Material[] source = sourceRenderer != null ? sourceRenderer.sharedMaterials : null;
            // Preserve the exact generated material objects and ordering. UMA's runtime
            // atlas/array materials may contain state that is not safe to reconstruct on
            // clones or transfer through another renderer's property blocks.
            originalVertexMaterials = source != null ? (Material[])source.Clone() : new Material[0];
        }

        private void DestroyOriginalVertexMaterialCopies()
        {
            originalVertexMaterials = null;
        }

        private void ApplyVertexDisplayOptions(bool initializeCameraMode = false)
        {
            if (VertexObject == null) return;
            MeshRenderer renderer = VertexObject.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            Material[] applied;
            if (touchupWeightsMode)
            {
                int materialCount = BakedMesh != null ? Mathf.Max(1, BakedMesh.subMeshCount) : 1;
                bool[] weightSubmeshes = GetTouchupWeightSubmeshes(materialCount);
                Material previewMaterial = GetVertexColorPreviewMaterial();
                applied = new Material[materialCount];
                for (int i = 0; i < applied.Length; i++)
                {
                    applied[i] = weightSubmeshes[i]
                        ? previewMaterial
                        : GetOriginalOrPastelMaterial(i);
                }
            }
            else if (IsVertexPaintModeEnabled && !showOriginalMaterials)
            {
                Material previewMaterial = GetVertexColorPreviewMaterial();
                int materialCount = BakedMesh != null ? Mathf.Max(1, BakedMesh.subMeshCount) : 1;
                applied = new Material[materialCount];
                for (int i = 0; i < applied.Length; i++)
                {
                    applied[i] = previewMaterial;
                }
            }
            else
            {
                Material[] requested = showOriginalMaterials && originalVertexMaterials != null && originalVertexMaterials.Length > 0
                    ? originalVertexMaterials
                    : pastelVertexMaterials;
                applied = requested != null ? (Material[])requested.Clone() : new Material[0];
            }
            renderer.sharedMaterials = applied;
            renderer.SetPropertyBlock(null);
            int propertyBlockCount = BakedMesh != null ? Mathf.Max(BakedMesh.subMeshCount, applied.Length) : applied.Length;
            for (int i = 0; i < propertyBlockCount; i++) renderer.SetPropertyBlock(null, i);

            SceneView sceneView = openedSceneView;
            if (initializeCameraMode && sceneView != null)
            {
                DrawCameraMode requestedMode = showVertexWireframe ? DrawCameraMode.TexturedWire : DrawCameraMode.Textured;
                sceneView.cameraMode = SceneView.GetBuiltinCameraMode(requestedMode);
            }

#pragma warning disable CS0618
            // The Scene camera toolbar owns wireframe display after initial setup.
            // Suppress Unity's separate selection-only wireframe overlay.
            EditorUtility.SetSelectedWireframeHidden(renderer, true);
#pragma warning restore CS0618
        }

        private bool[] GetTouchupWeightSubmeshes(int materialCount)
        {
            bool[] result = new bool[materialCount];
            if (BakedMesh == null || touchupWeightSlot == null || touchupWeightSlot.asset == null ||
                UMAMeshData.IsNullOrEmptyMeshData(touchupWeightSlot.asset.meshData))
            {
                return result;
            }

            int vertexStart = touchupWeightSlot.vertexOffset;
            int vertexEnd = vertexStart + touchupWeightSlot.asset.meshData.vertexCount;
            for (int submeshIndex = 0;
                 submeshIndex < BakedMesh.subMeshCount && submeshIndex < result.Length;
                 submeshIndex++)
            {
                int[] indices = BakedMesh.GetIndices(submeshIndex);
                for (int i = 0; i < indices.Length; i++)
                {
                    if (indices[i] >= vertexStart && indices[i] < vertexEnd)
                    {
                        result[submeshIndex] = true;
                        break;
                    }
                }
            }

            return result;
        }

        private Material GetOriginalOrPastelMaterial(int submeshIndex)
        {
            if (originalVertexMaterials != null &&
                submeshIndex >= 0 &&
                submeshIndex < originalVertexMaterials.Length &&
                originalVertexMaterials[submeshIndex] != null)
            {
                return originalVertexMaterials[submeshIndex];
            }

            if (pastelVertexMaterials != null &&
                submeshIndex >= 0 &&
                submeshIndex < pastelVertexMaterials.Length)
            {
                return pastelVertexMaterials[submeshIndex];
            }

            return null;
        }

        private Material GetVertexColorPreviewMaterial()
        {
            if (vertexColorPreviewMaterial != null)
            {
                return vertexColorPreviewMaterial;
            }

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                return null;
            }

            vertexColorPreviewMaterial = new Material(shader)
            {
                name = "Vertex Color Paint Preview",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (vertexColorPreviewMaterial.HasProperty("_Color"))
            {
                vertexColorPreviewMaterial.SetColor("_Color", Color.white);
            }
            if (vertexColorPreviewMaterial.HasProperty("_MainTex"))
            {
                vertexColorPreviewMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
            }
            return vertexColorPreviewMaterial;
        }

        internal void RemoveVertexAdjustment(VertexAdjustment removeMe)
        {
            Adjustments.Remove(removeMe);
        }


    }
}
