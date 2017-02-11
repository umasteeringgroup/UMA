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
	[CustomEditor(typeof(UMAWardrobeRecipe), true)]
	public partial class UMAWardrobeRecipeEditor : RecipeEditor
	{
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

			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.Popup("Recipe Type", 0, new string[] { "Wardrobe" });
			EditorGUI.EndDisabledGroup();

			PreRecipeGUI(ref doUpdate);

			hideRaceField = true;
			hideToolBar = true;
			slotEditor = new WardrobeRecipeMasterEditor(_recipe);


			//CompatibleRaces drop area
			if (DrawCompatibleRacesUI(TargetType))
				doUpdate = true;

			//wardrobeSlots fields
			if (DrawWardrobeSlotsFields(TargetType))
				doUpdate = true;

			EditorGUILayout.Space();
			return doUpdate;
		}
	}
}
#endif
