using UnityEngine;
using System.Collections.Generic;
using UMA.CharacterSystem;

namespace UMA.AssetBundles
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
				if (GetTypeWithoutAssembly(assetType) == "UMATextRecipe")
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
				else if (GetTypeWithoutAssembly(assetType) == "UMAWardrobeRecipe")
				{
					assetWardrobeCompatibleWith = (obj as UMAWardrobeRecipe).compatibleRaces;
					if ((obj as UMAWardrobeRecipe).wardrobeSlot != "None")
					{
						assetWardrobeSlot = (obj as UMAWardrobeRecipe).wardrobeSlot;
						assetWardrobeHides = (obj as UMAWardrobeRecipe).Hides;
					}
				}
				else if (GetTypeWithoutAssembly(assetType) == "UMAWardrobeCollection")
				{
					assetWardrobeCompatibleWith = (obj as UMAWardrobeCollection).compatibleRaces;
					if (assetWardrobeCompatibleWith.Count > 0)
					{
						bool hasActiveSets = false;
						for (int i = 0; i < assetWardrobeCompatibleWith.Count; i++)
							if ((obj as UMAWardrobeCollection).wardrobeCollection[assetWardrobeCompatibleWith[i]].Count > 0)
								hasActiveSets = true;
						if (hasActiveSets)
							assetWardrobeSlot = (obj as UMAWardrobeCollection).wardrobeSlot;
						else
							assetWardrobeSlot = "None";
					}
				}
			}
		}

		//Wardrobe recipe specific data search functions...
		public bool IsAssetWardrobeRecipe(string assetBundleName, string assetName)
		{
			var thisIndexAsset = GetAssetBundleIndexItem(assetBundleName, assetName, "UMAWardrobeRecipe");
			if (thisIndexAsset != null)
				return true;
			else
			{
				thisIndexAsset = GetAssetBundleIndexItem(assetBundleName, assetName, "UMATextRecipe");
				if (thisIndexAsset.assetWardrobeSlot != "" || thisIndexAsset.assetWardrobeHides.Count > 0)
				{
					return true;
				}
				else
				{
					return false;
				}
			}
		}

		public string AssetWardrobeSlot(string assetBundleName, string assetName)
		{
			var thisIndexAsset = GetAssetBundleIndexItem(assetBundleName, assetName, "UMAWardrobeRecipe");
			if (thisIndexAsset == null)
				thisIndexAsset = GetAssetBundleIndexItem(assetBundleName, assetName, "UMATextRecipe");
			if (thisIndexAsset == null)
				return "";
			return thisIndexAsset.assetWardrobeSlot;
		}
		public List<string> AssetWardrobeHides(string assetBundleName, string assetName)
		{
			var thisIndexAsset = GetAssetBundleIndexItem(assetBundleName, assetName, "UMAWardrobeRecipe");
			if (thisIndexAsset == null)
				thisIndexAsset = GetAssetBundleIndexItem(assetBundleName, assetName, "UMATextRecipe");
			if (thisIndexAsset == null)
				return null;
			return thisIndexAsset.assetWardrobeHides;
		}
		public List<string> AssetWardrobeCompatibleWith(string assetBundleName, string assetName)
		{
			var thisIndexAsset = GetAssetBundleIndexItem(assetBundleName, assetName, "UMAWardrobeRecipe");
			if (thisIndexAsset == null)
				thisIndexAsset = GetAssetBundleIndexItem(assetBundleName, assetName, "UMATextRecipe");
			if (thisIndexAsset == null)
				return null;
			return thisIndexAsset.assetWardrobeCompatibleWith;
		}
		//Wardrobe Collection specific data search functions...
		public string AssetWardrobeCollectionSlot(string assetBundleName, string assetName)
		{
			AssetBundleIndexItem thisIndexAsset = GetAssetBundleIndexItem(assetBundleName, assetName, "UMAWardrobeCollection");
			return thisIndexAsset.assetWardrobeSlot;
		}
		public List<string> AssetWardrobeCollectionCompatibleWith(string assetBundleName, string assetName)
		{
			AssetBundleIndexItem thisIndexAsset = GetAssetBundleIndexItem(assetBundleName, assetName, "UMAWardrobeCollection");
			return thisIndexAsset.assetWardrobeCompatibleWith;
		}

	}
}
