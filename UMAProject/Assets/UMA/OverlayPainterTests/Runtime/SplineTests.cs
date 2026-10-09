#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class SplineTests
    {
        [Test]
        public void LegacyWorldCurveUpgradeReplacesSurfaceRoutedControlsOnlyOnce()
        {
            TexturePaintSpline spline = new TexturePaintSpline { worldSpace = true, useBezier = true };
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.AddPoint(new Vector3(0f, 3f, 0f), Vector2.one, 1, 0, Vector3.forward);
            spline.worldCurveVersion = 0;
            spline.worldOutControls[0] = new Vector3(5f, 0f, 0f);
            spline.worldInControls[1] = new Vector3(5f, 3f, 0f);

            Assert.That(spline.UpgradeWorldCurve(), Is.True);
            Assert.That(spline.worldOutControls[0], Is.EqualTo(new Vector3(0f, 1f, 0f)));
            Assert.That(spline.worldInControls[1], Is.EqualTo(new Vector3(0f, 2f, 0f)));

            spline.worldOutControls[0] = new Vector3(1f, 1f, 0f);
            Assert.That(spline.UpgradeWorldCurve(), Is.False);
            Assert.That(spline.worldOutControls[0], Is.EqualTo(new Vector3(1f, 1f, 0f)),
                "Once upgraded, user-authored world handles must not be reprojected or reset.");
        }

        [Test]
        public void BezierControlsChangeSampledCurve()
        {
            TexturePaintSpline spline = new TexturePaintSpline { worldSpace = false, useBezier = true, smoothHandles = false };
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.AddPoint(Vector3.right, Vector2.right, 0, 0, Vector3.forward);
            spline.SetWorldControl(0, false, new Vector3(0.25f, 1f, 0f), new Vector2(0.25f, 1f));
            spline.SetWorldControl(1, true, new Vector3(0.75f, 1f, 0f), new Vector2(0.75f, 1f));

            var samples = spline.Sample(0.05f);
            Assert.That(samples.Count, Is.GreaterThan(2));
            Assert.That(samples[samples.Count / 2].uv.y, Is.GreaterThan(0.5f));
        }

        [Test]
        public void TwoDimensionalSamplingUsesOnlyTheUvCurve()
        {
            TexturePaintSpline spline = new TexturePaintSpline { worldSpace = false, useBezier = false };
            spline.AddPoint(new Vector3(100f, 200f, 300f), new Vector2(0.1f, 0.2f), 9, 42,
                Vector3.down);
            spline.AddPoint(new Vector3(-100f, -200f, -300f), new Vector2(0.9f, 0.8f), 12, 77,
                Vector3.left);

            var samples = spline.Sample(0.1f, 3);

            Assert.That(samples, Is.Not.Empty);
            Assert.That(samples[0].worldPosition,
                Is.EqualTo(new Vector3(samples[0].uv.x, samples[0].uv.y, 0f)));
            Assert.That(samples[samples.Count - 1].worldPosition,
                Is.EqualTo(new Vector3(samples[samples.Count - 1].uv.x,
                    samples[samples.Count - 1].uv.y, 0f)));
            for (int i = 0; i < samples.Count; i++)
            {
                Assert.That(samples[i].surfaceIndex, Is.EqualTo(3));
                Assert.That(samples[i].triangleIndex, Is.EqualTo(-1));
                Assert.That(samples[i].worldNormal, Is.EqualTo(Vector3.forward));
            }
        }

        [Test]
        public void LinkedHandlesRemainCollinearAndOpposed()
        {
            TexturePaintSpline spline = new TexturePaintSpline { smoothHandles = true };
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.SetWorldControl(0, false, Vector3.right, Vector2.right);
            Assert.That(Vector3.Distance(spline.worldInControls[0], Vector3.left), Is.LessThan(0.00001f));
            Assert.That(Vector2.Distance(spline.uvInControls[0], Vector2.left), Is.LessThan(0.00001f));
        }

        [Test]
        public void InsertPointSplitsBezierWithoutMovingCurveMidpoint()
        {
            TexturePaintSpline spline = new TexturePaintSpline { smoothHandles = false };
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.AddPoint(Vector3.right, Vector2.right, 0, 1, Vector3.forward);
            spline.SetWorldControl(0, false, new Vector3(0f, 1f), new Vector2(0f, 1f));
            spline.SetWorldControl(1, true, new Vector3(1f, 1f), new Vector2(1f, 1f));
            spline.EvaluateSegment(0, 1, 0.5f, out Vector3 expectedWorld, out Vector2 expectedUV);

            int inserted = spline.InsertPointAfter(0);

            Assert.That(inserted, Is.EqualTo(1));
            Assert.That(spline.PointCount, Is.EqualTo(3));
            Assert.That(Vector3.Distance(spline.worldPoints[inserted], expectedWorld), Is.LessThan(0.00001f));
            Assert.That(Vector2.Distance(spline.uvPoints[inserted], expectedUV), Is.LessThan(0.00001f));
        }

        [Test]
        public void InsertPointAtRequestedFractionPreservesBothBezierSubcurves()
        {
            const float splitT = 0.27f;
            TexturePaintSpline spline = new TexturePaintSpline { smoothHandles = false };
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.AddPoint(new Vector3(2f, 0f), new Vector2(2f, 0f), 0, 1, Vector3.forward);
            spline.SetWorldControl(0, false, new Vector3(0.25f, 1.5f), new Vector2(0.25f, 1.5f));
            spline.SetWorldControl(1, true, new Vector3(1.5f, -0.75f), new Vector2(1.5f, -0.75f));
            spline.EvaluateSegment(0, 1, splitT * 0.6f,
                out Vector3 expectedFirstWorld, out Vector2 expectedFirstUV);
            spline.EvaluateSegment(0, 1, splitT + (1f - splitT) * 0.4f,
                out Vector3 expectedSecondWorld, out Vector2 expectedSecondUV);

            int inserted = spline.InsertPointAfter(0, splitT);
            spline.EvaluateSegment(0, inserted, 0.6f,
                out Vector3 actualFirstWorld, out Vector2 actualFirstUV);
            spline.EvaluateSegment(inserted, 2, 0.4f,
                out Vector3 actualSecondWorld, out Vector2 actualSecondUV);

            Assert.That(inserted, Is.EqualTo(1));
            Assert.That(Vector3.Distance(actualFirstWorld, expectedFirstWorld), Is.LessThan(0.00001f));
            Assert.That(Vector2.Distance(actualFirstUV, expectedFirstUV), Is.LessThan(0.00001f));
            Assert.That(Vector3.Distance(actualSecondWorld, expectedSecondWorld), Is.LessThan(0.00001f));
            Assert.That(Vector2.Distance(actualSecondUV, expectedSecondUV), Is.LessThan(0.00001f));
        }

        [Test]
        public void ReverseAndRemoveMaintainSplineCollections()
        {
            TexturePaintSpline spline = new TexturePaintSpline();
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.AddPoint(Vector3.right, Vector2.right, 0, 1, Vector3.forward);
            spline.AddPoint(Vector3.up, Vector2.up, 0, 2, Vector3.forward);

            spline.Reverse();
            Assert.That(spline.worldPoints[0], Is.EqualTo(Vector3.up));
            Assert.That(spline.RemovePoint(1), Is.True);
            Assert.That(spline.PointCount, Is.EqualTo(2));
            Assert.That(spline.worldInControls, Has.Count.EqualTo(2));
            Assert.That(spline.triangleIndices, Has.Count.EqualTo(2));
            Assert.That(spline.anchors, Has.Count.EqualTo(2));
            Assert.That(spline.tangentModes, Has.Count.EqualTo(2));
        }

        [Test]
        public void AdaptiveCurveSamplingMaintainsGapFreeArcLengthSpacing()
        {
            const float spacing = 0.075f;
            TexturePaintSpline spline = new TexturePaintSpline { smoothHandles = false };
            spline.AddPoint(Vector3.zero, Vector2.zero, 2, 3, Vector3.forward);
            spline.AddPoint(Vector3.right, Vector2.right, 7, 9, Vector3.forward);
            spline.SetWorldControl(0, false, new Vector3(0f, 2f, 0f), new Vector2(0f, 2f));
            spline.SetWorldControl(1, true, new Vector3(1f, -2f, 0f), new Vector2(1f, -2f));

            var samples = spline.Sample(spacing);

            Assert.That(samples.Count, Is.GreaterThan(10));
            for (int i = 1; i < samples.Count; i++)
                Assert.That(Vector3.Distance(samples[i - 1].worldPosition, samples[i].worldPosition),
                    Is.LessThanOrEqualTo(spacing * 1.15f));
            Assert.That(samples[0].surfaceIndex, Is.EqualTo(2));
            Assert.That(samples[samples.Count - 1].surfaceIndex, Is.EqualTo(7));
        }

        [Test]
        public void RibbonSamplesCompleteTilesAtIntervalCentersWithoutEndpointOverlap()
        {
            const float tileLength = 0.2f;
            TexturePaintSpline spline = new TexturePaintSpline { useBezier = false };
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.AddPoint(Vector3.right, Vector2.right, 0, 1, Vector3.forward);

            var samples = spline.SampleRibbon(tileLength);

            Assert.That(samples, Has.Count.EqualTo(5));
            Assert.That(samples[0].worldPosition.x, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(samples[samples.Count - 1].worldPosition.x, Is.EqualTo(0.9f).Within(0.0001f));
            for (int i = 1; i < samples.Count; i++)
            {
                float centerDistance = Vector3.Distance(samples[i - 1].worldPosition, samples[i].worldPosition);
                float previousDiameter = tileLength * samples[i - 1].sizeMultiplier;
                float currentDiameter = tileLength * samples[i].sizeMultiplier;
                Assert.That(centerDistance, Is.EqualTo((previousDiameter + currentDiameter) * 0.5f).Within(0.0001f),
                    "Adjacent complete ribbon images must meet edge-to-edge.");
            }
        }

        [Test]
        public void RibbonFitsOnlyWholeSourceTilesWhenPathLengthIsNotAnExactMultiple()
        {
            const float requestedTileLength = 0.3f;
            TexturePaintSpline spline = new TexturePaintSpline { useBezier = false };
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.AddPoint(Vector3.right, Vector2.right, 0, 1, Vector3.forward);

            var samples = spline.SampleRibbon(requestedTileLength);

            Assert.That(samples, Has.Count.EqualTo(3));
            float fittedTileLength = requestedTileLength * samples[0].sizeMultiplier;
            Assert.That(fittedTileLength, Is.EqualTo(1f / 3f).Within(0.0001f));
            Assert.That(samples[0].worldPosition.x - fittedTileLength * 0.5f, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(samples[samples.Count - 1].worldPosition.x + fittedTileLength * 0.5f,
                Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void RibbonSlicesKeepContinuousFootprintsAndContiguousSourceRanges()
        {
            const float tileLength = 0.2f;
            const int slicesPerTile = 4;
            TexturePaintSpline spline = new TexturePaintSpline { useBezier = false };
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.AddPoint(Vector3.right, Vector2.right, 0, 1, Vector3.forward);

            var samples = spline.SampleRibbonSlices(tileLength, slicesPerTile, false, false);

            Assert.That(samples, Has.Count.EqualTo(20));
            Assert.That(samples[0].worldPosition.x, Is.EqualTo(0.025f).Within(0.0001f));
            Assert.That(samples[samples.Count - 1].worldPosition.x, Is.EqualTo(0.975f).Within(0.0001f));
            for (int i = 0; i < samples.Count; i++)
            {
                Assert.That(samples[i].footprintScale.x, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(samples[i].footprintScale.y, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(samples[i].sourceUVScale.x, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(samples[i].sourceUVOffset.x,
                    Is.EqualTo((i % slicesPerTile) * 0.25f).Within(0.0001f));
                if (i > 0)
                    Assert.That(Vector3.Distance(samples[i - 1].worldPosition, samples[i].worldPosition),
                        Is.EqualTo(tileLength / slicesPerTile).Within(0.0001f));
            }
        }

        [Test]
        public void ReversedVerticalRibbonSlicesStillTraverseTheWholeSourceImage()
        {
            TexturePaintSpline spline = new TexturePaintSpline { useBezier = false };
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.AddPoint(new Vector3(0.2f, 0f, 0f), Vector2.right, 0, 1, Vector3.forward);

            var samples = spline.SampleRibbonSlices(0.2f, 4, true, true);

            Assert.That(samples, Has.Count.EqualTo(4));
            Assert.That(samples[0].footprintScale.y, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(samples[0].sourceUVScale.y, Is.EqualTo(-0.25f).Within(0.0001f));
            Assert.That(samples[0].sourceUVOffset.y, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(samples[3].sourceUVOffset.y, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void PointDynamicsAndSurfaceOffsetAreInterpolated()
        {
            TexturePaintSpline spline = new TexturePaintSpline();
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.up);
            spline.AddPoint(Vector3.right, Vector2.right, 0, 1, Vector3.up);
            spline.pressures[0] = 0.2f; spline.pressures[1] = 0.8f;
            spline.widths[0] = 0.5f; spline.widths[1] = 1.5f;
            spline.flows[0] = 0.4f; spline.flows[1] = 1f;
            spline.rolls[0] = 10f; spline.rolls[1] = 50f;
            spline.colors[0] = Color.red; spline.colors[1] = Color.blue;
            spline.offsets[0] = 0.1f; spline.offsets[1] = 0.3f;

            var samples = spline.Sample(0.25f);
            StrokeSample middle = samples[samples.Count / 2];

            Assert.That(middle.pressure, Is.InRange(0.2f, 0.8f));
            Assert.That(middle.sizeMultiplier, Is.InRange(0.5f, 1.5f));
            Assert.That(middle.flowMultiplier, Is.InRange(0.4f, 1f));
            Assert.That(middle.rotation, Is.InRange(10f, 50f));
            Assert.That(middle.worldPosition.y, Is.InRange(0.1f, 0.3f));
            Assert.That(middle.hasColor, Is.True);
        }

        [Test]
        public void BrokenTangentsCanBeEditedIndependently()
        {
            TexturePaintSpline spline = new TexturePaintSpline();
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.SetTangentMode(0, TexturePaintTangentMode.Broken);
            spline.SetWorldControl(0, false, Vector3.right, Vector2.right);

            Assert.That(spline.worldOutControls[0], Is.EqualTo(Vector3.right));
            Assert.That(spline.worldInControls[0], Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void StraightTangentsPointTowardAdjacentNodesInWorldAndUvSpace()
        {
            TexturePaintSpline spline = new TexturePaintSpline();
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.AddPoint(new Vector3(3f, 3f, 0f), new Vector2(0.3f, 0.6f), 0, 1, Vector3.forward);
            spline.AddPoint(new Vector3(9f, 0f, 0f), new Vector2(0.9f, 0.3f), 0, 2, Vector3.forward);

            spline.SetTangentMode(1, TexturePaintTangentMode.Straight);

            Assert.That(Vector3.Distance(spline.worldInControls[1], new Vector3(2f, 2f, 0f)),
                Is.LessThan(0.00001f));
            Assert.That(Vector3.Distance(spline.worldOutControls[1], new Vector3(5f, 2f, 0f)),
                Is.LessThan(0.00001f));
            Assert.That(Vector2.Distance(spline.uvInControls[1], new Vector2(0.2f, 0.4f)),
                Is.LessThan(0.00001f));
            Assert.That(Vector2.Distance(spline.uvOutControls[1], new Vector2(0.5f, 0.5f)),
                Is.LessThan(0.00001f));
        }

        [Test]
        public void StraightTangentRefreshesWhenAnAdjacentPointMoves()
        {
            TexturePaintSpline spline = new TexturePaintSpline();
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.AddPoint(Vector3.right * 3f, Vector2.right * 0.3f, 0, 1, Vector3.forward);
            spline.SetTangentMode(0, TexturePaintTangentMode.Straight);
            spline.worldPoints[1] = Vector3.up * 6f;
            spline.uvPoints[1] = Vector2.up * 0.6f;

            spline.RefreshStraightTangents();

            Assert.That(Vector3.Distance(spline.worldOutControls[0], Vector3.up * 2f),
                Is.LessThan(0.00001f));
            Assert.That(Vector2.Distance(spline.uvOutControls[0], Vector2.up * 0.2f),
                Is.LessThan(0.00001f));
        }

        [Test]
        public void EditingAStraightHandleChangesItToCustom()
        {
            TexturePaintSpline spline = new TexturePaintSpline();
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.AddPoint(Vector3.right, Vector2.right, 0, 1, Vector3.forward);
            spline.SetTangentMode(0, TexturePaintTangentMode.Straight);

            spline.SetWorldControl(0, false, Vector3.up, Vector2.up);

            Assert.That(spline.tangentModes[0], Is.EqualTo(TexturePaintTangentMode.Custom));
            Assert.That(spline.worldOutControls[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void EditingACollapsedCornerHandleChangesItToCustom()
        {
            TexturePaintSpline spline = new TexturePaintSpline();
            spline.AddPoint(Vector3.zero, Vector2.zero, 0, 0, Vector3.forward);
            spline.AddPoint(Vector3.right, Vector2.right, 0, 1, Vector3.forward);
            spline.SetTangentMode(0, TexturePaintTangentMode.Corner);

            spline.SetWorldControl(0, false, Vector3.up, Vector2.up);

            Assert.That(spline.tangentModes[0], Is.EqualTo(TexturePaintTangentMode.Custom));
            Assert.That(spline.worldOutControls[0], Is.EqualTo(Vector3.up));
            Assert.That(spline.uvOutControls[0], Is.EqualTo(Vector2.up));
        }
    }
}
#endif
