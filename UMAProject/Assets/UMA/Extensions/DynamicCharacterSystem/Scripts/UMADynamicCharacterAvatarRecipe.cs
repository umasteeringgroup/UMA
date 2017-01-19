using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMACharacterSystem;

//Because this is a class for user generated content it is marked as partial so it can be extended without modifying the underlying code
public partial class UMADynamicCharacterAvatarRecipe : UMATextRecipe
{
	//if we ditched the additional fields in UMATextRecipe this would need
	/*[SerializeField]
	public List<WardrobeSettings> activeWardrobeSet = new List<WardrobeSettings>();*/

	#region CONSTRUCTOR

	public UMADynamicCharacterAvatarRecipe()
	{
		recipeType = "DynamicCharacterAvatar";
	}

	public UMADynamicCharacterAvatarRecipe(UMATextRecipe recipeToCopyFrom)
	{
		if (recipeToCopyFrom.recipeType == "DynamicCharacterAvatar")
		{
			CopyFromUTR(recipeToCopyFrom);
		}
	}

	public UMADynamicCharacterAvatarRecipe(DynamicCharacterAvatar dca, string recipeName = "", DynamicCharacterAvatar.SaveOptions customSaveOptions = DynamicCharacterAvatar.SaveOptions.useDefaults)
	{
		recipeType = "DynamicCharacterAvatar";
		if (customSaveOptions.HasFlag(DynamicCharacterAvatar.SaveOptions.useDefaults))
			customSaveOptions = dca.defaultSaveOptions;
		if (recipeName == "")
			recipeName = dca.gameObject.name;
		recipeString = JsonUtility.ToJson(new DCSPackRecipe(dca, recipeName, "DynamicCharacterAvatar", customSaveOptions));
	}

	#endregion

	#region EDITOR ONLY METHODS
#if UNITY_EDITOR	
	/// <summary>
	/// If the given UMATextRecipe was of recipeType "DynamicCharacterAvatar", copies its to this UMADynamicCharacterAvatarRecipe, otherwise returns false.
	/// </summary>
	/// <param name="recipeToCopyFrom"></param>
	/// <returns></returns>
	private bool CopyFromUTR(UMATextRecipe recipeToCopyFrom)
	{
		if (recipeToCopyFrom.recipeType != "DynamicCharacterAvatar" || recipeToCopyFrom.GetType() != typeof(UMATextRecipe))
			return false;
		recipeType = "DynamicCharacterAvatar";
		var recipeModel = JsonUtility.FromJson<DCSPackRecipe>(recipeToCopyFrom.recipeString);
		recipeModel.packedRecipeType = "DynamicCharacterAvatar";
		recipeString = JsonUtility.ToJson(recipeModel);
		name = recipeToCopyFrom.name;
		return true;
	}

	protected override void ConvertFromUTR(UMATextRecipe sourceUTR, bool andSelect = false)
	{
		var thisUTRPath = AssetDatabase.GetAssetPath(sourceUTR);
		//copy the settings from UMATextRecipe to UMADynamicCharacterAvatarRecipe
		if (CopyFromUTR(sourceUTR))
		{
			Debug.Log("Converted "+ sourceUTR.name+" to an UMADynamicCharacterAvatarRecipe");
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
			Debug.Log("Conversion of " + sourceUTR.name + " to an UMADynmicCharacterAvatarRecipe failed. Was the recipeType 'DynamicCharacterAvatar'?");
			ScriptableObject.Destroy(this);
		}
	}
#endif
	#endregion

	#region STATIC METHODS

#if UNITY_EDITOR

	//DISABLED FOR NOW - I want to push this but not have people convert their recipes yet
	/*
		[UnityEditor.MenuItem("UMA/Utilities/Convert Old DynamicCharacterAvatar Recipes")]
		*/
	public static void ConvertOldDCARecipes()
	{
		EditorPrefs.SetBool("UMADCARecipesUpdated", true);
		var allTextRecipeGUIDs = AssetDatabase.FindAssets("t:UMATextRecipe");
		for (int i = 0; i < allTextRecipeGUIDs.Length; i++)
		{
			var thisUTRPath = AssetDatabase.GUIDToAssetPath(allTextRecipeGUIDs[i]);
			var thisUTR = AssetDatabase.LoadAssetAtPath<UMATextRecipe>(thisUTRPath);
			//if its not a DCA recipe or its actual type is anything other than UMATextRecipe
			if (thisUTR.recipeType != "DynamicCharacterAvatar" || thisUTR.GetType() != typeof(UMATextRecipe))
				continue;
			var thisDCS = ScriptableObject.CreateInstance<UMADynamicCharacterAvatarRecipe>();
			thisDCS.ConvertFromUTR(thisUTR);
		}
		Resources.UnloadUnusedAssets();
	}
#endif

	#endregion

	//Override Load from PackedRecipeBase
	/// <summary>
	/// NOTE: Use GetUniversalPackRecipe to get a recipe that includes a wardrobeSet. Load this Recipe's recipeString into the specified UMAData.UMARecipe.
	/// </summary>
	public override void Load(UMA.UMAData.UMARecipe umaRecipe, UMAContext context)
	{
		if ((recipeString != null) && (recipeString.Length > 0))
		{
			if (RecipeHasWardrobeSet(recipeString))
				activeWardrobeSet = GetRecipesWardrobeSet(recipeString);
			else
				Debug.LogWarning("[UMADynamicCharacterAvatar] recipe did not have wardrobe set");
			var packedRecipe = PackedLoadDCSInternal(context);
			if (packedRecipe != null)
				UnpackRecipeVersion2(umaRecipe, packedRecipe, context);
		}
	}
	/*we are not going to have a create menu option for DynamicCharacterAvatar recipes I dont think
#if UNITY_EDITOR
	[UnityEditor.MenuItem("Assets/Create/UMA Dynamic Character Avatar Recipe")]
	public static void CreateDCAAsset()
	{
		UMAEditor.CustomAssetUtility.CreateAsset<UMADynamicCharacterAvatarRecipe>();
	}
#endif
*/
}
