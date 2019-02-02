using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    public class UMARendererAsset : ScriptableObject
    {
        public string RendererName { get { return _RendererName; } }
        public bool UpdateWhenOffscreen { get { return _UpdateWhenOffscreen; } }
        public bool SkinnedMotionVectors { get { return _SkinnedMotionVectors; } }
        public UnityEngine.Rendering.ShadowCastingMode CastShadows { get { return _CastShadows; } }
        public bool ReceiveShadows { get { return _ReceiveShadows; } }
        public UMAClothProperties ClothProperties { get { return _ClothProperties; } }

        [SerializeField] private string _RendererName;
        [SerializeField] private bool _UpdateWhenOffscreen = false;
        [SerializeField] private bool _SkinnedMotionVectors = false;

        [Header("Lighting")]
        [SerializeField] private UnityEngine.Rendering.ShadowCastingMode _CastShadows = UnityEngine.Rendering.ShadowCastingMode.On;
        [SerializeField] private bool _ReceiveShadows = true;

        [Header("Cloth")]
        [Tooltip("The cloth properties asset to apply to this renderer. Use this only if planning to use the cloth component with this material.")]
        [SerializeField] private UMAClothProperties _ClothProperties;

        public void ApplySettingsToRenderer(SkinnedMeshRenderer smr)
        {
            if(!string.IsNullOrEmpty(RendererName))
                smr.name = RendererName;

            smr.updateWhenOffscreen = _UpdateWhenOffscreen;
            smr.skinnedMotionVectors = _SkinnedMotionVectors;
            smr.shadowCastingMode = _CastShadows;
            smr.receiveShadows = _ReceiveShadows;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Assets/Create/UMA/Misc/Renderer Asset")]
        public static void CreateRendererAsset()
        {
            CustomAssetUtility.CreateAsset<UMARendererAsset>();
        }
#endif
    }
}
