using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>One legacy race and the race that replaces it, for <see cref="UMALegacyContentConverter"/>.</summary>
    [Serializable]
    public sealed class UMALegacyRaceMapping
    {
        [Tooltip("The UMA 2 race the content was made for, for example HumanMale.")]
        public RaceData legacyRace;

        [Tooltip("The race that replaces it, for example Human Male 3.0.")]
        public RaceData newRace;

        [Tooltip("Body slots of the legacy race that get a Mesh Hide Asset set. Empty: the legacy race's body slots, without eyes, mouth, lashes and brows.")]
        public List<string> legacySlots = new List<string>();

        [Tooltip("Body slots of the new race the hides apply to. Empty: the new race's body slots, without eyes, mouth, lashes and brows.")]
        public List<string> targetSlots = new List<string>();

        [Tooltip("Race names taken off the recipes, for example HumanMaleDCS.")]
        public List<string> racesToRemove = new List<string>();
    }

    /// <summary>What <see cref="UMALegacyContentConverter.Convert"/> does.</summary>
    [Serializable]
    public sealed class UMALegacyContentConversionSettings
    {
        [Tooltip("Folder holding the legacy wardrobe recipes; searched with its subfolders. The Mesh Hide Assets are saved in a MeshHides folder inside it.")]
        public string contentFolder;

        public List<UMALegacyRaceMapping> mappings = new List<UMALegacyRaceMapping>();

        [Tooltip("Create a Mesh Hide Asset set per legacy race and add the matching hides to each recipe that hides a legacy body slot.")]
        public bool createMeshHides = true;

        [Tooltip("Add the new race to every recipe that is compatible with the legacy race.")]
        public bool addNewRace = true;

        [Tooltip("Take the 'races to remove' off every recipe that is compatible with the legacy race.")]
        public bool removeRaces = true;

        [Tooltip("Passes of a majority filter over the region labels, which removes single stray triangles along the seams.")]
        [Range(0, 10)]
        public int smoothingPasses = 3;
    }

    /// <summary>The outcome of a conversion, or of a preview.</summary>
    public sealed class UMALegacyContentConversionResult
    {
        public bool Preview;
        public int RecipesChanged;
        public int HideReferencesAdded;
        public int RacesAdded;
        public int RacesRemoved;
        public readonly List<MeshHideAsset> CreatedHides = new List<MeshHideAsset>();
        public readonly List<MeshHideAsset> UpdatedHides = new List<MeshHideAsset>();
        public readonly List<string> Lines = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public string Summary()
        {
            var text = new StringBuilder();
            text.AppendLine((Preview ? "Preview: " : string.Empty) +
                $"{RecipesChanged} recipes {(Preview ? "would change" : "changed")}, {HideReferencesAdded} mesh hide references, " +
                $"{RacesAdded} races added, {RacesRemoved} races removed, " +
                $"{CreatedHides.Count} hides created, {UpdatedHides.Count} hides updated.");
            if (Warnings.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Warnings:");
                foreach (string warning in Warnings) text.AppendLine("  " + warning);
            }
            if (Lines.Count > 0)
            {
                text.AppendLine();
                foreach (string line in Lines) text.AppendLine(line);
            }
            return text.ToString();
        }
    }

    /// <summary>
    /// Brings a folder of UMA 2 wardrobe recipes to the UMA 3 races.
    /// <list type="bullet">
    /// <item>For each legacy race, it makes a set of Mesh Hide Assets on the new race's body that cover
    /// the same parts of the body as the legacy race's body slots (torso, legs, feet, hands, head), so a
    /// recipe that hid "MaleTorso" on the old race hides the same area of the new body.</item>
    /// <item>Each recipe compatible with the legacy race gets the hides for the legacy slots it hides,
    /// gets the new race added, and loses the races named for removal (for example HumanMaleDCS).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The regions are found by converting the legacy body slots to the new race's rig (with the new
    /// race's Legacy Rig and Legacy Rig Profile when the Legacy Rig was baked from the legacy race, and
    /// from the legacy race's own skeleton otherwise) and giving every triangle of the new body the
    /// legacy slot whose surface lies nearest. Running it again updates the same assets in place.
    /// </remarks>
    public static class UMALegacyContentConverter
    {
        public const string HideFolderName = "MeshHides";

        private static readonly string[] NonBodyWords = { "eye", "brow", "lash", "mouth", "teeth", "tongue", "hair" };

        // --- defaults --------------------------------------------------------------------------------

        /// <summary>The slot names in a race's base recipe, in recipe order.</summary>
        public static List<string> GetBaseSlotNames(RaceData race, bool bodyOnly)
        {
            var names = new List<string>();
            UMAData.UMARecipe recipe = race != null && race.baseRaceRecipe != null ? race.baseRaceRecipe.GetCachedRecipe(true) : null;
            if (recipe == null || recipe.slotDataList == null)
            {
                return names;
            }
            foreach (SlotData slot in recipe.slotDataList)
            {
                if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
                {
                    continue;
                }
                string name = slot.asset.slotName;
                if (names.Contains(name) || (bodyOnly && !IsBodySlot(slot.asset, slot.tags)))
                {
                    continue;
                }
                names.Add(name);
            }
            return names;
        }

        /// <summary>
        /// A slot that is skin rather than eyes, mouth, lashes, brows or hair: those are separate slots on
        /// both generations of race, so they are left out of the hides.
        /// </summary>
        public static bool IsBodySlot(SlotDataAsset slot, string[] tags = null)
        {
            if (slot == null)
            {
                return false;
            }
            if (ContainsNonBodyWord(slot.slotName))
            {
                return false;
            }
            string[] allTags = tags ?? slot.tags;
            if (allTags != null)
            {
                foreach (string tag in allTags)
                {
                    if (ContainsNonBodyWord(tag))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool ContainsNonBodyWord(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            foreach (string word in NonBodyWords)
            {
                if (text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Fills empty slot and race lists of a mapping with their defaults.</summary>
        public static void ApplyDefaults(UMALegacyRaceMapping mapping)
        {
            if (mapping == null)
            {
                return;
            }
            if (mapping.legacySlots.Count == 0)
            {
                mapping.legacySlots.AddRange(GetBaseSlotNames(mapping.legacyRace, true));
            }
            if (mapping.targetSlots.Count == 0)
            {
                mapping.targetSlots.AddRange(GetBaseSlotNames(mapping.newRace, true));
            }
            if (mapping.racesToRemove.Count == 0 && mapping.legacyRace != null)
            {
                mapping.racesToRemove.Add(mapping.legacyRace.raceName + "DCS");
            }
        }

        /// <summary>The usual mappings: HumanMale to Human Male 3.0 and HumanFemale to Human Female 3.0, when the races are in the library.</summary>
        public static List<UMALegacyRaceMapping> DefaultMappings()
        {
            var mappings = new List<UMALegacyRaceMapping>();
            AddDefault(mappings, "HumanMale", "Human Male 3.0");
            AddDefault(mappings, "HumanFemale", "Human Female 3.0");
            return mappings;
        }

        private static void AddDefault(List<UMALegacyRaceMapping> mappings, string legacy, string replacement)
        {
            UMAAssetIndexer index = UMAAssetIndexer.Instance;
            RaceData legacyRace = index != null ? index.GetAsset<RaceData>(legacy) : null;
            RaceData newRace = index != null ? index.GetAsset<RaceData>(replacement) : null;
            if (legacyRace == null || newRace == null)
            {
                return;
            }
            var mapping = new UMALegacyRaceMapping { legacyRace = legacyRace, newRace = newRace };
            ApplyDefaults(mapping);
            mappings.Add(mapping);
        }

        /// <summary>Wardrobe recipes in a folder (and its subfolders) that are compatible with a race.</summary>
        public static List<UMAWardrobeRecipe> FindRecipes(string folder, string raceName)
        {
            var recipes = new List<UMAWardrobeRecipe>();
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder) || string.IsNullOrEmpty(raceName))
            {
                return recipes;
            }
            foreach (string guid in AssetDatabase.FindAssets("t:UMAWardrobeRecipe", new[] { folder }))
            {
                var recipe = AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(AssetDatabase.GUIDToAssetPath(guid));
                if (recipe != null && recipe.compatibleRaces != null && recipe.compatibleRaces.Contains(raceName))
                {
                    recipes.Add(recipe);
                }
            }
            recipes.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
            return recipes;
        }

        /// <summary>Where the hide for one legacy slot on one body slot of the new race is kept.</summary>
        public static string GetHidePath(string contentFolder, RaceData legacyRace, string legacySlot, string targetSlot)
        {
            string folder = contentFolder.TrimEnd('/') + "/" + HideFolderName + "/" + SafeFileName(legacyRace.raceName);
            return folder + "/" + SafeFileName("Legacy" + legacyRace.raceName + "_" + legacySlot + "_" + targetSlot) + ".asset";
        }

        // --- the whole conversion ----------------------------------------------------------------------

        /// <summary>
        /// Runs the conversion. With <paramref name="preview"/> nothing is written: the result lists the
        /// recipe changes, and the hides that already exist from an earlier run.
        /// </summary>
        public static UMALegacyContentConversionResult Convert(UMALegacyContentConversionSettings settings, bool preview)
        {
            var result = new UMALegacyContentConversionResult { Preview = preview };
            if (settings == null || string.IsNullOrEmpty(settings.contentFolder) || !AssetDatabase.IsValidFolder(settings.contentFolder))
            {
                result.Warnings.Add("Choose a content folder in the project.");
                return result;
            }
            if (settings.mappings == null || settings.mappings.Count == 0)
            {
                result.Warnings.Add("Add at least one legacy race and the race that replaces it.");
                return result;
            }

            var changedRecipes = new HashSet<UMAWardrobeRecipe>();
            try
            {
                for (int m = 0; m < settings.mappings.Count; m++)
                {
                    UMALegacyRaceMapping mapping = settings.mappings[m];
                    if (mapping == null || mapping.legacyRace == null || mapping.newRace == null)
                    {
                        result.Warnings.Add($"Mapping {m + 1} needs both a legacy race and a new race; it was skipped.");
                        continue;
                    }
                    result.Lines.Add($"== {mapping.legacyRace.raceName} -> {mapping.newRace.raceName}");

                    Dictionary<string, List<MeshHideAsset>> hides = settings.createMeshHides
                        ? (preview ? FindExistingHides(settings.contentFolder, mapping) : CreateHides(settings, mapping, result))
                        : new Dictionary<string, List<MeshHideAsset>>();

                    List<UMAWardrobeRecipe> recipes = FindRecipes(settings.contentFolder, mapping.legacyRace.raceName);
                    for (int r = 0; r < recipes.Count; r++)
                    {
                        if (!preview)
                        {
                            EditorUtility.DisplayProgressBar("Converting legacy recipes", recipes[r].name, (float)r / recipes.Count);
                        }
                        if (UpdateRecipe(recipes[r], mapping, settings, hides, preview, result))
                        {
                            changedRecipes.Add(recipes[r]);
                        }
                    }
                    if (recipes.Count == 0)
                    {
                        result.Lines.Add($"  No wardrobe recipes in '{settings.contentFolder}' are compatible with {mapping.legacyRace.raceName}.");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!preview)
                {
                    AssetDatabase.SaveAssets();
                    if (changedRecipes.Count > 0 && UMAAssetIndexer.Instance != null)
                    {
                        // The index keeps a race -> wardrobe slot -> recipe table built from compatibleRaces.
                        UMAAssetIndexer.Instance.RebuildRaceRecipes();
                    }
                }
            }
            result.RecipesChanged = changedRecipes.Count;
            return result;
        }

        /// <summary>
        /// Adds the hides, adds the new race and removes the retired races on one recipe. Returns true
        /// when the recipe changed (or, in a preview, would change).
        /// </summary>
        public static bool UpdateRecipe(UMAWardrobeRecipe recipe, UMALegacyRaceMapping mapping, UMALegacyContentConversionSettings settings,
            Dictionary<string, List<MeshHideAsset>> hidesByLegacySlot, bool preview, UMALegacyContentConversionResult result)
        {
            if (recipe == null || mapping == null || mapping.legacyRace == null || mapping.newRace == null)
            {
                return false;
            }
            var changes = new List<string>();
            var hidesToAdd = new List<MeshHideAsset>();
            var unmatched = new List<string>();

            if (settings.createMeshHides && recipe.Hides != null)
            {
                foreach (string hidden in recipe.Hides)
                {
                    if (string.IsNullOrEmpty(hidden))
                    {
                        continue;
                    }
                    if (hidesByLegacySlot != null && hidesByLegacySlot.TryGetValue(hidden, out List<MeshHideAsset> hides))
                    {
                        foreach (MeshHideAsset hide in hides)
                        {
                            bool present = recipe.MeshHideAssets != null && recipe.MeshHideAssets.Contains(hide);
                            if (hide != null && !present && !hidesToAdd.Contains(hide))
                            {
                                hidesToAdd.Add(hide);
                            }
                        }
                    }
                    else if (mapping.legacySlots.Contains(hidden))
                    {
                        // A selected legacy slot whose hides do not exist yet: only in a preview before the first run.
                        changes.Add($"+hides for {hidden}");
                    }
                    else
                    {
                        unmatched.Add(hidden);
                    }
                }
            }

            bool addRace = settings.addNewRace && !recipe.compatibleRaces.Contains(mapping.newRace.raceName);
            var racesToRemove = new List<string>();
            if (settings.removeRaces)
            {
                foreach (string race in mapping.racesToRemove)
                {
                    if (!string.IsNullOrEmpty(race) && race != mapping.newRace.raceName && race != mapping.legacyRace.raceName && recipe.compatibleRaces.Contains(race))
                    {
                        racesToRemove.Add(race);
                    }
                }
            }

            if (hidesToAdd.Count > 0) changes.Add($"+{hidesToAdd.Count} mesh hides");
            if (addRace) changes.Add("+" + mapping.newRace.raceName);
            foreach (string race in racesToRemove) changes.Add("-" + race);

            string path = AssetDatabase.GetAssetPath(recipe);
            string wardrobeNote = mapping.newRace.wardrobeSlots != null && !string.IsNullOrEmpty(recipe.wardrobeSlot) && !mapping.newRace.wardrobeSlots.Contains(recipe.wardrobeSlot)
                ? $" (wardrobe slot '{recipe.wardrobeSlot}' is not on {mapping.newRace.raceName})"
                : string.Empty;
            string unmatchedNote = unmatched.Count > 0 ? $" (no mesh hide for {string.Join(", ", unmatched)})" : string.Empty;
            if (!string.IsNullOrEmpty(wardrobeNote) && (addRace || recipe.compatibleRaces.Contains(mapping.newRace.raceName)))
            {
                result.Warnings.Add($"'{recipe.name}': wardrobe slot '{recipe.wardrobeSlot}' is not one of {mapping.newRace.raceName}'s wardrobe slots, so the recipe cannot be worn there.");
            }

            if (changes.Count == 0)
            {
                result.Lines.Add($"  {path}: no change{unmatchedNote}{wardrobeNote}");
                return false;
            }
            result.Lines.Add($"  {path}: {string.Join(", ", changes)}{unmatchedNote}{wardrobeNote}");
            result.HideReferencesAdded += hidesToAdd.Count;
            result.RacesAdded += addRace ? 1 : 0;
            result.RacesRemoved += racesToRemove.Count;
            if (preview)
            {
                return true;
            }

            Undo.RecordObject(recipe, "Convert Legacy Recipe");
            if (recipe.MeshHideAssets == null)
            {
                recipe.MeshHideAssets = new List<MeshHideAsset>();
            }
            recipe.MeshHideAssets.AddRange(hidesToAdd);
            if (addRace)
            {
                recipe.compatibleRaces.Add(mapping.newRace.raceName);
            }
            foreach (string race in racesToRemove)
            {
                recipe.compatibleRaces.RemoveAll(r => r == race);
            }
            EditorUtility.SetDirty(recipe);
            if (EditorUtility.IsPersistent(recipe))
            {
                AssetDatabase.SaveAssetIfDirty(recipe);
            }
            return true;
        }

        /// <summary>The hides an earlier run left in the content folder, by legacy slot.</summary>
        public static Dictionary<string, List<MeshHideAsset>> FindExistingHides(string contentFolder, UMALegacyRaceMapping mapping)
        {
            var hides = new Dictionary<string, List<MeshHideAsset>>(StringComparer.Ordinal);
            foreach (string legacySlot in mapping.legacySlots)
            {
                foreach (string targetSlot in mapping.targetSlots)
                {
                    var hide = AssetDatabase.LoadAssetAtPath<MeshHideAsset>(GetHidePath(contentFolder, mapping.legacyRace, legacySlot, targetSlot));
                    if (hide != null)
                    {
                        AddHide(hides, legacySlot, hide);
                    }
                }
            }
            return hides;
        }

        private static void AddHide(Dictionary<string, List<MeshHideAsset>> hides, string legacySlot, MeshHideAsset hide)
        {
            if (!hides.TryGetValue(legacySlot, out List<MeshHideAsset> list))
            {
                list = new List<MeshHideAsset>();
                hides.Add(legacySlot, list);
            }
            list.Add(hide);
        }

        // --- the hides ---------------------------------------------------------------------------------

        /// <summary>
        /// Creates or updates the Mesh Hide Assets for one legacy race: one per legacy slot and body slot
        /// of the new race that the legacy slot covers. Returns them by legacy slot name.
        /// </summary>
        public static Dictionary<string, List<MeshHideAsset>> CreateHides(UMALegacyContentConversionSettings settings, UMALegacyRaceMapping mapping,
            UMALegacyContentConversionResult result)
        {
            var hides = new Dictionary<string, List<MeshHideAsset>>(StringComparer.Ordinal);
            if (!TryClassify(mapping, settings.smoothingPasses, result, out List<UMAMeshHideAutoGenerator.BodySlot> bodies,
                out List<int[]> labels, out List<string> regions))
            {
                return hides;
            }

            for (int r = 0; r < regions.Count; r++)
            {
                for (int b = 0; b < bodies.Count; b++)
                {
                    UMAMeshHideAutoGenerator.BodySlot body = bodies[b];
                    int[] bodyLabels = labels[b];
                    // Labels cover the triangles of all submeshes in order; masks are per submesh.
                    var flags = new BitArray[body.Triangles.Length];
                    int hidden = 0;
                    int total = 0;
                    int offset = 0;
                    for (int s = 0; s < body.Triangles.Length; s++)
                    {
                        int count = body.Triangles[s] != null ? body.Triangles[s].Length / 3 : 0;
                        flags[s] = new BitArray(count);
                        for (int t = 0; t < count; t++)
                        {
                            if (bodyLabels[offset + t] == r)
                            {
                                flags[s][t] = true;
                                hidden++;
                            }
                        }
                        offset += count;
                        total += count;
                    }

                    string path = GetHidePath(settings.contentFolder, mapping.legacyRace, regions[r], body.BaseSlot.slotName);
                    var asset = AssetDatabase.LoadAssetAtPath<MeshHideAsset>(path);
                    if (hidden == 0 && asset == null)
                    {
                        continue;
                    }
                    if (asset != null && asset.autoGenerationLocked)
                    {
                        // Finished by hand: left exactly as it is, but still used.
                        AddHide(hides, regions[r], asset);
                        result.Lines.Add($"  {path}: locked, left as it is");
                        continue;
                    }
                    bool isNew = asset == null;
                    if (isNew)
                    {
                        asset = ScriptableObject.CreateInstance<MeshHideAsset>();
                    }
                    asset.asset = body.BaseSlot;
                    asset.SaveSelectionForLOD(flags, 0);
                    asset.UpdateEditorHashAndUVMaskFromFlags(body.BaseSlot);
                    if (isNew)
                    {
                        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                        AssetDatabase.CreateAsset(asset, path);
                        result.CreatedHides.Add(asset);
                    }
                    else
                    {
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssetIfDirty(asset);
                        result.UpdatedHides.Add(asset);
                    }
                    if (hidden > 0)
                    {
                        AddHide(hides, regions[r], asset);
                    }
                    result.Lines.Add($"  {path}: {hidden}/{total} triangles");
                }
            }
            foreach (string legacySlot in regions)
            {
                if (!hides.ContainsKey(legacySlot))
                {
                    result.Warnings.Add($"{mapping.legacyRace.raceName}: no triangle of {mapping.newRace.raceName} lies nearest to '{legacySlot}', so it has no hide.");
                }
            }
            return hides;
        }

        /// <summary>
        /// Labels every triangle of the new race's selected body slots with the index (into
        /// <paramref name="regions"/>) of the nearest legacy body slot. Labels are per body slot, over the
        /// base-LOD triangles of all its submeshes in order.
        /// </summary>
        public static bool TryClassify(UMALegacyRaceMapping mapping, int smoothingPasses, UMALegacyContentConversionResult result,
            out List<UMAMeshHideAutoGenerator.BodySlot> bodies, out List<int[]> labels, out List<string> regions)
        {
            bodies = new List<UMAMeshHideAutoGenerator.BodySlot>();
            labels = new List<int[]>();
            regions = new List<string>();

            if (!UMARigRestPose.TryFromRace(mapping.newRace, out UMARigRestPose rig, out string problem))
            {
                result.Warnings.Add($"{mapping.newRace.raceName}: {problem}");
                return false;
            }
            UMARigRestPose legacyRig;
            UMASlotRigConversionProfile profile = null;
            if (mapping.newRace.legacyRig != null && mapping.newRace.legacyRig.sourceRaceName == mapping.legacyRace.raceName)
            {
                legacyRig = mapping.newRace.legacyRig.GetRestPose();
                profile = mapping.newRace.legacyRigProfile;
            }
            else if (!UMARigRestPose.TryFromRace(mapping.legacyRace, out legacyRig, out problem))
            {
                result.Warnings.Add($"{mapping.legacyRace.raceName}: {problem}");
                return false;
            }
            else
            {
                result.Lines.Add($"  {mapping.newRace.raceName} has no Legacy Rig baked from {mapping.legacyRace.raceName}; the regions use {mapping.legacyRace.raceName}'s own skeleton and no rig profile.");
            }

            // The legacy body, converted to the new rig.
            var surface = new UMALegacyRegionSurface();
            Dictionary<string, SlotDataAsset> legacySlotAssets = GetBaseSlotAssets(mapping.legacyRace);
            var options = new UMASlotRigConversionOptions { Profile = profile };
            foreach (string slotName in mapping.legacySlots)
            {
                if (!legacySlotAssets.TryGetValue(slotName, out SlotDataAsset slot) &&
                    (UMAAssetIndexer.Instance == null || (slot = UMAAssetIndexer.Instance.GetAsset<SlotDataAsset>(slotName)) == null))
                {
                    result.Warnings.Add($"{mapping.legacyRace.raceName}: legacy slot '{slotName}' was not found.");
                    continue;
                }
                options.ProfileSlotName = slot.slotName;
                if (!UMASlotRigConverter.TryConvert(slot.meshData, legacyRig, rig, options, null, out UMAMeshData converted, out string reason))
                {
                    result.Warnings.Add($"'{slotName}' could not be converted to {mapping.newRace.raceName}'s rig: {reason}");
                    continue;
                }
                if (!UMARigBodySurface.TryBakeToAvatar(converted, rig, out Vector3[] positions, out Vector3[] normals))
                {
                    result.Warnings.Add($"'{slotName}' is not skinned with normals and was left out.");
                    continue;
                }
                int region = regions.Count;
                regions.Add(slotName);
                for (int s = 0; s < converted.submeshes.Length; s++)
                {
                    if (converted.submeshes[s] != null)
                    {
                        surface.Add(positions, normals, converted.submeshes[s].getManagedTriangles(0), region);
                    }
                }
            }
            if (surface.TriangleCount == 0)
            {
                result.Warnings.Add($"{mapping.legacyRace.raceName}: none of the legacy slots could be used.");
                return false;
            }

            // The new body, as the race builds it.
            var messages = new List<string>();
            foreach (UMAMeshHideAutoGenerator.BodySlot body in UMAMeshHideAutoGenerator.CollectBodySlots(mapping.newRace, rig, messages))
            {
                if (body.BaseSlot != null && mapping.targetSlots.Contains(body.BaseSlot.slotName))
                {
                    bodies.Add(body);
                }
            }
            foreach (string message in messages)
            {
                result.Warnings.Add(message);
            }
            if (bodies.Count == 0)
            {
                result.Warnings.Add($"{mapping.newRace.raceName}: none of the selected body slots were built.");
                return false;
            }

            var positionsPerBody = new List<Vector3[]>();
            var trianglesPerBody = new List<int[]>();
            for (int b = 0; b < bodies.Count; b++)
            {
                UMAMeshHideAutoGenerator.BodySlot body = bodies[b];
                EditorUtility.DisplayProgressBar("Finding legacy body regions", body.BaseSlot.slotName, (float)b / bodies.Count);
                int[] all = ConcatTriangles(body.Triangles);
                labels.Add(surface.ClassifyTriangles(body.Positions, body.Normals, all));
                positionsPerBody.Add(body.Positions);
                trianglesPerBody.Add(all);
            }
            UMALegacyRegionSurface.SmoothLabels(positionsPerBody, trianglesPerBody, labels, smoothingPasses);
            return true;
        }

        private static Dictionary<string, SlotDataAsset> GetBaseSlotAssets(RaceData race)
        {
            var assets = new Dictionary<string, SlotDataAsset>(StringComparer.Ordinal);
            UMAData.UMARecipe recipe = race != null && race.baseRaceRecipe != null ? race.baseRaceRecipe.GetCachedRecipe(true) : null;
            if (recipe?.slotDataList == null)
            {
                return assets;
            }
            foreach (SlotData slot in recipe.slotDataList)
            {
                if (slot?.asset != null && !assets.ContainsKey(slot.asset.slotName))
                {
                    assets.Add(slot.asset.slotName, slot.asset);
                }
            }
            return assets;
        }

        private static int[] ConcatTriangles(int[][] submeshes)
        {
            int length = 0;
            foreach (int[] triangles in submeshes)
            {
                length += triangles != null ? triangles.Length - triangles.Length % 3 : 0;
            }
            var all = new int[length];
            int offset = 0;
            foreach (int[] triangles in submeshes)
            {
                if (triangles == null) continue;
                int count = triangles.Length - triangles.Length % 3;
                Array.Copy(triangles, 0, all, offset, count);
                offset += count;
            }
            return all;
        }

        private static string SafeFileName(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }
            return name;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
            {
                return;
            }
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }

    /// <summary>
    /// Labelled triangles of a surface (the legacy body, one label per legacy slot), with a lookup of
    /// the label nearest to a point. A surface facing away from the query's normal counts as a little
    /// further away, so the inside of one leg is not taken for the other.
    /// </summary>
    public sealed class UMALegacyRegionSurface
    {
        private readonly List<Vector3> corners = new List<Vector3>();   // three per triangle
        private readonly List<Vector3> normals = new List<Vector3>();   // one per triangle
        private readonly List<int> labels = new List<int>();             // one per triangle

        private float cellSize;
        private Vector3 origin;
        private Vector3Int minCell;
        private Vector3Int maxCell;
        private Dictionary<long, List<int>> cells;
        private int[] stamps;
        private int stamp;

        /// <summary>Extra distance, in metres, for a surface that faces away from the query's normal.</summary>
        public float FacingPenalty = 0.05f;

        public int TriangleCount => labels.Count;

        /// <summary>Adds triangles with one label. Normals may be null; then the face normal is used.</summary>
        public void Add(Vector3[] positions, Vector3[] vertexNormals, int[] triangles, int label)
        {
            if (positions == null || triangles == null)
            {
                return;
            }
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = positions[triangles[i]];
                Vector3 b = positions[triangles[i + 1]];
                Vector3 c = positions[triangles[i + 2]];
                corners.Add(a);
                corners.Add(b);
                corners.Add(c);
                Vector3 n = vertexNormals != null
                    ? vertexNormals[triangles[i]] + vertexNormals[triangles[i + 1]] + vertexNormals[triangles[i + 2]]
                    : Vector3.Cross(b - a, c - a);
                normals.Add(n.normalized);
                labels.Add(label);
            }
            cells = null;
        }

        /// <summary>The label of the nearest triangle, or -1 for an empty surface.</summary>
        public int Classify(Vector3 point, Vector3 normal, out float distance)
        {
            distance = float.MaxValue;
            if (labels.Count == 0)
            {
                return -1;
            }
            EnsureGrid();
            stamp++;
            Vector3Int home = CellOf(point);
            int maxRing = 0;
            maxRing = Mathf.Max(maxRing, Mathf.Abs(home.x - minCell.x), Mathf.Abs(maxCell.x - home.x));
            maxRing = Mathf.Max(maxRing, Mathf.Abs(home.y - minCell.y), Mathf.Abs(maxCell.y - home.y));
            maxRing = Mathf.Max(maxRing, Mathf.Abs(home.z - minCell.z), Mathf.Abs(maxCell.z - home.z));

            float bestScore = float.MaxValue;
            int bestLabel = -1;
            for (int ring = 0; ring <= maxRing; ring++)
            {
                // Everything outside the cube of cells already searched is at least this far away.
                if (bestScore <= (ring - 1) * cellSize)
                {
                    break;
                }
                for (int x = -ring; x <= ring; x++)
                {
                    for (int y = -ring; y <= ring; y++)
                    {
                        for (int z = -ring; z <= ring; z++)
                        {
                            if (Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) != ring)
                            {
                                continue;
                            }
                            if (!cells.TryGetValue(Key(home.x + x, home.y + y, home.z + z), out List<int> list))
                            {
                                continue;
                            }
                            foreach (int t in list)
                            {
                                if (stamps[t] == stamp)
                                {
                                    continue;
                                }
                                stamps[t] = stamp;
                                float d = (point - ClosestPointOnTriangle(point, corners[t * 3], corners[t * 3 + 1], corners[t * 3 + 2])).magnitude;
                                float score = d + (Vector3.Dot(normal, normals[t]) < 0f ? FacingPenalty : 0f);
                                if (score < bestScore)
                                {
                                    bestScore = score;
                                    bestLabel = labels[t];
                                    distance = d;
                                }
                            }
                        }
                    }
                }
            }
            return bestLabel;
        }

        /// <summary>Labels each triangle of a mesh by the surface triangle nearest its centre.</summary>
        public int[] ClassifyTriangles(Vector3[] positions, Vector3[] vertexNormals, int[] triangles)
        {
            int count = triangles.Length / 3;
            var result = new int[count];
            for (int t = 0; t < count; t++)
            {
                Vector3 a = positions[triangles[t * 3]];
                Vector3 b = positions[triangles[t * 3 + 1]];
                Vector3 c = positions[triangles[t * 3 + 2]];
                Vector3 n = vertexNormals != null
                    ? (vertexNormals[triangles[t * 3]] + vertexNormals[triangles[t * 3 + 1]] + vertexNormals[triangles[t * 3 + 2]]).normalized
                    : Vector3.Cross(b - a, c - a).normalized;
                result[t] = Classify((a + b + c) / 3f, n, out _);
            }
            return result;
        }

        /// <summary>
        /// Majority filter over triangles that share a corner, across meshes (corners are matched by
        /// position, so seams between slots and UV seams count as shared). Removes stray labels.
        /// </summary>
        public static void SmoothLabels(IList<Vector3[]> positions, IList<int[]> triangles, IList<int[]> labels, int passes)
        {
            if (passes <= 0 || labels == null || labels.Count == 0)
            {
                return;
            }
            var offsets = new int[labels.Count + 1];
            for (int m = 0; m < labels.Count; m++)
            {
                offsets[m + 1] = offsets[m] + labels[m].Length;
            }
            int total = offsets[labels.Count];
            var byCorner = new Dictionary<(int, int, int), List<int>>();
            for (int m = 0; m < labels.Count; m++)
            {
                for (int t = 0; t < labels[m].Length; t++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 p = positions[m][triangles[m][t * 3 + k]];
                        var key = (Mathf.RoundToInt(p.x * 5000f), Mathf.RoundToInt(p.y * 5000f), Mathf.RoundToInt(p.z * 5000f));
                        if (!byCorner.TryGetValue(key, out List<int> list))
                        {
                            list = new List<int>(6);
                            byCorner.Add(key, list);
                        }
                        list.Add(offsets[m] + t);
                    }
                }
            }
            var neighbours = new List<int>[total];
            foreach (List<int> shared in byCorner.Values)
            {
                foreach (int a in shared)
                {
                    List<int> list = neighbours[a] ?? (neighbours[a] = new List<int>(12));
                    foreach (int b in shared)
                    {
                        if (a != b && !list.Contains(b))
                        {
                            list.Add(b);
                        }
                    }
                }
            }
            var flat = new int[total];
            int maxLabel = 0;
            for (int m = 0; m < labels.Count; m++)
            {
                Array.Copy(labels[m], 0, flat, offsets[m], labels[m].Length);
                foreach (int label in labels[m]) maxLabel = Mathf.Max(maxLabel, label);
            }
            var counts = new int[maxLabel + 2];
            for (int pass = 0; pass < passes; pass++)
            {
                var next = (int[])flat.Clone();
                bool changed = false;
                for (int i = 0; i < total; i++)
                {
                    Array.Clear(counts, 0, counts.Length);
                    counts[flat[i] + 1]++;
                    if (neighbours[i] != null)
                    {
                        foreach (int n in neighbours[i]) counts[flat[n] + 1]++;
                    }
                    int best = flat[i] + 1;
                    for (int c = 0; c < counts.Length; c++)
                    {
                        if (counts[c] > counts[best]) best = c;
                    }
                    if (best - 1 != flat[i])
                    {
                        next[i] = best - 1;
                        changed = true;
                    }
                }
                flat = next;
                if (!changed) break;
            }
            for (int m = 0; m < labels.Count; m++)
            {
                Array.Copy(flat, offsets[m], labels[m], 0, labels[m].Length);
            }
        }

        private void EnsureGrid()
        {
            if (cells != null)
            {
                return;
            }
            int count = labels.Count;
            Vector3 min = corners[0];
            Vector3 max = corners[0];
            float edgeSum = 0f;
            for (int i = 0; i < corners.Count; i++)
            {
                min = Vector3.Min(min, corners[i]);
                max = Vector3.Max(max, corners[i]);
            }
            for (int t = 0; t < count; t++)
            {
                edgeSum += (corners[t * 3 + 1] - corners[t * 3]).magnitude;
            }
            // A few triangles per cell.
            cellSize = Mathf.Max(0.005f, 3f * edgeSum / count);
            origin = min;
            minCell = CellOf(min);
            maxCell = CellOf(max);
            cells = new Dictionary<long, List<int>>();
            stamps = new int[count];
            stamp = 0;
            for (int t = 0; t < count; t++)
            {
                Vector3 a = corners[t * 3], b = corners[t * 3 + 1], c = corners[t * 3 + 2];
                Vector3Int lo = CellOf(Vector3.Min(a, Vector3.Min(b, c)));
                Vector3Int hi = CellOf(Vector3.Max(a, Vector3.Max(b, c)));
                for (int x = lo.x; x <= hi.x; x++)
                {
                    for (int y = lo.y; y <= hi.y; y++)
                    {
                        for (int z = lo.z; z <= hi.z; z++)
                        {
                            long key = Key(x, y, z);
                            if (!cells.TryGetValue(key, out List<int> list))
                            {
                                list = new List<int>(8);
                                cells.Add(key, list);
                            }
                            list.Add(t);
                        }
                    }
                }
            }
        }

        private Vector3Int CellOf(Vector3 p)
        {
            Vector3 local = (p - origin) / cellSize;
            return new Vector3Int(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y), Mathf.FloorToInt(local.z));
        }

        private static long Key(int x, int y, int z)
        {
            const long mask = (1L << 21) - 1;
            return ((x + (1L << 20)) & mask) | (((y + (1L << 20)) & mask) << 21) | (((z + (1L << 20)) & mask) << 42);
        }

        /// <summary>Closest point on triangle abc to p (Ericson, Real-Time Collision Detection 5.1.5).</summary>
        public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }
    }
}
