#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMACharacterSystem;

[CustomPropertyDrawer(typeof(DynamicCharacterAvatar.RaceSetter))]
public class RaceSetterPropertyDrawer : PropertyDrawer
{

	public DynamicCharacterAvatar thisDCA;
	public DynamicRaceLibrary thisDynamicRaceLibrary;
	//In the Editor when the app is NOT running this shows all the races you COULD choose- including those AssetBundles.
	//When the app IS running it shows the reaces you CAN choose- i.e. the ones that are either in the build or have been downloaded.
	public List<RaceData> foundRaces = new List<RaceData>();
	public List<string> foundRaceNames = new List<string>();
	bool raceListGenerated = false;

	public void SetRaceLists(RaceData[] raceDataArray = null)
	{
		if (raceDataArray == null)
		{
			raceDataArray = thisDynamicRaceLibrary.GetAllRaces();
		}
		foundRaces.Clear();
		foundRaceNames.Clear();
		foundRaces.Add(null);
		foundRaceNames.Add("None Set");
		foreach (RaceData race in raceDataArray)
		{
			if (race != null && race.raceName != "PlaceholderRace")
			{
				foundRaces.Add(race);
				foundRaceNames.Add(race.raceName);
			}
		}
		raceListGenerated = true;
	}
	private void CheckRaceDataLists()
	{
		if (Application.isPlaying)
		{
			//Start will have cleared any EditorAdded Assets and we only *need* the ones in the library
			var raceDatas = thisDynamicRaceLibrary.GetAllRacesBase();
			SetRaceLists(raceDatas);
		}
		else
		{
			//In this case we *need* all the races this setting *could* be so everything from the library, resources and asset bundles because the developer need to be able to set the race to be any of these
			//BUT we only need to do GetAllRaces ONCE for this data to be correct
			//thisDynamicRaceLibrary.ClearEditorAddedAssets();
			var raceDatas = thisDynamicRaceLibrary.GetAllRaces();
			if ((raceDatas.Length + 1) != (foundRaces.Count))
			{
				SetRaceLists(raceDatas);
			}
		}
	}

	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
	{
		if (!Application.isPlaying)
		{
			if (raceListGenerated == false)
				CheckRaceDataLists();
		}
		else
		{
			CheckRaceDataLists();
		}
		var RaceName = property.FindPropertyRelative("name");
		var RaceValue = property.FindPropertyRelative("_data");
		var keepDNAValue = property.FindPropertyRelative("keepDNA");
		var keepWardrobeValue = property.FindPropertyRelative("keepWardrobe");
		var keepBodyColorsValue = property.FindPropertyRelative("keepBodyColors");
		var cacheCurrentStateValue = property.FindPropertyRelative("cacheCurrentState");
		
		string rn = RaceName.stringValue;
		RaceData rv = (RaceData)RaceValue.objectReferenceValue;
		int rIndex = 0;
		int newrIndex;
		if (rn != "" || rv != null)
		{
			if (rn != "")
			{
				if (!foundRaceNames.Contains(rn))
				{
					foundRaceNames.Add(rn + " (Not Available)");
					foundRaces.Add(null);
				}
				rIndex = foundRaceNames.IndexOf(rn) == -1 ? (foundRaceNames.IndexOf(rn + " (Not Available)") == -1 ? 0 : foundRaceNames.IndexOf(rn + " (Not Available)")) : foundRaceNames.IndexOf(rn);
			}
			else
			{
				if (!foundRaces.Contains(rv))
				{
					foundRaceNames.Add(rv.raceName + " (Not Available)");
					foundRaces.Add(null);
				}
				rIndex = foundRaceNames.IndexOf(rn) == -1 ? (foundRaceNames.IndexOf(rn + " (Not Available)") == -1 ? 0 : foundRaceNames.IndexOf(rn + " (Not Available)")) : foundRaceNames.IndexOf(rn);
			}
		}
		EditorGUI.BeginProperty(position, label, property);
		Rect contentPosition = EditorGUI.PrefixLabel(position, new GUIContent("Active Race"));
		Rect contentPositionP = contentPosition;
		EditorGUI.BeginChangeCheck();
		newrIndex = EditorGUI.Popup(contentPositionP, rIndex, foundRaceNames.ToArray());
		if (EditorGUI.EndChangeCheck())
		{
			if (rIndex != newrIndex)
			{
				RaceValue.objectReferenceValue = foundRaces[newrIndex];
				RaceName.stringValue = foundRaceNames[newrIndex];
				thisDCA.ChangeRace(foundRaces[newrIndex]);
				RaceValue.serializedObject.ApplyModifiedProperties();
				RaceName.serializedObject.ApplyModifiedProperties();
			}
		}
		EditorGUI.indentLevel = EditorGUI.indentLevel + 1;
		keepDNAValue.isExpanded = EditorGUILayout.Foldout(keepDNAValue.isExpanded, new GUIContent("Change Race Options","When the race is changed the following rules will be applied by default. You can override these by calling the ChangeRace method and setting the params directly"));
		if (keepDNAValue.isExpanded)
		{
			
			EditorGUI.BeginChangeCheck();
			var line1 = EditorGUILayout.GetControlRect();
			var line1a = new Rect(line1.x, line1.y, line1.width / 2, line1.height);
			var line1b = new Rect(line1a.xMax, line1.y, line1.width / 2, line1.height);
			keepDNAValue.boolValue = EditorGUI.ToggleLeft(line1a, new GUIContent("Keep DNA", "If true changeRace will attempt to apply the dna from the previous race to the new race"), keepDNAValue.boolValue);
			cacheCurrentStateValue.boolValue = EditorGUI.ToggleLeft(line1b, new GUIContent("Cache Current State", "If true changeRace will cache the current state of this race. The next time it is selected the wardrobe and colors will return to the cached state"), cacheCurrentStateValue.boolValue);
			var line2 = EditorGUILayout.GetControlRect();
			var line2a = new Rect(line2.x, line2.y, line2.width / 2, line2.height);
			var line2b = new Rect(line2a.xMax, line2.y, line2.width / 2, line2.height);
			keepWardrobeValue.boolValue = EditorGUI.ToggleLeft(line2a, new GUIContent("Keep Wardrobe", "If true changeRace will attempt to assign the existing wardrobe to the new race if those wardrobe items are compatable. If no Wardrobe is assigned the default Wardrobe for this race (as defined in the component) will be assigned"), keepWardrobeValue.boolValue);
			keepBodyColorsValue.boolValue = EditorGUI.ToggleLeft(line2b, new GUIContent("Keep Body Colors", "If true changeRace will apply the colors that have been assigned to the baseRaceRecipe of the old race to the new race, otherwise the colors that were set up in the component will be applied to the new race"), keepBodyColorsValue.boolValue);
			if (EditorGUI.EndChangeCheck())
			{
				keepDNAValue.serializedObject.ApplyModifiedProperties();
				keepWardrobeValue.serializedObject.ApplyModifiedProperties();
				keepBodyColorsValue.serializedObject.ApplyModifiedProperties();
				cacheCurrentStateValue.serializedObject.ApplyModifiedProperties();
			}
			
		}
		EditorGUI.indentLevel = EditorGUI.indentLevel - 1;
		//EditorGUILayout.EndHorizontal();

		EditorGUI.EndProperty();
	}
}
#endif
