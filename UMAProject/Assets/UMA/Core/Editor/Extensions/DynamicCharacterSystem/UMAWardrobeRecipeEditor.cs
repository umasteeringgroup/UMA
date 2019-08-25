#if UNITY_EDITOR
using System; 
using UnityEditor;
using UMA.CharacterSystem;

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
				doUpdate = true;

			//wardrobeSlots fields
			if (DrawWardrobeSlotsFields(TargetType, ShowHelp))
				doUpdate = true;

			if (DrawIncompatibleSlots(ShowHelp))
				doUpdate = true;

			//Set this up after the other so we can send the popup data with it
			slotEditor = new WardrobeRecipeMasterEditor(_recipe, generatedBaseSlotOptions, generatedBaseSlotOptionsLabels);

			EditorGUILayout.Space();
			return doUpdate;
		}
	}
}
#endif
