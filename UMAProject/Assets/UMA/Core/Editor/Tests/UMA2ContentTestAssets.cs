using NUnit.Framework;
using UnityEditor;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The UMA 2 Content add-on (Assets/UMA2Content) is a separate Asset Store package, not part of UMA.
    /// Tests that use its RPG Starter wardrobe are ignored, not failed, in projects without it.
    /// </summary>
    internal static class UMA2ContentTestAssets
    {
        internal const string RpgStarter = "Assets/UMA2Content/UMA RPG Starter 1";

        internal static void RequireAsset(string path)
        {
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets)))
                Assert.Ignore("Needs the UMA 2 Content add-on package (" + path + " is not in this project).");
        }

        internal static void RequireFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
                Assert.Ignore("Needs the UMA 2 Content add-on package (" + path + " is not in this project).");
        }
    }
}
