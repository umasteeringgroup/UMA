//	============================================================
//	Name:		UMAExpressionSet
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

using UMA;

namespace UMA.PoseTools
{
	[System.Serializable]
	public class UMAExpressionSet : ScriptableObject
	{
		// Mutually exclusive expressions can share a curve
		[System.Serializable]
		public class PosePair
		{
			public UMABonePose primary = null;
			public UMABonePose inverse = null;
		}
		public PosePair[] posePairs = new PosePair[UMAExpressionPlayer.PoseCount];

		[System.NonSerialized]
		private int[] boneHashes = null;

		public void ResetBones(UMASkeleton umaSkeleton)
		{
			if (umaSkeleton == null) return;

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

			foreach (int hash in boneHashes)
			{
				if (!umaSkeleton.Reset(hash))
				{
					Debug.LogWarning("Couldn't reset bone!");
				}
			}
		}
	}
}