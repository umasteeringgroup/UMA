using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
using UMAEditor;
#endif

namespace UMA
{
	/// <summary>
	/// Contains Refrences for all UMA Assets that you want to be included in the build and accessed by any Libraries in your project. Also includes a list of AssetBundle assets, BUT THESE ARE NOT INCLUDED IN YOUR BUILD.
	/// The list is just here for 'simulation mode in the editor. But It could potentially in future be used to assign things to bundles (watch this space)
	/// </summary>
	//NOTES With this, there is no need for ahything to get the path, all this needs to serve up its the assets.
	//so we have an equivalents  for Resources.Load that deliver the ref'd asset. We can do that because this asset ITSELF will be included in Resources
	//and will cause any assets it refs to also be included in Resources whether they are or not. Its a really clever solution thumbs way, way up to joen for coming up with it!
	public partial class UMAAssetIndex : ScriptableObject
	{

#if UNITY_EDITOR
		//UMAAssetIndex creates this itself if Instance is null and no Index exists (or was deleted) in Resources
		public static void CreateUMAAssetIndex()
		{
            if (Resources.Load("UMAAssetIndex-DONOTDELETE") != null)
			{
				EditorUtility.DisplayDialog("UMA Asset Index Already Exists", "You dont need to create a new UMA Asset Index because one already exists in " + UMA.FileUtils.GetInternalDataStoreFolder(false, false) + ". If you want to update your index please go to UMA/Utilities/UMA Asset Index or delete the existing index first", "OK");
				return;
			}
			//This needs to be created in our internal data store folder (so *hopefully* people dont delete it)
			UMAEditor.CustomAssetUtility.CreateAsset<UMAAssetIndex>(Path.Combine(UMA.FileUtils.GetInternalDataStoreFolder(false, false), "UMAAssetIndex-DONOTDELETE.asset"));
		}
#endif
		static UMAAssetIndex _instance;

		[Tooltip("Set the types you wish to track in the index in here.")]
		public List<string> typesToIndex = new List<string>() { "SlotDataAsset", "OverlayDataAsset", "RaceData", "UMATextRecipe", "UMAWardrobeRecipe", "UMAWardrobeCollection", "RuntimeAnimatorController","DynamicUMADnaAsset" };
		//the index of all the possible assets you could have (excluding those in AssetBundles)
		//Used to generate the list in the Editor where you assign/unassign assets to the serialized _buildIndex
		private UMAAssetIndexData _fullIndex = new UMAAssetIndexData();
		//the index of all the assets you have selected to include
		//This is the only index that gets 'Saved' into the game
		[SerializeField]
		private UMAAssetIndexData _buildIndex = new UMAAssetIndexData();
		//This list is not serialized.
		//BUT it would be nice if we could use this when we simulateAssetBundles in the editor (because it would be quicker)
		private UMAAssetIndexData _assetBundleIndex = new UMAAssetIndexData();

#if UNITY_EDITOR
		//UMA Asset Modification Processor Lists
		//These lists are populated each time AssetModificationProcessor does anything with any assets
		List<string> AMPDeletedAssets = new List<string>();

		List<string> AMPCreatedAssets = new List<string>();

		List<string> AMPSavedAssets = new List<string>();

		//moved paths need a special Type
		private class AMPMovedAsset
		{
			public string prevPath = "";
			public string newPath = "";
			
			public AMPMovedAsset() { }
			public AMPMovedAsset(string _prevPath, string _newPath)
			{
				prevPath = _prevPath;
				newPath = _newPath;
			}
		}

		List<AMPMovedAsset> AMPMovedAssets = new List<AMPMovedAsset>();


		//The windowInstance is assigned when the index is viewed in a window, this is so we can refresh the view when this script modifies the index
		public EditorWindow windowInstance = null;
#endif

		/// <summary>
		/// Gets or creates the UMAAssetIndex Instance. If not Scriptable Object asset has yet been created, it creates one and stores it in UMAInternalStorage/InGame/Resources
		/// </summary>
		public static UMAAssetIndex Instance
		{
			get
			{
				if(_instance == null)
				{
					_instance = (UMAAssetIndex) Resources.Load("UMAAssetIndex-DONOTDELETE");
#if UNITY_EDITOR
					if (_instance == null)
					{
						CreateUMAAssetIndex();
						_instance = (UMAAssetIndex) Resources.Load("UMAAssetIndex-DONOTDELETE");
						_instance.GenerateLists();
                    }
#endif
				}
				return _instance;
			}
		}

