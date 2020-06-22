using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;
using UMA.CharacterSystem;

#if UMA_ADDRESSABLES
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using AsyncOp = UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<System.Collections.Generic.IList<UnityEngine.Object>>;
#endif
using PackSlot = UMA.UMAPackedRecipeBase.PackedSlotDataV3;
using SlotRecipes = System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<UMA.UMATextRecipe>>;
using RaceRecipes = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<UMA.UMATextRecipe>>>;
using System.Linq;
using System.Text;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#if UMA_ADDRESSABLES
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UMA;
#endif
#endif


namespace UMA
{
    public class UMAAddressablesSupport
    {
        public const string SharedGroupName = "UMA_SharedItems";
        public string umaBaseName = "UMA_Base";

#if UMA_ADDRESSABLES
        Dictionary<int, List<UMATextRecipe>> SlotTracker = new Dictionary<int, List<UMATextRecipe>>();
        Dictionary<int, List<UMATextRecipe>> OverlayTracker = new Dictionary<int, List<UMATextRecipe>>();
        Dictionary<int, List<UMATextRecipe>> TextureTracker = new Dictionary<int, List<UMATextRecipe>>();
        Dictionary<int, AddressableAssetGroup> GroupTracker = new Dictionary<int, AddressableAssetGroup>();
        Dictionary<int, string> AddressLookup = new Dictionary<int, string>();

        private AddressableAssetSettings _AddressableSettings;
        private static readonly UMAAddressablesSupport addressablesSupport = new UMAAddressablesSupport();

