using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// The rest (bind) skeleton of a UMA rig as pure data: the bone hierarchy the generator
    /// builds from slot <see cref="UMAMeshData.umaBones"/>, plus the transform that places the
    /// rig root ("Global") in avatar space. Every matrix this class returns is expressed in
    /// avatar space, which is the space a generated character stands in with its feet at the
    /// origin, so two rigs built for different races can be compared directly.
    /// </summary>
    /// <remarks>
    /// Avatar space here means the local space of the SkinnedMeshRenderer UMA generates, which
    /// sits at identity under the avatar GameObject. UMA parents the skeleton as
    /// avatar → Root → Global → Position → ...; Root is rotated by Euler(270, 0, 0) when the
    /// race has <c>FixupRotations</c> set, and Global takes its transform from the race T-pose.
    /// <see cref="ComputeRigRootToAvatar"/> reproduces that so no live skeleton is needed.
    /// </remarks>
    public sealed class UMARigRestPose
    {
        private readonly Dictionary<int, UMATransform> bones;
        private readonly Dictionary<int, Matrix4x4> avatarMatrixCache = new Dictionary<int, Matrix4x4>();
        private readonly List<UMAMeshData> donorMeshes = new List<UMAMeshData>();
        private Dictionary<int, List<int>> childrenByHash;
        private Dictionary<int, Matrix4x4> donorBindPoses;
        // The rig is immutable once built, so the canonical mesh space is computed once per tolerance.
        private bool canonicalCached;
        private float canonicalTolerance;
        private Matrix4x4 canonicalMeshToAvatar;
        private int canonicalMismatchCount;
        private float canonicalMaxMismatch;

        /// <summary>Name of the rig root bone that slot bind poses are relative to, normally "Global".</summary>
        public string RootBoneName { get; }

        /// <summary>Hash of <see cref="RootBoneName"/>.</summary>
        public int RootBoneHash { get; }

        /// <summary>Transform of the rig root bone in avatar space (Root * Global).</summary>
        public Matrix4x4 RigRootToAvatar { get; }

        /// <summary>Every bone in the rest skeleton, keyed by name hash. The root bone itself is not included.</summary>
        public IReadOnlyDictionary<int, UMATransform> Bones => bones;

        /// <summary>Number of bones below the root.</summary>
        public int BoneCount => bones.Count;

        /// <summary>
        /// Mesh data of slots bound to this rig (normally the race's base body slots). Their bind
        /// poses define the canonical mesh space for new slots on this rig.
        /// </summary>
        public IReadOnlyList<UMAMeshData> DonorMeshes => donorMeshes;

        private UMARigRestPose(Dictionary<int, UMATransform> bones, string rootBoneName, Matrix4x4 rigRootToAvatar)
        {
            this.bones = bones ?? new Dictionary<int, UMATransform>();
            RootBoneName = string.IsNullOrEmpty(rootBoneName) ? "Global" : rootBoneName;
            RootBoneHash = UMAUtils.StringToHash(RootBoneName);
            RigRootToAvatar = rigRootToAvatar;
        }

        /// <summary>
        /// Builds the rest pose of a race from its base recipe slots and T-pose.
        /// </summary>
        public static UMARigRestPose FromRace(RaceData race)
        {
            if (race == null)
            {
                throw new ArgumentNullException(nameof(race));
            }
            if (!TryFromRace(race, out UMARigRestPose rig, out string reason))
            {
                throw new InvalidOperationException(reason);
            }
            return rig;
        }

        /// <summary>
        /// Builds the rest pose of a race without throwing. Returns false, with the cause in
        /// <paramref name="reason"/>, when the race is null or its base recipe has no slots with mesh
        /// data (for example when those slots are not loaded in a build).
        /// </summary>
        public static bool TryFromRace(RaceData race, out UMARigRestPose rig, out string reason)
        {
            rig = null;
            reason = null;
            if (race == null)
            {
                reason = "No race was given.";
                return false;
            }

            var slots = new List<SlotDataAsset>();
            if (race.baseRaceRecipe != null)
            {
                UMAData.UMARecipe recipe = race.baseRaceRecipe.GetCachedRecipe(loadSlots: true);
                SlotData[] slotData = recipe != null ? recipe.GetAllSlots() : null;
                if (slotData != null)
                {
                    for (int i = 0; i < slotData.Length; i++)
                    {
                        if (slotData[i] != null && slotData[i].asset != null && !UMAMeshData.IsNullOrEmptyMeshData(slotData[i].asset.meshData))
                        {
                            slots.Add(slotData[i].asset);
                        }
                    }
                }
            }

            if (slots.Count == 0)
            {
                reason = $"Race '{race.raceName}' has no base recipe slots with mesh data; cannot build its rest pose.";
                return false;
            }

            string rootName = null;
            for (int i = 0; i < slots.Count && rootName == null; i++)
            {
                UMAMeshData md = slots[i].meshData;
                if (!UMAMeshData.IsNullOrEmptyMeshData(md) && !string.IsNullOrEmpty(md.RootBoneName))
                {
                    rootName = md.RootBoneName;
                }
            }

            rig = FromSlots(slots, rootName, ComputeRigRootToAvatar(race, rootName));
            return true;
        }

        /// <summary>
        /// Builds a rest pose from slots bound to the rig. Bones are taken from each slot's
        /// umaBones; the first slot that defines a bone wins, which is the order the generator uses.
        /// The slots also serve as bind-pose donors for the canonical mesh space.
        /// </summary>
        public static UMARigRestPose FromSlots(IEnumerable<SlotDataAsset> slots, string rootBoneName, Matrix4x4 rigRootToAvatar)
        {
            var map = new Dictionary<int, UMATransform>();
            var rig = new UMARigRestPose(map, rootBoneName, rigRootToAvatar);
            if (slots != null)
            {
                foreach (SlotDataAsset slot in slots)
                {
                    if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData))
                    {
                        continue;
                    }

                    AddBones(map, slot.meshData.umaBones);
                    rig.donorMeshes.Add(slot.meshData);
                }
            }
            return rig;
        }

        /// <summary>
        /// Builds a rest pose from explicit bone definitions, with no bind-pose donors. Mesh space
        /// is then identical to avatar space.
        /// </summary>
        public static UMARigRestPose FromBones(IEnumerable<UMATransform> boneDefinitions, string rootBoneName, Matrix4x4 rigRootToAvatar)
        {
            var map = new Dictionary<int, UMATransform>();
            AddBones(map, boneDefinitions);
            return new UMARigRestPose(map, rootBoneName, rigRootToAvatar);
        }

        /// <summary>
        /// Returns a rest pose that also knows the given bones, for bones a slot defines itself
        /// (for example prop bones parented to a hand). Bones already in this rig are not replaced.
        /// This rig is not modified.
        /// </summary>
        public UMARigRestPose WithAdditionalBones(IEnumerable<UMATransform> additionalBones)
        {
            if (additionalBones == null)
            {
                return this;
            }

            bool anyNew = false;
            foreach (UMATransform bone in additionalBones)
            {
                if (bone != null && !bones.ContainsKey(bone.hash))
                {
                    anyNew = true;
                    break;
                }
            }
            if (!anyNew)
            {
                return this;
            }

            var map = new Dictionary<int, UMATransform>(bones);
            AddBones(map, additionalBones);
            var rig = new UMARigRestPose(map, RootBoneName, RigRootToAvatar);
            rig.donorMeshes.AddRange(donorMeshes);
            return rig;
        }

        /// <summary>
        /// Reproduces the avatar-space transform of the rig root the generator creates for a race:
        /// Root (Euler(270,0,0) when FixupRotations is set) times Global (from the race T-pose, or the
        /// generator default when the T-pose has no Global entry).
        /// </summary>
        public static Matrix4x4 ComputeRigRootToAvatar(RaceData race, string rootBoneName = null)
        {
            if (race == null)
            {
                return Matrix4x4.identity;
            }

            bool fixup = race.FixupRotations;
            Matrix4x4 root = fixup ? Matrix4x4.Rotate(Quaternion.Euler(270f, 0f, 0f)) : Matrix4x4.identity;
            Matrix4x4 global = fixup ? Matrix4x4.Rotate(Quaternion.Euler(90f, 90f, 0f)) : Matrix4x4.identity;

            string globalName = string.IsNullOrEmpty(rootBoneName) ? "Global" : rootBoneName;
            UmaTPose tpose = race.TPose;
            if (tpose != null)
            {
                tpose.DeSerialize();
                SkeletonBone[] info = tpose.boneInfo;
                if (info != null)
                {
                    for (int i = 0; i < info.Length; i++)
                    {
                        if (string.Equals(info[i].name, globalName, StringComparison.Ordinal))
                        {
                            global = Matrix4x4.TRS(info[i].position, NormalizeSafe(info[i].rotation), SanitizeScale(info[i].scale));
                            break;
                        }
                    }
                }
            }

            return root * global;
        }

        /// <summary>True when the rig defines the bone, or it is the rig root.</summary>
        public bool Contains(int hash)
        {
            return hash == RootBoneHash || bones.ContainsKey(hash);
        }

        public bool TryGetBone(int hash, out UMATransform bone)
        {
            return bones.TryGetValue(hash, out bone);
        }

        /// <summary>Parent hash of a bone, 0 when unknown. The root's parent is 0.</summary>
        public int GetParentHash(int hash)
        {
            if (hash == RootBoneHash)
            {
                return 0;
            }
            return bones.TryGetValue(hash, out UMATransform bone) ? bone.parent : 0;
        }

        /// <summary>
        /// Avatar-space rest transform of a bone (bone local → avatar). The root bone returns
        /// <see cref="RigRootToAvatar"/>. Bones whose parent chain leaves the rig are treated as
        /// children of the root, matching how the generator parents orphaned bones.
        /// </summary>
        public bool TryGetAvatarMatrix(int hash, out Matrix4x4 boneToAvatar)
        {
            if (hash == RootBoneHash)
            {
                boneToAvatar = RigRootToAvatar;
                return true;
            }

            if (avatarMatrixCache.TryGetValue(hash, out boneToAvatar))
            {
                return true;
            }

            if (!bones.TryGetValue(hash, out UMATransform bone))
            {
                boneToAvatar = Matrix4x4.identity;
                return false;
            }

            Matrix4x4 local = Matrix4x4.TRS(bone.position, NormalizeSafe(bone.rotation), SanitizeScale(bone.scale));
            Matrix4x4 parentToAvatar;
            if (bone.parent == 0 || bone.parent == hash || !TryGetAvatarMatrix(bone.parent, out parentToAvatar))
            {
                parentToAvatar = RigRootToAvatar;
            }

            boneToAvatar = parentToAvatar * local;
            avatarMatrixCache[hash] = boneToAvatar;
            return true;
        }

        /// <summary>Avatar-space joint position of a bone.</summary>
        public bool TryGetAvatarPosition(int hash, out Vector3 position)
        {
            if (TryGetAvatarMatrix(hash, out Matrix4x4 m))
            {
                position = m.GetColumn(3);
                return true;
            }
            position = Vector3.zero;
            return false;
        }

        /// <summary>Hashes of the direct children of a bone that this rig defines.</summary>
        public IReadOnlyList<int> GetChildren(int hash)
        {
            if (childrenByHash == null)
            {
                childrenByHash = new Dictionary<int, List<int>>();
                foreach (KeyValuePair<int, UMATransform> kv in bones)
                {
                    int parent = kv.Value.parent;
                    if (!childrenByHash.TryGetValue(parent, out List<int> list))
                    {
                        list = new List<int>();
                        childrenByHash.Add(parent, list);
                    }
                    list.Add(kv.Key);
                }
            }
            return childrenByHash.TryGetValue(hash, out List<int> children) ? children : Array.Empty<int>();
        }

        /// <summary>
        /// The mesh → avatar transform that slots bound to this rig share, reconstructed from the
        /// donor slots' bind poses. Every bone of a consistently bound slot yields the same matrix.
        /// The transform most donor bones agree on (within <paramref name="tolerance"/>) is returned,
        /// so one oddly bound bone cannot decide the space; bones that disagree with it are counted in
        /// <paramref name="mismatchCount"/>. Without donors the canonical mesh space is avatar space
        /// (identity).
        /// </summary>
        public Matrix4x4 GetCanonicalMeshToAvatar(float tolerance, out int mismatchCount, out float maxMismatch)
        {
            if (!canonicalCached || canonicalTolerance != tolerance)
            {
                var candidates = new List<Matrix4x4>();
                for (int d = 0; d < donorMeshes.Count; d++)
                {
                    UMAMeshData md = donorMeshes[d];
                    if (md == null || md.bindPoses == null || md.boneNameHashes == null)
                    {
                        continue;
                    }

                    int count = Mathf.Min(md.bindPoses.Length, md.boneNameHashes.Length);
                    for (int i = 0; i < count; i++)
                    {
                        if (TryGetAvatarMatrix(md.boneNameHashes[i], out Matrix4x4 boneToAvatar))
                        {
                            candidates.Add(boneToAvatar * md.bindPoses[i]);
                        }
                    }
                }

                if (!TryGetMostCommon(candidates, tolerance, out canonicalMeshToAvatar, out canonicalMismatchCount, out canonicalMaxMismatch))
                {
                    canonicalMeshToAvatar = Matrix4x4.identity;
                }
                canonicalTolerance = tolerance;
                canonicalCached = true;
            }

            mismatchCount = canonicalMismatchCount;
            maxMismatch = canonicalMaxMismatch;
            return canonicalMeshToAvatar;
        }

        /// <summary>
        /// Picks the matrix most of <paramref name="candidates"/> agree on within
        /// <paramref name="tolerance"/>; the earliest group wins a tie. Reports how many candidates
        /// differ from it and by how much. Returns false when there are no candidates.
        /// </summary>
        internal static bool TryGetMostCommon(IReadOnlyList<Matrix4x4> candidates, float tolerance, out Matrix4x4 common,
            out int mismatchCount, out float maxMismatch)
        {
            common = Matrix4x4.identity;
            mismatchCount = 0;
            maxMismatch = 0f;
            if (candidates == null || candidates.Count == 0)
            {
                return false;
            }

            // Group candidates around the first member of each group. Consistently bound rigs have one
            // group, so this stays linear in practice.
            var representatives = new List<Matrix4x4>();
            var counts = new List<int>();
            for (int i = 0; i < candidates.Count; i++)
            {
                int group = -1;
                for (int g = 0; g < representatives.Count; g++)
                {
                    if (MaxAbsDifference(candidates[i], representatives[g]) <= tolerance)
                    {
                        group = g;
                        break;
                    }
                }
                if (group < 0)
                {
                    representatives.Add(candidates[i]);
                    counts.Add(1);
                }
                else
                {
                    counts[group]++;
                }
            }

            int best = 0;
            for (int g = 1; g < counts.Count; g++)
            {
                if (counts[g] > counts[best])
                {
                    best = g;
                }
            }
            common = representatives[best];

            for (int i = 0; i < candidates.Count; i++)
            {
                float diff = MaxAbsDifference(candidates[i], common);
                if (diff > tolerance)
                {
                    mismatchCount++;
                }
                if (diff > maxMismatch)
                {
                    maxMismatch = diff;
                }
            }
            return true;
        }

        /// <summary>
        /// Finds a donor bind pose for a bone, if any donor slot binds to it. The first donor slot
        /// that binds the bone wins.
        /// </summary>
        public bool TryGetDonorBindPose(int hash, out Matrix4x4 bindPose)
        {
            if (donorBindPoses == null)
            {
                donorBindPoses = new Dictionary<int, Matrix4x4>();
                for (int d = 0; d < donorMeshes.Count; d++)
                {
                    UMAMeshData md = donorMeshes[d];
                    if (md == null || md.bindPoses == null || md.boneNameHashes == null)
                    {
                        continue;
                    }

                    int count = Mathf.Min(md.bindPoses.Length, md.boneNameHashes.Length);
                    for (int i = 0; i < count; i++)
                    {
                        if (!donorBindPoses.ContainsKey(md.boneNameHashes[i]))
                        {
                            donorBindPoses.Add(md.boneNameHashes[i], md.bindPoses[i]);
                        }
                    }
                }
            }

            if (donorBindPoses.TryGetValue(hash, out bindPose))
            {
                return true;
            }
            bindPose = Matrix4x4.identity;
            return false;
        }

        /// <summary>Largest absolute difference between corresponding elements of two matrices.</summary>
        public static float MaxAbsDifference(Matrix4x4 a, Matrix4x4 b)
        {
            float max = 0f;
            for (int i = 0; i < 16; i++)
            {
                float diff = Mathf.Abs(a[i] - b[i]);
                if (diff > max)
                {
                    max = diff;
                }
            }
            return max;
        }

        private static void AddBones(Dictionary<int, UMATransform> map, IEnumerable<UMATransform> source)
        {
            if (source == null)
            {
                return;
            }
            foreach (UMATransform bone in source)
            {
                if (bone != null && !map.ContainsKey(bone.hash))
                {
                    map.Add(bone.hash, bone);
                }
            }
        }

        internal static Quaternion NormalizeSafe(Quaternion rotation)
        {
            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w);
            if (float.IsNaN(magnitude) || float.IsInfinity(magnitude) || magnitude < 1e-8f)
            {
                return Quaternion.identity;
            }
            float inv = 1f / magnitude;
            return new Quaternion(rotation.x * inv, rotation.y * inv, rotation.z * inv, rotation.w * inv);
        }

        internal static Vector3 SanitizeScale(Vector3 scale)
        {
            if (float.IsNaN(scale.x) || float.IsInfinity(scale.x) || scale.x == 0f) scale.x = 1f;
            if (float.IsNaN(scale.y) || float.IsInfinity(scale.y) || scale.y == 0f) scale.y = 1f;
            if (float.IsNaN(scale.z) || float.IsInfinity(scale.z) || scale.z == 0f) scale.z = 1f;
            return scale;
        }
    }
}
