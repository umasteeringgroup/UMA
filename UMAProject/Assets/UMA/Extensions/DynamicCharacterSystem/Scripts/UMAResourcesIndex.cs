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
		public bool enableDynamicIndexing = false;
		public bool makePersistent = false;

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
					Destroy(gameObject);
				else
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
                return;
            }
#else
            TextAsset textIndex = Resources.Load<TextAsset>("UMAResourcesIndex.txt");
            if (textIndex != null)
            {
                index = JsonUtility.FromJson<UMAResourcesIndexData>(textIndex.text);
                return;
            }
#endif
            // Not found anywhere
            index = new UMAResourcesIndexData();
            IndexAllResources();
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
			string info = "Total files indexed: " + totalIndexedFiles + " in " + totalIndexedTypes + " Types"/*.\nIndexed Types: \n" + String.Join(", ", typeNames.ToArray())*/;
			return info;
		}
#if UNITY_EDITOR

        private string GetIndexPath()
        {
            if (!string.IsNullOrEmpty(dataAssetPath))
            {
                return dataAssetPath;
            }
            return AssetDatabase.GetAssetPath(Resources.Load("UMAResourcesIndex"));
        }
#endif

        /// <summary>
        /// Saves any updates to the index to the data file. This only happens in the editor.
        /// </summary>
        public void Save()
		{
#if UNITY_EDITOR
            dataAssetPath = GetIndexPath();
            var jsonData = JsonUtility.ToJson(index);
			FileUtils.WriteAllText(dataAssetPath, jsonData);
#endif
        }

#if UNITY_EDITOR

		/// <summary>
		/// Clears the Index of all data.
		/// </summary>
		public void ClearIndex()
		{
			index = new UMAResourcesIndexData();
			Save();
		}
		/// <summary>
		/// Method to generate a full index of every file in Resources
		/// </summary>
		// slight issue here is that UMABonePose assets dont have a hash and expressions are called the same thing for every race (so we only end up with one set indexed). But since they are refrerenced in an expressionset this seems to work ok anyway.
		public void IndexAllResources()
		{
            Debug.Log("Indexing all resources");
			if (Application.isPlaying)
			{
				Debug.Log("You can only create a full Resources index while the application is not playing.");
				return;
			}
			var paths = AssetDatabase.GetAllAssetPaths();
			int pathsAdded = 0;
			for (int i = 0; i < paths.Length; i++)
			{
				if (paths[i].IndexOf("Resources/") > -1)
				{
					//we need to split the path and only use the part after resources
					var objResourcesPathArray = paths[i].Split(new string[] { "Resources/" }, StringSplitOptions.RemoveEmptyEntries);
					var extension = Path.GetExtension(objResourcesPathArray[1]);
					var objResourcesPath = objResourcesPathArray[1];
					if (extension != "")
					{
						objResourcesPath = objResourcesPath.Replace(extension, "");
					}
					var tempObj = Resources.Load(objResourcesPath);
					if (tempObj != null)
					{
						pathsAdded++;
						string thisName = Path.GetFileNameWithoutExtension(paths[i]);
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
						index.AddPath(tempObj, thisHash);
						if (tempObj.GetType() != typeof(UnityEngine.GameObject))
							Resources.UnloadAsset(tempObj);//TODO check if this is safe to do...
					}
				}
			}
			Debug.Log("[UMAResourcesIndex] Added/Updated " + index.Count() + " assets in the Index");
			Save();
		}
#endif
	}
}
