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
		List<string> generatedBaseSlotOptions = new List<string>();
		List<string> generatedBaseSlotOptionsLabels = new List<string>();

		protected override bool PreInspectorGUI()
		{
			return TextRecipeGUI();
		}

		protected override bool ToolbarGUI()
		{
			//hide the toolbar when its a recipe type that doesn't use DNA (like wardrobe or wardrobeCollection)
			if (hideToolBar)
			{
				return slotEditor.OnGUI(ref _dnaDirty, ref _textureDirty, ref _meshDirty);
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
						if (slotEditor.OnGUI(ref _dnaDirty, ref _textureDirty, ref _meshDirty))
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
								if (DnaConverter.GetType().ToString().IndexOf("DynamicDNAConverterBehaviour") > -1)
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
				foreach (DnaConverterBehaviour DnaConverter in thisDNAConverterList)
				{
					if (DnaConverter.GetType().ToString().IndexOf("DynamicDNAConverterBehaviour") > -1)
					{
						var thisDnaAsset = ((DynamicDNAConverterBehaviourBase)DnaConverter).dnaAsset;
						var dna = _recipe.GetOrCreateDna(DnaConverter.DNAType, DnaConverter.DNATypeHash);
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
								Debug.Log("Updated DNA to match DnaConverter " + DnaConverter.name + "'s dna asset");
								((DynamicUMADnaBase)dna).dnaAsset = ((DynamicDNAConverterBehaviourBase)DnaConverter).dnaAsset;
								DNAConvertersModified = true;
							}
						}
						if (((DynamicUMADnaBase)dna).DNATypeHash != DnaConverter.DNATypeHash)
						{
							Debug.Log("Updated DNA's typeHash to match DnaConverter " + DnaConverter.name + "'s dnaTypeHash");
							((DynamicUMADnaBase)dna).SetDnaTypeHash(DnaConverter.DNATypeHash);
							DNAConvertersModified = true;
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
							DNAConvertersModified = true;
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
							DNAConvertersModified = true;
						}
					}
					currentDNA = newCurrentDna.ToArray();
				}
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
			Type TargetType = target.GetType();
			bool doUpdate = false;

			if (TargetType.ToString() == "UMATextRecipe" /*|| TargetType.ToString() == "UMAWardrobeRecipe" || TargetType.ToString() == "UMADCSRecipe"*/)
			{
				FieldInfo RecipeTypeField = TargetType.GetField("recipeType", BindingFlags.Public | BindingFlags.Instance);
				//the Recipe Type field defines whether the extra wardrobe recipe fields show and whether we are overriding the SlotMasterEditor with WardrobeSetMasterEditor
				string recipeType = (string)RecipeTypeField.GetValue(target);

				FieldInfo ActiveWardrobeSetField = TargetType.GetField("activeWardrobeSet", BindingFlags.Public | BindingFlags.Instance);
				List<WardrobeSettings> activeWardrobeSet = (List<WardrobeSettings>)ActiveWardrobeSetField.GetValue(target);

				//if this recipeType == WardrobeCollection or DynamicCharacterAvatar or Wardrobe show a 'ConvertRecipe' button and bail/make the recipe uneditable?
				//show this if people have already run the full conversion from the nagger
				if (EditorPrefs.GetBool(Application.dataPath + ":UMADCARecipesUpdated") && EditorPrefs.GetBool(Application.dataPath + ":UMAWardrobeRecipesUpdated"))
				{
					if (recipeType == "WardrobeCollection" || recipeType == "DynamicCharacterAvatar" || recipeType == "Wardrobe")
					{
						//we want this button to convert the UMATextRecipe to the type it should be
						//and then for the resulting asset to be inspected
						MethodInfo ConvertMethod = TargetType.GetMethod("ConvertToType");
						string typeToConvertTo = "";
						if (recipeType == "WardrobeCollection")
						{
							typeToConvertTo = "UMAWardrobeCollection";
						}
						else if (recipeType == "DynamicCharacterAvatar")
						{
							typeToConvertTo = "UMADynamicCharacterAvatarRecipe";
						}
						else if (recipeType == "Wardrobe")
						{
							typeToConvertTo = "UMAWardrobeRecipe";
						}
						//I know this is messy but we can get rid of all of this in the actual release since people wont have made stuff that is wrong
						if (ConvertMethod != null && typeToConvertTo != "")
						{
							EditorGUILayout.HelpBox("Please convert this recipe", MessageType.Warning);
							if (GUILayout.Button("Convert"))
							{
								ConvertMethod.Invoke(target, new object[] { typeToConvertTo });
							}
						}
					}
				}

				//Draw the recipe type dropdown for the time being but disable it for types that cant be changed
				//if people have run the converter from the nagger stop them making UMATextRecipes that are WardrobeRecipes
				if (recipeType == "DynamicCharacterAvatar" || (EditorPrefs.GetBool(Application.dataPath + ":UMADCARecipesUpdated") && EditorPrefs.GetBool(Application.dataPath + ":UMAWardrobeRecipesUpdated")))
				EditorGUI.BeginDisabledGroup(true);

				if (!recipeTypeOpts.Contains(recipeType))
					recipeTypeOpts.Add(recipeType);

				int rtIndex = recipeTypeOpts.IndexOf(recipeType);
				int newrtIndex = EditorGUILayout.Popup("Recipe Type", rtIndex, recipeTypeOpts.ToArray());

				if (newrtIndex != rtIndex)
				{
					RecipeTypeField.SetValue(target, recipeTypeOpts[newrtIndex]);
					doUpdate = true;
				}

				if (recipeType == "DynamicCharacterAvatar" || (EditorPrefs.GetBool(Application.dataPath + ":UMADCARecipesUpdated") && EditorPrefs.GetBool(Application.dataPath + ":UMAWardrobeRecipesUpdated")))
					EditorGUI.EndDisabledGroup();

				//If this is a Standard recipe or a DynamicCharacterAvatar we may need to fix or update the DNA converters
				//This happens when the race the recipe uses has a DNA converter that has been changed from UMADNAHumanoid to DynamicDNA
				if (recipeType == "Standard" || recipeType == "DynamicCharacterAvatar")
				{
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
				}

				//When recipes are saved from a DynamicCharacterAvatar as a 'Standard' rather than 'Optimized' recipe they are saved as 'BackwardsCompatible'
				//This means they have slots/overlay data AND a wardrobeSet. In this case we need to draw the "DynamicCharacterAvatarRecipe' slot editor
				//and this will show an editable Wardrobe set which will update an (uneditable) slot/overlay list
				if ((activeWardrobeSet.Count > 0))
				{
					hideRaceField = false;
					slotEditor = new WardrobeSetMasterEditor(_recipe, activeWardrobeSet);
				}
				//else if its a wardrobe recipe override the slot editor
				else if (recipeType == "Wardrobe")
				{
					hideRaceField = true;
					hideToolBar = true;
					slotEditor = new WardrobeRecipeMasterEditor(_recipe);

					//CompatibleRaces drop area
					if (DrawCompatibleRacesUI(TargetType))
						doUpdate = true;

					//Wardrobe slots dropdowns
					if (DrawWardrobeSlotsFields(TargetType))
						doUpdate = true;

					EditorGUILayout.Space();
				}
			}
			return doUpdate;
		}
	}
}
#endif
