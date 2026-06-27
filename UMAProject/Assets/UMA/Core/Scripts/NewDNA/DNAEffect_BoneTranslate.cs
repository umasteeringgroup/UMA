using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// DNA effect that translates a specified bone by Translation * mappedValue in local space.
    /// </summary>
    [System.Serializable]
    public class DNAEffect_BoneTranslate : DNAEffect
    {
        [Tooltip("The bone to translate. If empty, the root bone will be used.")]
        public string BoneName;
        private uint boneHash;

        [Tooltip("The translation vector to apply to the bone.")]
        public Vector3 Translation;
        public override string Description => "Translates a bone by a specified vector. Normal values for min/max are -1 to 1. The curve takes the incoming 0..1 values and maps to the output values. Create a middle point on the curve at 0.5 for no effect in the center.";

        public override DNAInstanceCollection.DNABuildType AreaEffect => DNAInstanceCollection.DNABuildType.Rig;
#if UNITY_EDITOR
        /// <inheritdoc />
        public override void DoGui(bool showDescription, bool showHelp, out AnimationCurve curveToCopy)
        {
            base.DoGui(showDescription, showHelp, out curveToCopy);
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
        /// <inheritdoc />
        public override void Restore(UMAData avatar, DNA dna, float value)
        {
            if (avatar == null || avatar.skeleton == null || string.IsNullOrEmpty(BoneName))
            {
                return;
            }
            avatar.skeleton.Restore(UMAUtils.StringToHash(BoneName));
        }
        /// <inheritdoc />
        public override void Apply(UMAData avatar, DNA dna, float value)
        {
            base.Apply(avatar, dna, value);
            if (avatar != null && !string.IsNullOrEmpty(BoneName))
            {
                var skeleton = avatar.skeleton;
                if (skeleton != null)
                {
                    int hash = UMAUtils.StringToHash(BoneName);
                    Vector3 currentPos = skeleton.GetPosition(hash);
                    skeleton.SetPosition(hash, currentPos + Translation * GetMappedValue(value));
                }
            }
        }

    }
}