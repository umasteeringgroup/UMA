using System;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UMALegacyDnaRebuildTests
    {
        private static readonly string[] Wheels = { "FrontLeft", "FrontRight", "RearLeft", "RearRight" };

        [Test]
        public void ClearingAnUnusedFbxRoutePreservesTheStandardSkeleton()
        {
            var go = new GameObject("Unused FBX cleanup test");
            try
            {
                var data = go.AddComponent<UMAData>();
                var skeleton = new UMASkeleton(go.transform);
                data.skeleton = skeleton;
                data.ClearExternalSkeletonRoot();
                Assert.That(data.skeleton, Is.SameAs(skeleton));
                var route = go.AddComponent<UMAFbxRouteRuntime>();
                route.Teardown(data);
                route.Teardown(data);
                Assert.That(data.skeleton, Is.SameAs(skeleton));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClearingAnExternalSkeletonRespectsItsRendererOwner(bool matchingOwner)
        {
            var go = new GameObject("External skeleton owner test");
            var other = new GameObject("Other external owner");
            try
            {
                var data = go.AddComponent<UMAData>();
                var owner = go.AddComponent<SkinnedMeshRenderer>();
                var differentOwner = other.AddComponent<SkinnedMeshRenderer>();
                var skeleton = new UMASkeleton(go.transform);
                data.skeleton = skeleton;
                data.SetExternalSkeletonRoot(go.transform, owner);
                data.ClearExternalSkeletonRoot(matchingOwner ? owner : differentOwner);
                Assert.That(data.HasExternalSkeletonRoot, Is.EqualTo(!matchingOwner));
                if (matchingOwner) Assert.That(data.skeleton, Is.Null);
                else Assert.That(data.skeleton, Is.SameAs(skeleton));
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(other); }
        }

        [Test]
        public void UnusedFbxCleanupPreservesPreDnaScalePositionAndRotation()
        {
            var go = new GameObject("Heel baseline test");
            try
            {
                var position = new GameObject("Position").transform;
                position.SetParent(go.transform, false);
                position.localPosition = new Vector3(1, 2, 3);
                position.localRotation = Quaternion.Euler(10, 20, 30);
                Vector3 originalPosition = position.localPosition;
                Quaternion originalRotation = position.localRotation;
                var data = go.AddComponent<UMAData>();
                data.umaRoot = position.gameObject;
                data.skeleton = new UMASkeleton(position);
                var originalSkeleton = data.skeleton;
                var modifier = new BaseCharacterModifier(true, .965f, "Position",
                    UMAUtils.StringToHash("Position"), false, Vector3.zero, Vector2.zero, Vector3.zero);

                foreach (bool equipped in new[] { true, false, true, false, true })
                {
                    data.ClearExternalSkeletonRoot();
                    data.CheckSkeletonSetup();
                    Assert.That(data.skeleton, Is.SameAs(originalSkeleton));
                    data.skeleton.ResetAll();
                    if (equipped)
                    {
                        modifier.AdjustScale(data.skeleton);
                        position.localPosition += Vector3.up * .1f;
                        position.localRotation *= Quaternion.Euler(0, 15, 0);
                    }
                    Assert.That(position.localScale.x, Is.EqualTo(equipped ? .965f : 1f).Within(.0001f));
                    Assert.That(Vector3.Distance(position.localPosition, originalPosition + (equipped ? Vector3.up * .1f : Vector3.zero)), Is.LessThan(.0001f));
                    Assert.That(Quaternion.Angle(position.localRotation, originalRotation * (equipped ? Quaternion.Euler(0, 15, 0) : Quaternion.identity)), Is.LessThan(.01f));
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void DestroyedExternalReferencesStillInvalidateTheExternalSkeleton()
        {
            var go = new GameObject("Destroyed external skeleton test");
            var external = new GameObject("External root");
            try
            {
                var data = go.AddComponent<UMAData>();
                var renderer = external.AddComponent<SkinnedMeshRenderer>();
                data.SetExternalSkeletonRoot(external.transform, renderer);
                data.skeleton = new UMASkeleton(external.transform);
                Object.DestroyImmediate(external);
                data.ClearExternalSkeletonRoot();
                Assert.That(data.skeleton, Is.Null, "Unity's destroyed-object null must not skip real external cleanup.");
                Assert.That(data.HasExternalSkeletonRoot, Is.False);
            }
            finally
            {
                if (external != null) Object.DestroyImmediate(external);
                Object.DestroyImmediate(go);
            }
        }

        [TestCase("Default")]
        [TestCase("MeshAPI")]
        [TestCase("Jobified")]
        [TestCase("Incremental")]
        [TestCase("BoneBaking")]
        public void SpoilerChangesPreserveWheelSizeAndSkeletonBaseline(string backend)
        {
            CheckCarRebuilds(backend);
        }

        private static void CheckCarRebuilds(string backend)
        {
            const string folder = "Assets/UMA/SRP/Samples/Scenes/U3-Car Scene/SkyCar/";
            var race = AssetDatabase.LoadAssetAtPath<RaceData>(folder + "RaceData/SkyCar.asset");
            var spoiler = AssetDatabase.LoadAssetAtPath<UMATextRecipe>(folder + "Recipes/SpoilerRecipe.asset");
            if (race == null || spoiler == null) Assert.Ignore("The SkyCar sample content is not installed.");
            Assert.That(race.useNewDNA, Is.False);
            Assert.That(race.TPose, Is.Null, "Exercise the sample's authored slot baseline without a T-pose asset.");

            var generator = UMAAssetIndexer.Instance.Generator;
            var settings = UMASettings.GetSettings();
            var previousCombiner = generator.meshCombiner;
            bool previousMeshAPI = settings.useMeshAPICombiner;
            var combinerObject = new GameObject("Legacy DNA combiner test");
            var avatarObject = new GameObject("Legacy DNA car test");
            avatarObject.SetActive(false);
            try
            {
                Type combinerType = backend == "Jobified" ? typeof(UMAJobifiedMeshCombiner) :
                    backend == "Incremental" ? typeof(UMAIncrementalMeshCombiner) :
                    backend == "BoneBaking" ? typeof(UMABoneBakingMeshCombiner) : typeof(UMADefaultMeshCombiner);
                generator.meshCombiner = (UMAMeshCombiner)combinerObject.AddComponent(combinerType);
                settings.useMeshAPICombiner = backend == "MeshAPI";
                var avatar = avatarObject.AddComponent<RebuildTestAvatar>();
                avatar.inheritGeneratorQuality = false;
                avatar.umaRecipe = new UMAData.UMARecipe();
                avatar.activeRace.name = race.raceName;
                avatar.activeRace.data = race;
                avatar.defaultRendererAsset = generator.defaultRendererAsset;
                avatar.predefinedDNA = new UMAPredefinedDNA();
                avatar.predefinedDNA.AddDNA("WheelSize", 0.6f);

                // Displayed offset +0.1 corresponds to legacy DNA value 0.6.
                BuildAndCheck(avatar, generator, spoiler, false, 1.1f);
                var skeleton = avatar.skeleton;
                for (int i = 0; i < 6; i++)
                {
                    BuildAndCheck(avatar, generator, spoiler, i % 2 == 0, 1.1f);
                    Assert.That(avatar.skeleton, Is.SameAs(skeleton), backend + " replaced the authored baseline.");
                }

                avatar.GetDNA()["WheelSize"].Set(0.4f);
                BuildAndCheck(avatar, generator, spoiler, true, 0.9f);
                BuildAndCheck(avatar, generator, spoiler, false, 0.9f);
                avatar.GetDNA()["WheelSize"].Set(0.5f);
                BuildAndCheck(avatar, generator, spoiler, true, 1f);
                BuildAndCheck(avatar, generator, spoiler, false, 1f);
            }
            finally
            {
                // This fixture stays inactive, so Unity does not call OnDestroy.
                // Release generated native resources explicitly before destroying it.
                var data = avatarObject.GetComponent<UMAData>();
                if (data != null)
                {
                    data.CleanTextures();
                    data.CleanMesh(true);
                    data.CleanAvatar();
                }
                Object.DestroyImmediate(avatarObject);
                generator.meshCombiner = previousCombiner;
                settings.useMeshAPICombiner = previousMeshAPI;
                Object.DestroyImmediate(combinerObject);
            }
        }

        private static void BuildAndCheck(RebuildTestAvatar avatar, UMAGenerator generator, UMATextRecipe spoiler, bool enabled, float expectedScale)
        {
            if (enabled) avatar.SetSlot(spoiler);
            else avatar.ClearSlot(spoiler.wardrobeSlot);
            avatar.BuildCharacter(true, true);
            Assert.That(generator.GenerateSingleUMA(avatar, false), Is.True);
            Assert.That(avatar.GetDNA()["WheelSize"].Value, Is.EqualTo(expectedScale - 0.5f).Within(0.0001f));
            foreach (string wheel in Wheels)
            {
                var scale = avatar.skeleton.GetScale(UMAUtils.StringToHash(wheel));
                Assert.That(scale.x, Is.EqualTo(expectedScale).Within(0.0001f), wheel);
                Assert.That(scale.y, Is.EqualTo(expectedScale).Within(0.0001f), wheel);
                Assert.That(scale.z, Is.EqualTo(expectedScale).Within(0.0001f), wheel);
            }
        }

        // Keep production recipe assembly and generation, while avoiding a second queued build.
        public sealed class RebuildTestAvatar : DynamicCharacterAvatar
        {
            public override void Dirty() { }
        }
    }
}
