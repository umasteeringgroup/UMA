using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
#if ENABLE_IOS_ON_DEMAND_RESOURCES
using UnityEngine.iOS;
#endif
#if UNITY_EDITOR
using UnityEditor;
#if UNITY_2018_3_OR_NEWER
using UnityEditor.SceneManagement;
#endif
#endif
using System.Collections;
//added System.IO for loading/saving cached bundleIndexes
using System.IO;

namespace UMA.AssetBundles
{
	public abstract class AssetBundleLoadOperation : IEnumerator
	{
		public object Current
		{
			get
			{
				return null;
			}
		}
		public bool MoveNext()
		{
			return !IsDone();
		}

		public void Reset()
		{
		}

		abstract public bool Update();

		abstract public bool IsDone();
	}

	public abstract class AssetBundleDownloadOperation : AssetBundleLoadOperation
	{
		bool done;

		public string assetBundleName { get; private set; }
		public float downloadProgress { get; protected set; }
		public LoadedAssetBundle assetBundle { get; protected set; }
		public string error { get; protected set; }

		protected AssetBundle bundle;
		protected AssetBundleLoadDecrypted decryptedLoadOperation = null;

		protected abstract bool downloadIsDone { get; }
		protected abstract void FinishDownload();

		public override bool Update()
		{
			if (!done && downloadIsDone)
			{
				FinishDownload();
				done = true;
			}

			return !done;
		}

		public override bool IsDone()
		{
			return done;
		}

		protected virtual bool WasBundleEncrypted()
		{
			if (AssetBundleManager.BundleEncryptionKey != "")
			{
				var encryptedAsset = bundle.LoadAsset<UMAEncryptedBundle>("EncryptedData");
				if (encryptedAsset)
				{
					byte[] decryptedData = new byte[0];
					try {
						decryptedData = EncryptionUtil.Decrypt(encryptedAsset.data, AssetBundleManager.BundleEncryptionKey, encryptedAsset.IV);
					}
					catch(System.Exception e)
					{
						if (Debug.isDebugBuild)
							Debug.LogError("[AssetBundleLoadOperation] could not decrypt " + assetBundleName+ "Error message was "+e.Message+" : "+e.StackTrace);
						return false;
					}
					bundle.Unload (true);
					decryptedLoadOperation = new AssetBundleLoadDecrypted(decryptedData, assetBundleName);
					return true;
				}
			}
			return false;
		}

		public abstract string GetSourceURL();

		public AssetBundleDownloadOperation(string assetBundleName)
		{
			this.assetBundleName = assetBundleName;
		}
	}

#if ENABLE_IOS_ON_DEMAND_RESOURCES
	/// <summary>
	/// Read asset bundle asynchronously from iOS / tvOS asset catalog that is downloaded
	// using on demand resources functionality.
	/// </summary>
	public class AssetBundleDownloadFromODROperation : AssetBundleDownloadOperation
    {
        OnDemandResourcesRequest request;
		public AssetBundleDownloadFromODROperation(string assetBundleName)
        : base(assetBundleName)
        {
			// Work around Xcode crash when opening Resources tab when a 
			// resource name contains slash character
			request = OnDemandResources.PreloadAsync(new string[] { assetBundleName.Replace('/', '>') });
		}

		public override bool Update()
		{
			if (decryptedLoadOperation != null)
			{
				decryptedLoadOperation.Update();
				if (decryptedLoadOperation.IsDone())
				{
					assetBundle = decryptedLoadOperation.assetBundle;
					downloadProgress = 1f;
					return false;
				}
				else //keep updating
				{
					downloadProgress = 0.9f + (decryptedLoadOperation.progress / 100);
					return true;
				}
			}
			else
			{
				return base.Update();
			}
		}
		protected override bool downloadIsDone
		{
			get
			{
				if (decryptedLoadOperation != null)
				{
					return decryptedLoadOperation.assetBundle == null ? false : true;
				}
				return (request == null) || request.isDone;
			}
		}

        public override string GetSourceURL()
        {
            return "odr://" + assetBundleName;
        }

