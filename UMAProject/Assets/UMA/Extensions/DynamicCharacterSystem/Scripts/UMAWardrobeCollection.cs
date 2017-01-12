using UnityEngine;
using System.Collections;

public class UMAWardrobeCollection : UMATextRecipe {

#if UNITY_EDITOR
	[UnityEditor.MenuItem("Assets/Create/UMA Wardrobe Collection Recipe")]
	public static void CreateWardrobeCollectionAsset()
	{
		UMAEditor.CustomAssetUtility.CreateAsset<UMAWardrobeCollection>();
	}
#endif
}
