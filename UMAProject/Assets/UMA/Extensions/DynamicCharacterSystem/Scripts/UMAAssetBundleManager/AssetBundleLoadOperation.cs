using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_IOS_ON_DEMAND_RESOURCES
using UnityEngine.iOS;
#endif
using System.Collections;

namespace UMAAssetBundleManager
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

		public abstract string GetSourceURL();

		public AssetBundleDownloadOperation(string assetBundleName)
		{
			this.assetBundleName = assetBundleName;
		}
	}

#if ENABLE_IOS_ON_DEMAND_RESOURCES
    // Read asset bundle asynchronously from iOS / tvOS asset catalog that is downloaded
    // using on demand resources functionality.
    //TODO Make this set its progress in Update aswell
    public class AssetBundleDownloadFromODROperation : AssetBundleDownloadOperation
    {
        OnDemandResourcesRequest request;

        public AssetBundleDownloadFromODROperation(string assetBundleName)
        : base(assetBundleName)
        {
            request = OnDemandResources.PreloadAsync(new string[] { assetBundleName });
        }

        protected override bool downloadIsDone { get { return (request == null) || request.isDone; } }

        public override string GetSourceURL()
        {
            return "odr://" + assetBundleName;
        }

        protected override void FinishDownload()
        {
            error = request.error;
            if (error != null)
                return;

            var path = "res://" + assetBundleName;
            var bundle = AssetBundle.CreateFromFile(path);
            if (bundle == null)
            {
                error = string.Format("Failed to load {0}", path);
                request.Dispose();
            }
            else
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

            request = null;
        }
    }
#endif

