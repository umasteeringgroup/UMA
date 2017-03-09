using UnityEngine;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.IO;

namespace UMA
{
	[System.Serializable]
	public class UMAAssetIndexData
	{
		public TypeIndex[] data = new TypeIndex[0];
		[SerializeField]
		private List<string> _currentPaths = new List<string>();//never ref'd by index so order doesnt matter

		public List<string> CurrentPaths
		{
			get
			{
				if (_currentPaths.Count == 0 && data.Length > 0)
				{
					GenerateCurrentPaths();
				}
				return _currentPaths;
			}
			set
			{
				_currentPaths = value;
			}

		}

		void GenerateCurrentPaths()
		{
			for (int i = 0; i < data.Length; i++)
			{
				for (int ii = 0; ii < data[i].typeIndex.Length; ii++)
				{
					if (!_currentPaths.Contains(data[i].typeIndex[ii].fullPath))
						_currentPaths.Add(data[i].typeIndex[ii].fullPath);
				}
			}
		}

		public UMAAssetIndexData Clone(UMAAssetIndexData dataToClone, bool cloneIndexedNames, bool cloneIndexedHashes, bool cloneIndexedPaths, bool cloneIndexedFileRefs)
		{
			var thisClone = new UMAAssetIndexData();
			for (int i = 0; i < dataToClone.data.Length; i++)
			{
				thisClone.data[i] = new TypeIndex(dataToClone.data[i], cloneIndexedNames, cloneIndexedHashes, cloneIndexedPaths, cloneIndexedFileRefs);
				for (int ii = 0; ii < thisClone.data[i].typeIndex.Length; ii++)
				{
					if (!_currentPaths.Contains(data[i].typeIndex[ii].fullPath))
						_currentPaths.Add(data[i].typeIndex[ii].fullPath);
				}
			}
			return thisClone;
		}
		/// <summary>
		/// Returns the total number of indexed assets in all the indexed Types
		/// </summary>
		public int Count()
		{
			int totalCount = 0;
			for (int i = 0; i < data.Length; i++)
			{
				totalCount += data[i].Count();
			}
			return totalCount;
		}
		/// <summary>
		/// Is the given path indexed in any of the indexed types
		/// </summary>
		public bool Contains(string pathToCheck)
		{
			return _currentPaths.Contains(pathToCheck);
		}

		/// <summary>
		/// Check if the TypeIndex is currently indexing the given type
		/// </summary>
		/// <param name="typeToCheck"></param>
		public bool ContainsType(string typeToCheck)
		{
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type == typeToCheck)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Returns the total number of assets of the given type that are indexed
		/// </summary>
		/// <param name="typeToCount"></param>
		/// <returns></returns>
		public int CountType(string typeToCount)
		{
			int countedAssets = 0;
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type == typeToCount)
					return data[i].typeIndex.Length;
			}
			return countedAssets;
		}

