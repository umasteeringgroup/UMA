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
}
