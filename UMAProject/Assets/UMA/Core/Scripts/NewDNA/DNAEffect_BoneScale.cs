using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// DNA effect that scales a bone by a factor derived from the mapped DNA value.
    /// The resulting scale is (currentScale * (1 + ScaleFactor * mappedValue)).
    /// </summary>
    [System.Serializable]
    public class DNAEffect_BoneScale : DNAEffect
    {
        /// <summary>
        /// Name of the bone to scale.
        /// </summary>
        public string BoneName;
        private uint boneHash;
        /// <summary>
        /// Scale multiplier applied per axis; combined with mapped DNA value.
        /// </summary>
        public Vector3 ScaleFactor = Vector3.one;
        public override string Description => "Scales a bone by a specified factor. Normal values for min/max are -1 to 1. The curve takes the incoming 0..1 values and maps to the output values. Create a middle point on the curve at 0.5 for no effect in the center.";
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
            ScaleFactor = UnityEditor.EditorGUILayout.Vector3Field("Scale Factor", ScaleFactor);
        }
#endif
        /// <inheritdoc />
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