#if UNITY_EDITOR
		/// <summary>
		/// Adds an Asset to the index.
		/// </summary>
		/// <param name="obj"></param>
		/// <param name="objName"></param>
		public bool AddPath(UnityEngine.Object obj, string objName)
		{
			return AddPath(obj, UMAUtils.StringToHash(objName), objName);
		}
		/// <summary>
		/// Adds an Asset to the index. If addObject is true a refrence to the object to index is also added
		/// </summary>
		/// <param name="obj"></param>
		/// <param name="objNameHash"></param>
		/// <param name="objName">The name of the asset. If this is a SlotDataAsset/OverlayDataAsset/RaceData asset this should be the slotName/overlayName/raceName. If no name is given the asset name is used.</param>
		/// <param name="addObject">If true a reference to the object is added to the created indexItem. This will mean the asset gets included in the build.</param>
		/// <param name="compareFullPaths">If true the full path for the asset will be taken into account aswell. i.e. if an asset with the same slot/overlay/race name exists this asset will still be added even though its a duplicate asset</param>
		public bool AddPath(UnityEngine.Object obj, int objNameHash, string objName = "", bool addObject = false, bool compareFullPaths = true)
		{
			bool pathAdded = false;
			if (obj == null)
			{
				return pathAdded;
			}
			var objFullPath = AssetDatabase.GetAssetPath(obj);

			//deal with RuntimeAnimatorController Type craziness
			var objTypeString = obj.GetType().ToString();
			if (objTypeString == "UnityEditor.Animations.AnimatorController")
			{
				objTypeString = "UnityEngine.RuntimeAnimatorController";
			}
			bool hadType = false;
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type == objTypeString)
				{
					if (data[i].Add(objNameHash, objFullPath, objName, addObject ? obj : null, compareFullPaths))
					{
						pathAdded = true;
						if (!_currentPaths.Contains(objFullPath))
						{
							_currentPaths.Add(objFullPath);
						}
					}
					hadType = true;
				}
			}
			if (!hadType)
			{
				var list = new TypeIndex[data.Length + 1];
				Array.Copy(data, list, data.Length);
				list[data.Length] = new TypeIndex(objTypeString, objNameHash, objFullPath, objName, addObject ? obj : null);
				if (!_currentPaths.Contains(objFullPath))
				{
					_currentPaths.Add(objFullPath);
				}
				pathAdded = true;
				data = list;
			}
			return pathAdded;
		}
		/// <summary>
		/// Removes any entries in the index that have 0 or -1 as their hash or a null or empty string as their fullPath
		/// </summary>
		public void RemoveInvalidEntries()
		{
			for (int i = 0; i < data.Length; i++)
			{
				data[i].Remove(0);
				data[i].Remove(-1);
				data[i].Remove("");
			}
			CleanEmptyTypes();
		}
		/// <summary>
		/// Removes an Indexes asset at the given full path from the index
		/// </summary>
		/// <param name="path"></param>
		public bool RemovePath(string path)
		{
			if (String.IsNullOrEmpty(path))
			{
				return false;
			}
			var removed = false;
			var fileRefPath = GetResourcesPath(path, true);
			for (int i = 0; i < data.Length; i++)
			{
				if (removed == true)
					break;
				removed = false;
				for (int ii = 0; ii < data[i].typeIndex.Length; ii++)
				{
					if (fileRefPath != "")
					{
						if (data[i].typeIndex[ii].fileRefPath == fileRefPath)
						{
							removed = data[i].Remove(path);
							if (removed)
							{
								if (_currentPaths.Contains(data[i].typeIndex[ii].fullPath))
									_currentPaths.Remove(data[i].typeIndex[ii].fullPath);
								break;
							}
						}
					}
					if (data[i].typeIndex[ii].fullPath == path)
					{
						removed = data[i].Remove(path);
						if (removed)
						{
							if (_currentPaths.Contains(path))
								_currentPaths.Remove(path);
							break;
						}
					}
				}
			}
			CleanEmptyTypes();
			return removed;
		}

		/// <summary>
		/// clears all assets of the given type from the index and removes the type.
		/// </summary>
		/// <param name="type"></param>
		public void RemoveType(System.Type type)
		{
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type == type.ToString())
				{
					for (int ti = 0; ti < data[i].typeIndex.Length; ti++)
					{
						if (_currentPaths.Contains(data[i].typeIndex[ti].fullPath))
							_currentPaths.Remove(data[i].typeIndex[ti].fullPath);
						data[i].Remove(data[i].typeIndex[ti].fullPath);
					}
					RemoveTypeFromIndex(type);
				}
			}
		}

		private void CleanEmptyTypes()
		{
			bool needsCleaning = false;
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].typeIndex.Length == 0)
				{
					needsCleaning = true;
				}
			}
			if (!needsCleaning)
				return;
			List<TypeIndex> cleanedTypeIndexes = new List<TypeIndex>();
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].typeIndex.Length > 0)
				{
					cleanedTypeIndexes.Add(data[i]);
				}
			}
			data = cleanedTypeIndexes.ToArray();
		}

		private void RemoveTypeFromIndex(System.Type type)
		{
			var list = new TypeIndex[data.Length - 1];
			int listi = 0;
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type != type.ToString())
				{
					list[listi] = data[i];
					listi++;
				}
			}
			data = list;
		}