        public static UMAAddressablesSupport Instance
        {
            get
            {
                return addressablesSupport;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Removes the assent entry, if it exists
        /// </summary>
        /// <param name="ai"></param>
        private void ClearAddressableAssetEntry(AssetItem ai)
        {
            AddressableAssetEntry ae = AddressableUtility.GetAddressableAssetEntry(ai._Path, out AddressableAssetGroup group);
            if (group != null)
            {
                group.RemoveAssetEntry(ae);
            }
        }

        public bool IsUMAGroup(string GroupName)
        {
            if (GroupName.StartsWith("UMA_")) return true;
            if (GroupName.StartsWith("UTR_")) return true;
            if (GroupName.StartsWith("UWR_")) return true;
            return false;
        }

        public void CleanupAddressables(bool OnlyEmpty = false, bool RemoveFlags = false)
        {
            // delete all UMA groups
            // RemoveGroup.
            if (AddressableUtility.AddressableSettings == null)
            {
                EditorUtility.DisplayDialog("Warning", "Addressable Asset Settings not found", "OK");
                return;
            }
            List<AddressableAssetGroup> GroupsToDelete = new List<AddressableAssetGroup>();

            foreach (var group in AddressableUtility.AddressableSettings.groups)
            {
                if (IsUMAGroup(group.name))
                {
                    if (OnlyEmpty)
                    {
                        if (group.entries.Count > 0) continue;
                    }
                    GroupsToDelete.Add(group);
                }
            }

            float pos = 0.0f;
            float inc = 1.0f / GroupsToDelete.Count;

            foreach (AddressableAssetGroup group in GroupsToDelete)
            {
                int iPos = Mathf.CeilToInt(pos);
                EditorUtility.DisplayProgressBar("Cleanup", "Removing " + group.Name, iPos);
                if (group.name.Contains(SharedGroupName))
                {
                    List<AddressableAssetEntry> ItemsToClear = new List<AddressableAssetEntry>();
                    ItemsToClear.AddRange(group.entries);
                    foreach (AddressableAssetEntry ae in ItemsToClear)
                    {
                        group.RemoveAssetEntry(ae);
                    }
                }
                else
                {
                    AddressableUtility.AddressableSettings.RemoveGroup(group);
                }
                pos += inc;
            }

            if (RemoveFlags)
            {
                UMAAssetIndexer.Instance.ClearAddressableFlags();
            }
            EditorUtility.ClearProgressBar();
        }

        private void GenerateLookups(UMAContextBase context, List<AssetItem> wardrobe)
        {
            float pos = 0.0f;
            float inc = 1.0f / wardrobe.Count;

            // Get the slots, overlays, textures.
            // calculate the number of references for each of them.
            // Map the usage 
            foreach (AssetItem recipeItem in wardrobe)
            {
                UMATextRecipe uwr = recipeItem.Item as UMATextRecipe;
                int iPos = Mathf.CeilToInt(pos);
                EditorUtility.DisplayProgressBar("Generating", "Calculating Usage: " + uwr.name, iPos);

                // todo: cache this
                UMAData.UMARecipe ur = UMAAssetIndexer.Instance.GetRecipe(uwr, context);

                if (ur.slotDataList == null) continue;

                foreach (SlotData sd in ur.slotDataList)
                {
                    if (sd == null) continue;

                    AssetItem ai = UMAAssetIndexer.Instance.GetAssetItem<SlotDataAsset>(sd.slotName);

                    if (ai != null && ai.IsAlwaysLoaded == false)
                    {
                        // is this a utility slot? if so, we need to not delete it as an orphan. 
                        if (sd.asset.isUtilitySlot)
                        {
                            ai.IsAlwaysLoaded = true;
                        }
                    }

                    //if (!(ai != null && ai.IsAlwaysLoaded))
                    //{
                    //AddToTracker = false;
                    //}
                    //else
                    //{
                    int slotInstance = sd.asset.GetInstanceID();

                    if (!SlotTracker.ContainsKey(slotInstance))
                    {
                        ai.IsAddressable = true;
                        SlotTracker.Add(slotInstance, new List<UMATextRecipe>());
                    }
                    SlotTracker[slotInstance].Add(uwr);
                    if (!AddressLookup.ContainsKey(slotInstance))
                    {
                        AddressLookup.Add(slotInstance, "Slt-" + sd.slotName);
                    }
                    //}

                    List<OverlayData> odList = sd.GetOverlayList();

                    foreach (OverlayData od in odList)
                    {
                        if (od == null) continue;


                        /* = GetAssetItem<OverlayDataAsset>(od.overlayName);

                        if (ai != null && ai.IsAlwaysLoaded)
                        {
                            continue;
                        }*/

                        int OverlayInstance = od.asset.GetInstanceID();

                        if (!OverlayTracker.ContainsKey(OverlayInstance))
                        {
                            OverlayTracker.Add(OverlayInstance, new List<UMATextRecipe>());
                        }
                        OverlayTracker[OverlayInstance].Add(uwr);
                        if (!AddressLookup.ContainsKey(OverlayInstance))
                        {
                            ai.IsAddressable = true;
                            AddressLookup.Add(OverlayInstance, "Ovl-" + od.overlayName);
                        }
                        foreach (Texture tx in od.textureArray)
                        {
                            if (tx == null) continue;
                            int TextureID = tx.GetInstanceID();
                            if (!TextureTracker.ContainsKey(TextureID))
                            {
                                TextureTracker.Add(TextureID, new List<UMATextRecipe>());
                            }
                            TextureTracker[TextureID].Add(uwr);
                            if (!AddressLookup.ContainsKey(TextureID))
                            {
                                AddressLookup.Add(TextureID, "Tex-" + tx.name);
                            }
                        }
                    }
                }
                pos += inc;
            }
        }

        public void AssignLabel(AssetItem ai, string Label)
        {
            AddressableAssetEntry ae = AddressableUtility.GetAddressableAssetEntry(ai._Path);
            if (ae != null)
            {
                if (ae.labels.Contains(Label)) return;
                ae.SetLabel(Label, true, true, true);
            }
        }

        public void AddItemToSharedGroup(string GUID, string Address, List<string> labels, AddressableAssetGroup sharedGroup)
        {
            AddressableAssetEntry ae = AddressableUtility.AddressableSettings.CreateOrMoveEntry(GUID, sharedGroup, false, true);
            ae.SetAddress(Address);
            ae.SetLabel(umaBaseName, true, true, true);
            foreach (string s in labels)
            {
                ae.SetLabel(s, true, true, true);
            }
        }

        private void AddAddressableAssets(Dictionary<int, List<UMATextRecipe>> tracker, AddressableAssetGroup sharedGroup)
        {
            float pos = 0.0f;
            float inc = 1.0f / tracker.Keys.Count;

            // Go through the each item, and add them to the groups (denoted by the list of recipes).
            // if an item is in 1 group, then it goes in that group.
            // if it's in more than 1 group, then it goes into the shared group.
            // if it's not in any group... not sure how we got there, but it does nothing.
            foreach (KeyValuePair<int, List<UMATextRecipe>> kp in tracker)
            {
                try
                {
                    int iPos = Mathf.CeilToInt(pos);
                    pos += inc;
                    bool found = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(kp.Key, out string GUID, out long localid);

                    if (found)
                    {
                        EditorUtility.DisplayProgressBar("Generating", "Adding Asset " + GUID, iPos);
                        AddressableAssetEntry ae = null;

                        switch (kp.Value.Count)
                        {
                            case 0:
                                Debug.LogWarning("Warning: No wardrobe found for item: " + kp.Key);
                                continue;
                            case 1:
                                ae = AddressableUtility.AddressableSettings.CreateOrMoveEntry(GUID, GroupTracker[kp.Value[0].GetInstanceID()], false, true);
                                break;
                            default:
                                ae = AddressableUtility.AddressableSettings.CreateOrMoveEntry(GUID, sharedGroup, false, true);
                                break;
                        }

                        // modify ae here as needed...
                        ae.SetAddress(AddressLookup[kp.Key]);
                        AssetReference ar = new AssetReference(ae.guid);
                        ae.SetLabel(umaBaseName, true, true, true);
                        // get the name here
                        foreach (UMATextRecipe uwr in kp.Value)
                        {
                            ae.SetLabel(UMAAssetIndexer.Instance.GetLabel(uwr), true, true, true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        private void GenerateCollectionLabels()
        {
            //**********************************************************************************************
            //* Add Wardrobe Collections
            //**********************************************************************************************
            Type theType = UMAAssetIndexer.Instance.GetIndexedType(typeof(UMAWardrobeCollection));
            var wardrobecollections = UMAAssetIndexer.Instance.GetAssetDictionary(theType).Values;

            foreach (AssetItem ai in wardrobecollections)
            {
                UMAWardrobeCollection uwc = ai.Item as UMAWardrobeCollection;
                string Label = uwc.name;

                HashSet<string> collectionRecipes = new HashSet<string>();

                foreach (var recipeName in uwc.arbitraryRecipes)
                {
                    AddCollectionRecipe(uwc, recipeName);
                }
                foreach (var ws in uwc.wardrobeCollection.sets)
                {
                    foreach (var wsettings in ws.wardrobeSet)
                    {
                        string recipeName = wsettings.recipe;
                        AddCollectionRecipe(uwc, recipeName);
                    }
                }
            }
        }

        private void AddCollectionRecipe(UMAWardrobeCollection uwc, string recipeName)
        {
            if (string.IsNullOrEmpty(recipeName))
                return;

            AssetItem recipeAsset = UMAAssetIndexer.Instance.GetAssetItem<UMAWardrobeRecipe>(recipeName);
            if (recipeAsset != null)
            {
                UMAWardrobeRecipe uwr = recipeAsset.Item as UMAWardrobeRecipe;
                if (uwr == null)
                {
                    Debug.Log("Null recipe in wardrobe collection...");
                    return;
                }
                List<AssetItem> items = UMAAssetIndexer.Instance.GetAssetItems(uwr, true);
                foreach (AssetItem recipeitem in items)
                {
                    if (recipeitem.Item is SlotDataAsset)
                    {
                        AddSlotFromCollection(recipeitem.Item as SlotDataAsset, uwc);
                    }
                    if (recipeitem.Item is OverlayDataAsset)
                    {
                        AddOverlayFromCollection(recipeitem.Item as OverlayDataAsset, uwc);
                    }
                }
            }
        }

        private void AddOverlayFromCollection(OverlayDataAsset overlayDataAsset, UMAWardrobeCollection uwc)
        {
            if (!OverlayTracker.ContainsKey(overlayDataAsset.GetInstanceID()))
            {
                OverlayTracker.Add(overlayDataAsset.GetInstanceID(), new List<UMATextRecipe>());
            }
            OverlayTracker[overlayDataAsset.GetInstanceID()].Add(uwc);
            foreach (Texture tex in overlayDataAsset.textureList)
            {
                if (!TextureTracker.ContainsKey(tex.GetInstanceID()))
                {
                    TextureTracker.Add(tex.GetInstanceID(), new List<UMATextRecipe>());
                }
                TextureTracker[tex.GetInstanceID()].Add(uwc);
            }
        }

        private void AddSlotFromCollection(SlotDataAsset slotDataAsset, UMAWardrobeCollection uwc)
        {
            if (!SlotTracker.ContainsKey(slotDataAsset.GetInstanceID()))
            {
                SlotTracker.Add(slotDataAsset.GetInstanceID(), new List<UMATextRecipe>());
            }
            SlotTracker[slotDataAsset.GetInstanceID()].Add(uwc);
        }

        /// <summary>
        /// Get all the UMATextRecipes/UMWardrobeRecipes
        /// </summary>
        /// <returns></returns>
        private List<AssetItem> GetAddressableRecipes()
        {

            List<AssetItem> theRecipes = new List<AssetItem>();
            Type theType;
            //**********************************************************************************************
            //* Add Wardrobe Recipes
            //**********************************************************************************************

            theType = UMAAssetIndexer.Instance.GetIndexedType(typeof(UMAWardrobeRecipe));
            var wardrobe = UMAAssetIndexer.Instance.GetAssetDictionary(theType).Values;

            foreach (AssetItem ai in wardrobe)
            {
                UMAWardrobeRecipe uwr = ai.Item as UMAWardrobeRecipe;
                if (uwr != null)
                {
                    if (uwr.resourcesOnly)
                    {
                        var items = UMAAssetIndexer.Instance.GetAssetItems(uwr);
                        foreach (AssetItem resourceItem in items)
                        {
                            resourceItem.IsResource = true;
                        }
                        continue;
                    }
                    theRecipes.Add(ai);
                }
            }

            theType = UMAAssetIndexer.Instance.GetIndexedType(typeof(UMATextRecipe));
            var trecipes = UMAAssetIndexer.Instance.GetAssetDictionary(theType).Values;

            foreach (AssetItem ai in trecipes)
            {
                UMATextRecipe utr = ai.Item as UMATextRecipe;
                if (utr != null)
                {
                    if (utr.resourcesOnly)
                    {
                        var items = UMAAssetIndexer.Instance.GetAssetItems(utr);
                        foreach (AssetItem resourceItem in items)
                        {
                            resourceItem.IsResource = true;
                        }
                        continue;
                    }
                    theRecipes.Add(ai);
                }
            }

            theType = UMAAssetIndexer.Instance.GetIndexedType(typeof(UMAWardrobeCollection));
            var wcrecipes = UMAAssetIndexer.Instance.GetAssetDictionary(theType).Values;

            foreach (AssetItem ai in wcrecipes)
            {
                UMATextRecipe utr = ai.Item as UMATextRecipe;
                if (utr != null)
                {
                    if (utr.resourcesOnly)
                    {
                        var items = UMAAssetIndexer.Instance.GetAssetItems(utr);
                        foreach (AssetItem resourceItem in items)
                        {
                            resourceItem.IsResource = true;
                        }
                        continue;
                    }
                    theRecipes.Add(ai);
                }
            }

            return theRecipes;
        }


        private void AddAssetItemToGroup(AddressableAssetGroup theGroup, AssetItem theItem, string Address, string Label)
        {
            bool found = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(theItem.Item.GetInstanceID(), out string itemGUID, out long localID);
            if (found)
            {
                AddressableAssetEntry ae = AddressableUtility.AddressableSettings.CreateOrMoveEntry(itemGUID, theGroup, false, true);
                ae.SetAddress(Address);
                ae.labels.Add(Label);
            }
        }
        private void AddItemToGroup(AddressableAssetGroup theGroup, UnityEngine.Object theItem, string Address, string Label)
        {
            bool found = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(theItem.GetInstanceID(), out string itemGUID, out long localID);
            if (found)
            {
                AddressableAssetEntry ae = AddressableUtility.AddressableSettings.CreateOrMoveEntry(itemGUID, theGroup, false, true);
                ae.SetAddress(Address);
                ae.labels.Add(Label);
            }
        }

        public bool AddRecipeGroup(UMATextRecipe recipe)
        {
            List<AssetItem> items = UMAAssetIndexer.Instance.GetAssetItems(recipe, true);

            List<AssetItem> UniqueItems = new List<AssetItem>();
            foreach (AssetItem ai in items)
            {
                if (ai.IsAddressable)
                {
                    AddressableAssetEntry ae = AddressableUtility.GetAddressableAssetEntry(ai._Path);
                    if (ae != null && ae.parentGroup.Name == SharedGroupName)
                    {
                        continue;
                    }
                    UniqueItems.Add(ai);
                }
            }

            if (UniqueItems.Count == 0)
            {
                return false;
            }

            // create the group.
            // add the non-unique items to the group
            // Set the addressable stuff;
            AddressableAssetGroup theGroup = AddressableUtility.AddressableSettings.FindGroup(recipe.name);
            if (theGroup != null)
            {
                AddressableUtility.AddressableSettings.RemoveGroup(theGroup);
            }
            theGroup = AddressableUtility.AddressableSettings.CreateGroup(recipe.name, false, false, true, AddressableUtility.AddressableSettings.DefaultGroup.Schemas);
            theGroup.GetSchema<BundledAssetGroupSchema>().BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;

            foreach (AssetItem ai in UniqueItems)
            {
                ai.AddressableAddress = ""; // let the system assign it.
                ai.IsAddressable = true;
                ai.AddressableGroup = recipe.name;
                ai._SerializedItem = null;
                ai.AddressableLabels = recipe.AssignedLabel;

                AddAssetItemToGroup(theGroup, ai, recipe.name, recipe.AssignedLabel);
#if INCL_TEXTURES
                if (IsOverlayItem(ai))
                {
                    OverlayDataAsset od = ai.Item as OverlayDataAsset;
                    if (od == null) continue;

                    foreach (Texture tex in od.textureList)
                    {
                        if (tex == null) continue;
                        if (tex as Texture2D == null) continue;

                        string Address = "Texture2D-" + tex.name + "-" + tex.GetInstanceID();

                        bool found = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tex.GetInstanceID(), out string texGUID, out long texlocalID);
                        if (found)
                        {
                            AddItemToGroup(theGroup, tex, Address, recipe.AssignedLabel);
                        }
                    }
                }
#endif
            }
            return true;
        }

        private static bool IsOverlayItem(AssetItem ai)
        {
            return ai._Type == typeof(OverlayDataAsset);
        }

        public void GenerateAddressables(IUMAAddressablePlugin plugin)
        {
            bool OK = plugin.Prepare();
            if (!OK) return;

            foreach (Type t in UMAAssetIndexer.Instance.GetTypes())
            {
                ClearAddressableFlags(t);
            }

            AddressableAssetGroup sharedGroup = AddressableUtility.AddressableSettings.FindGroup(SharedGroupName);
            if (sharedGroup == null)
            {
                sharedGroup = AddressableUtility.AddressableSettings.CreateGroup(SharedGroupName, false, false, true, AddressableUtility.AddressableSettings.DefaultGroup.Schemas);
                sharedGroup.GetSchema<BundledAssetGroupSchema>().BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            }

            var items = GetAddressableRecipes();
            foreach (AssetItem ai in items)
            {
                plugin.ProcessRecipe(ai.Item as UMAPackedRecipeBase);
            }

            StringBuilder sb = new StringBuilder();

            List<AssetItem> SerializedItems = UMAAssetIndexer.Instance.UpdateSerializedList();
            foreach (AssetItem ai in SerializedItems)
            {
                List<string> labels = plugin.ProcessItem(ai);
                if (labels != null && labels.Count > 0)
                {
                    bool found = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ai.Item.GetInstanceID(), out string itemGUID, out long localID);
                    if (found)
                    {
                        ai.IsAddressable = true;
                        ai.AddressableAddress = ""; // let the system assign it if we are generating.
                        ai.AddressableGroup = sharedGroup.name;

                        AddItemToSharedGroup(itemGUID, ai.AddressableAddress, labels, sharedGroup);
#if INCL_TEXTURES
                        if (IsOverlayItem(ai))
                        {
                            OverlayDataAsset od = ai.Item as OverlayDataAsset;
                            if (od == null)
                            {
                                Debug.Log("Invalid overlay in recipe: " + ai._Name + ". Skipping.");
                                continue;
                            }
                            foreach (Texture tex in od.textureList)
                            {
                                if (tex == null) continue;
                                if (tex as Texture2D == null)
                                {
                                    Debug.Log("Texture is not Texture2D!!! " + tex.name);
                                    continue;
                                }
                                string Address = "Texture2D-" + tex.name + "-" + tex.GetInstanceID();

                                found = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tex.GetInstanceID(), out string texGUID, out long texlocalID);
                                if (found)
                                {
                                    AddItemToSharedGroup(texGUID, AssetItem.AddressableFolder + Address, labels, sharedGroup);
                                }
                            }
                        }
#endif
                        sb.Clear();
                        foreach (string s in labels)
                        {
                            // add the label to the item
                            sb.Append(s);
                            sb.Append(';');
                        }
                        ai.AddressableLabels = sb.ToString();
                    }
                }
            }

            plugin.Complete();
        }

        public void GenerateAddressables()
        {
            try
            {
                //**********************************************************************************************
                //*  Clear out the old data
                //**********************************************************************************************
                SlotTracker = new Dictionary<int, List<UMATextRecipe>>();
                OverlayTracker = new Dictionary<int, List<UMATextRecipe>>();
                TextureTracker = new Dictionary<int, List<UMATextRecipe>>();
                GroupTracker = new Dictionary<int, AddressableAssetGroup>();

                ClearAddressableFlags(typeof(SlotDataAsset));
                ClearAddressableFlags(typeof(OverlayDataAsset));

                // Will generate an editor context if needed.
                UMAContextBase context = UMAAssetIndexer.Instance.GetContext();

                // Create the shared group that has each item packed separately.
                AddressableAssetGroup sharedGroup = AddressableUtility.AddressableSettings.CreateGroup(SharedGroupName, false, false, true, AddressableUtility.AddressableSettings.DefaultGroup.Schemas);
                sharedGroup.GetSchema<BundledAssetGroupSchema>().BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;

                List<UMATextRecipe> theRecipes = new List<UMATextRecipe>();

                //**********************************************************************************************
                //*  Add Races
                //**********************************************************************************************

                System.Type theType = UMAAssetIndexer.Instance.GetIndexedType(typeof(RaceData));
                var races = UMAAssetIndexer.Instance.GetAssetDictionary(theType).Values;

                foreach (AssetItem ai in races)
                {
                    RaceData race = ai.Item as RaceData;
                    if (race == null)
                    {
                        Debug.Log("Invalid race found!");
                        continue;
                    }
                    if (race.baseRaceRecipe as UMATextRecipe == null)
                    {
                        Debug.Log("Invalid base race recipe on race: " + race.raceName);
                    }
                    theRecipes.Add(race.baseRaceRecipe as UMATextRecipe);
                    if (ai.IsAlwaysLoaded)
                    {
                        AssetItem recipe = UMAAssetIndexer.Instance.GetAssetItem<UMATextRecipe>(race.baseRaceRecipe.name);
                        recipe.IsAlwaysLoaded = true;

                        List<AssetItem> recipeItems = UMAAssetIndexer.Instance.GetAssetItems(race.baseRaceRecipe as UMAPackedRecipeBase, true);
                        foreach (AssetItem recipeitem in recipeItems)
                        {
                            recipeitem.IsAlwaysLoaded = true;
                        }
                    }
                }



                var theRecipeItems = GetAddressableRecipes();

                GenerateCollectionLabels();

                GenerateLookups(context, theRecipeItems);

                float pos = 0.0f;
                float inc = 1.0f / theRecipes.Count;

                const string tprefix = "UTR_";
                const string wprefix = "UWR_";

                // Create the Addressable groups
                foreach (AssetItem recipeItem in theRecipeItems)
                {
                    UMATextRecipe uwr = recipeItem.Item as UMATextRecipe;
                    int iPos = Mathf.CeilToInt(pos);
                    EditorUtility.DisplayProgressBar("Generating", "Creating Group: " + uwr.name, iPos);
                    Debug.Log("Generating group: " + uwr.name);
                    string groupName;
                    if (uwr is UMAWardrobeRecipe)
                    {
                        groupName = wprefix + uwr.name;
                    }
                    else
                    {
                        groupName = tprefix + uwr.name;
                    }
                    AddressableAssetGroup recipeGroup = AddressableUtility.AddressableSettings.CreateGroup(groupName, false, false, true, AddressableUtility.AddressableSettings.DefaultGroup.Schemas);
                    recipeGroup.GetSchema<BundledAssetGroupSchema>().BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;

                    if (GroupTracker.ContainsKey(uwr.GetInstanceID()))
                    {
                        Debug.Log("Group already exists????? " + uwr.name);
                        continue;
                    }
                    GroupTracker.Add(uwr.GetInstanceID(), recipeGroup);
                    pos += inc;
                }

                AddAddressableAssets(SlotTracker, sharedGroup);
                AddAddressableAssets(OverlayTracker, sharedGroup);
                AddAddressableAssets(TextureTracker, sharedGroup);

                AssignAddressableInformation();

                ReleaseReferences(UMAAssetIndexer.Instance.GetIndexedType(typeof(SlotDataAsset)));
                ReleaseReferences(UMAAssetIndexer.Instance.GetIndexedType(typeof(OverlayDataAsset)));

                CleanupAddressables(true);

            }
            finally
            {
                EditorUtility.ClearProgressBar();
                UMAAssetIndexer.Instance.DestroyEditorUMAContextBase();
                UMAAssetIndexer.Instance.ForceSave();
            }
        }

        public void AssignAddressableInformation()
        {
            List<AssetItem> SerializedItems = UMAAssetIndexer.Instance.UpdateSerializedList();
            foreach (AssetItem ai in SerializedItems)
            {
                AddressableAssetEntry ae = AddressableUtility.GetAddressableAssetEntry(ai._Path);
                if (ae != null)
                {
                    ai.AddressableAddress = ae.address;
                    ai.IsAddressable = true;
                    ai.AddressableGroup = ae.parentGroup.Name;
                    ai._SerializedItem = null;

                    ai.AddressableLabels = "";
                    foreach (string s in ae.labels)
                    {
                        ai.AddressableLabels += s + ";";
                    }
                }
                else
                {
                    ai.AddressableAddress = "";
                    ai.AddressableGroup = "";
                    ai.IsAddressable = false;
                    ai.AddressableLabels = "";
                }
            }
        }

        public void CleanupOrphans(Type type)
        {
            var items = UMAAssetIndexer.Instance.GetAssetDictionary(type);

            List<string> toRemove = new List<string>();
            foreach (KeyValuePair<string, AssetItem> pair in items)
            {
                if (pair.Value.IsAddressable == false && pair.Value.IsResource == false)
                {
                    toRemove.Add(pair.Key);
                }
            }

            foreach (var key in toRemove)
            {
                items.Remove(key);
            }
            UMAAssetIndexer.Instance.ForceSave();
        }

        public void ReleaseReferences(Type type)
        {
            var items = UMAAssetIndexer.Instance.GetAssetDictionary(type).Values;
            foreach (AssetItem ai in items)
            {
                //if (ai.IsAlwaysLoaded)
                //{
                //ai.CacheSerializedItem();
                //}
                //else
                //{
                ai._SerializedItem = null;
                //}
            }
        }

        public void ClearAddressableFlags(Type type)
        {
            var items = UMAAssetIndexer.Instance.GetAssetDictionary(type).Values;
            foreach (AssetItem ai in items)
            {
                if (ai.IsAddressable)
                {
                    ClearAddressableAssetEntry(ai);
                }
                ai.IsAddressable = false;
                ai.AddressableAddress = "";
                ai.AddressableLabels = "";
                ai._SerializedItem = null;
            }
        }
#endif
#endif
                    }
}