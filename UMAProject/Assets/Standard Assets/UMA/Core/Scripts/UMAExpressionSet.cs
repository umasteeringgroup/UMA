//	============================================================
//	Name:		UMAExpressionSet
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace UMA.PoseTools
{
	/// <summary>
	/// UMA expression set. Groups poses for expression player channels.
	/// </summary>
	[System.Serializable]
	public class UMAExpressionSet : ScriptableObject
	{
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
		/// Resets all the bones used by poses in the set to default position.
		/// </summary>
		/// <param name="umaSkeleton">Skeleton to be reset.</param>
		public void ResetBones(UMASkeleton umaSkeleton)
		{
			if (umaSkeleton == null) return;

			ValidateBoneHashes();

			foreach (int hash in boneHashes)
			{
				if (!umaSkeleton.Reset(hash))
				{
					Debug.LogWarning("Couldn't reset bone!");
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
			for(int i = 0; i < boneHashes.Length; i++ )
			{
				res[i] = umaSkeleton.GetBoneGameObject(boneHashes[i]).transform;
			}
			return res;
		}
	}
}