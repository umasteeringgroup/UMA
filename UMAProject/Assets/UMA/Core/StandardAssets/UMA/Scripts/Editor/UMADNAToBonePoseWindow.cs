//	============================================================
//	Name:		UMADNAToBonePoseWindow
//	Author: 	Eli Curtz
//	Copyright:	(c) 2016 Eli Curtz
//	============================================================

#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UMA;

namespace UMA.PoseTools
{
	public class UMADNAToBonePoseWindow : EditorWindow
	{
		public UMAData sourceUMA;
		public UnityEngine.Object outputFolder;

		private string folderPath = "";
		private string assetPath = "";
		private UnityEngine.Object assetObject;
		private GameObject tempAvatarPreDNA;
		private GameObject tempAvatarPostDNA;
		private bool avatarDNAisDirty = false;

		private int selectedDNAIndex = -1;
		private int selectedDNAHash = 0;

		private int poseSaveIndex = -1;
		const string startingPoseName = "StartingPose";
		private string poseSaveName = startingPoseName;

		private static GUIContent sourceGUIContent = new GUIContent(
			"Source UMA",
			"UMA character in an active scene to collect DNA poses from.");
		private static GUIContent converterGUIContent = new GUIContent(
			"DNA Converter",
			"DNA Converter Behavior being converted to poses.");
		private static GUIContent folderGUIContent = new GUIContent(
			"Output Folder",
			"Parent folder where the new set of bone poses will be saved.");
		private static GUIContent saveButtonGUIContent = new GUIContent(
			"Save Pose Set",
			"Generate the new poses (may take several seconds).");

		private Dictionary<string, UMABonePose> poseDict = new Dictionary<string, UMABonePose>();
		
