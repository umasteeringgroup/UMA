using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UMA.AssetBundles;

namespace UMA.CharacterSystem
{
	[System.Serializable]
	public class DownloadingAssetsList
	{
		public List<DownloadingAssetItem> downloadingItems = new List<DownloadingAssetItem>();
		public bool areDownloadedItemsReady = true;

		public bool DownloadingItemsContains(string itemToCheck)
		{
			bool res = false;
			if (downloadingItems.Find(item => item.requiredAssetName == itemToCheck) != null)
			{
				res = true;
			}
			return res;
		}
		public bool DownloadingItemsContains(List<string> itemsToCheck)
		{
			bool res = false;
			for (int i = 0; i < itemsToCheck.Count; i++)
			{
				if (downloadingItems.Find(item => item.requiredAssetName == itemsToCheck[i]) != null)
				{
					res = true;
					break;
				}
			}
			return res;
		}
		/// <summary>
		/// Generates a temporary item of type T. It then adds a new DownloadingAssetItem to downloadingItems that contains a refrence to this created temp asset and the name of the asset that it should be replaced by once the given assetbundle has completed downloading.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="requiredAssetName"></param>
		/// <param name="containingBundle"></param>
		/// <returns></returns>
		public T AddDownloadItem<T>(string requiredAssetName, int? requiredAssetNameHash, string containingBundle, Delegate callback = null) where T : UnityEngine.Object
		{
			T thisTempAsset = null;
			if (downloadingItems.Find(item => item.requiredAssetName == requiredAssetName) == null)
			{
				if (requiredAssetNameHash == null)
				{
					requiredAssetNameHash = UMAUtils.StringToHash(requiredAssetName);
				}
				thisTempAsset = GetTempAsset<T>();
				if (typeof(T) == typeof(RaceData))
				{
					(thisTempAsset as RaceData).raceName = requiredAssetName;
					(thisTempAsset as RaceData).name = requiredAssetName;
				}
				else if (typeof(T) == typeof(SlotDataAsset))
				{
					(thisTempAsset as SlotDataAsset).name = requiredAssetName;
					(thisTempAsset as SlotDataAsset).slotName = requiredAssetName;
					(thisTempAsset as SlotDataAsset).nameHash = (int)requiredAssetNameHash;
				}
				else if (typeof(T) == typeof(OverlayDataAsset))
				{
					(thisTempAsset as OverlayDataAsset).name = requiredAssetName;
					(thisTempAsset as OverlayDataAsset).overlayName = requiredAssetName;
					(thisTempAsset as OverlayDataAsset).nameHash = (int)requiredAssetNameHash;
				}
				else if (typeof(T) == typeof(UMATextRecipe))
				{
					//now that wardrobeRecipes have their own type, we can assume an UMATextRecipe is a full character recipe
					thisTempAsset.name = requiredAssetName;
				}
				else if (typeof(T) == typeof(UMAWardrobeRecipe))
				{
					(thisTempAsset as UMAWardrobeRecipe).recipeType = "Wardrobe";
					(thisTempAsset as UMAWardrobeRecipe).wardrobeSlot = AssetBundleManager.AssetBundleIndexObject.AssetWardrobeSlot(containingBundle, requiredAssetName);
					(thisTempAsset as UMAWardrobeRecipe).Hides = AssetBundleManager.AssetBundleIndexObject.AssetWardrobeHides(containingBundle, requiredAssetName);
					(thisTempAsset as UMAWardrobeRecipe).compatibleRaces = AssetBundleManager.AssetBundleIndexObject.AssetWardrobeCompatibleWith(containingBundle, requiredAssetName);
					thisTempAsset.name = requiredAssetName;

				}
				else if (typeof(T) == typeof(UMAWardrobeCollection))
				{
					(thisTempAsset as UMAWardrobeCollection).recipeType = "WardrobeCollection";
					(thisTempAsset as UMAWardrobeCollection).wardrobeSlot = AssetBundleManager.AssetBundleIndexObject.AssetWardrobeCollectionSlot(containingBundle, requiredAssetName);
					(thisTempAsset as UMAWardrobeCollection).compatibleRaces = AssetBundleManager.AssetBundleIndexObject.AssetWardrobeCollectionCompatibleWith(containingBundle, requiredAssetName);
					thisTempAsset.name = requiredAssetName;

				}
				else if (typeof(T) == typeof(RuntimeAnimatorController))
				{
					(thisTempAsset as RuntimeAnimatorController).name = requiredAssetName;
				}
				else
				{
					thisTempAsset.name = requiredAssetName;
				}
				var thisDlItem = new DownloadingAssetItem(requiredAssetName, thisTempAsset, containingBundle, callback);
				downloadingItems.Add(thisDlItem);
			}
			else
			{
				DownloadingAssetItem dlItem = null;
				if (downloadingItems.Find(item => item.requiredAssetName == requiredAssetName) != null)
					dlItem = downloadingItems.Find(item => item.requiredAssetName == requiredAssetName);
				if (dlItem != null)
				{
					//Debug.LogWarning("DownloadingAssetsList already had entry for " + requiredAssetName + " as type " + dlItem.tempAsset.GetType().ToString() + " new request wanted it as type " + typeof(T) + " and its callback was " + dlItem.dynamicCallback[0].Method.Name);
					if (callback != null)
						if (!dlItem.dynamicCallback.Contains(callback))
							dlItem.dynamicCallback.Add(callback);
					thisTempAsset = dlItem.tempAsset as T;
				}
				else
				{
					Debug.LogWarning("Could not get TempAsset for " + requiredAssetName);
				}
			}
			return thisTempAsset;
		}

