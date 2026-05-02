using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.Rendering;

namespace UMA
{
    public class FaceEditorStage : PreviewSceneStage
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
#endif
        }

        private static void ExitStageIfActive()
        {
#if UNITY_EDITOR
            try
            {
                if (StageUtility.GetCurrentStage() is FaceEditorStage)
                {
                    StageUtility.GoBackToPreviousStage();
                }
            }
            catch
            {
            }
#endif
        }

        private struct CachedSlotTriangle
        {
            public string slotName;
            public int slotSubmeshIndex;
            public int slotTriangleIndex;
            public int v0Slot;
            public int v1Slot;
            public int v2Slot;
        }
        public GUIContent titleContent;
        public SceneView openedSceneView;
        public GameObject selectedObject;

        public GameObject FaceObject;
        public GameObject cameraAnchor;
        private GameObject lightingObject;

        public bool NeedsCameraSetup;
        public bool closing;
        public bool hasSaved;

        public DynamicCharacterAvatar thisDCA;
        public Mesh BakedMesh;
        public MeshHideAsset CurrentHideAsset;
        public MeshHideAssetCollection CurrentHideCollection;

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

        [Serializable]
        private class SlotSelectionEntry
        {
            public string slotName;
            public bool isSelected;
        }

        private enum selectMode { Add, Remove, InvertSelection, HideFaces, UnhideFaces, ToggleHide };

        [Serializable]
        private struct SerializedSlotTriangleKey
        {
            public string slotName;
            public int slotSubmeshIndex;
            public int slotTriangleIndex;

            public SerializedSlotTriangleKey(string slotName, int slotSubmeshIndex, int slotTriangleIndex)
            {
                this.slotName = slotName;
                this.slotSubmeshIndex = slotSubmeshIndex;
                this.slotTriangleIndex = slotTriangleIndex;
            }
        }

        private struct TriangleKey : IEquatable<TriangleKey>
        {
            public int submeshIndex;
            public int triangleIndex;

            public TriangleKey(int submeshIndex, int triangleIndex)
            {
                this.submeshIndex = submeshIndex;
                this.triangleIndex = triangleIndex;
            }

            public bool Equals(TriangleKey other)
            {
                return submeshIndex == other.submeshIndex && triangleIndex == other.triangleIndex;
            }

            public override bool Equals(object obj)
            {
                if (obj is TriangleKey other)
                {
                    return Equals(other);
                }

                return false;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (submeshIndex * 397) ^ triangleIndex;
                }
            }
        }

        private struct SlotTriangleAddress
        {
            public SlotData slot;
            public int slotSubmeshIndex;
            public int slotTriangleIndex;
        }

        // Key for slot-local triangle identity (survives mesh rebuilds)
        private struct SlotTriangleKey : IEquatable<SlotTriangleKey>
        {
            public string slotName;
            public int slotSubmeshIndex;
            public int slotTriangleIndex;

            public SlotTriangleKey(string slotName, int slotSubmeshIndex, int slotTriangleIndex)
            {
                this.slotName = slotName;
                this.slotSubmeshIndex = slotSubmeshIndex;
                this.slotTriangleIndex = slotTriangleIndex;
            }

            public bool Equals(SlotTriangleKey other)
            {
                return slotSubmeshIndex == other.slotSubmeshIndex &&
                       slotTriangleIndex == other.slotTriangleIndex &&
                       string.Equals(slotName, other.slotName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is SlotTriangleKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = slotName != null ? slotName.GetHashCode() : 0;
                    hash = (hash * 397) ^ slotSubmeshIndex;
                    hash = (hash * 397) ^ slotTriangleIndex;
                    return hash;
                }
            }
        }

        // Serialized backing store for selection so Undo/Redo and domain reload restore selections.
        // In-memory authoritative source is `selectedSlotTriangles`.
        [SerializeField]
        private List<SerializedSlotTriangleKey> selectedSlotTrianglesSerialized = new List<SerializedSlotTriangleKey>();

        [SerializeField]
        private List<SlotSelectionEntry> slotSelectionEntries = new List<SlotSelectionEntry>();

        private selectMode currentMode = selectMode.HideFaces;
        private bool paintMode;
        private bool paintAddMode = true;
        private bool isPointerDown;
        private bool isPaintDragging;
        private Vector2 dragStartMousePos;
        private Rect currentDragRect;
        private bool rubberBandCullBackfaces = true;
        private const float ClickDragThreshold = 6f;

        private GUIStyle centeredLabel;
        private readonly Color rubberBandColor = new Color(0.8f, 0.8f, 0.95f, 0.15f);

        private const int FaceEditorToolsWindowID = 0x2234;
        private const int VisibleWearablesID = 0x2235;
        private const int MeshHideAssetsWindowID = 0x2236;
        private const float LeftPanelWidthMin = 320f;
        private const float LeftPanelWidthMax = 460f;
        private const float LeftPanelPadding = 6f;
        private const float LeftPanelHeaderHeight = 18f;

        public Vector2 FaceEditorScrollLocation = Vector2.zero;
        public Rect FaceEditorToolsWindow = new Rect(10, 10, 300, 280);

        public Vector2 VisibleWearablesLocation = Vector2.zero;
        public Rect VisibleWearablesWindow = new Rect(10, 300, 250, 320);

        public Vector2 MeshHideAssetsScrollLocation = Vector2.zero;
        public Rect MeshHideAssetsWindow = new Rect(10, 630, 250, 260);

        public Rect FaceEditorToolsCollapsedWindow;
        public Rect VisibleWearablesCollapsedWindow;
        public Rect MeshHideAssetsCollapsedWindow;

        private Rect leftPanelRect;
        private Vector2 lastSceneViewSize = Vector2.zero;
        private float cachedVisibilityHeight = -1f;
        private float cachedMeshHideAssetsHeight = 260f;

        private const string PanelCollapsePrefKeyPrefix = "UMA.FaceEditorStage.PanelCollapse.";
        [SerializeField]
        private bool faceToolsPanelCollapsed;
        [SerializeField]
        private bool visibilityPanelCollapsed;
        [SerializeField]
        private bool meshHideAssetsPanelCollapsed;
        private bool collapsePrefsLoaded;
        private const float CollapsedWindowHeight = 26f;

        private string[] selectFrom = new string[] { "All Slots" };
        private int selectionSlot = 0;
        private string[] visibleSelectFrom = new string[] { "All Slots" };

        private Dictionary<string, bool> originalSlotSuppressed;
        private Dictionary<string, bool> originalWearableDisabled;

        private MeshCollider meshCollider;

        private static Material overlayMaterial;
        private static Material overlayLineMaterial;

        private Mesh overlayVisibleFillMesh;
        private Mesh overlayVisibleLineMesh;
        private Mesh overlayHiddenFillMesh;
        private Mesh overlayHiddenLineMesh;
        private bool overlayMeshDirty = true;
        private Mesh overlayCacheSourceMesh;
        private int overlayCacheSelectionVersion = -1;
        private int overlayCacheStartSubmesh = -1;
        private int overlayCacheSubmeshCount = -1;

        private Dictionary<string, SlotData> slotLookupByName = new Dictionary<string, SlotData>(StringComparer.Ordinal);
        private Dictionary<TriangleKey, SlotTriangleAddress> triangleSlotOwnership = new Dictionary<TriangleKey, SlotTriangleAddress>();
        // Reverse lookup: slot-local key -> current baked key (rebuilt when mesh changes)
        private Dictionary<SlotTriangleKey, TriangleKey> slotLocalToBaked = new Dictionary<SlotTriangleKey, TriangleKey>();

        private readonly List<(int start, int endExclusive, SlotData slot)> bakedVertexSlotRanges = new List<(int start, int endExclusive, SlotData slot)>(64);

        private int visibleSlotsSignature;
        private int visibleSlotsCount;

        private bool slotTriangleCacheBuilt;
        private readonly List<CachedSlotTriangle> slotTriangleCache = new List<CachedSlotTriangle>(4096);
        private readonly HashSet<SlotTriangleKey> selectedSlotTriangles = new HashSet<SlotTriangleKey>();
        private int selectionVersion;

        private int SelectedSlotTriangleCount
        {
            get
            {
                return selectedSlotTriangles != null ? selectedSlotTriangles.Count : 0;
            }
        }

        private void RebuildSelectedSlotTrianglesFromSerialized()
        {
            selectedSlotTriangles.Clear();
            if (selectedSlotTrianglesSerialized == null || selectedSlotTrianglesSerialized.Count == 0)
            {
                return;
            }

            for (int i = 0; i < selectedSlotTrianglesSerialized.Count; i++)
            {
                var k = selectedSlotTrianglesSerialized[i];
                if (string.IsNullOrEmpty(k.slotName))
                {
                    continue;
                }
                selectedSlotTriangles.Add(new SlotTriangleKey(k.slotName, k.slotSubmeshIndex, k.slotTriangleIndex));
            }
        }

        private bool AddSelectedSlotTriangle(SlotTriangleKey key)
        {
            if (!selectedSlotTriangles.Add(key))
            {
                return false;
            }
            selectedSlotTrianglesSerialized.Add(new SerializedSlotTriangleKey(key.slotName, key.slotSubmeshIndex, key.slotTriangleIndex));
            return true;
        }

        private bool RemoveSelectedSlotTriangle(SlotTriangleKey key)
        {
            if (!selectedSlotTriangles.Remove(key))
            {
                return false;
            }

            if (selectedSlotTrianglesSerialized != null)
            {
                for (int i = selectedSlotTrianglesSerialized.Count - 1; i >= 0; i--)
                {
                    var k = selectedSlotTrianglesSerialized[i];
                    if (string.Equals(k.slotName, key.slotName, StringComparison.Ordinal) && k.slotSubmeshIndex == key.slotSubmeshIndex && k.slotTriangleIndex == key.slotTriangleIndex)
                    {
                        selectedSlotTrianglesSerialized.RemoveAt(i);
                        break;
                    }
                }
            }

            return true;
        }

        private static readonly Color OverlayFillGreen = new Color(0f, 1f, 0f, 0.33f);
        private static readonly Color OverlayLineGreen = new Color(0f, 1f, 0f, 1f);
        private static readonly Color OverlayFillRed = new Color(1f, 0f, 0f, 1f);
        private static readonly Color OverlayLineRed = new Color(0f, 0f, 0f, 1f);
        private static readonly Color OverlayLineBlack = new Color(0f, 0f, 0f, 1f);
        private const float OverlayVertexOffset = 0.0005f;

        private const string MeshHideAssetFolderPrefKeyPrefix = "UMA.FaceEditorStage.MeshHideAssetFolder.";

        private const string RaycastPrefKeyPrefix = "UMA.FaceEditorStage.RaycastOcclusion.";
        private const float RaycastDefaultOutward = 0.1f;
        private const float RaycastDefaultInward = 0.02f;
        private const float RaycastOriginEpsilon = 0.0005f;
        private const float RaycastHitEpsilon = 0.0001f;

        [SerializeField]
        private float raycastOcclusionOutward = RaycastDefaultOutward;
        [SerializeField]
        private float raycastOcclusionInward = RaycastDefaultInward;
        [SerializeField]
        private bool raycastOcclusionAdd = false;
        [SerializeField]
        private MeshHideAsset.TriangleHideStrategy raycastOcclusionStrategy = MeshHideAsset.TriangleHideStrategy.Conservative;
        [SerializeField]
        private string raycastOcclusionStatus;
        [SerializeField]
        private MessageType raycastOcclusionStatusType = MessageType.Info;

        [SerializeField]
        private bool raycastTestMode;
        [SerializeField]
        private string raycastTestSourceSlot;
        [SerializeField]
        private string raycastTestOccluderSlot;
        [SerializeField]
        private int raycastTestSlotVertexIndex;
        [SerializeField]
        private string raycastTestStatus;
        [SerializeField]
        private MessageType raycastTestStatusType = MessageType.Info;

        [SerializeField]
        private bool raycastDrawDebugRays;

        [SerializeField]
        private int raycastDebugRayCount = 2048;

        private struct DebugRay
        {
            public Vector3 origin;
            public Vector3 direction;
            public float distance;
            public Color color;
        }

        private readonly List<DebugRay> raycastDebugRays = new List<DebugRay>(2048);
        private int raycastDebugRaysAdded;

        [Serializable]
        private class SubmeshColorEntry
        {
            public string key;
            public Color color;
        }

        // Persist colors across rebuilds even if submesh order changes.
        [SerializeField]
        private List<SubmeshColorEntry> submeshColorEntries = new List<SubmeshColorEntry>();
        private Dictionary<string, Color> submeshColorCache;
        private int nextFallbackColorIndex;

        private readonly Color[] defaultColors = new Color[]
        {
            new Color(1.0f, 0.9f, 0.9f, 1.0f),
            new Color(0.9f, 1.0f, 0.9f, 1.0f),
            new Color(0.9f, 0.9f, 1.0f, 1.0f),
            new Color(1.0f, 1.0f, 0.9f, 1.0f),
            new Color(0.9f, 1.0f, 1.0f, 1.0f),
            new Color(1.0f, 0.9f, 1.0f, 1.0f)
        };

        public static FaceEditorStage ShowStage(DynamicCharacterAvatar DCA, MeshHideAsset hideAsset)
        {
            FaceEditorStage stage = ScriptableObject.CreateInstance<FaceEditorStage>();
            stage.titleContent = new GUIContent();
            stage.titleContent.text = "Mesh Hide Editor";
            stage.titleContent.image = EditorGUIUtility.IconContent("Mesh Icon").image;
            stage.thisDCA = DCA;
            stage.CurrentHideAsset = hideAsset;
            StageUtility.GoToStage(stage, true);
            return stage;
        }

        public static FaceEditorStage ShowStage(DynamicCharacterAvatar DCA, MeshHideAssetCollection hideCollection)
        {
            FaceEditorStage stage = ScriptableObject.CreateInstance<FaceEditorStage>();
            stage.titleContent = new GUIContent();
            stage.titleContent.text = "Mesh Hide Editor";
            stage.titleContent.image = EditorGUIUtility.IconContent("Mesh Icon").image;
            stage.thisDCA = DCA;
            stage.CurrentHideCollection = hideCollection;
            StageUtility.GoToStage(stage, true);
            return stage;
        }

        protected override GUIContent CreateHeaderContent()
        {
            return new GUIContent("Face Editor");
        }

        protected override bool OnOpenStage()
        {
            base.OnOpenStage();

            EnsureEditorEvents();

            centeredLabel = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            lightingObject = new GameObject("Directional Light");
            lightingObject.transform.rotation = Quaternion.Euler(50, 330, 0);
            lightingObject.AddComponent<Light>().type = LightType.Directional;

            SkinnedMeshRenderer smr = thisDCA != null ? thisDCA.gameObject.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (smr == null)
            {
                return false;
            }

            BakedMesh = new Mesh();
            BakedMesh.name = "BakedMesh";
            smr.BakeMesh(BakedMesh, true);
            RefreshBakedMeshCaches();

            GameObject go = new GameObject("FaceEditor");
            go.AddComponent<MeshFilter>().sharedMesh = BakedMesh;
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = go.AddComponent<MeshRenderer>();
            }

            renderer.sharedMaterials = new Material[BakedMesh.subMeshCount];
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            meshCollider = go.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = BakedMesh;

            go.SetActive(true);
            smr.enabled = false;

            FaceObject = go;

            SetFaceMaterialColors(go);

            cameraAnchor = new GameObject("CameraAnchor");
            cameraAnchor.transform.position = new Vector3(0, 1, 2.5f);
            cameraAnchor.transform.rotation = Quaternion.Euler(0, 180, 0);

            SceneManager.MoveGameObjectToScene(FaceObject, scene);
            SceneManager.MoveGameObjectToScene(lightingObject, scene);
            SceneManager.MoveGameObjectToScene(cameraAnchor, scene);

            Tools.hidden = true;
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedoSelection;
            NeedsCameraSetup = true;
            cachedVisibilityHeight = -1f;

            RefreshVisibleSlotLists();
            RefreshSlotSelectionEntries();
            RebuildTriangleSlotOwnership();

            CacheOriginalSlotVisibility();
            CacheOriginalWearableVisibility();

            ValidateMeshHideAssets();
            LoadSelections();
            MarkOverlayMeshDirty();

            return true;
        }

        private void CacheOriginalWearableVisibility()
        {
            originalWearableDisabled = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (thisDCA == null)
            {
                return;
            }

            var wearables = thisDCA.GetVisibleWearables();
            if (wearables == null)
            {
                return;
            }

            foreach (var w in wearables)
            {
                if (w == null || string.IsNullOrEmpty(w.name))
                {
                    continue;
                }

                if (!originalWearableDisabled.ContainsKey(w.name))
                {
                    originalWearableDisabled.Add(w.name, w.disabled);
                }
            }
        }

        private void RestoreOriginalWearableVisibility()
        {
            if (originalWearableDisabled == null || originalWearableDisabled.Count == 0)
            {
                return;
            }
            if (thisDCA == null)
            {
                return;
            }

            bool changed = false;
            var wearables = thisDCA.GetVisibleWearables();
            if (wearables == null)
            {
                return;
            }

            foreach (var w in wearables)
            {
                if (w == null || string.IsNullOrEmpty(w.name))
                {
                    continue;
                }

                if (originalWearableDisabled.TryGetValue(w.name, out bool disabled) && w.disabled != disabled)
                {
                    w.disabled = disabled;
                    changed = true;
                }
            }

            if (changed)
            {
                RebuildMesh(true, true);
            }
        }

        private void CacheOriginalSlotVisibility()
        {
            originalSlotSuppressed = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return;
            }

            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s == null || string.IsNullOrEmpty(s.slotName))
                {
                    continue;
                }
                if (!originalSlotSuppressed.ContainsKey(s.slotName))
                {
                    originalSlotSuppressed.Add(s.slotName, s.Suppressed);
                }
            }
        }

        private void RestoreOriginalSlotVisibility()
        {
            if (originalSlotSuppressed == null || originalSlotSuppressed.Count == 0)
            {
                return;
            }
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return;
            }

            bool changed = false;
            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s == null || string.IsNullOrEmpty(s.slotName))
                {
                    continue;
                }

                if (originalSlotSuppressed.TryGetValue(s.slotName, out bool suppressed) && s.Suppressed != suppressed)
                {
                    s.Suppressed = suppressed;
                    changed = true;
                }
            }

            if (changed)
            {
                RebuildMesh(true, true);
            }
        }

        private void ValidateMeshHideAssets()
        {
#if UNITY_EDITOR
            if (CurrentHideAsset != null)
            {
                ValidateMeshHideAsset(CurrentHideAsset);
                return;
            }

            if (CurrentHideCollection != null && CurrentHideCollection.Assets != null)
            {
                for (int i = 0; i < CurrentHideCollection.Assets.Count; i++)
                {
                    var mha = CurrentHideCollection.Assets[i];
                    if (mha == null) continue;
                    ValidateMeshHideAsset(mha);
                }
            }
#endif
        }

        private void ValidateMeshHideAsset(MeshHideAsset mha)
        {
#if UNITY_EDITOR
            if (mha == null)
            {
                return;
            }

            if (!mha.NeedsRebuildFromUV())
            {
                return;
            }

            mha.RebuildFlagsFromEditorUVMask();
            EditorUtility.SetDirty(mha);
            AssetDatabase.SaveAssetIfDirty(mha);
#endif
        }

        private void SetFaceMaterialColors(GameObject faceObject)
        {
            MeshRenderer mr = faceObject != null ? faceObject.GetComponent<MeshRenderer>() : null;
            if (mr == null)
            {
                return;
            }

            EnsureSubmeshColorCache();

            List<Material> newMaterials = new List<Material>();
            for (int i = 0; i < mr.sharedMaterials.Length; i++)
            {
                Color color = GetSubmeshColor(i);
                Material mat = UMAUtils.GetDefaultDiffuseMaterial();
                if (mat != null)
                {
                    mat.SetColor("_Color", color);
                    newMaterials.Add(mat);
                }
            }

            if (newMaterials.Count > 0)
            {
                mr.sharedMaterials = newMaterials.ToArray();
            }
        }

        private void EnsureSubmeshColorCache()
        {
            if (submeshColorCache != null)
            {
                return;
            }

            submeshColorCache = new Dictionary<string, Color>(StringComparer.Ordinal);
            if (submeshColorEntries != null)
            {
                for (int i = 0; i < submeshColorEntries.Count; i++)
                {
                    var e = submeshColorEntries[i];
                    if (e == null || string.IsNullOrEmpty(e.key)) continue;
                    if (!submeshColorCache.ContainsKey(e.key))
                    {
                        submeshColorCache.Add(e.key, e.color);
                    }
                }
            }

            nextFallbackColorIndex = 0;
        }

        private Color GetSubmeshColor(int submeshIndex)
        {
            string key = GetSubmeshColorKey(submeshIndex);
            if (!string.IsNullOrEmpty(key) && submeshColorCache.TryGetValue(key, out var col))
            {
                return col;
            }

            // Assign a new stable color.
            Color newColor = defaultColors[nextFallbackColorIndex % defaultColors.Length];
            nextFallbackColorIndex++;

            if (string.IsNullOrEmpty(key))
            {
                // If we can't determine a stable key, fall back to index-based key.
                key = "submesh:" + submeshIndex;
            }

            submeshColorCache[key] = newColor;
            if (submeshColorEntries == null)
            {
                submeshColorEntries = new List<SubmeshColorEntry>();
            }
            submeshColorEntries.Add(new SubmeshColorEntry { key = key, color = newColor });
            return newColor;
        }

        private string GetSubmeshColorKey(int submeshIndex)
        {
            // Best-effort stable identity: Slot name + submesh index within that slot.
            // If we can't resolve, return null and we'll fall back to index-based key.
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return null;
            }

            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            int running = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;

                // Skip slots that don't contribute to baked mesh
                if (slot.Suppressed) continue;

                var asset = slot.asset;
                if (asset == null || UMAMeshData.IsNullOrEmptyMeshData(asset.meshData)) continue;

                int slotSubmeshCount = Mathf.Max(1, asset.meshData.subMeshCount);

                if (submeshIndex >= running && submeshIndex < running + slotSubmeshCount)
                {
                    int localSubmesh = submeshIndex - running;
                    return "slot:" + slot.slotName + "#" + localSubmesh;
                }
                running += slotSubmeshCount;
            }

            return null;
        }

        protected override void OnCloseStage()
        {
            base.OnCloseStage();
            closing = true;

            RestoreOriginalSlotVisibility();
            RestoreOriginalWearableVisibility();

            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedoSelection;
            Tools.hidden = false;

            if (thisDCA != null)
            {
                var smr = thisDCA.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null)
                {
                    smr.enabled = true;
                }
                thisDCA.ignoreMeshHideAssets = false;
                if (thisDCA.editorTimeGeneration)
                {
                    thisDCA.GenerateSingleUMA();
                }
            }

            if (FaceObject != null)
            {
                DestroyImmediate(FaceObject);
                FaceObject = null;
            }

            if (lightingObject != null)
            {
                DestroyImmediate(lightingObject);
                lightingObject = null;
            }

            if (cameraAnchor != null)
            {
                DestroyImmediate(cameraAnchor);
                cameraAnchor = null;
            }

            if (BakedMesh != null)
            {
                DestroyImmediate(BakedMesh);
                BakedMesh = null;
            }

            RefreshBakedMeshCaches();

            ClearOverlayMeshCache();

            originalSlotSuppressed = null;
            originalWearableDisabled = null;

        }

        private void OnUndoRedoSelection()
        {
            RebuildSelectedSlotTrianglesFromSerialized();
            selectionVersion++;
            MarkOverlayMeshDirty();
            SceneView.RepaintAll();
        }

        private void OnSceneGUI(SceneView view)
        {
            if (closing)
            {
                return;
            }

            openedSceneView = view;
            DoSceneGUI(view);
        }

        private void DoSceneGUI(SceneView sceneView)
        {
            if (NeedsCameraSetup)
            {
                SetupCamera(sceneView);
                NeedsCameraSetup = false;
            }

            Handles.BeginGUI();
            DrawGUIWindows(sceneView);
            Handles.EndGUI();

            DrawRaycastDebugRaysHandles(sceneView);

            DrawMeshHideOverlay(sceneView);
            HandleFacePick(Event.current, sceneView);
        }

        private void DrawRaycastDebugRaysHandles(SceneView sceneView)
        {
            if (!raycastDrawDebugRays)
            {
                return;
            }
            if (raycastDebugRays == null || raycastDebugRays.Count == 0)
            {
                return;
            }

            for (int i = 0; i < raycastDebugRays.Count; i++)
            {
                var r = raycastDebugRays[i];
                Handles.color = r.color;
                Handles.DrawLine(r.origin, r.origin + (r.direction * r.distance));
            }
        }

        private void DrawMeshHideOverlay(SceneView sceneView)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (FaceObject == null || BakedMesh == null)
            {
                return;
            }

            EnsureSlotTriangleCacheBuilt();

            int startSubmesh = 0;
            int endSubmeshExclusive = BakedMesh.subMeshCount;
            if (endSubmeshExclusive <= 0)
            {
                return;
            }

            EnsureOverlayMaterials();

            if (overlayMeshDirty || overlayCacheSourceMesh != BakedMesh || overlayCacheSelectionVersion != selectionVersion || overlayCacheStartSubmesh != startSubmesh || overlayCacheSubmeshCount != (endSubmeshExclusive - startSubmesh))
            {
                RebuildOverlayMeshes(startSubmesh, endSubmeshExclusive);
            }

            Matrix4x4 matrix = FaceObject.transform.localToWorldMatrix;

            if (overlayVisibleLineMesh != null && overlayVisibleLineMesh.vertexCount > 0)
            {
                overlayLineMaterial.SetColor("_Color", OverlayLineBlack);
                overlayLineMaterial.SetPass(0);
                Graphics.DrawMesh(
                    overlayVisibleLineMesh,
                    matrix,
                    overlayLineMaterial,
                    0,
                    sceneView != null ? sceneView.camera : null,
                    0,
                    null,
                    ShadowCastingMode.Off,
                    false,
                    null,
                    false);
            }

            if (overlayHiddenFillMesh != null && overlayHiddenFillMesh.vertexCount > 0)
            {
                overlayMaterial.SetColor("_Color", OverlayFillRed);
                overlayMaterial.SetPass(0);
                Graphics.DrawMesh(
                    overlayHiddenFillMesh,
                    matrix,
                    overlayMaterial,
                    0,
                    sceneView != null ? sceneView.camera : null,
                    0,
                    null,
                    ShadowCastingMode.Off,
                    false,
                    null,
                    false);
            }

            if (overlayHiddenLineMesh != null && overlayHiddenLineMesh.vertexCount > 0)
            {
                overlayLineMaterial.SetColor("_Color", OverlayLineBlack);
                overlayLineMaterial.SetPass(0);
                Graphics.DrawMesh(
                    overlayHiddenLineMesh,
                    matrix,
                    overlayLineMaterial,
                    0,
                    sceneView != null ? sceneView.camera : null,
                    0,
                    null,
                    ShadowCastingMode.Off,
                    false,
                    null,
                    false);
            }
        }

        private void EnsureSlotTriangleCacheBuilt()
        {
            if (slotTriangleCacheBuilt)
            {
                return;
            }

            slotTriangleCacheBuilt = true;
            slotTriangleCache.Clear();

            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return;
            }

            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                {
                    continue;
                }

                int submeshCount = Mathf.Max(1, slot.asset.meshData.subMeshCount);
                for (int sm = 0; sm < submeshCount; sm++)
                {
                    int[] tris = null;
                    try
                    {
                        tris = slot.asset.meshData.submeshes[sm].GetBaseTriangles();
                    }
                    catch
                    {
                        tris = null;
                    }
                    if (tris == null || tris.Length == 0)
                    {
                        continue;
                    }

                    int triCount = tris.Length / 3;
                    for (int t = 0; t < triCount; t++)
                    {
                        int ti = t * 3;
                        slotTriangleCache.Add(new CachedSlotTriangle
                        {
                            slotName = slot.slotName,
                            slotSubmeshIndex = sm,
                            slotTriangleIndex = t,
                            v0Slot = tris[ti],
                            v1Slot = tris[ti + 1],
                            v2Slot = tris[ti + 2]
                        });
                    }
                }
            }
        }

        private void RebuildOverlayMeshes(int startSubmesh, int endSubmeshExclusive)
        {
            EnsureOverlayMesh(ref overlayVisibleFillMesh, "OverlayVisibleFill");
            EnsureOverlayMesh(ref overlayVisibleLineMesh, "OverlayVisibleLine");
            EnsureOverlayMesh(ref overlayHiddenFillMesh, "OverlayHiddenFill");
            EnsureOverlayMesh(ref overlayHiddenLineMesh, "OverlayHiddenLine");

            List<Vector3> visibleFillVertices = new List<Vector3>(1024);
            List<Vector3> visibleLineVertices = new List<Vector3>(2048);
            List<Vector3> hiddenFillVertices = new List<Vector3>(1024);
            List<Vector3> hiddenLineVertices = new List<Vector3>(2048);

            RefreshBakedMeshCaches();
            Vector3[] bakedVertices = this.bakedVertices;
            if (bakedVertices == null)
            {
                return;
            }
            for (int i = 0; i < slotTriangleCache.Count; i++)
            {
                var tri = slotTriangleCache[i];
                if (string.IsNullOrEmpty(tri.slotName))
                {
                    continue;
                }

                if (!slotLookupByName.TryGetValue(tri.slotName, out var slot) || slot == null)
                {
                    continue;
                }
                if (slot.Suppressed)
                {
                    continue;
                }

                // Per requirement: if slot is not selectable, do not draw any wireframe/overlay.
                if (!IsSlotSelected(tri.slotName))
                {
                    continue;
                }

                int v0b = slot.vertexOffset + tri.v0Slot;
                int v1b = slot.vertexOffset + tri.v1Slot;
                int v2b = slot.vertexOffset + tri.v2Slot;
                if (v0b < 0 || v1b < 0 || v2b < 0 || v0b >= bakedVertices.Length || v1b >= bakedVertices.Length || v2b >= bakedVertices.Length)
                {
                    continue;
                }

                Vector3 v0 = bakedVertices[v0b];
                Vector3 v1 = bakedVertices[v1b];
                Vector3 v2 = bakedVertices[v2b];

                bool isHidden = selectedSlotTriangles.Contains(new SlotTriangleKey(tri.slotName, tri.slotSubmeshIndex, tri.slotTriangleIndex));

                    Vector3 n = Vector3.Cross(v1 - v0, v2 - v0);
                    if (n.sqrMagnitude > 1e-12f)
                    {
                        n.Normalize();
                        Vector3 offset = n * OverlayVertexOffset;
                        v0 += offset;
                        v1 += offset;
                        v2 += offset;
                    }

                if (isHidden)
                {
                    hiddenFillVertices.Add(v0);
                    hiddenFillVertices.Add(v1);
                    hiddenFillVertices.Add(v2);

                    hiddenLineVertices.Add(v0);
                    hiddenLineVertices.Add(v1);
                    hiddenLineVertices.Add(v1);
                    hiddenLineVertices.Add(v2);
                    hiddenLineVertices.Add(v2);
                    hiddenLineVertices.Add(v0);
                }
                else
                {
                    visibleLineVertices.Add(v0);
                    visibleLineVertices.Add(v1);
                    visibleLineVertices.Add(v1);
                    visibleLineVertices.Add(v2);
                    visibleLineVertices.Add(v2);
                    visibleLineVertices.Add(v0);
                }
            }

            ApplyTriangleMesh(overlayVisibleFillMesh, visibleFillVertices);
            ApplyTriangleMesh(overlayHiddenFillMesh, hiddenFillVertices);
            ApplyLineMesh(overlayVisibleLineMesh, visibleLineVertices);
            ApplyLineMesh(overlayHiddenLineMesh, hiddenLineVertices);

            overlayCacheSourceMesh = BakedMesh;
            overlayCacheSelectionVersion = selectionVersion;
            overlayCacheStartSubmesh = startSubmesh;
            overlayCacheSubmeshCount = endSubmeshExclusive - startSubmesh;
            overlayMeshDirty = false;
        }

        private static void ApplyTriangleMesh(Mesh mesh, List<Vector3> vertices)
        {
            mesh.Clear();
            mesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);

            int[] indices = new int[vertices.Count];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static void ApplyLineMesh(Mesh mesh, List<Vector3> vertices)
        {
            mesh.Clear();
            mesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);

            int[] indices = new int[vertices.Count];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }
            mesh.SetIndices(indices, MeshTopology.Lines, 0, false);
            mesh.RecalculateBounds();
        }

        private static void EnsureOverlayMesh(ref Mesh mesh, string name)
        {
            if (mesh != null)
            {
                return;
            }

            mesh = new Mesh();
            mesh.name = name;
            mesh.hideFlags = HideFlags.HideAndDontSave;
        }

        private void MarkOverlayMeshDirty()
        {
            overlayMeshDirty = true;
            overlayCacheSourceMesh = null;
            overlayCacheSelectionVersion = -1;
            overlayCacheStartSubmesh = -1;
            overlayCacheSubmeshCount = -1;
        }

        private void ClearOverlayMeshCache()
        {
            DestroyOverlayMesh(ref overlayVisibleFillMesh);
            DestroyOverlayMesh(ref overlayVisibleLineMesh);
            DestroyOverlayMesh(ref overlayHiddenFillMesh);
            DestroyOverlayMesh(ref overlayHiddenLineMesh);
            MarkOverlayMeshDirty();
        }

        private static void DestroyOverlayMesh(ref Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            DestroyImmediate(mesh);
            mesh = null;
        }

        private static void EnsureOverlayMaterials()
        {
            if (overlayMaterial == null)
            {
                Shader s = Shader.Find("UMA/Diffuse");
                overlayMaterial = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
                if (overlayMaterial.HasProperty("_BaseMap"))
                {
                    overlayMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
                }
                if (overlayMaterial.HasProperty("_ColorModulation"))
                {
                    overlayMaterial.SetFloat("_ColorModulation", 1f);
                }
            }
            if (overlayLineMaterial == null)
            {
                Shader s = Shader.Find("UMA/Diffuse");
                overlayLineMaterial = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
                if (overlayLineMaterial.HasProperty("_BaseMap"))
                {
                    overlayLineMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
                }
                if (overlayLineMaterial.HasProperty("_ColorModulation"))
                {
                    overlayLineMaterial.SetFloat("_ColorModulation", 1f);
                }
            }
        }

        private SlotData GetCurrentHideSlot()
        {
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return null;
            }

            string slotName = CurrentHideAsset != null ? CurrentHideAsset.AssetSlotName : null;
            if (string.IsNullOrEmpty(slotName))
            {
                return null;
            }

            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;
                if (slot.slotName == slotName)
                {
                    return slot;
                }
            }
            return null;
        }

        private bool TryGetSlotSubmeshRange(SlotData slot, out int startSubmesh, out int submeshCount)
        {
            startSubmesh = 0;
            submeshCount = 0;
            if (slot == null || thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null)
            {
                return false;
            }

            // Target slot must have valid mesh data to contribute submeshes
            var asset = slot.asset;
            if (asset == null || UMAMeshData.IsNullOrEmptyMeshData(asset.meshData))
            {
                return false;
            }
            int count = Mathf.Max(1, asset.meshData.subMeshCount);

            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            int running = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s == null) continue;

                // Skip slots that don't contribute to baked mesh:
                // 1. Suppressed slots are hidden
                if (s.Suppressed) continue;

                // 2. Slots without valid mesh data (including utility slots)
                var sAsset = s.asset;
                if (sAsset == null || UMAMeshData.IsNullOrEmptyMeshData(sAsset.meshData)) continue;

                int sCount = Mathf.Max(1, sAsset.meshData.subMeshCount);

                if (s == slot)
                {
                    startSubmesh = running;
                    submeshCount = count;
                    return true;
                }

                running += sCount;
            }

            return false;
        }

        private static BitArray GetTriangleFlagsSafe(MeshHideAsset mha, int localSubmesh)
        {
            if (mha == null || mha.triangleFlags == null)
            {
                return null;
            }
            if (localSubmesh < 0 || localSubmesh >= mha.triangleFlags.Length)
            {
                return null;
            }
            return mha.triangleFlags[localSubmesh];
        }

        private void SetupCamera(SceneView view)
        {
            if (cameraAnchor == null)
            {
                return;
            }

            view.LookAtDirect(cameraAnchor.transform.position, cameraAnchor.transform.rotation, 2.5f);
            view.Repaint();
        }

        private void DrawGUIWindows(SceneView sceneView)
        {
            AdjustWindowRects();

            DrawPanel(faceToolsPanelCollapsed ? FaceEditorToolsCollapsedWindow : FaceEditorToolsWindow, "Face Tools", ref faceToolsPanelCollapsed, PanelCollapsePrefKeyPrefix + "FaceTools", () => DoToolsWindow(0));
            DrawPanel(visibilityPanelCollapsed ? VisibleWearablesCollapsedWindow : VisibleWearablesWindow, "Visibility", ref visibilityPanelCollapsed, PanelCollapsePrefKeyPrefix + "Visibility", () => DoVisibilityWindow(0));
            DrawPanel(meshHideAssetsPanelCollapsed ? MeshHideAssetsCollapsedWindow : MeshHideAssetsWindow, "Mesh Hide Assets", ref meshHideAssetsPanelCollapsed, PanelCollapsePrefKeyPrefix + "MeshHideAssets", () => DoMeshHideAssetsWindow(0));
        }

        private void DrawPanel(Rect rect, string title, ref bool collapsed, string prefsKey, Action drawContent)
        {
            // Simple, non-draggable panel.
            GUI.Box(rect, GUIContent.none);

            Rect headerRect = new Rect(rect.x, rect.y, rect.width, LeftPanelHeaderHeight + 6f);
            Rect buttonRect = new Rect(headerRect.x + 4f, headerRect.y + 3f, 22f, 18f);
            string arrow = collapsed ? "\u25BC" : "\u25B2";
            if (GUI.Button(buttonRect, arrow, EditorStyles.miniButton))
            {
                collapsed = !collapsed;
                EditorPrefs.SetBool(prefsKey, collapsed);
            }

            const float rightButtonWidth = 52f;
            Rect rightButtonRect = new Rect(headerRect.xMax - rightButtonWidth - 4f, headerRect.y + 3f, rightButtonWidth, 18f);
            bool showClose = string.Equals(prefsKey, PanelCollapsePrefKeyPrefix + "FaceTools", StringComparison.Ordinal);
            if (showClose)
            {
                if (GUI.Button(rightButtonRect, "Close", EditorStyles.miniButton))
                {
                    StageUtility.GoBackToPreviousStage();
                }
            }

            float rightInset = showClose ? (rightButtonWidth + 8f) : 4f;
            GUI.Label(new Rect(headerRect.x + 30f, headerRect.y + 4f, headerRect.width - 34f - rightInset, headerRect.height), title, EditorStyles.boldLabel);

            if (collapsed)
            {
                return;
            }

            Rect contentRect = new Rect(rect.x + 6f, headerRect.yMax + 2f, rect.width - 12f, Mathf.Max(0f, rect.yMax - (headerRect.yMax + 6f)));
            bool began = false;
            try
            {
                GUILayout.BeginArea(contentRect);
                began = true;
                drawContent?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                if (began)
                {
                    GUILayout.EndArea();
                }
            }
        }

        public void AdjustWindowRects()
        {
            if (openedSceneView == null)
            {
                return;
            }

            LoadCollapsePrefsIfNeeded();

            Rect usableRect = GetSceneViewUsableRect(openedSceneView);
            Vector2 sceneSize = usableRect.size;
            if (sceneSize != lastSceneViewSize)
            {
                lastSceneViewSize = sceneSize;
                cachedVisibilityHeight = -1f;
            }

            const float toolsMinExpanded = 180f;
            const float visibilityMinExpanded = 160f;
            const float meshHideMinExpanded = 200f;

            float width = Mathf.Clamp(FaceEditorToolsWindow.width, LeftPanelWidthMin, LeftPanelWidthMax);

            // Total available vertical space inside the usable SceneView rect for our stacked panels,
            // excluding the outer padding.
            // Unity's reported usable rect can be slightly optimistic when toolbars/insets are present,
            // so reserve a small buffer to reduce off-screen overlap.
            const float screenBufferPct = 0.08f;
            float availableStackHeight = Mathf.Max(0f, (usableRect.height * (1f - screenBufferPct)) - (LeftPanelPadding * 2f));

            // How much vertical padding between panels.
            const float betweenPanels = LeftPanelPadding;
            float separatorsHeight = betweenPanels * 2f;

            float collapsedToolsH = faceToolsPanelCollapsed ? CollapsedWindowHeight : 0f;
            float collapsedVisibilityH = visibilityPanelCollapsed ? CollapsedWindowHeight : 0f;
            float collapsedMeshHideH = meshHideAssetsPanelCollapsed ? CollapsedWindowHeight : 0f;
            float totalCollapsed = collapsedToolsH + collapsedVisibilityH + collapsedMeshHideH;

            float remainingForExpanded = Mathf.Max(0f, availableStackHeight - separatorsHeight - totalCollapsed);

            int expandedCount = 0;
            if (!faceToolsPanelCollapsed) expandedCount++;
            if (!visibilityPanelCollapsed) expandedCount++;
            if (!meshHideAssetsPanelCollapsed) expandedCount++;

            float each = expandedCount > 0 ? (remainingForExpanded / expandedCount) : 0f;

            float toolsExpandedHeight = faceToolsPanelCollapsed ? 0f : Mathf.Max(toolsMinExpanded, each);
            float visibilityExpandedHeight = visibilityPanelCollapsed ? 0f : Mathf.Max(visibilityMinExpanded, each);
            float meshHideExpandedHeight = meshHideAssetsPanelCollapsed ? 0f : Mathf.Max(meshHideMinExpanded, each);

            // If mins caused us to exceed available space, scale down only expanded panels (respecting mins best-effort).
            float expandedSumWithMins = (faceToolsPanelCollapsed ? 0f : toolsExpandedHeight) +
                                       (visibilityPanelCollapsed ? 0f : visibilityExpandedHeight) +
                                       (meshHideAssetsPanelCollapsed ? 0f : meshHideExpandedHeight);
            float overflow = expandedSumWithMins - remainingForExpanded;
            if (overflow > 0.01f)
            {
                // Recompute with a uniform scale factor but don't go below mins.
                float scale = remainingForExpanded / Mathf.Max(1f, expandedSumWithMins);
                if (!faceToolsPanelCollapsed)
                {
                    toolsExpandedHeight = Mathf.Max(toolsMinExpanded, toolsExpandedHeight * scale);
                }
                if (!visibilityPanelCollapsed)
                {
                    visibilityExpandedHeight = Mathf.Max(visibilityMinExpanded, visibilityExpandedHeight * scale);
                }
                if (!meshHideAssetsPanelCollapsed)
                {
                    meshHideExpandedHeight = Mathf.Max(meshHideMinExpanded, meshHideExpandedHeight * scale);
                }
            }

            float toolsActualHeight = faceToolsPanelCollapsed ? CollapsedWindowHeight : toolsExpandedHeight;
            float visibilityActualHeight = visibilityPanelCollapsed ? CollapsedWindowHeight : visibilityExpandedHeight;
            float meshHideActualHeight = meshHideAssetsPanelCollapsed ? CollapsedWindowHeight : meshHideExpandedHeight;

            leftPanelRect = new Rect(usableRect.x + LeftPanelPadding, usableRect.y + LeftPanelPadding, width,
                toolsActualHeight + visibilityActualHeight + meshHideActualHeight + separatorsHeight);

            float y = leftPanelRect.y;
            FaceEditorToolsWindow = new Rect(leftPanelRect.x, y, width, toolsExpandedHeight);
            FaceEditorToolsCollapsedWindow = new Rect(leftPanelRect.x, y, width, CollapsedWindowHeight);
            y += toolsActualHeight + betweenPanels;

            VisibleWearablesWindow = new Rect(leftPanelRect.x, y, width, visibilityExpandedHeight);
            VisibleWearablesCollapsedWindow = new Rect(leftPanelRect.x, y, width, CollapsedWindowHeight);
            y += visibilityActualHeight + betweenPanels;

            MeshHideAssetsWindow = new Rect(leftPanelRect.x, y, width, meshHideExpandedHeight);
            MeshHideAssetsCollapsedWindow = new Rect(leftPanelRect.x, y, width, CollapsedWindowHeight);
        }

        private static Rect GetSceneViewUsableRect(SceneView view)
        {
            if (view == null)
            {
                return new Rect(0f, 0f, 0f, 0f);
            }

            // `SceneView.position` is in GUI coordinates (top-left origin) and includes toolbars.
            // `camera.pixelRect` is in pixel coordinates (bottom-left origin) and excludes toolbars.
            // We can use both to derive the inset margins (as suggested) and then build a GUI-space usable rect.

            Rect full = view.position;
            Camera cam = view.camera;
            if (cam == null)
            {
                return new Rect(0f, 0f, full.width, full.height);
            }

            Rect pixelView = cam.pixelRect;

            // Convert pixelView Y values (bottom-left origin) into GUI-space Y values (top-left origin).
            // In GUI-space:
            // - pixelView top edge = full.height - pixelView.yMax
            // - pixelView bottom edge = full.height - pixelView.yMin
            float viewTopGui = full.height - pixelView.yMax;
            float viewBottomGui = full.height - pixelView.yMin;

            float topToolbar = Mathf.Max(0f, viewTopGui - 0f);
            float bottomToolbar = Mathf.Max(0f, full.height - viewBottomGui);
            float leftToolbar = Mathf.Max(0f, pixelView.xMin - 0f);
            float rightToolbar = Mathf.Max(0f, full.width - pixelView.xMax);

            float x = leftToolbar;
            float y = topToolbar;
            float w = Mathf.Max(0f, full.width - leftToolbar - rightToolbar);
            float h = Mathf.Max(0f, full.height - topToolbar - bottomToolbar);

            // Return in local GUI coords (origin 0,0 inside the SceneView GUI event).
            return new Rect(x, y, w, h);
        }

        private float GetVisibilitySectionHeightEstimate(float maxHeight)
        {
            if (cachedVisibilityHeight >= 0f)
            {
                return cachedVisibilityHeight;
            }

            float baseHeight = 200f;
            cachedVisibilityHeight = Mathf.Clamp(baseHeight, 160f, Mathf.Max(160f, maxHeight));
            return cachedVisibilityHeight;
        }

        private void DoMeshHideAssetsWindow(int id)
        {
            using (var scroll = new GUILayout.ScrollViewScope(MeshHideAssetsScrollLocation))
            {
                MeshHideAssetsScrollLocation = scroll.scrollPosition;
                DrawSelectableSlotsSection();
            }

            if (Event.current.type == EventType.Repaint)
            {
                cachedMeshHideAssetsHeight = MeshHideAssetsWindow.height;
            }

        }

        private void DrawSelectableSlotsSection()
        {
            RefreshSlotSelectionEntries();

            if (slotSelectionEntries == null || slotSelectionEntries.Count == 0)
            {
                EditorGUILayout.HelpBox("No slots available in the current recipe.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Selectable Slots", EditorStyles.boldLabel);

            DrawRecalculateFromUVSection();

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select Visible"))
                {
                    SetSlotSelectionFromVisibility(true);
                }

                if (GUILayout.Button("Clear"))
                {
                    SetAllSlotSelections(false);
                }
            }

            for (int i = 0; i < slotSelectionEntries.Count; i++)
            {
                var e = slotSelectionEntries[i];
                if (e == null || string.IsNullOrEmpty(e.slotName))
                {
                    continue;
                }

                bool newSelected = EditorGUILayout.ToggleLeft(e.slotName, e.isSelected);
                if (newSelected != e.isSelected)
                {
                    e.isSelected = newSelected;
                    MarkOverlayMeshDirty();
                }
            }

            GUILayout.Space(8);
            if (GUILayout.Button("Create MeshHideAssets (Split by Slot)"))
            {
                SaveSelections();
            }
        }

        private void DrawRaycastOcclusionSection()
        {
#if UNITY_EDITOR
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Raycast Occlusion", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Raycast all selected slots against visible slots.", MessageType.Info);

            LoadRaycastPrefsIfNeeded();

            raycastOcclusionOutward = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Outward Distance", "Raycast along vertex normal"), raycastOcclusionOutward));
            raycastOcclusionInward = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Inward Distance", "Raycast opposite vertex normal"), raycastOcclusionInward));

            raycastOcclusionAdd = EditorGUILayout.ToggleLeft("Add to existing hides (otherwise replace)", raycastOcclusionAdd);
            raycastOcclusionStrategy = (MeshHideAsset.TriangleHideStrategy)EditorGUILayout.EnumPopup(new GUIContent("Triangle Strategy"), raycastOcclusionStrategy);

            raycastDrawDebugRays = EditorGUILayout.ToggleLeft("Draw debug rays", raycastDrawDebugRays);
            using (new EditorGUI.DisabledScope(!raycastDrawDebugRays))
            {
                raycastDebugRayCount = EditorGUILayout.IntSlider(new GUIContent("Debug Ray Count"), raycastDebugRayCount, 0, 65536);
            }

            SaveRaycastPrefs();

            bool anySelected = false;
            if (slotSelectionEntries != null)
            {
                for (int i = 0; i < slotSelectionEntries.Count; i++)
                {
                    if (slotSelectionEntries[i] != null && slotSelectionEntries[i].isSelected)
                    {
                        anySelected = true;
                        break;
                    }
                }
            }

            EditorGUI.BeginDisabledGroup(!anySelected);
            if (GUILayout.Button("Raycast Occlusion To MeshHideAssets"))
            {
                RaycastOcclusionToMeshHideAssets();
            }
            EditorGUI.EndDisabledGroup();

            if (!string.IsNullOrEmpty(raycastOcclusionStatus))
            {
                EditorGUILayout.HelpBox(raycastOcclusionStatus, raycastOcclusionStatusType);
            }
