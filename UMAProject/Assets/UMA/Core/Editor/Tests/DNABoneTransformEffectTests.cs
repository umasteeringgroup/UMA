using NUnit.Framework;
using UMA.PoseTools;
using UnityEngine;

namespace UMA.Tests
{
    /// <summary>
    /// A bone transform effect offsets one bone the way a bone pose does: on top of whatever the bone
    /// holds now, so it stacks with bone poses and the other bone effects.
    /// </summary>
    public class DNABoneTransformEffectTests
    {
        private const string BoneName = "TestBone";
        private const float Tolerance = 0.0001f;

        private static readonly Vector3 StartPosition = new Vector3(1f, 2f, 3f);
        private static readonly Quaternion StartRotation = Quaternion.Euler(0f, 30f, 0f);
        private static readonly Vector3 StartScale = new Vector3(2f, 2f, 2f);

        private static readonly Vector3 PositionOffset = new Vector3(0.5f, 0f, -1f);
        private static readonly Vector3 RotationOffset = new Vector3(0f, 0f, 40f);
        private static readonly Vector3 ScaleMultiplier = new Vector3(1.5f, 1f, 0.5f);

        private GameObject avatarObject;
        private UMAData data;
        private Transform bone;
        private DNA dna;
        private UMABonePose pose;

        [SetUp]
        public void SetUp()
        {
            dna = ScriptableObject.CreateInstance<DNA>();
            avatarObject = new GameObject("DNABoneTransformEffectTests");
            data = avatarObject.AddComponent<UMAData>();
            bone = new GameObject(BoneName).transform;
            bone.SetParent(avatarObject.transform, false);
            bone.localPosition = StartPosition;
            bone.localRotation = StartRotation;
            bone.localScale = StartScale;
            data.skeleton = new UMASkeleton(avatarObject.transform);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(dna);
            if (pose != null)
            {
                Object.DestroyImmediate(pose);
            }
            Object.DestroyImmediate(avatarObject);
        }

        // Mapped value equals the DNA value, so the tests read in plain weights.
        private static DNAEffect_BoneTransform Effect()
        {
            return new DNAEffect_BoneTransform
            {
                boneName = BoneName,
                Position = PositionOffset,
                Rotation = RotationOffset,
                Scale = ScaleMultiplier,
                curve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                minMapping = 0f,
                maxMapping = 1f
            };
        }

        private void ResetBone()
        {
            bone.localPosition = StartPosition;
            bone.localRotation = StartRotation;
            bone.localScale = StartScale;
        }

