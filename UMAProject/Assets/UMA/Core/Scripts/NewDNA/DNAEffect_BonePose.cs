using UMA.CharacterSystem;
using UMA.PoseTools;
using UnityEngine;

namespace UMA
{
    [System.Serializable]

    public class DNAEffect_BonePose : DNAEffect
    {
        public UMABonePose bonePose;
        public override string Description => "Applies a bone pose to the character's skeleton. This is done before any single bone DNA is applied.";

        public override DNAInstanceCollection.DNABuildType AreaEffect => DNAInstanceCollection.DNABuildType.Rig;
#if UNITY_EDITOR
        override public void DoGui(bool showDescription, bool showHelp)
        {
            base.DoGui(showDescription,showHelp);
            bonePose = UnityEditor.EditorGUILayout.ObjectField("Bone Pose", bonePose, typeof(UMABonePose), true) as UMABonePose;
            if (bonePose == null)
            {
                UnityEditor.EditorGUILayout.HelpBox("Bone Pose is required.", UnityEditor.MessageType.Error);
            }
        }
#endif
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
