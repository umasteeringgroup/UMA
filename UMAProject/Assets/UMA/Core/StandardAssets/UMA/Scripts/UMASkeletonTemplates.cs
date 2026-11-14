using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Speeds up filling an empty skeleton. Creating a few hundred bone GameObjects one at a time is the slowest
    /// part of a character's first build (and of every build with Always Rebuild Skeleton). The second time the
    /// same set of bones is asked for, a hidden, inactive copy of that hierarchy is kept; from then on the bones
    /// are made by instantiating the copy, which gives the same names, components, parents, sibling order and
    /// local transforms in a fraction of the time.
    ///
    /// Only <see cref="UMASkeleton.TryEnsureBonesFromTemplate"/> calls this, and only for a skeleton that holds
    /// nothing but its root bone. Anything unusual (a missing parent, a duplicate bone, a copy that does not match)
    /// makes it step aside so the ordinary one-by-one path runs.
    /// </summary>
    internal static class UMASkeletonTemplates
    {
        internal struct Bone
        {
            public int hash;
            public int parent;
            public string name;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        private sealed class Template
        {
            public ulong signature;
            public int rootHash;
            public Bone[] bones;
            // Inactive and hidden; its children are the bones that hang directly off the skeleton's root bone.
            public GameObject holder;
            public Transform[] roots;
            // For each root: which bone every transform of GetComponentsInChildren is, and where in that same
            // list its parent sits (-1 for the root itself).
            public int[][] order;
            public int[][] parentInOrder;
            public long lastUsed;
        }

        // Below this a template is not worth keeping.
        internal const int MinimumBones = 16;
        private const int MaximumTemplates = 8;
        private const int MaximumRemembered = 512;

        private static readonly List<Template> templates = new List<Template>();
        // Bone sets asked for once. A template is only built when one comes back.
        private static readonly HashSet<ulong> seenOnce = new HashSet<ulong>();
        // Bone sets that failed validation; they always take the ordinary path.
        private static readonly HashSet<ulong> rejected = new HashSet<ulong>();
        private static readonly List<Transform> walk = new List<Transform>(512);
        private static long useCounter;

        internal static int TemplateCount => templates.Count;

        /// <summary>Destroys every kept hierarchy and forgets what has been seen.</summary>
        internal static void Clear()
        {
            for (int i = 0; i < templates.Count; i++)
            {
                if (templates[i].holder != null) UMAUtils.DestroySceneObject(templates[i].holder);
            }
            templates.Clear();
            seenOnce.Clear();
            rejected.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            Clear();
        }

#if UNITY_EDITOR
        // The kept hierarchies are hidden objects that outlive scene loads. They must not be left behind when the
        // static list is lost in a script reload, or carried across an edit/play transition.
        [UnityEditor.InitializeOnLoadMethod]
        private static void WatchEditor()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Clear;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Clear;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.ExitingEditMode || change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                Clear();
            }
        }
