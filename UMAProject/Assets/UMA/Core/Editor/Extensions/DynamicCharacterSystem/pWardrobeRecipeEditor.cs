#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;
using UnityEditorInternal;

namespace UMA.Editors
{
    public partial class RecipeEditor
	{
		private Dictionary<string,RaceData> _compatibleRaceDatas = new Dictionary<string,RaceData>();

		int currentRace = 0;
		bool showIncompatible;
		List<UMAWardrobeRecipe> DeletedRecipes = new List<UMAWardrobeRecipe>();
		int meshHideAssetPickerID = -1;
        int slotHidePickerID = -1;
		int selectedsuppressed = -1;
		private static bool showModifiers = false;


        // Drop area for compatible Races
        /// <summary>
        /// One thumbnail per distinct compatible race, in race order: the last stored entry for that race
        /// that has a sprite (the one characters use), else its first entry, else a new empty one. A race
        /// listed twice used to add every matching thumbnail once per listing, so the stored list doubled
        /// (and was saved) on every inspector draw.
        /// </summary>
        /// <summary>A placeholder slot name not used by another placeholder in the recipe: the name, then "name 2", "name 3"...</summary>
        public static string UniquePlaceholderName(UMAData.UMARecipe recipe, string baseName)
        {
            var used = new HashSet<string>();
            if (recipe != null && recipe.slotDataList != null)
            {
                foreach (SlotData existing in recipe.slotDataList)
                {
                    if (existing != null && existing.isPlaceholderSlot && existing.placeholderSlotName != null)
                    {
                        used.Add(existing.placeholderSlotName);
                    }
                }
            }
            string name = baseName;
            for (int suffix = 2; used.Contains(name); suffix++)
            {
                name = baseName + " " + suffix;
            }
            return name;
        }

        public static List<WardrobeRecipeThumb> BuildRaceThumbs(IList<string> compatibleRaces, IList<WardrobeRecipeThumb> storedThumbs)
        {
            var result = new List<WardrobeRecipeThumb>();
            if (compatibleRaces == null)
            {
                return result;
            }
            var seen = new HashSet<string>();
            foreach (string race in compatibleRaces)
            {
                if (race == null || !seen.Add(race))
                {
                    continue;
                }
                WardrobeRecipeThumb match = null;
                if (storedThumbs != null)
                {
                    foreach (WardrobeRecipeThumb thumb in storedThumbs)
                    {
                        if (thumb != null && thumb.race == race)
                        {
                            // Characters take the last entry with a sprite for a race
                            // (GetWardrobeRecipeThumbFor), so the inspector keeps that one.
                            if (match == null || thumb.thumb != null)
                            {
                                match = thumb;
                            }
                        }
                    }
                }
                result.Add(match ?? new WardrobeRecipeThumb(race));
            }

            // A race with no sprite of its own shows the first sprite in the list (GetWardrobeRecipeThumbFor),
            // which can be on an entry for a race that is no longer compatible. Dropping that entry took the
            // thumbnail away from those races, so it is kept first in the list while a race still uses it.
            Sprite fallback = null;
            WardrobeRecipeThumb fallbackEntry = null;
            if (storedThumbs != null)
            {
                foreach (WardrobeRecipeThumb thumb in storedThumbs)
                {
                    if (thumb != null && thumb.thumb != null)
                    {
                        fallback = thumb.thumb;
                        fallbackEntry = thumb;
                        break;
                    }
                }
            }
            if (fallback != null && result.Exists(t => t.thumb == null))
            {
                WardrobeRecipeThumb firstWithSprite = result.Find(t => t.thumb != null);
                if (firstWithSprite == null || firstWithSprite.thumb != fallback)
                {
                    result.Insert(0, fallbackEntry);
                }
            }
            return result;
        }

        /// <summary>
        /// The thumbnail list's label for an entry: its race, or for the entry kept only as the default for
        /// races without a sprite of their own (see BuildRaceThumbs), the race marked as the default.
        /// </summary>
        public static string RaceThumbLabel(IList<WardrobeRecipeThumb> thumbs, IList<string> compatibleRaces, int index)
        {
            WardrobeRecipeThumb entry = thumbs[index];
            bool repeated = false;
            for (int i = index + 1; i < thumbs.Count; i++)
            {
                if (thumbs[i] != null && thumbs[i].race == entry.race)
                {
                    repeated = true;
                    break;
                }
            }
            if (repeated || compatibleRaces == null || !compatibleRaces.Contains(entry.race))
            {
                return (string.IsNullOrEmpty(entry.race) ? "(no race)" : entry.race) + " (default for races without their own)";
            }
            return entry.race;
        }

