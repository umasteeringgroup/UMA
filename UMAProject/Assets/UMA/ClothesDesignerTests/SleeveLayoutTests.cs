#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>
    /// How sleeves are laid round the arms before sewing: on a cone that follows the arm, close enough that
    /// the underarm seam closes under the arm, with Tilt and End Distance to adjust it. The capsule body
    /// has no arms of its own, so it is given arm landmarks: shoulders just off its sides, arms hanging at
    /// about 45 degrees, and a profile that narrows from the shoulder to the wrist.
    /// </summary>
    public class SleeveLayoutTests
    {
        private CapsuleBodyFixture capsule;
        private readonly List<Object> made = new List<Object>();
        private readonly List<FitSession> sessions = new List<FitSession>();

        [SetUp]
        public void Make()
        {
            capsule = new CapsuleBodyFixture();
            BodyLandmarks m = capsule.Body.Landmarks;
            m.limbs = true;
            m.leftShoulder = new Vector3(-0.2f, 1.52f, 0f);
            m.rightShoulder = new Vector3(0.2f, 1.52f, 0f);
            m.leftWrist = new Vector3(-0.6f, 1.12f, 0f);
            m.rightWrist = new Vector3(0.6f, 1.12f, 0f);
            m.armRadius = 0.05f;
            m.armLengthCm = 56.6f;
            m.upperArmCm = 30f;
            m.neckCm = 34f;
            m.neckY = 1.62f;
            m.headTopY = 1.85f;
            m.crotchY = 0.75f;
            int count = Mathf.CeilToInt((0.566f - 0.01f - BodyLandmarks.ArmProfileStartM) / BodyLandmarks.ArmProfileStepM);
            m.leftArmProfile = new float[count];
            m.rightArmProfile = new float[count];
            for (int i = 0; i < count; i++) m.leftArmProfile[i] = m.rightArmProfile[i] = Mathf.Lerp(0.07f, 0.034f, i / (float)(count - 1));
        }

        [TearDown]
        public void Clean()
        {
            foreach (FitSession s in sessions) s.Dispose();
            sessions.Clear();
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            capsule.Dispose();
        }

        private FitSession LayOut(float tilt, float endDistance)
        {
            ClothesPattern tee = SamplePatterns.Create("T-Shirt");
            made.Add(tee);
            foreach (PatternPiece piece in tee.pieces)
            {
                if (!PiecePlacement.IsArm(piece.placement.part)) continue;
                piece.placement.tiltDeg = tilt;
                piece.placement.endDistanceCm = endDistance;
            }
            var fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(fit);
            fit.pattern = tee;
            var session = new FitSession { Pattern = tee, Fit = fit, Body = capsule.Body, PreferCpu = true };
            sessions.Add(session);
            session.Rebuild();
            Assert.That(session.HasCloth, Is.True, string.Join(" | ", session.Messages));
            return session;
        }

        private static int RightSleeve(FitSession s)
        {
            for (int p = 0; p < s.Pattern.pieces.Count; p++) if (s.Pattern.pieces[p].placement.part == BodyPart.RightArm) return p;
            Assert.Fail("The T-Shirt has no right sleeve.");
            return -1;
        }

        /// <summary>
        /// Per row in the far half of the right sleeve (by its drawn height): the share of the way round the
        /// arm it covers, how far its middle is below the arm's axis, and its least distance from the axis.
        /// </summary>
        private static List<(float share, float drop, float least, float towardsEnd)> FarRows(FitSession s)
        {
            int piece = RightSleeve(s);
            s.Body.Landmarks.ArmFrame(false, out Vector3 root, out Vector3 axis, out Vector3 up, out Vector3 side);
            PatternMesh mesh = s.Mesh;
            float top = float.MinValue, bottom = float.MaxValue;
            for (int v = mesh.pieceStart[piece]; v < mesh.pieceStart[piece] + mesh.pieceCount[piece]; v++) { top = Mathf.Max(top, mesh.layout[v].y); bottom = Mathf.Min(bottom, mesh.layout[v].y); }
            var rows = new List<(float, float, float, float)>();
            for (float f = 0.55f; f <= 0.96f; f += 0.1f)
            {
                float y = Mathf.Lerp(top, bottom, f);
                var angles = new List<float>();
                Vector3 sum = Vector3.zero;
                float least = float.MaxValue;
                for (int v = mesh.pieceStart[piece]; v < mesh.pieceStart[piece] + mesh.pieceCount[piece]; v++)
                {
                    if (Mathf.Abs(mesh.layout[v].y - y) > 1.2f) continue;
                    Vector3 q = s.Arranged.positions[v] - root;
                    Vector3 radial = q - axis * Vector3.Dot(q, axis);
                    angles.Add(Mathf.Atan2(Vector3.Dot(radial, side), Vector3.Dot(radial, up)) * Mathf.Rad2Deg);
                    sum += radial;
                    least = Mathf.Min(least, radial.magnitude);
                }
                if (angles.Count < 3) continue;
                angles.Sort();
                float gap = 360f - (angles[angles.Count - 1] - angles[0]);
                for (int i = 1; i < angles.Count; i++) gap = Mathf.Max(gap, angles[i] - angles[i - 1]);
                rows.Add((1f - gap / 360f, -Vector3.Dot(sum / angles.Count, up), least, f));
            }
            Assert.That(rows.Count, Is.GreaterThan(2));
            return rows;
        }

        private static bool WarnsAboutSleeves(FitSession s)
        {
            foreach (string message in s.Messages) if (message.Contains("of the way round the arm")) return true;
            return false;
        }

        [Test]
        public void Sleeve_IsLaidRoundMostOfTheArm_SoItsSeamClosesUnderIt()
        {
            FitSession s = LayOut(0f, 0f);
            foreach (var row in FarRows(s))
            {
                // Before the cone, a sleeve sat on one radius for the whole arm and its cuff covered about a
                // third of the way round: the underarm seam then closed above the forearm.
                Assert.That(row.share, Is.GreaterThan(0.55f), "row at " + row.towardsEnd.ToString("0.00") + " of the sleeve");
            }
            Assert.That(WarnsAboutSleeves(s), Is.False, string.Join(" | ", s.Messages));
            Assert.That(s.Arranged.gap, Is.EqualTo(Arrangement.DefaultGapM).Within(1e-5f), "no piece should have had to be moved out");
        }

        [Test]
        public void Sleeve_FollowsTheArmsThickness_NarrowerAtTheWrist()
        {
            FitSession s = LayOut(0f, 0f);
            List<(float share, float drop, float least, float towardsEnd)> rows = FarRows(s);
            Assert.That(rows[rows.Count - 1].least, Is.LessThan(rows[0].least), "the cuff end should lie closer to the arm's axis than the elbow");
            float clearance = s.Fit.clearanceCm * 0.01f;
            foreach (var row in rows)
            {
                // Never inside the arm's reach (at least the wrist's 3.4 cm) plus the clearance.
                Assert.That(row.least, Is.GreaterThan(0.034f + clearance), "row at " + row.towardsEnd.ToString("0.00"));
            }
        }

        [Test]
        public void EndDistance_BringsTheCuffIn_ButNotInsideTheClearance()
        {
            List<(float share, float drop, float least, float towardsEnd)> plain = FarRows(LayOut(0f, 0f));
            FitSession closer = LayOut(0f, -3f);
            List<(float share, float drop, float least, float towardsEnd)> rows = FarRows(closer);
            Assert.That(rows[rows.Count - 1].least, Is.LessThan(plain[plain.Count - 1].least - 0.001f), "the cuff end should come in");
            float clearance = closer.Fit.clearanceCm * 0.01f;
            Assert.That(rows[rows.Count - 1].least, Is.GreaterThan(0.034f + clearance), "but not inside the clearance from the wrist");
            Assert.That(closer.Arranged.gap, Is.EqualTo(Arrangement.DefaultGapM).Within(1e-5f), "and no piece should have had to be moved out");
        }

        [Test]
        public void Tilt_LowersALooseSleevesEnd_AsFarAsTheArmStillFits()
        {
            List<(float share, float drop, float least, float towardsEnd)> straight = FarRows(LayOut(0f, 4f));
            List<(float share, float drop, float least, float towardsEnd)> tilted = FarRows(LayOut(40f, 4f));
            Assert.That(tilted[tilted.Count - 1].drop, Is.GreaterThan(straight[straight.Count - 1].drop + 0.01f), "the far end should hang lower");
            foreach (var row in tilted)
            {
                // The arm stays inside: the tube's middle never moves more than its room allows.
                Assert.That(row.least, Is.GreaterThan(0.0f));
                Assert.That(row.drop, Is.LessThan(0.06f), "row at " + row.towardsEnd.ToString("0.00"));
            }
            // A sleeve laid only part of the way round, but low enough, closes under the arm: no warning.
            Assert.That(WarnsAboutSleeves(LayOut(40f, 4f)), Is.False);
        }

        [Test]
        public void ASleeveLaidTooFarOut_IsReported()
        {
            FitSession s = LayOut(0f, 6f);
            Assert.That(WarnsAboutSleeves(s), Is.True, "a cuff end 6 cm farther out covers too little of the arm for its seam to close under it");
        }

        [Test]
        public void LayoutGuide_LiesWhereThePiecesAreLaid()
        {
            FitSession s = LayOut(20f, 2f);
            PatternMesh mesh = s.Mesh;
            Assert.That(s.Arranged.guides, Is.Not.Null);
            Assert.That(s.Arranged.guides.Length, Is.EqualTo(s.Pattern.pieces.Count));
            for (int p = 0; p < s.Pattern.pieces.Count; p++)
            {
                Arrangement.GuideRing[] rings = s.Arranged.guides[p];
                string name = s.Pattern.pieces[p].name;
                Assert.That(rings.Length, Is.GreaterThan(5), name);
                for (int i = 1; i < rings.Length; i++) Assert.That(rings[i].downCm, Is.GreaterThan(rings[i - 1].downCm), name + ": rings from the top down");
                var byRow = new Dictionary<int, Arrangement.GuideRing>();
                foreach (Arrangement.GuideRing ring in rings) byRow[Mathf.RoundToInt(ring.downCm)] = ring;
                float top = float.MinValue;
                for (int v = mesh.pieceStart[p]; v < mesh.pieceStart[p] + mesh.pieceCount[p]; v++) top = Mathf.Max(top, mesh.layout[v].y);
                int on = 0, all = 0;
                for (int v = mesh.pieceStart[p]; v < mesh.pieceStart[p] + mesh.pieceCount[p]; v++)
                {
                    if (mesh.layoutDepth[v] > 0f || !byRow.TryGetValue(Mathf.RoundToInt(top - mesh.layout[v].y), out Arrangement.GuideRing r)) continue;
                    all++;
                    Vector3 x = s.Arranged.positions[v];
                    Vector3 onRing = r.Point(r.AngleOf(x));
                    Vector3 offset = x - onRing;
                    // Across the ring within 6 mm; along the part, within the half centimetre a row rounds to. A ring is
                    // its row's first point: a sleeve cap's width (and so its radius) changes within a row, and points
                    // that started against the body are moved out on their own.
                    float along = Mathf.Abs(Vector3.Dot(offset, r.axis));
                    if ((offset - r.axis * Vector3.Dot(offset, r.axis)).magnitude < 0.006f && along < 0.0075f) on++;
                }
                Assert.That(all, Is.GreaterThan(20), name);
                Assert.That(on, Is.GreaterThanOrEqualTo(all * 0.9f), name + ": points on their guide ring");
                // The handles' ring at the Faces angle is on the piece's middle, on the cloth.
                PiecePlacement placement = s.Fit.PlacementFor(s.Pattern.pieces[p]);
                Vector3 middle = Arrangement.HandleRing(rings).Point(Arrangement.GuideAngle(placement));
                float nearest = float.MaxValue;
                for (int v = mesh.pieceStart[p]; v < mesh.pieceStart[p] + mesh.pieceCount[p]; v++) nearest = Mathf.Min(nearest, Vector3.Distance(s.Arranged.positions[v], middle));
                Assert.That(nearest, Is.LessThan(0.02f), name + ": the guide's Faces handle should be on the piece");
            }
        }

        [Test]
        public void TiltAndEndDistance_ChangeThePlacementAndTheRevision()
        {
            ClothesPattern tee = SamplePatterns.Create("T-Shirt");
            made.Add(tee);
            PiecePlacement before = tee.pieces[2].placement.Clone();
            string revision = tee.ComputeRevision();
            tee.pieces[2].placement.tiltDeg = 10f;
            Assert.That(tee.pieces[2].placement.SameAs(before), Is.False);
            Assert.That(tee.ComputeRevision(), Is.Not.EqualTo(revision), "a kept drape must not be restored over a moved sleeve");
            tee.pieces[2].placement.tiltDeg = 0f;
            Assert.That(tee.ComputeRevision(), Is.EqualTo(revision), "unset, they leave older revisions as they were");
            tee.pieces[2].placement.endDistanceCm = -1f;
            Assert.That(tee.pieces[2].placement.SameAs(before), Is.False);
        }
    }
}
#endif
