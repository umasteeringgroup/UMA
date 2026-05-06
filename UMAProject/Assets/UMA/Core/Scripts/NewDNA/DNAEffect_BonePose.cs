using UMA.CharacterSystem;
using UMA.PoseTools;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// DNA effect that applies a UMABonePose to the avatar skeleton.
    /// This is evaluated through the curve/min/max mapping and applied during the rig pass,
    /// prior to individual bone effects so that later adjustments can layer on top.
    /// </summary>
    [System.Serializable]
    public class DNAEffect_BonePose : DNAEffect
    {
        /// <summary>
        /// The bone pose asset to apply to the skeleton.
        /// </summary>
        public UMABonePose bonePose;
        public bool isBasePose;

        public override string Description => "Applies a bone pose to the character's skeleton. This is done before any single bone DNA is applied.";

        public override DNAInstanceCollection.DNABuildType AreaEffect => DNAInstanceCollection.DNABuildType.Rig;
#if UNITY_EDITOR
        /// <inheritdoc />
        public override void DoGui(bool showDescription, bool showHelp, out AnimationCurve curveToCopy)
        {
            base.DoGui(showDescription, showHelp, out curveToCopy);
            bonePose = UnityEditor.EditorGUILayout.ObjectField("Bone Pose", bonePose, typeof(UMABonePose), true) as UMABonePose;
            if (bonePose == null)
            {
                UnityEditor.EditorGUILayout.HelpBox("Bone Pose is required.", UnityEditor.MessageType.Error);
            }
            UnityEditor.EditorGUILayout.HelpBox("Base Pose: If enabled, this pose will be applied first within the rig Apply phase, after the skeleton reset and before non-base rig effects. If disabled, the pose will be applied during the normal Apply phase, allowing it to layer on top of the current rig state.", UnityEditor.MessageType.Info);
            isBasePose = UnityEditor.EditorGUILayout.Toggle("Is Base Pose", isBasePose);
        }
#endif
        /// <inheritdoc />
        public override void Apply(UMAData avatar, DNA dna, float value)
        {
            base.Apply(avatar, dna, value);
            if (avatar != null && bonePose != null)
            {
                bonePose.ApplyPose(avatar.skeleton, GetMappedValue(value));
            }
        }

        /// <inheritdoc />
        public override void Restore(UMAData avatar, DNA dna, float value)
        {
            // Restore only bones touched by this pose to the saved post-DNA baseline
            if (avatar == null || avatar.skeleton == null || bonePose == null || bonePose.poses == null)
            {
                return;
            }

            var skeleton = avatar.skeleton;
            for (int i = 0; i < bonePose.poses.Length; i++)
            {
                var pb = bonePose.poses[i];
                skeleton.Restore(pb.hash);
            }
        }
    }
}
