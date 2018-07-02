using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

namespace UMA.AssetBundles
{
	public class AssetBundleLoadingIndicator : MonoBehaviour
	{
		public bool dontDestroyOnLoad = false;

		public enum statusOpts { Idle, Downloading, Unpacking, Complete };
		//[System.NonSerialized]
		[ReadOnly]
		public statusOpts status = statusOpts.Idle;
		//[System.NonSerialized]
		[ReadOnly]
		public float percentDone = 0f;
		//[System.NonSerialized]
		[ReadOnly]
		public float currentDownloadMeg = 0f;

		public GameObject indicatorObject;
		public Text indicatorText;
		public Slider indicatorBar;
		public Text indicatorBarText;

		public string loadingText = "Loading AssetBundles...";
		public string unpackingText = "Unpacking AssetBundles...";
		public string loadedText = "AssetBundles Loaded!";
		string _loadingMessage;
		string _unpackingMessage;
		string _loadedMessage;

		public List<string> _bundlesToCheck = new List<string>();

		public float delayHideWhenDone = 0f;

		static AssetBundleLoadingIndicator _instance = null;

		public static AssetBundleLoadingIndicator Instance
		{
			get
			{
				if (_instance == null)
				{
					FindInstance();
				}
				return _instance;
			}
		}

		// Use this for initialization
		void Start()
		{
			if (_instance != null && _instance != this)
			{
				if (_instance.dontDestroyOnLoad)
					Destroy(this.gameObject);
				else
					_instance = this;
			}
			else
			{
				_instance = this;
				if (this.dontDestroyOnLoad)
					DontDestroyOnLoad(this.gameObject);
			}
		}

		public static AssetBundleLoadingIndicator FindInstance()
		{
			if (_instance == null)
			{
				AssetBundleLoadingIndicator[] assetBundleLoadingIndicator = FindObjectsOfType(typeof(AssetBundleLoadingIndicator)) as AssetBundleLoadingIndicator[];
				if (assetBundleLoadingIndicator.Length > 0)
				{
					_instance = assetBundleLoadingIndicator[0];
					if (_instance.dontDestroyOnLoad)
						DontDestroyOnLoad(assetBundleLoadingIndicator[0].gameObject);
				}
			}
			return _instance;
		}

		// Update is called once per frame
		void Update()
		{
#if UNITY_EDITOR
			//If the download fails and we are in the editor we will automatically be switched back into simulation mode- so in this case reset and hide
			if (AssetBundleManager.SimulateOverride)
			{
				_bundlesToCheck.Clear();
				ResetAndHide();
			}
#endif
			if (status == statusOpts.Complete)
			{
				_bundlesToCheck.Clear();
				Hide();
			}
			if (status == statusOpts.Downloading || status == statusOpts.Unpacking)
			{
				if (_bundlesToCheck.Count > 0)
				{
					float overallProgress = 0;
					var newBundlesToCheck = new List<string>();
					for (int i = 0; i < _bundlesToCheck.Count; i++)
					{
						var thisProgress = AssetBundleManager.GetBundleDownloadProgress(_bundlesToCheck[i], true);
						if(thisProgress != 1f)
						{
							newBundlesToCheck.Add(_bundlesToCheck[i]);
						overallProgress += thisProgress;
					}
						
					}
					_bundlesToCheck = newBundlesToCheck;
					if (_bundlesToCheck.Count > 0)
					{
					percentDone = overallProgress / _bundlesToCheck.Count;
					}
					else
						percentDone = 1f;
				}
				UpdateProgress();
			}
		}

		public void UpdateProgress()
		{
			string donePercent = Mathf.Round(percentDone * 100).ToString();
			string msg = _loadingMessage;
			if (donePercent == "99")
			{
				msg = _unpackingMessage;
				status = statusOpts.Unpacking;
			}
			if (donePercent == "100")
			{
				msg = _loadedMessage;
				status = statusOpts.Complete;
			}
			if (indicatorText != null)
			{
				indicatorText.text = msg + " (" + donePercent + "%)";
			}
			if (indicatorBar != null)
			{
				indicatorBar.value = percentDone;
			}
			if (indicatorBarText != null)
			{
				indicatorBarText.text = donePercent + "%";
			}
		}
		public void Show(string bundleToCheck, string loadingMessage = "", string unpackingMessage = "", string loadedMessage = "")
		{
			var bundlesToCheckList = new List<string>();
			bundlesToCheckList.Add(bundleToCheck);
			Show(bundlesToCheckList, loadingMessage, unpackingMessage, loadedMessage);
		}

		public void Show(List<string> bundlesToCheck, string loadingMessage = "", string unpackingMessage = "", string loadedMessage = "")
		{
			StopCoroutine("DelayedHide");
			ResetAndHide();
			_bundlesToCheck.AddRange(bundlesToCheck);
			_loadingMessage = loadingMessage != "" ? loadingMessage : loadingText;
			_unpackingMessage = unpackingMessage != "" ? unpackingMessage : unpackingText;
			_loadedMessage = loadedMessage != "" ? loadedMessage : loadedText;
			if (indicatorText != null)
			{
				indicatorText.text = _loadingMessage + "(0%)";
			}
			if (indicatorObject != null)
			{
				indicatorObject.SetActive(true);
			}
			status = statusOpts.Downloading;
		}

		public void ShowManual(float thisPercentDone, string loadingMessage = "", string unpackingMessage = "", string loadedMessage = "")
		{
			//if we are not showing a status
			if (_bundlesToCheck.Count == 0)
			{
				StopCoroutine("DelayedHide");
				ResetAndHide();
				_loadingMessage = loadingMessage != "" ? loadingMessage : loadingText;
				_unpackingMessage = unpackingMessage != "" ? unpackingMessage : unpackingText;
				_loadedMessage = loadedMessage != "" ? loadedMessage : loadedText;
				percentDone = thisPercentDone;
				string donePercent = Mathf.Round(percentDone * 100).ToString();
				if (indicatorText != null)
				{
					indicatorText.text = _loadingMessage + "(" + donePercent + "%)";
				}
				if (indicatorObject != null)
				{
					indicatorObject.SetActive(true);
				}
				if (indicatorBar != null)
				{
					indicatorBar.value = percentDone;
				}
				if (indicatorBarText != null)
				{
					indicatorBarText.text = donePercent + "%";
				}
				status = statusOpts.Downloading;
			}
		}

		public void Hide()
		{
			if (delayHideWhenDone > 0)
			{
				StartCoroutine("DelayedHide");
			}
			else
			{
				ResetAndHide();
			}
		}

		IEnumerator DelayedHide()
		{
			yield return null;
			if (indicatorText != null)
			{
				indicatorText.text = _loadedMessage + " (100%)";
			}
			if (indicatorBar != null)
			{
				indicatorBar.value = 1f;
			}
			if (indicatorBarText != null)
			{
				indicatorBarText.text = "100%";
			}
			yield return new WaitForSeconds(delayHideWhenDone);
			ResetAndHide();
		}

		void ResetAndHide()
		{
			if (indicatorBar != null)
			{
				indicatorBar.value = 0;
			}
			if (indicatorText != null)
			{
				indicatorText.text = "";
			}
			if (indicatorBarText != null)
			{
				indicatorBarText.text = "0%";
			}
			if (indicatorObject != null)
			{
				indicatorObject.SetActive(false);
			}
			status = statusOpts.Idle;
		}
	}
}
