using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA.CharacterSystem;
using UMA.PoseTools;

namespace UMA
{
    public class BonePoseUtilities : EditorWindow
	{
		public static string[] raceNames;

		string fileName = "PoseSlot";
		bool createWardrobeRecipe;
		bool addToLibrary;
		int selectedRace;

		[MenuItem("Assets/Create/UMA/DNA/Physique Bonepose set")]
		static void BonePoseUtilitiesWindow()
		{
			BonePoseUtilities window = ScriptableObject.CreateInstance(typeof(BonePoseUtilities)) as BonePoseUtilities;
			// Load races for lookup.
			List<string> RaceNames = new List<string>();
			string[] foundRacesStrings = AssetDatabase.FindAssets("t:RaceData");
			for (int i = 0; i < foundRacesStrings.Length; i++)
			{
				RaceData thisFoundRace = AssetDatabase.LoadAssetAtPath<RaceData>(AssetDatabase.GUIDToAssetPath(foundRacesStrings[i]));
				RaceNames.Add(thisFoundRace.raceName);
			}
			raceNames = RaceNames.ToArray();
			window.ShowUtility();
		}

		void OnGUI()
		{
			EditorGUILayout.HelpBox("This will create a Slot, DNAConverter, and Bonepose for adding DNA to a recipe. Edit the bonepose to specify the layouts. Assign the slot to a recipe to assign the DNA", MessageType.Info);
			GUILayout.Space(20);
			fileName = EditorGUILayout.TextField("Enter Filename: ", fileName);
			createWardrobeRecipe = EditorGUILayout.Toggle("Create Wardrobe Recipe", createWardrobeRecipe);
			EditorGUI.BeginDisabledGroup(!createWardrobeRecipe);
			selectedRace = EditorGUILayout.Popup("Select Race:", selectedRace, raceNames);
			EditorGUI.EndDisabledGroup();
			addToLibrary = EditorGUILayout.Toggle("Add to Global Library", addToLibrary);
			GUILayout.Space(20);
			EditorGUILayout.BeginHorizontal();
			GUILayout.Space(100);
			if (GUILayout.Button("Create Pose Set"))
			{
				CreatePoseSet(fileName);
				Close();
			}

			if (GUILayout.Button("Close"))
            {
                Close();
            }

            EditorGUILayout.EndHorizontal();
		}

		private void CreatePoseSet(string fileName)
		{
			SlotDataAsset sda = CustomAssetUtility.CreateAsset<SlotDataAsset>("", false, fileName + "_Slot", false);
			sda.slotName = sda.name;
			sda.nameHash = UMAUtils.StringToHash(sda.slotName);

			DynamicDNAConverterController ddcc = UMA.CustomAssetUtility.CreateAsset<DynamicDNAConverterController>("", false, fileName + "_Controller", false);
			sda.slotDNA = ddcc;


			DynamicUMADnaAsset duda = UMA.CustomAssetUtility.CreateAsset<DynamicUMADnaAsset>("", false, fileName + "_DNAAsset", false);
			ddcc.DNAAsset = duda;

			UMABonePose bp = UMA.CustomAssetUtility.CreateAsset<UMABonePose>("", true, fileName + "_Pose", false);
			BonePoseDNAConverterPlugin bpdcp = (BonePoseDNAConverterPlugin)ddcc.AddPlugin(typeof(BonePoseDNAConverterPlugin));

			bpdcp.poseDNAConverters.Add(new BonePoseDNAConverterPlugin.BonePoseDNAConverter());
			bpdcp.poseDNAConverters[0].poseToApply = bp;
			bpdcp.poseDNAConverters[0].startingPoseWeight = 1.0f;


			EditorUtility.SetDirty(sda);
			EditorUtility.SetDirty(ddcc);
			EditorUtility.SetDirty(duda);
			EditorUtility.SetDirty(bp);
			EditorUtility.SetDirty(bpdcp);
			if (addToLibrary)
			{
				UMAAssetIndexer.Instance.EvilAddAsset(typeof(SlotDataAsset), sda);
				UMAAssetIndexer.Instance.EvilAddAsset(typeof(DynamicUMADnaAsset), duda);
				EditorUtility.SetDirty(UMAAssetIndexer.Instance);
			}

			if (createWardrobeRecipe)
			{
				string path = CustomAssetUtility.GetAssetPathAndName<UMAWardrobeRecipe>(fileName, false);
				UMAWardrobeRecipe uwr = UMAEditorUtilities.CreateRecipe(path, sda, null, fileName, addToLibrary);
				uwr.compatibleRaces = new List<string>();
				uwr.wardrobeSlot = "Physique";
				uwr.compatibleRaces.Add(raceNames[selectedRace]);
				EditorUtility.SetDirty(uwr);
			}

			AssetDatabase.SaveAssets();
		}

		void OnInspectorUpdate()
		{
			Repaint();
		}
	}
}