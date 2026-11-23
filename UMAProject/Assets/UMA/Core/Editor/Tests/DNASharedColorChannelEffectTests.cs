using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    /// <summary>
    /// A shared color effect writes its color while the recipe is loaded, so a DNA change has to ask for
    /// that step again, and an effect that sets a value outright has to run at the default DNA value too.
    /// </summary>
    public class DNASharedColorChannelEffectTests
    {
        private class CountingEffect : DNAEffect
        {
            public bool appliesAtDefault;
            public int calls;
            public float lastValue;

            public override bool AppliesAtDefaultValue => appliesAtDefault;

            public override DNAInstanceCollection.DNABuildType AreaEffect =>
                DNAInstanceCollection.DNABuildType.Texture;

            public override void AfterRecipeGenerated(UMAData avatar, DNA dna, float value)
            {
                calls++;
                lastValue = value;
            }
        }

        private DNA dna;

        [SetUp]
        public void SetUp()
        {
            dna = ScriptableObject.CreateInstance<DNA>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(dna);
        }

        [Test]
        public void SharedColorEffectsAskForTheSharedColorStep()
        {
            const DNAInstanceCollection.DNABuildType sharedColors = DNAInstanceCollection.DNABuildType.SharedColors;

            Assert.AreNotEqual(0, (int)(new DNAEffect_SharedColorChannel().AreaEffect & sharedColors));
            Assert.AreNotEqual(0, (int)(new DNAEffect_SharedColor().AreaEffect & sharedColors));
            Assert.AreNotEqual(0, (int)(new DNAEffect_SharedColorProperty().AreaEffect & sharedColors));

            dna.effects.Add(new DNAEffect_SharedColorChannel());
            Assert.AreNotEqual(0, (int)(dna.GetBuildType() & sharedColors),
                "A DNA with a shared color effect must report the SharedColors build type.");
        }

        [Test]
        public void SharedColorChannelRunsAtTheDefaultValue()
        {
            Assert.IsTrue(new DNAEffect_SharedColorChannel().AppliesAtDefaultValue);
            // These write their value outright too: a color worked out from two fixed colors,
            // a property block value, and an overlay transform.
            Assert.IsTrue(new DNAEffect_SharedColor().AppliesAtDefaultValue);
            Assert.IsTrue(new DNAEffect_SharedColorProperty().AppliesAtDefaultValue);
            Assert.IsTrue(new DNAEffect_OverlayUVTransform().AppliesAtDefaultValue);
        }

        [Test]
        public void DefaultValueOnlyRunsEffectsThatAskForIt()
        {
            var relative = new CountingEffect();
            var absolute = new CountingEffect { appliesAtDefault = true };
            dna.effects.Add(relative);
            dna.effects.Add(absolute);

            DNAInstanceCollection.DNABuildType flags = dna.AfterRecipeGeneration(null, 0.5f, true);

            Assert.AreEqual(0, relative.calls);
            Assert.AreEqual(1, absolute.calls);
            Assert.AreEqual(0.5f, absolute.lastValue);
            Assert.AreEqual(DNAInstanceCollection.DNABuildType.Texture, flags);
        }

        [Test]
        public void OtherValuesRunEveryEffect()
        {
            var relative = new CountingEffect();
            var absolute = new CountingEffect { appliesAtDefault = true };
            dna.effects.Add(relative);
            dna.effects.Add(absolute);

            dna.AfterRecipeGeneration(null, 0.75f, false);

            Assert.AreEqual(1, relative.calls);
            Assert.AreEqual(1, absolute.calls);
        }

        [Test]
        public void DefaultValueWithNoAbsoluteEffectDoesNothing()
        {
            var relative = new CountingEffect();
            dna.effects.Add(relative);

            DNAInstanceCollection.DNABuildType flags = dna.AfterRecipeGeneration(null, 0.5f, true);

            Assert.AreEqual(0, relative.calls);
            Assert.AreEqual(DNAInstanceCollection.DNABuildType.None, flags);
        }

        [Test]
        public void InspectorFlagsAnEffectThatIsSkippedAtTheDefaultButHasAValueThere()
        {
            dna.defaultValue = 0.5f;
            var modifier = new DNAEffect_MeshModifier
            {
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                minMapping = 0f,
                maxMapping = 1f
            };

            // 0..1 with a default of 0.5: nothing at the default, half the effect just beside it.
            Assert.IsTrue(DNAEditor.IsSkippedAtDefaultWithAValue(dna, modifier, out float mapped));
            Assert.AreEqual(0.5f, mapped, 0.0001f);

            // -1..1 gives 0 at the default, so skipping it there changes nothing.
            modifier.minMapping = -1f;
            Assert.IsFalse(DNAEditor.IsSkippedAtDefaultWithAValue(dna, modifier, out _));

            // An effect that runs at the default, a rig effect and a disabled effect are not skipped wrongly.
            var color = new DNAEffect_SharedColor { minMapping = 0f, maxMapping = 1f };
            Assert.IsFalse(DNAEditor.IsSkippedAtDefaultWithAValue(dna, color, out _));
            var scale = new DNAEffect_BoneScale { minMapping = 0f, maxMapping = 1f };
            Assert.IsFalse(DNAEditor.IsSkippedAtDefaultWithAValue(dna, scale, out _));
            modifier.minMapping = 0f;
            modifier.enabled = false;
            Assert.IsFalse(DNAEditor.IsSkippedAtDefaultWithAValue(dna, modifier, out _));
        }
    }
}
