using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>How the converter moves the vertices a bone influences from the source rig's joint to the target rig's joint.</summary>
    public enum UMARigBoneCorrectionMode
    {
        /// <summary>Move to the target joint and rotate by the change in bone direction (the default).</summary>
        Full = 0,
        /// <summary>Move to the target joint without rotating.</summary>
        TranslateOnly = 1,
        /// <summary>Use the parent bone's correction unchanged.</summary>
        InheritParent = 2,
        /// <summary>Leave vertices where the global bake put them.</summary>
        None = 3,
    }

    /// <summary>A manual adjustment for one bone, applied on top of the computed correction.</summary>
    [Serializable]
    public sealed class UMARigBoneCorrection
    {
        [Tooltip("Bone name in both rigs.")]
        public string boneName;

        public UMARigBoneCorrectionMode mode = UMARigBoneCorrectionMode.Full;

        [Tooltip("Extra offset, in avatar space (metres), added after the computed correction.")]
        public Vector3 positionOffset;

        [Tooltip("Extra rotation, in degrees about the target joint, applied after the computed correction.")]
        public Vector3 rotationOffsetEuler;

        [Tooltip("Extra scale about the target joint, along the bone's own axes, applied after the computed correction. (1, 1, 1) changes nothing.")]
        public Vector3 scaleOffset = Vector3.one;

        [Tooltip("Also apply this bone's offsets to the vertices of every bone below it, the way moving a real bone carries its children.")]
        public bool includeChildren = true;

        public int BoneHash => string.IsNullOrEmpty(boneName) ? 0 : UMAUtils.StringToHash(boneName);

        /// <summary>The scale to apply. A zero component (an entry saved before scale existed) counts as 1.</summary>
        public Vector3 SafeScale => new Vector3(
            scaleOffset.x == 0f ? 1f : scaleOffset.x,
            scaleOffset.y == 0f ? 1f : scaleOffset.y,
            scaleOffset.z == 0f ? 1f : scaleOffset.z);

        /// <summary>True when the scale is anything other than (1, 1, 1).</summary>
        public bool HasScale => SafeScale != Vector3.one;

        /// <summary>True when the entry moves, rotates or scales anything.</summary>
        public bool HasOffsets => positionOffset != Vector3.zero || rotationOffsetEuler != Vector3.zero || HasScale;

        /// <summary>Puts position, rotation and scale back to neutral. The mode is kept.</summary>
        public void ResetOffsets()
        {
            positionOffset = Vector3.zero;
            rotationOffsetEuler = Vector3.zero;
            scaleOffset = Vector3.one;
        }
    }

    /// <summary>
    /// Bone entries that apply to one slot only. For that slot, an entry here replaces the profile's
    /// general entry for the same bone; every other bone keeps following the general entries.
    /// </summary>
    [Serializable]
    public sealed class UMARigSlotOverride
    {
        [Tooltip("The slot these entries apply to, by slot name.")]
        public string slotName;

        public List<UMARigBoneCorrection> boneCorrections = new List<UMARigBoneCorrection>();

        [Tooltip("Effectors that act on this slot only, after the ones that act on every slot.")]
        public List<UMARigEffector> effectors = new List<UMARigEffector>();

        /// <summary>True when the slot has nothing of its own left.</summary>
        public bool IsEmpty => boneCorrections.Count == 0 && effectors.Count == 0;
    }

    /// <summary>
    /// Per-bone overrides for <see cref="UMASlotRigConverter"/>. The converter computes a correction
    /// for every bone from the two rest skeletons; this asset changes that for the bones it lists,
    /// so hand tuning lives in data rather than code. Edit it with the Rig Profile Editor
    /// (UMA > Tools > Slot Tools > Rig Profile Editor), which previews the result live.
    /// </summary>
    [CreateAssetMenu(fileName = "RigConversionProfile", menuName = "UMA/Misc/Rig Conversion Profile", order = 1)]
    public sealed class UMASlotRigConversionProfile : ScriptableObject
    {
        [Tooltip("Correction mode for bones that have no entry below.")]
        public UMARigBoneCorrectionMode defaultMode = UMARigBoneCorrectionMode.Full;

        public List<UMARigBoneCorrection> boneCorrections = new List<UMARigBoneCorrection>();

        [Tooltip("Bones starred in the Rig Profile Editor, so they can be found again quickly. They do not affect conversion.")]
        public List<string> favoriteBones = new List<string>();

        [Tooltip("Entries for single slots. A generic profile fits most clothing; a slot that needs something else gets its own entries here, which replace the general ones bone by bone for that slot.")]
        public List<UMARigSlotOverride> slotOverrides = new List<UMARigSlotOverride>();

        [Tooltip("Regions that reshape every converted slot passing through them, after the bone entries. An effector kept to one slot is listed under that slot in Slot Overrides instead.")]
        public List<UMARigEffector> effectors = new List<UMARigEffector>();

        private Dictionary<int, UMARigBoneCorrection> lookup;
        private Dictionary<string, Dictionary<int, UMARigBoneCorrection>> slotLookups;

        // --- entries for one slot ------------------------------------------------------------------

        /// <summary>The override block for a slot, or null when the slot has none.</summary>
        public UMARigSlotOverride GetSlotOverride(string slotName)
        {
            if (string.IsNullOrEmpty(slotName))
            {
                return null;
            }
            for (int i = 0; i < slotOverrides.Count; i++)
            {
                if (slotOverrides[i] != null && string.Equals(slotOverrides[i].slotName, slotName, StringComparison.Ordinal))
                {
                    return slotOverrides[i];
                }
            }
            return null;
        }

        // --- effectors -------------------------------------------------------------------------------

        /// <summary>
        /// The list of effectors for one scope: the ones that act on every slot when no slot name is
        /// given, otherwise the slot's own. With <paramref name="create"/> false this returns null for
        /// a slot that has none.
        /// </summary>
        public List<UMARigEffector> GetEffectorList(string slotName, bool create = false)
        {
            if (string.IsNullOrEmpty(slotName))
            {
                return effectors;
            }
            UMARigSlotOverride slot = GetSlotOverride(slotName);
            if (slot == null)
            {
                if (!create)
                {
                    return null;
                }
                slot = new UMARigSlotOverride { slotName = slotName };
                slotOverrides.Add(slot);
                slotLookups = null;
            }
            return slot.effectors;
        }

        /// <summary>
        /// Every switched-on effector that acts on a slot, in the order they are applied: the ones for
        /// all slots, then the slot's own.
        /// </summary>
        public void CollectEffectors(string slotName, List<UMARigEffector> into)
        {
            AddActive(effectors, into);
            UMARigSlotOverride slot = GetSlotOverride(slotName);
            if (slot != null)
            {
                AddActive(slot.effectors, into);
            }
        }

        /// <summary>True when any switched-on effector acts on the slot.</summary>
        public bool HasAnyEffectors(string slotName)
        {
            if (HasActive(effectors, false))
            {
                return true;
            }
            UMARigSlotOverride slot = GetSlotOverride(slotName);
            return slot != null && HasActive(slot.effectors, false);
        }

        /// <summary>True when an effector acting on the slot clears the body, and so needs the body's surface.</summary>
        public bool NeedsBodySurface(string slotName)
        {
            if (HasActive(effectors, true))
            {
                return true;
            }
            UMARigSlotOverride slot = GetSlotOverride(slotName);
            return slot != null && HasActive(slot.effectors, true);
        }

        /// <summary>True when any switched-on effector in the profile, for any slot, clears the body.</summary>
        public bool HasClearBodyEffectors
        {
            get
            {
                if (HasActive(effectors, true))
                {
                    return true;
                }
                for (int i = 0; i < slotOverrides.Count; i++)
                {
                    if (slotOverrides[i] != null && HasActive(slotOverrides[i].effectors, true))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Which slots an effector acts on: <paramref name="slotName"/> is the one slot it is kept to,
        /// or null when it acts on every slot. False when the profile does not hold the effector.
        /// </summary>
        public bool TryGetEffectorSlot(UMARigEffector effector, out string slotName)
        {
            return FindEffectorList(effector, out slotName) != null;
        }

        /// <summary>
        /// Changes which slots an effector acts on: one slot, or every slot when no name is given. It
        /// joins the end of the list it moves to. False when the profile does not hold the effector.
        /// </summary>
        public bool SetEffectorSlot(UMARigEffector effector, string slotName)
        {
            List<UMARigEffector> from = FindEffectorList(effector, out string current);
            if (from == null)
            {
                return false;
            }
            if (string.IsNullOrEmpty(slotName))
            {
                slotName = null;
            }
            if (string.Equals(current, slotName, StringComparison.Ordinal))
            {
                return true;
            }
            from.Remove(effector);
            GetEffectorList(slotName, true).Add(effector);
            RemoveSlotOverrideIfEmpty(current);
            return true;
        }

        /// <summary>The list that holds an effector, and the slot that list belongs to (null: all slots).</summary>
        private List<UMARigEffector> FindEffectorList(UMARigEffector effector, out string slotName)
        {
            slotName = null;
            if (effector == null)
            {
                return null;
            }
            if (effectors.Contains(effector))
            {
                return effectors;
            }
            for (int i = 0; i < slotOverrides.Count; i++)
            {
                UMARigSlotOverride slot = slotOverrides[i];
                if (slot != null && !string.IsNullOrEmpty(slot.slotName) && slot.effectors.Contains(effector))
                {
                    slotName = slot.slotName;
                    return slot.effectors;
                }
            }
            return null;
        }

        /// <summary>Drops a slot's override block when nothing is left in it. Call after removing its last effector.</summary>
        public void RemoveSlotOverrideIfEmpty(string slotName)
        {
            UMARigSlotOverride slot = GetSlotOverride(slotName);
            if (slot != null && slot.IsEmpty)
            {
                slotOverrides.Remove(slot);
                slotLookups = null;
            }
        }

        private static void AddActive(List<UMARigEffector> from, List<UMARigEffector> into)
        {
            if (from == null)
            {
                return;
            }
            for (int i = 0; i < from.Count; i++)
            {
                if (from[i] != null && from[i].IsActive)
                {
                    into.Add(from[i]);
                }
            }
        }

        private static bool HasActive(List<UMARigEffector> list, bool clearBodyOnly)
        {
            if (list == null)
            {
                return false;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].IsActive && (!clearBodyOnly || list[i].action == UMARigEffectorAction.ClearBody))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Removes everything a slot has of its own, bone entries and effectors, so it follows the general profile again.</summary>
        public bool RemoveSlotOverride(string slotName)
        {
            int removed = slotOverrides.RemoveAll(o => o != null && string.Equals(o.slotName, slotName, StringComparison.Ordinal));
            slotLookups = null;
            return removed > 0;
        }

        /// <summary>True when the bone has an entry of the slot's own (not merely a general one).</summary>
        public bool HasSlotCorrection(string boneName, string slotName)
        {
            return !string.IsNullOrEmpty(boneName) && TryGetSlotCorrection(UMAUtils.StringToHash(boneName), slotName, out _);
        }

        /// <summary>
        /// The entry that applies to a bone for a slot: the slot's own entry when it has one, otherwise
        /// the general entry. With no slot name this is the general entry.
        /// </summary>
        public bool TryGetCorrection(int boneHash, string slotName, out UMARigBoneCorrection correction)
        {
            if (TryGetSlotCorrection(boneHash, slotName, out correction))
            {
                return true;
            }
            return TryGetCorrection(boneHash, out correction);
        }

        public bool TryGetCorrection(string boneName, string slotName, out UMARigBoneCorrection correction)
        {
            correction = null;
            return !string.IsNullOrEmpty(boneName) && TryGetCorrection(UMAUtils.StringToHash(boneName), slotName, out correction);
        }

        /// <summary>True when the profile has any entry that can affect the slot: a general one, or one of the slot's own.</summary>
        public bool HasAnyCorrections(string slotName)
        {
            if (boneCorrections.Count > 0)
            {
                return true;
            }
            UMARigSlotOverride slot = GetSlotOverride(slotName);
            return slot != null && slot.boneCorrections.Count > 0;
        }

        /// <summary>
        /// Returns the bone's entry in the given scope, adding one when there is none. With a slot name
        /// the entry is the slot's own, and a new one starts as a copy of the general entry, so making
        /// it changes nothing until it is edited. With no slot name this is the general entry.
        /// </summary>
        public UMARigBoneCorrection GetOrAddCorrection(string boneName, string slotName)
        {
            if (string.IsNullOrEmpty(slotName))
            {
                return GetOrAddCorrection(boneName);
            }
            if (string.IsNullOrEmpty(boneName))
            {
                throw new ArgumentException("A bone name is required.", nameof(boneName));
            }
            if (TryGetSlotCorrection(UMAUtils.StringToHash(boneName), slotName, out UMARigBoneCorrection existing))
            {
                return existing;
            }

            UMARigSlotOverride slot = GetSlotOverride(slotName);
            if (slot == null)
            {
                slot = new UMARigSlotOverride { slotName = slotName };
                slotOverrides.Add(slot);
            }
            var entry = new UMARigBoneCorrection { boneName = boneName, mode = defaultMode };
            if (TryGetCorrection(boneName, out UMARigBoneCorrection general))
            {
                entry.mode = general.mode;
                entry.includeChildren = general.includeChildren;
                entry.positionOffset = general.positionOffset;
                entry.rotationOffsetEuler = general.rotationOffsetEuler;
                entry.scaleOffset = general.scaleOffset;
            }
            slot.boneCorrections.Add(entry);
            slotLookups = null;
            return entry;
        }

        /// <summary>
        /// Removes the bone's entry in the given scope. For a slot, the bone then follows the general
        /// entry again; a slot left with no entries of its own is dropped from the list.
        /// </summary>
        public bool RemoveCorrection(string boneName, string slotName)
        {
            if (string.IsNullOrEmpty(slotName))
            {
                return RemoveCorrection(boneName);
            }
            UMARigSlotOverride slot = GetSlotOverride(slotName);
            if (slot == null)
            {
                return false;
            }
            int removed = slot.boneCorrections.RemoveAll(entry => entry != null && string.Equals(entry.boneName, boneName, StringComparison.Ordinal));
            if (slot.IsEmpty)
            {
                slotOverrides.Remove(slot);
            }
            slotLookups = null;
            return removed > 0;
        }

        private bool TryGetSlotCorrection(int boneHash, string slotName, out UMARigBoneCorrection correction)
        {
            correction = null;
            if (string.IsNullOrEmpty(slotName) || slotOverrides.Count == 0)
            {
                return false;
            }
            if (slotLookups == null)
            {
                slotLookups = new Dictionary<string, Dictionary<int, UMARigBoneCorrection>>(StringComparer.Ordinal);
            }
            if (!slotLookups.TryGetValue(slotName, out Dictionary<int, UMARigBoneCorrection> slotLookup))
            {
                slotLookup = new Dictionary<int, UMARigBoneCorrection>();
                UMARigSlotOverride slot = GetSlotOverride(slotName);
                if (slot != null)
                {
                    for (int i = 0; i < slot.boneCorrections.Count; i++)
                    {
                        UMARigBoneCorrection entry = slot.boneCorrections[i];
                        if (entry != null && !string.IsNullOrEmpty(entry.boneName))
                        {
                            slotLookup[entry.BoneHash] = entry;
                        }
                    }
                }
                slotLookups.Add(slotName, slotLookup);
            }
            return slotLookup.TryGetValue(boneHash, out correction);
        }

        // --- general entries -----------------------------------------------------------------------

        public bool TryGetCorrection(int boneHash, out UMARigBoneCorrection correction)
        {
            if (lookup == null || lookup.Count != boneCorrections.Count)
            {
                lookup = new Dictionary<int, UMARigBoneCorrection>();
                for (int i = 0; i < boneCorrections.Count; i++)
                {
                    UMARigBoneCorrection entry = boneCorrections[i];
                    if (entry == null || string.IsNullOrEmpty(entry.boneName))
                    {
                        continue;
                    }
                    lookup[entry.BoneHash] = entry;
                }
            }
            return lookup.TryGetValue(boneHash, out correction);
        }

        public bool TryGetCorrection(string boneName, out UMARigBoneCorrection correction)
        {
            correction = null;
            return !string.IsNullOrEmpty(boneName) && TryGetCorrection(UMAUtils.StringToHash(boneName), out correction);
        }

        /// <summary>Returns the entry for a bone, adding a neutral one when the bone has none yet.</summary>
        public UMARigBoneCorrection GetOrAddCorrection(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                throw new ArgumentException("A bone name is required.", nameof(boneName));
            }
            if (TryGetCorrection(boneName, out UMARigBoneCorrection existing))
            {
                return existing;
            }
            var entry = new UMARigBoneCorrection { boneName = boneName, mode = defaultMode };
            boneCorrections.Add(entry);
            lookup = null;
            return entry;
        }

        /// <summary>Removes a bone's entry. Returns false when the bone had none.</summary>
        public bool RemoveCorrection(string boneName)
        {
            int removed = boneCorrections.RemoveAll(entry => entry != null && string.Equals(entry.boneName, boneName, StringComparison.Ordinal));
            lookup = null;
            return removed > 0;
        }

        public bool IsFavorite(string boneName)
        {
            return !string.IsNullOrEmpty(boneName) && favoriteBones.Contains(boneName);
        }

        public void SetFavorite(string boneName, bool favorite)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return;
            }
            bool isFavorite = favoriteBones.Contains(boneName);
            if (favorite && !isFavorite)
            {
                favoriteBones.Add(boneName);
            }
            else if (!favorite && isFavorite)
            {
                favoriteBones.Remove(boneName);
            }
        }

        /// <summary>
        /// The name of the bone on the other side of the body, by the naming UMA rigs use: a
        /// <c>Left</c> or <c>Right</c> prefix, or an <c>_L</c> or <c>_R</c> suffix. Null for a bone
        /// that is not named for a side.
        /// </summary>
        public static string GetOppositeBoneName(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return null;
            }
            if (boneName.EndsWith("_L", StringComparison.Ordinal)) return boneName.Substring(0, boneName.Length - 2) + "_R";
            if (boneName.EndsWith("_R", StringComparison.Ordinal)) return boneName.Substring(0, boneName.Length - 2) + "_L";
            if (boneName.StartsWith("Left", StringComparison.Ordinal)) return "Right" + boneName.Substring(4);
            if (boneName.StartsWith("Right", StringComparison.Ordinal)) return "Left" + boneName.Substring(5);
            return null;
        }

        /// <summary>True for a bone named for the left side (<c>Left...</c> or <c>..._L</c>).</summary>
        public static bool IsLeftBoneName(string boneName)
        {
            return !string.IsNullOrEmpty(boneName) &&
                   (boneName.StartsWith("Left", StringComparison.Ordinal) || boneName.EndsWith("_L", StringComparison.Ordinal));
        }

        /// <summary>
        /// Makes the opposite-side bone's entry the mirror image of <paramref name="boneName"/>'s,
        /// across the avatar's left-right plane (x = 0 in avatar space): the position offset has its
        /// x flipped, the rotation turns the other way about the y and z axes, and the scale is
        /// carried to whichever of the opposite bone's own axes mirrors each of this bone's axes.
        /// Mode and "carry to children" are copied. Returns the opposite bone's entry, or null when
        /// the bone has no entry, is not named for a side, or its opposite is not in the rig.
        /// </summary>
        /// <param name="rig">
        /// The rig the profile converts to. It supplies the bones' rest axes for the scale and is
        /// used to check that the opposite bone exists. May be null: scale is then copied as it is.
        /// </param>
        public UMARigBoneCorrection MirrorCorrection(string boneName, UMARigRestPose rig)
        {
            return MirrorCorrection(boneName, rig, null);
        }

        /// <summary>
        /// As <see cref="MirrorCorrection(string, UMARigRestPose)"/>, within one scope. With a slot
        /// name, the entry that applies to the bone for that slot (its own, or the general one) is
        /// mirrored into the slot's own entry for the opposite bone; general entries are not touched.
        /// </summary>
        public UMARigBoneCorrection MirrorCorrection(string boneName, UMARigRestPose rig, string slotName)
        {
            string oppositeName = GetOppositeBoneName(boneName);
            if (oppositeName == null || !TryGetCorrection(boneName, slotName, out UMARigBoneCorrection source))
            {
                return null;
            }
            if (rig != null && !rig.Contains(UMAUtils.StringToHash(oppositeName)))
            {
                return null;
            }

            UMARigBoneCorrection target = GetOrAddCorrection(oppositeName, slotName);
            target.mode = source.mode;
            target.includeChildren = source.includeChildren;
            target.positionOffset = new Vector3(-source.positionOffset.x, source.positionOffset.y, source.positionOffset.z);
            target.rotationOffsetEuler = new Vector3(source.rotationOffsetEuler.x, -source.rotationOffsetEuler.y, -source.rotationOffsetEuler.z);
            target.scaleOffset = MirrorScale(source.SafeScale, boneName, oppositeName, rig);
            return target;
        }

        /// <summary>
        /// Mirrors every entry on one side onto the other, replacing what the other side had.
        /// Returns how many entries were mirrored.
        /// </summary>
        /// <param name="leftToRight">True copies left bones onto right bones; false the other way.</param>
        public int MirrorSide(bool leftToRight, UMARigRestPose rig)
        {
            return MirrorSide(leftToRight, rig, null);
        }

        /// <summary>
        /// As <see cref="MirrorSide(bool, UMARigRestPose)"/>, within one scope. With a slot name, only
        /// the slot's own entries are mirrored, into the slot's own entries for the other side.
        /// </summary>
        public int MirrorSide(bool leftToRight, UMARigRestPose rig, string slotName)
        {
            List<UMARigBoneCorrection> entries = boneCorrections;
            if (!string.IsNullOrEmpty(slotName))
            {
                UMARigSlotOverride slot = GetSlotOverride(slotName);
                if (slot == null)
                {
                    return 0;
                }
                entries = slot.boneCorrections;
            }

            // Mirroring adds entries, so work from a copy of the names.
            var names = new List<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                UMARigBoneCorrection entry = entries[i];
                if (entry != null && GetOppositeBoneName(entry.boneName) != null && IsLeftBoneName(entry.boneName) == leftToRight)
                {
                    names.Add(entry.boneName);
                }
            }
            int mirrored = 0;
            for (int i = 0; i < names.Count; i++)
            {
                if (MirrorCorrection(names[i], rig, slotName) != null)
                {
                    mirrored++;
                }
            }
            return mirrored;
        }

        /// <summary>
        /// A scale is stored along a bone's own rest axes, and the opposite bone's axes need not be
        /// laid out the same way. Each of the opposite bone's axes takes the scale of whichever of
        /// this bone's axes it is the mirror image of.
        /// </summary>
        private static Vector3 MirrorScale(Vector3 scale, string boneName, string oppositeName, UMARigRestPose rig)
        {
            if (rig == null ||
                !rig.TryGetAvatarMatrix(UMAUtils.StringToHash(boneName), out Matrix4x4 boneToAvatar) ||
                !rig.TryGetAvatarMatrix(UMAUtils.StringToHash(oppositeName), out Matrix4x4 oppositeToAvatar))
            {
                return scale;
            }

            Quaternion frame = UMARigRestPose.NormalizeSafe(boneToAvatar.rotation);
            Quaternion oppositeFrame = UMARigRestPose.NormalizeSafe(oppositeToAvatar.rotation);
            Quaternion toBone = Quaternion.Inverse(frame);
            var result = Vector3.one;
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 local = Vector3.zero;
                local[axis] = 1f;
                // The opposite bone's axis, in avatar space, mirrored, then seen from this bone.
                Vector3 inAvatar = oppositeFrame * local;
                Vector3 seen = toBone * new Vector3(-inAvatar.x, inAvatar.y, inAvatar.z);
                int match = 0;
                if (Mathf.Abs(seen.y) > Mathf.Abs(seen[match])) match = 1;
                if (Mathf.Abs(seen.z) > Mathf.Abs(seen[match])) match = 2;
                result[axis] = scale[match];
            }
            return result;
        }

        private void OnValidate()
        {
            lookup = null;
            slotLookups = null;
        }

        /// <summary>Drops the cached lookups after entries were edited in code.</summary>
        public void InvalidateLookup()
        {
            lookup = null;
            slotLookups = null;
        }
    }
}
