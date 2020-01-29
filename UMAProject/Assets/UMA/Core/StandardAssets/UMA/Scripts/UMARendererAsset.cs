using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
        #endregion

        /// <summary>
        /// Sets the Skinned Mesh Renderer to the values on this UMA Renderer Asset.
        /// </summary>
        /// <param name="smr"></param>
        public void ApplySettingsToRenderer(SkinnedMeshRenderer smr)
        {
            if(!string.IsNullOrEmpty(RendererName))
                smr.name = RendererName;

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
        }

        /// <summary>
        /// Reset the given Skinned Mesh Renderer to default values.
        /// </summary>
        /// <param name="renderer"></param>
        static public void ResetRenderer(SkinnedMeshRenderer renderer)
        {
            renderer.gameObject.layer = 0;
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
