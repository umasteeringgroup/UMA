#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>Draping fine and baking coarse: carrying a drape between mesh levels, substeps by mesh level, and the normal map from the drape.</summary>
    public sealed class DrapeDetailTests
    {
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void Clean()
        {
            foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private PatternMesh Mesh(ClothesPattern pattern, float edge) => PatternMesher.Build(pattern, PatternGeometry.Evaluate(pattern, null, null, null), edge);

        private ClothesPattern Vest()
        {
            ClothesPattern p = VestPattern.Create();
            made.Add(p);
            return p;
        }

        /// <summary>A made-up drape: each piece rolled round a cylinder with a small ripple, as if folded.</summary>
        private static Vector3[] Rolled(PatternMesh mesh, float ripple)
        {
            var result = new Vector3[mesh.VertexCount];
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                Vector2 f = mesh.flat[v] * 0.01f;
                float angle = f.x / 0.3f + mesh.piece[v] * Mathf.PI;
                float r = 0.3f + ripple * Mathf.Sin(f.x * 120f) * Mathf.Sin(f.y * 90f);
                result[v] = new Vector3(Mathf.Sin(angle) * r, f.y, Mathf.Cos(angle) * r);
            }
            return result;
        }

        [Test]
        public void Carry_PutsEveryVertexOnTheDrape()
        {
            ClothesPattern pattern = Vest();
            PatternMesh fine = Mesh(pattern, 1f), coarse = Mesh(pattern, 3f);
            Vector3[] drape = Rolled(fine, 0f);
            Vector3[] carried = new PatternMeshSampler(fine).Carry(drape, coarse);
            // On a smooth roll the carried points lie on the same cylinder (to within the fine mesh's chords).
            for (int v = 0; v < coarse.VertexCount; v++)
            {
                float r = new Vector2(carried[v].x, carried[v].z).magnitude;
                Assert.That(r, Is.EqualTo(0.3f).Within(0.002f), "vertex " + v);
            }
            // And a vertex both meshes share (a piece corner) lands exactly where the fine one is.
            int corner = 0;
            for (int v = 0; v < fine.VertexCount; v++) if ((fine.drawn[v] - coarse.drawn[corner]).sqrMagnitude < 1e-6f && fine.piece[v] == coarse.piece[corner])
            {
                Assert.That((drape[v] - carried[corner]).magnitude, Is.LessThan(1e-5f));
            }
        }

        [Test]
        public void Substeps_GrowWithFinerMeshes()
        {
            Assert.That(ClothModel.Substeps(false, 2f), Is.EqualTo(12));
            Assert.That(ClothModel.Substeps(true, 2f), Is.EqualTo(24));
            Assert.That(ClothModel.Substeps(false, 3f), Is.EqualTo(12), "Never fewer than at 2 cm.");
            Assert.That(ClothModel.Substeps(false, 1f), Is.EqualTo(24));
            Assert.That(ClothModel.Substeps(false, 0.5f), Is.EqualTo(48));
            Assert.That(ClothModel.Substeps(true, 0.3f), Is.LessThanOrEqualTo(160));
        }

        [Test]
        public void NormalMap_OfTheSameSurface_IsFlat()
        {
            ClothesPattern pattern = Vest();
            PatternMesh mesh = Mesh(pattern, 2f);
            Vector3[] drape = Rolled(mesh, 0f);
            int[] tris = mesh.triangles.ToArray();
            DrapeNormalMap.SlotFrame(drape, tris, mesh.uv, out Vector3[] normals, out Vector4[] tangents);
            Texture2D map = DrapeNormalMap.Bake(mesh, drape, tris, mesh, tris, normals, tangents, 256, 4, out DrapeNormalMap.Stats stats);
            made.Add(map);
            Assert.That(stats.covered, Is.GreaterThan(1000));
            Assert.That(stats.meanDegrees, Is.LessThan(0.01f));
            Color32[] pixels = map.GetPixels32();
            foreach (Color32 p in pixels)
            {
                Assert.That(p.b, Is.GreaterThanOrEqualTo(254));
                Assert.That(Mathf.Abs(p.r - 128), Is.LessThanOrEqualTo(1));
                Assert.That(Mathf.Abs(p.g - 128), Is.LessThanOrEqualTo(1));
            }
        }

        [Test]
        public void NormalMap_FromARippledDrape_TurnsTheCoarseNormalBackToTheFineOne()
        {
            ClothesPattern pattern = Vest();
            PatternMesh fine = Mesh(pattern, 0.6f), coarse = Mesh(pattern, 3f);
            Vector3[] drape = Rolled(fine, 0.004f);
            Vector3[] coarsePositions = new PatternMeshSampler(fine).Carry(drape, coarse);
            int[] fineTris = fine.triangles.ToArray(), coarseTris = coarse.triangles.ToArray();
            DrapeNormalMap.SlotFrame(coarsePositions, coarseTris, coarse.uv, out Vector3[] cn, out Vector4[] ct);
            const int size = 512;
            Texture2D map = DrapeNormalMap.Bake(fine, drape, fineTris, coarse, coarseTris, cn, ct, size, 4, out DrapeNormalMap.Stats stats);
            made.Add(map);
            Assert.That(stats.meanDegrees, Is.GreaterThan(2f), "The ripple shows in the map.");
            // At the coarse vertices inside a piece, decode the map and turn the coarse frame by it: it
            // must point along the fine surface's normal there.
            var fineNormals = new Vector3[drape.Length];
            new UMABuddyNormals(drape, fineTris).Recalculate(drape, fineNormals);
            var sampler = new PatternMeshSampler(fine);
            Color32[] pixels = map.GetPixels32();
            int checkedTexels = 0;
            double error = 0;
            for (int v = 0; v < coarse.VertexCount; v++)
            {
                if (coarse.boundaryEdge[v] >= 0) continue;
                Vector2 uv = coarse.uv[v];
                int x = Mathf.FloorToInt(uv.x * size), y = Mathf.FloorToInt(uv.y * size);
                Color32 p = pixels[y * size + x];
                var local = new Vector3(p.r / 255f * 2f - 1f, p.g / 255f * 2f - 1f, p.b / 255f * 2f - 1f).normalized;
                Vector3 n = cn[v].normalized, t = ((Vector3)ct[v] - n * Vector3.Dot(n, ct[v])).normalized;
                Vector3 b = Vector3.Cross(n, t) * (ct[v].w < 0f ? -1f : 1f);
                Vector3 turned = (t * local.x + b * local.y + n * local.z).normalized;
                Vector3 expected = sampler.Sample(fineNormals, coarse.piece[v], coarse.drawn[v]).normalized;
                error += Vector3.Angle(turned, expected);
                checkedTexels++;
            }
            Assert.That(checkedTexels, Is.GreaterThan(50));
            // Texel centres are up to half a texel from the vertex, and the map holds 8 bits a channel.
            Assert.That(error / checkedTexels, Is.LessThan(3.0), "mean error in degrees");
        }
    }
}
#endif