        protected override void FinishDownload()
        {
            error = request.error;
            if (!string.IsNullOrEmpty(error))
                return;

            var path = "res://" + assetBundleName;
            bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                error = string.Format("Failed to load {0}", path);
                request.Dispose();
            }
            else
            {
				if (!WasBundleEncrypted())
				{
					assetBundle = new LoadedAssetBundle(bundle);
					// At the time of unload request is already set to null, so capture it to local variable.
					var localRequest = request;
					// Dispose of request only when bundle is unloaded to keep the ODR pin alive.
					assetBundle.unload += () =>
					{
						localRequest.Dispose();
					};
				}
            }

            request = null;
        }
    }
#endif

#if ENABLE_IOS_APP_SLICING
	/// <summary>
	/// Read asset bundle synchronously from an iOS / tvOS asset catalog
	/// </summary>
	public class AssetBundleOpenFromAssetCatalogOperation : AssetBundleDownloadOperation
    {
        public AssetBundleOpenFromAssetCatalogOperation(string assetBundleName)
        : base(assetBundleName)
        {
            var path = "res://" + assetBundleName;
            bundle = AssetBundle.LoadFromFile(path);
			if (bundle == null)
                error = string.Format("Failed to load {0}", path);
            else
				if(!WasBundleEncrypted())
                assetBundle = new LoadedAssetBundle(bundle);
        }
		public override bool Update()
		{
			if (decryptedLoadOperation != null)
			{
				decryptedLoadOperation.Update();
				if (decryptedLoadOperation.IsDone())
				{
					assetBundle = decryptedLoadOperation.assetBundle;
					downloadProgress = 1f;
					return false;
				}
				else //keep updating
				{
					downloadProgress = 0.9f + (decryptedLoadOperation.progress / 100);
					return true;
				}
			}
			else
			{
				downloadProgress = 1f;
				return base.Update();
			}
		}
        protected override bool downloadIsDone
		{
			get
			{
				if (decryptedLoadOperation != null)
				{
					return decryptedLoadOperation.assetBundle == null ? false : true;
				}
				return true;
			}
		}
		protected override void FinishDownload() { }

        public override string GetSourceURL()
        {
            return "res://" + assetBundleName;
        }
    }