#endif

		public UnityEngine.Object Get(int nameHash, string type, string[] foldersToSearch = null)
		{
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type == type)
				{
					return data[i].Get(nameHash, foldersToSearch);
				}
			}
			return null;
		}

		public T Get<T>(int umaNameHash, string[] foldersToSearch = null) where T : UnityEngine.Object
		{
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type == typeof(T).ToString())
				{
					return data[i].Get<T>(umaNameHash, foldersToSearch);
				}
			}
			return null;
		}

		public UnityEngine.Object Get(string name, string type, string[] foldersToSearch = null)
		{
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type == type)
				{
					return data[i].Get(name, foldersToSearch);
				}
			}
			return null;
		}
		public T Get<T>(string umaName, string[] foldersToSearch = null) where T : UnityEngine.Object
		{
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type == typeof(T).ToString())
				{
					return data[i].Get<T>(umaName, foldersToSearch);
				}
			}
			return null;
		}

		public List<T> GetAll<T>(string[] foldersToSearch = null) where T : UnityEngine.Object
		{
			List<T> allAssets = new List<T>();
			for (int ti = 0; ti < data.Length; ti++)
			{
				if (data[ti].type == typeof(T).ToString())
				{
					for (int i = 0; i < data[ti].typeIndex.Length; i++)
					{
						if (WasEntryInFolders(data[ti].typeIndex[i], foldersToSearch))
						{
							if (!String.IsNullOrEmpty(data[ti].typeIndex[i].fileRefPath))
							{
								allAssets.Add(data[ti].typeIndex[i].TheFileReference as T);
							}
							else
							{
								var resourcesPath = GetResourcesPath(data[ti].typeIndex[i].fullPath, true);
								if (String.IsNullOrEmpty(resourcesPath))
								{
									//Debug.LogWarning("No Resources path or fileReference was found for Index entry at path " + data[ti].typeIndex[i].fullPath);
									continue;
								}
								else
								{
									var thisAsset = Resources.Load<T>(resourcesPath);
									if (thisAsset == null)
									{
										Debug.Log("Resources could not load an asset of type " + typeof(T).ToString() + " from path " + resourcesPath);
									}
									else
									{
										allAssets.Add(thisAsset as T);
									}
								}
							}
						}
					}
				}
			}
			return allAssets;
		}

		/// <summary>
		/// Gets a resources path for use with Resources.Load froma given full path. Returns an empty string if no Resources relative path was found
		/// </summary>
		/// <param name="fullPath">The full path relative to and including the 'Assets/' folder</param>
		/// <returns></returns>
		public static string GetResourcesPath(string fullPath, bool confirmResources = false)
		{
			//fullPath may have slashes going the wrong way
			fullPath = fullPath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string resourcesPath = "";
			if (confirmResources && fullPath.IndexOf("Resources/") == -1)
				return resourcesPath;
			var resourcesPathArray = fullPath.Split(new string[] { "Resources/" }, StringSplitOptions.RemoveEmptyEntries);
			if (resourcesPathArray.Length != 2)
			{
				//Debug.LogWarning("Full path given did not contain a Resources path for "+fullPath);
				return "";
			}
			var extension = Path.GetExtension(resourcesPathArray[1]);
			resourcesPath = resourcesPathArray[1];
			if (extension != "")
			{
				resourcesPath = resourcesPath.Replace(extension, "");
			}
			return resourcesPath;
		}

		protected static bool WasEntryInFolders(IndexData data, string[] foldersToSearch = null)
		{
			if (foldersToSearch == null)
				return true;
			if (foldersToSearch.Length == 0)
				return true;
			for (int fi = 0; fi < foldersToSearch.Length; fi++)
			{
				if (data.fullPath.IndexOf(foldersToSearch[fi]) > -1)
					return true;
			}
			return false;
		}

		public IndexData GetEntryFromNameHash(int nameHash)
		{
			for (int i = 0; i < data.Length; i++)
			{
				for (int di = 0; di < data[i].typeIndex.Length; di++)
				{
					if (data[i].typeIndex[di].nameHash == nameHash)
					{
						return data[i].typeIndex[di];
					}
				}
			}
			return null;
		}

		public IndexData GetEntryFromName(string name)
		{
			for (int i = 0; i < data.Length; i++)
			{
				for (int di = 0; di < data[i].typeIndex.Length; di++)
				{
					if (data[i].typeIndex[di].name == name)
					{
						return data[i].typeIndex[di];
					}
				}
			}
			return null;
		}

		public IndexData GetEntryFromPath(string path)
		{
			for (int i = 0; i < data.Length; i++)
			{
				for (int di = 0; di < data[i].typeIndex.Length; di++)
				{
					if (data[i].typeIndex[di].fullPath == path)
					{
						return data[i].typeIndex[di];
					}
				}
			}
			return null;
		}

		#region SPECIAL TYPES
		[System.Serializable]
		public class TypeIndex
		{
			public string type;
			public IndexData[] typeIndex = new IndexData[0];

			public TypeIndex() { }

			public TypeIndex(string _type)
			{
				type = _type;
				typeIndex = new IndexData[0];
			}

			public TypeIndex(TypeIndex typeIndexToClone, bool cloneIndexNames = true, bool cloneIndexHashes = true, bool cloneIndexPaths = true, bool cloneIndexRefs = true)
			{
				type = typeIndexToClone.type;
				typeIndex = new IndexData[typeIndexToClone.typeIndex.Length];
				for (int i = 0; i < typeIndexToClone.typeIndex.Length; i++)
				{
					typeIndex[i] = new IndexData();
					if (cloneIndexNames)
						typeIndex[i].name = typeIndexToClone.typeIndex[i].name;
					if (cloneIndexHashes)
						typeIndex[i].nameHash = typeIndexToClone.typeIndex[i].nameHash;
					if (cloneIndexPaths)
						typeIndex[i].fullPath = typeIndexToClone.typeIndex[i].fullPath;
					if (cloneIndexRefs)
						typeIndex[i].fileRefPath = typeIndexToClone.typeIndex[i].fileRefPath;
				}
			}

			public TypeIndex(string _type, int _nameHash, string _fullPath = "", string _name = "", UnityEngine.Object _obj = null)
			{
				type = _type;
				typeIndex = new IndexData[1];
				if (_obj != null)
					typeIndex[0] = new IndexData(_obj, _nameHash, _fullPath, _name);
				else
					typeIndex[0] = new IndexData(_nameHash, _fullPath, _name);
			}

			public int Count()
			{
				return typeIndex.Length;
			}

#if UNITY_EDITOR
			/// <summary>
			/// Adds an asset to the index's data if it is not there already
			/// </summary>
			/// <param name="nameHash"></param>
			/// <param name="fullPath"></param>
			/// <param name="objName"></param>
			/// <param name="obj"></param>
			/// <param name="compareFullPaths">if compareFullPaths is true any existsing indexed item is only considered to be the same 
			/// as the one being requested to add if its nameHash AND its fullpath are the same</param>
			/// <returns>True if asset was added or false if it already existed in the index</returns>
			public bool Add(int nameHash, string fullPath, string objName = "", UnityEngine.Object obj = null, bool compareFullPaths = true)
			{
				bool found = false;
				for (int i = 0; i < typeIndex.Length; i++)
				{
					//if the given namehash == this items name hash then dont add a new entry just update its fileRefrence to the obj (which can also be null)
					//if compareFullPaths is true the indexed item is only considered to be the same if its nameHash AND its fullpath are the same
					//the result if compareFullPaths is true, is, if the fullPaths are NOT the same 
					//the existing index item is NOT considered to be the same as the requested one to be added
					//and so a DUPLICATE asset with the same hash but a different path is added 
					//- this wont get loaded but is used by the UI to show the user they have a duplicate asset
					if (typeIndex[i].nameHash == nameHash && (compareFullPaths ? typeIndex[i].fullPath == fullPath : true))
					{
						typeIndex[i].TheFileReference = obj;

						found = true;
						break;
					}
				}
				if (!found)
				{
					var list = new IndexData[typeIndex.Length + 1];
					Array.Copy(typeIndex, list, typeIndex.Length);
					if (obj != null)
						list[typeIndex.Length] = new IndexData(obj, nameHash, fullPath, objName);
					else
						list[typeIndex.Length] = new IndexData(nameHash, fullPath, objName);
					typeIndex = list;
				}
				return !found;
			}

			public bool Remove(int nameHash)
			{
				if (typeIndex.Length == 0)
					return false;
				var list = new List<IndexData>();
				bool removed = false;
				for (int i = 0; i < typeIndex.Length; i++)
				{
					if (typeIndex[i].nameHash == nameHash)
					{
						typeIndex[i].TheFileReference = null;//delete the fileRefrence object and DONT add this entry to the new list
						removed = true;
					}
					else
					{
						list.Add(new IndexData(typeIndex[i].fileRefPath, typeIndex[i].nameHash, typeIndex[i].fullPath, typeIndex[i].name));
					}
				}
				typeIndex = list.ToArray();
				return removed;
			}
			/// <summary>
			/// Remove a path from the index must be the full path, not the resources path
			/// </summary>
			/// <param name="path"></param>
			public bool Remove(string path)
			{
				if (typeIndex.Length == 0)
					return false;
				var list = new List<IndexData>();
				bool removed = false;
				for (int i = 0; i < typeIndex.Length; i++)
				{
					if (typeIndex[i].fullPath == path)//delete the fileRefrence object and DONT add this entry to the new list
					{
						typeIndex[i].TheFileReference = null;
						removed = true;
					}
					else
					{
						var thisFileRefPath = typeIndex[i].fileRefPath;
						if (thisFileRefPath == null)
							thisFileRefPath = "";
						list.Add(new IndexData(thisFileRefPath, typeIndex[i].nameHash, typeIndex[i].fullPath, typeIndex[i].name));
					}
				}
				typeIndex = list.ToArray();
				return removed;
			}

#endif

			public UnityEngine.Object Get(string name, string[] foldersToSearch = null)
			{
				return Get<UnityEngine.Object>(name, foldersToSearch);
			}

			public T Get<T>(string name, string[] foldersToSearch = null) where T : UnityEngine.Object
			{
				for (int i = 0; i < typeIndex.Length; i++)
				{
					if (typeIndex[i].name == name && WasEntryInFolders(typeIndex[i], foldersToSearch))
					{
						if (!String.IsNullOrEmpty(typeIndex[i].fileRefPath))
						{
							return (typeIndex[i].TheFileReference as T);
						}
						else
						{
							var resourcesPath = GetResourcesPath(typeIndex[i].fullPath, true);
							if (String.IsNullOrEmpty(resourcesPath))
							{
								//Debug.LogWarning("No Resources path or fileReference was found for Index entry at path " + typeIndex[i].fullPath);
								return null;
							}
							else
							{
								var thisAsset = Resources.Load<T>(resourcesPath);
								if (thisAsset == null)
								{
									Debug.Log("Resources could not load an asset from path " + resourcesPath);
								}
								else
								{
									return (thisAsset as T);
								}
							}
						}
					}
				}
				return null;
			}

			public UnityEngine.Object Get(int nameHash, string[] foldersToSearch = null)
			{
				return Get<UnityEngine.Object>(nameHash, foldersToSearch);
			}
			public T Get<T>(int nameHash, string[] foldersToSearch = null) where T : UnityEngine.Object
			{
				for (int i = 0; i < typeIndex.Length; i++)
				{
					if (typeIndex[i].nameHash == nameHash && WasEntryInFolders(typeIndex[i], foldersToSearch))
					{
						if (!String.IsNullOrEmpty(typeIndex[i].fileRefPath))
						{
							return (typeIndex[i].TheFileReference as T);
						}
						else
						{
							var resourcesPath = GetResourcesPath(typeIndex[i].fullPath, true);
							if (String.IsNullOrEmpty(resourcesPath))
							{
								//Debug.LogWarning("No Resources path or fileReference was found for Index entry at path " + typeIndex[i].fullPath);
								return null;
							}
							else
							{
								var thisAsset = Resources.Load<T>(resourcesPath);
								if (thisAsset == null)
								{
									Debug.Log("Resources could not load an asset from path " + resourcesPath);
								}
								else
								{
									return (thisAsset as T);
								}
							}
						}
					}
				}
				return null;
			}
		}

		[System.Serializable]
		public class IndexData
		{
			public string name;
			public int nameHash;
			//someType of GUID we all agree on- I'd say a short GUID since this is a good balance between global uniueness and shortness
			public string fullPath; //this is the full path rather than the resources path
									//public UnityEngine.Object fileReference;
									//public UMAAssetIndexFileRef fileRefObj = null;
			public string fileRefPath = "";

			public UnityEngine.Object TheFileReference
			{
				get
				{
					if (!String.IsNullOrEmpty(fileRefPath))
					{
						var thisFileRefObj = Resources.Load<UMAAssetIndexFileRef>(fileRefPath);
						if (thisFileRefObj)
						{
							//Debug.Log("TheFileReference.get loaded file ref was " + thisFileRefObj+" object ref was "+ thisFileRefObj.objectRef);
							return thisFileRefObj.objectRef;
						}
						else
							fileRefPath = "";
					}
					/*if (fileRefObj)
					{
						return fileRefObj.objectRef;
					}*/
					return null;
				}
#if UNITY_EDITOR
				set
				{
					UMAAssetIndexFileRef thisFileRefObj = null;
					if (value != null)
					{
						if (String.IsNullOrEmpty(fileRefPath))
						{
							//Debug.Log("TheFileReference.set fileRefPath was empty. Creating...");
							var fileRefsPath = Path.Combine(UMA.FileUtils.GetInternalDataStoreFolder(false, false), "UMAAssetIndexRefs-DONOTDELETE");
							var fileRefsTypePath = Path.Combine(fileRefsPath, value.GetType().ToString().Replace(".", "_"));
							Directory.CreateDirectory(fileRefsTypePath);
							var fileRefFullPath = Path.Combine(fileRefsTypePath, value.name + "-fileRef.asset");
							fileRefPath = GetResourcesPath(fileRefFullPath);
							thisFileRefObj = UMAEditor.CustomAssetUtility.CreateAsset<UMAAssetIndexFileRef>(fileRefFullPath, false);
						}
						else
						{
							//Debug.Log("TheFileReference.set fileRefPath was NOT empty.");
							thisFileRefObj = Resources.Load<UMAAssetIndexFileRef>(fileRefPath);
						}
						if (thisFileRefObj)
						{
							//Debug.Log("TheFileReference.set set value to "+value);
							thisFileRefObj.objectRef = value;
							EditorUtility.SetDirty(thisFileRefObj);
						}
						else
							fileRefPath = "";
					}
					else
					{
						DeleteFileRefAsset();
					}
				}
#endif
			}

			public IndexData()
			{

			}
			public IndexData(int _nameHash, string _fullPath = "", string _name = "")
			{
				name = _name;
				nameHash = _nameHash;
				fullPath = _fullPath;
				fileRefPath = "";
			}
			public IndexData(string _fileRefPath, int _nameHash, string _fullPath = "", string _name = "")
			{
				name = _name;
				nameHash = _nameHash;
				fullPath = _fullPath;
				fileRefPath = String.IsNullOrEmpty(_fileRefPath) ? "" : _fileRefPath;
			}
			public IndexData(UnityEngine.Object _fileReference, int _nameHash, string _fullPath = "", string _name = "")
			{
				name = _name;
				nameHash = _nameHash;
				fullPath = _fullPath;
#if UNITY_EDITOR
				if (_fileReference != null)
				{
					CreateFileRefAsset(_fileReference);//creates the fileref asset and sets the fileRefPath
				}
				else
				{
					fileRefPath = "";
				}
#endif
			}
			/// <summary>
			/// Creates the fileRef asset and sets the fileRefPath to the path of the created asset
			/// </summary>
			/// <param name="fileToRef">The file the FileRefAsset should reference</param>
			public void CreateFileRefAsset(UnityEngine.Object fileToRef)
			{
				UMAAssetIndexFileRef thisFileRefObj = null;
				var fileRefsPath = Path.Combine(UMA.FileUtils.GetInternalDataStoreFolder(false, false), "UMAAssetIndexRefs-DONOTDELETE");
				var fileRefsTypePath = Path.Combine(fileRefsPath, fileToRef.GetType().ToString().Replace(".", "_"));
				Directory.CreateDirectory(fileRefsTypePath);
				var fileRefFullPath = Path.Combine(fileRefsTypePath, fileToRef.name + "-fileRef.asset");
				fileRefPath = GetResourcesPath(fileRefFullPath);
				thisFileRefObj = Resources.Load<UMAAssetIndexFileRef>(fileRefPath);
				if (thisFileRefObj == null)
					thisFileRefObj = UMAEditor.CustomAssetUtility.CreateAsset<UMAAssetIndexFileRef>(fileRefFullPath, false);
				//set the ref to the actual object
				thisFileRefObj.objectRef = fileToRef;
				EditorUtility.SetDirty(thisFileRefObj);
			}
			/// <summary>
			/// Deletes the fileRef asset if it exists and sets the fileRefPath to empty
			/// </summary>
			public void DeleteFileRefAsset()
			{
				UMAAssetIndexFileRef thisFileRefObj = null;
                if (!String.IsNullOrEmpty(fileRefPath))
				{
					thisFileRefObj = Resources.Load<UMAAssetIndexFileRef>(fileRefPath);
				}
				if (thisFileRefObj != null)
				{
					var fileRefObjPath = AssetDatabase.GetAssetPath(thisFileRefObj);
					ScriptableObject.DestroyImmediate(thisFileRefObj, true);
					fileRefPath = "";
					AssetDatabase.DeleteAsset(fileRefObjPath);
				}
			}

			public void UpdateIndexData(int _nameHash, string _fullPath, string _name)
			{
				nameHash = _nameHash;
				fullPath = _fullPath;
				if(_name != name)
				{
					//we need to delete the fileRef asset if there is one nad make a new one with the new name
					if(!String.IsNullOrEmpty(fileRefPath))
					{
						var thisRefAsset = Resources.Load<UMAAssetIndexFileRef>(fileRefPath);
						if(thisRefAsset != null)
						{
							var thisAssetRef = thisRefAsset.objectRef;
							if(thisAssetRef != null)
							{
								DeleteFileRefAsset();
								CreateFileRefAsset(thisAssetRef);
							}
							else
							{
								fileRefPath = "";
							}
						}
						else
						{
							fileRefPath = "";
						}
					}
					name = _name;
				}
			}

		}
		#endregion
	}
}
