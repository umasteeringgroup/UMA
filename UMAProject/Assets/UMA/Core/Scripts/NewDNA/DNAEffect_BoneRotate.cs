using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
    [System.Serializable]

    public class DNAEffect_BoneRotate : DNAEffect
    {
        public string BoneName;
        public uint boneHash;
        public Vector3 RotationAxis = Vector3.up;
        public float RotationAngle = 45f; // Degrees
        public override string Description => "Rotates a bone around a specified axis by a given angle.";
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
            RotationAxis = UnityEditor.EditorGUILayout.Vector3Field("Rotation Axis", RotationAxis);
            RotationAngle = UnityEditor.EditorGUILayout.FloatField("Rotation Angle (degrees)", RotationAngle);
        }
#endif
        public override void PostApply(DynamicCharacterAvatar avatar, DNA dna, float value)
        {
            base.PostApply(avatar, dna, value);
            if (avatar != null && !string.IsNullOrEmpty(BoneName))
            {
                Transform boneTransform = avatar.umaData.skeleton.GetBoneTransform(BoneName);
                if (boneTransform != null)
                {
                    float angle = RotationAngle * GetMappedValue(value);
                    Quaternion rotation = Quaternion.AngleAxis(angle, RotationAxis);
                    boneTransform.localRotation *= rotation;
                }
            }
        }
    }
}
