#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;
using UnityEditor.Rendering;

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
					_recipe.ClearDNAConverters();
					changed = true;
				}
			}
			_toolbarIndex = GUILayout.Toolbar(_toolbarIndex, toolbar);
			_LastToolBar = _toolbarIndex;
			if (dnaEditor != null && slotEditor != null)
            {
                switch (_toolbarIndex)
				{
					case 1:
						if (!dnaEditor.IsValid)
                        {
                            return false;
                        }
                        else if (dnaEditor.OnGUI(ref _dnaDirty, ref _textureDirty, ref _meshDirty))
                        {
                            return true;
                        }
                        else
                        {
                            return changed;
                        }

                    case 0:
						if (slotEditor.OnGUI(target.name, ref _dnaDirty, ref _textureDirty, ref _meshDirty))
                        {
                            return true;
                        }
                        else
                        {
                            return changed;
                        }
                }
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
            {
                return false;
            }



            //This enables us to create a new recipe using the Editor menu command but also add DNA to it based on the set race's converters
            var currentDNA = _recipe.GetAllDna();
			//we also need current slots because GetAllDna returns a zero length array if _recipe.slotdatalist == null
			SlotData[] currentSlots = _recipe.GetAllSlots();
			bool couldAddDNA = false;
			bool DNAConvertersAdded = false;
			if (currentDNA.Length == 0 && currentSlots != null)
			{
				var thisDNAConverterList = standardRaceData.dnaConverterList;
				if (thisDNAConverterList != null)
				{
					for (int i = 0; i < thisDNAConverterList.Length; i++)
					{
						IDNAConverter DnaConverter = thisDNAConverterList[i];
						if (DnaConverter != null)
						{
							if (DnaConverter.DNATypeHash != 0)
							{
								couldAddDNA = true;
							}
						}
					}
				}
				if (couldAddDNA || standardRaceData.useNewDNA)
				{
					GUILayout.BeginHorizontal();
					_recipe.raceData = EditorGUILayout.ObjectField("RaceData: ", _recipe.raceData, typeof(RaceData), false) as RaceData;
					if (GUILayout.Button("Add DNA"))
					{
						if (standardRaceData.useNewDNA)
						{
							_recipe.ClearDna();
							standardRaceData.dnaConverterList = new DynamicDNAConverterController[0];
							var dnaInstanceCollection = standardRaceData.DNACollection.GetDefaultDNA(_recipe.raceData);
							_recipe.AddDna(new UMADnaInstance(dnaInstanceCollection));

							return true;
						}
						else
						{
							for (int i = 0; i < thisDNAConverterList.Length; i++)
							{
								IDNAConverter DnaConverter = thisDNAConverterList[i];
								if (DnaConverter != null)
								{
									DNAConvertersAdded = true;
									//the recipe already has the DNAConverter, it just doesn't have the values it requires to show the output in the DNA tab of the recipe
									//_recipe.AddDNAUpdater(DnaConverter);
									Type thisType = DnaConverter.DNAType;
									if (DnaConverter is IDynamicDNAConverter)
									{
										var dna = _recipe.GetOrCreateDna(thisType, DnaConverter.DNATypeHash);
										if (((IDynamicDNAConverter)DnaConverter).dnaAsset != null)
										{
											((DynamicUMADnaBase)dna).dnaAsset = ((IDynamicDNAConverter)DnaConverter).dnaAsset;
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
					GUILayout.EndHorizontal();
				}
			}
			return DNAConvertersAdded;
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
                {
                    ActiveWardrobeSetField = TargetType.GetField("activeWardrobeSet", BindingFlags.Public | BindingFlags.Instance);
                }

                activeWardrobeSet = (List<WardrobeSettings>)ActiveWardrobeSetField.GetValue(target);
				//draws a button to 'Add DNA' when a new 'standard' recipe is created
				if (AddDNAButtonUI())
				{

                    dnaEditor = new DNAMasterEditor(_recipe);
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
