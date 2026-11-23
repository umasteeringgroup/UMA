#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>The cloth mesh made from a pattern, its stitches and its UVs.</summary>
    public sealed class MeshAndUvTests
    {
        private ClothesPattern vest;

        [SetUp]
        public void Make() => vest = VestPattern.Create();

        [TearDown]
        public void Clean() { if (vest != null) Object.DestroyImmediate(vest); }

        private PatternMesh Mesh(float edgeCm = 2f, float chest = 0f)
        {
            BodyLandmarks body = chest > 0f ? new BodyLandmarks { valid = true, chestCm = chest, shoulderTopY = 1.5f, waistY = 1.1f } : null;
            return PatternMesher.Build(vest, PatternGeometry.Evaluate(vest, body, null, null), edgeCm);
        }

        [Test]
        public void Mesh_KeepsEveryOutlineSampleAsAnEdge_NoWarnings()
        {
            PatternMesh mesh = Mesh();
            Assert.That(mesh.warnings, Is.Empty);
            Assert.That(mesh.VertexCount, Is.GreaterThan(500));
        }

        [Test]
        public void Mesh_TrianglesAreCounterClockwiseAndNotDegenerate()
        {
            PatternMesh mesh = Mesh();
            for (int t = 0; t < mesh.triangles.Count; t += 3)
            {
                Vector2 a = mesh.flat[mesh.triangles[t]], b = mesh.flat[mesh.triangles[t + 1]], c = mesh.flat[mesh.triangles[t + 2]];
                float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                Assert.That(area, Is.GreaterThan(1e-4f), "triangle " + t / 3);
                Assert.That(mesh.piece[mesh.triangles[t]], Is.EqualTo(mesh.piece[mesh.triangles[t + 1]]));
            }
        }

        [Test]
        public void Stitches_JoinOneVertexToOneVertex_OnTheSewnEdges()
        {
            PatternMesh mesh = Mesh();
            var used = new HashSet<int>();
            foreach (Vector2Int s in mesh.stitches)
            {
                Assert.That(mesh.piece[s.x], Is.Not.EqualTo(mesh.piece[s.y]), "A vest stitch joins the front to the back.");
                Assert.That(mesh.boundaryEdge[s.x], Is.GreaterThanOrEqualTo(0));
                Assert.That(mesh.boundaryEdge[s.y], Is.GreaterThanOrEqualTo(0));
                Assert.That(used.Add(s.x * 100000 + s.y), Is.True);
            }
            // Each seam: as many samples on both sides; four seams.
            Assert.That(mesh.stitches.Count, Is.GreaterThan(4 * 3));
        }

        [Test]
        public void Stitches_KnowTheirSeam_InOrderAlongIt()
        {
            PatternMesh mesh = Mesh();
            Assert.That(mesh.stitchSeam.Count, Is.EqualTo(mesh.stitches.Count));
            // Each seam's stitches are together, so the Scene view can draw a seam as one line per side.
            var finished = new HashSet<int>();
            for (int k = 0; k < mesh.stitchSeam.Count; k++)
            {
                int seam = mesh.stitchSeam[k];
                Assert.That(seam, Is.InRange(0, vest.seams.Count - 1));
                if (k > 0 && mesh.stitchSeam[k - 1] != seam) Assert.That(finished.Add(mesh.stitchSeam[k - 1]), Is.True, "seam " + mesh.stitchSeam[k - 1] + " is split");
                Assert.That(finished.Contains(seam), Is.False);
                // Neighbouring stitches of one seam are neighbouring samples on the edge: about one mesh edge apart.
                if (k > 0 && mesh.stitchSeam[k - 1] == seam)
                {
                    float step = Vector2.Distance(mesh.flat[mesh.stitches[k].x], mesh.flat[mesh.stitches[k - 1].x]);
                    Assert.That(step, Is.LessThan(mesh.edgeLengthCm * 1.5f), "stitch " + k);
                }
            }
            Assert.That(finished.Count + 1, Is.EqualTo(vest.seams.Count));
        }

        [Test]
        public void Stitches_MatchAtEveryMeshLevel()
        {
            foreach (float h in new[] { 3f, 2f, 1.5f })
            {
                PatternMesh mesh = Mesh(h);
                Assert.That(mesh.warnings, Is.Empty, "edge " + h);
                foreach (Vector2Int s in mesh.stitches)
                {
                    // Matched samples sit at the same fraction of their edges: mirrored x, same height.
                    Vector2 a = mesh.drawn[s.x], b = mesh.drawn[s.y];
                    Assert.That(a.y, Is.EqualTo(b.y).Within(0.05f), "edge " + h);
                }
            }
        }

        [Test]
        public void MeshLevel_FinerHasMoreVertices()
        {
            int draft = Mesh(3f).VertexCount, medium = Mesh(2f).VertexCount, fine = Mesh(1.5f).VertexCount;
            Assert.That(draft, Is.LessThan(medium));
            Assert.That(medium, Is.LessThan(fine));
        }

        [Test]
        public void Uvs_InsideTheTexture_NoOverlap_OneDensity()
        {
            PatternMesh mesh = Mesh();
            foreach (Vector2 uv in mesh.uv)
            {
                Assert.That(uv.x, Is.InRange(0f, 1f));
                Assert.That(uv.y, Is.InRange(0f, 1f));
            }
            Assert.That(UvLayout.FindProblems(vest, mesh), Is.Empty);
            Assert.That(vest.pieces[0].uvIsland.scale, Is.EqualTo(vest.pieces[1].uvIsland.scale));
        }

        [Test]
        public void Uvs_ComeFromTheDrawnPattern_SoABodySizeDoesNotMoveThem()
        {
            PatternMesh small = Mesh(2f, 90f);
            PatternMesh large = Mesh(2f, 130f);
            // The corners of each piece land on the same UVs whatever the body.
            for (int p = 0; p < vest.pieces.Count; p++)
            {
                foreach (PatternPoint corner in vest.pieces[p].points)
                {
                    Vector2 a = UvLayout.ToUV(vest, p, corner.position);
                    int vs = Nearest(small, p, corner.position), vl = Nearest(large, p, corner.position);
                    Assert.That((small.uv[vs] - a).magnitude, Is.LessThan(1e-5f));
                    Assert.That((large.uv[vl] - a).magnitude, Is.LessThan(1e-5f));
                }
            }
            Assert.That(UvLayout.FindProblems(vest, large), Is.Empty);
        }

        private static int Nearest(PatternMesh mesh, int piece, Vector2 drawn)
        {
            int best = -1; float d = float.MaxValue;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                if (mesh.piece[v] != piece) continue;
                float e = (mesh.drawn[v] - drawn).sqrMagnitude;
                if (e < d) { d = e; best = v; }
            }
            return best;
        }

        [Test]
        public void Repack_ChangesNothingForAnUnchangedPattern()
        {
            PatternMesh mesh = Mesh();
            var before = new List<Vector2>(mesh.uv);
            UvLayout.Repack(vest);
            UvLayout.ApplyUVs(vest, mesh);
            for (int i = 0; i < before.Count; i++) Assert.That((mesh.uv[i] - before[i]).magnitude, Is.LessThan(1e-6f));
        }

        [Test]
        public void CheckTexture_DrawsEveryPiece()
        {
            PatternMesh mesh = Mesh();
            Texture2D texture = UvCheckTexture.Draw(vest, mesh, 256);
            try
            {
                foreach (int p in new[] { 0, 1 })
                {
                    Rect island = vest.pieces[p].uvIsland.reserved;
                    Color c = texture.GetPixelBilinear(island.center.x, island.center.y);
                    Assert.That(c.a, Is.GreaterThan(0.5f), "piece " + p + " is painted at its island's centre");
                }
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
#endif
