#if !StripLitJson
using UnityEngine;
using System.Collections;
using LitJson;
using UMA;

/// <summary>
/// Packed recipe which uses JSON text serialization for storage.
/// Class is marked partial so the developer can implement their own properties in UMATextRecipe without
/// changing the distribution code.
/// </summary>
public partial class UMATextRecipe : UMAPackedRecipeBase
{
	/// <summary>
	/// Complete text of recipe.
	/// </summary>
	public string recipeString="";

	/// <summary>
	/// Deserialize recipeString data into packed recipe.
	/// </summary>
	/// <returns>The packed recipe.</returns>
	/// <param name="context">Context.</param>
	public override UMAPackedRecipeBase.UMAPackRecipe PackedLoad(UMAContext context)
	{
		if ((recipeString== null) || (recipeString.Length == 0)) return new UMAPackRecipe();
		return JsonMapper.ToObject<UMAPackRecipe>(recipeString);
	}

	/// <summary>
	/// Serialize recipeString data into packed recipe.
	/// </summary>
	/// <param name="packedRecipe">Packed recipe.</param>
	/// <param name="context">Context.</param>
	public override void PackedSave(UMAPackedRecipeBase.UMAPackRecipe packedRecipe, UMAContext context)
	{
		recipeString = JsonMapper.ToJson(packedRecipe);
	}

	public override string GetInfo()
	{
		return string.Format("UMATextRecipe, internal storage string Length {0}", recipeString.Length);
	}

	public override byte[] GetBytes()
	{
		return System.Text.Encoding.UTF8.GetBytes (recipeString);
	}
	public override void  SetBytes(byte[] data)
	{
		recipeString = System.Text.Encoding.UTF8.GetString(data); 	
	}
	
	#if UNITY_EDITOR
	[UnityEditor.MenuItem("Assets/Create/UMA Text Recipe")]
	public static void CreateTextRecipeAsset()
	{
		UMAEditor.CustomAssetUtility.CreateAsset<UMATextRecipe>();
	}
	#endif

}
#endif