#endif

        /// <summary>
        /// Creates the given bones under <paramref name="root"/> from a kept hierarchy.
        /// </summary>
        /// <param name="bones">The bones to create, none of which exists yet. Every parent is either
        /// <paramref name="rootHash"/> or another bone in the list.</param>
        /// <param name="created">Receives the new transform of each bone, in the same order.</param>
        /// <returns>False when nothing was created and the caller must create the bones itself.</returns>
        internal static bool TryCreate(Bone[] bones, int count, int rootHash, Transform root, Transform[] created)
        {
            if (count < MinimumBones || root == null)
            {
                return false;
            }

            ulong signature = Signature(bones, count, rootHash);
            if (rejected.Contains(signature))
            {
                return false;
            }

            Template template = Find(bones, count, rootHash, signature);
            if (template == null)
            {
                if (seenOnce.Count > MaximumRemembered) seenOnce.Clear();
                if (seenOnce.Add(signature))
                {
                    // First request for this set: not worth a template yet.
                    return false;
                }

                template = Build(bones, count, rootHash, signature);
                if (template == null)
                {
                    Reject(signature);
                    return false;
                }
                seenOnce.Remove(signature);
                if (templates.Count >= MaximumTemplates) RemoveLeastRecentlyUsed();
                templates.Add(template);
            }

            template.lastUsed = ++useCounter;
            if (!Instantiate(template, root, created))
            {
                // The copy did not come out as recorded. Never guess: drop the template for good.
                Remove(template);
                Reject(signature);
                return false;
            }
            return true;
        }

        private static void Reject(ulong signature)
        {
            if (rejected.Count > MaximumRemembered) rejected.Clear();
            rejected.Add(signature);
        }

        private static Template Find(Bone[] bones, int count, int rootHash, ulong signature)
        {
            for (int i = templates.Count - 1; i >= 0; i--)
            {
                Template template = templates[i];
                if (template.holder == null)
                {
                    // Destroyed from outside, for example with its scene.
                    templates.RemoveAt(i);
                    continue;
                }
                if (template.signature != signature || template.rootHash != rootHash || template.bones.Length != count) continue;
                bool same = true;
                for (int b = 0; b < count && same; b++)
                {
                    same = Same(template.bones[b], bones[b]);
                }
                if (same) return template;
            }
            return null;
        }

        // Bit for bit: a template is only used for exactly the transforms it was built from.
        private static bool Same(in Bone a, in Bone b)
        {
            return a.hash == b.hash && a.parent == b.parent && a.name == b.name &&
                a.position.x.Equals(b.position.x) && a.position.y.Equals(b.position.y) && a.position.z.Equals(b.position.z) &&
                a.rotation.x.Equals(b.rotation.x) && a.rotation.y.Equals(b.rotation.y) && a.rotation.z.Equals(b.rotation.z) && a.rotation.w.Equals(b.rotation.w) &&
                a.scale.x.Equals(b.scale.x) && a.scale.y.Equals(b.scale.y) && a.scale.z.Equals(b.scale.z);
        }

        private static ulong Signature(Bone[] bones, int count, int rootHash)
        {
            ulong hash = Mix(0x554D41536B656CUL, ((ulong)(uint)rootHash << 32) | (uint)count);
            for (int i = 0; i < count; i++)
            {
                hash = Mix(hash, ((ulong)(uint)bones[i].hash << 32) | (uint)bones[i].parent);
                hash = Mix(hash, ((ulong)Bits(bones[i].position.x) << 32) | Bits(bones[i].position.y));
                hash = Mix(hash, ((ulong)Bits(bones[i].position.z) << 32) | Bits(bones[i].rotation.x));
                hash = Mix(hash, ((ulong)Bits(bones[i].rotation.y) << 32) | Bits(bones[i].rotation.z));
                hash = Mix(hash, ((ulong)Bits(bones[i].rotation.w) << 32) | Bits(bones[i].scale.x));
                hash = Mix(hash, ((ulong)Bits(bones[i].scale.y) << 32) | Bits(bones[i].scale.z));
            }
            return hash;
        }

        private static uint Bits(float value)
        {
            return unchecked((uint)System.BitConverter.SingleToInt32Bits(value));
        }

        private static ulong Mix(ulong hash, ulong value)
        {
            hash ^= value + 0x9E3779B97F4A7C15UL + (hash << 6) + (hash >> 2);
            return hash * 0xFF51AFD7ED558CCDUL;
        }

        private static void Remove(Template template)
        {
            templates.Remove(template);
            if (template.holder != null) UMAUtils.DestroySceneObject(template.holder);
        }

        private static void RemoveLeastRecentlyUsed()
        {
            int oldest = 0;
            for (int i = 1; i < templates.Count; i++)
            {
                if (templates[i].lastUsed < templates[oldest].lastUsed) oldest = i;
            }
            Remove(templates[oldest]);
        }

        // Builds the hidden hierarchy the way the ordinary path builds the real one: every bone is created in list
        // order, then parented and posed in list order, so siblings end up in the same order.
        private static Template Build(Bone[] bones, int count, int rootHash, ulong signature)
        {
            var index = new Dictionary<int, int>(count);
            for (int i = 0; i < count; i++)
            {
                if (bones[i].hash == rootHash || string.IsNullOrEmpty(bones[i].name) || index.ContainsKey(bones[i].hash)) return null;
                index.Add(bones[i].hash, i);
            }

            // Every bone must hang off the root bone through bones of this list: no missing parents, no loops.
            var depth = new int[count];
            for (int i = 0; i < count; i++)
            {
                int current = i;
                int steps = 0;
                while (bones[current].parent != rootHash)
                {
                    if (!index.TryGetValue(bones[current].parent, out current) || ++steps > count) return null;
                }
                depth[i] = steps;
            }

            var holder = new GameObject("UMA Skeleton Template") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                holder.SetActive(false);
                var transforms = new Transform[count];
                var boneOfTransform = new Dictionary<Transform, int>(count);
                for (int i = 0; i < count; i++)
                {
                    var go = new GameObject(bones[i].name);
                    go.AddComponent<UMAGeneratedBone>();
                    transforms[i] = go.transform;
                    boneOfTransform.Add(transforms[i], i);
                }

                var roots = new List<Transform>(2);
                for (int i = 0; i < count; i++)
                {
                    bool topLevel = bones[i].parent == rootHash;
                    transforms[i].SetParent(topLevel ? holder.transform : transforms[index[bones[i].parent]], false);
                    transforms[i].localPosition = bones[i].position;
                    transforms[i].localRotation = bones[i].rotation;
                    transforms[i].localScale = bones[i].scale;
                    if (topLevel) roots.Add(transforms[i]);
                }

                var template = new Template
                {
                    signature = signature,
                    rootHash = rootHash,
                    bones = new Bone[count],
                    holder = holder,
                    roots = roots.ToArray(),
                    order = new int[roots.Count][],
                    parentInOrder = new int[roots.Count][]
                };
                System.Array.Copy(bones, template.bones, count);

                int total = 0;
                var position = new Dictionary<Transform, int>(count);
                for (int r = 0; r < roots.Count; r++)
                {
                    walk.Clear();
                    roots[r].GetComponentsInChildren(true, walk);
                    var order = new int[walk.Count];
                    var parentInOrder = new int[walk.Count];
                    position.Clear();
                    for (int k = 0; k < walk.Count; k++)
                    {
                        if (!boneOfTransform.TryGetValue(walk[k], out order[k])) { UMAUtils.DestroySceneObject(holder); return null; }
                        position.Add(walk[k], k);
                        parentInOrder[k] = -1;
                        if (k > 0 && !position.TryGetValue(walk[k].parent, out parentInOrder[k])) { UMAUtils.DestroySceneObject(holder); return null; }
                    }
                    template.order[r] = order;
                    template.parentInOrder[r] = parentInOrder;
                    total += walk.Count;
                }
                walk.Clear();
                if (total != count) { UMAUtils.DestroySceneObject(holder); return null; }
                return template;
            }
            catch
            {
                UMAUtils.DestroySceneObject(holder);
                throw;
            }
        }

        private static bool Instantiate(Template template, Transform root, Transform[] created)
        {
            int rootCount = template.roots.Length;
            var clones = new Transform[rootCount];
            bool ok = true;
            for (int r = 0; r < rootCount && ok; r++)
            {
                int[] order = template.order[r];
                int[] parentInOrder = template.parentInOrder[r];
                // Local position, rotation and scale are kept; the new bones are appended after the root's children.
                Transform clone = Object.Instantiate(template.roots[r], root, false);
                clones[r] = clone;
                // Instantiate adds "(Clone)" to the name of the object it was given.
                clone.name = template.bones[order[0]].name;

                walk.Clear();
                clone.GetComponentsInChildren(true, walk);
                ok = walk.Count == order.Length && walk[0] == clone;
                for (int k = 1; k < walk.Count && ok; k++)
                {
                    ok = walk[k].parent == walk[parentInOrder[k]];
                }
                if (ok)
                {
                    for (int k = 0; k < walk.Count; k++) created[order[k]] = walk[k];
                }
            }
            walk.Clear();

            if (!ok)
            {
                // Gone at once, so the ordinary path that follows does not create its bones next to these.
                for (int r = 0; r < rootCount; r++)
                {
                    if (clones[r] != null) Object.DestroyImmediate(clones[r].gameObject, false);
                }
                return false;
            }
            return true;
        }
    }
}
