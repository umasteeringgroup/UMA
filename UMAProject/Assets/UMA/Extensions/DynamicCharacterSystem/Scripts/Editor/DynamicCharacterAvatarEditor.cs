using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UMA;
using UMACharacterSystem;
using System;

[CustomEditor(typeof(DynamicCharacterAvatar), true)]
public partial class DynamicCharacterAvatarEditor : Editor
{
    protected DynamicCharacterAvatar thisDCA;
    private RaceSetterPropertyDrawer _racePropDrawer = new RaceSetterPropertyDrawer();

    public void OnEnable()
    {
        thisDCA = target as DynamicCharacterAvatar;
        //Set this DynamicCharacterAvatar for RaceSetter so if the user chages the race dropdown the race changes
        if(_racePropDrawer.thisDCA == null)
        {
            _racePropDrawer.thisDCA = thisDCA;
            //Set the raceLibrary for the race setter
            var context = UMAContext.FindInstance();
            var dynamicRaceLibrary = (DynamicRaceLibrary)context.raceLibrary as DynamicRaceLibrary;
            _racePropDrawer.thisDynamicRaceLibrary = dynamicRaceLibrary;
        }
    }

    protected bool characterAvatarLoadSaveOpen;
    public override void OnInspectorGUI()
    {
        Editor.DrawPropertiesExcluding(serializedObject, new string[] { "activeRace","preloadWardrobeRecipes", "raceAnimationControllers",
			"characterColors", "loadString", "loadFileOnStart", "loadPathType", "loadPath", "loadFilename","loadOptions", "savePathType",
			"savePath", "saveFilename", "makeUnique", "BoundsOffset",
			/*Moved into AdvancedOptions*/"context","umaData","umaRecipe", "umaAdditionalRecipes","umaGenerator", "animationController",
			/*Moved into CharacterEvents*/"CharacterCreated", "CharacterUpdated", "CharacterDestroyed", "RecipeUpdated" });
        serializedObject.ApplyModifiedProperties();
        SerializedProperty thisRaceSetter = serializedObject.FindProperty("activeRace");
        Rect currentRect = EditorGUILayout.GetControlRect(false, _racePropDrawer.GetPropertyHeight(thisRaceSetter, GUIContent.none));
		_racePropDrawer.OnGUI(currentRect, thisRaceSetter, new GUIContent(thisRaceSetter.displayName));
		//Other DCA propertyDrawers
		GUILayout.Space(2f);
		EditorGUI.BeginChangeCheck();
		EditorGUILayout.PropertyField(serializedObject.FindProperty("preloadWardrobeRecipes"));
		if (EditorGUI.EndChangeCheck())
		{
			serializedObject.ApplyModifiedProperties();
			if (Application.isPlaying)
			{
				thisDCA.ClearSlots();
				thisDCA.LoadDefaultWardrobe();
				thisDCA.BuildCharacter();
			}
		}
		EditorGUI.BeginChangeCheck();
		EditorGUILayout.PropertyField(serializedObject.FindProperty("raceAnimationControllers"));
		if (EditorGUI.EndChangeCheck())
		{
			serializedObject.ApplyModifiedProperties();
			if (Application.isPlaying)
			{
				thisDCA.SetExpressionSet();//this triggers any expressions to reset.
				thisDCA.SetAnimatorController();
			}
		}
		//GUILayout.Space(1f);
		EditorGUI.BeginChangeCheck();
		SerializedProperty characterColors = serializedObject.FindProperty("characterColors");
		//Rect charColsRect = EditorGUILayout.GetControlRect();
		//EditorGUI.PropertyField(charColsRect,characterColors.FindPropertyRelative("Colors"), new GUIContent("Character Colors"),true);
		EditorGUILayout.PropertyField(characterColors.FindPropertyRelative("Colors"), new GUIContent("Character Colors"), true);
		if (EditorGUI.EndChangeCheck())
		{
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
            SerializedProperty loadOptions = serializedObject.FindProperty("loadOptions");
            SerializedProperty savePathType = serializedObject.FindProperty("savePathType");
            SerializedProperty savePath = serializedObject.FindProperty("savePath");
            SerializedProperty saveFilename = serializedObject.FindProperty("saveFilename");
            SerializedProperty makeUnique = serializedObject.FindProperty("makeUnique");

			EditorGUILayout.PropertyField(loadPathType);

			if (loadPathType.enumValueIndex == Convert.ToInt32(DynamicCharacterAvatar.loadPathTypes.String))
            {
                EditorGUILayout.PropertyField(loadString);
            }
            else
            {
                if (loadPathType.enumValueIndex <= 2)
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
            EditorGUILayout.PropertyField(loadOptions, true);
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
            EditorGUILayout.PropertyField(makeUnique);
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
			SerializedProperty RecipeUpdated = serializedObject.FindProperty("RecipeUpdated");

			EditorGUILayout.PropertyField(CharacterCreated);
			EditorGUILayout.PropertyField(CharacterUpdated);
			EditorGUILayout.PropertyField(CharacterDestroyed);
			EditorGUILayout.PropertyField(RecipeUpdated);
		}
		if (EditorGUI.EndChangeCheck())
		{
			serializedObject.ApplyModifiedProperties();
		}
		GUILayout.Space(2f);
		//for AdvancedOptions
		EditorGUI.BeginChangeCheck();
		SerializedProperty context = serializedObject.FindProperty("context");
		context.isExpanded = EditorGUILayout.Foldout(context.isExpanded, "Advanced Options");
		if (context.isExpanded)
		{
			SerializedProperty umaData = serializedObject.FindProperty("umaData");
			SerializedProperty umaGenerator = serializedObject.FindProperty("umaGenerator");
			SerializedProperty umaRecipe = serializedObject.FindProperty("umaRecipe");
			SerializedProperty umaAdditionalRecipes = serializedObject.FindProperty("umaAdditionalRecipes");
			SerializedProperty animationController = serializedObject.FindProperty("animationController");

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

		/*EditorGUI.BeginChangeCheck();
        
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
        }*/
		GUILayout.Space(2f);
		if (Application.isPlaying)
        {
            EditorGUILayout.LabelField("AssetBundles used by Avatar");
            SerializedProperty assetBundlesUsedbyCharacter = serializedObject.FindProperty("assetBundlesUsedbyCharacter");
            string assetBundlesUsed = "";
            if (assetBundlesUsedbyCharacter.arraySize == 0)
            {
                assetBundlesUsed = "None";
            }
            else
            {
                for (int i = 0; i < assetBundlesUsedbyCharacter.arraySize; i++)
                {
                    assetBundlesUsed = assetBundlesUsed + assetBundlesUsedbyCharacter.GetArrayElementAtIndex(i).stringValue;
                    if (i < (assetBundlesUsedbyCharacter.arraySize - 1))
                        assetBundlesUsed = assetBundlesUsed + "\n";
                }
            }
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextArea(assetBundlesUsed);
            EditorGUI.EndDisabledGroup();
        }
    }
}

