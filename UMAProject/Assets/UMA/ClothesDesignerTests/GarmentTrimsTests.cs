#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UMA.Editors;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>Trims (zippers, stitching, hems) placed on the pattern, as handed to the trim painter.</summary>
    public sealed class GarmentTrimsTests
    {
        private ClothesPattern vest;

        [SetUp]
        public void Make()
        {
            vest = VestPattern.Create();
            UvLayout.Repack(vest);
        }

        [TearDown]
        public void Clean() { if (vest != null) Object.DestroyImmediate(vest); }

        private PatternTrim TrimOnEdge(int edge, float start = 0f, float end = 1f, float inset = 0f)
        {
            PatternPiece piece = vest.pieces[0];
            var trim = new PatternTrim { id = ClothesPattern.NewId(), name = "Zip", pieceId = piece.id, pathId = piece.edges[edge].id, start = start, end = end, insetCm = inset, constructionId = "zip" };
            vest.trims.Add(trim);
            return trim;
        }

        private static float Length(List<Vector2> path)
        {
            float length = 0f;
            for (int i = 1; i < path.Count; i++) length += Vector2.Distance(path[i - 1], path[i]);
            return length;
        }

        [Test]
        public void DrawnPath_FollowsItsEdge_FromEndToEnd()
        {
            PatternTrim trim = TrimOnEdge(0);
            PatternGeometry.EdgeCurve curve = LineGeometry.CurveOf(vest.pieces[0], trim.pathId, PieceScale.Identity);
            List<Vector2> path = GarmentTrims.DrawnPath(vest, trim, out int pieceIndex);
            Assert.That(pieceIndex, Is.EqualTo(0));
            Assert.That(Vector2.Distance(path[0], curve.At(0f)), Is.LessThan(1e-3f));
            Assert.That(Vector2.Distance(path[path.Count - 1], curve.At(1f)), Is.LessThan(1e-3f));
            Assert.That(Length(path), Is.EqualTo(curve.Length).Within(curve.Length * 0.02f));
            for (int i = 1; i < path.Count; i++) Assert.That(Vector2.Distance(path[i - 1], path[i]), Is.LessThanOrEqualTo(GarmentTrims.SampleCm + 1e-3f));
        }

        [Test]
        public void DrawnPath_Span_TakesThatPartOfTheEdge()
        {
            PatternTrim whole = TrimOnEdge(0);
            PatternTrim half = TrimOnEdge(0, 0.25f, 0.75f);
            float wholeLength = Length(GarmentTrims.DrawnPath(vest, whole, out _));
            Assert.That(Length(GarmentTrims.DrawnPath(vest, half, out _)), Is.EqualTo(wholeLength * 0.5f).Within(wholeLength * 0.05f));
        }

        [Test]
        public void DrawnPath_Inset_MovesTheTrimInsideThePiece()
        {
            for (int edge = 0; edge < vest.pieces[0].edges.Count; edge++)
            {
                PatternTrim trim = TrimOnEdge(edge, 0.2f, 0.8f, 1f);
                List<Vector2> path = GarmentTrims.DrawnPath(vest, trim, out _);
                List<Vector2> outline = PatternGeometry.SampleOutline(vest.pieces[0], 0.5f, null);
                foreach (Vector2 p in path)
                {
                    Assert.That(PatternGeometry.PointInPolygon(outline, p), Is.True, "edge " + edge);
                }
                Assert.That(PatternGeometry.DistanceToPolyline(outline, path[path.Count / 2]), Is.EqualTo(1f).Within(0.15f), "edge " + edge);
            }
        }

        [Test]
        public void UvPaths_MapThroughTheLayout_WithTheWidthInTexels()
        {
            PatternTrim trim = TrimOnEdge(0, 0f, 1f, 0.8f);
            trim.widthCm = 1.5f;
            List<UMATrimPath> paths = GarmentTrims.UvPaths(vest);
            Assert.That(paths.Count, Is.EqualTo(1));
            List<Vector2> drawn = GarmentTrims.DrawnPath(vest, trim, out _);
            Assert.That(paths[0].uv.Count, Is.EqualTo(drawn.Count));
            for (int i = 0; i < drawn.Count; i++)
            {
                Vector2 uv = paths[0].uv[i];
                Assert.That((uv - UvLayout.ToUV(vest, 0, drawn[i])).magnitude, Is.LessThan(1e-5f));
                Assert.That(uv.x, Is.InRange(0f, 1f)); Assert.That(uv.y, Is.InRange(0f, 1f));
            }
            Assert.That(paths[0].widthUV, Is.EqualTo(1.5f * vest.pieces[0].uvIsland.scale).Within(1e-6f));
            Assert.That(paths[0].constructionId, Is.EqualTo("zip"));
            Assert.That(paths[0].name, Is.EqualTo("Zip"));
        }

        [Test]
        public void UvPaths_TrimOnAMissingEdge_IsLeftOutAndNamed()
        {
            PatternTrim trim = TrimOnEdge(0);
            trim.pathId = "gone";
            trim.name = "Lost Zip";
            var missing = new List<string>();
            Assert.That(GarmentTrims.UvPaths(vest, missing), Is.Empty);
            Assert.That(missing, Is.EqualTo(new[] { "Lost Zip" }));
        }

        [Test]
        public void ShortName_IsTheLastPartOfTheMenuPath()
        {
            Assert.That(GarmentTrims.ShortName("Fasteners and Distress/Metal Zipper"), Is.EqualTo("Metal Zipper"));
            Assert.That(GarmentTrims.ShortName("Topstitch"), Is.EqualTo("Topstitch"));
            Assert.That(GarmentTrims.ShortName(null), Is.EqualTo("Trim"));
        }

        [Test]
        public void Create_PrefersAZipper_WhenAPainterIsInstalled()
        {
            PatternPiece piece = vest.pieces[0];
            PatternTrim trim = GarmentTrims.Create(piece, piece.edges[0].id, 0f, 1f);
            Assert.That(trim.pieceId, Is.EqualTo(piece.id));
            if (UMATrimPainting.Painter == null) Assert.Ignore("No trim painter installed.");
            Assert.That(trim.constructionName.ToLowerInvariant(), Does.Contain("zipper"));
            Assert.That(UMATrimPainting.MenuPathOf(trim.constructionId), Is.EqualTo(trim.constructionName));
        }
    }
}
#endif
