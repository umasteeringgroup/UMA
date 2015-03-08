#if !StripLitJson
using UnityEngine;
using System.Collections;
using LitJson;
using UMA;

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

	public override byte[] GetBytes()
	{
		return System.Text.Encoding.UTF8.GetBytes (recipeString);
	}
	public override void  SetBytes(byte[] data)
	{
		recipeString = System.Text.Encoding.UTF8.GetString(data); 	
	}
}
#endif
