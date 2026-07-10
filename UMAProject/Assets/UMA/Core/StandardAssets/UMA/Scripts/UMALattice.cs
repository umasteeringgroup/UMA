using UnityEngine;
using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine.Rendering;

namespace UMA
{
    /// <summary>Preset falloff curve shapes for lattice deformation.</summary>
    public enum CurveMode
    {
        /// <summary>Full effect regardless of distance from the handle.</summary>
        Constant,
        /// <summary>Strongest near the handle, quickly dropping off with distance.</summary>
        EaseIn,
        /// <summary>Weakest near the handle, strongest further away.</summary>
        EaseOut,
        /// <summary>Use the editable customCurve field.</summary>
        Custom
    }

    /// <summary>
    /// Free-Form Deformation (FFD) lattice component, matching Blender/3dsMax lattice deformers.
    /// A 3D grid of control points surrounds a target mesh; dragging any control point smoothly
    /// deforms enclosed vertices using Bernstein polynomial (Bezier volume) interpolation.
    ///
    /// Modular: EvaluateLattice() is pure math, separate from mesh application, so future
    /// targets (SkinnedMeshRenderer, slot baking) can reuse the same deformation core.
    /// </summary>
    [ExecuteAlways]
    public class UMALattice : MonoBehaviour
    {
        [Header("Lattice Shape")]
        [Tooltip("Width (X), Height (Y), Depth (Z) of the lattice volume in local space.")]
        public Vector3 size = Vector3.one;

        [Tooltip("Local-space offset from GameObject origin to lattice minimum corner.")]
        public Vector3 offset = new Vector3(0f, 1f, 0f);

        [Tooltip("Number of subdivisions along each axis. Minimum 1 each.")]
        public Vector3Int cuts = new Vector3Int(2, 2, 2);

        [Range(0f, 1f)]
        [Tooltip("Reduces lattice influence near the volume edges. 0 is full effect, 1 strongly limits deformation to the center.")]
        public float damping;

        [Header("Falloff")]
        [Tooltip("How deformation strength changes with distance from the dragged handle.")]
        public CurveMode curveMode = CurveMode.Constant;

        [Tooltip("Custom falloff curve. X axis = normalized distance (0 = at the handle, 1 = farthest corner), Y axis = deformation weight.")]
        public AnimationCurve customCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        [Tooltip("When enabled, uses global Bernstein evaluation where every control point affects the entire lattice volume. When disabled (default), each control point only affects its immediate neighboring cells.")]
        public bool useGlobalDeformation = false;

        [Header("Visualization")]
        public bool drawLattice = true;
        [Tooltip("Radius of the sphere handles in the scene view.")]
        public float handleRadius = 0.05f;

        [Header("Target")]
        [Tooltip("MeshFilter whose mesh will be deformed. Mesh vertices should lie inside the lattice volume.")]
        public MeshFilter targetFilter;

        [Tooltip("SkinnedMeshRenderer whose sharedMesh will be deformed. Takes priority if both are assigned.")]
        public SkinnedMeshRenderer targetRenderer;

        [SerializeField, Tooltip("When set, only this UMA slot is copied from the skeleton-baked mesh and deformed. Empty deforms the whole baked mesh.")]
        private string selectedSlotName;

        [SerializeField, Tooltip("Child UMAEffectors that are applied after the lattice deformation pass.")]
        private UMAEffector[] effectors;

        // -------- serialized state --------
        [SerializeField] private Vector3[] baseControlPoints;
        [SerializeField] private Vector3[] controlPoints;

        // -------- runtime caches (not serialized) --------
        [NonSerialized] private Vector3[] _originalVertices;
        [NonSerialized] private Mesh _workingMesh;
        [NonSerialized] private MeshFilter _bakedMeshFilter;
        [NonSerialized] private MeshRenderer _bakedMeshRenderer;
        [NonSerialized] private Material[] _bakedMaterials;
        [NonSerialized] private bool _targetRendererWasEnabled;
        [NonSerialized] private MeshRenderer _targetFilterMeshRenderer;
        [NonSerialized] private bool _targetFilterRendererWasEnabled;
        [NonSerialized] private bool _targetFilterRendererInitialized;
        [NonSerialized] private DynamicCharacterAvatar _dca;
        [NonSerialized] private bool _pendingDeform;
        [NonSerialized] private int _pendingDeformAttempts;
        [NonSerialized] private bool _meshPreviewVisible = true;

        /// <summary>Indices of currently selected handles. Shift-click to add/remove; regular click replaces selection.</summary>
        [System.NonSerialized] public List<int> selectedHandleIndices = new List<int>();

        // -------- public API --------

        /// <summary>Total control points = (cuts.x+1)*(cuts.y+1)*(cuts.z+1).</summary>
        public int ControlPointCount => (cuts.x + 1) * (cuts.y + 1) * (cuts.z + 1);

        /// <summary>Grid dimensions of control points.</summary>
        public Vector3Int ControlPointGrid => new Vector3Int(cuts.x + 1, cuts.y + 1, cuts.z + 1);

        public string SelectedSlotName => selectedSlotName;

        public UMAEffector[] Effectors => effectors ?? Array.Empty<UMAEffector>();

        public int EffectorCount => Effectors.Length;

        public UMAEffector GetEffector(int index)
        {
            UMAEffector[] currentEffectors = Effectors;
            if (index < 0 || index >= currentEffectors.Length)
                return null;

            return currentEffectors[index];
        }

        public Vector3 GetControlPoint(int index)
        {
            if (controlPoints == null || index < 0 || index >= controlPoints.Length)
                return Vector3.zero;
            return controlPoints[index];
        }

        /// <summary>Set a control point and optionally trigger deformation.</summary>
        public void SetControlPoint(int index, Vector3 localPosition, bool deform = true)
        {
            if (controlPoints == null || index < 0 || index >= controlPoints.Length) return;
            controlPoints[index] = localPosition;
            if (deform) DeformTarget();
        }

        /// <summary>
        /// Move every control point that lies on the same cut as the given handle.
        /// The cut is moved along the dominant axis of localDelta so it behaves like a
        /// lattice rest-shape edit instead of a vertex deformation.
        /// </summary>
        public void MoveCut(int handleIndex, Vector3 localDelta, bool deform = true)
        {
            if (controlPoints == null || handleIndex < 0 || handleIndex >= controlPoints.Length) return;

            int axis = DominantAxis(localDelta);
            float delta = axis == 0 ? localDelta.x : axis == 1 ? localDelta.y : localDelta.z;
            if (Mathf.Abs(delta) < 1e-6f) return;

            UnflattenIndex(handleIndex, out int ix, out int iy, out int iz);
            int cutIndex = axis == 0 ? ix : axis == 1 ? iy : iz;
            MoveCutAxis(axis, cutIndex, delta);

            if (!deform)
                RebaseWorkingMeshBaseline();

            if (deform) DeformTarget();
        }

        /// <summary>
        /// Move every selected handle's cut using the same dominant-axis delta.
        /// Duplicate cuts are ignored so the same plane does not move twice.
        /// </summary>
        public void MoveCuts(IEnumerable<int> handleIndices, Vector3 localDelta, bool deform = true)
        {
            if (controlPoints == null || handleIndices == null) return;

            int axis = DominantAxis(localDelta);
            float delta = axis == 0 ? localDelta.x : axis == 1 ? localDelta.y : localDelta.z;
            if (Mathf.Abs(delta) < 1e-6f) return;

            HashSet<int> cutIndices = new HashSet<int>();
            foreach (int handleIndex in handleIndices)
            {
                if (handleIndex < 0 || handleIndex >= controlPoints.Length) continue;
                UnflattenIndex(handleIndex, out int ix, out int iy, out int iz);
                cutIndices.Add(axis == 0 ? ix : axis == 1 ? iy : iz);
            }

            foreach (int cutIndex in cutIndices)
                MoveCutAxis(axis, cutIndex, delta);

            if (!deform)
                RebaseWorkingMeshBaseline();

            if (deform) DeformTarget();
        }

