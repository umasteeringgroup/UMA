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
		// Drop area for compatible Races
		private void DropAreaGUI(Rect dropArea, List<string> newcrf)
		{
			Event evt = Event.current;

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
                                if(!newcrf.Contains(tempRaceDataAsset.raceName))
								newcrf.Add(tempRaceDataAsset.raceName);
								continue;
							}
						}
					}
				}
			}
		}

		List<string> generatedWardrobeSlotOptions = new List<string> ();
		//generate new wardrobeSlots dropdown based on Compatible Races
		//what happens if the slot already has a value from when it is created but the end user does not have the appropriate race downloaded?
		//THIS only happens in the editor- at runtime the values are never changed
		//BUT it could still be an issue if multiple developers are working on slots, but they dont all have all the races those slots are compatible with...
		//So if the current value is not found because we could not find all the compatible races DISABLE the dropdown so that the value cannot be changed and show a warning...
		private int GenerateWardrobeSlotsEnum(string selectedOption, List<string> compatibleRaces = null, bool forceUpdate = false){
			int selectedIndex = 0;
			if (compatibleRaces == null) {
				selectedIndex = -1;
				generatedWardrobeSlotOptions = new List<string> (){"None","Face","Hair","Complexion","Eyebrows","Beard","Ears","Helmet","Shoulders","Chest","Arms","Hands","Waist","Legs","Feet"};
			} else {
				if (compatibleRaces.Count == 0) {
					//show the default list? Or say that a compatible race needs to be added?
					selectedIndex = -1;
					generatedWardrobeSlotOptions = new List<string> (){"None","Face","Hair","Complexion","Eyebrows","Beard","Ears","Helmet","Shoulders","Chest","Arms","Hands","Waist","Legs","Feet"};
				} else if (generatedWardrobeSlotOptions.Count == 0 || forceUpdate) {
					List<RaceData> thisRaceDatas = new List<RaceData>();
					for (int i = 0; i < compatibleRaces.Count; i++) {
						thisRaceDatas.Add(GetCompatibleRaceData (compatibleRaces [i]));
					}
					for (int i = 0; i < thisRaceDatas.Count; i++) {
						if (thisRaceDatas[i] != null) {
							List<string> thisWardrobeSlots = thisRaceDatas[i].wardrobeSlots;
							for (int wi = 0; wi < thisWardrobeSlots.Count; wi++) {
								//Slots display as 'Hair (FemaleOnly)' (for example) if the wardrobe slot is only available for one of the compatible races
								//This gets handled below
								if (compatibleRaces.Count > 1 && i > 0) {
									if (!generatedWardrobeSlotOptions.Contains (thisWardrobeSlots [wi])) {
										generatedWardrobeSlotOptions.Insert (wi, thisWardrobeSlots [wi]);
									}
								} else {
									generatedWardrobeSlotOptions.Add (thisWardrobeSlots [wi]);
								}
							}
						} else {
							//Compatible Race is missing
							thisRaceDatas.RemoveAt(i);
							selectedIndex = -2;
						}
					}
					for (int i = 0; i < generatedWardrobeSlotOptions.Count; i++) {
						List<string> onlyIn = new List<string> ();
						for (int ii = 0; ii < thisRaceDatas.Count; ii++) {
							if (thisRaceDatas [ii].wardrobeSlots.Contains (generatedWardrobeSlotOptions [i])) {
								onlyIn.Add (thisRaceDatas [ii].raceName);
							}
						}
						if (onlyIn.Count < thisRaceDatas.Count) {
							//its not in all of them
							generatedWardrobeSlotOptions[i] = generatedWardrobeSlotOptions[i] + "  ("+ String.Join(", ",onlyIn.ToArray())+" only)";
						}
					}
				}
			}
			if (generatedWardrobeSlotOptions.Count > 0) {
				for (int i = 0; i < generatedWardrobeSlotOptions.Count; i++) {
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
                List<UMARecipeBase> thisBaseRecipes = new List<UMARecipeBase>();
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
                            if(slot != null)
                            if (!generatedBaseSlotOptions.Contains(slot.asset.slotName))
                            {
                                generatedBaseSlotOptions.Add(slot.asset.slotName);
                                if(compatibleRaces.Count > 1)
                                {
                                    generatedBaseSlotOptionsLabels.Add(slot.asset.slotName +" ("+ thisBaseRecipes[i].name+")");
                                }
                                else
                                {
                                    generatedBaseSlotOptionsLabels.Add(slot.asset.slotName);
                                }
                            }
                        }
                    }
                }
             }
        }

        private RaceData GetCompatibleRaceData(string raceName){
			RaceData foundRace = null;
			RaceData[] foundRaces;
			string[] foundRacesStrings = AssetDatabase.FindAssets ("t:RaceData");
			for (int i = 0; i < foundRacesStrings.Length; i++) {
				RaceData thisFoundRace = AssetDatabase.LoadAssetAtPath<RaceData> (AssetDatabase.GUIDToAssetPath (foundRacesStrings [i]));
				if (thisFoundRace.raceName == raceName) {
					foundRace = thisFoundRace;
					break;
				}
			}
			if (foundRace == null) {
				foundRaces = Resources.LoadAll<RaceData> ("");
				for (int i = 0; i < foundRaces.Length; i++) {
					if (foundRaces [i].raceName == raceName) {
						foundRace = foundRaces [i];
					}
				}
			}
			if (foundRace == null) {
				//TODO: try looking in assetBundles- But I think this is covered now by DynamicRaceLibrary
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

        public string[] recipeTypeOpts = new string[]{"Standard", "Wardrobe"};
        int wrdSelected = 0;

        private bool TextRecipeGUI()
		{
            float padding = 2f;
			Type TargetType = target.GetType();

			if (TargetType.ToString() == "UMATextRecipe")
			{
                FieldInfo RecipeTypeField;
				FieldInfo CompatibleRacesField;
                FieldInfo WardrobeRecipeThumbsField;
				FieldInfo WardrobeSlotField;
				FieldInfo SuppressWardrobeSlotField;
                FieldInfo HidesField;

                bool doUpdate = false;

                RaceData standardRaceData = null;
                if(_recipe != null)
                {
                    standardRaceData = _recipe.raceData;
                }

                //the Recipe Type field defines whether the extra wardrobe recipe fields show.
                RecipeTypeField = TargetType.GetField("recipeType", BindingFlags.Public|BindingFlags.Instance);

				string rt = (string)RecipeTypeField.GetValue (target);
				int rtIndex = rt == "Standard" ? 0 : 1;
                int newrtIndex = EditorGUILayout.Popup ("Recipe Type", rtIndex, recipeTypeOpts);

                if (newrtIndex != rtIndex)
                    {
                        RecipeTypeField.SetValue(target, recipeTypeOpts[newrtIndex]);
                        doUpdate = true;
                    }


                if(rt == "Standard" && standardRaceData != null)
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
                        bool needsDnaUpdate = false;
                        //first test if there is any UMADnaHumanoid dna
                        for(int i = 0; i < currentDNA.Length; i++)
                        {
                            if (currentDNA[i].GetType().ToString() == "UMA.UMADnaHumanoid")
                            {
                                thisUMADnaHumanoid = i;
                                break;
                            }
                        }
                        if (thisUMADnaHumanoid != -1)
                        {
                            needsDnaUpdate = true;
                            foreach (DnaConverterBehaviour DnaConverter in thisDNAConverterList)
                            {
                                if (DnaConverter.DNAType.ToString() == "UMA.UMADnaHumanoid")
                                {
                                    needsDnaUpdate = false;
                                }
                            }
                        }
                        if (needsDnaUpdate)
                        {
                            //find each DynamicUMADna and try adding the UMADnaHumnoid values to it
                            int dnaImported = 0;
                            List<UMADnaBase> newCurrentDna = new List<UMADnaBase>();
                            for (int i = 0; i < currentDNA.Length; i++)
                            {
                                if (currentDNA[i].GetType().ToString().IndexOf("DynamicUMADna") > -1)
                                {
                                    //keep trying to find a new home for dnavalues until they have all been set
                                    dnaImported +=((DynamicUMADnaBase)currentDNA[i]).ImportUMADnaValues(currentDNA[thisUMADnaHumanoid]);
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
                                currentDNA = newCurrentDna.ToArray();
                                //remove the UMADnaHumanoid from the recipe
                                _recipe.RemoveDna(UMAUtils.StringToHash("UMADnaHumanoid"));
                                DNAConvertersAdded = true;
                            }
                            else
                            {
                                Debug.Log("UMADnaHumanoid Import Failed.");
                            }
                        }
                        //Finally if there are more DNA sets than there are converters we need to remove the dna that should not be there
                        if(currentDNA.Length != thisDNAConverterList.Length)
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
                                //Usually when there are two dna of the same type but with different hashes the one we dont have a converter for has the right values
                                //not sure what to do about that... and maybe it wont happen anyway once I have stopped messing around?
                                if (!foundMatch)
                                {
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
				if (rt == "Wardrobe") {				
					CompatibleRacesField = TargetType.GetField ("compatibleRaces", BindingFlags.Public | BindingFlags.Instance);
                    WardrobeRecipeThumbsField = TargetType.GetField("wardrobeRecipeThumbs", BindingFlags.Public | BindingFlags.Instance);
                    WardrobeSlotField = TargetType.GetField ("wardrobeSlot", BindingFlags.Public | BindingFlags.Instance);
					SuppressWardrobeSlotField = TargetType.GetField ("suppressWardrobeSlots", BindingFlags.Public | BindingFlags.Instance);
					HidesField = TargetType.GetField("Hides", BindingFlags.Public | BindingFlags.Instance);

					List<string> crf = (List<string>)CompatibleRacesField.GetValue (target);
                    List<WardrobeRecipeThumb> wardrobeThumbs = (List<WardrobeRecipeThumb>)WardrobeRecipeThumbsField.GetValue(target);
                    string wsl = (string)WardrobeSlotField.GetValue (target);
					List<string> swsl2 = (List<string>)SuppressWardrobeSlotField.GetValue (target);
                    List<string> hidesList2 = (List<string>)HidesField.GetValue(target);
                    List<WardrobeRecipeThumb> newWardrobeThumbs = new List<WardrobeRecipeThumb>();
                    List<string> wrtdd = new List<string>();
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
                    List<string> newcrf = (List<string>)CompatibleRacesField.GetValue (target);
                    GUILayout.Space (10);
                    Rect dropArea = new Rect();
                    Rect dropAreaBox = new Rect();
                    if (newcrf.Count > 0)
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
                    GUI.Box (dropAreaBox, "Drag Races compatible with this slot here");
					if (newcrf.Count > 0) {
						for (int i = 0; i < newcrf.Count; i++) {
							GUI.enabled = false; //we readonly to prevent typos
                            Rect crfRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight + padding, GUILayout.ExpandWidth(true));
                            crfRect.y = i == 0 ? crfRect.y + padding : crfRect.y + padding + padding;
                            Rect crfDelRect = crfRect;
                            crfRect.width = crfRect.width - 75f - 20f -20f;
                            crfDelRect.width = 20f + padding;
                            crfDelRect.x = crfRect.width + 20f + padding;
                            EditorGUI.TextField (crfRect,newcrf[i]);
							GUI.enabled = true;
							if (GUI.Button (crfDelRect, "X")) {
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
                        if (newcrf.Count > 1)
                        {
                            thumbnailThumbRect.y = thumbnailThumbRect.y + EditorGUIUtility.singleLineHeight + padding;
                            wrdSelected = EditorGUI.Popup(thumbnailDDRect,wrdSelected, wrtdd.ToArray());
                        }
                        if(newWardrobeThumbs.Count != newcrf.Count)
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
                    } else {
						EditorGUILayout.LabelField ("No Compatible Races set. This wardrobe slot will be available to ALL races.");
					}
					DropAreaGUI (dropArea, newcrf);
					GUILayout.Space (10);
					//
					if (newcrf.Count > 0) {
						int selectedWardrobeSlotIndex = GenerateWardrobeSlotsEnum (wsl, crf, false);
						string newwsl;
						int newSuppressFlags = 0;
						List<string> newswsl2 = new List<string> ();
						if (selectedWardrobeSlotIndex == -1) {
							EditorGUILayout.LabelField ("No Compatible Races set. You need to select a compatible race in order to set a wardrobe slot");
							newwsl = "None";
						} else if (selectedWardrobeSlotIndex == -2) {
							EditorGUILayout.LabelField ("Not all compatible races found. Do you have the all correct Race(s) available Locally?");
							newwsl = "None";
						} else {
							int newSelectedWardrobeSlotIndex = EditorGUILayout.Popup ("Wardrobe Slot", selectedWardrobeSlotIndex, generatedWardrobeSlotOptions.ToArray ());
							if (newSelectedWardrobeSlotIndex != selectedWardrobeSlotIndex) {
								WardrobeSlotField.SetValue (target, generatedWardrobeSlotOptions [newSelectedWardrobeSlotIndex]);
                                doUpdate = true;
                            }
							newwsl = generatedWardrobeSlotOptions.Count > 0 ? generatedWardrobeSlotOptions [selectedWardrobeSlotIndex] : "None";
						}
                        //
                        int suppressFlags = 0;
                        for (int i=0; i < generatedWardrobeSlotOptions.Count; i++)
                        {
                            if (swsl2.Contains(generatedWardrobeSlotOptions[i]))
                            {
                                suppressFlags |= 0x1 << i;
                            }
                        }
                        newSuppressFlags = EditorGUILayout.MaskField ("Suppress Wardrobe Slot(s)", suppressFlags, generatedWardrobeSlotOptions.ToArray ());
                        for (int i = 0; i < generatedWardrobeSlotOptions.Count; i++) {
							if ((newSuppressFlags & (1 << i)) == (1 << i)) {
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
                        //
                        if (newcrf != crf) {
							CompatibleRacesField.SetValue (target, newcrf);
							doUpdate = true;
						}
                        if(!AreListsEqual<WardrobeRecipeThumb>(newWardrobeThumbs, wardrobeThumbs)){
                            WardrobeRecipeThumbsField.SetValue(target, newWardrobeThumbs);
                            doUpdate = true;
                        }
                        if (newwsl != wsl) {
							WardrobeSlotField.SetValue (target, newwsl);
							doUpdate = true;
						}
                        if (!AreListsEqual<string>(newswsl2, swsl2)) {
							SuppressWardrobeSlotField.SetValue (target, newswsl2);
							doUpdate = true;
						}
                        if (!AreListsEqual<string>(newhidesList2, hidesList2)) {
                            HidesField.SetValue(target, newhidesList2);
                            doUpdate = true;
                        }
					}

                    /*if (GUILayout.Button ("Save Asset")) { //Not needed now
						return true;
					}*/
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