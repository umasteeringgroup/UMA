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
        }
#endif
        /// <inheritdoc />
        public override void Apply(UMAData avatar, DNA dna, float value)
        {
            base.PostApply(avatar, dna, value);
            if (avatar != null && bonePose != null)
            {
                bonePose.ApplyPose(avatar.skeleton, GetMappedValue(value));
            }
        }
    }
}
