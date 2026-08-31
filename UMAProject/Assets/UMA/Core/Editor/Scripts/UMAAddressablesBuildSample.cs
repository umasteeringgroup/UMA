using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Minimal UMA player-build example that works without optional Addressables support.
    /// UMAAddressablesBuildSample uses the same preparation and player-build path, adding
    /// only the Addressables generation and bundle build steps.
    /// </summary>
    public static class UMABuildSample
    {
        public static void Build(string destinationFolder, bool developmentBuild,
            string applicationName)
        {
            BuildInternal(destinationFolder, developmentBuild, applicationName, false);
        }

        internal static void BuildWithAddressables(string destinationFolder,
            bool developmentBuild, string applicationName)
        {
            BuildInternal(destinationFolder, developmentBuild, applicationName, true);
        }

        private static void BuildInternal(string destinationFolder,
            bool developmentBuild, string applicationName, bool useAddressables)
        {
            if (string.IsNullOrWhiteSpace(destinationFolder))
            {
                Debug.LogError("UMA Build Sample requires an output folder.");
                return;
            }
            if (string.IsNullOrWhiteSpace(applicationName))
            {
                Debug.LogError("UMA Build Sample requires an application name.");
                return;
            }
            if (useAddressables && !UMAAddressablesBuildSample.IsAvailable)
            {
                Debug.LogError("UMA Addressables Build Sample is unavailable. Install " +
                    "Addressables and enable UMA_ADDRESSABLES.");
                return;
            }
            if (!PrepareUmaContent(useAddressables))
                return;

#if UMA_ADDRESSABLES
            if (useAddressables &&
                !UMAAddressablesEditorBridge.TryBuildPlayerContent(out string error))
            {
                Debug.LogError("UMA Addressables player-content build failed: " + error);
                return;
            }
#endif

            try
            {
                BuildReport report = BuildPlayer(destinationFolder, developmentBuild,
                    applicationName);
                if (report == null)
                {
                    Debug.LogError("UMA Build Sample failed to produce a build report.");
                    return;
                }
                if (report.summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError("UMA Build Sample finished with result " +
                        report.summary.result + ".");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("UMA Build Sample failed: " + exception.Message);
                Debug.LogException(exception);
            }
        }

        internal static bool PrepareAddressables()
        {
            if (!UMAAddressablesBuildSample.IsAvailable)
            {
                Debug.LogError("UMA Addressables support is unavailable. Install " +
                    "Addressables and enable UMA_ADDRESSABLES.");
                return false;
            }
            return PrepareUmaContent(true);
        }

        private static bool PrepareUmaContent(bool useAddressables)
        {
            Debug.Log("UMA Build Sample - Rebuilding asset index.");
            UMAAssetIndexer assetIndex = UMAAssetIndexer.Instance;
            try
            {
                assetIndex.PrepareBuild();

#if UMA_ADDRESSABLES
                if (useAddressables)
                {
                    Debug.Log("UMA Build Sample - Generating UMA addressable labels.");
                    IUMAAddressablePlugin generator =
                        UMAAddressablesEditorBridge.CreateSingleGroupGenerator(true);
                    if (generator == null)
                        return false;
                    UMAAddressablesEditorBridge.GenerateAddressables(generator, assetIndex);

                    // Addressable flags have changed since PrepareBuild. Refresh the
                    // retained references so only non-addressable items remain embedded.
                    Debug.Log("UMA Build Sample - Refreshing UMA resource references.");
                    assetIndex.AddReferences();
                }
#else
                if (useAddressables)
                    return false;
#endif
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("UMA Build Sample preparation failed: " + exception.Message);
                Debug.LogException(exception);
                return false;
            }
        }

        private static BuildReport BuildPlayer(string destinationFolder,
            bool developmentBuild, string applicationName)
        {
            var scenePaths = new List<string>();
            EditorBuildSettingsScene[] editorScenes = EditorBuildSettings.scenes;
            for (int sceneIndex = 0; sceneIndex < editorScenes.Length; sceneIndex++)
            {
                EditorBuildSettingsScene scene = editorScenes[sceneIndex];
                if (scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                    scenePaths.Add(scene.path);
            }
            if (scenePaths.Count == 0)
            {
                Debug.LogError("UMA Build Sample requires at least one enabled scene in " +
                    "Build Profiles.");
                return null;
            }

            string resolvedDestination = Path.GetFullPath(destinationFolder);
            Directory.CreateDirectory(resolvedDestination);
            string locationPathName = Path.Combine(resolvedDestination, applicationName);
            BuildOptions options = developmentBuild
                ? BuildOptions.Development | BuildOptions.AllowDebugging
                : BuildOptions.None;

            Debug.Log("Building " + (developmentBuild ? "Development" : "Release") +
                " Player to " + locationPathName);
            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenePaths.ToArray(),
                locationPathName = locationPathName,
                target = EditorUserBuildSettings.activeBuildTarget,
                options = options
            };
            return BuildPipeline.BuildPlayer(buildPlayerOptions);
        }

        public static void PostBuildMaterialUpdate()
        {
            Debug.Log("UMA Post Build - Adding UMA resource references.");
            try
            {
                UMAAssetIndexer.Instance.PostBuildMaterialFixup();
            }
            catch (Exception exception)
            {
                Debug.LogError("UMA Post Build material update failed: " +
                    exception.Message);
                Debug.LogException(exception);
            }
        }
    }

    /// <summary>
    /// Addressables variant retained as a separate sample API. The class always
    /// compiles; only its optional Addressables operations are conditionally included.
    /// </summary>
    public static class UMAAddressablesBuildSample
    {
        public static bool IsAvailable
        {
            get
            {
#if UMA_ADDRESSABLES
                return UMAAddressablesEditorBridge.IsAvailable;
#else
                return false;
#endif
            }
        }

        public static void Build(string destinationFolder, bool developmentBuild,
            string applicationName)
        {
#if UMA_ADDRESSABLES
            UMABuildSample.BuildWithAddressables(destinationFolder, developmentBuild,
                applicationName);
#else
            Debug.LogError("UMA Addressables Build Sample is unavailable. Install " +
                "Addressables and enable UMA_ADDRESSABLES.");
#endif
        }

        public static void GenerateUMAAddressables()
        {
#if UMA_ADDRESSABLES
            UMABuildSample.PrepareAddressables();
#else
            Debug.LogError("UMA Addressables support is unavailable. Install " +
                "Addressables and enable UMA_ADDRESSABLES.");
#endif
        }

        public static void UMAPostBuildMaterialUpdate()
        {
            UMABuildSample.PostBuildMaterialUpdate();
        }
    }
}
