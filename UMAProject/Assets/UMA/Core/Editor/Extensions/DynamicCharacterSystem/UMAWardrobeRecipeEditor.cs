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
		public static bool showWardrobeSettings = false;
		public static bool showRaceSettings = false;

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


			showRaceSettings = GUIHelper.FoldoutBar(showRaceSettings,"Race Settings");
			if (showRaceSettings)
			{
				//CompatibleRaces drop area
				if (DrawCompatibleRacesUI(TargetType, ShowHelp))
				{
					doUpdate = true;
				}
			}


            showWardrobeSettings = GUIHelper.FoldoutBar(showWardrobeSettings, "Wardrobe Settings");
			if (showWardrobeSettings)
			{
				//wardrobeSlots fields
				if (DrawWardrobeSlotsFields(TargetType, ShowHelp))
				{
					doUpdate = true;
				}
                if (DrawIncompatibleSlots(ShowHelp))
                {
                    doUpdate = true;
                }
            }


            //Set this up after the other so we can send the popup data with it
            // Keep the picker instances alive while their asynchronous popup is open.
            // Rebuild() replaces slotEditor after recipe changes, so the rows still refresh then.
            if (!(slotEditor is WardrobeRecipeMasterEditor wardrobeEditor) || !wardrobeEditor.IsEditing(_recipe))
            {
                wardrobeEditor = new WardrobeRecipeMasterEditor(_recipe, generatedBaseSlotOptions, generatedBaseSlotOptionsLabels, target);
                slotEditor = wardrobeEditor;
            }
            wardrobeEditor.SetBaseSlotOptions(generatedBaseSlotOptions, generatedBaseSlotOptionsLabels);
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

			// A new asset only. Choosing the recipe itself replaced it with a new asset (a new GUID, so every
			// reference to it broke), and any other existing asset would be replaced the same way.
			if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null || File.Exists(assetPath))
			{
				bool self = assetPath == currentPath;
				EditorUtility.DisplayDialog("Save As",
					self ? "Save As makes a new recipe, so it cannot be saved over '" + target.name + "' itself. Use Save Recipe, or choose a new name."
						 : "'" + assetPath + "' already exists. Save As makes a new recipe: choose a new name.", "OK");
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

			// Fill in the recipe before the file exists, so a failure here leaves no half-made asset.
			try
			{
				newRecipe.Save(_recipe);
			}
			catch
			{
				DestroyImmediate(newRecipe);
				throw;
			}
			AssetDatabase.CreateAsset(newRecipe, assetPath);
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
