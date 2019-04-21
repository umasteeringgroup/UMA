#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using UMA;

namespace UMA.AssetBundles
{
	public class BuildScript
	{
		public static string overloadedDevelopmentServerURL = "";

		static public string CreateAssetBundleDirectory()
		{
			// Choose the output path according to the build target.
			string outputPath = Path.Combine(Utility.AssetBundlesOutputPath, Utility.GetPlatformName());
			if (!Directory.Exists(outputPath))
				Directory.CreateDirectory(outputPath);

			return outputPath;
		}

		public static void BuildAssetBundles()
		{
			var thisIndexAssetPath = "";
			var thisEncryptionAssetPath = "";
			try {
				// Choose the output path according to the build target.
				string outputPath = CreateAssetBundleDirectory();

				var options = BuildAssetBundleOptions.None;

				bool shouldCheckODR = EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS;
#if UNITY_TVOS
			shouldCheckODR |= EditorUserBuildSettings.activeBuildTarget == BuildTarget.tvOS;
#endif
				if (shouldCheckODR)
				{
#if ENABLE_IOS_ON_DEMAND_RESOURCES
					if (PlayerSettings.iOS.useOnDemandResources)
						options |= BuildAssetBundleOptions.UncompressedAssetBundle;
					else if(UMAABMSettings.GetEncryptionEnabled())
						options |= BuildAssetBundleOptions.ChunkBasedCompression;
#endif
#if ENABLE_IOS_APP_SLICING
					if(UMAABMSettings.GetBuildForSlicing())
						options |= BuildAssetBundleOptions.UncompressedAssetBundle;
					else if(!PlayerSettings.iOS.useOnDemandResources && UMAABMSettings.GetEncryptionEnabled())
						options |= BuildAssetBundleOptions.ChunkBasedCompression;
#endif
				}
				if (UMAABMSettings.GetEncryptionEnabled())
				{
					if (!shouldCheckODR)
						options |= BuildAssetBundleOptions.ChunkBasedCompression;
					options |= BuildAssetBundleOptions.ForceRebuildAssetBundle;
				}

				//AssetBundleIndex
				AssetBundleIndex thisIndex = ScriptableObject.CreateInstance<AssetBundleIndex>();
				//Added a bundlesPlayerVersion value to the assetBundles that can be used when determining
				//whether the app version matches the bundles version or if the bundles/application require updating.
				if (UMAABMSettings.EnableBundleIndexVersioning)
				{
					string versionErrMsg = "";
					if (UMAABMSettings.BundleIndexVersioningMethod == UMAABMSettingsStore.BundleIndexVersioningOpts.UseBuildVersion)
					{
						thisIndex.bundlesPlayerVersion = Application.version;
						versionErrMsg = "there was no 'Version' value set up in Edit->ProjectSettings->Player->Version";
					}
					else if (UMAABMSettings.BundleIndexVersioningMethod == UMAABMSettingsStore.BundleIndexVersioningOpts.Custom)
					{
						thisIndex.bundlesPlayerVersion = UMAABMSettings.BundleIndexCustomValue;
						versionErrMsg = "there was no value set up UMA-> UMA AssetBundle Manager window";
					}
					if (string.IsNullOrEmpty(thisIndex.bundlesPlayerVersion))
					{
						EditorUtility.DisplayDialog("Bundle Index Versioning Error", "You have turned on 'Bundle Index Versioning' but " + versionErrMsg + ". Please set that and try building your bundles again.", "OK");
						return;
					}
					else
					{
						if (!EditorUtility.DisplayDialog("Building Bundles", "Asset Bundles will be built with bundlesPlayerVersion value of '" + thisIndex.bundlesPlayerVersion + "' Is that correct?", "Build", "Cancel"))
						{
							//If the user did not aggree to that bail.
							return;
						}
					}
				}

				string[] assetBundleNamesArray = AssetDatabase.GetAllAssetBundleNames();
				//Generate a buildmap as we go
				AssetBundleBuild[] buildMap = new AssetBundleBuild[assetBundleNamesArray.Length + 1];//+1 for the index bundle
				for (int i = 0; i < assetBundleNamesArray.Length; i++)
				{
					//Building the map takes a while so show something...
					EditorUtility.DisplayProgressBar("Building Asset Bundles", "Creating Build Map", (1f / (float)assetBundleNamesArray.Length) * (i + 1) );

					string bundleName = assetBundleNamesArray[i];

					string[] assetBundleAssetsArray = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
					//If there are no assets added to this bundle show a warning telling the user to remove Unused asset bundle names
					if (assetBundleAssetsArray == null || assetBundleAssetsArray.Length == 0)
					{
						//if (Debug.isDebugBuild)//isDebugBuild is changed during the build process, this message always need to be logged
						Debug.Log(assetBundleNamesArray[i]+" was an empty assetBundle. Please do 'Remove Unused Names' in the 'Asset Labels' section of the Inspector");
					}

					thisIndex.bundlesIndex.Add(new AssetBundleIndex.AssetBundleIndexList(bundleName));

					if (bundleName.IndexOf('.') > -1)
					{
						buildMap[i].assetBundleName = bundleName.Split('.')[0];
						buildMap[i].assetBundleVariant = bundleName.Split('.')[1];
					}
					else
					{
						buildMap[i].assetBundleName = bundleName;
					}

					buildMap[i].assetNames = assetBundleAssetsArray;

					foreach (string path in assetBundleAssetsArray)
					{
						var sysPath = Path.Combine(Application.dataPath, path);
						var filename = Path.GetFileNameWithoutExtension(sysPath);
						var tempObj = AssetDatabase.LoadMainAssetAtPath(path);
						thisIndex.bundlesIndex[i].AddItem(filename, tempObj);
					}
				}

				EditorUtility.ClearProgressBar();

				thisIndexAssetPath = "Assets/" + Utility.GetPlatformName() + "Index.asset";
				thisIndex.name = "AssetBundleIndex";
				AssetDatabase.CreateAsset(thisIndex, thisIndexAssetPath);
				AssetImporter thisIndexAsset = AssetImporter.GetAtPath(thisIndexAssetPath);
				thisIndexAsset.assetBundleName = Utility.GetPlatformName() + "index";
				buildMap[assetBundleNamesArray.Length].assetBundleName = Utility.GetPlatformName() + "index";
				buildMap[assetBundleNamesArray.Length].assetNames = new string[1] { "Assets/" + Utility.GetPlatformName() + "Index.asset" };

				//Build the current state so we can get the AssetBundleManifest object and add its values to OUR index
				var assetBundleManifest = BuildPipeline.BuildAssetBundles(outputPath, buildMap, options, EditorUserBuildSettings.activeBuildTarget);
				if (assetBundleManifest == null)
				{
					throw new System.Exception("Your assetBundles did not build properly.");
				}
				thisIndex = AssetDatabase.LoadAssetAtPath<AssetBundleIndex>("Assets/" + Utility.GetPlatformName() + "Index.asset");
				//Get any bundles with variants
				string[] bundlesWithVariant = assetBundleManifest.GetAllAssetBundlesWithVariant();
				thisIndex.bundlesWithVariant = bundlesWithVariant;

				//then loop over each bundle in the bundle names and get the bundle specific data
				for (int i = 0; i < assetBundleNamesArray.Length; i++)
				{
					string[] assetBundleAssetsArray = AssetDatabase.GetAssetPathsFromAssetBundle(assetBundleNamesArray[i]);
					//If there are no assets added to this bundle continue because it wont be in the resulting assetBundleManifest
					if (assetBundleAssetsArray == null)
						continue;
					string assetBundleHash = assetBundleManifest.GetAssetBundleHash(assetBundleNamesArray[i]).ToString();
					string[] allDependencies = assetBundleManifest.GetAllDependencies(assetBundleNamesArray[i]);
					string[] directDependencies = assetBundleManifest.GetDirectDependencies(assetBundleNamesArray[i]);
					thisIndex.bundlesIndex[i].assetBundleHash = assetBundleHash;
					thisIndex.bundlesIndex[i].allDependencies = allDependencies;
					thisIndex.bundlesIndex[i].directDependencies = directDependencies;
					//Add suffixed names to the index if enabled and we are using encrption
					//we cant append the suffix to the index file because when we are loading we dont know the set suffix or suffixed names until we have the index
					//it also cant be the same as the unencrypted name because it needs a different memory address, so its going to have to be name + "encrypted"
					if (UMAABMSettings.GetEncryptionEnabled())
					{
						var encryptedBundleName = assetBundleNamesArray[i] == Utility.GetPlatformName() + "index" ? assetBundleNamesArray[i] + "encrypted" : assetBundleNamesArray[i] + UMAABMSettings.GetEncryptionSuffix();
						if (UMAABMSettings.GetEncodeNames() && assetBundleNamesArray[i] != Utility.GetPlatformName() + "index")
							encryptedBundleName = EncryptionUtil.EncodeFileName(encryptedBundleName);
						thisIndex.bundlesIndex[i].encryptedName = encryptedBundleName;
					}
				}
				var relativeAssetBundlesOutputPathForPlatform = Path.Combine(Utility.AssetBundlesOutputPath, Utility.GetPlatformName());
				//Update and Save the index asset and build again. This will store the updated asset in the windowsindex asset bundle
				EditorUtility.SetDirty(thisIndex);
				AssetDatabase.SaveAssets();
				//Build the Index AssetBundle
				var indexBuildMap = new AssetBundleBuild[1];
				indexBuildMap[0] = buildMap[assetBundleNamesArray.Length];
				BuildPipeline.BuildAssetBundles(outputPath, indexBuildMap, options, EditorUserBuildSettings.activeBuildTarget);

				//Save a json version of the data- this can be used for uploading to a server to update a database or something
				string thisIndexJson = JsonUtility.ToJson(thisIndex);
				var thisIndexJsonPath = Path.Combine(relativeAssetBundlesOutputPathForPlatform, Utility.GetPlatformName().ToLower()) + "index.json";
				File.WriteAllText(thisIndexJsonPath, thisIndexJson);
				//Build Encrypted Bundles
				if (UMAABMSettings.GetEncryptionEnabled())
				{
					var encryptedBuildMap = new AssetBundleBuild[1];
					var EncryptionAsset = ScriptableObject.CreateInstance<UMAEncryptedBundle>();
					EncryptionAsset.name = "EncryptedData";
					thisEncryptionAssetPath = "Assets/EncryptedData.asset";
					AssetDatabase.CreateAsset(EncryptionAsset, thisEncryptionAssetPath);
					//var encryptedOutputPath = Path.Combine(outputPath, "Encrypted");
					var encryptedOutputPath = Path.Combine(Utility.AssetBundlesOutputPath, Path.Combine("Encrypted", Utility.GetPlatformName()));
					if (!Directory.Exists(encryptedOutputPath))
						Directory.CreateDirectory(encryptedOutputPath);
					for (int bmi = 0; bmi < buildMap.Length; bmi++)
					{
						var thisEncryptionAsset = AssetDatabase.LoadAssetAtPath<UMAEncryptedBundle>(thisEncryptionAssetPath);
						//get the data from the unencrypted bundle and encrypt it into the EncryptedData asset
						thisEncryptionAsset.GenerateData(buildMap[bmi].assetBundleName, Path.Combine(relativeAssetBundlesOutputPathForPlatform, buildMap[bmi].assetBundleName));
						EditorUtility.SetDirty(thisEncryptionAsset);
						AssetDatabase.SaveAssets();
						//Sort out the name of this encrypted bundle
						var encryptedBundleName = "";
						if (buildMap[bmi].assetBundleName != Utility.GetPlatformName() + "index")
						{
							encryptedBundleName = buildMap[bmi].assetBundleName + UMAABMSettings.GetEncryptionSuffix();
							if (UMAABMSettings.GetEncodeNames() && buildMap[bmi].assetBundleName != Utility.GetPlatformName() + "index")
								encryptedBundleName = EncryptionUtil.EncodeFileName(encryptedBundleName);
						}
						else
						{
							encryptedBundleName = buildMap[bmi].assetBundleName + "encrypted";
						}
						//set the Build map value
						encryptedBuildMap[0].assetBundleName = encryptedBundleName;
						encryptedBuildMap[0].assetNames = new string[1] { "Assets/EncryptedData.asset" };
						//and build the bundle
						BuildPipeline.BuildAssetBundles(encryptedOutputPath, encryptedBuildMap, BuildAssetBundleOptions.None, EditorUserBuildSettings.activeBuildTarget);
					}
					//save a json index in there too
					var thisIndexJsonEncPath = Path.Combine(encryptedOutputPath, Utility.GetPlatformName().ToLower()) + "indexencrypted.json";
					File.WriteAllText(thisIndexJsonEncPath, thisIndexJson);
					AssetDatabase.DeleteAsset(thisEncryptionAssetPath);
				}
				//Now we can remove the temp Index item from the assetDatabase
				AssetDatabase.DeleteAsset(thisIndexAssetPath);
				//And remove its assetBundle name from the assetBundleNames
				AssetDatabase.RemoveAssetBundleName(Utility.GetPlatformName().ToLower() + "index", false);
				//if (Debug.isDebugBuild)//isDebugBuild is changed during the build process, this message always need to be logged
				Debug.Log("Asset Bundles built successfully for platform "+Utility.GetPlatformName()+(UMAABMSettings.GetEncryptionEnabled() ? " (Encrypted)!" : "!"));
			}
			catch (System.Exception e)
			{
				if(thisIndexAssetPath != "")
					AssetDatabase.DeleteAsset(thisIndexAssetPath);
				if(thisEncryptionAssetPath != "")
					AssetDatabase.DeleteAsset(thisEncryptionAssetPath);
				//And remove its assetBundle name from the assetBundleNames
				AssetDatabase.RemoveAssetBundleName(Utility.GetPlatformName().ToLower() + "index", false);
				//if (Debug.isDebugBuild)//isDebugBuild is changed during the build process, this message always need to be logged
				Debug.LogError("Your AssetBundles did not build properly. Error Message: " + e.Message+" Error Exception: "+e.InnerException+" Error StackTrace: "+e.StackTrace);
			}
		}


