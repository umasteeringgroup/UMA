using UnityEngine;

namespace UMA
{
	[System.Serializable]
	public class BlendShapeDnaAsset : ScriptableObject
	{
		[System.Serializable]
		public class BlendShapePair
		{
			public string blendShapeName;
			public string dnaEntryName;
		}

		public BlendShapePair[] blendShapeDnaList;

		void OnEnable()
		{
			if (blendShapeDnaList == null)
			{
				blendShapeDnaList = new BlendShapePair[0];
			}
		}

		#if UNITY_EDITOR
		[UnityEditor.MenuItem("Assets/Create/UMA/DNA/BlendShape DNA")]
		public static void CreateBlendShapeAsset()
		{
			UMA.Editors.CustomAssetUtility.CreateAsset<BlendShapeDnaAsset>();
		}
		#endif
	}
}
