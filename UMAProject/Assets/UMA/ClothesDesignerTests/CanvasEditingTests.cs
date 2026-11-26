#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>The Pattern Canvas's editing aids: axis lock, snapping, mirror partners, and duplicating what is selected.</summary>
    public sealed class CanvasEditingTests
    {
        /// <summary>A bodice-like piece symmetric about x = 20: hem, right side, right shoulder to the neck point, left shoulder, left side.</summary>
        private static PatternPiece Bodice()
        {
            var piece = new PatternPiece { id = ClothesPattern.NewId(), name = "Front" };
            foreach (Vector2 p in new[] { new Vector2(0f, 0f), new Vector2(40f, 0f), new Vector2(40f, 50f), new Vector2(20f, 60f), new Vector2(0f, 50f) })
            {
                piece.points.Add(new PatternPoint { id = ClothesPattern.NewId(), position = p });
                piece.edges.Add(new PatternEdge { id = ClothesPattern.NewId() });
            }
            return piece;
        }

        [Test]
        public void AxisLock_KeepsToTheAxisChosen_OrTheOneMovedFurtherWithCtrl()
        {
            var move = new Vector2(3f, -1f);
            Assert.That(CanvasSnapping.Constrain(move, CanvasMoveAxis.Horizontal), Is.EqualTo(new Vector2(3f, 0f)));
            Assert.That(CanvasSnapping.Constrain(move, CanvasMoveAxis.Vertical), Is.EqualTo(new Vector2(0f, -1f)));
            Assert.That(CanvasSnapping.Constrain(move, CanvasMoveAxis.Free), Is.EqualTo(move));
            Assert.That(CanvasSnapping.AxisFor(move, CanvasMoveAxis.Free, true), Is.EqualTo(CanvasMoveAxis.Horizontal), "Ctrl: the way it moved further.");
            Assert.That(CanvasSnapping.AxisFor(new Vector2(0.5f, 4f), CanvasMoveAxis.Free, true), Is.EqualTo(CanvasMoveAxis.Vertical));
            Assert.That(CanvasSnapping.AxisFor(move, CanvasMoveAxis.Free, false), Is.EqualTo(CanvasMoveAxis.Free));
            Assert.That(CanvasSnapping.AxisFor(move, CanvasMoveAxis.Vertical, true), Is.EqualTo(CanvasMoveAxis.Vertical), "A chosen axis wins.");
        }

        [Test]
        public void Snap_LinesAMovingPointUpWithTheNearestTargets_AcrossAndInHeight_WithGuides()
        {
            var moving = new List<Vector2> { new Vector2(0f, 0f), new Vector2(5f, 0f) };
            var targets = new List<Vector2> { new Vector2(10.48f, 50f), new Vector2(40f, 5.2f), new Vector2(15.45f, 80f) };
            var guides = new List<CanvasSnapGuide>();
            Vector2 snapped = CanvasSnapping.Snap(new Vector2(10f, 5f), moving, targets, 0.5f, CanvasMoveAxis.Free, guides);
            // Across: the second moving point (15) is 0.45 from 15.45, nearer than the first (10) is to 10.48; in height 5 to 5.2.
            Assert.That(snapped.x, Is.EqualTo(10.45f).Within(1e-4f));
            Assert.That(snapped.y, Is.EqualTo(5.2f).Within(1e-4f));
            Assert.That(guides.Count, Is.EqualTo(2));
            Assert.That(guides.Exists(g => g.vertical && Mathf.Abs(g.moving.x - 15.45f) < 1e-4f && g.target == targets[2]), Is.True, "A vertical guide where it lined up across.");
            Assert.That(guides.Exists(g => !g.vertical && Mathf.Abs(g.moving.y - 5.2f) < 1e-4f && g.target == targets[1]), Is.True);

            Vector2 across = CanvasSnapping.Snap(new Vector2(10f, 0f), moving, targets, 0.5f, CanvasMoveAxis.Horizontal, guides);
            Assert.That(across.y, Is.EqualTo(0f), "Kept to across: never snapped up or down.");
            Assert.That(guides.TrueForAll(g => g.vertical), Is.True);
            Assert.That(CanvasSnapping.Snap(new Vector2(20f, 20f), moving, targets, 0.5f, CanvasMoveAxis.Free, guides), Is.EqualTo(new Vector2(20f, 20f)), "Out of reach: unchanged.");
            Assert.That(guides, Is.Empty);
        }

        [Test]
        public void MirrorPartners_PairPointsAndEdgesAcrossThePiecesMiddle()
        {
            PatternPiece piece = Bodice();
            float axis = piece.Bounds().center.x;
            Assert.That(axis, Is.EqualTo(20f));
            Assert.That(PatternEditing.MirrorPartnerPoint(piece, piece.points[0].position, axis), Is.EqualTo(1));
            Assert.That(PatternEditing.MirrorPartnerPoint(piece, piece.points[2].position, axis), Is.EqualTo(4));
            Assert.That(PatternEditing.MirrorPartnerPoint(piece, piece.points[3].position, axis), Is.EqualTo(3), "The neck point is on the middle: its own partner.");
            Assert.That(PatternEditing.MirrorPartnerPoint(piece, piece.points[2].position + new Vector2(0.3f, 0f), axis), Is.EqualTo(4), "Within half a centimetre.");
            Assert.That(PatternEditing.MirrorPartnerPoint(piece, new Vector2(30f, 20f), axis), Is.EqualTo(-1));
            Assert.That(PatternEditing.MirrorEdge(piece, 1, axis), Is.EqualTo(4), "The right side mirrors the left, which runs the other way.");
            Assert.That(PatternEditing.MirrorEdge(piece, 2, axis), Is.EqualTo(3), "Right shoulder, left shoulder.");
            Assert.That(PatternEditing.MirrorEdge(piece, 0, axis), Is.EqualTo(0), "The hem mirrors itself.");
        }

        [Test]
        public void DuplicateDart_BesideIt_OrMirroredAcrossThePiecesMiddle()
        {
            PatternPiece piece = Bodice();
            PatternDart diamond = PatternEditing.AddDoubleDart(piece, new Vector2(12f, 25f));
            diamond.widthCm = 3f;
            Vector2 tip = diamond.tip, tip2 = diamond.tip2;
            PatternDart beside = PatternEditing.DuplicateDart(piece, diamond, null);
            Assert.That(beside.id, Is.Not.EqualTo(diamond.id));
            Assert.That(beside.tip, Is.EqualTo(tip + new Vector2(5f, 0f)), "Its width and 2 cm across.");
            Assert.That(beside.tip2, Is.EqualTo(tip2 + new Vector2(5f, 0f)));
            PatternDart mirrored = PatternEditing.DuplicateDart(piece, diamond, 20f);
            Assert.That(mirrored.tip, Is.EqualTo(new Vector2(40f - tip.x, tip.y)));
            Assert.That(piece.darts.Count, Is.EqualTo(3));

            PatternDart side = PatternEditing.AddEdgeDart(piece, 1, 0.3f);
            PatternDart along = PatternEditing.DuplicateDart(piece, side, null);
            Assert.That(along.edgeId, Is.EqualTo(side.edgeId));
            Assert.That(along.position, Is.GreaterThan(side.position), "Further along its edge.");
            PatternDart across = PatternEditing.DuplicateDart(piece, side, 20f);
            Assert.That(across.edgeId, Is.EqualTo(piece.edges[4].id), "On the mirror edge.");
            Assert.That(across.position, Is.EqualTo(1f - side.position).Within(1e-5f));
            Assert.That(across.tip, Is.EqualTo(PatternEditing.Mirror(side.tip, 20f)));
        }

        [Test]
        public void AddDoubleDartPair_AddsTwoMirroredDartsInsideThePiece_HalfwayToTheSides()
        {
            PatternPiece piece = Bodice();
            PatternDart[] pair = PatternEditing.AddDoubleDartPair(piece, 20f);
            Assert.That(pair, Is.Not.Null);
            Assert.That(piece.darts.Count, Is.EqualTo(2));
            Assert.That(pair[0].kind, Is.EqualTo(DartKind.Double));
            Assert.That(pair[0].tip.x, Is.EqualTo(10f).Within(1e-4f), "Halfway between the middle (20) and the side (0).");
            Assert.That(pair[1].tip, Is.EqualTo(PatternEditing.Mirror(pair[0].tip, 20f)));
            Assert.That(pair[1].tip2, Is.EqualTo(PatternEditing.Mirror(pair[0].tip2, 20f)));
            Assert.That(pair[1].widthCm, Is.EqualTo(pair[0].widthCm));
            foreach (PatternDart d in pair)
            {
                Assert.That(LineGeometry.Inside(piece, d.tip) && LineGeometry.Inside(piece, d.tip2), Is.True, "Both points inside the piece.");
                Assert.That(Vector2.Distance(d.tip, d.tip2), Is.GreaterThan(5f));
            }
            Assert.That(PatternEditing.AddDoubleDartPair(piece, 0.5f), Is.Null, "No room on one side of the middle: nothing added.");
            Assert.That(piece.darts.Count, Is.EqualTo(2));
        }

        [Test]
        public void Duplicate_OfALineOrPartOnTheMiddle_WithMirrorX_GoesBesideIt_NotOnTopOfItself()
        {
            PatternPiece piece = Bodice();
            var pattern = ScriptableObject.CreateInstance<ClothesPattern>();
            try
            {
                pattern.pieces.Add(piece);
                PatternLine centre = PatternEditing.AddLine(piece, PatternLineKind.Seam, new Vector2(20f, 10f), new Vector2(20f, 40f));
                PatternLine copy = PatternEditing.DuplicateLine(piece, centre, 20f);
                Assert.That(copy.points[0], Is.EqualTo(new Vector2(22f, 10f)));
                PatternLine across = PatternEditing.AddLine(piece, PatternLineKind.Seam, new Vector2(10f, 20f), new Vector2(30f, 20f));
                Assert.That(PatternEditing.DuplicateLine(piece, across, 20f).points[0], Is.EqualTo(new Vector2(12f, 20f)), "A line across the middle mirrors itself too.");
                var part = new PatternPart { id = ClothesPattern.NewId(), pieceId = piece.id, position = new Vector2(20.2f, 40f) };
                pattern.parts.Add(part);
                Assert.That(PatternEditing.DuplicatePart(pattern, part, 20f).position, Is.EqualTo(new Vector2(22.2f, 40f)));
            }
            finally { Object.DestroyImmediate(pattern); }
        }

        [Test]
        public void DuplicatePleatLineAndPart_BesideOrMirrored()
        {
            PatternPiece piece = Bodice();
            var pattern = ScriptableObject.CreateInstance<ClothesPattern>();
            try
            {
                pattern.pieces.Add(piece);
                PatternPleat pleat = PatternEditing.AddPleat(piece, 2, 0.4f);
                pleat.kind = PleatKind.KnifeLeft;
                PatternPleat mirrored = PatternEditing.DuplicatePleat(piece, pleat, 20f);
                Assert.That(mirrored.edgeId, Is.EqualTo(piece.edges[3].id));
                Assert.That(mirrored.position, Is.EqualTo(0.6f).Within(1e-5f));
                Assert.That(mirrored.kind, Is.EqualTo(PleatKind.KnifeRight), "Its fold turned the other way.");
                Assert.That(PatternEditing.DuplicatePleat(piece, pleat, null).position, Is.GreaterThan(pleat.position));

                PatternLine line = PatternEditing.AddLine(piece, PatternLineKind.Seam, new Vector2(5f, 10f), new Vector2(10f, 30f));
                PatternLine copy = PatternEditing.DuplicateLine(piece, line, null);
                Assert.That(copy.id, Is.Not.EqualTo(line.id));
                Assert.That(copy.points[0], Is.EqualTo(new Vector2(7f, 10f)));
                Assert.That(PatternEditing.DuplicateLine(piece, line, 20f).points[1], Is.EqualTo(new Vector2(30f, 30f)));

                var part = new PatternPart { id = ClothesPattern.NewId(), pieceId = piece.id, position = new Vector2(15f, 40f) };
                pattern.parts.Add(part);
                Assert.That(PatternEditing.DuplicatePart(pattern, part, 20f).position, Is.EqualTo(new Vector2(25f, 40f)));
                Assert.That(pattern.parts.Count, Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(pattern); }
        }
    }
}
#endif
