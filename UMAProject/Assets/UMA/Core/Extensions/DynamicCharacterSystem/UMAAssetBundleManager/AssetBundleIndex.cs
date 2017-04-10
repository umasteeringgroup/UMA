using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace UMA.AssetBundles
{
	/// <summary>
	/// An AssetBunldeIndex containing a list of assetbundles each with a list of assets inside that asset bundle. The user can customize the fields of data that are stored for each asset.
	/// The entire class is marked as partial so that extra methods for searching the index can be added as necessary.
	/// </summary>
	public partial class AssetBundleIndex : ScriptableObject
	{
		/// <summary>
		/// The actual data that gets added to the index for any given asset. Made partial so the user can add extra fields to this as required;
		/// </summary>
		[System.Serializable]
		public partial class AssetBundleIndexItem
		{
			public string filename;
			public string assetName;
			public int assetHash;
			public string assetType;

			public AssetBundleIndexItem()
			{

			}
			/// <summary>
			/// Adds data about the given asset object to this given index item. 
			/// Calls AddDataPreProcess before assigning any data and AddDataPostProcess afterwards, both of which are partial classes the user can use if necessary.
			/// </summary>
			/// <param name="_filename"></param>
			/// <param name="obj"></param>
			public virtual void AddData(string _filename, UnityEngine.Object obj)
			{
				AddDataPreProcess(_filename, obj);
				assetType = obj.GetType().ToString();
				//deal with RuntimeAnimatorController Type craziness
				if (assetType == "UnityEditor.Animations.AnimatorController")
				{
					assetType = "UnityEngine.RuntimeAnimatorController";
				}
				filename = _filename;
				if (assetType == "UMA.OverlayDataAsset" || assetType == "UMA.SlotDataAsset" || assetType == "UMA.RaceData" || assetType == "UMATextRecipe")
				{
					if (assetType == "UMA.RaceData")
					{
						assetName = (obj as UMA.RaceData).raceName;
						assetHash = UMA.UMAUtils.StringToHash((obj as UMA.RaceData).raceName);
					}
					else if (assetType == "UMA.OverlayDataAsset")
					{
						assetName = (obj as UMA.OverlayDataAsset).overlayName;
						assetHash = UMA.UMAUtils.StringToHash((obj as UMA.OverlayDataAsset).overlayName);
					}
					else if (assetType == "UMA.SlotDataAsset")
					{
						assetName = (obj as UMA.SlotDataAsset).slotName;
						assetHash = (obj as UMA.SlotDataAsset).nameHash;
					}
					else if (assetType == "UMATextRecipe")
					{
						assetName = _filename;
						assetHash = UMA.UMAUtils.StringToHash(filename);
					}
				}
				else
				{
					assetName = _filename;
					assetHash = UMA.UMAUtils.StringToHash(filename);
				}
				AddDataPostProcess(_filename, obj);
			}

			/// <summary>
			/// Impliment this method to run any extra code before data gets added to the index item in AddData
			/// </summary>
			/// <param name="filename"></param>
			/// <param name="obj"></param>
			partial void AddDataPreProcess(string filename, UnityEngine.Object obj);
			/// <summary>
			/// Impliment this method to run any extra code after data has been added to the index item in AddData
			/// </summary>
			/// <param name="filename"></param>
			/// <param name="obj"></param>
			partial void AddDataPostProcess(string filename, UnityEngine.Object obj);
		}
		/// <summary>
		/// A list of the available assetbundles each conatining a list of all the assets in that bundle. Marked as partial so this can be extended if necessary.
		/// </summary>
		[System.Serializable]
		public partial class AssetBundleIndexList
		{
			public string assetBundleName;
			public List<AssetBundleIndexItem> assetBundleAssets = new List<AssetBundleIndexItem>();
			public string[] allDependencies;
			public string[] directDependencies;
			public string assetBundleHash;
			public string encryptedName = "";

			public AssetBundleIndexList(string _assetBundleName)
			{
				assetBundleName = _assetBundleName;
			}
			public AssetBundleIndexList(string _assetBundleName, string _encryptedName)
			{
				assetBundleName = _assetBundleName;
				encryptedName = _encryptedName;
			}
			/// <summary>
			/// Adds an AssetBundleIndexItem to the list of assetBundleAssets with the given filename.
			/// </summary>
			/// <param name="filename"></param>
			/// <param name="obj"></param>
			public void AddItem(string filename, UnityEngine.Object obj)
			{
				AssetBundleIndexItem thisItem = new AssetBundleIndexItem();
				thisItem.AddData(filename, obj);
				assetBundleAssets.Add(thisItem);
			}
		}
		[SerializeField]
		public string ownBundleHash;
		[SerializeField]
		public List<AssetBundleIndexList> bundlesIndex = new List<AssetBundleIndexList>();
		[SerializeField]
		public string[] bundlesWithVariant;

		public AssetBundleIndex()
		{

		}

		public AssetBundleIndexList GetIndexItem(string assetBundleName)
		{
			for (int i = 0; i < bundlesIndex.Count; i++)
			{
				if (bundlesIndex[i].assetBundleName == assetBundleName)
				{
					return bundlesIndex[i];
                }
			}
			return null;
		}

		public string GetAssetBundleEncryptedName(string assetBundleName)
		{
			for (int i = 0; i < bundlesIndex.Count; i++)
			{
				if (bundlesIndex[i].assetBundleName == assetBundleName)
				{
					if (bundlesIndex[i].encryptedName != "")
						return bundlesIndex[i].encryptedName;
					else
						return bundlesIndex[i].assetBundleName;//do we do that?
                }
			}
			return null;
		}

		#region AssetBundleManifest clone methods

		//These methods are replicas of the AssetBundleManifest methods so that we can just use this Index in place of the manifest

		public string[] GetAllAssetBundles()
		{
			return GetAllAssetBundleNames();
		}

		public Hash128 GetAssetBundleHash(string assetBundleName)
		{
			Hash128 hash = new Hash128();
			for (int i = 0; i < bundlesIndex.Count; i++)
			{
				if (bundlesIndex[i].assetBundleName == assetBundleName)
				{
					hash = Hash128.Parse(bundlesIndex[i].assetBundleHash);
				}
			}
			return hash;
		}
		//TODO work out what this actually is and how its made so we can recreate it server side
		public string[] GetAllAssetBundlesWithVariant()
		{
			return bundlesWithVariant;
		}

		public string[] GetAllDependencies(string assetBundleName)
		{
			string[] deps = new string[0];
			for (int i = 0; i < bundlesIndex.Count; i++)
			{
				if (bundlesIndex[i].assetBundleName == assetBundleName)
				{
					deps = bundlesIndex[i].allDependencies;
				}
			}
			return deps;
		}

		public string[] GetDirectDependencies(string assetBundleName)
		{
			string[] deps = new string[0];
			for (int i = 0; i < bundlesIndex.Count; i++)
			{
				if (bundlesIndex[i].assetBundleName == assetBundleName)
				{
					deps = bundlesIndex[i].directDependencies;
				}
			}
			return deps;
		}

		#endregion

		/// <summary>
		/// Replicates AssetDatabase.GetAllAssetBundleNames() method. Gets the names of all available asset bundles.
		/// </summary>
		/// <returns>String array of all available bundles.</returns>
		public string[] GetAllAssetBundleNames()
		{
			List<string> assetBundleNames = new List<string>();
			foreach (AssetBundleIndexList iAssetList in bundlesIndex)
			{
				assetBundleNames.Add(iAssetList.assetBundleName);
			}
			return assetBundleNames.ToArray();
		}

		/// <summary>
		/// Replicates AssetBundle.Contains but adds an optional type filter
		/// </summary>
		/// <param name="assetBundleName"></param>
		/// <param name="assetName"></param>
		/// <param name="type"></param>
		/// <returns></returns>
		public bool AssetBundleContains(string assetBundleName, string assetName, string type = "")
		{
			bool assetFound = false;
			if (GetAssetBundleIndexItem(assetBundleName, assetName, type) != null)
			{
				assetFound = true;
			}
			return assetFound;
		}
		/// <summary>
		/// Replicates AssetBundle.Contains but uses assetNameHash and adds an optional type filter
		/// </summary>
		/// <param name="assetBundleName"></param>
		/// <param name="assetName"></param>
		/// <param name="type"></param>
		/// <returns></returns>
		public bool AssetBundleContains(string assetBundleName, int? assetHash, string type = "")
		{
			bool assetFound = false;
			if (GetAssetBundleIndexItem(assetBundleName, assetHash, type) != null)
			{
				assetFound = true;
			}
			return assetFound;
		}
		/// <summary>
		/// Searches the available AssetBundles for the given assetName optionally filtered by type
		/// </summary>
		/// <param name="assetName"></param>
		/// <param name="type"></param>
		/// <returns></returns>
		public string[] FindContainingAssetBundle(string assetNameOrFilename, string type = "")
		{
			List<string> assetFoundIn = new List<string>();
			for (int i = 0; i < bundlesIndex.Count; i++)
			{
				for (int ii = 0; ii < bundlesIndex[i].assetBundleAssets.Count; ii++)
				{
					if (assetNameOrFilename == bundlesIndex[i].assetBundleAssets[ii].assetName)
					{
						if (type == "" || (type != "" && (type == bundlesIndex[i].assetBundleAssets[ii].assetType || type == GetTypeWithoutAssembly(bundlesIndex[i].assetBundleAssets[ii].assetType))))
						{
							assetFoundIn.Add(bundlesIndex[i].assetBundleName);
						}

					}
				}
			}
			//if we didn't find it check the filename?
			if (assetFoundIn.Count == 0)
			{
				for (int i = 0; i < bundlesIndex.Count; i++)
				{
					for (int ii = 0; ii < bundlesIndex[i].assetBundleAssets.Count; ii++)
					{
						if (assetNameOrFilename == bundlesIndex[i].assetBundleAssets[ii].filename)
						{
							if (type == "" || (type != "" && (type == bundlesIndex[i].assetBundleAssets[ii].assetType || type == GetTypeWithoutAssembly(bundlesIndex[i].assetBundleAssets[ii].assetType))))
							{
								assetFoundIn.Add(bundlesIndex[i].assetBundleName);
							}

						}
					}
				}
			}
			return assetFoundIn.ToArray();
		}
		/// <summary>
		/// Searches the available AssetBundles for the given assetNameHash optionally filtered by type (type may be un-necessary it depends how unique the hashes are)
		/// </summary>
		/// <param name="assetNameHash"></param>
		/// <param name="type"></param>
		/// <returns></returns>
		public string[] FindContainingAssetBundle(int? assetNameHash, string type = "")
		{
			List<string> assetFoundIn = new List<string>();
			for (int i = 0; i < bundlesIndex.Count; i++)
			{
				for (int ii = 0; ii < bundlesIndex[i].assetBundleAssets.Count; ii++)
				{
					if (assetNameHash == bundlesIndex[i].assetBundleAssets[ii].assetHash)
					{
						if (type == "" || (type != "" && (type == bundlesIndex[i].assetBundleAssets[ii].assetType || type == GetTypeWithoutAssembly(bundlesIndex[i].assetBundleAssets[ii].assetType))))
						{
							assetFoundIn.Add(bundlesIndex[i].assetBundleName);
						}

					}
				}
			}
			return assetFoundIn.ToArray();
		}
		/// <summary>
		/// Gets all the assets of a particular type that are contained in the given asset bundle
		/// </summary>
		/// <param name="assetBundleName"></param>
		/// <param name="type"></param>
		/// <returns></returns>
		public string[] GetAllAssetsOfTypeInBundle(string assetBundleName, string type)
		{
			List<string> foundAssets = new List<string>();
			foreach (AssetBundleIndexList iAssetList in bundlesIndex)
			{
				if (iAssetList.assetBundleName == assetBundleName)
				{
					foreach (AssetBundleIndexItem iAsset in iAssetList.assetBundleAssets)
					{
						if (type == "" || (type != "" && (type == iAsset.assetType || type == GetTypeWithoutAssembly(iAsset.assetType))))
						{
							foundAssets.Add(iAsset.assetName);
						}

					}
				}
			}
			return foundAssets.ToArray();
		}

		public AssetBundleIndexItem GetAssetBundleIndexItem(string assetBundleName, string assetNameOrFilename, string type = "")
		{
			AssetBundleIndexItem indexAsset = null;
			foreach (AssetBundleIndexList iAssetList in bundlesIndex)
			{
				if (indexAsset != null)
					break;
				if (iAssetList.assetBundleName == assetBundleName)
				{
					foreach (AssetBundleIndexItem iAsset in iAssetList.assetBundleAssets)
					{
						if (assetNameOrFilename == iAsset.assetName)
						{
							if (type == "" || (type != "" && (type == iAsset.assetType || type == GetTypeWithoutAssembly(iAsset.assetType))))
							{
								indexAsset = iAsset;
							}

						}
						else if (assetNameOrFilename == iAsset.filename)
						{
							if (type == "" || (type != "" && (type == iAsset.assetType || type == GetTypeWithoutAssembly(iAsset.assetType))))
							{
								indexAsset = iAsset;
							}

						}
						if (indexAsset != null)
							break;
					}
				}
			}
			return indexAsset;
		}

		public AssetBundleIndexItem GetAssetBundleIndexItem(string assetBundleName, int? assetNameHash, string type = "")
		{
			AssetBundleIndexItem indexAsset = null;
			foreach (AssetBundleIndexList iAssetList in bundlesIndex)
			{
				if (indexAsset != null)
					break;
				if (iAssetList.assetBundleName == assetBundleName)
				{
					foreach (AssetBundleIndexItem iAsset in iAssetList.assetBundleAssets)
					{
						if (assetNameHash == iAsset.assetHash)
						{
							if (type == "" || (type != "" && (type == iAsset.assetType || type == GetTypeWithoutAssembly(iAsset.assetType))))
							{
								indexAsset = iAsset;
							}

						}
						if (indexAsset != null)
							break;
					}
				}
			}
			return indexAsset;
		}

		public string GetFilenameFromAssetName(string assetBundleName, string assetname, string type = "")
		{
			return GetAssetBundleIndexItem(assetBundleName, assetname, type).filename;
		}


		public string GetAssetNameFromFilename(string filename, string type = "")
		{
			string assetName = "";
			string[] foundInBundles = FindContainingAssetBundle(filename, type);
			if (foundInBundles.Length > 0)
			{
				assetName = GetAssetBundleIndexItem(foundInBundles[0], filename, type).assetName;
			}
			return assetName;
		}
		public string GetAssetNameFromFilename(string assetBundleName, string filename, string type = "")
		{
			string assetName = "";
			assetName = GetAssetBundleIndexItem(assetBundleName, filename, type).assetName;
			return assetName;
		}
		public string GetAssetNameFromHash(int? assetNameHash, string type = "")
		{
			string assetName = "";
			string[] foundInBundles = FindContainingAssetBundle(assetNameHash, type);
			if (foundInBundles.Length > 0)
			{
				assetName = GetAssetBundleIndexItem(foundInBundles[0], assetNameHash, type).assetName;
			}
			return assetName;
		}

		public string GetAssetNameFromHash(string assetBundleName, int? assetNameHash, string type = "")
		{
			string assetName = "";
			assetName = GetAssetBundleIndexItem(assetBundleName, assetNameHash, type).assetName;
			return assetName;
		}

		public int? GetAssetHashFromName(string assetBundleName, string assetName, string type = "")
		{
			int? assetNameHash = null;
			assetNameHash = (int?)GetAssetBundleIndexItem(assetBundleName, assetName, type).assetHash;
			return assetNameHash;
		}

		static string GetTypeWithoutAssembly(string fullType)
		{
			var typeParts = fullType.Split(new string[1] { "." }, System.StringSplitOptions.None);
			string typeWithoutAssembly = typeParts[typeParts.Length - 1];
			return typeWithoutAssembly;
		}
	}
}