		/// <summary>
		/// The Build Index contains refrences to all the assets you have made live in the game using the UMAAssetIndex, 
		/// including those in Resources folders
		/// </summary>
		public UMAAssetIndexData BuildIndex
		{
			get { return _buildIndex; }
		}
		/// <summary>
		/// The Full Index contains a list of all the assets you *could* make live in your game (but not refrences) 
		/// this is not serialized and should only be used to find assets to add to Build Index
		/// </summary>
		public UMAAssetIndexData FullIndex
		{
			get { return _fullIndex; }
		}

		/// <summary>
		/// The AssetBundleIndex contains a list of assets that are currently assigned to an asset bundle (but not refrences)
		/// This is not serliaized but can be used to check if a given asset is in an asset bundle inside the editor
		/// </summary>
		public UMAAssetIndexData AssetBundleIndex
		{
			//Possibly if this is empty in the editor (because we dont serialize it) we could load the last data from a file
			//That way we could use this instead of searching AssetDatabase when we are Simulating AssetBundles in the Edior?
			get { return _assetBundleIndex; }
		}

		public UMAAssetIndex()
		{
			_instance = this;
        }

#if UNITY_EDITOR

		public void OnEnable()
		{
			_instance = this;
			GenerateLists();
        }

		private void GenerateLists()
		{
			GetAllAvailableUMAAssets();
		}

		#region ASSET MODIFICATION PROCESSOR CALLBACKS

		/// <summary>
		/// Callback for UMAAssetModificationProcessor that is triggered when Assets or folders are moved in the project. 
		/// Assigns paths to the AMPMovedAssets list and sets up DoMoveAsset to process the list when the assetModificationProcessor is finished
		/// </summary>
		public void OnMoveAsset(string assetPrevPath, string assetNewPath)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return;
			//We need to check if this is a folder because if it is then its the contents rather than the folder itself that we want
			if (!Path.HasExtension(assetPrevPath))
			{
				var thisFolderContentsUGUIDS = AssetDatabase.FindAssets("t:Object", new string[1] { assetPrevPath });
				for (int i = 0; i < thisFolderContentsUGUIDS.Length; i++)
				{
					var prevPath = AssetDatabase.GUIDToAssetPath(thisFolderContentsUGUIDS[i]);
					var newPath = prevPath.Replace(assetPrevPath, assetNewPath);
					var extension = Path.GetExtension(prevPath);
					//skip some known extensions we will never index ("" means its a folder)
					bool extensionOk = (extension == ".meta" || extension == ".cs" || extension == "" || extension == ".js") ? false : true;
					if (extensionOk)
					{
						if(!AMPMovedAssetsContains(prevPath, newPath))
							AMPMovedAssets.Add(new AMPMovedAsset(prevPath, newPath));
					}
				}
			}
			else
			{
				if (!AMPMovedAssetsContains(assetPrevPath, assetNewPath))
					AMPMovedAssets.Add(new AMPMovedAsset(assetPrevPath, assetNewPath));
			}
			if (AMPMovedAssets.Count > 0)
			{
				EditorApplication.update -= DoMoveAsset;
				EditorApplication.update += DoMoveAsset;
			}
		}

		/// <summary>
		/// Callback for UMAAssetModificationProcessor that is triggered when Assets or folders are deleted in the project. 
		/// Assigns paths to the AMPDeletedAssets list and sets up DoDeleteAsset to process the list when the assetModificationProcessor is finished
		/// </summary>
		public void OnDeleteAsset(string assetToDelete)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return;

