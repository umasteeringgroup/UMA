#if UNITY_EDITOR
using UnityEditor;

namespace UMA.Editors
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
