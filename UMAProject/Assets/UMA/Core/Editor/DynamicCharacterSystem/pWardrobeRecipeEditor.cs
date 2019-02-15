#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA.Editors
{
	public partial class RecipeEditor
	{
		private Dictionary<string,RaceData> _compatibleRaceDatas = new Dictionary<string,RaceData>();

		int meshHideAssetPickerID = -1;
        int slotHidePickerID = -1;

		// Drop area for compatible Races
		private void CompatibleRacesDropArea(Rect dropArea, List<string> compatibleRaces)
		{
			Event evt = Event.current;
			//make the box clickable so that the user can select raceData assets from the asset selection window
			if (evt.type == EventType.MouseUp)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					compatibleRacePickerID = EditorGUIUtility.GetControlID(new GUIContent("crfObjectPicker"), FocusType.Passive);
					EditorGUIUtility.ShowObjectPicker<RaceData>(null, false, "", compatibleRacePickerID);
					Event.current.Use();//stops the Mismatched LayoutGroup errors
					return;
				}
			}
			if (evt.commandName == "ObjectSelectorUpdated" && EditorGUIUtility.GetObjectPickerControlID() == compatibleRacePickerID)
			{
				RaceData tempRaceDataAsset = EditorGUIUtility.GetObjectPickerObject() as RaceData;
				if (tempRaceDataAsset)
				{
					AddRaceDataAsset(tempRaceDataAsset, compatibleRaces);
				}
				Event.current.Use();//stops the Mismatched LayoutGroup errors
				return;
			}
			if (evt.type == EventType.DragUpdated)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
				}
			}
			if (evt.type == EventType.DragPerform)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.AcceptDrag();

					UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
					for (int i = 0; i < draggedObjects.Length; i++)
					{
						if (draggedObjects[i])
						{
							RaceData tempRaceDataAsset = draggedObjects[i] as RaceData;
							if (tempRaceDataAsset)
							{
								AddRaceDataAsset(tempRaceDataAsset, compatibleRaces);
								continue;
							}

							var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
							if (System.IO.Directory.Exists(path))
							{
								RecursiveScanFoldersForAssets(path, compatibleRaces);
							}
						}
					}
				}
			}
		}

		private void RecursiveScanFoldersForAssets(string path, List<string> compatibleRaces)
		{
			var assetFiles = System.IO.Directory.GetFiles(path, "*.asset");
			foreach (var assetFile in assetFiles)
			{
				var tempRaceDataAsset = AssetDatabase.LoadAssetAtPath(assetFile, typeof(RaceData)) as RaceData;
				if (tempRaceDataAsset)
				{
					AddRaceDataAsset(tempRaceDataAsset, compatibleRaces);
				}
			}
			foreach (var subFolder in System.IO.Directory.GetDirectories(path))
			{
				RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'), compatibleRaces);
			}
		}

		private void AddRaceDataAsset(RaceData raceDataAsset, List<string> compatibleRaces)
		{
			if (!compatibleRaces.Contains(raceDataAsset.raceName))
				compatibleRaces.Add(raceDataAsset.raceName);
		}
		//this needs to generate labels too because the values are not the same as the labels
		private int GenerateWardrobeSlotsEnum(string selectedOption, List<string> compatibleRaces = null, bool forceUpdate = false)
		{
			int selectedIndex = 0;
			if (compatibleRaces == null)
			{
				selectedIndex = -1;
				generatedWardrobeSlotOptionsLabels = generatedWardrobeSlotOptions = new List<string>() { "None", "Face", "Hair", "Complexion", "Eyebrows", "Beard", "Ears", "Helmet", "Shoulders", "Chest", "Arms", "Hands", "Waist", "Legs", "Feet" };
			}
			else
			{
				if (compatibleRaces.Count == 0)
				{
					selectedIndex = -1;
					generatedWardrobeSlotOptionsLabels = generatedWardrobeSlotOptions = new List<string>() { "None", "Face", "Hair", "Complexion", "Eyebrows", "Beard", "Ears", "Helmet", "Shoulders", "Chest", "Arms", "Hands", "Waist", "Legs", "Feet" };
				}
				else if (generatedWardrobeSlotOptions.Count == 0 || forceUpdate)
				{
					//Clear the list if we are forcing update
					if (forceUpdate)
					{
						generatedWardrobeSlotOptions = new List<string>();
						generatedWardrobeSlotOptionsLabels = new List<string>();
					}
					UpdateCompatibleRacesDict(compatibleRaces);
					for (int i = 0; i < compatibleRaces.Count; i++)
					{
						if(_compatibleRaceDatas.ContainsKey(compatibleRaces[i]))
						{
							List<string> thisWardrobeSlots = _compatibleRaceDatas[compatibleRaces[i]].wardrobeSlots;
							for (int wi = 0; wi < thisWardrobeSlots.Count; wi++)
							{
								//WardrobeSlots display as 'Hair (FemaleOnly)' (for example) if the wardrobe slot is only available for one of the compatible races
								if (compatibleRaces.Count > 1 && i > 0)
								{
									if (!generatedWardrobeSlotOptions.Contains(thisWardrobeSlots[wi]))
									{
										generatedWardrobeSlotOptions.Insert(wi, thisWardrobeSlots[wi]);
										generatedWardrobeSlotOptionsLabels.Insert(wi, thisWardrobeSlots[wi]);
                                    }
								}
								else
								{
									generatedWardrobeSlotOptions.Add(thisWardrobeSlots[wi]);
									generatedWardrobeSlotOptionsLabels.Add(thisWardrobeSlots[wi]);
								}
							}
						}
						else
						{
							//Compatible Race is missing
							selectedIndex = -2;
						}
					}
					for (int i = 0; i < generatedWardrobeSlotOptions.Count; i++)
					{
						List<string> onlyIn = new List<string>();
						foreach(KeyValuePair<string,RaceData> kp in _compatibleRaceDatas)
						{
							if(kp.Value.wardrobeSlots.Contains(generatedWardrobeSlotOptions[i]))
							{
								onlyIn.Add(kp.Key);
							}
						}
						if(onlyIn.Count < _compatibleRaceDatas.Count)
						{
							//its not in all of them
							generatedWardrobeSlotOptionsLabels[i] = generatedWardrobeSlotOptionsLabels[i] + "  (" + String.Join(", ", onlyIn.ToArray()) + " Only)";
						}
					}
				}
			}
			if (generatedWardrobeSlotOptions.Count > 0)
			{
				for (int i = 0; i < generatedWardrobeSlotOptions.Count; i++)
				{
					if (generatedWardrobeSlotOptions[i] == selectedOption)
						selectedIndex = i;
				}
			}
			return selectedIndex;
		}

		//generate an option list for the BaseSlots that are available to hide for each race so we can make this a mask field too
		private void GenerateBaseSlotsEnum(List<string> compatibleRaces, bool forceUpdate = false, List<string> hides = null)
		{
            List<string> Unfound = new List<string>();
            Unfound.AddRange(hides);

			if (generatedBaseSlotOptions.Count == 0 || forceUpdate)
			{
				//clear the lists if we are forcing update
				if (forceUpdate)
				{
					generatedBaseSlotOptions = new List<string>();
					generatedBaseSlotOptionsLabels = new List<string>();
				}
				List<UMARecipeBase> thisBaseRecipes = new List<UMARecipeBase>();
				Dictionary<string, List<string>> slotsRacesDict = new Dictionary<string, List<string>>();
				UpdateCompatibleRacesDict(compatibleRaces);
				for (int i = 0; i < compatibleRaces.Count; i++)
				{
					if(_compatibleRaceDatas.ContainsKey(compatibleRaces[i]))
						thisBaseRecipes.Add(_compatibleRaceDatas[compatibleRaces[i]].baseRaceRecipe);
				}
				for (int i = 0; i < thisBaseRecipes.Count; i++)
				{
					if (thisBaseRecipes[i] != null)
					{
						UMAData.UMARecipe thisBaseRecipe = thisBaseRecipes[i].GetCachedRecipe(UMAContext.Instance);
						SlotData[] thisSlots = thisBaseRecipe.GetAllSlots();
						foreach (SlotData slot in thisSlots)
						{
							if (slot != null)
							{
								if (!generatedBaseSlotOptions.Contains(slot.asset.slotName))
								{
									generatedBaseSlotOptions.Add(slot.asset.slotName);
                                    Unfound.Remove(slot.asset.slotName); 
								}
								if (!slotsRacesDict.ContainsKey(slot.asset.slotName))
								{
									slotsRacesDict.Add(slot.asset.slotName, new List<string>());
								}
								slotsRacesDict[slot.asset.slotName].Add(compatibleRaces[i]);
							}
						}
					}
				}

                foreach(string s in Unfound)
                {
                    generatedBaseSlotOptions.Add(s);
                    if (!slotsRacesDict.ContainsKey(s))
                    {
                        slotsRacesDict.Add(s, new List<string>());
                    }
                    if (!slotsRacesDict[s].Contains("other"))
                    {
                        slotsRacesDict[s].Add("other");
                    }
                }
				//sort out the labels showing which race(s) the base slots are for if there is more than one compatible race
				foreach (KeyValuePair<string, List<string>> kp in slotsRacesDict)
				{
					string compatibleRaceNames = "";
					if (compatibleRaces.Count > 1)
					{
						compatibleRaceNames = " (" + String.Join(", ", kp.Value.ToArray()) + ")";
					}
					generatedBaseSlotOptionsLabels.Add(kp.Key + compatibleRaceNames);
				}
			}
		}
		//Updates a dictionary of racenames and racedatas so that GetCompatibleRaceData is called far less frequently (because its slow)
		private void UpdateCompatibleRacesDict(List<string> compatibleRaces)
		{
			if (compatibleRaces.Count == 0)
			{
				_compatibleRaceDatas.Clear();
				return;
			}
			Dictionary<string, RaceData> newDict = new Dictionary<string, RaceData>();
			for (int i = 0; i < compatibleRaces.Count; i++)
			{
				if (_compatibleRaceDatas.ContainsKey(compatibleRaces[i]))
					newDict.Add(compatibleRaces[i], _compatibleRaceDatas[compatibleRaces[i]]);
				else
				{
					var thisRaceData = GetCompatibleRaceData(compatibleRaces[i]);
					if(thisRaceData != null)
						newDict.Add(compatibleRaces[i], thisRaceData);
				}
			}
			_compatibleRaceDatas = newDict;
		}
		//Avoid calling this all the time because its slow
		private RaceData GetCompatibleRaceData(string raceName)
		{
			RaceData foundRace = null;
			string[] foundRacesStrings = AssetDatabase.FindAssets("t:RaceData");
			for (int i = 0; i < foundRacesStrings.Length; i++)
			{
				RaceData thisFoundRace = AssetDatabase.LoadAssetAtPath<RaceData>(AssetDatabase.GUIDToAssetPath(foundRacesStrings[i]));
				if (thisFoundRace.raceName == raceName)
				{
					foundRace = thisFoundRace;
					break;
				}
			}
			return foundRace;
		}

		protected virtual bool DrawCompatibleRacesUI(Type TargetType, bool ShowHelp = false)
		{
			bool doUpdate = false;
			float padding = 2f;
			//FieldInfos
			FieldInfo CompatibleRacesField = TargetType.GetField("compatibleRaces", BindingFlags.Public | BindingFlags.Instance);
			FieldInfo WardrobeRecipeThumbsField = TargetType.GetField("wardrobeRecipeThumbs", BindingFlags.Public | BindingFlags.Instance);
			//may not be needed- Check
			FieldInfo WardrobeSlotField = TargetType.GetField("wardrobeSlot", BindingFlags.Public | BindingFlags.Instance);
			//field values
			List<string> compatibleRaces = (List<string>)CompatibleRacesField.GetValue(target);
			List<WardrobeRecipeThumb> wardrobeThumbs = (List<WardrobeRecipeThumb>)WardrobeRecipeThumbsField.GetValue(target);
			string wardrobeSlot = (string)WardrobeSlotField.GetValue(target);
			//new field values
			List<string> newCompatibleRaces = new List<string>(compatibleRaces);
			List<WardrobeRecipeThumb> newWardrobeThumbs = new List<WardrobeRecipeThumb>();
			List<string> wardrobeThumbsDropDown = new List<string>();

			UpdateCompatibleRacesDict(compatibleRaces);

			if (compatibleRaces.Count > 0)
			{
				foreach (string cr in compatibleRaces)
				{
					bool wrtFound = false;
					foreach (WardrobeRecipeThumb wrt in wardrobeThumbs)
					{
						if (wrt.race == cr)
						{
							newWardrobeThumbs.Add(wrt);
							wrtFound = true;
						}
					}
					if (wrtFound == false)
					{
						newWardrobeThumbs.Add(new WardrobeRecipeThumb(cr));
					}
				}
				foreach (WardrobeRecipeThumb wrt in newWardrobeThumbs)
				{
					wardrobeThumbsDropDown.Add(wrt.race);
				}
			}

			//GUILayout.Space(10);
			Rect dropArea = new Rect();
			Rect dropAreaBox = new Rect();
			if (compatibleRaces.Count > 0)
			{
				dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f + EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
				dropArea.width = dropArea.width - 85f;
				dropAreaBox = dropArea;
				dropAreaBox.y = dropAreaBox.y + EditorGUIUtility.singleLineHeight;
				dropAreaBox.height = dropAreaBox.height - EditorGUIUtility.singleLineHeight;
			}
			else
			{
				dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
				dropAreaBox = dropArea;
			}
			GUI.Box(dropAreaBox, "Drag Races compatible with this Recipe here. Click to pick.");
			if (compatibleRaces.Count > 0)
			{
				for (int i = 0; i < compatibleRaces.Count; i++)
				{
					GUILayout.Space(padding);
					Rect crfRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
					Rect crfDelRect = crfRect;
					crfRect.width = crfRect.width - 75f - 20f - 20f;
					crfDelRect.width = 20f + padding;
					crfDelRect.x = crfRect.width /*+ 20f*/ + padding;
					//We need to check if the RaceData is in the index or an assetBundle otherwise show the add buttons
					if(!_compatibleRaceDatas.ContainsKey(compatibleRaces[i]) || !RaceInIndex(_compatibleRaceDatas[compatibleRaces[i]]))
					{
						crfRect.width = crfRect.width - 20f;
						crfRect.x = crfRect.x + 20f;
						var warningRect = new Rect((crfRect.xMin - 20f), crfRect.yMin, 20f, crfRect.height);
						string warningMsg = "";
						//the race is in the project but not assigned to the index or any assetBundles
						if (_compatibleRaceDatas.ContainsKey(compatibleRaces[i]))
							warningMsg = compatibleRaces[i] + " is not indexed! Either assign it to an assetBundle or use one of the buttons below to add it to the Scene/Global Library.";
						else //the race is missing from the project
							warningMsg = compatibleRaces[i] + " could not be found in the project. Have you deleted it?";
						var warningGUIContent = new GUIContent("", warningMsg);
						warningGUIContent.image = warningIcon;
						EditorGUI.LabelField(warningRect, warningGUIContent);
						if (_compatibleRaceDatas.ContainsKey(compatibleRaces[i]))
						{
							Rect addButsRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
							addButsRect.xMin = crfRect.xMin;
							addButsRect.width = crfRect.width;
							Rect butScene = addButsRect;
							Rect butIndex = addButsRect;
							butScene.width = addButsRect.width / 3f;
							butIndex.xMin = butScene.xMax;
							butIndex.width = (addButsRect.width / 3f)*2;
                            if (GUI.Button(butScene,"Add to Scene Only", EditorStyles.miniButton))
							{
								UMAContext.Instance.AddRace(_compatibleRaceDatas[compatibleRaces[i]]);
							}
							if (GUI.Button(butIndex,"Add to Global Index (Recommended)", EditorStyles.miniButton))
							{
								UMAAssetIndexer.Instance.EvilAddAsset(typeof(RaceData), _compatibleRaceDatas[compatibleRaces[i]]);
							}
						}
					}
					GUI.enabled = false; //we readonly to prevent typos
					EditorGUI.TextField(crfRect, compatibleRaces[i]);
					GUI.enabled = true;
					if (GUI.Button(crfDelRect, "X"))
					{
						newCompatibleRaces.RemoveAt(i);
					}
				}
				Rect thumbnailRect = dropArea;
				thumbnailRect.x = dropArea.width + padding /*+ 20f*/;
				thumbnailRect.width = 75f;
				thumbnailRect.y = thumbnailRect.y - 3f;
				Rect thumbnailDDRect = thumbnailRect;
				Rect thumbnailThumbRect = thumbnailRect;
				thumbnailThumbRect.height = 75f;
				EditorGUI.LabelField(thumbnailRect, "Thumbnail");
				thumbnailDDRect.y = thumbnailDDRect.y + EditorGUIUtility.singleLineHeight;
				thumbnailThumbRect.y = thumbnailThumbRect.y + EditorGUIUtility.singleLineHeight;
				if (newCompatibleRaces.Count > 1)
				{
					thumbnailThumbRect.y = thumbnailThumbRect.y + EditorGUIUtility.singleLineHeight + padding;
					selectedWardrobeThumb = EditorGUI.Popup(thumbnailDDRect, selectedWardrobeThumb, wardrobeThumbsDropDown.ToArray());
				}
				if (newWardrobeThumbs.Count != newCompatibleRaces.Count)
				{
					selectedWardrobeThumb = 0;
				}
				EditorGUI.BeginChangeCheck();
				var thisImg = EditorGUI.ObjectField(thumbnailThumbRect, newWardrobeThumbs[selectedWardrobeThumb].thumb, typeof(Sprite), false);
				if (EditorGUI.EndChangeCheck())
				{
					if (thisImg != newWardrobeThumbs[selectedWardrobeThumb].thumb)
					{
						newWardrobeThumbs[selectedWardrobeThumb].thumb = (Sprite)thisImg;
						doUpdate = true;
					}
				}
			}
			else
			{
				//EditorGUILayout.HelpBox("No Compatible Races set. This " + TargetType.ToString() + " will be available to all races.", MessageType.None);
				//DOS MODIFIED this should really be possible tho...
				EditorGUILayout.HelpBox("No Compatible Races set.", MessageType.Warning);
			}
			CompatibleRacesDropArea(dropArea, newCompatibleRaces);


			//update values
			if (!AreListsEqual<string>(newCompatibleRaces, compatibleRaces))
			{
				//if the compatible races has changed we need to regenerate the enums
				//If the libraries cannot load the raceBaseRecipe because of missing slots/overlays
				//we dont want to actually change anything and need to show an error- but still show the recipe as it was
				try
				{
                    FieldInfo HidesField = TargetType.GetField("Hides", BindingFlags.Public | BindingFlags.Instance);
                    List<string> hides = (List<string>)HidesField.GetValue(target);
                    GenerateBaseSlotsEnum(newCompatibleRaces, true, hides);
				}
				catch (UMAResourceNotFoundException e)
				{
					newCompatibleRaces = new List<string>(compatibleRaces);
					Debug.LogError("The Recipe Editor could not add the selected compatible race because some required assets could not be found: " + e.Message);
				}
				GenerateWardrobeSlotsEnum(wardrobeSlot, newCompatibleRaces, true);
				CompatibleRacesField.SetValue(target, newCompatibleRaces);
				doUpdate = true;
			}
			if (!AreListsEqual<WardrobeRecipeThumb>(newWardrobeThumbs, wardrobeThumbs))
			{
				WardrobeRecipeThumbsField.SetValue(target, newWardrobeThumbs);
				doUpdate = true;
			}
            if (ShowHelp)
            {
                EditorGUILayout.HelpBox("Compatible races are used to assign this recipe to a race or races. Recipes are restricted to the races to which they are assigned - you cannot assign wardrobe items to races that are not compatible. Thumbnails can be used to attach sprites to the recipe for use in UI design.", MessageType.Info);
            }
			GUILayout.Space(10);
			return doUpdate;
		}

		protected virtual bool DrawWardrobeSlotsFields(Type TargetType, bool ShowHelp = false)
		{
            #region Setup
            bool doUpdate = false;
            //Field Infos
            FieldInfo ReplacesField = TargetType.GetField("replaces", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo CompatibleRacesField = TargetType.GetField("compatibleRaces", BindingFlags.Public | BindingFlags.Instance);
			FieldInfo WardrobeSlotField = TargetType.GetField("wardrobeSlot", BindingFlags.Public | BindingFlags.Instance);
			FieldInfo SuppressWardrobeSlotField = TargetType.GetField("suppressWardrobeSlots", BindingFlags.Public | BindingFlags.Instance);
			FieldInfo HidesField = TargetType.GetField("Hides", BindingFlags.Public | BindingFlags.Instance);
			FieldInfo DisplayValueField = TargetType.GetField("DisplayValue", BindingFlags.Public | BindingFlags.Instance);

            // ************************************
            // field values
            // ************************************
            string replaces = "";
			if (ReplacesField != null)
			{
				object o = ReplacesField.GetValue(target);
				if (o != null)
				{
					replaces = (string)ReplacesField.GetValue(target);
				}
			}

            List<string> compatibleRaces = (List<string>)CompatibleRacesField.GetValue(target);
			string wardrobeSlot = (string)WardrobeSlotField.GetValue(target);
			List<string> suppressWardrobeSlot = (List<string>)SuppressWardrobeSlotField.GetValue(target);
			List<string> hides = (List<string>)HidesField.GetValue(target);
			string displayValue = (string)DisplayValueField.GetValue(target);
            #endregion

            #region Display Value UI
            //displayValue UI
            string PreviousValue = displayValue;
			displayValue = EditorGUILayout.DelayedTextField("Display Value", displayValue);
			if (displayValue != PreviousValue)
			{
				DisplayValueField.SetValue(target, displayValue);
				doUpdate = true;
			}
            if (ShowHelp)
            {
                EditorGUILayout.HelpBox("Display Value can be used to store a user-friendly name for this item. It's not used for constructing the character, but it can be used in UI design by accessing the .DisplayValue field on the recipe.", MessageType.Info);
            }
            #endregion

            #region Wardrobe Slot UI
            //wardrobeSlot UI
            int selectedWardrobeSlotIndex = GenerateWardrobeSlotsEnum(wardrobeSlot, compatibleRaces, false);
			string newWardrobeSlot;
			int newSuppressFlags = 0;
			List<string> newSuppressWardrobeSlot = new List<string>();
			if (selectedWardrobeSlotIndex == -1)
			{
				EditorGUILayout.LabelField("No Compatible Races set. You need to select a compatible race in order to set a wardrobe slot");
				newWardrobeSlot = "None";
			}
			else if (selectedWardrobeSlotIndex == -2)
			{
				EditorGUILayout.LabelField("Not all compatible races found. Do you have the all correct Race(s) available Locally?");
				newWardrobeSlot = "None";
			}
			else
			{
				int newSelectedWardrobeSlotIndex = EditorGUILayout.Popup("Wardrobe Slot", selectedWardrobeSlotIndex, generatedWardrobeSlotOptionsLabels.ToArray());
				if (newSelectedWardrobeSlotIndex != selectedWardrobeSlotIndex)
				{
					WardrobeSlotField.SetValue(target, generatedWardrobeSlotOptions[newSelectedWardrobeSlotIndex]);
					doUpdate = true;
				}
				newWardrobeSlot = generatedWardrobeSlotOptions.Count > 0 ? generatedWardrobeSlotOptions[selectedWardrobeSlotIndex] : "None";
			}
            if (ShowHelp)
            {
                EditorGUILayout.HelpBox("Wardrobe Slot: This assigns the recipe to a Wardrobe Slot. The wardrobe slots are defined on the race. Characters can have only one recipe per Wardrobe Slot at a time, so for example, adding a 'beard' recipe to a character will replace the existing 'beard' if there is one", MessageType.Info);
            }
            #endregion

            #region Suppress UI
            //SuppressedSlots UI
            int suppressFlags = 0;
			for (int i = 0; i < generatedWardrobeSlotOptions.Count; i++)
			{
				if (suppressWardrobeSlot.Contains(generatedWardrobeSlotOptions[i]))
				{
					suppressFlags |= 0x1 << i;
				}
			}
			newSuppressFlags = EditorGUILayout.MaskField("Suppress Wardrobe Slot(s)", suppressFlags, generatedWardrobeSlotOptionsLabels.ToArray());
			for (int i = 0; i < generatedWardrobeSlotOptions.Count; i++)
			{
				if ((newSuppressFlags & (1 << i)) == (1 << i))
				{
					newSuppressWardrobeSlot.Add(generatedWardrobeSlotOptions[i]);
				}
			}
			if (newSuppressWardrobeSlot.Count > 1)
			{
				GUI.enabled = false;
				string swsl2Result = String.Join(", ", newSuppressWardrobeSlot.ToArray());
				EditorGUILayout.TextField(swsl2Result);
				GUI.enabled = true;
			}
            if (ShowHelp)
            {
                EditorGUILayout.HelpBox("Suppress: This will stop a different wardrobe slot from displaying. For example, if you have a full-length robe assigned to a 'chest' wardrobe slot, you would want to suppress whatever is assigned to the 'legs' wardrobe slot, so they don't poke through. This is typically used for dresses, robes, and other items that cover multiple body areas.", MessageType.Info);
            }
            #endregion

            #region Hides UI
            //Hides UI
            EditorGUILayout.BeginHorizontal();
            GenerateBaseSlotsEnum(compatibleRaces, false, hides);
			int hiddenBaseFlags = 0;
			List<string> newHides = new List<string>();
			for (int i = 0; i < generatedBaseSlotOptions.Count; i++)
			{
				if (hides.Contains(generatedBaseSlotOptions[i]))
				{
					hiddenBaseFlags |= 0x1 << i;
				}
			}

			if (generatedBaseSlotOptionsLabels.Count > 0)
			{
				int newHiddenBaseFlags = 0;

				newHiddenBaseFlags = EditorGUILayout.MaskField("Hides Base Slot(s)", hiddenBaseFlags, generatedBaseSlotOptionsLabels.ToArray());
				for (int i = 0; i < generatedBaseSlotOptionsLabels.Count; i++)
				{
					if ((newHiddenBaseFlags & (1 << i)) == (1 << i))
					{
						newHides.Add(generatedBaseSlotOptions[i]);
					}
				}
			}
			else
				EditorGUILayout.Popup("Hides Base Slots(s)", 0, new string[1] {"Nothing"} );

            GUILayout.Space(8);
            if (GUILayout.Button("Select",GUILayout.MaxWidth(64), GUILayout.MaxHeight(16)))
            {
                slotHidePickerID = EditorGUIUtility.GetControlID(FocusType.Passive) + 101;
                EditorGUIUtility.ShowObjectPicker<SlotDataAsset>(null, false, "", slotHidePickerID);
            }
            if (Event.current.commandName == "ObjectSelectorUpdated" && EditorGUIUtility.GetObjectPickerControlID() == slotHidePickerID)
            {
                SlotDataAsset sda = EditorGUIUtility.GetObjectPickerObject() as SlotDataAsset;
                newHides.Add(sda.slotName);
                Event.current.Use();
                GenerateBaseSlotsEnum(compatibleRaces, true, hides);
            }

            EditorGUILayout.EndHorizontal();
            if (newHides.Count > 1)
            {
                GUI.enabled = false;
                string newHidesResult = String.Join(", ", newHides.ToArray());
                EditorGUILayout.TextField(newHidesResult);
                GUI.enabled = true;
            }

            if (ShowHelp)
            {
                EditorGUILayout.HelpBox("Hides: This is used to hide parts of the base recipe. For example, if you create gloves, you may want to hide the 'hands', so you don't get poke-through", MessageType.Info);
            }
            #endregion

            #region Replaces UI
            if (ReplacesField != null)
			{
				List<string> ReplacesSlots = new List<string>(generatedBaseSlotOptions);
				ReplacesSlots.Insert(0, "Nothing");
				int selectedIndex = ReplacesSlots.IndexOf(replaces);
				if (selectedIndex < 0) selectedIndex = 0; // not found, point at "nothing"

				selectedIndex = EditorGUILayout.Popup("Replaces", selectedIndex, ReplacesSlots.ToArray());

				ReplacesField.SetValue(target, ReplacesSlots[selectedIndex]);
			}

            if (ShowHelp)
            {
                EditorGUILayout.HelpBox("Replaces: This is used to replace part of the base recipe while keeping it's overlays. For example, if you want to replace the head from the base race recipe with a High Poly head, you would 'replace' the head, not hide it. Only one slot can be replaced, and the recipe should only contain one slot.", MessageType.Info);
            }
            #endregion

            #region MeshHideArray
			//EditorGUIUtility.LookLikeInspector();
			SerializedProperty meshHides = serializedObject.FindProperty ("MeshHideAssets");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.BeginHorizontal();
			if(GUILayout.Button("+", GUILayout.MaxWidth(30)))
			{
				meshHideAssetPickerID = EditorGUIUtility.GetControlID(FocusType.Passive) + 100;
				EditorGUIUtility.ShowObjectPicker<MeshHideAsset>(null, false, "", meshHideAssetPickerID);
			}
			GUILayout.Space(10);
			if (Event.current.commandName == "ObjectSelectorUpdated" && EditorGUIUtility.GetObjectPickerControlID() == meshHideAssetPickerID)
			{
				meshHides.InsertArrayElementAtIndex(0);
				SerializedProperty element = meshHides.GetArrayElementAtIndex(0);
				element.objectReferenceValue = EditorGUIUtility.GetObjectPickerObject();
				meshHideAssetPickerID = -1;
			}
			EditorGUILayout.PropertyField(meshHides, true);
			EditorGUILayout.EndHorizontal();
			if (EditorGUI.EndChangeCheck())
				serializedObject.ApplyModifiedProperties();
            //EditorGUIUtility.LookLikeControls();
            if(ShowHelp)
            {
                EditorGUILayout.HelpBox("MeshHideAssets: This is a list of advanced mesh hiding assets to hide their corresponding slot meshes on a per triangle basis.", MessageType.Info);
            }
            #endregion

            #region Update
            //Update the values
            if (newWardrobeSlot != wardrobeSlot)
			{
				WardrobeSlotField.SetValue(target, newWardrobeSlot);
				doUpdate = true;
			}
			if (!AreListsEqual<string>(newSuppressWardrobeSlot, suppressWardrobeSlot))
			{
				SuppressWardrobeSlotField.SetValue(target, newSuppressWardrobeSlot);
				doUpdate = true;
			}
			if (!AreListsEqual<string>(newHides, hides))
			{
				HidesField.SetValue(target, newHides);
				doUpdate = true;
			}
            #endregion

            return doUpdate;
		}
		/// <summary>
		/// And editor for a WardrobeRecipe that shows sharedColors and Slots but hides the 'raceData' field (because WardrobeRecipes have a 'compatibleRaces' list)
		/// </summary>
		public class WardrobeRecipeMasterEditor : SlotMasterEditor
		{
			List<string> _baseSlotOptions = new List<string>();
			List<string> _baseSlotOptionsLabels = new List<string>();

			public WardrobeRecipeMasterEditor(UMAData.UMARecipe recipe, List<string> baseSlotOptions, List<string> baseSlotOptionsLabels) : base(recipe)
			{
				_baseSlotOptions = baseSlotOptions;
				_baseSlotOptionsLabels = baseSlotOptionsLabels;
			}
			public override bool OnGUI(string targetName, ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
			{
				bool changed = false;

				if (_sharedColorsEditor.OnGUI(_recipe))
				{
					changed = true;
					_textureDirty = true;
				}

				GUILayout.Space(20);
				Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
				GUI.Box(dropArea, "Drag Slots and Overlays here. Click to pick");
				if (DropAreaGUI(dropArea))
				{
					changed |= true;
					_dnaDirty |= true;
					_textureDirty |= true;
					_meshDirty |= true;
				}
				GUILayout.Space(10);

				if (_baseSlotOptions.Count > 0)
				{
					var baseSlotsNamesList = new List<string>() { "None" };
					for (int i = 0; i < _baseSlotOptionsLabels.Count; i++)
					{
						baseSlotsNamesList.Add(_baseSlotOptionsLabels[i]);
					}
					EditorGUI.BeginChangeCheck();
					var baseAdded = EditorGUILayout.Popup("Add Base Slot", 0, baseSlotsNamesList.ToArray());
					if (EditorGUI.EndChangeCheck())
					{
						if (baseAdded != 0)
						{
							var slotName = _baseSlotOptions[baseAdded - 1];
							LastSlot = slotName;
							//we know there should be one because we created a virtual one when we unpacked the recipe if it didn't exist
							var context = UMAContext.FindInstance();
							var slotToAdd = context.InstantiateSlot(slotName);
							_recipe.MergeSlot(slotToAdd, false);
							changed |= true;
							_dnaDirty |= true;
							_textureDirty |= true;
							_meshDirty |= true;
						}
					}
				}

				var added = (SlotDataAsset)EditorGUILayout.ObjectField("Add Slot", null, typeof(SlotDataAsset), false);

				if (added != null)
				{
					LastSlot = added.slotName;
					var slot = new SlotData(added);
					_recipe.MergeSlot(slot, false);
					changed |= true;
					_dnaDirty |= true;
					_textureDirty |= true;
					_meshDirty |= true;
				}

				GUILayout.Space(20);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Clear Recipe"))
                {
                    _recipe.slotDataList = new SlotData[0];
                    changed |= true;
                    _dnaDirty |= true;
                    _textureDirty |= true;
                    _meshDirty |= true;
                }
                if (GUILayout.Button("Remove Nulls"))
				{
					var newList = new List<SlotData>(_recipe.slotDataList.Length);
					foreach (var slotData in _recipe.slotDataList)
					{
						if (slotData != null) newList.Add(slotData);
					}
					_recipe.slotDataList = newList.ToArray();
					changed |= true;
					_dnaDirty |= true;
					_textureDirty |= true;
					_meshDirty |= true;
				}
                GUILayout.EndHorizontal();

				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Collapse All"))
				{
					foreach (SlotEditor se in _slotEditors)
					{
						se.FoldOut = false;
					}
				}
				if (GUILayout.Button("Expand All"))
				{
					foreach (SlotEditor se in _slotEditors)
					{
						se.FoldOut = true;
					}
				}
				GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();

                if (GUILayout.Button("Select All Slots"))
                {
                    SelectAllSlots();
                }
                if (GUILayout.Button("Select All Overlays"))
                {
                    SelectAllOverlays();
                }

                GUILayout.EndHorizontal();

                for (int i = 0; i < _slotEditors.Count; i++)
				{
					var editor = _slotEditors[i];

					if (editor == null)
					{
						GUILayout.Label("Empty Slot");
						continue;
					}

					changed |= editor.OnGUI(ref _dnaDirty, ref _textureDirty, ref _meshDirty);

					if (editor.Delete)
					{
						_dnaDirty = true;
						_textureDirty = true;
						_meshDirty = true;

						_slotEditors.RemoveAt(i);
						_recipe.SetSlot(editor.idx, null);
						i--;
						changed = true;
					}
				}

				return changed;
			}
		}
       /* private void SelectAllSlots()
        {
            List<UnityEngine.Object> slots = new List<UnityEngine.Object>();
            foreach (var slotData in _recipe.slotDataList)
            {
                if (slotData != null)
                {
                    slots.Add(slotData.asset);
                }
            }
            Selection.objects = slots.ToArray();
        }

        private void SelectAllOverlays()
        {
            HashSet<UnityEngine.Object> overlays = new HashSet<UnityEngine.Object>();
            foreach (var slotData in _recipe.slotDataList)
            {
                if (slotData != null)
                {
                    List<OverlayData> overlayData = slotData.GetOverlayList();
                    foreach (var overlay in overlayData)
                    {
                        if (overlay != null)
                        {
                            overlays.Add(overlay.asset);
                        }
                    }
                }
            }
            UnityEngine.Object[] newSelection = new UnityEngine.Object[overlays.Count];
            overlays.CopyTo(newSelection);
            Selection.objects = newSelection;
        } */
    }
}
#endif
