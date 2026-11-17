using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Covers <see cref="UMAGlobalLibraryAutoRegistration"/>: new slots, overlays and races go into the
    /// Global Library when created, duplicated or moved, and leave it when deleted; the library asset is
    /// saved once per finished import, alone, keeping every other item as the file stores it; "No Auto Add",
    /// the Global Library Filters and the UMASettings toggle are honoured.
    /// Every test works in its own temporary folder and backs up AssetIndexer.asset first. Deleting the
    /// folder in TearDown removes the test items again, after which the file must be byte-identical to
    /// the backup (it is restored from the backup if not, and the test fails).
    /// </summary>
    public sealed class UMAGlobalLibraryAutoRegistrationTests
    {
        private string folder;
        private string id;
        private UMAAssetIndexer indexer;
        private string indexerPath;
        private byte[] indexerBackup;
        private bool indexerWasDirty;
        private HashSet<string> itemsBefore;
        private string savedState;
        private bool savedSetting;
        private bool savedAllow;
        private Dictionary<string, List<string>> savedFolderFilters;

        private struct StoredItem
        {
            public string name;
            public string path;
            public bool hasReference;
            public int referenceCount;
        }

        [SetUp]
        public void SetUp()
        {
            indexer = UMAAssetIndexer.Instance;
            Assert.That(indexer, Is.Not.Null, "The Global Library is not available.");
            indexerPath = AssetDatabase.GetAssetPath(indexer);
            Assert.That(File.Exists(indexerPath), Is.True, "Global Library asset not found at " + indexerPath);
            indexerBackup = File.ReadAllBytes(indexerPath);
            indexerWasDirty = EditorUtility.IsDirty(indexer);
            itemsBefore = SnapshotItems();

            savedState = SessionState.GetString(UMAGlobalLibraryAutoRegistration.StateKey, string.Empty);
            UMAGlobalLibraryAutoRegistration.ResetState();
            savedAllow = UMAGlobalLibraryAutoRegistration.AllowDuringTestRuns;
            UMAGlobalLibraryAutoRegistration.AllowDuringTestRuns = true;

            // The project's Global Library Filters may limit types to folders such as Assets/UMA; the
            // tests use their own folder, so they run without filters (restored in TearDown).
            savedFolderFilters = indexer.TypeFolderSearch;
            indexer.TypeFolderSearch = new Dictionary<string, List<string>>();

            UMASettings settings = UMASettings.GetOrCreateSettings();
            savedSetting = settings.autoAddToGlobalLibrary;
            settings.autoAddToGlobalLibrary = true;

            id = Guid.NewGuid().ToString("N").Substring(0, 12);
            folder = "Assets/__UMAGlobalLibraryAutoRegTests_" + id;
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            UMAGlobalLibraryAutoRegistration.ProcessPending();
        }

        [TearDown]
        public void TearDown()
        {
            string problem = null;
            try
            {
                // Deleting the folder takes the test items back out of the library (and saves it).
                UMASettings.GetOrCreateSettings().autoAddToGlobalLibrary = true;
                UMAGlobalLibraryAutoRegistration.AllowDuringTestRuns = true;
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
                UMAGlobalLibraryAutoRegistration.ProcessPending();

                HashSet<string> after = SnapshotItems();
                var added = after.Except(itemsBefore).ToList();
                var missing = itemsBefore.Except(after).ToList();
                if (added.Count > 0 || missing.Count > 0)
                {
                    problem = "Global Library in memory differs after the test. Extra: " + string.Join("; ", added.Take(5)) +
                              " Missing: " + string.Join("; ", missing.Take(5));
                }
            }
            finally
            {
                UMAGlobalLibraryAutoRegistration.ResetState();
                if (!string.IsNullOrEmpty(savedState)) SessionState.SetString(UMAGlobalLibraryAutoRegistration.StateKey, savedState);
                UMAGlobalLibraryAutoRegistration.ReloadState();
                UMAGlobalLibraryAutoRegistration.AllowDuringTestRuns = savedAllow;
                if (indexer != null && savedFolderFilters != null) indexer.TypeFolderSearch = savedFolderFilters;
                UMASettings.GetOrCreateSettings().autoAddToGlobalLibrary = savedSetting;

                byte[] now = File.ReadAllBytes(indexerPath);
                if (!now.SequenceEqual(indexerBackup))
                {
                    File.WriteAllBytes(indexerPath, indexerBackup);
                    AssetDatabase.ImportAsset(indexerPath, ImportAssetOptions.ForceUpdate);
                    UMAAssetIndexer.Instance?.RebuildIndex();
                    problem = (problem == null ? string.Empty : problem + " ") +
                              "After the test items were removed, the Global Library asset was not byte-identical to the backup (restored from the backup).";
                }
                UMAAssetIndexer current = UMAAssetIndexer.Instance;
                if (!indexerWasDirty && current != null) EditorUtility.ClearDirty(current);
            }
            if (problem != null) Assert.Fail(problem);
        }

        // ---------------------------------------------------------------- helpers

        private HashSet<string> SnapshotItems()
        {
            var set = new HashSet<string>();
            foreach (AssetItem ai in indexer.SerializedItems)
            {
                if (ai == null) continue;
                set.Add(ai._BaseTypeName + "|" + ai._Guid + "|" + ai._Name + "|" + ai._Path);
            }
            return set;
        }

        /// <summary>What the Global Library asset on disk stores, keyed by type and GUID (or name).</summary>
        private Dictionary<string, StoredItem> ReadFile()
        {
            var result = new Dictionary<string, StoredItem>();
            Object[] loaded = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(indexerPath);
            try
            {
                UMAAssetIndexer copy = loaded.OfType<UMAAssetIndexer>().Single();
                foreach (AssetItem ai in copy.SerializedItems)
                {
                    if (ai == null) continue;
                    string key = ai._BaseTypeName + "|" + (string.IsNullOrEmpty(ai._Guid) ? "name:" + ai._Name : ai._Guid);
                    result[key] = new StoredItem { name = ai._Name, path = ai._Path, hasReference = ai._SerializedItem != null, referenceCount = ai.ReferenceCount };
                }
            }
            finally
            {
                foreach (Object o in loaded) if (o != null) Object.DestroyImmediate(o);
            }
            return result;
        }

        private static string FileKey(Type type, Object asset) => type.Name + "|" + GuidOf(asset);

        private T Create<T>(string fileName, Action<T> configure = null) where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            asset.name = Path.GetFileName(fileName);
            configure?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, folder + "/" + fileName + ".asset");
            return asset;
        }

        private static string GuidOf(Object o) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o));

        private List<AssetItem> ItemsWithGuid(Type lookupType, string guid)
        {
            return indexer.GetAssetDictionary(lookupType).Values.Where(ai => ai != null && ai._Guid == guid).ToList();
        }

        private int SerializedCountWithGuid(string guid)
        {
            return indexer.SerializedItems.Count(ai => ai != null && ai._Guid == guid);
        }

        // ---------------------------------------------------------------- tests

        [Test]
        public void CreatedSlotOverlayAndRaceAreAddedAndSavedOnce()
        {
            SlotDataAsset slot = Create<SlotDataAsset>("AutoRegSlot_" + id);
            OverlayDataAsset overlay = Create<OverlayDataAsset>("AutoRegOverlay_" + id);
            RaceData race = Create<RaceData>("AutoRegRace_" + id);
            int saves = UMAGlobalLibraryAutoRegistration.SaveCount;

            int changes = UMAGlobalLibraryAutoRegistration.ProcessPending();

            Assert.That(changes, Is.EqualTo(3));
            Assert.That(UMAGlobalLibraryAutoRegistration.SaveCount, Is.EqualTo(saves + 1), "One save for the whole import.");
            Assert.That(ItemsWithGuid(typeof(SlotDataAsset), GuidOf(slot)).Single()._Name, Is.EqualTo(slot.slotName));
            Assert.That(ItemsWithGuid(typeof(OverlayDataAsset), GuidOf(overlay)).Single()._Name, Is.EqualTo(overlay.overlayName));
            Assert.That(ItemsWithGuid(typeof(RaceData), GuidOf(race)).Single()._Name, Is.EqualTo(race.raceName));
            Assert.That(indexer.HasAsset<SlotDataAsset>(slot.slotName), Is.True);
            Assert.That(EditorUtility.IsDirty(indexer), Is.False, "The library is saved.");

            Dictionary<string, StoredItem> file = ReadFile();
            Assert.That(file[FileKey(typeof(SlotDataAsset), slot)].name, Is.EqualTo(slot.slotName));
            Assert.That(file[FileKey(typeof(OverlayDataAsset), overlay)].name, Is.EqualTo(overlay.overlayName));
            Assert.That(file[FileKey(typeof(RaceData), race)].name, Is.EqualTo(race.raceName));
        }

        [Test]
        public void SaveKeepsEveryOtherItemAsTheFileStoresIt()
        {
            Dictionary<string, StoredItem> before = ReadFile();
            SlotDataAsset slot = Create<SlotDataAsset>("AutoRegSlot_" + id);
            UMAGlobalLibraryAutoRegistration.ProcessPending();
            Dictionary<string, StoredItem> after = ReadFile();

            Assert.That(after.Count, Is.EqualTo(before.Count + 1));
            var changed = before.Where(kv => !after.TryGetValue(kv.Key, out StoredItem now) ||
                                             now.name != kv.Value.name || now.path != kv.Value.path ||
                                             now.hasReference != kv.Value.hasReference ||
                                             now.referenceCount != kv.Value.referenceCount)
                                .Select(kv => kv.Key).ToList();
            Assert.That(changed, Is.Empty, "Existing items must be stored exactly as before: " + string.Join(", ", changed.Take(5)));

            // The new item is stored like most slots in the file (with or without its reference).
            var slots = before.Where(kv => kv.Key.StartsWith(nameof(SlotDataAsset) + "|")).Select(kv => kv.Value).ToList();
            bool siblingsKeepReferences = slots.Count > 0 && slots.Count(s => s.hasReference) * 2 > slots.Count;
            StoredItem added = after[FileKey(typeof(SlotDataAsset), slot)];
            Assert.That(added.hasReference, Is.EqualTo(siblingsKeepReferences));
            Assert.That(added.referenceCount, Is.Zero);
        }

        [Test]
        public void ItemsThatOnlyExistInMemoryAreNotSaved()
        {
            // Like the slots UMA bakes at run time: in the library, but not an asset.
            string bakedName = "AutoRegBaked_" + id;
            SlotDataAsset baked = ScriptableObject.CreateInstance<SlotDataAsset>();
            baked.name = bakedName;
            try
            {
                Assert.That(indexer.AddAssetItem(new AssetItem(typeof(SlotDataAsset), bakedName, string.Empty, baked), noDirty: true), Is.True);

                Create<SlotDataAsset>("AutoRegSlot_" + id);
                UMAGlobalLibraryAutoRegistration.ProcessPending();

                Assert.That(ReadFile().Values.Any(s => s.name == bakedName), Is.False, "The in-memory item must not be written.");
                Assert.That(indexer.GetAssetDictionary(typeof(SlotDataAsset)).ContainsKey(bakedName), Is.True, "It stays in memory.");
            }
            finally
            {
                indexer.RemoveAsset(typeof(SlotDataAsset), bakedName, false);
                indexer.RebuildIndex();
                Object.DestroyImmediate(baked);
            }
        }

        [Test]
        public void SaveWritesTheLibraryAlone()
        {
            // An unrelated asset with unsaved edits must stay unsaved.
            SlotDataAsset other = Create<SlotDataAsset>("AutoRegOther_" + id, s => s.noAutoAdd = true);
            string otherPath = AssetDatabase.GetAssetPath(other);
            byte[] otherBefore = File.ReadAllBytes(otherPath);
            other._oldSlotName = "AutoRegOtherEdited_" + id;
            EditorUtility.SetDirty(other);

            SlotDataAsset slot = Create<SlotDataAsset>("AutoRegSlot_" + id);
            UMAGlobalLibraryAutoRegistration.ProcessPending();

            Assert.That(ReadFile().ContainsKey(FileKey(typeof(SlotDataAsset), slot)), Is.True, "The library is saved.");
            Assert.That(File.ReadAllBytes(otherPath), Is.EqualTo(otherBefore), "No other asset is saved.");
            Assert.That(EditorUtility.IsDirty(other), Is.True);
        }

        [Test]
        public void ReimportingAnItemAlreadyInTheLibraryChangesAndSavesNothing()
        {
            SlotDataAsset slot = Create<SlotDataAsset>("AutoRegSlot_" + id);
            UMAGlobalLibraryAutoRegistration.ProcessPending();
            int count = indexer.SerializedItems.Count;
            int saves = UMAGlobalLibraryAutoRegistration.SaveCount;
            byte[] file = File.ReadAllBytes(indexerPath);

            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(slot), ImportAssetOptions.ForceUpdate);
            int changes = UMAGlobalLibraryAutoRegistration.ProcessPending();

            Assert.That(changes, Is.Zero);
            Assert.That(UMAGlobalLibraryAutoRegistration.SaveCount, Is.EqualTo(saves));
            Assert.That(File.ReadAllBytes(indexerPath), Is.EqualTo(file));
            Assert.That(indexer.SerializedItems.Count, Is.EqualTo(count));
            Assert.That(SerializedCountWithGuid(GuidOf(slot)), Is.EqualTo(1));
        }

        [Test]
        public void DuplicatedSlotIsAddedUnderItsOwnName()
        {
            SlotDataAsset slot = Create<SlotDataAsset>("AutoRegSlot_" + id);
            UMAGlobalLibraryAutoRegistration.ProcessPending();

            string copyPath = folder + "/AutoRegSlotCopy_" + id + ".asset";
            Assert.That(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(slot), copyPath), Is.True);
            int changes = UMAGlobalLibraryAutoRegistration.ProcessPending();

            var copy = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(copyPath);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(ItemsWithGuid(typeof(SlotDataAsset), GuidOf(copy)).Single()._Name, Is.EqualTo("AutoRegSlotCopy_" + id));
            Assert.That(SerializedCountWithGuid(GuidOf(slot)), Is.EqualTo(1), "The original stays as it was.");
            Assert.That(ReadFile().ContainsKey(FileKey(typeof(SlotDataAsset), copy)), Is.True);
        }

        [Test]
        public void DuplicateWithTheSameSlotNameIsNotAddedAndWarns()
        {
            string slotName = "AutoRegNamedSlot_" + id;
            SlotDataAsset slot = Create<SlotDataAsset>("AutoRegSlotFile_" + id, s => s._oldSlotName = slotName);
            UMAGlobalLibraryAutoRegistration.ProcessPending();
            AssetItem original = indexer.GetAssetDictionary(typeof(SlotDataAsset))[slotName];
            int saves = UMAGlobalLibraryAutoRegistration.SaveCount;

            string copyPath = folder + "/AutoRegSlotFileCopy_" + id + ".asset";
            Assert.That(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(slot), copyPath), Is.True);
            var warnings = new List<string>();
            Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Warning) warnings.Add(message); };
            Application.logMessageReceived += capture;
            int changes;
            try { changes = UMAGlobalLibraryAutoRegistration.ProcessPending(); }
            finally { Application.logMessageReceived -= capture; }

            var expected = new Regex("was not added to the Global Library because the slot name '" + slotName + "' is already used");
            Assert.That(warnings.Any(w => expected.IsMatch(w)), Is.True, "Expected a duplicate-name warning. Got: " + string.Join(" | ", warnings));
            Assert.That(changes, Is.Zero);
            Assert.That(UMAGlobalLibraryAutoRegistration.SaveCount, Is.EqualTo(saves), "Nothing to save.");
            Assert.That(indexer.GetAssetDictionary(typeof(SlotDataAsset))[slotName], Is.SameAs(original));
            Assert.That(SerializedCountWithGuid(AssetDatabase.AssetPathToGUID(copyPath)), Is.Zero);
        }

        [Test]
        public void MovedItemKeepsItsEntryWithTheNewPath()
        {
            OverlayDataAsset overlay = Create<OverlayDataAsset>("AutoRegOverlay_" + id);
            UMAGlobalLibraryAutoRegistration.ProcessPending();
            string guid = GuidOf(overlay);
            int count = indexer.SerializedItems.Count;

            AssetDatabase.CreateFolder(folder, "Moved");
            string newPath = folder + "/Moved/AutoRegOverlay_" + id + ".asset";
            Assert.That(AssetDatabase.MoveAsset(AssetDatabase.GetAssetPath(overlay), newPath), Is.Empty);
            int changes = UMAGlobalLibraryAutoRegistration.ProcessPending();

            Assert.That(changes, Is.EqualTo(1));
            Assert.That(indexer.SerializedItems.Count, Is.EqualTo(count));
            Assert.That(ItemsWithGuid(typeof(OverlayDataAsset), guid).Single()._Path, Is.EqualTo(newPath));
            Assert.That(ReadFile()[nameof(OverlayDataAsset) + "|" + guid].path, Is.EqualTo(newPath), "The new path is saved.");
        }

        [Test]
        public void RenamedItemIsReplacedUnderItsNewName()
        {
            SlotDataAsset slot = Create<SlotDataAsset>("AutoRegSlot_" + id);
            UMAGlobalLibraryAutoRegistration.ProcessPending();
            string guid = GuidOf(slot);

            Assert.That(AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(slot), "AutoRegSlotRenamed_" + id), Is.Empty);
            UMAGlobalLibraryAutoRegistration.ProcessPending();

            var dic = indexer.GetAssetDictionary(typeof(SlotDataAsset));
            Assert.That(dic.ContainsKey("AutoRegSlot_" + id), Is.False);
            Assert.That(dic["AutoRegSlotRenamed_" + id]._Guid, Is.EqualTo(guid));
            Assert.That(SerializedCountWithGuid(guid), Is.EqualTo(1));
            Assert.That(ReadFile()[nameof(SlotDataAsset) + "|" + guid].name, Is.EqualTo("AutoRegSlotRenamed_" + id));
        }

        [Test]
        public void DeletedItemIsRemovedAndSaved()
        {
            RaceData race = Create<RaceData>("AutoRegRace_" + id);
            UMAGlobalLibraryAutoRegistration.ProcessPending();
            string key = FileKey(typeof(RaceData), race);
            string guid = GuidOf(race);
            string name = race.raceName;
            Assert.That(ReadFile().ContainsKey(key), Is.True);

            Assert.That(AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(race)), Is.True);
            UMAGlobalLibraryAutoRegistration.ProcessPending();

            Assert.That(SerializedCountWithGuid(guid), Is.Zero);
            Assert.That(indexer.GetAssetDictionary(typeof(RaceData)).ContainsKey(name), Is.False);
            Assert.That(indexer.SerializedItems.Contains(null), Is.False, "Removed entries are compacted.");
            Assert.That(ReadFile().ContainsKey(key), Is.False, "The removal is saved.");
            Assert.That(File.ReadAllBytes(indexerPath), Is.EqualTo(indexerBackup), "Added and removed again: the file is as it was.");
        }

        [Test]
        public void NoAutoAddAssetsAreSkipped()
        {
            int saves = UMAGlobalLibraryAutoRegistration.SaveCount;
            SlotDataAsset slot = Create<SlotDataAsset>("AutoRegSlot_" + id, s => s.noAutoAdd = true);
            OverlayDataAsset overlay = Create<OverlayDataAsset>("AutoRegOverlay_" + id, o => o.noAutoAdd = true);
            RaceData race = Create<RaceData>("AutoRegRace_" + id, r => r.noAutoAdd = true);

            int changes = UMAGlobalLibraryAutoRegistration.ProcessPending();

            Assert.That(changes, Is.Zero);
            Assert.That(SerializedCountWithGuid(GuidOf(slot)), Is.Zero);
            Assert.That(SerializedCountWithGuid(GuidOf(overlay)), Is.Zero);
            Assert.That(SerializedCountWithGuid(GuidOf(race)), Is.Zero);
            Assert.That(UMAGlobalLibraryAutoRegistration.SaveCount, Is.EqualTo(saves), "Nothing to save.");
            Assert.That(File.ReadAllBytes(indexerPath), Is.EqualTo(indexerBackup));
        }

        [Test]
        public void NewItemsOutsideTheGlobalLibraryFiltersAreSkippedButKnownItemsFollowMoves()
        {
            OverlayDataAsset known = Create<OverlayDataAsset>("AutoRegOverlayKnown_" + id);
            UMAGlobalLibraryAutoRegistration.ProcessPending();
            Assert.That(SerializedCountWithGuid(GuidOf(known)), Is.EqualTo(1));

            indexer.TypeFolderSearch[nameof(OverlayDataAsset)] = new List<string> { folder + "/OnlyHere" };
            OverlayDataAsset outside = Create<OverlayDataAsset>("AutoRegOverlayOutside_" + id);
            AssetDatabase.CreateFolder(folder, "Elsewhere");
            string movedPath = folder + "/Elsewhere/AutoRegOverlayKnown_" + id + ".asset";
            Assert.That(AssetDatabase.MoveAsset(AssetDatabase.GetAssetPath(known), movedPath), Is.Empty);
            UMAGlobalLibraryAutoRegistration.ProcessPending();

            Assert.That(SerializedCountWithGuid(GuidOf(outside)), Is.Zero, "A new overlay outside the filter folders is not added.");
            Assert.That(ItemsWithGuid(typeof(OverlayDataAsset), GuidOf(known)).Single()._Path, Is.EqualTo(movedPath),
                "An overlay already in the library keeps its entry when moved.");

            AssetDatabase.CreateFolder(folder, "OnlyHere");
            OverlayDataAsset inside = Create<OverlayDataAsset>("OnlyHere/AutoRegOverlayInside_" + id);
            UMAGlobalLibraryAutoRegistration.ProcessPending();
            Assert.That(SerializedCountWithGuid(GuidOf(inside)), Is.EqualTo(1), "A new overlay inside the filter folders is added.");
        }

        [Test]
        public void NothingHappensWhenTheSettingIsOff()
        {
            UMASettings.GetOrCreateSettings().autoAddToGlobalLibrary = false;
            int saves = UMAGlobalLibraryAutoRegistration.SaveCount;

            SlotDataAsset slot = Create<SlotDataAsset>("AutoRegSlot_" + id);
            Assert.That(UMAGlobalLibraryAutoRegistration.HasPendingWork, Is.False, "Nothing should be queued.");
            int changes = UMAGlobalLibraryAutoRegistration.ProcessPending();

            Assert.That(changes, Is.Zero);
            Assert.That(SerializedCountWithGuid(GuidOf(slot)), Is.Zero);
            Assert.That(UMAGlobalLibraryAutoRegistration.SaveCount, Is.EqualTo(saves));
            Assert.That(File.ReadAllBytes(indexerPath), Is.EqualTo(indexerBackup));

            UMASettings.GetOrCreateSettings().autoAddToGlobalLibrary = true;
            UMAGlobalLibraryAutoRegistration.ProcessPending();
            Assert.That(SerializedCountWithGuid(GuidOf(slot)), Is.Zero, "Assets imported while off are not picked up later.");
        }
    }
}
