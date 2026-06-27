using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// DNA effect that rotates a single bone around an axis by an angle scaled by the mapped DNA value.
    /// </summary>
    [System.Serializable]
    public class DNAEffect_BoneRotate : DNAEffect
    {
        /// <summary>
        /// Name of the bone to rotate.
        /// </summary>
        public string BoneName;
        public uint boneHash;
        /// <summary>
        /// Local axis to rotate around.
        /// </summary>
        public Vector3 RotationAxis = Vector3.up;
        /// <summary>
        /// Maximum rotation in degrees applied when the mapped value equals 1.
        /// </summary>
        public float RotationAngle = 45f; // Degrees
        public override string Description => "Rotates a bone around a specified axis by a given angle. Normal values for min/max are -1 to 1. The curve takes the incoming 0..1 values and maps to the output values. Create a middle point on the curve at 0.5 for no effect in the center.";

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
            RotationAxis = UnityEditor.EditorGUILayout.Vector3Field("Rotation Axis", RotationAxis);
            RotationAngle = UnityEditor.EditorGUILayout.FloatField("Rotation Angle (degrees)", RotationAngle);
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
                    float angle = RotationAngle * GetMappedValue(value);
                    Quaternion deltaRotation = Quaternion.AngleAxis(angle, RotationAxis);
                    Quaternion currentRotation = skeleton.GetRotation(hash);
                    skeleton.SetRotation(hash, currentRotation * deltaRotation);
                }
            }
        }
    }
}