        /// <summary>Reset all control points to their default grid positions.</summary>
        public void ResetControlPoints()
        {
            EnsureControlPointArray();
            var grid = ControlPointGrid;
            for (int iz = 0; iz < grid.z; iz++)
                for (int iy = 0; iy < grid.y; iy++)
                    for (int ix = 0; ix < grid.x; ix++)
                    {
                        int flat = FlatIndex(ix, iy, iz);
                        Vector3 position = UniformPosition(ix, iy, iz);
                        baseControlPoints[flat] = position;
                        controlPoints[flat] = position;
                    }

            SyncVolumeToBaseCuts();
        }

        /// <summary>Rest (default) position for control point (ix,iy,iz).</summary>
        public Vector3 DefaultPosition(int ix, int iy, int iz)
        {
            if (baseControlPoints != null)
            {
                int flat = FlatIndex(ix, iy, iz);
                if (flat >= 0 && flat < baseControlPoints.Length)
                    return baseControlPoints[flat];
            }

            return UniformPosition(ix, iy, iz);
        }

        /// <summary>Recache original vertex positions from the target mesh (call after changing target).</summary>
        public void RecacheOriginalVertices()
        {
            _originalVertices = null;
            _workingMesh = null;
            CleanupBakedRenderer();
            EnsureInitialized();
        }

        public void SetSelectedSlotName(string slotName)
        {
            slotName = slotName ?? string.Empty;
            if (string.Equals(selectedSlotName, slotName, StringComparison.Ordinal)) return;

            selectedSlotName = slotName;
            RecacheOriginalVertices();
            if (isActiveAndEnabled)
                DeformTarget();
        }

        public void SetPreviewVisible(bool visible)
        {
            _meshPreviewVisible = visible;

            if (_bakedMeshRenderer != null)
                _bakedMeshRenderer.enabled = visible;

            if (_targetFilterMeshRenderer != null && _targetFilterRendererInitialized)
                _targetFilterMeshRenderer.enabled = visible && _targetFilterRendererWasEnabled;
        }

        public SlotData[] GetUMASlots()
        {
            UMAData umaData = GetUMAData();
            if (umaData == null || umaData.umaRecipe == null || umaData.umaRecipe.slotDataList == null)
                return Array.Empty<SlotData>();

            return umaData.umaRecipe.slotDataList;
        }

        public void RefreshEffectorsFromChildren()
        {
            List<UMAEffector> childEffectors = new List<UMAEffector>();
            GetComponentsInChildren<UMAEffector>(true, childEffectors);

            int validCount = 0;
            for (int i = 0; i < childEffectors.Count; i++)
            {
                UMAEffector e = childEffectors[i];
                if (e != null && e.gameObject != gameObject)
                    validCount++;
            }

            if (!EffectorsMatch(childEffectors, validCount))
            {
                UMAEffector[] newArray = new UMAEffector[validCount];
                int w = 0;
                for (int i = 0; i < childEffectors.Count; i++)
                {
                    UMAEffector e = childEffectors[i];
                    if (e != null && e.gameObject != gameObject)
                        newArray[w++] = e;
                }
                effectors = newArray;
            }
        }

        private bool EffectorsMatch(List<UMAEffector> next, int nextValidCount)
        {
            if (effectors == null)
                return nextValidCount == 0;

            // Count valid entries in current without allocating
            int curValidCount = 0;
            for (int i = 0; i < effectors.Length; i++)
            {
                UMAEffector e = effectors[i];
                if (e != null && e.gameObject != null)
                    curValidCount++;
            }

            if (curValidCount != nextValidCount)
                return false;

            // Walk both sequences simultaneously — zero allocation
            int nextIdx = 0;
            for (int curIdx = 0; curIdx < effectors.Length; curIdx++)
            {
                UMAEffector cur = effectors[curIdx];
                if (cur == null || cur.gameObject == null)
                    continue;

                // Skip nulls and self in next
                while (nextIdx < next.Count)
                {
                    UMAEffector nxt = next[nextIdx];
                    if (nxt != null && nxt.gameObject != gameObject)
                        break;
                    nextIdx++;
                }

                if (nextIdx >= next.Count)
                    return false;
                if (!ReferenceEquals(cur, next[nextIdx]))
                    return false;
                nextIdx++;
            }

            return true;
        }

        /// <summary>
        /// Center the lattice on the target mesh by computing its bounding box
        /// and setting offset/size to encompass it with small padding.
        /// For SkinnedMeshRenderer the bounds are taken from a baked mesh.
        /// </summary>
        public void CenterOnTarget()
        {
            ResolveTargetComponents();

            Transform targetTransform = GetTargetTransform();
            if (targetTransform == null) return;

            Bounds sourceBounds;
            Matrix4x4 boundsToLattice;
            if (targetRenderer != null)
            {
                Mesh sourceMesh = targetRenderer.sharedMesh;
                if (sourceMesh == null) return;

                Mesh bakedMesh = new Mesh();
                targetRenderer.BakeMesh(bakedMesh);
                TransformMesh(bakedMesh, targetRenderer.transform.localToWorldMatrix);
                SlotData selectedSlot = GetSelectedSlotData();
                if (!string.IsNullOrEmpty(selectedSlotName))
                {
                    if (selectedSlot == null || !TryCreateSlotMeshFromBakedMesh(bakedMesh, selectedSlot, out Mesh slotMesh))
                    {
                        DestroyImmediate(bakedMesh);
                        return;
                    }

                    sourceBounds = slotMesh.bounds;
                    DestroyImmediate(slotMesh);
                }
                else
                {
                    sourceBounds = bakedMesh.bounds;
                }
                DestroyImmediate(bakedMesh);

                boundsToLattice = transform.worldToLocalMatrix;
                if (_targetFilterMeshRenderer != null && _targetFilterRendererInitialized)
                {
                    _targetFilterMeshRenderer.enabled = _targetFilterRendererWasEnabled;
                }
            }
            else
            {
                Mesh sourceMesh = targetFilter.sharedMesh;
                if (sourceMesh == null) return;

                sourceBounds = sourceMesh.bounds;
                _meshPreviewVisible = true;
                boundsToLattice = transform.worldToLocalMatrix * targetTransform.localToWorldMatrix;
            }

            if (sourceBounds.size.sqrMagnitude < 1e-6f) return;

            Bounds latticeBounds = TransformBounds(sourceBounds, boundsToLattice);

            float padding = Mathf.Max(latticeBounds.size.x, latticeBounds.size.y, latticeBounds.size.z) * 0.05f;
            offset = latticeBounds.min - Vector3.one * padding;
            size = latticeBounds.size + Vector3.one * (padding * 2f);
            size = new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y), Mathf.Max(0.01f, size.z));

