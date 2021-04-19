using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using UMA.Editors;
using UMA.CharacterSystem;

namespace UMA.CharacterSystem.Editors
{
	[CustomEditor(typeof(DynamicCharacterAvatar), true)]
	public partial class DynamicCharacterAvatarEditor : Editor
	{		
		public static bool showHelp = false;
		public static bool showWardrobe = false;
		public static bool showEditorCustomization = true;
		public static bool showPrefinedDNA = false;

		public static int currentcolorfilter=0;
		public string[] colorfilters = { "Base", "All", "Hide ColorDNA" };
		public List<string> baseColorNames = new List<string>();
		public int currentDNA = 0;
		private string cachedRace = "";
		private string[] cachedRaceDNA = { };
		private string[] rawcachedRaceDNA = { };

		protected DynamicCharacterAvatar thisDCA;
		protected RaceSetterPropertyDrawer _racePropDrawer = new RaceSetterPropertyDrawer();
		protected WardrobeRecipeListPropertyDrawer _wardrobePropDrawer = new WardrobeRecipeListPropertyDrawer();
		protected RaceAnimatorListPropertyDrawer _animatorPropDrawer = new RaceAnimatorListPropertyDrawer();

		public void OnEnable()
		{
			baseColorNames.Clear();
			baseColorNames.AddRange(new string[] { "skin","hair","eyes"});
			thisDCA = target as DynamicCharacterAvatar;
			if (thisDCA.context == null)
			{
				thisDCA.context = UMAContextBase.Instance;
				if (thisDCA.context == null)
				{
					thisDCA.context = thisDCA.CreateEditorContext();
				}
			}
			else if (thisDCA.context.gameObject.name == "UMAEditorContext")
			{
				//this will set also the existing Editorcontext if there is one
				thisDCA.CreateEditorContext();
			}
			else if (thisDCA.context.gameObject.transform.parent != null)
			{
				//this will set also the existing Editorcontext if there is one
				if (thisDCA.context.gameObject.transform.parent.gameObject.name == "UMAEditorContext")
					thisDCA.CreateEditorContext();
			}
			_racePropDrawer.thisDCA = thisDCA;
			_wardrobePropDrawer.thisDCA = thisDCA;
			_animatorPropDrawer.thisDCA = thisDCA;
		}

		public void SetNewColorCount(int colorCount)
		{
			var newcharacterColors = new List<DynamicCharacterAvatar.ColorValue>();
			for (int i = 0; i < colorCount; i++)
			{
				if (thisDCA.characterColors.Colors.Count > i)
				{
					newcharacterColors.Add(thisDCA.characterColors.Colors[i]);
				}
				else
				{
					newcharacterColors.Add(new DynamicCharacterAvatar.ColorValue(3));
				}
			}
			thisDCA.characterColors.Colors = newcharacterColors;
		}

		protected bool characterAvatarLoadSaveOpen;

		private void BeginVerticalPadded()
        {
			if (EditorGUIUtility.isProSkin)
			{
				GUIHelper.BeginVerticalPadded(10, new Color(1.3f, 1.4f, 1.5f));
			}
			else
			{
				GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
			}
		}

		private void EndVerticalPadded()
        {
			GUIHelper.EndVerticalPadded(10);
        }

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			EditorGUI.BeginChangeCheck();
			showHelp = EditorGUILayout.Toggle("Show Help", showHelp);
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}

			Editor.DrawPropertiesExcluding(serializedObject, new string[] { "hide","BundleCheck", "loadBlendShapes","activeRace","defaultChangeRaceOptions","cacheCurrentState", "rebuildSkeleton", "preloadWardrobeRecipes", "raceAnimationControllers",
				/* Editor Only Fields */ "editorTimeGeneration",
				"characterColors","BoundsOffset","_buildCharacterEnabled","keepAvatar","KeepAnimatorController",
				/*LoadOtions fields*/ "defaultLoadOptions", "loadPathType", "loadPath", "loadFilename", "loadString", "loadFileOnStart", "waitForBundles", /*"buildAfterLoad",*/
				/*SaveOptions fields*/ "defaultSaveOptions", "savePathType","savePath", "saveFilename", "makeUniqueFilename","ensureSharedColors", 
				/*Moved into AdvancedOptions*/"context","umaData","umaRecipe", "umaAdditionalRecipes","umaGenerator", "animationController", "defaultRendererAsset",
				/*Moved into CharacterEvents*/"CharacterCreated", "CharacterBegun", "CharacterUpdated", "CharacterDestroyed", "CharacterDnaUpdated", "RecipeUpdated", "AnimatorStateSaved", "AnimatorStateRestored","WardrobeAdded","WardrobeRemoved",
				/*PlaceholderOptions fields*/"showPlaceholder", "previewModel", "customModel", "customRotation", "previewColor", "AtlasResolutionScale","DelayUnload","predefinedDNA","alwaysRebuildSkeleton", "umaRecipe"});

