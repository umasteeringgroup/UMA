using UnityEngine;
using Unity.Collections;
using UnityEngine.Rendering;


#if UMA_BURSTCOMPILE
using Unity.Jobs;
#endif

namespace UMA
{
    /// <summary>
    /// This asset stores values to set on a skinned mesh renderer during uma generation.
    /// </summary>
    public class UMARendererAsset : ScriptableObject
    {
        #region Public Getter Properties
        public string RendererName { get { return _RendererName; } }
        public int Layer { get { return _Layer; } }
#if UNITY_2018_3_OR_NEWER
        public uint RendererLayerMask {  get { return _RendererLayerMask; } }
        public int RendererPriority { get { return _RendererPriority; } }
#endif
        public bool UpdateWhenOffscreen { get { return _UpdateWhenOffscreen; } }
        public bool SkinnedMotionVectors { get { return _SkinnedMotionVectors; } }
        public MotionVectorGenerationMode MotionVectors { get { return _MotionVectors; } }
#if UNITY_2017_2_OR_NEWER
        public bool DynamicOccluded { get { return _DynamicOccluded; } }
#endif
        public UnityEngine.Rendering.ShadowCastingMode CastShadows { get { return _CastShadows; } }
        public bool ReceiveShadows { get { return _ReceiveShadows; } }
        public UMAClothProperties ClothProperties { get { return _ClothProperties; } }
        public bool RecalculateNormals { get { return _RecalculateNormals; } }
        public float NormalAngle { get { return _NormalAngle; } }
        #endregion

        #region Private variables
        [Tooltip("The name that will be given to the object that this renderer will be added to.")]
        [SerializeField] private string _RendererName="";
        [Tooltip("This is the layer that the renderer object will be set to.")]
        [SerializeField] private int _Layer = 0; //Is this still needed in 2018.3 with RendererLayerMask?
#if UNITY_2018_3_OR_NEWER
        [SerializeField] private uint _RendererLayerMask = 1;
        [SerializeField] private int _RendererPriority = 0;
#endif
        [SerializeField] private bool _UpdateWhenOffscreen = false;
        [SerializeField] private bool _SkinnedMotionVectors = true;
        [SerializeField] MotionVectorGenerationMode _MotionVectors = MotionVectorGenerationMode.Object;
#if UNITY_2017_2_OR_NEWER
        [SerializeField] private bool _DynamicOccluded = true;
#endif
        [Header("Lighting")]
        [SerializeField] private UnityEngine.Rendering.ShadowCastingMode _CastShadows = UnityEngine.Rendering.ShadowCastingMode.On;
        [SerializeField] private bool _ReceiveShadows = true;

        [Header("Cloth")]
        [Tooltip("The cloth properties asset to apply to this renderer. Use this only if planning to use the cloth component with this material.")]
        [SerializeField] private UMAClothProperties _ClothProperties = null;

        [Header("Build Options")]
        [Tooltip("If true, normals are recalculated after DNA and race blendshape weights are applied. Requires BURST option in preferences. " +
            "These renderers never share meshes through mesh reuse (so avatars using them cannot be NPC templates), and with Mark Not Readable " +
            "every DNA change rebuilds the mesh.")]
        [SerializeField] private bool _RecalculateNormals = false;
        [Tooltip("The angle to use when recalculating normals.")]
        [SerializeField] private float _NormalAngle = 60f;
#if UMA_BURSTCOMPILE
        [Tooltip("The name of the blendshape to use when recalculating normals.")]
        [SerializeField] private string blendShapeNameStartsWith = "";
#endif
#endregion

        /// <summary>
        /// Sets the Skinned Mesh Renderer to the values on this UMA Renderer Asset.
        /// </summary>
        /// <param name="smr"></param>
        public void ApplySettingsToRenderer(SkinnedMeshRenderer smr)
        {
            if(!string.IsNullOrEmpty(RendererName))
            {
                smr.name = RendererName;
            }

            smr.gameObject.layer = _Layer;
#if UNITY_2018_3_OR_NEWER
            smr.renderingLayerMask = _RendererLayerMask;
            smr.rendererPriority = _RendererPriority;
#endif
            smr.updateWhenOffscreen = _UpdateWhenOffscreen;
            smr.skinnedMotionVectors = _SkinnedMotionVectors;
            smr.motionVectorGenerationMode = _MotionVectors;
#if UNITY_2017_2_OR_NEWER
            smr.allowOcclusionWhenDynamic = _DynamicOccluded;
#endif
            smr.shadowCastingMode = _CastShadows;
            smr.receiveShadows = _ReceiveShadows;
            // Normal recalculation runs after combining and applying blendshape DNA,
            // before the final upload; see FinalizeGeneratedNormals.
        }

