using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// A rig's rest skeleton saved as an asset: the bone hierarchy with each bone's local rest
    /// transform, and where the rig root sits in avatar space. It is what
    /// <see cref="UMARigRestPose.FromRace"/> reads from a race's base slots, kept on its own so a
    /// rig can be described without its race, body slots or recipes being in the build.
    /// </summary>
    /// <remarks>
    /// A race points at one of these through <see cref="RaceData.legacyRig"/> to say which older rig
    /// it replaces; <see cref="UMALegacySlotConverter"/> then converts slots built for that rig.
    /// The asset holds no bind poses, so it describes a rig to convert from, not one to convert to.
    /// </remarks>
    public sealed class UMARigRestPoseAsset : ScriptableObject
    {
        [Tooltip("Race this rest pose was baked from. For reference only.")]
        public string sourceRaceName;

        [Tooltip("Name of the rig root bone that slot bind poses are relative to, normally \"Global\".")]
        public string rootBoneName = "Global";

        [Tooltip("Transform of the rig root bone in avatar space.")]
        public Matrix4x4 rigRootToAvatar = Matrix4x4.identity;

        [Tooltip("Every bone below the rig root, with its local rest transform.")]
        public UMATransform[] bones = Array.Empty<UMATransform>();

        [NonSerialized] private UMARigRestPose restPose;

        /// <summary>The rest pose this asset describes. Built once and reused.</summary>
        public UMARigRestPose GetRestPose()
        {
            if (restPose == null)
            {
                restPose = UMARigRestPose.FromBones(bones, rootBoneName, rigRootToAvatar);
            }
            return restPose;
        }

        /// <summary>Copies a rest pose into this asset. The caller saves the asset.</summary>
        public void SetFrom(UMARigRestPose rig, string raceName)
        {
            if (rig == null)
            {
                throw new ArgumentNullException(nameof(rig));
            }

            var copied = new List<UMATransform>(rig.BoneCount);
            foreach (KeyValuePair<int, UMATransform> entry in rig.Bones)
            {
                if (entry.Value != null)
                {
                    copied.Add(entry.Value.Duplicate());
                }
            }
            // A fixed order keeps the saved asset stable from one bake to the next.
            copied.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            sourceRaceName = raceName;
            rootBoneName = rig.RootBoneName;
            rigRootToAvatar = rig.RigRootToAvatar;
            bones = copied.ToArray();
            restPose = null;
        }

        private void OnEnable()
        {
            restPose = null;
        }

        private void OnValidate()
        {
            restPose = null;
        }
    }
}
