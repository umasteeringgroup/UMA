using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Project-window commands for adding existing project assets to the UMA global library.
    /// </summary>
    internal static class UMAAssetIndexerProjectMenu
    {
        private const string AddSelectedAssetsMenuPath = "Assets/Add selected assets to UMA global library";

        [MenuItem(AddSelectedAssetsMenuPath, false, 2000)]
        private static void AddSelectedAssetsToGlobalLibrary()
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null)
            {
                Debug.LogError("[UMA] Cannot add assets to the UMA global library because the UMA Asset Indexer is unavailable.");
                return;
            }

            Object[] selectedAssets = Selection.GetFiltered(typeof(Object), SelectionMode.Assets);
            int indexedAssetCount = 0;

            Undo.RecordObject(indexer, "Add selected assets to UMA global library");
            for (int i = 0; i < selectedAssets.Length; i++)
            {
                Object asset = selectedAssets[i];
                if (asset == null || AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(asset)) || !indexer.IsIndexedType(asset.GetType()))
                {
                    continue;
                }

                indexer.ProcessNewItem(asset, false, false);
                indexedAssetCount++;
            }

            if (indexedAssetCount > 0)
            {
                indexer.ForceSave();
                EditorUtility.DisplayDialog("UMA", $"Added {indexedAssetCount} selected asset(s) to the UMA global library.", "OK");
                Debug.Log($"[UMA] Added {indexedAssetCount} selected asset(s) to the UMA global library.");
            }
        }

        [MenuItem(AddSelectedAssetsMenuPath, true)]
        private static bool ValidateAddSelectedAssetsToGlobalLibrary()
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null)
            {
                return false;
            }

            Object[] selectedAssets = Selection.GetFiltered(typeof(Object), SelectionMode.Assets);
            for (int i = 0; i < selectedAssets.Length; i++)
            {
                Object asset = selectedAssets[i];
                if (asset != null &&
                    !AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(asset)) &&
                    indexer.IsIndexedType(asset.GetType()))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
