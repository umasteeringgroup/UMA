#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA.Editors
{
	[CustomEditor(typeof(UMAWardrobeCollection), true)]
	public partial class UMAWardrobeCollectionEditor : RecipeEditor
	{
		static bool coverImagesIsExpanded = false;

		protected override bool PreInspectorGUI()
		{
			hideToolBar = true;
			hideRaceField = true;
			return TextRecipeGUI();
		}

		/// <summary>
		/// Impliment this method to output any extra GUI for any extra fields you have added to UMAWardrobeCollection before the main RecipeGUI
		/// </summary>
		partial void PreRecipeGUI(ref bool changed);
		/// <summary>
		/// Impliment this method to output any extra GUI for any extra fields you have added to UMAWardrobeCollection after the main RecipeGUI
		/// </summary>
		partial void PostRecipeGUI(ref bool changed);

		protected override bool PostInspectorGUI()
		{
			bool changed = false;
			PostRecipeGUI(ref changed);
			return changed;
		}

		//draws the coverImages foldout
		protected virtual bool DrawCoverImagesUI(Type TargetType)
		{
			bool doUpdate = false;
			//FieldInfos
			var CoverImagesField = TargetType.GetField("coverImages", BindingFlags.Public | BindingFlags.Instance);
			//field values
			List<Sprite> coverImages = (List<Sprite>)CoverImagesField.GetValue(target);
			//drawUI
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			coverImagesIsExpanded = EditorGUILayout.Foldout(coverImagesIsExpanded, new GUIContent("Cover Images"));
			GUILayout.EndHorizontal();
			if (coverImagesIsExpanded)
			{
				List<Sprite> prevCoverImages = coverImages;
				GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
				EditorGUILayout.BeginHorizontal();
				for (int i = 0; i < coverImages.Count; i++)
				{
					EditorGUI.BeginChangeCheck();
					var thisImg = EditorGUILayout.ObjectField(coverImages[i], typeof(Sprite), false, GUILayout.Width(75), GUILayout.Height(75));
					if (EditorGUI.EndChangeCheck())
					{
						if (thisImg != coverImages[i])
						{
							if (thisImg == null)
							{
								coverImages.RemoveAt(i);
							}
							else
							{
								coverImages[i] = (Sprite)thisImg;
							}
							doUpdate = true;
						}
					}
				}
				EditorGUILayout.EndHorizontal();
				if (GUILayout.Button("Add"))
				{
					coverImages.Add(new Sprite());
				}
				if (!AreListsEqual<Sprite>(prevCoverImages, coverImages))
				{
					CoverImagesField.SetValue(target, coverImages);
				}
				GUIHelper.EndVerticalPadded(10);
			}
			GUILayout.Space(-5f);
			return doUpdate;
		}
		/// <summary>
		/// An editor for a WardrobeCollection. Wardrobe collections can have Shared Colors and multiple WardrobeSets, but dont need a standard Slot or DNA Editor
		/// </summary>
		public class WardrobeCollectionMasterEditor : SlotMasterEditor
		{
			private List<string> _compatibleRaces = new List<string>();
			private WardrobeCollectionList _wardrobeCollection;
			private List<string> _arbitraryRecipes = new List<string>();
			private bool forceGUIUpdate = false;
			private static string recipesAddErrMsg = "";
			//int recipePickerID = -1; This is needed if we can make the recipe drop area work with 'Click To Pick'

			public WardrobeCollectionMasterEditor(UMAData.UMARecipe recipe, List<string> compatibleRaces, WardrobeCollectionList wardrobeCollection, List<string> arbitraryRecipes) : base(recipe)
			{
				_compatibleRaces = compatibleRaces;
				_wardrobeCollection = wardrobeCollection;
				_arbitraryRecipes = arbitraryRecipes;
				UpdateFoldouts();
				recipesAddErrMsg = "";
			}

			public void UpdateVals(List<string> compatibleRaces, WardrobeCollectionList wardrobeCollection, List<string> arbitraryRecipes)
			{
				forceGUIUpdate = false;
				_wardrobeCollection = wardrobeCollection;
				_compatibleRaces = compatibleRaces;
				forceGUIUpdate = UpdateCollectionRaces();
                UpdateFoldouts();
			}

			private void UpdateFoldouts()
			{
				if (!OpenSlots.ContainsKey("wardrobeSets"))
					OpenSlots.Add("wardrobeSets", true);
				if (!OpenSlots.ContainsKey("arbitraryRecipes"))
					OpenSlots.Add("arbitraryRecipes", true);

				for (int i = 0; i < _compatibleRaces.Count; i++)
				{
					bool open = i == 0 ? true : false;
					if (!OpenSlots.ContainsKey(_compatibleRaces[i]))
					{
						OpenSlots.Add(_compatibleRaces[i], open);
					}
				}
			}
			private bool UpdateCollectionRaces()
			{
				bool changed = false;
				if (_compatibleRaces.Count == 0 && _wardrobeCollection.sets.Count > 0)
				{
					_wardrobeCollection.Clear();
					changed = true;
				}
				else
				{
					for(int i = 0; i < _compatibleRaces.Count; i++)
					{
						if (!_wardrobeCollection.Contains(_compatibleRaces[i]))
						{
							_wardrobeCollection.Add(_compatibleRaces[i]);
							changed = true;
						}
					}
					var collectionNames = _wardrobeCollection.GetAllRacesInCollection();
					for(int i = 0; i < collectionNames.Count; i++)
					{
						if (!_compatibleRaces.Contains(collectionNames[i]))
						{
							_wardrobeCollection.Remove(collectionNames[i]);
							changed = true;
                        }
					}			
				}
				return changed;
			}

			public override bool OnGUI(string targetName, ref bool _dnaDirty, ref bool _textureDirty, ref bool _meshDirty)
			{
				var context = UMAContext.FindInstance();
				if (context == null)
				{
					var _errorMessage = "Editing a recipe requires a loaded scene with a valid UMAContext.";
					Debug.LogWarning(_errorMessage);
					return false;
				}
				bool changed = forceGUIUpdate;
				//Make a foldout for WardrobeSets - the UI for an individual WardrobeSet is added for each compatible race in the collection
				GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
				GUILayout.Space(10);
				bool wsfoldoutOpen = OpenSlots["wardrobeSets"];
				wsfoldoutOpen = EditorGUILayout.Foldout(OpenSlots["wardrobeSets"], "Wardrobe Sets");
				OpenSlots["wardrobeSets"] = wsfoldoutOpen;
				GUILayout.EndHorizontal();
				if (wsfoldoutOpen)
				{
					GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

					EditorGUILayout.HelpBox("Wardrobe Sets are added for each 'Compatible Race' assigned above. 'SharedColors' in this section are derived from all the recipes assigned in the set and are will be applied to the Avatar when the wardrobe sets recipes are added.", MessageType.Info);
					if (_compatibleRaces.Count > 0)
					{
						//dont show shared colors unless there are 'FullOutfits' to apply them to
						if (_sharedColorsEditor.OnGUI(_recipe))
						{
							changed = true;
							_textureDirty = true;
						}
						for (int i = 0; i < _compatibleRaces.Count; i++)
						{
							var thisRace = context.raceLibrary.GetRace(_compatibleRaces[i]);
							if (thisRace != null)
							{
								GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
								GUILayout.Space(10);
								bool foldoutOpen = OpenSlots[_compatibleRaces[i]];
								foldoutOpen = EditorGUILayout.Foldout(OpenSlots[_compatibleRaces[i]], " Wardrobe Set: " + _compatibleRaces[i]);
								OpenSlots[_compatibleRaces[i]] = foldoutOpen;
								GUILayout.EndHorizontal();
								if (foldoutOpen)
								{
									var thisSetEditor = new WardrobeSetEditor(thisRace, _wardrobeCollection[thisRace.raceName], _recipe, false);
									if (thisSetEditor.OnGUI())
									{
										_wardrobeCollection[thisRace.raceName] = thisSetEditor.WardrobeSet;
										changed = true;
									}
								}
							}
							else
							{
								//Do the foldout thing but show as 'missing'
								GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
								GUILayout.Space(10);
								bool foldoutOpen = OpenSlots[_compatibleRaces[i]];
								foldoutOpen = EditorGUILayout.Foldout(OpenSlots[_compatibleRaces[i]], _compatibleRaces[i] + " Wardrobe Set (Missing)");
								OpenSlots[_compatibleRaces[i]] = foldoutOpen;
								GUILayout.EndHorizontal();
								if (foldoutOpen)
								{
									GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
									EditorGUILayout.HelpBox("_compatibleRaces[i] could not be located by the Dynamic Race Library", MessageType.Warning);
									GUIHelper.EndVerticalPadded(10);
								}
							}
						}
					}
					else
					{
						EditorGUILayout.HelpBox("Drag in compatible races at the top of this recipe and WardrobeSets for those races will show here", MessageType.Info);
					}
					GUIHelper.EndVerticalPadded(10);
				}
				GUILayout.Space(10);
				//the Arbitrary Recipes section
				GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
				GUILayout.Space(10);
				bool arbiOpen = OpenSlots["arbitraryRecipes"];
				arbiOpen = EditorGUILayout.Foldout(OpenSlots["arbitraryRecipes"], "Arbitrary Recipes");
				OpenSlots["arbitraryRecipes"] = arbiOpen;
				Rect dropArea = new Rect();
				GUILayout.EndHorizontal();
				if (arbiOpen)
				{
					GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
					EditorGUILayout.HelpBox("Drop recipes in to this area to create a collection that is not a full outfit or connected to any given race, for example a 'Hair Styles' pack or 'Tattoos' pack.", MessageType.Info);
					dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
					GUI.Box(dropArea, "Drag WardrobeRecipes here. " + recipesAddErrMsg);
					if (_arbitraryRecipes.Count > 0)
					{
						for (int i = 0; i < _arbitraryRecipes.Count; i++)
						{
							GUILayout.Space(2f);
							GUI.enabled = false; //we readonly to prevent typos
							Rect crfRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
							Rect crfDelRect = crfRect;
							crfRect.width = crfRect.width - 20f - 5f;
							crfDelRect.width = 20f + 2f;
							crfDelRect.x = crfRect.width + 20f + 10f;
							EditorGUI.TextField(crfRect, _arbitraryRecipes[i]);
							GUI.enabled = true;
							if (GUI.Button(crfDelRect, "X"))
							{
								_arbitraryRecipes.RemoveAt(i);
								changed = true;
							}
						}
					}
					GUIHelper.EndVerticalPadded(10);
					if (AddRecipesDropAreaGUI(ref recipesAddErrMsg, dropArea, _arbitraryRecipes))
						changed = true;
				}
				return changed;
			}
			// Drop area for Arbitrary Wardrobe recipes
			private bool AddRecipesDropAreaGUI(ref string errorMsg, Rect dropArea, List<string> recipes)
			{
				Event evt = Event.current;
				bool changed = false;
				//make the box clickable so that the user can select raceData assets from the asset selection window
				//TODO: cant make this work without layout errors. Anyone know how to fix?
				/*if (evt.type == EventType.MouseUp)
				{
					if (dropArea.Contains(evt.mousePosition))
					{
						recipePickerID = EditorGUIUtility.GetControlID(new GUIContent("recipeObjectPicker"), FocusType.Passive);
						EditorGUIUtility.ShowObjectPicker<UMARecipeBase>(null, false, "", recipePickerID);
					}
				}
				if (evt.commandName == "ObjectSelectorUpdated" && EditorGUIUtility.GetObjectPickerControlID() == recipePickerID)
				{
					UMARecipeBase tempRecipeAsset = EditorGUIUtility.GetObjectPickerObject() as UMARecipeBase;
					if (tempRecipeAsset)
					{
						if (AddIfWardrobeRecipe(tempRecipeAsset, recipes))
						{
							changed = true;
							errorMsg = "";
						}
						else
							errorMsg = "That recipe was not a Wardrobe recipe";

					}
				}*/
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
						bool allAdded = true;
						for (int i = 0; i < draggedObjects.Length; i++)
						{
							if (draggedObjects[i])
							{
								UMARecipeBase tempRecipeAsset = draggedObjects[i] as UMARecipeBase;
								if (tempRecipeAsset)
								{
									if (AddIfWardrobeRecipe(tempRecipeAsset, recipes))
										changed = true;
									else
									{
										allAdded = false;
									}
									continue;
								}

								var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
								if (System.IO.Directory.Exists(path))
								{
									RecursiveScanFoldersForAssets(path, recipes);
								}
							}
						}
						if (!allAdded)
							errorMsg = "Some of the recipes you tried to add were not Wardrobe recipes";
						else
							errorMsg = "";
					}
				}
				return changed;
			}
			private bool AddIfWardrobeRecipe(UnityEngine.Object tempRecipeAsset, List<string> recipes)
			{
				bool added = false;
				if (!recipes.Contains(tempRecipeAsset.name))
				{
					Type TargetType = tempRecipeAsset.GetType();
					if (TargetType.ToString() == "UMATextRecipe" || TargetType.ToString() == "UMAWardrobeRecipe")
					{
						FieldInfo RecipeTypeField = TargetType.GetField("recipeType", BindingFlags.Public | BindingFlags.Instance);
						string recipeType = (string)RecipeTypeField.GetValue(tempRecipeAsset);
						if (recipeType == "Wardrobe")
						{
							recipes.Add(tempRecipeAsset.name);
							added = true;
						}
					}
				}
				return added;
			}
			private void RecursiveScanFoldersForAssets(string path, List<string> recipes)
			{
				var assetFiles = System.IO.Directory.GetFiles(path, "*.asset");
				foreach (var assetFile in assetFiles)
				{
					var tempRecipeAsset = AssetDatabase.LoadAssetAtPath(assetFile, typeof(UMARecipeBase)) as UMARecipeBase;
					if (tempRecipeAsset)
					{
						AddIfWardrobeRecipe(tempRecipeAsset, recipes);
					}
				}
				foreach (var subFolder in System.IO.Directory.GetDirectories(path))
				{
					RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'), recipes);
				}
			}
		}

		protected virtual bool TextRecipeGUI()
		{
			Type TargetType = target.GetType();
			bool doUpdate = false;

			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.Popup("Recipe Type", 0, new string[] { "WardrobeCollection"});
			EditorGUI.EndDisabledGroup();

			PreRecipeGUI(ref doUpdate);

			FieldInfo CompatibleRacesField = TargetType.GetField("compatibleRaces", BindingFlags.Public | BindingFlags.Instance);
			//WardrobeCollections use the WardrobeSlot field to allow the user to define a Collection Group
			FieldInfo WardrobeSlotField = TargetType.GetField("wardrobeSlot", BindingFlags.Public | BindingFlags.Instance);
			string wardrobeSlot = (string)WardrobeSlotField.GetValue(target);
			List<string> compatibleRaces = (List<string>)CompatibleRacesField.GetValue(target);
		
			//FieldInfos
			FieldInfo WardrobeCollectionList = TargetType.GetField("wardrobeCollection", BindingFlags.Public | BindingFlags.Instance);
			FieldInfo ArbitraryRecipesList = TargetType.GetField("arbitraryRecipes", BindingFlags.Public | BindingFlags.Instance);
			//field values
			WardrobeCollectionList wardrobeCollection = (WardrobeCollectionList)WardrobeCollectionList.GetValue(target);
			List<string> arbitraryRecipes = (List<string>)ArbitraryRecipesList.GetValue(target);

			if (slotEditor == null || slotEditor.GetType() != typeof(WardrobeCollectionMasterEditor))
			{
				slotEditor = new WardrobeCollectionMasterEditor(_recipe, compatibleRaces, wardrobeCollection, arbitraryRecipes);
			}
			else
			{
				(slotEditor as WardrobeCollectionMasterEditor).UpdateVals(compatibleRaces, wardrobeCollection, arbitraryRecipes);
			}
			//wardrobe collection also has a 'cover image' field
			if (DrawCoverImagesUI(TargetType))
				doUpdate = true;

			//CompatibleRaces drop area
			if (DrawCompatibleRacesUI(TargetType))
				doUpdate = true;

			EditorGUILayout.Space();
			//Draw the Wardrobe slot field as a WardrobeCollection Group text field.
			EditorGUILayout.HelpBox("When a collection is placed on an avatar it replaces any other collections belonging to this group and unloads that collections recipes", MessageType.Info);
            var newWardrobeSlot = EditorGUILayout.TextField("Collection Group", wardrobeSlot);
			if(newWardrobeSlot != wardrobeSlot)
			{
				WardrobeSlotField.SetValue(target, newWardrobeSlot);
				doUpdate = true;
			}

			EditorGUILayout.Space();
				
			return doUpdate;
		}

	}
}
#endif
