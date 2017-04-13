using UnityEngine;
using UMA.PoseTools;

namespace UMA
{
	[System.Serializable]
	public class MorphSetDnaAsset : ScriptableObject
	{
		[System.Serializable]
		public class DNASizeAdjustment
		{
			public float heightRatio = 1f;
			public float massRatio = 1f;
			public float radiusRatio = 1f;
		}

		[System.Serializable]
		public class DNAMorphSet
		{
			public string dnaEntryName;
			public UMABonePose poseZero;
			public UMABonePose poseOne;
			public string blendShapeZero;
			public string blendShapeOne;
			public DNASizeAdjustment sizeZero;
			public DNASizeAdjustment sizeOne;
		}

		public int dnaTypeHash;

		public UMABonePose startingPose;
		public string startingBlendShape;

		public DNAMorphSet[] dnaMorphs;

		void OnEnable()
		{
			if (dnaMorphs == null)
			{
				dnaMorphs = new DNAMorphSet[0];
			}
		}

		#if UNITY_EDITOR
		[UnityEditor.MenuItem("Assets/Create/UMA/DNA/Morph Set DNA")]
		public static void CreateMorphSetDnaAsset()
		{
			UMA.CustomAssetUtility.CreateAsset<MorphSetDnaAsset>();
		}
		#endif
	}
}