#endif

	public class AssetBundleDownloadFromWebOperation : AssetBundleDownloadOperation
	{
		UnityWebRequest m_WWW;
		string m_Url;
		int zeroDownload = 0;
		bool m_isJsonIndex = false;
		int retryAttempts = 0;
		int maxRetryAttempts = 5;

		public AssetBundleDownloadFromWebOperation(string assetBundleName, UnityWebRequest www, bool isJsonIndex = false)
			: base(assetBundleName)
		{
			if (www == null)
				throw new System.ArgumentNullException("www");
			m_Url = www.url;
			m_isJsonIndex = isJsonIndex;
			m_WWW = www;
		}

		public override bool Update()
		{
			if (decryptedLoadOperation != null)
			{
				decryptedLoadOperation.Update();
				if (decryptedLoadOperation.IsDone())
				{
					assetBundle = decryptedLoadOperation.assetBundle;
					downloadProgress = 1f;
					m_WWW.Dispose();
					m_WWW = null;
					return false;
				}
				else //keep updating
				{
					downloadProgress = 0.9f + (decryptedLoadOperation.progress / 100);
					return true;
				}
			}
			else
			{
			base.Update();
			}
            
			// TODO: iOS AppSlicing and OnDemandResources will need something like this too
			// This checks that the download is actually happening and restarts it if it is not
			//fixes a bug in SimpleWebServer where it would randomly stop working for some reason
			if (!downloadIsDone)
			{
				//We actually need to know if progress has stalled, not just if there is none
				//so set the progress after we compare
				//downloadProgress = m_WWW.progress;
				if (!string.IsNullOrEmpty(m_WWW.error))
				{
					if (Debug.isDebugBuild)
						Debug.Log("[AssetBundleLoadOperation] download error for "+ m_WWW.url+" : " + m_WWW.error);
				}
				else
				{
					if(m_WWW.downloadProgress == downloadProgress)
					{
						zeroDownload++;
					}
					else
					{
						downloadProgress = m_WWW.downloadProgress;
						zeroDownload = 0;
                    }
#if UNITY_EDITOR
					//Sometimes SimpleWebServer randomly looses it port connection
					//Sometimes restarting the download helps, sometimes it needs to be completely restarted
					if (SimpleWebServer.Instance != null)
					{
						if (zeroDownload == 150)
						{
							if (Debug.isDebugBuild)
								Debug.Log("[AssetBundleLoadOperation] progress was zero for 150 frames restarting dowload");
							m_WWW.Dispose();//sometimes makes a difference when the download fails
							m_WWW = null;
#if UNITY_2018_1_OR_NEWER
							m_WWW = UnityWebRequestAssetBundle.GetAssetBundle(m_Url);
#else
							m_WWW = UnityWebRequest.GetAssetBundle(m_Url);
#endif
#if UNITY_2017_2_OR_NEWER
							m_WWW.SendWebRequest();
#else
							m_WWW.Send();
#endif
						}

						if (zeroDownload == 300)//If we are in the editor we can restart the Server and this will make it work
						{
							if (Debug.isDebugBuild)
								Debug.LogWarning("[AssetBundleLoadOperation] progress was zero for 300 frames restarting the server");
							//we wont be able to do the following from a build
							int port = SimpleWebServer.Instance.Port;
							SimpleWebServer.Start(port);
							m_WWW.Dispose();
							m_WWW = null;
#if UNITY_2018_1_OR_NEWER
							m_WWW = UnityWebRequestAssetBundle.GetAssetBundle(m_Url);
#else
							m_WWW = UnityWebRequest.GetAssetBundle(m_Url);
#endif
#if UNITY_2017_2_OR_NEWER
							m_WWW.SendWebRequest();
#else
							m_WWW.Send();
#endif
							zeroDownload = 0;
						}
					}
					else
#endif
							if((downloadProgress == 0 && zeroDownload == 500) || zeroDownload >= Mathf.Clamp((2500 * downloadProgress), 500,2500))//let this number get larger the more has been downloaded (cos its really annoying to be at 98% and have it fail)
					{
						//when we cannot download because WiFi is connected but a hotspot needs authentication
						//or because the user has run out of mobile data the www class takes a while error out
						if ((AssetBundleManager.ConnectionChecker != null && !AssetBundleManager.ConnectionChecker.InternetAvailable) || retryAttempts > maxRetryAttempts)
						{
							if (retryAttempts > maxRetryAttempts)
							{
								//there was some unknown error with the connection
								//tell the user we could not complete the download
								error = "Downloading of " + assetBundleName + " failed after 10 attempts. Something is wrong with the internet connection.";
								m_WWW.Dispose();
								m_WWW = null;
							}
							else
							{
                                //if we have a connection checker and no connection, leave the www alone so it times out on its own
								if (Debug.isDebugBuild)
									Debug.Log("[AssetBundleLoadOperation] progress was zero for "+ zeroDownload+" frames and the ConnectionChecker said there was no Internet Available.");
							}
						}
						else
						{
							if (Debug.isDebugBuild)
								Debug.Log("[AssetBundleLoadOperation] progress was zero for " + zeroDownload + " frames restarting dowload");
							m_WWW.Dispose();
							m_WWW = null;
							//m_WWW = new WWW(m_Url);//make sure this still caches
							if (AssetBundleManager.AssetBundleIndexObject != null)
#if UNITY_2018_1_OR_NEWER
								m_WWW = UnityWebRequestAssetBundle.GetAssetBundle(m_Url, AssetBundleManager.AssetBundleIndexObject.GetAssetBundleHash(assetBundleName), 0);
#else
								m_WWW = UnityWebRequest.GetAssetBundle(m_Url, AssetBundleManager.AssetBundleIndexObject.GetAssetBundleHash(assetBundleName), 0);
#endif
							else
#if UNITY_2018_1_OR_NEWER
								m_WWW = UnityWebRequestAssetBundle.GetAssetBundle(m_Url);
#else
								m_WWW = UnityWebRequest.GetAssetBundle(m_Url);
#endif
							//but increment the retry either way so the failed Ui shows sooner
							retryAttempts++;
						}
						zeroDownload = 0;
					}

				}
				return true;
			}
			else
			{
				if (string.IsNullOrEmpty(error))
				  downloadProgress = 1f;
				else
					downloadProgress = 0;
				return false;
			}
		}

		protected override bool downloadIsDone
		{
			get
			{
				if(decryptedLoadOperation != null)
				{
					return decryptedLoadOperation.assetBundle == null ? false : true;
				}
				return (m_WWW == null) || m_WWW.isDone;
			}
		}

		protected override void FinishDownload()
		{
			if(m_WWW != null)
			   error = m_WWW.error;
			if (!string.IsNullOrEmpty(error))
			{
				if (Debug.isDebugBuild)
					Debug.LogWarning("[AssetBundleLoadOperation.AssetBundleDownloadFromWebOperation] URL was "+ m_Url + " error was " + error);
				return;
			}

			if (!m_isJsonIndex)
			{
				bundle = DownloadHandlerAssetBundle.GetContent(m_WWW);
				if (bundle == null)
				{
					if (Debug.isDebugBuild)
						Debug.LogWarning("[AssetBundleLoadOperation.AssetBundleDownloadFromWebOperation] "+assetBundleName+" was not a valid assetBundle");
					m_WWW.Dispose();
					m_WWW = null;
				}
				else if (!WasBundleEncrypted())
				{
					assetBundle = new LoadedAssetBundle(bundle);
					m_WWW.Dispose();
					m_WWW = null;
				}
			}
			else
			{
				string indexData = m_WWW.downloadHandler.text;
				if (indexData == "")
				{
					if (Debug.isDebugBuild)
						Debug.LogWarning("[AssetBundleLoadOperation.AssetBundleDownloadFromWebOperation] The JSON AssetBundleIndex was empty");
				}
				else
				{
					assetBundle = new LoadedAssetBundle(m_WWW.downloadHandler.text);
				}
				m_WWW.Dispose();
				m_WWW = null;
			}	
		}

		public override string GetSourceURL()
		{
			return m_Url;
		}
	}

	/// <summary>
	/// Loads the bytes of a decrypted asset bundle from memory asynchroniously;
	/// </summary>
	public class AssetBundleLoadDecrypted : AssetBundleDownloadOperation
	{
		AssetBundleCreateRequest m_Operation = null;
		public float progress;

		public AssetBundleLoadDecrypted(byte[] decryptedData, string assetBundleName) : base(assetBundleName)
		{
           m_Operation = AssetBundle.LoadFromMemoryAsync(decryptedData);
		}
		protected override bool downloadIsDone
		{
			get { return true; }
		}
		//update needs to return true if more updates are required
		public override bool Update()
		{
			progress = m_Operation.progress;
			if (progress == 1)
			{
				FinishDownload();
				return false;
			}
			progress = m_Operation.progress;
			return true;
		}

		protected override void FinishDownload()
		{
			bundle = m_Operation.assetBundle;
			if (bundle == null)
			{
				if (Debug.isDebugBuild)
					Debug.LogWarning("[AssetBundleLoadOperation.AssetBundleLoadDecrypted] could not create bundle from decrypted bytes for " + assetBundleName);
			}
			else
			{
				//Debug.Log("[AssetBundleLoadOperation.AssetBundleLoadEncrypted] " + assetBundleName+" loaded from decrypted bytes successfully");
				assetBundle = new LoadedAssetBundle(bundle);
			}
			m_Operation = null;
		}

		public override bool IsDone()
		{
			return m_Operation == null || m_Operation.isDone;
		}
		//just inherited junk
		public override string GetSourceURL()
		{
			return "";
		}
	}

