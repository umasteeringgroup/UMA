using UnityEditor;

namespace UMA.Editors
{
    internal sealed class UMAResourceReuseImportChanges : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (importedAssets.Length + deletedAssets.Length + movedAssets.Length + movedFromAssetPaths.Length != 0)
                UMAResourceReuse.InvalidateTextureInputs();
        }
    }
}