        private void CompatibleRacesDropArea(Rect dropArea, List<string> compatibleRaces)
		{
			// Drag events reach a disabled (locked) inspector too.
			if (!GUI.enabled) return;
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
			// Act when the picker closes, not on every highlight (ObjectSelectorUpdated), so browsing
			// does not add each race looked at. A cancelled picker returns nothing.
			if (evt.commandName == "ObjectSelectorClosed" && EditorGUIUtility.GetObjectPickerControlID() == compatibleRacePickerID)
			{
				RaceData tempRaceDataAsset = EditorGUIUtility.GetObjectPickerObject() as RaceData;
				if (tempRaceDataAsset)
				{
					AddRaceDataAsset(tempRaceDataAsset, compatibleRaces);
				}
				if (evt.type != EventType.Layout)
                {
                    Event.current.Use();//stops the Mismatched LayoutGroup errors
                }

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

		private static bool IsBakedRace(RaceData raceData)
		{
			return raceData != null && raceData.PrebakedBlendshapes != null && raceData.PrebakedBlendshapes.Count > 0;
		}

		private static bool IsBakedSlotName(string slotName, RaceData raceData)
		{
			if (string.IsNullOrEmpty(slotName))
			{
				return false;
			}

			if (raceData != null && !string.IsNullOrEmpty(raceData.raceName))
			{
				return slotName.EndsWith("_baked_" + raceData.raceName, StringComparison.OrdinalIgnoreCase);
			}

			return slotName.IndexOf("_baked_", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static string GetPreferredBakedSlotName(SlotDataAsset slotAsset, RaceData raceData)
		{
			if (slotAsset == null || raceData == null)
			{
				return string.Empty;
			}

			string baseName = !string.IsNullOrEmpty(slotAsset.name) ? slotAsset.name : slotAsset.slotName;
			if (string.IsNullOrEmpty(baseName))
			{
				return string.Empty;
			}

			return baseName + "_baked_" + raceData.raceName;
		}

		private static List<string> GetBakedSlotNameCandidates(SlotDataAsset slotAsset, RaceData raceData)
		{
			List<string> candidates = new List<string>();
			if (slotAsset == null || raceData == null)
			{
				return candidates;
			}

			if (IsBakedSlotName(slotAsset.slotName, raceData))
			{
				candidates.Add(slotAsset.slotName);
			}

			if (!string.IsNullOrEmpty(slotAsset.slotName))
			{
				string slotNameCandidate = slotAsset.slotName + "_baked_" + raceData.raceName;
				if (!candidates.Contains(slotNameCandidate))
				{
					candidates.Add(slotNameCandidate);
				}
			}

			string preferredCandidate = GetPreferredBakedSlotName(slotAsset, raceData);
			if (!string.IsNullOrEmpty(preferredCandidate) && !candidates.Contains(preferredCandidate))
			{
				candidates.Add(preferredCandidate);
			}

			return candidates;
		}

		private static SlotDataAsset BakeSlotForRaceIfNeeded(SlotDataAsset slotAsset, RaceData raceData)
		{
			if (slotAsset == null || raceData == null)
			{
				return null;
			}

			if (!IsBakedRace(raceData))
			{
				return slotAsset;
			}

			if (IsBakedSlotName(slotAsset.slotName, raceData) ||
                !SlotMasterEditor.SlotHasBakeTargetsForRace(slotAsset, raceData))
			{
				return slotAsset;
			}

			List<string> candidates = GetBakedSlotNameCandidates(slotAsset, raceData);
			var indexer = UMAAssetIndexer.Instance;
			if (indexer != null)
			{
				for (int i = 0; i < candidates.Count; i++)
				{
					string candidate = candidates[i];
					if (string.IsNullOrEmpty(candidate))
					{
						continue;
					}

					SlotDataAsset existing = indexer.GetAssetItem<SlotDataAsset>(candidate)?.Item as SlotDataAsset;
					if (existing != null)
					{
						return existing;
					}
				}
			}

			SlotDataAsset.BakeSlotParams options = new SlotDataAsset.BakeSlotParams();
			options.burnOptions = raceData.PrebakedBlendshapes;
			options.ShapesToInclude = raceData.UnbakedShapesToInclude;
			options.addToIndexer = true;
			options.smoothingAngleDegrees = -1.0f;
			options.forceRebuildRaceSlots = raceData.forceRebuildRaceSlots;
			options.newSlotName = GetPreferredBakedSlotName(slotAsset, raceData);

			if (raceData.UnbakedShapesToInclude != null && raceData.UnbakedShapesToInclude.Count > 0)
			{
				options.copyUnbakedBlendshapes = true;
			}

			return slotAsset.BakeNewSlotData(options);
		}

		private string ResolveBaseSlotNameForRace(string slotName, RaceData raceData)
		{
			if (string.IsNullOrEmpty(slotName) || !IsBakedRace(raceData))
			{
				return slotName;
			}

			if (IsBakedSlotName(slotName, raceData))
			{
				return slotName;
			}

			SlotDataAsset slotAsset = UMAAssetIndexer.Instance.GetAsset<SlotDataAsset>(slotName, false, true);
			if (slotAsset == null)
			{
				return slotName;
			}

			SlotDataAsset bakedSlot = BakeSlotForRaceIfNeeded(slotAsset, raceData);
			if (bakedSlot != null && !string.IsNullOrEmpty(bakedSlot.slotName))
			{
				return bakedSlot.slotName;
			}

			return slotName;
		}
		// The compatible races each cached menu was built for. The menus are rebuilt when the races differ,
		// whoever changed them (Copy Selected Values, Standard View): a cached menu could offer the old
		// races' regions and base slots.
		private string wardrobeSlotOptionsRaces;
		private string baseSlotOptionsRaces;

		private static string RaceListKey(List<string> compatibleRaces)
		{
			return compatibleRaces == null ? null : string.Join("|", compatibleRaces);
		}

		//this needs to generate labels too because the values are not the same as the labels
		private int GenerateWardrobeSlotsEnum(string selectedOption, List<string> compatibleRaces = null, bool forceUpdate = false)
		{
			string raceKey = RaceListKey(compatibleRaces);
			if (raceKey != wardrobeSlotOptionsRaces)
			{
				forceUpdate = true;
				wardrobeSlotOptionsRaces = raceKey;
			}
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
                    {
                        selectedIndex = i;
                    }
                }
			}
			return selectedIndex;
		}


       private List<string> GetSlotNames(UMAPackedRecipeBase recipe, RaceData raceData)
        {
			List<string> theSlots = new List<string>();
			UMAPackedRecipeBase.UMAPackRecipe PackRecipe =recipe.PackedLoad();

			if (PackRecipe.slotsV2 != null)
			{
				foreach (var s2 in PackRecipe.slotsV2)
				{
                    if (!string.IsNullOrEmpty(s2.id))
					{
                        theSlots.Add(ResolveBaseSlotNameForRace(s2.id, raceData));
					}
				}
			}
			if (PackRecipe.slotsV3 != null)
            {
				foreach (var s3 in PackRecipe.slotsV3)
				{
                    if (!string.IsNullOrEmpty(s3.id))
					{
                        theSlots.Add(ResolveBaseSlotNameForRace(s3.id, raceData));
					}
				}
			}

			return theSlots;
        }
		//generate an option list for the BaseSlots that are available to hide for each race so we can make this a mask field too
		private void GenerateBaseSlotsEnum(List<string> compatibleRaces, bool forceUpdate = false, List<string> hides = null)
		{
			string raceKey = RaceListKey(compatibleRaces);
			if (raceKey != baseSlotOptionsRaces)
			{
				forceUpdate = true;
				baseSlotOptionsRaces = raceKey;
			}
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
				Dictionary<string, List<string>> slotsRacesDict = new Dictionary<string, List<string>>();
				UpdateCompatibleRacesDict(compatibleRaces);
				for (int i = 0; i < compatibleRaces.Count; i++)
				{
                    if(_compatibleRaceDatas.ContainsKey(compatibleRaces[i]))
					{
						RaceData raceData = _compatibleRaceDatas[compatibleRaces[i]];
						if (raceData != null && raceData.baseRaceRecipe != null)
                        {
                           List<string> slots = GetSlotNames(raceData.baseRaceRecipe as UMAPackedRecipeBase, raceData);
							foreach(string slotName in slots)
							{
								if (!generatedBaseSlotOptions.Contains(slotName))
								{
									generatedBaseSlotOptions.Add(slotName);
									Unfound.Remove(slotName);
								}
								if (!slotsRacesDict.ContainsKey(slotName))
								{
									slotsRacesDict.Add(slotName, new List<string>());
								}
								slotsRacesDict[slotName].Add(compatibleRaces[i]);
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
				if (newDict.ContainsKey(compatibleRaces[i]))
				{
					// The same race listed twice (Standard View allows it): one entry is enough.
					continue;
				}
				if (_compatibleRaceDatas.ContainsKey(compatibleRaces[i]))
                {
                    newDict.Add(compatibleRaces[i], _compatibleRaceDatas[compatibleRaces[i]]);
                }
                else
				{
					var thisRaceData = GetCompatibleRaceData(compatibleRaces[i]);
					if(thisRaceData != null)
                    {
                        newDict.Add(compatibleRaces[i], thisRaceData);
                    }
                }
			}
			_compatibleRaceDatas = newDict;
		}
		//Avoid calling this all the time because its slow
		protected RaceData GetCompatibleRaceData(string raceName)
		{
			/* RaceData foundRace = null; */
			return UMAAssetIndexer.Instance.GetAsset<RaceData>(raceName);
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

			if (compatibleRaces.Count == 0)
			{
				// Nothing to sort by race: keep the stored thumbnails (a fallback thumbnail was cleared
				// just by opening this section).
				newWardrobeThumbs = new List<WardrobeRecipeThumb>(wardrobeThumbs);
			}
			if (compatibleRaces.Count > 0)
			{
				newWardrobeThumbs = BuildRaceThumbs(compatibleRaces, wardrobeThumbs);
				for (int thumbIndex = 0; thumbIndex < newWardrobeThumbs.Count; thumbIndex++)
				{
                    wardrobeThumbsDropDown.Add(RaceThumbLabel(newWardrobeThumbs, compatibleRaces, thumbIndex));
				}
			}

			//GUILayout.Space(10);
			Rect dropArea = new Rect();
			Rect dropAreaBox = new Rect();
			if (compatibleRaces.Count > 0)
			{
				dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f + EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
				dropArea.width = dropArea.width - 105f;
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
                        {
                            warningMsg = compatibleRaces[i] + " is not indexed! Either assign it to an assetBundle or use one of the buttons below to add it to the Scene/Global Library.";
                        }
                        else //the race is missing from the project
                        {
                            warningMsg = compatibleRaces[i] + " could not be found in the project. Have you deleted it?";
                        }

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
							if (GUI.Button(butIndex,"Add to Index", EditorStyles.miniButton))
							{
								// Saves the library alone, not the project's other unsaved assets.
								UMAGlobalLibraryAutoRegistration.AddAndSave(_compatibleRaceDatas[compatibleRaces[i]]);
							}
						}
					}
					// Read only to prevent typos. A scope, not GUI.enabled = true afterwards: that turned
					// the rest of a locked (incompletely loaded) inspector back on.
					using (new EditorGUI.DisabledScope(true))
					{
						EditorGUI.TextField(crfRect, compatibleRaces[i]);
					}
					if (GUI.Button(crfDelRect, "X"))
					{
						newCompatibleRaces.RemoveAt(i);
					}
				}
				Rect labelRect = dropArea;
				labelRect.x = dropArea.width + padding + 20f;
				labelRect.width = 85f;
				labelRect.height = EditorGUIUtility.singleLineHeight;
				labelRect.y = dropArea.y - EditorGUIUtility.singleLineHeight;
				
				Rect DDRect = labelRect;
				Rect SpriteRect = labelRect;
				SpriteRect.height = 75f;
				
				EditorGUI.LabelField(labelRect, "Thumbnail");

				DDRect.y = DDRect.y + EditorGUIUtility.singleLineHeight;
				SpriteRect.y = DDRect.y + EditorGUIUtility.singleLineHeight;
				// One row per thumbnail: one race can have two (its own and the default kept for races
				// without their own), and its own row could not be chosen.
				if (newWardrobeThumbs.Count > 1)
				{
					SpriteRect.y = SpriteRect.y + EditorGUIUtility.singleLineHeight + padding;
					selectedWardrobeThumb = EditorGUI.Popup(DDRect, selectedWardrobeThumb, wardrobeThumbsDropDown.ToArray());
				}
				// One thumbnail per distinct race, so a race listed twice must not reset the selection
				// on every draw.
				if (selectedWardrobeThumb < 0 || selectedWardrobeThumb >= newWardrobeThumbs.Count)
				{
					selectedWardrobeThumb = 0;
				}
				EditorGUI.BeginChangeCheck();
				var thisImg = EditorGUI.ObjectField(SpriteRect, newWardrobeThumbs[selectedWardrobeThumb].thumb, typeof(Sprite), false);
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
			//		public bool thumbnailFromTexture;
		    //public Rect thumbnailRect = new Rect(0.25f, 0.25f, 0.5f, 0.5f);

			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(serializedObject.FindProperty("thumbnailFromTexture"), new GUIContent("Generate from Texture?"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("thumbnailRect"), new GUIContent("Thumbnail Rect (if generating from texture)"));
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				doUpdate = true; 
			}
			return doUpdate;
		}

		protected virtual bool DrawIncompatibleSlots(bool ShowHelp)
		{
			bool doUpdate = false;
			DeletedRecipes.Clear();
			UMAWardrobeRecipe uwr = target as UMAWardrobeRecipe;

			if (uwr == null)
            {
                return false;
            }

            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			showIncompatible = EditorGUILayout.Foldout(showIncompatible, "Incompatible Recipes");
			GUILayout.EndHorizontal();

			if (showIncompatible)
			{
				GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
				if (GUILayout.Button("Add Incompatible Recipe"))
				{
					uwr.IncompatibleRecipes.Add(null);
					doUpdate = true;
				}

				for (int i=0;i<uwr.IncompatibleRecipes.Count;i++)
				{
					UMAWardrobeRecipe u = uwr.IncompatibleRecipes[i];
					GUILayout.BeginHorizontal();
					uwr.IncompatibleRecipes[i] = (UMAWardrobeRecipe)EditorGUILayout.ObjectField(u, typeof(UMAWardrobeRecipe),false);
					if (u != uwr.IncompatibleRecipes[i])
					{
						doUpdate = true;
					}
					if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(24)))
					{
						doUpdate = true;
						DeletedRecipes.Add(u);
					}
					GUILayout.EndHorizontal();
				}

				GUIHelper.EndVerticalPadded(3);
			}

			if (DeletedRecipes.Count > 0)
			{
				uwr.IncompatibleRecipes.Remove(DeletedRecipes[0]);
			}
			if (ShowHelp)
			{
				EditorGUILayout.HelpBox("Incompatible Wardrobe Recipes are recipes that will not work with this specific recipe. It is up to your application to enforce this.", MessageType.Info);
			}
			return doUpdate;
		}

		private bool SelectingSlot;
		private string slotFilter = "";
		private bool ShowHidetags;
		private bool ShowHideBaseSlots;
		private bool ShowSuppressSlots;
		private bool ShowOverrideDNA;
		private ReorderableList hideTagsList = null;

		private RaceData lastRace;
		public int currentDNA = 0;
		private string cachedRace = "";
		private string[] cachedRaceDNA = { };
		private string[] rawcachedRaceDNA = { };
		protected List<MeshHideAsset> DraggedMHA = new List<MeshHideAsset>();
		protected List<MeshModifier> Modifiers = new List<MeshModifier>();
		protected List<MeshHideAssetCollection> DraggedCollections = new List<MeshHideAssetCollection>();

		protected void AddDraggedFiles()
		{
			UMAWardrobeRecipe recipe = target as UMAWardrobeRecipe;
			Undo.RecordObject(target, "Add Mesh Modifications");

			// Each dragged item is checked on its own. Comparing the objects directly also copes with
			// empty entries already in the lists.
			foreach (MeshHideAsset mha in DraggedMHA)
			{
				if (mha != null && !recipe.MeshHideAssets.Contains(mha))
				{
					recipe.MeshHideAssets.Add(mha);
				}
			}
			// Collections are kept as collections, so later changes to a collection reach this recipe.
			foreach (MeshHideAssetCollection mhac in DraggedCollections)
			{
				if (mhac != null && !recipe.MeshHideAssetCollections.Contains(mhac))
				{
					recipe.MeshHideAssetCollections.Add(mhac);
				}
			}
			foreach (MeshModifier mm in Modifiers)
			{
				if (mm != null && !recipe.MeshModifiers.Contains(mm))
				{
					recipe.MeshModifiers.Add(mm);
				}
			}
        }

		protected bool DropAreaGUI(Rect dropArea)
		{
			// Drag events reach a disabled (locked) inspector too.
			if (!GUI.enabled) return false;
			var evt = Event.current;

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
					DraggedMHA.Clear();
					DraggedCollections.Clear();
					DragAndDrop.AcceptDrag();

					UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
					for (int i = 0; i < draggedObjects.Length; i++)
					{
						if (draggedObjects[i] is MeshHideAssetCollection mhac)
						{
							// Add the collection itself, not a copy of its current members.
							DraggedCollections.Add(mhac);
						}
						else if (draggedObjects[i] is MeshHideAsset mha)
						{
							DraggedMHA.Add(mha);
						}
						else if (draggedObjects[i] is MeshModifier mm)
                        {
                            Modifiers.Add(mm);
                        }
						else if (draggedObjects[i] != null)
						{
							// A folder: add what it contains. (This check used to sit inside the
							// MeshHideAsset case, which a folder never is, so folders added nothing.)
							var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
							if (AssetDatabase.IsValidFolder(path))
							{
								RecursiveScanFoldersForAssets(path);
							}
						}
                    }
				}
			}
			return false;
		}