		private T GetTempAsset<T>() where T : UnityEngine.Object
		{
			T thisTempAsset = null;
			//we only want the last bit after any assembly
			var thisTypeName = typeof(T).ToString().Replace(typeof(T).Namespace + ".", "");
			//check RuntimeAnimatorController because these get called different things in the editor and in game
			if (typeof(T) == typeof(RuntimeAnimatorController))
				thisTypeName = "RuntimeAnimatorController";
			T thisPlaceholder = (T)Resources.Load<T>("PlaceholderAssets/" + thisTypeName + "Placeholder") as T;
			if (thisPlaceholder != null)//can we assume if an asset was found its a scriptableobject
			{
				thisTempAsset = ScriptableObject.Instantiate(thisPlaceholder) as T;
			}
			else
			{
				if (typeof(ScriptableObject).IsAssignableFrom(typeof(T)))
				{
					thisTempAsset = ScriptableObject.CreateInstance(typeof(T)) as T;
				}
				else
				{
					thisTempAsset = (T)Activator.CreateInstance(typeof(T));
				}
			}
			return thisTempAsset;
		}
		/// <summary>
		/// Removes a list of downloadingAssetItems from the downloadingItems List.
		/// </summary>
		/// <param name="assetName"></param>
		public IEnumerator RemoveDownload(List<DownloadingAssetItem> itemsToRemove)
		{
			//Not used any more UMAs check the status of stuff they asked for themselves
			//Dictionary<UMAAvatarBase, List<string>> updatedUMAs = new Dictionary<UMAAvatarBase, List<string>>();
			foreach (DownloadingAssetItem item in itemsToRemove)
			{
				item.isBeingRemoved = true;
			}

			foreach (DownloadingAssetItem item in itemsToRemove)
			{
				string error = "";
				//we need to check everyitem in this batch belongs to an asset bundle that has actually been loaded
				LoadedAssetBundle loadedBundleTest = AssetBundleManager.GetLoadedAssetBundle(item.containingBundle, out error);
				AssetBundle loadedBundleABTest = loadedBundleTest.m_AssetBundle;
				if (loadedBundleABTest == null && (String.IsNullOrEmpty(error)))
				{
					while (loadedBundleTest.m_AssetBundle == null)
					{
						//could say we are unpacking here
						yield return null;
					}
				}
				if (!String.IsNullOrEmpty(error))
				{
					Debug.LogError(error);
					yield break;
				}
			}
			//Now every item in the batch should be in a loaded bundle that is ready to use.
			foreach (DownloadingAssetItem item in itemsToRemove)
			{
				if (item != null)
				{
					string error = "";
					var loadedBundle = AssetBundleManager.GetLoadedAssetBundle(item.containingBundle, out error);
					var loadedBundleAB = loadedBundle.m_AssetBundle;
					if (!String.IsNullOrEmpty(error))
					{
						Debug.LogError(error);
						yield break;
					}
					var assetType = item.tempAsset.GetType();
					//deal with RuntimeAnimatorController funkiness
					//the actual type of an instantiated clone of a RuntimeAnimatorController in the editor is UnityEditor.Animations.AnimatorController
					if (assetType.ToString().IndexOf("AnimatorController") > -1)
						assetType = typeof(RuntimeAnimatorController);
					var itemFilename = AssetBundleManager.AssetBundleIndexObject.GetFilenameFromAssetName(item.containingBundle, item.requiredAssetName, assetType.ToString());
					if (assetType == typeof(RaceData))
					{
						//HACK TO FIX RACEDATA DYNAMICDNACONVERTERS DYNAMICDNA ASSETS CAUSING LOAD FAILURES in UNITY 5.5+
						//As of Unity 5.5 a bug has reappeared when loading some types of assets that reference assets in other bundles.
						//AssetBundleManager successfully ensures these required bundles are loaded first, but even so Unity fils to load
						//the required asset from them in some cases, notably it seems when the required asset is set in the field of a Prefab (like our DNAAssets are)
						//To fix this generally we could 'LoadAllAssets' from any dependent bundles, but this could incur significant memory overhead
						//So for now we will just fix this for UMA and hope a patch is forthcoming in a subsequent version of Unity 
						//FIXED as of Unity5.6.2f1
#if UNITY_5_5 || UNITY_5_6_0 || UNITY_5_6_1
						if (AssetBundleManager.AssetBundleIndexObject.GetAllDependencies(item.containingBundle).Length > 0)
						{
							var allDeps = AssetBundleManager.AssetBundleIndexObject.GetAllDependencies(item.containingBundle);
							for (int i = 0; i < allDeps.Length; i++)
							{
								string depsError = "";
								LoadedAssetBundle depsBundle = AssetBundleManager.GetLoadedAssetBundle(allDeps[i], out depsError);
								if (String.IsNullOrEmpty(depsError) && depsBundle != null)
								{
									depsBundle.m_AssetBundle.LoadAllAssets<DynamicUMADnaAsset>();
								}
							}
						}
#endif
						RaceData actualRace = loadedBundleAB.LoadAsset<RaceData>(itemFilename);
						UMAContext.Instance.raceLibrary.AddRace(actualRace);
						UMAContext.Instance.raceLibrary.UpdateDictionary();
						//Refresh DCS so that anything that this race is cross compatible with gets added to its list of available recipes
						(UMAContext.Instance.dynamicCharacterSystem as DynamicCharacterSystem).RefreshRaceKeys();
					}
					else if (assetType == typeof(SlotDataAsset))
					{
						SlotDataAsset thisSlot = null;
						thisSlot = loadedBundleAB.LoadAsset<SlotDataAsset>(itemFilename);
						if (thisSlot != null)
						{
							UMAContext.Instance.slotLibrary.AddSlotAsset(thisSlot);
						}
						else
						{
							Debug.LogWarning("[DynamicAssetLoader] could not add downloaded slot" + item.requiredAssetName);
						}
					}
					else if (assetType == typeof(OverlayDataAsset))
					{
						OverlayDataAsset thisOverlay = null;
						thisOverlay = loadedBundleAB.LoadAsset<OverlayDataAsset>(itemFilename);
						if (thisOverlay != null)
						{
							UMAContext.Instance.overlayLibrary.AddOverlayAsset(thisOverlay);
						}
						else
						{
							Debug.LogWarning("[DynamicAssetLoader] could not add downloaded overlay" + item.requiredAssetName + " from assetbundle " + item.containingBundle);
						}
					}
					else if (assetType == typeof(UMATextRecipe))
					{
						UMATextRecipe downloadedRecipe = loadedBundleAB.LoadAsset<UMATextRecipe>(itemFilename);
						(UMAContext.Instance.dynamicCharacterSystem as DynamicCharacterSystem).AddRecipe(downloadedRecipe);
					}
					else if (assetType == typeof(UMAWardrobeRecipe))
					{
						UMAWardrobeRecipe downloadedRecipe = loadedBundleAB.LoadAsset<UMAWardrobeRecipe>(itemFilename);
						(UMAContext.Instance.dynamicCharacterSystem as DynamicCharacterSystem).AddRecipe(downloadedRecipe);
					}
					else if (item.dynamicCallback.Count > 0)
					{
						//get the asset as whatever the type of the tempAsset is
						//send this as an array to the dynamicCallback
						var downloadedAsset = loadedBundleAB.LoadAsset(itemFilename, assetType);
						var downloadedAssetArray = Array.CreateInstance(assetType, 1);
						downloadedAssetArray.SetValue(downloadedAsset, 0);
						for (int i = 0; i < item.dynamicCallback.Count; i++)
						{
							item.dynamicCallback[i].DynamicInvoke(downloadedAssetArray);
						}
					}
					if (!String.IsNullOrEmpty(error))
					{
						Debug.LogError(error);
					}
				}
				downloadingItems.Remove(item);
			}
			if (downloadingItems.Count == 0)
			{
				areDownloadedItemsReady = true;
				//AssetBundleManager.UnloadAllAssetBundles();//we cant do this yet
			}
			//yield break;
		}

