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
		//if we move to having different types for the different kinds of UMATextRecipe (UMAWardrobeRecipe, UMAWardrobeCollection etc) then we will stop displaying this UI element (and just use the value when saving txt recipes)
		public List<string> recipeTypeOpts = new List<string>(new string[] { "Standard", "Wardrobe" });
		protected bool hideToolBar = false;
		protected bool hideRaceField = true;//if true hides the extra race field that we draw *above* the toolbar
		int compatibleRacePickerID = -1;
		int selectedWardrobeThumb = 0;
		List<string> generatedWardrobeSlotOptions = new List<string>();
		List<string> generatedWardrobeSlotOptionsLabels = new List<string>();
		protected List<string> generatedBaseSlotOptions = new List<string>();
		protected List<string> generatedBaseSlotOptionsLabels = new List<string>();

		FieldInfo ActiveWardrobeSetField = null;
		List<WardrobeSettings> activeWardrobeSet = null;

		protected override bool PreInspectorGUI()
		{
			return TextRecipeGUI();
		}

		protected override bool ToolbarGUI()
		{
			//hide the toolbar when its a recipe type that doesn't use DNA (like wardrobe or wardrobeCollection)
			if (hideToolBar)
			{
				return slotEditor.OnGUI(target.name, ref _dnaDirty, ref _textureDirty, ref _meshDirty);
			}
			bool changed = false;
			//the raceData field should really be ABOVE the toolbar, since it defines what the dna will be
			GUILayout.Space(10);
			if (!hideRaceField)
			{
				RaceData newRace = (RaceData)EditorGUILayout.ObjectField("RaceData", _recipe.raceData, typeof(RaceData), false);
				if (_recipe.raceData != newRace)
				{
					_recipe.SetRace(newRace);
					changed = true;
				}
			}
			_toolbarIndex = GUILayout.Toolbar(_toolbarIndex, toolbar);
			_LastToolBar = _toolbarIndex;
			if (dnaEditor != null && slotEditor != null)
				switch (_toolbarIndex)
				{
					case 0:
						if (!dnaEditor.IsValid) return false;
						else if (dnaEditor.OnGUI(ref _dnaDirty, ref _textureDirty, ref _meshDirty))
							return true;
						else
							return changed;
					case 1:
						if (slotEditor.OnGUI(target.name, ref _dnaDirty, ref _textureDirty, ref _meshDirty))
							return true;
						else
							return changed;
				}
			return changed;
		}

		protected bool AreListsEqual<T>(List<T> x, List<T> y)
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

		/// <summary>
		/// Adds a button for adding dna to a newly created UMATextRecipe
		/// </summary>
		/// <returns></returns>
		protected virtual bool AddDNAButtonUI()
		{
			RaceData standardRaceData = null;
			if (_recipe != null)
			{
				standardRaceData = _recipe.raceData;
			}
			if (standardRaceData == null)
				return false;
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
					{
						if(DnaConverter.DNATypeHash != 0)
							couldAddDNA = true;
					}
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
								//the recipe already has the DNAConverter, it just doesn't have the values it requires to show the output in the DNA tab of the recipe
								//_recipe.AddDNAUpdater(DnaConverter);
								Type thisType = DnaConverter.DNAType;
								if (DnaConverter is DynamicDNAConverterBehaviourBase)
								{
									var dna = _recipe.GetOrCreateDna(thisType, DnaConverter.DNATypeHash);
									if (((DynamicDNAConverterBehaviourBase)DnaConverter).dnaAsset != null)
									{
										((DynamicUMADnaBase)dna).dnaAsset = ((DynamicDNAConverterBehaviourBase)DnaConverter).dnaAsset;
									}
								}
								else
								{
									_recipe.GetOrCreateDna(thisType, DnaConverter.DNATypeHash);
								}
							}
						}
					}
				}
			}
			return DNAConvertersAdded;
		}

		protected virtual bool FixDNAConverters()
		{
			RaceData standardRaceData = null;
			if (_recipe != null)
			{
				standardRaceData = _recipe.raceData;
			}
			if (standardRaceData == null)
				return false;

			var currentDNA = _recipe.GetAllDna();
			//we also need current slots because GetAllDna returns a zero length array if _recipe.slotdatalist == null
			SlotData[] currentSlots = _recipe.GetAllSlots();
			bool DNAConvertersModified = false;
			if (currentDNA.Length > 0 && currentSlots != null)
			{
				//check if any DynamicDNA needs its DynamicDNAAsset updating
				var thisDNAConverterList = standardRaceData.dnaConverterList;
				for(int i = 0; i < thisDNAConverterList.Length; i++) /*DnaConverterBehaviour DnaConverter in thisDNAConverterList)*/
				{
					if(thisDNAConverterList[i] == null)
					{
						Debug.LogWarning(standardRaceData.raceName + " RaceData has a missing DNA Converter");
						continue;
					}
					//'Old' UMA DNA will have a typehash based on its type name (never 0) 
					//DynamicDNA will only be zero if the converter does not have a DNA asset assigned (and will show a warning)
					//so if the typeHash is 0 bail
					if (thisDNAConverterList[i].DNATypeHash == 0)
					{
						Debug.LogWarning("Dynamic DNA Converter "+ thisDNAConverterList[i].name+" needs a DNA Asset assigned to it");
                        continue;
					}
					var dna = _recipe.GetOrCreateDna(thisDNAConverterList[i].DNAType, thisDNAConverterList[i].DNATypeHash);
					if (thisDNAConverterList[i] is DynamicDNAConverterBehaviourBase)
					{
						var thisDnaAsset = ((DynamicDNAConverterBehaviourBase)thisDNAConverterList[i]).dnaAsset;
						if (((DynamicUMADnaBase)dna).dnaAsset != thisDnaAsset || ((DynamicUMADnaBase)dna).didDnaAssetUpdate)
						{
							if (((DynamicUMADnaBase)dna).didDnaAssetUpdate)
							{
								Debug.Log("DynamicDNA found a missing asset");
								((DynamicUMADnaBase)dna).didDnaAssetUpdate = false;
								DNAConvertersModified = true;
							}
							else
							{
								//When this happens the values get lost
								((DynamicUMADnaBase)dna).dnaAsset = thisDnaAsset;
								//so we need to try to add any existing dna values to this dna
								int imported = 0;
								for (int j = 0; j < currentDNA.Length; j++)
								{
									if (currentDNA[j].DNATypeHash != dna.DNATypeHash)
									{
										imported += ((DynamicUMADnaBase)dna).ImportUMADnaValues(currentDNA[j]);
                                    }
								}
								Debug.Log("Updated DNA to match DnaConverter " + thisDNAConverterList[i].name + "'s dna asset and imported "+imported+" values from previous dna");
								DNAConvertersModified = true;
							}
						}
					}
				}
				for (int i = 0; i < currentDNA.Length; i++)
				{
					if (_recipe.raceData.GetConverter(currentDNA[i]) == null)
					{
						int dnaToImport = currentDNA[i].Count;
						int dnaImported = 0;

						for (int j = 0; j < currentDNA.Length; j++)
						{
							if (currentDNA[j] is DynamicUMADnaBase)
							{
								// Keep trying to find a new home for DNA values until they have all been set
								dnaImported += ((DynamicUMADnaBase)currentDNA[j]).ImportUMADnaValues(currentDNA[i]);
								if (dnaImported >= dnaToImport)
									break;
							}
						}

						if (dnaImported > 0)
						{
							if(_recipe.GetDna(currentDNA[i].DNATypeHash) != null)
								_recipe.RemoveDna(currentDNA[i].DNATypeHash);
							DNAConvertersModified = true;
						}
					}
				}
				currentDNA = _recipe.GetAllDna();

				//Finally if there are more DNA sets than there are converters we need to remove the dna that should not be there
				if (currentDNA.Length > thisDNAConverterList.Length)
				{
					Debug.Log("There were more dna sets in the recipe than converters. Removing unused Dna...");
					List<UMADnaBase> newCurrentDna = new List<UMADnaBase>();
					for (int i = 0; i < currentDNA.Length; i++)
					{
						bool foundMatch = false;
						for (int ii = 0; ii < thisDNAConverterList.Length; ii++)
						{
							if (thisDNAConverterList[ii].DNATypeHash == currentDNA[i].DNATypeHash)
							{
								newCurrentDna.Add(currentDNA[i]);
								foundMatch = true;
							}
						}
						if (!foundMatch)
						{
							if (_recipe.dnaValues.Contains(currentDNA[i]))
								_recipe.RemoveDna(currentDNA[i].DNATypeHash);
						}
					}
					currentDNA = newCurrentDna.ToArray();
					DNAConvertersModified = true;
				}
			}
			return DNAConvertersModified;
		}

		private bool TextRecipeGUI()
		{
			Type TargetType = target.GetType();//used to get the UMATextRecipe type taher than UMARecipeBase
			bool doUpdate = false;

			if (TargetType.ToString() == "UMA.UMATextRecipe")
			{

				EditorGUI.BeginDisabledGroup(true);

				EditorGUILayout.Popup("Recipe Type", 0, new string[] { "Standard" });//other types (WardrobeRecipe, DynamicCharacterAvatarRecipe) have their own editors now so this is just for UI consistancy

				EditorGUI.EndDisabledGroup();

				if (ActiveWardrobeSetField == null)
					ActiveWardrobeSetField = TargetType.GetField("activeWardrobeSet", BindingFlags.Public | BindingFlags.Instance);
				activeWardrobeSet = (List<WardrobeSettings>)ActiveWardrobeSetField.GetValue(target);
				//draws a button to 'Add DNA' when a new 'standard' recipe is created
				if (AddDNAButtonUI())
				{
					hideToolBar = false;
					return true;
				}
				//fixes dna when the recipes race has updated from UMADnaHumanoid/Tutorial to DynamicDna
				if (FixDNAConverters())
				{
					hideToolBar = false;
					return true;
				}

				//When recipes are saved from a DynamicCharacterAvatar as a 'Standard' rather than 'Optimized' recipe they are saved as 'BackwardsCompatible'
				//This means they have slots/overlay data AND a wardrobeSet. In this case we need to draw the "DynamicCharacterAvatarRecipe' slot editor
				//and this will show an editable Wardrobe set which will update and a slot/overlay list
				if ((activeWardrobeSet.Count > 0))
				{
					hideRaceField = false;
					slotEditor = new WardrobeSetMasterEditor(_recipe, activeWardrobeSet);
				}
				
			}
			return doUpdate;
		}
	}
}
#endif
