using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic; 
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;

namespace UMA 
{
	[System.Serializable]
	public class UMAResourcesIndex : MonoBehaviour, ISerializationCallbackReceiver
	{
        static string dataAssetPath;
        public static UMAResourcesIndex Instance;
		private static UMAResourcesIndexData index = null;
		//private static UMAAssetModificationProcessor AMP = null;
		public bool enableDynamicIndexing = false;
		public bool makePersistent = false;

		//when generating the index this is used to warn the user of any assets that have duplicate names
		//These will need to be made unique because the libraries ask for assets by name (because they dont know the path)
		public UMAResourcesIndexData duplicateNamesIndex = null;

		//Index (with a capital I) need to be a property that calls LoadOrCreateData if this is not initialized
		public UMAResourcesIndexData Index
		{
			get
			{
				if (index == null)
				{
					LoadOrCreateData();
				}
				return index;
			}
		}


		public UMAResourcesIndex()
		{
#if UNITY_EDITOR
			//AMP = new UMAAssetModificationProcessor();
			if (Instance == null)
				Instance = this;
			if (Instance != null)
			{

			}
#endif
		}

        void Start()
		{
            if (Instance == null)
			{
				Instance = this;
				if (makePersistent && Application.isPlaying)
					DontDestroyOnLoad(gameObject);
			}
			else if (Instance != this)
			{
				if (Instance.makePersistent && Application.isPlaying)
                {
                    Destroy(Instance.gameObject);
                }
				Instance = this;

			}
			else if (Instance == this)//OnAfterDeserialize() gets called in the editor but doesn't do anything with the makePersistent value
			{
				if (makePersistent && Application.isPlaying)
					DontDestroyOnLoad(gameObject);
			}
			if (index == null)
				LoadOrCreateData();
		}

		void OnApplicationQuit()
		{
			Save();
		}

		public void OnBeforeSerialize()
		{

		}

		public void OnAfterDeserialize()
		{
			if (Instance == null)//make an Instance in the editor too 
			{
				Instance = this;
			}
		}

#if UNITY_EDITOR
		List<string> assetsToAdd = new List<string>();
		List<string> assetsToRemove = new List<string>();
		//TODO grr we also need to check if its a slot/overlay/race asset because even if its not renamed or moved the user may still have changed 
		//the internal name and so the hash will need to be updated
		public void DoIndexUpdate()
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return;
			//if (!EditorApplication.isCompiling)
			//{
				EditorApplication.update -= CheckIndexUptoDate;
				EditorApplication.update += CheckIndexUptoDate;
			//}
		}

		public void DoModifiedUMAAssets(string[] assetsToSave)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return;

