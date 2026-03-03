using System;
using System.Collections.Generic;
using System.Linq;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.Rendering;

namespace UMA
{
    public class FaceEditorStage : PreviewSceneStage
    {
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

        [Serializable]
        private class SlotSelectionEntry
        {
            public string slotName;
            public bool isSelected;
        }

        private enum selectMode { Add, Remove, InvertSelection, HideFaces, UnhideFaces, ToggleHide };

        [Serializable]
        private class FaceSelection
        {
            public int submeshIndex;
            public int triangleIndex; // index in submesh triangles array / 3
            public string slotName;
            public int slotSubmeshIndex;
            public int slotTriangleIndex;
            public bool isHidden;
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

        [SerializeField]
        private List<FaceSelection> SelectedFaces = new List<FaceSelection>();

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

        private Rect leftPanelRect;
        private Vector2 lastSceneViewSize = Vector2.zero;
        private float cachedVisibilityHeight = -1f;
        private float cachedMeshHideAssetsHeight = 260f;

        private string[] selectFrom = new string[] { "All Slots" };
        private int selectionSlot = 0;
        private string[] visibleSelectFrom = new string[] { "All Slots" };

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
        private int selectionVersion;

        private static readonly Color OverlayFillGreen = new Color(0f, 1f, 0f, 0.33f);
        private static readonly Color OverlayLineGreen = new Color(0f, 1f, 0f, 1f);
        private static readonly Color OverlayFillRed = new Color(1f, 0f, 0f, 1f);
        private static readonly Color OverlayLineRed = new Color(0f, 0f, 0f, 1f);
        private const float OverlayVertexOffset = 0.0005f;

        private const string MeshHideAssetFolderPrefKeyPrefix = "UMA.FaceEditorStage.MeshHideAssetFolder.";

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

            ValidateMeshHideAssets();
            LoadSelections();
            MarkOverlayMeshDirty();

            return true;
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

                int slotSubmeshCount = 1;
                var asset = slot.asset;
                if (asset != null && asset.meshData != null)
                {
                    slotSubmeshCount = Mathf.Max(1, asset.meshData.subMeshCount);
                }

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

            ClearOverlayMeshCache();

        }

