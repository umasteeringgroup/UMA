using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    [System.Serializable]
    public class DNAEffect_BoneScale : DNAEffect
    {
        [Tooltip("The bone to scale. If empty, the root bone will be used.")]
        public string BoneName;
        private uint boneHash;
        [Tooltip("The scale factor to apply to the bone.")]
        public Vector3 ScaleFactor = Vector3.one;
        public override string Description => "Scales a bone by a specified factor.";
        public override DNAInstanceCollection.DNABuildType AreaEffect => DNAInstanceCollection.DNABuildType.Rig;
#if UNITY_EDITOR
        override public void DoGui(bool showDescription, bool showHelp)
        {
            base.DoGui(showDescription, showHelp);
            BoneName = UnityEditor.EditorGUILayout.TextField("Bone Name", BoneName);
            if (string.IsNullOrEmpty(BoneName))
            {
                UnityEditor.EditorGUILayout.HelpBox("Bone Name is required.", UnityEditor.MessageType.Error);
            }
            else
            {
                boneHash = (uint)UMAUtils.StringToHash(BoneName);
            }
            ScaleFactor = UnityEditor.EditorGUILayout.Vector3Field("Scale Factor", ScaleFactor);
        }
#endif
        public override void PostApply(UMAData avatar, DNA dna, float value)
        {
            base.PostApply(avatar, dna, value);
            if (avatar != null && !string.IsNullOrEmpty(BoneName))
            {
                Transform boneTransform = avatar.skeleton.GetBoneTransform(BoneName);
                if (boneTransform != null)
                {
                    Vector3 ScaleAmount = ScaleFactor * GetMappedValue(value);
                    Vector3 ResultScale = Vector3.one + ScaleAmount; 
                    boneTransform.localScale = Vector3.Scale(boneTransform.localScale, ResultScale);
                }
            }
        }

    }
}