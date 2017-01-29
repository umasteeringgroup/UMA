using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;

/*  The AssetBundle Manager provides a High-Level API for working with AssetBundles. 
    The AssetBundle Manager will take care of loading AssetBundles and their associated 
    Asset Dependencies.
        Initialize()
            Initializes the AssetBundle index object. This contains the standard Unity AssetBundleIndex data as well as an index of what assets are in what asset bundles
        LoadAssetAsync()
            Loads a given asset from a given AssetBundle and handles all the dependencies.
        LoadLevelAsync()
            Loads a given scene from a given AssetBundle and handles all the dependencies.
        LoadDependencies()
            Loads all the dependent AssetBundles for a given AssetBundle.
        BaseDownloadingURL
            Sets the base downloading url which is used for automatic downloading dependencies.
        SimulateAssetBundleInEditor
            Sets Simulation Mode in the Editor.
        Variants
            Sets the active variant.
        RemapVariantName()
            Resolves the correct AssetBundle according to the active variant.
*/

namespace UMAAssetBundleManager
{

	/// <summary>
	/// After an asset bundles download or cache retrieval opertaion is complete a LoadaedAssetBundle object is created for it. 
	/// Loaded assetBundle contains the references count which can be used to unload dependent assetBundles automatically.
	/// </summary>
	public class LoadedAssetBundle
	{
		public AssetBundle m_AssetBundle;
		public int m_ReferencedCount;
		//to enable a json index we need to have a string/data field here
		public string m_data;

		internal event Action unload;

		internal void OnUnload()
		{
			m_AssetBundle.Unload(false);
			if (unload != null)
				unload();
		}

		public LoadedAssetBundle(AssetBundle assetBundle)
		{
			m_AssetBundle = assetBundle;
			m_ReferencedCount = 1;
		}
		public LoadedAssetBundle(string data)
		{
			m_AssetBundle = null;
			m_data = data;
			m_ReferencedCount = 1;
		}
	}

	/// <summary>
	/// Class takes care of loading assetBundle and its dependencies automatically, loading variants automatically.
	/// </summary>
	public class AssetBundleManager : MonoBehaviour
	{
		public enum LogMode { All, JustErrors };
		public enum LogType { Info, Warning, Error };

		static LogMode m_LogMode = LogMode.All;
		static string m_BaseDownloadingURL = "";

		//If we are using Encrypted Bundles DynamicAssetLoader will set the encryption key here.
		static string m_BundleEncryptionKey = "";

		static string[] m_ActiveVariants = { };
		static AssetBundleIndex m_AssetBundleIndex = null;
#if UNITY_EDITOR
		static int m_SimulateAssetBundleInEditor = -1;
		const string kSimulateAssetBundles = "SimulateAssetBundles";
		static SimpleWebServer webserver;
#endif

		static Dictionary<string, LoadedAssetBundle> m_LoadedAssetBundles = new Dictionary<string, LoadedAssetBundle>();
		static Dictionary<string, string> m_DownloadingErrors = new Dictionary<string, string>();
		static List<string> m_DownloadingBundles = new List<string>();
		static List<AssetBundleLoadOperation> m_InProgressOperations = new List<AssetBundleLoadOperation>();
		static Dictionary<string, string[]> m_Dependencies = new Dictionary<string, string[]>();

		public static bool SimulateOverride;

		public static LogMode logMode
		{
			get { return m_LogMode; }
			set { m_LogMode = value; }
		}

		/// <summary>
        /// The base downloading url which is used to generate the full
        /// downloading url with the assetBundle names.
        /// </summary>
		public static string BaseDownloadingURL
		{
			get { return m_BaseDownloadingURL; }
			set { m_BaseDownloadingURL = value; }
		}

		public static string BundleEncryptionKey
		{
			get { return m_BundleEncryptionKey; }
			set { m_BundleEncryptionKey = value; }
		}

		public delegate string OverrideBaseDownloadingURLDelegate(string bundleName);

		/// <summary>
		/// Implements per-bundle base downloading URL override.
		/// The subscribers must return null values for unknown bundle names;
		/// </summary>
		public static event OverrideBaseDownloadingURLDelegate overrideBaseDownloadingURL;

