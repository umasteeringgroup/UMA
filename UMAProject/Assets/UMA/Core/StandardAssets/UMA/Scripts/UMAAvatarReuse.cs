using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Lets a rig update keep its humanoid Avatar when building it again would give the same one.
    ///
    /// Every rig update resets the skeleton to its T-pose, applies DNA, builds a new Avatar from that pose and
    /// then applies the T-pose asset's stored human pose through the new Avatar. When DNA has not changed, the
    /// Avatar comes out the same as last time. This class records what the current Avatar was built from; when a
    /// later update starts from exactly the same inputs, the Avatar is kept and only the pose step is run again,
    /// with the same Unity call a rebuild uses, so the bones end up where a rebuild would leave them.
    ///
    /// "The same" is checked value for value, not estimated: the same Animator, Avatar, skeleton and bone
    /// objects with the same parents; every bone's local position, rotation and scale bit for bit; and the same
    /// human description (bone names and poses, human mapping, limits and stretch settings, as built from the
    /// T-pose asset). Any difference builds a new Avatar as before.
    /// </summary>
    internal static class UMAAvatarReuse
    {
        internal sealed class Record
        {
            public bool complete;
            public Avatar avatar;
            public Animator animator;
            public UMASkeleton skeleton;
            public bool preserveHips;
            public Transform root;
            public Transform umaRoot;
            public Vector3 rootScale;
            public HumanDescription description;

            // Every skeleton bone as it was just before the Avatar was built, in the skeleton's own order.
            public int boneCount;
            public Transform[] bones;
            public Transform[] parents;
            public Vector3[] positions;
            public Quaternion[] rotations;
            public Vector3[] scales;
        }

        /// <summary>Number of Avatars kept instead of rebuilt since the last reset. For diagnostics and tests.</summary>
        internal static long ReuseCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            ReuseCount = 0;
        }

        private static bool Supported(UMAData data)
        {
            // Other skeleton types create, pose and remove their bones differently.
            return data != null && data.skeleton != null && data.skeleton.GetType() == typeof(UMASkeleton) &&
                data.umaRoot != null && data.umaRecipe != null && data.umaRecipe.raceData != null &&
                data.umaRecipe.raceData.umaTarget == RaceData.UMATarget.Humanoid;
        }

        /// <summary>
        /// Call just before a humanoid Avatar is built: notes the state of every bone. Returns null when this
        /// avatar cannot use the shortcut (for example a bone-baking skeleton).
        /// </summary>
        internal static Record Begin(UMAData data, Animator animator, bool preserveHips)
        {
            if (data != null) data.avatarReuse = null;
            if (!Supported(data) || animator == null)
            {
                return null;
            }

            var record = new Record
            {
                animator = animator,
                skeleton = data.skeleton,
                preserveHips = preserveHips,
                root = data.transform,
                umaRoot = data.umaRoot.transform,
                rootScale = data.transform.localScale
            };
            int count = data.skeleton.boneHashData.Count;
            record.bones = new Transform[count];
            record.parents = new Transform[count];
            record.positions = new Vector3[count];
            record.rotations = new Quaternion[count];
            record.scales = new Vector3[count];
            int index = 0;
            foreach (UMASkeleton.BoneData bone in data.skeleton.boneHashData.Values)
            {
                Transform transform = bone != null ? bone.boneTransform : null;
                if (transform == null)
                {
                    return null;
                }
                record.bones[index] = transform;
                record.parents[index] = transform.parent;
                transform.GetLocalPositionAndRotation(out record.positions[index], out record.rotations[index]);
                record.scales[index] = transform.localScale;
                index++;
            }
            record.boneCount = index;
            return record;
        }

        /// <summary>
        /// Call after the Avatar from <see cref="Begin"/> has been built and assigned: notes the description it
        /// was built from.
        /// </summary>
        internal static void Complete(UMAData data, Record record, Avatar avatar, HumanDescription description)
        {
            if (data == null || record == null || avatar == null)
            {
                return;
            }

            record.avatar = avatar;
            // Own copies: the T-pose asset's arrays can be edited later.
            description.skeleton = description.skeleton != null ? (SkeletonBone[])description.skeleton.Clone() : null;
            description.human = description.human != null ? (HumanBone[])description.human.Clone() : null;
            record.description = description;
            record.complete = true;
            data.avatarReuse = record;
        }

        /// <summary>
        /// True when the Animator's current Avatar was built from exactly the state the avatar is in now. The
        /// Avatar is then kept and the stored human pose is applied through it, as building it would.
        /// </summary>
        internal static bool TryReuse(UMAData data, Animator animator, bool preserveHips)
        {
            Record record = data != null ? data.avatarReuse : null;
            if (record == null || !record.complete || animator == null || !Supported(data))
            {
                return false;
            }
            if (record.avatar == null || animator.avatar != record.avatar || record.animator != animator ||
                !ReferenceEquals(record.skeleton, data.skeleton) || record.preserveHips != preserveHips ||
                record.root != data.transform || record.umaRoot != data.umaRoot.transform ||
                !Same(record.rootScale, data.transform.localScale))
            {
                return false;
            }

            var bones = data.skeleton.boneHashData;
            if (bones.Count != record.boneCount)
            {
                return false;
            }
            int index = 0;
            foreach (UMASkeleton.BoneData bone in bones.Values)
            {
                Transform transform = bone != null ? bone.boneTransform : null;
                if (transform == null || transform != record.bones[index] || transform.parent != record.parents[index])
                {
                    return false;
                }
                transform.GetLocalPositionAndRotation(out Vector3 position, out Quaternion rotation);
                if (!Same(position, record.positions[index]) || !Same(rotation, record.rotations[index]) ||
                    !Same(transform.localScale, record.scales[index]))
                {
                    return false;
                }
                index++;
            }

            UmaTPose tpose = data.GetTPose();
            if (tpose == null)
            {
                return false;
            }
            tpose.DeSerialize();
            if (!Same(UMAGeneratorBase.CreateHumanDescription(data, tpose), record.description))
            {
                return false;
            }

            try
            {
                UMAGeneratorBase.ApplyExtractedHumanPose(data, tpose, record.avatar, preserveHips);
            }
            catch (System.Exception ex)
            {
                // Building an Avatar reports a failing pose step the same way.
                Debug.LogError("Error creating avatar: " + ex.Message);
            }
            ReuseCount++;
            return true;
        }

        // All comparisons are exact. Unity's own == on vectors and quaternions allows a small difference.
        private static bool Same(float a, float b)
        {
            return a.Equals(b);
        }

        private static bool Same(Vector3 a, Vector3 b)
        {
            return a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z);
        }

        private static bool Same(Quaternion a, Quaternion b)
        {
            return a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z) && a.w.Equals(b.w);
        }

        private static bool Same(HumanDescription a, HumanDescription b)
        {
            if (!Same(a.armStretch, b.armStretch) || !Same(a.legStretch, b.legStretch) || !Same(a.feetSpacing, b.feetSpacing) ||
                !Same(a.lowerArmTwist, b.lowerArmTwist) || !Same(a.lowerLegTwist, b.lowerLegTwist) ||
                !Same(a.upperArmTwist, b.upperArmTwist) || !Same(a.upperLegTwist, b.upperLegTwist) ||
                a.hasTranslationDoF != b.hasTranslationDoF)
            {
                return false;
            }

            SkeletonBone[] skeletonA = a.skeleton, skeletonB = b.skeleton;
            if (skeletonA == null || skeletonB == null || skeletonA.Length != skeletonB.Length)
            {
                return false;
            }
            for (int i = 0; i < skeletonA.Length; i++)
            {
                if (skeletonA[i].name != skeletonB[i].name || !Same(skeletonA[i].position, skeletonB[i].position) ||
                    !Same(skeletonA[i].rotation, skeletonB[i].rotation) || !Same(skeletonA[i].scale, skeletonB[i].scale))
                {
                    return false;
                }
            }

            HumanBone[] humanA = a.human, humanB = b.human;
            if (humanA == null || humanB == null || humanA.Length != humanB.Length)
            {
                return false;
            }
            for (int i = 0; i < humanA.Length; i++)
            {
                HumanLimit limitA = humanA[i].limit, limitB = humanB[i].limit;
                if (humanA[i].boneName != humanB[i].boneName || humanA[i].humanName != humanB[i].humanName ||
                    limitA.useDefaultValues != limitB.useDefaultValues || !Same(limitA.min, limitB.min) ||
                    !Same(limitA.max, limitB.max) || !Same(limitA.center, limitB.center) || !Same(limitA.axisLength, limitB.axisLength))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
