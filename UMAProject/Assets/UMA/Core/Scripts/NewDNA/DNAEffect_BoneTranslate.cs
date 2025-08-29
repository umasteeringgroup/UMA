using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    [System.Serializable]
    public class DNAEffect_BoneTranslate : DNAEffect
    {
        [Tooltip("The bone to translate. If empty, the root bone will be used.")]
        public string BoneName;
        private uint boneHash;

        [Tooltip("The translation vector to apply to the bone.")]
        public Vector3 Translation;
        public override string Description => "Translates a bone by a specified vector.";
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
            Translation = UnityEditor.EditorGUILayout.Vector3Field("Translation", Translation);
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
                    boneTransform.localPosition += Translation * GetMappedValue(value);
                }
            }
        }

    }
}