		/// <summary>
		/// Variants which is used to define the active variants.
		/// </summary>
		public static string[] ActiveVariants
		{
			get { return m_ActiveVariants; }
			set { m_ActiveVariants = value; }
		}

		/// <summary>
		/// AssetBundleIndex object which can be used to check the contents of
	   ///  any asset bundle without having to download it first. 
		/// </summary>
		public static AssetBundleIndex AssetBundleIndexObject
		{
			get { return m_AssetBundleIndex; }
			set { m_AssetBundleIndex = value; }
		}

		private static void Log(LogType logType, string text)
		{
			if (logType == LogType.Error)
				Debug.LogError("[AssetBundleManager] " + text);
			else if (m_LogMode == LogMode.All)
				Debug.Log("[AssetBundleManager] " + text);
		}

#if UNITY_EDITOR
		// Flag to indicate if we want to simulate assetBundles in Editor without building them actually.
		//we dont want an editorPrefs for this now because there is no way of changing it!
		public static bool SimulateAssetBundleInEditor
		{
			get
			{
				if (SimulateOverride)
					return true;
				if (Application.isPlaying == false) // always simulate when out of play mode
					return true;

				if (m_SimulateAssetBundleInEditor == -1)
					m_SimulateAssetBundleInEditor = 0;

				return m_SimulateAssetBundleInEditor != 0;
			}
			set
			{
				int newValue = value ? 1 : 0;
				if (newValue != m_SimulateAssetBundleInEditor)
				{
					m_SimulateAssetBundleInEditor = newValue;
				}
			}
		}
#else
        public static bool SimulateAssetBundleInEditor
        {
            get { return false;}
        }
#endif

		private static string GetStreamingAssetsPath()
		{
			if (Application.isEditor)
				return "file://" + System.Environment.CurrentDirectory.Replace("\\", "/"); // Use the build output folder directly.
			else if (Application.isWebPlayer)
				return System.IO.Path.GetDirectoryName(Application.absoluteURL).Replace("\\", "/") + "/StreamingAssets";
			else if (Application.isMobilePlatform || Application.isConsolePlatform)
				return Application.streamingAssetsPath;
			else // For standalone player.
				return "file://" + Application.streamingAssetsPath;
		}

		/// <summary>
		/// Sets base downloading URL to a directory relative to the streaming assets directory.Asset bundles are loaded from a local directory.
		/// </summary>
		public static void SetSourceAssetBundleDirectory(string relativePath)
		{
			BaseDownloadingURL = GetStreamingAssetsPath() + relativePath;
		}

		/// <summary>
		/// Sets base downloading URL to a web URL. The directory pointed to by this URL
		/// on the web-server should have the same structure as the AssetBundles directory
		/// in the demo project root. For example, AssetBundles/iOS/xyz-scene must map to
		/// absolutePath/iOS/xyz-scene.
		/// If you are using assetBundle encryption this should be absolutePath/Encrypted/iOS/xyz-scene
		/// </summary>
		/// <param name="absolutePath"></param>
		public static void SetSourceAssetBundleURL(string absolutePath)
		{
			string encryptedSuffix = m_BundleEncryptionKey != "" ? "Encrypted/" : "";
			if (absolutePath != "")
			{
				if (!absolutePath.EndsWith("/"))
					absolutePath += "/";
				Debug.Log("[AssetBundleManager] SetSourceAssetBundleURL to " + absolutePath + encryptedSuffix + Utility.GetPlatformName() + "/");
				BaseDownloadingURL = absolutePath + encryptedSuffix + Utility.GetPlatformName() + "/";
			}
		}

