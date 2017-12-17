using UnityEngine;
using UnityEditor;
using System;
using UnityEditorInternal;

namespace UMA
{
	[CustomEditor(typeof(UMAAtlasMaterial))]
	public class UMAAtlasMaterialInspector : UMAMappedPropertyAssetInspector
	{
		UMAAtlasMaterial atlas { get { return target as UMAAtlasMaterial; } }

		public override void OnInspectorGUI()
		{
			EditorGUI.BeginChangeCheck();
			
			atlas.materialTemplate = EditorGUILayout.ObjectField("Material Template", atlas.materialTemplate , typeof(Material), false) as Material;

			base.OnInspectorGUI();

			if (EditorGUI.EndChangeCheck())
			{
				RefreshCachedData();
				EditorUtility.SetDirty(atlas);
				AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(atlas), ImportAssetOptions.ForceSynchronousImport);
			}
		}
	}
}