using UnityEngine; 
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;
using System.Collections;
using UMA;

public partial class UMAWardrobeRecipe : UMATextRecipe
{
    #region FIELDS
    [SerializeField]
    public string replaces;

    public bool HasReplaces
    {
        get
        {
            if (string.IsNullOrEmpty(replaces))
                return false;
            if (replaces.ToLower() == "nothing")
                return false;
            return true;
        }
    }

    #endregion
    #region CONSTRUCTOR
    //if we get sent an UMATextRecipe that has a recipe type of Wardrobe then we create a new asset that has that assets properties
    //save that asset and rename the asset to be the name of the asset we deleted and maybe show a message saying 'Please update your AssetBundles'
    public UMAWardrobeRecipe()
	{
		recipeType = "Wardrobe";
	}
#if UNITY_EDITOR
	public UMAWardrobeRecipe(UMATextRecipe recipeToCopyFrom)
	{
		if(recipeToCopyFrom.recipeType == "Wardrobe")
		{
			CopyFromUTR(recipeToCopyFrom);
		}
	}
#endif
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

#endif
	#endregion

	#region STATIC METHODS

	//So this conversion would work but changing all refs to UMAWardrobeRecipe rather than UMATextRecipe will be a huge pain- 
	//and will break any existing code that refrences UMATextRecipe.compatibleRaces for example (unless we leave the fields in UMATextRecipe which kind of defeats the purpose)
#if UNITY_EDITOR

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
			var thisUWR = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
			thisUWR.ConvertFromUTR(thisUTR);
		}
		EditorPrefs.SetBool(Application.dataPath + ":UMAWardrobeRecipesUpToDate", true);
		Resources.UnloadUnusedAssets();
	}

	/// <summary>
	/// Checks to see if any UMATextRecipes require converting to UMAWardrobeRecipes and returns the number that do
	/// </summary>
	/// <returns></returns>
	public static int TestForOldRecipes(string recipeToTest = "")
	{
		int oldRecipesFound = 0;
		var allTextRecipeGUIDs = AssetDatabase.FindAssets("t:UMATextRecipe");
		for (int i = 0; i < allTextRecipeGUIDs.Length; i++)
		{
			var thisUTRPath = AssetDatabase.GUIDToAssetPath(allTextRecipeGUIDs[i]);
			if (recipeToTest != "" && thisUTRPath.IndexOf(recipeToTest) == -1)
				continue;
			var thisUTR = AssetDatabase.LoadAssetAtPath<UMATextRecipe>(thisUTRPath); 
			if (thisUTR.recipeType == "Wardrobe" && thisUTR.GetType() == typeof(UMATextRecipe))
				oldRecipesFound++;
		}
		if (oldRecipesFound > 0)
		{
			Debug.LogWarning(oldRecipesFound + " UMATextRecipes require converting to UMAWardrobeRecipes. Please go to UMA > Utilities > Convert Old Recipes to update them");
			EditorPrefs.SetBool(Application.dataPath + ":UMAWardrobeRecipesUpToDate", false);
		}
		else
		{
			EditorPrefs.SetBool(Application.dataPath + ":UMAWardrobeRecipesUpToDate", true);
		}
		Resources.UnloadUnusedAssets();
		return oldRecipesFound;
	}
#endif

	#endregion

#if UNITY_EDITOR
	[UnityEditor.MenuItem("Assets/Create/UMA/DCS/Wardrobe Recipe")]
	public static void CreateWardrobeRecipeAsset()
	{
		UMAEditor.CustomAssetUtility.CreateAsset<UMAWardrobeRecipe>();
	}
#endif
}
