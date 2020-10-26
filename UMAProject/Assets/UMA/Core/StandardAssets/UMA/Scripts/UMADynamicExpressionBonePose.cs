using UnityEngine;
using System.Collections.Generic;

namespace UMA.PoseTools
{
    /// <summary>
    /// UMA expression set. Groups poses for expression player channels.
    /// </summary>
    [System.Serializable]
    public  class UMADynamicExpressionBonePose : UMADynamicExpression
    {
        // If this is one of the overridable bones.
        public UMADynamicExpressionSet.MecanimJoint overrideBone;

        /// <summary>
        /// bone based expression (can be null)
        /// </summary>
        public UMABonePose primaryBone;
        public UMABonePose inverseBone;
        public uint PrimaryHash;
        public uint InverseHash;

        /// <summary>
        /// Initialize should be called OnCharacterCreated and OnCharacterUpdated
        /// </summary>
        /// <param name="umadata"></param>
        public override void Initialize(UMAData umadata)
        {
            // calc the bone hashes
            // cache any vars needed
        }

        public override void PreProcess(UMAData umadata)
        {
            // restore the bones in the skeleton
        }

        public override void Process(UMAData umadata)
        {
            // Adjust the bones.
        }
    }
}
