#if UNITY_EDITOR
using UnityEngine;
using System.Collections;
using UnityEditor;
using UMA;


namespace UMAEditor
{
	[CustomEditor(typeof(OverlayDataAsset))]
	public class OverlayDataAssetInspector : Editor
	{
		public override void OnInspectorGUI()
		{
			EditorGUI.BeginChangeCheck();
			base.OnInspectorGUI();
			if (EditorGUI.EndChangeCheck())
			{
				EditorUtility.SetDirty(target);
				AssetDatabase.SaveAssets();
			}
		}

	}
}
#endif
