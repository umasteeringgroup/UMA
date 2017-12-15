using UnityEngine;
using UnityEditor;
using System;
using UnityEditorInternal;

namespace UMA
{
	[CustomEditor(typeof(UMATextureChannelCombiner))]
	public class UMATextureChannelCombinerInspector : UMAMappedPropertyAssetInspector
	{
		UMATextureChannelCombiner combiner { get { return target as UMATextureChannelCombiner; } }

		public override void OnInspectorGUI()
		{
			EditorGUI.BeginChangeCheck();
			
			combiner.combineShader = EditorGUILayout.ObjectField("Combine Shader", combiner.combineShader, typeof(Shader), false) as Shader;

			base.OnInspectorGUI();

			if (EditorGUI.EndChangeCheck())
			{
				RefreshCachedData();
				EditorUtility.SetDirty(combiner);
				AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(combiner), ImportAssetOptions.ForceSynchronousImport);
			}
		}
	}
}