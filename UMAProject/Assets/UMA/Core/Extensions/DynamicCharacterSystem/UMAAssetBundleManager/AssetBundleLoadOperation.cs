using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_5_4_OR_NEWER
using UnityEngine.Networking;
#endif
#if ENABLE_IOS_ON_DEMAND_RESOURCES
using UnityEngine.iOS;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;

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
		WWW m_WWW;
		string m_Url;
		int zeroDownload = 0;
		bool m_isJsonIndex = false;

		public AssetBundleDownloadFromWebOperation(string assetBundleName, WWW www, bool isJsonIndex = false)
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
            
			// TODO: When can check iOS copy this into the iOS functions above
			// This checks that the download is actually happening and restarts it if it is not
			//fixes a bug in SimpleWebServer where it would randomly stop working for some reason
			if (!downloadIsDone)
			{
				downloadProgress = m_WWW.progress;
				if (!string.IsNullOrEmpty(m_WWW.error))
				{
					Debug.Log("[AssetBundleLoadOperation] download error for "+ m_WWW.url+" : " + m_WWW.error);
				}
				else
				{
					if (m_WWW.progress == 0)
					{
						zeroDownload++;
					}
#if UNITY_EDITOR
					//Sometimes SimpleWebServer randomly looses it port connection
					//Sometimes restarting the download helps, sometimes it needs to be completely restarted
					if (SimpleWebServer.Instance != null)
					{
						if (zeroDownload == 150)
						{
							Debug.Log("[AssetBundleLoadOperation] progress was zero for 150 frames restarting dowload");
							m_WWW.Dispose();//sometimes makes a difference when the download fails
							m_WWW = null;
							m_WWW = new WWW(m_Url);
						}

						if (zeroDownload == 300)//If we are in the editor we can restart the Server and this will make it work
						{
							Debug.LogWarning("[AssetBundleLoadOperation] progress was zero for 300 frames restarting the server");
							//we wont be able to do the following from a build
							int port = SimpleWebServer.Instance.Port;
							SimpleWebServer.Start(port);
							m_WWW.Dispose();
							m_WWW = null;
							m_WWW = new WWW(m_Url);
							zeroDownload = 0;
						}
					}
					else
#endif
					if(zeroDownload == 500)
					{
						Debug.Log("[AssetBundleLoadOperation] progress was zero for 500 frames restarting dowload");
						m_WWW.Dispose();
						m_WWW = null;
						m_WWW = new WWW(m_Url);
						zeroDownload = 0;
					}

				}
				return true;
			}
			else
			{
				downloadProgress = 1f;
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
			error = m_WWW.error;
			if (!string.IsNullOrEmpty(error))
			{
				Debug.LogWarning("[AssetBundleLoadOperation.AssetBundleDownloadFromWebOperation] URL was "+ m_WWW.url+" error was " + error);
				return;
			}

			if (!m_isJsonIndex)
			{
				bundle = m_WWW.assetBundle;
				if (bundle == null)
				{
					Debug.LogWarning("[AssetBundleLoadOperation.AssetBundleDownloadFromWebOperation] "+assetBundleName+" was not a valid assetBundle");
					m_WWW.Dispose();
					m_WWW = null;
				}
				else if (!WasBundleEncrypted())
				{
					assetBundle = new LoadedAssetBundle(m_WWW.assetBundle);
					m_WWW.Dispose();
					m_WWW = null;
				}
			}
			else
			{
				string indexData = m_WWW.text;
				if (indexData == "")
				{
					Debug.LogWarning("[AssetBundleLoadOperation.AssetBundleDownloadFromWebOperation] The JSON AssetBundleIndex was empty");
				}
				else
				{
					assetBundle = new LoadedAssetBundle(m_WWW.text);
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
				Debug.LogWarning("[AssetBundleLoadOperation.AssetBundleLoadDecrypted] could not create bundle from decrypted bytes for " + assetBundleName);
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

				Debug.LogError("There is no scene with name \"" + levelName + "\" in " + assetBundleName);
				return;
			}

			if (isAdditive)
				m_Operation = EditorApplication.LoadLevelAdditiveAsyncInPlayMode(levelPaths[0]);
			else
				m_Operation = EditorApplication.LoadLevelAsyncInPlayMode(levelPaths[0]);
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
				///@TODO: When asset bundle download fails this throws an exception...
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
		bool _isJsonIndex;
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
                return false;
            }
			if (AssetBundleManager.AssetBundleIndexObject != null)
			{
				return false;
			}
			else
				return true;
		}
	}
}
