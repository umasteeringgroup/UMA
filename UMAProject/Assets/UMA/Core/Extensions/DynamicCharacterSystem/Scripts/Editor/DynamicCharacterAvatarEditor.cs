using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;

namespace UMA.CharacterSystem.Editors
{
	[CustomEditor(typeof(DynamicCharacterAvatar), true)]
   public partial class DynamicCharacterAvatarEditor : Editor
	{
		public bool showHelp = false;

		protected DynamicCharacterAvatar thisDCA;
		protected RaceSetterPropertyDrawer _racePropDrawer = new RaceSetterPropertyDrawer();
		protected WardrobeRecipeListPropertyDrawer _wardrobePropDrawer = new WardrobeRecipeListPropertyDrawer();
		protected RaceAnimatorListPropertyDrawer _animatorPropDrawer = new RaceAnimatorListPropertyDrawer();

		public void OnEnable()
		{
			thisDCA = target as DynamicCharacterAvatar;
			if (thisDCA.context == null)
			{
				thisDCA.context = UMAContext.FindInstance();
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
			_racePropDrawer.thisDynamicRaceLibrary = (DynamicRaceLibrary)thisDCA.context.raceLibrary as DynamicRaceLibrary;
			_wardrobePropDrawer.thisDCA = thisDCA;
			_wardrobePropDrawer.thisDCS = (DynamicCharacterSystem)thisDCA.context.dynamicCharacterSystem as DynamicCharacterSystem;
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

		public override void OnInspectorGUI()
		{
			serializedObject.Update();
		   Editor.DrawPropertiesExcluding(serializedObject, new string[] { "hide", "loadBlendShapes","activeRace","defaultChangeRaceOptions","cacheCurrentState", "rebuildSkeleton", "preloadWardrobeRecipes", "raceAnimationControllers",
				"characterColors","BoundsOffset","_buildCharacterEnabled",
				/*LoadOtions fields*/ "defaultLoadOptions", "loadPathType", "loadPath", "loadFilename", "loadString", "loadFileOnStart", "waitForBundles", /*"buildAfterLoad",*/
				/*SaveOptions fields*/ "defaultSaveOptions", "savePathType","savePath", "saveFilename", "makeUniqueFilename","ensureSharedColors", 
				/*Moved into AdvancedOptions*/"context","umaData","umaRecipe", "umaAdditionalRecipes","umaGenerator", "animationController",
				/*Moved into CharacterEvents*/"CharacterCreated", "CharacterUpdated", "CharacterDestroyed", "CharacterDnaUpdated", "RecipeUpdated",
				/*PlaceholderOptions fields*/"showPlaceholder", "previewModel", "customModel", "customRotation", "previewColor"});

			//The base DynamicAvatar properties- get these early because changing the race changes someof them
			SerializedProperty context = serializedObject.FindProperty("context");
			SerializedProperty umaData = serializedObject.FindProperty("umaData");
			SerializedProperty umaGenerator = serializedObject.FindProperty("umaGenerator");
			SerializedProperty umaRecipe = serializedObject.FindProperty("umaRecipe");
			SerializedProperty umaAdditionalRecipes = serializedObject.FindProperty("umaAdditionalRecipes");
			SerializedProperty animationController = serializedObject.FindProperty("animationController");

			EditorGUI.BeginChangeCheck();
			showHelp = EditorGUILayout.Toggle("Show Help", showHelp);
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}
			SerializedProperty thisRaceSetter = serializedObject.FindProperty("activeRace");
			Rect currentRect = EditorGUILayout.GetControlRect(false, _racePropDrawer.GetPropertyHeight(thisRaceSetter, GUIContent.none));
			EditorGUI.BeginChangeCheck();
			_racePropDrawer.OnGUI(currentRect, thisRaceSetter, new GUIContent(thisRaceSetter.displayName));
			if (EditorGUI.EndChangeCheck())
			{
				thisDCA.ChangeRace((string)thisRaceSetter.FindPropertyRelative("name").stringValue);
				//Changing the race may cause umaRecipe, animationController to change so forcefully update these too
				umaRecipe.objectReferenceValue = thisDCA.umaRecipe;
				animationController.objectReferenceValue = thisDCA.animationController;
				serializedObject.ApplyModifiedProperties();
			}
			if (showHelp)
			{
				EditorGUILayout.HelpBox("Active Race: Sets the race of the character, which defines the base recipe to build the character, the available DNA, and the available wardrobe.", MessageType.Info);
			}
			//the ChangeRaceOptions
			SerializedProperty defaultChangeRaceOptions = serializedObject.FindProperty("defaultChangeRaceOptions");
			EditorGUI.indentLevel++;
			defaultChangeRaceOptions.isExpanded = EditorGUILayout.Foldout(defaultChangeRaceOptions.isExpanded, new GUIContent("Race Change Options", "The default options for when the Race is changed. These can be overidden when calling 'ChangeRace' directly."));
			if (defaultChangeRaceOptions.isExpanded)
			{
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
			}
			EditorGUI.indentLevel--;
			//Other DCA propertyDrawers
			//in order for the "preloadWardrobeRecipes" prop to properly check if it can load the recipies it gets assigned to it
			//it needs to know that its part of this DCA
			GUILayout.Space(2f);
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
			}
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
			//NewCharacterColors
			SerializedProperty characterColors = serializedObject.FindProperty("characterColors");
			SerializedProperty newCharacterColors = characterColors.FindPropertyRelative("_colors");
			//for ColorValues as OverlayColorDatas we need to outout something that looks like a list but actully uses a method to add/remove colors because we need the new OverlayColorData to have 3 channels	
			newCharacterColors.isExpanded = EditorGUILayout.Foldout(newCharacterColors.isExpanded, new GUIContent("Character Colors"));
			var n_origArraySize = newCharacterColors.arraySize;
			var n_newArraySize = n_origArraySize;
			EditorGUI.BeginChangeCheck();
			if (newCharacterColors.isExpanded)
			{
				n_newArraySize = EditorGUILayout.DelayedIntField(new GUIContent("Size"), n_origArraySize);
				EditorGUILayout.Space();
				EditorGUI.indentLevel++;
				if (n_origArraySize > 0)
				{
					for(int i = 0; i < n_origArraySize; i++)
					{
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
				SerializedProperty waitForBundles = serializedObject.FindProperty("waitForBundles");
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
					EditorGUILayout.PropertyField(waitForBundles);
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
				SerializedProperty CharacterUpdated = serializedObject.FindProperty("CharacterUpdated");
				SerializedProperty CharacterDestroyed= serializedObject.FindProperty("CharacterDestroyed");
				SerializedProperty CharacterDnaUpdated = serializedObject.FindProperty ("CharacterDnaUpdated");
				SerializedProperty RecipeUpdated = serializedObject.FindProperty("RecipeUpdated");

				EditorGUILayout.PropertyField(CharacterCreated);
				EditorGUILayout.PropertyField(CharacterUpdated);
				EditorGUILayout.PropertyField(CharacterDestroyed);
				EditorGUILayout.PropertyField (CharacterDnaUpdated);
				EditorGUILayout.PropertyField(RecipeUpdated);
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
				EditorGUILayout.PropertyField(serializedObject.FindProperty("hide"));
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
				EditorGUILayout.PropertyField(serializedObject.FindProperty("loadBlendShapes"), new GUIContent("Load BlendShapes (experimental)"));
				EditorGUILayout.PropertyField(context);
				EditorGUILayout.PropertyField(umaData);
				EditorGUILayout.PropertyField(umaGenerator);
				EditorGUILayout.Space();
				EditorGUILayout.PropertyField(umaRecipe);
				EditorGUILayout.PropertyField(umaAdditionalRecipes, true);
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
				if(previewModel.enumValueIndex == 2)
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
				EditorGUILayout.LabelField("AssetBundles used by Avatar");
				string assetBundlesUsed = "";
				if (thisDCA.assetBundlesUsedbyCharacter.Count == 0)
				{
					assetBundlesUsed = "None";
				}
				else
				{
					for (int i = 0; i < thisDCA.assetBundlesUsedbyCharacter.Count; i++)
					{
						assetBundlesUsed = assetBundlesUsed + thisDCA.assetBundlesUsedbyCharacter[i];
						if (i < (thisDCA.assetBundlesUsedbyCharacter.Count - 1))
							assetBundlesUsed = assetBundlesUsed + "\n";
					}
				}
				EditorGUI.BeginDisabledGroup(true);
				EditorGUILayout.TextArea(assetBundlesUsed);
				EditorGUI.EndDisabledGroup();
			}
		}
	}
}
