using UnityEngine;
using UMA;
using UMA.CharacterSystem;
using static UMA.DNAInstanceCollection;

namespace UMA
{
    /// <summary>
    /// DNA effect that offsets one bone's local position, rotation and scale by the mapped DNA value.
    /// It uses the same math as a bone pose (<see cref="UMASkeleton.Morph"/>), so it stacks with
    /// bone poses and the other bone effects, in the order the effects run.
    /// </summary>
    [System.Serializable]
    public class DNAEffect_BoneTransform : DNAEffect
    {
        /// <summary>
        /// Offset added to the local position at a mapped value of 1. Zero leaves the position alone.
        /// </summary>
        public Vector3 Position;
        /// <summary>
        /// Rotation in euler degrees applied on top of the local rotation at a mapped value of 1.
        /// Zero leaves the rotation alone.
        /// </summary>
        public Vector3 Rotation;
        /// <summary>
        /// Multiplier for the local scale at a mapped value of 1. One leaves the scale alone.
        /// </summary>
        public Vector3 Scale = Vector3.one;

        public string boneName;
        private uint boneHash;

        public override string Description => "Offsets a bone's local position, rotation and scale by the translated DNA value, the way a bone pose does for one bone. Position is added, Rotation is applied on top of the current rotation, and Scale multiplies the current scale. Position 0, Rotation 0 and Scale 1 change nothing.";

        public override DNAInstanceCollection.DNABuildType AreaEffect => DNABuildType.Rig;
        public override ExpressionEffectPhase ExpressionPhases =>
            ExpressionEffectPhase.EarlyRestore |
            ExpressionEffectPhase.LateRig;
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

        /// <summary>
        /// The scale part only: <see cref="UMASkeleton.Morph"/> lerps the local scale towards
        /// scale * Scale by the mapped value, so it is multiplied by (1 + (Scale - 1) * mappedValue).
        /// The position and rotation offsets are not a scale and are left out.
        /// </summary>
        public override bool TryGetScaleFactor(float value, out Vector3 factor)
        {
            factor = Vector3.one + (Scale - Vector3.one) * GetMappedValue(value);
            return true;
        }

        /// <inheritdoc />
        public override void Apply(UMAData avatar, DNA dna, float value)
        {
            base.Apply(avatar, dna, value);
            MorphBone(avatar, value, null);
        }

        public override void CollectExpressionBones(
            System.Collections.Generic.List<int> boneHashes)
        {
            if (boneHashes != null && !string.IsNullOrEmpty(boneName))
            {
                boneHashes.Add(UMAUtils.StringToHash(boneName));
            }
        }

        public override void ApplyExpressionRig(
            UMAData avatar,
            DNA dna,
            float value,
            System.Predicate<int> shouldApplyBone)
        {
            MorphBone(avatar, value, shouldApplyBone);
        }

        // One bone of a bone pose: the offsets go on top of whatever the bone holds now,
        // so earlier bone poses and bone effects are kept.
        private void MorphBone(UMAData avatar, float value, System.Predicate<int> shouldApplyBone)
        {
            if (avatar == null || avatar.skeleton == null ||
                string.IsNullOrEmpty(boneName))
            {
                return;
            }

            int hash = UMAUtils.StringToHash(boneName);
            if ((shouldApplyBone != null && !shouldApplyBone(hash)) ||
                !avatar.skeleton.HasBone(hash))
            {
                return;
            }

            float mappedValue = GetMappedValue(value);
            if (Mathf.Approximately(mappedValue, 0f))
            {
                return;
            }

            avatar.skeleton.Morph(hash, Position, Scale, Quaternion.Euler(Rotation), mappedValue);
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
            Position = UnityEditor.EditorGUILayout.Vector3Field("Position Offset", Position);
            Rotation = UnityEditor.EditorGUILayout.Vector3Field("Rotation Offset", Rotation);
            Scale = UnityEditor.EditorGUILayout.Vector3Field("Scale Multiplier", Scale);
        }
#endif
    }
} // namespace UMA