#endif
        }

#if UNITY_EDITOR
        private bool raycastPrefsLoaded;

        private void LoadRaycastPrefsIfNeeded()
        {
            if (raycastPrefsLoaded)
            {
                return;
            }
            raycastPrefsLoaded = true;

            raycastOcclusionOutward = EditorPrefs.GetFloat(RaycastPrefKeyPrefix + "Outward", RaycastDefaultOutward);
            raycastOcclusionInward = EditorPrefs.GetFloat(RaycastPrefKeyPrefix + "Inward", RaycastDefaultInward);
            raycastOcclusionAdd = EditorPrefs.GetBool(RaycastPrefKeyPrefix + "Add", false);
            raycastOcclusionStrategy = (MeshHideAsset.TriangleHideStrategy)EditorPrefs.GetInt(RaycastPrefKeyPrefix + "Strategy", (int)MeshHideAsset.TriangleHideStrategy.Conservative);
        }

        private void SaveRaycastPrefs()
        {
            EditorPrefs.SetFloat(RaycastPrefKeyPrefix + "Outward", raycastOcclusionOutward);
            EditorPrefs.SetFloat(RaycastPrefKeyPrefix + "Inward", raycastOcclusionInward);
            EditorPrefs.SetBool(RaycastPrefKeyPrefix + "Add", raycastOcclusionAdd);
            EditorPrefs.SetInt(RaycastPrefKeyPrefix + "Strategy", (int)raycastOcclusionStrategy);
        }

        private void RaycastOcclusionToMeshHideAssets()
        {
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null)
            {
                raycastOcclusionStatusType = MessageType.Warning;
                raycastOcclusionStatus = "Raycast skipped: character data is not available.";
                return;
            }

            if (FaceObject == null || BakedMesh == null || BakedMesh.vertexCount == 0)
            {
                raycastOcclusionStatusType = MessageType.Warning;
                raycastOcclusionStatus = "Raycast skipped: baked mesh is not available.";
                return;
            }

            if (raycastOcclusionOutward <= 0f && raycastOcclusionInward <= 0f)
            {
                raycastOcclusionStatusType = MessageType.Info;
                raycastOcclusionStatus = "Raycast skipped: both distances are 0.";
                return;
            }

            // Determine which slots we're processing: Visible + NOT in selection mode + selected in UI
            HashSet<string> selectedSlotNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < slotSelectionEntries.Count; i++)
            {
                var e = slotSelectionEntries[i];
                if (e == null || string.IsNullOrEmpty(e.slotName)) continue;
                if (e.isSelected)
                {
                    selectedSlotNames.Add(e.slotName);
                }
            }

            if (selectedSlotNames.Count == 0)
            {
                raycastOcclusionStatusType = MessageType.Info;
                raycastOcclusionStatus = "No selected slots to raycast.";
                return;
            }

            // Candidate slots: visible, selectable, not selected
            HashSet<string> visibleSlotNames = new HashSet<string>(StringComparer.Ordinal);
            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s == null) continue;
                if (s.Suppressed) continue;
                visibleSlotNames.Add(s.slotName);
            }

            // We do NOT require MeshHideAssets here; raycast behaves like manual selection.
            // Results are applied to stage selections (`SelectedFaces`) and can be split into assets later.

            // Build CPU triangle cache for candidate geometry: visible AND not in selected slots.
            // IMPORTANT: this relies on correct slot ownership mapping (GetSlotNameForBakedVertex).
            List<(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 normal)> candidates = BuildCandidateTrianglesCPU(visibleSlotNames, selectedSlotNames);
            if (candidates.Count == 0)
            {
                raycastOcclusionStatusType = MessageType.Warning;
                raycastOcclusionStatus = "Raycast skipped: no visible non-selected geometry to test against.";
                EditorUtility.DisplayDialog("Raycast Occlusion", raycastOcclusionStatus, "OK");
                return;
            }

            int totalTrianglesMarked = 0;
            int totalVerticesTested = 0;
            int totalVertexHits = 0;
            int totalSlotsProcessed = 0;
            int totalTrianglesChecked = 0;
            int slotsWithNewTriangles = 0;

            // SlotName -> localSubmesh -> local triangle flags.
            Dictionary<string, Dictionary<int, BitArray>> slotHiddenFlags = new Dictionary<string, Dictionary<int, BitArray>>(StringComparer.Ordinal);

            raycastDebugRaysAdded = 0;
            if (raycastDrawDebugRays)
            {
                raycastDebugRays.Clear();
            }

            try
            {
                int skippedNotVisible = 0;
                int skippedMissingMesh = 0;
                foreach (string slotName in selectedSlotNames)
                {
                    if (!visibleSlotNames.Contains(slotName))
                    {
                        skippedNotVisible++;
                        continue;
                    }

                    SlotData slot = thisDCA.umaData.umaRecipe.GetSlot(slotName);
                    if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                    {
                        skippedMissingMesh++;
                        continue;
                    }

                    bool[] occludedVerts = ComputeSlotVertexOcclusionCPU(slot, candidates, raycastOcclusionOutward, raycastOcclusionInward, ref totalVerticesTested, ref totalVertexHits);
                    if (occludedVerts == null)
                    {
                        continue;
                    }

                    int markedForSlot = 0;

                    int slotSubmeshCount = Mathf.Max(1, slot.asset.meshData.subMeshCount);
                    if (!slotHiddenFlags.TryGetValue(slotName, out var perSubmesh))
                    {
                        perSubmesh = new Dictionary<int, BitArray>();
                        slotHiddenFlags[slotName] = perSubmesh;
                    }

                    for (int localSubmesh = 0; localSubmesh < slotSubmeshCount; localSubmesh++)
                    {
                        int triCount = GetLocalTriangleCountForSlotSubmesh(slot, localSubmesh);
                        if (triCount <= 0)
                        {
                            continue;
                        }

                        if (!perSubmesh.TryGetValue(localSubmesh, out BitArray flags) || flags == null || flags.Count != triCount)
                        {
                            flags = new BitArray(triCount);
                            perSubmesh[localSubmesh] = flags;
                        }
                        else if (!raycastOcclusionAdd)
                        {
                            flags.SetAll(false);
                        }

                        int marked = ApplyTriangleOcclusionFromVertexOcclusion(slot, localSubmesh, occludedVerts, flags, raycastOcclusionStrategy);
                        if (marked > 0)
                        {
                            markedForSlot += marked;
                            totalTrianglesMarked += marked;
                        }

                        totalTrianglesChecked += triCount;
                    }

                    if (markedForSlot > 0)
                    {
                        slotsWithNewTriangles++;
                    }

                    totalSlotsProcessed++;
                }

                // Append skip info for debugging.
                if (skippedNotVisible > 0 || skippedMissingMesh > 0)
                {
                    raycastOcclusionStatusType = MessageType.Info;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // Apply the hidden flags to stage selections (manual-selection equivalent).
            if (!raycastOcclusionAdd)
            {
                RecordSelectionUndo("Raycast Occlusion");
                // Replace selection for processed slots
                if (selectedSlotTrianglesSerialized != null && selectedSlotTrianglesSerialized.Count > 0)
                {
                    for (int i = selectedSlotTrianglesSerialized.Count - 1; i >= 0; i--)
                    {
                        var k = selectedSlotTrianglesSerialized[i];
                        if (!string.IsNullOrEmpty(k.slotName) && slotHiddenFlags.ContainsKey(k.slotName))
                        {
                            selectedSlotTrianglesSerialized.RemoveAt(i);
                        }
                    }
                }
                RebuildSelectedSlotTrianglesFromSerialized();
            }

            foreach (var pair in slotHiddenFlags)
            {
                string slotName = pair.Key;
                var perSubmesh = pair.Value;
                if (perSubmesh == null || perSubmesh.Count == 0)
                {
                    continue;
                }

                foreach (var smPair in perSubmesh)
                {
                    int localSubmesh = smPair.Key;
                    BitArray flags = smPair.Value;
                    if (flags == null)
                    {
                        continue;
                    }

                    int triCount = flags.Count;
                    for (int slotTri = 0; slotTri < triCount; slotTri++)
                    {
                        if (!flags[slotTri])
                        {
                            continue;
                        }

                        AddSelectedSlotTriangle(new SlotTriangleKey(slotName, localSubmesh, slotTri));
                    }
                }
            }

            selectionVersion++;
            MarkOverlayMeshDirty();

            raycastOcclusionStatusType = MessageType.Info;
            raycastOcclusionStatus = $"Raycast complete\nCandidate triangles: {candidates.Count}\nSlots processed: {totalSlotsProcessed}\nSlots with new triangles: {slotsWithNewTriangles}\nTriangles checked: {totalTrianglesChecked}\nVertices tested: {totalVerticesTested}\nVertex hits: {totalVertexHits}\nTriangles marked: {totalTrianglesMarked}";

            ShowCopyableDialog("Raycast Occlusion Complete", raycastOcclusionStatus);

            SceneView.RepaintAll();
        }

        private static void ShowCopyableDialog(string title, string text)
        {
            int choice = EditorUtility.DisplayDialogComplex(title, text, "Copy", "OK", string.Empty);
            if (choice == 0)
            {
                EditorGUIUtility.systemCopyBuffer = text ?? string.Empty;
            }
        }

        private void RunTestRaycastForSelectedVertex()
        {
            raycastTestStatusType = MessageType.Info;
            raycastTestStatus = null;

            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null)
            {
                raycastTestStatusType = MessageType.Warning;
                raycastTestStatus = "Test raycast skipped: character data is not available.";
                return;
            }
            if (FaceObject == null || BakedMesh == null)
            {
                raycastTestStatusType = MessageType.Warning;
                raycastTestStatus = "Test raycast skipped: baked mesh is not available.";
                return;
            }
            if (string.IsNullOrEmpty(raycastTestSourceSlot) || raycastTestSourceSlot == "All Slots")
            {
                raycastTestStatusType = MessageType.Warning;
                raycastTestStatus = "Pick a Source Slot first.";
                return;
            }
            if (string.IsNullOrEmpty(raycastTestOccluderSlot) || raycastTestOccluderSlot == "All Slots")
            {
                raycastTestStatusType = MessageType.Warning;
                raycastTestStatus = "Pick an Occluder Slot first.";
                return;
            }

            SlotData sourceSlot = thisDCA.umaData.umaRecipe.GetSlot(raycastTestSourceSlot);
            if (sourceSlot == null || sourceSlot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(sourceSlot.asset.meshData))
            {
                raycastTestStatusType = MessageType.Warning;
                raycastTestStatus = "Source slot not found or missing mesh data.";
                return;
            }

            int bakedIndex = GetBakedVertexIndexForSlotVertex(sourceSlot, raycastTestSlotVertexIndex);
            if (bakedIndex < 0 || bakedIndex >= BakedMesh.vertexCount)
            {
                raycastTestStatusType = MessageType.Warning;
                raycastTestStatus = $"Invalid baked vertex index for slot vertex {raycastTestSlotVertexIndex}.";
                return;
            }

            RefreshBakedMeshCaches();
            if (bakedVertices == null || bakedNormals == null)
            {
                raycastTestStatusType = MessageType.Warning;
                raycastTestStatus = "Test raycast skipped: baked mesh caches are not available.";
                return;
            }

            Vector3 originWorld = FaceObject.transform.TransformPoint(bakedVertices[bakedIndex]);
            Vector3 nWorld = Vector3.up;
            if (bakedIndex >= 0 && bakedIndex < bakedNormals.Length)
            {
                nWorld = FaceObject.transform.TransformDirection(bakedNormals[bakedIndex]).normalized;
            }
            if (nWorld.sqrMagnitude < 1e-8f)
            {
                raycastTestStatusType = MessageType.Warning;
                raycastTestStatus = "Vertex normal is invalid/zero.";
                return;
            }

            HashSet<string> visibleSlotNames = new HashSet<string>(StringComparer.Ordinal);
            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s == null) continue;
                if (s.Suppressed) continue;
                visibleSlotNames.Add(s.slotName);
            }

            var selectedSlotNames = new HashSet<string>(StringComparer.Ordinal) { raycastTestSourceSlot };
            List<(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 normal)> candidates = BuildCandidateTrianglesCPU(visibleSlotNames, selectedSlotNames);
            if (candidates.Count == 0)
            {
                raycastTestStatusType = MessageType.Warning;
                raycastTestStatus = "No candidate triangles found. Slot ownership mapping may be failing.";
                return;
            }

            // Rebuild candidate list for ONLY the occluder slot.
            HashSet<string> selectedExceptOccluder = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in visibleSlotNames)
            {
                if (!string.Equals(s, raycastTestOccluderSlot, StringComparison.Ordinal))
                {
                    selectedExceptOccluder.Add(s);
                }
            }
            List<(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 normal)> occCandidates = BuildCandidateTrianglesCPU(visibleSlotNames, selectedExceptOccluder);
            if (occCandidates.Count == 0)
            {
                raycastTestStatusType = MessageType.Warning;
                raycastTestStatus = "No occluder triangles found for the chosen occluder slot (or it is suppressed).";
                return;
            }

            float outMax = Mathf.Max(0f, raycastOcclusionOutward);
            float inMax = Mathf.Max(0f, raycastOcclusionInward);

            bool hitOut = false;
            bool hitIn = false;
            if (outMax > 0f)
            {
                Vector3 dir = nWorld;
                Vector3 origin = originWorld + (dir * 0.0005f);
                hitOut = RaycastCPU(origin, dir, outMax, occCandidates);
            }
            if (inMax > 0f)
            {
                Vector3 dir = -nWorld;
                Vector3 origin = originWorld + (dir * 0.0005f);
                hitIn = RaycastCPU(origin, dir, inMax, occCandidates);
            }

            raycastTestStatusType = (hitOut || hitIn) ? MessageType.Info : MessageType.Warning;
            raycastTestStatus = $"Test vertex bakedIndex={bakedIndex}\nSource={raycastTestSourceSlot} Occluder={raycastTestOccluderSlot}\nCandidates(all)={candidates.Count} Candidates(occluder)={occCandidates.Count}\nHit outward={hitOut} Hit inward={hitIn}";
        }

        private bool IsSlotInSelectionMode(string slotName)
        {
            // Slot is "in selection mode" when it is currently part of the active selection set for manual editing.
            // Implementation: use slotSelectionEntries' isSelected as the selection mode flag.
            // The raycast feature requires slots NOT in selection mode.
            if (slotSelectionEntries == null)
            {
                return false;
            }
            for (int i = 0; i < slotSelectionEntries.Count; i++)
            {
                var e = slotSelectionEntries[i];
                if (e == null) continue;
                if (e.slotName == slotName)
                {
                    return e.isSelected;
                }
            }
            return false;
        }

        private List<(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 normal)> BuildCandidateTrianglesCPU(HashSet<string> visibleSlotNames, HashSet<string> selectedSlotNames)
        {
            var result = new List<(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 normal)>();

            RefreshBakedMeshCaches();
            var tris = bakedTriangles;
            var verts = bakedVertices;
            if (tris == null || verts == null)
            {
                return result;
            }
            int triangleCount = tris.Length / 3;

            // Build triangles only if they belong to a visible, non-selected slot.
            for (int t = 0; t < triangleCount; t++)
            {
                int i0 = tris[t * 3 + 0];
                int i1 = tris[t * 3 + 1];
                int i2 = tris[t * 3 + 2];

                string slotName = GetSlotNameForBakedVertex(i0);
                if (string.IsNullOrEmpty(slotName))
                {
                    continue;
                }
                if (!visibleSlotNames.Contains(slotName))
                {
                    continue;
                }
                if (selectedSlotNames.Contains(slotName))
                {
                    continue;
                }

                Vector3 v0 = FaceObject.transform.TransformPoint(verts[i0]);
                Vector3 v1 = FaceObject.transform.TransformPoint(verts[i1]);
                Vector3 v2 = FaceObject.transform.TransformPoint(verts[i2]);
                Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0);
                if (faceNormal.sqrMagnitude > 1e-12f)
                {
                    faceNormal.Normalize();
                }
                else
                {
                    faceNormal = Vector3.up;
                }

                result.Add((v0, v1, v2, faceNormal));
            }

            return result;
        }

        private bool[] ComputeSlotVertexOcclusionCPU(SlotData sourceSlot,
            List<(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 normal)> candidates,
            float outwardDistance,
            float inwardDistance,
            ref int totalVerticesTested,
            ref int totalVertexHits)
        {
            if (sourceSlot == null || sourceSlot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(sourceSlot.asset.meshData))
            {
                return null;
            }

            int sourceVertexCount = sourceSlot.asset.meshData.vertexCount;
            if (sourceVertexCount <= 0)
            {
                return null;
            }

            RefreshBakedMeshCaches();
            var verts = bakedVertices;
            var normals = bakedNormals;
            if (verts == null || normals == null)
            {
                return null;
            }
            bool[] occluded = new bool[sourceVertexCount];

            float outMax = outwardDistance > 0f ? outwardDistance : 0f;
            float inMax = inwardDistance > 0f ? inwardDistance : 0f;

            for (int slotVertexIndex = 0; slotVertexIndex < sourceVertexCount; slotVertexIndex++)
            {
                int bakedIndex = GetBakedVertexIndexForSlotVertex(sourceSlot, slotVertexIndex);
                if (bakedIndex < 0)
                {
                    continue;
                }
                if (bakedIndex < 0 || bakedIndex >= verts.Length)
                {
                    continue;
                }

                Vector3 originWorld = FaceObject.transform.TransformPoint(verts[bakedIndex]);
                Vector3 nWorld = Vector3.up;
                if (bakedIndex >= 0 && bakedIndex < normals.Length)
                {
                    nWorld = FaceObject.transform.TransformDirection(normals[bakedIndex]).normalized;
                }
                if (nWorld.sqrMagnitude < 1e-8f)
                {
                    continue;
                }

                bool hit = false;

                // Outward
                if (outMax > 0f)
                {
                    Vector3 dir = nWorld;
                    Vector3 origin = originWorld + (dir * Mathf.Max(RaycastOriginEpsilon, outMax * 0.001f));

                    if (raycastDrawDebugRays && raycastDebugRaysAdded < raycastDebugRayCount)
                    {
                        raycastDebugRays.Add(new DebugRay
                        {
                            origin = origin,
                            direction = dir,
                            distance = outMax,
                            color = Color.blue
                        });
                        raycastDebugRaysAdded++;
                    }

                    hit = RaycastCPU(origin, dir, outMax, candidates);
                }

                // Inward (only if not already hit)
                if (!hit && inMax > 0f)
                {
                    Vector3 dir = -nWorld;
                    Vector3 origin = originWorld + (dir * Mathf.Max(RaycastOriginEpsilon, inMax * 0.001f));

                    if (raycastDrawDebugRays && raycastDebugRaysAdded < raycastDebugRayCount)
                    {
                        raycastDebugRays.Add(new DebugRay
                        {
                            origin = origin,
                            direction = dir,
                            distance = inMax,
                            color = Color.green
                        });
                        raycastDebugRaysAdded++;
                    }

                    hit = RaycastCPU(origin, dir, inMax, candidates);
                }

                totalVerticesTested++;
                if (hit)
                {
                    totalVertexHits++;
                    occluded[slotVertexIndex] = true;
                }
            }

            return occluded;
        }

        private string GetSlotNameForBakedVertex(int bakedVertexIndex)
        {
            SlotData s = GetSlotForBakedVertexIndex(bakedVertexIndex);
            return s != null ? s.slotName : null;
        }

        private int GetBakedVertexIndexForSlotVertex(SlotData slot, int slotVertexIndex)
        {
            if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
            {
                return -1;
            }
            if (slot.Suppressed)
            {
                return -1;
            }

            int baked = slot.vertexOffset + slotVertexIndex;
            if (BakedMesh == null)
            {
                return -1;
            }
            if (baked < 0 || baked >= BakedMesh.vertexCount)
            {
                return -1;
            }
            return baked;
        }

        private SlotData GetSlotForBakedVertexIndex(int bakedVertexIndex)
        {
            if (bakedVertexSlotRanges.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < bakedVertexSlotRanges.Count; i++)
            {
                var r = bakedVertexSlotRanges[i];
                if (bakedVertexIndex >= r.start && bakedVertexIndex < r.endExclusive)
                {
                    return r.slot;
                }
            }

            return null;
        }

        private bool RaycastCPU(Vector3 origin, Vector3 direction, float maxDistance, List<(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 normal)> triangles)
        {
            if (triangles == null || triangles.Count == 0)
            {
                return false;
            }

            if (maxDistance <= 0f)
            {
                return false;
            }

            float bestT = maxDistance;
            bool hit = false;

            for (int i = 0; i < triangles.Count; i++)
            {
                var tri = triangles[i];
                if (RayTriangleIntersect(origin, direction, tri.v0, tri.v1, tri.v2, out float t))
                {
                    if (t > Mathf.Max(RaycastHitEpsilon, maxDistance * 0.0001f) && t < bestT)
                    {
                        // Apply face filter based on requested strategy? For occlusion, accept all.
                        bestT = t;
                        hit = true;
                    }
                }
            }

            return hit;
        }

        private static bool RayTriangleIntersect(Vector3 rayOrigin, Vector3 rayDir, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            static bool IntersectOne(Vector3 ro, Vector3 rd, Vector3 a0, Vector3 a1, Vector3 a2, out float outT)
            {
                outT = 0f;
                const float EPSILON = 1e-8f;

                Vector3 edge1 = a1 - a0;
                Vector3 edge2 = a2 - a0;
                Vector3 h = Vector3.Cross(rd, edge2);
                float det = Vector3.Dot(edge1, h);

                if (det > -EPSILON && det < EPSILON)
                {
                    return false;
                }

                float invDet = 1.0f / det;
                Vector3 s = ro - a0;
                float u = invDet * Vector3.Dot(s, h);
                if (u < 0.0f || u > 1.0f)
                {
                    return false;
                }

                Vector3 q = Vector3.Cross(s, edge1);
                float v = invDet * Vector3.Dot(rd, q);
                if (v < 0.0f || u + v > 1.0f)
                {
                    return false;
                }

                outT = invDet * Vector3.Dot(edge2, q);
                return outT > EPSILON;
            }

            // Try the original winding, then the reversed winding (double-sided).
            if (IntersectOne(rayOrigin, rayDir, v0, v1, v2, out t))
            {
                return true;
            }

            return IntersectOne(rayOrigin, rayDir, v0, v2, v1, out t);
        }

        private int GetLocalTriangleCountForSlotSubmesh(SlotData slot, int localSubmesh)
        {
            if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
            {
                return 0;
            }
            if (localSubmesh < 0 || localSubmesh >= slot.asset.meshData.subMeshCount)
            {
                return 0;
            }

            var tris = slot.asset.meshData.submeshes[localSubmesh].GetBaseTriangles();
            if (tris == null)
            {
                return 0;
            }
            return tris.Length / 3;
        }

        private int ApplyTriangleOcclusionFromVertexOcclusion(SlotData slot, int localSubmesh, bool[] occludedVerts, BitArray flags, MeshHideAsset.TriangleHideStrategy strategy)
        {
            if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
            {
                return 0;
            }
            if (occludedVerts == null || flags == null)
            {
                return 0;
            }

            var tris = slot.asset.meshData.submeshes[localSubmesh].GetBaseTriangles();
            if (tris == null || tris.Length == 0)
            {
                return 0;
            }

            int triCount = Mathf.Min(flags.Count, tris.Length / 3);
            int marked = 0;
            for (int t = 0; t < triCount; t++)
            {
                int ti = t * 3;
                int v0i = tris[ti + 0];
                int v1i = tris[ti + 1];
                int v2i = tris[ti + 2];

                bool v0 = (v0i >= 0 && v0i < occludedVerts.Length) ? occludedVerts[v0i] : false;
                bool v1 = (v1i >= 0 && v1i < occludedVerts.Length) ? occludedVerts[v1i] : false;
                bool v2 = (v2i >= 0 && v2i < occludedVerts.Length) ? occludedVerts[v2i] : false;

                bool hide;
                if (strategy == MeshHideAsset.TriangleHideStrategy.Strict)
                {
                    hide = v0 && v1 && v2;
                }
                else if (strategy == MeshHideAsset.TriangleHideStrategy.Weighted)
                {
                    int c = 0;
                    if (v0) c++;
                    if (v1) c++;
                    if (v2) c++;
                    hide = c >= 2;
                }
                else
                {
                    hide = v0 || v1 || v2;
                }

                if (hide)
                {
                    if (!flags[t])
                    {
                        flags[t] = true;
                        marked++;
                    }
                    else
                    {
                        // already marked
                    }
                }
            }

            return marked;
        }
