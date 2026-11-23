using System;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    /// <summary>
    /// An overlay transform effect goes from "untransformed" at a mapped value of 0 to its full offset,
    /// scale and rotation at 1, marks the overlay as transformed so the texture merge reads it, and takes
    /// the transform back when the DNA returns to a value that gives none.
    /// </summary>
    public class DNAOverlayUVTransformEffectTests
    {
        private const string OverlayName = "OverlayUVTransformTestOverlay";
        private const float Tolerance = 0.0001f;

        private GameObject avatarObject;
        private UMAData data;
        private SlotDataAsset slotAsset;
        private OverlayDataAsset overlayAsset;
        private OverlayData overlay;
        private DNA dna;

        [SetUp]
        public void SetUp()
        {
            dna = ScriptableObject.CreateInstance<DNA>();
            slotAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            slotAsset._oldSlotName = "OverlayUVTransformTestSlot";
            overlayAsset = ScriptableObject.CreateInstance<OverlayDataAsset>();
            overlayAsset._oldOverlayName = OverlayName;
            overlayAsset.textureList = Array.Empty<Texture>();

            var slot = new SlotData(slotAsset);
            overlay = new OverlayData(overlayAsset);
            slot.AddOverlay(overlay);

            avatarObject = new GameObject("DNAOverlayUVTransformEffectTests");
            data = avatarObject.AddComponent<UMAData>();
            data.umaRecipe = new UMAData.UMARecipe
            {
                slotDataList = new[] { slot },
                sharedColors = Array.Empty<OverlayColorData>()
            };
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(avatarObject);
            UnityEngine.Object.DestroyImmediate(slotAsset);
            UnityEngine.Object.DestroyImmediate(overlayAsset);
            UnityEngine.Object.DestroyImmediate(dna);
        }

        // Mapped value equals the DNA value, so the tests read in plain weights.
        private static DNAEffect_OverlayUVTransform Effect()
        {
            return new DNAEffect_OverlayUVTransform
            {
                overlayName = OverlayName,
                offset = new Vector2(0.2f, -0.1f),
                scale = new Vector2(2f, 0.5f),
                rotation = 90f,
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                minMapping = 0f,
                maxMapping = 1f
            };
        }

        private static void AssertVector(Vector2 expected, Vector2 actual, string what)
        {
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(Tolerance),
                $"{what}: expected {expected:F4}, was {actual:F4}");
        }

        [Test]
        public void ZeroGivesNoTransform()
        {
            Effect().GetTransform(0f, out Vector2 translate, out Vector2 scale, out float rotation);

            Assert.AreEqual(Vector2.zero, translate);
            Assert.AreEqual(Vector2.one, scale, "A value of 0 must leave the overlay at its own size, not shrink it to nothing.");
            Assert.AreEqual(0f, rotation);
        }

        [Test]
        public void ValuesBlendFromUntransformedToTheFullTransform()
        {
            DNAEffect_OverlayUVTransform effect = Effect();

            effect.GetTransform(1f, out Vector2 translate, out Vector2 scale, out float rotation);
            AssertVector(new Vector2(0.2f, -0.1f), translate, "full translate");
            AssertVector(new Vector2(2f, 0.5f), scale, "full scale");
            Assert.AreEqual(90f, rotation, Tolerance);

            effect.GetTransform(0.5f, out translate, out scale, out rotation);
            AssertVector(new Vector2(0.1f, -0.05f), translate, "half translate");
            AssertVector(new Vector2(1.5f, 0.75f), scale, "half scale");
            Assert.AreEqual(45f, rotation, Tolerance);
        }

        [Test]
        public void ScaleNeverCollapsesAndRotationStaysInRange()
        {
            DNAEffect_OverlayUVTransform effect = Effect();

            // Far outside 0..1 the unclamped blend would pass through zero and go negative.
            effect.GetTransform(4f, out _, out Vector2 scale, out float rotation);
            Assert.Greater(scale.y, 0f);
            Assert.That(rotation, Is.InRange(0f, 360f));

            effect.GetTransform(-1f, out _, out scale, out rotation);
            Assert.Greater(scale.x, 0f);
            Assert.That(rotation, Is.InRange(0f, 360f));
            Assert.AreEqual(270f, rotation, Tolerance);
        }

        [Test]
        public void TransformIsWrittenAndTheOverlayMarkedTransformed()
        {
            Assert.IsFalse(overlay.instanceTransformed);

            Effect().AfterRecipeGenerated(data, dna, 1f);

            Assert.IsTrue(overlay.instanceTransformed,
                "The texture merge only reads the transform of an overlay that is marked as transformed.");
            AssertVector(new Vector2(0.2f, -0.1f), overlay.Translate, "translate");
            AssertVector(new Vector2(2f, 0.5f), overlay.Scale, "scale");
            Assert.AreEqual(90f, overlay.Rotation, Tolerance);
        }

        [Test]
        public void ReturningToZeroTakesTheTransformBack()
        {
            DNAEffect_OverlayUVTransform effect = Effect();
            effect.AfterRecipeGenerated(data, dna, 0.75f);
            Assert.IsTrue(overlay.instanceTransformed);

            effect.AfterRecipeGenerated(data, dna, 0f);

            Assert.IsFalse(overlay.instanceTransformed);
            Assert.AreEqual(Vector2.zero, overlay.Translate);
            Assert.AreEqual(Vector2.one, overlay.Scale);
            Assert.AreEqual(0f, overlay.Rotation);
        }

        [Test]
        public void ZeroOnAnUntransformedOverlayLeavesItAlone()
        {
            Effect().AfterRecipeGenerated(data, dna, 0f);

            Assert.IsFalse(overlay.instanceTransformed);
            Assert.AreEqual(Vector2.one, overlay.Scale);
        }

        [Test]
        public void RunsAtTheDefaultValueAndAsksForATextureBuild()
        {
            var effect = new DNAEffect_OverlayUVTransform();

            Assert.IsTrue(effect.AppliesAtDefaultValue,
                "Going back to the default DNA value must take the transform back.");
            Assert.AreNotEqual(0, (int)(effect.AreaEffect & DNAInstanceCollection.DNABuildType.Texture));
        }

        [Test]
        public void MissingOverlayOrRecipeIsIgnored()
        {
            DNAEffect_OverlayUVTransform effect = Effect();
            effect.overlayName = "NoSuchOverlay";
            Assert.DoesNotThrow(() => effect.AfterRecipeGenerated(data, dna, 1f));
            Assert.IsFalse(overlay.instanceTransformed);

            effect.overlayName = OverlayName;
            data.umaRecipe = null;
            Assert.DoesNotThrow(() => effect.AfterRecipeGenerated(data, dna, 1f));
            Assert.DoesNotThrow(() => effect.AfterRecipeGenerated(null, dna, 1f));
        }
    }
}
