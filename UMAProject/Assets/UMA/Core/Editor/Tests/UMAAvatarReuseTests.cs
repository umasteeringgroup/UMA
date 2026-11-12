#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Tests
{
    // A rig update keeps its humanoid Avatar when building it again would give the same one. These tests build the
    // Human Male 3.0 base character and check that keeping the Avatar leaves every bone exactly where a rebuild
    // leaves it, and that any change to what the Avatar is built from builds a new one.
    public class UMAAvatarReuseTests
    {
        private const string RacePath = "Assets/UMA/UMA3/Races/HumanMale30.asset";
        private const BindingFlags NonPublic = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static readonly FieldInfo reuseCount = typeof(UMAData).Assembly.GetType("UMA.UMAAvatarReuse").GetField("ReuseCount", NonPublic);
        private static readonly FieldInfo recordField = typeof(UMAData).GetField("avatarReuse", NonPublic);
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private RaceData race;
        private UMAGenerator generator;
        private UMAData data;

        private static long Reused => (long)reuseCount.GetValue(null);

        [SetUp]
        public void SetUp()
        {
            race = AssetDatabase.LoadAssetAtPath<RaceData>(RacePath);
            if (race == null || race.umaTarget != RaceData.UMATarget.Humanoid || race.TPose == null || race.baseRaceRecipe == null)
            {
                Assert.Ignore("The Human Male 3.0 race is not installed as a humanoid race with a T-pose.");
            }
            generator = UMAAssetIndexer.Instance.Generator;
            if (generator == null || generator.meshCombiner == null)
            {
                Assert.Ignore("No UMA generator with a mesh combiner is available.");
            }

            var go = new GameObject("Avatar reuse test");
            created.Add(go);
            data = go.AddComponent<UMAData>();
            var rendererAsset = ScriptableObject.CreateInstance<UMARendererAsset>();
            created.Add(rendererAsset);
            data.defaultRendererAsset = rendererAsset;
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
            Assert.That(data.animator.avatar, Is.Not.Null);
            Assert.IsTrue(data.animator.avatar.isHuman, "This test needs a humanoid Avatar.");
        }

        [TearDown]
        public void TearDown()
        {
            if (data != null)
            {
                data.CleanTextures();
                data.CleanMesh(true);
                data.CleanAvatar();
            }
            foreach (var item in created) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            created.Clear();
            data = null;
        }

        private void RigUpdate()
        {
            data.isShapeDirty = true;
            Assert.IsTrue(generator.GenerateSingleUMA(data, false));
        }

        private void ForgetAvatarRecord() => recordField.SetValue(data, null);

        // Every transform below the avatar with its exact local values.
        private string[] Pose()
        {
            return data.GetComponentsInChildren<Transform>(true).Select(t =>
            {
                Vector3 p = t.localPosition, s = t.localScale; Quaternion q = t.localRotation;
                return t.name + "|" + (t.parent != null ? t.parent.name : "") + "|" +
                    p.x.ToString("R") + "," + p.y.ToString("R") + "," + p.z.ToString("R") + "|" +
                    q.x.ToString("R") + "," + q.y.ToString("R") + "," + q.z.ToString("R") + "," + q.w.ToString("R") + "|" +
                    s.x.ToString("R") + "," + s.y.ToString("R") + "," + s.z.ToString("R");
            }).ToArray();
        }

        private void ShiftDna(float amount)
        {
            if (race.useNewDNA)
            {
                var dna = data.umaRecipe.dnaInstanceCollection;
                float[] values = dna.GetValues();
                for (int i = 0; i < values.Length; i++) values[i] = Mathf.Clamp01(values[i] + amount);
                dna.SetValues(values);
            }
            else
            {
                foreach (UMADnaBase dna in data.umaRecipe.GetAllDna())
                {
                    float[] values = dna.Values;
                    for (int i = 0; i < values.Length; i++) values[i] = Mathf.Clamp01(values[i] + amount);
                    dna.Values = values;
                }
            }
        }

        [Test]
        public void UnchangedRigUpdateKeepsTheAvatarAndPosesTheBonesExactlyAsARebuildDoes()
        {
            RigUpdate();
            Avatar avatar = data.animator.avatar;
            long before = Reused;

            RigUpdate();
            Assert.That(data.animator.avatar, Is.SameAs(avatar));
            Assert.That(Reused, Is.EqualTo(before + 1));
            string[] kept = Pose();

            RigUpdate();
            Assert.That(data.animator.avatar, Is.SameAs(avatar));
            Assert.That(Pose(), Is.EqualTo(kept), "Keeping the Avatar twice gives the same pose.");

            // The same update with nothing remembered builds a new Avatar. The bones must match to the last bit.
            ForgetAvatarRecord();
            before = Reused;
            RigUpdate();
            Assert.That(data.animator.avatar, Is.Not.SameAs(avatar));
            Assert.IsTrue(avatar == null, "The replaced Avatar is destroyed.");
            Assert.That(Reused, Is.EqualTo(before));
            Assert.That(Pose(), Is.EqualTo(kept), "A kept Avatar and a rebuilt one must leave the same bones.");
            Assert.That(kept.Length, Is.GreaterThan(50), "Sanity: the character has a full skeleton.");
        }

        [Test]
        public void AFullRebuildWithUnchangedDnaAlsoKeepsTheAvatar()
        {
            RigUpdate();
            Avatar avatar = data.animator.avatar;
            string[] pose = Pose();
            long before = Reused;
            data.isMeshDirty = data.isTextureDirty = data.isShapeDirty = true;
            Assert.IsTrue(generator.GenerateSingleUMA(data, false));
            Assert.That(data.animator.avatar, Is.SameAs(avatar));
            Assert.That(Reused, Is.EqualTo(before + 1));
            Assert.That(Pose(), Is.EqualTo(pose));
        }

        [Test]
        public void ChangedDnaBuildsANewAvatarAndTheOriginalDnaGivesTheOriginalPoseAgain()
        {
            RigUpdate();
            string[] original = Pose();
            Avatar avatar = data.animator.avatar;
            long before = Reused;

            ShiftDna(0.2f);
            RigUpdate();
            Assert.That(data.animator.avatar, Is.Not.SameAs(avatar), "Different proportions need a different Avatar.");
            Assert.That(Reused, Is.EqualTo(before));
            Assert.That(Pose(), Is.Not.EqualTo(original), "Sanity: this DNA change moves bones.");
            Avatar changed = data.animator.avatar;

            // Back to where it was. The remembered Avatar belongs to the changed proportions, so this builds again.
            ShiftDna(-0.2f);
            RigUpdate();
            Assert.That(data.animator.avatar, Is.Not.SameAs(changed));
            Assert.That(Reused, Is.EqualTo(before));
        }

        [TestCase("avatar destroyed")]
        [TestCase("root scale")]
        [TestCase("t-pose settings")]
        [TestCase("pose evaluation flag")]
        [TestCase("another avatar assigned")]
        public void AnyOtherChangeToWhatTheAvatarIsBuiltFromBuildsANewOne(string change)
        {
            RigUpdate();
            Avatar avatar = data.animator.avatar;
            long before = Reused;
            RigUpdate();
            Assert.That(Reused, Is.EqualTo(before + 1), "Sanity: unchanged, the Avatar is kept.");
            before = Reused;

            bool evaluate = true;
            switch (change)
            {
                case "avatar destroyed":
                    UnityEngine.Object.DestroyImmediate(data.animator.avatar);
                    break;
                case "root scale":
                    data.transform.localScale = new Vector3(1.25f, 1.25f, 1.25f);
                    break;
                case "t-pose settings":
                {
                    // The same T-pose with one humanoid setting changed, as editing the asset would.
                    UmaTPose copy = UnityEngine.Object.Instantiate(race.TPose);
                    created.Add(copy);
                    copy.DeSerialize();
                    copy.upperArmTwist = copy.upperArmTwist == 0.25f ? 0.3f : 0.25f;
                    copy.Serialize();
                    data.OverrideTpose = copy;
                    break;
                }
                case "pose evaluation flag":
                    evaluate = false;
                    break;
                case "another avatar assigned":
                {
                    Avatar other = UnityEngine.Object.Instantiate(avatar);
                    created.Add(other);
                    data.animator.avatar = other;
                    break;
                }
            }

            data.isShapeDirty = true;
            Assert.IsTrue(generator.GenerateSingleUMA(data, false, evaluate));
            Assert.That(Reused, Is.EqualTo(before), "The change must be noticed.");
            Assert.That(data.animator.avatar, Is.Not.Null);
            Assert.That(data.animator.avatar, Is.Not.SameAs(avatar));
            Assert.IsTrue(data.animator.avatar.isValid);

            // And the new state is remembered in turn.
            Avatar rebuilt = data.animator.avatar;
            data.isShapeDirty = true;
            Assert.IsTrue(generator.GenerateSingleUMA(data, false, evaluate));
            Assert.That(data.animator.avatar, Is.SameAs(rebuilt));
            Assert.That(Reused, Is.EqualTo(before + 1));
        }

        [Test]
        public void KeepAvatarStillSkipsTheAvatarStepEntirely()
        {
            RigUpdate();
            Avatar avatar = data.animator.avatar;
            data.KeepAvatar = true;
            long before = Reused;
            ShiftDna(0.2f);
            RigUpdate();
            Assert.That(data.animator.avatar, Is.SameAs(avatar), "Keep Avatar keeps it even when proportions change.");
            Assert.That(Reused, Is.EqualTo(before), "That option does not go through the comparison at all.");
        }
    }
}
#endif
