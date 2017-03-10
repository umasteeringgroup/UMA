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
				//somehow if the app is playing this already works- and doing it here makes it not work
				if (!EditorApplication.isPlaying)
					property.serializedObject.ApplyModifiedProperties();
			}
		}

		EditorGUI.EndProperty();
	}
}
#endif
