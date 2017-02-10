#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections;

namespace UMA
{
	public class UMAAssetModificationProcessor : UnityEditor.AssetModificationProcessor
	{
		//create UMATextRecioe triggers create AND save - actually just seems to trigger save if the asset is duplicated
		//rename UMATextRecipe triggers move
		//Edit UMATextRecipe triggers save
		//Move UMATextRecipe triggers move
		//Delete UMATextRecipe triggers delete

		//so how do we do something once assetModificationProcessor its done

		//this happens when an asset is moved AND when its renamed
		static AssetMoveResult OnWillMoveAsset(string assetPrevPath, string assetNewPath)
		{
			//Debug.Log("UMAIndexAssetModificationProcessor OnWillMoveAsset");
			if(UMAResourcesIndex.Instance != null)
			{
				UMAResourcesIndex.Instance.DoIndexUpdate();
            }
			return AssetMoveResult.DidNotMove;
		}

		static AssetDeleteResult OnWillDeleteAsset(string assetToDelete, RemoveAssetOptions removeOpts)
		{
			//Debug.Log("UMAIndexAssetModificationProcessor OnWillDeleteAsset");
			if (UMAResourcesIndex.Instance != null)
			{
				UMAResourcesIndex.Instance.DoIndexUpdate();
			}
			return AssetDeleteResult.DidNotDelete;
		}

		static string OnWillCreateAsset(string createdAsset)
		{
			//Debug.Log("UMAIndexAssetModificationProcessor OnWillCreateAsset");
			if (UMAResourcesIndex.Instance != null)
			{
				UMAResourcesIndex.Instance.DoIndexUpdate();
			}
			return createdAsset;
		}

		static string[] OnWillSaveAssets(string[] assetsToSave)
		{
			//Debug.Log("UMAIndexAssetModificationProcessor OnWillSaveAssets");
			if (UMAResourcesIndex.Instance != null)
			{
				UMAResourcesIndex.Instance.DoModifiedUMAAssets(assetsToSave);
			}
			return assetsToSave;
		}

	}
}
#endif