		protected void RecursiveScanFoldersForAssets(string path)
		{
			var assetFiles = System.IO.Directory.GetFiles(path, "*.asset");
			foreach (var assetFile in assetFiles)
			{
				var asset = AssetDatabase.LoadMainAssetAtPath(assetFile.Replace('\\', '/'));
				if (asset is MeshHideAsset mha)
				{
					DraggedMHA.Add(mha);
				}
				else if (asset is MeshHideAssetCollection mhac)
				{
					DraggedCollections.Add(mhac);
				}
				else if (asset is MeshModifier mm)
				{
					Modifiers.Add(mm);
				}
			}
			foreach (var subFolder in System.IO.Directory.GetDirectories(path))
			{
                RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'));
			}
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
			FieldInfo UserField = TargetType.GetField("UserField", BindingFlags.Public | BindingFlags.Instance);
			FieldInfo AppendedField = TargetType.GetField("Appended", BindingFlags.Public | BindingFlags.Instance);
			FieldInfo HideTagsField = TargetType.GetField("HideTags", BindingFlags.Public | BindingFlags.Instance);

			var HideTagsProperty = serializedObject.FindProperty("HideTags");
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
			string userFieldValue = (string)UserField.GetValue(target);
			List<string> hideTags = (List<string>)HideTagsField.GetValue(target);

			UMAWardrobeRecipe recipe = target as UMAWardrobeRecipe;

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
			PreviousValue = userFieldValue;
			userFieldValue = EditorGUILayout.DelayedTextField("User Field", userFieldValue);
			if (userFieldValue != PreviousValue)
			{
				UserField.SetValue(target, userFieldValue);
				doUpdate = true;
			}
			if (ShowHelp)
			{
				EditorGUILayout.HelpBox("User Field is ignored by the system. You can use this to store data that can later be used by your application to provide filtering or categorizing, etc.", MessageType.Info);
			}
			#endregion

			#region Appended
			if (AppendedField != null)
			{
				bool appendedValue = (bool)AppendedField.GetValue(target);
				bool newAppend = EditorGUILayout.Toggle("Is Appended", appendedValue);
				if (newAppend != appendedValue)
				{
					AppendedField.SetValue(target, newAppend);
					doUpdate = true;
				}
			}
			#endregion

			#region Wardrobe Slot UI
			//wardrobeSlot UI
			int selectedWardrobeSlotIndex = GenerateWardrobeSlotsEnum(wardrobeSlot, compatibleRaces, false);
			string newWardrobeSlot;

			List<string> newSuppressWardrobeSlot = new List<string>();
			// When the region list cannot be built, show the stored region and keep it. (Setting it to
			// None here used to change the recipe just by drawing this section.)
			if (selectedWardrobeSlotIndex == -1)
			{
				EditorGUILayout.LabelField("No Compatible Races set. You need to select a compatible race in order to set a wardrobe region");
				EditorGUILayout.LabelField("Wardrobe Region", string.IsNullOrEmpty(wardrobeSlot) ? "None" : wardrobeSlot);
				newWardrobeSlot = wardrobeSlot;
			}
			else if (selectedWardrobeSlotIndex == -2)
			{
				EditorGUILayout.LabelField("Not all compatible races found. Do you have the all correct Race(s) available Locally?");
				EditorGUILayout.LabelField("Wardrobe Region", string.IsNullOrEmpty(wardrobeSlot) ? "None" : wardrobeSlot);
				newWardrobeSlot = wardrobeSlot;
			}
			else
			{
				newWardrobeSlot = wardrobeSlot;
				GUILayout.BeginHorizontal();
				EditorGUILayout.PrefixLabel("Wardrobe Region");
				if (GUILayout.Button(wardrobeSlot, EditorStyles.miniButton))
				{
					SelectingSlot = !SelectingSlot;
				}
				GUILayout.EndHorizontal();

				if (SelectingSlot)
				{
					GUIHelper.BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
					GUILayout.BeginHorizontal();
					slotFilter = EditorGUILayout.TextField("Filter", slotFilter, GUILayout.ExpandWidth(true));
					if (GUILayout.Button("x", GUILayout.Width(15)))
					{
						slotFilter = "";
					}
					GUILayout.EndHorizontal();
					GUILayout.BeginHorizontal();
					GUILayout.Label("Select Wardrobe Region or press cancel");
					if (GUILayout.Button("Cancel", EditorStyles.miniButton, GUILayout.Width(70)))
					{
						SelectingSlot = false;
					}
					GUILayout.EndHorizontal();

					for (int optionIndex = 0; optionIndex < generatedWardrobeSlotOptionsLabels.Count; optionIndex++)
					{
						string lbl = generatedWardrobeSlotOptionsLabels[optionIndex];
						if (!string.IsNullOrEmpty(slotFilter))
						{
							if (!lbl.ToLower().Contains(slotFilter.ToLower()))
							{
								continue;
							}
						}
						GUILayout.BeginHorizontal();
						GUILayout.Space(20);
						if (GUILayout.Button(lbl, EditorStyles.miniButton))
						{
							// Save the race's region key ("Tail"), not the label ("Tail  (Dragon Only)"):
							// characters look the region up by its exact key.
							string region = optionIndex < generatedWardrobeSlotOptions.Count ? generatedWardrobeSlotOptions[optionIndex] : lbl;
							newWardrobeSlot = region;
							WardrobeSlotField.SetValue(target, region);
							doUpdate = true;
							SelectingSlot = false;
						}
						GUILayout.EndHorizontal();
					}
					GUIHelper.EndVerticalPadded(3);
				}
				/*
				int newSelectedWardrobeSlotIndex = EditorGUILayout.Popup("Wardrobe Slot", selectedWardrobeSlotIndex, generatedWardrobeSlotOptionsLabels.ToArray());
				if (newSelectedWardrobeSlotIndex != selectedWardrobeSlotIndex)
				{
					WardrobeSlotField.SetValue(target, generatedWardrobeSlotOptions[newSelectedWardrobeSlotIndex]);
					doUpdate = true;
				}
				newWardrobeSlot = generatedWardrobeSlotOptions.Count > 0 ? generatedWardrobeSlotOptions[selectedWardrobeSlotIndex] : "None";
				*/
			}
			if (ShowHelp)
			{
				EditorGUILayout.HelpBox("Wardrobe Region: This assigns the recipe to a Wardrobe Region. The wardrobe regions are defined on the race. Characters can have only one recipe per Wardrobe Region at a time (unless the recipe is marked cumulative), so for example, adding a 'beard' recipe to a character will replace the existing 'beard' if there is one", MessageType.Info);
			}
			#endregion

			#region Suppress UI
			/*
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
			*/
			if (ShowHelp)
			{
				EditorGUILayout.HelpBox("Suppress: This will stop a different wardrobe region from displaying. For example, if you have a full-length robe assigned to a 'chest' wardrobe region, you would want to suppress whatever is assigned to the 'legs' wardrobe region, so they don't poke through. This is typically used for dresses, robes, and other items that cover multiple body areas.", MessageType.Info);
			}
			#endregion

			#region Hides UI
			//Hides UI
			GenerateBaseSlotsEnum(compatibleRaces, false, hides);
			List<string> newHides = hides != null ? new List<string>(hides) : new List<string>();
			bool hideBaseSlotsChanged = false;

			if (Event.current.commandName == "ObjectSelectorClosed" && EditorGUIUtility.GetObjectPickerControlID() == slotHidePickerID)
			{
				SlotDataAsset sda = EditorGUIUtility.GetObjectPickerObject() as SlotDataAsset;
				if (sda != null && !string.IsNullOrEmpty(sda.slotName))
				{
					if (!newHides.Contains(sda.slotName))
					{
						newHides.Add(sda.slotName);
						hideBaseSlotsChanged = true;
					}
					GenerateBaseSlotsEnum(compatibleRaces, true, newHides);
					Event.current.Use();
				}
			}

			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			ShowHideBaseSlots = EditorGUILayout.Foldout(ShowHideBaseSlots, "Hide Base Slots");
			GUILayout.EndHorizontal();

			if (ShowHideBaseSlots)
			{
				GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

				if (generatedBaseSlotOptions.Count > 0)
				{
					for (int i = 0; i < generatedBaseSlotOptions.Count; i++)
					{
						string slotName = generatedBaseSlotOptions[i];
						string slotLabel = i < generatedBaseSlotOptionsLabels.Count ? generatedBaseSlotOptionsLabels[i] : slotName;
						bool wasHidden = newHides.Contains(slotName);
						bool shouldHide = EditorGUILayout.ToggleLeft(slotLabel, wasHidden);
						if (shouldHide != wasHidden)
						{
							if (shouldHide)
							{
								newHides.Add(slotName);
							}
							else
							{
								newHides.RemoveAll(x => string.Equals(x, slotName, StringComparison.Ordinal));
							}
							hideBaseSlotsChanged = true;
						}
					}
				}
				else
				{
					using (new EditorGUI.DisabledScope(true))
					{
						EditorGUILayout.ToggleLeft("No base slots available", false);
					}
				}

				GUILayout.BeginHorizontal();
				GUILayout.Label("Add Base Slot Asset");
				if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(64)))
				{
					slotHidePickerID = EditorGUIUtility.GetControlID(FocusType.Passive) + 101;
					EditorGUIUtility.ShowObjectPicker<SlotDataAsset>(null, false, "", slotHidePickerID);
				}
				GUILayout.EndHorizontal();