#endif

        private void DrawRecalculateFromUVSection()
        {
#if UNITY_EDITOR
            MeshHideAssetCollection collection = CurrentHideCollection;
            MeshHideAsset single = CurrentHideAsset;

            if (single == null && collection == null)
            {
                return;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("MeshHideAssets", EditorStyles.boldLabel);

            if (single != null)
            {
                DrawRecalculateButtonForMeshHideAsset(single);
                return;
            }

            var assets = collection.Assets;
            if (assets == null || assets.Count == 0)
            {
                EditorGUILayout.HelpBox("This MeshHideAssetCollection has no MeshHideAssets.", MessageType.Info);
                return;
            }

            for (int i = 0; i < assets.Count; i++)
            {
                var mha = assets[i];
                if (mha == null)
                {
                    continue;
                }
                DrawRecalculateButtonForMeshHideAsset(mha);
            }
#endif
        }

        private void DrawRecalculateButtonForMeshHideAsset(MeshHideAsset mha)
        {
#if UNITY_EDITOR
            string slotName = (mha != null) ? mha.AssetSlotName : null;
            if (string.IsNullOrEmpty(slotName))
            {
                slotName = (mha != null) ? mha.name : "(unknown)";
            }

            using (new GUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(slotName, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Recalculate From UV", GUILayout.Width(150)))
                {
                    RecalculateFromUV(mha);
                }
            }
#endif
        }

        private void RecalculateFromUV(MeshHideAsset mha)
        {
#if UNITY_EDITOR
            if (mha == null)
            {
                return;
            }

            if (mha.HiddenVertexesByUV == null || mha.HiddenVertexesByUV.Length == 0)
            {
                EditorUtility.DisplayDialog("Recalculate From UV", "No UV mask is present on this MeshHideAsset.", "OK");
                return;
            }

            int choice = EditorUtility.DisplayDialogComplex(
                "Recalculate From UV",
                "Select a Hide Strategy:\n\nStrict: hide only if ALL 3 vertices are marked\nWeighted: hide if 2 or more vertices are marked\nConservative: hide if ANY vertex is marked",
                "Strict",
                "Weighted",
                "Conservative");

            MeshHideAsset.TriangleHideStrategy strategy;
            if (choice == 0)
            {
                strategy = MeshHideAsset.TriangleHideStrategy.Strict;
            }
            else if (choice == 1)
            {
                strategy = MeshHideAsset.TriangleHideStrategy.Weighted;
            }
            else
            {
                strategy = MeshHideAsset.TriangleHideStrategy.Conservative;
            }

            Undo.RecordObject(mha, "Recalculate MeshHideAsset From UV");
            var old = MeshHideAsset.HideStrategy;
            MeshHideAsset.HideStrategy = strategy;
            try
            {
                mha.RebuildFlagsFromEditorUVMask();
            }
            finally
            {
                MeshHideAsset.HideStrategy = old;
            }

            EditorUtility.SetDirty(mha);
            AssetDatabase.SaveAssetIfDirty(mha);

            // Reload stage selections from updated flags
            RebuildTriangleSlotOwnership();
            LoadSelections();
            MarkOverlayMeshDirty();
#endif
        }

        private void SetAllSlotSelections(bool selected)
        {
            if (slotSelectionEntries == null)
            {
                return;
            }

            for (int i = 0; i < slotSelectionEntries.Count; i++)
            {
                if (slotSelectionEntries[i] == null)
                {
                    continue;
                }

                slotSelectionEntries[i].isSelected = selected;
            }

            MarkOverlayMeshDirty();
        }

        private void SetSlotSelectionFromVisibility(bool selected)
        {
            if (slotSelectionEntries == null)
            {
                return;
            }

            for (int i = 0; i < slotSelectionEntries.Count; i++)
            {
                var e = slotSelectionEntries[i];
                if (e == null || string.IsNullOrEmpty(e.slotName))
                {
                    continue;
                }

                if (slotLookupByName.TryGetValue(e.slotName, out var slot) && slot != null)
                {
                    e.isSelected = selected && !slot.Suppressed;
                }
                else
                {
                    e.isSelected = false;
                }
            }

            MarkOverlayMeshDirty();
        }

        private string GetLastMeshHideAssetFolder()
        {
            string projectKey = Application.dataPath;
            string prefKey = MeshHideAssetFolderPrefKeyPrefix + projectKey;
            string folder = EditorPrefs.GetString(prefKey, "Assets");
            if (string.IsNullOrEmpty(folder))
            {
                folder = "Assets";
            }
            return folder;
        }

        private void SetLastMeshHideAssetFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }
            string projectKey = Application.dataPath;
            string prefKey = MeshHideAssetFolderPrefKeyPrefix + projectKey;
            EditorPrefs.SetString(prefKey, folder);
        }

        public void DoToolsWindow(int id)
        {
            using (var scroll = new GUILayout.ScrollViewScope(FaceEditorScrollLocation))
            {
                FaceEditorScrollLocation = scroll.scrollPosition;

                GUILayout.Label("Selection", centeredLabel);

                paintMode = EditorGUILayout.ToggleLeft("Paint Mode", paintMode);
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label("Operation", GUILayout.Width(70f));
                    bool add = GUILayout.Toggle(paintAddMode, "Add", "Button");
                    bool remove = GUILayout.Toggle(!paintAddMode, "Remove", "Button");
                    if (add != paintAddMode)
                    {
                        paintAddMode = true;
                    }
                    else if (remove == true)
                    {
                        paintAddMode = false;
                    }
                }

                rubberBandCullBackfaces = EditorGUILayout.ToggleLeft("Rubber Band Cull Backfaces", rubberBandCullBackfaces);

                if (GUILayout.Button("Reset Camera"))
                {
                    ResetCameraToSelectedSlots();
                }

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Clear"))
                    {
                        RecordSelectionUndo("Clear Face Selection");
                        selectedSlotTriangles.Clear();
                        if (selectedSlotTrianglesSerialized != null)
                        {
                            selectedSlotTrianglesSerialized.Clear();
                        }
                        selectionVersion++;
                        MarkOverlayMeshDirty();
                    }
                    if (GUILayout.Button("Select All"))
                    {
                        RecordSelectionUndo("Select All Faces");
                        SelectAllFaces();
                    }
                }

                GUILayout.Space(6);
                GUILayout.Label($"Selected Faces: {SelectedSlotTriangleCount}");

                DrawRaycastOcclusionSection();

                if (GUILayout.Button("Create MeshHideAssets (Split by Slot)"))
                {
                    SaveSelections();
                }
            }
        }

        private void ResetCameraToSelectedSlots()
        {
            SceneView view = openedSceneView;
            if (view == null)
            {
                view = SceneView.lastActiveSceneView;
            }
            if (view == null || FaceObject == null || BakedMesh == null)
            {
                return;
            }

            Bounds bounds;
            if (!TryGetSelectedSlotBoundsWorld(out bounds))
            {
                bounds = new Bounds(FaceObject.transform.position, Vector3.one);
            }

            float radius = Mathf.Max(0.01f, bounds.extents.magnitude);
            float distance = Mathf.Max(0.1f, radius * 1.5f);

            Quaternion rot = view.rotation;
            view.LookAtDirect(bounds.center, rot, distance);
            view.Repaint();
        }

        private bool TryGetSelectedSlotBoundsWorld(out Bounds bounds)
        {
            bounds = default;
            if (FaceObject == null || BakedMesh == null || triangleSlotOwnership == null || triangleSlotOwnership.Count == 0)
            {
                return false;
            }

            RefreshBakedMeshCaches();
            Vector3[] vertices = bakedVertices;
            if (vertices == null)
            {
                return false;
            }
            Matrix4x4 matrix = FaceObject.transform.localToWorldMatrix;
            Dictionary<int, int[]> trianglesBySubmesh = new Dictionary<int, int[]>();

            bool hasAny = false;
            foreach (var pair in triangleSlotOwnership)
            {
                var owner = pair.Value;
                string slotName = owner.slot != null ? owner.slot.slotName : null;
                if (!IsSlotSelected(slotName))
                {
                    continue;
                }

                int sm = pair.Key.submeshIndex;
                if (!trianglesBySubmesh.TryGetValue(sm, out var tris))
                {
                    tris = BakedMesh.GetTriangles(sm);
                    trianglesBySubmesh.Add(sm, tris);
                }

                int ti = pair.Key.triangleIndex * 3;
                if (tris == null || ti + 2 >= tris.Length)
                {
                    continue;
                }

                Vector3 w0 = matrix.MultiplyPoint3x4(vertices[tris[ti]]);
                Vector3 w1 = matrix.MultiplyPoint3x4(vertices[tris[ti + 1]]);
                Vector3 w2 = matrix.MultiplyPoint3x4(vertices[tris[ti + 2]]);

                if (!hasAny)
                {
                    bounds = new Bounds(w0, Vector3.zero);
                    bounds.Encapsulate(w1);
                    bounds.Encapsulate(w2);
                    hasAny = true;
                }
                else
                {
                    bounds.Encapsulate(w0);
                    bounds.Encapsulate(w1);
                    bounds.Encapsulate(w2);
                }
            }

            return hasAny;
        }

        private void DoVisibilityWindow(int id)
        {
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null)
            {
                EditorGUILayout.HelpBox("No UMA data available.", MessageType.Warning);
                return;
            }

            RefreshVisibleSlotListsIfNeeded();

            bool wasChanged = false;
            bool wasRecipeChanged = false;
            bool blockedHideAllSlots = false;

            var wearables = thisDCA.GetVisibleWearables();

            using (var scroll = new GUILayout.ScrollViewScope(VisibleWearablesLocation))
            {
                VisibleWearablesLocation = scroll.scrollPosition;

                if (EnsureAtLeastOneVisibleSlot())
                {
                    wasChanged = true;
                }

                GUILayout.Label("Visible Wearables", EditorStyles.boldLabel);
                if (wearables != null)
                {
                    foreach (var wearable in wearables)
                    {
                        if (wearable == null)
                        {
                            continue;
                        }

                        GUILayout.BeginHorizontal();
                        bool wasDisabled = wearable.disabled;
                        bool desiredVisible = GUILayout.Toggle(!wearable.disabled, string.Empty, GUILayout.Width(24));
                        wearable.disabled = !desiredVisible;
                        if (wearable.disabled != wasDisabled)
                        {
                            wasRecipeChanged = true;
                        }
                        GUILayout.Label(wearable.name);
                        GUILayout.EndHorizontal();
                    }
                }

                GUILayout.Space(10);
                GUILayout.Label("Visible Slots", EditorStyles.boldLabel);

                int visibleSlotCount = GetVisibleSlotCount();
                var slots = thisDCA.umaData.umaRecipe.slotDataList;
                if (slots != null)
                {
                    for (int i = 0; i < slots.Length; i++)
                    {
                        var slot = slots[i];
                        if (slot == null)
                        {
                            continue;
                        }

                        GUILayout.BeginHorizontal();
                        bool wasSuppressed = slot.Suppressed;
                        bool desiredVisible = GUILayout.Toggle(!slot.Suppressed, string.Empty, GUILayout.Width(24));
                        bool desiredSuppressed = !desiredVisible;

                        if (desiredSuppressed && !slot.Suppressed && visibleSlotCount <= 1)
                        {
                            desiredSuppressed = false;
                            blockedHideAllSlots = true;
                        }

                        slot.Suppressed = desiredSuppressed;
                        if (slot.Suppressed != wasSuppressed)
                        {
                            wasChanged = true;
                            visibleSlotCount += slot.Suppressed ? -1 : 1;
                        }

                        string label = slot.slotName;
                        if (label.Length > 27)
                        {
                            label = label.Substring(0, 24) + "...";
                        }
                        if (GUILayout.Button(label, EditorStyles.label))
                        {
                            if (!(slot.Suppressed == false && visibleSlotCount <= 1))
                            {
                                slot.Suppressed = !slot.Suppressed;
                                wasChanged = true;
                                visibleSlotCount += slot.Suppressed ? -1 : 1;
                            }
                            else
                            {
                                blockedHideAllSlots = true;
                            }
                        }
                        GUILayout.EndHorizontal();
                    }
                }

                GUILayout.Space(8);
                if (GUILayout.Button("Invert Visiblity", EditorStyles.miniButton))
                {
                    if (wearables != null)
                    {
                        foreach (var wearable in wearables)
                        {
                            if (wearable == null) continue;
                            wearable.disabled = !wearable.disabled;
                            wasRecipeChanged = true;
                        }
                    }

                    if (slots != null)
                    {
                        for (int i = 0; i < slots.Length; i++)
                        {
                            var slot = slots[i];
                            if (slot == null) continue;
                            slot.Suppressed = !slot.Suppressed;
                            wasChanged = true;
                        }
                    }

                    if (EnsureAtLeastOneVisibleSlot())
                    {
                        wasChanged = true;
                    }
                }

                if (blockedHideAllSlots)
                {
                    EditorGUILayout.HelpBox("At least one slot must remain visible.", MessageType.Info);
                }
            }

            if (wasRecipeChanged || wasChanged)
            {
                RebuildMesh(wasRecipeChanged, true);
            }

        }

        private void LoadCollapsePrefsIfNeeded()
        {
            if (collapsePrefsLoaded)
            {
                return;
            }
            collapsePrefsLoaded = true;

            visibilityPanelCollapsed = EditorPrefs.GetBool(PanelCollapsePrefKeyPrefix + "Visibility", false);
            meshHideAssetsPanelCollapsed = EditorPrefs.GetBool(PanelCollapsePrefKeyPrefix + "MeshHideAssets", false);
            faceToolsPanelCollapsed = EditorPrefs.GetBool(PanelCollapsePrefKeyPrefix + "FaceTools", false);
        }

        private int GetVisibleSlotCount()
        {
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return 0;
            }

            int visible = 0;
            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;
                if (!slot.Suppressed) visible++;
            }
            return visible;
        }

        private bool EnsureAtLeastOneVisibleSlot()
        {
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return false;
            }

            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            bool hasVisible = false;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;
                if (!slot.Suppressed)
                {
                    hasVisible = true;
                    break;
                }
            }

            if (hasVisible)
            {
                return false;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;
                slot.Suppressed = false;
                return true;
            }

            return false;
        }

        private void RefreshVisibleSlotListsIfNeeded()
        {
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                RefreshVisibleSlotLists();
                RefreshSlotSelectionEntries();
                return;
            }

            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            int sig = 17;
            int count = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null || string.IsNullOrEmpty(slot.slotName))
                {
                    continue;
                }

                // Build a compact signature of current slot list + suppression state.
                unchecked
                {
                    sig = (sig * 31) ^ slot.slotName.GetHashCode();
                    sig = (sig * 31) ^ (slot.Suppressed ? 1 : 0);
                }
                if (!slot.Suppressed)
                {
                    count++;
                }
            }

            if (sig == visibleSlotsSignature && count == visibleSlotsCount)
            {
                return;
            }

            visibleSlotsSignature = sig;
            visibleSlotsCount = count;

            RefreshVisibleSlotLists();
            RefreshSlotSelectionEntries();
        }

        private void RefreshVisibleSlotLists()
        {
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null)
            {
                selectFrom = new string[] { "All Slots" };
                visibleSelectFrom = new string[] { "All Slots" };
                selectionSlot = Mathf.Clamp(selectionSlot, 0, 0);
                return;
            }

            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            if (slots == null)
            {
                selectFrom = new string[] { "All Slots" };
                visibleSelectFrom = new string[] { "All Slots" };
                selectionSlot = Mathf.Clamp(selectionSlot, 0, 0);
                return;
            }

            List<string> all = new List<string>() { "All Slots" };
            List<string> visible = new List<string>() { "All Slots" };
            foreach (var slot in slots)
            {
                if (slot == null)
                {
                    continue;
                }
                all.Add(slot.slotName);
                if (!slot.Suppressed)
                {
                    visible.Add(slot.slotName);
                }
            }

            selectFrom = all.ToArray();
            visibleSelectFrom = visible.ToArray();

            selectionSlot = Mathf.Clamp(selectionSlot, 0, selectFrom.Length - 1);
            RefreshSlotSelectionEntries();
            RebuildTriangleSlotOwnership();
            PruneSelectionsForCurrentOwnership();
        }

        private void RefreshSlotSelectionEntries()
        {
            slotLookupByName.Clear();

            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                slotSelectionEntries.Clear();
                return;
            }

            Dictionary<string, bool> existing = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (int i = 0; i < slotSelectionEntries.Count; i++)
            {
                var e = slotSelectionEntries[i];
                if (e == null || string.IsNullOrEmpty(e.slotName))
                {
                    continue;
                }

                existing[e.slotName] = e.isSelected;
            }

            List<SlotSelectionEntry> rebuilt = new List<SlotSelectionEntry>();
            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null || string.IsNullOrEmpty(slot.slotName))
                {
                    continue;
                }

                if (!slotLookupByName.ContainsKey(slot.slotName))
                {
                    slotLookupByName.Add(slot.slotName, slot);

                    bool selected = false;
                    if (existing.TryGetValue(slot.slotName, out var existingSelected))
                    {
                        selected = existingSelected;
                    }

                    rebuilt.Add(new SlotSelectionEntry
                    {
                        slotName = slot.slotName,
                        isSelected = selected
                    });
                }
            }

            slotSelectionEntries = rebuilt;
        }

        private bool IsSlotSelected(string slotName)
        {
            if (string.IsNullOrEmpty(slotName) || slotSelectionEntries == null)
            {
                return false;
            }

            for (int i = 0; i < slotSelectionEntries.Count; i++)
            {
                var e = slotSelectionEntries[i];
                if (e == null || string.IsNullOrEmpty(e.slotName))
                {
                    continue;
                }

                if (string.Equals(e.slotName, slotName, StringComparison.Ordinal))
                {
                    return e.isSelected;
                }
            }

            return false;
        }

        private void RebuildTriangleSlotOwnership()
        {
            triangleSlotOwnership.Clear();
            slotLocalToBaked.Clear();
            bakedVertexSlotRanges.Clear();

            if (BakedMesh == null || thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return;
            }

            var slots = thisDCA.umaData.umaRecipe.slotDataList;

            // Build baked vertex ranges -> slot mapping using vertexOffset (set by mesh combiner).
            // This is the authoritative source of which vertices belong to which slot.
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s == null || s.Suppressed || s.asset == null || UMAMeshData.IsNullOrEmptyMeshData(s.asset.meshData))
                {
                    continue;
                }

                int vertexCount = s.asset.meshData.vertexCount;
                if (vertexCount <= 0)
                {
                    continue;
                }

                int start = Mathf.Max(0, s.vertexOffset);
                int end = start + vertexCount;
                bakedVertexSlotRanges.Add((start, end, s));
            }

            bakedVertexSlotRanges.Sort((a, b) => a.start.CompareTo(b.start));

            SlotData TryGetSlotForBakedVertex(int bakedVertexIndex) => GetSlotForBakedVertexIndex(bakedVertexIndex);

            // DISCOVER submesh ownership by inspecting actual vertex data in each baked submesh.
            // This is more robust than calculating based on slot order assumptions.
            var bakedSubmeshToSlot = new Dictionary<int, (SlotData slot, int slotSubmeshIndex)>();
            var slotSubmeshCounter = new Dictionary<string, int>(StringComparer.Ordinal); // Track local submesh index per slot

            for (int bakedSm = 0; bakedSm < BakedMesh.subMeshCount; bakedSm++)
            {
                int[] tris = BakedMesh.GetTriangles(bakedSm);
                if (tris == null || tris.Length == 0)
                {
                    continue;
                }

                // Sample vertices from this submesh to determine the owning slot
                SlotData owningSlot = null;
                for (int sampleIdx = 0; sampleIdx < Mathf.Min(tris.Length, 9); sampleIdx++)
                {
                    owningSlot = TryGetSlotForBakedVertex(tris[sampleIdx]);
                    if (owningSlot != null) break;
                }

                if (owningSlot == null)
                {
                    Debug.LogWarning($"[FaceEditorStage] Could not determine slot owner for baked submesh {bakedSm}");
                    continue;
                }

                // Determine local submesh index within this slot
                // (for multi-submesh slots, this is the Nth submesh we've seen from this slot)
                string slotName = owningSlot.slotName;
                if (!slotSubmeshCounter.TryGetValue(slotName, out int localSubmesh))
                {
                    localSubmesh = 0;
                }
                slotSubmeshCounter[slotName] = localSubmesh + 1;

                bakedSubmeshToSlot[bakedSm] = (owningSlot, localSubmesh);

                // Debug logging
               // Debug.Log($"[FaceEditorStage] Baked submesh {bakedSm} -> Slot '{slotName}' localSubmesh {localSubmesh} (triCount={tris.Length / 3})");
            }

            // Now build the triangle ownership maps
            for (int bakedSubmesh = 0; bakedSubmesh < BakedMesh.subMeshCount; bakedSubmesh++)
            {
                int[] bakedTriangles = BakedMesh.GetTriangles(bakedSubmesh);
                if (bakedTriangles == null || bakedTriangles.Length == 0)
                {
                    continue;
                }

                if (!bakedSubmeshToSlot.TryGetValue(bakedSubmesh, out var info) || info.slot == null)
                {
                    continue;
                }

                SlotData slot = info.slot;
                int slotSubmeshIndex = info.slotSubmeshIndex;

                int[] slotBaseTriangles = null;
                try
                {
                    var meshData = slot.asset != null ? slot.asset.meshData : null;
                    if (!UMAMeshData.IsNullOrEmptyMeshData(meshData) && slotSubmeshIndex >= 0 && slotSubmeshIndex < meshData.subMeshCount)
                    {
                        slotBaseTriangles = meshData.submeshes[slotSubmeshIndex].GetBaseTriangles();
                    }
                }
                catch
                {
                    slotBaseTriangles = null;
                }
                if (slotBaseTriangles == null || slotBaseTriangles.Length == 0)
                {
                    continue;
                }

                // Map (slot-local vertex triplet) -> slot-local triangle index
                var baseTriLookup = new Dictionary<(int a, int b, int c), int>(slotBaseTriangles.Length / 3);
                int baseTriCount = slotBaseTriangles.Length / 3;
                for (int i = 0; i < baseTriCount; i++)
                {
                    int ti = i * 3;
                    int a0 = slotBaseTriangles[ti + 0];
                    int a1 = slotBaseTriangles[ti + 1];
                    int a2 = slotBaseTriangles[ti + 2];

                    // Order-independent key (ignore winding)
                    int x = a0, y = a1, z = a2;
                    if (x > y) (x, y) = (y, x);
                    if (y > z) (y, z) = (z, y);
                    if (x > y) (x, y) = (y, x);

                    var key = (x, y, z);
                    if (!baseTriLookup.ContainsKey(key))
                    {
                        baseTriLookup.Add(key, i);
                    }
                }

                int bakedTriCount = bakedTriangles.Length / 3;
                for (int tri = 0; tri < bakedTriCount; tri++)
                {
                    int bi = tri * 3;
                    int b0 = bakedTriangles[bi + 0];
                    int b1 = bakedTriangles[bi + 1];
                    int b2 = bakedTriangles[bi + 2];

                    // Convert baked vertex indices to slot-local vertex indices
                    int s0 = b0 - slot.vertexOffset;
                    int s1 = b1 - slot.vertexOffset;
                    int s2 = b2 - slot.vertexOffset;
                    if (s0 < 0 || s1 < 0 || s2 < 0)
                    {
                        continue;
                    }

                    int x = s0, y = s1, z = s2;
                    if (x > y) (x, y) = (y, x);
                    if (y > z) (y, z) = (z, y);
                    if (x > y) (x, y) = (y, x);

                    if (!baseTriLookup.TryGetValue((x, y, z), out int slotTriangleIndex))
                    {
                        continue;
                    }

                    TriangleKey bakedKey = new TriangleKey(bakedSubmesh, tri);
                    SlotTriangleAddress address = new SlotTriangleAddress
                    {
                        slot = slot,
                        slotSubmeshIndex = slotSubmeshIndex,
                        slotTriangleIndex = slotTriangleIndex
                    };

                    if (!triangleSlotOwnership.ContainsKey(bakedKey))
                    {
                        triangleSlotOwnership.Add(bakedKey, address);
                    }

                    // Build reverse lookup: slot-local -> baked
                    SlotTriangleKey slotKey = new SlotTriangleKey(slot.slotName, slotSubmeshIndex, slotTriangleIndex);
                    if (!slotLocalToBaked.ContainsKey(slotKey))
                    {
                        slotLocalToBaked.Add(slotKey, bakedKey);
                    }
                }
            }

           // Debug.Log($"[FaceEditorStage] RebuildTriangleSlotOwnership complete: {triangleSlotOwnership.Count} triangles, {slotLocalToBaked.Count} reverse mappings, {BakedMesh.subMeshCount} submeshes");
        }

        private void PruneSelectionsForCurrentOwnership()
        {
            bool changed = false;

            if (selectedSlotTrianglesSerialized == null || selectedSlotTrianglesSerialized.Count == 0)
            {
                return;
            }

            // Remove invalid entries and de-dupe. Keep selections even if not currently visible/suppressed.
            var rebuilt = new List<SerializedSlotTriangleKey>(selectedSlotTrianglesSerialized.Count);
            var seen = new HashSet<SlotTriangleKey>();
            for (int i = 0; i < selectedSlotTrianglesSerialized.Count; i++)
            {
                var k = selectedSlotTrianglesSerialized[i];
                if (string.IsNullOrEmpty(k.slotName))
                {
                    changed = true;
                    continue;
                }

                var slotKey = new SlotTriangleKey(k.slotName, k.slotSubmeshIndex, k.slotTriangleIndex);
                if (!seen.Add(slotKey))
                {
                    changed = true;
                    continue;
                }

                rebuilt.Add(k);
            }

            if (changed)
            {
                selectedSlotTrianglesSerialized = rebuilt;
                RebuildSelectedSlotTrianglesFromSerialized();
            }

            if (changed)
            {
                selectionVersion++;
                MarkOverlayMeshDirty();
            }
        }

        private void SelectAllFaces()
        {
            EnsureSlotTriangleCacheBuilt();

            selectedSlotTriangles.Clear();
            if (selectedSlotTrianglesSerialized == null)
            {
                selectedSlotTrianglesSerialized = new List<SerializedSlotTriangleKey>();
            }
            else
            {
                selectedSlotTrianglesSerialized.Clear();
            }

            if (BakedMesh == null || FaceObject == null)
            {
                return;
            }

            for (int i = 0; i < slotTriangleCache.Count; i++)
            {
                var tri = slotTriangleCache[i];
                if (string.IsNullOrEmpty(tri.slotName))
                {
                    continue;
                }

                if (!IsSlotSelected(tri.slotName))
                {
                    continue;
                }

                if (!slotLookupByName.TryGetValue(tri.slotName, out var slot) || slot == null)
                {
                    continue;
                }

                if (slot.Suppressed)
                {
                    continue;
                }

                var slotKey = new SlotTriangleKey(tri.slotName, tri.slotSubmeshIndex, tri.slotTriangleIndex);
                if (!AddSelectedSlotTriangle(slotKey))
                {
                    continue;
                }
            }

            selectionVersion++;
            MarkOverlayMeshDirty();
        }

        private void RecordSelectionUndo(string action)
        {
            Undo.RegisterCompleteObjectUndo(this, action);
            EditorUtility.SetDirty(this);
        }

        private void HandleFacePick(Event evt, SceneView view)
        {
            if (evt == null)
            {
                return;
            }

            if (raycastTestMode && evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt && GUIUtility.hotControl == 0)
            {
                TryPickTestRaycastVertex(evt.mousePosition);
                // Let normal selection continue too.
            }

            if (evt.alt)
            {
                return;
            }

            if (isPointerDown && !isPaintDragging && (evt.type == EventType.Repaint || evt.type == EventType.Layout))
            {
                Handles.BeginGUI();
                Handles.DrawSolidRectangleWithOutline(currentDragRect, rubberBandColor, Color.black);
                Handles.EndGUI();
            }

            if (GUIUtility.hotControl != 0)
            {
                // interacting with UI
                return;
            }

            if (evt.button != 0 && evt.type != EventType.Repaint && evt.type != EventType.Layout)
            {
                return;
            }

            if (meshCollider == null)
            {
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                isPointerDown = true;
                dragStartMousePos = evt.mousePosition;
                currentDragRect = new Rect(dragStartMousePos, Vector2.zero);

                bool modifierPaint = !paintMode && (evt.shift || evt.control);
                isPaintDragging = paintMode || modifierPaint;

                if (isPaintDragging)
                {
                    bool add = paintMode ? paintAddMode : evt.shift;
                    RecordSelectionUndo(add ? "Paint Add Faces" : "Paint Remove Faces");
                    ApplySelectionAtMouse(evt.mousePosition, add);
                    evt.Use();
                }

                return;
            }

            if (!isPointerDown)
            {
                return;
            }

            if (evt.type == EventType.MouseDrag && evt.button == 0)
            {
                if (isPaintDragging)
                {
                    bool add = paintMode ? paintAddMode : evt.shift;
                    ApplySelectionAtMouse(evt.mousePosition, add);
                }
                else
                {
                    UpdateDragRect(evt.mousePosition);
                }

                evt.Use();
                SceneView.RepaintAll();
                return;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                float dragDistance = (evt.mousePosition - dragStartMousePos).magnitude;
                bool wasPaint = isPaintDragging;
                isPointerDown = false;
                isPaintDragging = false;

                if (wasPaint)
                {
                    evt.Use();
                    return;
                }

                UpdateDragRect(evt.mousePosition);
                if (dragDistance <= ClickDragThreshold)
                {
                    RecordSelectionUndo(paintAddMode ? "Click Add Face" : "Click Remove Face");
                    ApplySelectionAtMouse(evt.mousePosition, paintAddMode);
                }
                else
                {
                    RecordSelectionUndo(paintAddMode ? "Rectangle Add Faces" : "Rectangle Remove Faces");
                    ApplyRectangleSelection(currentDragRect, paintAddMode);
                }

                evt.Use();
            }
        }

        private void TryPickTestRaycastVertex(Vector2 mousePosition)
        {
            if (meshCollider == null || FaceObject == null || BakedMesh == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(raycastTestSourceSlot) || raycastTestSourceSlot == "All Slots")
            {
                return;
            }

            SlotData sourceSlot = thisDCA != null && thisDCA.umaData != null && thisDCA.umaData.umaRecipe != null ? thisDCA.umaData.umaRecipe.GetSlot(raycastTestSourceSlot) : null;
            if (sourceSlot == null || sourceSlot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(sourceSlot.asset.meshData))
            {
                return;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            if (!meshCollider.Raycast(ray, out RaycastHit hitInfo, 1000f))
            {
                return;
            }

            int globalTriangleIndex = hitInfo.triangleIndex;
            if (!TryMapGlobalTriangleIndexToSubmesh(globalTriangleIndex, out int submeshIndex, out int triangleIndexOnSubmesh))
            {
                return;
            }

            TriangleKey key = new TriangleKey(submeshIndex, triangleIndexOnSubmesh);
            if (!triangleSlotOwnership.TryGetValue(key, out var owner) || owner.slot == null)
            {
                return;
            }

            if (!string.Equals(owner.slot.slotName, raycastTestSourceSlot, StringComparison.Ordinal))
            {
                return;
            }

            int[] tris = BakedMesh.GetTriangles(submeshIndex);
            int ti = triangleIndexOnSubmesh * 3;
            if (tris == null || ti + 2 >= tris.Length)
            {
                return;
            }

            int v0 = tris[ti];
            int v1 = tris[ti + 1];
            int v2 = tris[ti + 2];

            RefreshBakedMeshCaches();
            Vector3[] verts = bakedVertices;
            if (verts == null)
            {
                return;
            }
            Vector3 hp = FaceObject.transform.InverseTransformPoint(hitInfo.point);
            float d0 = (verts[v0] - hp).sqrMagnitude;
            float d1 = (verts[v1] - hp).sqrMagnitude;
            float d2 = (verts[v2] - hp).sqrMagnitude;

            int chosen = v0;
            float best = d0;
            if (d1 < best)
            {
                best = d1;
                chosen = v1;
            }
            if (d2 < best)
            {
                chosen = v2;
            }

            raycastTestSlotVertexIndex = Mathf.Max(0, chosen - sourceSlot.vertexOffset);
            raycastTestStatusType = MessageType.Info;
            raycastTestStatus = $"Picked vertex on {raycastTestSourceSlot}: slotVertexIndex={raycastTestSlotVertexIndex} (bakedIndex={chosen})";
        }

        private void UpdateDragRect(Vector2 currentMousePos)
        {
            Vector2 size = currentMousePos - dragStartMousePos;
            Vector2 correctedPos = dragStartMousePos;

            if (size.x < 0)
            {
                size.x = Mathf.Abs(size.x);
                correctedPos.x = dragStartMousePos.x - size.x;
            }
            if (size.y < 0)
            {
                size.y = Mathf.Abs(size.y);
                correctedPos.y = dragStartMousePos.y - size.y;
            }

            currentDragRect = new Rect(correctedPos, size);
        }

        private void ApplySelectionAtMouse(Vector2 mousePosition, bool add)
        {
            if (!TryPickSlotTriangleAtMouse(mousePosition, out var slotKey))
            {
                return;
            }

            SetSlotTriangleSelection(slotKey, add);
        }

        private bool TryPickSlotTriangleAtMouse(Vector2 mousePosition, out SlotTriangleKey slotKey)
        {
            slotKey = default;
            if (FaceObject == null || BakedMesh == null)
            {
                return false;
            }

            EnsureSlotTriangleCacheBuilt();

            var slots = thisDCA != null && thisDCA.umaData != null && thisDCA.umaData.umaRecipe != null ? thisDCA.umaData.umaRecipe.slotDataList : null;
            if (slots == null)
            {
                return false;
            }

            Ray worldRay = HandleUtility.GUIPointToWorldRay(mousePosition);
            Ray localRay = new Ray(FaceObject.transform.InverseTransformPoint(worldRay.origin), FaceObject.transform.InverseTransformDirection(worldRay.direction));
            RefreshBakedMeshCaches();
            Vector3[] bakedVertices = this.bakedVertices;
            if (bakedVertices == null)
            {
                return false;
            }

            float bestT = float.PositiveInfinity;
            SlotTriangleKey bestKey = default;

            for (int i = 0; i < slotTriangleCache.Count; i++)
            {
                var tri = slotTriangleCache[i];
                if (string.IsNullOrEmpty(tri.slotName))
                {
                    continue;
                }
                if (!IsSlotSelected(tri.slotName))
                {
                    continue;
                }
                if (!slotLookupByName.TryGetValue(tri.slotName, out var slot) || slot == null)
                {
                    continue;
                }
                if (slot.Suppressed)
                {
                    continue;
                }

                int v0b = slot.vertexOffset + tri.v0Slot;
                int v1b = slot.vertexOffset + tri.v1Slot;
                int v2b = slot.vertexOffset + tri.v2Slot;
                if (v0b < 0 || v1b < 0 || v2b < 0 || v0b >= bakedVertices.Length || v1b >= bakedVertices.Length || v2b >= bakedVertices.Length)
                {
                    continue;
                }

                Vector3 v0 = bakedVertices[v0b];
                Vector3 v1 = bakedVertices[v1b];
                Vector3 v2 = bakedVertices[v2b];

                if (RayIntersectsTriangle(localRay, v0, v1, v2, out float tHit))
                {
                    if (tHit >= 0f && tHit < bestT)
                    {
                        bestT = tHit;
                        bestKey = new SlotTriangleKey(tri.slotName, tri.slotSubmeshIndex, tri.slotTriangleIndex);
                    }
                }
            }

            if (float.IsFinite(bestT))
            {
                slotKey = bestKey;
                return true;
            }

            return false;
        }

        private static bool RayIntersectsTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            t = 0f;

            // Moller-Trumbore
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 pvec = Vector3.Cross(ray.direction, edge2);
            float det = Vector3.Dot(edge1, pvec);
            if (det > -1e-8f && det < 1e-8f)
            {
                return false;
            }
            float invDet = 1f / det;
            Vector3 tvec = ray.origin - v0;
            float u = Vector3.Dot(tvec, pvec) * invDet;
            if (u < 0f || u > 1f)
            {
                return false;
            }
            Vector3 qvec = Vector3.Cross(tvec, edge1);
            float v = Vector3.Dot(ray.direction, qvec) * invDet;
            if (v < 0f || u + v > 1f)
            {
                return false;
            }
            t = Vector3.Dot(edge2, qvec) * invDet;
            return t >= 0f;
        }

        private bool SetSlotTriangleSelection(SlotTriangleKey slotKey, bool add)
        {
            if (string.IsNullOrEmpty(slotKey.slotName))
            {
                return false;
            }
            if (!IsSlotSelected(slotKey.slotName))
            {
                return false;
            }
            if (!slotLookupByName.TryGetValue(slotKey.slotName, out var slot) || slot == null || slot.Suppressed)
            {
                return false;
            }

            if (add)
            {
                if (!AddSelectedSlotTriangle(slotKey))
                {
                    return false;
                }

                selectionVersion++;
                MarkOverlayMeshDirty();
                return true;
            }

            if (!RemoveSelectedSlotTriangle(slotKey))
            {
                return false;
            }
            selectionVersion++;
            MarkOverlayMeshDirty();
            return true;
        }

        private void ApplyRectangleSelection(Rect selectionRect, bool add)
        {
            if (selectionRect.width <= 0f || selectionRect.height <= 0f || FaceObject == null || BakedMesh == null)
            {
                return;
            }

            EnsureSlotTriangleCacheBuilt();

            Matrix4x4 matrix = FaceObject.transform.localToWorldMatrix;
            Camera cam = openedSceneView != null ? openedSceneView.camera : (SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null);
            Vector3 camForward = cam != null ? cam.transform.forward : Vector3.forward;
            RefreshBakedMeshCaches();
            Vector3[] bakedVertices = this.bakedVertices;
            if (bakedVertices == null)
            {
                return;
            }

            for (int i = 0; i < slotTriangleCache.Count; i++)
            {
                var tri = slotTriangleCache[i];
                if (string.IsNullOrEmpty(tri.slotName))
                {
                    continue;
                }

                if (!IsSlotSelected(tri.slotName))
                {
                    continue;
                }

                if (!slotLookupByName.TryGetValue(tri.slotName, out var slot) || slot == null)
                {
                    continue;
                }

                if (slot.Suppressed)
                {
                    continue;
                }

                int v0b = slot.vertexOffset + tri.v0Slot;
                int v1b = slot.vertexOffset + tri.v1Slot;
                int v2b = slot.vertexOffset + tri.v2Slot;
                if (v0b < 0 || v1b < 0 || v2b < 0 || v0b >= bakedVertices.Length || v1b >= bakedVertices.Length || v2b >= bakedVertices.Length)
                {
                    continue;
                }

                Vector3 w0 = matrix.MultiplyPoint3x4(bakedVertices[v0b]);
                Vector3 w1 = matrix.MultiplyPoint3x4(bakedVertices[v1b]);
                Vector3 w2 = matrix.MultiplyPoint3x4(bakedVertices[v2b]);

                if (rubberBandCullBackfaces)
                {
                    Vector3 n = Vector3.Cross(w1 - w0, w2 - w0);
                    if (Vector3.Dot(n, camForward) >= 0f)
                    {
                        continue;
                    }
                }

                Vector2 p0 = HandleUtility.WorldToGUIPoint(w0);
                Vector2 p1 = HandleUtility.WorldToGUIPoint(w1);
                Vector2 p2 = HandleUtility.WorldToGUIPoint(w2);
                Vector2 center = (p0 + p1 + p2) / 3f;

                if (selectionRect.Contains(p0) || selectionRect.Contains(p1) || selectionRect.Contains(p2) || selectionRect.Contains(center))
                {
                    SetSlotTriangleSelection(new SlotTriangleKey(tri.slotName, tri.slotSubmeshIndex, tri.slotTriangleIndex), add);
                }
            }
        }

        private bool TryMapGlobalTriangleIndexToSubmesh(int globalTriangleIndex, out int submeshIndex, out int triangleIndexOnSubmesh)
        {
            submeshIndex = 0;
            triangleIndexOnSubmesh = 0;
            if (BakedMesh == null || globalTriangleIndex < 0)
            {
                return false;
            }

            int running = 0;
            for (int sm = 0; sm < BakedMesh.subMeshCount; sm++)
            {
                int triCount = BakedMesh.GetTriangles(sm).Length / 3;
                if (globalTriangleIndex >= running && globalTriangleIndex < running + triCount)
                {
                    submeshIndex = sm;
                    triangleIndexOnSubmesh = globalTriangleIndex - running;
                    return true;
                }
                running += triCount;
            }

            return false;
        }

        private void ToggleFaceSelection(int submeshIndex, int triangleIndex)
        {
            TriangleKey key = new TriangleKey(submeshIndex, triangleIndex);
            SetTriangleSelection(key, paintAddMode);
        }

        private bool SetTriangleSelection(TriangleKey key, bool add)
        {
            if (!triangleSlotOwnership.TryGetValue(key, out var owner))
            {
                return false;
            }

            string ownerSlotName = owner.slot != null ? owner.slot.slotName : null;
            if (!IsSlotSelected(ownerSlotName))
            {
                return false;
            }

            SlotTriangleKey slotKey = new SlotTriangleKey(ownerSlotName, owner.slotSubmeshIndex, owner.slotTriangleIndex);
            if (add)
            {
                if (!AddSelectedSlotTriangle(slotKey))
                {
                    return false;
                }

                selectionVersion++;
                MarkOverlayMeshDirty();
                return true;
            }

            if (!RemoveSelectedSlotTriangle(slotKey))
            {
                return false;
            }
            selectionVersion++;
            MarkOverlayMeshDirty();
            return true;
        }

        private void LoadSelections()
        {
            selectedSlotTriangles.Clear();
            if (selectedSlotTrianglesSerialized == null)
            {
                selectedSlotTrianglesSerialized = new List<SerializedSlotTriangleKey>();
            }
            else
            {
                selectedSlotTrianglesSerialized.Clear();
            }
            if (BakedMesh == null)
            {
                selectionVersion++;
                MarkOverlayMeshDirty();
                return;
            }

            var assetsToLoad = new List<MeshHideAsset>(1);
            if (CurrentHideAsset != null)
            {
                assetsToLoad.Add(CurrentHideAsset);
            }
            else if (CurrentHideCollection != null && CurrentHideCollection.Assets != null)
            {
                for (int i = 0; i < CurrentHideCollection.Assets.Count; i++)
                {
                    var a = CurrentHideCollection.Assets[i];
                    if (a != null)
                    {
                        assetsToLoad.Add(a);
                    }
                }
            }

            if (assetsToLoad.Count > 0 && thisDCA != null && thisDCA.umaData != null && thisDCA.umaData.umaRecipe != null && thisDCA.umaData.umaRecipe.slotDataList != null)
            {
                var slots = thisDCA.umaData.umaRecipe.slotDataList;
                for (int ai = 0; ai < assetsToLoad.Count; ai++)
                {
                    MeshHideAsset mha = assetsToLoad[ai];
                    if (mha == null)
                    {
                        continue;
                    }

                    string slotName = mha.AssetSlotName;
                    if (string.IsNullOrEmpty(slotName))
                    {
                        continue;
                    }

                    SlotData slot = null;
                    for (int si = 0; si < slots.Length; si++)
                    {
                        var s = slots[si];
                        if (s == null) continue;
                        if (string.Equals(s.slotName, slotName, StringComparison.Ordinal))
                        {
                            slot = s;
                            break;
                        }
                    }

                    // Slot missing or not visible in this UMA: leave as-is.
                    if (slot == null)
                    {
                        continue;
                    }

                    if (!TryGetSlotSubmeshRange(slot, out int startSubmesh, out int submeshCount))
                    {
                        continue;
                    }

                    for (int localSm = 0; localSm < submeshCount; localSm++)
                    {
                        var flags = GetTriangleFlagsSafe(mha, localSm);
                        if (flags == null)
                        {
                            continue;
                        }
                        int triCount = flags.Count;
                        for (int t = 0; t < triCount; t++)
                        {
                            if (!flags[t])
                            {
                                continue;
                            }

                            AddSelectedSlotTriangle(new SlotTriangleKey(slotName, localSm, t));
                        }
                    }

                    // Ensure loaded slots are enabled for display/editing in this stage.
                    if (slotSelectionEntries != null)
                    {
                        for (int i = 0; i < slotSelectionEntries.Count; i++)
                        {
                            var e = slotSelectionEntries[i];
                            if (e != null && string.Equals(e.slotName, slotName, StringComparison.Ordinal))
                            {
                                e.isSelected = true;
                                break;
                            }
                        }
                    }
                }
            }

            // Ensure hash set matches serialized (AddSelected... already keeps them in sync; this is a safety net).
            RebuildSelectedSlotTrianglesFromSerialized();

            selectionVersion++;
            MarkOverlayMeshDirty();
        }

        private void SaveSelections()
        {
            if (selectedSlotTriangles == null || selectedSlotTriangles.Count == 0)
            {
                EditorUtility.DisplayDialog("Mesh Hide Editor", "No selected triangles to save.", "OK");
                return;
            }

            string defaultFolder = GetLastMeshHideAssetFolder();
            string basePath = EditorUtility.SaveFilePanelInProject(
                "Save MeshHideAssets",
                "MeshHideAssets",
                "asset",
                "Choose a base name for the MeshHideAsset (single) or MeshHideAssetCollection (multiple)",
                defaultFolder);
            if (string.IsNullOrEmpty(basePath))
            {
                return;
            }

            string folderRelative = System.IO.Path.GetDirectoryName(basePath).Replace('\\', '/');
            if (string.IsNullOrEmpty(folderRelative))
            {
                folderRelative = "Assets";
            }
            SetLastMeshHideAssetFolder(folderRelative);

            string baseFileName = System.IO.Path.GetFileNameWithoutExtension(basePath);
            if (string.IsNullOrEmpty(baseFileName))
            {
                baseFileName = "MeshHideAssets";
            }

            Dictionary<string, List<SlotTriangleKey>> facesBySlot = new Dictionary<string, List<SlotTriangleKey>>(StringComparer.Ordinal);
            foreach (var k in selectedSlotTriangles)
            {
                if (string.IsNullOrEmpty(k.slotName))
                {
                    continue;
                }

                if (!facesBySlot.TryGetValue(k.slotName, out var list))
                {
                    list = new List<SlotTriangleKey>();
                    facesBySlot.Add(k.slotName, list);
                }

                list.Add(k);
            }

            if (facesBySlot.Count == 0)
            {
                EditorUtility.DisplayDialog("Mesh Hide Editor", "No slot selections were found to save.", "OK");
                return;
            }

            bool createCollection = facesBySlot.Count > 1;
            string collectionPath = folderRelative + "/" + baseFileName + ".asset";
            bool overwriteAll = false;
            if (createCollection)
            {
                UMA.MeshHideAssetCollection existingCollection = AssetDatabase.LoadAssetAtPath<UMA.MeshHideAssetCollection>(collectionPath);
                if (existingCollection != null)
                {
                    if (!EditorUtility.DisplayDialog("Mesh Hide Editor", $"Overwrite existing MeshHideAssetCollection?\n\n{collectionPath}\n\nThis will overwrite all generated MeshHideAssets in this save operation.", "Overwrite", "Cancel"))
                    {
                        return;
                    }

                    overwriteAll = true;
                }
            }

            UMA.MeshHideAssetCollection collection = null;
            if (createCollection)
            {
                collection = AssetDatabase.LoadAssetAtPath<UMA.MeshHideAssetCollection>(collectionPath);
                if (collection == null)
                {
                    collection = ScriptableObject.CreateInstance<UMA.MeshHideAssetCollection>();
                    AssetDatabase.CreateAsset(collection, collectionPath);
                }
            }
            else if (CurrentHideCollection != null)
            {
                // If the stage was opened on a collection but there is only one slot represented, still update the collection.
                collection = CurrentHideCollection;
            }

            foreach (var pair in facesBySlot)
            {
                if (!slotLookupByName.TryGetValue(pair.Key, out var slot) || slot == null)
                {
                    continue;
                }

                string path;
                if (createCollection)
                {
                    string safeSlotName = string.IsNullOrEmpty(pair.Key) ? "Slot" : pair.Key;
                    foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                    {
                        safeSlotName = safeSlotName.Replace(c, '_');
                    }
                    path = folderRelative + "/" + baseFileName + "_" + safeSlotName + ".asset";
                }
                else
                {
                    path = folderRelative + "/" + baseFileName + ".asset";
                }

                MeshHideAsset existing = AssetDatabase.LoadAssetAtPath<MeshHideAsset>(path);
                if (!overwriteAll && existing != null)
                {
                    if (!EditorUtility.DisplayDialog("Mesh Hide Editor", $"Overwrite existing MeshHideAsset?\n\n{path}", "Overwrite", "Cancel"))
                    {
                        return;
                    }
                }

                MeshHideAsset mha = existing;
                if (mha == null)
                {
                    mha = ScriptableObject.CreateInstance<MeshHideAsset>();
                    AssetDatabase.CreateAsset(mha, path);
                }

                mha.AssetSlotName = slot.slotName;
                mha.asset = slot.asset;

                int submeshCount = 1;
                if (slot.asset != null && !UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                {
                    submeshCount = Mathf.Max(1, slot.asset.meshData.subMeshCount);
                }

                EnsureTriangleFlagsAllocated(mha, slot, submeshCount);

            // Clear existing
                for (int localSm = 0; localSm < submeshCount; localSm++)
                {
                    var flags = mha.triangleFlags[localSm];
                    if (flags != null)
                    {
                        flags.SetAll(false);
                    }
                }

            // Write selections
                var slotFaces = pair.Value;
                for (int i = 0; i < slotFaces.Count; i++)
                {
                    var f = slotFaces[i];
                    int localSm = f.slotSubmeshIndex;
                    if (localSm < 0 || localSm >= submeshCount)
                    {
                        continue;
                    }

                    var flags = mha.triangleFlags[localSm];
                    if (flags == null)
                    {
                        continue;
                    }

                    if (f.slotTriangleIndex >= 0 && f.slotTriangleIndex < flags.Count)
                    {
                        flags[f.slotTriangleIndex] = true;
                    }
                }

#if UNITY_EDITOR
                mha.UpdateEditorHashAndUVMaskFromFlags();
#endif

                EditorUtility.SetDirty(mha);
                AssetDatabase.SaveAssetIfDirty(mha);

                if (collection != null)
                {
                    collection.AddOrUpdate(mha);
                    EditorUtility.SetDirty(collection);
                }
            }

            if (collection != null)
            {
                AssetDatabase.SaveAssetIfDirty(collection);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            hasSaved = true;

            if (collection != null)
            {
                EditorUtility.DisplayDialog("Mesh Hide Editor", $"Saved MeshHideAssetCollection:\n\n{collectionPath}", "OK");
            }
        }

        private static void EnsureTriangleFlagsAllocated(MeshHideAsset mha, SlotData slot, int submeshCount)
        {
            if (mha == null)
            {
                return;
            }

            int[] triCounts = new int[submeshCount];
            var asset = slot != null ? slot.asset : null;
            if (asset != null && !UMAMeshData.IsNullOrEmptyMeshData(asset.meshData))
            {
                for (int sm = 0; sm < submeshCount; sm++)
                {
                    int triLen = 0;
                    if (sm < asset.meshData.subMeshCount)
                    {
                        int[] tris = SlotToMesh.GetTriangles(asset.meshData, sm);
                        triLen = tris != null ? tris.Length : 0;
                    }
                    triCounts[sm] = triLen / 3;
                }
            }

            bool needsAlloc = mha.triangleFlags == null || mha.triangleFlags.Length != submeshCount;
            if (!needsAlloc)
            {
                for (int sm = 0; sm < submeshCount; sm++)
                {
                    int expected = triCounts[sm];
                    if (expected <= 0) continue;
                    if (mha.triangleFlags[sm] == null || mha.triangleFlags[sm].Count != expected)
                    {
                        needsAlloc = true;
                        break;
                    }
                }
            }

            if (!needsAlloc)
            {
                return;
            }

            var flags = new BitArray[submeshCount];
            for (int sm = 0; sm < submeshCount; sm++)
            {
                int count = Mathf.Max(0, triCounts[sm]);
                flags[sm] = new BitArray(count);
            }

            // Assign via reflection-safe internal field access is not available; use serialization callback structure.
            // MeshHideAsset exposes triangleFlags getter only, but it stores the backing field internally.
            // We can set it using Unity's serialization by directly writing the private field via reflection.
            var field = typeof(MeshHideAsset).GetField("_triangleFlags", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(mha, flags);
            }
        }

        public void RebuildMesh(bool recipeChanged, bool buildCollisionMesh = true)
        {
            if (thisDCA == null || thisDCA.umaData == null)
            {
                return;
            }

            UMAGeneratorBuiltin gb = thisDCA.umaData.umaGenerator as UMAGeneratorBuiltin;
            if (gb != null)
            {
                gb.Clear();
                if (recipeChanged)
                {
                    var suppressed = SaveSuppressedSlots();
                    thisDCA.BuildCharacter(true, true);
                    RestoreSuppressedSlots(suppressed);
                }

                // Slots can be regenerated during generator runs; always dirty mesh/materials.
                thisDCA.umaData.Dirty(false, true, true);
                gb.GenerateSingleUMA(thisDCA.umaData, true);
            }
            else
            {
                // Fallback: still attempt a rebuild if a different generator is in use.
                thisDCA.umaData.Dirty(false, true, true);
                thisDCA.BuildCharacter(true, true);
            }

            SkinnedMeshRenderer smr = thisDCA.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null)
            {
                return;
            }

            if (BakedMesh != null)
            {
                DestroyImmediate(BakedMesh);
            }

            BakedMesh = new Mesh();
            BakedMesh.name = "BakedMesh";
            smr.BakeMesh(BakedMesh, true);
            RefreshBakedMeshCaches();

            if (FaceObject != null)
            {
                var mf = FaceObject.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    mf.sharedMesh = BakedMesh;
                }

                meshCollider = FaceObject.GetComponent<MeshCollider>();
                if (meshCollider != null)
                {
                    meshCollider.sharedMesh = null;
                    meshCollider.sharedMesh = BakedMesh;
                }
            }

            MarkOverlayMeshDirty();

            // Slot suppression can change mesh topology/submesh mapping. Ensure the ownership map and selections
            // are rebuilt against the current baked mesh so overlay lines don't reference stale triangles.
            RebuildTriangleSlotOwnership();
            PruneSelectionsForCurrentOwnership();
            selectionVersion++;
            MarkOverlayMeshDirty();

            RefreshVisibleSlotLists();
        }

        private List<string> SaveSuppressedSlots()
        {
            List<string> suppressed = new List<string>();
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return suppressed;
            }

            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot != null && slot.Suppressed)
                {
                    suppressed.Add(slot.slotName);
                }
            }
            return suppressed;
        }

        private void RestoreSuppressedSlots(List<string> suppressed)
        {
            if (suppressed == null || suppressed.Count == 0)
            {
                return;
            }
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return;
            }

            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot != null && suppressed.Contains(slot.slotName))
                {
                    slot.Suppressed = true;
                }
            }
        }
    }
}
