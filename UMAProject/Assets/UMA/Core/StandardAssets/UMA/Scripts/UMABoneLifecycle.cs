using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>Per-avatar transform ownership. Never modifies a shared generated mesh.</summary>
    internal static class UMABoneLifecycle
    {
        internal static void Restore(UMAData data, List<UMASavedItem> items)
        {
            var replacements = new Dictionary<Transform, Transform>();
            var adopted = new List<Transform>();
            string ignoreTag = UMASettings.GetValidatedIgnoreTag(data.gameObject);
            // Parent saved chains first, then merge. This also handles nested chains saved
            // by older callers and missing parents after a wardrobe/race change.
            foreach (var item in items)
            {
                if (item.Object == null) continue;
                var parent = data.skeleton.GetBoneTransform(item.ParentBoneNameHash);
                item.Object.SetParent(parent != null ? parent : data.umaRoot.transform, false);
                item.Object.localPosition = item.position;
                item.Object.localRotation = item.rotation;
                item.Object.localScale = item.scale;
                if (item.replaceExisting) CollectChain(item.Object, ignoreTag, adopted);
            }

            foreach (var bone in adopted)
            {
                var generated = data.skeleton.GetBoneTransform(UMAUtils.StringToHash(bone.name));
                if (generated != null && generated != bone)
                {
                    replacements[generated] = bone;
                    // Preserve identity/components, not a stale animation or old DNA pose.
                    bone.SetLocalPositionAndRotation(generated.localPosition, generated.localRotation);
                    bone.localScale = generated.localScale;
                }
            }
            // Move ALL new children first. New slot extensions must not be destroyed with
            // their replacement parent. Matching generated children are removed below.
            foreach (var pair in replacements)
                while (pair.Key.childCount > 0) pair.Key.GetChild(0).SetParent(pair.Value, false);
            foreach (var bone in adopted) data.skeleton.AdoptBone(bone);

            if (replacements.Count == 0) return;
            // Includes mounted/custom renderers, not just UMA's renderer array.
            foreach (var renderer in data.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = renderer.bones;
                bool changed = false;
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null && replacements.TryGetValue(bones[i], out var bone))
                    { bones[i] = bone; changed = true; }
                if (changed) renderer.bones = bones;
                if (renderer.rootBone != null && replacements.TryGetValue(renderer.rootBone, out var root))
                    renderer.rootBone = root;
            }
            foreach (var pair in replacements)
            {
                // Descendants were moved above, so only obsolete duplicates are destroyed.
                pair.Key.SetParent(null, false);
                pair.Key.gameObject.SetActive(false);
                UMAUtils.DestroySceneObject(pair.Key.gameObject);
            }
        }

        private static void CollectChain(Transform root, string ignoreTag, List<Transform> result)
        {
            if (!string.IsNullOrEmpty(ignoreTag) && root.CompareTag(ignoreTag)) return;
            result.Add(root);
            for (int i = 0; i < root.childCount; i++) CollectChain(root.GetChild(i), ignoreTag, result);
        }

        // T-pose is read from the effective asset (including OverrideTpose). DeSerialize
        // caches its decoded boneInfo. Do not cache an asset identity indefinitely: editor
        // edits and runtime race changes must be reflected on the next build.
        internal static void AddBaseBones(UMAData data, HashSet<int> required)
        {
            var pose = data.GetTPose();
            if (pose == null) return;
            pose.DeSerialize();
            if (pose.boneInfo == null) return;
            foreach (var bone in pose.boneInfo)
                if (!string.IsNullOrEmpty(bone.name)) required.Add(UMAUtils.StringToHash(bone.name));
        }

        internal static List<UMATransform> CaptureBaseBones(UMAData data)
        {
            if (data.skeleton == null || data.HasExternalSkeletonRoot) return null;
            var required = new HashSet<int>();
            AddBaseBones(data, required);
            AddAncestors(data.skeleton, required);
            var result = new List<UMATransform>(required.Count);
            foreach (int hash in required)
            {
                var definition = data.skeleton.GetBoneDefinition(hash);
                if (definition != null) result.Add(definition);
            }
            return result;
        }

        internal static void PreserveBaseBonesForBaking(UMAData data, UMAGeneratorBase generator = null)
        {
            if (generator == null) generator = data.umaGenerator;
            if (generator == null) return;
            var hashes = new HashSet<int>();
            if (generator.CleanupUnusedBones) AddBaseBones(data, hashes);
            // Bone baking must not delete live KeepChain transforms before restoration.
            // These are frame-local baking flags, NOT cleanup exemptions.
            if (generator.SaveAndRestoreIgnoredItems || generator.CleanupUnusedBones)
            {
                string tag = generator.EffectiveKeepTag;
                if (!string.IsNullOrWhiteSpace(tag) && tag != "Untagged" && data.umaRoot != null)
                {
                    bool valid;
                    try { data.umaRoot.CompareTag(tag); valid = true; }
                    catch (UnityException) { valid = false; }
                    if (valid) CollectKeepHashes(data.umaRoot.transform, tag,
                        UMASettings.GetValidatedIgnoreTag(data.gameObject), false, hashes);
                }
            }
            AddAncestors(data.skeleton, hashes);
            foreach (int hash in hashes)
                if (data.skeleton.HasBone(hash))
                {
                    data.skeleton.SetAnimatedBone(hash);
                }
        }

        private static void CollectKeepHashes(Transform bone, string tag, string ignoreTag, bool inside, HashSet<int> hashes)
        {
            if (!string.IsNullOrEmpty(ignoreTag) && bone.CompareTag(ignoreTag)) return;
            inside |= bone.CompareTag(tag);
            if (inside) hashes.Add(UMAUtils.StringToHash(bone.name));
            for (int i = 0; i < bone.childCount; i++) CollectKeepHashes(bone.GetChild(i), tag, ignoreTag, inside, hashes);
        }

        private static void AddAncestors(UMASkeleton skeleton, HashSet<int> hashes)
        {
            var pending = new List<int>(hashes);
            for (int i = 0; i < pending.Count; i++)
            {
                int parent = skeleton.GetParentBoneHash(pending[i]);
                if (parent != 0 && hashes.Add(parent)) pending.Add(parent);
            }
        }

        internal static int Cleanup(UMAData data)
        {
            var skeleton = data.skeleton;
            var required = new HashSet<int> { skeleton.rootBoneHash };
            AddBaseBones(data, required);
            data.AddRegisteredBoneDependencies(required);
            var recipe = data.umaRecipe;
            if (recipe != null)
            {
                if (recipe.raceData != null && recipe.raceData.KeepBoneNames != null)
                    foreach (string name in recipe.raceData.KeepBoneNames)
                        if (!string.IsNullOrEmpty(name)) required.Add(UMAUtils.StringToHash(name));
                if (recipe.slotDataList != null)
                    foreach (var slot in recipe.slotDataList)
                    {
                        if (slot == null || slot.isDisabled || slot.Suppressed || slot.asset == null) continue;
                        var mesh = slot.asset.meshData;
                        // Retain declared slot skeleton dependencies, including unweighted
                        // helpers used by DNA, physics, expressions and slot callbacks.
                        if (mesh != null)
                        {
                            required.Add(mesh.rootBoneHash);
                            if (mesh.boneNameHashes != null) required.UnionWith(mesh.boneNameHashes);
                            if (mesh.umaBones != null)
                                foreach (var bone in mesh.umaBones)
                                    if (bone != null) required.Add(bone.hash);
                        }
                        if (slot.asset.UnbakedAnimatedBones != null)
                            foreach (string name in slot.asset.UnbakedAnimatedBones)
                                if (!string.IsNullOrEmpty(name)) required.Add(UMAUtils.StringToHash(name));
                    }
            }
            // Final renderer bindings are authoritative for every backend, including
            // generated-resource cache hits and mounted renderers with no SlotData.
            foreach (var renderer in data.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // Retired output has its mesh cleared before deferred Destroy. It must
                // not keep the previous wardrobe's bones alive for another rebuild.
                if (renderer.sharedMesh == null && renderer.TryGetComponent<UMAGeneratedRenderer>(out _)) continue;
                foreach (var bone in renderer.bones)
                    AddTransformAncestors(bone, data.umaRoot.transform, required);
                AddTransformAncestors(renderer.rootBone, data.umaRoot.transform, required);
            }
            AddAncestors(skeleton, required);

            string ignoreTag = UMASettings.GetValidatedIgnoreTag(data.gameObject);
            var ignored = new List<Transform>();
            CollectIgnored(data.umaRoot.transform, ignoreTag, ignored);
            var protectedTransforms = new HashSet<Transform>();
            foreach (var root in ignored)
                foreach (var transform in root.GetComponentsInChildren<Transform>(true)) protectedTransforms.Add(transform);

            var remove = new List<int>();
            var doomed = new HashSet<Transform>();
            foreach (int hash in skeleton.BoneHashes)
            {
                if (required.Contains(hash)) continue;
                var bone = skeleton.GetBoneTransform(hash);
                if (bone != null && (protectedTransforms.Contains(bone) || bone == data.umaRoot.transform ||
                    !bone.IsChildOf(data.umaRoot.transform))) continue;
                remove.Add(hash);
                if (bone != null) doomed.Add(bone);
            }
            // Keep ignored subtrees intact even when their former mount no longer exists.
            foreach (var root in ignored)
            {
                var parent = root.parent;
                for (var ancestor = parent; ancestor != null; ancestor = ancestor.parent)
                    if (doomed.Contains(ancestor)) parent = ancestor.parent;
                if (parent != root.parent) root.SetParent(parent != null ? parent : data.umaRoot.transform, true);
            }
            var rootsToDestroy = new List<Transform>();
            foreach (var bone in doomed)
                if (bone.parent == null || !doomed.Contains(bone.parent)) rootsToDestroy.Add(bone);
            foreach (int hash in remove) skeleton.RemoveBone(hash);
            foreach (var bone in rootsToDestroy)
            {
                // Delete only topmost unused nodes. Their unused descendants follow;
                // retained dependencies already protect all connecting ancestors.
                bone.SetParent(null, true);
                bone.gameObject.SetActive(false);
                UMAUtils.DestroySceneObject(bone.gameObject);
            }
            return remove.Count;
        }

        private static void CollectIgnored(Transform root, string tag, List<Transform> result)
        {
            if (!string.IsNullOrEmpty(tag) && root.CompareTag(tag)) { result.Add(root); return; }
            for (int i = 0; i < root.childCount; i++) CollectIgnored(root.GetChild(i), tag, result);
        }

        private static void AddTransformAncestors(Transform bone, Transform boundary, HashSet<int> hashes)
        {
            while (bone != null)
            {
                hashes.Add(UMAUtils.StringToHash(bone.name));
                if (bone == boundary) break;
                bone = bone.parent;
            }
        }
    }
}
