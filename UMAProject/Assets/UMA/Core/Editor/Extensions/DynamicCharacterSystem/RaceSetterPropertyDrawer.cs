#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UMA.Editors;


namespace UMA.CharacterSystem.Editors
{
    [CustomPropertyDrawer(typeof(DynamicCharacterAvatar.RaceSetter))]
	public class RaceSetterPropertyDrawer : PropertyDrawer
	{

		public DynamicCharacterAvatar thisDCA;
		//public DynamicRaceLibrary thisDynamicRaceLibrary;
		//In the Editor when the app is NOT running this shows all the races you COULD choose- including those AssetBundles.
		//When the app IS running it shows the reaces you CAN choose- i.e. the ones that are either in the build or have been downloaded.
		public List<RaceData> foundRaces = new List<RaceData>();
		public List<string> foundRaceNames = new List<string>();

		override public float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
            return 0;
        }

		public void SetRaceLists(RaceData[] raceDataArray = null)
		{
			foundRaces.Clear();
			foundRaceNames.Clear();
			foundRaces.Add(null);
			foundRaceNames.Add("None Set");
			if (raceDataArray == null)
            {
				return;
            }
            for (int i = 0; i < raceDataArray.Length; i++)
			{
                RaceData race = raceDataArray[i];
                if (race != null && race.raceName != "RaceDataPlaceholder")
				{
					foundRaces.Add(race);
					foundRaceNames.Add(race.raceName);
				}
			}
		}

		float lastTime = 0.0f;
        private void CheckRaceDataLists()
		{
			float currentTime = Time.realtimeSinceStartup;
			if (currentTime - lastTime < 5.0f)
			{
				lastTime = currentTime;
                return;
            }

			if (UMAContext.Instance == null)
            {
				var raceDatas = UMAAssetIndexer.Instance.GetAllAssets<RaceData>();
				SetRaceLists(raceDatas.ToArray());
				return;
            }
			if (Application.isPlaying)
			{
				//Start will have cleared any EditorAdded Assets and we only *need* the ones in the library
				var raceDatas = UMAContext.Instance.GetAllRacesBase();
				SetRaceLists(raceDatas);
			}
			else
			{
					var raceDatas = UMAContext.Instance.GetAllRaces();
					if ((raceDatas.Length + 1) != (foundRaces.Count))
					{
						SetRaceLists(raceDatas);
					}
			}
		}

		public List<Object> InspectMe = new List<Object>();


		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			DoGUI(position, property,label);
		}


        public List<Object> DoGUI(Rect position, SerializedProperty property, GUIContent label)
		{
            CheckRaceDataLists();

            var RaceName = property.FindPropertyRelative("name");
			
			string rn = RaceName.stringValue; 
			int rIndex = 0;
			int newrIndex;
			int converterCount = 0;
			if (rn != "")
			{
				if (!foundRaceNames.Contains(rn))
				{
					foundRaceNames.Add(rn + " (Not Available)");
					foundRaces.Add(null);
				}
				rIndex = foundRaceNames.IndexOf(rn) == -1 ? (foundRaceNames.IndexOf(rn + " (Not Available)") == -1 ? 0 : foundRaceNames.IndexOf(rn + " (Not Available)")) : foundRaceNames.IndexOf(rn);
			}

           // EditorGUI.BeginProperty(position, label, property);
            GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.875f, 1f));

           // Rect contentPosition = EditorGUI.PrefixLabel(position, new GUIContent("Active Race"));
			//Rect contentPositionP = contentPosition;
			EditorGUI.BeginChangeCheck();
			newrIndex = EditorGUILayout.Popup(new GUIContent("Active Race"),rIndex, foundRaceNames.ToArray());
			if (EditorGUI.EndChangeCheck())
			{
				if (rIndex != newrIndex)
				{
					RaceName.stringValue = foundRaceNames[newrIndex];
					//somehow if the app is playing this already works- and doing it here makes it not work
					if (!EditorApplication.isPlaying)
                    {
                        property.serializedObject.ApplyModifiedProperties();
                    }
                }
			}

            RaceData theRace = foundRaces[newrIndex];
			if (theRace != null)
			{
				if (theRace.dnaConverterList != null)
				{
                    converterCount = theRace.dnaConverterList.Length;
                }
			}

			EditorGUILayout.LabelField("Inspector Tools", EditorStyles.boldLabel);
			EditorGUILayout.BeginHorizontal();

			if (GUILayout.Button("Race"))
			{
				if (theRace != null)
				{
					//InspectorUtlity.InspectTarget(theRace);
					InspectMe.Add(theRace);
                }
			}
			if (GUILayout.Button("Base Recipe"))
			{
				if (theRace != null)
				{
					if (theRace.baseRaceRecipe != null)
                    {
						InspectMe.Add(theRace.baseRaceRecipe);
                        //InspectorUtlity.InspectTarget(theRace.baseRaceRecipe);
                    }
                }
			}
			if (GUILayout.Button($"DNA Cvts ({converterCount})"))
			{
                if (theRace != null)
				{
                    if (theRace.dnaConverterList != null)
					{
						foreach(var dna in theRace.dnaConverterList)
						{
                            InspectMe.Add(dna);
                            // InspectorUtlity.InspectTarget(dna);
                        }
                    }
                }
            }
			if (GUILayout.Button("BonePose"))
			{
				//UMABonePose firstPose = null;

                if (theRace != null)
                {
                    if (theRace.dnaConverterList != null)
                    {
						foreach (var dna in theRace.dnaConverterList)
						{ 
							if (dna.PluginCount > 0)
							{
								foreach( var plugin in dna.GetPlugins())
								{
                                    if (plugin is BonePoseDNAConverterPlugin)
									{
										var p = plugin as BonePoseDNAConverterPlugin;
										foreach (var bp in p.poseDNAConverters)
										{
											if (bp.poseToApply != null)
											{
												InspectMe.Add(bp.poseToApply);
                                            }
										}
                                        break;
                                    }
                                }
							}
						}
                    }
                }

            }
            EditorGUILayout.EndHorizontal();
            GUIHelper.EndVerticalPadded(5);

			 
			return InspectMe;
/*			if (Event.current.type == EventType.Used && InspectMe.Count > 0)
			{
				foreach(var obj in InspectMe)
                {
                    InspectorUtlity.InspectTarget(obj);
                }
				InspectMe.Clear();
            }

*/
            //EditorGUI.EndProperty();
        }
	}
}
#endif
