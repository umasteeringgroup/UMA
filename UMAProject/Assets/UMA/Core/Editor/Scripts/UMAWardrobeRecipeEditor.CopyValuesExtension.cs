#if UNITY_EDITOR
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public partial class UMAWardrobeRecipeEditor
    {
        private bool showWardrobeValueCopy;
        private UMAWardrobeRecipe sourceWardrobeRecipe;

        private bool copyCompatibleRaces = true;
        private bool copyWardrobeSlot = true;
        private bool copyHides = true;
        private bool copyReplaces = true;
        private bool copySuppressWardrobeSlots = true;

        partial void PreRecipeGUI(ref bool changed)
        {
            DrawWardrobeRecipeValueCopyUI(ref changed);
        }

        partial void PostRecipeGUI(ref bool changed)
        {
        }

        private void DrawWardrobeRecipeValueCopyUI(ref bool changed)
        {
            var targetWardrobeRecipe = target as UMAWardrobeRecipe;
            if (targetWardrobeRecipe == null)
            {
                return;
            }

            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.HelpBox("Value copy is disabled while editing multiple recipes.", MessageType.Info);
                return;
            }

            GUILayout.Space(4f);
            if (GUILayout.Button("Copy Values from another Wardrobe Recipe"))
            {
                showWardrobeValueCopy = !showWardrobeValueCopy;
            }

            if (!showWardrobeValueCopy)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            sourceWardrobeRecipe = EditorGUILayout.ObjectField(
                "Source Wardrobe Recipe",
                sourceWardrobeRecipe,
                typeof(UMAWardrobeRecipe),
                false
            ) as UMAWardrobeRecipe;

            GUILayout.Space(2f);
            copyCompatibleRaces = EditorGUILayout.ToggleLeft("Compatible Races", copyCompatibleRaces);
            copyWardrobeSlot = EditorGUILayout.ToggleLeft("Wardrobe Slot", copyWardrobeSlot);
            copyHides = EditorGUILayout.ToggleLeft("Hides Base Slot(s)", copyHides);
            copyReplaces = EditorGUILayout.ToggleLeft("Replaces", copyReplaces);
            copySuppressWardrobeSlots = EditorGUILayout.ToggleLeft("Wardrobe Slots to Suppress", copySuppressWardrobeSlots);

            bool hasSource = sourceWardrobeRecipe != null;
            bool isSelfReference = sourceWardrobeRecipe == targetWardrobeRecipe;
            bool hasAnySelection = copyCompatibleRaces || copyWardrobeSlot || copyHides || copyReplaces || copySuppressWardrobeSlots;

            if (!hasSource)
            {
                EditorGUILayout.HelpBox("Assign a source wardrobe recipe to copy from.", MessageType.Info);
            }
            else if (isSelfReference)
            {
                EditorGUILayout.HelpBox("Source and target recipes are the same. Choose a different source recipe.", MessageType.Warning);
            }
            else if (!hasAnySelection)
            {
                EditorGUILayout.HelpBox("Select at least one value to copy.", MessageType.Info);
            }

            EditorGUI.BeginDisabledGroup(!hasSource || isSelfReference || !hasAnySelection);
            if (GUILayout.Button("Copy Selected Values"))
            {
                Undo.RecordObject(targetWardrobeRecipe, "Copy Wardrobe Recipe Values");

                if (copyCompatibleRaces)
                {
                    targetWardrobeRecipe.compatibleRaces = CloneStringList(sourceWardrobeRecipe.compatibleRaces);
                }
                if (copyWardrobeSlot)
                {
                    targetWardrobeRecipe.wardrobeSlot = sourceWardrobeRecipe.wardrobeSlot;
                }
                if (copyHides)
                {
                    targetWardrobeRecipe.Hides = CloneStringList(sourceWardrobeRecipe.Hides);
                }
                if (copyReplaces)
                {
                    targetWardrobeRecipe.replaces = sourceWardrobeRecipe.replaces;
                }
                if (copySuppressWardrobeSlots)
                {
                    targetWardrobeRecipe.suppressWardrobeSlots = CloneStringList(sourceWardrobeRecipe.suppressWardrobeSlots);
                }

                EditorUtility.SetDirty(targetWardrobeRecipe);
                AssetDatabase.SaveAssetIfDirty(targetWardrobeRecipe);
                changed = true;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndVertical();
        }

        private static List<string> CloneStringList(List<string> source)
        {
            if (source == null)
            {
                return new List<string>();
            }

            return new List<string>(source);
        }
    }
}
#endif
