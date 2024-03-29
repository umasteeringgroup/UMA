using UnityEngine;
#if UNITY_EDITOR
#endif
using System.Collections.Generic;

namespace UMA.CharacterSystem
{
    public class DynamicOverlayLibrary : OverlayLibrary
    {

        //extra fields for Dynamic Version
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
        List<OverlayDataAsset> editorAddedAssets = new List<OverlayDataAsset>();
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
        OverlayData GetEditorAddedAsset(int? nameHash = null, string overlayName = "")
        {
            OverlayData foundOverlay = null;
            if (editorAddedAssets.Count > 0)
            {
                foreach (OverlayDataAsset edOverlay in editorAddedAssets)
                {
                    if(edOverlay != null)
                    {
                        if(nameHash != null)
                        {
                            if(edOverlay.nameHash == nameHash)
                                foundOverlay = new OverlayData(edOverlay);
                        }
                        else if(overlayName != null)
                        {
                            if(overlayName != "")
                                if(edOverlay.overlayName == overlayName)
                                    foundOverlay = new OverlayData(edOverlay);
                        }

                    }
                }
            }
            return foundOverlay;
        }
    #endif

        public void UpdateDynamicOverlayLibrary(int? nameHash = null)
        {
            var Overlays = UMAAssetIndexer.Instance.GetAllAssets<OverlayDataAsset>();

            if (nameHash != null)
            {
                Overlays.RemoveAll(x => x.nameHash != nameHash);
            }
            AddOverlayAssets(Overlays.ToArray());
        }

        public void UpdateDynamicOverlayLibrary(string overlayName)
        {
            var Overlays = UMAAssetIndexer.Instance.GetAllAssets<OverlayDataAsset>();

            Overlays.RemoveAll(x => x.overlayName != overlayName);
            AddOverlayAssets(Overlays.ToArray());
        }

    #pragma warning disable 618
        private void AddOverlayAssets(OverlayDataAsset[] overlays)
        {
            foreach (OverlayDataAsset overlay in overlays)
            {
    #if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    bool alreadyExisted = false;
                    foreach (OverlayDataAsset addedOverlay in overlayElementList)
                    {
                        if (addedOverlay == overlay)
                        {
                            alreadyExisted = true;
                            break;
                        }
                    }
                    if (alreadyExisted)
                        continue;
                    if (!editorAddedAssets.Contains(overlay))
                        editorAddedAssets.Add(overlay);
                }
                else
    #endif
                AddOverlayAsset(overlay);
            }
            //This doesn't actually seem to do anything apart from slow things down
            //StartCoroutine(CleanOverlaysFromResourcesAndBundles());
        }
    #pragma warning restore 618

        /*IEnumerator CleanOverlaysFromResourcesAndBundles()
        {
            yield return null;
            Resources.UnloadUnusedAssets();
            yield break;
        }*/

        public override OverlayData InstantiateOverlay(string name)
        {
            OverlayData res;
            try
            {
                res = base.InstantiateOverlay(name);
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
                //we try to load the overlay dynamically
                UpdateDynamicOverlayLibrary(name);
                try {
                    res = base.InstantiateOverlay(name);
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
                    throw new UMAResourceNotFoundException("dOverlayLibrary: Unable to find: " + name);
                }
            }
            return res;
        }
        //we dont seem to be able to use nameHash for some reason so in this case we are screwed- DOES THIS EVER HAPPEN?
        public override OverlayData InstantiateOverlay(int nameHash)
        {
            if (Debug.isDebugBuild)
                Debug.Log("OverlayLibrary tried to InstantiateOverlay using Hash");
            OverlayData res;
            try
            {
                res = base.InstantiateOverlay(nameHash);
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
                UpdateDynamicOverlayLibrary(nameHash);
                try {
                    res = base.InstantiateOverlay(nameHash);
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
                    throw new UMAResourceNotFoundException("dOverlayLibrary: Unable to find: " + nameHash);
                }
            }
            return res;
        }
        public override OverlayData InstantiateOverlay(string name, Color color)
        {
            OverlayData res;
            try
            {
                res = base.InstantiateOverlay(name);
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
                //we do something
                UpdateDynamicOverlayLibrary(name);
                try {
                    res = base.InstantiateOverlay(name);
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
                            res.colorData.color = color;
                            return res;
                        }
                    }
    #endif
                    throw new UMAResourceNotFoundException("dOverlayLibrary: Unable to find: " + name);
                }
            }
            res.colorData.color = color;
            return res;
        }
        //we dont seem to be able to use nameHash for some reason so in this case we are screwed- DOES THIS EVER HAPPEN?
        public override OverlayData InstantiateOverlay(int nameHash, Color color)
        {
            if (Debug.isDebugBuild)
                Debug.Log("OverlayLibrary tried to InstantiateOverlay using Hash");
            OverlayData res;
            try
            {
                res = base.InstantiateOverlay(nameHash);
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
                UpdateDynamicOverlayLibrary(nameHash);
                try {
                    res = base.InstantiateOverlay(nameHash);
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
                            res.colorData.color = color;
                            return res;
                        }
                    }
    #endif
                    throw new UMAResourceNotFoundException("dOverlayLibrary: Unable to find: " + nameHash);
                }
            }
            res.colorData.color = color;
            return res;
        }
        /// <summary>
        /// Gets the originating asset bundle.
        /// </summary>
        /// <returns>The originating asset bundle.</returns>
        /// <param name="overlayName">Overlay name.</param>
        public string GetOriginatingAssetBundle(string overlayName)
        {
            string originatingAssetBundle = "";
            if (assetBundlesUsedDict.Count > 0)
            {
                foreach (KeyValuePair<string, List<string>> kp in assetBundlesUsedDict)
                {
                    if (kp.Value.Contains(overlayName))
                    {
                        originatingAssetBundle = kp.Key;
                        break;
                    }
                }
            }
            if (originatingAssetBundle == "")
            {
                if (Debug.isDebugBuild)
                    Debug.Log(overlayName + " has not been loaded from any AssetBundle");
            }
            else
            {
                if (Debug.isDebugBuild)
                    Debug.Log("originatingAssetBundle for " + overlayName + " was " + originatingAssetBundle);
            }
            return originatingAssetBundle;
        }
    }
}
