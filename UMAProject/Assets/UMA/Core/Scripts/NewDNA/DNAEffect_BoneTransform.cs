using UnityEngine;
using UMA;
using UMA.CharacterSystem;
using static UMA.DNAInstanceCollection;

namespace UMA
{
    public class DNAEffect_BoneTransform : DNAEffect
    {
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Scale = Vector3.one;

        public string boneName;
        private uint boneHash;

        public override string Description => "Lerps a bones transform to an absolute local position/rotation/scale based on the translated DNA value. ";

        public override DNAInstanceCollection.DNABuildType AreaEffect => DNABuildType.Rig;
        public override void PostApply(DynamicCharacterAvatar avatar, DNA dna, float value)
        {
            base.PostApply(avatar, dna, value);
            if (avatar != null && !string.IsNullOrEmpty(boneName))
            {
                Transform boneTransform = avatar.umaData.skeleton.GetBoneTransform(boneName);
                if (boneTransform != null)
                {
                    value = GetMappedValue(value);
                    // Apply position, rotation, and scale
                    Vector3 scaleDelta = (boneTransform.localScale - Scale) * value;
                    Vector3 positionDelta = (boneTransform.localPosition - Position) * value;
                    Quaternion rotationQuat = Quaternion.Euler(Rotation);


                    boneTransform.localPosition +=  positionDelta;
                    boneTransform.localRotation = Quaternion.Slerp(boneTransform.localRotation, rotationQuat * boneTransform.localRotation, value);
                    boneTransform.localScale += scaleDelta;
                }
            }
        }

#if UNITY_EDITOR
        public override void DoGui(bool showDescription, bool showHelp)
        {
            base.DoGui(showDescription, showHelp);
            boneName = UnityEditor.EditorGUILayout.TextField("Bone Name", boneName);
            if (string.IsNullOrEmpty(boneName))
            {
                UnityEditor.EditorGUILayout.HelpBox("Bone Name is required.", UnityEditor.MessageType.Error);
            }
            else
            {
                boneHash = (uint)UMAUtils.StringToHash(boneName);
            }
            Position = UnityEditor.EditorGUILayout.Vector3Field("Position", Position);
            Rotation = UnityEditor.EditorGUILayout.Vector3Field("Rotation", Rotation);
            Scale = UnityEditor.EditorGUILayout.Vector3Field("Scale", Scale);
        }
#endif
    }
} // namespace UMA