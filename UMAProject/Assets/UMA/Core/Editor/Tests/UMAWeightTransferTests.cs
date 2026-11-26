using System;
using System.Collections.Generic;
using NUnit.Framework;
using UMA.Editors.Weighting;
using UnityEngine;

namespace UMA.Tests
{
    /// <summary>The shared weighting library on bodies and clothes made in code.</summary>
    public sealed class UMAWeightTransferTests
    {
        // ---- geometry -------------------------------------------------------------------------------------------

        /// <summary>A grid from origin along u and v; its triangles face u x v.</summary>
        private static void Grid(List<Vector3> positions, List<int> triangles, Vector3 origin, Vector3 u, Vector3 v, int nu, int nv)
        {
            int start = positions.Count;
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                    positions.Add(origin + u * (i / (float)nu) + v * (j / (float)nv));
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = start + j * (nu + 1) + i, b = a + 1, c = a + nu + 1, d = c + 1;
                    triangles.AddRange(new[] { a, b, c, b, d, c });
                }
        }

        private static UMAWeightDonor Donor(List<Vector3> positions, List<int> triangles, Func<Vector3, BoneWeight1[]> weightAt, int bones, string[] names = null)
        {
            var weights = new BoneWeight1[positions.Count][];
            for (int v = 0; v < positions.Count; v++) weights[v] = weightAt(positions[v]);
            return new UMAWeightDonor { Positions = positions.ToArray(), Triangles = triangles.ToArray(), Weights = weights, BoneCount = bones, BoneNames = names };
        }

        private static BoneWeight1[] One(int bone) => new[] { new BoneWeight1 { boneIndex = bone, weight = 1f } };

        /// <summary>Two patches of body, bone 0 on the left (x -0.5 to -0.3), bone 1 on the right (0.3 to 0.5), facing +z.</summary>
        private static UMAWeightDonor TwoPatches(int bones = 2, string[] names = null)
        {
            var p = new List<Vector3>();
            var t = new List<int>();
            Grid(p, t, new Vector3(-0.5f, 0f, 0f), new Vector3(0.2f, 0f, 0f), new Vector3(0f, 0.1f, 0f), 8, 4);
            Grid(p, t, new Vector3(0.3f, 0f, 0f), new Vector3(0.2f, 0f, 0f), new Vector3(0f, 0.1f, 0f), 8, 4);
            return Donor(p, t, x => One(x.x < 0f ? 0 : 1), bones, names);
        }

        /// <summary>A strip of cloth 5 mm above the patches, reaching from one to the other: its middle is far from both.</summary>
        private static UMAWeightTarget Strip(float x0 = -0.45f, float x1 = 0.45f, int nu = 36)
        {
            var p = new List<Vector3>();
            var t = new List<int>();
            Grid(p, t, new Vector3(x0, 0.02f, 0.005f), new Vector3(x1 - x0, 0f, 0f), new Vector3(0f, 0.06f, 0f), nu, 2);
            return new UMAWeightTarget { Positions = p.ToArray(), Triangles = t.ToArray() };
        }

        private static float WeightOf(BoneWeight1[] w, int bone)
        {
            foreach (BoneWeight1 b in w) if (b.boneIndex == bone) return b.weight;
            return 0f;
        }

        private static void AssertNormalized(UMAWeightResult result, int maxBones)
        {
            BoneWeight1[][] all = result.PerVertex();
            for (int v = 0; v < all.Length; v++)
            {
                Assert.That(all[v].Length, Is.InRange(1, maxBones), "vertex " + v);
                float sum = 0f;
                foreach (BoneWeight1 w in all[v]) { Assert.That(w.weight, Is.GreaterThanOrEqualTo(0f)); sum += w.weight; }
                Assert.That(sum, Is.EqualTo(1f).Within(1e-4f), "vertex " + v);
            }
        }

        /// <summary>The strip's middle row, left to right: vertex index of column i.</summary>
        private static int Middle(int i, int nu = 36) => (nu + 1) + i;

        // ---- solver ---------------------------------------------------------------------------------------------

