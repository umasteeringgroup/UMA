using UnityEngine;
using System.Collections.Generic;

namespace UMA.PoseTools
{
    /// <summary>
    /// UMA expression set. Groups poses for expression player channels.
    /// </summary>
    [System.Serializable]
    public class UMADynamicExpressionSet : ScriptableObject
    {
        public enum MecanimJoint : int
        {
            None = 0,
            Head = 1,
            Neck = 2,
            Jaw = 4,
            Eye = 8,
        };

        public List<UMADynamicExpression> Expressions = new List<UMADynamicExpression>();

        [System.Serializable]
        public class Expression
        {
            public string poseName;
            // If this is one of the overridable bones.
            public MecanimJoint overrideBone;

            /// <summary>
            /// bone based expression (can be null)
            /// </summary>
            public UMABonePose primaryBone;
            public UMABonePose inverseBone;

            /// <summary>
            /// Blendshape based pose (can be null or empty)
            /// </summary>
            public string Blendshape; 

            [Range(0.0f, 1.0f)]
            public float value = 0.0f;
            [Range(0.0f, 1.0f)]
            public float defaultValue = 0.5f;
            bool isBlink;
            bool isLeftEyeInOut;
            bool isLeftEyeUpDown;
            bool isRightEyeInOut;
            bool isRightEyeUpDown;
        }

        /// <summary>
        /// Pair of mutually exclusive expressions which can share a curve.
        /// </summary>
        [System.Serializable]
        public class PosePair
        {
            public UMABonePose primary = null;
            public UMABonePose inverse = null;
        }
        /// <summary>
        /// The pose pairs for each expression channel.
        /// </summary>
        public PosePair[] posePairs = new PosePair[UMAExpressionPlayer.PoseCount];

        [System.NonSerialized]
        private int[] boneHashes = null;

        private void ValidateBoneHashes()
        {
            if (boneHashes == null)
            {
                List<int> boneHashList = new List<int>();
                foreach (PosePair pair in posePairs)
                {
                    if (pair.primary != null)
                    {
                        foreach (UMABonePose.PoseBone bone in pair.primary.poses)
                        {
                            if (!boneHashList.Contains(bone.hash))
                            {
                                boneHashList.Add(bone.hash);
                            }
                        }
                    }
                    if (pair.inverse != null)
                    {
                        foreach (UMABonePose.PoseBone bone in pair.inverse.poses)
                        {
                            if (!boneHashList.Contains(bone.hash))
                            {
                                boneHashList.Add(bone.hash);
                            }
                        }
                    }
                }

                boneHashes = boneHashList.ToArray();
            }
        }

        /// <summary>
        /// Restores all the bones used by poses in the set to default (post DNA) position.
        /// </summary>
        /// <param name="umaSkeleton">Skeleton to be reset.</param>
        /// <param name="logErrors"></param>
        public void RestoreBones(UMASkeleton umaSkeleton, bool logErrors = false)
        {
            if (umaSkeleton == null) return;

            ValidateBoneHashes();

            foreach (int hash in boneHashes)
            {
                if (!umaSkeleton.Restore(hash))
                {
					if (logErrors)
					{
						//Since this generally logs like crazy which screws everything anyway, it might be nice to provide some useful information?
						var umaname = umaSkeleton.GetBoneGameObject(umaSkeleton.rootBoneHash).GetComponentInParent<UMAAvatarBase>().gameObject.name;
						string boneName = "";
						foreach (PosePair pair in posePairs)
						{
							if (pair.primary != null)
							{
								foreach (UMABonePose.PoseBone bone in pair.primary.poses)
								{
									if (bone.hash == hash)
									{
										boneName = bone.bone;
									}
								}
							}
							if (pair.inverse != null)
							{
								foreach (UMABonePose.PoseBone bone in pair.inverse.poses)
								{
									if (bone.hash == hash)
									{
										boneName = bone.bone;
									}
								}
							}
						}
						if (Debug.isDebugBuild)
							Debug.LogWarning("Couldn't reset bone! " + boneName + " on " + umaname);
					}
                }
            }
        }

        public int[] GetAnimatedBoneHashes()
        {
            ValidateBoneHashes();
            return boneHashes;
        }

        /// <summary>
        /// Gets the transforms for all animated bones.
        /// </summary>
        /// <returns>Array of transforms.</returns>
        /// <param name="umaSkeleton">Skeleton containing transforms.</param>
        public Transform[] GetAnimatedBones(UMASkeleton umaSkeleton)
        {
            if (umaSkeleton == null) return null;

            ValidateBoneHashes();

            var res = new Transform[boneHashes.Length];
            for (int i = 0; i < boneHashes.Length; i++)
            {
                res[i] = umaSkeleton.GetBoneGameObject(boneHashes[i]).transform;
            }
            return res;
        }
    }
}
