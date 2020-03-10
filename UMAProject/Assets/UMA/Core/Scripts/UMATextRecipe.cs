using UnityEngine;

namespace UMA
{
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
		public override UMAPackedRecipeBase.UMAPackRecipe PackedLoad(UMAContextBase context = null)
		{
			if ((recipeString == null) || (recipeString.Length == 0)) return new UMAPackRecipe();
				return JsonUtility.FromJson<UMAPackRecipe>(recipeString);
		}

		/// <summary>
		/// Serialize recipeString data into packed recipe.
		/// </summary>
		/// <param name="packedRecipe">Packed recipe.</param>
		/// <param name="context">Context.</param>
		public override void PackedSave(UMAPackedRecipeBase.UMAPackRecipe packedRecipe, UMAContextBase context)
		{
			recipeString = JsonUtility.ToJson(packedRecipe);
		}

		public override string GetInfo()
		{
			return string.Format(this.name+" "+this.GetType().ToString() + ", internal storage string Length {0}", recipeString.Length);
		}

		public override byte[] GetBytes()
		{
			return System.Text.Encoding.UTF8.GetBytes (recipeString);
		}
		public override void  SetBytes(byte[] data)
		{
			recipeString = System.Text.Encoding.UTF8.GetString(data); 	
		}

		public UMAData.UMARecipe GetUMARecipe()
		{
			return GetCachedRecipe(UMAContext.Instance);
		}

		public OverlayColorData[] SharedColors
		{
			get
			{
				var recipe = GetCachedRecipe(UMAContext.Instance);
				return recipe.sharedColors;
			}
		}

#if UNITY_EDITOR
		[UnityEditor.MenuItem("Assets/Create/UMA/Core/Text Recipe")]
		public static void CreateTextRecipeAsset()
		{
			UMA.CustomAssetUtility.CreateAsset<UMATextRecipe>();
		}
		#endif
	}
}
