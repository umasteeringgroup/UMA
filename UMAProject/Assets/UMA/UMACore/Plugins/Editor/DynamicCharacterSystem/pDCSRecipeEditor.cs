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
	//unfortunately this needs to be here if we are going to make it possible to have 'Backewards Compatible' DCA recipes (i.e. saved as 'Standard' but with a wardrobeSet)
	//if we removed that functionality this could all go into UMADynamicCharacterAvatarRecipeEditor
	public partial class RecipeEditor
	{
		/// <summary>
		/// Draws a popup containing the available Wardrobe recipes for a particular race for a particular wardrobe slot
		/// </summary>
		public class WardrobeSlotRecipePopup
		{
			private string _wsRace;
			private string _wsSlot;
			private string _wsRecipeName;

			public string RecipeName
			{
				get
				{
					return _wsRecipeName;
				}
			}
			public WardrobeSlotRecipePopup(string race, string slot, string recipeName)
			{
				_wsRace = race;
				_wsSlot = slot;
				_wsRecipeName = recipeName;
			}
			public bool OnGUI()
			{
				bool changed = false;
				var context = UMAContext.FindInstance();
				if (context == null)
				{
					var _errorMessage = "Editing a recipe requires a loaded scene with a valid UMAContext.";
					Debug.LogWarning(_errorMessage);
				}
				var recipesForRaceSlot = context.dynamicCharacterSystem.GetRecipeNamesForRaceSlot(_wsRace, _wsSlot);
				List<string> thisPopupVals = new List<string>();
				thisPopupVals.Add("None");
				thisPopupVals.AddRange(recipesForRaceSlot);
				var selected = 0;
				if (_wsRecipeName != "")
				{
					selected = thisPopupVals.IndexOf(_wsRecipeName);
					if (selected == -1)
					{
						selected = thisPopupVals.Count;
						string missingOrIncompatible = "missing";
						if (context.dynamicCharacterSystem.GetBaseRecipe(_wsRecipeName, false) != null)
							missingOrIncompatible = "incompatible";
						thisPopupVals.Add(_wsRecipeName + " (" + missingOrIncompatible + ")");
					}
				}
				var newSelected = selected;
				EditorGUI.BeginChangeCheck();
				newSelected = EditorGUILayout.Popup(_wsSlot, selected, thisPopupVals.ToArray());
				if (EditorGUI.EndChangeCheck())
				{
					if (newSelected != selected)
					{
						changed = true;
						_wsRecipeName = (thisPopupVals[newSelected].IndexOf("(missing)") == -1 && thisPopupVals[newSelected].IndexOf("(incompatible)") == -1) ? (thisPopupVals[newSelected] != "None" ? thisPopupVals[newSelected] : "") : _wsRecipeName.Replace("(missing)", "").Replace("(incompatible)", "");
					}
				}
				return changed;
			}
		}

		/// <summary>
		/// Draws an editor for a Wardrobe set which displays a list of popups listing all the possible recipes that could be set for any given wardrobe slot for the given race
		/// </summary>
		public class WardrobeSetEditor
		{
			private readonly UMAData.UMARecipe _recipe;
			private readonly List<WardrobeSettings> _wardrobeSet;
			private readonly RaceData _race;
			private readonly bool _allowWardrobeCollectionSlot = true;

			public List<WardrobeSettings> WardrobeSet
			{
				get
				{
					return _wardrobeSet;
				}
			}
			public WardrobeSetEditor(RaceData race, List<WardrobeSettings> wardrobeSet, UMAData.UMARecipe recipe, bool allowWardrobeCollectionSlot)
			{
				_recipe = recipe;
				_wardrobeSet = wardrobeSet;
				_race = race;
				_allowWardrobeCollectionSlot = allowWardrobeCollectionSlot;
			}
			public bool OnGUI()
			{
				bool changed = false;
				if (_race != null)
					if (_race.wardrobeSlots.Count > 0)
					{
						var context = UMAContext.FindInstance();
						if (context == null)
						{
							var _errorMessage = "Editing a recipe requires a loaded scene with a valid UMAContext.";
							Debug.LogWarning(_errorMessage);
						}

						if (_wardrobeSet == null || context == null)
							return false;
						GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
						foreach (string wsl in _race.wardrobeSlots)
						{
							if (wsl == "None")
								continue;

							if (wsl == "FullOutfit" && _allowWardrobeCollectionSlot == false)
								continue;

							WardrobeSlotRecipePopup thisPicker = null;
							bool assignedPicker = false;
							for (int wsi = 0; wsi < _wardrobeSet.Count; wsi++)
							{
								if (_wardrobeSet[wsi].slot == wsl)
								{
									thisPicker = new WardrobeSlotRecipePopup(_race.raceName, wsl, _wardrobeSet[wsi].recipe);
									assignedPicker = true;
									break;
								}
							}
							if (!assignedPicker)//means there was nothing in the wardrobe set for it
							{
								thisPicker = new WardrobeSlotRecipePopup(_race.raceName, wsl, "");
							}
							if (thisPicker.OnGUI())
							{
								changed = true;
								if (thisPicker.RecipeName != "None" && thisPicker.RecipeName != "")
								{
									bool contained = false;
									for (int i = 0; i < _wardrobeSet.Count; i++)
									{
										if (_wardrobeSet[i].slot == wsl)
										{
											_wardrobeSet[i].recipe = thisPicker.RecipeName;
											contained = true;
											break;
										}
									}
									if (!contained)
										_wardrobeSet.Add(new WardrobeSettings(wsl, thisPicker.RecipeName));
								}
								else
								{
									for (int i = 0; i < _wardrobeSet.Count; i++)
									{
										if (_wardrobeSet[i].slot == wsl)
										{
											_wardrobeSet.RemoveAt(i);
											break;
										}
									}
								}
							}
						}
						if (WardrobeSet.Count > 0)
						{
							if (GUILayout.Button(new GUIContent("UpdateSharedColors", "Automatically adds any shared colors defined in the selected recipes to this recipes SharedColors")))
							{
								for (int i = 0; i < _wardrobeSet.Count; i++)
								{
									changed = AddSharedColorsFromRecipe(_wardrobeSet[i].recipe, _recipe) == true ? true : changed;
								}
							}
						}
						GUIHelper.EndVerticalPadded(10);
					}
				return changed;
			}
			/// <summary>
			/// Adds the shared colors from a given recipe name into the target recipe
			/// </summary>
			/// <param name="sourceRecipeName"></param>
			protected virtual bool AddSharedColorsFromRecipe(string sourceRecipeName, UMAData.UMARecipe targetRecipe)
			{
				bool changed = false;
				var thisUmaDataRecipe = new UMAData.UMARecipe();
				var context = UMAContext.FindInstance();
				if (context == null)
					return false;
				var thisWardrobeRecipe = context.dynamicCharacterSystem.GetBaseRecipe(sourceRecipeName);
				if (thisWardrobeRecipe == null)
					return false;
				try
				{
					thisWardrobeRecipe.Load(thisUmaDataRecipe, context);
				}
				catch
				{
					return false;
				}
				if (thisUmaDataRecipe.sharedColors.Length > 0)
				{
					List<OverlayColorData> newSharedColors = new List<OverlayColorData>();
					newSharedColors.AddRange(targetRecipe.sharedColors);
					for (int i = 0; i < thisUmaDataRecipe.sharedColors.Length; i++)
					{
						bool existed = false;
						for (int ii = 0; ii < newSharedColors.Count; ii++)
							if (newSharedColors[ii].name == thisUmaDataRecipe.sharedColors[i].name)
							{
								existed = true;
								break;
							}
						if (!existed)
						{
							newSharedColors.Add(thisUmaDataRecipe.sharedColors[i]);
							changed = true;
						}
					}
					if (changed)
						targetRecipe.sharedColors = newSharedColors.ToArray();
				}
				return changed;
			}
		}

		/// <summary>
		/// Replaces the standard 'Slot' editor in a DynamicCharacterAvatar type of recipe with one that shows the assigned wardrobe recipes in its wardrobe set
		/// </summary>
		public class WardrobeSetMasterEditor : SlotMasterEditor
		{
			private List<WardrobeSettings> _wardrobeSet;
			//private bool _foldout = true;

			public WardrobeSetMasterEditor(UMAData.UMARecipe recipe, List<WardrobeSettings> wardrobeSet) : base(recipe)
			{
				_wardrobeSet = wardrobeSet;
			}
			public override bool OnGUI(ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
			{
				bool changed = false;
				if (!OpenSlots.ContainsKey("wardrobeSet"))
					OpenSlots.Add("wardrobeSet", true);

				if (_sharedColorsEditor.OnGUI(_recipe))
				{
					changed = true;
				}
				//if this is a backwards compatible DCS recipe (i.e. has SlotData AND a Wardrobe set) we need to show BOTH things
				//for this to really work youd need to be able to edit the WardrobeSet and have that modify the slotDataList
				//Hence the epic UpdateBackwardsCompatibleData method
				if (_recipe.slotDataList.Length > 0)
				{
					EditorGUILayout.HelpBox("This is a 'Backwards Compatible' DynamicCharacterAvatar recipe. The slots and overlays in the 'BackwardsCompatibleData' section will update as you change the items in the WardrobeSet.", MessageType.Info);
				}
				if (DrawWardrobeSetUI())
				{
					changed = true;
					if (_recipe.slotDataList.Length > 0)
					{
						UpdateBackwardsCompatibleData();
					}
				}
				if (_recipe.slotDataList.Length > 0)
				{
					if (!OpenSlots.ContainsKey("backwardsCompatibleData"))
						OpenSlots.Add("backwardsCompatibleData", false);
					GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
					GUILayout.Space(10);
					bool bcdfoldoutOpen = OpenSlots["backwardsCompatibleData"];
					bcdfoldoutOpen = EditorGUILayout.Foldout(OpenSlots["backwardsCompatibleData"], "Backwards Compatible Data");
					OpenSlots["backwardsCompatibleData"] = bcdfoldoutOpen;
					GUILayout.EndHorizontal();
					if (bcdfoldoutOpen)
					{
						EditorGUI.BeginDisabledGroup(true);
						GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
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
						GUIHelper.EndVerticalPadded(10);
						EditorGUI.EndDisabledGroup();
					}
				}
				return changed;
			}
			private bool DrawWardrobeSetUI()
			{
				bool changed = false;
				if (_recipe.raceData != null)
				{
					if (_recipe.raceData.wardrobeSlots.Count > 0)
					{
						GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
						GUILayout.Space(10);
						bool wsfoldoutOpen = OpenSlots["wardrobeSet"];
						wsfoldoutOpen = EditorGUILayout.Foldout(OpenSlots["wardrobeSet"], "Wardrobe Set");
						OpenSlots["wardrobeSet"] = wsfoldoutOpen;
						GUILayout.EndHorizontal();
						if (wsfoldoutOpen)
						{
							if (_wardrobeSet == null)
								return false;
							var thisWardrobeSetEditor = new WardrobeSetEditor(_recipe.raceData, _wardrobeSet, _recipe, true);
							if (thisWardrobeSetEditor.OnGUI())
							{
								_wardrobeSet = thisWardrobeSetEditor.WardrobeSet;
								changed = true;
							}
						}
					}
				}
				return changed;
			}
			private void UpdateBackwardsCompatibleData()
			{
				var context = UMAContext.FindInstance();
				if (context == null)
				{
					var _errorMessage = "Editing a recipe requires a loaded scene with a valid UMAContext.";
					Debug.LogWarning(_errorMessage);
				}
				//reset the recipe to the raceBase recipe
				//this screws up if this actual recipe IS the baseRaceRecipe
				//But you simply cant create a race that way. You HAVE to make a recipe from scratch, so I dont think its an issue
				var thisBaseRecipe = _recipe.raceData.baseRaceRecipe;
				thisBaseRecipe.Load(_recipe, context);
				if (_wardrobeSet.Count > 0)
				{
					var thisDCS = context.dynamicCharacterSystem;
					if (thisDCS == null)
					{
						var _errorMessage = "Editing a recipe requires a loaded scene with a valid UMAContext.";
						Debug.LogWarning(_errorMessage);
					}
					List<UMARecipeBase> Recipes = new List<UMARecipeBase>();
					List<string> SuppressSlotsStrings = new List<string>();
					List<string> HiddenSlots = new List<string>();
					var wardrobeRecipesToRender = new Dictionary<string, UMARecipeBase>();
					var activeRace = _recipe.raceData.name;
					//Dont add the WardrobeCollection to the recipes to render- they doesn't render directly and will have already set their actual wardrobeRecipe slots SetSlot
					foreach (WardrobeSettings set in _wardrobeSet)
					{
						var thisRecipe = thisDCS.GetBaseRecipe(set.recipe);
						if (thisRecipe == null)
						{
							continue;
						}
						if (thisRecipe.GetType().ToString() == "UMAWardrobeCollection")
						{
							var TargetType = thisRecipe.GetType();
							FieldInfo WardrobeCollectionField = TargetType.GetField("wardrobeCollection", BindingFlags.Public | BindingFlags.Instance);
							WardrobeCollectionList wardrobeCollection = (WardrobeCollectionList)WardrobeCollectionField.GetValue(thisRecipe);
							if (wardrobeCollection[activeRace] != null)
							{
								foreach (WardrobeSettings ws in wardrobeCollection[activeRace])
								{
									var wsRecipe = thisDCS.GetBaseRecipe(ws.recipe);
									if (wsRecipe != null)
									{
										if (wardrobeRecipesToRender.ContainsKey(ws.slot))
											wardrobeRecipesToRender[ws.slot] = wsRecipe;
										else
											wardrobeRecipesToRender.Add(ws.slot, wsRecipe);
									}
								}
							}
						}
						else
						{
							//_recipe.Merge(thisRecipe.GetCachedRecipe(context), true);
							if (wardrobeRecipesToRender.ContainsKey(set.slot))
								wardrobeRecipesToRender[set.slot] = thisRecipe;
							else
								wardrobeRecipesToRender.Add(set.slot, thisRecipe);
						}
					}
					if (wardrobeRecipesToRender.Count > 0)
					{
						foreach (UMARecipeBase utr in wardrobeRecipesToRender.Values)
						{
							var TargetType = utr.GetType();
							FieldInfo CompatibleRacesField = TargetType.GetField("compatibleRaces", BindingFlags.Public | BindingFlags.Instance);
							FieldInfo WardrobeSlotField = TargetType.GetField("wardrobeSlot", BindingFlags.Public | BindingFlags.Instance);
							FieldInfo SuppressWardrobeSlotField = TargetType.GetField("suppressWardrobeSlots", BindingFlags.Public | BindingFlags.Instance);

							//field values
							List<string> compatibleRaces = (List<string>)CompatibleRacesField.GetValue(utr);
							string wardrobeSlot = (string)WardrobeSlotField.GetValue(utr);
							List<string> suppressWardrobeSlot = (List<string>)SuppressWardrobeSlotField.GetValue(utr);

							if (suppressWardrobeSlot != null)
							{
								if (activeRace == "" || ((compatibleRaces.Count == 0 || compatibleRaces.Contains(activeRace)) || (_recipe.raceData.findBackwardsCompatibleWith(compatibleRaces) && _recipe.raceData.wardrobeSlots.Contains(wardrobeSlot))))
								{
									if (!SuppressSlotsStrings.Contains(wardrobeSlot))
									{
										foreach (string suppressedSlot in suppressWardrobeSlot)
										{
											SuppressSlotsStrings.Add(suppressedSlot);
										}
									}
								}
							}
						}
					}
					foreach (string ws in _recipe.raceData.wardrobeSlots)//this doesn't need to validate racedata- we wouldn't be here if it was null
					{
						if (SuppressSlotsStrings.Contains(ws))
						{
							continue;
						}
						if (wardrobeRecipesToRender.ContainsKey(ws))
						{
							UMARecipeBase utr = wardrobeRecipesToRender[ws];
							var TargetType = utr.GetType();
							FieldInfo CompatibleRacesField = TargetType.GetField("compatibleRaces", BindingFlags.Public | BindingFlags.Instance);
							FieldInfo WardrobeSlotField = TargetType.GetField("wardrobeSlot", BindingFlags.Public | BindingFlags.Instance);
							FieldInfo HidesField = TargetType.GetField("Hides", BindingFlags.Public | BindingFlags.Instance);

							//field values
							List<string> compatibleRaces = (List<string>)CompatibleRacesField.GetValue(utr);
							string wardrobeSlot = (string)WardrobeSlotField.GetValue(utr);
							List<string> hides = (List<string>)HidesField.GetValue(utr);

							if (activeRace == "" || ((compatibleRaces.Count == 0 || compatibleRaces.Contains(activeRace)) || (_recipe.raceData.findBackwardsCompatibleWith(compatibleRaces) && _recipe.raceData.wardrobeSlots.Contains(wardrobeSlot))))
							{
								Recipes.Add(utr);
								if (hides.Count > 0)
								{
									foreach (string s in hides)
									{
										HiddenSlots.Add(s);
									}
								}
							}
						}
					}
					//merge them in
					foreach (var additionalRecipe in Recipes)
					{
						_recipe.Merge(additionalRecipe.GetCachedRecipe(context), true);
					}
					if (HiddenSlots.Count > 0)
					{
						List<SlotData> NewSlots = new List<SlotData>();
						foreach (SlotData sd in _recipe.slotDataList)
						{
							if (sd == null)
								continue;
							if (!HiddenSlots.Contains(sd.asset.slotName))
							{
								NewSlots.Add(sd);
							}
						}
						_recipe.slotDataList = NewSlots.ToArray();
					}
					ResetSlotEditors();
				}
			}
			private void ResetSlotEditors()
			{
				if (_recipe.slotDataList == null)
				{
					_recipe.slotDataList = new SlotData[0];
				}
				for (int i = 0; i < _recipe.slotDataList.Length; i++)
				{
					var slot = _recipe.slotDataList[i];

					if (slot == null)
						continue;

					_slotEditors.Add(new SlotEditor(_recipe, slot, i));
				}

				if (_slotEditors.Count > 1)
				{
					// Don't juggle the order - this way, they're in the order they're in the file, or dropped in.
					List<SlotEditor> sortedSlots = new List<SlotEditor>(_slotEditors);
					sortedSlots.Sort(SlotEditor.comparer);

					var overlays1 = sortedSlots[0].GetOverlays();
					var overlays2 = sortedSlots[1].GetOverlays();
					for (int i = 0; i < sortedSlots.Count - 2; i++)
					{
						if (overlays1 == overlays2)
							sortedSlots[i].sharedOverlays = true;
						overlays1 = overlays2;
						overlays2 = sortedSlots[i + 2].GetOverlays();
					}
				}
			}
		}
	}
}
#endif