			if (assetsToSave.Length == 0)
			{
				//Debug.Log("assetsToSave.Length was zero- did we duplicate an asset?");
				EditorApplication.update -= CheckIndexUptoDate;
				EditorApplication.update += CheckIndexUptoDate;
			}
			else if (assetsToSave.Length == 1 && assetsToSave[0].IndexOf("ProjectSettings.asset") > 0)
			{
				//.Log("assetsToSave.Length was 1 (ProjectSettings.asset)- did we duplicate an asset?");
				EditorApplication.update -= CheckIndexUptoDate;
				EditorApplication.update += CheckIndexUptoDate;
			}
			else
			{
				//we are only intereste in assets in Resources
				for (int i = 0; i < assetsToSave.Length; i++)
				{
					if (assetsToSave[i].IndexOf("/Resources/") > -1)
					{
						if (!assetsToAdd.Contains(assetsToSave[i]))
							assetsToAdd.Add(assetsToSave[i]);
					}
				}

				EditorApplication.update -= CheckModifiedUMAAssets;
				EditorApplication.update += CheckModifiedUMAAssets;
			}
		}

		public void CheckModifiedUMAAssets()
		{
			EditorApplication.update -= CheckModifiedUMAAssets;
			//Debug.LogWarning("CheckModifiedUMAAssets HAPPENNED assetsToAdd.Count was  "+assetsToAdd.Count);
			var assetsToAddUpdate = new List<string>();
			var assetsToRemoveUpdate = new List<string>();
			if (assetsToAdd.Count > 0)
			{
				//if we have an UMA Asset and its already in the index, we need to make sure its hash is uptodate
				for(int i = 0; i < assetsToAdd.Count; i++)
				{
					//Debug.LogWarning("CheckModifiedUMAAssets tried to add " + assetsToAdd[i]);
					var tempObj = AssetDatabase.LoadMainAssetAtPath(assetsToAdd[i]);
					if (tempObj != null)
					{
						if (tempObj.GetType() == typeof(RaceData) || tempObj.GetType() == typeof(SlotDataAsset) || tempObj.GetType() == typeof(OverlayDataAsset))
						{
							if (Index.CurrentPaths.Contains(assetsToAdd[i]))
							{
								int thisHash = 0;
								if (tempObj.GetType() == typeof(SlotDataAsset))
								{
									thisHash = ((SlotDataAsset)tempObj).nameHash;
								}
								if (tempObj.GetType() == typeof(OverlayDataAsset))
								{
									thisHash = ((OverlayDataAsset)tempObj).nameHash;
								}
								if (tempObj.GetType() == typeof(RaceData))
								{
									var thisName = ((RaceData)tempObj).raceName;
									thisHash = UMAUtils.StringToHash(thisName);
								}
								//we need to get the index item for this object using the path and update its hash
								//if we can- there may be an asset with this hash already
								//in this case we need to REMOVE this asset from the index and show a duplicate assets warning
								if (!Index.UpdateHashByPath(tempObj.GetType().ToString(), thisHash, assetsToAdd[i]))
								{
									duplicateNamesIndex.AddPath(tempObj, thisHash);
									if(!assetsToRemoveUpdate.Contains(assetsToAdd[i]))
										assetsToRemoveUpdate.Add(assetsToAdd[i]);
									//Debug.LogWarning(tempObj.name + " could not use the set slot/overlay/racename because another Asset is using it! See the UMAResourcesIndex for details.");
								}
								else
								{
									//Debug.Log(tempObj.name + " updated slot/overlay/racename successfully");
									//check duplicate assets doesn't containg this any more
									if (duplicateNamesIndex.Contains(assetsToAdd[i]))
									{
										duplicateNamesIndex.RemovePath(assetsToAdd[i]);
									}
									else
									{
										//Debug.Log("DUPLICATE NAMES INDEX DIDN@T CONTAIN " + assetsToAdd[i]);
									}
								}
							}
							else
							{
								//try to add it the normal way, if there is already an asset with that hash the add method shows an error
								if (!assetsToAddUpdate.Contains(assetsToAdd[i]))
								{
									assetsToAddUpdate.Add(assetsToAdd[i]);
									if (duplicateNamesIndex.Contains(assetsToAdd[i]))
									{
										//Debug.LogWarning("Removed Duplicate for "+assetsToAdd[i]);
										duplicateNamesIndex.RemovePath(assetsToAdd[i]);
									}
									else
									{
										//Debug.Log("DUPLICATE NAMES INDEX DIDN@T CONTAIN " + assetsToAdd[i]);
									}
								}
							}
						}
					}
					else
					{
						Debug.LogWarning("tempObj was null for path " + assetsToAdd[i]);
                    }
				}
				if(assetsToAddUpdate.Count > 0 || assetsToRemoveUpdate.Count > 0 )
				{
					UpdateIndexInternal(assetsToAddUpdate, assetsToRemoveUpdate, false);
				}
				else
				{
					Save();
				}
			}
			assetsToAdd.Clear();
		}

		public void CheckIndexUptoDate()
		{
			EditorApplication.update -= CheckIndexUptoDate;
			var actualResourcesPaths = GetResourcesPaths();
			//Debug.Log("AMP called me Index.Count was " + Index.Count() + " Index.CurrentPaths was " + Index.CurrentPaths.Count+ " actualResourcesPaths = "+ actualResourcesPaths.Length);
			assetsToAdd.Clear();
			assetsToRemove.Clear();
			//what to add
			for(int i = 0; i < actualResourcesPaths.Length; i++)
			{
				if (!Index.CurrentPaths.Contains(actualResourcesPaths[i]))
				{
					assetsToAdd.Add(actualResourcesPaths[i]);
				}
			}
			//what to remove
			for (int i = 0; i < Index.CurrentPaths.Count; i++)
			{
				bool found = false;
				for(int ii = 0; ii < actualResourcesPaths.Length; ii++)
				{
					if(actualResourcesPaths[ii] == Index.CurrentPaths[i])
					{
						found = true;
						break;
					}
				}
				if (!found)
				{
					assetsToRemove.Add(Index.CurrentPaths[i]);
					if (duplicateNamesIndex.Contains(Index.CurrentPaths[i]))
					{
						//Debug.LogWarning("Removed Duplicate for because it was DELETED " + Index.CurrentPaths[i]);
						duplicateNamesIndex.RemovePath(Index.CurrentPaths[i]);
					}
				}
			}
			//Debug.Log("assetsToAdd.Count = " + assetsToAdd.Count + " assetsToRemove count = " + assetsToRemove.Count);
			if(assetsToAdd.Count > 0 || assetsToRemove.Count > 0)
			{
				UpdateIndexInternal(assetsToAdd, assetsToRemove);
				assetsToAdd.Clear();
				assetsToRemove.Clear();
			}
		}
