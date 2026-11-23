#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>Patterns: curves, sizes, edits, and keeping ids through copies and reloads.</summary>
    public sealed class PatternTests
    {
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void Clean()
        {
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private ClothesPattern Vest()
        {
            ClothesPattern p = VestPattern.Create();
            made.Add(p);
            return p;
        }

        [Test]
        public void Vest_IsReadyToSimulate()
        {
            ClothesPattern vest = Vest();
            Assert.That(vest.FindProblems(), Is.Empty);
            Assert.That(vest.pieces.Count, Is.EqualTo(2));
            Assert.That(vest.seams.Count, Is.EqualTo(4));
            foreach (PatternPiece piece in vest.pieces)
            {
                Assert.That(PatternGeometry.SignedArea(PatternGeometry.SampleOutline(piece, 1f, null)), Is.GreaterThan(0f), piece.name + " is drawn counter-clockwise.");
                Assert.That(PatternGeometry.OutlineSelfIntersects(piece), Is.False);
            }
        }

        [Test]
        public void Seams_JoinEdgesOfEqualLength()
        {
            ClothesPattern vest = Vest();
            foreach (PatternSeam seam in vest.seams) Assert.That(Mathf.Abs(PatternEditing.SeamMismatchCm(vest, seam)), Is.LessThan(0.01f));
        }

        [Test]
        public void StraightEdge_LengthIsTheDistance_CurveIsLongerThanItsChord()
        {
            ClothesPattern vest = Vest();
            PatternPiece front = vest.pieces[0];
            Assert.That(PatternGeometry.EdgeLength(front, 0, PieceScale.Identity), Is.EqualTo(52f).Within(0.01f));
            Vector2 a = front.points[2].position, b = front.points[3].position;
            Assert.That(PatternGeometry.EdgeLength(front, 2, PieceScale.Identity), Is.GreaterThan((b - a).magnitude));
        }

        [Test]
        public void Chest_96PlusEase8_GivesHalves26cmWide_108Gives29()
        {
            // The plan's worked example: a 96 cm chest with 8 cm of ease is 104 cm round, 52 cm per panel,
            // 26 cm per mirrored half. A 108 cm chest gives 58 cm per panel, 29 per half.
            ClothesPattern vest = Vest();
            var body = new BodyLandmarks { valid = true, chestCm = 96f, shoulderTopY = 1.5f, waistY = 1.1f };
            PieceScale[] scales = PatternGeometry.Evaluate(vest, body, null, null);
            List<Vector2> outline = PatternGeometry.SampleOutline(vest.pieces[0], 0.25f, scales[0]);
            // The chest line is drawn at 32 cm; the length size moves it with the rest of the piece.
            PatternGeometry.WidthAt(outline, 32f * scales[0].sy, out float min, out float max);
            Assert.That((max - min) * 0.5f, Is.EqualTo(26f).Within(0.05f));

            body.chestCm = 108f;
            scales = PatternGeometry.Evaluate(vest, body, null, null);
            outline = PatternGeometry.SampleOutline(vest.pieces[0], 0.25f, scales[0]);
            PatternGeometry.WidthAt(outline, 32f * scales[0].sy, out min, out max);
            Assert.That((max - min) * 0.5f, Is.EqualTo(29f).Within(0.05f));
        }

        [Test]
        public void Length_FollowsShoulderToWaistPlusAllowance()
        {
            ClothesPattern vest = Vest();
            var body = new BodyLandmarks { valid = true, chestCm = 100f, shoulderTopY = 1.5f, waistY = 1.1f };
            var report = new List<EvaluatedDimension>();
            PieceScale[] scales = PatternGeometry.Evaluate(vest, body, null, report);
            Assert.That(vest.pieces[0].Bounds().height * scales[0].sy, Is.EqualTo(40f + 20f).Within(0.01f));
            Assert.That(report.Count, Is.EqualTo(2));
        }

        [Test]
        public void Allowance_ChangedInTheFit_OverridesThePattern()
        {
            ClothesPattern vest = Vest();
            ClothesFit fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(fit);
            fit.allowances.Add(new AllowanceOverride { dimensionId = vest.dimensions[0].id, allowanceCm = 20f });
            var body = new BodyLandmarks { valid = true, chestCm = 96f, shoulderTopY = 1.5f, waistY = 1.1f };
            PieceScale[] scales = PatternGeometry.Evaluate(vest, body, fit, null);
            Assert.That(52f * scales[0].sx, Is.EqualTo((96f + 20f) / 2f).Within(0.05f));
        }

        [Test]
        public void SplitEdge_KeepsTheSeamResolving()
        {
            ClothesPattern vest = Vest();
            int points = vest.pieces[0].points.Count;
            PatternEditing.SplitEdge(vest, vest.pieces[0], 1, 0.5f);
            Assert.That(vest.pieces[0].points.Count, Is.EqualTo(points + 1));
            Assert.That(vest.FindProblems(), Is.Empty);
        }

        [Test]
        public void DeletePoint_RemovesTheSeamOnTheEdgeItTakes()
        {
            ClothesPattern vest = Vest();
            int seams = vest.seams.Count;
            Assert.That(PatternEditing.DeletePoint(vest, vest.pieces[0], 1), Is.True);
            Assert.That(vest.seams.Count, Is.EqualTo(seams - 1));
            Assert.That(vest.FindProblems(), Is.Empty);
        }

        [Test]
        public void MirrorCopy_IsCounterClockwise_SameArea_NewIds()
        {
            ClothesPattern vest = Vest();
            PatternPiece front = vest.pieces[0];
            PatternPiece copy = PatternEditing.MirrorCopy(front, "Copy");
            float a0 = PatternGeometry.SignedArea(PatternGeometry.SampleOutline(front, 0.5f, null));
            float a1 = PatternGeometry.SignedArea(PatternGeometry.SampleOutline(copy, 0.5f, null));
            Assert.That(a1, Is.EqualTo(a0).Within(a0 * 0.01f));
            Assert.That(copy.id, Is.Not.EqualTo(front.id));
            Assert.That(copy.mirrorOf, Is.EqualTo(front.id));
            for (int i = 0; i < copy.edges.Count; i++) Assert.That(copy.edges[i].id, Is.Not.EqualTo(front.edges[i].id));
        }

        [Test]
        public void Sew_RefusesOpeningsAndEdgesAlreadySewn()
        {
            ClothesPattern vest = Vest();
            Assert.That(PatternEditing.Sew(vest, vest.pieces[0], 0, vest.pieces[1], 0, out string why), Is.Null);
            Assert.That(why, Does.Contain("opening"));
            Assert.That(PatternEditing.Sew(vest, vest.pieces[0], 1, vest.pieces[1], 1, out why), Is.Null);
            Assert.That(why, Does.Contain("already sewn"));
        }

        [Test]
        public void Revision_IgnoresTheUvLayoutAndNames()
        {
            ClothesPattern vest = Vest();
            string before = vest.ComputeRevision();
            UvLayout.Repack(vest);
            vest.pieces[0].name = "Renamed";
            vest.notes = "changed";
            Assert.That(vest.ComputeRevision(), Is.EqualTo(before));
        }

        [Test]
        public void Copy_KeepsEveryIdAndValue()
        {
            ClothesPattern vest = Vest();
            ClothesPattern copy = Object.Instantiate(vest);
            made.Add(copy);
            Assert.That(JsonUtility.ToJson(copy), Is.EqualTo(JsonUtility.ToJson(vest)));
            Assert.That(copy.ComputeRevision(), Is.EqualTo(vest.ComputeRevision()));
        }

        [Test]
        public void Revision_ChangesWithTheShape()
        {
            ClothesPattern vest = Vest();
            string before = vest.ComputeRevision();
            vest.pieces[0].points[0].position += new Vector2(1f, 0f);
            Assert.That(vest.ComputeRevision(), Is.Not.EqualTo(before));
        }

        [Test]
        public void Migrate_GivesMissingIdsAndSettings()
        {
            ClothesPattern vest = Vest();
            vest.pieces[0].id = null;
            vest.pieces[0].edges[2].id = "";
            vest.uvLayout = null;
            vest.schemaVersion = 0;
            List<string> notes = vest.Migrate();
            Assert.That(vest.pieces[0].id, Is.Not.Empty);
            Assert.That(vest.pieces[0].edges[2].id, Is.Not.Empty);
            Assert.That(vest.uvLayout, Is.Not.Null);
            Assert.That(vest.schemaVersion, Is.EqualTo(ClothesPattern.CurrentSchemaVersion));
            Assert.That(notes, Is.Not.Empty);
        }

        [Test]
        public void FindProblems_ReportsASeamOnAMissingEdge()
        {
            ClothesPattern vest = Vest();
            vest.seams[0].a.edgeId = "missing";
            Assert.That(vest.FindProblems(), Has.Some.Contains("no longer exists"));
        }
    }
}
#endif