		/// <summary>
		/// Retrieves an asset bundle that has previously been requested via LoadAssetBundle.
		/// Returns null if the asset bundle or one of its dependencies have not been downloaded yet.
		/// </summary>
		/// <param name="assetBundleName"></param>
		/// <param name="error"></param>
		/// <returns></returns>
		static public LoadedAssetBundle GetLoadedAssetBundle(string assetBundleName, out string error)
		{
			if (m_DownloadingErrors.TryGetValue(assetBundleName, out error))
			{
				if (!error.StartsWith("-"))
				{
					m_DownloadingErrors[assetBundleName] = "-" + error;
#if UNITY_EDITOR
					if (assetBundleName == Utility.GetPlatformName().ToLower() + "index")
					{
						if (EditorPrefs.GetBool(Application.dataPath+"LocalAssetBundleServerEnabled") == false || SimpleWebServer.serverStarted == false)//when the user restarts Unity this might be true even if the server has not actually been started
						{
							if (SimulateAssetBundleInEditor)
							{
								//we already outputted a message in DynamicAssetloader
							}
							else
							{
								Debug.LogWarning("AssetBundleManager could not download the AssetBundleIndex from the Remote Server URL you have set in DynamicAssetLoader. Have you set the URL correctly and uploaded your AssetBundles?");
								error = "AssetBundleManager could not download the AssetBundleIndex from the Remote Server URL you have set in DynamicAssetLoader. Have you set the URL correctly and uploaded your AssetBundles?";
							}
						}
						else
						{

							//Otherwise the AssetBundles themselves will not have been built.
							Debug.LogWarning("Switched to Simulation mode because no AssetBundles were found. Have you build them? (Go to 'Assets/AssetBundles/Build AssetBundles').");
							error = "Switched to Simulation mode because no AssetBundles were found.Have you build them? (Go to 'Assets/AssetBundles/Build AssetBundles').";
							//this needs to hide the loading infobox- or something needs too..
						}
						SimulateOverride = true;
					}
					else
#endif
						Debug.LogWarning("Could not return " + assetBundleName + " because of error:" + error);
				}
				return null;
			}

			LoadedAssetBundle bundle = null;
			m_LoadedAssetBundles.TryGetValue(assetBundleName, out bundle);
			if (bundle == null)
				return null;

			// No dependencies are recorded, only the bundle itself is required.
			string[] dependencies = null;
			if (!m_Dependencies.TryGetValue(assetBundleName, out dependencies))
				return bundle;

			// Otherwise Make sure all dependencies are loaded
			foreach (var dependency in dependencies)
			{

				if (m_DownloadingErrors.TryGetValue(dependency, out error))
					return null;
				// Wait all the dependent assetBundles being loaded.
				LoadedAssetBundle dependentBundle = null;
				m_LoadedAssetBundles.TryGetValue(dependency, out dependentBundle);
				if (dependentBundle == null)
					return null;
			}
			return bundle;
		}

		/// <summary>
		/// Returns the download progress of an assetbundle, optionally including any bundles it is dependent on
		/// </summary>
		static public float GetBundleDownloadProgress(string assetBundleName, bool andDependencies)
		{
			float overallProgress = 0;
			string error;
			if (m_DownloadingErrors.TryGetValue(assetBundleName, out error))
			{
				Debug.LogWarning(error);
				return 0;
			}

			if (m_LoadedAssetBundles.ContainsKey(assetBundleName))
			{
				overallProgress = 1f;
			}
			else
			{
				//find out its progress
				foreach (AssetBundleLoadOperation operation in m_InProgressOperations)
				{
					if (operation.GetType() == typeof(AssetBundleDownloadOperation) || operation.GetType().IsSubclassOf(typeof(AssetBundleDownloadOperation)))
					{
						AssetBundleDownloadOperation typedOperation = (AssetBundleDownloadOperation)operation;
						if (typedOperation.assetBundleName == assetBundleName)
							overallProgress = typedOperation.downloadProgress == 1f ? 0.99f : typedOperation.downloadProgress;
					}
				}
			}
			//deal with dependencies if necessary
			if (andDependencies)
			{
				string[] dependencies = null;
				m_Dependencies.TryGetValue(assetBundleName, out dependencies);
				if (dependencies != null)
				{
					if (dependencies.Length > 0)
					{
						foreach (string dependency in dependencies)
						{
							if (m_LoadedAssetBundles.ContainsKey(dependency))
							{
								overallProgress += 1;
							}
							else //It must be in progress
							{
								foreach (AssetBundleLoadOperation operation in m_InProgressOperations)
								{
									if (operation.GetType() == typeof(AssetBundleDownloadOperation) || operation.GetType().IsSubclassOf(typeof(AssetBundleDownloadOperation)))
									{
										AssetBundleDownloadOperation typedOperation = (AssetBundleDownloadOperation)operation;
										if (typedOperation.assetBundleName == dependency)
											overallProgress += typedOperation.downloadProgress == 1f ? 0.99f : typedOperation.downloadProgress;
									}
								}
							}
						}
						//divide by num dependencies +1
						overallProgress = overallProgress / (dependencies.Length + 1);
					}
				}
			}
			return overallProgress;
		}

