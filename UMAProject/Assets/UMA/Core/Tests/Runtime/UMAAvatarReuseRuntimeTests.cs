#if UNITY_INCLUDE_TESTS && UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    // Play Mode: a rig update that keeps its humanoid Avatar must leave a playing character exactly as a rig update
    // that builds a new Avatar does. Two identical Human Male 3.0 characters play the same controller; one takes
    // the kept-Avatar path, the other is made to rebuild, and their bones and Animator states are compared.
    public sealed class UMAAvatarReuseRuntimeTests
    {
        private const string RacePath = "Assets/UMA/UMA3/Races/HumanMale30.asset";
        private const string ControllerPath = "Assets/UMA/UMA3/Animation/Locomotion.controller";
        private const BindingFlags NonPublic = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static readonly FieldInfo reuseCount = typeof(UMAData).Assembly.GetType("UMA.UMAAvatarReuse").GetField("ReuseCount", NonPublic);
        private static readonly FieldInfo recordField = typeof(UMAData).GetField("avatarReuse", NonPublic);
        private readonly List<Object> created = new List<Object>();

        private static long Reused => (long)reuseCount.GetValue(null);

        private UMAData Build(RaceData race, UMAGenerator generator, RuntimeAnimatorController controller, string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            var data = go.AddComponent<UMAData>();
            var rendererAsset = ScriptableObject.CreateInstance<UMARendererAsset>();
            created.Add(rendererAsset);
            data.defaultRendererAsset = rendererAsset;
            data.animationController = controller;
            data.umaRecipe = new UMAData.UMARecipe();
            data.umaRecipe.SetRace(race);
            race.baseRaceRecipe.Load(data.umaRecipe);
            if (race.useNewDNA)
            {
                data.umaRecipe.dnaInstanceCollection = race.DNACollection.GetDefaultDNA(race);
            }
            data.blendShapeSettings.ignoreBlendShapes = true;
            data.isMeshDirty = data.isTextureDirty = data.isShapeDirty = true;
            Assert.IsTrue(generator.GenerateSingleUMA(data, false));
            Assert.That(data.animator, Is.Not.Null);
            Assert.IsTrue(data.animator.avatar != null && data.animator.avatar.isHuman, "This test needs a humanoid Avatar.");
            Assert.That(data.animator.runtimeAnimatorController, Is.SameAs(controller));
            // Both characters stay where they are, so only the animation moves their bones.
            data.animator.applyRootMotion = false;
            return data;
        }

        private static float LargestBoneDifference(UMAData a, UMAData b, out string where)
        {
            float largest = 0f;
            where = "";
            foreach (var pair in a.skeleton.boneHashData)
            {
                Transform first = pair.Value.boneTransform;
                Transform second = b.skeleton.GetBoneTransform(pair.Key);
                Assert.That(second, Is.Not.Null, pair.Value.umaTransform.name);
                float difference = Mathf.Max(
                    Vector3.Distance(first.localPosition, second.localPosition),
                    Quaternion.Angle(first.localRotation, second.localRotation) * Mathf.Deg2Rad,
                    Vector3.Distance(first.localScale, second.localScale));
                if (difference > largest)
                {
                    largest = difference;
                    where = pair.Value.umaTransform.name;
                }
            }
            return largest;
        }

        private static Dictionary<int, Quaternion> Rotations(UMAData data)
        {
            var rotations = new Dictionary<int, Quaternion>();
            foreach (var pair in data.skeleton.boneHashData) rotations[pair.Key] = pair.Value.boneTransform.localRotation;
            return rotations;
        }

        private static float LargestAngleFrom(UMAData data, Dictionary<int, Quaternion> reference)
        {
            float largest = 0f;
            foreach (var pair in data.skeleton.boneHashData)
                largest = Mathf.Max(largest, Quaternion.Angle(pair.Value.boneTransform.localRotation, reference[pair.Key]));
            return largest;
        }

        [UnityTest]
        public IEnumerator KeptAvatarLeavesAPlayingCharacterExactlyAsARebuiltAvatarDoes()
        {
            var race = AssetDatabase.LoadAssetAtPath<RaceData>(RacePath);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (race == null || controller == null || race.umaTarget != RaceData.UMATarget.Humanoid || race.TPose == null || race.baseRaceRecipe == null)
            {
                Assert.Ignore("The Human Male 3.0 race or the Locomotion controller is not installed.");
            }
            var generator = UMAAssetIndexer.Instance.Generator;
            if (generator == null || generator.meshCombiner == null)
            {
                Assert.Ignore("No UMA generator with a mesh combiner is available.");
            }

            UMAData kept = null, rebuilt = null;
            try
            {
                kept = Build(race, generator, controller, "Avatar reuse runtime test (kept)");
                rebuilt = Build(race, generator, controller, "Avatar reuse runtime test (rebuilt)");
                rebuilt.transform.position = new Vector3(3f, 0f, 0f);
                Dictionary<int, Quaternion> builtPose = Rotations(kept);

                for (int frame = 0; frame < 8; frame++) yield return null;

                // Both are animating, and identically.
                Assert.That(kept.animator.isInitialized && rebuilt.animator.isInitialized, Is.True);
                Assert.That(LargestAngleFrom(kept, builtPose), Is.GreaterThan(1f), "Sanity: the controller is moving the bones.");
                Assert.That(LargestBoneDifference(kept, rebuilt, out string bone), Is.LessThan(1e-4f), "Before the rig update: " + bone);

                for (int round = 0; round < 3; round++)
                {
                    Avatar keptAvatar = kept.animator.avatar;
                    Avatar rebuiltAvatar = rebuilt.animator.avatar;
                    long before = Reused;

                    // One character keeps its Avatar; the other has nothing remembered and builds a new one.
                    kept.isShapeDirty = true;
                    Assert.IsTrue(generator.GenerateSingleUMA(kept, false));
                    recordField.SetValue(rebuilt, null);
                    rebuilt.isShapeDirty = true;
                    Assert.IsTrue(generator.GenerateSingleUMA(rebuilt, false));

                    Assert.That(Reused, Is.EqualTo(before + 1), "Exactly one of the two keeps its Avatar.");
                    Assert.That(kept.animator.avatar, Is.SameAs(keptAvatar));
                    Assert.That(rebuilt.animator.avatar, Is.Not.SameAs(rebuiltAvatar));

                    // Right after the update, and while the animation carries on.
                    for (int frame = 0; frame < 6; frame++)
                    {
                        AnimatorStateInfo keptState = kept.animator.GetCurrentAnimatorStateInfo(0);
                        AnimatorStateInfo rebuiltState = rebuilt.animator.GetCurrentAnimatorStateInfo(0);
                        Assert.That(keptState.fullPathHash, Is.EqualTo(rebuiltState.fullPathHash), "Round " + round + ", frame " + frame);
                        Assert.That(keptState.normalizedTime, Is.EqualTo(rebuiltState.normalizedTime).Within(1e-4f), "Round " + round + ", frame " + frame);
                        Assert.That(LargestBoneDifference(kept, rebuilt, out bone), Is.LessThan(1e-4f),
                            "Round " + round + ", frame " + frame + ": " + bone);
                        Assert.That(LargestAngleFrom(kept, builtPose), Is.GreaterThan(1f), "The animation still drives the kept character.");
                        yield return null;
                    }
                }
            }
            finally
            {
                foreach (UMAData data in new[] { kept, rebuilt })
                {
                    if (data == null) continue;
                    data.CleanTextures();
                    data.CleanMesh(true);
                    data.CleanAvatar();
                }
                foreach (var item in created) if (item != null) Object.Destroy(item);
                created.Clear();
            }
            yield return null;
        }
    }
}
#endif
