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
            UMAAssetIndexer.Instance.OnPostprocessAllAssets(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
		}
    }
}
#endif
