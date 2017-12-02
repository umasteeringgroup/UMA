using UnityEngine;
using UMA.PoseTools;

namespace UMA
{
	[System.Serializable]
	public class BonePoseSetDnaAsset : ScriptableObject
	{
// HACK move this into its own DNA
//		[System.Serializable]
//		public class DNASizeAdjustment
//		{
//			public float heightRatio = 1f;
//			public float massRatio = 1f;
//			public float radiusRatio = 1f;
//		}

		[System.Serializable]
		public class PosePair
		{
			public UMABonePose poseZero;
			public UMABonePose poseOne;
//			public DNASizeAdjustment sizeZero;
//			public DNASizeAdjustment sizeOne;
		}

		public int dnaTypeHash;
		public int dnaVersion;

		public UMABonePose startingPose;

		public PosePair[] posePairs;

		void OnEnable()
		{
			if (posePairs == null)
			{
				posePairs = new PosePair[0];
			}
		}

		#if UNITY_EDITOR
		[UnityEditor.MenuItem("Assets/Create/UMA/DNA/Bone Pose Set")]
		public static void CreateBonePoseSetDnaAsset()
		{
			UMA.CustomAssetUtility.CreateAsset<BonePoseSetDnaAsset>();
		}
		#endif
	}
}
