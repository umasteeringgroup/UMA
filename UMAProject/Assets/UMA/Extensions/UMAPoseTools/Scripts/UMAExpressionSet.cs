//	============================================================
//	Name:		UMAExpressionSet
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

using UMA;

namespace PoseTools.UMA {

[System.Serializable]
public class UMAExpressionSet : ScriptableObject {

	// Mutually exclusive expressions can share a curve
	[System.Serializable]
	public class PosePair {
		public UMABonePose primary = null;
		public UMABonePose inverse = null;
	}
	public PosePair[] posePairs = new PosePair[UMAExpressionPlayer.PoseCount];

	[System.NonSerialized]
	private List<int> boneHashes = null;

	public void ResetBones(UMASkeleton umaSkeleton) {
		if (umaSkeleton == null) return;

		if (boneHashes == null) {
			boneHashes = new List<int>();

			foreach(PosePair pair in posePairs) {
				if (pair.primary != null) {
					foreach(UMABonePose.PoseBone bone in pair.primary.poses) {
						if (!boneHashes.Contains(bone.hash)) {
							boneHashes.Add(bone.hash);
						}
					}
				}
				if (pair.inverse != null) {
					foreach(UMABonePose.PoseBone bone in pair.inverse.poses) {
						if (!boneHashes.Contains(bone.hash)) {
							boneHashes.Add(bone.hash);
						}
					}
				}
			}
		}

		foreach (int hash in boneHashes) {
			if (!umaSkeleton.Reset(hash)) {
				Debug.LogWarning("Couldn't reset bone!");
			}
		}
	}
}

}