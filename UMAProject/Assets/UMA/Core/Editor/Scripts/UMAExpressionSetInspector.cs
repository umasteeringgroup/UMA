//	============================================================
//	Name:		UMAExpressionSetInspector
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================
#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using System;

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
			if (expressionSet == null)
			{
				EditorGUILayout.HelpBox("Expression set is null.", MessageType.Error);
				return;
			}

			GUILayout.BeginVertical();

			// Duplicate button
			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Replace Expressions", GUILayout.Width(160)))
			{
				ReplaceExpressions();
			}
			if (GUILayout.Button("Duplicate Set", GUILayout.Width(160)))
			{
				DuplicateSetAndPoses();
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.Space();

			// Initialize array if missing
			if (expressionSet.posePairs == null)
			{
				expressionSet.posePairs = new UMAExpressionSet.PosePair[UMAExpressionPlayer.PoseCount];
			}

			if (expressionSet.posePairs.Length != UMAExpressionPlayer.PoseCount)
			{
				Debug.LogWarning("Expression Set out of sync with Expression Poses!");
				System.Array.Resize<UMAExpressionSet.PosePair>(ref expressionSet.posePairs, UMAExpressionPlayer.PoseCount);
			}

            expressionSet.UnmappedJawName =EditorGUILayout.TextField("Unmapped Jaw Name", expressionSet.UnmappedJawName);

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
				Undo.RecordObject(expressionSet, "Modify Expression Set");
				EditorUtility.SetDirty(expressionSet);
				AssetDatabase.SaveAssets();
			}
		}

		private void ReplaceExpressions()
		{
			string defaultFolderAbs = Application.dataPath;
			string chosenAbs = EditorUtility.OpenFolderPanel("Select source folder (inside Assets)", defaultFolderAbs, "");
			if (string.IsNullOrEmpty(chosenAbs)) { return; }
			string assetsAbs = Application.dataPath.Replace("\\", "/");
			string chosenNorm = chosenAbs.Replace("\\", "/");
			if (!chosenNorm.StartsWith(assetsAbs, StringComparison.OrdinalIgnoreCase))
			{
				EditorUtility.DisplayDialog("Invalid Folder", "Please choose a folder inside the project's Assets directory.", "OK");
				return;
			}
			string srcFolder = "Assets" + chosenNorm.Substring(assetsAbs.Length);

			string[] guids = AssetDatabase.FindAssets("t:UMABonePose", new string[] { srcFolder });
			if (guids.Length == 0)
			{
				EditorUtility.DisplayDialog("No Poses Found", "No UMABonePose assets were found in the selected folder.", "OK");
				return;
			}
			var poseByName = new Dictionary<string, UMABonePose>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				UMABonePose bp = AssetDatabase.LoadAssetAtPath<UMABonePose>(path);
				if (bp != null && !poseByName.ContainsKey(bp.name))
				{
					poseByName.Add(bp.name, bp);
				}
			}

			if (expressionSet.posePairs == null || expressionSet.posePairs.Length != UMAExpressionPlayer.PoseCount)
			{
				expressionSet.posePairs = new UMAExpressionSet.PosePair[UMAExpressionPlayer.PoseCount];
			}

			Undo.RecordObject(expressionSet, "Replace Expressions");

			int replacedPrimary = 0;
			int replacedInverse = 0;
			int missingPrimary = 0;
			int missingInverse = 0;

			for (int i = 0; i < UMAExpressionPlayer.PoseCount; i++)
			{
				var pair = expressionSet.posePairs[i];
				if (pair == null)
				{
					pair = new UMAExpressionSet.PosePair();
					expressionSet.posePairs[i] = pair;
					continue;
				}

                if (pair.primary != null)
				{
					string primaryName = pair.primary.name;
					UMABonePose newPrimary;

					if (!poseByName.ContainsKey(primaryName))
					{
                        // does it contain a _L or _R suffix?
                        // if so, get the base, and duplicate with the suffixes

                    }

                    if (poseByName.TryGetValue(primaryName, out newPrimary))
					{
						if (pair.primary != newPrimary)
						{
							pair.primary = newPrimary;
							replacedPrimary++;
						}
					}
					else
					{
						missingPrimary++;
					}
				}

				if (pair.inverse != null)
				{
					string inverseName = pair.inverse.name;
					UMABonePose newInverse;
					if (poseByName.TryGetValue(inverseName, out newInverse))
					{
						if (pair.inverse != newInverse)
						{
							pair.inverse = newInverse;
							replacedInverse++;
						}
					}
					else
					{
						missingInverse++;
					}
				}
			}

			EditorUtility.SetDirty(expressionSet);
			AssetDatabase.SaveAssets();

			EditorUtility.DisplayDialog(
				"Expressions Replaced",
				$"Primary Replaced: {replacedPrimary}\nInverse Replaced: {replacedInverse}\nPrimary Missing: {missingPrimary}\nInverse Missing: {missingInverse}",
				"OK");
		}

		private UMABonePose ReplacePose(string sourceFolder, UMABonePose oldPose, ref int replacedCount, ref int missingCount)
		{
			if (oldPose == null)
			{
				return null;
			}
			string poseName = oldPose.name;
			// Limit search strictly to the chosen source folder (and its subfolders)
			string[] guids = AssetDatabase.FindAssets("t:UMABonePose " + poseName, new string[] { sourceFolder });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (!path.StartsWith(sourceFolder, StringComparison.OrdinalIgnoreCase))
				{
					continue; // safety: ignore assets outside folder
				}
				UMABonePose candidate = AssetDatabase.LoadAssetAtPath<UMABonePose>(path);
				if (candidate != null)
				{
					if (string.Equals(candidate.name, poseName, StringComparison.OrdinalIgnoreCase))
					{
						replacedCount++;
                        return candidate; // first match in folder
					}
				}
			}
			missingCount++;
            return oldPose; // No replacement found in folder
        }

        private void DuplicateSetAndPoses()
		{
			// Choose destination folder (must be under Assets)
			string defaultFolderAbs = Application.dataPath;
			string chosenAbs = EditorUtility.OpenFolderPanel("Select destination folder (inside Assets)", defaultFolderAbs, "");
			if (string.IsNullOrEmpty(chosenAbs)) return;

			// Convert to project-relative path
			string assetsAbs = Application.dataPath.Replace("\\", "/");
			string chosenNorm = chosenAbs.Replace("\\", "/");
			if (!chosenNorm.StartsWith(assetsAbs))
			{
				EditorUtility.DisplayDialog("Invalid Folder", "Please choose a folder inside the project's Assets directory.", "OK");
				return;
			}
			string destFolder = "Assets" + chosenNorm.Substring(assetsAbs.Length);
			if (!AssetDatabase.IsValidFolder(destFolder))
			{
				// Create nested folders if needed
				CreateFoldersRecursively(destFolder);
			}

			// Duplicate expression set
			UMAExpressionSet srcSet = expressionSet;
			UMAExpressionSet newSet = ScriptableObject.CreateInstance<UMAExpressionSet>();
			newSet.posePairs = new UMAExpressionSet.PosePair[UMAExpressionPlayer.PoseCount];

			// Duplicate referenced bone poses (cache to avoid duplicating twice)
			var dupCache = new Dictionary<UMABonePose, UMABonePose>();
			for (int i = 0; i < newSet.posePairs.Length; i++)
			{
				var srcPair = (i < srcSet.posePairs.Length) ? srcSet.posePairs[i] : null;
				if (srcPair != null)
				{
					var dstPair = new UMAExpressionSet.PosePair();
					dstPair.primary = DuplicateBonePose(srcPair.primary, destFolder, dupCache);
					dstPair.inverse = DuplicateBonePose(srcPair.inverse, destFolder, dupCache);
					newSet.posePairs[i] = dstPair;
				}
				else
				{
					newSet.posePairs[i] = new UMAExpressionSet.PosePair();
				}
			}

			string newSetPath = AssetDatabase.GenerateUniqueAssetPath(destFolder + "/" + srcSet.name + ".asset");
			AssetDatabase.CreateAsset(newSet, newSetPath);
			EditorUtility.SetDirty(newSet);
			AssetDatabase.SaveAssets();

			Selection.activeObject = newSet;
			EditorGUIUtility.PingObject(newSet);
		}

		private static UMABonePose DuplicateBonePose(UMABonePose src, string destFolder, Dictionary<UMABonePose, UMABonePose> cache)
		{
			if (src == null) return null;
			if (cache.TryGetValue(src, out var existing)) return existing;

			UMABonePose dup = ScriptableObject.CreateInstance<UMABonePose>();
			// Copy simple fields
			if (src.poses != null)
			{
				dup.poses = ClonePoseArray(src.poses);
			}
			dup.tweenWeights = (src.tweenWeights != null) ? (float[])src.tweenWeights.Clone() : null;
			dup.tweenPoses = src.tweenPoses; // keep references; deep-copying tweens is optional

			string posePath = AssetDatabase.GenerateUniqueAssetPath(destFolder + "/" + src.name + ".asset");
			AssetDatabase.CreateAsset(dup, posePath);
			EditorUtility.SetDirty(dup);
			cache[src] = dup;
			return dup;
		}

		private static UMABonePose.PoseBone[] ClonePoseArray(UMABonePose.PoseBone[] src)
		{
			var arr = new UMABonePose.PoseBone[src.Length];
			for (int i = 0; i < src.Length; i++)
			{
				var s = src[i];
				arr[i] = new UMABonePose.PoseBone
				{
					bone = s.bone,
					hash = s.hash,
					position = s.position,
					rotation = s.rotation,
					scale = s.scale,
					category = s.category,
					enabled = s.enabled
				};
			}
			return arr;
		}

		private static void CreateFoldersRecursively(string assetPath)
		{
			// assetPath like "Assets/Sub/Folder"
			if (AssetDatabase.IsValidFolder(assetPath)) return;
			string[] parts = assetPath.Split('/');
			string cur = parts[0]; // "Assets"
			for (int i = 1; i < parts.Length; i++)
			{
				string next = cur + "/" + parts[i];
				if (!AssetDatabase.IsValidFolder(next))
				{
					AssetDatabase.CreateFolder(cur, parts[i]);
				}
				cur = next;
			}
		}

		static string GetAssetFolder()
		{
			string assetFolder = "Assets";
			UnityEngine.Object[] selected = Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets);
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

		[MenuItem("Assets/Create/UMA/Misc/Expression Set")]
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