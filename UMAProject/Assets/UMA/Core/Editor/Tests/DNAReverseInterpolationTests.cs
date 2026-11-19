using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Tests
{
    /// <summary>
    /// Reverse interpolation: from a base measurement and a wanted one, find the DNA value to set.
    /// </summary>
    public class DNAReverseInterpolationTests
    {
        private const string HeightDnaPath = "Assets/UMA/UMA3/DNA/Body/height.asset";
        private const float Tolerance = 0.0001f;

        private DNA dna;
        private GameObject avatarObject;

        [SetUp]
        public void SetUp()
        {
            dna = ScriptableObject.CreateInstance<DNA>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(dna);
            if (avatarObject != null)
            {
                Object.DestroyImmediate(avatarObject);
            }
        }

        // Linear curve, min -1, max 1: factor(v) = 1 + (2v - 1) = 2v, so 1 at the default of 0.5.
        private static DNAEffect_BoneScale LinearHeightEffect()
        {
            return new DNAEffect_BoneScale
            {
                BoneName = "Position",
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                minMapping = -1f,
                maxMapping = 1f,
                ScaleFactor = Vector3.one,
            };
        }

        [Test]
        public void HeightExampleTwoMetresToOnePointSix()
        {
            dna.defaultValue = 0.5f;
            dna.effects.Add(LinearHeightEffect());

            // 2.0 m at 0.5; height(v) = 4v, so 1.6 m needs 0.4.
            Assert.AreEqual(0.4f, dna.ReverseInterpolate(2.0f, 1.6f), Tolerance);

            var result = dna.SolveReverseInterpolation(2.0f, 1.6f, 0.5f);
            Assert.IsTrue(result.Solvable);
            Assert.IsTrue(result.Reachable);
            Assert.AreEqual(1.6f, result.Achieved, Tolerance);
            Assert.AreEqual(0f, result.Minimum, Tolerance);
            Assert.AreEqual(4f, result.Maximum, Tolerance);
        }

        [Test]
        public void SameMeasurementGivesBaseValueBack()
        {
            dna.effects.Add(LinearHeightEffect());
            Assert.AreEqual(0.5f, dna.ReverseInterpolate(2.0f, 2.0f), Tolerance);
            Assert.AreEqual(0.7f, dna.ReverseInterpolate(1.8f, 1.8f, 0.7f), Tolerance);
        }

        [Test]
        public void BaseTakenAwayFromTheDefault()
        {
            dna.effects.Add(LinearHeightEffect());

            // 2.4 m at 0.6 (factor 1.2) means 2.0 m per unit factor; 1.6 m is factor 0.8, value 0.4.
            Assert.AreEqual(0.4f, dna.ReverseInterpolate(2.4f, 1.6f, 0.6f), Tolerance);
        }

        [Test]
        public void TargetOutOfReachIsClampedAndReported()
        {
            dna.effects.Add(LinearHeightEffect());

            var tooTall = dna.SolveReverseInterpolation(2.0f, 5.0f, 0.5f);
            Assert.IsTrue(tooTall.Solvable);
            Assert.IsFalse(tooTall.Reachable);
            Assert.AreEqual(1f, tooTall.Value, Tolerance);
            Assert.AreEqual(4f, tooTall.Achieved, Tolerance);

            var negative = dna.SolveReverseInterpolation(2.0f, -1.0f, 0.5f);
            Assert.IsFalse(negative.Reachable);
            Assert.AreEqual(0f, negative.Value, Tolerance);
        }

        [Test]
        public void ShippedHeightDnaCurveIsInverted()
        {
            var height = AssetDatabase.LoadAssetAtPath<DNA>(HeightDnaPath);
            Assert.IsNotNull(height, "UMA 3 height DNA not found at " + HeightDnaPath);

            var result = height.SolveReverseInterpolation(2.0f, 1.6f, height.defaultValue);
            Assert.IsTrue(result.Reachable);

            // Forward check through the effect's own mapping: 2.0 * factor(v) / factor(default) = 1.6.
            Assert.IsTrue(height.TryGetScaleFactor(result.Value, out Vector3 at));
            Assert.IsTrue(height.TryGetScaleFactor(height.defaultValue, out Vector3 atDefault));
            Assert.AreEqual(1.6f, 2.0f * at.y / atDefault.y, Tolerance);

            // The curve is not linear: a straight line would give 0.4, the shipped curve needs less.
            Assert.Greater(result.Value, 0f);
            Assert.Less(result.Value, 0.25f);

            // The shipped curve cannot go below about 0.73 of the default height.
            var tooShort = height.SolveReverseInterpolation(2.0f, 1.0f, height.defaultValue);
            Assert.IsFalse(tooShort.Reachable);
            Assert.AreEqual(0f, tooShort.Value, Tolerance);
            Assert.AreEqual(tooShort.Minimum, tooShort.Achieved, Tolerance);
        }

        [Test]
        public void DnaWithoutScaleEffectIsNotSolvable()
        {
            dna.defaultValue = 0.3f;
            var result = dna.SolveReverseInterpolation(2.0f, 1.6f, dna.defaultValue);
            Assert.IsFalse(result.Solvable);
            Assert.AreEqual(0.3f, result.Value, Tolerance);
            Assert.AreEqual(0.3f, dna.ReverseInterpolate(2.0f, 1.6f), Tolerance);
        }

        [Test]
        public void DisabledEffectsAreIgnored()
        {
            var effect = LinearHeightEffect();
            effect.enabled = false;
            dna.effects.Add(effect);
            Assert.IsFalse(dna.SolveReverseInterpolation(2.0f, 1.6f, 0.5f).Solvable);
        }

        [Test]
        public void BoneTransformScaleUsesTheChosenAxis()
        {
            dna.effects.Add(new DNAEffect_BoneTransform
            {
                boneName = "Position",
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                minMapping = 0f,
                maxMapping = 1f,
                Scale = new Vector3(1.5f, 1f, 0.5f),
            });

            // factor(v) = 1 + (Scale - 1) * v.
            Assert.AreEqual(1f, dna.SolveReverseInterpolation(2f, 3f, 0f, 0).Value, Tolerance);
            Assert.AreEqual(0.5f, dna.SolveReverseInterpolation(2f, 1.5f, 0f, 2).Value, Tolerance);
            // Y does not scale, so any other height is out of reach.
            Assert.IsFalse(dna.SolveReverseInterpolation(2f, 3f, 0f, 1).Reachable);
        }

        [Test]
        public void ScaleFactorsMatchWhatTheEffectsDoToTheBone()
        {
            avatarObject = new GameObject("DNAReverseInterpolationTests");
            var data = avatarObject.AddComponent<UMAData>();
            var bone = new GameObject("Position").transform;
            bone.SetParent(avatarObject.transform, false);
            bone.localScale = new Vector3(2f, 2f, 2f);
            data.skeleton = new UMASkeleton(avatarObject.transform);

            var scaleEffect = LinearHeightEffect();
            scaleEffect.ScaleFactor = new Vector3(1f, 0.5f, 2f);
            var transformEffect = new DNAEffect_BoneTransform
            {
                boneName = "Position",
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                minMapping = 0f,
                maxMapping = 1f,
                Scale = new Vector3(1.5f, 0.8f, 1.2f),
            };
            dna.effects.Add(scaleEffect);
            dna.effects.Add(transformEffect);

            const float value = 0.7f;
            Vector3 before = bone.localScale;
            dna.Apply(data, value);
            Vector3 after = bone.localScale;

            Assert.IsTrue(dna.TryGetScaleFactor(value, out Vector3 factor));
            Assert.AreEqual(after.x / before.x, factor.x, Tolerance);
            Assert.AreEqual(after.y / before.y, factor.y, Tolerance);
            Assert.AreEqual(after.z / before.z, factor.z, Tolerance);
        }

        [Test]
        public void MappedValueIsInverted()
        {
            var effect = LinearHeightEffect();
            var result = effect.GetValueForMappedValue(-0.2f);
            Assert.IsTrue(result.Reachable);
            Assert.AreEqual(0.4f, result.Value, Tolerance);
        }

        [TestCase(0.9f, 0.75f)]
        [TestCase(0.1f, 0.25f)]
        public void CurveThatTurnsBackPrefersTheRootNearestTheBase(float preferred, float expected)
        {
            // Rises to 1 at 0.5 and falls back to 0: 0.5 is reached at 0.25 and at 0.75.
            var result = DNAReverseInterpolation.Solve(v => 1f - Mathf.Abs(2f * v - 1f), 0.5f, preferred);
            Assert.IsTrue(result.Reachable);
            Assert.AreEqual(expected, result.Value, Tolerance);
        }

        [Test]
        public void SteepFunctionStillCountsAsReached()
        {
            var result = DNAReverseInterpolation.Solve(v => 10000f * v, 1234.567f);
            Assert.IsTrue(result.Reachable);
            Assert.AreEqual(0.1234567f, result.Value, 0.000001f);
        }

        [Test]
        public void JumpOverTheTargetIsNotReached()
        {
            var result = DNAReverseInterpolation.Solve(v => v < 0.5f ? 0f : 1f, 0.5f);
            Assert.IsFalse(result.Reachable);
        }
    }
}