		/// <summary>
		/// Returns the current LoadedAssetBundlesDictionary
		/// </summary>
		static public Dictionary<string, LoadedAssetBundle> GetLoadedAssetBundles()
		{
			return m_LoadedAssetBundles;
		}

		/// <summary>
		/// Returns true if certain asset bundle has been downloaded regardless of whether its 
		/// whether it's dependencies have been loaded.
		/// </summary>
		static public bool IsAssetBundleDownloaded(string assetBundleName)
		{
			return m_LoadedAssetBundles.ContainsKey(assetBundleName);
		}

		/// <summary>
		/// Returns true if any asset bundles are still downloading optionally filtered by name.
		/// </summary>
		static public bool AreBundlesDownloading(string assetBundleName = "")
		{
			if (assetBundleName == "")
			{
				return (m_DownloadingBundles.Count > 0 && m_InProgressOperations.Count > 0);
			}
			else
			{
				if (m_DownloadingBundles.Contains(assetBundleName))
				{
					return true;
				}
				else
				{
					foreach (string key in m_DownloadingBundles)
					{
						if (key.IndexOf(assetBundleName + "/") > -1)
						{
							return true;
						}
					}
					return false;
				}
			}
		}

		static public bool IsOperationInProgress(AssetBundleLoadOperation operation)
		{
			if (m_InProgressOperations.Contains(operation))
				return true;
			else
				return false;
		}

		/// <summary>
		/// Initializes asset bundle namager and starts download of index asset bundle
		/// </summary>
		/// <returns>Returns the index asset bundle download operation object.</returns>
		// TODO I think that the index should be available if the device is offline. 
		// Right now I think ABM always tries to download the index and the game will break if it cant.
		// What I think should happen is that if the game is offline it should still be able to get a cached index
		static public AssetBundleLoadIndexOperation Initialize()
		{
			return Initialize(Utility.GetPlatformName(), false, "");
		}

		static public AssetBundleLoadIndexOperation Initialize(bool useJsonIndex)
		{
			return Initialize(Utility.GetPlatformName(), useJsonIndex, "");
		}

		static public AssetBundleLoadIndexOperation Initialize(bool useJsonIndex, string jsonIndexUrl)
		{
			return Initialize(Utility.GetPlatformName(), useJsonIndex, jsonIndexUrl);
		}

		static public AssetBundleLoadIndexOperation Initialize(string indexAssetBundleName, bool useJsonIndex, string jsonIndexUrl)
		{
			if (!SimulateAssetBundleInEditor)//dont show the indicator if we are not using asset bundles - TODO we need a more comprehensive solution for this scenerio
			{
				if(AssetBundleLoadingIndicator.Instance)
					AssetBundleLoadingIndicator.Instance.Show(indexAssetBundleName.ToLower() + "index", "Initializing...", "", "Initialized");
			}
#if UNITY_EDITOR
			Log(LogType.Info, "Simulation Mode: " + (SimulateAssetBundleInEditor ? "Enabled" : "Disabled"));
#endif

			var go = new GameObject("AssetBundleManager", typeof(AssetBundleManager));
			DontDestroyOnLoad(go);

#if UNITY_EDITOR
			// If we're in Editor simulation mode, we don't need the index assetBundle.
			if (SimulateAssetBundleInEditor)
				return null;
#endif
			//as of 05/08/2016 we dont use Unitys AssetBundleManifest at all we just use our AssetBundleIndex
			LoadAssetBundle(indexAssetBundleName.ToLower() + "index", true, useJsonIndex, jsonIndexUrl);
			var operation = new AssetBundleLoadIndexOperation(indexAssetBundleName.ToLower() + "index", indexAssetBundleName + "Index", typeof(AssetBundleIndex), useJsonIndex);
			m_InProgressOperations.Add(operation);
			return operation;
		}