#endif
		public void Add(UnityEngine.Object obj, bool bulkAdd = false)
		{
#if UNITY_EDITOR
			if (obj == null)
				return;
			string thisName = obj.name;
			if (obj.GetType() == typeof(SlotDataAsset))
			{
				thisName = ((SlotDataAsset)obj).slotName;
			}
			if (obj.GetType() == typeof(OverlayDataAsset))
			{
				thisName = ((OverlayDataAsset)obj).overlayName;
			}
			if (obj.GetType() == typeof(RaceData))
			{
				thisName = ((RaceData)obj).raceName;
			}
			Index.AddPath(obj, thisName);
            if (!bulkAdd) Save();
#endif
		}

        public void Add(UnityEngine.Object obj, string objName, bool bulkAdd = false)
		{
#if UNITY_EDITOR
			if (obj == null || objName == "")
				return;
			Index.AddPath(obj, objName);
            if (!bulkAdd) Save();
#endif
		}
        public void Add(UnityEngine.Object obj, int objNameHash, bool bulkAdd = false)
		{
#if UNITY_EDITOR
			if (obj == null)
				return;
			Index.AddPath(obj, objNameHash);
			if (!bulkAdd) Save();
#endif
		}
		/// <summary>
		/// Loads saved Index data from a file or creates new data object;
		/// </summary>
		/// <returns></returns>
		public void LoadOrCreateData()
		{
#if UNITY_EDITOR
            dataAssetPath = GetIndexPath();

            if (File.Exists(dataAssetPath))
            {
                var rawData = FileUtils.ReadAllText(dataAssetPath);
                index = JsonUtility.FromJson<UMAResourcesIndexData>(rawData);
				//this can happen because of the domain reload bug that blanks the index somehow
				if (index == null)
				{
					Debug.Log("Index was blanked after domain reload bug. Rebuilding...");
					index = new UMAResourcesIndexData();
					IndexAllResources();
				}
				return;
            }
#else
            TextAsset textIndex = Resources.Load<TextAsset>("UMAResourcesIndex");
            if (textIndex != null)
            {
                index = JsonUtility.FromJson<UMAResourcesIndexData>(textIndex.text);
				if(index == null)
					index = new UMAResourcesIndexData();    
				return;
            }
			else
			{
				Debug.LogWarning("No UMAResourcesIndex.txt file was found. Please ensure you have done 'Create/Update Index' in a UMAResourcesIndex gameobject component before you build.");
			}
#endif
			// Not found anywhere
			index = new UMAResourcesIndexData();
#if UNITY_EDITOR
            IndexAllResources();
#endif
        }

        public string GetIndexInfo()
		{
			int totalIndexedTypes = 0;
			int totalIndexedFiles = 0;
			if (Index.data != null)
			{
				totalIndexedTypes = index.data.Length;
				totalIndexedFiles = 0;
				List<string> typeNames = new List<string>();
				for (int i = 0; i < totalIndexedTypes; i++)
				{
					typeNames.Add(index.data[i].type);
					totalIndexedFiles += index.data[i].typeFiles.Length;
				}
			}
			string info = "Total files indexed: " + totalIndexedFiles + " in " + totalIndexedTypes + " Types";
			return info;
		}
#if UNITY_EDITOR

        private string GetIndexPath()
        {
			return Path.Combine(FileUtils.GetInternalDataStoreFolder(false, false), "UMAResourcesIndex.txt");
		}