				if (newHides.Count > 0)
				{
					using (new EditorGUI.DisabledScope(true))
					{
						string newHidesResult = String.Join(", ", newHides.ToArray());
						EditorGUILayout.TextField("Selected", newHidesResult);
					}
				}

				GUIHelper.EndVerticalPadded(10);
			}

			if (hideBaseSlotsChanged)
			{
				GenerateBaseSlotsEnum(compatibleRaces, true, newHides);
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
				List<string> ReplacesLabels = new List<string>(ReplacesSlots);
				int selectedIndex = string.IsNullOrEmpty(replaces) ? 0 : ReplacesSlots.IndexOf(replaces);
				if (selectedIndex < 0)
				{
					// The stored slot is not in the base slots found for the compatible races (a race or
					// base recipe may be missing). Keep it as a choice instead of falling back to Nothing,
					// which used to clear the value just by drawing this section.
					ReplacesSlots.Insert(1, replaces);
					ReplacesLabels.Insert(1, replaces + " (not in the base recipes found)");
					selectedIndex = 1;
				}

				EditorGUI.BeginChangeCheck();
				int newIndex = EditorGUILayout.Popup("Replaces", selectedIndex, ReplacesLabels.ToArray());
				if (EditorGUI.EndChangeCheck() && newIndex != selectedIndex)
				{
					ReplacesField.SetValue(target, ReplacesSlots[newIndex]);
					doUpdate = true;
				}
			}

			if (ShowHelp)
			{
				EditorGUILayout.HelpBox("Replaces: This is used to replace part of the base recipe while keeping it's overlays. For example, if you want to replace the head from the base race recipe with a High Poly head, you would 'replace' the head, not hide it. Only one slot can be replaced, and the recipe should only contain one slot.", MessageType.Info);
			}
			#endregion

			#region MeshHideArray
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			showModifiers = EditorGUILayout.Foldout(showModifiers, "Mesh Modifications");
			GUILayout.EndHorizontal();
			if (showModifiers)
			{
				//EditorGUIUtility.LookLikeInspector();
				GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
				GUILayout.Label("Mesh Modifications", EditorStyles.boldLabel);
				SerializedProperty meshHides = serializedObject.FindProperty("MeshHideAssets");
				EditorGUI.BeginChangeCheck();
				GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
				if (GUILayout.Button("Add Mesh Hide Asset"))
				{
					meshHideAssetPickerID = EditorGUIUtility.GetControlID(FocusType.Passive) + 100;
					EditorGUIUtility.ShowObjectPicker<MeshHideAsset>(null, false, "", meshHideAssetPickerID);
				}
				if (GUILayout.Button("Add Mesh Hide Collection"))
				{
					meshHideAssetPickerID = EditorGUIUtility.GetControlID(FocusType.Passive) + 100;
					EditorGUIUtility.ShowObjectPicker<MeshHideAssetCollection>(null, false, "", meshHideAssetPickerID);
				}
				if (GUILayout.Button("Add Mesh Modifier"))
				{
					meshHideAssetPickerID = EditorGUIUtility.GetControlID(FocusType.Passive) + 100;
					EditorGUIUtility.ShowObjectPicker<MeshModifier>(null, false, "", meshHideAssetPickerID);
				}
				GUILayout.EndHorizontal();

				// On close, not on every highlight: browsing the picker must not add each asset looked at.
				if (Event.current.commandName == "ObjectSelectorClosed" && EditorGUIUtility.GetObjectPickerControlID() == meshHideAssetPickerID)
				{
					meshHideAssetPickerID = -1;
					if (recipe != null)
					{
						UnityEngine.Object picked = EditorGUIUtility.GetObjectPickerObject();
						if (picked is MeshHideAssetCollection mhac)
						{
							doUpdate |= AddMeshHideAssetCollection(recipe, mhac);
						}
						else if (picked is MeshHideAsset mha)
						{
							doUpdate |= AddMeshHideAsset(recipe, mha);
						}
						else if (picked is MeshModifier mm)
						{
							doUpdate |= AddMeshModifier(recipe, mm);
						}
					}
				}
				GUILayout.Space(10);
				Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
				GUI.Box(dropArea, "Drag Mesh Hide or Modifier Assets or collections here, or use buttons above to select.");
				if (DropAreaGUI(dropArea))
				{
					EditorUtility.SetDirty(target);
					AssetDatabase.SaveAssetIfDirty(target);
					string path = AssetDatabase.GetAssetPath(target.GetEntityId());
					AssetDatabase.ImportAsset(path);
					Repaint();
				}

				EditorGUILayout.BeginVertical(EditorStyles.helpBox);
				MeshHideAsset deleteme = null;
				bool deleteNulls = false;
				GUILayout.Label("Mesh Hide Assets", EditorStyles.boldLabel);
				int count = 0;
				foreach (MeshHideAsset mha in recipe.MeshHideAssets)
				{
					EditorGUILayout.BeginHorizontal();
					if (mha != null)
					{
						count++;
						GUILayout.Space(10);
						EditorGUILayout.LabelField(mha.name, GUILayout.ExpandWidth(true));
						GUILayout.Label($"[{mha.AssetSlotName}]", GUILayout.Width(90.0f));
						if (GUILayout.Button("ping", GUILayout.Width(65)))
						{
							EditorGUIUtility.PingObject(mha);
						}
						if (GUILayout.Button("Inspect", GUILayout.Width(65)))
						{
							InspectMe.Add(mha);
							//InspectorUtlity.InspectTarget(mha);
						}
						if (GUILayout.Button("X", GUILayout.Width(20.0f)))
						{
							deleteme = mha;
						}
					}
					else
					{
						deleteNulls = true;
					}
					EditorGUILayout.EndHorizontal();
				}
				if (count == 0)
				{
					GUILayout.BeginHorizontal();
					GUILayout.Space(10);
					EditorGUILayout.LabelField("No Mesh Hide Assets", EditorStyles.miniLabel);
					GUILayout.EndHorizontal();
				}


				MeshHideAssetCollection collectiondeleteme = null;
				bool deleteNullCollections = false;
				GUILayout.Label("Mesh Hide Asset Collections", EditorStyles.boldLabel);
				count = 0;
				foreach (MeshHideAssetCollection mhac in recipe.MeshHideAssetCollections)
				{
					EditorGUILayout.BeginHorizontal();
					if (mhac != null)
					{
						count++;
						GUILayout.Space(10);
						EditorGUILayout.LabelField(mhac.name, GUILayout.ExpandWidth(true));
						if (GUILayout.Button("Inspect", GUILayout.Width(65)))
						{
							InspectMe.Add(mhac);
						}
						if (GUILayout.Button("X", GUILayout.Width(20.0f)))
						{
							collectiondeleteme = mhac;
						}
					}
					else
					{
						deleteNullCollections = true;
					}
					EditorGUILayout.EndHorizontal();
				}
				if (count == 0)
				{
					GUILayout.BeginHorizontal();
					GUILayout.Space(10);
					EditorGUILayout.LabelField("No Collections", EditorStyles.miniLabel);
					GUILayout.EndHorizontal();
				}


				GUILayout.Label("Mesh Modifiers", EditorStyles.boldLabel);

				count = 0;
				int delPos = -1;
				int delCount = 0;
				foreach (MeshModifier mm in recipe.MeshModifiers)
				{
					count++;
					EditorGUILayout.BeginHorizontal();
					GUILayout.Space(10);
					if (mm == null)
					{
						EditorGUILayout.LabelField("Null Mesh Modifier", EditorStyles.miniLabel);
					}
					else
					{
						EditorGUILayout.LabelField(mm.name, GUILayout.ExpandWidth(true));
						//GUILayout.Label($"[{mm.SlotName}]", GUILayout.Width(90.0f));
						if (GUILayout.Button("ping", GUILayout.Width(65)))
						{
							EditorGUIUtility.PingObject(mm);
						}
						if (GUILayout.Button("Inspect", GUILayout.Width(65)))
						{
							InspectMe.Add(mm);
							//InspectorUtlity.InspectTarget(mm);
						}
					}
					if (GUILayout.Button("X", GUILayout.Width(20.0f)))
					{

						delPos = delCount;
					}
					EditorGUILayout.EndHorizontal();
					delCount++;
				}

				if (delPos > -1)
				{
					// Report the change so the recipe is saved, as every other edit in this inspector is.
					Undo.RecordObject(target, "Remove Mesh Modifier");
					recipe.MeshModifiers.RemoveAt(delPos);
					EditorUtility.SetDirty(target);
					doUpdate = true;
				}
				if (count == 0)
				{
					GUILayout.BeginHorizontal();
					GUILayout.Space(10);
					EditorGUILayout.LabelField("No Mesh Modifiers", EditorStyles.miniLabel);
					GUILayout.EndHorizontal();
				}





				EditorGUILayout.EndVertical();
				GUIHelper.EndVerticalPadded(10);

				if (deleteNulls == true)
				{
					recipe.MeshHideAssets.RemoveAll(x => x == null);
					EditorUtility.SetDirty(target);
					doUpdate = true;
				}
				if (deleteme != null)
				{
					Undo.RecordObject(target, "Remove Mesh Hide Asset");
					recipe.MeshHideAssets.Remove(deleteme);
					EditorUtility.SetDirty(target);
					doUpdate = true;
				}
				if (deleteNullCollections || collectiondeleteme != null)
				{
					Undo.RecordObject(target, "Remove Mesh Hide Asset Collection");
					if (deleteNullCollections)
					{
						recipe.MeshHideAssetCollections.RemoveAll(x => x == null);
					}
					if (collectiondeleteme != null)
					{
						recipe.MeshHideAssetCollections.Remove(collectiondeleteme);
					}
					EditorUtility.SetDirty(target);
					doUpdate = true;
				}
				// EditorGUILayout.PropertyField(meshHides, true);
				if (EditorGUI.EndChangeCheck())
				{
					serializedObject.ApplyModifiedProperties();
				}
				//EditorGUIUtility.LookLikeControls();
				if (ShowHelp)
				{
					EditorGUILayout.HelpBox("MeshHideAssets: This is a list of advanced mesh hiding assets to hide their corresponding slot meshes on a per triangle basis.", MessageType.Info);
				}
			}
			#endregion

			#region New Suppress UI
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			ShowSuppressSlots = EditorGUILayout.Foldout(ShowSuppressSlots, "Wardrobe Slots to Suppress");
			GUILayout.EndHorizontal();
			bool suppressChanged = false;
			if (ShowSuppressSlots)
			{
				string suppressdelete = "";
				GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
				foreach (string s in suppressWardrobeSlot)
				{
					EditorGUILayout.BeginHorizontal();
					GUILayout.Label(s);
					if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(24)))
					{
						suppressdelete = s;
					}

					EditorGUILayout.EndHorizontal();
				}
				GUILayout.BeginHorizontal();
				selectedsuppressed = EditorGUILayout.Popup("Add Wardrobe Slot", selectedsuppressed, generatedWardrobeSlotOptions.ToArray());
				if (GUILayout.Button("Add Slot", EditorStyles.miniButton, GUILayout.Width(80)))
				{
					if (selectedsuppressed >= 0)
					{
						string newSlot = generatedWardrobeSlotOptions[selectedsuppressed];
						if (!suppressWardrobeSlot.Contains(newSlot))
						{
							suppressWardrobeSlot.Add(newSlot);
							suppressChanged = true;
						}
					}
				}
				GUILayout.EndHorizontal();
				if (suppressdelete != "")
				{
					suppressWardrobeSlot.Remove(suppressdelete);
					suppressdelete = "";
					suppressChanged = true;
				}

				// start box
				// loop through suppressWardrobeSlot
				//    draw item, + "[X]" button.
				//    if (x button); add to delete list
				// end loop
				// show add dropdown/ popup
				// if selected from add dropdown
				//    add to list;
				// end;
				// if any items to delete
				//    delete from list
				//    clear delete list
				// end
				// end box
				GUIHelper.EndVerticalPadded(10);
				// Information: 
				// current list is suppressWardrobeSlot;
				// updated list is newSuppressWardrobeSlot;
				// available items are: generatedWardrobeSlotOptions
			}
			#endregion

			#region Override UI

			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			ShowOverrideDNA = EditorGUILayout.Foldout(ShowOverrideDNA, "Override DNA");
			GUILayout.EndHorizontal();
			if (ShowOverrideDNA)
			{
				if (_compatibleRaceDatas.Count == 0)
				{
					EditorGUILayout.HelpBox("No races set. Please set races before adding override DNA", MessageType.Warning);
				}
				else
				{
					EditorGUILayout.HelpBox("You can add Override DNA that is applied during the build process. It will only be applied while this wardrobe recipe is equipped.", MessageType.Info);

					// Only races that were found can list DNA (a missing race has no entry here).
					List<string> availableRaces = new List<string>(_compatibleRaceDatas.Keys);
					if (currentRace >= availableRaces.Count)
					{
						currentRace = 0;
					}

					EditorGUILayout.BeginHorizontal();
					currentRace = EditorGUILayout.Popup(currentRace, availableRaces.ToArray());
					string raceName = availableRaces[currentRace];

					if (cachedRace != raceName)
					{
						cachedRace = raceName;
						RaceData currentRaceData = _compatibleRaceDatas[raceName];
						rawcachedRaceDNA = currentRaceData.GetDNANames().ToArray();
						List<string> MenuDNA = new List<string>();
						foreach (string s in rawcachedRaceDNA)
						{
							MenuDNA.Add(s.MenuCamelCase());
						}
						cachedRaceDNA = MenuDNA.ToArray();
					}

					if (currentDNA >= rawcachedRaceDNA.Length)
					{
						currentDNA = 0;
					}
					currentDNA = EditorGUILayout.Popup(currentDNA, cachedRaceDNA);
					if (recipe.OverrideDNA == null)
					{
						recipe.OverrideDNA = new UMAPredefinedDNA();
					}
					bool raceHasDna = rawcachedRaceDNA.Length > 0;
					bool addDna;
					using (new EditorGUI.DisabledScope(!raceHasDna))
					{
						addDna = GUILayout.Button(new GUIContent("Add DNA", raceHasDna ? "" : "This race has no DNA names."));
					}
					if (addDna && raceHasDna)
					{
						string theDna = rawcachedRaceDNA[currentDNA];


						if (recipe.OverrideDNA.ContainsName(theDna))
						{
							EditorUtility.DisplayDialog("Error", "Override DNA Already contains DNA: " + theDna, "OK");
						}
						else
						{
							recipe.OverrideDNA.AddDNA(theDna, 0.5f);
							doUpdate = true;
						}
					}
					EditorGUILayout.EndHorizontal();
					string delme = "";
					EditorGUI.BeginChangeCheck();
					foreach (var pd in recipe.OverrideDNA.PreloadValues)
					{
						GUILayout.BeginHorizontal();
						GUILayout.Label(ObjectNames.NicifyVariableName(pd.Name), GUILayout.Width(100));
						//pd.Value = GUILayout.HorizontalSlider(pd.Value, 0.0f, 1.0f);
						pd.Value = EditorGUILayout.Slider(pd.Value, 0.0f, 1.0f);

						bool delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
						if (delete)
						{
							delme = pd.Name;
						}
						GUILayout.EndHorizontal();
					}
					if (!string.IsNullOrEmpty(delme))
					{
						recipe.OverrideDNA.RemoveDNA(delme);
						doUpdate = true;
						Repaint();
					}
					if (EditorGUI.EndChangeCheck())
					{
						doUpdate = true;
					}
				}
			}
			#endregion

			#region HideTags UI
			if (hideTagsList == null)
			{
				hideTagsList = GUIHelper.InitTagsList("HideTags", serializedObject);
			}

			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			ShowHidetags = EditorGUILayout.Foldout(ShowHidetags, "Tags to Hide");
			GUILayout.EndHorizontal();
			if (ShowHidetags)
			{
				GUIHelper.BeginVerticalPadded(10, new Color(0.55f, 0.25f, 0.25f));

				/*
				doUpdate |= DoTagSelector(hideTags);
				if (doUpdate)
				{
					HideTagsProperty.SetValue( hideTags);
					serializedObject.ApplyModifiedProperties();
					serializedObject.Update();
				}
				*/
				EditorGUI.BeginChangeCheck();
				hideTagsList.DoLayoutList();
				GUIHelper.EndVerticalPadded(10);

				if (EditorGUI.EndChangeCheck())
				{
					serializedObject.ApplyModifiedProperties();
					doUpdate = true;
				}
			}
			#endregion

			#region Update
			//Update the values
			if (newWardrobeSlot != wardrobeSlot)
			{
				WardrobeSlotField.SetValue(target, newWardrobeSlot);
				doUpdate = true;
			}
			if (suppressChanged)
			{
				SuppressWardrobeSlotField.SetValue(target, suppressWardrobeSlot);
				doUpdate = true;
			}
			if (!AreListsEqual<string>(newHides, hides))
			{
				HidesField.SetValue(target, newHides);
				doUpdate = true;
			}
			#endregion

			if (DraggedMHA.Count > 0 || Modifiers.Count > 0 || DraggedCollections.Count > 0)
			{
				AddDraggedFiles();
				DraggedMHA.Clear();
				Modifiers.Clear();
				DraggedCollections.Clear();
				return true;
			}


			return doUpdate;
		}

        // The picker adds below change the recipe in memory and report it; the inspector then saves it
        // like any other edit (straight away with Automatic Updates on, on Save Recipe with it off).
        private bool AddMeshModifier(UMAWardrobeRecipe recipe, MeshModifier mm)
        {
            if (mm == null || recipe.MeshModifiers.Contains(mm))
            {
                return false;
            }
            Undo.RecordObject(target, "Add Mesh Modifier");
            recipe.MeshModifiers.Add(mm);
            EditorUtility.SetDirty(target);
            Repaint();
            return true;
        }

        private bool AddMeshHideAssetCollection(UMAWardrobeRecipe recipe, MeshHideAssetCollection mhac)
        {
            if (mhac == null || recipe.MeshHideAssetCollections.Contains(mhac))
            {
                return false;
            }
            Undo.RecordObject(target, "Add Mesh Hide Asset Collection");
            recipe.MeshHideAssetCollections.Add(mhac);
            EditorUtility.SetDirty(target);
            Repaint();
            return true;
        }

        private bool AddMeshHideAsset(UMAWardrobeRecipe recipe, MeshHideAsset mha)
        {
            if (mha == null || recipe.MeshHideAssets.Contains(mha))
            {
                return false;
            }
            Undo.RecordObject(target, "Add Mesh Hide Asset");
            recipe.MeshHideAssets.Add(mha);
            EditorUtility.SetDirty(target);
            Repaint();
            return true;
        }

        /// <summary>
        /// And editor for a WardrobeRecipe that shows sharedColors and Slots but hides the 'raceData' field (because WardrobeRecipes have a 'compatibleRaces' list)
        /// </summary>
        public class WardrobeRecipeMasterEditor : SlotMasterEditor
		{
			List<string> _baseSlotOptions = new List<string>();
			List<string> _baseSlotOptionsLabels = new List<string>();

            public WardrobeRecipeMasterEditor(UMAData.UMARecipe recipe, List<string> baseSlotOptions, List<string> baseSlotOptionsLabels, UnityEngine.Object recipeContext = null) : base(recipe, recipeContext)
			{
                SetBaseSlotOptions(baseSlotOptions, baseSlotOptionsLabels);
			}

            internal void SetBaseSlotOptions(List<string> baseSlotOptions, List<string> baseSlotOptionsLabels)
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

				GUILayout.Space(6);
				Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
				GUI.Box(dropArea, "Drag Slots, Overlays or Recipes here. Click to pick");
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
					var baseAdded = EditorGUILayout.Popup("Add Base slot", 0, baseSlotsNamesList.ToArray());
					if (EditorGUI.EndChangeCheck())
					{
						if (baseAdded != 0)
						{
							var slotName = _baseSlotOptions[baseAdded - 1];
							LastSlot = slotName;
							//we know there should be one because we created a virtual one when we unpacked the recipe if it didn't exist
							var slotToAdd = UMAAssetIndexer.Instance.InstantiateSlot(slotName);
							// The slot goes into the working copy; the recipe is written after this draw, so Undo is
							// recorded now (Add Slot had no Undo entry).
							if (_recipeContext != null)
							{
								Undo.RecordObject(_recipeContext, "Add Base Slot");
							}
							_recipe.MergeSlot(slotToAdd, false);
							changed |= true;
							_dnaDirty |= true;
							_textureDirty |= true;
							_meshDirty |= true;
						}
					}
				}

             GUILayout.BeginHorizontal();
				var added = DrawIndexedSlotPicker("Add Slot");

				if (added != null)
				{
					LastSlot = added.slotName;
					var slot = new SlotData(added);
					if (_recipeContext != null)
					{
						Undo.RecordObject(_recipeContext, "Add Slot");
					}
					_recipe.MergeSlot(slot, false);
					changed |= true;
					_dnaDirty |= true;
					_textureDirty |= true;
					_meshDirty |= true;
				}

				if (GUILayout.Button("Add Placeholder Slot", GUILayout.Width(160)))
				{
					// A new name each time: placeholders with the same name are one slot, so a second press
					// silently gave back the first placeholder.
					string placeholderName = UniquePlaceholderName(_recipe, "NewPlaceholderSlot");
					var placeholder = SlotData.CreatePlaceholder(placeholderName, new string[0]);
					if (_recipeContext != null)
					{
						Undo.RecordObject(_recipeContext, "Add Placeholder Slot");
					}
					_recipe.MergeSlot(placeholder, false);
					LastSlot = placeholderName;
					changed |= true;
					_dnaDirty |= true;
					_textureDirty |= true;
					_meshDirty |= true;
				}
				GUILayout.EndHorizontal();

				GUILayout.Space(20);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Clear Recipe"))
                {
					if (EditorUtility.DisplayDialog("Clear recipe", "Are you sure?", "OK", "Cancel") == true)
					{
						// Undo restores the saved recipe; the inspector reloads it from there.
						if (_recipeContext != null)
						{
							Undo.RecordObject(_recipeContext, "Clear Recipe");
						}
						_recipe.slotDataList = new SlotData[0];
						changed |= true;
						_dnaDirty |= true;
						_textureDirty |= true;
						_meshDirty |= true;
					}
                }
                if (GUILayout.Button("Remove Nulls"))
				{
					if (_recipeContext != null)
					{
						Undo.RecordObject(_recipeContext, "Remove Empty Slots");
					}
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

					if (_slotEditors[i].Slot.isBlendShapeSource)
                        continue;

                    changed |= editor.OnGUI(ref _dnaDirty, ref _textureDirty, ref _meshDirty);

					if (editor.Delete)
					{
						_dnaDirty = true;
						_textureDirty = true;
						_meshDirty = true;

						if (_recipeContext != null)
						{
							Undo.RecordObject(_recipeContext, "Remove Slot");
						}
						_slotEditors.RemoveAt(i);
						RemoveEditedSlot(editor);
						i--;
						changed = true;
					}
				}

				return changed;
			}
		}
    }
}
#endif
