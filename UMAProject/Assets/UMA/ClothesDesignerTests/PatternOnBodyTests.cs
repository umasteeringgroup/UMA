#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>Darts, pleats and pieces put on the character: from points on the cloth or body to pattern data.</summary>
    public sealed class PatternOnBodyTests
    {
        private ClothesPattern vest;

        [SetUp]
        public void Make() => vest = VestPattern.Create();

        [TearDown]
        public void Clean() { if (vest != null) Object.DestroyImmediate(vest); }

        [Test]
        public void NearestEdge_FindsTheEdgeAndThePlaceOnIt()
        {
            PatternPiece piece = vest.pieces[0];
            var curve = new PatternGeometry.EdgeCurve(piece, 1, PieceScale.Identity);
            Vector2 on = curve.At(0.3f);
            Vector2 inward = Vector2.zero;
            Assert.That(PatternOnBody.NearestEdge(piece, on + inward, out int edge, out float fraction, out float distance), Is.True);
            Assert.That(edge, Is.EqualTo(1));
            Assert.That(fraction, Is.EqualTo(0.3f).Within(0.01f));
            Assert.That(distance, Is.LessThan(0.1f));
        }

        [Test]
        public void AddDartAt_PutsThePointWherePressed_AndOpensOnTheEdgeReleasedNear()
        {
            PatternPiece piece = vest.pieces[0];
            var curve = new PatternGeometry.EdgeCurve(piece, 0, PieceScale.Identity);
            Vector2 edgePoint = curve.At(0.5f);
            Vector2 tip = piece.Bounds().center;
            PatternDart dart = PatternOnBody.AddDartAt(vest, 0, tip, edgePoint, false, out string message);
            Assert.That(dart, Is.Not.Null, message);
            Assert.That(dart.kind, Is.EqualTo(DartKind.Edge));
            Assert.That(dart.edgeId, Is.EqualTo(piece.edges[0].id));
            Assert.That(dart.position, Is.EqualTo(0.5f).Within(0.02f));
            Assert.That((dart.tip - tip).magnitude, Is.LessThan(1e-4f));
            Assert.That(piece.darts, Does.Contain(dart));
        }

        [Test]
        public void AddDartAt_WithShift_MakesADoublePointedDartBetweenThePoints()
        {
            PatternPiece piece = vest.pieces[0];
            Vector2 c = piece.Bounds().center;
            PatternDart dart = PatternOnBody.AddDartAt(vest, 0, c + new Vector2(0f, 8f), c - new Vector2(0f, 8f), true, out string message);
            Assert.That(dart, Is.Not.Null, message);
            Assert.That(dart.kind, Is.EqualTo(DartKind.Double));
            Assert.That((dart.tip - (c + new Vector2(0f, 8f))).magnitude, Is.LessThan(1e-4f));
            Assert.That((dart.tip2 - (c - new Vector2(0f, 8f))).magnitude, Is.LessThan(1e-4f));
            Assert.That(PatternOnBody.AddDartAt(vest, 0, c, c + new Vector2(0.5f, 0f), true, out _), Is.Null, "too short");
        }

        [Test]
        public void AddPleatAt_PutsAPleatOnTheNearestEdge()
        {
            PatternPiece piece = vest.pieces[1];
            var curve = new PatternGeometry.EdgeCurve(piece, 2, PieceScale.Identity);
            PatternPleat pleat = PatternOnBody.AddPleatAt(vest, 1, curve.At(0.4f), out string message);
            Assert.That(pleat, Is.Not.Null, message);
            Assert.That(pleat.edgeId, Is.EqualTo(piece.edges[2].id));
            Assert.That(pleat.position, Is.EqualTo(0.4f).Within(0.02f));
        }

        [Test]
        public void Original_OfALinkedMirror_IsItsSourceWithThePointMirrored()
        {
            PatternPiece source = vest.pieces[0];
            PatternPiece copy = PatternEditing.MirrorCopy(source, "Mirror");
            copy.mirrorLinked = true;
            foreach (PatternPoint p in copy.points) p.position += new Vector2(80f, 3f);
            vest.pieces.Add(copy);
            int n = source.points.Count;
            for (int k = 0; k < n; k++)
            {
                PatternPiece original = PatternOnBody.Original(vest, copy, copy.points[k].position, out Vector2 onOriginal);
                Assert.That(original, Is.SameAs(source));
                Assert.That((onOriginal - source.points[(n - k) % n].position).magnitude, Is.LessThan(1e-3f), "corner " + k);
            }
        }

        private static BodyLandmarks Marks() => new BodyLandmarks { valid = true, torsoCenter = new Vector2(0f, 0f), shoulderTopY = 1.45f };

        /// <summary>Points on a 15 cm cylinder round the torso's centre, at an angle (0 front, 90 the body's left) and height.</summary>
        private static Vector3 OnTorso(float degrees, float y)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(a) * 0.15f, y, Mathf.Cos(a) * 0.15f);
        }

        [Test]
        public void UnrollOnTorso_GivesTheArcLengthAcross_TheHeightUp_AndWhereItFaces()
        {
            var points = new List<Vector3> { OnTorso(10f, 1.0f), OnTorso(50f, 1.0f), OnTorso(50f, 1.3f), OnTorso(10f, 1.3f) };
            List<Vector2> outline = PatternOnBody.UnrollOnTorso(points, Marks(), 0f, out float angle, out float top);
            Assert.That(outline.Count, Is.EqualTo(4));
            Assert.That(PatternGeometry.SignedArea(outline), Is.GreaterThan(0f), "counter-clockwise");
            Rect b = Bounds(outline);
            Assert.That(b.width, Is.EqualTo(40f * Mathf.Deg2Rad * 15f).Within(0.05f), "arc length across");
            Assert.That(b.height, Is.EqualTo(30f).Within(1e-3f));
            Assert.That(angle, Is.EqualTo(30f).Within(0.01f));
            Assert.That(top, Is.EqualTo(-15f).Within(1e-3f), "its top 15 cm below the shoulders");
        }

        [Test]
        public void UnrollOnTorso_AcrossTheBack_DoesNotJumpAtHalfATurn()
        {
            var points = new List<Vector3> { OnTorso(160f, 1.0f), OnTorso(-160f, 1.0f), OnTorso(-160f, 1.2f), OnTorso(160f, 1.2f) };
            List<Vector2> outline = PatternOnBody.UnrollOnTorso(points, Marks(), 0f, out float angle, out _);
            Assert.That(Bounds(outline).width, Is.EqualTo(40f * Mathf.Deg2Rad * 15f).Within(0.05f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(angle, 180f)), Is.LessThan(0.01f));
        }

        [Test]
        public void PieceFromBody_MakesATorsoPieceBesideTheOthers_AndRefusesACrossedOutline()
        {
            var points = new List<Vector3> { OnTorso(-20f, 1.0f), OnTorso(20f, 1.0f), OnTorso(20f, 1.3f), OnTorso(-20f, 1.3f) };
            PatternPiece piece = PatternOnBody.PieceFromBody(vest, points, Marks(), 0.01f, out string message);
            Assert.That(piece, Is.Not.Null, message);
            Assert.That(piece.points.Count, Is.EqualTo(4));
            Assert.That(piece.edges.Count, Is.EqualTo(4));
            Assert.That(piece.placement.part, Is.EqualTo(BodyPart.Torso));
            Assert.That(Mathf.Abs(piece.placement.angle), Is.LessThan(0.5f));
            Assert.That(piece.placement.topAboveShoulderCm, Is.EqualTo(-15f).Within(0.05f));
            float right = float.MinValue;
            foreach (PatternPiece p in vest.pieces) right = Mathf.Max(right, p.Bounds().xMax);
            Assert.That(piece.Bounds().xMin, Is.GreaterThan(right), "beside the other pieces on the canvas");

            var crossed = new List<Vector3> { OnTorso(-20f, 1.0f), OnTorso(20f, 1.3f), OnTorso(20f, 1.0f), OnTorso(-20f, 1.3f) };
            Assert.That(PatternOnBody.PieceFromBody(vest, crossed, Marks(), 0.01f, out string why), Is.Null);
            Assert.That(why, Does.Contain("crosses"));
        }

        private static Rect Bounds(List<Vector2> outline)
        {
            Vector2 min = outline[0], max = outline[0];
            foreach (Vector2 p in outline) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
#endif
