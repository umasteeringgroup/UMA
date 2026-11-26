#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UMA.Editors.Weighting;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>The bake's bone weights (<see cref="GarmentWeights"/>) on a vest draped on the capsule body.</summary>
    public sealed class GarmentWeightsTests
    {
        private CapsuleBodyFixture capsule;
        private readonly List<Object> made = new List<Object>();
        private readonly List<System.IDisposable> disposables = new List<System.IDisposable>();

        [SetUp]
        public void Make() => capsule = new CapsuleBodyFixture();

        [TearDown]
        public void Clean()
        {
            foreach (System.IDisposable d in disposables) d.Dispose();
            disposables.Clear();
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            capsule.Dispose();
        }

        private FitSession DrapedVest()
        {
            ClothesPattern pattern = VestPattern.Create();
            ClothesFit fit = ScriptableObject.CreateInstance<ClothesFit>();
            made.Add(pattern); made.Add(fit);
            fit.pattern = pattern;
            fit.overrideMeshLevel = true;
            fit.meshLevel = MeshLevel.Draft;
            var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = true };
            disposables.Add(session);
            session.Rebuild();
            Assert.That(session.HasCloth, Is.True, string.Join(" ", session.Messages));
            session.Step(200);
            return session;
        }

        [TestCase(UMAWeightMethod.RobustInpaint)]
        [TestCase(UMAWeightMethod.TightSurface)]
        [TestCase(UMAWeightMethod.Volumetric)]
        [TestCase(UMAWeightMethod.ExactMatch)]
        public void EveryMethod_GivesSewnCopiesOneSet_AndWeightsSumToOne(UMAWeightMethod method)
        {
            FitSession session = DrapedVest();
            session.Fit.weightMethod = method;
            session.Fit.weightMaxBones = 2;
            PatternMesh mesh = session.Mesh;
            int[] group = GarmentBaker.WeldGroups(mesh.VertexCount, mesh.stitches);
            Vector3[] positions = GarmentBaker.Weld((Vector3[])session.SyncPositions().Clone(), group);
            var messages = new List<string>();
            UMAWeightResult result = GarmentWeights.Transfer(session, mesh, positions, group, null, null, messages);
            Assert.That(result.Report.Used, Is.EqualTo(method));
            Assert.That(messages, Has.Some.StartsWith("Weights: "));
            BoneWeight1[][] w = result.PerVertex();
            for (int v = 0; v < w.Length; v++)
            {
                Assert.That(w[v].Length, Is.InRange(1, 2));
                float sum = 0f;
                foreach (BoneWeight1 b in w[v]) { Assert.That(b.boneIndex, Is.InRange(0, 1)); sum += b.weight; }
                Assert.That(sum, Is.EqualTo(1f).Within(1e-4f));
                int g = group[v];
                Assert.That(w[v].Length, Is.EqualTo(w[g].Length), "sewn copy " + v);
                for (int k = 0; k < w[v].Length; k++) Assert.That(w[v][k].weight, Is.EqualTo(w[g][k].weight), "sewn copy " + v);
            }
            // The vest lies on the capsule: most of it copies the body (the hem below the capsule is filled in).
            Assert.That(result.Report.Matched + result.Report.Exact, Is.GreaterThan(w.Length / 2));
        }

        [Test]
        public void Auto_OnABodyThatCannotBePosed_UsesRobustInpaint()
        {
            FitSession session = DrapedVest();
            session.Fit.weightMethod = UMAWeightMethod.Auto;
            PatternMesh mesh = session.Mesh;
            var messages = new List<string>();
            GarmentWeights.Poses poses = GarmentWeights.Prepare(session, mesh, session.SyncPositions(), messages);
            Assert.That(poses, Is.Null);
            Assert.That(messages, Has.Some.Contains("cannot be put in test poses"));
            int[] group = GarmentBaker.WeldGroups(mesh.VertexCount, mesh.stitches);
            UMAWeightResult result = GarmentWeights.Transfer(session, mesh, GarmentBaker.Weld((Vector3[])session.SyncPositions().Clone(), group), group, poses, null, messages);
            Assert.That(result.Report.Used, Is.EqualTo(UMAWeightMethod.RobustInpaint));
        }

        [Test]
        public void TheCapsule_UpperBoneWeightRisesUpTheVest()
        {
            FitSession session = DrapedVest();
            PatternMesh mesh = session.Mesh;
            int[] group = GarmentBaker.WeldGroups(mesh.VertexCount, mesh.stitches);
            Vector3[] positions = GarmentBaker.Weld((Vector3[])session.SyncPositions().Clone(), group);
            UMAWeightResult result = GarmentWeights.Transfer(session, mesh, positions, group, null, null, new List<string>());
            BoneWeight1[][] w = result.PerVertex();
            float bottom = float.MaxValue, top = float.MinValue;
            foreach (Vector3 p in positions) { bottom = Mathf.Min(bottom, p.y); top = Mathf.Max(top, p.y); }
            float lowSum = 0f, highSum = 0f;
            int low = 0, high = 0;
            for (int v = 0; v < positions.Length; v++)
            {
                float upper = 0f;
                foreach (BoneWeight1 b in w[v]) if (b.boneIndex == 1) upper += b.weight;
                // The capsule's upper bone rises from 0 at its bottom to 1 at its top: the vest's top quarter has more of it.
                if (positions[v].y < Mathf.Lerp(bottom, top, 0.25f)) { lowSum += upper; low++; }
                else if (positions[v].y > Mathf.Lerp(bottom, top, 0.75f)) { highSum += upper; high++; }
            }
            Assert.That(low, Is.GreaterThan(0));
            Assert.That(high, Is.GreaterThan(0));
            Assert.That(highSum / high, Is.GreaterThan(lowSum / low + 0.2f));
        }
    }
}
#endif
