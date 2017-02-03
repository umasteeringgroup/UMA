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
	public class UMAResourcesIndexData
	{

		//there is no need to be able to edit this index in a release because you cannot add anything to resources in a built game
		//to update resources you would need to update the game and so you would update the index this creates.
		#region special types
		[System.Serializable]
		public class TypeIndex
		{
			public string type;
			public NameIndex[] typeFiles;

			public TypeIndex() { }

			public TypeIndex(string _type, int _nameHash, string _path = "", string _fullPath = "")
			{
				type = _type;
				typeFiles = new NameIndex[1];
				typeFiles[0] = new NameIndex(_nameHash, _path, _fullPath);
			}

			public int Count()
			{
				return typeFiles.Length;
			}

			public void Add(int nameHash, string path, string fullPath)
			{
				bool found = false;
				for (int i = 0; i < typeFiles.Length; i++)
				{
					if (typeFiles[i].nameHash == nameHash)
					{
						typeFiles[i].path = path;
						typeFiles[i].fullPath = fullPath;
						found = true;
					}
				}
				if (!found)
				{
					var list = new NameIndex[typeFiles.Length + 1];
					Array.Copy(typeFiles, list, typeFiles.Length);
					list[typeFiles.Length] = new NameIndex(nameHash, path, fullPath);
					typeFiles = list;
				}
			}
			public void Remove(int nameHash)
			{
				var list = new NameIndex[typeFiles.Length - 1];
				int listi = 0;
				for (int i = 0; i < typeFiles.Length; i++)
				{
					if (typeFiles[i].nameHash != nameHash)
					{
						list[listi].nameHash = typeFiles[i].nameHash;
						list[listi].path = typeFiles[i].path;
						listi++;
					}
				}
				typeFiles = list;
			}

			public string Get(int nameHash, bool fullPath = false)
			{
				for (int i = 0; i < typeFiles.Length; i++)
				{
					if (typeFiles[i].nameHash == nameHash)
					{
						if (fullPath == true)
							return typeFiles[i].fullPath;
						else
							return typeFiles[i].path;
					}
				}
				return "";
			}
		}

		[System.Serializable]
		public class NameIndex
		{
			public int nameHash;
			public string path;
			//this is the full path rather than the resources path
			public string fullPath;

			public NameIndex()
			{

			}
			public NameIndex(int _nameHash, string _path, string _fullPath = "")
			{
				nameHash = _nameHash;
				path = _path;
				fullPath = _fullPath;
			}

		}
		#endregion
		public TypeIndex[] data = new TypeIndex[0];

		public int Count()
		{
			int totalCount = 0;
			for (int i = 0; i < data.Length; i++)
			{
				totalCount += data[i].Count();
			}
			return totalCount;
		}

#if UNITY_EDITOR
		/// <summary>
		/// Adds a path terporarily to the index. To add it permanently use UMAResourcesIndex.Instance.Add
		/// </summary>
		/// <param name="obj"></param>
		/// <param name="objName"></param>
		public void AddPath(UnityEngine.Object obj, string objName)
		{
			AddPath(obj, UMAUtils.StringToHash(objName));
		}
		/// <summary>
		/// Adds a path terporarily to the index. To add it permanently use UMAResourcesIndex.Instance.Add
		/// </summary>
		/// <param name="obj"></param>
		/// <param name="objNameHash"></param>
		public void AddPath(UnityEngine.Object obj, int objNameHash)
		{
			if (obj == null)
				return;
			var objFullPath = AssetDatabase.GetAssetPath(obj);
			var objResourcesPathArray = objFullPath.Split(new string[] { "Resources/" }, StringSplitOptions.RemoveEmptyEntries);
			var extension = Path.GetExtension(objResourcesPathArray[1]);
			var objResourcesPath = objResourcesPathArray[1];
			if (extension != "")
			{
				objResourcesPath = objResourcesPath.Replace(extension, "");
			}
			bool hadType = false;
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type == obj.GetType().ToString())
				{
					data[i].Add(objNameHash, objResourcesPath, objFullPath);
					hadType = true;
				}
			}
			if (!hadType)
			{
				var list = new TypeIndex[data.Length + 1];
				Array.Copy(data, list, data.Length);
				list[data.Length] = new TypeIndex(obj.GetType().ToString(), objNameHash, objResourcesPath, objFullPath);
				data = list;
			}
		}
