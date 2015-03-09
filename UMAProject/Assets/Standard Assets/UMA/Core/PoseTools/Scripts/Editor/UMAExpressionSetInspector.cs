//	============================================================
//	Name:		UMAExpressionSetInspector
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================
#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Collections;

namespace UMA.PoseTools
{
	[CustomEditor(typeof(UMAExpressionSet))]
	public class UMAExpressionSetInspector : Editor
	{
		private UMAExpressionSet expressionSet;
		public void OnEnable()
		{
			expressionSet = target as UMAExpressionSet;
		}

		public override void OnInspectorGUI()
		{
			GUILayout.BeginVertical();

			if (expressionSet.posePairs.Length != UMAExpressionPlayer.PoseCount)
			{
				Debug.LogWarning("Expression Set out of sync with Expression Poses!");
				System.Array.Resize<UMAExpressionSet.PosePair>(ref expressionSet.posePairs, UMAExpressionPlayer.PoseCount);
			}

			for (int i = 0; i < UMAExpressionPlayer.PoseCount; i++)
			{
				string primary = ExpressionPlayer.PrimaryPoseName(i);
				string inverse = ExpressionPlayer.InversePoseName(i);
				if (expressionSet.posePairs[i] == null)
				{
					expressionSet.posePairs[i] = new UMAExpressionSet.PosePair();
				}
				if (primary != null)
				{
					EditorGUILayout.LabelField(primary);
					expressionSet.posePairs[i].primary = EditorGUILayout.ObjectField(expressionSet.posePairs[i].primary, typeof(UMABonePose), false) as UMABonePose;
				}
				if (inverse != null)
				{
					EditorGUILayout.LabelField(inverse);
					expressionSet.posePairs[i].inverse = EditorGUILayout.ObjectField(expressionSet.posePairs[i].inverse, typeof(UMABonePose), false) as UMABonePose;
				}
				EditorGUILayout.Space();
			}

			GUILayout.EndVertical();

			if (GUI.changed)
			{
				EditorUtility.SetDirty(target);
				AssetDatabase.SaveAssets();
			}
		}

		static string GetAssetFolder()
		{
			string assetFolder = "Assets";
			Object[] selected = Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets);
			if (selected.Length > 0)
			{
				string assetPath = AssetDatabase.GetAssetPath(selected[0]);
				if (System.IO.Directory.Exists(assetPath))
				{
					assetFolder = assetPath;
				}
				else
				{
					assetFolder = System.IO.Path.GetDirectoryName(assetPath);
				}
			}

			return assetFolder;
		}

		[MenuItem("Assets/Create/UMA Expression Set")]
		static void CreateExpressionSetMenuItem()
		{
			UMAExpressionSet asset = ScriptableObject.CreateInstance<UMAExpressionSet>();

			string assetFolder = GetAssetFolder();
			AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(assetFolder + "/Expression Set.asset"));

			AssetDatabase.SaveAssets();
			Selection.activeObject = asset;
		}
	}
}
#endif