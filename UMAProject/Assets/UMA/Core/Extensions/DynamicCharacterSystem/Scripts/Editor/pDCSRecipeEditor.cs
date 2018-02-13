#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA.Editors
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
			private string _wcOverrideName;
			Texture warningIcon;

			public string RecipeName
			{
				get
				{
					return _wsRecipeName;
				}
			}
			public WardrobeSlotRecipePopup(string race, string slot, string recipeName, string wcOverrideName = "")
			{
				_wsRace = race;
				_wsSlot = slot;
				_wsRecipeName = recipeName;
				_wcOverrideName = wcOverrideName;
			}

			public bool OnGUI()
			{
				if (warningIcon == null)
				{
					warningIcon = EditorGUIUtility.FindTexture("console.warnicon.sml");
				}
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
				List<string> thisPopupLabels = new List<string>();
				var noneString = "None";
				if (_wcOverrideName != "")
					noneString = "None (Set by '" + _wcOverrideName + "' Wardrobe Collection)";
				thisPopupLabels.Add(noneString);
				thisPopupVals.AddRange(recipesForRaceSlot);
				thisPopupLabels.AddRange(recipesForRaceSlot);
				var selected = 0;
				var recipeIsLive = true;
				Rect valRBut = new Rect();
				var warningStyle = new GUIStyle(EditorStyles.label);
				warningStyle.fixedHeight = warningIcon.height + 4f;
				warningStyle.contentOffset = new Vector2(0, -2f);
				if (_wsRecipeName != "")
				{
					recipeIsLive = context.dynamicCharacterSystem.CheckRecipeAvailability(_wsRecipeName);
					selected = thisPopupVals.IndexOf(_wsRecipeName);
					if (selected == -1)
					{
						selected = thisPopupVals.Count;
						string missingOrIncompatible = "missing";
						if (context.dynamicCharacterSystem.GetBaseRecipe(_wsRecipeName, false) != null)
							missingOrIncompatible = "incompatible";
						thisPopupLabels.Add(_wsRecipeName + " (" + missingOrIncompatible + ")");
                    }
				}
				var newSelected = selected;
				if (!recipeIsLive)
					EditorGUI.indentLevel++;
				var label = _wsSlot == "WardrobeCollection" ? " " : _wsSlot;
				EditorGUI.BeginChangeCheck();
				newSelected = EditorGUILayout.Popup(label, selected, thisPopupLabels.ToArray());
				if (!recipeIsLive)
				{
					EditorGUI.indentLevel--;
					valRBut = GUILayoutUtility.GetLastRect();
				}
				if (EditorGUI.EndChangeCheck())
				{
					if (newSelected != selected)
					{
						changed = true;
						_wsRecipeName = (thisPopupLabels[newSelected].IndexOf("(missing)") == -1 && thisPopupLabels[newSelected].IndexOf("(incompatible)") == -1) ? (thisPopupVals[newSelected] != "None" ? thisPopupVals[newSelected] : "") : _wsRecipeName;
					}
				}
				if (!recipeIsLive)
				{
					var warningRect = new Rect((valRBut.xMin - 5f), valRBut.yMin, 20f, valRBut.height);
					var warningGUIContent = new GUIContent("", _wsRecipeName + " was not Live. You can make it live by adding it to the UMA/UMA Global Library.");
					warningGUIContent.image = warningIcon;
					GUI.Button(warningRect, warningGUIContent, warningStyle);
					//TODO we can probably use AssetIndexer.AddEvilAsset here so it gets added without having to go there
					//Id like this to be a button that opens the window, opens the recipe section and ideally highlights the asset that needs to be made live
					/*if(GUI.Button(warningRect, warningGUIContent, warningStyle))
					{
						UMAAssetIndexWindow.Init();
                    }*/
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

						EditorGUILayout.HelpBox("Recently added recipes not showing up? Make sure you have added them to the 'UMA Global Library' and click the 'Refresh Recipes' button below.", MessageType.Info);
						if (GUILayout.Button("Refresh Recipes"))
						{
							context.dynamicCharacterSystem.Refresh(false);
							return false;
						}
						//a dictionary of slots that are being assigned by WardrobeCollections
						var slotsAssignedByWCs = new Dictionary<string, string>();
						if (_allowWardrobeCollectionSlot)
						{
							var wcRecipesForRace = context.dynamicCharacterSystem.GetRecipesForRaceSlot(_race.raceName, "WardrobeCollection");
							var wcGroupDict = new Dictionary<string, List<UMARecipeBase>>();
							//I'm using reflection here to get fields and methods from the UMAWardrobeCollection type so this will still work if 'StandardAssets' is moved to 'Standard Assets'
							for (int i = 0; i < wcRecipesForRace.Count; i++)
							{
								Type wcType = wcRecipesForRace[i].GetType();
								if (wcType.ToString().Replace(wcType.Namespace+".", "") == "UMAWardrobeCollection")
								{
									FieldInfo wcRecipeSlotField = wcType.GetField("wardrobeSlot", BindingFlags.Public | BindingFlags.Instance);
									var wcRecipeSlot = (string)wcRecipeSlotField.GetValue(wcRecipesForRace[i]);
									if (!wcGroupDict.ContainsKey(wcRecipeSlot))
									{
										wcGroupDict.Add(wcRecipeSlot, new List<UMARecipeBase>());
									}
									wcGroupDict[wcRecipeSlot].Add(wcRecipesForRace[i]);
								}
							}
							if (wcGroupDict.Count > 0)
							{
								MethodInfo WCGetRacesWardrobeSetMethod = null;
								EditorGUILayout.LabelField("WardrobeCollections");
								EditorGUI.indentLevel++;
								foreach (KeyValuePair<string, List<UMARecipeBase>> kp in wcGroupDict)
								{
									if (WCGetRacesWardrobeSetMethod == null)
										WCGetRacesWardrobeSetMethod = kp.Value[0].GetType().GetMethod("GetRacesWardrobeSet", new Type[] { typeof(RaceData) });
									var selected = 0;
									var prevRecipe = "";
									var thisPopupVals = new List<string>();
									thisPopupVals.Add("None");
									for (int i = 0; i < kp.Value.Count; i++)
									{
										thisPopupVals.Add(kp.Value[i].name);
										//check if this is selected
										for (int wsi = 0; wsi < _wardrobeSet.Count; wsi++)
										{
											if (kp.Value[i].name == _wardrobeSet[wsi].recipe)
											{
												prevRecipe = _wardrobeSet[wsi].recipe;
												selected = i + 1;
												var thisWCWardrobeSet = (List<WardrobeSettings>)WCGetRacesWardrobeSetMethod.Invoke(kp.Value[i], new object[] { _race });
												for (int wcwsi = 0; wcwsi < thisWCWardrobeSet.Count; wcwsi++)
												{
													if (!slotsAssignedByWCs.ContainsKey(thisWCWardrobeSet[wcwsi].slot))
														slotsAssignedByWCs.Add(thisWCWardrobeSet[wcwsi].slot, kp.Value[i].name);
													else
														slotsAssignedByWCs[thisWCWardrobeSet[wcwsi].slot] = kp.Value[i].name;
												}
												break;
											}
										}
									}
									EditorGUI.BeginChangeCheck();
									var newSelected = EditorGUILayout.Popup(kp.Key, selected, thisPopupVals.ToArray());
									if (EditorGUI.EndChangeCheck())
									{
										for (int wsi = 0; wsi < _wardrobeSet.Count; wsi++)
										{
											if (_wardrobeSet[wsi].recipe == prevRecipe)
											{
												//we need to remove the wardrobeSettings that has prevRecipe as its value from _wardrobeSettings
												if (newSelected == 0)
												{
													_wardrobeSet.RemoveAt(wsi);
												}
												else
												{
													//we need to make wardrobeSettings that has prevRecipe have the new value
													_wardrobeSet[wsi].recipe = thisPopupVals[newSelected];
												}
											}
										}
										changed = true;
									}
								}
								EditorGUI.indentLevel--;
								EditorGUILayout.Space();
								EditorGUILayout.LabelField("WardrobeSlots");
								EditorGUI.indentLevel++;
							}
						}
						foreach (string wsl in _race.wardrobeSlots)
						{
							if (wsl == "None")
								continue;

							//Obsolete- now wardrobeCollections apply their WardrobeSet to any slots
							//if (wsl == "FullOutfit" && _allowWardrobeCollectionSlot == false)
							//	continue;

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
								//This may still be being assigned by a wardrobe collection though so show that
								var wcOverrideName = "";
								if (slotsAssignedByWCs.ContainsKey(wsl))
									wcOverrideName = slotsAssignedByWCs[wsl];
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
						if (_allowWardrobeCollectionSlot)
						{
							EditorGUI.indentLevel--;
						}
						if (WardrobeSet.Count > 0)
						{
							EditorGUILayout.Space();
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
			/// <param name="targetRecipe"></param>
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
			public override bool OnGUI(string targetName, ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
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
						EditorGUILayout.HelpBox("If you are going to use the recipe with a DynamicCharacterAvatar, it is recommended that you that you edit the 'WardrobeSet' section above and/or the base/wardrobe recipes directly. Changes you make manually below will only affect old style UMAAvatars", MessageType.Info);
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
							//if this is a 'backwards compatible' recipe dont show the 'wardrobeCollections' bit since old avatars cannot wear collections
							bool showWardrobeCollections = _recipe.slotDataList.Length > 0 ? false : true;
                            var thisWardrobeSetEditor = new WardrobeSetEditor(_recipe.raceData, _wardrobeSet, _recipe, showWardrobeCollections);
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
					var activeRace = _recipe.raceData.raceName;
					//Dont add the WardrobeCollection to the recipes to render- they doesn't render directly and will have already set their actual wardrobeRecipe slots SetSlot
					foreach (WardrobeSettings set in _wardrobeSet)
					{
						var thisRecipe = thisDCS.GetBaseRecipe(set.recipe);
						if (thisRecipe == null)
						{
							continue;
						}
						if (thisRecipe.GetType().ToString() == "UMA.UMAWardrobeCollection")
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
								if (activeRace == "" || ((compatibleRaces.Count == 0 || compatibleRaces.Contains(activeRace)) || (_recipe.raceData.IsCrossCompatibleWith(compatibleRaces) && _recipe.raceData.wardrobeSlots.Contains(wardrobeSlot))))
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
					foreach (string ws in _recipe.raceData.wardrobeSlots)
					{
                        if (SuppressSlotsStrings.Contains(ws))
						{
							continue;
						}
						if (wardrobeRecipesToRender.ContainsKey(ws))
						{
							UMARecipeBase utr = wardrobeRecipesToRender[ws];
							var TargetType = wardrobeRecipesToRender[ws].GetType();
                            FieldInfo CompatibleRacesField = TargetType.GetField("compatibleRaces", BindingFlags.Public | BindingFlags.Instance);
							FieldInfo WardrobeSlotField = TargetType.GetField("wardrobeSlot", BindingFlags.Public | BindingFlags.Instance);
							FieldInfo HidesField = TargetType.GetField("Hides", BindingFlags.Public | BindingFlags.Instance);

							//field values
							List<string> compatibleRaces = (List<string>)CompatibleRacesField.GetValue(utr);
							string wardrobeSlot = (string)WardrobeSlotField.GetValue(utr);
							List<string> hides = (List<string>)HidesField.GetValue(utr);

							if (activeRace == "" || ((compatibleRaces.Count == 0 || compatibleRaces.Contains(activeRace)) || (_recipe.raceData.IsCrossCompatibleWith(compatibleRaces) && _recipe.raceData.wardrobeSlots.Contains(wardrobeSlot))))
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