#endif
		public void Remove(System.Type type)
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
		public void Remove<T>(string thisName) where T : UnityEngine.Object
		{
			Remove<T>(UMAUtils.StringToHash(thisName));
		}
		public void Remove<T>(int nameHash) where T : UnityEngine.Object
		{
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type == typeof(T).ToString())
				{
					data[i].Remove(nameHash);
					if (data[i].Count() == 0)
						Remove(typeof(T));
				}
			}
		}
		/// <summary>
		/// Get a path for the given type and name out of the index, optionally filtering result based on specified folders
		/// </summary>
		public string GetPath(string type, string nameToFind, bool fullPath = false, string[] foldersToSearch = null)
		{
			return GetPath(type, UMAUtils.StringToHash(nameToFind), fullPath, foldersToSearch);
		}
		/// <summary>
		/// Get a path for the given type and namehash  out of the index, optionally filtering result based on specified folders
		/// </summary>
		public string GetPath(string type, int nameHash, bool fullPath = false, string[] foldersToSearch = null)
		{
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type == type)
				{
					var foundPath = data[i].Get(nameHash, fullPath);
					if (foldersToSearch != null && foldersToSearch.Length > 0)
					{
						for (int ii = 0; ii < foldersToSearch.Length; ii++)
						{
							if (foundPath.IndexOf(foldersToSearch[ii]) > -1)
							{
								return foundPath;
							}
						}
					}
					else
					{
						return foundPath;
					}
				}
			}
			return "";
		}
		/// <summary>
		/// Get a path out of the index for the given name, optionally filtering result based on specified folders
		/// </summary>
		public string GetPath<T>(string nameToFind, string[] foldersToSearch = null) where T : UnityEngine.Object
		{
			return GetPath<T>(UMAUtils.StringToHash(nameToFind), foldersToSearch);
		}
		/// <summary>
		/// Get a path out of the index for the given nameHash, optionally filtering result based on specified folders
		/// </summary>
		public string GetPath<T>(int nameHash, string[] foldersToSearch = null) where T : UnityEngine.Object
		{
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type == typeof(T).ToString())
				{
					var foundPath = data[i].Get(nameHash);
					if (foldersToSearch != null && foldersToSearch.Length > 0)
					{
						for (int ii = 0; ii < foldersToSearch.Length; ii++)
						{
							if (foundPath.IndexOf(foldersToSearch[ii]) > -1)
							{
								return foundPath;
							}
						}
					}
					else
					{
						return foundPath;
					}
				}
			}
			return "";
		}
		/// <summary>
		/// Get all the paths for a given type out of the index, optionally filtering result based on specified folders
		/// </summary>
		public string[] GetPaths<T>(string[] foldersToSearch = null) where T : UnityEngine.Object
		{
			List<string> foundPaths = new List<string>();
			for (int i = 0; i < data.Length; i++)
			{
				if (data[i].type == typeof(T).ToString())
				{
					for (int ii = 0; ii < data[i].typeFiles.Length; ii++)
					{
						if (foldersToSearch != null && foldersToSearch.Length > 0)
						{
							for (int iii = 0; iii < foldersToSearch.Length; iii++)
							{
								if (data[i].typeFiles[ii].path.IndexOf(foldersToSearch[iii]) > -1)
								{
									foundPaths.Add(data[i].typeFiles[ii].path);
									break;
								}
							}
						}
						else
						{
							foundPaths.Add(data[i].typeFiles[ii].path);
						}
					}
				}
			}
			return foundPaths.ToArray();
		}
	}
}