			//The base DynamicAvatar properties- get these early because changing the race changes someof them
			SerializedProperty context = serializedObject.FindProperty("context");
			SerializedProperty umaData = serializedObject.FindProperty("umaData");
			SerializedProperty umaGenerator = serializedObject.FindProperty("umaGenerator");
			SerializedProperty umaRecipe = serializedObject.FindProperty("umaRecipe");
			SerializedProperty umaAdditionalRecipes = serializedObject.FindProperty("umaAdditionalRecipes");
			SerializedProperty animationController = serializedObject.FindProperty("animationController");

			// ************************************************************
			// Set the race
			// ************************************************************
			SerializedProperty thisRaceSetter = serializedObject.FindProperty("activeRace");
			Rect currentRect = EditorGUILayout.GetControlRect(false, _racePropDrawer.GetPropertyHeight(thisRaceSetter, GUIContent.none));
			EditorGUI.BeginChangeCheck();
			_racePropDrawer.OnGUI(currentRect, thisRaceSetter, new GUIContent(thisRaceSetter.displayName));
			if (EditorGUI.EndChangeCheck())
			{
				bool okToProcess = true;
				// check to see if we changed it while playing, and if so, don't do it again.
				if (Application.isPlaying)
                {
					if (thisDCA.activeRace.data != null)
                    {
						if (thisDCA.activeRace.data.raceName == (string)thisRaceSetter.FindPropertyRelative("name").stringValue)
                        {
							okToProcess = false;
                        }
                    }
                }

				if (okToProcess)
				{
					thisDCA.ChangeRace((string)thisRaceSetter.FindPropertyRelative("name").stringValue, DynamicCharacterAvatar.ChangeRaceOptions.useDefaults, true);
					//Changing the race may cause umaRecipe, animationController to change so forcefully update these too
					umaRecipe.objectReferenceValue = thisDCA.umaRecipe;
					animationController.objectReferenceValue = thisDCA.animationController;
					serializedObject.ApplyModifiedProperties();
					GenerateSingleUMA(thisDCA.rebuildSkeleton);
				}
			}
			if (showHelp)
			{
				EditorGUILayout.HelpBox("Active Race: Sets the race of the character, which defines the base recipe to build the character, the available DNA, and the available wardrobe.", MessageType.Info);
			}


			//**************************************
			// Begin In-Editor customization
			//**************************************
			showEditorCustomization = EditorGUILayout.Foldout(showEditorCustomization,new GUIContent("Customization","Properties for customizing the look of the UMA"));

			if (showEditorCustomization)
            {
				BeginVerticalPadded();

				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Save Preset"))
                {
					string fileName = EditorUtility.SaveFilePanel("Save Preset", "", "DCAPreset", "umapreset");
					if (!string.IsNullOrEmpty(fileName))
                    {
						try
						{
							UMAPreset prs = new UMAPreset();
							prs.DefaultColors = thisDCA.characterColors;
							prs.PredefinedDNA = thisDCA.predefinedDNA;
							prs.DefaultWardrobe = thisDCA.preloadWardrobeRecipes;
							string presetstring = JsonUtility.ToJson(prs);
							System.IO.File.WriteAllText(fileName, presetstring);
						}
						catch(Exception ex)
                        {
							Debug.LogException(ex);
							EditorUtility.DisplayDialog("Error", "Error writing preset file: " + ex.Message,"OK");
                        }
                    }
                }
				if (GUILayout.Button("Load Preset"))
                {
					string fileName = EditorUtility.OpenFilePanel("Load Preset", "", "umapreset");
					if (!string.IsNullOrEmpty(fileName))
                    {
						try
                        {
							string presetstring = System.IO.File.ReadAllText(fileName);
							thisDCA.InitializeFromPreset(presetstring);
							UpdateCharacter();
                        }
						catch(Exception ex)
                        {
							Debug.LogException(ex);
							EditorUtility.DisplayDialog("Error", "Error writing preset file: " + ex.Message, "OK");
						}
					}
                }
				if (GUILayout.Button("Save Legacy File"))
				{
					string fileName = EditorUtility.SaveFilePanel("Save Legacy File", "", "", "crs");
					if (!string.IsNullOrEmpty(fileName))
					{
						try
						{
							string charstr = thisDCA.GetCurrentRecipe(false);
							System.IO.File.WriteAllText(fileName, charstr);
						}
						catch (Exception ex)
						{
							Debug.LogException(ex);
							EditorUtility.DisplayDialog("Error", "Error writing preset file: " + ex.Message, "OK");
						}
					}
				}
				EditorGUILayout.EndHorizontal();
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(serializedObject.FindProperty("editorTimeGeneration"));
				if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    UpdateCharacter();
                }

