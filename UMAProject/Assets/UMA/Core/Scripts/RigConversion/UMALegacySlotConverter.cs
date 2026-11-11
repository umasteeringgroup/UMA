using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Converts legacy slots automatically while a character is built. When a race names the older
    /// rig it replaces (<see cref="RaceData.legacyRig"/>), every slot in the character's recipe that
    /// is still flagged <see cref="SlotDataAsset.isLegacySlot"/> and was built for that older rig is
    /// swapped for a copy converted to the race's own rig by <see cref="UMASlotRigConverter"/>.
    /// </summary>
    /// <remarks>
    /// The flag alone is not trusted: many slots that are native to the newer rigs still carry it.
    /// What decides is the slot's own bone table (<see cref="UMAMeshData.umaBones"/>), which records
    /// the skeleton the slot was built from: it must match the legacy rig and not the race's rig.
    /// The slot asset itself is never changed. Converted copies are made once per slot and race,
    /// kept for reuse, and destroyed when no recipe uses them any more.
    /// </remarks>
    public static class UMALegacySlotConverter
    {
        /// <summary>Set false to turn automatic conversion off for every race.</summary>
        public static bool Enabled = true;

        /// <summary>Two bone definitions match when their local positions are within this many metres...</summary>
        public const float BonePositionTolerance = 1e-3f;
        /// <summary>...and their local rotations within this many degrees.</summary>
        public const float BoneRotationTolerance = 1f;
        /// <summary>A slot was built for the legacy rig when at least this share of its bones match that rig...</summary>
        public const float LegacyRigMatch = 0.8f;
        /// <summary>...and less than this share match the race's own rig.</summary>
        public const float OwnRigMatch = 0.5f;

        private sealed class RaceEntry
        {
            public UMARigRestPoseAsset LegacyRigAsset;
            public UMASlotRigConversionProfile Profile;
            public UMARigRestPose LegacyRig;
            public UMARigRestPose TargetRig;
            public UMASlotRigConversionOptions Options;
            public bool Usable;
            public int PruneAt = 256;
            public readonly Dictionary<SlotDataAsset, SlotDataAsset> Converted = new Dictionary<SlotDataAsset, SlotDataAsset>();
            public readonly HashSet<SlotDataAsset> LeftAlone = new HashSet<SlotDataAsset>();

            /// <summary>
            /// Slots UMA bakes at runtime come and go. Drop the entries of slots that were destroyed
            /// so a long session does not accumulate them.
            /// </summary>
            public void PruneIfLarge()
            {
                if (LeftAlone.Count + Converted.Count < PruneAt)
                {
                    return;
                }
                LeftAlone.RemoveWhere(slot => slot == null);
                var dead = new List<SlotDataAsset>();
                foreach (KeyValuePair<SlotDataAsset, SlotDataAsset> pair in Converted)
                {
                    if (pair.Key == null || pair.Value == null)
                    {
                        dead.Add(pair.Key);
                    }
                }
                for (int i = 0; i < dead.Count; i++)
                {
                    Converted.Remove(dead[i]);
                }
                PruneAt = Mathf.Max(256, (LeftAlone.Count + Converted.Count) * 2);
            }
        }

        private static readonly Dictionary<RaceData, RaceEntry> races = new Dictionary<RaceData, RaceEntry>();

        /// <summary>
        /// Swaps the legacy slots in a character's recipe for converted copies. Called by the
        /// generator before it validates and builds the character. Returns how many slots were swapped.
        /// </summary>
        public static int ConvertLegacySlots(UMAData umaData)
        {
            if (!Enabled || umaData == null || umaData.umaRecipe == null)
            {
                return 0;
            }
            return ConvertLegacySlots(umaData.umaRecipe.slotDataList, umaData.umaRecipe.raceData);
        }

        /// <summary>
        /// Swaps the legacy slots in <paramref name="slots"/> for copies converted to
        /// <paramref name="race"/>'s rig. Slots that are not flagged legacy, were not built for the
        /// race's legacy rig, or cannot be converted are left as they are. Does nothing for a race
        /// without a legacy rig. Returns how many slots were swapped.
        /// </summary>
        public static int ConvertLegacySlots(SlotData[] slots, RaceData race)
        {
            if (!Enabled || slots == null || race == null || race.legacyRig == null)
            {
                return 0;
            }

            RaceEntry entry = null;
            int swapped = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                SlotData slot = slots[i];
                if (slot == null)
                {
                    continue;
                }
                SlotDataAsset asset = slot.asset;
                if (asset == null || !asset.isLegacySlot)
                {
                    continue;
                }
                if (entry == null)
                {
                    entry = GetEntry(race);
                    if (!entry.Usable)
                    {
                        return 0;
                    }
                }

                SlotDataAsset converted = GetConverted(entry, asset, race, slots);
                if (converted != null)
                {
                    slot.asset = converted;
                    // The copy lives as long as some recipe uses it; this hands it to the same
                    // lifetime tracking UMA uses for slots it bakes at runtime.
                    UMARuntimeBakedSlotCleanup.Register(slot);
                    swapped++;
                }
            }
            return swapped;
        }

        /// <summary>
        /// The share of a slot's bone definitions that match <paramref name="rig"/>: same local
        /// position within <see cref="BonePositionTolerance"/> and rotation within
        /// <see cref="BoneRotationTolerance"/>. Bones the rig does not have are not counted.
        /// </summary>
        /// <param name="comparedBones">How many of the slot's bones the rig has.</param>
        public static float GetBoneMatch(UMAMeshData mesh, UMARigRestPose rig, out int comparedBones)
        {
            comparedBones = 0;
            if (rig == null || UMAMeshData.IsNullOrEmptyMeshData(mesh) || mesh.umaBones == null)
            {
                return 0f;
            }

            int matched = 0;
            float positionTolerance = BonePositionTolerance * BonePositionTolerance;
            for (int i = 0; i < mesh.umaBones.Length; i++)
            {
                UMATransform bone = mesh.umaBones[i];
                if (bone == null || !rig.TryGetBone(bone.hash, out UMATransform rigBone))
                {
                    continue;
                }
                comparedBones++;
                if ((bone.position - rigBone.position).sqrMagnitude <= positionTolerance &&
                    Quaternion.Angle(bone.rotation, rigBone.rotation) <= BoneRotationTolerance)
                {
                    matched++;
                }
            }
            return comparedBones > 0 ? (float)matched / comparedBones : 0f;
        }

        /// <summary>
        /// True when the slot's bone table says it was built for <paramref name="legacyRig"/> and not
        /// for <paramref name="ownRig"/>. A slot built for a third rig (another race) matches neither
        /// and is left alone.
        /// </summary>
        public static bool IsBuiltForLegacyRig(UMAMeshData mesh, UMARigRestPose legacyRig, UMARigRestPose ownRig)
        {
            float legacyMatch = GetBoneMatch(mesh, legacyRig, out int compared);
            if (compared == 0 || legacyMatch < LegacyRigMatch)
            {
                return false;
            }
            return GetBoneMatch(mesh, ownRig, out _) < OwnRigMatch;
        }

        /// <summary>
        /// Forgets every converted copy and every decision. Copies still worn by a character stay
        /// alive until that character lets go of them; the next build converts afresh. Call after
        /// changing a slot, a race's legacy rig or a conversion profile in code.
        /// </summary>
        public static void ClearCache()
        {
            races.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            races.Clear();
            Enabled = true;
        }

        private static RaceEntry GetEntry(RaceData race)
        {
            if (races.TryGetValue(race, out RaceEntry entry) && entry.LegacyRigAsset == race.legacyRig && entry.Profile == race.legacyRigProfile)
            {
                return entry;
            }

            entry = new RaceEntry { LegacyRigAsset = race.legacyRig, Profile = race.legacyRigProfile };
            races[race] = entry;

            entry.LegacyRig = race.legacyRig.GetRestPose();
            if (entry.LegacyRig == null || entry.LegacyRig.BoneCount == 0)
            {
                Debug.LogWarning($"[UMA] Race '{race.raceName}' has a legacy rig with no bones ('{race.legacyRig.name}'); legacy slots will not be converted.", race);
                return entry;
            }
            if (!UMARigRestPose.TryFromRace(race, out entry.TargetRig, out string reason))
            {
                Debug.LogWarning($"[UMA] Legacy slots will not be converted for race '{race.raceName}': {reason}", race);
                return entry;
            }

            entry.Options = new UMASlotRigConversionOptions { Profile = race.legacyRigProfile };
            entry.Usable = true;
            return entry;
        }

        /// <summary>
        /// The race's skin at rest, for effectors that clear the body: the slots of the race's base
        /// recipe, taken from the character being built where it wears them (a race with prebaked
        /// blend shapes wears baked copies, whose shape is the one that counts), and from the base
        /// recipe itself otherwise. Clothing is never part of it, so the result is the same for every
        /// character of the race and the converted slot can be shared between them.
        /// </summary>
        private static UMARigBodySurface BuildBodySurface(RaceEntry entry, RaceData race, SlotData[] wornSlots)
        {
            var baseSlots = new Dictionary<string, SlotDataAsset>(System.StringComparer.Ordinal);
            if (race.baseRaceRecipe != null)
            {
                UMAData.UMARecipe recipe = race.baseRaceRecipe.GetCachedRecipe(loadSlots: true);
                SlotData[] slotData = recipe != null ? recipe.GetAllSlots() : null;
                if (slotData != null)
                {
                    for (int i = 0; i < slotData.Length; i++)
                    {
                        SlotDataAsset baseAsset = slotData[i] != null ? slotData[i].asset : null;
                        if (baseAsset != null && !string.IsNullOrEmpty(baseAsset.slotName) && !UMAMeshData.IsNullOrEmptyMeshData(baseAsset.meshData))
                        {
                            baseSlots[baseAsset.slotName] = baseAsset;
                        }
                    }
                }
            }
            if (wornSlots != null)
            {
                for (int i = 0; i < wornSlots.Length; i++)
                {
                    SlotDataAsset worn = wornSlots[i] != null ? wornSlots[i].asset : null;
                    if (worn == null || !worn.isBaked || string.IsNullOrEmpty(worn.sourceSlot) || UMAMeshData.IsNullOrEmptyMeshData(worn.meshData))
                    {
                        continue;
                    }
                    if (baseSlots.ContainsKey(worn.sourceSlot))
                    {
                        baseSlots[worn.sourceSlot] = worn;
                    }
                }
            }
            return UMARigBodySurface.FromSlots(baseSlots.Values, entry.TargetRig);
        }

        private static SlotDataAsset GetConverted(RaceEntry entry, SlotDataAsset asset, RaceData race, SlotData[] wornSlots)
        {
            if (entry.LeftAlone.Contains(asset))
            {
                return null;
            }
            if (entry.Converted.TryGetValue(asset, out SlotDataAsset cached))
            {
                if (cached != null)
                {
                    return cached;
                }
                // The copy was destroyed after its last user let go of it. Convert again.
                entry.Converted.Remove(asset);
            }

            entry.PruneIfLarge();
            UMAMeshData mesh = asset.meshData;
            if (!IsBuiltForLegacyRig(mesh, entry.LegacyRig, entry.TargetRig))
            {
                entry.LeftAlone.Add(asset);
                return null;
            }

            var report = new UMASlotRigConversionReport();
            // The profile may hold entries for this slot alone; they are looked up by slot name.
            entry.Options.ProfileSlotName = asset.slotName;
            if (entry.Options.BodySurface == null && entry.Options.Profile != null && entry.Options.Profile.NeedsBodySurface(asset.slotName))
            {
                entry.Options.BodySurface = BuildBodySurface(entry, race, wornSlots);
            }
            if (!UMASlotRigConverter.TryConvert(mesh, entry.LegacyRig, entry.TargetRig, entry.Options, report, out UMAMeshData convertedMesh, out string reason))
            {
                entry.LeftAlone.Add(asset);
                Debug.LogWarning($"[UMA] Legacy slot '{asset.slotName}' was not converted for race '{race.raceName}': {reason}", asset);
                return null;
            }

            SlotDataAsset copy = Object.Instantiate(asset);
            copy.name = asset.name;
            copy.hideFlags = HideFlags.HideAndDontSave;
            // Instantiate copied the legacy mesh data too; let go of it before it is replaced.
            if (copy.meshData != null && copy.meshData.LoadedBoneweights)
            {
                copy.meshData.FreeBoneWeights();
            }
            convertedMesh.SlotName = asset.slotName;
            copy.meshData = convertedMesh;
            copy.isLegacySlot = false;
            entry.Converted[asset] = copy;
            return copy;
        }
    }
}
