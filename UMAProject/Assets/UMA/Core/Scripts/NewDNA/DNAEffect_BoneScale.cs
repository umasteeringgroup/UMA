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
                    var bone = skeleton.GetBoneTransform(hash);
                    if (bone == null) return;
                    //Vector3 currentScale = skeleton.GetScale(hash);
                    Vector3 currentScale = bone.localScale;
                    Vector3 scaleAmount = ScaleFactor * GetMappedValue(value);
                    Vector3 resultScale = Vector3.Scale(currentScale, Vector3.one + scaleAmount);
                    skeleton.SetScale(hash, resultScale);
                }
            }
        }
    }
}