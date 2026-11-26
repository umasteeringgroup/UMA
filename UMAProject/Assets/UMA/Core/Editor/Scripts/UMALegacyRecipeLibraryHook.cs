using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>What <see cref="UMALegacyRecipeLibraryHook.Apply"/> did to one wardrobe recipe.</summary>
    public sealed class UMALegacyRecipeFixResult
    {
        public UMAWardrobeRecipe Recipe;

        /// <summary>True when the recipe is compatible with a UMA 2 race that a UMA 3 race replaces.</summary>
        public bool IsLegacyRecipe;

        /// <summary>True when the recipe puts a slot on the UMA 2 head (so the UMA 3 race is not added).</summary>
        public bool UsesLegacyHead;

        public readonly List<MeshHideAsset> HidesAdded = new List<MeshHideAsset>();
        public readonly List<string> RacesAdded = new List<string>();

        /// <summary>UMA 3 races not added because the recipe uses the UMA 2 head slot.</summary>
        public readonly List<string> RacesSkipped = new List<string>();

        /// <summary>Hidden slots for which no matching Mesh Hide Asset exists.</summary>
        public readonly List<string> HiddenSlotsWithoutMeshHide = new List<string>();

        public readonly List<string> Warnings = new List<string>();

        /// <summary>True when the recipe was saved to disk.</summary>
        public bool Saved;

        /// <summary>True when the recipe changed but was not saved, because it already had unsaved edits.</summary>
        public bool LeftUnsaved;

        public bool Changed => HidesAdded.Count > 0 || RacesAdded.Count > 0;

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append('\'').Append(Recipe != null ? Recipe.name : "(none)").Append("': ");
            var parts = new List<string>();
            foreach (string race in RacesAdded) parts.Add("+race " + race);
            foreach (MeshHideAsset hide in HidesAdded) parts.Add("+mesh hide " + (hide != null ? hide.name : "(null)"));
            if (parts.Count == 0) parts.Add("no change");
            sb.Append(string.Join(", ", parts));
            if (RacesSkipped.Count > 0)
            {
                sb.Append("; ").Append(string.Join(", ", RacesSkipped)).Append(" not added (the recipe uses the UMA 2 head slot)");
            }
            if (HiddenSlotsWithoutMeshHide.Count > 0)
            {
                sb.Append("; no mesh hide for hidden slot(s) ").Append(string.Join(", ", HiddenSlotsWithoutMeshHide));
            }
            if (LeftUnsaved) sb.Append("; NOT saved (the recipe already had unsaved changes)");
            foreach (string w in Warnings) sb.Append("; ").Append(w);
            return sb.ToString();
        }
    }

    /// <summary>
    /// When a UMA 2 wardrobe recipe is added to the Global Library, brings it to the UMA 3 races:
    /// <list type="bullet">
    /// <item>For each slot the recipe hides, adds the Mesh Hide Assets that hide the same part of the UMA 3
    /// body. They are the assets <see cref="UMALegacyContentConverter"/> made, named
    /// <c>Legacy{UMA 2 race}_{hidden slot}_{UMA 3 slot}</c> (one per UMA 3 body slot the region covers).
    /// A hidden slot with none is reported, never guessed.</item>
    /// <item>Adds the UMA 3 race that replaces the recipe's UMA 2 race: the race whose Legacy Rig was baked
    /// from it (<see cref="RaceData.legacyRig"/>, <c>sourceRaceName</c>), for example HumanMale to Human Male 3.0.
    /// When several races share that Legacy Rig, it is the one the others are cross-compatible with (see <see cref="GetRaceMap(IEnumerable{RaceData}, List{string})"/>).
    /// Recipes that put a slot on the UMA 2 head (<see cref="LegacyHeadSlots"/>: face packs, eyebrows, hair
    /// painted on the head) are left without it, because on a UMA 3 character they would add a second, UMA 2 head.
    /// Their mesh hides are then left out too, as they only act on the UMA 3 race.</item>
    /// </list>
    /// Runs from <see cref="UMAAssetIndexer.EvilAddAsset"/> (every explicit add: the Global Library window's drop
    /// and folder drop, "Add Selected Assets to Global Library", the builders), from
    /// <see cref="UMAGlobalLibraryAutoRegistration"/> when a UMA 2 wardrobe recipe is imported, and when a recipe is
    /// worn in edit mode (<see cref="DynamicCharacterAvatar.EditorRecipeEquipping"/>: the avatar's wardrobe in the
    /// inspector, editor-time previews, <c>SetSlot</c> from editor tools). A library rebuild (Add Everything) leaves
    /// recipes as they are. Running it again changes nothing (no duplicate entries).
    /// Only the changed recipe is saved (<c>AssetDatabase.SaveAssetIfDirty</c>); a recipe that already had
    /// unsaved edits is changed in memory and left for its owner to save.
    /// </summary>
    [InitializeOnLoad]
    public static class UMALegacyRecipeLibraryHook
    {
        /// <summary>The UMA 2 head slots. A recipe that uses one does not get the UMA 3 race.</summary>
        public static readonly string[] LegacyHeadSlots = { "F_High poly head", "M_High poly Head" };

        /// <summary>
        /// The hook does nothing while a Unity Test Framework run is active, so other tests that add recipes
        /// do not change them. Tests of this class set this to true.
        /// </summary>
        public static bool AllowDuringTestRuns { get; set; }

        /// <summary>How many recipes the hook has saved in this Editor session.</summary>
        public static int SaveCount { get; private set; }

        // Recipes already checked when worn, with their dirty count at the time. Worn recipes are checked again
        // after they change or the project changes (new mesh hides or races), not on every avatar rebuild.
        static readonly Dictionary<UMAWardrobeRecipe, int> wornChecked = new Dictionary<UMAWardrobeRecipe, int>();
        static bool raceRecipesRefreshQueued;

        static UMALegacyRecipeLibraryHook()
        {
            UMAAssetIndexer.EditorAssetAdding -= OnAssetAdding;
            UMAAssetIndexer.EditorAssetAdding += OnAssetAdding;
            DynamicCharacterAvatar.EditorRecipeEquipping -= OnRecipeEquipping;
            DynamicCharacterAvatar.EditorRecipeEquipping += OnRecipeEquipping;
            EditorApplication.projectChanged -= ForgetWornRecipes;
            EditorApplication.projectChanged += ForgetWornRecipes;
        }

        static void OnAssetAdding(UnityEngine.Object asset)
        {
            if (!(asset is UMAWardrobeRecipe recipe) || !CanRun()) return;
            ApplyAndReport(recipe);
        }

        static void OnRecipeEquipping(UMATextRecipe recipe)
        {
            ApplyWhenWorn(recipe);
        }

        /// <summary>Applies the fix, saves the recipe and writes a Console line when anything changed or is missing.</summary>
        public static UMALegacyRecipeFixResult ApplyAndReport(UMAWardrobeRecipe recipe)
        {
            UMALegacyRecipeFixResult result = Apply(recipe, true);
            if (result.IsLegacyRecipe && (result.Changed || result.HiddenSlotsWithoutMeshHide.Count > 0 || result.Warnings.Count > 0))
            {
                Report(result, "added to the Global Library");
            }
            return result;
        }

        /// <summary>
        /// Brings a UMA 2 wardrobe recipe to the UMA 3 races when it is worn in edit mode, before the avatar checks
        /// its compatible races, and saves it (see <see cref="Apply"/>). Each recipe is checked once until it or the
        /// project changes. Collections, recipes that are not assets, and other recipes are left alone.
        /// </summary>
        /// <returns>The result, or null when the recipe was not checked.</returns>
        public static UMALegacyRecipeFixResult ApplyWhenWorn(UMATextRecipe recipe)
        {
            if (!(recipe is UMAWardrobeRecipe uwr) || recipe is UMAWardrobeCollection) return null;
            if (!CanRun() || !EditorUtility.IsPersistent(uwr)) return null;
            int dirtyCount = EditorUtility.GetDirtyCount(uwr);
            if (wornChecked.TryGetValue(uwr, out int seen) && seen == dirtyCount) return null;

            UMALegacyRecipeFixResult result = Apply(uwr, true);
            wornChecked[uwr] = EditorUtility.GetDirtyCount(uwr);
            if (!result.Changed) return result;

            Report(result, "worn in the editor");
            QueueRaceRecipesRefresh(uwr);
            return result;
        }

        static void Report(UMALegacyRecipeFixResult result, string what)
        {
            string line = "UMA: UMA 2 recipe " + what + ", brought to UMA 3: " + result.Describe();
            if (result.LeftUnsaved || result.Warnings.Count > 0) Debug.LogWarning(line, result.Recipe);
            else Debug.Log(line, result.Recipe);
        }

        static void ForgetWornRecipes()
        {
            wornChecked.Clear();
        }

        // A recipe already in the library is listed under its races when the library loads. After a race was
        // added, the lists are rebuilt once, after the current avatar build, so the recipe shows for the UMA 3 race.
        static void QueueRaceRecipesRefresh(UMAWardrobeRecipe recipe)
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null || raceRecipesRefreshQueued) return;
            if (!indexer.HasAsset<UMAWardrobeRecipe>(recipe.name)) return;
            raceRecipesRefreshQueued = true;
            EditorApplication.delayCall += () =>
            {
                raceRecipesRefreshQueued = false;
                UMAAssetIndexer current = UMAAssetIndexer.Instance;
                if (current != null) current.RebuildRaceRecipes();
            };
        }

        /// <summary>True when nothing is running that the hook must stay out of.</summary>
        public static bool CanRun()
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return false;
            if (BuildPipeline.isBuildingPlayer) return false;
            if (UnityEditorInternal.InternalEditorUtility.inBatchMode) return false;
            if (!AllowDuringTestRuns && UMAGlobalLibraryAutoRegistration.IsTestRunActive()) return false;
            return true;
        }

        // --- races -------------------------------------------------------------------------------------

        /// <summary>
        /// UMA 2 race name to the UMA 3 race that replaces it, from the Global Library's races whose Legacy Rig
        /// names a source race. See <see cref="GetRaceMap(IEnumerable{RaceData}, List{string})"/> for races that
        /// share a Legacy Rig.
        /// </summary>
        public static Dictionary<string, RaceData> GetRaceMap(List<string> problems = null)
        {
            var races = new List<RaceData>();
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null) return new Dictionary<string, RaceData>(StringComparer.Ordinal);

            foreach (AssetItem ai in indexer.GetAssetDictionary(typeof(RaceData)).Values)
            {
                if (ai == null || string.IsNullOrEmpty(ai._Guid)) continue;
                // Loaded through the AssetDatabase so the library's cached references are not changed.
                string path = AssetDatabase.GUIDToAssetPath(ai._Guid);
                RaceData race = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<RaceData>(path);
                if (race != null) races.Add(race);
            }
            return GetRaceMap(races, problems);
        }

        /// <summary>
        /// UMA 2 race name to the UMA 3 race that replaces it, from <paramref name="races"/> whose Legacy Rig names
        /// a source race. Several UMA 3 races may share one Legacy Rig (every female 3.0 race converts UMA 2
        /// HumanFemale slots); the UMA 2 race then goes to the claiming race that all the others are
        /// cross-compatible with, for example Human Female 3.0, whose recipes ElfFemale30 and SpriteFemale30 wear.
        /// When no single race is that, the UMA 2 race is left out and reported. Only a race's own Legacy Rig counts
        /// here: a race that leaves it empty and uses a cross-compatible race's (<see cref="RaceData.GetLegacyRig(out UMASlotRigConversionProfile)"/>)
        /// does not claim the UMA 2 race.
        /// </summary>
        public static Dictionary<string, RaceData> GetRaceMap(IEnumerable<RaceData> races, List<string> problems = null)
        {
            var map = new Dictionary<string, RaceData>(StringComparer.Ordinal);
            if (races == null) return map;

            // Claiming races per UMA 2 race, in library order. The same race listed twice counts once.
            var claims = new Dictionary<string, List<RaceData>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (RaceData race in races)
            {
                if (race == null || race.legacyRig == null) continue;
                string legacy = race.legacyRig.sourceRaceName;
                if (string.IsNullOrEmpty(legacy) || legacy == race.raceName) continue;
                if (!claims.TryGetValue(legacy, out List<RaceData> claimants))
                {
                    claimants = new List<RaceData>();
                    claims.Add(legacy, claimants);
                    order.Add(legacy);
                }
                if (!claimants.Exists(r => r == race || r.raceName == race.raceName)) claimants.Add(race);
            }

            foreach (string legacy in order)
            {
                List<RaceData> claimants = claims[legacy];
                RaceData chosen = claimants.Count == 1 ? claimants[0] : FindBaseRace(claimants);
                if (chosen == null)
                {
                    var names = claimants.ConvertAll(r => "'" + r.raceName + "'");
                    problems?.Add($"UMA 2 race '{legacy}' is the Legacy Rig source of {string.Join(", ", names)}, and none of them is the race the others are cross-compatible with; none is added.");
                    continue;
                }
                map.Add(legacy, chosen);
            }
            return map;
        }

        /// <summary>The one race that every other race in <paramref name="claimants"/> is cross-compatible with, or null.</summary>
        static RaceData FindBaseRace(List<RaceData> claimants)
        {
            RaceData found = null;
            foreach (RaceData candidate in claimants)
            {
                bool isBase = true;
                foreach (RaceData other in claimants)
                {
                    if (other != candidate && !other.IsCrossCompatibleWith(candidate.raceName))
                    {
                        isBase = false;
                        break;
                    }
                }
                if (!isBase) continue;
                if (found != null) return null;
                found = candidate;
            }
            return found;
        }

        /// <summary>True when the recipe is compatible with a UMA 2 race that a UMA 3 race replaces.</summary>
        public static bool IsLegacyRecipe(UMAWardrobeRecipe recipe)
        {
            if (recipe == null || recipe.compatibleRaces == null || recipe.compatibleRaces.Count == 0) return false;
            Dictionary<string, RaceData> map = GetRaceMap();
            foreach (string race in recipe.compatibleRaces)
            {
                if (!string.IsNullOrEmpty(race) && map.ContainsKey(race)) return true;
            }
            return false;
        }

        // --- head slot ---------------------------------------------------------------------------------

        /// <summary>True when one of the recipe's slots is a UMA 2 head slot (<see cref="LegacyHeadSlots"/>).</summary>
        public static bool UsesLegacyHeadSlot(UMATextRecipe recipe)
        {
            foreach (string slot in GetSlotNames(recipe))
            {
                foreach (string head in LegacyHeadSlots)
                {
                    if (string.Equals(slot, head, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        /// <summary>The slot names in a recipe's packed data, read without loading the slots.</summary>
        public static List<string> GetSlotNames(UMATextRecipe recipe)
        {
            var names = new List<string>();
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.recipeString)) return names;
            UMAPackedRecipeBase.UMAPackRecipe packed;
            try { packed = JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(recipe.recipeString); }
            catch (Exception) { return names; }
            UMAPackedRecipeBase.PackedSlotDataV3[] slots = UMAScanRepairUtility.GetRecipeSlotsForScan(packed);
            if (slots == null) return names;
            foreach (UMAPackedRecipeBase.PackedSlotDataV3 slot in slots)
            {
                if (slot != null && !string.IsNullOrWhiteSpace(slot.id)) names.Add(slot.id);
            }
            return names;
        }

        // --- mesh hides --------------------------------------------------------------------------------

        /// <summary>
        /// The Mesh Hide Assets for one hidden UMA 2 slot: assets named
        /// <c>Legacy{legacyRace}_{hiddenSlot}_{target slot}</c> (as <see cref="UMALegacyContentConverter.GetHidePath"/>
        /// names them) whose own slot is that target slot. Sorted by name.
        /// </summary>
        public static List<MeshHideAsset> FindHides(string legacyRace, string hiddenSlot, IList<string> meshHidePaths = null)
        {
            var hides = new List<MeshHideAsset>();
            if (string.IsNullOrEmpty(legacyRace) || string.IsNullOrEmpty(hiddenSlot)) return hides;
            string prefix = SafeFileName("Legacy" + legacyRace + "_" + hiddenSlot + "_");
            foreach (string path in meshHidePaths ?? FindMeshHidePaths())
            {
                string file = Path.GetFileNameWithoutExtension(path);
                if (!file.StartsWith(prefix, StringComparison.Ordinal) || file.Length == prefix.Length) continue;
                var hide = AssetDatabase.LoadAssetAtPath<MeshHideAsset>(path);
                if (hide == null) continue;
                // The rest of the name is the UMA 3 slot the hide acts on. Checking it against the hide's own
                // slot keeps a hidden slot "MaleTorso" from also taking the hides of a slot "MaleTorso_Extra".
                string target = file.Substring(prefix.Length);
                string own = hide.AssetSlotName;
                if (string.IsNullOrEmpty(own) || !own.StartsWith(SafeFileName(target), StringComparison.Ordinal)) continue;
                hides.Add(hide);
            }
            hides.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return hides;
        }

        static List<string> FindMeshHidePaths()
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:MeshHideAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && Path.GetFileName(path).StartsWith("Legacy", StringComparison.Ordinal)) paths.Add(path);
            }
            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        static string SafeFileName(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            return name;
        }

        // --- the fix -----------------------------------------------------------------------------------

        /// <summary>
        /// Adds the mesh hides and the UMA 3 race to a UMA 2 wardrobe recipe (see the class summary). With
        /// <paramref name="save"/>, a changed recipe asset is saved on its own, unless it already had unsaved edits.
        /// Recipes that are not UMA 2 recipes are not touched.
        /// </summary>
        public static UMALegacyRecipeFixResult Apply(UMAWardrobeRecipe recipe, bool save)
        {
            var result = new UMALegacyRecipeFixResult { Recipe = recipe };
            if (recipe == null || recipe.compatibleRaces == null) return result;

            Dictionary<string, RaceData> map = GetRaceMap(result.Warnings);
            var legacyRaces = new List<string>();
            foreach (string race in recipe.compatibleRaces)
            {
                if (!string.IsNullOrEmpty(race) && map.ContainsKey(race) && !legacyRaces.Contains(race)) legacyRaces.Add(race);
            }
            if (legacyRaces.Count == 0)
            {
                result.Warnings.Clear(); // not a UMA 2 recipe: nothing to report
                return result;
            }
            result.IsLegacyRecipe = true;
            result.UsesLegacyHead = UsesLegacyHeadSlot(recipe);

            // Which UMA 3 races the recipe is (or will be) worn on.
            var activeLegacyRaces = new List<string>();
            foreach (string legacy in legacyRaces)
            {
                string newRace = map[legacy].raceName;
                if (recipe.compatibleRaces.Contains(newRace))
                {
                    activeLegacyRaces.Add(legacy);
                }
                else if (result.UsesLegacyHead)
                {
                    if (!result.RacesSkipped.Contains(newRace)) result.RacesSkipped.Add(newRace);
                }
                else
                {
                    if (!result.RacesAdded.Contains(newRace)) result.RacesAdded.Add(newRace);
                    activeLegacyRaces.Add(legacy);
                }
            }

            // Mesh hides for each hidden slot, on the UMA 3 races the recipe is worn on.
            if (recipe.Hides != null && recipe.Hides.Count > 0)
            {
                List<string> paths = FindMeshHidePaths();
                foreach (string hidden in recipe.Hides)
                {
                    if (string.IsNullOrEmpty(hidden)) continue;
                    bool found = false;
                    foreach (string legacy in legacyRaces)
                    {
                        List<MeshHideAsset> hides = FindHides(legacy, hidden, paths);
                        if (hides.Count == 0) continue;
                        found = true;
                        if (!activeLegacyRaces.Contains(legacy)) continue;
                        foreach (MeshHideAsset hide in hides)
                        {
                            bool present = recipe.MeshHideAssets != null && recipe.MeshHideAssets.Contains(hide);
                            if (!present && !result.HidesAdded.Contains(hide)) result.HidesAdded.Add(hide);
                        }
                    }
                    if (!found && !result.HiddenSlotsWithoutMeshHide.Contains(hidden)) result.HiddenSlotsWithoutMeshHide.Add(hidden);
                }
            }

            if (!result.Changed) return result;

            bool persistent = EditorUtility.IsPersistent(recipe);
            bool hadUnsavedEdits = persistent && EditorUtility.IsDirty(recipe);

            if (recipe.MeshHideAssets == null) recipe.MeshHideAssets = new List<MeshHideAsset>();
            recipe.MeshHideAssets.AddRange(result.HidesAdded);
            recipe.compatibleRaces.AddRange(result.RacesAdded);
            EditorUtility.SetDirty(recipe);

            if (save && persistent)
            {
                if (hadUnsavedEdits)
                {
                    // Saving would also write the owner's own pending edits; leave the save to them.
                    result.LeftUnsaved = true;
                }
                else
                {
                    AssetDatabase.SaveAssetIfDirty(recipe);
                    result.Saved = !EditorUtility.IsDirty(recipe);
                    if (result.Saved) SaveCount++;
                }
            }
            return result;
        }
    }
}