        /// <summary>
        /// Returns the hierarchy name for a generated renderer. A configured
        /// Renderer Name takes precedence over UMA's indexed fallback name.
        /// </summary>
        public static string GetRendererGameObjectName(
            UMARendererAsset rendererAsset,
            int rendererIndex)
        {
            if (rendererAsset != null &&
                !string.IsNullOrWhiteSpace(rendererAsset.RendererName))
            {
                return rendererAsset.RendererName;
            }

            return rendererIndex == 0
                ? "UMARenderer"
                : $"UMARenderer {rendererIndex}";
        }

        /// <summary>
        /// Applies renderer settings and the configured hierarchy name in one
        /// operation so every mesh-combiner path follows the same rules.
        /// </summary>
        public static void ApplySettingsAndName(
            SkinnedMeshRenderer renderer,
            UMARendererAsset rendererAsset,
            int rendererIndex)
        {
            if (renderer == null)
            {
                return;
            }

            if (rendererAsset != null)
            {
                rendererAsset.ApplySettingsToRenderer(renderer);
            }
            else
            {
                ResetRenderer(renderer);
            }

            renderer.gameObject.name =
                GetRendererGameObjectName(rendererAsset, rendererIndex);
        }

        /// <summary>
        /// The renderer asset that configures a renderer built from these
        /// generated materials. All materials of one renderer share the same
        /// renderer asset; null means the avatar's default renderer.
        /// </summary>
        internal static UMARendererAsset ResolveRendererAsset(UMAData data,
            System.Collections.Generic.IReadOnlyList<UMAData.GeneratedMaterial> materials)
        {
            if (materials != null)
            {
                for (int i = 0; i < materials.Count; i++)
                {
                    if (materials[i] != null)
                    {
                        return materials[i].rendererAsset != null
                            ? materials[i].rendererAsset
                            : data != null ? data.defaultRendererAsset : null;
                    }
                }
            }
            return data != null ? data.defaultRendererAsset : null;
        }

        /// <summary>
        /// True when a renderer built from these materials must own its mesh.
        /// Recalculated normals depend on that renderer's blendshape weights,
        /// which are not part of the mesh cache key, so such a mesh can never
        /// be shared through the generated-resource cache.
        /// </summary>
        internal static bool RequiresPrivateMesh(UMAData data,
            System.Collections.Generic.IReadOnlyList<UMAData.GeneratedMaterial> materials)
        {
#if UMA_BURSTCOMPILE
            UMARendererAsset rendererAsset = ResolveRendererAsset(data, materials);
            return rendererAsset != null && rendererAsset._RecalculateNormals;
#else
            return false;
#endif
        }

        /// <summary>
        /// Uploads combined geometry, retaining CPU data when generator finalization
        /// still needs to calculate normals using the final blendshape weights.
        /// </summary>
        internal static void UploadGeneratedMesh(UMAData data, SkinnedMeshRenderer renderer,
            System.Collections.Generic.IReadOnlyList<UMAData.GeneratedMaterial> materials)
        {
            if (renderer == null || renderer.sharedMesh == null) return;
            // The combiner has just written new geometry.
            UMAResourceLeaseOwner.ForgetPrivateMesh(renderer);
#if UMA_BURSTCOMPILE
            // Recalculated normals from an earlier build are gone with it.
            UMARendererNormals.Invalidate(renderer.sharedMesh);
            UMARendererAsset rendererAsset = ResolveRendererAsset(data, materials);
            if (rendererAsset != null && rendererAsset._RecalculateNormals)
            {
                if (data.deferRendererMeshUpload)
                {
                    renderer.sharedMesh.UploadMeshData(false);
                    return;
                }
                rendererAsset.Recalculate(renderer);
            }
#endif
            renderer.sharedMesh.UploadMeshData(data.markNotReadable);
        }

        /// <summary>
        /// For a renderer whose own mesh was kept instead of being combined again (see
        /// UMAResourceLeaseOwner.TryKeepPrivateMesh). The geometry is unchanged, but the blendshape weights may
        /// not be: a direct combiner call recalculates the normals now, as it would after combining; a
        /// generator-managed build leaves that to FinalizeGeneratedNormals.
        /// </summary>
        internal static void RefreshKeptMesh(UMAData data, SkinnedMeshRenderer renderer,
            System.Collections.Generic.IReadOnlyList<UMAData.GeneratedMaterial> materials)
        {
#if UMA_BURSTCOMPILE
            if (renderer == null || renderer.sharedMesh == null || data.deferRendererMeshUpload) return;
            UMARendererAsset rendererAsset = ResolveRendererAsset(data, materials);
            if (rendererAsset != null && rendererAsset._RecalculateNormals)
            {
                rendererAsset.Recalculate(renderer);
                renderer.sharedMesh.UploadMeshData(data.markNotReadable);
            }
#endif
        }

