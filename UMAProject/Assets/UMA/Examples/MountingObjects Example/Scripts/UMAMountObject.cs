using UnityEngine;
using System.Collections.Generic;
using UMA;
namespace UMA
{
    public class UMAMountObject : MonoBehaviour
    {
        [System.Serializable]
        public class mountInfo
        {
            [Tooltip("Prefab of the object that will get mounted.")]
            public GameObject objPrefab;
            [Tooltip("Name of the bone that the object will get mounted to.")]
            public string boneName;

            public Vector3 position;
            public Vector3 rotation;
            public Vector3 scale = Vector3.one;
        }
        [Tooltip("A list of the objects that can be dynamically mounted.")]
        public mountInfo[] mountInfos;

        private UMAData _umaData;
        private Dictionary<string, int> nameMap = new Dictionary<string, int>();

        void OnEnable()
        {
            if (_umaData == null)
            {
                _umaData = gameObject.GetComponent<UMAData>();
            }

            for (int i = 0; i < mountInfos.Length; i++)
            {
                if (nameMap.ContainsKey(mountInfos[i].objPrefab.name))
                {
                    if (Debug.isDebugBuild)
                    {
                        Debug.LogWarning("ObjPrefab already added! " + mountInfos[i].objPrefab.name);
                    }
                }
                nameMap.Add(mountInfos[i].objPrefab.name, i);
            }
        }

        private bool IsValid()
        {
            if (_umaData == null)
            {
                return false;
            }

            if (mountInfos == null)
            {
                return false;
            }

            if (mountInfos.Length <= 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// If a mount point already exists for a prefab, this function will change the mount info for that prefab to the given mountInfo
        /// </summary>
        /// <param name="newInfo"></param>
        public void ChangeMountInfo(mountInfo newInfo)
        {
            int index = -1;
            if (nameMap.TryGetValue(newInfo.objPrefab.name, out index))
            {
                mountInfos[index] = newInfo;
            }
            else
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning("ObjPrefab doesnt exist: " + mountInfos[index].objPrefab.name);
                }
            }
        }

        public void MountObject(string name)
        {
            if (nameMap.ContainsKey(name))
            {
                MountObject(nameMap[name]);
            }
            else
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning(name + " not found in list!");
                }
            }
        }

        public void MountObject(int index)
        {
            if (_umaData == null)
            {
                _umaData = gameObject.GetComponent<UMAData>();
            }

            if (!IsValid())
            {
                return;
            }

            GameObject boneObj = null;

            boneObj = _umaData.GetBoneGameObject(mountInfos[index].boneName);

            if (boneObj == null)
            {
                return;
            }

            Transform objTransform = boneObj.transform.Find(mountInfos[index].objPrefab.name);
            if (objTransform == null)
            {
                GameObject newObj = GameObject.Instantiate(mountInfos[index].objPrefab);
                newObj.name = mountInfos[index].objPrefab.name;
                newObj.transform.SetParent(boneObj.transform, false);
                newObj.transform.localPosition = mountInfos[index].position;
                newObj.transform.localRotation = Quaternion.Euler(mountInfos[index].rotation);
                newObj.transform.localScale = mountInfos[index].scale;
            }
            else
            {
                objTransform.gameObject.SetActive(true);
            }
        }

        public void UnMountObject(string name)
        {
            if (nameMap.ContainsKey(name))
            {
                UnMountObject(nameMap[name]);
            }
            else
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning(name + " not found in list!");
                }
            }
        }

        public void UnMountObject(int index)
        {
            if (_umaData == null)
            {
                _umaData = gameObject.GetComponent<UMAData>();
            }

            if (!IsValid())
            {
                return;
            }

            GameObject boneObj = _umaData.GetBoneGameObject(mountInfos[index].boneName);

            if (boneObj == null)
            {
                return;
            }

            Transform objTransform = boneObj.transform.Find(mountInfos[index].objPrefab.name);
            if (objTransform != null)
            {
                objTransform.gameObject.SetActive(false);
            }
        }
    }
}