		/// <summary>
		/// Updates the list of downloadingItems, checks if any have finished downloading and if they have triggers the RemoveDownload method on them
		/// </summary>
		public void Update()
		{
			List<DownloadingAssetItem> finishedItems = new List<DownloadingAssetItem>();
			if (downloadingItems.Count > 0)
			{
				areDownloadedItemsReady = false;
				List<string> finishedBundles = new List<string>();
				foreach (DownloadingAssetItem dli in downloadingItems)
				{
					bool canProcessBatch = true;
					dli.UpdateProgress();
					string error = "";
					if (finishedBundles.Contains(dli.containingBundle))
					{
						if (dli.flagForRemoval == false)
						{
							dli.flagForRemoval = true;
						}
						else
						{
							if (dli.isBeingRemoved)
								canProcessBatch = false;
						}
					}
					else if (AssetBundleManager.GetLoadedAssetBundle(dli.containingBundle, out error) != null)
					{
						finishedBundles.Add(dli.containingBundle);
						if (dli.flagForRemoval == false)
						{
							dli.flagForRemoval = true;
						}
						else
						{
							if (dli.isBeingRemoved)
								canProcessBatch = false;
						}
					}
					else
					{
						canProcessBatch = false;
					}
					if (canProcessBatch)
					{
						finishedItems.Add(dli);
					}
				}
			}
			//send the finished downloads to be processed
			if (finishedItems.Count > 0)
			{
				DynamicAssetLoader.Instance.StartCoroutine(RemoveDownload(finishedItems));
			}
		}
		/// <summary>
		/// Returns the temporary asset that was generated when the DownloadingAssetItem for the given assetName was created
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="assetName"></param>
		/// <returns></returns>
		public T Get<T>(string assetName) where T : UnityEngine.Object
		{
			T tempAsset = null;
			if (downloadingItems.Find(item => item.requiredAssetName == assetName) != null)
			{
				if (downloadingItems.Find(item => item.requiredAssetName == assetName).tempAsset.GetType() == typeof(T))
					tempAsset = downloadingItems.Find(item => item.requiredAssetName == assetName) as T;
			}
			return tempAsset;
		}
		/// <summary>
		/// Returns the download progress of the asset bundle(s) required for the given asset to become available
		/// </summary>
		/// <param name="assetName"></param>
		/// <returns></returns>
		public float GetDownloadProgressOf(string assetName)
		{
			float progress = 0;
			DownloadingAssetItem item = null;
			item = downloadingItems.Find(aitem => aitem.requiredAssetName == assetName);
			if (item != null)
			{
				progress = item.Progress;
			}
			else
			{
				Debug.Log(assetName + " was not downloading");
			}
			return progress;
		}
	}
}
