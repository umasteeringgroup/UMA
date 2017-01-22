using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;
using System.Collections;
using UMA;

public partial class UMAWardrobeRecipe : UMATextRecipe
{

	#region CONSTRUCTOR
	//if we get sent an UMATextRecipe that has a recipe type of Wardrobe then we create a new asset that has that assets properties
	//save that asset and rename the asset to be the name of the asset we deleted and maybe show a message saying 'Please update your AssetBundles'
	public UMAWardrobeRecipe()
	{
		recipeType = "Wardrobe";
	}

	public UMAWardrobeRecipe(UMATextRecipe recipeToCopyFrom)
	{
		if(recipeToCopyFrom.recipeType == "Wardrobe")
		{
			CopyFromUTR(recipeToCopyFrom);
		}
	}
	#endregion

	#region EDITOR ONLY METHODS
#if UNITY_EDITOR
	private bool CopyFromUTR(UMATextRecipe recipeToCopyFrom)
	{
		if (recipeToCopyFrom.recipeType != "Wardrobe" || recipeToCopyFrom.GetType() != typeof(UMATextRecipe))
			return false;
		recipeType = "Wardrobe";
		recipeString = recipeToCopyFrom.recipeString;
		compatibleRaces = recipeToCopyFrom.compatibleRaces;
		wardrobeSlot = recipeToCopyFrom.wardrobeSlot;
		suppressWardrobeSlots = recipeToCopyFrom.suppressWardrobeSlots;
		Hides = recipeToCopyFrom.Hides;
		wardrobeRecipeThumbs = recipeToCopyFrom.wardrobeRecipeThumbs;
		name = recipeToCopyFrom.name;
		DisplayValue = recipeToCopyFrom.DisplayValue;
		return true;
	}

	protected override void ConvertFromUTR(UMATextRecipe sourceUTR, bool andSelect = false)
	{
		var thisUTRPath = AssetDatabase.GetAssetPath(sourceUTR);
		//copy the settings from UMATextRecipe to UMADynamicCharacterAvatarRecipe
		if (CopyFromUTR(sourceUTR))
		{
			Debug.Log("Converted " + sourceUTR.name + " to an UMAWardrobeRecipe");
			//rename old UMATextRecipe recipe to *name*_old
			AssetDatabase.RenameAsset(thisUTRPath, sourceUTR.name + "_old");
			AssetDatabase.CreateAsset(this, thisUTRPath);
			//Delete the old Asset
			AssetDatabase.DeleteAsset(Path.Combine(Path.GetDirectoryName(thisUTRPath), (Path.GetFileNameWithoutExtension(thisUTRPath) + "_old" + Path.GetExtension(thisUTRPath))));
			AssetDatabase.SaveAssets();
			if (andSelect)
				Selection.activeObject = this;
		}
		else
		{
			Debug.Log("Conversion of " + sourceUTR.name + " to an UMAWardrobeRecipe failed. Was the recipeType 'Wardrobe'?");
			ScriptableObject.Destroy(this);
		}
	}
#endif
	#endregion

	#region STATIC METHODS

	//So this conversion would work but changing all refs to UMAWardrobeRecipe rather than UMATextRecipe will be a huge pain- 
	//and will break any existing code that refrences UMATextRecipe.compatibleRaces for example (unless we leave the fields in UMATextRecipe which kind of defeats the purpose)
#if UNITY_EDITOR

//DISABLED FOR NOW - I want to push this but not have people convert their recipes yet
/*
	[UnityEditor.MenuItem("UMA/Utilities/Convert Old Wardrobe Recipes")]
	*/
	public static void ConvertOldWardrobeRecipes()
	{
		var allTextRecipeGUIDs = AssetDatabase.FindAssets("t:UMATextRecipe");
		for(int i = 0; i < allTextRecipeGUIDs.Length; i++)
		{
			var thisUTRPath = AssetDatabase.GUIDToAssetPath(allTextRecipeGUIDs[i]);
            var thisUTR = AssetDatabase.LoadAssetAtPath<UMATextRecipe>(thisUTRPath);
			//if its not a Wardrobe recipe or its actual type is anything other than UMATextRecipe
			if (thisUTR.recipeType != "Wardrobe" || thisUTR.GetType() != typeof(UMATextRecipe))
				continue;
			Debug.Log("UMAWardrobeRecipe did conversion of TestConvertRecipe");
			var thisUWR = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
			thisUWR.ConvertFromUTR(thisUTR);
		}
		EditorPrefs.SetBool(Application.dataPath + ":UMAWardrobeRecipesUpdated", true);
		Resources.UnloadUnusedAssets();
	}

	/// <summary>
	/// Checks to see if any UMATextRecipes require converting to UMAWardrobeRecipes and returns the number that do
	/// </summary>
	/// <returns></returns>
	public static int TestForOldRecipes()
	{
		int oldRecipesFound = 0;
		var allTextRecipeGUIDs = AssetDatabase.FindAssets("t:UMATextRecipe");
		for (int i = 0; i < allTextRecipeGUIDs.Length; i++)
		{
			var thisUTRPath = AssetDatabase.GUIDToAssetPath(allTextRecipeGUIDs[i]);
			var thisUTR = AssetDatabase.LoadAssetAtPath<UMATextRecipe>(thisUTRPath);
			//if its not a Wardrobe recipe or its actual type is anything other than UMATextRecipe
			if (thisUTR.recipeType == "Wardrobe" && thisUTR.GetType() == typeof(UMATextRecipe))
				oldRecipesFound++;
		}
		Debug.Log(oldRecipesFound + " UMATextRecipes require converting to UMAWardrobeRecipes.");
		Resources.UnloadUnusedAssets();
		return oldRecipesFound;
	}
#endif

	#endregion

#if UNITY_EDITOR
	[UnityEditor.MenuItem("Assets/Create/UMA Wardrobe Recipe")]
	public static void CreateWardrobeRecipeAsset()
	{
		UMAEditor.CustomAssetUtility.CreateAsset<UMAWardrobeRecipe>();
	}
#endif
}