            ResetControlPoints();
            RecacheOriginalVertices();
        }

        /// <summary>
        /// Evaluate the FFD lattice at a local-space point.
        /// Returns the deformed position, or the original point unchanged if outside the lattice volume.
        /// Pure math — usable from any caller (MeshFilter, SkinnedMeshRenderer, slot baker, etc.).
        /// </summary>
        public Vector3 EvaluateLattice(Vector3 localPoint)
        {
            EnsureControlPointArray();
            GetAxisPositions(out float[] axisX, out float[] axisY, out float[] axisZ);
            return EvaluateLattice(localPoint, axisX, axisY, axisZ);
        }

        /// <summary>Apply the current lattice deformation to the target mesh.</summary>
        public void DeformTarget()
        {
            RefreshEffectorsFromChildren();
            ResolveTargetComponents();

            Transform targetTransform = GetTargetTransform();
            if (targetTransform == null)
            {
                Debug.LogWarning($"[UMALattice] DeformTarget on '{name}': no target transform");
                return;
            }

            if (targetRenderer == null && targetFilter == null)
            {
                Debug.LogWarning($"[UMALattice] DeformTarget on '{name}': no target renderer or filter");
                return;
            }

            // Lazily create or recreate the working mesh (single bake for SMR)
            if (_workingMesh == null)
            {
                //Debug.Log($"[UMALattice] DeformTarget on '{name}': creating working mesh from {(targetRenderer != null ? "SkinnedMeshRenderer" : "MeshFilter")}");
                if (targetRenderer != null)
                {
                    Mesh shared = targetRenderer.sharedMesh;
                    if (shared == null) return;

                    // Bake once to get current-pose vertex positions, then detach them into world space.
                    Mesh baked = new Mesh();
                    targetRenderer.BakeMesh(baked);
                    TransformMesh(baked, targetRenderer.transform.localToWorldMatrix);

                    SlotData selectedSlot = GetSelectedSlotData();
                    if (!string.IsNullOrEmpty(selectedSlotName))
                    {
                        if (selectedSlot == null || !TryCreateSlotMeshFromBakedMesh(baked, selectedSlot, out Mesh slotMesh))
                        {
                            DestroyImmediate(baked);
                            Debug.LogWarning($"[UMALattice] Selected slot '{selectedSlotName}' is not available on '{name}', so no baked lattice mesh was created.");
                            return;
                        }

                        _workingMesh = slotMesh;
                        _workingMesh.name = selectedSlot.slotName + " (Lattice Baked World Slot)";
                    }
                    else
                    {
                        _workingMesh = Instantiate(baked);
                        _workingMesh.name = shared.name + " (Lattice Baked World)";
                    }

                    DestroyImmediate(baked);
                }
                else
                {
                    Mesh mfShared = targetFilter.sharedMesh;
                    if (mfShared == null) return;
                    _workingMesh = Instantiate(mfShared);
                    _workingMesh.name = mfShared.name + " (Lattice)";
                }

                ApplyWorkingMesh(_workingMesh);
                _originalVertices = (Vector3[])_workingMesh.vertices.Clone();
            }

            if (_originalVertices == null || _originalVertices.Length != _workingMesh.vertexCount)
                _originalVertices = (Vector3[])_workingMesh.vertices.Clone();

            GetAxisPositions(out float[] axisX, out float[] axisY, out float[] axisZ);

            Matrix4x4 meshToLattice = targetRenderer != null
                ? transform.worldToLocalMatrix
                : transform.worldToLocalMatrix * targetTransform.localToWorldMatrix;
            Matrix4x4 latticeToMesh = targetRenderer != null
                ? transform.localToWorldMatrix
                : targetTransform.worldToLocalMatrix * transform.localToWorldMatrix;

            Vector3[] verts = new Vector3[_originalVertices.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 lp = meshToLattice.MultiplyPoint3x4(_originalVertices[i]);
                verts[i] = latticeToMesh.MultiplyPoint3x4(EvaluateLattice(lp, axisX, axisY, axisZ));
            }

            if (HasActiveEffectors())
            {
                _workingMesh.vertices = verts;
                _workingMesh.RecalculateNormals();
                ApplyEffectors(verts, _workingMesh.normals, targetTransform, targetRenderer != null ? Matrix4x4.identity : targetTransform.localToWorldMatrix);
            }

            _workingMesh.vertices = verts;
            _workingMesh.RecalculateNormals();
            _workingMesh.RecalculateBounds();

            ApplyWorkingMesh(_workingMesh);
            // Debug.Log($"[UMALattice] DeformTarget on '{name}': complete ({verts.Length} vertices)");
        }

        /// <summary>Restore target mesh to its original undeformed state.</summary>
        public void RestoreTarget()
        {
            if (_workingMesh != null)
            {
                if (_originalVertices != null && _originalVertices.Length == _workingMesh.vertexCount)
                {
                    _workingMesh.vertices = _originalVertices;
                    _workingMesh.RecalculateNormals();
                    _workingMesh.RecalculateBounds();
                }
                _workingMesh = null;
                _originalVertices = null;
            }

            CleanupBakedRenderer();
        }

        // -------- target helpers --------
        private void ResolveTargetComponents()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
            }

            if (targetRenderer == null && targetFilter == null)
            {
                targetFilter = GetComponentInChildren<MeshFilter>(true);
            }
        }

        private UMAData GetUMAData()
        {
            if (_dca != null && _dca.umaData != null)
                return _dca.umaData;

            if (targetRenderer != null)
            {
                DynamicCharacterAvatar rendererDca = targetRenderer.GetComponentInParent<DynamicCharacterAvatar>();
                if (rendererDca != null && rendererDca.umaData != null)
                    return rendererDca.umaData;

                UMAData rendererUmaData = targetRenderer.GetComponentInParent<UMAData>();
                if (rendererUmaData != null)
                    return rendererUmaData;
            }

            DynamicCharacterAvatar parentDca = GetComponentInParent<DynamicCharacterAvatar>();
            if (parentDca != null && parentDca.umaData != null)
                return parentDca.umaData;

            return GetComponentInParent<UMAData>();
        }

        private SlotData GetSelectedSlotData()
        {
            if (string.IsNullOrEmpty(selectedSlotName))
                return null;

            SlotData[] slots = GetUMASlots();
            for (int i = 0; i < slots.Length; i++)
            {
                SlotData slot = slots[i];
                if (SlotMatchesName(slot, selectedSlotName))
                    return slot;
            }

            return null;
        }

        public bool IsSelectedSlot(SlotData slot)
        {
            return SlotMatchesName(slot, selectedSlotName);
        }

        private static bool SlotMatchesName(SlotData slot, string slotName)
        {
            if (slot == null || string.IsNullOrEmpty(slotName))
                return false;

            if (string.Equals(slot.slotName, slotName, StringComparison.Ordinal))
                return true;

            SlotDataAsset asset = slot.asset;
            if (asset == null)
                return false;

            return string.Equals(asset.sourceSlot, slotName, StringComparison.Ordinal)
                || string.Equals(asset.name, slotName, StringComparison.Ordinal);
        }

        public static string GetSlotSelectionName(SlotData slot)
        {
            if (slot == null)
                return string.Empty;

            if (!string.IsNullOrEmpty(slot.slotName))
                return slot.slotName;

            if (slot.asset != null)
                return slot.asset.sourceSlot;

            return string.Empty;
        }

        private Transform GetTargetTransform()
        {
            if (targetRenderer != null) return targetRenderer.transform;
            if (targetFilter != null) return targetFilter.transform;
            return null;
        }

        private Mesh GetTargetMesh()
        {
            if (targetRenderer != null) return targetRenderer.sharedMesh;
            if (targetFilter != null) return targetFilter.sharedMesh;
            return null;
        }

        private void ApplyWorkingMesh(Mesh mesh)
        {
            if (targetRenderer != null) ApplyBakedRendererMesh(mesh);
            else if (targetFilter != null)
            {
                targetFilter.mesh = mesh;

                _targetFilterMeshRenderer = targetFilter.GetComponent<MeshRenderer>();
                if (_targetFilterMeshRenderer != null)
                {
                    if (!_targetFilterRendererInitialized)
                    {
                        _targetFilterRendererWasEnabled = _targetFilterMeshRenderer.enabled;
                        _targetFilterRendererInitialized = true;
                    }

                    _targetFilterMeshRenderer.enabled = _meshPreviewVisible && _targetFilterRendererWasEnabled;
                }
            }
        }

        private void ApplyBakedRendererMesh(Mesh mesh)
        {
            if (targetRenderer == null || mesh == null) return;

            EnsureBakedRenderer();
            if (_bakedMeshFilter == null || _bakedMeshRenderer == null) return;

            _bakedMeshFilter.sharedMesh = mesh;
            _bakedMeshRenderer.sharedMaterials = BakePreviewMaterials(targetRenderer.sharedMaterials, mesh.subMeshCount, GetSelectedSlotData());
            if (targetRenderer.enabled)
            {
                _targetRendererWasEnabled = true;
                targetRenderer.enabled = false;
            }
            _bakedMeshRenderer.enabled = _meshPreviewVisible;
        }

        private Material[] BakePreviewMaterials(Material[] sourceMaterials, int targetSubMeshCount, SlotData selectedSlot)
        {
            if (sourceMaterials == null || sourceMaterials.Length == 0)
                return null;

            if (_bakedMaterials != null)
            {
                for (int i = 0; i < _bakedMaterials.Length; i++)
                {
                    if (_bakedMaterials[i] != null)
                        DestroyImmediate(_bakedMaterials[i]);
                }
            }

            int materialCount = selectedSlot != null ? Mathf.Max(1, targetSubMeshCount) : sourceMaterials.Length;
            _bakedMaterials = new Material[materialCount];

            for (int i = 0; i < materialCount; i++)
            {
                int sourceIndex = selectedSlot != null ? Mathf.Clamp(selectedSlot.submeshIndex, 0, sourceMaterials.Length - 1) : i;
                Material source = sourceMaterials[sourceIndex];
                if (source == null)
                {
                    _bakedMaterials[i] = null;
                    continue;
                }

                Material clone = new Material(source);
                clone.name = source.name + " (Lattice Baked)";

                // Convert any RenderTexture properties to persistent Texture2D copies
                Shader shader = source.shader;
                if (shader != null)
                {
                    int propertyCount = shader.GetPropertyCount();
                    for (int p = 0; p < propertyCount; p++)
                    {
                        if (shader.GetPropertyType(p) != UnityEngine.Rendering.ShaderPropertyType.Texture)
                            continue;

                        string propName = shader.GetPropertyName(p);
                        Texture tex = source.GetTexture(propName);
                        if (tex == null || !(tex is RenderTexture))
                            continue;

                        RenderTexture rt = (RenderTexture)tex;
                        Texture2D baked = BakeRenderTextureToTexture2D(rt, propName);
                        if (baked != null)
                            clone.SetTexture(propName, baked);
                    }
                }

                _bakedMaterials[i] = clone;
            }

            return _bakedMaterials;
        }

        private bool TryCreateSlotMeshFromBakedMesh(Mesh bakedMesh, SlotData slot, out Mesh slotMesh)
        {
            slotMesh = null;
            if (bakedMesh == null || slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                return false;

            UMAMeshData meshData = slot.asset.meshData;
            int sourceVertexStart = slot.vertexOffset;
            int sourceVertexCount = meshData.vertexCount;
            if (sourceVertexStart < 0 || sourceVertexCount <= 0 || sourceVertexStart + sourceVertexCount > bakedMesh.vertexCount)
            {
                Debug.LogWarning($"[UMALattice] Slot '{slot.slotName}' cannot be extracted from baked mesh '{bakedMesh.name}' (offset={sourceVertexStart}, count={sourceVertexCount}, bakedVerts={bakedMesh.vertexCount}).");
                return false;
            }

            slotMesh = new Mesh
            {
                indexFormat = sourceVertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };

            slotMesh.vertices = CopyRange(bakedMesh.vertices, sourceVertexStart, sourceVertexCount);
            Vector3[] normals = bakedMesh.normals;
            if (normals != null && normals.Length >= sourceVertexStart + sourceVertexCount)
                slotMesh.normals = CopyRange(normals, sourceVertexStart, sourceVertexCount);

            Vector4[] tangents = bakedMesh.tangents;
            if (tangents != null && tangents.Length >= sourceVertexStart + sourceVertexCount)
                slotMesh.tangents = CopyRange(tangents, sourceVertexStart, sourceVertexCount);

            Vector2[] uv = bakedMesh.uv;
            if (uv != null && uv.Length >= sourceVertexStart + sourceVertexCount)
                slotMesh.uv = CopyRange(uv, sourceVertexStart, sourceVertexCount);

            Vector2[] uv2 = bakedMesh.uv2;
            if (uv2 != null && uv2.Length >= sourceVertexStart + sourceVertexCount)
                slotMesh.uv2 = CopyRange(uv2, sourceVertexStart, sourceVertexCount);

            Vector2[] uv3 = bakedMesh.uv3;
            if (uv3 != null && uv3.Length >= sourceVertexStart + sourceVertexCount)
                slotMesh.uv3 = CopyRange(uv3, sourceVertexStart, sourceVertexCount);

            Vector2[] uv4 = bakedMesh.uv4;
            if (uv4 != null && uv4.Length >= sourceVertexStart + sourceVertexCount)
                slotMesh.uv4 = CopyRange(uv4, sourceVertexStart, sourceVertexCount);

            Color[] colors = bakedMesh.colors;
            if (colors != null && colors.Length == bakedMesh.vertexCount)
                slotMesh.colors = CopyRange(colors, sourceVertexStart, sourceVertexCount);

            Color32[] colors32 = bakedMesh.colors32;
            if (colors32 != null && colors32.Length == bakedMesh.vertexCount)
                slotMesh.colors32 = CopyRange(colors32, sourceVertexStart, sourceVertexCount);

            int subMeshCount = Mathf.Max(1, meshData.subMeshCount);
            slotMesh.subMeshCount = subMeshCount;
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                int[] triangles = GetSlotTriangles(meshData, subMesh);
                slotMesh.SetTriangles(triangles, subMesh, false);
            }

            slotMesh.RecalculateBounds();
            if (slotMesh.normals == null || slotMesh.normals.Length != sourceVertexCount)
                slotMesh.RecalculateNormals();

            return true;
        }

        private static int[] GetSlotTriangles(UMAMeshData meshData, int subMesh)
        {
            if (meshData == null || meshData.submeshes == null || subMesh < 0 || subMesh >= meshData.submeshes.Length || meshData.submeshes[subMesh] == null)
                return Array.Empty<int>();

            int[] triangles = meshData.submeshes[subMesh].GetBaseTriangles();
            return triangles ?? Array.Empty<int>();
        }

        private static T[] CopyRange<T>(T[] source, int start, int count)
        {
            T[] result = new T[count];
            Array.Copy(source, start, result, 0, count);
            return result;
        }

        private static Texture2D BakeRenderTextureToTexture2D(RenderTexture rt, string propertyName)
        {
            if (rt == null || rt.width <= 0 || rt.height <= 0)
                return null;

            RenderTexture active = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D result = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            result.name = "LatticeBaked_" + propertyName;
            result.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            result.Apply();

            RenderTexture.active = active;

            return result;
        }

        private void EnsureBakedRenderer()
        {
            if (_bakedMeshFilter != null && _bakedMeshRenderer != null) return;
            if (targetRenderer == null) return;

            string previewName = "UMALattice Baked Preview " + GetInstanceID();
            GameObject previewObject = GameObject.Find(previewName);
            if (previewObject == null) previewObject = new GameObject(previewName);
            previewObject.hideFlags = HideFlags.DontSave;
            Transform previewTransform = previewObject.transform;
            previewTransform.SetParent(null, false);
            previewTransform.localPosition = Vector3.zero;
            previewTransform.localRotation = Quaternion.identity;
            previewTransform.localScale = Vector3.one;

            _bakedMeshFilter = previewObject.GetComponent<MeshFilter>();
            if (_bakedMeshFilter == null) _bakedMeshFilter = previewObject.AddComponent<MeshFilter>();

            _bakedMeshRenderer = previewObject.GetComponent<MeshRenderer>();
            if (_bakedMeshRenderer == null) _bakedMeshRenderer = previewObject.AddComponent<MeshRenderer>();
        }

        private void CleanupBakedRenderer()
        {
            if (targetRenderer != null && _targetRendererWasEnabled)
            {
                targetRenderer.enabled = true;
            }

            if (_bakedMaterials != null)
            {
                for (int i = 0; i < _bakedMaterials.Length; i++)
                {
                    if (_bakedMaterials[i] != null)
                        DestroyImmediate(_bakedMaterials[i]);
                }
                _bakedMaterials = null;
            }

            if (_bakedMeshRenderer != null)
            {
                _bakedMeshRenderer.enabled = false;
            }

            if (_targetFilterMeshRenderer != null && _targetFilterRendererInitialized)
            {
                _targetFilterMeshRenderer.enabled = _targetFilterRendererWasEnabled;
            }

            if (_bakedMeshFilter != null)
            {
                _bakedMeshFilter.sharedMesh = null;
            }

            if (_bakedMeshRenderer != null)
            {
                if (_bakedMeshRenderer.gameObject != null)
                    DestroyImmediate(_bakedMeshRenderer.gameObject);
            }

            _bakedMeshFilter = null;
            _bakedMeshRenderer = null;
            _targetRendererWasEnabled = false;
            _targetFilterMeshRenderer = null;
            _targetFilterRendererWasEnabled = false;
            _targetFilterRendererInitialized = false;
        }

        private static Bounds TransformBounds(Bounds sourceBounds, Matrix4x4 matrix)
        {
            Vector3 min = sourceBounds.min;
            Vector3 max = sourceBounds.max;
            Vector3 first = matrix.MultiplyPoint3x4(new Vector3(min.x, min.y, min.z));
            Bounds transformed = new Bounds(first, Vector3.zero);

            transformed.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(max.x, min.y, min.z)));
            transformed.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(min.x, max.y, min.z)));
            transformed.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(max.x, max.y, min.z)));
            transformed.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(min.x, min.y, max.z)));
            transformed.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(max.x, min.y, max.z)));
            transformed.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(min.x, max.y, max.z)));
            transformed.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(max.x, max.y, max.z)));

            return transformed;
        }

        private static void TransformMesh(Mesh mesh, Matrix4x4 matrix)
        {
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            }
            mesh.vertices = vertices;

            Vector3[] normals = mesh.normals;
            if (normals != null && normals.Length == vertices.Length)
            {
                for (int i = 0; i < normals.Length; i++)
                {
                    normals[i] = matrix.MultiplyVector(normals[i]).normalized;
                }
                mesh.normals = normals;
            }

            mesh.RecalculateBounds();
        }

        private void OffsetCut(int axis, int cutIndex, float delta)
        {
            EnsureControlPointArray();

            var grid = ControlPointGrid;
            if (axis == 0 && (cutIndex < 0 || cutIndex >= grid.x)) return;
            if (axis == 1 && (cutIndex < 0 || cutIndex >= grid.y)) return;
            if (axis == 2 && (cutIndex < 0 || cutIndex >= grid.z)) return;

            for (int iz = 0; iz < grid.z; iz++)
            for (int iy = 0; iy < grid.y; iy++)
            for (int ix = 0; ix < grid.x; ix++)
            {
                if ((axis == 0 && ix != cutIndex) || (axis == 1 && iy != cutIndex) || (axis == 2 && iz != cutIndex))
                    continue;

                int flat = FlatIndex(ix, iy, iz);
                Vector3 point = controlPoints[flat];
                if (axis == 0) point.x += delta;
                else if (axis == 1) point.y += delta;
                else point.z += delta;
                controlPoints[flat] = point;
            }
        }

        private static int DominantAxis(Vector3 localDelta)
        {
            float absX = Mathf.Abs(localDelta.x);
            float absY = Mathf.Abs(localDelta.y);
            float absZ = Mathf.Abs(localDelta.z);

            if (absX >= absY && absX >= absZ) return 0;
            if (absY >= absZ) return 1;
            return 2;
        }

        private float GetDampingWeight(float s, float t, float u)
        {
            if (damping <= 0f) return 1f;

            float edgeS = 1f - Mathf.Abs(s * 2f - 1f);
            float edgeT = 1f - Mathf.Abs(t * 2f - 1f);
            float edgeU = 1f - Mathf.Abs(u * 2f - 1f);
            float edgeFade = Mathf.Min(edgeS, Mathf.Min(edgeT, edgeU));
            edgeFade = edgeFade * edgeFade * (3f - 2f * edgeFade);

            return Mathf.Lerp(1f, edgeFade, damping);
        }

        private static AnimationCurve s_easeInCurve = CreateEaseInCurve();
        private static AnimationCurve s_easeOutCurve = CreateEaseOutCurve();

        private static AnimationCurve CreateEaseInCurve()
        {
            var c = new AnimationCurve();
            c.AddKey(new Keyframe(0f, 1f, 0f, -2f));
            c.AddKey(new Keyframe(1f, 0f, -2f, 0f));
            return c;
        }

        private static AnimationCurve CreateEaseOutCurve()
        {
            var c = new AnimationCurve();
            c.AddKey(new Keyframe(0f, 0f, 0f, 2f));
            c.AddKey(new Keyframe(1f, 1f, 2f, 0f));
            return c;
        }

        private float GetFalloffWeight(float s, float t, float u)
        {
            if (curveMode == CurveMode.Constant) return 1f;

            Vector3 influenceCenter = ComputeInfluenceCenter();
            if (float.IsNaN(influenceCenter.x)) return 1f;

            float dx = s - influenceCenter.x;
            float dy = t - influenceCenter.y;
            float dz = u - influenceCenter.z;
            float distance = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
            float normalizedDistance = Mathf.Clamp01(distance / Mathf.Sqrt(3f));

            AnimationCurve activeCurve = null;
            switch (curveMode)
            {
                case CurveMode.EaseIn: activeCurve = s_easeInCurve; break;
                case CurveMode.EaseOut: activeCurve = s_easeOutCurve; break;
                case CurveMode.Custom: activeCurve = customCurve; break;
            }

            if (activeCurve == null || activeCurve.length == 0) return 1f;
            return Mathf.Clamp01(activeCurve.Evaluate(normalizedDistance));
        }

        /// <summary>
        /// Returns the parametric (s,t,u) centroid of all control points that have been
        /// displaced from their base positions. Returns a vector with NaN x when no
        /// points are displaced.
        /// </summary>
        private Vector3 ComputeInfluenceCenter()
        {
            EnsureControlPointArray();

            var grid = ControlPointGrid;
            Vector3 sum = Vector3.zero;
            int count = 0;

            for (int iz = 0; iz < grid.z; iz++)
            for (int iy = 0; iy < grid.y; iy++)
            for (int ix = 0; ix < grid.x; ix++)
            {
                int flat = FlatIndex(ix, iy, iz);
                if ((controlPoints[flat] - baseControlPoints[flat]).sqrMagnitude < 1e-7f)
                    continue;

                float si = grid.x > 1 ? ix / (float)(grid.x - 1) : 0f;
                float ti = grid.y > 1 ? iy / (float)(grid.y - 1) : 0f;
                float ui = grid.z > 1 ? iz / (float)(grid.z - 1) : 0f;
                sum += new Vector3(si, ti, ui);
                count++;
            }

            if (count == 0)
                return new Vector3(float.NaN, float.NaN, float.NaN);

            return sum / count;
        }

        // -------- index helpers --------
        public int FlatIndex(int ix, int iy, int iz)
        {
            var g = ControlPointGrid;
            return ix + iy * g.x + iz * g.x * g.y;
        }

        public void UnflattenIndex(int flat, out int ix, out int iy, out int iz)
        {
            var g = ControlPointGrid;
            iz = flat / (g.x * g.y);
            int r = flat - iz * g.x * g.y;
            iy = r / g.x;
            ix = r - iy * g.x;
        }

        // -------- Bernstein polynomial math --------
        private static float Bernstein(int i, int n, float t)
        {
            if (i < 0 || i > n) return 0f;
            if (n == 0) return 1f;
            t = Mathf.Clamp01(t);
            if (t <= 0f) return i == 0 ? 1f : 0f;
            if (t >= 1f) return i == n ? 1f : 0f;
            return Binomial(n, i) * Mathf.Pow(t, i) * Mathf.Pow(1f - t, n - i);
        }

        private static int[,] _binomCache = new int[13, 13];
        private static bool _binomCacheBuilt;

        private static int Binomial(int n, int k)
        {
            if (k < 0 || k > n) return 0;
            if (k == 0 || k == n) return 1;
            if (n < 13)
            {
                if (!_binomCacheBuilt) { BuildBinomCache(); _binomCacheBuilt = true; }
                return _binomCache[n, k];
            }
            if (k > n - k) k = n - k;
            long v = 1;
            for (int i = 1; i <= k; i++) v = v * (n - k + i) / i;
            return (int)v;
        }

        private static void BuildBinomCache()
        {
            for (int n = 0; n < 13; n++)
            for (int k = 0; k <= n; k++)
                _binomCache[n, k] = (k == 0 || k == n) ? 1 : _binomCache[n - 1, k - 1] + _binomCache[n - 1, k];
        }

        // -------- Unity lifecycle --------
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void StaticInitializeOnLoad()
        {
            s_easeInCurve = CreateEaseInCurve();
            s_easeOutCurve = CreateEaseOutCurve();
            _binomCache = new int[13, 13];
            _binomCacheBuilt = false;
        }

        private void OnEnable()
        {
           // Debug.Log($"[UMALattice] OnEnable on '{name}' (instance {GetInstanceID()})");
            EnsureInitialized();
            RefreshEffectorsFromChildren();
            DeferDeformUntilReady();
        }

        private void OnDisable()
        {
            //Debug.Log($"[UMALattice] OnDisable on '{name}' (instance {GetInstanceID()})");
            UnsubscribeFromUMA();
            RestoreTarget();
        }

        private void OnValidate()
        {
            cuts.x = Mathf.Max(1, cuts.x);
            cuts.y = Mathf.Max(1, cuts.y);
            cuts.z = Mathf.Max(1, cuts.z);
            size.x = Mathf.Max(0.01f, size.x);
            size.y = Mathf.Max(0.01f, size.y);
            size.z = Mathf.Max(0.01f, size.z);
            damping = Mathf.Clamp01(damping);
            RefreshEffectorsFromChildren();
            if (isActiveAndEnabled) EditorDelayInit();
        }

        private void EditorDelayInit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) EnsureInitialized(); };
