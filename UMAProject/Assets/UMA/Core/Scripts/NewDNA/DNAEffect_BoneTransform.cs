using UnityEngine;
using UMA;
using UMA.CharacterSystem;
using static UMA.DNAInstanceCollection;

namespace UMA
{
    /// <summary>
    /// DNA effect that lerps a bone's local position/rotation/scale toward target values based on the mapped DNA value.
    /// </summary>
    [System.Serializable]
    public class DNAEffect_BoneTransform : DNAEffect
    {
        /// <summary>
        /// Target local position.
        /// </summary>
        public Vector3 Position;
        /// <summary>
        /// Target local euler rotation in degrees.
        /// </summary>
        public Vector3 Rotation;
        /// <summary>
        /// Target local scale.
        /// </summary>
        public Vector3 Scale = Vector3.one;

        public string boneName;
        private uint boneHash;

        public override string Description => "Lerps a bones transform to an absolute local position/rotation/scale based on the translated DNA value. ";

        public override DNAInstanceCollection.DNABuildType AreaEffect => DNABuildType.Rig;
        /// <inheritdoc />
        public override void Restore(UMAData avatar, DNA dna, float value)
        {
            if (avatar == null || avatar.skeleton == null || string.IsNullOrEmpty(boneName))
            {
                return;
            }
            // Restore baseline for this bone only
            avatar.skeleton.Restore(UMAUtils.StringToHash(boneName));
        }

        /// <inheritdoc />
        public override void Apply(UMAData avatar, DNA dna, float value)
        {
            base.Apply(avatar, dna, value);
            if (avatar != null && !string.IsNullOrEmpty(boneName))  
            {
                var skeleton = avatar.skeleton;
                int hash = UMAUtils.StringToHash(boneName);
                if (skeleton != null && skeleton.HasBone(hash))
                {
                    value = GetMappedValue(value);
                    Vector3 currentPosition = skeleton.GetPosition(hash);
                    Quaternion currentRotation = skeleton.GetRotation(hash);
                    Vector3 currentScale = skeleton.GetScale(hash);
                    Vector3 scaleDelta = (currentScale - Scale) * value;
                    Vector3 positionDelta = (currentPosition - Position) * value;
                    Quaternion rotationQuat = Quaternion.Euler(Rotation);

                    skeleton.SetPosition(hash, currentPosition + positionDelta);
                    skeleton.SetRotation(hash, Quaternion.Slerp(currentRotation, rotationQuat * currentRotation, value));
                    skeleton.SetScale(hash, currentScale + scaleDelta);
                }
            }
        }

#if UNITY_EDITOR
        /// <inheritdoc />
        public override void DoGui(bool showDescription, bool showHelp, out AnimationCurve curveToCopy)
        {
            base.DoGui(showDescription, showHelp, out curveToCopy);
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