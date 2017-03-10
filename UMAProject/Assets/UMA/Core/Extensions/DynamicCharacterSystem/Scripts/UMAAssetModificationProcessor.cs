#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections;

namespace UMA
{
	public class UMAAssetPostProcessor : AssetPostprocessor
	{
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return;
			if (UMAAssetIndex.Instance != null)
			{
				UMAAssetIndex.Instance.OnPostprocessAllAssets(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
			}
		}
    }

    public class UMAAssetModificationProcessor : UnityEditor.AssetModificationProcessor
	{
		//On will create asset happens when an asset is created from the Create menu but NOT when assets are duplicated
		static string OnWillCreateAsset(string createdAsset)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return createdAsset;
			
			if (UMAAssetIndex.Instance != null)
			{
				UMAAssetIndex.Instance.OnCreateAsset(createdAsset);
			}
			return createdAsset;
		}

	}
}
#endif
