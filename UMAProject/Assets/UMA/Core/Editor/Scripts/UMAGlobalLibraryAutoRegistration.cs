using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UMA.CharacterSystem;
using UMA.Editors.PackageSupport;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Adds new slots, overlays and races to the Global Library (<see cref="UMAAssetIndexer"/>) when they are
    /// created, imported, duplicated or moved into the project, and removes them when they are deleted.
    /// The work is queued during an import and applied once the import has finished (from the Editor
    /// update loop, when Unity is neither compiling nor importing); the library asset is then saved once,
    /// on its own (<c>AssetDatabase.SaveAssetIfDirty</c>), never with <c>AssetDatabase.SaveAssets</c>.
    /// The save writes the real additions and removals and keeps every other library item as the file
    /// stores it (with or without an object reference); items that exist only in memory are not written.
    /// Each batch is reported in the Console and in a short Scene view notification.
    /// UMA 2 wardrobe recipes (compatible with a UMA 2 race that a UMA 3 race replaces) are added too, after
    /// <see cref="UMA.Editors.UMALegacyRecipeLibraryHook"/> has brought them to the UMA 3 races; other recipes are not.
    /// A deleted wardrobe recipe (UMA 2 or UMA 3) is removed from the library.
    /// Turned on and off with the "Auto-add to Global Library" setting in Project Settings > UMA.
    /// Assets whose "No Auto Add" flag is set are left alone.
    /// </summary>
    [InitializeOnLoad]
    public static class UMAGlobalLibraryAutoRegistration
    {
        /// <summary>SessionState key that keeps queued work across script reloads.</summary>
        public const string StateKey = "UMA.GlobalLibraryAutoRegistration.State";

        static readonly Type[] HandledTypes = { typeof(SlotDataAsset), typeof(OverlayDataAsset), typeof(RaceData) };

        // Deleted assets of these types are removed from the library. Wardrobe recipes are included: once the
        // file is gone it can no longer be read to tell a UMA 2 recipe from a UMA 3 one, and the entry is dead either way.
        static readonly Type[] RemovedTypes = { typeof(SlotDataAsset), typeof(OverlayDataAsset), typeof(RaceData), typeof(UMAWardrobeRecipe) };

        const int Added = 0, Removed = 1, Moved = 2;

        struct Change
        {
            public string typeName;
            public string name;
            public string path;
            public int kind;
            public string key; // ItemKey of the added or moved item
        }

        [Serializable]
        class State
        {
            public List<string> pendingImports = new List<string>();
            public List<string> pendingDeletes = new List<string>();
        }

        static State state;

        /// <summary>Raised after this class changed and saved the Global Library.</summary>
        public static event Action Changed;

        /// <summary>How many times this class has saved the Global Library in this Editor session.</summary>
        public static int SaveCount { get; private set; }

        /// <summary>
        /// UMA does not register assets while a Unity Test Framework run is active, so tests that create
        /// temporary slots do not change the library. Tests of this class set this to true.
        /// </summary>
        public static bool AllowDuringTestRuns { get; set; }

        static UMAGlobalLibraryAutoRegistration()
        {
            // Work queued before a script reload is kept in SessionState and finished afterwards.
            if (HasPendingWork) Schedule();
        }

        #region State

        static State LoadState()
        {
            if (state != null) return state;
            string json = SessionState.GetString(StateKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try { state = JsonUtility.FromJson<State>(json); }
                catch (Exception) { state = null; }
            }
            if (state == null) state = new State();
            if (state.pendingImports == null) state.pendingImports = new List<string>();
            if (state.pendingDeletes == null) state.pendingDeletes = new List<string>();
            return state;
        }

        static void SaveState()
        {
            if (state == null) return;
            if (state.pendingImports.Count == 0 && state.pendingDeletes.Count == 0)
            {
                SessionState.EraseString(StateKey);
            }
            else
            {
                SessionState.SetString(StateKey, JsonUtility.ToJson(state));
            }
        }

        /// <summary>
        /// Forgets queued work and drops the cached copy so the next call reads <see cref="StateKey"/>
        /// from SessionState again. Does not touch the library.
        /// </summary>
        public static void ResetState()
        {
            state = null;
            SessionState.EraseString(StateKey);
            EditorApplication.update -= OnUpdate;
        }

        /// <summary>Drops the cached copy so the next call reads <see cref="StateKey"/> from SessionState again.</summary>
        public static void ReloadState()
        {
            state = null;
            if (HasPendingWork) Schedule();
        }

        /// <summary>True while queued imports or deletions are waiting for the import to finish.</summary>
        public static bool HasPendingWork
        {
            get
            {
                State s = LoadState();
                return s.pendingImports.Count > 0 || s.pendingDeletes.Count > 0;
            }
        }

        #endregion

        #region Queue

        /// <summary>Called by <see cref="ImportProcessor"/> from OnPostprocessAllAssets.</summary>
        public static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (!CanRun()) return;

            State s = LoadState();
            bool queued = false;
            queued |= Queue(s.pendingImports, importedAssets);
            queued |= Queue(s.pendingImports, movedAssets);
            queued |= Queue(s.pendingDeletes, deletedAssets);
            if (!queued) return;

            SaveState();
            Schedule();
        }

        static bool Queue(List<string> list, string[] paths)
        {
            if (paths == null) return false;
            bool any = false;
            for (int i = 0; i < paths.Length; i++)
            {
                string p = paths[i];
                if (!IsCandidatePath(p)) continue;
                if (!list.Contains(p)) list.Add(p);
                any = true;
            }
            return any;
        }

        static bool IsCandidatePath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);
        }

        static void Schedule()
        {
            // Run once from the Editor update loop after the import has finished, so a whole import is
            // handled (and the library saved) once. delayCall does not fire while Unity is in the background.
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
        }

        static void OnUpdate()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return; // finish after play mode
            EditorApplication.update -= OnUpdate;
            ProcessPending();
        }

        #endregion

        #region Guards

        /// <summary>True when the setting is on and nothing is running that registration must stay out of.</summary>
        public static bool CanRun()
        {
            if (!UMASettings.AutoAddToGlobalLibrary) return false;
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return false;
            if (BuildPipeline.isBuildingPlayer) return false;
            if (UnityEditorInternal.InternalEditorUtility.inBatchMode) return false;
            if (IsPluginInstallActive()) return false;
            if (!AllowDuringTestRuns && IsTestRunActive()) return false;
            return true;
        }

        static bool IsPluginInstallActive()
        {
            // The plugin installer rebuilds the Global Library itself once its import has finished.
            try
            {
                return UMAContentPackageInstaller.IsInstallingAllPlugins
                    || UMAPluginPackageDownload.IsActive
                    || File.Exists("Library/UMA/ContentInstaller/pending.json")
                    || File.Exists("Library/UMA/PluginRemoval/pending.json");
            }
            catch (Exception)
            {
                return false;
            }
        }

        static MethodInfo isRunActive;
        static bool isRunActiveLooked;

        internal static bool IsTestRunActive()
        {
            try
            {
                if (!isRunActiveLooked)
                {
                    isRunActiveLooked = true;
                    Type api = Type.GetType("UnityEditor.TestTools.TestRunner.Api.TestRunnerApi, UnityEditor.TestRunner");
                    if (api != null)
                    {
                        isRunActive = api.GetMethod("IsRunActive", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    }
                }
                return isRunActive != null && (bool)isRunActive.Invoke(null, null);
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region Processing

        /// <summary>
        /// Applies the queued imports and deletions now and saves the library once if anything changed.
        /// Normally called once from the Editor update loop after an import; tests call it directly.
        /// </summary>
        /// <returns>The number of library changes made.</returns>
        public static int ProcessPending()
        {
            State s = LoadState();
            if (s.pendingImports.Count == 0 && s.pendingDeletes.Count == 0) return 0;

            if (!CanRun())
            {
                // Turned off, or a build/install/test started meanwhile: drop the queue.
                s.pendingImports.Clear();
                s.pendingDeletes.Clear();
                SaveState();
                return 0;
            }

            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null)
            {
                Schedule(); // not available during a reload; try again next update
                return 0;
            }

            List<string> imports = new List<string>(s.pendingImports);
            List<string> deletes = new List<string>(s.pendingDeletes);
            s.pendingImports.Clear();
            s.pendingDeletes.Clear();
            SaveState();

            var batch = new List<Change>();
            var outsideSearchFolders = new List<string>();
            bool needsRebuild = false;

            try
            {
                for (int i = 0; i < deletes.Count; i++)
                {
                    string path = deletes[i];
                    if (AssetDatabase.GetMainAssetTypeAtPath(path) != null) continue; // re-created at the same path
                    needsRebuild |= RemoveItemsAtPath(indexer, path, batch);
                }

                for (int i = 0; i < imports.Count; i++)
                {
                    needsRebuild |= AddOrUpdate(indexer, imports[i], batch, outsideSearchFolders);
                }

                if (needsRebuild)
                {
                    indexer.RebuildIndex();
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            if (outsideSearchFolders.Count > 0)
            {
                Debug.Log("UMA Global Library: " + outsideSearchFolders.Count + " new UMA asset(s) were not added because they are outside the folders set for their type " +
                          "in UMA > Global Library Filters (a library rebuild skips them too):\n  " + string.Join("\n  ", outsideSearchFolders));
            }

            if (batch.Count > 0)
            {
                var touched = new HashSet<string>();
                foreach (Change c in batch)
                {
                    if (!string.IsNullOrEmpty(c.key)) touched.Add(c.key);
                }
                SaveIndexer(indexer, touched);
                Announce(batch);
                Changed?.Invoke();
            }
            return batch.Count;
        }

        /// <summary>
        /// Adds one slot, overlay or race to the Global Library on request (an inspector's "Add to Global
        /// Index" button) and saves the library alone, writing only that item. Unlike
        /// <see cref="UMAAssetIndexer.ForceSave"/> this never saves the project's other unsaved assets.
        /// Works whether or not automatic registration is turned on.
        /// </summary>
        /// <returns>True when the asset was added and the library saved.</returns>
        public static bool AddAndSave(UnityEngine.Object asset)
        {
            if (asset == null) return false;
            Type lookupType = GetLookupType(asset.GetType());
            if (lookupType == null) return false;
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null) return false;

            var ai = new AssetItem(lookupType, asset);
            if (!indexer.AddAssetItem(ai, noDirty: true)) return false;

            SaveIndexer(indexer, new HashSet<string> { ItemKey(ai) });
            Changed?.Invoke();
            return true;
        }

        static bool AddOrUpdate(UMAAssetIndexer indexer, string path, List<Change> batch, List<string> outsideSearchFolders)
        {
            Type mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (mainType == typeof(UMAWardrobeRecipe))
            {
                return AddLegacyWardrobeRecipe(indexer, path, batch, outsideSearchFolders);
            }
            Type lookupType = GetLookupType(mainType);
            if (lookupType == null) return false;

            string guid = AssetDatabase.AssetPathToGUID(path);
            Dictionary<string, AssetItem> dic = indexer.GetAssetDictionary(lookupType);
            AssetItem existing = FindByGuid(dic, guid);

            // New items follow the same folder rules as a library rebuild. Items already in the
            // library are kept wherever they are moved.
            if (existing == null && !PassesLibraryFilters(indexer, lookupType, path))
            {
                outsideSearchFolders.Add(path);
                return false;
            }

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(path, mainType);
            if (asset == null) return false;
            if (existing == null && asset is IUMAIndexOptions options && options.NoAutoAdd) return false;

            string name = AssetItem.GetEvilName(asset);
            if (string.IsNullOrWhiteSpace(name)) return false;

            bool rebuild = false;

            // Already in the library under this GUID: keep it, follow a move or rename.
            if (existing != null)
            {
                if (existing._Name == name)
                {
                    if (!string.Equals(existing._Path, path, StringComparison.Ordinal))
                    {
                        batch.Add(new Change { typeName = lookupType.Name, name = name, path = path, kind = Moved, key = ItemKey(existing) });
                        existing._Path = path;
                    }
                    return false;
                }

                // The asset was renamed and its library name changed with it.
                RemoveItem(indexer, lookupType, existing, batch);
                rebuild = true;
            }

            if (dic.TryGetValue(name, out AssetItem sameName) && sameName != null)
            {
                string otherPath = string.IsNullOrEmpty(sameName._Guid) ? string.Empty : AssetDatabase.GUIDToAssetPath(sameName._Guid);
                bool otherExists = !string.IsNullOrEmpty(otherPath) && AssetDatabase.GetMainAssetTypeAtPath(otherPath) != null;
                if (otherExists)
                {
                    Debug.LogWarning($"UMA: '{path}' was not added to the Global Library because the {Describe(lookupType.Name, 1)} name '{name}' is already used by '{otherPath}'. Give it a unique name, then add it from the Global Library window.", asset);
                    return rebuild;
                }

                // The entry points at an asset that no longer exists: replace it.
                RemoveItem(indexer, lookupType, sameName, batch);
                rebuild = true;
            }

            var ai = new AssetItem(lookupType, asset);
            if (!indexer.AddAssetItem(ai, noDirty: true)) return rebuild;

            batch.Add(new Change { typeName = lookupType.Name, name = ai._Name, path = ai._Path, kind = Added, key = ItemKey(ai) });
            return rebuild;
        }

        /// <summary>
        /// A newly imported UMA 2 wardrobe recipe (compatible with a UMA 2 race that a UMA 3 race replaces) is
        /// brought to the UMA 3 races by <see cref="UMA.Editors.UMALegacyRecipeLibraryHook"/>, saved, and added to
        /// the library. Other wardrobe recipes, and recipes already in the library, are left alone.
        /// </summary>
        static bool AddLegacyWardrobeRecipe(UMAAssetIndexer indexer, string path, List<Change> batch, List<string> outsideSearchFolders)
        {
            Type lookupType = typeof(UMAWardrobeRecipe);
            string guid = AssetDatabase.AssetPathToGUID(path);
            Dictionary<string, AssetItem> dic = indexer.GetAssetDictionary(lookupType);
            if (FindByGuid(dic, guid) != null) return false;

            var recipe = AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(path);
            if (recipe == null || recipe.NoAutoAdd) return false;
            if (!UMA.Editors.UMALegacyRecipeLibraryHook.IsLegacyRecipe(recipe)) return false;
            if (!PassesLibraryFilters(indexer, lookupType, path))
            {
                outsideSearchFolders.Add(path);
                return false;
            }

            string name = AssetItem.GetEvilName(recipe);
            if (string.IsNullOrWhiteSpace(name)) return false;

            bool rebuild = false;
            if (dic.TryGetValue(name, out AssetItem sameName) && sameName != null)
            {
                string otherPath = string.IsNullOrEmpty(sameName._Guid) ? string.Empty : AssetDatabase.GUIDToAssetPath(sameName._Guid);
                bool otherExists = !string.IsNullOrEmpty(otherPath) && AssetDatabase.GetMainAssetTypeAtPath(otherPath) != null;
                if (otherExists)
                {
                    Debug.LogWarning($"UMA: '{path}' was not added to the Global Library because the wardrobe recipe name '{name}' is already used by '{otherPath}'. Give it a unique name, then add it from the Global Library window.", recipe);
                    return false;
                }
                RemoveItem(indexer, lookupType, sameName, batch);
                rebuild = true;
            }

            UMA.Editors.UMALegacyRecipeLibraryHook.ApplyAndReport(recipe);

            var ai = new AssetItem(lookupType, recipe);
            if (!indexer.AddAssetItem(ai, noDirty: true)) return rebuild;
            batch.Add(new Change { typeName = lookupType.Name, name = ai._Name, path = ai._Path, kind = Added, key = ItemKey(ai) });
            return rebuild;
        }

        static bool RemoveItemsAtPath(UMAAssetIndexer indexer, string path, List<Change> batch)
        {
            bool removed = false;
            for (int t = 0; t < RemovedTypes.Length; t++)
            {
                Type lookupType = RemovedTypes[t];
                Dictionary<string, AssetItem> dic = indexer.GetAssetDictionary(lookupType);
                var matches = new List<AssetItem>();
                foreach (AssetItem ai in dic.Values)
                {
                    if (ai != null && string.Equals(ai._Path, path, StringComparison.OrdinalIgnoreCase)) matches.Add(ai);
                }
                for (int i = 0; i < matches.Count; i++)
                {
                    RemoveItem(indexer, lookupType, matches[i], batch);
                    removed = true;
                }
            }

            // The library can hold the same asset more than once (only one entry is in the lookup), and the
            // rebuild that follows would bring a leftover copy back. Drop every remaining entry for the path.
            List<AssetItem> items = indexer.SerializedItems;
            for (int i = 0; i < items.Count; i++)
            {
                AssetItem ai = items[i];
                if (ai == null || !string.Equals(ai._Path, path, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsRemovedTypeName(ai._BaseTypeName)) continue;
                items[i] = null;
                if (!removed) batch.Add(new Change { typeName = ai._BaseTypeName, name = ai._Name, path = ai._Path, kind = Removed });
                removed = true;
            }
            return removed;
        }

        static bool IsRemovedTypeName(string typeName)
        {
            for (int i = 0; i < RemovedTypes.Length; i++)
            {
                if (RemovedTypes[i].Name == typeName) return true;
            }
            return false;
        }

        static void RemoveItem(UMAAssetIndexer indexer, Type lookupType, AssetItem ai, List<Change> batch)
        {
            batch.Add(new Change { typeName = lookupType.Name, name = ai._Name, path = ai._Path, kind = Removed });
            indexer.RemoveAsset(lookupType, ai._Name, false);
        }

        static AssetItem FindByGuid(Dictionary<string, AssetItem> dic, string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            foreach (AssetItem ai in dic.Values)
            {
                if (ai != null && ai._Guid == guid) return ai;
            }
            return null;
        }

        static Type GetLookupType(Type mainType)
        {
            if (mainType == null) return null;
            for (int i = 0; i < HandledTypes.Length; i++)
            {
                if (HandledTypes[i].IsAssignableFrom(mainType)) return HandledTypes[i];
            }
            return null;
        }

        /// <summary>The same folder rules a library rebuild uses: backup folders and per-type search folders.</summary>
        static bool PassesLibraryFilters(UMAAssetIndexer indexer, Type lookupType, string path)
        {
            if (UMASettings.IgnoreBackupFolders && path.ToLowerInvariant().Contains("backup")) return false;

            if (indexer.TypeFolderSearch != null &&
                indexer.TypeFolderSearch.TryGetValue(lookupType.Name, out List<string> folders) &&
                folders != null && folders.Count > 0)
            {
                string fixedPath = path.Replace("\\", "/").ToLowerInvariant();
                for (int i = 0; i < folders.Count; i++)
                {
                    if (string.IsNullOrEmpty(folders[i])) continue;
                    if (fixedPath.Contains(folders[i].Replace("\\", "/").ToLowerInvariant())) return true;
                }
                return false;
            }
            return true;
        }

        #endregion

        #region Saving

        struct SavedItemState
        {
            public AssetItem item;
            public UnityEngine.Object reference;
            public int referenceCount;
            public int index;
        }

        /// <summary>
        /// Saves the Global Library asset alone, writing only this import's changes. Every item already in
        /// the file that this import did not touch is written from the file's own record, exactly as stored
        /// (with or without its object reference, its reference count, path, name and index). The Editor
        /// caches references and counts and corrects stale fields in memory while it works, and a plain
        /// save would write all of that. Moved or renamed items keep their stored form with the new path or
        /// name; new items are stored like most items of their kind in the file. Items that only exist in
        /// memory (no asset GUID, such as runtime-baked slots) are left out. The in-memory library is unchanged.
        /// </summary>
        /// <param name="touched">Keys (<see cref="ItemKey"/>) of the items this import added, moved or renamed.</param>
        static void SaveIndexer(UMAAssetIndexer indexer, HashSet<string> touched)
        {
            string path = AssetDatabase.GetAssetPath(indexer);
            if (string.IsNullOrEmpty(path)) return;

            UMAAssetIndexer fileCopy = null;
            UnityEngine.Object[] loaded = null;
            try
            {
                if (File.Exists(path))
                {
                    loaded = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(path);
                    if (loaded != null)
                    {
                        foreach (UnityEngine.Object o in loaded)
                        {
                            if (o is UMAAssetIndexer copy) fileCopy = copy;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("UMA: could not read the saved Global Library to keep its stored form (" + ex.Message + "); saving it as it is in memory.");
                fileCopy = null;
            }

            if (fileCopy == null)
            {
                EditorUtility.SetDirty(indexer);
                AssetDatabase.SaveAssetIfDirty(indexer);
                SaveCount++;
                DestroyLoaded(loaded);
                return;
            }

            // How the file stores each item, and whether most items of a kind keep their reference.
            // The library can hold the same asset more than once; such entries are matched in order.
            var stored = new Dictionary<string, Queue<AssetItem>>();
            var withReference = new Dictionary<string, int>();
            var ofKind = new Dictionary<string, int>();
            foreach (AssetItem fi in fileCopy.SerializedItems)
            {
                if (fi == null) continue;
                string fileKey = ItemKey(fi);
                if (!stored.TryGetValue(fileKey, out Queue<AssetItem> entries)) stored[fileKey] = entries = new Queue<AssetItem>();
                entries.Enqueue(fi);
                string kind = fi._BaseTypeName ?? string.Empty;
                ofKind.TryGetValue(kind, out int n);
                ofKind[kind] = n + 1;
                if (fi._SerializedItem != null)
                {
                    withReference.TryGetValue(kind, out int r);
                    withReference[kind] = r + 1;
                }
            }

            List<AssetItem> inMemory = indexer.SerializedItems;
            var toSave = new List<AssetItem>(inMemory.Count);
            var restore = new List<SavedItemState>(inMemory.Count);
            try
            {
                foreach (AssetItem ai in inMemory)
                {
                    if (ai == null) continue;
                    restore.Add(new SavedItemState { item = ai, reference = ai._SerializedItem, referenceCount = ai.ReferenceCount, index = ai.Index });

                    string key = ItemKey(ai);
                    if (stored.TryGetValue(key, out Queue<AssetItem> matches) && matches.Count > 0)
                    {
                        AssetItem fi = matches.Dequeue();
                        if (!touched.Contains(key))
                        {
                            // Not changed by this import: write the file's own record, unchanged. (The
                            // Editor also corrects stale paths, names and indexes in memory; those are
                            // not this import's changes and are left for a library save to write.)
                            toSave.Add(fi);
                            continue;
                        }

                        // Moved or renamed by this import: the new path or name, in the stored form.
                        ai._SerializedItem = fi._SerializedItem != null ? (ai._SerializedItem != null ? ai._SerializedItem : fi._SerializedItem) : null;
                        ai.ReferenceCount = fi.ReferenceCount;
                        ai.Index = fi.Index;
                        toSave.Add(ai);
                        continue;
                    }
                    else
                    {
                        bool persistent = !string.IsNullOrEmpty(ai._Guid) && !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(ai._Guid));
                        if (!persistent) continue; // exists only in memory

                        string kind = ai._BaseTypeName ?? string.Empty;
                        ofKind.TryGetValue(kind, out int total);
                        withReference.TryGetValue(kind, out int refs);
                        bool keepReference = total > 0 && refs * 2 > total;
                        if (keepReference) ai.CacheSerializedItem();
                        else ai._SerializedItem = null;
                        ai.ReferenceCount = 0;
                    }
                    ai.Index = toSave.Count;
                    toSave.Add(ai);
                }

                indexer.SerializedItems = toSave;
                EditorUtility.SetDirty(indexer);
                AssetDatabase.SaveAssetIfDirty(indexer);
                SaveCount++;
            }
            finally
            {
                indexer.SerializedItems = inMemory;
                foreach (SavedItemState r in restore)
                {
                    r.item._SerializedItem = r.reference;
                    r.item.ReferenceCount = r.referenceCount;
                    r.item.Index = r.index;
                }
                DestroyLoaded(loaded);
            }
        }

        static string ItemKey(AssetItem ai)
        {
            string kind = ai._BaseTypeName ?? string.Empty;
            return string.IsNullOrEmpty(ai._Guid) ? kind + "|name|" + ai._Name : kind + "|" + ai._Guid;
        }

        static void DestroyLoaded(UnityEngine.Object[] loaded)
        {
            if (loaded == null) return;
            foreach (UnityEngine.Object o in loaded)
            {
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }
        }

        #endregion

        #region Reporting

        static void Announce(List<Change> batch)
        {
            int added = 0, removed = 0, moved = 0;
            var kinds = new Dictionary<string, int>();
            foreach (Change c in batch)
            {
                if (c.kind == Added) { added++; kinds.TryGetValue(c.typeName, out int n); kinds[c.typeName] = n + 1; }
                else if (c.kind == Removed) removed++;
                else moved++;
            }

            var parts = new List<string>();
            if (added > 0)
            {
                var what = new List<string>();
                foreach (var kv in kinds) what.Add(kv.Value + " " + Describe(kv.Key, kv.Value));
                parts.Add("added " + string.Join(", ", what));
            }
            if (removed > 0) parts.Add("removed " + removed + (removed == 1 ? " deleted item" : " deleted items"));
            if (moved > 0) parts.Add("updated the path of " + moved + (moved == 1 ? " moved item" : " moved items"));
            string summary = "UMA Global Library: " + string.Join("; ", parts) + " (saved).";

            var sb = new StringBuilder(summary);
            sb.Append('\n');
            foreach (Change c in batch)
            {
                string verb = c.kind == Added ? "Added" : c.kind == Removed ? "Removed" : "Moved";
                sb.Append("  ").Append(verb).Append(' ').Append(Describe(c.typeName, 1)).Append(" '").Append(c.name).Append("' (").Append(c.path).Append(")\n");
            }
            Debug.Log(sb.ToString());

            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                view.ShowNotification(new GUIContent(summary), 3.0);
            }
        }

        static string Describe(string typeName, int count)
        {
            string word = typeName == nameof(SlotDataAsset) ? "slot" : typeName == nameof(OverlayDataAsset) ? "overlay" : typeName == nameof(RaceData) ? "race" :
                typeName == nameof(UMAWardrobeRecipe) ? "wardrobe recipe" : typeName;
            return count == 1 ? word : word + "s";
        }

        #endregion
    }
}