		// Temporarily work around a il2cpp bug
		static protected void LoadAssetBundle(string assetBundleName)
		{
			LoadAssetBundle(assetBundleName, false);
		}

		/// <summary>
		/// Starts the download of the asset bundle identified by the given name. Also downloads any asset bundles the given asset bundle is dependent on.
		/// </summary>
		/// <param name="assetBundleName">The bundle to load- if bundles are encrypted this should be the name of the UNENCRYPTED bundle.</param>
		/// <param name="isLoadingAssetBundleIndex">If true does not check for the existance of the assetBundleIndex. This should be false unless you ARE downloading the index</param>
		/// <param name="useJsonIndex">if true will attempt to download an asset called [platformname]index.json unless a specific json Url is supplied in the following param</param>
		/// <param name="jsonIndexUrl">provides a specific url to download a json index from</param>
		public static void LoadAssetBundle(string assetBundleName, bool isLoadingAssetBundleIndex = false, bool useJsonIndex = false, string jsonIndexUrl = "")
		{
#if UNITY_EDITOR
			string fromLocalServer = (EditorPrefs.GetBool(Application.dataPath+"LocalAssetBundleServerEnabled") && SimpleWebServer.serverStarted) ? "from LocalServer " : "";
			string encrypted = BundleEncryptionKey != "" ? " (Encrypted)" : "";
			Log(LogType.Info, "Loading Asset Bundle " + fromLocalServer + (isLoadingAssetBundleIndex ? "Index: " : ": ") + assetBundleName + encrypted);

			// If we're in Editor simulation mode, we don't have to really load the assetBundle and its dependencies.
			if (SimulateAssetBundleInEditor)
				return;
#endif

			if (!isLoadingAssetBundleIndex)
			{
				if (m_AssetBundleIndex == null)
				{
					Debug.LogError("Please initialize AssetBundleIndex by calling AssetBundleManager.Initialize()");
					return;
				}
			}

			// Check if the assetBundle has already been processed.
			bool isAlreadyProcessed = LoadAssetBundleInternal(assetBundleName, isLoadingAssetBundleIndex, useJsonIndex, jsonIndexUrl);

			// Load dependencies.
			if (!isAlreadyProcessed && !isLoadingAssetBundleIndex)
				LoadDependencies(assetBundleName);
		}

		/// <summary>
		/// Returns base downloading URL for the given asset bundle.
		/// This URL may be overridden on per-bundle basis via overrideBaseDownloadingURL event.
		/// </summary>
		protected static string GetAssetBundleBaseDownloadingURL(string bundleName)
		{
			if (overrideBaseDownloadingURL != null)
			{
				foreach (OverrideBaseDownloadingURLDelegate method in overrideBaseDownloadingURL.GetInvocationList())
				{
					string res = method(bundleName);
					if (res != null)
						return res;
				}
			}
			return m_BaseDownloadingURL;
		}

		/// <summary>
		/// Checks who is responsible for determination of the correct asset bundle variant that should be loaded on this platform. 
		/// 
		/// On most platforms, this is done by the AssetBundleManager itself. However, on
		/// certain platforms (iOS at the moment) it's possible that an external asset bundle
		/// variant resolution mechanism is used. In these cases, we use base asset bundle 
		/// name (without the variant tag) as the bundle identifier. The platform-specific 
		/// code is responsible for correctly loading the bundle.
		/// </summary>
		static protected bool UsesExternalBundleVariantResolutionMechanism(string baseAssetBundleName)
		{
#if ENABLE_IOS_APP_SLICING
            var url = GetAssetBundleBaseDownloadingURL(baseAssetBundleName);
            if (url.ToLower().StartsWith("res://") ||
                url.ToLower().StartsWith("odr://"))
                return true;
#endif
			return false;
		}

