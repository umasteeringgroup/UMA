using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    public class UMARendererAsset : ScriptableObject
    {
        public string RendererName;
        public bool UpdateWhenOffscreen = false;
        public bool SkinnedMotionVectors = false;
        [Header("Lighting")]
        public UnityEngine.Rendering.ShadowCastingMode CastShadows = UnityEngine.Rendering.ShadowCastingMode.On;
        public bool ReceiveShadows = true;

        public void ApplySettingsToRenderer(SkinnedMeshRenderer smr)
        {
            if(!string.IsNullOrEmpty(RendererName))
                smr.name = RendererName;

            smr.updateWhenOffscreen = UpdateWhenOffscreen;
            smr.skinnedMotionVectors = SkinnedMotionVectors;
            smr.shadowCastingMode = CastShadows;
            smr.receiveShadows = ReceiveShadows;
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
