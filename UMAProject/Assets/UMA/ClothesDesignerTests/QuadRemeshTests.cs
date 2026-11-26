#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>Slot Topology: a quad mesh made from the pattern, carried from the drape at bake.</summary>
    public sealed class QuadRemeshTests
    {
        private const string Folder = "Assets/UMAProjectData/ClothesDesigner/Tests_Temp_Quads";

        private static PieceScale[] Scales(ClothesPattern pattern) => PatternGeometry.Evaluate(pattern, null, null, null);

        /// <summary>Points on edges sewn whole (so the count is exact) that are sewn to nothing.</summary>
        private static int Unsewn(ClothesPattern pattern, PatternMesh mesh)
        {
            var sewnEdges = new HashSet<long>();
            foreach (PatternSeam s in pattern.seams)
            {
                foreach (SeamSpan span in new[] { s.a, s.b })
                {
                    int p = pattern.IndexOfPiece(span.pieceId);
                    if (p < 0) continue;
                    int e = pattern.pieces[p].IndexOfEdge(span.edgeId);
                    if (e < 0 || span.start > 0.001f || span.end < 0.999f) continue;
                    sewnEdges.Add(((long)p << 32) | (uint)e);
                }
            }
            var stitched = new HashSet<int>();
            foreach (Vector2Int st in mesh.stitches) { stitched.Add(st.x); stitched.Add(st.y); }
            int missing = 0;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                if (mesh.boundaryEdge[v] < 0 || !sewnEdges.Contains(((long)mesh.piece[v] << 32) | (uint)mesh.boundaryEdge[v])) continue;
                if (!stitched.Contains(v)) missing++;
            }
            return missing;
        }

        private static void AssertFacesSound(PatternMesh mesh, string label)
        {
            for (int t = 0; t < mesh.triangles.Count; t += 3)
            {
                Vector2 a = mesh.flat[mesh.triangles[t]], b = mesh.flat[mesh.triangles[t + 1]], c = mesh.flat[mesh.triangles[t + 2]];
                Assert.That((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x), Is.GreaterThan(1e-6f), label + " triangle " + t / 3);
            }
            for (int q = 0; q < mesh.quads.Count; q += 4)
            {
                Assert.That(mesh.piece[mesh.quads[q]], Is.EqualTo(mesh.piece[mesh.quads[q + 2]]), label + " quad " + q / 4 + " spans two pieces");
            }
        }

        [Test]
        public void EverySample_IsMostlyQuads_InBothModes_WithEverySewnPointMatched()
        {
            foreach (var sample in SamplePatterns.All)
            {
                ClothesPattern pattern = sample.make();
                try
                {
                    PieceScale[] scales = Scales(pattern);
                    PatternMesh quads = QuadRemesher.Build(pattern, scales, 2f, false);
                    QuadRemesher.Measure(quads, out float share, out _, out _);
                    Assert.That(share, Is.GreaterThan(0.9f), sample.name + ": share of the cloth in quads");
                    Assert.That(quads.warnings, Is.Empty, sample.name);
                    Assert.That(Unsewn(pattern, quads), Is.Zero, sample.name + " quads");
                    AssertFacesSound(quads, sample.name + " quads");

                    PatternMesh all = QuadRemesher.Build(pattern, scales, 2f, true);
                    QuadRemesher.Measure(all, out float allShare, out _, out int triangles);
                    Assert.That(triangles, Is.Zero, sample.name + ": all quads");
                    Assert.That(allShare, Is.EqualTo(1f).Within(1e-4f), sample.name);
                    Assert.That(all.warnings, Is.Empty, sample.name);
                    Assert.That(Unsewn(pattern, all), Is.Zero, sample.name + " all quads (the middles of sewn stretches sewn too)");
                    AssertFacesSound(all, sample.name + " all quads");
                    Assert.That(UvLayout.FindProblems(pattern, all), Is.Empty, sample.name);
                    // Each piece keeps its vertices together, as every mesh does.
                    for (int v = 0; v < all.VertexCount; v++)
                    {
                        int p = all.piece[v];
                        Assert.That(v, Is.InRange(all.pieceStart[p], all.pieceStart[p] + all.pieceCount[p] - 1), sample.name);
                    }
                }
                finally { Object.DestroyImmediate(pattern); }
            }
        }

        [Test]
        public void Quads_FollowTheGrain_WithALoopAlongTheOutline()
        {
            ClothesPattern vest = VestPattern.Create();
            try
            {
                PatternMesh mesh = QuadRemesher.Build(vest, Scales(vest), 2f, false);
                float grain = vest.pieces[0].grainAngle;
                int aligned = 0, total = 0;
                for (int q = 0; q < mesh.quads.Count; q += 4)
                {
                    if (mesh.piece[mesh.quads[q]] != 0) continue;
                    bool inside = true;
                    for (int i = 0; i < 4; i++) if (mesh.boundaryEdge[mesh.quads[q + i]] >= 0) inside = false;
                    if (!inside) continue;
                    for (int i = 0; i < 4; i++)
                    {
                        Vector2 d = mesh.drawn[mesh.quads[q + (i + 1) % 4]] - mesh.drawn[mesh.quads[q + i]];
                        float angle = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + grain;
                        total++;
                        if (Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 90f) * 90f)) < 5f) aligned++;
                    }
                }
                Assert.That(aligned / (float)total, Is.GreaterThan(0.8f), "quad sides along the grain or across it");

                // The outline loop: next to almost every outline point there is a quad with two points on the outline.
                var onOutlineQuad = new HashSet<int>();
                for (int q = 0; q < mesh.quads.Count; q += 4)
                {
                    int count = 0;
                    for (int i = 0; i < 4; i++) if (mesh.boundaryEdge[mesh.quads[q + i]] >= 0) count++;
                    if (count != 2) continue;
                    for (int i = 0; i < 4; i++) if (mesh.boundaryEdge[mesh.quads[q + i]] >= 0) onOutlineQuad.Add(mesh.quads[q + i]);
                }
                int outline = 0;
                for (int v = 0; v < mesh.VertexCount; v++) if (mesh.piece[v] == 0 && mesh.boundaryEdge[v] >= 0) outline++;
                int covered = 0;
                foreach (int v in onOutlineQuad) if (mesh.piece[v] == 0) covered++;
                Assert.That(covered / (float)outline, Is.GreaterThan(0.85f), "outline points on a quad of the loop");
            }
            finally { Object.DestroyImmediate(vest); }
        }

        [Test]
        public void AllQuads_KeepsTheEdgeLength_AndSplitsSeamsWithTheirPartners()
        {
            ClothesPattern tee = SamplePatterns.TShirt();
            try
            {
                PieceScale[] scales = Scales(tee);
                PatternMesh triangles = PatternMesher.Build(tee, scales, 2f);
                PatternMesh all = QuadRemesher.Build(tee, scales, 2f, true);
                Assert.That(all.edgeLengthCm, Is.EqualTo(2f).Within(1e-4f));
                // Made at twice the spacing and split in two each way: about as many points as the triangle mesh.
                Assert.That(all.VertexCount, Is.InRange(triangles.VertexCount * 0.7f, triangles.VertexCount * 1.3f));

                // Seams between pieces: a sewn stretch split in two on one side is split in two on the other, so
                // both sides have the same number of points along the seam.
                var perSide = new Dictionary<(int seam, int piece), HashSet<int>>();
                for (int k = 0; k < all.stitches.Count; k++)
                {
                    Vector2Int st = all.stitches[k];
                    Assert.That(st.x, Is.Not.EqualTo(st.y));
                    if (all.piece[st.x] == all.piece[st.y]) continue;
                    foreach (int v in new[] { st.x, st.y })
                    {
                        var key = (all.stitchSeam[k], all.piece[v]);
                        if (!perSide.TryGetValue(key, out HashSet<int> set)) perSide[key] = set = new HashSet<int>();
                        set.Add(v);
                    }
                }
                var bySeam = new Dictionary<int, List<int>>();
                foreach (var kv in perSide)
                {
                    if (!bySeam.TryGetValue(kv.Key.seam, out List<int> counts)) bySeam[kv.Key.seam] = counts = new List<int>();
                    counts.Add(kv.Value.Count);
                }
                foreach (var kv in bySeam)
                {
                    if (kv.Value.Count != 2) continue;
                    Assert.That(kv.Value[0], Is.EqualTo(kv.Value[1]), "seam group " + kv.Key + ": points on each side");
                }
            }
            finally { Object.DestroyImmediate(tee); }
        }

        [Test]
        public void SplitAlongShorterDiagonals_BendsAQuadAlongItsShorterDiagonal()
        {
            var mesh = new PatternMesh();
            for (int i = 0; i < 4; i++)
            {
                mesh.flat.Add(new Vector2(i == 1 || i == 2 ? 1f : 0f, i >= 2 ? 1f : 0f));
                mesh.piece.Add(0);
            }
            mesh.quads.AddRange(new[] { 0, 1, 2, 3 });
            mesh.triangles.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
            // Folded along 1-3: corners 0 and 2 come together, so 0-2 is the short diagonal.
            var positions = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0.5f), new Vector3(0.2f, 0f, 0f), new Vector3(0f, 1f, 0.5f) };
            QuadRemesher.SplitAlongShorterDiagonals(mesh, positions);
            Assert.That(mesh.triangles, Is.EqualTo(new[] { 0, 1, 2, 0, 2, 3 }));
            // Now 1-3 is shorter.
            positions = new[] { new Vector3(0f, 0f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(1f, 1f, 0.3f), new Vector3(0.5f, 0.5f, 0.1f) };
            QuadRemesher.SplitAlongShorterDiagonals(mesh, positions);
            Assert.That(mesh.triangles, Is.EqualTo(new[] { 0, 1, 3, 1, 2, 3 }));
            Assert.That(mesh.quads, Is.EqualTo(new[] { 0, 1, 2, 3 }), "the quad itself does not change");
        }

        [Test]
        public void QuadObj_HasAFaceForEveryQuad_OnePositionPerSewnPoint_AndAUvPerVertex()
        {
            ClothesPattern vest = VestPattern.Create();
            try
            {
                PatternMesh mesh = QuadRemesher.Build(vest, Scales(vest), 3f, true);
                var positions = new Vector3[mesh.VertexCount];
                for (int v = 0; v < positions.Length; v++) positions[v] = new Vector3(mesh.flat[v].x, mesh.flat[v].y, 0f) * 0.01f;
                string text = QuadObjWriter.ToText(vest, mesh, positions, false);
                int v3 = 0, vt = 0, quads = 0, others = 0, objects = 0, maxIndex = 0, maxUv = 0;
                foreach (string line in text.Split('\n'))
                {
                    if (line.StartsWith("v ")) v3++;
                    else if (line.StartsWith("vt ")) vt++;
                    else if (line.StartsWith("o ")) objects++;
                    else if (line.StartsWith("f "))
                    {
                        string[] corners = line.Substring(2).Split(' ');
                        if (corners.Length == 4) quads++; else others++;
                        foreach (string c in corners)
                        {
                            string[] parts = c.Split('/');
                            maxIndex = Mathf.Max(maxIndex, int.Parse(parts[0]));
                            maxUv = Mathf.Max(maxUv, int.Parse(parts[1]));
                        }
                    }
                }
                int groups = new HashSet<int>(GarmentBaker.WeldGroups(mesh.VertexCount, mesh.stitches)).Count;
                Assert.That(quads, Is.EqualTo(mesh.QuadCount));
                Assert.That(others, Is.Zero);
                Assert.That(v3, Is.EqualTo(groups), "sewn points share a position");
                Assert.That(v3, Is.LessThan(mesh.VertexCount));
                Assert.That(vt, Is.EqualTo(mesh.VertexCount));
                Assert.That(objects, Is.EqualTo(vest.pieces.Count));
                Assert.That(maxIndex, Is.EqualTo(v3));
                Assert.That(maxUv, Is.LessThanOrEqualTo(vt));
                // X is negated for right-handed tools.
                Assert.That(text, Does.Contain("X negated"));
            }
            finally { Object.DestroyImmediate(vest); }
        }

        [Test]
        public void Bake_AllQuads_GivesTwoTrianglesAQuad_MatchedSeams_AndAnObj()
        {
            var capsule = new CapsuleBodyFixture();
            ClothesPattern pattern = VestPattern.Create();
            ClothesFit fit = ScriptableObject.CreateInstance<ClothesFit>();
            var session = new FitSession { Pattern = pattern, Fit = fit, Body = capsule.Body, PreferCpu = true };
            try
            {
                fit.pattern = pattern;
                fit.overrideMeshLevel = true;
                fit.meshLevel = MeshLevel.Draft;
                session.Rebuild();
                session.Step(80);
                GarmentBaker.EnsureFolder(Folder);
                PublishSettings p = fit.publish;
                p.slotName = "QuadsTest"; p.folder = Folder; p.material = DesignerSession.DefaultMaterial();
                p.createWardrobeRecipe = false; p.addToGlobalLibrary = false; p.makeUvCheckTexture = false; p.bakeNormalMap = false;
                p.addTopstitch = false; p.addFacing = false; p.addRim = false; p.addParts = false; p.fullThickness = false; p.lodLevels = 1;
                p.slotTopology = SlotTopology.AllQuads;
                p.writeQuadObj = true;
                GarmentBaker.Result result = GarmentBaker.Bake(session);
                Assert.That(result.Published.Slot, Is.Not.Null, string.Join("\n", result.Messages));
                Assert.That(result.Quads, Is.GreaterThan(0));
                Assert.That(result.Triangles, Is.EqualTo(result.Quads * 2), "every quad two triangles, nothing else");
                Assert.That(result.MaxWeightSumError, Is.LessThan(1e-4f));
                Assert.That(result.SeamWeightsMatch, Is.True);
                Assert.That(result.LodSlots.Count, Is.EqualTo(1));
                Assert.That(result.LodTriangles[0], Is.LessThan(result.Triangles), "the LOD slot is quads too, coarser");
                Assert.That(result.QuadObjPath, Is.Not.Null);
                Assert.That(File.Exists(Path.GetFullPath(result.QuadObjPath)), Is.True);
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(result.QuadObjPath), Is.Not.Null, "Unity reads the OBJ back");

                // Quads mode: fewer, some triangles left, still matched; no OBJ when it is off.
                p.slotTopology = SlotTopology.Quads;
                p.writeQuadObj = false;
                GarmentBaker.Result quads = GarmentBaker.Bake(session);
                Assert.That(quads.Triangles, Is.GreaterThan(quads.Quads * 2));
                Assert.That(quads.SeamWeightsMatch, Is.True);
                Assert.That(quads.QuadObjPath, Is.Null);
            }
            finally
            {
                session.Dispose();
                Object.DestroyImmediate(fit);
                Object.DestroyImmediate(pattern);
                capsule.Dispose();
                if (AssetDatabase.IsValidFolder(Folder))
                {
                    AssetDatabase.DeleteAsset(Folder);
                    if (AssetDatabase.FindAssets("", new[] { "Assets/UMAProjectData/ClothesDesigner" }).Length == 0) AssetDatabase.DeleteAsset("Assets/UMAProjectData/ClothesDesigner");
                }
            }
        }
    }
}
#endif