			//When a folder is deleted only the folder path is sent here so we need to get all the assets from it as well
			if (!Path.HasExtension(assetToDelete))
			{
				var thisFolderContentsUGUIDS = AssetDatabase.FindAssets("t:Object", new string[1] { assetToDelete });
				for (int i = 0; i < thisFolderContentsUGUIDS.Length; i++)
				{
					var delPath = AssetDatabase.GUIDToAssetPath(thisFolderContentsUGUIDS[i]);
					if(!AMPDeletedAssets.Contains(delPath))
						AMPDeletedAssets.Add(delPath);
				}
			}
			else
			{
				if(!AMPDeletedAssets.Contains(assetToDelete))
					AMPDeletedAssets.Add(assetToDelete);
			}
			if (AMPDeletedAssets.Count > 0)
			{
				EditorApplication.update -= DoDeleteAsset;
				EditorApplication.update += DoDeleteAsset;
			}
		}

		/// <summary>
		/// Callback for UMAAssetModificationProcessor that is triggered when Assets are created from the 'Create' menu in the project. 
		/// Assigns paths to the AMPCreatedAssets list and sets up DoCreateAsset to process the list when the assetModificationProcessor is finished 
		/// </summary>
		public void OnCreateAsset(string createdAsset)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return;
			var extension = Path.GetExtension(createdAsset);
			//skip some known extensions we will never index ("" means its a folder)
			bool extensionOk = (extension == ".meta" || extension == ".cs" || extension == "" || extension == ".js") ? false : true;
			if (createdAsset.IndexOf("ProjectSettings.asset") == -1 && AMPCreatedAssets.Contains(createdAsset) == false && extensionOk)
				AMPCreatedAssets.Add(createdAsset);
			if (AMPCreatedAssets.Count > 0)
			{
				EditorApplication.update -= DoCreateAsset;
				EditorApplication.update += DoCreateAsset;
			}
		}

		/// <summary>
		/// Callback for UMAAssetModificationProcessor that is triggered when Assets are edited and saved in the project. 
		/// Assigns paths to the AMPSavedAssets list and sets up DoSaveAssets to process the list when the assetModificationProcessor is finished 
		/// </summary>
		public void OnSaveAssets(string[] assetsToSave)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return;
			for (int i = 0; i < assetsToSave.Length; i++)
			{
				if (assetsToSave[i].IndexOf("ProjectSettings.asset") == -1 && AMPSavedAssets.Contains(assetsToSave[i]) == false)
				{
					AMPSavedAssets.Add(assetsToSave[i]);
				}
			}
			if (AMPSavedAssets.Count > 0)
			{
				EditorApplication.update -= DoSaveAssets;
				EditorApplication.update += DoSaveAssets;
			}
		}

		/// <summary>
		/// Callback for UMAAssetPostProcessor that is triggered when assets are imported OR when an asset is Duplucated using "Edit/Duplicate".
		/// Adds the assets to the AMPCreatedAssets list and and sets up DoCreateAsset to process the list when the assetPostProcessor is finished
		/// </summary>
		public void OnEditorDuplicatedAsset(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return;
			//we need to loop this- Project settings is still getting through
			for (int i = 0; i < importedAssets.Length; i++)
			{
				var extension = Path.GetExtension(importedAssets[i]);
				//skip some known extensions we will never index ("" means its a folder)
				bool extensionOk = (extension == ".meta" || extension == ".cs" || extension == "" || extension == ".js") ? false : true;
				if (importedAssets[i].IndexOf("ProjectSettings.asset") == -1 && AMPCreatedAssets.Contains(importedAssets[i]) == false && extensionOk)
				{
					AMPCreatedAssets.Add(importedAssets[i]);
				}
			}
			if (AMPCreatedAssets.Count > 0)
			{
				EditorApplication.update -= DoCreateAsset;
				EditorApplication.update += DoCreateAsset;
			}
		}
		#endregion

		#region ASSET MODIFICATION PROCESSOR METHODS

		/// <summary>
		/// Processers the AMPMovedAssets after AssetModificationProcessor has finished moving assets
		/// </summary>
		private void DoMoveAsset()
		{
			EditorApplication.update -= DoMoveAsset;
			//assets have moved.
			foreach (AMPMovedAsset path in AMPMovedAssets)
			{
				//if the asset is NOT slot/overlay/race its asset hash may have also changed (because its previous hash was based on its asset name and that might be what has changed)
				//But to determine that we need to load the asset, because we dont know if its slot/overlay/race until we do
				var thisAsset = AssetDatabase.LoadMainAssetAtPath(path.newPath);
				if (thisAsset)
				{
					if (!IsAssetATrackedType(thisAsset))
						continue;

					int thisAssetHash = -1;
					string thisAssetName = "";
					GetAssetHashAndNames(thisAsset, ref thisAssetHash, ref thisAssetName);
					//find the asset in the index using the prev path
					var fullIndexData = _fullIndex.GetEntryFromPath(path.prevPath);
					if (fullIndexData != null)
					{
						//If the asset has been moved INTO an assetbundle, we need to remove it from the FullIndex and the Build Index (if it was in there)
						//and add the new path to the assetBundleIndex
						if (InAssetBundle(path.newPath, thisAsset.name))
						{
							_fullIndex.RemovePath(path.prevPath);
							if (_buildIndex.Contains(path.prevPath))
								_buildIndex.RemovePath(path.prevPath);
							_assetBundleIndex.AddPath(thisAsset, thisAssetHash, thisAssetName);
						}
						else
						{
							//set the path for the asset to be the new path
							//and update the name/hash stuff
							fullIndexData.fullPath = path.newPath;
							fullIndexData.name = thisAssetName;
							fullIndexData.nameHash = thisAssetHash;
							//THEN check if the asset was in the BuildList
							var buildIndexData = _buildIndex.GetEntryFromPath(path.prevPath);
							if (buildIndexData != null)
							{
								buildIndexData.fullPath = fullIndexData.fullPath;
								buildIndexData.name = fullIndexData.name;
								buildIndexData.nameHash = fullIndexData.nameHash;
							}
							//Then check if it was moved in or out of Resources
							if (InResources(path.prevPath) && !InResources(path.newPath))
							{
								//remove from build index
								MakeAssetNotLive(fullIndexData, thisAsset.GetType().ToString());
							}
							else if (!InResources(path.prevPath) && InResources(path.newPath))
							{
								//add to build index
								MakeAssetLive(fullIndexData, thisAsset.GetType().ToString());
							}
						}
					}
					else
					{
						//if fullIndexData is null it will mean the asset was previously in an asset bundle but now is not
						if (!InAssetBundle(path.newPath, thisAsset.name))
						{
							_fullIndex.AddPath(thisAsset, thisAssetHash, thisAssetName);
							//Automatically add anything in a Resources folder because it will already be included in the game
							if (InResources(path.newPath))
								_buildIndex.AddPath(thisAsset, thisAssetHash, thisAssetName, true);
						}
					}
					var assetBundleIndexData = _assetBundleIndex.GetEntryFromPath(path.prevPath);
					if (assetBundleIndexData != null)
					{
						if (InAssetBundle(path.newPath, thisAsset.name))
						{
							//if the asset is still in an assetBundle update its path
							assetBundleIndexData.fullPath = path.newPath;
							assetBundleIndexData.name = thisAssetName;
							assetBundleIndexData.nameHash = thisAssetHash;
						}
						else
						{
							//otherwise remove it from the assetBundle list
							_assetBundleIndex.RemovePath(assetBundleIndexData.fullPath);
						}
					}
				}
			}
			AMPMovedAssets.Clear();
			SortIndexes();
			CheckAndUpdateWindow();
		}

		/// <summary>
		/// Processers the AMPDeletedAssets after AssetModificationProcessor has finished moving assets 
		/// </summary>
		private void DoDeleteAsset()
		{
			EditorApplication.update -= DoDeleteAsset;
			//Remove the asset from all indexes
			foreach (string path in AMPDeletedAssets)
			{
				_fullIndex.RemovePath(path);
				_buildIndex.RemovePath(path);
				_assetBundleIndex.RemovePath(path);
			}
			AMPDeletedAssets.Clear();
			SortIndexes();
			CheckAndUpdateWindow();
		}

		/// <summary>
		/// Processers the AMPCreatedAssets after AssetModificationProcessor has finished moving assets or AssetPostProcessor has finished creating or importing
		/// </summary>
		private void DoCreateAsset()
		{
			EditorApplication.update -= DoCreateAsset;
			foreach (string path in AMPCreatedAssets)
			{
				var thisAsset = AssetDatabase.LoadMainAssetAtPath(path);
				if (!IsAssetATrackedType(thisAsset))
					continue;
				//then add it
				int thisAssetHash = UMAUtils.StringToHash(thisAsset.name);
				string thisAssetName = thisAsset.name;
				GetAssetHashAndNames(thisAsset, ref thisAssetHash, ref thisAssetName);
				if (InAssetBundle(path, thisAsset.name))
				{
					_assetBundleIndex.AddPath(thisAsset, thisAssetHash, thisAssetName);
				}
				else
				{
					_fullIndex.AddPath(thisAsset, thisAssetHash, thisAssetName);
					//Automatically add anything in a Resources folder because it will already be included in the game
					if (InResources(path))
						_buildIndex.AddPath(thisAsset, thisAssetHash, thisAssetName, true);
				}
			}
			AMPCreatedAssets.Clear();
			SortIndexes();
			CheckAndUpdateWindow();
		}

		/// <summary>
		/// Processers the AMPSavedAssets after AssetModificationProcessor has finished saving assets. 
		/// This also updates the Asset Names and hashes refrenced in the index (in the case of slot/overlay/race these are the slotname/overlayname/racename) 
		/// </summary>
		private void DoSaveAssets()
		{
			EditorApplication.update -= DoSaveAssets;
			//assets were saved
			foreach (string path in AMPSavedAssets)
			{
				if (path.IndexOf(".meta") > -1)
					continue;
				var thisAsset = AssetDatabase.LoadMainAssetAtPath(path);
				int thisAssetHash = -1;
				string thisAssetName = "";
				if (thisAsset)
				{
					GetAssetHashAndNames(thisAsset, ref thisAssetHash, ref thisAssetName);
					var fullIndexData = _fullIndex.GetEntryFromPath(path);
					if (fullIndexData != null)
					{
						fullIndexData.name = thisAssetName;
						fullIndexData.nameHash = thisAssetHash;
						//it will only be in the build index if its also in Full Index
						var buildIndexData = _buildIndex.GetEntryFromPath(path);
						if (buildIndexData != null)
						{
							buildIndexData.name = thisAssetName;
							buildIndexData.nameHash = thisAssetHash;
						}
					}
					var assetBundleIndexData = _assetBundleIndex.GetEntryFromPath(path);
					if (assetBundleIndexData != null)
					{
						assetBundleIndexData.name = thisAssetName;
						assetBundleIndexData.nameHash = thisAssetHash;
					}
				}
			}
			AMPSavedAssets.Clear();
			SortIndexes();
			CheckAndUpdateWindow();
		}
		#endregion

		#region ADD/REMOVE ASSETS FROM INDEXES METHODS

		/// <summary>
		/// Called by the inspector to forcefully refresh the Indexes 
		/// </summary>
		/// <param name="clearBuildIndex">If true clears the build index as well (only assets in Resources will be live after this)</param>
		public void ClearAndReIndex(bool clearBuildIndex = false)
		{
			_fullIndex = new UMAAssetIndexData();
			if (clearBuildIndex)
				_buildIndex = new UMAAssetIndexData();
			_assetBundleIndex = new UMAAssetIndexData();
			GenerateLists();
		}

		/// <summary>
		/// Called onEnable to generate the initial Lists
		/// </summary>
		private void GetAllAvailableUMAAssets()
		{
			AddTypesToIndex(typesToIndex);
		}

		/// <summary>
		/// Called by the inspector when the Types To Index list is modified. 
		/// Finds the assets for newly added types removes the assets for any types that are no longer tracked
		/// </summary>
		/// <param name="newTypesToIndex"></param>
		public void UpdateIndexedTypes(List<string> newTypesToIndex)
		{
			List<string> typesToAdd = new List<string>();
			List<string> typesToRemove = new List<string>();
			//add types that are not there
			for (int i = 0; i < newTypesToIndex.Count; i++)
			{
				if (!typesToIndex.Contains(newTypesToIndex[i]))
					typesToAdd.Add(newTypesToIndex[i]);
			}
			//remove types that are no longer there
			for (int i = 0; i < typesToIndex.Count; i++)
			{
				if (!newTypesToIndex.Contains(typesToIndex[i]))
					typesToRemove.Add(typesToIndex[i]);
			}
			if (typesToAdd.Count > 0)
			{
				AddTypesToIndex(typesToAdd);
			}
			if (typesToRemove.Count > 0)
			{
				RemoveTypesFromIndex(typesToRemove);
			}
			typesToIndex = new List<string>(newTypesToIndex);
		}

		/// <summary>
		/// Adds assets for the given types to the indexes
		/// </summary>
		public void AddTypesToIndex(List<string> typesToAdd)
		{
			for (int i = 0; i < typesToAdd.Count; i++)
			{
				//with this search when it is t:UMATextRecipe it DOES load Child classes too
				var typeGUIDs = AssetDatabase.FindAssets("t:" + typesToAdd[i]);
				for (int tid = 0; tid < typeGUIDs.Length; tid++)
				{
					UnityEngine.Object thisAsset = null;
					var thisPath = AssetDatabase.GUIDToAssetPath(typeGUIDs[tid]);
					thisAsset = AssetDatabase.LoadAssetAtPath(thisPath, Type.GetType(typesToAdd[i], false, true));
					//if we didn't get anything its probably because Type.GetType didn't return anything because it wants the assembly qualified name
					if (thisAsset == null)
					{
						var typeToFind = GetTypeByName(typesToAdd[i]);
						if (typeToFind != null)
							thisAsset = AssetDatabase.LoadAssetAtPath(thisPath, typeToFind);
						else
							Debug.LogWarning("[UMAAssetIndex] Could not determine the System Type for the given type name " + typesToIndex[i]);
					}
					if (thisAsset)
					{
						int thisAssetHash = UMAUtils.StringToHash(thisAsset.name);
						string thisAssetName = thisAsset.name;
						if (thisAsset is SlotDataAsset)
						{
							thisAssetName = (thisAsset as SlotDataAsset).slotName;
							thisAssetHash = (thisAsset as SlotDataAsset).nameHash;
						}
						else if (thisAsset is OverlayDataAsset)
						{
							thisAssetName = (thisAsset as OverlayDataAsset).overlayName;
							thisAssetHash = (thisAsset as OverlayDataAsset).nameHash;
						}
						else if (thisAsset is RaceData)
						{
							thisAssetName = (thisAsset as RaceData).raceName;
							thisAssetHash = UMAUtils.StringToHash((thisAsset as RaceData).raceName);
						}
						//
						if (InAssetBundle(thisPath, thisAsset.name))
						{
							_assetBundleIndex.AddPath(thisAsset, thisAssetHash, thisAssetName);
						}
						else
						{
							_fullIndex.AddPath(thisAsset, thisAssetHash, thisAssetName);
							//Automatically add anything in a Resources folder because it will already be included in the game
							if (InResources(thisPath))
								_buildIndex.AddPath(thisAsset, thisAssetHash, thisAssetName, true);
						}
					}
				}
			}
			SortIndexes();
			CheckAndUpdateWindow();
			Resources.UnloadUnusedAssets();
		}

		/// <summary>
		/// Removes Assets of the given types from the indexes
		/// </summary>
		public void RemoveTypesFromIndex(List<string> typesToRemove)
		{
			for (int i = 0; i < typesToRemove.Count; i++)
			{
				Type thisType = Type.GetType(typesToRemove[i]);
				if (thisType == null)
				{
					thisType = GetTypeByName(typesToRemove[i]);
				}
				if (thisType == null)
				{
					Debug.LogWarning("[UMAAssetIndex] could not find the requested type using type name " + typesToRemove[i] + ". Did you enter the name correctly?");
					continue;
				}
				if (_fullIndex.ContainsType(thisType.ToString()))
				{
					_fullIndex.RemoveType(thisType);
				}
				if (_buildIndex.ContainsType(thisType.ToString()))
				{
					_buildIndex.RemoveType(thisType);
				}
				if (_assetBundleIndex.ContainsType(thisType.ToString()))
				{
					_assetBundleIndex.RemoveType(thisType);
				}
			}
			SortIndexes();
			CheckAndUpdateWindow();
			Resources.UnloadUnusedAssets();
		}

		/// <summary>
		/// Adds the given indexed item to the 'BuildIndex' by finding its referenced asset and adding an entry to the Build index that references this asset.
		/// </summary>
		/// <param name="fullIndexData"></param>
		/// <param name="assetType"></param>
		public void MakeAssetLive(UMAAssetIndexData.IndexData fullIndexData, string assetType)
		{
			if (fullIndexData != null)
			{
				UnityEngine.Object thisAsset = null;
				thisAsset = AssetDatabase.LoadAssetAtPath(fullIndexData.fullPath, Type.GetType(assetType, false, true));
				//if we didn't get anything its probably because Type.GetType didn't return anything because it wants the assembly qualified name
				if (thisAsset == null)
				{
					var typeToFind = GetTypeByName(assetType);
					if (typeToFind != null)
						thisAsset = AssetDatabase.LoadAssetAtPath(fullIndexData.fullPath, typeToFind);
					else
						Debug.LogWarning("[UMAAssetIndex] Could not determine the System Type for the given type name " + assetType);
				}
				if (thisAsset)
				{
					int thisAssetHash = UMAUtils.StringToHash(thisAsset.name);
					string thisAssetName = thisAsset.name;
					if (thisAsset is SlotDataAsset)
					{
						thisAssetName = (thisAsset as SlotDataAsset).slotName;
						thisAssetHash = (thisAsset as SlotDataAsset).nameHash;
					}
					else if (thisAsset is OverlayDataAsset)
					{
						thisAssetName = (thisAsset as OverlayDataAsset).overlayName;
						thisAssetHash = (thisAsset as OverlayDataAsset).nameHash;
					}
					else if (thisAsset is RaceData)
					{
						thisAssetName = (thisAsset as RaceData).raceName;
						thisAssetHash = UMAUtils.StringToHash((thisAsset as RaceData).raceName);
					}
					//
					_buildIndex.AddPath(thisAsset, thisAssetHash, thisAssetName, true);
				}
			}
			SortIndexes();
			CheckAndUpdateWindow();
		}

		/// <summary>
		/// Removes a previously indexed asset from the BuildIndex so its asset is no longer referenced
		/// </summary>
		/// <param name="fullIndexData"></param>
		/// <param name="assetType"></param>
		public void MakeAssetNotLive(UMAAssetIndexData.IndexData fullIndexData, string assetType)
		{
			_buildIndex.RemovePath(fullIndexData.fullPath);
			SortIndexes();
			CheckAndUpdateWindow();
		}

		#endregion

		#region INDEX MODIFICATION HELPER METHODS

		private bool AMPMovedAssetsContains(string ppath, string npath)
		{
			for(int i = 0; i < AMPMovedAssets.Count; i++)
			{
				if (AMPMovedAssets[i].prevPath == ppath && AMPMovedAssets[i].newPath == npath)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Checks whether the given Unity Object is one of the types we are tracking
		/// </summary>
		private bool IsAssetATrackedType(UnityEngine.Object assetToTest)
		{
			if (assetToTest == null)
				return false;
			bool isTypeTracked = false;
			for (int sti = 0; sti < typesToIndex.Count; sti++)
			{
				Type foundType = Type.GetType(typesToIndex[sti], false, true);
				if (foundType == null)
				{
					foundType = GetTypeByName(typesToIndex[sti]);
					if (foundType == null)
						Debug.LogWarning("Type not found in types to index for " + typesToIndex[sti]);
				}
				//Debug.Log("foundType was " + foundType.ToString() + " and assetType was " + assetToTest.GetType().ToString());
				if (foundType != null)
				{
					//this works but includes child types- which we dont want
					//But I dont think it matters because the next step sorts it out
					if (assetToTest.GetType().IsAssignableFrom(foundType) || assetToTest.GetType() == foundType)
					{
						isTypeTracked = true;
						break;
					}
				}
			}
			return isTypeTracked;
		}

		/// <summary>
		/// Sorts all the indexes by folder name
		/// </summary>
		private void SortIndexes()
		{
			for (int ti = 0; ti < _fullIndex.data.Length; ti++)
				Array.Sort(_fullIndex.data[ti].typeIndex, CompareByFolderName);
			for (int ti = 0; ti < _buildIndex.data.Length; ti++)
				Array.Sort(_buildIndex.data[ti].typeIndex, CompareByFolderName);
			for (int ti = 0; ti < _assetBundleIndex.data.Length; ti++)
				Array.Sort(_assetBundleIndex.data[ti].typeIndex, CompareByFolderName);
		}

		/// <summary>
		/// Comparer for SortIndexes above
		/// </summary>
		private int CompareByFolderName(UMAAssetIndexData.IndexData obj1, UMAAssetIndexData.IndexData obj2)
		{
			if (obj1 == null)
			{
				if (obj2 == null) return 0;
				else return -1;
			}
			else
			{
				if (obj2 == null)
				{
					return 1;
				}
				else
				{
					string folder1 = System.IO.Path.GetDirectoryName(obj1.fullPath);
					string folder2 = System.IO.Path.GetDirectoryName(obj2.fullPath);
					int folderCompare = String.Compare(folder1, folder2);
					if (folderCompare != 0) return folderCompare;
					string file1 = System.IO.Path.GetFileName(obj1.fullPath);
					string file2 = System.IO.Path.GetFileName(obj2.fullPath);
					int fileCompare = String.Compare(file1, file2);
					return fileCompare;
				}
			}
		}

		public void CheckAndUpdateWindow()
		{
			if (windowInstance != null)
				windowInstance.Repaint();
		}

		private void GetAssetHashAndNames(UnityEngine.Object obj, ref int assetHash, ref string assetName)
		{
			assetName = obj.name;
			assetHash = UMAUtils.StringToHash(obj.name);
			if (obj is SlotDataAsset)
			{
				assetName = (obj as SlotDataAsset).slotName;
				assetHash = (obj as SlotDataAsset).nameHash;
			}
			else if (obj is OverlayDataAsset)
			{
				assetName = (obj as OverlayDataAsset).overlayName;
				assetHash = (obj as OverlayDataAsset).nameHash;
			}
			else if (obj is RaceData)
			{
				assetName = (obj as RaceData).raceName;
				assetHash = UMAUtils.StringToHash((obj as RaceData).raceName);
			}
		}

		private Type GetTypeByName(string name)
		{
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				foreach (Type type in assembly.GetTypes())
				{
					if (type.Name == name)
						return type;
				}
			}

			return null;
		}

		private bool InResources(string path)
		{
			return (path.IndexOf("/Resources/") > -1 || path.IndexOf("\\Resources\\") > -1);
		}

		private bool InAssetBundle(string path, string assetName)
		{
			string[] assetBundleNames = AssetDatabase.GetAllAssetBundleNames();
			List<string> pathsInBundle;
			for (int i = 0; i < assetBundleNames.Length; i++)
			{
				pathsInBundle = new List<string>(AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(assetBundleNames[i], assetName));
				if (pathsInBundle.Contains(path))
					return true;
			}
			return false;
		}

		#endregion

#endif

		#region INDEX FIND ASSETS METHODS

		/// <summary>
		/// Returns a List of the given Type containing all the assets that have been made Live in the BuildIndex, the list is empty if not assets of the requested type are found.
		/// </summary>
		public List<T> LoadAllAssetsOfType<T>() where T : UnityEngine.Object
		{
				return _buildIndex.GetAll<T>();
		}
		/// <summary>
		/// Returns the asset of the given Type and uma name if it has been added to the BuildIndex, null if not found
		/// </summary>
		public UnityEngine.Object LoadAsset<T>(string umaName) where T : UnityEngine.Object
		{
				return _buildIndex.Get<T>(umaName);
		}
		/// <summary>
		/// Returns the asset of the given Type and uma nameHash if it has been added to the BuildIndex, null if not found 
		/// </summary>
		public UnityEngine.Object LoadAsset<T>(int umaNameHash) where T : UnityEngine.Object
		{
			return _buildIndex.Get<T>(umaNameHash);
		}
		/// <summary>
		/// Loads an asset at the given path from the BuildIndex and returns it as UnityEngine.Object. Null if no object can be found.
		/// </summary>
		public UnityEngine.Object LoadAssetAtPath(string path)
		{
			UnityEngine.Object foundObj = null;
			for(int i = 0; i < _buildIndex.data.Length; i++)
			{
				for(int ti = 0; ti < _buildIndex.data[i].typeIndex.Length; ti++)
				{
					if (_buildIndex.data[i].typeIndex[ti].fullPath == path)
						return _buildIndex.data[i].typeIndex[ti].fileReference;
                }
			}
			return foundObj;
        }

		#endregion
	}
}