#if UNITY_EDITOR
	public class AssetBundleLoadLevelSimulationOperation : AssetBundleLoadOperation
	{
		AsyncOperation m_Operation = null;

		public AssetBundleLoadLevelSimulationOperation(string assetBundleName, string levelName, bool isAdditive)
		{
			string[] levelPaths = UnityEditor.AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(assetBundleName, levelName);
			if (levelPaths.Length == 0)
			{
				///@TODO: The error needs to differentiate that an asset bundle name doesn't exist
				//        from that the right scene does not exist in the asset bundle...
				if (Debug.isDebugBuild)
					Debug.LogError("There is no scene with name \"" + levelName + "\" in " + assetBundleName);
				return;
			}

			if (isAdditive)
			{
#if UNITY_2018_3_OR_NEWER
				m_Operation = EditorSceneManager.LoadSceneAsyncInPlayMode(levelPaths[0], new LoadSceneParameters(LoadSceneMode.Additive));
#else
				m_Operation = EditorApplication.LoadLevelAdditiveAsyncInPlayMode(levelPaths[0]);
#endif
			}
			else
			{
#if UNITY_2018_3_OR_NEWER
				m_Operation = EditorSceneManager.LoadSceneAsyncInPlayMode(levelPaths[0], new LoadSceneParameters(LoadSceneMode.Single));
#else
				m_Operation = EditorApplication.LoadLevelAsyncInPlayMode(levelPaths[0]);
#endif
			}
		}

		public override bool Update()
		{
			return false;
		}

		public override bool IsDone()
		{
			return m_Operation == null || m_Operation.isDone;
		}
	}