#if ENABLE_IOS_APP_SLICING
    // Read asset bundle synchronously from an iOS / tvOS asset catalog
    //TODO Make this set its progress in Update aswell
    public class AssetBundleOpenFromAssetCatalogOperation : AssetBundleDownloadOperation
    {
        public AssetBundleOpenFromAssetCatalogOperation(string assetBundleName)
        : base(assetBundleName)
        {
            var path = "res://" + assetBundleName;
            var bundle = AssetBundle.CreateFromFile(path);
            if (bundle == null)
                error = string.Format("Failed to load {0}", path);
            else
                assetBundle = new LoadedAssetBundle(bundle);
        }

        protected override bool downloadIsDone { get { return true; } }

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
			this.m_WWW = www;
		}

		public override bool Update()
		{
			base.Update();
			// TODO: When can check iOS copy this into the iOS functions above
			if (!downloadIsDone)
			{
				downloadProgress = m_WWW.progress;
				if (!string.IsNullOrEmpty(m_WWW.error))
				{
					Debug.Log("[AssetBundleLoadOperation] download error: " + m_WWW.error);
				}
				else
				{
					if (m_WWW.progress == 0)
					{
						zeroDownload++;
					}
					if (zeroDownload == 150)
					{
						Debug.Log("[AssetBundleLoadOperation] progress was zero for 150 frames restarting dowload AssetBundleManager.SimulateOverride was " + AssetBundleManager.SimulateOverride + " SimpleWebServer.serverStarted was " + SimpleWebServer.serverStarted);
						m_WWW.Dispose();//sometimes makes a difference when the download fails
						m_WWW = null;
						m_WWW = new WWW(m_Url);
#if !UNITY_EDITOR
                        zeroDownload = 0;
#endif
					}
#if UNITY_EDITOR
					if (zeroDownload == 300)//If we are in the editor we can restart the Server and this will make it work
					{
						Debug.LogWarning("[AssetBundleLoadOperation] progress was zero for 300 frames restarting the server");
						//we wont be able to do the following from a build
						if (SimpleWebServer.Instance == null)
						{
							Debug.Log("[AssetBundleLoadOperation] SimpleWebServer was Null!!");
						}
						else
						{
							int port = SimpleWebServer.Instance.Port;
							SimpleWebServer.Start(port);
							m_WWW.Dispose();
							m_WWW = null;
							m_WWW = new WWW(m_Url);
						}
						zeroDownload = 0;
					}
#endif
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
				bool ret = false;
				try
				{
					ret = (m_WWW == null) || m_WWW.isDone;
				}
				catch
				{
					Debug.LogError("[AssetBundleLoadOperation] ERROR: m_WWW was not null or isDone");
				}
				return ret;
			}
		}

		protected override void FinishDownload()
		{
			error = m_WWW.error;
			if (!string.IsNullOrEmpty(error))
			{
				if (error != null)
					Debug.LogWarning("[AssetBundleLoadOperation.AssetBundleDownloadFromWebOperation] " + error);
				return;
			}

			if (!m_isJsonIndex)
			{
				AssetBundle bundle = m_WWW.assetBundle;
				if (bundle == null)
				{
					error = string.Format("{0} is not a valid asset bundle.", assetBundleName);
					Debug.LogWarning(error);
				}
				else
				{
					assetBundle = new LoadedAssetBundle(m_WWW.assetBundle);
				}
			}
			else
			{
				string indexData = m_WWW.text;
				if (indexData == "")
				{
					Debug.LogWarning("The JSON AssetBundleIndex was empty");
				}
				else
				{
					assetBundle = new LoadedAssetBundle(m_WWW.text);
				}
			}
			m_WWW.Dispose();
			m_WWW = null;
		}

		public override string GetSourceURL()
		{
			return m_Url;
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
				m_Operation = UnityEditor.EditorApplication.LoadLevelAdditiveAsyncInPlayMode(levelPaths[0]);
			else
				m_Operation = UnityEditor.EditorApplication.LoadLevelAsyncInPlayMode(levelPaths[0]);
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

		public override bool Update()
		{
			if (m_Request != null)
				return false;

			LoadedAssetBundle bundle = AssetBundleManager.GetLoadedAssetBundle(m_AssetBundleName, out m_DownloadingError);
			if (bundle != null)
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
			if (m_Request == null && m_DownloadingError != null)
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

			LoadedAssetBundle bundle = AssetBundleManager.GetLoadedAssetBundle(m_AssetBundleName, out m_DownloadingError);
			if (bundle != null)
			{
				///@TODO: When asset bundle download fails this throws an exception...
				m_Request = bundle.m_AssetBundle.LoadAssetAsync(m_AssetName, m_Type);
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
			if (m_Request == null && m_DownloadingError != null)
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
				LoadedAssetBundle bundle = AssetBundleManager.GetLoadedAssetBundle(m_AssetBundleName, out m_DownloadingError);
				if (bundle != null)
				{
					//TODO Ideally we want to use the following async request but it doesn't work... see here (http://forum.unity3d.com/threads/assetbundles-do-you-have-to-use-the-assetbundlemanifest-with-assetbundlerequest.423197/)
					//m_Request = bundle.m_AssetBundle.LoadAssetAsync<AssetBundleIndex>(m_AssetName);
					//so instead
					if (_isJsonIndex)
					{
						AssetBundleManager.AssetBundleIndexObject = ScriptableObject.CreateInstance<AssetBundleIndex>();
						JsonUtility.FromJsonOverwrite(bundle.m_data, AssetBundleManager.AssetBundleIndexObject);
					}
					else
					{
						AssetBundleManager.AssetBundleIndexObject = bundle.m_AssetBundle.LoadAsset<AssetBundleIndex>(m_AssetName);
					}
				}
			}
			//TODO ideally we want to use the async request but it doesn't work so we cant until I sus out why
			/*if (m_Request != null && m_Request.isDone)
            {
                AssetBundleManager.AssetBundleIndexObject = GetAsset<AssetBundleIndex>();
                return false;
            }*/
			if (AssetBundleManager.AssetBundleIndexObject != null)
			{
				return false;
			}
			else
				return true;
		}
	}
}
