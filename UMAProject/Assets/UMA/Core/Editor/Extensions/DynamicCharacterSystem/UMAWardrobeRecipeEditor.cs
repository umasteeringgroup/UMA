#if UNITY_EDITOR
using System; 
using System.IO;
using UnityEditor;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA.Editors
{
	[CustomEditor(typeof(UMAWardrobeRecipe), true)]
	public partial class UMAWardrobeRecipeEditor : RecipeEditor
	{
        public static bool ShowHelp = false;

		protected override bool PreInspectorGUI()
		{
			hideToolBar = false;
			hideRaceField = false;//hide race field is topsyturvy its about hiding our EXTRA race field (above the toolbar)
			return TextRecipeGUI();
		}

		/// <summary>
		/// Impliment this method to output any extra GUI for any extra fields you have added to UMAWardrobeRecipe before the main RecipeGUI
		/// </summary>
		partial void PreRecipeGUI(ref bool changed);
		/// <summary>
		/// Impliment this method to output any extra GUI for any extra fields you have added to UMAWardrobeRecipe after the main RecipeGUI
		/// </summary>
		partial void PostRecipeGUI(ref bool changed);

		protected override bool PostInspectorGUI()
		{
			bool changed = false;
			PostRecipeGUI(ref changed);
			return changed;
		}

		protected virtual bool TextRecipeGUI()
		{
			Type TargetType = target.GetType();
			bool doUpdate = false;

			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Save As", GUILayout.Width(120f)))
			{
				SaveAsRecipe(TargetType);
			}
			GUILayout.EndHorizontal();

			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.Popup("Recipe Type", 0, new string[] { "Wardrobe" });
			EditorGUI.EndDisabledGroup();

			PreRecipeGUI(ref doUpdate);

			hideRaceField = true;
			hideToolBar = true;
            //slotEditor = new WardrobeRecipeMasterEditor(_recipe, target);

            ShowHelp = EditorGUILayout.Toggle("Show Help", ShowHelp);


            //CompatibleRaces drop area
            if (DrawCompatibleRacesUI(TargetType, ShowHelp))
            {
                doUpdate = true;
            }

            //wardrobeSlots fields
            if (DrawWardrobeSlotsFields(TargetType, ShowHelp))
            {
                doUpdate = true;
            }

            if (DrawIncompatibleSlots(ShowHelp))
            {
                doUpdate = true;
            }

            //Set this up after the other so we can send the popup data with it
            slotEditor = new WardrobeRecipeMasterEditor(_recipe, generatedBaseSlotOptions, generatedBaseSlotOptionsLabels, target);
			return doUpdate;
		}

		private void SaveAsRecipe(Type targetType)
		{
			string currentPath = AssetDatabase.GetAssetPath(target);
			string directory = string.IsNullOrEmpty(currentPath) ? "Assets" : Path.GetDirectoryName(currentPath)?.Replace('\\', '/');
			if (string.IsNullOrEmpty(directory))
			{
				directory = "Assets";
			}

			string assetPath = EditorUtility.SaveFilePanelInProject(
				"Save Wardrobe Recipe As",
				target.name,
				"asset",
				"Choose where to save the duplicated wardrobe recipe.",
				directory);

			if (string.IsNullOrEmpty(assetPath))
			{
				return;
			}

			var newRecipe = ScriptableObject.CreateInstance(targetType) as UMARecipeBase;
			if (newRecipe == null)
			{
				Debug.LogError("Unable to create the selected recipe type for Save As.");
				return;
			}

			EditorUtility.CopySerialized(target, newRecipe);
			newRecipe.name = Path.GetFileNameWithoutExtension(assetPath);

			AssetDatabase.CreateAsset(newRecipe, assetPath);
			newRecipe.Save(_recipe);
			EditorUtility.SetDirty(newRecipe);
			AssetDatabase.SaveAssetIfDirty(newRecipe);

			if (newRecipe is UMATextRecipe textRecipe)
			{
				UMAUpdateProcessor.UpdateRecipe(textRecipe);
			}

			AssetDatabase.Refresh();
			Selection.activeObject = newRecipe;
			EditorGUIUtility.PingObject(newRecipe);
		}
	}
}
#endif
