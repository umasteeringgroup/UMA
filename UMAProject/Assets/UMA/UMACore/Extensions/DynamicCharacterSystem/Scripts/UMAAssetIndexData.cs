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
		public void AddPath(UnityEngine.Object obj, string objName)
		{
			AddPath(obj, UMAUtils.StringToHash(objName), objName);
		}
		/// <summary>
		/// Adds an Asset to the index. If addObject is true a refrence to the object to index is also added
		/// </summary>
		/// <param name="obj"></param>
		/// <param name="objNameHash"></param>
		/// <param name="objName">The name of the asset. If this is a SlotDataAsset/OverlayDataAsset/RaceData asset this should be the slotName/overlayName/raceName. If no name is given the asset name is used.</param>
		/// <param name="addObject">If true a reference to the object is added to the created indexItem. This will mean the asset gets included in the build.</param>
		public void AddPath(UnityEngine.Object obj, int objNameHash, string objName = "", bool addObject = false)
		{
			if (obj == null)
			{
				return;
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
					if (data[i].Add(objNameHash, objFullPath, objName, addObject ? obj : null))
					{
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
				data = list;
			}
		}
#endif
		/// <summary>
		/// Removes an Indexes asset at the given full path from the index
		/// </summary>
		/// <param name="path"></param>
		public bool RemovePath(string path)
		{
			if (String.IsNullOrEmpty(path))
				return false;
			var removed = false;
			for (int i = 0; i < data.Length; i++)
			{
				if (removed == true)
					break;
				removed = false;
				for (int ii = 0; ii < data[i].typeIndex.Length; ii++)
				{
					if (data[i].typeIndex[ii].fullPath == path)
					{
						data[i].Remove(path);//we still dont know if this actually happenned, but...
						removed = true;
						if (removed)
						{
							if (_currentPaths.Contains(path))
								_currentPaths.Remove(path);
						}
						break;
					}
				}
			}
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
					for(int ti = 0; ti < data[i].typeIndex.Length; ti++)
					{
						data[i].Remove(data[i].typeIndex[ti].fullPath);
						if (_currentPaths.Contains(data[i].typeIndex[ti].fullPath))
							_currentPaths.Remove(data[i].typeIndex[ti].fullPath);
					}
					RemoveTypeFromIndex(type);
				}
			}
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

		public T Get<T>(int umaNameHash, string[] foldersToSearch = null) where T : UnityEngine.Object
		{
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type == typeof(T).ToString())
				{
					return data[i].Get(umaNameHash, foldersToSearch) as T;
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
					return data[i].Get(umaName, foldersToSearch) as T;
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
					for(int i = 0; i < data[ti].typeIndex.Length; i++)
					{
						if (data[ti].typeIndex[i].fileReference != null)
						{
							if(WasEntryInFolders(data[ti].typeIndex[i], foldersToSearch))
								allAssets.Add(data[ti].typeIndex[i].fileReference as T);
						}
					}
				}
			}
			return allAssets;
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
		
		public IndexData GetEntryFromPath(string path)
		{
			for (int i = 0; i < data.Length; i++)
			{
				for(int di = 0; di < data[i].typeIndex.Length; di++)
				{
					if(data[i].typeIndex[di].fullPath == path)
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
			/// <summary>
			/// Adds an asset to the index's data if it is not there already
			/// </summary>
			/// <param name="nameHash"></param>
			/// <param name="fullPath"></param>
			/// <param name="objName"></param>
			/// <param name="obj"></param>
			/// <returns>True if asset was added or false if it already existed in the index</returns>
			public bool Add(int nameHash, string fullPath, string objName = "", UnityEngine.Object obj = null)
			{
				bool found = false;
				for (int i = 0; i < typeIndex.Length; i++)
				{
					if (typeIndex[i].nameHash == nameHash && typeIndex[i].fullPath == fullPath)
					{
						found = true;
						break;
					}
				}
				if (!found)
				{
					var list = new IndexData[typeIndex.Length + 1];
					Array.Copy(typeIndex, list, typeIndex.Length);//the crashing issue could be to do with this array copy- or ANY of the array copies because they are shallow
					if (obj != null)
						list[typeIndex.Length] = new IndexData(obj, nameHash, fullPath, objName);
					else
						list[typeIndex.Length] = new IndexData(nameHash, fullPath, objName);
					typeIndex = list;
				}
				return !found;
			}
			public void Remove(int nameHash)
			{
				if (typeIndex.Length == 0)
					return;
				var list = new IndexData[typeIndex.Length - 1];
				int listi = 0;
				for (int i = 0; i < typeIndex.Length; i++)
				{
					if (typeIndex[i].nameHash != nameHash)
					{
						list[listi] = new IndexData(typeIndex[i].fileReference, typeIndex[i].nameHash, typeIndex[i].fullPath, typeIndex[i].name);
						listi++;
					}
				}
				typeIndex = list;
			}
			/// <summary>
			/// Remove a path from the index must be the full path, not the resources path
			/// </summary>
			/// <param name="path"></param>
			public void Remove(string path)
			{
				if (typeIndex.Length == 0)
					return;
				var list = new IndexData[typeIndex.Length - 1];
				int listi = 0;

				for (int i = 0; i < typeIndex.Length; i++)
				{
					if (typeIndex[i].fullPath != path)
					{
						list[listi] = new IndexData(typeIndex[i].fileReference, typeIndex[i].nameHash, typeIndex[i].fullPath, typeIndex[i].name);
						listi++;
					}
				}
				typeIndex = list;
			}

			public UnityEngine.Object Get(string name, string[] foldersToSearch = null)
			{
				for (int i = 0; i < typeIndex.Length; i++)
				{
					if (typeIndex[i].name == name && WasEntryInFolders(typeIndex[i], foldersToSearch))
					{
						return typeIndex[i].fileReference;
					}
				}
				return null;
			}
			public UnityEngine.Object Get(int nameHash, string[] foldersToSearch = null)
			{
				for (int i = 0; i < typeIndex.Length; i++)
				{
					if (typeIndex[i].nameHash == nameHash && WasEntryInFolders(typeIndex[i], foldersToSearch))
					{
						return typeIndex[i].fileReference;
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
			public UnityEngine.Object fileReference;

			public IndexData()
			{

			}
			public IndexData(int _nameHash, string _fullPath = "", string _name = "")
			{
				name = _name;
				nameHash = _nameHash;
				fullPath = _fullPath;
			}
			public IndexData(UnityEngine.Object _fileReference, int _nameHash, string _fullPath = "", string _name = "")
			{
				name = _name;
				nameHash = _nameHash;
				fullPath = _fullPath;
				fileReference = _fileReference;
			}

		}
		#endregion
	}
}