        internal readonly struct DeferredMeshUpload : System.IDisposable
        {
            private readonly UMAData data;
            private readonly bool previous;
            internal DeferredMeshUpload(UMAData data)
            {
                this.data = data;
                previous = data.deferRendererMeshUpload;
                data.deferRendererMeshUpload = true;
            }
            public void Dispose() { if (data != null) data.deferRendererMeshUpload = previous; }
        }

        // A shape-only update cannot recalculate an already uploaded, unreadable
        // mesh. Rebuild its geometry before applying the new blendshape weights.
        internal static void RequireReadableNormalsMesh(UMAData data)
        {
#if UMA_BURSTCOMPILE
            if (!data.isShapeDirty && !data.NeedsAnimatorBindingRefresh) return;
            for (int i = 0; i < data.RendererCount; i++)
            {
                var asset = data.GetRendererAsset(i) ?? data.defaultRendererAsset;
                var mesh = data.GetRenderer(i) != null ? data.GetRenderer(i).sharedMesh : null;
                if (asset != null && asset._RecalculateNormals && mesh != null &&
                    (!mesh.isReadable || UMAResourceLeaseOwner.IsSharedMesh(data.GetRenderer(i))))
                    data.isMeshDirty = true;
            }
#endif
        }

        /// <summary>Run after DNA and race blendshape weights, before completion events.</summary>
        internal static void FinalizeGeneratedNormals(UMAData data)
        {
#if UMA_BURSTCOMPILE
            for (int i = 0; i < data.RendererCount; i++)
            {
                var renderer = data.GetRenderer(i);
                var asset = data.GetRendererAsset(i) ?? data.defaultRendererAsset;
                if (asset == null || !asset._RecalculateNormals || renderer == null ||
                    renderer.sharedMesh == null || !renderer.sharedMesh.isReadable ||
                    UMAResourceLeaseOwner.IsSharedMesh(renderer)) continue;
                asset.Recalculate(renderer);
                renderer.sharedMesh.UploadMeshData(data.markNotReadable);
            }
#endif
        }

#if UMA_BURSTCOMPILE
        // Test hook: the recalculated normals, observed before a final upload can
        // discard the CPU copy of the mesh. Never used by runtime code.
        internal static System.Action<SkinnedMeshRenderer, Vector3[]> NormalsRecalculatedForTests;

        private void Recalculate(SkinnedMeshRenderer smr)
        {
            Mesh mesh = smr != null ? smr.sharedMesh : null;
            if (!_RecalculateNormals || mesh == null || mesh.vertexCount == 0 || !mesh.isReadable)
            {
                return;
            }

            // A reused mesh is owned by the generated-resource cache and may be
            // bound to other avatars with different blendshape weights. Never
            // write per-renderer results into it.
            if (UMAResourceLeaseOwner.IsSharedMesh(smr))
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning($"UMARendererAsset '{name}': Recalculate Normals skipped for '{smr.name}' because its mesh is shared by the generated-resource cache. Disable mesh reuse for this renderer to recalculate normals.", smr);
                }
                return;
            }

            // Skipped when the mesh already holds the result for these blendshape weights; see UMARendererNormals.
            System.Action<NativeArray<Vector3>> observer = null;
            if (NormalsRecalculatedForTests != null)
            {
                // The arguments are only evaluated when a test observes the result.
                observer = normals => NormalsRecalculatedForTests?.Invoke(smr, normals.ToArray());
            }
            UMARendererNormals.Recalculate(smr, this.NormalAngle, blendShapeNameStartsWith, observer);
        }
#endif
        /// <summary>
        /// Reset the given Skinned Mesh Renderer to default values.
        /// </summary>
        /// <param name="renderer"></param>
        static public void ResetRenderer(SkinnedMeshRenderer renderer)
        {
            // renderer.gameObject.layer = 0;
#if UNITY_2018_3_OR_NEWER
            renderer.renderingLayerMask = 1;
            renderer.rendererPriority = 0;
#endif
            renderer.updateWhenOffscreen = false;
            renderer.skinnedMotionVectors = true;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
#if UNITY_2017_2_OR_NEWER
            renderer.allowOcclusionWhenDynamic = true;
#endif
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Assets/Create/UMA/Rendering/Renderer Asset")]
        public static void CreateRendererAsset()
        {
            CustomAssetUtility.CreateAsset<UMARendererAsset>();
        }
#endif
    }
}
