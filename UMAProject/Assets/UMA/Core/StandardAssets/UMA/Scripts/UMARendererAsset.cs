using UnityEngine;
using Unity.Collections;

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
        [Tooltip("If true, the normals will be recalculated on the mesh when it is created. Requires BURST option in preferences.")]
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
#if UMA_BURSTCOMPILE
            if (_RecalculateNormals)
            {
                Recalculate(smr);
            }
#endif
        }

#if UMA_BURSTCOMPILE
        private void Recalculate(SkinnedMeshRenderer smr)
        {
            if (_RecalculateNormals)
            {
                NativeArray<Vector3> vertices = new NativeArray<Vector3>(smr.sharedMesh.vertices, Allocator.TempJob);
                NativeArray<Vector3> normals = new NativeArray<Vector3>(smr.sharedMesh.normals, Allocator.TempJob);
                NativeArray<Vector2> uvs = new NativeArray<Vector2>(smr.sharedMesh.uv, Allocator.TempJob);
                NativeArray<Vector4> tangents = new NativeArray<Vector4>(smr.sharedMesh.tangents, Allocator.TempJob);
                NativeArray<int> triangles = new NativeArray<int>(smr.sharedMesh.triangles, Allocator.TempJob);
                Vector3[] deltaVertices = new Vector3[vertices.Length];
                JobHandle handle = default;

                int blendShapeCount = smr.sharedMesh.blendShapeCount;
                for (int shapeIndex = 0; shapeIndex < blendShapeCount; shapeIndex++)
                {
                    float weight = smr.GetBlendShapeWeight(shapeIndex);
                    if (weight > 0f)
                    {
                        weight /= 100f; //bring the weight into 0-1 range.

                        string blendShapeName = smr.sharedMesh.GetBlendShapeName(shapeIndex);
                        if (blendShapeName.StartsWith(blendShapeNameStartsWith))
                        {
                            smr.sharedMesh.GetBlendShapeFrameVertices(shapeIndex, 0, deltaVertices, null, null);
                            NativeArray<Vector3> blendShapeDeltaVertices = new NativeArray<Vector3>(deltaVertices, Allocator.TempJob);

                            handle = MeshUtilities.BakeOneFramePositionBlendShape(vertices, blendShapeDeltaVertices, weight, handle);
                        }
                    }
                }
                handle = MeshUtilities.RecalculateNormalsTangentsJobified(vertices, normals, uvs, tangents, triangles, this.NormalAngle, handle);

                handle.Complete();

                //We don't need to do this if we know our mesh is a single instance!
                //Mesh mesh = Instantiate<Mesh>(smr.sharedMesh);
                Mesh mesh = smr.sharedMesh;

                //mesh.SetVertices(vertices); //don't update the vertices if the blendshapes are going to continue...
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTangents(tangents);

                smr.sharedMesh = mesh;

                vertices.Dispose();
                normals.Dispose();
                uvs.Dispose();
                tangents.Dispose();
                triangles.Dispose();
            }
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
