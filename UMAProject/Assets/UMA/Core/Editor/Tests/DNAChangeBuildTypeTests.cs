using System.Collections.Generic;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA.Tests
{
    /// <summary>
    /// A DNA value set on a loaded character at play time is applied by the build types its effects report,
    /// so a color effect (Fitness fading the muscle overlays through the Skin alpha) writes its colors again.
    /// Setting a color keeps what the DNA color effects wrote. No generator is used: the test avatar counts
    /// the builds it would have queued.
    /// </summary>
    public class DNAChangeBuildTypeTests
    {
        private class CountingAvatar : DynamicCharacterAvatar
        {
            public int dirtyCalls;
            public override void Dirty() { dirtyCalls++; }
        }

        private readonly List<Object> objects = new List<Object>();
        private CountingAvatar avatar;
        private const string FitnessName = "TestFitness";
        private const string BoneName = "TestBoneOnly";

        private T Keep<T>(T obj) where T : Object { objects.Add(obj); return obj; }

        [SetUp]
        public void SetUp()
        {
            var root = Keep(new GameObject("DNAChangeBuildTypeTests"));
            root.hideFlags = HideFlags.HideAndDontSave;
            root.SetActive(false);
            avatar = root.AddComponent<CountingAvatar>();
            avatar.umaRecipe = new UMAData.UMARecipe();
            // A loaded recipe always has a slot list (updating the colors walks it).
            avatar.umaRecipe.slotDataList = new SlotData[0];

            // A DNA whose effect sets the Skin alpha on every channel (linear: the alpha equals the value),
            // and one with only a rig effect.
            var fitness = Keep(ScriptableObject.CreateInstance<DNA>());
            fitness.name = FitnessName;
            fitness.defaultValue = 0.5f;
            fitness.effects.Add(new DNAEffect_SharedColorChannel
            {
                SharedColorName = "Skin",
                colorComponent = DNAEffect_SharedColorChannel.ColorComponent.Alpha,
                TextureNumber = -1,
                ChannelValue = 1f,
                curve = new AnimationCurve(),
                minMapping = 0f,
                maxMapping = 1f,
                enabled = true,
            });
            var bone = Keep(ScriptableObject.CreateInstance<DNA>());
            bone.name = BoneName;
            bone.defaultValue = 0.5f;
            bone.effects.Add(new DNAEffect_BonePose { enabled = true });

            var group = Keep(ScriptableObject.CreateInstance<DNAGroup>());
            group.dnaList.Add(fitness);
            group.dnaList.Add(bone);
            var race = Keep(ScriptableObject.CreateInstance<RaceData>());
            race.name = "DNAChangeBuildTypeRace";
            race.useNewDNA = true;
            race.DNACollection = new DNACollection();
            race.DNACollection.DNAGroups.Add(group);
            avatar.activeRace.name = race.raceName;
            avatar.activeRace.data = race;

            avatar.dnaInstanceCollection = new DNAInstanceCollection();
            avatar.dnaInstanceCollection.Initialize(race.DNACollection);
            avatar.dnaInstanceCollection.dnaInstances.Add(new DNAInstance(FitnessName, 0.5f, group));
            avatar.dnaInstanceCollection.dnaInstances.Add(new DNAInstance(BoneName, 0.5f, group));

            // The character's Skin color and the loaded recipe's shared Skin color, both opaque.
            avatar.characterColors.SetRawColor("Skin", Named(new OverlayColorData(3), "Skin"));
            avatar.umaRecipe.sharedColors = new[] { Named(new OverlayColorData(3), "Skin") };
            avatar.dirtyCalls = 0;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            }
            objects.Clear();
        }

        private static OverlayColorData Named(OverlayColorData c, string name)
        {
            c.name = name;
            return c;
        }

        private static void AssertAlpha(OverlayColorData color, float expected, string what)
        {
            Assert.That(color, Is.Not.Null, what + " is missing");
            for (int i = 0; i < color.channelCount; i++)
            {
                Assert.That(color.channelMask[i].a, Is.EqualTo(expected).Within(1e-4f), what + " channel " + i);
            }
        }

        private OverlayColorData RecipeSkin()
        {
            foreach (OverlayColorData c in avatar.umaRecipe.sharedColors)
            {
                if (c != null && c.name == "Skin") return c;
            }
            return null;
        }

        [Test]
        public void BuildTypeIsFoundForNewDnaOnly()
        {
            Assert.That(avatar.TryGetDNABuildType(FitnessName, out DNAInstanceCollection.DNABuildType colorType), Is.True);
            Assert.That(colorType & DNAInstanceCollection.DNABuildType.SharedColors, Is.Not.EqualTo(DNAInstanceCollection.DNABuildType.None));
            Assert.That(avatar.TryGetDNABuildType(BoneName, out DNAInstanceCollection.DNABuildType boneType), Is.True);
            Assert.That(boneType, Is.EqualTo(DNAInstanceCollection.DNABuildType.Rig));
            Assert.That(avatar.TryGetDNABuildType("NotADna", out _), Is.False);

            avatar.activeRace.data.useNewDNA = false;
            Assert.That(avatar.TryGetDNABuildType(FitnessName, out _), Is.False, "A race on the older DNA system has no build types.");
        }

        [Test]
        public void SetDnaWithRebuildWritesTheColorEffectAgain()
        {
            // The slider path at play time (NewUMAGUI, SliderController, sample DnaGUI) used to mark the
            // textures dirty without running the color effect, so the alpha kept its old value.
            avatar.SetDNA(FitnessName, 0.8f, true);

            AssertAlpha(avatar.GetColor("Skin"), 0.8f, "avatar Skin");
            AssertAlpha(RecipeSkin(), 0.8f, "recipe Skin (what the textures are drawn from)");
            Assert.That(avatar.isTextureDirty, Is.True);
            Assert.That(avatar.dirtyCalls, Is.EqualTo(1));
        }

        [Test]
        public void ApplyDnaChangeFallsBackForDnaWithoutBuildTypes()
        {
            avatar.ApplyDNAChange("NotADna", true);

            Assert.That(avatar.isShapeDirty, Is.True);
            Assert.That(avatar.isTextureDirty, Is.True);
            Assert.That(avatar.isMeshDirty, Is.True);
            AssertAlpha(avatar.GetColor("Skin"), 1f, "avatar Skin (no color effect ran)");
        }

        [Test]
        public void RigOnlyDnaDoesNotRedrawTextures()
        {
            avatar.SetDNA(BoneName, 0.9f, true);

            Assert.That(avatar.isShapeDirty, Is.True);
            Assert.That(avatar.isTextureDirty, Is.False);
            Assert.That(avatar.dirtyCalls, Is.EqualTo(1));
        }

        [Test]
        public void SettingAColorKeepsTheDnaColorEffect()
        {
            avatar.SetDNA(FitnessName, 0.3f, true);

            // A skin swatch: opaque on every channel.
            avatar.SetRawColor("Skin", new OverlayColorData(3), true);

            AssertAlpha(avatar.GetColor("Skin"), 0.3f, "avatar Skin after a new color");
            AssertAlpha(RecipeSkin(), 0.3f, "recipe Skin after a new color");
        }

        [Test]
        public void ReapplyingRecipeEffectsMarksNothingDirty()
        {
            // A RecipeUpdated handler (NewUMAGUI's DNA restore after a race change) sets DNA after the build
            // ran the recipe effects; running them again must not queue a second build.
            avatar.GetDNA()[FitnessName].Set(0.65f);

            avatar.ReapplyRecipeDNAEffects(DNAInstanceCollection.DNABuildType.SharedColors, out bool drawn, out bool mesh);

            Assert.That(drawn, Is.True);
            Assert.That(mesh, Is.False);
            AssertAlpha(RecipeSkin(), 0.65f, "recipe Skin");
            Assert.That(avatar.dirtyCalls, Is.EqualTo(0));
        }
    }
}
