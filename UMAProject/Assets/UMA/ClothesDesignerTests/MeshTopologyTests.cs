#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>Mesh Topology: even triangles, or a grid along each piece's grain.</summary>
    public sealed class MeshTopologyTests
    {
        private static PatternMesh Mesh(ClothesPattern pattern, float edgeCm = 2f) =>
            PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), edgeCm);

        /// <summary>The share of a piece's inside edges (both ends inside) within 5 degrees of its grain or across it.</summary>
        private static float GrainAligned(PatternMesh mesh, int piece, float grainDeg)
        {
            int aligned = 0, total = 0;
            var seen = new HashSet<long>();
            for (int t = 0; t < mesh.triangles.Count; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = mesh.triangles[t + e], b = mesh.triangles[t + (e + 1) % 3];
                    if (mesh.piece[a] != piece || mesh.boundaryEdge[a] >= 0 || mesh.boundaryEdge[b] >= 0) continue;
                    if (!seen.Add(a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a)) continue;
                    Vector2 d = mesh.drawn[b] - mesh.drawn[a];
                    float angle = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + grainDeg;
                    float off = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 90f) * 90f));
                    total++;
                    if (off < 5f) aligned++;
                }
            }
            return total > 0 ? aligned / (float)total : 0f;
        }

        [Test]
        public void GrainGrid_EdgesFollowTheGrainAndAcrossIt_EvenTrianglesDoNot()
        {
            ClothesPattern vest = VestPattern.Create();
            try
            {
                float even = GrainAligned(Mesh(vest), 0, vest.pieces[0].grainAngle);
                vest.meshTopology = MeshTopology.GrainGrid;
                float grid = GrainAligned(Mesh(vest), 0, vest.pieces[0].grainAngle);
                // A grid's inside edges are two thirds along or across (the rest its diagonals); an even mesh's a third.
                Assert.That(grid, Is.GreaterThan(0.55f), "grain grid");
                Assert.That(even, Is.LessThan(0.45f), "even");

                // A piece cut at an angle: the grid turns with its grain.
                vest.pieces[0].grainAngle = 30f;
                Assert.That(GrainAligned(Mesh(vest), 0, 30f), Is.GreaterThan(0.55f), "turned grain");
            }
            finally { Object.DestroyImmediate(vest); }
        }

        [Test]
        public void GrainGrid_EverySample_MeshesWithoutWarnings()
        {
            foreach (var sample in SamplePatterns.All)
            {
                ClothesPattern pattern = sample.make();
                try
                {
                    pattern.meshTopology = MeshTopology.GrainGrid;
                    PatternMesh mesh = Mesh(pattern);
                    Assert.That(mesh.warnings, Is.Empty, sample.name);
                    for (int t = 0; t < mesh.triangles.Count; t += 3)
                    {
                        Vector2 a = mesh.flat[mesh.triangles[t]], b = mesh.flat[mesh.triangles[t + 1]], c = mesh.flat[mesh.triangles[t + 2]];
                        Assert.That((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x), Is.GreaterThan(1e-5f), sample.name + " triangle " + t / 3);
                    }
                    Assert.That(UvLayout.FindProblems(pattern, mesh), Is.Empty, sample.name);
                }
                finally { Object.DestroyImmediate(pattern); }
            }
        }

        [Test]
        public void Revision_ChangesWithTheTopology_OnlyWhenItIsSet()
        {
            ClothesPattern vest = VestPattern.Create();
            try
            {
                string even = vest.ComputeRevision();
                vest.meshTopology = MeshTopology.GrainGrid;
                Assert.That(vest.ComputeRevision(), Is.Not.EqualTo(even));
                vest.meshTopology = MeshTopology.Even;
                Assert.That(vest.ComputeRevision(), Is.EqualTo(even));
            }
            finally { Object.DestroyImmediate(vest); }
        }

        [Test]
        public void GrainGrid_Drapes_WithTheSeamsClosed()
        {
            var capsule = new CapsuleBodyFixture();
            ClothesPattern vest = VestPattern.Create();
            vest.meshTopology = MeshTopology.GrainGrid;
            ClothesFit fit = ScriptableObject.CreateInstance<ClothesFit>();
            var session = new FitSession { Pattern = vest, Fit = fit, Body = capsule.Body, PreferCpu = true };
            try
            {
                fit.pattern = vest;
                fit.overrideMeshLevel = true;
                fit.meshLevel = MeshLevel.Draft;
                session.Rebuild();
                Assert.That(session.HasCloth, Is.True, string.Join(" ", session.Messages));
                session.Step(200);
                FitMetrics m = session.Measure();
                Assert.That(m.maxStitchGapMm, Is.LessThan(5f), m.ToString());
            }
            finally
            {
                session.Dispose();
                Object.DestroyImmediate(fit);
                Object.DestroyImmediate(vest);
                capsule.Dispose();
            }
        }
    }
}
#endif
