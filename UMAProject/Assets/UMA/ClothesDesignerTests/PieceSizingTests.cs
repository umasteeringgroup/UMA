#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>The Size tool: resizing a piece as cut on the body, through its outline or the size that sets it.</summary>
    public sealed class PieceSizingTests
    {
        private readonly List<Object> made = new List<Object>();
        private readonly List<FitSession> sessions = new List<FitSession>();

        [TearDown]
        public void Clean()
        {
            foreach (FitSession s in sessions) s.Dispose();
            sessions.Clear();
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private ClothesPattern NewPattern(params PatternPiece[] pieces)
        {
            var pattern = ScriptableObject.CreateInstance<ClothesPattern>();
            made.Add(pattern);
            pattern.pieces.AddRange(pieces);
            return pattern;
        }

        private ClothesFit NewFit(ClothesPattern pattern)
        {
            var fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(fit);
            fit.pattern = pattern;
            return fit;
        }

        private static PatternDimension Width(string name, BodyMeasurement measurement, float fixedCm, float allowanceCm, params PatternPiece[] pieces)
        {
            var size = new PatternDimension { id = ClothesPattern.NewId(), name = name, kind = DimensionKind.Width, measurement = measurement, fixedCm = fixedCm, allowanceCm = allowanceCm, shareCount = pieces.Length, lineY = 30f, pieceIds = new List<string>() };
            foreach (PatternPiece p in pieces) size.pieceIds.Add(p.id);
            return size;
        }

        [Test]
        public void UnsizedPiece_IsScaledAboutItsMiddleAndTop_WithWhatIsDrawnOnIt()
        {
            PatternPiece belt = PatternEditing.NewRectanglePiece("Belt", new Rect(10f, 50f, 60f, 6f));
            PatternPiece dress = PatternEditing.NewRectanglePiece("Dress", new Rect(0f, 0f, 60f, 80f));
            ClothesPattern pattern = NewPattern(belt, dress);
            PatternEditing.AddLine(belt, PatternLineKind.Seam, new Vector2(10f, 53f), new Vector2(70f, 53f));
            pattern.tacks.Add(new PatternTack { id = ClothesPattern.NewId(), pieceA = belt.id, a = new Vector2(70f, 56f), pieceB = dress.id, b = new Vector2(55f, 40f) });
            pattern.parts.Add(new PatternPart { id = ClothesPattern.NewId(), pieceId = belt.id, position = new Vector2(40f, 50f) });
            ClothesFit fit = NewFit(pattern);
            fit.pins.Add(new ClothPin { pieceId = belt.id, flatPosition = new Vector2(10f, 56f) });
            fit.paint.Add(new FitPaintPoint { pieceId = belt.id, flatPosition = new Vector2(25f, 53f), hug = 1f });

            PieceSizing.Result result = PieceSizing.Resize(pattern, fit, null, 0, 90f, 9f);

            Assert.That(result.changed, Is.True, result.message);
            Assert.That(result.sizes, Is.Empty);
            Rect b = belt.Bounds();
            Assert.That(b.width, Is.EqualTo(90f).Within(1e-3f));
            Assert.That(b.height, Is.EqualTo(9f).Within(1e-3f));
            Assert.That(b.center.x, Is.EqualTo(40f).Within(1e-3f), "Scaled about its middle across.");
            Assert.That(b.yMax, Is.EqualTo(56f).Within(1e-3f), "Its top stays: that is where it is laid on the body.");
            // What is drawn on it keeps its place on the piece: x from the middle times 1.5, y from the top times 1.5.
            Assert.That(belt.lines[0].points[0], Is.EqualTo(new Vector2(-5f, 51.5f)).Using(Vector2EqualityComparer(1e-3f)));
            Assert.That(pattern.tacks[0].a, Is.EqualTo(new Vector2(85f, 56f)).Using(Vector2EqualityComparer(1e-3f)));
            Assert.That(pattern.tacks[0].b, Is.EqualTo(new Vector2(55f, 40f)), "The other piece's end of a tack does not move.");
            Assert.That(pattern.parts[0].position, Is.EqualTo(new Vector2(40f, 47f)).Using(Vector2EqualityComparer(1e-3f)));
            Assert.That(fit.pins[0].flatPosition, Is.EqualTo(new Vector2(-5f, 56f)).Using(Vector2EqualityComparer(1e-3f)));
            Assert.That(fit.paint[0].flatPosition, Is.EqualTo(new Vector2(17.5f, 51.5f)).Using(Vector2EqualityComparer(1e-3f)));
            Assert.That(dress.Bounds(), Is.EqualTo(new Rect(0f, 0f, 60f, 80f)), "Other pieces are left alone.");
            Assert.That(result.rescaled.Keys, Is.EquivalentTo(new[] { 0 }));
            // Before() finds where a point of the piece as drawn now was drawn before, for carrying the drape over.
            Assert.That(result.Before(0, new Vector2(-5f, 47f)), Is.EqualTo(new Vector2(10f, 50f)).Using(Vector2EqualityComparer(1e-3f)));
            Assert.That(result.Before(1, new Vector2(3f, 4f)), Is.EqualTo(new Vector2(3f, 4f)));
            StringAssert.Contains("now 90.0 cm wide and 9.0 cm long", result.message);
        }

        [Test]
        public void PieceASizeSets_ChangesThatSize_NotTheOutline_AndThePiecesItSizesFollow()
        {
            PatternPiece front = PatternEditing.NewRectanglePiece("Front", new Rect(0f, 0f, 50f, 60f));
            PatternPiece back = PatternEditing.NewRectanglePiece("Back", new Rect(60f, 0f, 50f, 60f));
            ClothesPattern pattern = NewPattern(front, back);
            ClothesFit fit = NewFit(pattern);

            // A fixed length: the length itself changes.
            PatternDimension fixedChest = Width("Chest", BodyMeasurement.None, 100f, 0f, front, back);
            pattern.dimensions.Add(fixedChest);
            Assert.That(PieceSizing.CutSize(pattern, fit, null, 0).x, Is.EqualTo(50f).Within(1e-3f));
            PieceSizing.Result result = PieceSizing.Resize(pattern, fit, null, 0, 60f, 60f);
            Assert.That(result.changed, Is.True, result.message);
            Assert.That(fixedChest.fixedCm, Is.EqualTo(120f).Within(1e-3f));
            Assert.That(result.rescaled, Is.Empty, "The outline is not scaled: the size would only size it back.");
            Assert.That(front.Bounds().width, Is.EqualTo(50f).Within(1e-3f));
            Assert.That(PieceSizing.CutSize(pattern, fit, null, 0).x, Is.EqualTo(60f).Within(0.01f));
            Assert.That(PieceSizing.CutSize(pattern, fit, null, 1).x, Is.EqualTo(60f).Within(0.01f), "The back shares the size.");
            StringAssert.Contains("'Chest'", result.message);
            StringAssert.Contains("Back changed with it", result.message);

            // From the body: the allowance changes. 90 cm chest + 10 = 100, 50 a piece; 60 a piece is 120, so 30 of allowance.
            pattern.dimensions.Clear();
            PatternDimension chest = Width("Chest", BodyMeasurement.Chest, 0f, 10f, front, back);
            pattern.dimensions.Add(chest);
            var body = new BodyLandmarks { valid = true, chestCm = 90f };
            result = PieceSizing.Resize(pattern, fit, body, 1, 60f, 60f);
            Assert.That(result.changed, Is.True, result.message);
            Assert.That(chest.allowanceCm, Is.EqualTo(30f).Within(0.05f));
            Assert.That(PieceSizing.CutSize(pattern, fit, body, 0).x, Is.EqualTo(60f).Within(0.05f));

            // A fit with its own allowance for the size: that one changes, the pattern's is left alone.
            fit.allowances.Add(new AllowanceOverride { dimensionId = chest.id, allowanceCm = 30f });
            result = PieceSizing.Resize(pattern, fit, body, 0, 55f, 60f);
            Assert.That(result.changed, Is.True, result.message);
            Assert.That(fit.allowances[0].allowanceCm, Is.EqualTo(20f).Within(0.05f));
            Assert.That(chest.allowanceCm, Is.EqualTo(30f).Within(1e-3f));
            StringAssert.Contains("this fit's allowance", result.message);

            // Its length is not set by a size, so a length change scales the outline: alone when asked...
            result = PieceSizing.Resize(pattern, fit, body, 0, 55f, 66f, false);
            Assert.That(result.changed, Is.True, result.message);
            Assert.That(front.Bounds().height, Is.EqualTo(66f).Within(1e-3f));
            Assert.That(back.Bounds().height, Is.EqualTo(60f).Within(1e-3f));
            // ...and otherwise with the back, which shares the Chest size with it.
            Assert.That(PieceSizing.MatchingPieces(pattern, fit, body, 0, DimensionKind.Length), Is.EqualTo(new[] { 1 }));
            Assert.That(PieceSizing.MatchingPieces(pattern, fit, body, 0, DimensionKind.Width), Is.Empty, "The Chest size sets their widths.");
            result = PieceSizing.Resize(pattern, fit, body, 0, 55f, 72f);
            Assert.That(front.Bounds().height, Is.EqualTo(72f).Within(1e-3f));
            Assert.That(back.Bounds().height, Is.EqualTo(60f * 72f / 66f).Within(1e-3f), "The back grows by the same factor.");
            StringAssert.Contains("'Back' shares a size with it", result.message);
        }

        [Test]
        public void MatchingPieces_AreResizedTogether_SoTheirSeamsStillMatch()
        {
            PatternPiece front = PatternEditing.NewRectanglePiece("Front Belt", new Rect(0f, 0f, 33f, 6f));
            PatternPiece back = PatternEditing.NewRectanglePiece("Back Belt", new Rect(40f, 0f, 33f, 6f));
            PatternPiece dress = PatternEditing.NewRectanglePiece("Dress", new Rect(0f, 20f, 60f, 80f));
            ClothesPattern pattern = NewPattern(front, back, dress);
            pattern.dimensions.Add(Width("Waist", BodyMeasurement.None, 66f, 0f, front, back));
            pattern.dimensions[0].lineY = 3f;
            // Joined at both sides, like the sample Belted Dress's belt.
            Assert.That(PatternEditing.Sew(pattern, front, 1, back, 3, out string problem), Is.Not.Null, problem);
            Assert.That(PatternEditing.Sew(pattern, back, 1, front, 3, out problem), Is.Not.Null, problem);
            ClothesFit fit = NewFit(pattern);

            PieceSizing.Result together = PieceSizing.Resize(pattern, fit, null, 0, 33f, 9f);
            Assert.That(together.changed, Is.True, together.message);
            Assert.That(front.Bounds().height, Is.EqualTo(9f).Within(1e-3f));
            Assert.That(back.Bounds().height, Is.EqualTo(9f).Within(1e-3f));
            Assert.That(dress.Bounds().height, Is.EqualTo(80f), "A piece that shares no size is left alone.");
            Assert.That(together.rescaled.Keys, Is.EquivalentTo(new[] { 0, 1 }));
            StringAssert.Contains("'Back Belt' shares a size with it", together.message);
            StringAssert.DoesNotContain("differ in length", together.message);

            PieceSizing.Result alone = PieceSizing.Resize(pattern, fit, null, 0, 33f, 12f, false);
            Assert.That(front.Bounds().height, Is.EqualTo(12f).Within(1e-3f));
            Assert.That(back.Bounds().height, Is.EqualTo(9f).Within(1e-3f));
            StringAssert.Contains("Front Belt to Back Belt (3.0 cm, 2 seams)", alone.message);
        }

        [Test]
        public void SizeWhoseWidthLineMissesThePiece_IsLeftAlone_AndTheOutlineIsScaled()
        {
            PatternPiece front = PatternEditing.NewRectanglePiece("Front", new Rect(0f, 0f, 50f, 20f));
            ClothesPattern pattern = NewPattern(front);
            PatternDimension chest = Width("Chest", BodyMeasurement.None, 50f, 0f, front);
            chest.lineY = 40f; // above the piece
            pattern.dimensions.Add(chest);
            PieceSizing.Result result = PieceSizing.Resize(pattern, NewFit(pattern), null, 0, 75f, 20f);
            Assert.That(result.changed, Is.True, result.message);
            Assert.That(chest.fixedCm, Is.EqualTo(50f), "A size that does not set the piece is put back.");
            Assert.That(front.Bounds().width, Is.EqualTo(75f).Within(1e-3f));
        }

        [Test]
        public void LinkedMirror_ResizesItsOriginal_AndTheMirrorFollows()
        {
            PatternPiece sleeve = PatternEditing.NewRectanglePiece("Sleeve", new Rect(0f, 0f, 30f, 20f));
            PatternPiece mirror = PatternEditing.MirrorCopy(sleeve, "Sleeve Mirrored");
            mirror.mirrorLinked = true;
            foreach (PatternPoint p in mirror.points) p.position += new Vector2(40f, 0f);
            ClothesPattern pattern = NewPattern(sleeve, mirror);
            PieceSizing.Result result = PieceSizing.Resize(pattern, NewFit(pattern), null, 1, 45f, 20f);
            Assert.That(result.changed, Is.True, result.message);
            Assert.That(sleeve.Bounds().width, Is.EqualTo(45f).Within(1e-3f));
            Assert.That(mirror.Bounds().width, Is.EqualTo(45f).Within(1e-3f));
            Assert.That(result.rescaled.Keys, Is.EquivalentTo(new[] { 0, 1 }));
            StringAssert.Contains("linked mirror of 'Sleeve'", result.message);
        }

        [Test]
        public void Resize_ThatMakesASeamUneven_SaysWhich()
        {
            PatternPiece front = PatternEditing.NewRectanglePiece("Front", new Rect(0f, 0f, 40f, 60f));
            PatternPiece back = PatternEditing.NewRectanglePiece("Back", new Rect(50f, 0f, 40f, 60f));
            ClothesPattern pattern = NewPattern(front, back);
            // Rectangles run bottom, right, top, left: the front's right side to the back's left side.
            Assert.That(PatternEditing.Sew(pattern, front, 1, back, 3, out string problem), Is.Not.Null, problem);
            PieceSizing.Result result = PieceSizing.Resize(pattern, NewFit(pattern), null, 0, 40f, 90f);
            Assert.That(result.changed, Is.True, result.message);
            StringAssert.Contains("differ in length: Front to Back (30.0 cm)", result.message);
        }

        [Test]
        public void Resize_KeepsWithinItsLimits_AndSaysWhenNothingChanges()
        {
            PatternPiece piece = PatternEditing.NewRectanglePiece("Patch", new Rect(0f, 0f, 10f, 10f));
            ClothesPattern pattern = NewPattern(piece);
            ClothesFit fit = NewFit(pattern);
            PieceSizing.Result same = PieceSizing.Resize(pattern, fit, null, 0, 10f, 10f);
            Assert.That(same.changed, Is.False);
            StringAssert.Contains("already 10.0 x 10.0 cm", same.message);
            PieceSizing.Result huge = PieceSizing.Resize(pattern, fit, null, 0, 1000f, 10f);
            Assert.That(huge.changed, Is.True);
            Assert.That(piece.Bounds().width, Is.EqualTo(10f * PieceSizing.MaxFactor).Within(1e-3f));
            Assert.That(PieceSizing.Resize(pattern, fit, null, 5, 10f, 10f).changed, Is.False, "No such piece.");
        }

        [Test]
        public void ScaledPieceGeometry_ScalesDartsAlongTheirEdge_AndPleatsAlongAndInto()
        {
            PatternPiece piece = PatternEditing.NewRectanglePiece("Skirt", new Rect(0f, 0f, 40f, 60f));
            PatternDart dart = PatternEditing.AddEdgeDart(piece, 2, 0.5f); // the top edge, horizontal
            PatternPleat pleat = PatternEditing.AddPleat(piece, 0, 0.5f); // the bottom edge, horizontal
            pleat.lengthCm = 10f; pleat.stitchedCm = 4f;
            float width = dart.widthCm, depth = pleat.depthCm;
            PieceSizing.ScalePieceGeometry(piece, 1.5f, 2f);
            Assert.That(dart.widthCm, Is.EqualTo(width * 1.5f).Within(1e-3f), "A dart on a horizontal edge takes the width's scale.");
            Assert.That(pleat.depthCm, Is.EqualTo(depth * 1.5f).Within(1e-3f));
            Assert.That(pleat.lengthCm, Is.EqualTo(20f).Within(1e-3f), "Its folds run into the piece, so they take the length's scale.");
            Assert.That(pleat.stitchedCm, Is.EqualTo(8f).Within(1e-3f));
        }

        [Test]
        public void Carry_LooksUpAResizedPiecesPointsWhereTheyWereDrawnBefore()
        {
            PatternPiece piece = PatternEditing.NewRectanglePiece("Panel", new Rect(0f, 0f, 30f, 40f));
            ClothesPattern pattern = NewPattern(piece);
            PatternMesh before = PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), 3f);
            // A drape that is a plane in the piece's own coordinates, so sampling it anywhere is exact.
            var drape = new Vector3[before.VertexCount];
            for (int v = 0; v < drape.Length; v++) drape[v] = new Vector3(before.drawn[v].x * 0.01f, 1f + before.drawn[v].y * 0.02f, 0.3f);
            PieceSizing.Result result = PieceSizing.Resize(pattern, NewFit(pattern), null, 0, 45f, 50f);
            PatternMesh after = PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), 3f);
            Vector3[] carried = new PatternMeshSampler(before).Carry(drape, after, result.Before);
            Vector3[] plain = new PatternMeshSampler(before).Carry(drape, after);
            float worst = 0f, plainWorst = 0f;
            for (int v = 0; v < after.VertexCount; v++)
            {
                Vector2 was = result.Before(0, after.drawn[v]);
                var expected = new Vector3(was.x * 0.01f, 1f + was.y * 0.02f, 0.3f);
                worst = Mathf.Max(worst, (carried[v] - expected).magnitude);
                plainWorst = Mathf.Max(plainWorst, (plain[v] - expected).magnitude);
            }
            Assert.That(worst, Is.LessThan(1e-4f), "Every point of the larger piece starts where the same point of the piece was.");
            Assert.That(plainWorst, Is.GreaterThan(0.01f), "Without the lookup the corners would be taken from the wrong place.");
        }

        [Test]
        public void SizeAfterDrag_ReadsTheMoveAlongThePiece_WiderAcrossLongerDown()
        {
            var from = new Vector2(50f, 60f);
            // 8 points a centimetre: across the piece to the right, up the piece up the screen (screen y runs down).
            Vector2 across = new Vector2(8f, 0f), along = new Vector2(0f, -8f);
            PieceScale one = PieceScale.Identity;
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(16f, 2f), across, along, 1f, one, false), Is.EqualTo(new Vector2(54f, 60f)), "2 cm out on the right: both sides move 2 cm.");
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(-16f, 0f), across, along, -1f, one, false), Is.EqualTo(new Vector2(54f, 60f)), "Out on the left widens it too.");
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(-16f, 0f), across, along, 1f, one, false), Is.EqualTo(new Vector2(46f, 60f)), "In from the right narrows it.");
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(3f, 24f), across, along, 1f, one, false), Is.EqualTo(new Vector2(50f, 63f)), "3 cm down: the hem moves 3 cm.");
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(16f, 0f), across, along, 1f, one, true), Is.EqualTo(new Vector2(54f, 64.8f)).Using(Vector2EqualityComparer(1e-3f)), "Shift keeps the proportions.");
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(0.8f, 0f), across, along, 1f, one, false), Is.EqualTo(from), "A tremble changes nothing.");
            // A piece turned on screen (a sleeve along a raised arm): the move is read along the piece, not the screen.
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(0f, -16f), new Vector2(0f, -8f), new Vector2(8f, 0f), 1f, one, false), Is.EqualTo(new Vector2(54f, 60f)));
            // A piece cut twice as wide as drawn: a centimetre as drawn is two as cut.
            var doubled = new PieceScale { sx = 2f, sy = 1f };
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(16f, 0f), across, along, 1f, doubled, false), Is.EqualTo(new Vector2(58f, 60f)));
            // Seen edge on: right widens and down lengthens, 10 points a centimetre.
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(20f, 0f), Vector2.zero, along, 1f, one, false), Is.EqualTo(new Vector2(54f, 60f)));
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(0f, 30f), Vector2.zero, along, -1f, one, false), Is.EqualTo(new Vector2(50f, 63f)));
            // Never smaller than a quarter.
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(-4000f, 0f), across, along, 1f, one, false).x, Is.EqualTo(50f / PieceSizing.MaxFactor).Within(1e-3f));
        }

        [Test]
        public void ClothAxes_GiveTheClothsMovePerCentimetreAsDrawn()
        {
            // Drawn 10 cm across and up; on the cloth 20 cm across (x) and 10 cm deep (z).
            Assert.That(DesignerSession.ClothAxes(Vector3.zero, new Vector3(0.2f, 0f, 0f), new Vector3(0f, 0f, 0.1f), Vector2.zero, new Vector2(10f, 0f), new Vector2(0f, 10f), out Vector3 x, out Vector3 y), Is.True);
            Assert.That((x - new Vector3(0.02f, 0f, 0f)).magnitude, Is.LessThan(1e-6f));
            Assert.That((y - new Vector3(0f, 0f, 0.01f)).magnitude, Is.LessThan(1e-6f));
            Assert.That(DesignerSession.ClothAxes(Vector3.zero, Vector3.right, Vector3.up, Vector2.zero, new Vector2(1f, 0f), new Vector2(2f, 0f), out _, out _), Is.False, "A triangle with no area as drawn.");
        }

        [Test]
        public void SizeAfterDrag_FromAHandle_ChangesOnlyItsWay()
        {
            var from = new Vector2(50f, 60f);
            Vector2 across = new Vector2(8f, 0f), along = new Vector2(0f, -8f);
            PieceScale one = PieceScale.Identity;
            // A side handle dragged mostly down still only widens: 1 cm out, 4 down.
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(8f, 32f), across, along, 1f, one, false, SizeAxis.Across), Is.EqualTo(new Vector2(52f, 60f)));
            // The left side's handle: out is to the left.
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(-8f, 0f), across, along, -1f, one, false, SizeAxis.Across), Is.EqualTo(new Vector2(52f, 60f)));
            // The hem handle dragged mostly sideways still only lengthens: 2 cm down.
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(40f, 16f), across, along, 1f, one, false, SizeAxis.Down), Is.EqualTo(new Vector2(50f, 62f)));
            Assert.That(DesignerSession.SizeAfterDrag(from, new Vector2(0f, 48f), across, along, 1f, one, true, SizeAxis.Down), Is.EqualTo(new Vector2(55f, 66f)).Using(Vector2EqualityComparer(1e-3f)), "Shift on the hem handle scales both by the length's factor.");
        }

        [Test]
        public void SizeHandles_SitAtTheMiddleOfEachSideAndOfTheHem()
        {
            PatternPiece piece = PatternEditing.NewRectanglePiece("Panel", new Rect(0f, 0f, 40f, 60f));
            ClothesPattern pattern = NewPattern(piece);
            PatternMesh mesh = PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), 2f);
            int[] handles = DesignerSession.SizeHandleVertices(mesh, 0);
            Assert.That((mesh.drawn[handles[0]] - new Vector2(40f, 30f)).magnitude, Is.LessThan(1.5f), "Right side.");
            Assert.That((mesh.drawn[handles[1]] - new Vector2(0f, 30f)).magnitude, Is.LessThan(1.5f), "Left side.");
            Assert.That((mesh.drawn[handles[2]] - new Vector2(20f, 0f)).magnitude, Is.LessThan(1.5f), "Hem.");
            Assert.That(DesignerSession.SizeHandleVertices(mesh, 3), Is.EqualTo(new[] { -1, -1, -1 }), "No such piece.");
        }

        [Test]
        public void Preview_GivesEachPiecesFactor_AndChangesNothing()
        {
            PatternPiece front = PatternEditing.NewRectanglePiece("Front", new Rect(0f, 0f, 50f, 60f));
            PatternPiece back = PatternEditing.NewRectanglePiece("Back", new Rect(60f, 0f, 50f, 60f));
            PatternPiece pocket = PatternEditing.NewRectanglePiece("Pocket", new Rect(0f, 80f, 12f, 12f));
            ClothesPattern pattern = NewPattern(front, back, pocket);
            PatternDimension chest = Width("Chest", BodyMeasurement.None, 100f, 0f, front, back);
            pattern.dimensions.Add(chest);
            ClothesFit fit = NewFit(pattern);
            string before = JsonUtility.ToJson(pattern) + JsonUtility.ToJson(fit);
            int copiesBefore = Resources.FindObjectsOfTypeAll<ClothesPattern>().Length;

            Vector2[] wider = PieceSizing.Preview(pattern, fit, null, 0, 60f, 60f, true, out string message);
            Assert.That(wider[0], Is.EqualTo(new Vector2(1.2f, 1f)).Using(Vector2EqualityComparer(1e-3f)));
            Assert.That(wider[1], Is.EqualTo(new Vector2(1.2f, 1f)).Using(Vector2EqualityComparer(1e-3f)), "The back shares the Chest size.");
            Assert.That(wider[2], Is.EqualTo(Vector2.one), "Exactly one for a piece that does not change.");
            StringAssert.Contains("'Chest'", message);
            Vector2[] longer = PieceSizing.Preview(pattern, fit, null, 0, 50f, 72f, true, out _);
            Assert.That(longer[1], Is.EqualTo(new Vector2(1f, 1.2f)).Using(Vector2EqualityComparer(1e-3f)), "Matching: the back is lengthened with it.");
            Assert.That(PieceSizing.Preview(pattern, fit, null, 0, 50f, 72f, false, out _)[1], Is.EqualTo(Vector2.one));

            Assert.That(JsonUtility.ToJson(pattern) + JsonUtility.ToJson(fit), Is.EqualTo(before), "The pattern and the fit are untouched.");
            Assert.That(Resources.FindObjectsOfTypeAll<ClothesPattern>().Length, Is.EqualTo(copiesBefore), "No copy is left behind.");
        }

        [Test]
        public void ScaleRests_ScaleAPiecesOwnConstraintsByTheirDirection_AndLeaveStitchesAlone()
        {
            int[] piece = { 0, 0, 0, 1, 1 };
            Vector2[] flat = { new Vector2(0f, 0f), new Vector2(0.1f, 0f), new Vector2(0f, 0.1f), new Vector2(0.3f, 0f), new Vector2(0.4f, 0f) };
            var constraints = new[]
            {
                new ClothConstraint { a = 0, b = 1, rest = 0.1f, kind = (int)ConstraintKind.Stretch },
                new ClothConstraint { a = 0, b = 2, rest = 0.1f, kind = (int)ConstraintKind.Stretch },
                new ClothConstraint { a = 1, b = 2, rest = 0.1f * Mathf.Sqrt(2f), kind = (int)ConstraintKind.Bend },
                new ClothConstraint { a = 1, b = 3, rest = 0f, kind = (int)ConstraintKind.Stitch },
                new ClothConstraint { a = 3, b = 4, rest = 0.1f, kind = (int)ConstraintKind.Stretch },
                // Hugged cloth rests shorter than its flat length: the scale keeps that.
                new ClothConstraint { a = 0, b = 1, rest = 0.09f, kind = (int)ConstraintKind.Stretch }
            };
            var baseRest = new float[constraints.Length];
            for (int i = 0; i < baseRest.Length; i++) baseRest[i] = constraints[i].rest;
            FitSession.ScaleRests(constraints, baseRest, piece, flat, new[] { new Vector2(1.5f, 2f), Vector2.one });
            Assert.That(constraints[0].rest, Is.EqualTo(0.15f).Within(1e-6f), "Across: the width's factor.");
            Assert.That(constraints[1].rest, Is.EqualTo(0.2f).Within(1e-6f), "Up: the length's factor.");
            Assert.That(constraints[2].rest, Is.EqualTo(Mathf.Sqrt(0.15f * 0.15f + 0.2f * 0.2f)).Within(1e-6f), "Slanting: both.");
            Assert.That(constraints[3].rest, Is.EqualTo(0f), "A stitch stays shut.");
            Assert.That(constraints[4].rest, Is.EqualTo(0.1f), "Another piece is left alone.");
            Assert.That(constraints[5].rest, Is.EqualTo(0.135f).Within(1e-6f));
            FitSession.ScaleRests(constraints, baseRest, piece, flat, null);
            for (int i = 0; i < constraints.Length; i++) Assert.That(constraints[i].rest, Is.EqualTo(baseRest[i]), "Null puts every rest length back.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PreviewPieceScale_ResizesTheClothAsItSimulates_AndPutsItBack(bool gpu)
        {
            if (gpu && !ClothSolverGpu.Supported) Assert.Ignore("This graphics device cannot run compute shaders.");
            using var capsule = new CapsuleBodyFixture();
            FitSession Tube()
            {
                var b = new PatternBuilder("Tube", "test");
                PieceBuilder Panel(string name, float angle) => b.Piece(name).Point(-30f, 0f, "Bottom", true).Point(30f, 0f, "Right").Point(30f, 40f, "Top", true).Point(-30f, 40f, "Left")
                    .Place(BodyPart.Torso, angle, -20f);
                PieceBuilder front = Panel("Front", 0f), back = Panel("Back", 180f);
                b.Sew(front, "Right", back, "Left");
                b.Sew(front, "Left", back, "Right");
                ClothesPattern pattern = b.Finish();
                made.Add(pattern);
                var fit = ScriptableObject.CreateInstance<ClothesFit>();
                made.Add(fit);
                fit.pattern = pattern;
                var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = !gpu };
                sessions.Add(session);
                session.Rebuild();
                // Sewn, gravity only just coming in: a tube on a straight body slides down it once gravity is on.
                session.Step(90);
                return session;
            }
            (float mean, float far) Radius(FitSession s)
            {
                Vector3[] x = s.SyncPositions();
                double sum = 0;
                float far = 0f;
                foreach (Vector3 p in x) { float r = new Vector2(p.x, p.z).magnitude; sum += r; far = Mathf.Max(far, r); }
                return ((float)(sum / x.Length), far);
            }
            // The tube is laid below this short body and falls free of it: cloth with nothing to hold it, where a sudden
            // change of a fifth blew it apart (its points ended a metre out).
            FitSession plain = Tube(), resized = Tube();
            Assert.That(resized.Solver.IsGpu, Is.EqualTo(gpu));
            var rest = new float[resized.Model.Constraints.Length];
            for (int i = 0; i < rest.Length; i++) rest[i] = resized.Model.Constraints[i].rest;
            resized.PreviewPieceScale(new[] { new Vector2(0.8f, 1f), new Vector2(0.8f, 1f) });
            Assert.That(resized.PreviewingScale, Is.True);
            Assert.That(resized.PreviewEasing, Is.True, "Eased in, not at once.");
            plain.Step(90);
            resized.Step(90);
            Assert.That(resized.PreviewEasing, Is.False, "A fifth takes about 67 frames.");
            Assert.That(resized.PreviewScaleNow[0], Is.EqualTo(new Vector2(0.8f, 1f)));
            (float loose, float looseFar) = Radius(plain);
            (float taken, float takenFar) = Radius(resized);
            Assert.That(taken, Is.EqualTo(loose * 0.8f).Within(loose * 0.08f), $"Taken in to four fifths round: {taken * 100f:F1} cm from its axis against {loose * 100f:F1}.");
            Assert.That(takenFar, Is.LessThan(looseFar), "Smoothly: no point flung out.");
            resized.PreviewPieceScale(null);
            Assert.That(resized.PreviewingScale, Is.False);
            for (int i = 0; i < rest.Length; i++) Assert.That(resized.Model.Constraints[i].rest, Is.EqualTo(rest[i]), "The model's own rest lengths are back.");

            // Built again larger than the drape is (released before it caught up): it starts the drape's size and eases out.
            plain.EaseFrom(new[] { new Vector2(0.8f, 1f), Vector2.one });
            Assert.That(plain.PreviewEasing, Is.True);
            plain.Step(90);
            Assert.That(plain.PreviewingScale, Is.False, "Eased all the way to its own rest lengths, and done.");
        }

        [Test]
        public void EaseStart_IsWhatTheDrapeShowsAsAShareOfTheNewSize()
        {
            Vector2[] whole = { new Vector2(1.2f, 1f), Vector2.one, new Vector2(1f, 0.5f) };
            Vector2[] start = DesignerSession.EaseStart(new[] { new Vector2(1.2f, 1f), Vector2.one, new Vector2(1f, 0.75f) }, whole);
            Assert.That(start[0], Is.EqualTo(Vector2.one), "Caught up: nothing to ease.");
            Assert.That(start[2], Is.EqualTo(new Vector2(1f, 1.5f)).Using(Vector2EqualityComparer(1e-5f)));
            Vector2[] typed = DesignerSession.EaseStart(null, whole);
            Assert.That(typed[0], Is.EqualTo(new Vector2(1f / 1.2f, 1f)).Using(Vector2EqualityComparer(1e-5f)), "A typed size: the drape is still the old size.");
            Assert.That(DesignerSession.EaseStart(null, null), Is.Null);
        }

        // ---- The drag's preview (SizePreview) ----------------------------------------------------------------

        /// <summary>A pattern's cloth laid flat as drawn: a centimetre as drawn is a centimetre in x and y.</summary>
        private static Vector3[] Flat(PatternMesh mesh)
        {
            var x = new Vector3[mesh.VertexCount];
            for (int v = 0; v < x.Length; v++) x[v] = new Vector3(mesh.drawn[v].x * 0.01f, mesh.drawn[v].y * 0.01f, 0f);
            return x;
        }

        [Test]
        public void Preview_StretchesAPieceAcrossFromItsMiddle_AndDownFromItsTop()
        {
            PatternPiece piece = PatternEditing.NewRectanglePiece("Panel", new Rect(0f, 0f, 40f, 60f));
            ClothesPattern pattern = NewPattern(piece);
            PatternMesh mesh = PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), 2f);
            Vector3[] start = Flat(mesh);
            SizePreview.Axes(mesh, start, out Vector3[] across, out Vector3[] up);
            for (int v = 0; v < start.Length; v++)
            {
                Assert.That((across[v] - new Vector3(0.01f, 0f, 0f)).magnitude, Is.LessThan(1e-5f));
                Assert.That((up[v] - new Vector3(0f, 0.01f, 0f)).magnitude, Is.LessThan(1e-5f));
            }
            Vector3[] wider = SizePreview.Shape(mesh, start, across, up, new[] { new Vector2(1.5f, 1f) }, null, null, 0f);
            Vector3[] longer = SizePreview.Shape(mesh, start, across, up, new[] { new Vector2(1f, 1.5f) }, null, null, 0f);
            for (int v = 0; v < start.Length; v++)
            {
                Vector2 d = mesh.drawn[v];
                Assert.That(wider[v].x, Is.EqualTo((20f + (d.x - 20f) * 1.5f) * 0.01f).Within(1e-4f), "Across from its middle: its sides move out by a quarter of its width.");
                Assert.That(wider[v].y, Is.EqualTo(start[v].y).Within(1e-6f));
                Assert.That(longer[v].y, Is.EqualTo((60f + (d.y - 60f) * 1.5f) * 0.01f).Within(1e-4f), "Down from its top: the hem moves down half its length.");
                Assert.That(longer[v].x, Is.EqualTo(start[v].x).Within(1e-6f));
            }
            Assert.That(SizePreview.Shape(mesh, start, across, up, new[] { Vector2.one }, null, null, 0f), Is.EqualTo(start), "Not resized: not moved.");
        }

        /// <summary>A front and a back sewn at both sides, laid as a tube of the radius round the upright y axis.</summary>
        private (ClothesPattern pattern, PatternMesh mesh, Vector3[] start) TubeOfTwo(float radius, float bottomY)
        {
            PatternPiece front = PatternEditing.NewRectanglePiece("Front", new Rect(0f, 0f, 30f, 20f));
            PatternPiece back = PatternEditing.NewRectanglePiece("Back", new Rect(40f, 0f, 30f, 20f));
            ClothesPattern pattern = NewPattern(front, back);
            Assert.That(PatternEditing.Sew(pattern, front, 1, back, 3, out string problem), Is.Not.Null, problem);
            Assert.That(PatternEditing.Sew(pattern, back, 1, front, 3, out problem), Is.Not.Null, problem);
            PatternMesh mesh = PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), 2f);
            var start = new Vector3[mesh.VertexCount];
            for (int v = 0; v < start.Length; v++)
            {
                // The front from -90 to 90 degrees round, the back from 90 to 270: their sides meet.
                bool isFront = mesh.piece[v] == 0;
                float u = (mesh.drawn[v].x - (isFront ? 0f : 40f)) / 30f;
                float angle = ((isFront ? -90f : 90f) + u * 180f) * Mathf.Deg2Rad;
                start[v] = new Vector3(Mathf.Sin(angle) * radius, bottomY + mesh.drawn[v].y * 0.01f, Mathf.Cos(angle) * radius);
            }
            return (pattern, mesh, start);
        }

        [Test]
        public void Preview_PiecesWrappedRoundTheBodyTogether_GrowRoundIt_AndKeepTheirSeams()
        {
            (ClothesPattern pattern, PatternMesh mesh, Vector3[] start) = TubeOfTwo(0.2f, 1f);
            var body = new BodyLandmarks { valid = true, torsoCenter = Vector2.zero };
            var factors = new[] { new Vector2(1.2f, 1f), new Vector2(1.2f, 1f) };
            Ray?[] rings = SizePreview.RingAxes(mesh, pattern, NewFit(pattern), body, factors);
            Assert.That(rings[0].HasValue && rings[1].HasValue, Is.True, "Sewn to each other at the same factor, round the torso.");
            Assert.That(SizePreview.RingAxes(mesh, pattern, null, body, new[] { new Vector2(1.2f, 1f), Vector2.one })[0].HasValue, Is.False, "Widened alone: stretched across itself instead.");
            SizePreview.Axes(mesh, start, out Vector3[] across, out Vector3[] up);
            Vector3[] grown = SizePreview.Shape(mesh, start, across, up, factors, rings, null, 0f);
            for (int v = 0; v < grown.Length; v++)
            {
                Assert.That(new Vector2(grown[v].x, grown[v].z).magnitude, Is.EqualTo(0.24f).Within(1e-4f), "A fifth further round the body.");
                Assert.That(grown[v].y, Is.EqualTo(start[v].y).Within(1e-6f));
            }
            float worst = 0f;
            foreach (Vector2Int s in mesh.stitches) worst = Mathf.Max(worst, Vector3.Distance(grown[s.x], grown[s.y]));
            Assert.That(worst, Is.LessThan(1e-3f), "The side seams stay together.");
        }

        [Test]
        public void Preview_APieceSewnToAResizedOne_MovesWithIt()
        {
            PatternPiece skirt = PatternEditing.NewRectanglePiece("Skirt", new Rect(0f, 20f, 40f, 40f));
            PatternPiece band = PatternEditing.NewRectanglePiece("Hem Band", new Rect(0f, 0f, 40f, 20f));
            ClothesPattern pattern = NewPattern(skirt, band);
            // The skirt's bottom to the band's top.
            Assert.That(PatternEditing.Sew(pattern, skirt, 0, band, 2, out string problem), Is.Not.Null, problem);
            PatternMesh mesh = PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), 2f);
            Vector3[] start = Flat(mesh);
            SizePreview.Axes(mesh, start, out Vector3[] across, out Vector3[] up);
            Vector3[] longer = SizePreview.Shape(mesh, start, across, up, new[] { new Vector2(1f, 1.5f), Vector2.one }, null, null, 0f);
            for (int v = 0; v < start.Length; v++)
            {
                if (mesh.piece[v] != 1) continue;
                Assert.That((longer[v] - start[v] - new Vector3(0f, -0.2f, 0f)).magnitude, Is.LessThan(1e-4f), "The band moves down with the skirt's hem, 20 cm.");
            }
        }

        [Test]
        public void Preview_KeepsTheClothOutsideTheBody()
        {
            using var capsule = new CapsuleBodyFixture();
            // A tube just outside the capsule, round its middle, taken in by a fifth: it would go inside.
            (ClothesPattern pattern, PatternMesh mesh, Vector3[] start) = TubeOfTwo(CapsuleBodyFixture.Radius + 0.01f, 1f);
            var body = new BodyLandmarks { valid = true, torsoCenter = Vector2.zero };
            var factors = new[] { new Vector2(0.8f, 1f), new Vector2(0.8f, 1f) };
            Ray?[] rings = SizePreview.RingAxes(mesh, pattern, null, body, factors);
            SizePreview.Axes(mesh, start, out Vector3[] across, out Vector3[] up);
            Vector3[] taken = SizePreview.Shape(mesh, start, across, up, factors, rings, new SizePreview.BodyPlanes(capsule.Body.Collider, start), 0.005f);
            foreach (Vector3 p in taken)
            {
                float r = new Vector2(p.x, p.z).magnitude;
                Assert.That(r, Is.GreaterThan(CapsuleBodyFixture.Radius), "Put on the body, outside it.");
                Assert.That(r, Is.LessThan(CapsuleBodyFixture.Radius + 0.0075f), "At the clearance, not where it was.");
            }
        }

        private static IEqualityComparer<Vector2> Vector2EqualityComparer(float tolerance) => new Vector2Comparer(tolerance);

        private sealed class Vector2Comparer : IEqualityComparer<Vector2>
        {
            private readonly float tolerance;
            public Vector2Comparer(float tolerance) { this.tolerance = tolerance; }
            public bool Equals(Vector2 a, Vector2 b) => (a - b).magnitude <= tolerance;
            public int GetHashCode(Vector2 v) => 0;
        }
    }
}
#endif
