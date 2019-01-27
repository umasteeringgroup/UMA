using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using UMA;
using UMA.AssetBundles;

namespace UMA.CharacterSystem.Examples
{
	public class BtnGetAssetBundle : MonoBehaviour
	{

		public string assetBundleToGet = "";
		public string loadingMessage = "";
		public string loadedMessage = "";
		//could probably do with an event to trigger here too
		//so that perhaps it could trigger the Avatar to switch to the race it loaded?
		public float loadedMessageDisplaySecs = 3f;
		public UnityEvent bundleLoaded;

		bool bundleLoading = false;

		public void Update()
		{
			if (bundleLoading == true)
			{
				//ask abm if its still downloading if it is set the progress percent
				if (DynamicAssetLoader.Instance.assetBundlesDownloading == false)
				{
					DynamicAssetLoader.Instance.percentDone = 100;
					//if(AssetBundleManager.GetLoadedAssetBundle(assetBundleToGet, out error).m_AssetBundle != null)
					OnBundleLoaded();
				}
				else
				{
					DynamicAssetLoader.Instance.percentDone = (AssetBundleManager.GetBundleDownloadProgress(assetBundleToGet, true) * 100);
				}
			}
		}

		//The problem here is that because we are downloading the bundle directly it is not getting added to downloadingItems
		//The so the bundle for the race gets added immediately then when the baseRecipe is called 
		//it finds it also needs wardrobe1 and starts downloading that, 
		//but by that point we have already started building the character
		public void GetAssetBundle()
		{
			if (assetBundleToGet == "")
				return;
			bundleLoading = true;
			DynamicAssetLoader.Instance.LoadAssetBundle(assetBundleToGet.ToLower(), loadingMessage, loadedMessage);
		}

		void OnBundleLoaded()
		{
			//if it loaded properly disable the button
			//make the libraries refresh...
			//if there is an event set up trigger it
			bundleLoading = false;
			bundleLoaded.Invoke();
		}
	}
}
