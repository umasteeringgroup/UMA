#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;    
using System.Reflection;

using UnityEditor;

using UnityEngine;  

using Object = UnityEngine.Object;
using UMA;
using UMA.Integrations;
using UMACharacterSystem;


namespace UMAEditor
{
	public partial class  RecipeEditor
	{

		public override bool PreInspectorGUI()
		{
			return TextRecipeGUI();
		}

		int compatibleRacePickerID = -1;
		// Drop area for compatible Races
		private void DropAreaGUI(Rect dropArea, List<string> newcrf)
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
					AddRaceDataAsset(tempRaceDataAsset, newcrf);
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
								AddRaceDataAsset (tempRaceDataAsset, newcrf);
								continue;
							}

							var path = AssetDatabase.GetAssetPath (draggedObjects [i]);
							if (System.IO.Directory.Exists (path)) 
							{
								RecursiveScanFoldersForAssets (path, newcrf);
							}
						}
					}
				}
			}
		}

		private void RecursiveScanFoldersForAssets(string path, List<string> newcrf)
		{
			var assetFiles = System.IO.Directory.GetFiles (path, "*.asset");
			foreach (var assetFile in assetFiles) 
			{
				var tempRaceDataAsset = AssetDatabase.LoadAssetAtPath (assetFile, typeof(RaceData)) as RaceData;
				if (tempRaceDataAsset) 
				{
					AddRaceDataAsset (tempRaceDataAsset, newcrf);
				}
			}
			foreach (var subFolder in System.IO.Directory.GetDirectories(path)) 
			{
				RecursiveScanFoldersForAssets (subFolder.Replace ('\\', '/'), newcrf);
			}
		}

		private void AddRaceDataAsset(RaceData raceDataAsset, List<string> newcrf)
		{
			if(!newcrf.Contains(raceDataAsset.raceName))
				newcrf.Add (raceDataAsset.raceName);
		}

		List<string> generatedWardrobeSlotOptions = new List<string> ();
		private int GenerateWardrobeSlotsEnum(string selectedOption, List<string> compatibleRaces = null, bool forceUpdate = false)
		{
			int selectedIndex = 0;
			if (compatibleRaces == null)
			{
				selectedIndex = -1;
				generatedWardrobeSlotOptions = new List<string> (){"None","Face","Hair","Complexion","Eyebrows","Beard","Ears","Helmet","Shoulders","Chest","Arms","Hands","Waist","Legs","Feet"};
			}
			else
			{
				if (compatibleRaces.Count == 0)
				{
					selectedIndex = -1;
					generatedWardrobeSlotOptions = new List<string> (){"None","Face","Hair","Complexion","Eyebrows","Beard","Ears","Helmet","Shoulders","Chest","Arms","Hands","Waist","Legs","Feet"};
				}
				else if (generatedWardrobeSlotOptions.Count == 0 || forceUpdate)
				{
					//Clear the list if we are forcing update
					if (forceUpdate)
					{
						generatedWardrobeSlotOptions = new List<string>();
					}
					List<RaceData> thisRaceDatas = new List<RaceData>();
					for (int i = 0; i < compatibleRaces.Count; i++)
					{
						thisRaceDatas.Add(GetCompatibleRaceData (compatibleRaces [i]));
					}
					for (int i = 0; i < thisRaceDatas.Count; i++)
					{
						if (thisRaceDatas[i] != null)
						{
							List<string> thisWardrobeSlots = thisRaceDatas[i].wardrobeSlots;
							for (int wi = 0; wi < thisWardrobeSlots.Count; wi++)
							{
								//WardrobeSlots display as 'Hair (FemaleOnly)' (for example) if the wardrobe slot is only available for one of the compatible races
								if (compatibleRaces.Count > 1 && i > 0)
								{
									if (!generatedWardrobeSlotOptions.Contains (thisWardrobeSlots [wi]))
									{
										generatedWardrobeSlotOptions.Insert (wi, thisWardrobeSlots [wi]);
									}
								}
								else
								{
									generatedWardrobeSlotOptions.Add (thisWardrobeSlots [wi]);
								}
							}
						}
						else
						{
							//Compatible Race is missing
							thisRaceDatas.RemoveAt(i);
							selectedIndex = -2;
						}
					}
					for (int i = 0; i < generatedWardrobeSlotOptions.Count; i++)
					{
						List<string> onlyIn = new List<string> ();
						for (int ii = 0; ii < thisRaceDatas.Count; ii++)
						{
							if (thisRaceDatas [ii].wardrobeSlots.Contains (generatedWardrobeSlotOptions [i]))
							{
								onlyIn.Add (thisRaceDatas [ii].raceName);
							}
						}
						if (onlyIn.Count < thisRaceDatas.Count)
						{
							//its not in all of them
							generatedWardrobeSlotOptions[i] = generatedWardrobeSlotOptions[i] + "  ("+ String.Join(", ",onlyIn.ToArray())+" Only)";
						}
					}
				}
			}
			if (generatedWardrobeSlotOptions.Count > 0)
			{
				for (int i = 0; i < generatedWardrobeSlotOptions.Count; i++)
				{
					if (generatedWardrobeSlotOptions [i] == selectedOption)
						selectedIndex = i;
				}
			}
			return selectedIndex;
		}
        //generate an option list for the BaseSlots that are available to hide for each race so we can make this a mask field too
        List<string> generatedBaseSlotOptions = new List<string>();
        List<string> generatedBaseSlotOptionsLabels = new List<string>();
        private void GenerateBaseSlotsEnum(List<string> compatibleRaces, bool forceUpdate = false)
        {
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
				for (int i = 0; i < compatibleRaces.Count; i++)
                {
                    thisBaseRecipes.Add(GetCompatibleRaceData(compatibleRaces[i]).baseRaceRecipe);
                }
                for (int i = 0; i < thisBaseRecipes.Count; i++)
                {
                    if (thisBaseRecipes[i] != null)
                    {
						UMAData.UMARecipe thisBaseRecipe = thisBaseRecipes[i].GetCachedRecipe(UMAContext.Instance);
						SlotData[] thisSlots = thisBaseRecipe.GetAllSlots();
                        foreach(SlotData slot in thisSlots)
                        {
							if (slot != null)
							{
								if (!generatedBaseSlotOptions.Contains(slot.asset.slotName))
								{
									generatedBaseSlotOptions.Add(slot.asset.slotName);
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

        private RaceData GetCompatibleRaceData(string raceName)
		{
			RaceData foundRace = null;
			string[] foundRacesStrings = AssetDatabase.FindAssets ("t:RaceData");
			for (int i = 0; i < foundRacesStrings.Length; i++)
			{
				RaceData thisFoundRace = AssetDatabase.LoadAssetAtPath<RaceData> (AssetDatabase.GUIDToAssetPath (foundRacesStrings [i]));
				if (thisFoundRace.raceName == raceName)
				{
					foundRace = thisFoundRace;
					break;
				}
			}
			return foundRace;
		}

        private bool AreListsEqual<T>(List<T> x, List<T> y)
        {
            if (x == y)
            {
                return true;
            }
            if (x == null || y == null)
            {
                return false;
            }
            if (x.Count != y.Count)
            {
                return false;
            }
            for (int i = 0; i < x.Count; i++)
            {
                if (!x[i].Equals(y[i]))
                {
                    return false;
                }
            }
            return true;
        }

		//we dont want people to 'create' a recipe of type "DynamicCharacterAvatar" or "WardrobeCollection" (right now)- but if the recipe is one of these we want to show it is
		//when we do have decent UI for the latter two options we will use the recipeTypeOpts enum- till then...
        public List<string> recipeTypeOpts = new List<string>(new string[]{"Standard", "Wardrobe"});
        int wrdSelected = 0;

		//we need a different kind of 'Slot' editor for recipeTypes of "DynamicCharacterAvatar" and "WardrobeSet" that just shows a list of the wardrobe slots for the set race and the recipes that have been assigned to those slots
		public class WardrobeSetMasterEditor : SlotMasterEditor
		{
			private readonly List<WardrobeSettings> _wardrobeSet;
			private bool _foldout = true;

			public WardrobeSetMasterEditor(UMAData.UMARecipe recipe, List<WardrobeSettings> wardrobeSet) : base(recipe)
			{
				_wardrobeSet = wardrobeSet;
			}
			public override bool OnGUI(ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
			{
				bool changed = false;

				RaceData newRace = (RaceData)EditorGUILayout.ObjectField("RaceData", _recipe.raceData, typeof(RaceData), false);
				if (_recipe.raceData != newRace)
				{
					_recipe.SetRace(newRace);
					changed = true;
				}
				GUILayout.Space(20);
				if (_sharedColorsEditor.OnGUI(_recipe))
				{
					changed = true;
				}
				//now we need a UI that shows all the wardrobe slots the given race has as object fields that can have a *compatible* wardrobe Recipe dragged into them
				if(_recipe.raceData.wardrobeSlots.Count > 0)
				{
					var context = UMAContext.FindInstance();
					if (context == null)
					{
						var _errorMessage = "Editing a recipe requires a loaded scene with a valid UMAContext.";
						Debug.LogWarning(_errorMessage);
					}
					GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
					GUILayout.Space(10);
					_foldout = EditorGUILayout.Foldout(_foldout, "Assigned Wardrobe Recipes");
					GUILayout.EndHorizontal();
					if (_foldout)
					{
						GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
						EditorGUILayout.HelpBox("We will make this so you can edit directly in here in future. But for now...", MessageType.Info);
						EditorGUI.BeginDisabledGroup(true);
						foreach (string wsl in _recipe.raceData.wardrobeSlots)
						{
							UMARecipeBase thisAssignedRecipe = null;
							if (_wardrobeSet != null && context != null)
							{
								foreach (WardrobeSettings ws in _wardrobeSet)
								{
									if (ws.slot == wsl)
									{
										thisAssignedRecipe = context.dynamicCharacterSystem.GetBaseRecipe(ws.recipe, true);
									}
								}
							}
							EditorGUILayout.ObjectField(wsl, thisAssignedRecipe, typeof(UMARecipeBase), false);
						}
						EditorGUI.EndDisabledGroup();
						GUIHelper.EndVerticalPadded(10);
					}
				}

				return changed;
			}
		}

		public class WardrobeItemMasterEditor : SlotMasterEditor
		{
            public WardrobeItemMasterEditor(UMAData.UMARecipe recipe): base(recipe)
			{
				
			}
			public override bool OnGUI(ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
			{
				bool changed = false;

				GUILayout.Space(20);

				if (_sharedColorsEditor.OnGUI(_recipe))
				{
					changed = true;
					_textureDirty = true;
				}

				if (GUILayout.Button("Remove Nulls"))//this button should probably be after shared colors no?
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

				GUILayout.Space(20);
				Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
				GUI.Box(dropArea, "Drag Slots here");
				GUILayout.Space(20);


				if (DropAreaGUI(dropArea))
				{
					changed |= true;
					_dnaDirty |= true;
					_textureDirty |= true;
					_meshDirty |= true;
				}

				var added = (SlotDataAsset)EditorGUILayout.ObjectField("Add Slot", null, typeof(SlotDataAsset), false);

				if (added != null)
				{
					var slot = new SlotData(added);
					_recipe.MergeSlot(slot, false);
					changed |= true;
					_dnaDirty |= true;
					_textureDirty |= true;
					_meshDirty |= true;
				}

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

		private bool TextRecipeGUI()
		{
            float padding = 2f;
			Type TargetType = target.GetType();

			if (TargetType.ToString() == "UMATextRecipe" || TargetType.ToString() == "UMAWardrobeRecipe" || TargetType.ToString() == "UMAWardrobeCollection")
			{
                FieldInfo DisplayValueField;
                FieldInfo RecipeTypeField;
				//get the RecipeTypeField now
				//the Recipe Type field defines whether the extra wardrobe recipe fields show and whether we are overriding the SlotMasterEditor with WardrobeSetMasterEditor
				RecipeTypeField = TargetType.GetField("recipeType", BindingFlags.Public | BindingFlags.Instance);
				string recipeType = (string)RecipeTypeField.GetValue(target);
				FieldInfo CompatibleRacesField;
                FieldInfo WardrobeRecipeThumbsField;
				FieldInfo WardrobeSlotField;
				FieldInfo SuppressWardrobeSlotField;
                FieldInfo HidesField;
				FieldInfo ActiveWardrobeSetField;
				//Do we have an activeWardrobeSet?
				ActiveWardrobeSetField = TargetType.GetField("activeWardrobeSet", BindingFlags.Public | BindingFlags.Instance);
				List<WardrobeSettings> activeWardrobeSettings = (List<WardrobeSettings>)ActiveWardrobeSetField.GetValue(target);
				//if we did and the recipe is not "Standard" we dont want to show the slots editor
				//if it is Standard we DO want to show the slots and stuff because thats what Stock UMA will actually load
				if ((activeWardrobeSettings.Count > 0  && recipeType != "Standard") || recipeType == "WardrobeCollection")
				{
					slotEditor = new WardrobeSetMasterEditor(_recipe, activeWardrobeSettings);
				}
				//else if its a wrdrobe recipe we dont want to show the 'race' section because we want that to be based on the 'compatible races' the user has set above
				else if(recipeType == "Wardrobe" || recipeType == "WardrobeItem")
				{
					slotEditor = new WardrobeItemMasterEditor(_recipe);
				}
				bool doUpdate = false;

                RaceData standardRaceData = null;
                if(_recipe != null)
                {
                    standardRaceData = _recipe.raceData;
                }
				//Here if the recipeType is DynamicCharacterAvatar we want to show that option- but we dont want (yet) for people to make DynamicCharacterAvatar recipes from scratch
				if (!recipeTypeOpts.Contains(recipeType))
					recipeTypeOpts.Add(recipeType);
				int rtIndex = recipeTypeOpts.IndexOf(recipeType);
                int newrtIndex = EditorGUILayout.Popup ("Recipe Type", rtIndex, recipeTypeOpts.ToArray());

                if (newrtIndex != rtIndex)
                    {
                        RecipeTypeField.SetValue(target, recipeTypeOpts[newrtIndex]);
                        doUpdate = true;
                    }


                if(recipeType == "Standard" && standardRaceData != null)
                {
                    //This enables us to create a new recipe using the Editor menu command but also add DNA to it based on the set race's converters
                    var currentDNA = _recipe.GetAllDna();
                    //we also need current slots because GetAllDna returns a zero length array if _recipe.slotdatalist == null
                    SlotData[] currentSlots = _recipe.GetAllSlots();
                    bool couldAddDNA = false;
                    bool DNAConvertersAdded = false;
                    if (currentDNA.Length == 0 && currentSlots != null)
                    {
                        var thisDNAConverterList = standardRaceData.dnaConverterList;
                        foreach (DnaConverterBehaviour DnaConverter in thisDNAConverterList)
                        {
                            if (DnaConverter != null)
                                couldAddDNA = true;
                        }
                        if (couldAddDNA)
                        {
                            if (GUILayout.Button("Add DNA"))
                            {
                                foreach (DnaConverterBehaviour DnaConverter in thisDNAConverterList)
                                {
                                    if (DnaConverter != null)
                                    {
                                        DNAConvertersAdded = true;
                                        _recipe.AddDNAUpdater(DnaConverter);
                                        Type thisType = DnaConverter.DNAType;
                                        if(DnaConverter.GetType().ToString().IndexOf("DynamicDNAConverterBehaviour") > -1)
                                        {
                                            var dna = _recipe.GetOrCreateDna(thisType, DnaConverter.GetDnaTypeHash());
                                            if(((DynamicDNAConverterBehaviourBase)DnaConverter).dnaAsset != null)
                                            {
                                                ((DynamicUMADnaBase)dna).dnaAsset = ((DynamicDNAConverterBehaviourBase)DnaConverter).dnaAsset;
                                            }
                                        }
                                        else
                                        {
                                            _recipe.GetOrCreateDna(thisType, DnaConverter.GetDnaTypeHash());
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else if(currentDNA.Length > 0 && currentSlots != null)
                    {
                        //check if any DynamicDNA needs its DynamicDNAAsset updating
                        var thisDNAConverterList = standardRaceData.dnaConverterList;
                        foreach (DnaConverterBehaviour DnaConverter in thisDNAConverterList)
                        {
                            if (DnaConverter.GetType().ToString().IndexOf("DynamicDNAConverterBehaviour") > -1)
                            {
                                var thisDnaAsset = ((DynamicDNAConverterBehaviourBase)DnaConverter).dnaAsset;
                                var dna = _recipe.GetOrCreateDna(DnaConverter.DNAType, DnaConverter.GetDnaTypeHash());
                                if (((DynamicUMADnaBase)dna).dnaAsset != thisDnaAsset || ((DynamicUMADnaBase)dna).didDnaAssetUpdate)
                                {
                                    if (((DynamicUMADnaBase)dna).didDnaAssetUpdate)
                                    {
                                        Debug.Log("DynamicDNA found a missing asset");
                                        ((DynamicUMADnaBase)dna).didDnaAssetUpdate = false;
                                        DNAConvertersAdded = true;
                                    }
                                    else
                                    {
                                        Debug.Log("Updated DNA to match DnaConverter " + DnaConverter.name + "'s dna asset");
                                        ((DynamicUMADnaBase)dna).dnaAsset = ((DynamicDNAConverterBehaviourBase)DnaConverter).dnaAsset;
                                        DNAConvertersAdded = true;
                                    }
                                }
                                if(((DynamicUMADnaBase)dna).dnaTypeHash != DnaConverter.GetDnaTypeHash())
                                {
                                    Debug.Log("Updated DNA's typeHash to match DnaConverter " + DnaConverter.name + "'s dnaTypeHash");
                                    ((DynamicUMADnaBase)dna).SetDnaTypeHash(DnaConverter.GetDnaTypeHash());
                                    DNAConvertersAdded = true;
                                }
                            }
                        }
                        //Also if the user has switched a race to use DynamicConverter/DynamicDNA the recipe will contain DNA values for UMADNAHumanoid
                        //In that case these values need to be converted to DynamicDna values
                        int thisUMADnaHumanoid = -1;
						int thisUMADnaTutorial = -1;
						bool needsHumanoidDnaUpdate = false;
						bool needsTutorialDnaUpdate = false;
						//first test if there is any UMADnaHumanoid dna
						for (int i = 0; i < currentDNA.Length; i++)
                        {
                            if (currentDNA[i].GetType().ToString() == "UMA.UMADnaHumanoid")
                            {
                                thisUMADnaHumanoid = i;
								needsHumanoidDnaUpdate = true;
							}
							if (currentDNA[i].GetType().ToString() == "UMA.UMADnaTutorial")
							{
								thisUMADnaTutorial = i;
								needsTutorialDnaUpdate = true;
							}
						}
                        if (thisUMADnaHumanoid != -1 || thisUMADnaTutorial != -1)
                        {
							//If there actually still is a 'old style' converter in the race we dont need to update to dynamicDNA
							foreach (DnaConverterBehaviour DnaConverter in thisDNAConverterList)
                            {
								if (DnaConverter.DNAType.ToString() == "UMA.UMADnaHumanoid")
								{
									needsHumanoidDnaUpdate = false;
								}
								if (DnaConverter.DNAType.ToString() == "UMA.UMADnaTutorial")
								{
									needsTutorialDnaUpdate = false;
								}
							}
                        }
						if (needsHumanoidDnaUpdate || needsTutorialDnaUpdate)
						{
							List<UMADnaBase> newCurrentDna = new List<UMADnaBase>();
							if (needsHumanoidDnaUpdate)
							{
								//find each DynamicUMADna and try adding the UMADnaHumnoid values to it
								int dnaImported = 0;
								for (int i = 0; i < currentDNA.Length; i++)
								{
									if (currentDNA[i].GetType().ToString().IndexOf("DynamicUMADna") > -1)
									{
										//keep trying to find a new home for dnavalues until they have all been set
										dnaImported += ((DynamicUMADnaBase)currentDNA[i]).ImportUMADnaValues(currentDNA[thisUMADnaHumanoid]);
										if (dnaImported >= currentDNA[thisUMADnaHumanoid].Values.Length)
											break;
									}
								}
								if (dnaImported > 0)//we say greater than 0 because we want to get rid of Humanoid even if all the values did not cross over
								{
									Debug.Log("UMADnaHumanoid imported successfully");
									//remove the UMADnaHumanoid from current DNA
									for (int i = 0; i < currentDNA.Length; i++)
									{
										if (i != thisUMADnaHumanoid)
											newCurrentDna.Add(currentDNA[i]);
									}
									//remove the UMADnaHumanoid from the recipe
									_recipe.RemoveDna(UMAUtils.StringToHash("UMADnaHumanoid"));
									DNAConvertersAdded = true;
								}
							}
							if (needsTutorialDnaUpdate)
							{
								//find each DynamicUMADna and try adding the UMADnaHumnoid values to it
								int dnaImported = 0;
								for (int i = 0; i < currentDNA.Length; i++)
								{
									if (currentDNA[i].GetType().ToString().IndexOf("DynamicUMADna") > -1)
									{
										//keep trying to find a new home for dnavalues until they have all been set
										dnaImported += ((DynamicUMADnaBase)currentDNA[i]).ImportUMADnaValues(currentDNA[thisUMADnaTutorial]);
										if (dnaImported >= currentDNA[thisUMADnaTutorial].Values.Length)
											break;
									}
								}
								if (dnaImported > 0)//we say greater than 0 because we want to get rid of Tutorial even if all the values did not cross over
								{
									Debug.Log("UMADnaTutorial imported successfully");
									//remove the UMADnaHumanoid from current DNA
									for (int i = 0; i < currentDNA.Length; i++)
									{
										if (i != thisUMADnaTutorial)
											newCurrentDna.Add(currentDNA[i]);
									}
									//remove the UMADnaTutorial from the recipe
									_recipe.RemoveDna(UMAUtils.StringToHash("UMADnaTutorial"));
									DNAConvertersAdded = true;
								}
							}
							currentDNA = newCurrentDna.ToArray();
						}
                        //Finally if there are more DNA sets than there are converters we need to remove the dna that should not be there
                        if(currentDNA.Length > thisDNAConverterList.Length)
                        {
                            Debug.Log("There were more dna sets in the recipe than converters. Removing unused Dna...");
                            List<UMADnaBase> newCurrentDna = new List<UMADnaBase>();
                            for (int i = 0; i < currentDNA.Length; i++)
                            {
                                bool foundMatch = false;
                                for (int ii = 0; ii < thisDNAConverterList.Length; ii++)
                                {
                                    if(thisDNAConverterList[ii].GetDnaTypeHash() == currentDNA[i].GetDnaTypeHash())
                                    {
                                        newCurrentDna.Add(currentDNA[i]);
                                        foundMatch = true;
                                    }
                                }
                                if (!foundMatch)
                                {
									if (_recipe.dnaValues.Contains(currentDNA[i]))
										_recipe.RemoveDna(currentDNA[i].GetDnaTypeHash());
                                }
                            }
                            currentDNA = newCurrentDna.ToArray();
                            DNAConvertersAdded = true;
                        }
                    }
                    if (DNAConvertersAdded)
                    {
                        return true;
                    }
                }
				//TODO Test the consequences of wardrobe slots having DNA when perhaps they should not
				if (recipeType == "Wardrobe" || recipeType == "WardrobeCollection")
				{

					CompatibleRacesField = TargetType.GetField ("compatibleRaces", BindingFlags.Public | BindingFlags.Instance);
                    WardrobeRecipeThumbsField = TargetType.GetField("wardrobeRecipeThumbs", BindingFlags.Public | BindingFlags.Instance);
                    WardrobeSlotField = TargetType.GetField ("wardrobeSlot", BindingFlags.Public | BindingFlags.Instance);
					SuppressWardrobeSlotField = TargetType.GetField ("suppressWardrobeSlots", BindingFlags.Public | BindingFlags.Instance);
					HidesField = TargetType.GetField("Hides", BindingFlags.Public | BindingFlags.Instance);
                    DisplayValueField = TargetType.GetField("DisplayValue",BindingFlags.Public | BindingFlags.Instance);

					List<string> crf = (List<string>)CompatibleRacesField.GetValue (target);
                    List<WardrobeRecipeThumb> wardrobeThumbs = (List<WardrobeRecipeThumb>)WardrobeRecipeThumbsField.GetValue(target);
                    string wsl = (string)WardrobeSlotField.GetValue (target);
					List<string> swsl2 = (List<string>)SuppressWardrobeSlotField.GetValue (target);
                    List<string> hidesList2 = (List<string>)HidesField.GetValue(target);
                    List<WardrobeRecipeThumb> newWardrobeThumbs = new List<WardrobeRecipeThumb>();
                    List<string> wrtdd = new List<string>();
                    string DisplayValue = (string)DisplayValueField.GetValue(target);

					if (crf.Count > 0)
                    {
                        foreach (string cr in crf)
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
                            wrtdd.Add(wrt.race);
                        }
                    }
					List<string> newcrf = new List<string>(crf);
					GUILayout.Space (10);
                    Rect dropArea = new Rect();
                    Rect dropAreaBox = new Rect();
                    if (crf.Count > 0)
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
                    GUI.Box (dropAreaBox, "Drag Races compatible with this Recipe here. Click to pick.");
					if (crf.Count > 0)
					{
						for (int i = 0; i < crf.Count; i++)
						{
							GUILayout.Space(padding);
							GUI.enabled = false; //we readonly to prevent typos
                            Rect crfRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                            Rect crfDelRect = crfRect;
                            crfRect.width = crfRect.width - 75f - 20f -20f;
                            crfDelRect.width = 20f + padding;
                            crfDelRect.x = crfRect.width + 20f + padding;
                            EditorGUI.TextField (crfRect, crf[i]);
							GUI.enabled = true;
							if (GUI.Button (crfDelRect, "X"))
							{
								newcrf.RemoveAt (i);
							}
						}
                        Rect thumbnailRect = dropArea;
                        thumbnailRect.x = dropArea.width + padding + 20f;
                        thumbnailRect.width = 75f;
                        thumbnailRect.y = thumbnailRect.y - 3f;
                        Rect thumbnailDDRect = thumbnailRect;
                        Rect thumbnailThumbRect = thumbnailRect;
                        thumbnailThumbRect.height = 75f;
                        EditorGUI.LabelField(thumbnailRect, "Thumbnail");
                        thumbnailDDRect.y = thumbnailDDRect.y + EditorGUIUtility.singleLineHeight;
                        thumbnailThumbRect.y = thumbnailThumbRect.y + EditorGUIUtility.singleLineHeight;
                        if (crf.Count > 1)
                        {
                            thumbnailThumbRect.y = thumbnailThumbRect.y + EditorGUIUtility.singleLineHeight + padding;
                            wrdSelected = EditorGUI.Popup(thumbnailDDRect,wrdSelected, wrtdd.ToArray());
                        }
                        if(newWardrobeThumbs.Count != crf.Count)
                        {
                            wrdSelected = 0;
                        }
                        EditorGUI.BeginChangeCheck();
                        var thisImg = EditorGUI.ObjectField(thumbnailThumbRect, newWardrobeThumbs[wrdSelected].thumb, typeof(Sprite), false);
                        if (EditorGUI.EndChangeCheck())
                        {
                            if(thisImg != newWardrobeThumbs[wrdSelected].thumb)
                            {
                                newWardrobeThumbs[wrdSelected].thumb = (Sprite)thisImg;
                                doUpdate = true;
                            }
                        }
                    }
					else
					{
						EditorGUILayout.LabelField ("No Compatible Races set. This wardrobe slot will be available to ALL races.");
					}
					DropAreaGUI (dropArea, newcrf);
					GUILayout.Space (10);
					//Wardrobe only fields
					if (crf.Count > 0 && recipeType == "Wardrobe")
					{
						int selectedWardrobeSlotIndex = GenerateWardrobeSlotsEnum (wsl, crf, false);
						string newwsl;
						int newSuppressFlags = 0;
						List<string> newswsl2 = new List<string> ();
						if (selectedWardrobeSlotIndex == -1)
						{
							EditorGUILayout.LabelField ("No Compatible Races set. You need to select a compatible race in order to set a wardrobe slot");
							newwsl = "None";
						}
						else if (selectedWardrobeSlotIndex == -2)
						{
							EditorGUILayout.LabelField ("Not all compatible races found. Do you have the all correct Race(s) available Locally?");
							newwsl = "None";
						}
						else
						{
							int newSelectedWardrobeSlotIndex = EditorGUILayout.Popup ("Wardrobe Slot", selectedWardrobeSlotIndex, generatedWardrobeSlotOptions.ToArray ());
							if (newSelectedWardrobeSlotIndex != selectedWardrobeSlotIndex)
							{
								WardrobeSlotField.SetValue (target, generatedWardrobeSlotOptions [newSelectedWardrobeSlotIndex]);
                                doUpdate = true;
                            }
							newwsl = generatedWardrobeSlotOptions.Count > 0 ? generatedWardrobeSlotOptions [selectedWardrobeSlotIndex] : "None";
						}
                        //
                        string PreviousValue = DisplayValue;
                           
                        DisplayValue = EditorGUILayout.TextField("Display Value", DisplayValue);
                        if (DisplayValue != PreviousValue)
                        {
                            DisplayValueField.SetValue(target, DisplayValue);
                            doUpdate = true;
                        }


                        int suppressFlags = 0;
                        for (int i=0; i < generatedWardrobeSlotOptions.Count; i++)
                        {
                            if (swsl2.Contains(generatedWardrobeSlotOptions[i]))
                            {
                                suppressFlags |= 0x1 << i;
                            }
                        }
                        newSuppressFlags = EditorGUILayout.MaskField ("Suppress Wardrobe Slot(s)", suppressFlags, generatedWardrobeSlotOptions.ToArray ());
                        for (int i = 0; i < generatedWardrobeSlotOptions.Count; i++)
						{
							if ((newSuppressFlags & (1 << i)) == (1 << i))
							{
								newswsl2.Add (generatedWardrobeSlotOptions [i]);
							}
						}
                        //I added this because I dont like not being able to see what the actual values of a mask field are when it shows 'Mixed...'
                        if(newswsl2.Count > 1)
                        {
                            GUI.enabled = false;
                            string swsl2Result = String.Join(", ", newswsl2.ToArray());
                            EditorGUILayout.TextField(swsl2Result);
                            GUI.enabled = true;
                        }

                        //New HiddenBaseSots Mask dropdown
                        GenerateBaseSlotsEnum(crf, false);
                        int hiddenBaseFlags = 0;
                        List<string> newhidesList2 = new List<string>();
                        for (int i=0; i < generatedBaseSlotOptions.Count; i++)
                        {
                            if (hidesList2.Contains(generatedBaseSlotOptions[i]))
                            {
                                hiddenBaseFlags |= 0x1 << i;
                            }
                        }
                        int newHiddenBaseFlags = 0;
                        newHiddenBaseFlags = EditorGUILayout.MaskField("Hides Base Slot(s)", hiddenBaseFlags, generatedBaseSlotOptionsLabels.ToArray());
                        for (int i = 0; i < generatedBaseSlotOptionsLabels.Count; i++)
                        {
                            if ((newHiddenBaseFlags & (1 << i)) == (1 << i))
                            {
                                newhidesList2.Add(generatedBaseSlotOptions[i]);
                            }
                        }
                        //I added this because I dont like not being able to see what the actual values of a mask field are when it shows 'Mixed...'
                        if (newhidesList2.Count > 1)
                        {
                            GUI.enabled = false;
                            string newhidesList2Result = String.Join(", ", newhidesList2.ToArray());
                            EditorGUILayout.TextField(newhidesList2Result);
                            GUI.enabled = true;
                        }
                        if (newwsl != wsl)
						{
							WardrobeSlotField.SetValue (target, newwsl);
							doUpdate = true;
						}
                        if (!AreListsEqual<string>(newswsl2, swsl2))
						{
							SuppressWardrobeSlotField.SetValue (target, newswsl2);
							doUpdate = true;
						}
                        if (!AreListsEqual<string>(newhidesList2, hidesList2))
						{
                            HidesField.SetValue(target, newhidesList2);
                            doUpdate = true;
                        }
						//if the compatible races has changed we need to regenerate the enums
						if (!AreListsEqual<string>(newcrf, crf))
						{
							//If the libraries cannot load the raceBaseRecipe because of missing slots/overlays
							//we dont want to actually change anything and need to show an error- but still show the recipe as it was
							try
							{
								GenerateBaseSlotsEnum(newcrf, true);
							}
							catch (UMAResourceNotFoundException e)
							{
								newcrf = new List<string>(crf);
								Debug.LogError("The Recipe Editor could not add the selected compatible race because some required assets could not be found: " + e.Message);
							}
							GenerateWardrobeSlotsEnum(newwsl, newcrf, true);
						}
					}
					if (!AreListsEqual<string>(newcrf, crf))
					{
						CompatibleRacesField.SetValue(target, newcrf);
						doUpdate = true;
					}
					if (!AreListsEqual<WardrobeRecipeThumb>(newWardrobeThumbs, wardrobeThumbs))
					{
						WardrobeRecipeThumbsField.SetValue(target, newWardrobeThumbs);
						doUpdate = true;
					}
					EditorGUILayout.Space();
				}
                if (doUpdate == true)
                {
                    return true;
                }
            }
			return false;
		}
	}
}
#endif
