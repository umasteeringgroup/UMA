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
		}
		/// <summary>
		/// Replaces the standard 'Slot' editor in a DynamicCharacterAvatar type of recipe with one that shows the assigned wardrobe recipes in its wardrobe set
		/// </summary>
		public class WardrobeSetMasterEditor : SlotMasterEditor
		{
			private List<WardrobeSettings> _wardrobeSet;
			private bool _foldout = true;

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
				//and to do that correctly we would need to use DynamicCharacterAvatars SetSlot and BuildCharacter methods!
				//really Standard recipes should NOT have any WardrobeSet data- they only do because there used to be no other way of saving them as assets
				if (_recipe.slotDataList.Length > 0)
				{
					EditorGUILayout.HelpBox("This is a 'Backwards Compatible' DynamicCharacterAvatar recipe. It is recommended that you edit the following by loading the recipe into a DynamicCharacterAvatar and saving it from there", MessageType.Warning);
				}
				if (DrawWardrobeSetUI())
				{
					changed = true;
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
						var context = UMAContext.FindInstance();
						if (context == null)
						{
							var _errorMessage = "Editing a recipe requires a loaded scene with a valid UMAContext.";
							Debug.LogWarning(_errorMessage);
						}
						GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
						GUILayout.Space(10);
						bool wsfoldoutOpen = OpenSlots["wardrobeSet"];
						wsfoldoutOpen = EditorGUILayout.Foldout(OpenSlots["wardrobeSet"], "Wardrobe Set");
						OpenSlots["wardrobeSet"] = wsfoldoutOpen;
						GUILayout.EndHorizontal();
						if (wsfoldoutOpen)
						{
							if (_wardrobeSet == null || context == null)
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
		}
	}
}
#endif