        private static void AssertVector(Vector3 expected, Vector3 actual, string what)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(Tolerance),
                $"{what}: expected {expected:F4}, was {actual:F4}");
        }

        private static void AssertRotation(Quaternion expected, Quaternion actual, string what)
        {
            Assert.That(Quaternion.Angle(expected, actual), Is.LessThan(0.01f),
                $"{what}: expected {expected.eulerAngles:F3}, was {actual.eulerAngles:F3}");
        }

        [Test]
        public void FullValueAddsTheOffsetsToTheCurrentBone()
        {
            Effect().Apply(data, dna, 1f);

            AssertVector(StartPosition + PositionOffset, bone.localPosition, "position");
            AssertRotation(StartRotation * Quaternion.Euler(RotationOffset), bone.localRotation, "rotation");
            AssertVector(Vector3.Scale(StartScale, ScaleMultiplier), bone.localScale, "scale");
        }

        [Test]
        public void HalfValueAppliesHalfOfEachOffset()
        {
            Effect().Apply(data, dna, 0.5f);

            AssertVector(StartPosition + PositionOffset * 0.5f, bone.localPosition, "position");
            AssertRotation(StartRotation * Quaternion.Euler(RotationOffset * 0.5f), bone.localRotation, "rotation");
            AssertVector(Vector3.Lerp(StartScale, Vector3.Scale(StartScale, ScaleMultiplier), 0.5f),
                bone.localScale, "scale");
        }

        [Test]
        public void ZeroValueLeavesTheBoneAlone()
        {
            Effect().Apply(data, dna, 0f);

            AssertVector(StartPosition, bone.localPosition, "position");
            AssertRotation(StartRotation, bone.localRotation, "rotation");
            AssertVector(StartScale, bone.localScale, "scale");
        }

        [Test]
        public void NewEffectWithOnlyABoneNameChangesNothing()
        {
            // Field defaults and the default curve and mapping, as the DNA editor creates it.
            var effect = new DNAEffect_BoneTransform { boneName = BoneName };

            effect.Apply(data, dna, 1f);
            effect.Apply(data, dna, 0f);

            AssertVector(StartPosition, bone.localPosition, "position");
            AssertRotation(StartRotation, bone.localRotation, "rotation");
            AssertVector(StartScale, bone.localScale, "scale");
        }

        [Test]
        public void MatchesABonePoseWithTheSameValues()
        {
            pose = ScriptableObject.CreateInstance<UMABonePose>();
            pose.poses = new[]
            {
                new UMABonePose.PoseBone
                {
                    bone = BoneName,
                    hash = UMAUtils.StringToHash(BoneName),
                    position = PositionOffset,
                    rotation = Quaternion.Euler(RotationOffset),
                    scale = ScaleMultiplier,
                    enabled = true
                }
            };

            pose.ApplyPose(data.skeleton, 0.7f);
            Vector3 posePosition = bone.localPosition;
            Quaternion poseRotation = bone.localRotation;
            Vector3 poseScale = bone.localScale;

            ResetBone();
            Effect().Apply(data, dna, 0.7f);

            AssertVector(posePosition, bone.localPosition, "position");
            AssertRotation(poseRotation, bone.localRotation, "rotation");
            AssertVector(poseScale, bone.localScale, "scale");
        }

        [Test]
        public void StacksOnTopOfAnEarlierEffect()
        {
            DNAEffect_BoneTransform effect = Effect();

            effect.Apply(data, dna, 1f);
            effect.Apply(data, dna, 1f);

            AssertVector(StartPosition + PositionOffset * 2f, bone.localPosition, "position");
            AssertRotation(StartRotation * Quaternion.Euler(RotationOffset) * Quaternion.Euler(RotationOffset),
                bone.localRotation, "rotation");
            AssertVector(Vector3.Scale(Vector3.Scale(StartScale, ScaleMultiplier), ScaleMultiplier),
                bone.localScale, "scale");
        }

        [Test]
        public void ExpressionPathHonorsTheBoneFilter()
        {
            DNAEffect_BoneTransform effect = Effect();

            effect.ApplyExpressionRig(data, dna, 1f, hash => false);
            AssertVector(StartPosition, bone.localPosition, "filtered position");
            AssertRotation(StartRotation, bone.localRotation, "filtered rotation");
            AssertVector(StartScale, bone.localScale, "filtered scale");

            effect.ApplyExpressionRig(data, dna, 1f, hash => hash == UMAUtils.StringToHash(BoneName));
            AssertVector(StartPosition + PositionOffset, bone.localPosition, "position");
            AssertRotation(StartRotation * Quaternion.Euler(RotationOffset), bone.localRotation, "rotation");
            AssertVector(Vector3.Scale(StartScale, ScaleMultiplier), bone.localScale, "scale");
        }

        [Test]
        public void MissingBoneOrSkeletonIsIgnored()
        {
            DNAEffect_BoneTransform effect = Effect();
            effect.boneName = "NoSuchBone";
            Assert.DoesNotThrow(() => effect.Apply(data, dna, 1f));

            effect.boneName = BoneName;
            data.skeleton = null;
            Assert.DoesNotThrow(() => effect.Apply(data, dna, 1f));
            Assert.DoesNotThrow(() => effect.Apply(null, dna, 1f));

            AssertVector(StartPosition, bone.localPosition, "position");
        }
    }
}
