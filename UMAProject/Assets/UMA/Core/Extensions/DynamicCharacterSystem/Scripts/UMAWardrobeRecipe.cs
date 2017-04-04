using UnityEngine; 
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA.CharacterSystem
{
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
			Debug.Log("WardrobeConverts");
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

	#if UNITY_EDITOR
		[UnityEditor.MenuItem("Assets/Create/UMA/DCS/Wardrobe Recipe")]
		public static void CreateWardrobeRecipeAsset()
		{
			UMA.CustomAssetUtility.CreateAsset<UMAWardrobeRecipe>();
		}
	#endif
	}
}