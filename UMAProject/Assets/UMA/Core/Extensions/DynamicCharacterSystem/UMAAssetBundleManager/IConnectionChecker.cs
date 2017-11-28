using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.AssetBundles
{
	//Use this Interface to enable AssetbundleManager to check if it has internet access and/or attempt allow your app to run offline. 
	//If the bundle index and bundles themselves have previously been downloaded ABM will use the last cached version of the index to allow it to load cached bundles.
	//Create a script that uses this interface as a wrapper for an Internet Connection checker such as 'OnlineCheckPro' or 'InternetReachbility'
	//Your script should set itself as AssetbundleManager's static ConnectionChecker value when it starts.
	public interface IConnectionChecker
	{
		//Should tell AssetBundleManager if there is a connection. You should make sure your connection checker completes its initial connection test before anything (ie DynamicAssetLoader) calls Initialize on the AssetBundleManager
		//i.e make the connection checker turn the DynamicAssetLoader game object on after its completed its initial check (DAL initializes ABM in the UMA implimentation)
		bool InternetAvailable { get; }

		//Should tell AssetBundleManager to cache a successfully downloaded bundleIndex and also to load it if there is no connection. It is stored in Application.persistentDataPath + "/cachedBundleIndex/"+ platformIndexName +".json"
		bool UseBundleIndexCaching { get; }

		//Should tell AssetBundleManager whether to restart any downloads that failed because there was no connection automatically or not.
		bool RestartFailedDownloads { get; }

		//AssetBundleManager will call this if it requires a connection to complete an operation (i.e. if it has no cached index, or is trying to download a bundle that it cannot load from the cache using LoadFromCacheOrDownload)
		//you could make this method show the user a message that they need to connect to the internet
		void ShowConnectionRequiredUI();

		//triggered by AssetbundleManager when a connection is available but a download fails because of a storm or something else stopping large downloads
		//If RestartFailedDownloads is true ABM will keep trying to download, so you should keep a note of the failed downloads and when they complete 
		//(or if the connection disappears and so ShowConnectionRequiredUI gets called) you should hide any UI you display
		//You can set RestartFailedDownloads to false while you carry out some further checks.
		void ShowDownloadFailedUI(string downloadThatFailed);
    }
}
