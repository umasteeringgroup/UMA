using UnityEngine;
using System;
using System.Collections;
using UMAAssetBundleManager;

namespace UMA
{
	/// <summary>
	/// Downloading assets Item that provides access to the progress of the requested asset
	/// </summary>
	//TODO when finished developing make this non serialized
	[System.Serializable]
	public class DownloadingAssetItem
	{
		public string requiredAssetName;
		public UnityEngine.Object tempAsset;
		public string containingBundle;
		public Delegate dynamicCallback;
		[Range(0, 1f)]
		public float _progress = 0;
		public bool flagForRemoval = false;
		public bool isBeingRemoved = false;

		#region CONSTRUCTOR
		public DownloadingAssetItem()
		{
		}
		public DownloadingAssetItem(string _requiredAssetName, UnityEngine.Object _tempAsset, string _containingBundle, Delegate callback = null)
		{
			requiredAssetName = _requiredAssetName;
			tempAsset = _tempAsset;
			containingBundle = _containingBundle;
			if (callback != null)
				dynamicCallback = callback;
		}
		#endregion

		public float Progress
		{
			get
			{
				UpdateProgress();
				return _progress;
			}
		}

		public void UpdateProgress()
		{
			_progress = AssetBundleManager.GetBundleDownloadProgress(containingBundle, true);
		}
	}
}
