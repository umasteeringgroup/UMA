#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UMA.CharacterSystem;

namespace UMA.Editors
{
	[CustomEditor(typeof(UMADynamicCharacterAvatarRecipe), true)]
	public partial class UMADynamicCharacterAvatarRecipeEditor : RecipeEditor
	{
		protected override bool PreInspectorGUI()
		{
			hideToolBar = false;
			hideRaceField = false;//hide race field is topsyturvy its about hiding our EXTRA race field (above the toolbar)
			return TextRecipeGUI();
		}

		/// <summary>
		/// Impliment this method to output any extra GUI for any extra fields you have added to UMADynamicCharacterAvatarRecipe before the main RecipeGUI
		/// </summary>
		partial void PreRecipeGUI(ref bool changed);
		/// <summary>
		/// Impliment this method to output any extra GUI for any extra fields you have added to UMADynamicCharacterAvatarRecipe after the main RecipeGUI
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
			EditorGUILayout.Popup("Recipe Type", 0, new string[] { "DynamicCharacterAvatar" });
			EditorGUI.EndDisabledGroup();

			PreRecipeGUI(ref doUpdate);

			//draws a button to 'Add DNA' when a new 'standard' recipe is created
			if (AddDNAButtonUI())
			{
				hideToolBar = false;
				return true;
			}
			//fixes dna when the recipes race has updated from UMADnaHumanoid/Tutorial to DynamicDna
			if (FixDNAConverters())
			{
				hideToolBar = false;
				return true;
			}

			FieldInfo ActiveWardrobeSetField = TargetType.GetField("activeWardrobeSet", BindingFlags.Public | BindingFlags.Instance);
			List<WardrobeSettings> activeWardrobeSet = (List<WardrobeSettings>)ActiveWardrobeSetField.GetValue(target);

			slotEditor = new WardrobeSetMasterEditor(_recipe, activeWardrobeSet);

			return doUpdate;
		}

	}
}
#endif