		/// <summary>
		/// Remaps the asset bundle name to the best fitting asset bundle variant.
		/// </summary>
		static protected string RemapVariantName(string assetBundleName)
		{
			string[] bundlesWithVariant = m_AssetBundleIndex.GetAllAssetBundlesWithVariant();

			// Get base bundle name
			string baseName = assetBundleName.Split('.')[0];

			if (UsesExternalBundleVariantResolutionMechanism(baseName))
				return baseName;

			int bestFit = int.MaxValue;
			int bestFitIndex = -1;
			// Loop all the assetBundles with variant to find the best fit variant assetBundle.
			for (int i = 0; i < bundlesWithVariant.Length; i++)
			{
				string[] curSplit = bundlesWithVariant[i].Split('.');
				string curBaseName = curSplit[0];
				string curVariant = curSplit[1];

				if (curBaseName != baseName)
					continue;

				int found = System.Array.IndexOf(m_ActiveVariants, curVariant);

				// If there is no active variant found. We still want to use the first
				if (found == -1)
					found = int.MaxValue - 1;

				if (found < bestFit)
				{
					bestFit = found;
					bestFitIndex = i;
				}
			}

			if (bestFit == int.MaxValue - 1)
			{
				Log(LogType.Warning, "Ambigious asset bundle variant chosen because there was no matching active variant: " + bundlesWithVariant[bestFitIndex]);
			}

			if (bestFitIndex != -1)
			{
				return bundlesWithVariant[bestFitIndex];
			}
			else
			{
				return assetBundleName;
			}
		}

		/// <summary>
		/// Sets up download operation for the given asset bundle if it's not downloaded already.
		/// </summary>
		static protected bool LoadAssetBundleInternal(string assetBundleToFind, bool isLoadingAssetBundleIndex = false, bool useJsonIndex = false, string jsonIndexUrl = "")
		{
			//encrypted bundles have the suffix 'encrypted' appended to the name TODO this should probably go in the index though and be settable in the UMAAssetBundleManagerSettings window
			//string encryptedSuffix = BundleEncryptionKey != "" ? "encrypted" : "";
			string assetBundleToGet = assetBundleToFind;
			if(BundleEncryptionKey != "" && m_AssetBundleIndex != null)
			{
				assetBundleToGet = m_AssetBundleIndex.GetAssetBundleEncryptedName(assetBundleToFind);
				Debug.Log("assetBundleToFind was " + assetBundleToFind + " assetBundleToGet was " + assetBundleToGet);
            }
			else if(BundleEncryptionKey != "" && m_AssetBundleIndex == null)
			{
				assetBundleToGet = assetBundleToFind + "encrypted";
			}

			// Already loaded.
			LoadedAssetBundle bundle = null;
			m_LoadedAssetBundles.TryGetValue(assetBundleToFind, out bundle);//encrypted or not this will have the assetbundlename without the 'encrypted' suffix
			if (bundle != null && bundle.m_AssetBundle != null)
			{
				bundle.m_ReferencedCount++;
				return true;
			}

			// @TODO: Do we need to consider the referenced count of WWWs?
			// users can call LoadAssetAsync()/LoadLevelAsync() several times then wait them to be finished which might have duplicate WWWs.
			if (m_DownloadingBundles.Contains(assetBundleToFind))
				return true;

			string bundleBaseDownloadingURL = GetAssetBundleBaseDownloadingURL(assetBundleToFind);

			//TODO These dont support encrypted bundles yet
			if (bundleBaseDownloadingURL.ToLower().StartsWith("odr://"))
			{
#if ENABLE_IOS_ON_DEMAND_RESOURCES
                Log(LogType.Info, "Requesting bundle " + assetBundleName + " through ODR");
                m_InProgressOperations.Add(new AssetBundleDownloadFromODROperation(assetBundleToGet));
#else
				new ApplicationException("Can't load bundle " + assetBundleToFind + " through ODR: this Unity version or build target doesn't support it.");
#endif
			}
			else if (bundleBaseDownloadingURL.ToLower().StartsWith("res://"))
			{
#if ENABLE_IOS_APP_SLICING
                Log(LogType.Info, "Requesting bundle " + assetBundleName + " through asset catalog");
                m_InProgressOperations.Add(new AssetBundleOpenFromAssetCatalogOperation(assetBundleToGet));
#else
				new ApplicationException("Can't load bundle " + assetBundleToFind + " through asset catalog: this Unity version or build target doesn't support it.");
#endif
			}
			else
			{
				WWW download = null;
				if (!bundleBaseDownloadingURL.EndsWith("/"))
					bundleBaseDownloadingURL += "/";

				string url = bundleBaseDownloadingURL + assetBundleToGet;
				// For index assetbundle, always download it as we don't have hash for it.
				//TODO make something to test if there is and internet connection and if not try to get a cached version of this so we can still access the stuff that has been previously cached
				//TODO2 Make the index cache somewhere when it is downloaded.
				if (isLoadingAssetBundleIndex)
				{
					if (useJsonIndex && jsonIndexUrl != "")
					{
						url = jsonIndexUrl.Replace("[PLATFORM]", Utility.GetPlatformName());
					}
					else if (useJsonIndex)
					{
						url = url+ ".json";
					}
					/*else
					{
						url = url + encryptedSuffix;
                    }*/
					download = new WWW(url);
					if (download.error != null || download == null)
					{
						if (download.error != null)
							Log(LogType.Warning, download.error);
						else
							Log(LogType.Warning, " index new WWW(url) was NULL");
					}
				}
				else
				{
					download = WWW.LoadFromCacheOrDownload(url/* + encryptedSuffix*/, m_AssetBundleIndex.GetAssetBundleHash(assetBundleToFind), 0);
				}
				m_InProgressOperations.Add(new AssetBundleDownloadFromWebOperation(assetBundleToFind/* + encryptedSuffix*/, download, useJsonIndex));
			}

			m_DownloadingBundles.Add(assetBundleToFind);

			return false;
		}

