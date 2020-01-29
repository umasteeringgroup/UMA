using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA.CharacterSystem
{
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

	#if UNITY_EDITOR
		public UMADynamicCharacterAvatarRecipe(UMATextRecipe recipeToCopyFrom)
		{
			if (recipeToCopyFrom.recipeType == "DynamicCharacterAvatar")
			{
				CopyFromUTR(recipeToCopyFrom);
			}
		}
	#endif

		public UMADynamicCharacterAvatarRecipe(DynamicCharacterAvatar dca, string recipeName = "", DynamicCharacterAvatar.SaveOptions customSaveOptions = DynamicCharacterAvatar.SaveOptions.useDefaults)
		{
			recipeType = "DynamicCharacterAvatar";
			if (customSaveOptions.HasFlagSet(DynamicCharacterAvatar.SaveOptions.useDefaults))
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

	#endif
		#endregion

		//Override Load from PackedRecipeBase
		/// <summary>
		/// NOTE: Use GetUniversalPackRecipe to get a recipe that includes a wardrobeSet. Load this Recipe's recipeString into the specified UMAData.UMARecipe.
		/// </summary>
		public override void Load(UMA.UMAData.UMARecipe umaRecipe, UMAContextBase context)
		{
			if ((recipeString != null) && (recipeString.Length > 0))
			{
				if (RecipeHasWardrobeSet(recipeString))
					activeWardrobeSet = GetRecipesWardrobeSet(recipeString);
				else
				{
					if (Debug.isDebugBuild)
						Debug.LogWarning("[UMADynamicCharacterAvatar] recipe did not have wardrobe set");
				}
				var packedRecipe = PackedLoadDCSInternal(context);
				if (packedRecipe != null)
				   UnpackRecipe(umaRecipe, packedRecipe, context);
			}
		}
		/*we are not going to have a create menu option for DynamicCharacterAvatar recipes I dont think
	#if UNITY_EDITOR
		[UnityEditor.MenuItem("Assets/Create/UMA Dynamic Character Avatar Recipe")]
		public static void CreateDCAAsset()
		{
			UMA.Editor.CustomAssetUtility.CreateAsset<UMADynamicCharacterAvatarRecipe>();
		}
	#endif
	*/
	}
}