#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>Repack UVs: outlines and boxes packed, turns, Texture Detail and mirrors sharing an island.</summary>
    public sealed class UvPackingTests
    {
        private static PatternMesh Mesh(ClothesPattern pattern) =>
            PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), 2f);

        [Test]
        public void Repack_EverySample_FitsWithNoOverlapsAndCoversAtLeastAThird()
        {
            float total = 0f;
            foreach (var sample in SamplePatterns.All)
            {
                ClothesPattern pattern = sample.make();
                try
                {
                    UvLayout.Repack(pattern, out float coverage);
                    Assert.That(UvLayout.FindProblems(pattern, Mesh(pattern)), Is.Empty, sample.name);
                    Assert.That(coverage, Is.GreaterThan(0.4f), sample.name);
                    total += coverage;
                }
                finally { Object.DestroyImmediate(pattern); }
            }
            // Measured 63% over the 20 samples (54% with shelf packing by bounding box).
            Assert.That(total / SamplePatterns.All.Length, Is.GreaterThan(0.58f));
        }

        [Test]
        public void Repack_PiecesCloseToRectangles_PackAtLeastAsWellAsBoxes()
        {
            // The beanie's crown panels are where box packing beat the outline packer; with both, it keeps the better.
            foreach (var sample in SamplePatterns.All)
            {
                if (sample.name != "Beanie" && sample.name != "Boxer Shorts") continue;
                ClothesPattern pattern = sample.make();
                try
                {
                    UvLayout.Repack(pattern, out float coverage);
                    Assert.That(coverage, Is.GreaterThan(0.65f), sample.name);
                }
                finally { Object.DestroyImmediate(pattern); }
            }
        }

        [Test]
        public void Repack_WithoutTurns_KeepsEveryPieceGrainUp()
        {
            ClothesPattern pattern = SamplePatterns.All[0].make();
            try
            {
                pattern.uvLayout.turnPieces = false;
                UvLayout.Repack(pattern);
                foreach (PatternPiece piece in pattern.pieces)
                {
                    Assert.That(Mathf.DeltaAngle(piece.uvIsland.rotation, -piece.grainAngle), Is.EqualTo(0f).Within(0.01f), piece.name);
                }
                Assert.That(UvLayout.FindProblems(pattern, Mesh(pattern)), Is.Empty);
            }
            finally { Object.DestroyImmediate(pattern); }
        }

        [Test]
        public void Repack_WithTurns_OnlyTurnsByQuarters()
        {
            ClothesPattern pattern = SamplePatterns.All[0].make();
            try
            {
                UvLayout.Repack(pattern);
                foreach (PatternPiece piece in pattern.pieces)
                {
                    float turn = Mathf.Repeat(piece.uvIsland.rotation + piece.grainAngle, 360f);
                    float off = Mathf.Abs(Mathf.DeltaAngle(turn, Mathf.Round(turn / 90f) * 90f));
                    Assert.That(off, Is.LessThan(0.01f), piece.name);
                }
            }
            finally { Object.DestroyImmediate(pattern); }
        }

        [Test]
        public void Repack_TextureDetail_ScalesThatPiecesTexelDensity()
        {
            ClothesPattern pattern = VestPattern.Create();
            try
            {
                pattern.pieces[0].textureDetail = 2f;
                pattern.pieces[1].textureDetail = 1f;
                UvLayout.Repack(pattern);
                Assert.That(pattern.pieces[0].uvIsland.scale / pattern.pieces[1].uvIsland.scale, Is.EqualTo(2f).Within(1e-4f));
                Assert.That(UvLayout.FindProblems(pattern, Mesh(pattern)), Is.Empty);
            }
            finally { Object.DestroyImmediate(pattern); }
        }

        [Test]
        public void Repack_SharingMirrors_GivesALinkedMirrorItsOriginalsTexels()
        {
            ClothesPattern pattern = VestPattern.Create();
            try
            {
                PatternPiece source = pattern.pieces[0];
                PatternPiece copy = PatternEditing.MirrorCopy(source, "Mirror");
                copy.mirrorLinked = true;
                foreach (PatternPoint p in copy.points) p.position += new Vector2(120f, 0f);
                pattern.pieces.Add(copy);

                pattern.uvLayout.shareMirrors = false;
                UvLayout.Repack(pattern);
                Assert.That(copy.uvIsland.sharesWith, Is.Null.Or.Empty);
                Assert.That(UvLayout.FindProblems(pattern, Mesh(pattern)), Is.Empty);

                pattern.uvLayout.shareMirrors = true;
                UvLayout.Repack(pattern);
                Assert.That(copy.uvIsland.sharesWith, Is.EqualTo(source.id));
                int n = source.points.Count, copyIndex = pattern.pieces.IndexOf(copy);
                for (int k = 0; k < n; k++)
                {
                    Vector2 mirrored = UvLayout.ToUV(pattern, copyIndex, copy.points[k].position);
                    Vector2 original = UvLayout.ToUV(pattern, 0, source.points[(n - k) % n].position);
                    Assert.That((mirrored - original).magnitude, Is.LessThan(1e-4f), "corner " + k);
                }
                Assert.That(UvLayout.FindProblems(pattern, Mesh(pattern)), Is.Empty);
            }
            finally { Object.DestroyImmediate(pattern); }
        }
    }
}
#endif
