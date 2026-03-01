using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UMA;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
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
        PhysicsScene phyScene;

        // Edit Options
        float HandlesSize = 0.01f;
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
                    modifierEditor.Repaint();
                }
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
        private bool paintModeSet = false;
        private bool paintModeState = false;
        private readonly HashSet<string> paintedVerticesThisStroke = new HashSet<string>();
        // End Options

        const int VertexEditorToolsWindowID = 0x1234;
        const int VisibleWearablesID = 0x1235;
        private const float LeftPanelWidthMin = 320f;
        private const float LeftPanelWidthMax = 460f;
        private const float LeftPanelPadding = 6f;
        private const float LeftPanelHeaderHeight = 18f;

        public Vector2 VertexEditorScrollLocation = Vector2.zero;
        public Rect VertexEditorToolsWindow = new Rect(10, 10, 300, 300);


        public Vector2 VisibleWearablesLocation = Vector2.zero;
        public Rect VisibleWearablesWindow = new Rect(10, 310, 250, 300);
        private Rect leftPanelRect;
        private Vector2 lastSceneViewSize = Vector2.zero;
        private float cachedVisibilityHeight = -1f;

        private MeshModifierEditor modifierEditor;
        public bool rectSelect = false;
        public bool painting = false;
        private bool pendingStateClickAction = false;
        private Vector2 pendingStateClickStart = Vector2.zero;
        public Vector2 RectStart = Vector2.zero;
        public MeshModifier Currentmodifier;
        public Type[] ModifierTypes;



     [SerializeReference]
        private List<VertexAdjustment> _adjustments = new List<VertexAdjustment>();

        private bool selectionUndoArmed = false;

        private bool IsPaintModeEnabled
        {
            get { return currentDefineMode == DefineMode.DefineVertexSet ? paintModeSet : paintModeState; }
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

            int sourceVertexCount = sourceSlot.asset != null && sourceSlot.asset.meshData != null
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
            var verts = BakedMesh.vertices;
            var normals = BakedMesh.normals;
            var tris = BakedMesh.triangles;
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
        /// Möller–Trumbore ray-triangle intersection algorithm.
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

        private vertexState currentState;


        GUIStyle HelpBoxStyle;
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
            if (bakedIndex < 0 || bakedIndex >= BakedMesh.vertexCount)
            {
                return Vector3.zero;
            }
            return VertexObject.transform.TransformPoint(BakedMesh.vertices[bakedIndex]);
        }

        private bool IsSelectableSlot(SlotData slot)
        {
            return slot != null &&
                   slot.asset != null &&
                   slot.asset.meshData != null &&
                   !slot.Suppressed &&
                   !slot.asset.isUtilitySlot;
        }

        private bool TryGetSlotForBakedVertex(int bakedVertexIndex, out SlotData foundSlot, out int slotVertexIndex)
        {
            foundSlot = null;
            slotVertexIndex = -1;

            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null)
            {
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
            VertexEditorStage stage = ScriptableObject.CreateInstance<VertexEditorStage>();
            stage.titleContent = new GUIContent();
            stage.titleContent.text = "Mesh Modifier Editor";
            stage.titleContent.image = EditorGUIUtility.IconContent("GameObject Icon").image;
            stage.thisDCA = DCA;
            stage.Currentmodifier = modifier;
            StageUtility.GoToStage(stage, true);
            return stage;
        }

        public VertexSelection GetSelectedVertex()
        {
            if (currentSelected >= 0 && currentSelected < SelectedVertexes.Count)
            {
                return SelectedVertexes[currentSelected];
            }
            return null;
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
            vs.slot = thisDCA.umaData.umaRecipe.FindSlot(va.slotName);
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
            //scene = EditorSceneManager.NewPreviewScene();

            centeredLabel = new GUIStyle(GUI.skin.label);
            centeredLabel.fontStyle = FontStyle.Bold;
            centeredLabel.alignment = TextAnchor.MiddleCenter;

            modifierEditor = MeshModifierEditor.GetOrCreateWindowFromModifier(Currentmodifier, thisDCA, this);
            if (Currentmodifier != null)
            {
                modifierEditor.Modifiers = Currentmodifier.EditorModifiers;
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
            }
            else
            {
                modifierEditor.Modifiers = new List<MeshModifier.Modifier>();
            }
            GameObject lightingObject = new GameObject("Directional Light");
            lightingObject.transform.rotation = Quaternion.Euler(50, 330, 0);
            lightingObject.AddComponent<Light>().type = LightType.Directional;

            SkinnedMeshRenderer smr = thisDCA.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();

            BakedMesh = new Mesh();
            BakedMesh.name = "BakedMesh";
            smr.BakeMesh(BakedMesh, true);
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
            cameraAnchor = new GameObject("CameraAnchor");
            cameraAnchor.transform.position = new Vector3(0, 1, 2.5f);
            cameraAnchor.transform.rotation = Quaternion.Euler(0, 180, 0);

            SceneManager.MoveGameObjectToScene(VertexObject, scene);
            SceneManager.MoveGameObjectToScene(lightingObject, scene);
            SceneManager.MoveGameObjectToScene(cameraAnchor, scene);
            Tools.hidden = true;
            SceneView.duringSceneGui += OnSceneGUI;
            NeedsCameraSetup = true;
            HelpBoxStyle = new GUIStyle(EditorStyles.miniLabel);
            HelpBoxStyle.wordWrap = true;
            //AssetDatabase.StartAssetEditing();
            thisDCA.GenerateSingleUMA();
            cachedVisibilityHeight = -1f;

            return true;
        }

        private List<Type> LoadTypes(Type baseType)
        {
            List<Type> theTypes = new List<Type>();
            var Assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(p => !p.IsDynamic);

            foreach (var asm in Assemblies)
            {
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
            Tools.hidden = false;
            DestroyImmediate(VertexObject);
            DestroyImmediate(lightingObject);
            DestroyImmediate(cameraAnchor);
            SceneView.duringSceneGui -= OnSceneGUI;
            var wearables = thisDCA.GetVisibleWearables();
            foreach (var wearable in wearables)
            {
                wearable.disabled = false;
            }
            thisDCA.umaData.ManualMeshModifiers = new List<MeshModifier.Modifier>();
            if (thisDCA.editorTimeGeneration)
            {
                thisDCA.GenerateSingleUMA();
            }
            if (modifierEditor != null)
            {
                modifierEditor.Close();
            }
            if (vertexMaterial != null)
            {
                DestroyImmediate(vertexMaterial);
            }
            if (vertexMesh != null)
            {
                DestroyImmediate(vertexMesh);
            }
            base.OnCloseStage();
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
            if (panelWidth > 300f)
            {
                panelWidth = 300f;
            }
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
                    modifierEditor.Repaint();

                    if (modifierEditor.RebuildOnChanges)
                    {
                        modifierEditor.DoCharacterRebuild();
                    }
                }
            }

            Handles.BeginGUI();

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
                    currentState = vertexState.unKnown;

                    flippedVertexes.Clear();
                    paintedVerticesThisStroke.Clear();
                    //Debug.Log("Currentevent.button = "+ currentEvent.button);
                    if (currentEvent.button == 0)
                    {
                        BeginSelectionUndoSnapshot(currentDefineMode == DefineMode.DefineVertexSet ? "Modify Vertex Set" : "Modify Vertex State");

                        if (currentDefineMode == DefineMode.DefineVertexSet)
                        {
                            if (IsPaintModeEnabled)
                            {
                                replaceSelectionOnRectSelect = false;
                                rectSelect = false;
                                painting = true;
                            }
                            else
                            {
                                replaceSelectionOnRectSelect = GetEffectiveSelectMode(currentEvent) == selectMode.Add && !currentEvent.shift && !currentEvent.control;
                                rectSelect = true;
                                painting = false;
                                RectStart = currentEvent.mousePosition - currentEvent.delta;
                            }
                        }
                        else
                        {
                            if (IsPaintModeEnabled)
                            {
                                pendingStateClickAction = false;
                                replaceSelectionOnRectSelect = false;
                                rectSelect = false;
                                painting = true;
                                SingleSelect(currentEvent);
                            }
                            else
                            {
                                pendingStateClickAction = true;
                                pendingStateClickStart = currentEvent.mousePosition;
                                replaceSelectionOnRectSelect = false;
                                rectSelect = true;
                                painting = false;
                                RectStart = currentEvent.mousePosition - currentEvent.delta;
                            }
                        }

                        if (currentDefineMode == DefineMode.DefineVertexSet)
                        {
                            if (IsPaintModeEnabled)
                            {
                                SingleSelect(currentEvent);
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
                        pendingStateClickAction = false;
                        replaceSelectionOnRectSelect = false;
                        paintedVerticesThisStroke.Clear();
                        rectSelect = false;
                    }
                }



            // This is to prevent the scene view from capturing the selection and doing it's own routines.
            // But we must not eat events intended for our own IMGUI windows/scrollviews.
           if (currentEvent.type == EventType.MouseDrag)
            {
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
                    EndSelectionUndoSnapshot();
                    painting = false;

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
                    paintedVerticesThisStroke.Clear();
                }



                if (currentEvent.type == EventType.MouseLeaveWindow)
                {
                    if (rectSelect)
                    {
                        Vector2 RectEnd = currentEvent.mousePosition;
                        Rect MinMax = GetMinMax(RectStart, RectEnd);
                        RectangleSelect(currentEvent, MinMax);
                        rectSelect = false;
                        painting = false;
                        sceneView.Repaint();
                    }
                    pendingStateClickAction = false;
                    replaceSelectionOnRectSelect = false;
                    paintedVerticesThisStroke.Clear();
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

            if (isEditing)
            {
                Rect topCenter = new Rect(0, 25, sceneView.position.width, 20);
                GUI.Label(topCenter, "** Edit Mode **", centeredLabel);
            }
            else
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
                if (painting)
                {
                    SingleSelect(currentEvent);
                }
                SceneView.RepaintAll();
            }
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

            Vector3[] normals = BakedMesh.normals;

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

            if (editAdjustment != null && editorMode == MeshModifierEditor.EditorMode.VertexAdjustments)
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
                if (!TryGetVisibleBakedVertexIndex(editSelection.slot, editSelection.vertexIndexOnSlot, out int bakedIndex))
                {
                    return false;
                }

                if (van.bakedNormalSet == false)
                {
                    van.bakedNormal = BakedMesh.normals[bakedIndex];
                    van.bakedNormalSet = true;
                }

                editSelection.WorldPosition = VertexObject.transform.TransformPoint(BakedMesh.vertices[bakedIndex]);
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
                if (!TryGetVisibleBakedVertexIndex(editSelection.slot, editSelection.vertexIndexOnSlot, out int bakedIndex))
                {
                    return false;
                }

                UMAData umaData = thisDCA.umaData;
                SlotData slot = thisDCA.umaData.umaRecipe.FindSlot(vas.slotName);

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
                Vector3 worldRotation = VertexObject.transform.TransformVector(BakedMesh.normals[bakedIndex]);
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
            GUILayout.BeginArea(leftPanelRect, EditorStyles.helpBox);
            {
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
                    VertexEditorScrollLocation = GUILayout.BeginScrollView(VertexEditorScrollLocation);
                    DoToolsPanel();
                    GUILayout.EndScrollView();
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
                wearableCount = wearables != null ? wearables.Count() : 0;
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

            float estimated = header + ((scrollContentLines * (line + vSpacing)) + (LeftPanelPadding * 2f));
            return Mathf.Clamp(estimated, 50f, maxHeight);
        }

        private void DoToolsPanel()
        {
            GUILayout.Label("Tools", EditorStyles.boldLabel);
            DoToolsWindow(VertexEditorToolsWindowID);
        }

      private void DrawVisibilityPanel(float availableHeight)
        {
            GUILayout.Label("Visibility", EditorStyles.boldLabel);

            // Fill the remainder of the visibility section with the scroll view.
            float headerHeight = Mathf.Max(LeftPanelHeaderHeight, EditorGUIUtility.singleLineHeight);
            float scrollHeight = Mathf.Max(0f, availableHeight - headerHeight);
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
                modifierEditor.Repaint();
            }
            if (wasRecipeChanged)
            {
                RebuildMesh(true);
                modifierEditor.Repaint();
            }
        }

        private Vector2 ToolsPos = new Vector2(0, 0);
        private GUIStyle smallButtonStyle;
        private GUIStyle threeButtonStyle;
        bool doneButton = false;
        public float ToolWindowAreaHeight = 0.0f;
        public MeshModifierEditor.EditorMode editorMode = MeshModifierEditor.EditorMode.VertexAdjustments;

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
            GUILayout.BeginArea(new Rect(0, 0, VertexEditorToolsWindow.width - 12, ToolsPos.y + ToolWindowAreaHeight));
            SceneView sceneView = SceneView.lastActiveSceneView;
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
            GUIHelper.EndVerticalPadded(5);
            #endregion
            #region Selection Options
            GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
            GUILayout.Label("Selection Options", centeredLabel);

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
                paintModeSet = EditorGUILayout.Toggle("Paint Mode", paintModeSet);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Set Action", GUILayout.Width(92));
                int setAction = currentMode == selectMode.Remove ? 1 : currentMode == selectMode.InvertSelection ? 2 : 0;
                int newSetAction = GUILayout.Toolbar(setAction, new string[] { "Add", "Remove", "Invert" });
                currentMode = newSetAction == 1 ? selectMode.Remove : newSetAction == 2 ? selectMode.InvertSelection : selectMode.Add;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Slot Filter", GUILayout.Width(92));

                if (selectionSlot >= selectFrom.Length)
                {
                    selectionSlot = 0;
                }
                selectionSlot = EditorGUILayout.Popup(selectionSlot, selectFrom);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                showRaycastSelection = EditorGUILayout.Foldout(showRaycastSelection, "Select by raycasting", true);
                GUILayout.EndHorizontal();

                if (showRaycastSelection)
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
                        modifierEditor.Repaint();
                        SceneView.RepaintAll();
                    }
                    EditorGUI.EndDisabledGroup();

                    if (!string.IsNullOrEmpty(raycastStatusMessage))
                    {
                       GUILayout.Label("Result (copy/paste):", EditorStyles.miniBoldLabel);
                        float line = EditorGUIUtility.singleLineHeight;
                        raycastStatusMessage = EditorGUILayout.TextArea(raycastStatusMessage, GUILayout.MinHeight(line * 4f));
                    }

                    GUIHelper.EndVerticalPadded(5);
                }
            }
            else
            {
                paintModeState = EditorGUILayout.Toggle("Paint Mode", paintModeState);

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
                modifierEditor.Repaint();
            }
            if (GUILayout.Button("Append", threeButtonStyle))
            {
                // Append the vertex selections
                Undo.RegisterCompleteObjectUndo(this, "Append Vertex Selection");
                LoadSelections();
                modifierEditor.Repaint();
            }
            GUILayout.EndHorizontal();

            if (currentDefineMode == DefineMode.DefineVertexSet)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Invert Selection", smallButtonStyle))
                {
                    Undo.RegisterCompleteObjectUndo(this, "Invert Vertex Selection");
                    InvertSelection();
                    modifierEditor.Repaint();
                }
                if (GUILayout.Button("Select All", smallButtonStyle))
                {
                    Undo.RegisterCompleteObjectUndo(this, "Select All Vertexes");
                    SelectAll();
                    modifierEditor.Repaint();
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Clear Selection", smallButtonStyle))
                {
                    Undo.RegisterCompleteObjectUndo(this, "Clear Vertex Selection");
                    SelectedVertexes.Clear();
                    modifierEditor.Repaint();
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
                    modifierEditor.Repaint();
                }
                if (GUILayout.Button("Activate all", smallButtonStyle))
                {
                    Undo.RegisterCompleteObjectUndo(this, "Activate Vertex State");
                    for (int i = 0; i < SelectedVertexes.Count; i++)
                    {
                        SelectedVertexes[i].isActive = true;
                    }
                    modifierEditor.Repaint();
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
                    modifierEditor.Repaint();
                }
                GUILayout.EndHorizontal();
            }
            GUIHelper.EndVerticalPadded(5);
            #endregion


            //GUILayout.Label("camera: " + sceneView.camera.transform.position.ToString());
            if (GUILayout.Button("Reset Camera"))
            {
                SceneView.lastActiveSceneView.pivot = new Vector3(0, 1, 2.5f);
                Selection.activeObject = VertexObject;
                sceneView.AlignViewToObject(cameraAnchor.transform);
                sceneView.FrameSelected(true);
                sceneView.AlignViewToObject(cameraAnchor.transform);
            }
            GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.85f, 1f));
            if (currentDefineMode == DefineMode.DefineVertexSet)
            {
                if (paintModeSet)
                {
                    GUILayout.TextArea("Define Vertex Set mode\nPaint Mode enabled: click-drag applies Set Action to vertices under cursor\nSet Action is selected from Add / Remove / Invert\nEach vertex is processed only once per stroke\n\nHold Alt and use mouse buttons/wheel to navigate.", HelpBoxStyle);
                }
                else
                {
                    GUILayout.TextArea("Define Vertex Set mode\nLeft-click applies Set Action to a vertex\nLeft-drag box applies Set Action to multiple vertices\nSet Action is selected from Add / Remove / Invert\n\nHold Alt and use mouse buttons/wheel to navigate.", HelpBoxStyle);
                }
            }
            else
            {
                if (paintModeState)
                {
                    GUILayout.TextArea("Define Vertex State mode\nOnly affects already selected vertices\nPaint Mode enabled: click-drag applies State Action\nState Action is selected from Toggle / Activate / Deactivate\nEach vertex is processed only once per stroke\n\nHold Alt and use mouse buttons/wheel to navigate.", HelpBoxStyle);
                }
                else
                {
                    GUILayout.TextArea("Define Vertex State mode\nOnly affects already selected vertices\nLeft-click applies State Action\nState Action is selected from Toggle / Activate / Deactivate\n\nHold Alt and use mouse buttons/wheel to navigate.", HelpBoxStyle);
                }
            }
            if (Event.current.type == EventType.Repaint)
            {
                float height = GUILayoutUtility.GetLastRect().yMax;
                ToolWindowAreaHeight = height;
            }
            GUIHelper.EndVerticalPadded(5);
            GUILayout.EndArea();
            GUILayout.Space(ToolWindowAreaHeight + 10);
            GUILayout.EndScrollView();
            // Define a small drag area so the rest of the window is NOT draggable
        }

        private void CancelInteraction()
        {
            pendingStateClickAction = false;
            replaceSelectionOnRectSelect = false;
            paintedVerticesThisStroke.Clear();
            rectSelect = false;
            painting = false;
            EndSelectionUndoSnapshot();
        }

        private selectMode GetEffectiveSelectMode(Event currentEvent)
        {
            if (currentDefineMode == DefineMode.DefineVertexSet && currentEvent != null)
            {
                if (currentEvent.control)
                {
                    return selectMode.InvertSelection;
                }

                if (currentEvent.shift)
                {
                    return selectMode.Add;
                }
            }

            return currentMode;
        }


        public void SelectAll()
        {
            SelectedVertexes.Clear();
            var vertexes = BakedMesh.vertices;
            for (int i = 0; i < vertexes.Length; i++)
            {
                if (TryGetSlotForBakedVertex(i, out SlotData foundSlot, out int foundVert))
                {
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

                        if (currentDefineMode == DefineMode.DefineVertexSet && selectionSlot > 0)
                        {
                            if (foundSlot.slotName != selectFrom[selectionSlot])
                            {
                                continue;
                            }
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
            modifierEditor.Repaint();
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
                SlotData slot = thisDCA.umaData.umaRecipe.GetSlot(va.slotName);
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

        private bool SingleSelect(Event currentEvent)
        {
            bool found = false;
            selectMode effectiveMode = GetEffectiveSelectMode(currentEvent);
            bool replaceSelectionOnAdd = currentDefineMode == DefineMode.DefineVertexSet &&
                                         effectiveMode == selectMode.Add &&
                                         !currentEvent.shift &&
                                         !currentEvent.control &&
                                         !(IsPaintModeEnabled && painting);

            Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
            if (phyScene.Raycast(ray.origin, ray.direction, out RaycastHit hit))
            {
                if (hit.transform != null && hit.transform.gameObject == VertexObject)
                {
                    VertexSelection vs = FindVertex(hit, BakedMesh, VertexObject);
                    if (vs != null)
                    {
                        bool trackPaintStroke = IsPaintModeEnabled && painting;
                        string paintKey = vs.slot.slotName + ":" + vs.vertexIndexOnSlot;
                        if (trackPaintStroke && paintedVerticesThisStroke.Contains(paintKey))
                        {
                            return false;
                        }

                        if (currentDefineMode == DefineMode.DefineVertexSet && selectionSlot > 0)
                        {
                            if (vs.slot.slotName != selectFrom[selectionSlot])
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

                        if (trackPaintStroke)
                        {
                            paintedVerticesThisStroke.Add(paintKey);
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
            modifierEditor.Repaint();
            return found;
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
            SkinnedMeshRenderer smr = thisDCA.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
            GameObject.DestroyImmediate(BakedMesh);
            if (smr == null)
            {
                Debug.LogError("No SkinnedMeshRenderer found");
                return;
            }
            BakedMesh = new Mesh();
            BakedMesh.name = "BakedMesh";
            smr.BakeMesh(BakedMesh, true);
            VertexObject.GetComponent<MeshFilter>().sharedMesh = BakedMesh;
            MeshCollider mc = VertexObject.GetComponent<MeshCollider>();
            mc.sharedMesh = BakedMesh;
            UpdateSelections();
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

            Tools.current = Tool.None;
            Tools.hidden = true;

            SceneView.CameraMode camMode = sceneView.cameraMode;
            camMode.drawMode = DrawCameraMode.TexturedWire;

            // Setup Scene view state
            sceneView.sceneViewState.showFlares = false;
            sceneView.sceneViewState.alwaysRefresh = false;
            sceneView.sceneViewState.showFog = false;
            sceneView.sceneViewState.showSkybox = false;
            sceneView.sceneViewState.showImageEffects = false;
            sceneView.sceneViewState.showParticleSystems = false;
            sceneView.sceneLighting = false;
            //sceneView.cameraMode = camMode; // this quit working? Now gets an error that the cameraMode is not registered?
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
                mr.sharedMaterials = newMaterials.ToArray();
            }
            else
            {
                Debug.LogError("No MeshRenderer found");
            }
        }

        internal void RemoveVertexAdjustment(VertexAdjustment removeMe)
        {
            Adjustments.Remove(removeMe);
        }


    }
}