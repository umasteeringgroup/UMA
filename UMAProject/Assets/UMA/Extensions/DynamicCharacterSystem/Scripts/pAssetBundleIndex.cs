using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace UMAAssetBundleManager
{
	//This is an example of how the AssetBundleIndex class can be extended to contain extra data about items in the index. 
	//Here we have extended it to include extra data about UMATextRecipes that are set to be 'Wardrobe' recipes rather than 'Standard' ones.
	public partial class AssetBundleIndex : ScriptableObject
	{
		public partial class AssetBundleIndexItem
		{
			public string assetWardrobeSlot;
			public List<string> assetWardrobeHides;
			public List<string> assetWardrobeCompatibleWith = new List<string>();

			partial void AddDataPostProcess(string filename, UnityEngine.Object obj)
			{
				if (assetType == "UMATextRecipe")
				{
					if ((obj as UMATextRecipe).recipeType == "Wardrobe")
					{
						assetWardrobeCompatibleWith = (obj as UMATextRecipe).compatibleRaces;
						if ((obj as UMATextRecipe).wardrobeSlot != "None")
						{
							assetWardrobeSlot = (obj as UMATextRecipe).wardrobeSlot;
							assetWardrobeHides = (obj as UMATextRecipe).Hides;
						}
					}
				}
			}
		}

		//Wardrobe recipe specific data search functions...
		public bool IsAssetWardrobeRecipe(string assetBundleName, string assetName)
		{
			var thisIndexAsset = GetAssetBundleIndexItem(assetBundleName, assetName, "UMATextRecipe");
			if (thisIndexAsset.assetWardrobeSlot != "" || thisIndexAsset.assetWardrobeHides.Count > 0)
			{
				return true;
			}
			else
			{
				return false;
			}
		}

		public string AssetWardrobeSlot(string assetBundleName, string assetName)
		{
			var thisIndexAsset = GetAssetBundleIndexItem(assetBundleName, assetName, "UMATextRecipe");
			return thisIndexAsset.assetWardrobeSlot;
		}
		public List<string> AssetWardrobeHides(string assetBundleName, string assetName)
		{
			var thisIndexAsset = GetAssetBundleIndexItem(assetBundleName, assetName, "UMATextRecipe");
			return thisIndexAsset.assetWardrobeHides;
		}
		public List<string> AssetWardrobeCompatibleWith(string assetBundleName, string assetName)
		{
			var thisIndexAsset = GetAssetBundleIndexItem(assetBundleName, assetName, "UMATextRecipe");
			return thisIndexAsset.assetWardrobeCompatibleWith;
		}

	}
}