		/// <summary>
		/// Where we get all the dependencies from the index for the given asset bundle and load them all.
		/// </summary>
		/// <param name="assetBundleName"></param>
		static protected void LoadDependencies(string assetBundleName)
		{
			if (m_AssetBundleIndex == null)
			{
				Log(LogType.Error, "Please initialize AssetBundleIndex by calling AssetBundleManager.Initialize()");
				return;
			}

			// Get dependecies from the AssetBundleIndex object..
			string[] dependencies = m_AssetBundleIndex.GetAllDependencies(assetBundleName);
			if (dependencies.Length == 0)
				return;

			for (int i = 0; i < dependencies.Length; i++)
				dependencies[i] = RemapVariantName(dependencies[i]);

			// Record and load all dependencies.
			m_Dependencies.Add(assetBundleName, dependencies);
			for (int i = 0; i < dependencies.Length; i++)
				LoadAssetBundleInternal(dependencies[i], false);
		}
		/// <summary>
		/// WARNING Not working right with DynamicAssetLoader yet! Unloads all AssetBundles compressed data (not the assets loaded from the bundle) to free up memory
		/// </summary>
		static public void UnloadAllAssetBundles()
		{
#if UNITY_EDITOR
			// If we're in Editor simulation mode, we don't have to load the manifest assetBundle.
			if (SimulateAssetBundleInEditor)
				return;
#endif
			List<string> bundlesToUnload = new List<string>();
			foreach (KeyValuePair<string, LoadedAssetBundle> kp in m_LoadedAssetBundles)
			{
				if (kp.Key.IndexOf(Utility.GetPlatformName().ToLower() + "index") == -1)//dont try to unload the index...
					bundlesToUnload.Add(kp.Key);
			}
			foreach (string bundleName in bundlesToUnload)
			{
				UnloadAssetBundleInternal(bundleName);
				UnloadDependencies(bundleName);//I think its unloading dependencies thats causing an issue with UMA
			}

		}
		/// <summary>
		/// Unloads assetbundle and its dependencies.
		/// </summary>
		/// <param name="assetBundleName"></param>
		static public void UnloadAssetBundle(string assetBundleName)
		{
#if UNITY_EDITOR
			// If we're in Editor simulation mode, we don't have to load the index assetBundle.
			if (SimulateAssetBundleInEditor)
				return;
#endif
			UnloadAssetBundleInternal(assetBundleName);
			UnloadDependencies(assetBundleName);

		}

