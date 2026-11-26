#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>
    /// The pattern tools added after Garment Tool's: Delete Point and Merge Points keeping seams, the knife along a path
    /// or a line, Duplicate with Seams and Sewn Round, Cut in Half and Symmetrize.
    /// </summary>
    public sealed class PatternToolsTests
    {
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private ClothesPattern Vest()
        {
            ClothesPattern pattern = VestPattern.Create();
            made.Add(pattern);
            return pattern;
        }

        private ClothesPattern Empty()
        {
            var pattern = ScriptableObject.CreateInstance<ClothesPattern>();
            made.Add(pattern);
            return pattern;
        }

        private static PatternPiece Polygon(string name, params Vector2[] points)
        {
            var piece = new PatternPiece { id = ClothesPattern.NewId(), name = name };
            foreach (Vector2 p in points)
            {
                piece.points.Add(new PatternPoint { id = ClothesPattern.NewId(), position = p });
                piece.edges.Add(new PatternEdge { id = ClothesPattern.NewId() });
            }
            return piece;
        }

        private static float Area(PatternPiece piece) => Mathf.Abs(PatternGeometry.SignedArea(PatternGeometry.SampleOutline(piece, 0.5f, null)));

        private static PatternSeam Sew(ClothesPattern pattern, PatternPiece a, int ea, PatternPiece b, int eb, bool reversed = true)
        {
            var seam = new PatternSeam { id = ClothesPattern.NewId(), a = new SeamSpan(a.id, a.edges[ea].id), b = new SeamSpan(b.id, b.edges[eb].id), reversed = reversed };
            pattern.seams.Add(seam);
            return seam;
        }

        // ---- Delete Point and Merge Points ----

        [Test]
        public void DeletePoint_UndoesASplit_TheSeamIsOneAgain()
        {
            ClothesPattern vest = Vest();
            PatternPiece front = vest.pieces[0];
            int seams = vest.seams.Count;
            string side = front.edges[1].id;
            int added = PatternEditing.SplitEdge(vest, front, 1, 0.4f);
            Assert.That(vest.seams.Count, Is.EqualTo(seams + 1), "Splitting a sewn edge cuts its seam in two.");
            Assert.That(PatternEditing.DeletePoint(vest, front, added), Is.True);
            Assert.That(vest.seams.Count, Is.EqualTo(seams), "Deleting the point joins the two seams again.");
            List<PatternSeam> on = vest.FindSeamsOn(front.id, side);
            Assert.That(on.Count, Is.EqualTo(1));
            SeamSpan mine = on[0].a.pieceId == front.id ? on[0].a : on[0].b, theirs = on[0].a.pieceId == front.id ? on[0].b : on[0].a;
            Assert.That(mine.start, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(mine.end, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(theirs.start, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(theirs.end, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(vest.FindProblems(), Is.Empty);
        }

        [Test]
        public void MergePoints_MakesAnEdgesPointsOne_TheEdgesBesideKeepTheirSeams()
        {
            ClothesPattern pattern = Empty();
            PatternPiece a = Polygon("A", new Vector2(0f, 0f), new Vector2(30f, 0f), new Vector2(30f, 20f), new Vector2(20f, 30f), new Vector2(0f, 20f));
            PatternPiece b = Polygon("B", new Vector2(40f, 0f), new Vector2(60f, 0f), new Vector2(60f, 20f), new Vector2(40f, 20f));
            pattern.pieces.Add(a);
            pattern.pieces.Add(b);
            Sew(pattern, a, 1, b, 3);
            Sew(pattern, a, 2, b, 2);
            Assert.That(PatternEditing.MergePoints(pattern, a, 2), Is.True, "The slanted top-right edge.");
            Assert.That(a.points.Count, Is.EqualTo(4));
            Assert.That(a.points.Exists(p => Vector2.Distance(p.position, new Vector2(25f, 25f)) < 1e-3f), Is.True, "One point at the edge's middle.");
            Assert.That(pattern.seams.Count, Is.EqualTo(1), "The merged edge's seam goes; the side seam stays.");
            Assert.That(a.IndexOfEdge(pattern.seams[0].a.edgeId), Is.GreaterThanOrEqualTo(0));
            Assert.That(pattern.FindProblems(), Is.Empty);
        }

        // ---- The knife ----

        [Test]
        public void CutPieceAlong_APath_CutsThroughItsPoints_EndsRunOnToTheOutline_SewnBack()
        {
            ClothesPattern pattern = Empty();
            PatternPiece piece = Polygon("Panel", new Vector2(0f, 0f), new Vector2(40f, 0f), new Vector2(40f, 60f), new Vector2(0f, 60f));
            pattern.pieces.Add(piece);
            float area = Area(piece);
            var path = new List<Vector2> { new Vector2(10f, 5f), new Vector2(15f, 30f), new Vector2(10f, 55f) };
            PatternPiece half = PatternEditing.CutPieceAlong(pattern, piece, path, false, true, out string problem);
            Assert.That(half, Is.Not.Null, problem);
            Assert.That(Area(piece) + Area(half), Is.EqualTo(area).Within(area * 0.01f));
            foreach (PatternPiece p in new[] { piece, half }) Assert.That(p.points.Exists(q => Vector2.Distance(q.position, new Vector2(15f, 30f)) < 1e-3f), Is.True, "The path's point is on both halves.");
            Assert.That(pattern.seams.Count, Is.EqualTo(2), "One seam per stretch of the cut.");
            Assert.That(pattern.seams.TrueForAll(s => s.reversed), Is.True);
            Assert.That(pattern.FindProblems(), Is.Empty);

            ClothesPattern smoothPattern = Empty();
            PatternPiece smoothPiece = Polygon("Panel", new Vector2(0f, 0f), new Vector2(40f, 0f), new Vector2(40f, 60f), new Vector2(0f, 60f));
            smoothPattern.pieces.Add(smoothPiece);
            PatternPiece smoothHalf = PatternEditing.CutPieceAlong(smoothPattern, smoothPiece, path, true, false, out problem);
            Assert.That(smoothHalf, Is.Not.Null, problem);
            Assert.That(smoothPiece.edges.FindAll(e => e.name == "Cut").TrueForAll(e => e.curved), Is.True, "Smooth: the cut's edges curve.");
            Assert.That(smoothPattern.seams, Is.Empty, "Not sewn back.");
            Assert.That(Area(smoothPiece) + Area(smoothHalf), Is.EqualTo(area).Within(area * 0.01f));
        }

        [Test]
        public void CutPieceAlong_MustCrossTheOutlineTwice()
        {
            ClothesPattern pattern = Empty();
            PatternPiece piece = Polygon("Panel", new Vector2(0f, 0f), new Vector2(40f, 0f), new Vector2(40f, 60f), new Vector2(0f, 60f));
            pattern.pieces.Add(piece);
            var zigzag = new List<Vector2> { new Vector2(-5f, 10f), new Vector2(45f, 10f), new Vector2(45f, 20f), new Vector2(-5f, 20f) };
            Assert.That(PatternEditing.CutPieceAlong(pattern, piece, zigzag, false, true, out string problem), Is.Null);
            Assert.That(problem, Does.Contain("4 times"));
            Assert.That(pattern.pieces.Count, Is.EqualTo(1));
            Assert.That(piece.points.Count, Is.EqualTo(4), "Nothing split.");
        }

        [Test]
        public void CutAlongLine_CutsAlongASeamLine_WhichGoes_RefusedWhenSomethingIsSewnOnIt()
        {
            ClothesPattern pattern = Empty();
            PatternPiece piece = Polygon("Front", new Vector2(0f, 0f), new Vector2(40f, 0f), new Vector2(40f, 60f), new Vector2(0f, 60f));
            pattern.pieces.Add(piece);
            PatternLine princess = PatternEditing.AddLine(piece, PatternLineKind.Seam, new Vector2(20f, 0f), new Vector2(20f, 60f));
            princess.points.Insert(1, new Vector2(25f, 30f));
            PatternPiece half = PatternEditing.CutAlongLine(pattern, piece, princess, true, out string problem);
            Assert.That(half, Is.Not.Null, problem);
            Assert.That(piece.lines, Is.Empty);
            Assert.That(half.lines, Is.Empty, "The line is now the cut.");
            Assert.That(pattern.seams.Count, Is.EqualTo(2));
            Assert.That(pattern.FindProblems(), Is.Empty);

            ClothesPattern other = Empty();
            PatternPiece front = Polygon("Front", new Vector2(0f, 0f), new Vector2(40f, 0f), new Vector2(40f, 60f), new Vector2(0f, 60f));
            PatternPiece pocket = Polygon("Pocket", new Vector2(50f, 0f), new Vector2(60f, 0f), new Vector2(60f, 10f), new Vector2(50f, 10f));
            other.pieces.Add(front);
            other.pieces.Add(pocket);
            PatternLine line = PatternEditing.AddLine(front, PatternLineKind.Seam, new Vector2(20f, 0f), new Vector2(20f, 60f));
            other.seams.Add(new PatternSeam { id = ClothesPattern.NewId(), a = new SeamSpan(pocket.id, pocket.edges[0].id), b = new SeamSpan(front.id, line.id, 0.2f, 0.4f) });
            Assert.That(PatternEditing.CutAlongLine(other, front, line, true, out problem), Is.Null);
            Assert.That(problem, Does.Contain("Unsew"));
            Assert.That(front.lines.Count, Is.EqualTo(1), "The line is kept.");
        }

        // ---- Duplicate with Seams and Sewn Round ----

        [Test]
        public void DuplicateWithSeams_CopiesComeOutSewnTogetherAsTheOriginals()
        {
            ClothesPattern vest = Vest();
            PatternPiece front = vest.pieces[0], back = vest.pieces[1];
            int seams = vest.seams.Count;
            PatternPiece frontCopy = PatternEditing.DuplicateWithSeams(vest, front, "Front Copy", new Vector2(120f, 0f), out int sewn, out int waiting);
            Assert.That(frontCopy.duplicateOf, Is.EqualTo(front.id));
            Assert.That(sewn, Is.EqualTo(0));
            Assert.That(waiting, Is.EqualTo(seams), "Every seam goes to the back, which has no copy yet.");
            PatternPiece backCopy = PatternEditing.DuplicateWithSeams(vest, back, "Back Copy", new Vector2(120f, 0f), out sewn, out waiting);
            Assert.That(sewn, Is.EqualTo(seams), "Now each is made between the copies.");
            Assert.That(waiting, Is.EqualTo(0));
            Assert.That(vest.seams.Count, Is.EqualTo(seams * 2));
            foreach (PatternSeam s in vest.seams)
            {
                bool copies = s.a.pieceId == frontCopy.id || s.a.pieceId == backCopy.id;
                Assert.That(copies, Is.EqualTo(s.b.pieceId == frontCopy.id || s.b.pieceId == backCopy.id), "Originals to originals, copies to copies.");
            }
            Assert.That(vest.FindProblems(), Is.Empty);
        }

        [Test]
        public void DuplicateSewnRound_IsOnTheLayerAbove_SewnWhereThePieceIsFree_OpeningsClosed()
        {
            ClothesPattern pattern = Empty();
            PatternPiece flap = Polygon("Flap", new Vector2(0f, 0f), new Vector2(16f, 0f), new Vector2(16f, 6f), new Vector2(0f, 6f));
            PatternPiece front = Polygon("Front", new Vector2(30f, 0f), new Vector2(60f, 0f), new Vector2(60f, 40f), new Vector2(30f, 40f));
            pattern.pieces.Add(flap);
            pattern.pieces.Add(front);
            flap.edges[0].opening = true;
            Sew(pattern, flap, 2, front, 0);
            PatternPiece outer = PatternEditing.DuplicateSewnRound(pattern, flap, "Flap Outer", new Vector2(0f, 10f), out int round, out int sewn, out int waiting);
            Assert.That(outer.layer, Is.EqualTo(flap.layer + 1));
            Assert.That(round, Is.EqualTo(3), "The three free edges, the opening among them; the top is sewn to the front.");
            Assert.That(waiting, Is.EqualTo(1), "The top's seam waits for a copy of the front.");
            Assert.That(flap.edges[0].opening || outer.edges[0].opening, Is.False, "Sewn round, the opening is closed.");
            foreach (PatternSeam s in pattern.seams)
            {
                if (s.a.pieceId != flap.id || s.b.pieceId != outer.id) continue;
                Assert.That(s.reversed, Is.False, "Start to start: the copy lies on the piece.");
                Assert.That(flap.IndexOfEdge(s.a.edgeId), Is.EqualTo(outer.IndexOfEdge(s.b.edgeId)));
            }
            Assert.That(pattern.FindProblems(), Is.Empty);
            Assert.That(ClothModel.FreePieces(pattern)[pattern.IndexOfPiece(outer.id)], Is.False, "A ply is free only when its original is (the flap, on the lowest layer, is not).");

            // A belt (a free piece over a dress) stays free with its second ply.
            ClothesPattern dress = Empty();
            PatternPiece skirt = Polygon("Dress", new Vector2(0f, 0f), new Vector2(40f, 0f), new Vector2(40f, 60f), new Vector2(0f, 60f));
            PatternPiece belt = Polygon("Belt", new Vector2(0f, 70f), new Vector2(40f, 70f), new Vector2(40f, 75f), new Vector2(0f, 75f));
            belt.layer = 1;
            dress.pieces.Add(skirt);
            dress.pieces.Add(belt);
            Assert.That(ClothModel.FreePieces(dress)[1], Is.True);
            PatternPiece beltOuter = PatternEditing.DuplicateSewnRound(dress, belt, "Belt Outer", new Vector2(50f, 0f), out _, out _, out _);
            bool[] free = ClothModel.FreePieces(dress);
            Assert.That(free[dress.IndexOfPiece(belt.id)] && free[dress.IndexOfPiece(beltOuter.id)], Is.True, "Both plies of the belt are free.");
        }

        // ---- Cut in Half and Symmetrize ----

        [Test]
        public void CutInHalf_KeepsOneHalfWithTheId_TheOtherALinkedMirror_SeamsMovedByPlace()
        {
            ClothesPattern vest = Vest();
            PatternPiece front = vest.pieces[0];
            string id = front.id;
            int seams = vest.seams.Count;
            PatternPiece mirror = PatternEditing.CutInHalf(vest, front, 0f, true, out string problem, out int dropped);
            Assert.That(mirror, Is.Not.Null, problem);
            Assert.That(dropped, Is.EqualTo(0));
            PatternPiece kept = vest.FindPiece(id);
            Assert.That(kept, Is.Not.Null);
            Assert.That(kept.name, Is.EqualTo("Front"));
            Assert.That(kept.Bounds().center.x, Is.LessThan(0f), "The left half is kept.");
            Assert.That(mirror.mirrorLinked && mirror.mirrorOf == id, Is.True);
            Assert.That(mirror.Bounds().xMin, Is.EqualTo(0f).Within(0.01f), "The mirror lies where the right half was.");
            Assert.That(vest.seams.Count, Is.EqualTo(seams + 1), "The back's seams move to the mirror, and the halves are sewn along the cut.");
            Assert.That(vest.seams.FindAll(s => s.a.pieceId == mirror.id || s.b.pieceId == mirror.id).Count, Is.EqualTo(3), "Side, shoulder and the middle.");
            // A linked mirror update leaves it where it is.
            Vector2 before = mirror.points[0].position;
            PatternEditing.UpdateLinkedMirrors(vest);
            Assert.That(mirror.points.Exists(p => Vector2.Distance(p.position, before) < 1e-3f), Is.True);
            Assert.That(vest.seams.Count, Is.EqualTo(seams + 1), "Its seams survive the update.");
            Assert.That(vest.FindProblems(), Is.Empty);
        }

        [Test]
        public void Symmetrize_MakesTheOtherHalfTheMirrorImage_OnePieceWithItsId_SeamsKept()
        {
            ClothesPattern pattern = Empty();
            // Lopsided: the right shoulder 1 cm higher than the left.
            PatternPiece front = Polygon("Front", new Vector2(0f, 0f), new Vector2(40f, 0f), new Vector2(40f, 46f), new Vector2(20f, 60f), new Vector2(0f, 45f));
            PatternPiece back = Polygon("Back", new Vector2(60f, 0f), new Vector2(100f, 0f), new Vector2(100f, 45f), new Vector2(60f, 45f));
            pattern.pieces.Add(front);
            pattern.pieces.Add(back);
            front.edges[0].opening = true;
            Sew(pattern, front, 1, back, 3);
            Sew(pattern, front, 4, back, 1);
            string id = front.id;
            PatternPiece result = PatternEditing.Symmetrize(pattern, front, 20f, true, out string problem, out int dropped);
            Assert.That(result, Is.Not.Null, problem);
            Assert.That(result.id, Is.EqualTo(id));
            Assert.That(pattern.pieces.Count, Is.EqualTo(2));
            Assert.That(dropped, Is.EqualTo(0));
            Assert.That(result.points.Count, Is.EqualTo(5), "The point the cut left on the straight hem is taken out again.");
            foreach (PatternPoint p in result.points)
                Assert.That(PatternEditing.MirrorPartnerPoint(result, p.position, 20f), Is.GreaterThanOrEqualTo(0), "Symmetric: " + p.position);
            Assert.That(result.points.Exists(p => Vector2.Distance(p.position, new Vector2(40f, 45f)) < 1e-3f), Is.True, "The right shoulder now mirrors the left.");
            Assert.That(pattern.seams.Count, Is.EqualTo(2), "Both side seams kept.");
            Assert.That(result.edges.Exists(e => e.opening), Is.True, "The hem is still an opening.");
            Assert.That(pattern.FindProblems(), Is.Empty);
        }
    }
}
#endif