                //******************************************************************
                // Preload wardrobe
                //Other DCA propertyDrawers
                //in order for the "preloadWardrobeRecipes" prop to properly check if it can load the recipies it gets assigned to it
                //it needs to know that its part of this DCA
                SerializedProperty thisPreloadWardrobeRecipes = serializedObject.FindProperty("preloadWardrobeRecipes");
				Rect pwrCurrentRect = EditorGUILayout.GetControlRect(false, _wardrobePropDrawer.GetPropertyHeight(thisPreloadWardrobeRecipes, GUIContent.none));
				_wardrobePropDrawer.OnGUI(pwrCurrentRect, thisPreloadWardrobeRecipes, new GUIContent(thisPreloadWardrobeRecipes.displayName));
				if (showHelp)
				{
					EditorGUILayout.HelpBox("Preload Wardrobe: Sets the default wardrobe recipes to use on the Avatar. This is useful when creating specific Avatar prefabs.", MessageType.Info);
				}
				if (_wardrobePropDrawer.changed)
				{
					serializedObject.ApplyModifiedProperties();
					if (Application.isPlaying)
					{
						thisDCA.ClearSlots();
						thisDCA.LoadDefaultWardrobe();
						thisDCA.BuildCharacter(true);
					}
					else
					{
						GenerateSingleUMA();
					}
				}
				// *********************************************************************************
				// 
				//NewCharacterColors
				SerializedProperty characterColors = serializedObject.FindProperty("characterColors");
				SerializedProperty newCharacterColors = characterColors.FindPropertyRelative("_colors");
				GUILayout.BeginHorizontal();
				GUILayout.Space(2);
				//for ColorValues as OverlayColorDatas we need to outout something that looks like a list but actully uses a method to add/remove colors because we need the new OverlayColorData to have 3 channels	
				newCharacterColors.isExpanded = EditorGUILayout.Foldout(newCharacterColors.isExpanded, new GUIContent("Character Colors"));
				GUILayout.EndHorizontal();
				var n_origArraySize = newCharacterColors.arraySize;
				var n_newArraySize = n_origArraySize;
				EditorGUI.BeginChangeCheck();
				if (newCharacterColors.isExpanded)
				{
					currentcolorfilter = EditorGUILayout.Popup("Filter Colors", currentcolorfilter, colorfilters);

					n_newArraySize = EditorGUILayout.DelayedIntField(new GUIContent("Size"), n_origArraySize);
					EditorGUILayout.Space();
					EditorGUI.indentLevel++;
					if (n_origArraySize > 0)
					{
						for (int i = 0; i < n_origArraySize; i++)
						{
							SerializedProperty currentColor = newCharacterColors.GetArrayElementAtIndex(i);
							if (currentcolorfilter == 0 && !baseColorNames.Contains(currentColor.displayName.ToLower())) continue;
							if (currentcolorfilter == 2 && currentColor.displayName.ToLower().Contains("colordna")) continue;
							EditorGUILayout.PropertyField(newCharacterColors.GetArrayElementAtIndex(i));
						}
					}
					EditorGUI.indentLevel--;
				}
				if (showHelp)
				{
					EditorGUILayout.HelpBox("Character Colors: This lets you set predefined colors to be used when building the Avatar. The colors will be assigned to the Shared Colors on the overlays as they are applied to the Avatar.", MessageType.Info);
				}
				if (EditorGUI.EndChangeCheck())
				{
					if (n_newArraySize != n_origArraySize)
					{
						SetNewColorCount(n_newArraySize);//this is not prompting a save so mark the scene dirty...
						if (!Application.isPlaying)
							EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
					}
					serializedObject.ApplyModifiedProperties();
					if (Application.isPlaying)
						thisDCA.UpdateColors(true);
					else
					{
						GenerateSingleUMA();
						//thisDCA.UpdateColors(false); // todo: this block is losing all the colors in the recipe somehow...
						//thisDCA.umaData.isTextureDirty = true;
						//UpdateUMA();
					}
				}