#endif
	public class AssetBundleLoadLevelOperation : AssetBundleLoadOperation
	{
		protected string m_AssetBundleName;
		protected string m_LevelName;
		protected bool m_IsAdditive;
		protected string m_DownloadingError;
		protected AsyncOperation m_Request;

		public AssetBundleLoadLevelOperation(string assetbundleName, string levelName, bool isAdditive)
		{
			m_AssetBundleName = assetbundleName;
			m_LevelName = levelName;
			m_IsAdditive = isAdditive;
		}

		public float Progress
		{
			get
			{
				float progress = 0;
				if (m_Request != null)
				{
					if (m_Request.isDone)
					{
						progress = 1f;
					}
					else
					{
						//asyncOperation progress shows 99% when the scene is loaded but not activated which is not very helpful since this is what takes the time!
						progress = Mathf.Clamp01(m_Request.progress / 0.9f);
					}
				}
				return progress;
			}
		}

		public override bool Update()
		{
			if (m_Request != null)
				return false;

			LoadedAssetBundle loadedBundle = AssetBundleManager.GetLoadedAssetBundle(m_AssetBundleName, out m_DownloadingError);
			if (loadedBundle != null)
			{
				m_Request = SceneManager.LoadSceneAsync(m_LevelName, m_IsAdditive ? LoadSceneMode.Additive : LoadSceneMode.Single);
				return false;
			}
			else
				return true;
		}

		public override bool IsDone()
		{
			// Return if meeting downloading error.
			// m_DownloadingError might come from the dependency downloading.
			if (m_Request == null && !string.IsNullOrEmpty(m_DownloadingError))
			{
				if (Debug.isDebugBuild)
					Debug.LogError(m_DownloadingError);
				return true;
			}

			return m_Request != null && m_Request.isDone;
		}
	}

	public abstract class AssetBundleLoadAssetOperation : AssetBundleLoadOperation
	{
		public abstract T GetAsset<T>() where T : UnityEngine.Object;
	}

	public class AssetBundleLoadAssetOperationSimulation : AssetBundleLoadAssetOperation
	{
		Object m_SimulatedObject;

		public AssetBundleLoadAssetOperationSimulation(Object simulatedObject)
		{
			m_SimulatedObject = simulatedObject;
		}

		public override T GetAsset<T>()
		{
			return m_SimulatedObject as T;
		}

		public override bool Update()
		{
			return false;
		}

		public override bool IsDone()
		{
			return true;
		}
	}

	public class AssetBundleLoadAssetOperationFull : AssetBundleLoadAssetOperation
	{
		protected string m_AssetBundleName;
		protected string m_AssetName;
		protected string m_DownloadingError;
		protected System.Type m_Type;
		protected AssetBundleRequest m_Request = null;

		public AssetBundleLoadAssetOperationFull(string bundleName, string assetName, System.Type type)
		{
			m_AssetBundleName = bundleName;
			m_AssetName = assetName;
			m_Type = type;
		}

		public override T GetAsset<T>()
		{
			if (m_Request != null && m_Request.isDone)
				return m_Request.asset as T;
			else
				return null;
		}

		// Returns true if more Update calls are required.
		public override bool Update()
		{
			if (m_Request != null)
				return false;

			LoadedAssetBundle loadedBundle = AssetBundleManager.GetLoadedAssetBundle(m_AssetBundleName, out m_DownloadingError);
			if (loadedBundle != null)
			{
				/// When asset bundle download fails this throws an exception but this should be caught above now
				m_Request = loadedBundle.m_AssetBundle.LoadAssetAsync(m_AssetName, m_Type);
				return false;
			}
			else
			{
				return true;
			}
		}

		public override bool IsDone()
		{
			// Return if meeting downloading error.
			// m_DownloadingError might come from the dependency downloading.
			if (m_Request == null && !string.IsNullOrEmpty(m_DownloadingError))
			{
				if (Debug.isDebugBuild)
					Debug.LogError(m_DownloadingError);
				return true;
			}

			return m_Request != null && m_Request.isDone;
		}
	}

	/// <summary>
	/// Operation for loading the AssetBundleIndex
	/// </summary>
	public class AssetBundleLoadIndexOperation : AssetBundleLoadAssetOperationFull
	{
		//made protected so descendent class can inherit
		protected bool _isJsonIndex;
		public AssetBundleLoadIndexOperation(string bundleName, string assetName, System.Type type, bool isJsonIndex = false)
			: base(bundleName, assetName, type)
		{
			_isJsonIndex = isJsonIndex;
		}
		// Returns true if more Update calls are required.
		public override bool Update()
		{
			//base.Update();
			if (m_Request == null)
			{
				LoadedAssetBundle loadedBundle = AssetBundleManager.GetLoadedAssetBundle(m_AssetBundleName, out m_DownloadingError);
				if (loadedBundle != null)
				{
					if (_isJsonIndex)
					{
						AssetBundleManager.AssetBundleIndexObject = ScriptableObject.CreateInstance<AssetBundleIndex>();
						JsonUtility.FromJsonOverwrite(loadedBundle.m_data, AssetBundleManager.AssetBundleIndexObject);
					}
					else
					{
						if (loadedBundle.m_AssetBundle == null)
						{
							if (Debug.isDebugBuild)
								Debug.LogWarning("AssetBundle was null for " + m_AssetBundleName);
							return false;
						}
						m_Request = loadedBundle.m_AssetBundle.LoadAssetAsync<AssetBundleIndex>(m_AssetName);
					}
				}
			}
			if (m_Request != null && m_Request.isDone)
            {
                AssetBundleManager.AssetBundleIndexObject = GetAsset<AssetBundleIndex>();
				//If there is an AssetBundleManager.m_ConnectionChecker and it is set to m_ConnectionChecker.UseBundleIndexCaching
				//cache this so we can use it offline if we need to
				if(AssetBundleManager.ConnectionChecker != null && AssetBundleManager.ConnectionChecker.UseBundleIndexCaching == true)
					if (AssetBundleManager.AssetBundleIndexObject != null)
					{
						if (Debug.isDebugBuild)
							Debug.Log("Caching downloaded index with Build version " + AssetBundleManager.AssetBundleIndexObject.bundlesPlayerVersion);
						var cachedIndexPath = Path.Combine(Application.persistentDataPath, "cachedBundleIndex");
						if (!Directory.Exists(cachedIndexPath))
							Directory.CreateDirectory(cachedIndexPath);
						cachedIndexPath = Path.Combine(cachedIndexPath, m_AssetBundleName + ".json");
						File.WriteAllText(cachedIndexPath, JsonUtility.ToJson(AssetBundleManager.AssetBundleIndexObject));
					}
                return false;
            }
			if (AssetBundleManager.AssetBundleIndexObject != null)
			{
				return false;
			}
			else
			{
				// if there has been an error make this return false
				//It wont need any more updates and ABM needs to put it in failed downloads
				if(string.IsNullOrEmpty(m_DownloadingError))
					return true;
				else
				{
					return false;
				}
			}
		}
	}

	public class AssetBundleLoadCachedIndexOperation : AssetBundleLoadIndexOperation
	{
		public AssetBundleLoadCachedIndexOperation(string bundleName, string assetName, System.Type type, bool isJsonIndex = false)
			: base(bundleName, assetName, type, isJsonIndex)
		{
			var cachedIndexPath = Path.Combine(Application.persistentDataPath, "cachedBundleIndex");
			if (!Directory.Exists(cachedIndexPath))
				Directory.CreateDirectory(cachedIndexPath);
			cachedIndexPath = Path.Combine(cachedIndexPath, bundleName+".json");
			if (File.Exists(cachedIndexPath))
			{
				if (Debug.isDebugBuild)
					Debug.Log("Cached index found for " + cachedIndexPath);
				AssetBundleManager.AssetBundleIndexObject = ScriptableObject.CreateInstance<AssetBundleIndex>();
				JsonUtility.FromJsonOverwrite(File.ReadAllText(cachedIndexPath), AssetBundleManager.AssetBundleIndexObject);
				//AssetBundleManager.AssetBundleIndexObject = JsonUtility.FromJson<AssetBundleIndex>(File.ReadAllText(cachedIndexPath));
			}
			else
			{
				//we need an error saying no cached index existed
				m_DownloadingError = "No cached Index existed for bundleName";
            }
		}

		public override bool Update()
		{
			return false;
		}
		public override bool IsDone()
		{
				return true;
		}
	}
}