#endif
        }

        private void DeferDeformUntilReady()
        {
            ResolveTargetComponents();
            _pendingDeform = true;
            _pendingDeformAttempts = 0;
            SubscribeToUMA();

           // Debug.Log($"[UMALattice] Scheduling saved deformation apply on '{name}' (targetRenderer={targetRenderer}, targetFilter={targetFilter}, dca={_dca})");
            TryApplyPendingDeform("OnEnable");
        }

        private void SubscribeToUMA()
        {
            if (_dca != null) return;

           // Debug.Log($"[UMALattice] Attempting to subscribe to UMA on '{name}'");
            if (targetRenderer != null)
                _dca = targetRenderer.GetComponentInParent<DynamicCharacterAvatar>();

            // Fallback: search from the lattice's own transform
            if (_dca == null)
                _dca = GetComponentInParent<DynamicCharacterAvatar>();

            if (_dca != null)
            {
               // Debug.Log($"[UMALattice] Subscribed to DynamicCharacterAvatar.OnCharacterUpdated on '{_dca.name}'");
                _dca.OnCharacterUpdated += OnUMACharacterUpdated;
            }
            else
            {
               // Debug.Log($"[UMALattice] No DynamicCharacterAvatar found for '{name}' yet; retry loop will keep checking for a mesh");
            }
        }

        private void UnsubscribeFromUMA()
        {
            if (_dca != null)
            {
                _dca.OnCharacterUpdated -= OnUMACharacterUpdated;
                _dca = null;
            }
            _pendingDeform = false;
            CancelInvoke("RetryApplyPendingDeform");
        }

        private void OnUMACharacterUpdated(UMAData umaData)
        {
            Debug.Log($"[UMALattice] UMA generated/updated for '{name}' (pendingDeform={_pendingDeform}, isActiveAndEnabled={isActiveAndEnabled})");

            _pendingDeform = true;
            TryApplyPendingDeform("UMA OnCharacterUpdated");
        }

        private void RetryApplyPendingDeform()
        {
            TryApplyPendingDeform("retry");
        }

        private void TryApplyPendingDeform(string reason)
        {
            if (!_pendingDeform) return;
            if (!isActiveAndEnabled) return;

            // Re-resolve the target renderer — the UMA may have created or replaced it.
            if (targetRenderer == null)
                ResolveTargetComponents();

            Mesh targetMesh = GetTargetMesh();
            // Debug.Log($"[UMALattice] TryApplyPendingDeform ({reason}) on '{name}': targetRenderer={targetRenderer}, mesh={targetMesh?.name}, verts={targetMesh?.vertexCount ?? 0}, dca={_dca}");

            if (targetMesh == null || targetMesh.vertexCount == 0)
            {
                if (SchedulePendingDeformRetry())
                    return;

                Debug.LogWarning($"[UMALattice] Gave up waiting for a built target mesh on '{name}' after {_pendingDeformAttempts} attempts");
                return;
            }

            if (!string.IsNullOrEmpty(selectedSlotName) && GetSelectedSlotData() == null)
            {
                if (SchedulePendingDeformRetry())
                    return;

                Debug.LogWarning($"[UMALattice] Gave up waiting for selected slot '{selectedSlotName}' on '{name}' after {_pendingDeformAttempts} attempts");
                return;
            }

            _pendingDeform = false;
            CancelInvoke("RetryApplyPendingDeform");

            //Debug.Log($"[UMALattice] Applying saved deformation on '{name}' ({reason})");
            _workingMesh = null;
            _originalVertices = null;
            DeformTarget();
        }

        private bool SchedulePendingDeformRetry()
        {
            if (_pendingDeformAttempts >= 40)
                return false;

            _pendingDeformAttempts++;
            SubscribeToUMA();
            CancelInvoke("RetryApplyPendingDeform");
            Invoke("RetryApplyPendingDeform", 0.25f);
            return true;
        }

        private bool HasActiveEffectors()
        {
            UMAEffector[] currentEffectors = Effectors;
            for (int i = 0; i < currentEffectors.Length; i++)
            {
                UMAEffector effector = currentEffectors[i];
                if (effector != null && effector.enabled && effector.gameObject.activeInHierarchy)
                    return true;
            }

            return false;
        }

        private const float SimulatedVertexMergeEpsilon = 0.0001f;

        private void ApplyEffectors(Vector3[] verts, Vector3[] normals, Transform targetTransform, Matrix4x4 meshToWorld)
        {
            UMAEffector[] currentEffectors = Effectors;
            if (verts == null || verts.Length == 0 || currentEffectors.Length == 0)
                return;

            bool[] claimedVertices = new bool[verts.Length];
            Matrix4x4 worldToMesh = meshToWorld.inverse;
            Matrix4x4 normalToWorld = meshToWorld.inverse.transpose;

            for (int effectorIndex = 0; effectorIndex < currentEffectors.Length; effectorIndex++)
            {
                UMAEffector effector = currentEffectors[effectorIndex];
                if (effector == null || !effector.enabled || !effector.gameObject.activeInHierarchy)
                    continue;

                if (effector.simulateVertexMerging)
                {
                    ApplyEffectorWithMergedVertices(effector, verts, normals, targetTransform, meshToWorld, worldToMesh, normalToWorld, claimedVertices);
                    continue;
                }

                ApplyEffector(effector, verts, normals, targetTransform, meshToWorld, worldToMesh, normalToWorld, claimedVertices);
            }
        }

        private void ApplyEffector(UMAEffector effector, Vector3[] verts, Vector3[] normals, Transform targetTransform, Matrix4x4 meshToWorld, Matrix4x4 worldToMesh, Matrix4x4 normalToWorld, bool[] claimedVertices)
        {
            for (int vertexIndex = 0; vertexIndex < verts.Length; vertexIndex++)
            {
                if (!effector.accumulate && claimedVertices[vertexIndex])
                    continue;

                Vector3 worldPoint = meshToWorld.MultiplyPoint3x4(verts[vertexIndex]);
                Vector3 worldNormal = normals != null && vertexIndex < normals.Length
                    ? normalToWorld.MultiplyVector(normals[vertexIndex]).normalized
                    : targetTransform.up;

                if (!effector.TryGetWorldDelta(worldPoint, worldNormal, out Vector3 worldDelta))
                    continue;

                verts[vertexIndex] += worldToMesh.MultiplyVector(worldDelta);
                claimedVertices[vertexIndex] = true;
            }
        }

        private void ApplyEffectorWithMergedVertices(UMAEffector effector, Vector3[] verts, Vector3[] normals, Transform targetTransform, Matrix4x4 meshToWorld, Matrix4x4 worldToMesh, Matrix4x4 normalToWorld, bool[] claimedVertices)
        {
            bool[] processedVertices = new bool[verts.Length];
            float mergeEpsilonSqr = SimulatedVertexMergeEpsilon * SimulatedVertexMergeEpsilon;

            for (int representativeIndex = 0; representativeIndex < verts.Length; representativeIndex++)
            {
                if (processedVertices[representativeIndex])
                    continue;

                Vector3 representativeWorldPoint = meshToWorld.MultiplyPoint3x4(verts[representativeIndex]);
                Vector3 representativeWorldNormal = normals != null && representativeIndex < normals.Length
                    ? normalToWorld.MultiplyVector(normals[representativeIndex]).normalized
                    : targetTransform.up;

                List<int> mergedVertices = null;
                for (int candidateIndex = representativeIndex + 1; candidateIndex < verts.Length; candidateIndex++)
                {
                    if (processedVertices[candidateIndex])
                        continue;

                    Vector3 candidateWorldPoint = meshToWorld.MultiplyPoint3x4(verts[candidateIndex]);
                    if ((candidateWorldPoint - representativeWorldPoint).sqrMagnitude <= mergeEpsilonSqr)
                    {
                        mergedVertices ??= new List<int>();
                        mergedVertices.Add(candidateIndex);
                    }
                }

                bool clusterClaimed = !effector.accumulate && claimedVertices[representativeIndex];
                if (!clusterClaimed && mergedVertices != null)
                {
                    for (int i = 0; i < mergedVertices.Count; i++)
                    {
                        if (claimedVertices[mergedVertices[i]])
                        {
                            clusterClaimed = true;
                            break;
                        }
                    }
                }

                processedVertices[representativeIndex] = true;
                if (mergedVertices != null)
                {
                    for (int i = 0; i < mergedVertices.Count; i++)
                        processedVertices[mergedVertices[i]] = true;
                }

                if (clusterClaimed)
                    continue;

                if (!effector.TryGetWorldDelta(representativeWorldPoint, representativeWorldNormal, out Vector3 worldDelta))
                    continue;

                Vector3 localDelta = worldToMesh.MultiplyVector(worldDelta);
                verts[representativeIndex] += localDelta;
                claimedVertices[representativeIndex] = true;

                if (mergedVertices != null)
                {
                    for (int i = 0; i < mergedVertices.Count; i++)
                    {
                        int mergedIndex = mergedVertices[i];
                        verts[mergedIndex] += localDelta;
                        claimedVertices[mergedIndex] = true;
                    }
                }
            }
        }

        private void EnsureInitialized()
        {
            EnsureControlPointArray();
        }

        private void EnsureControlPointArray()
        {
            int expected = ControlPointCount;
            bool controlPointsValid = controlPoints != null && controlPoints.Length == expected;
            bool baseControlPointsValid = baseControlPoints != null && baseControlPoints.Length == expected;

            if (!controlPointsValid && !baseControlPointsValid)
            {
                controlPoints = new Vector3[expected];
                baseControlPoints = new Vector3[expected];
                for (int iz = 0; iz < ControlPointGrid.z; iz++)
                    for (int iy = 0; iy < ControlPointGrid.y; iy++)
                        for (int ix = 0; ix < ControlPointGrid.x; ix++)
                        {
                            int flat = FlatIndex(ix, iy, iz);
                            Vector3 position = UniformPosition(ix, iy, iz);
                            baseControlPoints[flat] = position;
                            controlPoints[flat] = position;
                        }
                return;
            }

            if (!baseControlPointsValid)
            {
                baseControlPoints = new Vector3[expected];
                for (int iz = 0; iz < ControlPointGrid.z; iz++)
                    for (int iy = 0; iy < ControlPointGrid.y; iy++)
                        for (int ix = 0; ix < ControlPointGrid.x; ix++)
                            baseControlPoints[FlatIndex(ix, iy, iz)] = UniformPosition(ix, iy, iz);
            }

            if (!controlPointsValid)
            {
                controlPoints = new Vector3[expected];
                Array.Copy(baseControlPoints, controlPoints, expected);
            }
        }

        private Vector3 UniformPosition(int ix, int iy, int iz)
        {
            float dx = cuts.x > 0 ? (ix / (float)cuts.x) * size.x : 0f;
            float dy = cuts.y > 0 ? (iy / (float)cuts.y) * size.y : 0f;
            float dz = cuts.z > 0 ? (iz / (float)cuts.z) * size.z : 0f;
            return new Vector3(offset.x + dx, offset.y + dy, offset.z + dz);
        }

        private void GetAxisPositions(out float[] axisX, out float[] axisY, out float[] axisZ)
        {
            var grid = ControlPointGrid;
            axisX = new float[grid.x];
            axisY = new float[grid.y];
            axisZ = new float[grid.z];

            if (baseControlPoints == null || baseControlPoints.Length != ControlPointCount)
            {
                for (int ix = 0; ix < grid.x; ix++) axisX[ix] = UniformPosition(ix, 0, 0).x;
                for (int iy = 0; iy < grid.y; iy++) axisY[iy] = UniformPosition(0, iy, 0).y;
                for (int iz = 0; iz < grid.z; iz++) axisZ[iz] = UniformPosition(0, 0, iz).z;
                return;
            }

            for (int ix = 0; ix < grid.x; ix++) axisX[ix] = baseControlPoints[FlatIndex(ix, 0, 0)].x;
            for (int iy = 0; iy < grid.y; iy++) axisY[iy] = baseControlPoints[FlatIndex(0, iy, 0)].y;
            for (int iz = 0; iz < grid.z; iz++) axisZ[iz] = baseControlPoints[FlatIndex(0, 0, iz)].z;
        }

        private Vector3 EvaluateLattice(Vector3 localPoint, float[] axisX, float[] axisY, float[] axisZ)
        {
            var grid = ControlPointGrid;
            if (grid.x < 2 || grid.y < 2 || grid.z < 2) return localPoint;

            float s = ParameterFromAxis(localPoint.x, axisX);
            float t = ParameterFromAxis(localPoint.y, axisY);
            float u = ParameterFromAxis(localPoint.z, axisZ);

            if (s < 0f || t < 0f || u < 0f)
                return localPoint;

            if (useGlobalDeformation)
                return EvaluateLatticeGlobal(localPoint, s, t, u, axisX, axisY, axisZ);

            return EvaluateLatticeLocal(localPoint, s, t, u, axisX, axisY, axisZ);
        }

        private Vector3 EvaluateLatticeGlobal(Vector3 localPoint, float s, float t, float u, float[] axisX, float[] axisY, float[] axisZ)
        {
            var grid = ControlPointGrid;
            int L = cuts.x, M = cuts.y, N = cuts.z;

            float[] bS = new float[grid.x];
            float[] bT = new float[grid.y];
            float[] bU = new float[grid.z];
            for (int i = 0; i <= L; i++) bS[i] = Bernstein(i, L, s);
            for (int j = 0; j <= M; j++) bT[j] = Bernstein(j, M, t);
            for (int k = 0; k <= N; k++) bU[k] = Bernstein(k, N, u);

            Vector3 basePosition = Vector3.zero;
            Vector3 controlPosition = Vector3.zero;
            for (int k = 0; k <= N; k++)
            {
                float bu = bU[k];
                if (bu < 1e-7f) continue;
                for (int j = 0; j <= M; j++)
                {
                    float bt = bT[j] * bu;
                    if (bt < 1e-7f) continue;
                    for (int i = 0; i <= L; i++)
                    {
                        float w = bS[i] * bt;
                        if (w < 1e-7f) continue;
                        int flat = FlatIndex(i, j, k);
                        basePosition += baseControlPoints[flat] * w;
                        controlPosition += controlPoints[flat] * w;
                    }
                }
            }

            float dampingWeight = GetDampingWeight(s, t, u);
            float falloffWeight = GetFalloffWeight(s, t, u);
            return localPoint + (controlPosition - basePosition) * (dampingWeight * falloffWeight);
        }

        private Vector3 EvaluateLatticeLocal(Vector3 localPoint, float s, float t, float u, float[] axisX, float[] axisY, float[] axisZ)
        {
            var grid = ControlPointGrid;
            int L = cuts.x, M = cuts.y, N = cuts.z;

            // Find the two-neighbor span along each axis (linear B-spline / hat function)
            float sScaled = s * L;
            int i0 = Mathf.Clamp((int)sScaled, 0, L - 1);
            float fracS = sScaled - i0;
            float wS0 = 1f - fracS;
            float wS1 = fracS;

            float tScaled = t * M;
            int j0 = Mathf.Clamp((int)tScaled, 0, M - 1);
            float fracT = tScaled - j0;
            float wT0 = 1f - fracT;
            float wT1 = fracT;

            float uScaled = u * N;
            int k0 = Mathf.Clamp((int)uScaled, 0, N - 1);
            float fracU = uScaled - k0;
            float wU0 = 1f - fracU;
            float wU1 = fracU;

            Vector3 basePosition = Vector3.zero;
            Vector3 controlPosition = Vector3.zero;
            float totalWeight = 0f;

            for (int dk = 0; dk <= 1; dk++)
            for (int dj = 0; dj <= 1; dj++)
            for (int di = 0; di <= 1; di++)
            {
                int ix = i0 + di;
                int iy = j0 + dj;
                int iz = k0 + dk;

                float wx = di == 0 ? wS0 : wS1;
                float wy = dj == 0 ? wT0 : wT1;
                float wz = dk == 0 ? wU0 : wU1;
                float w = wx * wy * wz;

                int flat = FlatIndex(ix, iy, iz);
                basePosition += baseControlPoints[flat] * w;
                controlPosition += controlPoints[flat] * w;
                totalWeight += w;
            }

            // Guard against degenerate cells where totalWeight is not 1.0
            if (totalWeight < 1e-6f)
                return localPoint;

            float dampingWeight = GetDampingWeight(s, t, u);
            float falloffWeight = GetFalloffWeight(s, t, u);
            return localPoint + (controlPosition - basePosition) * (dampingWeight * falloffWeight);
        }

        private void MoveCutAxis(int axis, int cutIndex, float delta)
        {
            EnsureControlPointArray();

            int axisCount = axis == 0 ? ControlPointGrid.x : axis == 1 ? ControlPointGrid.y : ControlPointGrid.z;
            if (cutIndex < 0 || cutIndex >= axisCount) return;

            float current = GetCutAxisCoordinate(axis, cutIndex);
            SetCutAxisCoordinate(axis, cutIndex, current + delta);
        }

        private void SetCutAxisCoordinate(int axis, int cutIndex, float coordinate)
        {
            EnsureControlPointArray();

            int axisCount = axis == 0 ? ControlPointGrid.x : axis == 1 ? ControlPointGrid.y : ControlPointGrid.z;
            if (cutIndex < 0 || cutIndex >= axisCount) return;

            float min = cutIndex > 0 ? GetCutAxisCoordinate(axis, cutIndex - 1) + 1e-4f : float.NegativeInfinity;
            float max = cutIndex < axisCount - 1 ? GetCutAxisCoordinate(axis, cutIndex + 1) - 1e-4f : float.PositiveInfinity;
            float clamped = Mathf.Clamp(coordinate, min, max);

            var grid = ControlPointGrid;
            for (int iz = 0; iz < grid.z; iz++)
            for (int iy = 0; iy < grid.y; iy++)
            for (int ix = 0; ix < grid.x; ix++)
            {
                if ((axis == 0 && ix != cutIndex) || (axis == 1 && iy != cutIndex) || (axis == 2 && iz != cutIndex))
                    continue;

                int flat = FlatIndex(ix, iy, iz);
                Vector3 deformationOffset = controlPoints[flat] - baseControlPoints[flat];
                Vector3 basePoint = baseControlPoints[flat];
                if (axis == 0) basePoint.x = clamped;
                else if (axis == 1) basePoint.y = clamped;
                else basePoint.z = clamped;

                baseControlPoints[flat] = basePoint;
                controlPoints[flat] = basePoint + deformationOffset;
            }
        }

        private float GetCutAxisCoordinate(int axis, int cutIndex)
        {
            if (baseControlPoints == null || baseControlPoints.Length != ControlPointCount) return axis == 0 ? offset.x : axis == 1 ? offset.y : offset.z;

            if (axis == 0) return baseControlPoints[FlatIndex(cutIndex, 0, 0)].x;
            if (axis == 1) return baseControlPoints[FlatIndex(0, cutIndex, 0)].y;
            return baseControlPoints[FlatIndex(0, 0, cutIndex)].z;
        }

        private void SyncVolumeToBaseCuts()
        {
            GetAxisPositions(out float[] axisX, out float[] axisY, out float[] axisZ);

            if (axisX.Length > 0 && axisY.Length > 0 && axisZ.Length > 0)
            {
                offset = new Vector3(axisX[0], axisY[0], axisZ[0]);
                size = new Vector3(axisX[axisX.Length - 1] - axisX[0], axisY[axisY.Length - 1] - axisY[0], axisZ[axisZ.Length - 1] - axisZ[0]);
                size = new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y), Mathf.Max(0.01f, size.z));
            }
        }

        private void RebaseWorkingMeshBaseline()
        {
            if (_workingMesh == null) return;
            _originalVertices = (Vector3[])_workingMesh.vertices.Clone();
        }

        private static float ParameterFromAxis(float coordinate, float[] axisPositions)
        {
            if (axisPositions == null || axisPositions.Length < 2) return -1f;

            float start = axisPositions[0];
            float end = axisPositions[axisPositions.Length - 1];
            if (coordinate < start || coordinate > end) return -1f;
            if (coordinate <= start) return 0f;
            if (coordinate >= end) return 1f;

            int last = axisPositions.Length - 1;
            for (int i = 0; i < last; i++)
            {
                float a = axisPositions[i];
                float b = axisPositions[i + 1];
                if (coordinate <= b)
                {
                    float span = b - a;
                    float local = Mathf.Abs(span) > 1e-6f ? (coordinate - a) / span : 0f;
                    return (i + Mathf.Clamp01(local)) / last;
                }
            }

            return 1f;
        }
    }
}
