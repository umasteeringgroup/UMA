#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    // An empty skeleton can get all of its bones by instantiating a kept copy of the same hierarchy. The result
    // must be indistinguishable from adding the bones one by one, and anything unusual must fall back to that.
    public class UMASkeletonTemplateTests
    {
        private const BindingFlags NonPublic = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static readonly Type templatesType = typeof(UMAData).Assembly.GetType("UMA.UMASkeletonTemplates");
        private static readonly MethodInfo tryEnsure = typeof(UMASkeleton).GetMethod("TryEnsureBonesFromTemplate", NonPublic);
        private readonly List<GameObject> created = new List<GameObject>();

        private static void ClearTemplates() => templatesType.GetMethod("Clear", NonPublic).Invoke(null, null);
        private static int TemplateCount => (int)templatesType.GetProperty("TemplateCount", NonPublic).GetValue(null);

        [SetUp]
        public void SetUp() => ClearTemplates();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in created) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            created.Clear();
            ClearTemplates();
        }

        // Bones with two branches off the root bone, a chain, and uneven transforms; sorted by hash like the
        // merged list the mesh combiner passes.
        private static UMATransform[] Bones(int count, int seed, bool includeRoot = false)
        {
            var random = new System.Random(seed);
            var names = new List<string>();
            var parents = new List<string>();
            for (int i = 0; i < count; i++)
            {
                names.Add("Bone_" + seed + "_" + i);
                // The first two hang off the root bone; the rest pick an earlier bone.
                parents.Add(i < 2 ? "Global" : names[random.Next(0, i)]);
            }
            var bones = new List<UMATransform>();
            for (int i = 0; i < count; i++)
            {
                bones.Add(new UMATransform
                {
                    name = names[i], hash = UMAUtils.StringToHash(names[i]), parent = UMAUtils.StringToHash(parents[i]),
                    position = new Vector3((float)random.NextDouble(), (float)random.NextDouble() - 0.5f, (float)random.NextDouble() * 3f),
                    rotation = Quaternion.Euler(random.Next(0, 360), random.Next(0, 360), random.Next(0, 360)),
                    scale = new Vector3(1f + (float)random.NextDouble(), 1f, 0.5f + (float)random.NextDouble())
                });
            }
            if (includeRoot)
            {
                bones.Add(new UMATransform { name = "Global", hash = UMAUtils.StringToHash("Global"), parent = 0, position = Vector3.one, rotation = Quaternion.identity, scale = Vector3.one });
            }
            return bones.OrderBy(bone => bone.hash).ToArray();
        }

        private UMASkeleton EmptySkeleton(out Transform global, bool withOtherChild = false)
        {
            var root = new GameObject("Skeleton test root");
            created.Add(root);
            global = new GameObject("Global").transform;
            global.SetParent(root.transform, false);
            global.localPosition = new Vector3(0.5f, 1f, -2f);
            global.localRotation = Quaternion.Euler(10, 20, 30);
            var skeleton = new UMASkeleton(global);
            if (withOtherChild)
            {
                // Something that is not a bone and was parented after the skeleton was made.
                new GameObject("Mounted item").transform.SetParent(global, false);
            }
            return skeleton;
        }

        private static void AddOneByOne(UMASkeleton skeleton, UMATransform[] bones)
        {
            skeleton.BeginSkeletonUpdate();
            foreach (var bone in bones) skeleton.EnsureBone(bone);
            skeleton.EnsureBoneHierarchy();
            skeleton.EndSkeletonUpdate();
        }

        // The mesh combiner's sequence, reporting whether the template made the bones.
        private static bool AddLikeTheCombiner(UMASkeleton skeleton, UMATransform[] bones)
        {
            skeleton.BeginSkeletonUpdate();
            bool fromTemplate = (bool)tryEnsure.Invoke(skeleton, new object[] { bones, bones.Length });
            if (!fromTemplate)
            {
                foreach (var bone in bones) skeleton.EnsureBone(bone);
            }
            skeleton.EnsureBoneHierarchy();
            skeleton.EndSkeletonUpdate();
            return fromTemplate;
        }

        private static string Path(Transform transform, Transform root)
        {
            return transform == root ? root.name : Path(transform.parent, root) + "/" + transform.name;
        }

        private static void AssertSame(UMASkeleton expected, Transform expectedGlobal, UMASkeleton actual, Transform actualGlobal)
        {
            Assert.That(actual.boneCount, Is.EqualTo(expected.boneCount));
            Assert.That(actual.boneHashData.Keys, Is.EqualTo(expected.boneHashData.Keys), "Bones are registered in the same order.");

            Transform[] expectedTransforms = expectedGlobal.GetComponentsInChildren<Transform>(true);
            Transform[] actualTransforms = actualGlobal.GetComponentsInChildren<Transform>(true);
            Assert.That(actualTransforms.Select(t => Path(t, actualGlobal)), Is.EqualTo(expectedTransforms.Select(t => Path(t, expectedGlobal))),
                "Same hierarchy, with siblings in the same order.");
            for (int i = 0; i < expectedTransforms.Length; i++)
            {
                Transform e = expectedTransforms[i], a = actualTransforms[i];
                string path = Path(e, expectedGlobal);
                Assert.That(a.localPosition, Is.EqualTo(e.localPosition), path);
                Assert.That(a.localRotation, Is.EqualTo(e.localRotation), path);
                Assert.That(a.localScale, Is.EqualTo(e.localScale), path);
                Assert.That(a.position, Is.EqualTo(e.position), path);
                Assert.That(a.gameObject.activeSelf, Is.EqualTo(e.gameObject.activeSelf), path);
                Assert.That(a.gameObject.activeInHierarchy, Is.EqualTo(e.gameObject.activeInHierarchy), path);
                Assert.That(a.gameObject.hideFlags, Is.EqualTo(e.gameObject.hideFlags), path);
                Assert.That(a.gameObject.layer, Is.EqualTo(e.gameObject.layer), path);
                Assert.That(a.gameObject.tag, Is.EqualTo(e.gameObject.tag), path);
                Assert.That(a.gameObject.scene, Is.EqualTo(e.gameObject.scene), path);
                Assert.That(a.GetComponents<Component>().Select(c => c.GetType()), Is.EqualTo(e.GetComponents<Component>().Select(c => c.GetType())), path);
            }

            foreach (var pair in expected.boneHashData)
            {
                UMASkeleton.BoneData e = pair.Value, a = actual.boneHashData[pair.Key];
                string name = e.umaTransform.name;
                Assert.That(a.boneNameHash, Is.EqualTo(e.boneNameHash), name);
                Assert.That(a.parentBoneNameHash, Is.EqualTo(e.parentBoneNameHash), name);
                Assert.That(a.boneTransform, Is.Not.Null, name);
                Assert.That(a.boneTransform.name, Is.EqualTo(e.boneTransform.name), name);
                Assert.That(Path(a.boneTransform, actualGlobal), Is.EqualTo(Path(e.boneTransform, expectedGlobal)), name);
                Assert.That(a.position, Is.EqualTo(e.position), name);
                Assert.That(a.rotation, Is.EqualTo(e.rotation), name);
                Assert.That(a.scale, Is.EqualTo(e.scale), name);
                Assert.That(a.umaTransform, Is.Not.SameAs(e.umaTransform), name);
                Assert.That(a.umaTransform.name, Is.EqualTo(e.umaTransform.name), name);
                Assert.That(a.umaTransform.hash, Is.EqualTo(e.umaTransform.hash), name);
                Assert.That(a.umaTransform.parent, Is.EqualTo(e.umaTransform.parent), name);
                Assert.That(a.umaTransform.position, Is.EqualTo(e.umaTransform.position), name);
                Assert.That(a.umaTransform.rotation, Is.EqualTo(e.umaTransform.rotation), name);
                Assert.That(a.umaTransform.scale, Is.EqualTo(e.umaTransform.scale), name);
                // The same answers a build asks the skeleton for afterwards.
                Assert.That(actual.GetBoneTransform(pair.Key), Is.SameAs(a.boneTransform), name);
                Assert.That(actual.GetParentBoneHash(pair.Key), Is.EqualTo(expected.GetParentBoneHash(pair.Key)), name);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void BonesFromATemplateAreTheSameAsBonesAddedOneByOne(bool rootInList, bool otherChildUnderRoot)
        {
            UMATransform[] bones = Bones(60, 7, rootInList);
            UMASkeleton expected = EmptySkeleton(out Transform expectedGlobal, otherChildUnderRoot);
            AddOneByOne(expected, bones);

            // First request: nothing kept yet. Second: the template is built and used. Third: used again.
            UMASkeleton first = EmptySkeleton(out Transform firstGlobal, otherChildUnderRoot);
            Assert.IsFalse(AddLikeTheCombiner(first, bones));
            Assert.That(TemplateCount, Is.Zero);
            AssertSame(expected, expectedGlobal, first, firstGlobal);

            UMASkeleton second = EmptySkeleton(out Transform secondGlobal, otherChildUnderRoot);
            Assert.IsTrue(AddLikeTheCombiner(second, bones));
            Assert.That(TemplateCount, Is.EqualTo(1));
            AssertSame(expected, expectedGlobal, second, secondGlobal);

            UMASkeleton third = EmptySkeleton(out Transform thirdGlobal, otherChildUnderRoot);
            Assert.IsTrue(AddLikeTheCombiner(third, bones));
            Assert.That(TemplateCount, Is.EqualTo(1));
            AssertSame(expected, expectedGlobal, third, thirdGlobal);

            // The copies are independent objects.
            int probe = bones.First(bone => bone.hash != UMAUtils.StringToHash("Global")).hash;
            Assert.That(third.GetBoneTransform(probe), Is.Not.SameAs(second.GetBoneTransform(probe)));
            second.GetBoneTransform(probe).localPosition += Vector3.one;
            UMASkeleton fourth = EmptySkeleton(out Transform fourthGlobal, otherChildUnderRoot);
            Assert.IsTrue(AddLikeTheCombiner(fourth, bones));
            AssertSame(expected, expectedGlobal, fourth, fourthGlobal);
        }

        [Test]
        public void ADifferentBoneSetNeverUsesAnotherSetsTemplate()
        {
            UMATransform[] bones = Bones(40, 3);
            Assert.IsFalse(AddLikeTheCombiner(EmptySkeleton(out _), bones));
            Assert.IsTrue(AddLikeTheCombiner(EmptySkeleton(out _), bones));

            // One bone posed slightly differently is a different skeleton.
            UMATransform[] moved = Bones(40, 3);
            moved[11].position += new Vector3(0, 1e-4f, 0);
            UMASkeleton expected = EmptySkeleton(out Transform expectedGlobal);
            AddOneByOne(expected, moved);
            UMASkeleton actual = EmptySkeleton(out Transform actualGlobal);
            Assert.IsFalse(AddLikeTheCombiner(actual, moved), "First request for the changed set.");
            AssertSame(expected, expectedGlobal, actual, actualGlobal);
            actual = EmptySkeleton(out actualGlobal);
            Assert.IsTrue(AddLikeTheCombiner(actual, moved));
            AssertSame(expected, expectedGlobal, actual, actualGlobal);
            Assert.That(TemplateCount, Is.EqualTo(2));

            // The original set still gets its own bones.
            expected = EmptySkeleton(out expectedGlobal);
            AddOneByOne(expected, bones);
            actual = EmptySkeleton(out actualGlobal);
            Assert.IsTrue(AddLikeTheCombiner(actual, bones));
            AssertSame(expected, expectedGlobal, actual, actualGlobal);
        }

        [TestCase("few bones")]
        [TestCase("skeleton not empty")]
        [TestCase("duplicate bone")]
        [TestCase("missing parent")]
        [TestCase("parent loop")]
        [TestCase("null entry")]
        public void UnusualInputIsLeftToTheOrdinaryPath(string situation)
        {
            UMATransform[] bones = Bones(situation == "few bones" ? 8 : 30, 5);
            switch (situation)
            {
                case "duplicate bone": bones[4] = bones[3].Duplicate(); break;
                case "missing parent": bones[9].parent = UMAUtils.StringToHash("No such bone"); break;
                case "parent loop": bones[6].parent = bones[7].hash; bones[7].parent = bones[6].hash; break;
                case "null entry": bones[2] = null; break;
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                UMASkeleton skeleton = EmptySkeleton(out Transform global);
                if (situation == "skeleton not empty")
                {
                    var existing = new GameObject("Already here").transform;
                    existing.SetParent(global, false);
                    skeleton.AddBone(UMAUtils.StringToHash("Global"), UMAUtils.StringToHash("Already here"), existing);
                }
                int childrenBefore = global.childCount;
                skeleton.BeginSkeletonUpdate();
                Assert.IsFalse((bool)tryEnsure.Invoke(skeleton, new object[] { bones, bones.Length }), "Attempt " + attempt);
                skeleton.EndSkeletonUpdate();
                Assert.That(global.childCount, Is.EqualTo(childrenBefore), "Nothing may be left behind.");
                Assert.That(skeleton.boneCount, Is.EqualTo(situation == "skeleton not empty" ? 2 : 1));
            }
            Assert.That(TemplateCount, Is.Zero);
        }

        [Test]
        public void ADestroyedTemplateIsBuiltAgainAndOldTemplatesAreDropped()
        {
            UMATransform[] bones = Bones(30, 11);
            UMASkeleton expected = EmptySkeleton(out Transform expectedGlobal);
            AddOneByOne(expected, bones);
            Assert.IsFalse(AddLikeTheCombiner(EmptySkeleton(out _), bones));
            Assert.IsTrue(AddLikeTheCombiner(EmptySkeleton(out _), bones));

            // Something destroys the hidden copy, as unloading everything would.
            foreach (var holder in Resources.FindObjectsOfTypeAll<GameObject>().Where(go => go.name == "UMA Skeleton Template").ToArray())
            {
                Assert.That(holder.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
                Assert.IsFalse(holder.activeSelf);
                UnityEngine.Object.DestroyImmediate(holder);
            }
            UMASkeleton actual = EmptySkeleton(out Transform actualGlobal);
            AddLikeTheCombiner(actual, bones);
            AssertSame(expected, expectedGlobal, actual, actualGlobal);
            actual = EmptySkeleton(out actualGlobal);
            AddLikeTheCombiner(actual, bones);
            actual = EmptySkeleton(out actualGlobal);
            Assert.IsTrue(AddLikeTheCombiner(actual, bones));
            AssertSame(expected, expectedGlobal, actual, actualGlobal);

            // Only a limited number of hierarchies is kept.
            for (int set = 0; set < 12; set++)
            {
                UMATransform[] other = Bones(20, 100 + set);
                AddLikeTheCombiner(EmptySkeleton(out _), other);
                AddLikeTheCombiner(EmptySkeleton(out _), other);
            }
            Assert.That(TemplateCount, Is.LessThanOrEqualTo(8));
            int holders = Resources.FindObjectsOfTypeAll<GameObject>().Count(go => go.name == "UMA Skeleton Template");
            Assert.That(holders, Is.EqualTo(TemplateCount), "Dropped templates are destroyed, not left behind.");

            ClearTemplates();
            Assert.That(Resources.FindObjectsOfTypeAll<GameObject>().Count(go => go.name == "UMA Skeleton Template"), Is.Zero);
        }
    }
}
#endif
