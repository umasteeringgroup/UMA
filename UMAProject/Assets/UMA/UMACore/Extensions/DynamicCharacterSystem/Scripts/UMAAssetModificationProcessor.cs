#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections;

namespace UMA
{
	public class UMAAssetPostProcessor : AssetPostprocessor
	{
		public void OnPostprocessAssetbundleNameChanged(string assetPath, string previousAssetBundleName, string newAssetBundleName)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return;
			if (UMAAssetIndex.Instance != null)
			{
				UMAAssetIndex.Instance.OnAssetBundleNameChange(assetPath, previousAssetBundleName, newAssetBundleName);
			}
		}
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return;
			if (UMAAssetIndex.Instance != null)
			{
				UMAAssetIndex.Instance.OnEditorDuplicatedAsset(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
			}
		}
    }

    public class UMAAssetModificationProcessor : UnityEditor.AssetModificationProcessor
	{

		//create UMATextRecioe triggers create AND save - actually just seems to trigger save if the asset is duplicated
		//rename UMATextRecipe triggers move
		//Edit UMATextRecipe triggers save
		//Move UMATextRecipe triggers move
		//Delete UMATextRecipe triggers delete

		//this happens when an asset is moved AND when its renamed
		static AssetMoveResult OnWillMoveAsset(string assetPrevPath, string assetNewPath)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return AssetMoveResult.DidNotMove;
			
			//Debug.Log("UMAIndexAssetModificationProcessor OnWillMoveAsset");
			/*if(UMAResourcesIndex.Instance != null)
			{
				UMAResourcesIndex.Instance.DoIndexUpdate();
            }*/
			if (UMAAssetIndex.Instance != null)
			{
				UMAAssetIndex.Instance.OnMoveAsset(assetPrevPath, assetNewPath);
			}
			return AssetMoveResult.DidNotMove;
		}

		static AssetDeleteResult OnWillDeleteAsset(string assetToDelete, RemoveAssetOptions removeOpts)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return AssetDeleteResult.DidNotDelete;
			
			//Debug.Log("UMAIndexAssetModificationProcessor OnWillDeleteAsset");
			/*if (UMAResourcesIndex.Instance != null)
			{
				UMAResourcesIndex.Instance.DoIndexUpdate();
			}*/
			if (UMAAssetIndex.Instance != null)
			{
				UMAAssetIndex.Instance.OnDeleteAsset(assetToDelete);
            }
			return AssetDeleteResult.DidNotDelete;
		}

		//On will create asset happens when an asset is created from the Create menu but NOT when assets are duplicated
		static string OnWillCreateAsset(string createdAsset)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return createdAsset;
			
			/*Debug.Log("UMAIndexAssetModificationProcessor OnWillCreateAsset at "+createdAsset);
			if (UMAResourcesIndex.Instance != null)
			{
				UMAResourcesIndex.Instance.DoIndexUpdate();
			}*/
			if (UMAAssetIndex.Instance != null)
			{
				UMAAssetIndex.Instance.OnCreateAsset(createdAsset);
			}
			return createdAsset;
		}

		//OnWillSaveAssets happens when an asset is Duplicated but the path for the asset is NOT sent
		static string[] OnWillSaveAssets(string[] assetsToSave)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return assetsToSave;
			
			/*Debug.Log("UMAIndexAssetModificationProcessor OnWillSaveAssets");
			foreach (string path in assetsToSave)
				Debug.Log("[AMP Path] " + path);
			if (UMAResourcesIndex.Instance != null)
			{
				UMAResourcesIndex.Instance.DoModifiedUMAAssets(assetsToSave);
			}*/
			if (UMAAssetIndex.Instance != null)
			{
				UMAAssetIndex.Instance.OnSaveAssets(assetsToSave);
			}
			return assetsToSave;
		}

	}
}
#endif