				//***********************************************************************************
				// Predefined DNA
				//***********************************************************************************

				// Dropdown of the current DNA.
				// button to "add" it.

				showPrefinedDNA = EditorGUILayout.Foldout(showPrefinedDNA, "Predefined DNA");
				if (showPrefinedDNA)
				{
					if (cachedRace != thisDCA.activeRace.name)
					{
						cachedRace = thisDCA.activeRace.name;
						rawcachedRaceDNA = thisDCA.activeRace.data.GetDNANames().ToArray();
						List<string> MenuDNA = new List<string>();
						foreach(string s in rawcachedRaceDNA)
                        {
							MenuDNA.Add(s.MenuCamelCase());
                        }
						cachedRaceDNA = MenuDNA.ToArray();
					}

					GUILayout.BeginHorizontal();
					currentDNA = EditorGUILayout.Popup(currentDNA, cachedRaceDNA);
					if (GUILayout.Button("Add DNA"))
					{
						string theDna = rawcachedRaceDNA[currentDNA];

						if (thisDCA.predefinedDNA == null)
						{
							thisDCA.predefinedDNA = new UMAPredefinedDNA();
						}
						if (thisDCA.predefinedDNA.ContainsName(theDna))
						{
							EditorUtility.DisplayDialog("Error", "Predefined DNA Already contains DNA: " + theDna, "OK");
						}
						else
                        {
                            AddSingleDNA(theDna);
                        }
                    }
					if (GUILayout.Button("Add All"))
                    {
						foreach(string s in rawcachedRaceDNA)
                        {
							if (!thisDCA.predefinedDNA.ContainsName(s))
							{
								AddSingleDNA(s);
							}
						}
                    }
					GUILayout.EndHorizontal();

					if (thisDCA.predefinedDNA != null)
					{
						string delme = "";
						EditorGUI.BeginChangeCheck();
						foreach (var pd in thisDCA.predefinedDNA.PreloadValues)
						{
							GUILayout.BeginHorizontal();
							GUILayout.Label(ObjectNames.NicifyVariableName(pd.Name), GUILayout.Width(100));
							pd.Value = GUILayout.HorizontalSlider(pd.Value, 0.0f, 1.0f);

							bool delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
							if (delete)
							{
								delme = pd.Name;
							}
							GUILayout.EndHorizontal();
						}
						if (!string.IsNullOrEmpty(delme))
						{
							thisDCA.predefinedDNA.RemoveDNA(delme);
							GenerateSingleUMA();
							Repaint();
						}
						if (EditorGUI.EndChangeCheck())
						{
							GenerateSingleUMA();
						}
					}
				}
				if (showHelp)
                {
					EditorGUILayout.HelpBox("Predefined DNA is loaded onto the character in the initial character build. Select the DNA in the dropdown, and add it to the list of DNA to load, then edit the values as needed.",MessageType.Info);
                }
				EndVerticalPadded();
			}

			//**************************************
			// End In-Editor customization
			//**************************************


			//the ChangeRaceOptions
			SerializedProperty defaultChangeRaceOptions = serializedObject.FindProperty("defaultChangeRaceOptions");
			defaultChangeRaceOptions.isExpanded = EditorGUILayout.Foldout(defaultChangeRaceOptions.isExpanded, new GUIContent("Race Change Options", "The default options for when the Race is changed. These can be overidden when calling 'ChangeRace' directly."));
			if (defaultChangeRaceOptions.isExpanded)
			{
				BeginVerticalPadded();
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(defaultChangeRaceOptions, GUIContent.none);
				EditorGUI.indentLevel++;
				EditorGUILayout.PropertyField(serializedObject.FindProperty("cacheCurrentState"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("rebuildSkeleton"));
				EditorGUI.indentLevel--;
				if (EditorGUI.EndChangeCheck())
				{
					serializedObject.ApplyModifiedProperties();
				}
				EndVerticalPadded();
			}


			//Move UMAAddidtionalRecipes out of advanced into its own section
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(umaAdditionalRecipes, new GUIContent("Additional Utility Recipes", "Additional Recipes to add when the character is generated, like the capsuleCollider recipe for example"), true);
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}
			GUILayout.Space(2f);

			SerializedProperty thisRaceAnimationControllers = serializedObject.FindProperty("raceAnimationControllers");
			Rect racCurrentRect = EditorGUILayout.GetControlRect(false, _animatorPropDrawer.GetPropertyHeight(thisRaceAnimationControllers, GUIContent.none));
			EditorGUI.BeginChangeCheck();
			_animatorPropDrawer.OnGUI(racCurrentRect, thisRaceAnimationControllers, new GUIContent(thisRaceAnimationControllers.displayName));
			if (showHelp)
			{
				EditorGUILayout.HelpBox("Race Animation Controllers: This sets the animation controllers used for each race. When changing the race, the animation controller for the active race will be used by default.", MessageType.Info);
			}
			//EditorGUI.BeginChangeCheck();
			//EditorGUILayout.PropertyField(serializedObject.FindProperty("raceAnimationControllers"));
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				if (Application.isPlaying)
				{
					thisDCA.SetExpressionSet();//this triggers any expressions to reset.
					thisDCA.SetAnimatorController();
				}
			}

			GUILayout.Space(2f);
			//Load save fields
			EditorGUI.BeginChangeCheck();
			SerializedProperty loadPathType = serializedObject.FindProperty("loadPathType");
			loadPathType.isExpanded = EditorGUILayout.Foldout(loadPathType.isExpanded, "Load/Save Options");
			if (loadPathType.isExpanded)
			{
				SerializedProperty loadString = serializedObject.FindProperty("loadString");
				SerializedProperty loadPath = serializedObject.FindProperty("loadPath");
				SerializedProperty loadFilename = serializedObject.FindProperty("loadFilename");
				SerializedProperty loadFileOnStart = serializedObject.FindProperty("loadFileOnStart");
				SerializedProperty savePathType = serializedObject.FindProperty("savePathType");
				SerializedProperty savePath = serializedObject.FindProperty("savePath");
				SerializedProperty saveFilename = serializedObject.FindProperty("saveFilename");
				//LoadSave Flags
				SerializedProperty defaultLoadOptions = serializedObject.FindProperty("defaultLoadOptions");
				SerializedProperty defaultSaveOptions = serializedObject.FindProperty("defaultSaveOptions");
				//extra LoadSave Options in addition to flags
				//SerializedProperty waitForBundles = serializedObject.FindProperty("waitForBundles");
				SerializedProperty makeUniqueFilename = serializedObject.FindProperty("makeUniqueFilename");
				SerializedProperty ensureSharedColors = serializedObject.FindProperty("ensureSharedColors");

				EditorGUILayout.PropertyField(loadPathType);

				if (loadPathType.enumValueIndex == Convert.ToInt32(DynamicCharacterAvatar.loadPathTypes.String))
				{
					EditorGUILayout.PropertyField(loadString);
				}
				else
				{
					if (loadPathType.enumValueIndex <= 1)
					{
						EditorGUILayout.PropertyField(loadPath);

					}
				}

				EditorGUILayout.PropertyField(loadFilename);
				if (loadFilename.stringValue != "")
				{
					EditorGUILayout.PropertyField(loadFileOnStart);
				}
				EditorGUI.indentLevel++;
				//LoadOptionsFlags
				defaultLoadOptions.isExpanded = EditorGUILayout.Foldout(defaultLoadOptions.isExpanded, new GUIContent("Load Options", "The default options for when a character is loaded from an UMATextRecipe asset or a recipe string. Can be overidden when calling 'LoadFromRecipe' or 'LoadFromString' directly."));
				if (defaultLoadOptions.isExpanded)
				{
					EditorGUILayout.PropertyField(defaultLoadOptions, GUIContent.none);
					EditorGUI.indentLevel++;
					//waitForBundles.boolValue = EditorGUILayout.ToggleLeft(new GUIContent(waitForBundles.displayName, waitForBundles.tooltip), waitForBundles.boolValue);
					//buildAfterLoad.boolValue = EditorGUILayout.ToggleLeft(new GUIContent(buildAfterLoad.displayName, buildAfterLoad.tooltip), buildAfterLoad.boolValue);
					//just drawing these as propertyFields because the toolTip on toggle left doesn't work
					//EditorGUILayout.PropertyField(waitForBundles);
					EditorGUI.indentLevel--;
				}
				EditorGUI.indentLevel--;
				if (Application.isPlaying)
				{
					if (GUILayout.Button("Perform Load"))
					{
						thisDCA.DoLoad();
					}
				}
				EditorGUILayout.Space();
				EditorGUILayout.PropertyField(savePathType);
				if (savePathType.enumValueIndex <= 2)
				{
					EditorGUILayout.PropertyField(savePath);
				}
				EditorGUILayout.PropertyField(saveFilename);
				EditorGUI.indentLevel++;
				defaultSaveOptions.isExpanded = EditorGUILayout.Foldout(defaultSaveOptions.isExpanded, new GUIContent("Save Options", "The default options for when a character is save to UMATextRecipe asset or a txt. Can be overidden when calling 'DoSave' directly."));
				if (defaultSaveOptions.isExpanded)
				{
					EditorGUILayout.PropertyField(defaultSaveOptions, GUIContent.none);
					EditorGUI.indentLevel++;
					//ensureSharedColors.boolValue = EditorGUILayout.ToggleLeft(new GUIContent(ensureSharedColors.displayName, ensureSharedColors.tooltip), ensureSharedColors.boolValue);
					//makeUniqueFilename.boolValue = EditorGUILayout.ToggleLeft(new GUIContent(makeUniqueFilename.displayName, makeUniqueFilename.tooltip), makeUniqueFilename.boolValue);
					//just drawing these as propertyFields because the toolTip on toggle left doesn't work
					EditorGUILayout.PropertyField(ensureSharedColors);
					EditorGUILayout.PropertyField(makeUniqueFilename);
					EditorGUI.indentLevel--;
				}
				EditorGUI.indentLevel--;
				if (Application.isPlaying)
				{
					if (GUILayout.Button("Perform Save"))
					{
						thisDCA.DoSave();
					}
				}
				EditorGUILayout.Space();
			}
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}
			GUILayout.Space(2f);
			//for CharacterEvents
			EditorGUI.BeginChangeCheck();
			SerializedProperty CharacterCreated = serializedObject.FindProperty("CharacterCreated");
			CharacterCreated.isExpanded = EditorGUILayout.Foldout(CharacterCreated.isExpanded, "Character Events");
			if (CharacterCreated.isExpanded)
			{
				SerializedProperty CharacterBegun = serializedObject.FindProperty("CharacterBegun");
				SerializedProperty CharacterUpdated = serializedObject.FindProperty("CharacterUpdated");
				SerializedProperty CharacterDestroyed = serializedObject.FindProperty("CharacterDestroyed");
				SerializedProperty CharacterDnaUpdated = serializedObject.FindProperty("CharacterDnaUpdated");
				SerializedProperty RecipeUpdated = serializedObject.FindProperty("RecipeUpdated");
				SerializedProperty AnimatorSaved = serializedObject.FindProperty("AnimatorStateSaved");
				SerializedProperty AnimatorRestored = serializedObject.FindProperty("AnimatorStateRestored");
				SerializedProperty WardrobeAdded = serializedObject.FindProperty("WardrobeAdded");
				SerializedProperty WardrobeRemoved = serializedObject.FindProperty("WardrobeRemoved");

				EditorGUILayout.PropertyField(CharacterBegun);
				EditorGUILayout.PropertyField(CharacterCreated);
				EditorGUILayout.PropertyField(CharacterUpdated);
				EditorGUILayout.PropertyField(CharacterDestroyed);
				EditorGUILayout.PropertyField(CharacterDnaUpdated);
				EditorGUILayout.PropertyField(RecipeUpdated);
				EditorGUILayout.PropertyField(AnimatorSaved);
				EditorGUILayout.PropertyField(AnimatorRestored);
				EditorGUILayout.PropertyField(WardrobeAdded);
				EditorGUILayout.PropertyField(WardrobeRemoved);
			}
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}
			GUILayout.Space(2f);
			//for AdvancedOptions
			EditorGUI.BeginChangeCheck();
			context.isExpanded = EditorGUILayout.Foldout(context.isExpanded, "Advanced Options");
			if (context.isExpanded)
			{
				EditorGUI.BeginChangeCheck();
				EditorGUILayout.PropertyField(serializedObject.FindProperty("alwaysRebuildSkeleton"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("hide"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("DelayUnload"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("BundleCheck"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("AtlasResolutionScale"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultRendererAsset"));

				if (EditorGUI.EndChangeCheck())
				{
					serializedObject.ApplyModifiedProperties();
				}
				if (showHelp)
				{
					EditorGUILayout.HelpBox("Hide: This disables the display of the Avatar without preventing it from being generated. If you want to prevent the character from being generated at all disable the DynamicCharacterAvatar component itself.", MessageType.Info);
				}
				//for _buildCharacterEnabled we want to set the value using the DCS BuildCharacterEnabled property because this actually triggers BuildCharacter
				var buildCharacterEnabled = serializedObject.FindProperty("_buildCharacterEnabled");
				var buildCharacterEnabledValue = buildCharacterEnabled.boolValue;
				EditorGUI.BeginChangeCheck();
				var buildCharacterEnabledNewValue = EditorGUILayout.Toggle(new GUIContent(buildCharacterEnabled.displayName, "Builds the character on recipe load or race changed. If you want to load multiple recipes into a character you can disable this and enable it when you are done. By default this should be true."), buildCharacterEnabledValue);
				if (EditorGUI.EndChangeCheck())
				{
					if (buildCharacterEnabledNewValue != buildCharacterEnabledValue)
						thisDCA.BuildCharacterEnabled = buildCharacterEnabledNewValue;
					serializedObject.ApplyModifiedProperties();
				}
				if (showHelp)
				{
					EditorGUILayout.HelpBox("Build Character Enabled: Builds the character on recipe load or race changed. If you want to load multiple recipes into a character you can disable this and enable it when you are done. By default this should be true.", MessageType.Info);
				}
				EditorGUILayout.PropertyField(serializedObject.FindProperty("loadBlendShapes"), new GUIContent("Load BlendShapes"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("keepAvatar"), new GUIContent("Keep Avatar"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("KeepAnimatorController"), new GUIContent("Keep Animator Controller"));
				EditorGUILayout.PropertyField(context);
				EditorGUILayout.PropertyField(umaData);
				EditorGUILayout.PropertyField(umaGenerator);
				EditorGUILayout.Space();
//				EditorGUILayout.PropertyField(umaRecipe);
				EditorGUILayout.PropertyField(animationController);
				EditorGUILayout.PropertyField(serializedObject.FindProperty("BoundsOffset"));
			}
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}
			GUILayout.Space(2f);
			//for PlaceholderOptions
			EditorGUI.BeginChangeCheck();
			SerializedProperty gizmo = serializedObject.FindProperty("showPlaceholder");
			SerializedProperty enableGizmo = serializedObject.FindProperty("showPlaceholder");
			SerializedProperty previewModel = serializedObject.FindProperty("previewModel");
			SerializedProperty customModel = serializedObject.FindProperty("customModel");
			SerializedProperty customRotation = serializedObject.FindProperty("customRotation");
			SerializedProperty previewColor = serializedObject.FindProperty("previewColor");
			gizmo.isExpanded = EditorGUILayout.Foldout(gizmo.isExpanded, "Placeholder Options");
			if (gizmo.isExpanded)
			{
				EditorGUILayout.PropertyField(enableGizmo);
				EditorGUILayout.PropertyField(previewModel);
				if (previewModel.enumValueIndex == 2)
				{
					EditorGUILayout.PropertyField(customModel);
					EditorGUILayout.PropertyField(customRotation);
				}
				EditorGUILayout.PropertyField(previewColor);
			}
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}

			if (Application.isPlaying)
			{
				showWardrobe = EditorGUILayout.Foldout(showWardrobe, "Current Wardrobe");
				if (showWardrobe)
				{

					EditorGUI.indentLevel++;
					Dictionary<string, UMATextRecipe> currentWardrobe = thisDCA.WardrobeRecipes;

					foreach (KeyValuePair<string, UMATextRecipe> item in currentWardrobe)
					{
						GUILayout.BeginHorizontal();
						EditorGUI.BeginDisabledGroup(true);
						EditorGUILayout.LabelField(item.Key, GUILayout.Width(88.0f));
						EditorGUILayout.TextField(item.Value.DisplayValue + " (" + item.Value.name + ")");
						EditorGUI.EndDisabledGroup();
						if (GUILayout.Button("Inspect", EditorStyles.toolbarButton, GUILayout.Width(40)))
						{
							InspectorUtlity.InspectTarget(item.Value);
						}
						GUILayout.EndHorizontal();
					}
					EditorGUI.indentLevel--;
				}
			}

			List<GameObject> GetRenderers(GameObject parent)
			{
				List<GameObject> objs = new List<GameObject>();
				foreach (Transform t in parent.transform)
				{
					if (t.GetComponent<SkinnedMeshRenderer>() != null)
						objs.Add(t.gameObject);
				}
				return objs;
			}

			/* void UpdateUMA()
			{
				UMAGenerator ugb = UMAContext.Instance.gameObject.GetComponentInChildren<UMAGenerator>();
				if (ugb == null)
				{
					EditorUtility.DisplayDialog("Error", "Cannot find generator!", "OK");
				}
				else
				{
					DynamicCharacterAvatar dca = target as DynamicCharacterAvatar;
					bool oldFastGen = ugb.fastGeneration;
					ugb.fastGeneration = true;
					ugb.FreezeTime = true;
					ugb.GenerateSingleUMA(dca.umaData);
					ugb.fastGeneration = oldFastGen;
					ugb.FreezeTime = false;
				}
			} */

			void GenerateSingleUMA(bool rebuild=false)
			{
				if (Application.isPlaying)
					return;

				if (thisDCA.editorTimeGeneration == false)
					return;

				// Debug.Log("prefab instance asset type: " + PrefabUtility.GetPrefabInstanceStatus(thisDCA.gameObject) + ", asset type: " + PrefabUtility.GetPrefabAssetType(thisDCA.gameObject));

				// Don't generate UMAs from project prefabs or if the gameObject is not active.
				if (!thisDCA.gameObject.activeInHierarchy)//PrefabUtility.GetPrefabInstanceStatus(thisDCA.gameObject) == PrefabInstanceStatus.NotAPrefab && PrefabUtility.GetPrefabAssetType(thisDCA.gameObject) != PrefabAssetType.NotAPrefab)
				{
					return;
				}

				UMAGenerator ugb = UMAContext.Instance.gameObject.GetComponentInChildren<UMAGenerator>();
				if (ugb == null)
				{
					EditorUtility.DisplayDialog("Error", "Cannot find generator!", "OK");
				}
				else
				{

					DynamicCharacterAvatar dca = target as DynamicCharacterAvatar;

					CleanupGeneratedData(rebuild);

					dca.activeRace.SetRaceData();
					if (dca.activeRace.racedata == null)
                    {
						return;
                    }

					dca.LoadDefaultWardrobe();

					// save the predefined DNA...
					var dna = dca.predefinedDNA.Clone();
					dca.BuildCharacter(false, true);
					dca.predefinedDNA = dna;

					bool oldFastGen = ugb.fastGeneration;
					int oldScaleFactor = ugb.InitialScaleFactor;
					int oldAtlasResolution = ugb.atlasResolution;

					ugb.FreezeTime = true;
					ugb.fastGeneration = true;
					ugb.InitialScaleFactor = ugb.editorInitialScaleFactor;
					ugb.atlasResolution = ugb.editorAtlasResolution;


					dca.activeRace.racedata.ResetDNA();

					ugb.GenerateSingleUMA(dca.umaData,false);
					
					ugb.fastGeneration = oldFastGen;
					ugb.FreezeTime = false;
					ugb.InitialScaleFactor = oldScaleFactor;
					ugb.atlasResolution = oldAtlasResolution;

					var mountedItems = dca.gameObject.GetComponentsInChildren<UMAMountedItem>();
					foreach (var mi in mountedItems)
                    {
						mi.ResetMountPoint();
                    }
				}
			}

			void CleanupGeneratedData(bool clear)
			{
				if (Application.isPlaying)
					return;
				List<GameObject> Cleaners = GetRenderers(thisDCA.gameObject);
				thisDCA.Hide(clear);
				foreach (GameObject go in Cleaners)
				{
					DestroyImmediate(go);
				}
				DestroyImmediate(thisDCA.umaData);
				thisDCA.umaData = null;
				thisDCA.ClearSlots();
			}

            void UpdateCharacter()
            {
                if (thisDCA.gameObject.scene != default)
                {
                    if (thisDCA.editorTimeGeneration)
                    {
                        GenerateSingleUMA();
                    }
                    else
                    {
                        CleanupGeneratedData(true);
                    }
                }
            }
        }

        private void AddSingleDNA(string theDna)
        {
            float value = 0.5f;

            if (thisDCA.umaData != null)
            {
                var characterDNA = thisDCA.GetDNA();
                if (characterDNA != null)
                {
                    if (characterDNA.ContainsKey(theDna))
                    {
                        value = characterDNA[theDna].Value;
                    }
                }
            }
            thisDCA.predefinedDNA.AddDNA(theDna, value);
        }
    }
}