        private void OnUndoRedoSelection()
        {
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

            DrawMeshHideOverlay(sceneView);
            HandleFacePick(Event.current, sceneView);
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
                overlayLineMaterial.SetColor("_Color", OverlayLineRed);
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

            HashSet<TriangleKey> selectedKeys = new HashSet<TriangleKey>();
            for (int i = 0; i < SelectedFaces.Count; i++)
            {
                var f = SelectedFaces[i];
                selectedKeys.Add(new TriangleKey(f.submeshIndex, f.triangleIndex));
            }

            Vector3[] vertices = BakedMesh.vertices;
            for (int sm = startSubmesh; sm < endSubmeshExclusive; sm++)
            {
                int[] triangles = BakedMesh.GetTriangles(sm);
                if (triangles == null || triangles.Length == 0)
                {
                    continue;
                }

                int triCount = triangles.Length / 3;
                for (int tri = 0; tri < triCount; tri++)
                {
                    TriangleKey key = new TriangleKey(sm, tri);
                    if (!triangleSlotOwnership.TryGetValue(key, out var owner))
                    {
                        continue;
                    }

                    string slotName = owner.slot != null ? owner.slot.slotName : null;
                    if (!IsSlotSelected(slotName))
                    {
                        continue;
                    }

                    bool isHidden = selectedKeys.Contains(key);

                    int ti = tri * 3;
                    Vector3 v0 = vertices[triangles[ti]];
                    Vector3 v1 = vertices[triangles[ti + 1]];
                    Vector3 v2 = vertices[triangles[ti + 2]];

                    Vector3 n = Vector3.Cross(v1 - v0, v2 - v0);
                    if (n.sqrMagnitude > 1e-12f)
                    {
                        n.Normalize();
                        Vector3 offset = n * OverlayVertexOffset;
                        v0 += offset;
                        v1 += offset;
                        v2 += offset;
                    }

                    List<Vector3> lineTarget = hiddenLineVertices;

                    if (isHidden)
                    {
                        hiddenFillVertices.Add(v0);
                        hiddenFillVertices.Add(v1);
                        hiddenFillVertices.Add(v2);
                    }

                    lineTarget.Add(v0);
                    lineTarget.Add(v1);
                    lineTarget.Add(v1);
                    lineTarget.Add(v2);
                    lineTarget.Add(v2);
                    lineTarget.Add(v0);
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

            int count = 1;
            var asset = slot.asset;
            if (asset != null && asset.meshData != null)
            {
                count = Mathf.Max(1, asset.meshData.subMeshCount);
            }

            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            int running = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s == null) continue;
                int sCount = 1;
                var sAsset = s.asset;
                if (sAsset != null && sAsset.meshData != null)
                {
                    sCount = Mathf.Max(1, sAsset.meshData.subMeshCount);
                }

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

            FaceEditorToolsWindow = GUILayout.Window(FaceEditorToolsWindowID, FaceEditorToolsWindow, DoToolsWindow, "Face Tools");
            VisibleWearablesWindow = GUILayout.Window(VisibleWearablesID, VisibleWearablesWindow, DoVisibilityWindow, "Visibility");
            MeshHideAssetsWindow = GUILayout.Window(MeshHideAssetsWindowID, MeshHideAssetsWindow, DoMeshHideAssetsWindow, "Mesh Hide Assets");
        }

        public void AdjustWindowRects()
        {
            if (openedSceneView == null)
            {
                return;
            }

            Rect usableRect = GetSceneViewUsableRect(openedSceneView);
            Vector2 sceneSize = usableRect.size;
            if (sceneSize != lastSceneViewSize)
            {
                lastSceneViewSize = sceneSize;
                cachedVisibilityHeight = -1f;
            }

            float toolsHeight = Mathf.Clamp(FaceEditorToolsWindow.height, 180f, sceneSize.y - 40f);
            float availableBelowTools = Mathf.Max(0f, sceneSize.y - toolsHeight - (LeftPanelPadding * 4f));
            float visibilityHeight = GetVisibilitySectionHeightEstimate(availableBelowTools);

            float meshHideHeight = Mathf.Clamp(cachedMeshHideAssetsHeight, 200f, Mathf.Max(200f, availableBelowTools - visibilityHeight));

            float width = Mathf.Clamp(FaceEditorToolsWindow.width, LeftPanelWidthMin, LeftPanelWidthMax);

            leftPanelRect = new Rect(usableRect.x + LeftPanelPadding, usableRect.y + LeftPanelPadding, width, toolsHeight + visibilityHeight + meshHideHeight + (LeftPanelPadding * 2f));
            FaceEditorToolsWindow = new Rect(leftPanelRect.x, leftPanelRect.y, width, toolsHeight);
            VisibleWearablesWindow = new Rect(leftPanelRect.x, leftPanelRect.y + toolsHeight + LeftPanelPadding, width, visibilityHeight);
            MeshHideAssetsWindow = new Rect(leftPanelRect.x, VisibleWearablesWindow.yMax + LeftPanelPadding, width, meshHideHeight);
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

            GUI.DragWindow();
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
            using (new GUILayout.VerticalScope())
            {
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
                        SelectedFaces.Clear();
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
                GUILayout.Label($"Selected Faces: {SelectedFaces.Count}");

                if (GUILayout.Button("Create MeshHideAssets (Split by Slot)"))
                {
                    SaveSelections();
                }

                if (GUILayout.Button("Close"))
                {
                    StageUtility.GoBackToPreviousStage();
                }
            }

            GUI.DragWindow();
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

            Vector3[] vertices = BakedMesh.vertices;
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
                GUI.DragWindow();
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

            GUI.DragWindow();
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

            if (BakedMesh == null || thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null)
            {
                return;
            }

            var slots = thisDCA.umaData.umaRecipe.slotDataList;
            int[] runningBySubmesh = new int[Mathf.Max(1, BakedMesh.subMeshCount)];

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null || slot.asset == null || slot.asset.meshData == null)
                {
                    continue;
                }

                int slotSubmeshCount = Mathf.Max(1, slot.asset.meshData.subMeshCount);
                for (int localSm = 0; localSm < slotSubmeshCount; localSm++)
                {
                    int bakedSubmesh = Mathf.Clamp(localSm, 0, BakedMesh.subMeshCount - 1);
                    if (bakedSubmesh < 0 || bakedSubmesh >= BakedMesh.subMeshCount)
                    {
                        continue;
                    }

                    int[] slotTriangles = SlotToMesh.GetTriangles(slot.asset.meshData, localSm);
                    int triCount = slotTriangles != null ? slotTriangles.Length / 3 : 0;
                    if (triCount <= 0)
                    {
                        continue;
                    }

                    int[] bakedTriangles = BakedMesh.GetTriangles(bakedSubmesh);
                    int bakedTriCount = bakedTriangles != null ? bakedTriangles.Length / 3 : 0;
                    int startTri = runningBySubmesh[bakedSubmesh];
                    int mapCount = Mathf.Max(0, Mathf.Min(triCount, bakedTriCount - startTri));

                    for (int t = 0; t < mapCount; t++)
                    {
                        TriangleKey key = new TriangleKey(bakedSubmesh, startTri + t);
                        if (!triangleSlotOwnership.ContainsKey(key))
                        {
                            triangleSlotOwnership.Add(key, new SlotTriangleAddress
                            {
                                slot = slot,
                                slotSubmeshIndex = localSm,
                                slotTriangleIndex = t
                            });
                        }
                    }

                    runningBySubmesh[bakedSubmesh] = startTri + triCount;
                }
            }
        }

