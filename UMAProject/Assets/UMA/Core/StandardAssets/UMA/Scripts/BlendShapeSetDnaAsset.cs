using UnityEngine;
using UMA.PoseTools;

namespace UMA
{
	[System.Serializable]
	public class BlendShapeSetDnaAsset : ScriptableObject
	{
		[System.Serializable]
		public class BlendShapePair
		{
			public string dnaEntryName;
			public string blendShapeZero;
			public string blendShapeOne;
		}

		public int dnaTypeHash;
		public int dnaVersion;

		public string startingBlendShape;

		public BlendShapePair[] shapePairs;

		void OnEnable()
		{
			if (shapePairs == null)
			{
				shapePairs = new BlendShapePair[0];
			}
		}

		#if UNITY_EDITOR
		[UnityEditor.MenuItem("Assets/Create/UMA/DNA/Blend Shape Set")]
		public static void CreateBelndShapeSetDnaAsset()
		{
			UMA.CustomAssetUtility.CreateAsset<BlendShapeSetDnaAsset>();
		}
		#endif
	}
}