        [Test]
        public void SparseCholesky_SolvesAGridLaplacian()
        {
            const int n = 24;
            int count = n * n;
            var cols = new List<int>[count];
            var vals = new List<double>[count];
            var positions = new Vector3[count];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    positions[i] = new Vector3(x, y, 0f);
                    cols[i] = new List<int>(); vals[i] = new List<double>();
                    double diag = 0.1;
                    void Edge(int j) { cols[i].Add(j); vals[i].Add(-1.0); diag += 1.0; }
                    if (x > 0) Edge(i - 1);
                    if (x < n - 1) Edge(i + 1);
                    if (y > 0) Edge(i - n);
                    if (y < n - 1) Edge(i + n);
                    cols[i].Add(i); vals[i].Add(diag);
                }
            var random = new System.Random(5);
            var truth = new double[count];
            for (int i = 0; i < count; i++) truth[i] = random.NextDouble() * 2 - 1;
            var b = new double[count];
            for (int i = 0; i < count; i++) for (int e = 0; e < cols[i].Count; e++) b[i] += vals[i][e] * truth[cols[i][e]];
            var chol = new UMASparseCholesky();
            Assert.That(chol.Factor(cols, vals, positions), Is.True);
            chol.Solve(b);
            double worst = 0;
            for (int i = 0; i < count; i++) worst = Math.Max(worst, Math.Abs(b[i] - truth[i]));
            Assert.That(worst, Is.LessThan(1e-8));
            // Nested dissection keeps the factor far smaller than a dense one.
            Assert.That(chol.FactorNonZeros, Is.LessThan(count * count / 8));
        }

        // ---- methods --------------------------------------------------------------------------------------------

        [Test]
        public void TightSurface_CopiesTheBodyUnderEachVertex()
        {
            var p = new List<Vector3>();
            var t = new List<int>();
            Grid(p, t, new Vector3(-0.5f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0.4f, 0f), 10, 4);
            // Bone 1's weight rises from left to right: copied on the surface it must stay linear.
            UMAWeightDonor donor = Donor(p, t, x =>
            {
                float s = Mathf.Clamp01(x.x + 0.5f);
                return new[] { new BoneWeight1 { boneIndex = 0, weight = 1f - s }, new BoneWeight1 { boneIndex = 1, weight = s } };
            }, 2);
            var tp = new List<Vector3>();
            var tt = new List<int>();
            Grid(tp, tt, new Vector3(-0.37f, 0.05f, 0.004f), new Vector3(0.71f, 0f, 0f), new Vector3(0f, 0.2f, 0f), 13, 3);
            var target = new UMAWeightTarget { Positions = tp.ToArray(), Triangles = tt.ToArray() };
            UMAWeightResult result = UMAWeightTransfer.Transfer(donor, target, new UMAWeightSettings { Method = UMAWeightMethod.TightSurface, MinWeight = 0f });
            BoneWeight1[][] w = result.PerVertex();
            for (int v = 0; v < tp.Count; v++)
            {
                Assert.That(WeightOf(w[v], 1), Is.EqualTo(Mathf.Clamp01(tp[v].x + 0.5f)).Within(0.01f), "vertex " + v);
                Assert.That(result.Report.States[v], Is.EqualTo(UMAWeightVertexState.Matched));
            }
            AssertNormalized(result, 4);
        }

        [Test]
        public void ExactMatch_CopiesTheBodyVertexAtTheSamePlace()
        {
            UMAWeightDonor donor = TwoPatches();
            // The cloth is the left patch itself.
            var p = new List<Vector3>();
            var t = new List<int>();
            Grid(p, t, new Vector3(-0.5f, 0f, 0f), new Vector3(0.2f, 0f, 0f), new Vector3(0f, 0.1f, 0f), 8, 4);
            var target = new UMAWeightTarget { Positions = p.ToArray(), Triangles = t.ToArray() };
            UMAWeightResult result = UMAWeightTransfer.Transfer(donor, target, new UMAWeightSettings { Method = UMAWeightMethod.ExactMatch });
            Assert.That(result.Report.Exact, Is.EqualTo(p.Count));
            foreach (BoneWeight1[] w in result.PerVertex()) Assert.That(WeightOf(w, 0), Is.EqualTo(1f));
        }

        [TestCase(UMAInpaintEnergy.Biharmonic)]
        [TestCase(UMAInpaintEnergy.Harmonic)]
        public void RobustInpaint_FillsClothBetweenTwoPartsSmoothly(UMAInpaintEnergy energy)
        {
            UMAWeightTarget target = Strip();
            UMAWeightResult result = UMAWeightTransfer.Transfer(TwoPatches(), target, new UMAWeightSettings { Method = UMAWeightMethod.RobustInpaint, Energy = energy });
            BoneWeight1[][] w = result.PerVertex();
            // Ends on the body copy it; the middle, far from both, is filled in half and half.
            Assert.That(result.Report.States[Middle(0)], Is.EqualTo(UMAWeightVertexState.Matched));
            Assert.That(WeightOf(w[Middle(0)], 0), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(result.Report.States[Middle(18)], Is.EqualTo(UMAWeightVertexState.Inpainted));
            Assert.That(WeightOf(w[Middle(18)], 1), Is.EqualTo(0.5f).Within(0.02f));
            // Bone 1 rises steadily from left to right.
            for (int i = 1; i <= 36; i++) Assert.That(WeightOf(w[Middle(i)], 1), Is.GreaterThanOrEqualTo(WeightOf(w[Middle(i - 1)], 1) - 1e-3f), "column " + i);
            AssertNormalized(result, 4);
        }

        [Test]
        public void RobustInpaint_DoesNotCopyClothFacingAnotherWay()
        {
            var p = new List<Vector3>();
            var t = new List<int>();
            Grid(p, t, new Vector3(-0.3f, 0f, 0f), new Vector3(0.6f, 0f, 0f), new Vector3(0f, 0.2f, 0f), 12, 4);
            UMAWeightDonor donor = Donor(p, t, x => One(x.x < 0f ? 0 : 1), 2);
            // An L of cloth: lying on the body for x < 0, then standing up square to it at x = 0.
            var tp = new List<Vector3>();
            var tt = new List<int>();
            const int nu = 20, nv = 2;
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                {
                    float s = i / (float)nu, y = 0.05f + 0.1f * j / nv;
                    tp.Add(s <= 0.5f ? new Vector3(-0.2f + 0.4f * s, y, 0.005f) : new Vector3(0f, y, 0.005f + 0.07f * (s - 0.5f)));
                }
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = j * (nu + 1) + i, b = a + 1, c = a + nu + 1, d = c + 1;
                    tt.AddRange(new[] { a, b, c, b, d, c });
                }
            var target = new UMAWeightTarget { Positions = tp.ToArray(), Triangles = tt.ToArray() };
            UMAWeightResult result = UMAWeightTransfer.Transfer(donor, target, new UMAWeightSettings { Method = UMAWeightMethod.RobustInpaint, MatchDistance = 0.05f, NormalSmoothingRings = 0 });
            for (int j = 0; j <= nv; j++)
            {
                for (int i = 0; i < 8; i++) Assert.That(result.Report.States[j * (nu + 1) + i], Is.EqualTo(UMAWeightVertexState.Matched), "lying " + i);
                for (int i = 13; i <= nu; i++) Assert.That(result.Report.States[j * (nu + 1) + i], Is.EqualTo(UMAWeightVertexState.Inpainted), "standing " + i);
            }
        }

        [TestCase(UMAWeightMethod.RobustInpaint)]
        [TestCase(UMAWeightMethod.TightSurface)]
        public void DeepPokeThrough_IsFilledInsteadOfCopied(UMAWeightMethod method)
        {
            var p = new List<Vector3>();
            var t = new List<int>();
            Grid(p, t, new Vector3(-0.3f, 0f, 0f), new Vector3(0.6f, 0f, 0f), new Vector3(0f, 0.2f, 0f), 12, 4);
            UMAWeightDonor donor = Donor(p, t, x => One(x.x < 0.05f ? 0 : 1), 2);
            // Cloth on the body for x < 0, then sinking 5.5 cm into it (the body's inside is -z).
            var tp = new List<Vector3>();
            var tt = new List<int>();
            Grid(tp, tt, new Vector3(-0.2f, 0.05f, 0f), new Vector3(0.4f, 0f, 0f), new Vector3(0f, 0.1f, 0f), 20, 2);
            for (int v = 0; v < tp.Count; v++) tp[v] = new Vector3(tp[v].x, tp[v].y, tp[v].x < 0f ? 0.005f : 0.005f - 0.3f * tp[v].x);
            var target = new UMAWeightTarget { Positions = tp.ToArray(), Triangles = tt.ToArray() };
            UMAWeightResult result = UMAWeightTransfer.Transfer(donor, target, new UMAWeightSettings { Method = method });
            int deep = 0;
            for (int v = 0; v < tp.Count; v++)
            {
                bool shouldBeDeep = tp[v].z < -0.016f;
                if (!shouldBeDeep) continue;
                deep++;
                Assert.That(result.Report.DeepInside[v], Is.True, "vertex " + v);
                Assert.That(result.Report.States[v], Is.Not.EqualTo(UMAWeightVertexState.Matched), "vertex " + v);
            }
            Assert.That(deep, Is.GreaterThan(0));
            Assert.That(result.Report.Inside, Is.EqualTo(deep));
        }

        [Test]
        public void Volumetric_BlendsClothFarFromTheBody()
        {
            UMAWeightTarget target = Strip();
            UMAWeightResult result = UMAWeightTransfer.Transfer(TwoPatches(), target, new UMAWeightSettings { Method = UMAWeightMethod.Volumetric, FieldCellSize = 0.02f, FieldMargin = 0.1f });
            BoneWeight1[][] w = result.PerVertex();
            Assert.That(result.Report.States[Middle(0)], Is.EqualTo(UMAWeightVertexState.Matched));
            Assert.That(result.Report.States[Middle(18)], Is.EqualTo(UMAWeightVertexState.Field));
            Assert.That(WeightOf(w[Middle(18)], 1), Is.EqualTo(0.5f).Within(0.05f));
            Assert.That(WeightOf(w[Middle(9)], 0), Is.GreaterThan(WeightOf(w[Middle(9)], 1)));
            AssertNormalized(result, 4);
        }

        [Test]
        public void WeightField_GpuGivesTheSameFieldAsTheCpu()
        {
            if (!UMAWeightField.GpuSupported) Assert.Ignore("This machine cannot run compute shaders.");
            UMAWeightDonor donor = TwoPatches();
            var surface = new UMAWeightSurface(donor.Positions, donor.Triangles, donor.VertexNormals);
            var bounds = new Bounds(new Vector3(0f, 0.05f, 0f), new Vector3(1.2f, 0.4f, 0.3f));
            List<UMAWeightField.Level> levels = UMAWeightField.BuildLevels(surface, donor.Weights, bounds, 0.03f);
            List<int> columns = UMAWeightField.Columns(levels);
            Assert.That(columns, Is.EqualTo(new List<int> { 0, 1 }));
            float[] cpu = UMAWeightField.SolveBatchCpu(levels, columns, 0, columns.Count);
            float[] gpu = UMAWeightField.SolveBatchGpu(levels, columns, 0, columns.Count);
            Assert.That(gpu.Length, Is.EqualTo(cpu.Length));
            float worst = 0f;
            for (int i = 0; i < cpu.Length; i++) worst = Mathf.Max(worst, Mathf.Abs(cpu[i] - gpu[i]));
            Assert.That(worst, Is.LessThan(1e-4f));
        }

        // ---- shared rules ---------------------------------------------------------------------------------------

        [TestCase(UMAWeightMethod.ExactMatch)]
        [TestCase(UMAWeightMethod.TightSurface)]
        [TestCase(UMAWeightMethod.RobustInpaint)]
        [TestCase(UMAWeightMethod.Volumetric)]
        public void VerticesAtOnePlace_EndWithOneSetOfWeights(UMAWeightMethod method)
        {
            // Two halves of the strip, their shared edge doubled (a seam or UV island edge).
            var p = new List<Vector3>();
            var t = new List<int>();
            Grid(p, t, new Vector3(-0.45f, 0.02f, 0.005f), new Vector3(0.45f, 0f, 0f), new Vector3(0f, 0.06f, 0f), 18, 2);
            int half = p.Count;
            Grid(p, t, new Vector3(0f, 0.02f, 0.005f), new Vector3(0.45f, 0f, 0f), new Vector3(0f, 0.06f, 0f), 18, 2);
            var target = new UMAWeightTarget { Positions = p.ToArray(), Triangles = t.ToArray() };
            UMAWeightResult result = UMAWeightTransfer.Transfer(TwoPatches(), target, new UMAWeightSettings { Method = method, FieldCellSize = 0.02f, FieldMargin = 0.1f });
            BoneWeight1[][] w = result.PerVertex();
            int pairs = 0;
            for (int a = 0; a < half; a++)
                for (int b = half; b < p.Count; b++)
                {
                    if ((p[a] - p[b]).sqrMagnitude > 1e-12f) continue;
                    pairs++;
                    Assert.That(w[b].Length, Is.EqualTo(w[a].Length));
                    for (int k = 0; k < w[a].Length; k++)
                    {
                        Assert.That(w[b][k].boneIndex, Is.EqualTo(w[a][k].boneIndex));
                        Assert.That(w[b][k].weight, Is.EqualTo(w[a][k].weight));
                    }
                }
            Assert.That(pairs, Is.EqualTo(3));
        }

        [Test]
        public void LockedVertices_KeepTheirWeights_AndTheFillMeetsThem()
        {
            UMAWeightTarget target = Strip();
            int n = target.VertexCount;
            target.Locked = new bool[n];
            target.CurrentWeights = new BoneWeight1[n][];
            for (int v = 0; v < n; v++)
            {
                target.CurrentWeights[v] = One(2);
                target.Locked[v] = target.Positions[v].x < -0.1f;
            }
            UMAWeightResult result = UMAWeightTransfer.Transfer(TwoPatches(3), target, new UMAWeightSettings { Method = UMAWeightMethod.RobustInpaint });
            BoneWeight1[][] w = result.PerVertex();
            for (int v = 0; v < n; v++)
            {
                if (!target.Locked[v]) continue;
                Assert.That(result.Report.States[v], Is.EqualTo(UMAWeightVertexState.Locked));
                Assert.That(w[v].Length, Is.EqualTo(1));
                Assert.That(w[v][0].boneIndex, Is.EqualTo(2));
            }
            // Next to the locked part the fill starts from bone 2, not from the body's bone 0.
            Assert.That(WeightOf(w[Middle(16)], 2), Is.GreaterThan(0.5f));
            Assert.That(WeightOf(w[Middle(36)], 1), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void MaxBones_AreKept_AndWeightsSumToOne()
        {
            var p = new List<Vector3>();
            var t = new List<int>();
            Grid(p, t, new Vector3(-0.3f, 0f, 0f), new Vector3(0.6f, 0f, 0f), new Vector3(0f, 0.3f, 0f), 6, 6);
            UMAWeightDonor donor = Donor(p, t, x =>
            {
                var w = new BoneWeight1[6];
                for (int b = 0; b < 6; b++) w[b] = new BoneWeight1 { boneIndex = b, weight = (b + 1) / 21f };
                return w;
            }, 6);
            UMAWeightTarget target = Strip(-0.25f, 0.25f, 10);
            UMAWeightResult result = UMAWeightTransfer.Transfer(donor, target, new UMAWeightSettings { Method = UMAWeightMethod.TightSurface, MaxBones = 2 });
            AssertNormalized(result, 2);
            foreach (BoneWeight1[] w in result.PerVertex())
            {
                Assert.That(w[0].boneIndex, Is.EqualTo(5));
                Assert.That(w[1].boneIndex, Is.EqualTo(4));
            }
        }

        [TestCase("LeftUpLeg", "RightUpLeg")]
        [TestCase("RightHand", "LeftHand")]
        [TestCase("hand_L", "hand_R")]
        [TestCase("thigh.r", "thigh.l")]
        [TestCase("L_Toe", "R_Toe")]
        [TestCase("Spine", "Spine")]
        public void MirrorName_SwapsTheSides(string name, string expected)
        {
            Assert.That(UMAWeightTransfer.MirrorName(name), Is.EqualTo(expected));
        }

        [Test]
        public void MirrorLeftRight_MakesBothSidesMatch()
        {
            string[] names = { "LeftThigh", "RightThigh" };
            // The right patch is nearer the middle than the left one, so the plain result is lopsided.
            var p = new List<Vector3>();
            var t = new List<int>();
            Grid(p, t, new Vector3(-0.5f, 0f, 0f), new Vector3(0.2f, 0f, 0f), new Vector3(0f, 0.1f, 0f), 8, 4);
            Grid(p, t, new Vector3(0.2f, 0f, 0f), new Vector3(0.3f, 0f, 0f), new Vector3(0f, 0.1f, 0f), 12, 4);
            UMAWeightDonor donor = Donor(p, t, x => One(x.x < 0f ? 0 : 1), 2, names);
            UMAWeightTarget target = Strip(-0.43f, 0.43f, 43);
            BoneWeight1[][] plain = UMAWeightTransfer.Transfer(donor, target, new UMAWeightSettings { Method = UMAWeightMethod.RobustInpaint }).PerVertex();
            BoneWeight1[][] w = UMAWeightTransfer.Transfer(donor, target, new UMAWeightSettings { Method = UMAWeightMethod.RobustInpaint, MirrorLeftRight = true, MirrorAxis = 0, MirrorPlane = 0f, MirrorTolerance = 0.002f }).PerVertex();
            const int row = 44;
            float lopsided = 0f;
            for (int i = 0; i <= 43; i++)
            {
                int a = row + i, b = row + 43 - i;
                lopsided = Mathf.Max(lopsided, Mathf.Abs(WeightOf(plain[a], 0) - WeightOf(plain[b], 1)));
                Assert.That(WeightOf(w[a], 0), Is.EqualTo(WeightOf(w[b], 1)).Within(0.01f), "column " + i);
            }
            Assert.That(lopsided, Is.GreaterThan(0.05f), "the test needs a lopsided starting point");
        }

        // ---- poses ----------------------------------------------------------------------------------------------

        /// <summary>A pose: bone 0 still, bone 1 turned about the z axis through (0.3, 0, 0).</summary>
        private static Matrix4x4[] Turned(float degrees) => new[]
        {
            Matrix4x4.identity,
            Matrix4x4.Translate(new Vector3(0.3f, 0f, 0f)) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, degrees)) * Matrix4x4.Translate(new Vector3(-0.3f, 0f, 0f))
        };

        [Test]
        public void SimulatedFit_FindsTheWeightsThatReproduceThePoses()
        {
            UMAWeightTarget target = Strip();
            int n = target.VertexCount;
            // The cloth "hangs" with bone 1's weight rising linearly from x = -0.3 to 0.3.
            float Truth(Vector3 p) => Mathf.Clamp01((p.x + 0.3f) / 0.6f);
            var poses = new List<UMAWeightPose>();
            foreach (float angle in new[] { 25f, -20f, 40f, -35f })
            {
                Matrix4x4[] m = Turned(angle);
                var placed = new Vector3[n];
                for (int v = 0; v < n; v++)
                {
                    float s = Truth(target.Positions[v]);
                    placed[v] = m[0].MultiplyPoint3x4(target.Positions[v]) * (1f - s) + m[1].MultiplyPoint3x4(target.Positions[v]) * s;
                }
                poses.Add(new UMAWeightPose { Name = angle.ToString(), BoneMatrices = m, TargetPositions = placed });
            }
            var settings = new UMAWeightSettings { Method = UMAWeightMethod.SimulatedFit, Poses = poses, FitPrior = 0.001f, FitSmoothness = 0.01f, FitIterations = 4 };
            UMAWeightResult result = UMAWeightTransfer.Transfer(TwoPatches(), target, settings);
            Assert.That(result.Report.Used, Is.EqualTo(UMAWeightMethod.SimulatedFit));
            BoneWeight1[][] w = result.PerVertex();
            // Columns 8 to 28 (x -0.25 to 0.25) are off the body; nearer the patches the cloth keeps the body's weights.
            for (int i = 8; i <= 28; i++)
            {
                int v = Middle(i);
                Assert.That(result.Report.States[v], Is.EqualTo(UMAWeightVertexState.Fitted), "column " + i);
                Assert.That(WeightOf(w[v], 1), Is.EqualTo(Truth(target.Positions[v])).Within(0.06f), "column " + i);
            }
            AssertNormalized(result, 4);
        }

        [Test]
        public void SimulatedFit_WithoutDrapedPoses_UsesRobustInpaint()
        {
            UMAWeightResult result = UMAWeightTransfer.Transfer(TwoPatches(), Strip(), new UMAWeightSettings { Method = UMAWeightMethod.SimulatedFit });
            Assert.That(result.Report.Used, Is.EqualTo(UMAWeightMethod.RobustInpaint));
            Assert.That(result.Report.Messages, Has.Some.Contains("Simulated Fit"));
        }

        /// <summary>A body plane at z = 0 on bone 0, and the pose that lifts bone 0 by 5 cm (bone 1 stays).</summary>
        private static List<UMAWeightPose> Lifted(UMAWeightDonor donor)
        {
            Matrix4x4 lift = Matrix4x4.Translate(new Vector3(0f, 0f, 0.05f));
            var positions = new Vector3[donor.Positions.Length];
            for (int v = 0; v < positions.Length; v++) positions[v] = lift.MultiplyPoint3x4(donor.Positions[v]);
            return new List<UMAWeightPose> { new UMAWeightPose { Name = "lift", BoneMatrices = new[] { lift, Matrix4x4.identity }, DonorPositions = positions } };
        }

        [Test]
        public void Score_CountsClothLeftInsideThePosedBody()
        {
            var p = new List<Vector3>();
            var t = new List<int>();
            Grid(p, t, new Vector3(-0.3f, 0f, 0f), new Vector3(0.6f, 0f, 0f), new Vector3(0f, 0.2f, 0f), 12, 4);
            UMAWeightDonor donor = Donor(p, t, x => One(0), 2);
            UMAWeightTarget target = Strip(-0.2f, 0.2f, 8);
            List<UMAWeightPose> poses = Lifted(donor);
            UMAWeightResult follows = UMAWeightTransfer.Transfer(donor, target, new UMAWeightSettings { Method = UMAWeightMethod.TightSurface });
            int n = target.VertexCount;
            var stays = new UMAWeightResult { BonesPerVertex = new byte[n], BoneWeights = new BoneWeight1[n] };
            for (int v = 0; v < n; v++) { stays.BonesPerVertex[v] = 1; stays.BoneWeights[v] = new BoneWeight1 { boneIndex = 1, weight = 1f }; }
            UMAWeightScore good = UMAWeightTransfer.Score(donor, target, follows, poses);
            UMAWeightScore bad = UMAWeightTransfer.Score(donor, target, stays, poses);
            Assert.That(good.InsideShare, Is.EqualTo(0f));
            Assert.That(bad.InsideShare, Is.EqualTo(1f));
            Assert.That(bad.MaxInsideDepth, Is.EqualTo(0.045f).Within(0.002f));
            Assert.That(good.MeanStretch, Is.LessThan(1e-4f));
        }

        [Test]
        public void Auto_KeepsTheMethodThatScoresBest()
        {
            UMAWeightDonor donor = TwoPatches();
            var poses = new List<UMAWeightPose>();
            foreach (float angle in new[] { 20f, -20f })
            {
                Matrix4x4[] m = Turned(angle);
                var positions = new Vector3[donor.Positions.Length];
                for (int v = 0; v < positions.Length; v++) positions[v] = m[donor.Positions[v].x < 0f ? 0 : 1].MultiplyPoint3x4(donor.Positions[v]);
                poses.Add(new UMAWeightPose { Name = angle.ToString(), BoneMatrices = m, DonorPositions = positions });
            }
            UMAWeightResult result = UMAWeightTransfer.Transfer(donor, Strip(), new UMAWeightSettings { Method = UMAWeightMethod.Auto, Poses = poses, FieldCellSize = 0.02f, FieldMargin = 0.1f });
            Assert.That(result.Report.Scores.Count, Is.EqualTo(3));
            UMAWeightScore best = result.Report.Scores[0];
            foreach (UMAWeightScore s in result.Report.Scores) if (s.Total < best.Total) best = s;
            Assert.That(result.Report.Used, Is.EqualTo(best.Method));
            AssertNormalized(result, 4);
        }

        [Test]
        public void Auto_WithoutPoses_UsesRobustInpaint()
        {
            UMAWeightResult result = UMAWeightTransfer.Transfer(TwoPatches(), Strip(), new UMAWeightSettings { Method = UMAWeightMethod.Auto });
            Assert.That(result.Report.Used, Is.EqualTo(UMAWeightMethod.RobustInpaint));
            Assert.That(result.Report.Scores, Is.Empty);
        }

        [TestCase(UMAWeightMethod.RobustInpaint)]
        [TestCase(UMAWeightMethod.TightSurface)]
        [TestCase(UMAWeightMethod.Volumetric)]
        [TestCase(UMAWeightMethod.Auto)]
        public void SlotBuilderOverload_WeighsAMeshFromACapturedRenderer(UMAWeightMethod method)
        {
            // A two-bone capsule character (lower and upper bone, the upper one's weight rising with height), captured
            // as the slot builders capture a character, and a looser tube with a flared hem round it.
            var root = new GameObject("Weighting capsule (test)") { hideFlags = HideFlags.HideAndDontSave };
            Mesh mesh = null;
            try
            {
                Transform lower = new GameObject("Lower").transform; lower.SetParent(root.transform, false); lower.localPosition = new Vector3(0f, 0.7f, 0f);
                Transform upper = new GameObject("Upper").transform; upper.SetParent(lower, false); upper.localPosition = new Vector3(0f, 0.45f, 0f);
                const int segments = 32, rings = 20;
                const float radius = 0.16f, bottom = 0.7f, top = 1.6f;
                var vertices = new List<Vector3>();
                var weights = new List<BoneWeight>();
                var triangles = new List<int>();
                for (int j = 0; j <= rings; j++)
                {
                    float y = Mathf.Lerp(bottom, top, j / (float)rings);
                    for (int i = 0; i < segments; i++)
                    {
                        float a = i * Mathf.PI * 2f / segments;
                        vertices.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                        float t = j / (float)rings;
                        weights.Add(new BoneWeight { boneIndex0 = 0, weight0 = 1f - t, boneIndex1 = 1, weight1 = t });
                    }
                }
                for (int j = 0; j < rings; j++)
                    for (int i = 0; i < segments; i++)
                    {
                        int i1 = (i + 1) % segments;
                        int p00 = j * segments + i, p10 = j * segments + i1, p01 = (j + 1) * segments + i, p11 = (j + 1) * segments + i1;
                        triangles.AddRange(new[] { p00, p01, p10, p10, p01, p11 });
                    }
                mesh = new Mesh();
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.boneWeights = weights.ToArray();
                mesh.bindposes = new[] { lower.worldToLocalMatrix * root.transform.localToWorldMatrix, upper.worldToLocalMatrix * root.transform.localToWorldMatrix };
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.bones = new[] { lower, upper };
                renderer.rootBone = lower;
                var skin = new UMA.Editors.UMASlotSkinTransfer.TargetData
                {
                    Renderer = renderer,
                    SurfaceRenderers = new[] { renderer },
                    SourceMesh = mesh,
                    Bones = renderer.bones,
                    BindPoses = mesh.bindposes,
                    BoneNameHashes = new[] { UMAUtils.StringToHash(lower.name), UMAUtils.StringToHash(upper.name) },
                    RootBone = lower
                };
                UMA.Editors.UMASlotSkinTransfer.TargetSurface surface = UMA.Editors.UMASlotSkinTransfer.BuildTargetSurface(skin);

                // The tube: 1 cm off the capsule down to y = 0.9, then flaring out to 12 cm at y = 0.75.
                var tube = new List<Vector3>();
                var tubeTriangles = new List<int>();
                const int tubeRings = 16;
                for (int j = 0; j <= tubeRings; j++)
                {
                    float y = Mathf.Lerp(0.75f, 1.5f, j / (float)tubeRings);
                    float r = radius + 0.01f + Mathf.Max(0f, (0.9f - y) / 0.15f) * 0.11f;
                    for (int i = 0; i < segments; i++)
                    {
                        float a = i * Mathf.PI * 2f / segments;
                        tube.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                    }
                }
                for (int j = 0; j < tubeRings; j++)
                    for (int i = 0; i < segments; i++)
                    {
                        int i1 = (i + 1) % segments;
                        int p00 = j * segments + i, p10 = j * segments + i1, p01 = (j + 1) * segments + i, p11 = (j + 1) * segments + i1;
                        tubeTriangles.AddRange(new[] { p00, p01, p10, p10, p01, p11 });
                    }
                var source = new UMA.Editors.UMASlotSkinTransfer.SourceGeometry { TargetLocalVertices = tube.ToArray(), Triangles = tubeTriangles.ToArray() };
                UMA.Editors.UMASlotSkinTransfer.WeightTransferResult result = UMA.Editors.UMASlotSkinTransfer.TransferWeights(source, skin, surface,
                    new UMAWeightSettings { Method = method, FieldCellSize = 0.03f, FieldMargin = 0.1f }, 1, out UMAWeightReport report);
                Assert.That(result.BonesPerVertex.Length, Is.EqualTo(tube.Count));
                Assert.That(report.Used, Is.EqualTo(method == UMAWeightMethod.Auto ? UMAWeightMethod.RobustInpaint : method));
                if (method == UMAWeightMethod.Auto) Assert.That(report.Messages, Has.Some.Contains("Auto needs poses"));
                int offset = 0;
                float topUpper = 0f, bottomUpper = 0f;
                for (int v = 0; v < tube.Count; v++)
                {
                    float sum = 0f, upperWeight = 0f;
                    for (int k = 0; k < result.BonesPerVertex[v]; k++)
                    {
                        BoneWeight1 w = result.BoneWeights[offset + k];
                        sum += w.weight;
                        if (w.boneIndex == 1) upperWeight += w.weight;
                    }
                    offset += result.BonesPerVertex[v];
                    Assert.That(sum, Is.EqualTo(1f).Within(1e-4f), "vertex " + v);
                    if (v >= tubeRings * segments) topUpper += upperWeight / segments;
                    if (v < segments) bottomUpper += upperWeight / segments;
                }
                // The capsule's upper bone weight is (y - 0.7) / 0.9: about 0.89 at the tube's top, and the flared hem,
                // copied or filled from the cloth above it, keeps little of it.
                Assert.That(topUpper, Is.EqualTo(0.89f).Within(0.05f));
                Assert.That(bottomUpper, Is.LessThan(0.35f));
                Assert.That(report.Matched, Is.GreaterThan(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Regions_KeepVerticesToTheirPartOfTheBody()
        {
            UMAWeightDonor donor = TwoPatches();
            // Region 1 may only copy the left patch; the cloth lies over the right one.
            var leftOnly = new bool[donor.TriangleCount];
            for (int k = 0; k < leftOnly.Length; k++) leftOnly[k] = donor.Positions[donor.Triangles[k * 3]].x < 0f;
            UMAWeightTarget target = Strip(0.32f, 0.48f, 8);
            target.Region = new int[target.VertexCount];
            for (int v = 0; v < target.VertexCount; v++) target.Region[v] = 1;
            var settings = new UMAWeightSettings { Method = UMAWeightMethod.TightSurface, RegionTriangles = new List<bool[]> { null, leftOnly } };
            foreach (BoneWeight1[] w in UMAWeightTransfer.Transfer(donor, target, settings).PerVertex()) Assert.That(WeightOf(w, 0), Is.EqualTo(1f));
            target.Region = null;
            foreach (BoneWeight1[] w in UMAWeightTransfer.Transfer(donor, target, settings).PerVertex()) Assert.That(WeightOf(w, 1), Is.EqualTo(1f));
        }
    }
}
