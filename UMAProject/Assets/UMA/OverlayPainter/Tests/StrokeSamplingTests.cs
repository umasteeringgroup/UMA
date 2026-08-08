#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class StrokeSamplingTests
    {
        [Test]
        public void SamplerCarriesSpacingRemainderAcrossInputEvents()
        {
            WorldSpaceStrokeSampler sampler = new WorldSpaceStrokeSampler { Spacing = 1f };
            List<StrokeSample> output = new List<StrokeSample>();

            sampler.Add(Sample(0f), output);
            sampler.Add(Sample(0.6f), output);
            sampler.Add(Sample(1.2f), output);
            sampler.Add(Sample(2.1f), output);

            Assert.That(output, Has.Count.EqualTo(3));
            Assert.That(output[0].worldPosition.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(output[1].worldPosition.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(output[2].worldPosition.x, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(sampler.DistanceAfterLastStamp, Is.EqualTo(0.1f).Within(0.0001f));
        }

        [Test]
        public void EmittedSamplesRetainWorldMotionPressureAndTime()
        {
            WorldSpaceStrokeSampler sampler = new WorldSpaceStrokeSampler { Spacing = 0.5f };
            List<StrokeSample> output = new List<StrokeSample>();
            StrokeSample first = Sample(0f); first.pressure = 0.2f; first.time = 4f;
            StrokeSample second = Sample(1f); second.pressure = 0.8f; second.time = 6f;

            sampler.Add(first, output);
            sampler.Add(second, output);

            Assert.That(output[1].previousWorldPosition.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(output[1].pressure, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(output[1].time, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(output[0].direction, Is.EqualTo(Vector3.right),
                "The first provisional path sample should inherit the first resolved direction.");
            Assert.That(output[1].direction, Is.EqualTo(Vector3.right));
        }

        [Test]
        public void SourceOverAlphaUsesHardnessCoverageOnce()
        {
            Assert.That(TexturePaintMath.SourceOverAlpha(0f, 0.25f), Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(TexturePaintMath.SourceOverAlpha(0.5f, 0.25f), Is.EqualTo(0.625f).Within(0.0001f));
        }

        [Test]
        public void PartialPaintOnTransparentLayerKeepsStraightColor()
        {
            Color result = PaintingEngine.CompositeStraightAlpha(Color.clear, Color.green, 0.25f);

            Assert.That(result.r, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(result.g, Is.EqualTo(1f).Within(0.0001f),
                "Layer RGB must not be premultiplied before the straight-alpha compositor reads it.");
            Assert.That(result.b, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(result.a, Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void CappedStrokeDepositKeepsStraightColorAtPartialCoverage()
        {
            Color first = PaintingEngine.DepositStraightAlpha(Color.clear, Color.clear, Color.green, 0.15f);
            Color second = PaintingEngine.DepositStraightAlpha(first, Color.clear, Color.green, 0.10f);

            Assert.That(second.g, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(second.a, Is.EqualTo(0.25f).Within(0.0001f));
        }

        private static StrokeSample Sample(float x)
            => new StrokeSample(new Vector3(x, 0f, 0f), Vector3.up, new Vector2(x, 0f), 0, 0);
    }
}
#endif
