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
		//the idea with 'batchId' is that before you do anything that might cause assets to start downloading you call DynamicAssetLoader.Instance.GenerateBatchID()
		//this will tag any downloading assetItems created in that cycle with the same batchID. This means we can sort downloading assets by this ID and only 
		//perform the library updates when all items in that batch are available. This stops all the errors that are happening when new assets dont match the temp assets
		public int batchID = 0;
		[Range(0, 1f)]
		public float _progress = 0;
		public bool flagForRemoval = false;
		public bool isBeingRemoved = false;

		#region CONSTRUCTOR
		public DownloadingAssetItem()
		{
		}
		public DownloadingAssetItem(int _batchID, string _requiredAssetName, UnityEngine.Object _tempAsset, string _containingBundle, Delegate callback = null/*, UMAAvatarBase _requestingUma*/)
		{
			batchID = _batchID;
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

