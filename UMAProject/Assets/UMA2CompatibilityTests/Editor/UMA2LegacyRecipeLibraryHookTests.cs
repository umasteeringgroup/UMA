using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Covers <see cref="UMALegacyRecipeLibraryHook"/>: a UMA 2 wardrobe recipe added to the Global Library gets
    /// the Mesh Hide Assets for the slots it hides and the UMA 3 race that replaces its UMA 2 race (not when it
    /// uses the UMA 2 head slot), once, and only that recipe is saved.
    /// The tests use real UMA 2 recipes, but always on renamed copies in a temporary folder (new GUIDs). The
    /// shipped recipes are never written (their bytes are checked in TearDown), and the Global Library file is
    /// backed up and must be byte-identical afterwards (it is restored from the backup if not, and the test fails).
    /// </summary>
    public sealed class UMA2LegacyRecipeLibraryHookTests
    {
        const string RpgWardrobe = "Assets/UMA2Content/UMA RPG Starter 1/Wardrobe/";
        const string MPoor2Boots = RpgWardrobe + "M Poor 2/MPoor2Boots.asset";
        const string ChainmailShirtM = RpgWardrobe + "Chainmail/Male/ChainmailShirtM.asset";
        const string FFace1 = RpgWardrobe + "FacePack/Female/FFace1.asset";
        const string FEyebrow2 = RpgWardrobe + "FacePack/Female/FEyebrow2.asset";
        const string MHair8 = RpgWardrobe + "HairPack/Male/MHair8.asset";
        const string MaleHandsMid = "Assets/UMA2/Races/HumanMale/Recipes/WardrobeRecipes/MaleHandsMid.asset";
        const string FemaleHandsMid = "Assets/UMA2/Races/HumanFemale/Recipes/Wardrobe Recipes/HumanFemale/FemaleHandsMid.asset";
        const string FemaleSwappedFace = "Assets/UMA2/Races/HumanFemale/Recipes/Wardrobe Recipes/HumanFemale/FemaleSwappedFace.asset";

        string folder;
        string id;
        UMAAssetIndexer indexer;
        string indexerPath;
        byte[] indexerBackup;
        bool indexerWasDirty;
        List<AssetItem> itemsBefore;
        readonly Dictionary<string, string> sourceHashes = new Dictionary<string, string>();
        bool savedHookAllow;
        bool savedRegAllow;
        bool savedSetting;
        string savedState;
        Dictionary<string, List<string>> savedFolderFilters;

        [SetUp]
        public void SetUp()
        {
            indexer = UMAAssetIndexer.Instance;
            Assert.That(indexer, Is.Not.Null, "The Global Library is not available.");
            indexerPath = AssetDatabase.GetAssetPath(indexer);
            indexerBackup = File.ReadAllBytes(indexerPath);
            indexerWasDirty = EditorUtility.IsDirty(indexer);
            itemsBefore = new List<AssetItem>(indexer.SerializedItems);

            savedHookAllow = UMALegacyRecipeLibraryHook.AllowDuringTestRuns;
            savedRegAllow = UMAGlobalLibraryAutoRegistration.AllowDuringTestRuns;
            savedState = SessionState.GetString(UMAGlobalLibraryAutoRegistration.StateKey, string.Empty);
            UMAGlobalLibraryAutoRegistration.ResetState();
            savedFolderFilters = indexer.TypeFolderSearch;
            savedSetting = UMASettings.GetOrCreateSettings().autoAddToGlobalLibrary;
            // Copies are not auto-added unless a test turns this on (the import tests).
            UMASettings.GetOrCreateSettings().autoAddToGlobalLibrary = false;

            Dictionary<string, RaceData> map = UMALegacyRecipeLibraryHook.GetRaceMap();
            if (!map.ContainsKey("HumanMale") || !map.ContainsKey("HumanFemale"))
            {
                Assert.Ignore("Human Male 3.0 / Human Female 3.0 with a UMA 2 Legacy Rig are not in the Global Library.");
            }

            id = Guid.NewGuid().ToString("N").Substring(0, 10);
            folder = "Assets/__UMALegacyRecipeHookTests_" + id;
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
        }

        [TearDown]
        public void TearDown()
        {
            var problems = new List<string>();
            try
            {
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }
            finally
            {
                UMALegacyRecipeLibraryHook.AllowDuringTestRuns = savedHookAllow;
                UMAGlobalLibraryAutoRegistration.ResetState();
                if (!string.IsNullOrEmpty(savedState)) SessionState.SetString(UMAGlobalLibraryAutoRegistration.StateKey, savedState);
                UMAGlobalLibraryAutoRegistration.ReloadState();
                UMAGlobalLibraryAutoRegistration.AllowDuringTestRuns = savedRegAllow;
                UMASettings.GetOrCreateSettings().autoAddToGlobalLibrary = savedSetting;
                if (indexer != null && savedFolderFilters != null) indexer.TypeFolderSearch = savedFolderFilters;

                // The library in memory goes back to what it held before the test.
                if (indexer != null && !indexer.SerializedItems.SequenceEqual(itemsBefore))
                {
                    indexer.SerializedItems = new List<AssetItem>(itemsBefore);
                    indexer.RebuildIndex();
                }

                byte[] now = File.ReadAllBytes(indexerPath);
                if (!now.SequenceEqual(indexerBackup))
                {
                    // Written by the import test: save it again without the test item, the way the auto-registration
                    // saves (every item from the file's own record). Must give back the original bytes.
                    InvokeSaveIndexer();
                    now = File.ReadAllBytes(indexerPath);
                    if (!now.SequenceEqual(indexerBackup))
                    {
                        File.WriteAllBytes(indexerPath, indexerBackup);
                        AssetDatabase.ImportAsset(indexerPath, ImportAssetOptions.ForceUpdate);
                        UMAAssetIndexer.Instance?.RebuildIndex();
                        problems.Add("The Global Library asset was not byte-identical to the backup after the test (restored from the backup).");
                    }
                }
                UMAAssetIndexer current = UMAAssetIndexer.Instance;
                if (!indexerWasDirty && current != null) EditorUtility.ClearDirty(current);

                foreach (KeyValuePair<string, string> kv in sourceHashes)
                {
                    if (Hash(kv.Key) != kv.Value) problems.Add("The shipped recipe " + kv.Key + " changed on disk.");
                }
                sourceHashes.Clear();
            }
            if (problems.Count > 0) Assert.Fail(string.Join(" ", problems));
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>A renamed copy (new GUID) of a shipped UMA 2 recipe in the test folder.</summary>
        UMAWardrobeRecipe CopyRecipe(string source)
        {
            if (!File.Exists(source)) Assert.Ignore("UMA 2 recipe not installed: " + source);
            byte[] bytes = File.ReadAllBytes(source);
            string text = Encoding.UTF8.GetString(bytes);
            Assert.That(text.StartsWith("%YAML"), Is.True, source + " is not a text (YAML) asset.");
            if (!sourceHashes.ContainsKey(source)) sourceHashes.Add(source, Hash(source));

            string name = "HookTest_" + id + "_" + Path.GetFileNameWithoutExtension(source);
            string renamed = Regex.Replace(text, @"^(  m_Name: )[^\r\n]*", "${1}" + name, RegexOptions.Multiline);
            Assert.That(renamed, Is.Not.EqualTo(text), "m_Name not found in " + source);
            string path = folder + "/" + name + ".asset";
            File.WriteAllText(path, renamed, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var recipe = AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(path);
            Assert.That(recipe, Is.Not.Null, "Copy of " + source + " did not import.");
            Assert.That(recipe.name, Is.EqualTo(name));
            return recipe;
        }

        static string Hash(string path)
        {
            using (SHA256 sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        static string GuidOf(Object o) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o));

        /// <summary>The recipe as it is stored on disk (YAML text).</summary>
        static string DiskText(Object recipe) => File.ReadAllText(AssetDatabase.GetAssetPath(recipe));

        static int CountOnDisk(Object recipe, string what) => Regex.Matches(DiskText(recipe), Regex.Escape(what)).Count;

        static List<string> HideNames(UMALegacyRecipeFixResult r) => r.HidesAdded.Select(h => h.name).ToList();

        static void InvokeSaveIndexer()
        {
            MethodInfo save = typeof(UMAGlobalLibraryAutoRegistration).GetMethod("SaveIndexer", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(save, Is.Not.Null, "UMAGlobalLibraryAutoRegistration.SaveIndexer not found.");
            save.Invoke(null, new object[] { UMAAssetIndexer.Instance, new HashSet<string>() });
        }

        void AssertSavedOnce(UMAWardrobeRecipe recipe, UMALegacyRecipeFixResult result)
        {
            Assert.That(result.Saved, Is.True, "The changed recipe is saved.");
            Assert.That(EditorUtility.IsDirty(recipe), Is.False);
            foreach (MeshHideAsset hide in result.HidesAdded)
            {
                Assert.That(CountOnDisk(recipe, GuidOf(hide)), Is.EqualTo(1), hide.name + " is stored once in the recipe file.");
            }
            foreach (string race in result.RacesAdded)
            {
                Assert.That(Regex.Matches(DiskText(recipe), "^  - " + Regex.Escape(race) + "\\r?$", RegexOptions.Multiline).Count, Is.EqualTo(1),
                    race + " is listed once in compatibleRaces on disk.");
            }
        }

        void AssertSecondRunChangesNothing(UMAWardrobeRecipe recipe)
        {
            string before = DiskText(recipe);
            int hides = recipe.MeshHideAssets.Count;
            int races = recipe.compatibleRaces.Count;
            UMALegacyRecipeFixResult again = UMALegacyRecipeLibraryHook.Apply(recipe, true);
            Assert.That(again.Changed, Is.False, "Running it again: " + again.Describe());
            Assert.That(recipe.MeshHideAssets.Count, Is.EqualTo(hides));
            Assert.That(recipe.compatibleRaces.Count, Is.EqualTo(races));
            Assert.That(DiskText(recipe), Is.EqualTo(before), "Not saved again.");
        }

        // ---------------------------------------------------------------- tests

        [Test]
        public void MPoor2BootsGetsItsMissingFeetHide()
        {
            UMAWardrobeRecipe recipe = CopyRecipe(MPoor2Boots);
            Assert.That(recipe.compatibleRaces, Does.Contain("Human Male 3.0"));

            UMALegacyRecipeFixResult r = UMALegacyRecipeLibraryHook.Apply(recipe, true);
            Debug.Log("UMA2LegacyRecipeLibraryHookTests: " + r.Describe());

            Assert.That(r.IsLegacyRecipe, Is.True);
            Assert.That(HideNames(r), Is.EqualTo(new[] { "LegacyHumanMale_MaleFeet_UMA30_Body_UDIM1004" }));
            Assert.That(r.RacesAdded, Is.Empty, "Already compatible with Human Male 3.0.");
            Assert.That(r.HiddenSlotsWithoutMeshHide, Is.Empty);
            AssertSavedOnce(recipe, r);
            AssertSecondRunChangesNothing(recipe);
        }

        [TestCase(MaleHandsMid, "Human Male 3.0", "LegacyHumanMale_MaleHands_UMA30_Body_UDIM1005")]
        [TestCase(FemaleHandsMid, "Human Female 3.0", "LegacyHumanFemale_FemaleHands_UMA30_Body_UDIM1005")]
        public void HandsRecipeGetsTheUma3RaceAndTheHandHide(string source, string race, string hide)
        {
            UMAWardrobeRecipe recipe = CopyRecipe(source);
            Assert.That(recipe.compatibleRaces, Does.Not.Contain(race));

            UMALegacyRecipeFixResult r = UMALegacyRecipeLibraryHook.Apply(recipe, true);
            Debug.Log("UMA2LegacyRecipeLibraryHookTests: " + r.Describe());

            Assert.That(r.RacesAdded, Is.EqualTo(new[] { race }));
            Assert.That(HideNames(r), Is.EqualTo(new[] { hide }));
            Assert.That(recipe.compatibleRaces, Does.Contain(race));
            AssertSavedOnce(recipe, r);
            AssertSecondRunChangesNothing(recipe);
        }

        [Test]
        public void SwappedFaceGetsTheRaceAndHeadHidesAndReportsTheEyebrows()
        {
            UMAWardrobeRecipe recipe = CopyRecipe(FemaleSwappedFace);

            UMALegacyRecipeFixResult r = UMALegacyRecipeLibraryHook.Apply(recipe, true);
            Debug.Log("UMA2LegacyRecipeLibraryHookTests: " + r.Describe());

            Assert.That(r.UsesLegacyHead, Is.False, "Its head is its own FemaleHead_* slots, not the UMA 2 head slot.");
            Assert.That(r.RacesAdded, Is.EqualTo(new[] { "Human Female 3.0" }));
            Assert.That(HideNames(r), Is.EqualTo(new[]
            {
                "LegacyHumanFemale_F_High poly head_UMA30_Body_UDIM1001",
                "LegacyHumanFemale_F_High poly head_UMA30_Body_UDIM1002",
                "LegacyHumanFemale_F_High poly head_UMA30_Body_UDIM1003",
            }));
            Assert.That(r.HiddenSlotsWithoutMeshHide, Is.EqualTo(new[] { "F_High poly Eyebrows" }), "No mesh hide exists for it: reported, not guessed.");
            AssertSavedOnce(recipe, r);
            AssertSecondRunChangesNothing(recipe);
        }

        [TestCase(FFace1, "Human Female 3.0")]
        [TestCase(FEyebrow2, "Human Female 3.0")]
        [TestCase(MHair8, "Human Male 3.0")]
        public void RecipeOnTheUma2HeadSlotDoesNotGetTheUma3Race(string source, string race)
        {
            UMAWardrobeRecipe recipe = CopyRecipe(source);
            string before = DiskText(recipe);

            UMALegacyRecipeFixResult r = UMALegacyRecipeLibraryHook.Apply(recipe, true);
            Debug.Log("UMA2LegacyRecipeLibraryHookTests: " + r.Describe());

            Assert.That(r.IsLegacyRecipe, Is.True);
            Assert.That(r.UsesLegacyHead, Is.True);
            Assert.That(r.RacesSkipped, Is.EqualTo(new[] { race }));
            Assert.That(r.Changed, Is.False, "Neither the race nor mesh hides (they would only act on the UMA 3 race).");
            Assert.That(recipe.compatibleRaces, Does.Not.Contain(race));
            Assert.That(r.Saved, Is.False);
            Assert.That(DiskText(recipe), Is.EqualTo(before));
        }

        [Test]
        public void ConvertedRecipeIsUnchangedAndReportsTheUnmappedOrcSlot()
        {
            UMAWardrobeRecipe recipe = CopyRecipe(ChainmailShirtM);
            string before = DiskText(recipe);

            UMALegacyRecipeFixResult r = UMALegacyRecipeLibraryHook.Apply(recipe, true);
            Debug.Log("UMA2LegacyRecipeLibraryHookTests: " + r.Describe());

            Assert.That(r.Changed, Is.False, r.Describe());
            Assert.That(r.HiddenSlotsWithoutMeshHide, Is.EqualTo(new[] { "HumanMale2_Torso" }));
            Assert.That(DiskText(recipe), Is.EqualTo(before));
        }

        [Test]
        public void Uma3RecipeIsNotTouched()
        {
            UMAWardrobeRecipe source = null;
            foreach (string guid in AssetDatabase.FindAssets("t:UMAWardrobeRecipe", new[] { "Assets/UMA/UMA3" }))
            {
                var r = AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(AssetDatabase.GUIDToAssetPath(guid));
                if (r != null && r.compatibleRaces.Contains("Human Male 3.0") && !r.compatibleRaces.Contains("HumanMale") && !r.compatibleRaces.Contains("HumanFemale"))
                {
                    source = r;
                    break;
                }
            }
            if (source == null) Assert.Ignore("No UMA 3 wardrobe recipe found.");
            UMAWardrobeRecipe clone = Object.Instantiate(source);
            try
            {
                int races = clone.compatibleRaces.Count;
                int hides = clone.MeshHideAssets.Count;
                UMALegacyRecipeFixResult result = UMALegacyRecipeLibraryHook.Apply(clone, false);
                Assert.That(result.IsLegacyRecipe, Is.False);
                Assert.That(result.Changed, Is.False);
                Assert.That(clone.compatibleRaces.Count, Is.EqualTo(races));
                Assert.That(clone.MeshHideAssets.Count, Is.EqualTo(hides));
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }

        [Test]
        public void RecipeWithUnsavedEditsIsChangedButNotSaved()
        {
            UMAWardrobeRecipe recipe = CopyRecipe(MaleHandsMid);
            string before = DiskText(recipe);
            recipe.DisplayValue = "Unsaved edit " + id;
            EditorUtility.SetDirty(recipe);

            UMALegacyRecipeFixResult r = UMALegacyRecipeLibraryHook.Apply(recipe, true);

            Assert.That(r.Changed, Is.True);
            Assert.That(r.LeftUnsaved, Is.True);
            Assert.That(r.Saved, Is.False);
            Assert.That(DiskText(recipe), Is.EqualTo(before), "The owner's pending edit is not written.");
        }

        [Test]
        public void EvilAddAssetBringsTheRecipeToUma3OnceAndSavesOnlyIt()
        {
            UMAWardrobeRecipe recipe = CopyRecipe(MaleHandsMid);
            UMALegacyRecipeLibraryHook.AllowDuringTestRuns = true;
            int saves = UMALegacyRecipeLibraryHook.SaveCount;

            Assert.That(indexer.EvilAddAsset(typeof(UMAWardrobeRecipe), recipe), Is.True);
            Assert.That(indexer.EvilAddAsset(typeof(UMAWardrobeRecipe), recipe), Is.True, "Added a second time.");

            Assert.That(UMALegacyRecipeLibraryHook.SaveCount, Is.EqualTo(saves + 1), "Saved once, on the first add.");
            Assert.That(recipe.compatibleRaces.Count(r => r == "Human Male 3.0"), Is.EqualTo(1));
            var hide = recipe.MeshHideAssets.Where(h => h != null && h.name == "LegacyHumanMale_MaleHands_UMA30_Body_UDIM1005").ToList();
            Assert.That(hide.Count, Is.EqualTo(1));
            Assert.That(CountOnDisk(recipe, GuidOf(hide[0])), Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(indexerPath).SequenceEqual(indexerBackup), Is.True, "EvilAddAsset does not save the library.");
            // The race lookup was built after the recipe got its race.
            Assert.That(indexer.GetRecipesForRaceSlot("Human Male 3.0", recipe.wardrobeSlot).Select(x => x != null ? x.name : null), Does.Contain(recipe.name));
        }

        [Test]
        public void HookDoesNothingDuringOtherTestRuns()
        {
            UMAWardrobeRecipe recipe = CopyRecipe(MaleHandsMid);
            UMALegacyRecipeLibraryHook.AllowDuringTestRuns = false;
            if (UMALegacyRecipeLibraryHook.CanRun()) Assert.Ignore("Not inside a Test Runner run (run by reflection).");
            string before = DiskText(recipe);

            indexer.EvilAddAsset(typeof(UMAWardrobeRecipe), recipe);

            Assert.That(recipe.compatibleRaces, Does.Not.Contain("Human Male 3.0"));
            Assert.That(DiskText(recipe), Is.EqualTo(before));
        }

        [Test]
        public void ImportedUma2RecipeIsAddedToTheLibraryAndFixed()
        {
            UMASettings.GetOrCreateSettings().autoAddToGlobalLibrary = true;
            UMAGlobalLibraryAutoRegistration.AllowDuringTestRuns = true;
            UMALegacyRecipeLibraryHook.AllowDuringTestRuns = true;
            indexer.TypeFolderSearch = new Dictionary<string, List<string>>();

            UMAWardrobeRecipe recipe = CopyRecipe(MPoor2Boots);
            int changes = UMAGlobalLibraryAutoRegistration.ProcessPending();

            Assert.That(changes, Is.EqualTo(1), "One recipe added.");
            string guid = GuidOf(recipe);
            Assert.That(indexer.GetAssetDictionary(typeof(UMAWardrobeRecipe)).Values.Count(ai => ai != null && ai._Guid == guid), Is.EqualTo(1));
            Assert.That(recipe.MeshHideAssets.Count(h => h != null && h.name == "LegacyHumanMale_MaleFeet_UMA30_Body_UDIM1004"), Is.EqualTo(1));
            Assert.That(EditorUtility.IsDirty(recipe), Is.False, "The recipe is saved.");
            Assert.That(File.ReadAllBytes(indexerPath).SequenceEqual(indexerBackup), Is.False, "The library is saved with the new recipe.");

            // Saving the recipe re-imports it; the queued re-import changes nothing more.
            Assert.That(UMAGlobalLibraryAutoRegistration.ProcessPending(), Is.EqualTo(0));
            Assert.That(indexer.SerializedItems.Count(ai => ai != null && ai._Guid == guid), Is.EqualTo(1), "No duplicate library entry.");
        }

        [Test]
        public void ImportedUma3RecipeIsNotAutoAdded()
        {
            UMASettings.GetOrCreateSettings().autoAddToGlobalLibrary = true;
            UMAGlobalLibraryAutoRegistration.AllowDuringTestRuns = true;
            UMALegacyRecipeLibraryHook.AllowDuringTestRuns = true;
            indexer.TypeFolderSearch = new Dictionary<string, List<string>>();

            var recipe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            recipe.compatibleRaces = new List<string> { "Human Male 3.0" };
            AssetDatabase.CreateAsset(recipe, folder + "/HookTest_" + id + "_Uma3.asset");

            Assert.That(UMAGlobalLibraryAutoRegistration.ProcessPending(), Is.EqualTo(0));
            string guid = GuidOf(recipe);
            Assert.That(indexer.SerializedItems.Any(ai => ai != null && ai._Guid == guid), Is.False);
        }

        [Test]
        public void DeletedUma2RecipeIsRemovedFromTheLibrary()
        {
            UMASettings.GetOrCreateSettings().autoAddToGlobalLibrary = true;
            UMAGlobalLibraryAutoRegistration.AllowDuringTestRuns = true;
            UMALegacyRecipeLibraryHook.AllowDuringTestRuns = true;
            indexer.TypeFolderSearch = new Dictionary<string, List<string>>();

            UMAWardrobeRecipe recipe = CopyRecipe(MPoor2Boots);
            Assert.That(UMAGlobalLibraryAutoRegistration.ProcessPending(), Is.EqualTo(1), "Added on import.");
            UMAGlobalLibraryAutoRegistration.ProcessPending(); // the re-import after the recipe was saved
            string guid = GuidOf(recipe);
            string path = AssetDatabase.GetAssetPath(recipe);
            string name = recipe.name;
            string slot = recipe.wardrobeSlot;
            Assert.That(indexer.GetRecipesForRaceSlot("Human Male 3.0", slot).Select(x => x != null ? x.name : null), Does.Contain(name));
            // A second entry for the same file, as a repeated explicit add leaves behind.
            indexer.SerializedItems.Add(new AssetItem(typeof(UMAWardrobeRecipe), recipe));

            Assert.That(AssetDatabase.DeleteAsset(path), Is.True);
            int changes = UMAGlobalLibraryAutoRegistration.ProcessPending();

            Assert.That(changes, Is.EqualTo(1), "One removal.");
            Assert.That(indexer.GetAssetDictionary(typeof(UMAWardrobeRecipe)).Values.Any(ai => ai != null && ai._Guid == guid), Is.False);
            Assert.That(indexer.SerializedItems.Any(ai => ai != null && ai._Guid == guid), Is.False, "No entry is left for the deleted file.");
            Assert.That(indexer.GetRecipesForRaceSlot("Human Male 3.0", slot).Select(x => x != null ? x.name : null), Does.Not.Contain(name));
            Assert.That(File.ReadAllBytes(indexerPath).SequenceEqual(indexerBackup), Is.True,
                "The library is saved without the recipe, which gives back the file as it was before the test.");
        }

        [Test]
        public void Uma2RecipeWornInTheEditorIsBroughtToUma3BeforeTheRaceCheck()
        {
            UMAWardrobeRecipe recipe = CopyRecipe(MaleHandsMid);
            UMALegacyRecipeLibraryHook.AllowDuringTestRuns = true;
            RaceData race = UMALegacyRecipeLibraryHook.GetRaceMap()["HumanMale"];
            Assert.That(recipe.compatibleRaces, Does.Not.Contain(race.raceName));
            int saves = UMALegacyRecipeLibraryHook.SaveCount;

            // How an avatar's default wardrobe picks what it wears in edit mode.
            var wardrobe = new DynamicCharacterAvatar.WardrobeRecipeList();
            wardrobe.recipes.Add(new DynamicCharacterAvatar.WardrobeRecipeListItem(recipe));
            List<DynamicCharacterAvatar.WardrobeRecipeListItem> worn = wardrobe.GetRecipesForRace(race.raceName, race);

            Assert.That(worn.Select(w => w._recipe), Does.Contain(recipe), "Worn on " + race.raceName + " once brought to UMA 3.");
            Assert.That(recipe.compatibleRaces.Count(r => r == race.raceName), Is.EqualTo(1));
            Assert.That(recipe.MeshHideAssets.Count(h => h != null && h.name == "LegacyHumanMale_MaleHands_UMA30_Body_UDIM1005"), Is.EqualTo(1));
            Assert.That(EditorUtility.IsDirty(recipe), Is.False, "The recipe is saved.");
            Assert.That(UMALegacyRecipeLibraryHook.SaveCount, Is.EqualTo(saves + 1));

            // Every avatar rebuild wears it again; it is not checked again until it or the project changes.
            wardrobe.GetRecipesForRace(race.raceName, race);
            Assert.That(UMALegacyRecipeLibraryHook.ApplyWhenWorn(recipe), Is.Null);
            Assert.That(UMALegacyRecipeLibraryHook.SaveCount, Is.EqualTo(saves + 1));
            Assert.That(File.ReadAllBytes(indexerPath).SequenceEqual(indexerBackup), Is.True, "Wearing a recipe does not change the library.");
        }

        [Test]
        public void Uma2HeadSlotRecipeWornInTheEditorStaysOffTheUma3Race()
        {
            UMAWardrobeRecipe recipe = CopyRecipe(FFace1);
            UMALegacyRecipeLibraryHook.AllowDuringTestRuns = true;
            RaceData race = UMALegacyRecipeLibraryHook.GetRaceMap()["HumanFemale"];
            string before = DiskText(recipe);

            var wardrobe = new DynamicCharacterAvatar.WardrobeRecipeList();
            wardrobe.recipes.Add(new DynamicCharacterAvatar.WardrobeRecipeListItem(recipe));
            List<DynamicCharacterAvatar.WardrobeRecipeListItem> worn = wardrobe.GetRecipesForRace(race.raceName, race);

            Assert.That(worn, Is.Empty);
            Assert.That(recipe.compatibleRaces, Does.Not.Contain(race.raceName));
            Assert.That(DiskText(recipe), Is.EqualTo(before));
        }
    }
}
