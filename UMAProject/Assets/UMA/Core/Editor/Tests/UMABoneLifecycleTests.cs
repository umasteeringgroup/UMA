using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UMABoneLifecycleTests
    {
        private GameObject avatar;
        private UMAData data;
        private SchedulerTestGenerator generator;
        private Transform root;
        private readonly List<Object> assets = new List<Object>();

        [SetUp] public void SetUp()
        {
            avatar = new GameObject("Bone lifecycle fixture");
            data = avatar.AddComponent<UMAData>();
            data.umaRecipe = new UMAData.UMARecipe();
            generator = avatar.AddComponent<SchedulerTestGenerator>();
            generator.enabled = false;
            generator.CleanupUnusedBones = true;
            data.umaRoot = Child(avatar.transform, "Root").gameObject;
            root = Child(data.umaRoot.transform, "Global");
        }

        [TearDown] public void TearDown()
        {
            if (avatar != null) Object.DestroyImmediate(avatar);
            foreach (var asset in assets) if (asset != null) Object.DestroyImmediate(asset);
            assets.Clear();
        }

        private T Asset<T>() where T : ScriptableObject
        { var a = ScriptableObject.CreateInstance<T>(); assets.Add(a); return a; }
        private static Transform Child(Transform parent, string name)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
        private static int Hash(string name) => UMAUtils.StringToHash(name);
        private void Skeleton(bool baking = false) => data.skeleton = baking ? new UMAImprovedSkeleton(root) : new UMASkeleton(root);
        private UmaTPose Pose(params string[] names)
        {
            var pose = Asset<UmaTPose>();
            pose.boneInfo = new SkeletonBone[names.Length];
            for (int i = 0; i < names.Length; i++) pose.boneInfo[i] = new SkeletonBone { name = names[i], rotation = Quaternion.identity, scale = Vector3.one };
            pose.humanInfo = Array.Empty<HumanBone>();
            return pose;
        }
        private SlotData Slot(Transform bone)
        {
            var asset = Asset<SlotDataAsset>();
            asset.meshData = new UMAMeshData { boneNameHashes = new[] { Hash(bone.name) } };
            return new SlotData(asset);
        }
        private SkinnedMeshRenderer Renderer(params Transform[] bones)
        {
            var r = Child(avatar.transform, "Renderer").gameObject.AddComponent<SkinnedMeshRenderer>();
            r.bones = bones; r.rootBone = root; return r;
        }

        [TestCase(false), TestCase(true)]
        public void CleanupKeepsBaseBonesSlotDependenciesAndAncestors(bool baking)
        {
            var baseBone = Child(root, "Base");
            var bridge = Child(root, "Bridge"); var used = Child(bridge, "Used");
            var unused = Child(root, "Unused"); var unusedChild = Child(unused, "UnusedChild");
            data.OverrideTpose = Pose("Base");
            data.umaRecipe.slotDataList = new[] { Slot(used) };
            Skeleton(baking);
            Assert.That(data.CleanupUnusedBones(generator), Is.EqualTo(2));
            Assert.That(baseBone && bridge && used, Is.True);
            Assert.That(unused == null && unusedChild == null, Is.True);
            Assert.That(data.skeleton.HasBone(Hash("Unused")), Is.False);
            Assert.That(data.CleanupUnusedBones(generator), Is.Zero);
        }

        [Test]
        public void DisabledCleanupAndExternalSkeletonAreUntouched()
        {
            var unused = Child(root, "Unused"); Skeleton();
            generator.CleanupUnusedBones = false;
            Assert.That(data.CleanupUnusedBones(generator), Is.Zero);
            generator.CleanupUnusedBones = true;
            data.SetExternalSkeletonRoot(root, null);
            Assert.That(data.CleanupUnusedBones(generator), Is.Zero);
            Assert.That(unused != null, Is.True); Assert.That(generator.BoneCleanupRuns, Is.Zero);
        }

        [Test]
        public void OverridePoseWinsAndInPlacePoseEditsAreObserved()
        {
            var a = Child(root, "A"); var b = Child(root, "B");
            data.umaRecipe.raceData = Asset<RaceData>();
            data.umaRecipe.raceData.TPose = Pose("A");
            data.OverrideTpose = Pose("A");
            data.OverrideTpose.boneInfo[0] = new SkeletonBone { name = "B", rotation = Quaternion.identity, scale = Vector3.one };
            Skeleton(); data.CleanupUnusedBones(generator);
            Assert.That(a == null, Is.True); Assert.That(b != null, Is.True);
        }

        [TestCase(false), TestCase(true)]
        public void IgnoredSubtreeSurvivesRemovalOfItsMount(bool baking)
        {
            var extra = Child(root, "UnusedMount");
            var ignored = Child(extra, "Attachment"); ignored.tag = "UMAIgnore";
            var child = Child(ignored, "AttachmentChild");
            Vector3 position = ignored.position = new Vector3(1, 2, 3);
            Skeleton(baking);
            Assert.That(data.skeleton.HasBone(Hash(child.name)), Is.False);
            Assert.That(data.CleanupUnusedBones(generator), Is.EqualTo(1));
            Assert.That(ignored.parent, Is.SameAs(root));
            Assert.That(child.parent, Is.SameAs(ignored)); Assert.That(ignored.position, Is.EqualTo(position));
        }

        [Test]
        public void KeepChainIsNotAnUnusedBoneExemption()
        {
            var extra = Child(root, "UnusedChain"); extra.tag = "UMAKeepChain";
            Child(extra, "Tail"); Skeleton();
            Assert.That(data.CleanupUnusedBones(generator), Is.EqualTo(2));
            Assert.That(extra == null, Is.True);
        }

        [TestCase(false), TestCase(true)]
        public void RestoreMergesNewChildrenRebindsRendererAndRegistersSavedChildren(bool baking)
        {
            var saved = Child(root, "Chain"); var savedTip = Child(saved, "Tip");
            var savedOnly = Child(saved, "SavedOnly");
            var component = saved.gameObject.AddComponent<BoxCollider>();
            data.AddSavedItem(saved, true); saved.SetParent(avatar.transform, false);
            var replacement = Child(root, "Chain"); var replacementTip = Child(replacement, "Tip");
            var newOnly = Child(replacementTip, "NewOnly");
            Skeleton(baking);
            var renderer = Renderer(replacement, replacementTip, newOnly); renderer.rootBone = replacement;
            data.RestoreSavedItems();
            Assert.That(replacement == null && replacementTip == null, Is.True);
            Assert.That(component != null, Is.True);
            Assert.That(data.skeleton.GetBoneTransform(Hash("Chain")), Is.SameAs(saved));
            Assert.That(data.skeleton.GetBoneTransform(Hash("Tip")), Is.SameAs(savedTip));
            Assert.That(data.skeleton.GetBoneTransform(Hash("SavedOnly")), Is.SameAs(savedOnly));
            Assert.That(newOnly.parent, Is.SameAs(savedTip));
            CollectionAssert.AreEqual(new[] { saved, savedTip, newOnly }, renderer.bones);
            Assert.That(renderer.rootBone, Is.SameAs(saved));
            Assert.DoesNotThrow(data.RestoreSavedItems);
        }

        [TestCase(false), TestCase(true)]
        public void RestoreMissingReplacementIsSafeAndCleanupRemovesUnusedChain(bool baking)
        {
            var saved = Child(root, "RemovedSlotChain");
            data.AddSavedItem(saved, true); saved.SetParent(avatar.transform, false);
            Skeleton(baking);
            Assert.DoesNotThrow(data.RestoreSavedItems);
            Assert.That(data.skeleton.GetBoneTransform(Hash(saved.name)), Is.SameAs(saved));
            Assert.That(data.CleanupUnusedBones(generator), Is.EqualTo(1));
            Assert.That(saved == null, Is.True);
        }

        [Test]
        public void RendererDependenciesProtectPhysicalAncestorsAndSharedMesh()
        {
            var parent = Child(root, "Mount"); Skeleton();
            var dynamicBone = Child(parent, "UnregisteredDynamicBone");
            var r = Renderer(dynamicBone);
            var mesh = new Mesh(); assets.Add(mesh); r.sharedMesh = mesh;
            Assert.That(data.CleanupUnusedBones(generator), Is.Zero);
            Assert.That(parent != null, Is.True); Assert.That(r.sharedMesh, Is.SameAs(mesh));
        }

        [TestCase(false), TestCase(true)]
        public void KeptChainUsesNewRestPoseNotThePreviousAnimatedPose(bool baking)
        {
            var saved = Child(root, "Chain"); saved.localPosition = Vector3.up * 5;
            data.AddSavedItem(saved, true); saved.SetParent(avatar.transform, false);
            var replacement = Child(root, "Chain"); replacement.localPosition = Vector3.up * 2;
            Skeleton(baking);
            // Separate the new source rest pose (2) from the generated DNA pose (3).
            replacement.localPosition = Vector3.up * 3;
            data.RestoreSavedItems();
            Assert.That(saved.localPosition, Is.EqualTo(Vector3.up * 3));
            data.skeleton.BeginSkeletonUpdate(); data.skeleton.ResetAll();
            if (baking)
            {
                data.skeleton.SetAnimatedBone(Hash("Chain"));
                data.skeleton.EnsureBoneHierarchy();
            }
            Assert.That(saved.localPosition, Is.EqualTo(Vector3.up * 2));
            data.skeleton.EndSkeletonUpdate();
        }

        [TestCase(false), TestCase(true)]
        public void DestructiveRebuildRestoresIgnoredAndKeptSubtrees(bool baking)
        {
            var kept = Child(root, "Chain"); kept.tag = "UMAKeepChain";
            var ignored = Child(kept, "IgnoredAttachment"); ignored.tag = "UMAIgnore";
            var attachment = Child(ignored, "AttachmentChild");
            generator.SaveAndRestoreIgnoredItems = false; // cleanup itself requires saving
            generator.SaveMountedItems(data);
            Object.DestroyImmediate(data.umaRoot);
            data.umaRoot = Child(avatar.transform, "Root").gameObject;
            root = Child(data.umaRoot.transform, "Global");
            var generated = Child(root, "Chain"); Skeleton(baking);
            var renderer = Renderer(generated);
            data.RestoreSavedItems(); data.CleanupUnusedBones(generator);
            Assert.That(kept != null && ignored != null && attachment != null, Is.True);
            Assert.That(renderer.bones[0], Is.SameAs(kept));
            Assert.That(data.skeleton.HasBone(Hash("IgnoredAttachment")), Is.False);
        }

        [Test]
        public void CompletedRigTransactionCleansOnceButTextureOnlyCompletionDoesNot()
        {
            var unused = Child(root, "Unused"); Skeleton();
            var complete = typeof(UMAGeneratorBuiltin).GetMethod("CompleteBoneCleanup", BindingFlags.Instance | BindingFlags.NonPublic);
            var pending = typeof(UMAData).GetField("BoneCleanupPending", BindingFlags.Instance | BindingFlags.NonPublic);
            complete.Invoke(generator, new object[] { data });
            Assert.That(unused != null, Is.True); Assert.That(generator.BoneCleanupRuns, Is.Zero);
            data.disableAnimation = true;
            // The fixture has no animation rig; only test the completion boundary here.
            pending.SetValue(data, true);
            complete.Invoke(generator, new object[] { data });
            Assert.That(unused == null, Is.True); Assert.That(generator.BoneCleanupRuns, Is.EqualTo(1));
            complete.Invoke(generator, new object[] { data });
            Assert.That(generator.BoneCleanupRuns, Is.EqualTo(1));
        }

        [TestCase(false), TestCase(true)]
        public void DestructiveRebuildRetainsBaseBonesNoLongerSuppliedBySlots(bool baking)
        {
            var bridge = Child(root, "Bridge"); var basis = Child(bridge, "Base");
            basis.localPosition = Vector3.up * 2;
            data.OverrideTpose = Pose("Base");
            data.OverrideTpose.boneInfo[0] = new SkeletonBone { name = "Base", position = Vector3.up * 2, rotation = Quaternion.identity, scale = Vector3.one };
            Skeleton(baking);
            basis.localPosition = Vector3.up * 8; // animated pose must not become rest data
            generator.SaveMountedItems(data);
            Object.DestroyImmediate(data.umaRoot);
            data.umaRoot = Child(avatar.transform, "Root").gameObject;
            root = Child(data.umaRoot.transform, "Global"); Skeleton(baking);
            data.RestoreSavedItems();
            Assert.That(data.skeleton.GetBoneTransform(Hash("Base")), Is.Not.Null);
            Assert.That(data.skeleton.GetBoneTransform(Hash("Base")).localPosition, Is.EqualTo(Vector3.up * 2));
            Assert.That(data.CleanupUnusedBones(generator), Is.Zero);
            Assert.That(data.skeleton.GetBoneTransform(Hash("Base")).parent.name, Is.EqualTo("Bridge"));
        }

        [Test]
        public void RemovedDisabledAndSuppressedSlotsDoNotAccumulateBones()
        {
            Skeleton();
            for (int i = 0; i < 20; i++)
            {
                var bone = Child(root, "Clothing" + i);
                data.skeleton.AddBone(Hash("Global"), Hash(bone.name), bone);
                var slot = Slot(bone); data.umaRecipe.slotDataList = new[] { slot };
                Assert.That(data.CleanupUnusedBones(generator), Is.Zero);
                if (i % 3 == 0) slot.isDisabled = true;
                else if (i % 3 == 1) slot.Suppressed = true;
                else data.umaRecipe.slotDataList = Array.Empty<SlotData>();
                Assert.That(data.CleanupUnusedBones(generator), Is.EqualTo(1));
                Assert.That(data.skeleton.boneCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void RegisteredAnimationDependenciesKeepTheirAncestors()
        {
            var parent = Child(root, "Parent"); var bone = Child(parent, "Animated"); Skeleton();
            data.RegisterAnimatedBoneHierarchy(Hash(bone.name));
            Assert.That(data.CleanupUnusedBones(generator), Is.Zero);
            data.ResetAnimatedBones();
            Assert.That(data.CleanupUnusedBones(generator), Is.EqualTo(2));
        }

        [Test]
        public void RetiredRendererAwaitingDeferredDestructionDoesNotRetainOldBones()
        {
            var oldBone = Child(root, "RetiredWardrobe"); Skeleton();
            var retired = Renderer(oldBone);
            retired.gameObject.AddComponent<UMAGeneratedRenderer>();
            // DestroyGeneratedRenderer clears sharedMesh before calling deferred Destroy.
            retired.sharedMesh = null;
            Assert.That(data.CleanupUnusedBones(generator), Is.EqualTo(1));
            Assert.That(oldBone == null, Is.True);
        }

        [Test]
        public void BakingPreservesBaseTransformsButDoesNotRegisterUnusedKeepChainForever()
        {
            var baseBone = Child(root, "Base"); var chain = Child(root, "Chain"); chain.tag = "UMAKeepChain";
            data.OverrideTpose = Pose("Base"); Skeleton(true);
            var method = typeof(UMAData).Assembly.GetType("UMA.UMABoneLifecycle").GetMethod("PreserveBaseBonesForBaking", BindingFlags.Static | BindingFlags.NonPublic);
            data.skeleton.BeginSkeletonUpdate(); data.skeleton.ResetAll();
            method.Invoke(null, new object[] { data, generator });
            data.skeleton.EndSkeletonUpdate();
            Assert.That(baseBone != null && chain != null, Is.True);
            Assert.That(data.CleanupUnusedBones(generator), Is.EqualTo(1));
            Assert.That(chain == null && baseBone != null, Is.True);
        }

        [Test]
        public void QualityConfigurationCarriesCleanupAndTimingWithoutEnablingByDefault()
        {
            var baseline = new GeneratorQualityConfiguration();
            Assert.That(baseline.CleanupUnusedBones, Is.False);
            generator.MeasureBoneCleanup = true;
            var captured = GeneratorQualityConfiguration.Capture(generator);
            Assert.That(captured.CleanupUnusedBones && captured.MeasureBoneCleanup, Is.True);
            Assert.That(captured.SameOutput(baseline), Is.False);
            baseline.ApplyTo(generator);
            Assert.That(generator.CleanupUnusedBones || generator.MeasureBoneCleanup, Is.False);
        }

        [Test]
        public void CleanupBenchmarkReportsDisabledRetainedAndPruningCosts()
        {
            var names = new string[300];
            for (int i = 0; i < names.Length; i++) { names[i] = "Base" + i; Child(root, names[i]); }
            data.OverrideTpose = Pose(names); Skeleton();
            data.CleanupUnusedBones(generator); // warm-up
            var clock = Stopwatch.StartNew();
            generator.CleanupUnusedBones = false;
            for (int i = 0; i < 1000; i++) data.CleanupUnusedBones(generator);
            double disabled = clock.Elapsed.TotalMilliseconds / 1000;
            generator.CleanupUnusedBones = true; generator.MeasureBoneCleanup = true;
            clock.Restart();
            for (int i = 0; i < 100; i++) data.CleanupUnusedBones(generator);
            double retained = clock.Elapsed.TotalMilliseconds / 100;
            for (int i = 0; i < 100; i++)
            {
                var bone = Child(root, "Unused" + i);
                data.skeleton.AddBone(Hash(root.name), Hash(bone.name), bone);
            }
            Assert.That(data.CleanupUnusedBones(generator), Is.EqualTo(100));
            TestContext.WriteLine($"Bone cleanup: disabled {disabled:F6} ms; 301 retained {retained:F4} ms; prune 100 {data.LastBoneCleanupMilliseconds:F4} ms (Editor DestroyImmediate).");
            Assert.That(data.skeleton.boneCount, Is.EqualTo(301));
            Assert.That(generator.BoneCleanupTotalMilliseconds, Is.GreaterThan(0));
        }
    }
}
