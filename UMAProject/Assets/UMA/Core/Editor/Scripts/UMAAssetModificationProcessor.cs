using UnityEngine;
using UnityEditor;

namespace UMA.CharacterSystem
{
    /*
    public class UMAAssetPostProcessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
            {
                return;
            }

            if (UMASettings.PostProcessAllAssets)
            {
                // don't call if it's the indexer that's being updated!!!
                if (UMAAssetIndexer.Instance != null)
                {
                    UMAAssetIndexer.Instance.OnPostprocessAllAssets(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
                }
            }
        }
    }*/
}