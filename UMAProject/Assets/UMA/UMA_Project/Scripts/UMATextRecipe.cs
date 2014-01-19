using UnityEngine;
using System.Collections;
using LitJson;

public class UMATextRecipe : UMAPackedRecipeBase
{
	public string recipeString;
	public override UMAPackedRecipeBase.UMAPackRecipe PackedLoad(UMAContext context)
	{
		return JsonMapper.ToObject<UMAPackRecipe>(recipeString);
	}

	public override void PackedSave(UMAPackedRecipeBase.UMAPackRecipe packedRecipe, UMAContext context)
	{
		recipeString = JsonMapper.ToJson(packedRecipe);
	}

	public override string GetInfo()
	{
		return string.Format("UMATextRecipe, internal storage string Length {0}", recipeString.Length);
	}
}