		public static void BuildAndRunPlayer(bool developmentBuild)
		{
			//This should ask where people want to build the player
			//
			string outputPath = EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL ? Utility.AssetBundlesOutputPath : (EditorUserBuildSettings.activeBuildTarget.ToString().IndexOf("Standalone") > -1 ? "Builds-Standalone" : "Builds-Devices");
			if (!Directory.Exists(outputPath))
				Directory.CreateDirectory(outputPath);
			//IMPORTANT Standalone Builds DELETE everything in the folder they are saved in- so building into the AssetBundles Folder DELETES ALL ASSETBUNDLES
			string[] levels = GetLevelsFromBuildSettings();
			if (levels.Length == 0)
			{
				//if (Debug.isDebugBuild)//isDebugBuild is changed during the build process, this message always need to be logged
				Debug.LogWarning("There were no Scenes in you Build Settings. Adding the current active Scene.");
				levels = new string[1] { UnityEngine.SceneManagement.SceneManager.GetActiveScene().path };
			}
			string targetName = GetBuildTargetName(EditorUserBuildSettings.activeBuildTarget);
			if (targetName == null)
				return;
			//For Standalone or WebGL that can run locally make the server write a file with its current setting that it can get when the game runs if the localserver is enabled
			if (SimpleWebServer.serverStarted && CanRunLocally(EditorUserBuildSettings.activeBuildTarget))
				SimpleWebServer.WriteServerURL();
			else if (SimpleWebServer.serverStarted && !CanRunLocally(EditorUserBuildSettings.activeBuildTarget))
			{
				//if (Debug.isDebugBuild)//isDebugBuild is changed during the build process, this message always need to be logged
				Debug.LogWarning("Builds for " + EditorUserBuildSettings.activeBuildTarget.ToString() + " cannot access the LocalServer. AssetBundles will be downloaded from the remoteServerUrl's");
			}
			//BuildOptions
			BuildOptions option = BuildOptions.None;
			if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
			{
				option = developmentBuild ? BuildOptions.Development : BuildOptions.None;
			}
			else
			{
				option = developmentBuild ? BuildOptions.Development | BuildOptions.AutoRunPlayer : BuildOptions.AutoRunPlayer;
			}

			BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
			buildPlayerOptions.scenes = levels;
			buildPlayerOptions.locationPathName = outputPath + targetName;
			buildPlayerOptions.assetBundleManifestPath = GetAssetBundleManifestFilePath();
			buildPlayerOptions.target = EditorUserBuildSettings.activeBuildTarget;
			buildPlayerOptions.options = option;

#if UNITY_2018_1_OR_NEWER
			UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
#else
			string report = BuildPipeline.BuildPlayer(buildPlayerOptions);
#endif
			//always destroy the serverURL file if one was created
			if (SimpleWebServer.serverStarted && CanRunLocally(EditorUserBuildSettings.activeBuildTarget))
				SimpleWebServer.DestroyServerURLFile();
#if UNITY_2018_1_OR_NEWER
			if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
			{
#else
			if(string.IsNullOrEmpty(report))
			{
#endif
				string fullPathToBuild = Path.Combine(Directory.GetParent(Application.dataPath).FullName, outputPath);
				//if (Debug.isDebugBuild)//isDebugBuild is changed during the build process, this message always need to be logged
				Debug.Log("Build Successful! Build Location: " + fullPathToBuild);
				if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
				{
					Application.OpenURL(SimpleWebServer.ServerURL + "index.html");
				}
			}
		}

		//DOS NOTES This is called by the Menu Item Assets/AssetBundles/Build Player (for use with engine code stripping)
		//Not sure we need it.
		public static void BuildPlayer()
		{
			var outputPath = EditorUtility.SaveFolderPanel("Choose Location of the Built Game", "", "");
			if (outputPath.Length == 0)
				return;

			string[] levels = GetLevelsFromBuildSettings();
			if (levels.Length == 0)
			{
				if (Debug.isDebugBuild)
					Debug.Log("Nothing to build.");
				return;
			}

			string targetName = GetBuildTargetName(EditorUserBuildSettings.activeBuildTarget);
			if (targetName == null)
				return;

			// Build and copy AssetBundles.
			BuildScript.BuildAssetBundles();
			//DOS NOTES this was added in the latest pull requests for the original AssetBundleManager not sure why?
			BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
			buildPlayerOptions.scenes = levels;
			buildPlayerOptions.locationPathName = outputPath + targetName;
			buildPlayerOptions.assetBundleManifestPath = GetAssetBundleManifestFilePath();
			buildPlayerOptions.target = EditorUserBuildSettings.activeBuildTarget;
			buildPlayerOptions.options = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None;
			BuildPipeline.BuildPlayer(buildPlayerOptions);
		}

		public static void BuildStandalonePlayer()
		{
			var outputPath = EditorUtility.SaveFolderPanel("Choose Location of the Built Game", "", "");
			if (outputPath.Length == 0)
				return;

			string[] levels = GetLevelsFromBuildSettings();
			if (levels.Length == 0)
			{
				if (Debug.isDebugBuild)
					Debug.Log("Nothing to build.");
				return;
			}

			string targetName = GetBuildTargetName(EditorUserBuildSettings.activeBuildTarget);
			if (targetName == null)
				return;

			// Build and copy AssetBundles.
			BuildScript.BuildAssetBundles();
			BuildScript.CopyAssetBundlesTo(Path.Combine(Application.streamingAssetsPath, Utility.AssetBundlesOutputPath));
			AssetDatabase.Refresh();

			BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
			buildPlayerOptions.scenes = levels;
			buildPlayerOptions.locationPathName = outputPath + targetName;
			buildPlayerOptions.assetBundleManifestPath = GetAssetBundleManifestFilePath();
			buildPlayerOptions.target = EditorUserBuildSettings.activeBuildTarget;
			buildPlayerOptions.options = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None;
			BuildPipeline.BuildPlayer(buildPlayerOptions);
		}
		/// <summary>
		/// Returns true if the build can potentially run on the current machine (a local build)
		/// </summary>
		/// <param name="target"></param>
		/// <returns></returns>
		public static bool CanRunLocally(BuildTarget target)
		{
			var currentEnvironment = Application.platform.ToString();
			switch (target)
			{
				case BuildTarget.StandaloneLinux:
				case BuildTarget.StandaloneLinux64:
				case BuildTarget.StandaloneLinuxUniversal:
					if (currentEnvironment.IndexOf("Linux") > -1)
						return true;
					else
						return false;
				case BuildTarget.StandaloneWindows:
				case BuildTarget.StandaloneWindows64:
				case BuildTarget.WSAPlayer:
					if (currentEnvironment.IndexOf("Windows") > -1 || currentEnvironment.IndexOf("WSA") > -1)
						return true;
					else
						return false;
#if UNITY_2017_3_OR_NEWER
				case BuildTarget.StandaloneOSX:
#else
				case BuildTarget.StandaloneOSXIntel:
#endif
					if (currentEnvironment.IndexOf("OSX") > -1)
						return true;
					else
						return false;
				case BuildTarget.WebGL:
					return true;
				default:
					return false;
			}

		}

		public static string GetBuildTargetName(BuildTarget target)
		{
			switch (target)
			{
				case BuildTarget.Android:
					return "/test.apk";
				case BuildTarget.StandaloneWindows:
				case BuildTarget.StandaloneWindows64:
					return "/test.exe";
#if UNITY_2017_3_OR_NEWER
				case BuildTarget.StandaloneOSX:
#else
				case BuildTarget.StandaloneOSXIntel:
#endif
					return "/test.app";
				case BuildTarget.WebGL:
				case BuildTarget.iOS:
					return "";
				// Add more build targets for your own.
				default:
					if (Debug.isDebugBuild)
						Debug.Log("Target not implemented.");
					return null;
			}
		}

		static void CopyAssetBundlesTo(string outputPath)
		{
			// Clear streaming assets folder.
			FileUtil.DeleteFileOrDirectory(Application.streamingAssetsPath);
			Directory.CreateDirectory(outputPath);

			string outputFolder = Utility.GetPlatformName();

			// Setup the source folder for assetbundles.
			var source = Path.Combine(Path.Combine(System.Environment.CurrentDirectory, Utility.AssetBundlesOutputPath), outputFolder);
			if (!System.IO.Directory.Exists(source))
			{
				if (Debug.isDebugBuild)
					Debug.Log("No assetBundle output folder, try to build the assetBundles first.");
			}

			// Setup the destination folder for assetbundles.
			var destination = System.IO.Path.Combine(outputPath, outputFolder);
			if (System.IO.Directory.Exists(destination))
				FileUtil.DeleteFileOrDirectory(destination);

			FileUtil.CopyFileOrDirectory(source, destination);
		}

		static string[] GetLevelsFromBuildSettings()
		{
			List<string> levels = new List<string>();
			for (int i = 0; i < EditorBuildSettings.scenes.Length; ++i)
			{
				if (EditorBuildSettings.scenes[i].enabled)
					levels.Add(EditorBuildSettings.scenes[i].path);
			}

			return levels.ToArray();
		}


		static string GetAssetBundleManifestFilePath()
		{
			var relativeAssetBundlesOutputPathForPlatform = Path.Combine(Utility.AssetBundlesOutputPath, Utility.GetPlatformName());
			return Path.Combine(relativeAssetBundlesOutputPathForPlatform, Utility.GetPlatformName()) + ".manifest";
		}
	}
}
#endif
