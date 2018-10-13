using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;

namespace UMA.CharacterSystem
{
    public class DynamicSlotLibrary : SlotLibrary
    {

        public bool dynamicallyAddFromResources = true;
        [Tooltip("Limit the Resources search to the following folders (no starting slash and seperate multiple entries with a comma)")]
        public string resourcesFolderPath = "";
        public bool dynamicallyAddFromAssetBundles;
        [Tooltip("Limit the AssetBundles search to the following bundles (no starting slash and seperate multiple entries with a comma)")]
        public string assetBundleNamesToSearch = "";
        //This is a ditionary of asset bundles that were loaded into the library at runtime. 
        //CharacterAvatar can query this this to find out what asset bundles were required to create itself 
        //or other scripts can use it to find out which asset bundles are being used by the Libraries at any given point.
        public Dictionary<string, List<string>> assetBundlesUsedDict = new Dictionary<string, List<string>>();
    #if UNITY_EDITOR
        List<SlotDataAsset> editorAddedAssets = new List<SlotDataAsset>();
    #endif
        [System.NonSerialized]
        [HideInInspector]
        public bool downloadAssetsEnabled = true;

        public void Start()
        {
            if (Application.isPlaying)
            {
                assetBundlesUsedDict.Clear();
            }
    #if UNITY_EDITOR
            if (Application.isPlaying)
            {

                ClearEditorAddedAssets();
            }
    #endif
        }

        /// <summary>
        /// Clears any editor added assets when the Scene is closed
        /// </summary>
        void OnDestroy()
        {
    #if UNITY_EDITOR
            ClearEditorAddedAssets();
    #endif
        }

        public void ClearEditorAddedAssets()
        {
    #if UNITY_EDITOR
            if (editorAddedAssets.Count > 0)
            {
                editorAddedAssets.Clear();
            }
    #endif
        }

    #if UNITY_EDITOR
        SlotData GetEditorAddedAsset(int? nameHash = null, string slotName = "")
        {
            SlotData foundSlot = null;
            if (editorAddedAssets.Count > 0)
            {
                foreach (SlotDataAsset edSlot in editorAddedAssets)
                {
                    if(edSlot != null)
                    {
                        if (nameHash != null)
                        {
                            if(edSlot.nameHash == nameHash)
                                foundSlot = new SlotData(edSlot);
                        }else if(slotName != null)
                        {
                            if(slotName != "")
                                if(edSlot.slotName == slotName)
                                    foundSlot = new SlotData(edSlot);
                        }                
                    }
                }
            }
            return foundSlot;
        }
    #endif

        public void UpdateDynamicSlotLibrary(int? nameHash = null)
        {
                DynamicAssetLoader.Instance.AddAssets<SlotDataAsset>(ref assetBundlesUsedDict, dynamicallyAddFromResources, dynamicallyAddFromAssetBundles, downloadAssetsEnabled, assetBundleNamesToSearch, resourcesFolderPath, nameHash, "", AddSlotAssets);
        }

        public void UpdateDynamicSlotLibrary(string slotName)
        {
                DynamicAssetLoader.Instance.AddAssets<SlotDataAsset>(ref assetBundlesUsedDict, dynamicallyAddFromResources, dynamicallyAddFromAssetBundles, downloadAssetsEnabled, assetBundleNamesToSearch, resourcesFolderPath, null, slotName, AddSlotAssets);
        }


        private void AddSlotAssets(SlotDataAsset[] slots)
        {
            foreach (SlotDataAsset slot in slots)
            {
    #if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    if (HasSlot(slot.nameHash))
                        continue;
                    if (!editorAddedAssets.Contains(slot))
                        editorAddedAssets.Add(slot);
                }
                else
    #endif
                AddSlotAsset(slot);
            }
            //This doesn't actually seem to do anything apart from slow things down
            //StartCoroutine(CleanSlotsFromResourcesAndBundles());
        }

        /*IEnumerator CleanSlotsFromResourcesAndBundles()
        {
            yield return null;
            Resources.UnloadUnusedAssets();
            yield break;
        }*/

