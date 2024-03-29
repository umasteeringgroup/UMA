using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
#endif

namespace UMA.CharacterSystem
{
    public partial class UMAWardrobeRecipe : UMATextRecipe
	{
		[SerializeField]
		[Tooltip("For tracking incompatible items. Not automatic.")]
		public List<UMAWardrobeRecipe> IncompatibleRecipes = new List<UMAWardrobeRecipe>();

		[SerializeField]
		[Tooltip("The system does not use this field. Use it for whatever you need.")]
		public string UserField; 

		#region FIELDS
		[SerializeField]
		public string replaces;

		public bool HasReplaces
		{
			get
			{
				if (string.IsNullOrEmpty(replaces))
                {
                    return false;
                }

                if (replaces.ToLower() == "nothing")
                {
                    return false;
                }

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
			if (Debug.isDebugBuild)
            {
                Debug.Log("WardrobeConverts");
            }

            if (recipeToCopyFrom.recipeType != "Wardrobe" || recipeToCopyFrom.GetType() != typeof(UMATextRecipe))
            {
                return false;
            }

            recipeType = "Wardrobe";
			recipeString = recipeToCopyFrom.recipeString;
			compatibleRaces = recipeToCopyFrom.compatibleRaces;
			wardrobeSlot = recipeToCopyFrom.wardrobeSlot;
			suppressWardrobeSlots = recipeToCopyFrom.suppressWardrobeSlots;
			Hides = recipeToCopyFrom.Hides;
			wardrobeRecipeThumbs = recipeToCopyFrom.wardrobeRecipeThumbs;
			name = recipeToCopyFrom.name;
			
			if (recipeToCopyFrom.OverrideDNA != null)
            {
                OverrideDNA = recipeToCopyFrom.OverrideDNA.Clone();
            }

            DisplayValue = recipeToCopyFrom.DisplayValue;
			return true;
		}

	#endif
		#endregion

	#if UNITY_EDITOR
		#if UMA_HOTKEYS
		[UnityEditor.MenuItem("Assets/Create/UMA/DCS/Wardrobe Recipe %#w")]
		#else
		[UnityEditor.MenuItem("Assets/Create/UMA/DCS/Wardrobe Recipe")]
		#endif
		public static void CreateWardrobeRecipeAsset()
		{
			UMA.CustomAssetUtility.CreateAsset<UMAWardrobeRecipe>();
		}
	#endif
	}
}