        private void PruneSelectionsForCurrentOwnership()
        {
            bool changed = false;

            for (int i = SelectedFaces.Count - 1; i >= 0; i--)
            {
                var f = SelectedFaces[i];
                TriangleKey key = new TriangleKey(f.submeshIndex, f.triangleIndex);
                if (!triangleSlotOwnership.TryGetValue(key, out var owner))
                {
                    SelectedFaces.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (!string.Equals(f.slotName, owner.slot != null ? owner.slot.slotName : null, StringComparison.Ordinal))
                {
                    f.slotName = owner.slot != null ? owner.slot.slotName : null;
                    f.slotSubmeshIndex = owner.slotSubmeshIndex;
                    f.slotTriangleIndex = owner.slotTriangleIndex;
                    changed = true;
                }
            }

            if (changed)
            {
                selectionVersion++;
                MarkOverlayMeshDirty();
            }
        }

        private void SelectAllFaces()
        {
            SelectedFaces.Clear();

            if (BakedMesh == null)
            {
                return;
            }

            foreach (var kvp in triangleSlotOwnership)
            {
                var owner = kvp.Value;
                string slotName = owner.slot != null ? owner.slot.slotName : null;
                if (!IsSlotSelected(slotName))
                {
                    continue;
                }

                SelectedFaces.Add(new FaceSelection
                {
                    submeshIndex = kvp.Key.submeshIndex,
                    triangleIndex = kvp.Key.triangleIndex,
                    slotName = slotName,
                    slotSubmeshIndex = owner.slotSubmeshIndex,
                    slotTriangleIndex = owner.slotTriangleIndex,
                    isHidden = true
                });
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
            if (!TryGetTriangleKeyAtMouse(mousePosition, out var key))
            {
                return;
            }

            SetTriangleSelection(key, add);
        }

        private bool TryGetTriangleKeyAtMouse(Vector2 mousePosition, out TriangleKey key)
        {
            key = default;
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            if (!meshCollider.Raycast(ray, out RaycastHit hitInfo, 1000f))
            {
                return false;
            }

            if (!TryMapGlobalTriangleIndexToSubmesh(hitInfo.triangleIndex, out int submeshIndex, out int triangleIndexOnSubmesh))
            {
                return false;
            }

            key = new TriangleKey(submeshIndex, triangleIndexOnSubmesh);
            return true;
        }

        private void ApplyRectangleSelection(Rect selectionRect, bool add)
        {
            if (selectionRect.width <= 0f || selectionRect.height <= 0f || FaceObject == null || BakedMesh == null)
            {
                return;
            }

            Matrix4x4 matrix = FaceObject.transform.localToWorldMatrix;
            Camera cam = openedSceneView != null ? openedSceneView.camera : (SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null);
            Vector3 camForward = cam != null ? cam.transform.forward : Vector3.forward;
            Vector3[] vertices = BakedMesh.vertices;
            Dictionary<int, int[]> trianglesBySubmesh = new Dictionary<int, int[]>();

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
                    SetTriangleSelection(pair.Key, add);
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

            int idx = SelectedFaces.FindIndex(x => x.submeshIndex == key.submeshIndex && x.triangleIndex == key.triangleIndex);
            if (add)
            {
                if (idx >= 0)
                {
                    return false;
                }

                SelectedFaces.Add(new FaceSelection
                {
                    submeshIndex = key.submeshIndex,
                    triangleIndex = key.triangleIndex,
                    slotName = ownerSlotName,
                    slotSubmeshIndex = owner.slotSubmeshIndex,
                    slotTriangleIndex = owner.slotTriangleIndex,
                    isHidden = true
                });

                selectionVersion++;
                MarkOverlayMeshDirty();
                return true;
            }

            if (idx < 0)
            {
                return false;
            }

            SelectedFaces.RemoveAt(idx);
            selectionVersion++;
            MarkOverlayMeshDirty();
            return true;
        }

        private void LoadSelections()
        {
            SelectedFaces.Clear();
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

                        int bakedSm = startSubmesh + localSm;
                        int triCount = flags.Count;
                        for (int t = 0; t < triCount; t++)
                        {
                            if (!flags[t])
                            {
                                continue;
                            }

                            TriangleKey key = new TriangleKey(bakedSm, t);
                            if (!triangleSlotOwnership.TryGetValue(key, out var owner))
                            {
                                continue;
                            }

                            SelectedFaces.Add(new FaceSelection
                            {
                                submeshIndex = bakedSm,
                                triangleIndex = t,
                                slotName = owner.slot != null ? owner.slot.slotName : slotName,
                                slotSubmeshIndex = owner.slotSubmeshIndex,
                                slotTriangleIndex = owner.slotTriangleIndex,
                                isHidden = true
                            });
                        }
                    }
                }
            }

            selectionVersion++;
            MarkOverlayMeshDirty();
        }

        private void SaveSelections()
        {
            if (SelectedFaces == null || SelectedFaces.Count == 0)
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

            Dictionary<string, List<FaceSelection>> facesBySlot = new Dictionary<string, List<FaceSelection>>(StringComparer.Ordinal);
            for (int i = 0; i < SelectedFaces.Count; i++)
            {
                var f = SelectedFaces[i];
                if (f == null || string.IsNullOrEmpty(f.slotName))
                {
                    continue;
                }

                if (!facesBySlot.TryGetValue(f.slotName, out var list))
                {
                    list = new List<FaceSelection>();
                    facesBySlot.Add(f.slotName, list);
                }

                list.Add(f);
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
                if (slot.asset != null && slot.asset.meshData != null)
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
            if (asset != null && asset.meshData != null)
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