		static protected void UnloadDependencies(string assetBundleName)
		{
			string[] dependencies = null;
			if (!m_Dependencies.TryGetValue(assetBundleName, out dependencies))
				return;

			// Loop dependencies.
			foreach (var dependency in dependencies)
			{
				UnloadAssetBundleInternal(dependency);
			}

			m_Dependencies.Remove(assetBundleName);
		}

		static protected void UnloadAssetBundleInternal(string assetBundleName, bool disregardRefrencedStatus = false)
		{
			string error;
			LoadedAssetBundle bundle = GetLoadedAssetBundle(assetBundleName, out error);
			if (bundle == null)
				return;

			if (--bundle.m_ReferencedCount == 0 || disregardRefrencedStatus)
			{
				bundle.OnUnload();
				m_LoadedAssetBundles.Remove(assetBundleName);

				Log(LogType.Info, assetBundleName + " has been unloaded successfully");
			}
		}

		void Update()
		{
			// Update all in progress operations
			for (int i = 0; i < m_InProgressOperations.Count;)
			{
				var operation = m_InProgressOperations[i];
				if (operation.Update())
				{
					i++;
				}
				else
				{
					m_InProgressOperations.RemoveAt(i);
					ProcessFinishedOperation(operation);
				}
			}
		}

		void ProcessFinishedOperation(AssetBundleLoadOperation operation)
		{
			AssetBundleDownloadOperation download = operation as AssetBundleDownloadOperation;
			if (download == null)
				return;

			if (download.error == null)
				m_LoadedAssetBundles.Add(download.assetBundleName, download.assetBundle);
			else
			{
				string msg = string.Format("Failed downloading bundle {0} from {1}: {2}",
						download.assetBundleName, download.GetSourceURL(), download.error);
				m_DownloadingErrors.Add(download.assetBundleName, msg);
			}
			m_DownloadingBundles.Remove(download.assetBundleName);
		}

		/// <summary>
		/// Starts a load operation for an asset from the given asset bundle.
		/// </summary>
		static public AssetBundleLoadAssetOperation LoadAssetAsync(string assetBundleName, string assetName, System.Type type)
		{
			Log(LogType.Info, "Loading " + assetName + " from " + assetBundleName + " bundle");

			AssetBundleLoadAssetOperation operation = null;
#if UNITY_EDITOR
			if (SimulateAssetBundleInEditor)
			{
				string[] assetPaths = AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(assetBundleName, assetName);
				if (assetPaths.Length == 0)
				{
					Debug.LogError("There is no asset with name \"" + assetName + "\" in " + assetBundleName);
					return null;
				}

				// @TODO: Now we only get the main object from the first asset. Should consider type also.
				UnityEngine.Object target = AssetDatabase.LoadMainAssetAtPath(assetPaths[0]);
				operation = new AssetBundleLoadAssetOperationSimulation(target);
			}
			else
#endif
			{
				assetBundleName = RemapVariantName(assetBundleName);
				LoadAssetBundle(assetBundleName, false);
				operation = new AssetBundleLoadAssetOperationFull(assetBundleName, assetName, type);

				m_InProgressOperations.Add(operation);
			}

			return operation;
		}

		/// <summary>
		/// Starts a load operation for a level from the given asset bundle.
		/// </summary>
		static public AssetBundleLoadOperation LoadLevelAsync(string assetBundleName, string levelName, bool isAdditive)
		{
			Log(LogType.Info, "Loading " + levelName + " from " + assetBundleName + " bundle");

			AssetBundleLoadOperation operation = null;
#if UNITY_EDITOR
			if (SimulateAssetBundleInEditor)
			{
				operation = new AssetBundleLoadLevelSimulationOperation(assetBundleName, levelName, isAdditive);
			}
			else
#endif
			{
				assetBundleName = RemapVariantName(assetBundleName);
				LoadAssetBundle(assetBundleName, false);
				operation = new AssetBundleLoadLevelOperation(assetBundleName, levelName, isAdditive);

				m_InProgressOperations.Add(operation);
			}

			return operation;
		}

	} // End of AssetBundleManager.
}