#endif

        /// <summary>
        /// Saves any updates to the index to the data file. This only happens in the editor.
        /// </summary>
        public void Save()
		{
#if UNITY_EDITOR
            dataAssetPath = GetIndexPath();
			var currentPathsCache = new List<string>(Index.CurrentPaths);
			Index.CurrentPaths = new List<string>();
            var jsonData = JsonUtility.ToJson(index);
			Index.CurrentPaths = currentPathsCache;
			FileUtils.WriteAllText(dataAssetPath, jsonData);
#endif
        }

#if UNITY_EDITOR

		public string[] GetResourcesPaths()
		{
			var paths = AssetDatabase.GetAllAssetPaths();
			var retPaths = new List<string>();
			var retAssetPaths = new List<string>();
			//check they are in Resources
			for (int i = 0; i < paths.Length; i++)
			{
				if (paths[i].IndexOf("/Resources/") > -1)
				{
					retPaths.Add(paths[i]);
				}
			}
			//check they are not just folders
			for (int i = 0; i < retPaths.Count; i++)
			{
				var extension = Path.GetExtension(retPaths[i]);
				//this is still not right- probably more not right on non windows systems buts its close
				if (extension != "" && extension.IndexOf(".db") == -1)
				{
					retAssetPaths.Add(retPaths[i]);
				}
			}
			return retAssetPaths.ToArray();
		}

		/// <summary>
		/// Clears the Index of all data.
		/// </summary>
		public void ClearIndex()
		{
			index = new UMAResourcesIndexData();
			Save();
		}

		/// <summary>
		/// Used when assets are Created/Deleted/Modified to 'automagically' update the index
		/// </summary>
		/// <param name="assetsToAdd"></param>
		/// <param name="assetsToRemove"></param>
		void UpdateIndexInternal(List<string> assetsToAdd, List<string> assetsToRemove, bool clearDuplicatesIndex = true)
		{
			//Debug.Log("UpdateIndexInternal");
			if(clearDuplicatesIndex)
				duplicateNamesIndex = new UMAResourcesIndexData();
			//remove
			for (int i = 0; i < assetsToRemove.Count; i++)
			{
				if (Index.RemovePath(assetsToRemove[i]))
				{
					//Debug.Log("REMOVED " + assetsToRemove[i]);
				}
				else
				{
					//Debug.Log("REMOVE FAILED " + assetsToRemove[i]);
				}
			}
			//add
			for (int i = 0; i < assetsToAdd.Count; i++)
			{
				var objResourcesPathArray = assetsToAdd[i].Split(new string[] { "Resources/" }, StringSplitOptions.RemoveEmptyEntries);
				var extension = Path.GetExtension(objResourcesPathArray[1]);
				var objResourcesPath = objResourcesPathArray[1];
				if (extension != "")
				{
					objResourcesPath = objResourcesPath.Replace(extension, "");
				}
				var tempObj = AssetDatabase.LoadMainAssetAtPath(assetsToAdd[i]);
				if (tempObj != null)
				{
					if (AddObjectToIndex(tempObj, objResourcesPath))
					{
						//Debug.Log("ADDED " + assetsToAdd[i]);
					}
					else
					{
                        //Debug.Log("FAILED to add " + assetsToAdd[i]);
                    }
				}
				else
				{
					//Debug.Log("FAILED to add NULL " + assetsToAdd[i]);
				}
			}
			var msg = "[UMAResourcesIndex] Added/Updated " + index.Count() + " assets in the Index.";
			if (duplicateNamesIndex.Count() > 0)
			{
				msg += " There were also " + duplicateNamesIndex.Count() + " duplicate assets. See the UMAResourcesIndex component for details.";
				Debug.LogWarning(msg);
			}
			else
				Debug.Log(msg);
			Save();
		}

		/// <summary>
		/// Method to generate a full index of every file in Resources
		/// </summary>
		// slight issue here is that UMABonePose assets dont have a hash and expressions are called the same thing for every race (so we only end up with one set indexed). But since they are refrerenced in an expressionset this seems to work ok anyway.
		public void IndexAllResources()
		{
            if (Application.isPlaying)
			{
				Debug.Log("You can only create a full Resources index while the application is not playing.");
				return;
			}
			//Debug.Log("Indexing all resources");
			//02022016 Make sure the index is always cleared first because sometimes the type of the asset may have changed and we dont want the old index any more
			index = new UMAResourcesIndexData();
			duplicateNamesIndex = new UMAResourcesIndexData();
			var paths = GetResourcesPaths();
			int pathsAdded = 0;
			for (int i = 0; i < paths.Length; i++)
			{
				//we need to split the path and only use the part after resources
				var objResourcesPathArray = paths[i].Split(new string[] { "Resources/" }, StringSplitOptions.RemoveEmptyEntries);
				var extension = Path.GetExtension(objResourcesPathArray[1]);
				var objResourcesPath = objResourcesPathArray[1];
                if (extension != "")
				{
					objResourcesPath = objResourcesPath.Replace(extension, "");
				}
				var tempObj = AssetDatabase.LoadMainAssetAtPath(paths[i]);
				if (tempObj != null)
				{
					if(AddObjectToIndex(tempObj, objResourcesPath)) {
						pathsAdded++;
					}
				}
				else
				{
					//Debug.Log("Null object for "+ paths[i]);
				}
			}
			var msg = "[UMAResourcesIndex] Added/Updated " + index.Count() + " assets in the Index.";
			if(duplicateNamesIndex.Count() > 0)
			{
				msg += " There were also " + duplicateNamesIndex.Count() + " duplicate assets. See the UMAResourcesIndex component for details.";
				Debug.LogWarning(msg);
            }
			else
				Debug.Log(msg);
			Save();
			Resources.UnloadUnusedAssets();
        }

		private bool AddObjectToIndex(UnityEngine.Object tempObj, string path)
		{
			bool added = false;
			//string existingAsset = "";
			string thisName = Path.GetFileNameWithoutExtension(path);
			int thisHash = UMAUtils.StringToHash(thisName);
			if (tempObj.GetType() == typeof(SlotDataAsset))
			{
				thisName = ((SlotDataAsset)tempObj).slotName;
				thisHash = ((SlotDataAsset)tempObj).nameHash;
			}
			if (tempObj.GetType() == typeof(OverlayDataAsset))
			{
				thisName = ((OverlayDataAsset)tempObj).overlayName;
				thisHash = ((OverlayDataAsset)tempObj).nameHash;
			}
			if (tempObj.GetType() == typeof(RaceData))
			{
				thisName = ((RaceData)tempObj).raceName;
				thisHash = UMAUtils.StringToHash(thisName);
			}
			if(!IsAssetDuplicate(tempObj, thisHash, path))
			{
				index.AddPath(tempObj, thisHash);
				added = true;
			}
			return added;
		}

		private bool IsAssetDuplicate(UnityEngine.Object tempObj, int assetHash, string path)
		{
			//Is there already an asset with this name?
			//duplicate names only matters if they are things that will be got by the UMA libraries BY NAME and these things currently are
			//SlotDataAsset, OverlayDataAsset, RaceDataAsset, Maybe UMATextRecipe (and Descendents) and RuntimeAnimationControllers
			var existingAsset = "";
			if (tempObj.GetType() == typeof(RaceData) || tempObj.GetType() == typeof(SlotDataAsset) || tempObj.GetType() == typeof(OverlayDataAsset) || tempObj.GetType() == typeof(UMATextRecipe) || tempObj.GetType() == typeof(UMAWardrobeRecipe) || tempObj.GetType() == typeof(RuntimeAnimatorController))
			{
				//when you have two different types of assets with the same name in the same place we get a false positive						 
				var existingAssetFullPath = index.GetPath(tempObj.GetType().ToString(), assetHash, true);
				if (existingAssetFullPath != "")
				{
					var existingAssetFullPathArray = existingAssetFullPath.Split(new string[] { "Resources/" }, StringSplitOptions.RemoveEmptyEntries);
					var existingAssetExtension = Path.GetExtension(existingAssetFullPathArray[1]);
					var existingAssetPath = existingAssetFullPathArray[1];
					if (existingAssetExtension != "")
					{
						existingAssetPath = existingAssetPath.Replace(existingAssetExtension, "");
					}
					//if existingAssetPath != objResourcesPath then its another slot/overlay that has the same name as one in the index
					existingAsset = existingAssetPath != path ? existingAssetPath : "";
					//BUT if its a Race/Slot/Overlay the user may have changed the race/slot/overlay name and we will need to update the hash to the new race/slot/overlay hash
				}
				if (existingAsset != "")
				{
					Debug.LogWarning("had existing asset for " + tempObj.name + " type of " + tempObj.GetType().ToString());
					duplicateNamesIndex.AddPath(tempObj, assetHash);
					return true;
				}
			}
			return false;
		}
#endif
	}
}