        public override SlotData InstantiateSlot(string name)
        {
            SlotData res;
            try
            {
                res = base.InstantiateSlot(UMAUtils.StringToHash(name));
            }
            catch
            {
                res = null;
            }
    #if UNITY_EDITOR
            if (!Application.isPlaying && res == null)
            {
                res = GetEditorAddedAsset(null, name);
            }
    #endif
            if (res == null)
            {
                //we try to load the slot dynamically
                UpdateDynamicSlotLibrary(name);
                try
                {
                    res = base.InstantiateSlot(name);
                }
                catch
                {
                    res = null;
                }
                if (res == null)
                {
    #if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        res = GetEditorAddedAsset(null, name);
                        if (res != null)
                        {
                            return res;
                        }
                    }
    #endif
                    throw new UMAResourceNotFoundException("dSlotLibrary (211): Unable to find: " + name);
                }
            }
            return res;
        }
        public override SlotData InstantiateSlot(int nameHash)
        {
            SlotData res;
            try
            {
                res = base.InstantiateSlot(nameHash);
            }
            catch
            {
                res = null;
            }
    #if UNITY_EDITOR
            if (!Application.isPlaying && res == null)
            {
                res = GetEditorAddedAsset(nameHash);
            }
    #endif
            if (res == null)
            {
                UpdateDynamicSlotLibrary(nameHash);
                try
                {
                    res = base.InstantiateSlot(nameHash);
                }
                catch
                {
                    res = null;
                }
                if (res == null)
                {
    #if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        res = GetEditorAddedAsset(nameHash);
                        if (res != null)
                        {
                            return res;
                        }
                    }
    #endif
                    throw new UMAResourceNotFoundException("dSlotLibrary: Unable to find: " + nameHash);
                }
            }
            return res;
        }
        public override SlotData InstantiateSlot(string name, List<OverlayData> overlayList)
        {
            SlotData res;
            try
            {
                res = base.InstantiateSlot(name);
            }
            catch
            {
                res = null;
            }
    #if UNITY_EDITOR
            if (!Application.isPlaying && res == null)
            {
                res = GetEditorAddedAsset(null, name);
            }
    #endif
            if (res == null)
            {
                //we load dynamically
                UpdateDynamicSlotLibrary(name);
                try {
                    res = base.InstantiateSlot(name);
                }
                catch
                {
                    res = null;
                }
                if (res == null)
                {
    #if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        res = GetEditorAddedAsset(null, name);
                        if (res != null)
                        {
                            res.SetOverlayList(overlayList);
                            return res;
                        }
                    }
    #endif
                    throw new UMAResourceNotFoundException("dSlotLibrary: Unable to find: " + name);
                }
            }
            res.SetOverlayList(overlayList);
            return res;
        }
        public override SlotData InstantiateSlot(int nameHash, List<OverlayData> overlayList)
        {
            SlotData res;
            try
            {
                res = base.InstantiateSlot(nameHash);
            }
            catch
            {
                res = null;
            }
    #if UNITY_EDITOR
            if (!Application.isPlaying && res == null)
            {
                res = GetEditorAddedAsset(nameHash);
            }
    #endif
            if (res == null)
            {
                UpdateDynamicSlotLibrary(nameHash);
                try {
                    res = base.InstantiateSlot(nameHash);
                }
                catch
                {
                    res = null;
                }
                if (res == null)
                {
    #if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        res = GetEditorAddedAsset(nameHash);
                        if (res != null)
                        {
                            res.SetOverlayList(overlayList);
                            return res;
                        }
                    }
    #endif
                    throw new UMAResourceNotFoundException("dSlotLibrary: Unable to find: " + nameHash);
                }
            }
            res.SetOverlayList(overlayList);
            return res;
        }
        /// <summary>
        /// Gets the originating asset bundle.
        /// </summary>
        /// <returns>The originating asset bundle.</returns>
        /// <param name="slotName">slot name.</param>
        public string GetOriginatingAssetBundle(string slotName)
        {
            string originatingAssetBundle = "";
            if (assetBundlesUsedDict.Count > 0)
            {
                foreach (KeyValuePair<string, List<string>> kp in assetBundlesUsedDict)
                {
                    if (kp.Value.Contains(slotName) || kp.Value.Contains(slotName.Replace("_Slot", "")))//just try without '_Slot' as well since autogenerated slot.slotname doesn't end with autogenrated '_Slot'
                    {
                        originatingAssetBundle = kp.Key;
                        break;
                    }
                }
            }
            if (originatingAssetBundle == "")
            {
                Debug.Log(slotName + " was not found in any loaded AssetBundle");
            }
            else
            {
                Debug.Log("originatingAssetBundle for " + slotName + " was " + originatingAssetBundle);
            }
            return originatingAssetBundle;
        }
    }
}