		void OnGUI()
		{
			sourceUMA = EditorGUILayout.ObjectField(sourceGUIContent, sourceUMA, typeof(UMAData), true) as UMAData;

			EditorGUI.indentLevel++;
			selectedDNAHash = 0;
			if (sourceUMA == null)
			{
				EditorGUI.BeginDisabledGroup(true);
				GUIContent[] dnaNames = new GUIContent[1];
				dnaNames[0] = new GUIContent("");
				EditorGUILayout.Popup(converterGUIContent, selectedDNAIndex, dnaNames);
				EditorGUI.EndDisabledGroup();
			}
			else
			{
				DnaConverterBehaviour[] dnaConverters = sourceUMA.umaRecipe.raceData.dnaConverterList;
				GUIContent[] dnaNames = new GUIContent[dnaConverters.Length];
				for (int i = 0; i < dnaConverters.Length; i++)
				{
					dnaNames[i] = new GUIContent(dnaConverters[i].name);
				}

				selectedDNAIndex = EditorGUILayout.Popup(converterGUIContent, selectedDNAIndex, dnaNames);
				if ((selectedDNAIndex >= 0) && (selectedDNAIndex < dnaConverters.Length))
				{
					selectedDNAHash = dnaConverters[selectedDNAIndex].DNATypeHash;
				}
			}
			EditorGUI.indentLevel--;

			EditorGUILayout.Space();

			outputFolder = EditorGUILayout.ObjectField(folderGUIContent, outputFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
			EnforceFolder(ref outputFolder);

			EditorGUI.BeginDisabledGroup((sourceUMA == null) || (selectedDNAHash == 0) || (outputFolder == null));
			if (GUILayout.Button(saveButtonGUIContent))
			{
				SavePoseSet();
			}
			EditorGUI.EndDisabledGroup();
		}

		void Update()
		{
			if (avatarDNAisDirty)
			{
				avatarDNAisDirty = false;
				UMAData umaPostDNA = tempAvatarPostDNA.GetComponent<UMADynamicAvatar>().umaData;
				if (umaPostDNA != null)
				{
					umaPostDNA.Dirty(true, false, false);
				}
			}
		}

		// This code is generally the same as used in the DynamicDNAConverterCustomizer
		// Probably worth breaking it out at some point and having it geenric
		protected void CreateBonePoseCallback(UMAData umaData)
		{
			avatarDNAisDirty = false;
			UMABonePose bonePose = ScriptableObject.CreateInstance<UMABonePose>();

			UMAData umaPreDNA = tempAvatarPreDNA.GetComponent<UMADynamicAvatar>().umaData;
			UMAData umaPostDNA = tempAvatarPostDNA.GetComponent<UMADynamicAvatar>().umaData;
			UMADnaBase activeDNA = umaPostDNA.umaRecipe.GetDna(selectedDNAHash);
			UMASkeleton skeletonPreDNA = umaPreDNA.skeleton;
			UMASkeleton skeletonPostDNA = umaPostDNA.skeleton;

			if (poseSaveIndex < 0)
			{
				poseSaveName = startingPoseName;

				// Now that StartingPose has been generated
				// add the active DNA to the pre DNA avatar
				DnaConverterBehaviour activeConverter = sourceUMA.umaRecipe.raceData.GetConverter(sourceUMA.umaRecipe.GetDna(selectedDNAHash));
				umaPreDNA.umaRecipe.raceData.dnaConverterList = new DnaConverterBehaviour[1];
				umaPreDNA.umaRecipe.raceData.dnaConverterList[0] = activeConverter;
				umaPreDNA.umaRecipe.EnsureAllDNAPresent();
				umaPreDNA.Dirty(true, false, true);
			}

			Transform transformPreDNA;
			Transform transformPostDNA;
			foreach (int boneHash in skeletonPreDNA.BoneHashes)
			{
				skeletonPreDNA.TryGetBoneTransform(boneHash, out transformPreDNA);
				skeletonPostDNA.TryGetBoneTransform(boneHash, out transformPostDNA);

				if ((transformPreDNA == null) || (transformPostDNA == null))
				{
					Debug.LogWarning("Bad bone hash in skeleton: " + boneHash);
					continue;
				}

				if (!LocalTransformsMatch(transformPreDNA, transformPostDNA))
				{
					bonePose.AddBone(transformPreDNA, transformPostDNA.localPosition, transformPostDNA.localRotation, transformPostDNA.localScale);
				}
			}

			int activeDNACount = activeDNA.Count;
			for (int i = 0; i < activeDNACount; i++)
			{
				activeDNA.SetValue(i, 0.5f);
			}

			bonePose.name = poseSaveName;
			bonePose.hideFlags = HideFlags.HideInHierarchy;
			AssetDatabase.AddObjectToAsset(bonePose, assetObject);
			poseDict.Add(poseSaveName, bonePose);
			EditorUtility.SetDirty(bonePose);
			AssetDatabase.SaveAssets();

			poseSaveIndex++;
			if (poseSaveIndex < activeDNACount)
			{
				poseSaveName = activeDNA.Names[poseSaveIndex] + "_0";
				activeDNA.SetValue(poseSaveIndex, 0.0f);
				umaPostDNA.Dirty();
				avatarDNAisDirty = true;
			}
			else if (poseSaveIndex < (activeDNACount * 2))
			{
				int dnaIndex = poseSaveIndex - activeDNACount;
				poseSaveName = activeDNA.Names[dnaIndex] + "_1";
				activeDNA.SetValue(dnaIndex, 1.0f);
				umaPostDNA.Dirty();
				avatarDNAisDirty = true;
			}
			else
			{
				UMAUtils.DestroySceneObject(tempAvatarPreDNA);
				UMAUtils.DestroySceneObject(tempAvatarPostDNA);

				// Populate the asset
				SerializedObject serializedAsset = new SerializedObject(assetObject);

				SerializedProperty startingPose = serializedAsset.FindProperty("startingPose");
				startingPose.objectReferenceValue = poseDict[startingPoseName];

				SerializedProperty posePairs = serializedAsset.FindProperty("posePairs");
				posePairs.ClearArray();
				for (int i = 0; i < activeDNACount; i++)
				{
					string posePairName = activeDNA.Names[i];

					posePairs.InsertArrayElementAtIndex(i);
					SerializedProperty posePair = posePairs.GetArrayElementAtIndex(i);

					SerializedProperty zeroPose = posePair.FindPropertyRelative("poseZero");
					zeroPose.objectReferenceValue = poseDict[posePairName + "_0"];
					SerializedProperty onePose = posePair.FindPropertyRelative("poseOne");
					onePose.objectReferenceValue = poseDict[posePairName + "_1"];
				}
				serializedAsset.ApplyModifiedPropertiesWithoutUndo();
					
				// Build a prefab DNA Converter and populate it with the bone pose set
				string prefabName = "Converter Prefab";
				string prefabPath = AssetDatabase.GenerateUniqueAssetPath(folderPath + "/" + prefabName + ".prefab");

				GameObject tempConverterPrefab = new GameObject(prefabName);
				BonePoseSetDnaConverterBehaviour converter = tempConverterPrefab.AddComponent<BonePoseSetDnaConverterBehaviour>();
				SerializedObject serializedConverter = new SerializedObject(converter);

				SerializedProperty poseSet = serializedAsset.FindProperty("bonePoseSet");
				poseSet.objectReferenceValue = AssetDatabase.LoadAssetAtPath<BonePoseSetDnaAsset>(assetPath);

				serializedConverter.ApplyModifiedPropertiesWithoutUndo();
				PrefabUtility.CreatePrefab(prefabPath, tempConverterPrefab);
				DestroyImmediate(tempConverterPrefab, false);
			}
		}

		protected void SavePoseSet()
		{
			DnaConverterBehaviour activeConverter = sourceUMA.umaRecipe.raceData.GetConverter(sourceUMA.umaRecipe.GetDna(selectedDNAHash));
			folderPath = AssetDatabase.GetAssetPath(outputFolder) + "/" + activeConverter.name;
			if (!AssetDatabase.IsValidFolder(folderPath))
			{
				string folderGUID = AssetDatabase.CreateFolder(AssetDatabase.GetAssetPath(outputFolder), activeConverter.name);
				folderPath = AssetDatabase.GUIDToAssetPath(folderGUID);
			}

			// Create the asset
			string assetName = "Pose Set";
			assetPath = AssetDatabase.GenerateUniqueAssetPath(folderPath + "/" + assetName + ".asset");

			assetObject = CustomAssetUtility.CreateAsset<BonePoseSetDnaAsset>(assetPath, false);

			poseDict.Clear();
			poseSaveIndex = -1;

			// Build a temporary version of the Avatar with no DNA to get original state
			SlotData[] activeSlots = sourceUMA.umaRecipe.GetAllSlots();
			int slotIndex;

			tempAvatarPreDNA = new GameObject("Temp Raw Avatar");
			tempAvatarPreDNA.transform.parent = sourceUMA.transform.parent;
			tempAvatarPreDNA.transform.localPosition = Vector3.zero;
			tempAvatarPreDNA.transform.localRotation = sourceUMA.transform.localRotation;

			UMADynamicAvatar tempAvatar = tempAvatarPreDNA.AddComponent<UMADynamicAvatar>();
			tempAvatar.umaGenerator = sourceUMA.umaGenerator;
			tempAvatar.Initialize();
			tempAvatar.umaData.umaRecipe = new UMAData.UMARecipe();
			tempAvatar.umaData.umaRecipe.raceData = ScriptableObject.CreateInstance<RaceData>();
			tempAvatar.umaData.umaRecipe.raceData.raceName = "Temp Raw Race";
			tempAvatar.umaData.umaRecipe.raceData.TPose = sourceUMA.umaRecipe.raceData.TPose;
			tempAvatar.umaData.umaRecipe.raceData.umaTarget = sourceUMA.umaRecipe.raceData.umaTarget;
			slotIndex = 0;
			foreach (SlotData slotEntry in activeSlots) {
				if ((slotEntry == null) || slotEntry.dontSerialize) continue;
				tempAvatar.umaData.umaRecipe.SetSlot(slotIndex++, slotEntry);
			}
			tempAvatar.Show();

			tempAvatarPostDNA = new GameObject("Temp DNA Avatar");
			tempAvatarPostDNA.transform.parent = sourceUMA.transform.parent;
			tempAvatarPostDNA.transform.localPosition = Vector3.zero;
			tempAvatarPostDNA.transform.localRotation = sourceUMA.transform.localRotation;

			UMADynamicAvatar tempAvatar2 = tempAvatarPostDNA.AddComponent<UMADynamicAvatar>();
			tempAvatar2.umaGenerator = sourceUMA.umaGenerator;
			tempAvatar2.Initialize();
			tempAvatar2.umaData.umaRecipe = new UMAData.UMARecipe();
			tempAvatar2.umaData.umaRecipe.raceData = ScriptableObject.CreateInstance<RaceData>();
			tempAvatar2.umaData.umaRecipe.raceData.raceName = "Temp DNA Race";
			tempAvatar2.umaData.umaRecipe.raceData.TPose = sourceUMA.umaRecipe.raceData.TPose;
			tempAvatar2.umaData.umaRecipe.raceData.umaTarget = sourceUMA.umaRecipe.raceData.umaTarget;
			tempAvatar2.umaData.umaRecipe.raceData.dnaConverterList = new DnaConverterBehaviour[1];
			tempAvatar2.umaData.umaRecipe.raceData.dnaConverterList[0] = activeConverter;

			slotIndex = 0;
			foreach (SlotData slotEntry in activeSlots) {
				if ((slotEntry == null) || slotEntry.dontSerialize) continue;
				tempAvatar2.umaData.umaRecipe.SetSlot(slotIndex++, slotEntry);
			}

			tempAvatar2.umaData.OnCharacterUpdated += CreateBonePoseCallback;
			tempAvatar2.Show();
		}

		public static void EnforceFolder(ref UnityEngine.Object folderObject)
		{
			if (folderObject != null)
			{
				string destpath = AssetDatabase.GetAssetPath(folderObject);

				if (string.IsNullOrEmpty(destpath))
				{
					folderObject = null;
				}
				else if (!System.IO.Directory.Exists(destpath))
				{
					destpath = destpath.Substring(0, destpath.LastIndexOf('/'));
					folderObject = AssetDatabase.LoadMainAssetAtPath(destpath);
				}
			}
		}

		private static float squaredPosAccuracy = Mathf.Pow(0.002f, 2f); // 2mm movement or more
		private static float squaredScaleAccuracy = Mathf.Pow(0.01f, 2f); // 1% change in scale
		private static bool LocalTransformsMatch(Transform t1, Transform t2)
		{
			if ((t1.localPosition - t2.localPosition).sqrMagnitude > squaredPosAccuracy) return false;
			if ((t1.localScale - t2.localScale).sqrMagnitude > squaredScaleAccuracy) return false;
			if (t1.localRotation != t2.localRotation) return false;

			return true;
		}

		[MenuItem("UMA/Pose Tools/Bone Pose DNA Extractor")]
		public static void OpenUMADNAToBonePoseWindow()
		{
			EditorWindow win = EditorWindow.GetWindow(typeof(UMADNAToBonePoseWindow));

			win.titleContent.text = "Pose Extractor";
        }
	}
}
#endif