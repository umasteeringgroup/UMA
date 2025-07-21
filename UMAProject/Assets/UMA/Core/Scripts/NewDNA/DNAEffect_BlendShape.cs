using System;
using UMA;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    [Serializable]
    public class DNAEffect_BlendShape : DNAEffect
    {
        public string BlendShapeName;
        public override string Description => "Sets a blendshape to the translated value on every skinned mesh renderer in the avatar.";
#if UNITY_EDITOR
        public override void DoGui(bool showDescription, bool showHelp)
        {
            base.DoGui(showDescription,showHelp);
            BlendShapeName = UnityEditor.EditorGUILayout.TextField("Blend Shape Name", BlendShapeName);
        }
#endif
        public override DNAInstanceCollection.DNABuildType AreaEffect => DNAInstanceCollection.DNABuildType.BlendShape;
        public override void PostApply(UMAData avatar, DNA dna, float value)
        {
            base.PostApply(avatar, dna, value);
            if (avatar != null && !string.IsNullOrEmpty(BlendShapeName))
            {
                value = GetMappedValue(value);
                var skinnedMeshRenderers = avatar.GetRenderers();
                foreach (var smr in skinnedMeshRenderers)
                {
                    if (smr is SkinnedMeshRenderer skinnedMeshRenderer)
                    {
                        int blendShapeIndex = skinnedMeshRenderer.sharedMesh.GetBlendShapeIndex(BlendShapeName);
                        if (blendShapeIndex >= 0)
                        {
                            skinnedMeshRenderer.SetBlendShapeWeight(blendShapeIndex, value * 100f); // UMA uses 0-100 for blendshape weights
                        }
                    }
                }
            }
        }
    }
}