using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA;
using System;
using UnityEditor.Build.Reporting;
using UnityEditor;
using System.Linq;
using System.IO;

namespace UMA
{
#if UMA_ADDRESSABLES
public class UMAAddressablesBuildSample
{
    public static void Build(string destFolder, bool dev, string appName)
    {
        // Generate the addressables for UMA, and cleanup after it
        GenerateUMAAddressables();
        UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.BuildPlayerContent(out var result);
        // UMAPostBuildMaterialUpdate();
        // Addressable bundles are built at this point, so we can build the player


        BuildReport buildReport = null;
        try
        {
            // Build the player
            buildReport = BuildPlayer(destFolder, dev, appName);
            if (buildReport == null)
            {
                Debug.LogError($"UMABuildScript.BuildPlayer Failed");
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"UMABuildScript.BuildPlayer Failed: {e.Message}");
            return;
        }
    }

    private static BuildReport BuildPlayer(string path, bool dev, string AppName)
    {
        Debug.Log($"Building player to {path}");
        // The default is development build with debugging enabled
        var buildOptions = BuildOptions.Development | BuildOptions.AllowDebugging;

        // if we don't pass true, we are building a release build
        if (!dev)
        {
            buildOptions = BuildOptions.None;
        }

        // Include the scenes speicified in the build settings
        // and write output to the path chosen by the user

        string pathName = $"{path}/{AppName}";
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        var buildPlayerOptions = new BuildPlayerOptions()
        {
            scenes = EditorBuildSettings.scenes.Select(scene => scene.path.ToString()).ToArray(),
            locationPathName = pathName,
            target = EditorUserBuildSettings.activeBuildTarget,
            options = buildOptions
        };


        if (!dev)
        {
            Debug.Log("Building Development Player");
        }
        else
        {
            Debug.Log("Building Release Player");
        }

        return BuildPipeline.BuildPlayer(buildPlayerOptions);
    }

    public static void UMAPostBuildMaterialUpdate()
    {
        Debug.Log($"UMAPostProcessBuild - Adding UMA resource references");
        try
        {
            UMAAssetIndexer.Instance.PostBuildMaterialFixup();
        }
        catch (Exception ex)
        {
            Debug.Log($"UMAPostProcessBuild - Adding UMA resource references failed with exception {ex.Message}");
        }
    }

    public static void GenerateUMAAddressables()
    { 
        // Clear the index, rebuild the type arrays, and then query to project for the indexed types, and
        // add everything to the index. Do not add the text assets (only needed if loading characters from resources)
        Debug.Log("UMABuildScript - Rebuilding asset index.");
        UMA.UMAAssetIndexer assetIndex = UMAAssetIndexer.Instance;
        try
        {
            assetIndex.PrepareBuild();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }

        // Generate all UMA addressable labels by recipe. Every recipe gets a unique label, so when that
        // recipe needs to be loaded, all bundles that contain that item are demand loaded into memory.
        // they are unloaded when there is no active character using any of the assets.
        Debug.Log($"UMABuildScript - Generating UMA addressable labels.");
        UMAAddressablesSupport.Instance.GenerateAddressables(new SingleGroupGenerator { ClearMaterials = true });

        // Make sure that the global library has a reference to every item that is not addressable.
        // This ensures that they item is included in resources. (Since the items are built dynamically,
        // they must be able to be loaded at runtime either through addressable bundles or resources).
        Debug.Log($"UMABuildScript - Adding UMA resource references");
        assetIndex.AddReferences(); 
    }
}
#endif
}