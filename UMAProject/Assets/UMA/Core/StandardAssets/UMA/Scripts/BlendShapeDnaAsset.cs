using System.Collections;
using System.Collections.Generic;
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
		[UnityEditor.MenuItem("Assets/Create/UMA/DNA/UMA BlendShape DNA")]
		public static void CreateBlendShapeAsset()
		{
			UMAEditor.CustomAssetUtility.CreateAsset<BlendShapeDnaAsset>();
		}
		#endif
	}
}
