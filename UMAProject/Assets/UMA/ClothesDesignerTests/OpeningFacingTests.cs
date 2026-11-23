#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>The facing inside the openings and the rim round them.</summary>
    public sealed class OpeningFacingTests
    {
        private readonly List<Object> made = new List<Object>();
        private ClothesPattern pattern;
        private PatternMesh mesh;
        private Vector3[] positions;
        private int[] triangles, group;

        [SetUp]
        public void Make()
        {
            pattern = VestPattern.Create();
            made.Add(pattern);
            mesh = PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), 2f);
            // Each piece rolled round a cylinder, the front facing +z and the back -z, seams meeting at the sides.
            positions = new Vector3[mesh.VertexCount];
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                Vector2 f = mesh.flat[v] * 0.01f;
                float angle = (mesh.piece[v] == 0 ? 0f : Mathf.PI) + (mesh.piece[v] == 0 ? 1f : -1f) * f.x / 0.17f;
                positions[v] = new Vector3(Mathf.Sin(angle) * 0.17f, f.y, Mathf.Cos(angle) * 0.17f);
            }
            group = GarmentBaker.WeldGroups(mesh.VertexCount, mesh.stitches);
            triangles = mesh.triangles.ToArray();
            // Every triangle facing out of the cylinder (the back is mirrored, so check each).
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 n = Vector3.Cross(positions[triangles[t + 1]] - positions[triangles[t]], positions[triangles[t + 2]] - positions[triangles[t]]);
                Vector3 c = (positions[triangles[t]] + positions[triangles[t + 1]] + positions[triangles[t + 2]]) / 3f;
                if (Vector3.Dot(n, new Vector3(c.x, 0f, c.z)) < 0f) { int tmp = triangles[t + 1]; triangles[t + 1] = triangles[t + 2]; triangles[t + 2] = tmp; }
            }
        }

        [TearDown]
        public void Clean()
        {
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private bool[] Openings()
        {
            OpeningFacing.OpeningSegments(mesh, pattern, triangles, out bool[] at);
            return at;
        }

        [Test]
        public void Rim_ReachesBothCornersOfEveryOpening()
        {
            // An edge's last point is the next edge's first; the segment into it still belongs to the
            // opening. Found by the maintainer: the rim stopped one segment short at one end of each opening.
            OpeningFacing.Output o = OpeningFacing.Build(mesh, pattern, positions, triangles, group, false, 0.05f, true, 0.003f);
            var rimEdges = new HashSet<(int, int)>();
            for (int t = triangles.Length; t < o.triangles.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = o.triangles[t + k], b = o.triangles[t + (k + 1) % 3];
                    if (a < mesh.VertexCount && b < mesh.VertexCount) rimEdges.Add((Mathf.Min(a, b), Mathf.Max(a, b)));
                }
            }
            int checkedEdges = 0;
            for (int p = 0; p < pattern.pieces.Count; p++)
            {
                int count = pattern.pieces[p].edges.Count;
                for (int e = 0; e < count; e++)
                {
                    if (!pattern.pieces[p].edges[e].opening) continue;
                    bool start = false, end = false;
                    foreach ((int a, int b) in rimEdges)
                    {
                        if (mesh.piece[a] != p) continue;
                        int ea = mesh.boundaryEdge[a], eb = mesh.boundaryEdge[b];
                        if ((ea == e && eb == (e + 1) % count) || (eb == e && ea == (e + 1) % count)) end = true;
                        if (ea == e && eb == e) start = true;
                    }
                    Assert.That(start, Is.True, "piece " + p + " opening edge " + e + " has a rim");
                    Assert.That(end, Is.True, "piece " + p + " opening edge " + e + " rim reaches its end corner");
                    checkedEdges++;
                }
            }
            Assert.That(checkedEdges, Is.GreaterThanOrEqualTo(6));
            // Every outline segment along an opening has its rim, and no seam segment has one.
            List<(int a, int b, int third)> segments = OpeningFacing.OpeningSegments(mesh, pattern, triangles, out _);
            Assert.That(o.rimTriangles, Is.EqualTo(segments.Count * 2));
            foreach ((int a, int b, int _) in segments) Assert.That(rimEdges.Contains((Mathf.Min(a, b), Mathf.Max(a, b))), Is.True);
        }

        private static Vector3 Normal(Vector3[] p, int[] tris, int t) => Vector3.Cross(p[tris[t + 1]] - p[tris[t]], p[tris[t + 2]] - p[tris[t]]).normalized;

        [Test]
        public void Facing_ReachesTheDepth_FacesIn_AndCopiesTheCloth()
        {
            OpeningFacing.Output o = OpeningFacing.Build(mesh, pattern, positions, triangles, group, true, 0.05f, false, 0f);
            Assert.That(o.facingTriangles, Is.GreaterThan(100));
            Assert.That(o.rimTriangles, Is.EqualTo(0));
            Assert.That(o.positions.Length, Is.EqualTo(mesh.VertexCount + o.addedVertices));
            Assert.That(o.triangles.Length, Is.EqualTo(triangles.Length + o.facingTriangles * 3));
            float[] distance = OpeningFacing.DistanceFromOpenings(positions, triangles, group, Openings());
            for (int t = triangles.Length; t < o.triangles.Length; t += 3)
            {
                int a = o.sourceOf[o.triangles[t]], b = o.sourceOf[o.triangles[t + 1]], c = o.sourceOf[o.triangles[t + 2]];
                Assert.That(Mathf.Max(distance[a], Mathf.Max(distance[b], distance[c])), Is.LessThanOrEqualTo(0.05f + 1e-5f));
                // Facing into the cylinder: opposite to the cloth there.
                Vector3 n = Normal(o.positions, o.triangles, t);
                Vector3 centre = (o.positions[o.triangles[t]] + o.positions[o.triangles[t + 1]] + o.positions[o.triangles[t + 2]]) / 3f;
                Assert.That(Vector3.Dot(n, new Vector3(centre.x, 0f, centre.z)), Is.LessThan(0f), "triangle " + t / 3);
            }
            for (int v = mesh.VertexCount; v < o.positions.Length; v++)
            {
                int s = o.sourceOf[v];
                Assert.That(o.uv[v], Is.EqualTo(mesh.uv[s]));
                // A fraction of a millimetre inside the cloth.
                float inside = new Vector2(positions[s].x, positions[s].z).magnitude - new Vector2(o.positions[v].x, o.positions[v].z).magnitude;
                Assert.That(inside, Is.EqualTo(OpeningFacing.FacingGap).Within(0.0002f));
            }
            // Something more than 5 cm from every opening (the middle of the front) gets no facing.
            int farthest = 0;
            for (int v = 0; v < distance.Length; v++) if (distance[v] > distance[farthest]) farthest = v;
            Assert.That(distance[farthest], Is.GreaterThan(0.05f));
            Assert.That(System.Array.IndexOf(o.sourceOf, farthest, mesh.VertexCount), Is.EqualTo(-1));
        }

        [Test]
        public void Facing_CarriesOnAcrossSewnSeams()
        {
            float[] distance = OpeningFacing.DistanceFromOpenings(positions, triangles, group, Openings());
            foreach (Vector2Int s in mesh.stitches) Assert.That(distance[s.x], Is.EqualTo(distance[s.y]));
        }

        [Test]
        public void Rim_JoinsEveryOpeningEdge_FacingOutOfTheOpening()
        {
            OpeningFacing.Output o = OpeningFacing.Build(mesh, pattern, positions, triangles, group, true, 0.05f, true, 0.003f);
            Assert.That(o.rimTriangles, Is.GreaterThan(40));
            Assert.That(o.rimTriangles % 2, Is.EqualTo(0));
            int rimStart = triangles.Length + o.facingTriangles * 3;
            Assert.That(o.triangles.Length, Is.EqualTo(rimStart + o.rimTriangles * 3));
            for (int t = rimStart; t < o.triangles.Length; t += 3)
            {
                // A rim triangle joins the outside (an original vertex) to the facing (a copy).
                bool outside = false, inside = false;
                for (int k = 0; k < 3; k++) { if (o.triangles[t + k] < mesh.VertexCount) outside = true; else inside = true; }
                Assert.That(outside && inside, Is.True);
            }
            // With a rim the facing lies its thickness inside the cloth.
            for (int v = mesh.VertexCount; v < o.positions.Length; v++)
            {
                int s = o.sourceOf[v];
                float inside = new Vector2(positions[s].x, positions[s].z).magnitude - new Vector2(o.positions[v].x, o.positions[v].z).magnitude;
                Assert.That(inside, Is.EqualTo(0.003f).Within(0.0003f));
            }
            // The hem's rim faces down, out of the opening.
            int hemRim = 0;
            for (int t = rimStart; t < o.triangles.Length; t += 3)
            {
                int a = o.sourceOf[o.triangles[t]];
                if (mesh.boundaryEdge[a] != 0) continue; // edge 0 is the hem on both vest pieces
                bool allHem = true;
                for (int k = 0; k < 3; k++) if (mesh.boundaryEdge[o.sourceOf[o.triangles[t + k]]] != 0) allHem = false;
                if (!allHem) continue;
                Assert.That(Normal(o.positions, o.triangles, t).y, Is.LessThan(-0.5f));
                hemRim++;
            }
            Assert.That(hemRim, Is.GreaterThan(10));
        }

        [Test]
        public void RimAlone_AddsOnlyTheRim()
        {
            OpeningFacing.Output o = OpeningFacing.Build(mesh, pattern, positions, triangles, group, false, 0.05f, true, 0.003f);
            Assert.That(o.facingTriangles, Is.EqualTo(0));
            Assert.That(o.rimTriangles, Is.GreaterThan(40));
        }
    }
}
#endif
