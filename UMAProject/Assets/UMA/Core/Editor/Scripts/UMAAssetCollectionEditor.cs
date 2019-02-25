#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	[CustomEditor(typeof(UMAAssetCollection), true)]
	public class UMAAssetCollectionEditor : Editor 
	{
		public override void OnInspectorGUI()
		{
			var context = UMAContext.FindInstance();
			EditorGUI.BeginDisabledGroup(context == null);
			if (GUILayout.Button("Add to Scene Context"))
			{
				var collection = target as UMAAssetCollection;
				collection.AddToContext(context);
			}
			EditorGUI.EndDisabledGroup();

			base.OnInspectorGUI();
		}

		[MenuItem("Assets/Create/UMA/Core/Asset Collection")]
		public static void CreateUMAAssetCollection()
		{
			UMA.CustomAssetUtility.CreateAsset<UMAAssetCollection>();
		}
	}
	#endif
}
