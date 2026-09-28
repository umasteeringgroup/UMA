using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace UMA.HairCards
{
    /// <summary>BVH surface correspondence with normalized, variable-influence skin weights.</summary>
    public sealed class HairSurfaceSkinning
    {
        private readonly Mesh source;
        private readonly HairMeshRaycaster surface;
        private readonly Vector3[] vertices;
        private readonly int[] offsets;
        private readonly BoneWeight1[] weights;
        private readonly Dictionary<int, float> blend = new Dictionary<int, float>();
        private readonly List<BoneWeight1> sorted = new List<BoneWeight1>();
        public HairSurfaceSkinning(Mesh source, bool requireWeights = true)
        {
            if (source == null || !source.isReadable) throw new ArgumentException("A readable donor mesh is required.");
            this.source = source; surface = new HairMeshRaycaster(source); vertices = source.vertices;
            var counts = source.GetBonesPerVertex(); var all = source.GetAllBoneWeights();
            offsets = new int[vertices.Length + 1]; weights = all.ToArray();
            if (!requireWeights) return;
            if (counts.Length != vertices.Length || source.bindposes.Length == 0)
                throw new ArgumentException("The selected body has no usable skin weights and bind poses.");
            for (int i = 0; i < vertices.Length; i++)
            {
                if (counts[i] == 0) throw new ArgumentException("The selected body contains unweighted vertices.");
                offsets[i + 1] = offsets[i] + counts[i];
            }
            foreach (var w in weights)
                if (!float.IsFinite(w.weight) || w.weight < 0 || w.boneIndex < 0 || w.boneIndex >= source.bindposes.Length)
                    throw new ArgumentException("The selected body contains invalid bone indices or weights.");
        }
        public HairSurfaceSample Sample(Vector3 point, float limit = float.PositiveInfinity)
        {
            if (!surface.ClosestPoint(point, out var hit, limit) ||
                !surface.TryGetTriangleVertices(hit.TriangleIndex, out int a, out int b, out int c)) return default;
            Vector3 bary = HairMeshUtility.Barycentric(hit.Point, vertices[a], vertices[b], vertices[c]);
            bary = Vector3.Max(Vector3.zero, bary); bary /= Mathf.Max(1e-12f, bary.x + bary.y + bary.z);
            return new HairSurfaceSample { a = a, b = b, c = c, weights = bary, distance = hit.Distance, valid = true };
        }
        public void AppendWeights(HairSurfaceSample sample, List<byte> counts, List<BoneWeight1> output)
        {
            if (!sample.valid) throw new InvalidOperationException("No aligned body surface near a hair root. Rebind in Source & Setup.");
            blend.Clear(); sorted.Clear();
            Add(sample.a, sample.weights.x); Add(sample.b, sample.weights.y); Add(sample.c, sample.weights.z);
            float total = 0;
            foreach (var pair in blend) if (pair.Value > 1e-7f) { sorted.Add(new BoneWeight1 { boneIndex = pair.Key, weight = pair.Value }); total += pair.Value; }
            if (total <= 1e-7f) throw new InvalidOperationException("A donor triangle has no positive weights.");
            sorted.Sort((a,b) => a.weight == b.weight ? a.boneIndex.CompareTo(b.boneIndex) : b.weight.CompareTo(a.weight));
            if (sorted.Count > 255) throw new InvalidOperationException("More than 255 blended influences on one vertex.");
            counts.Add((byte)sorted.Count);
            foreach (var entry in sorted) output.Add(new BoneWeight1 { boneIndex = entry.boneIndex, weight = entry.weight / total });
        }
        private void Add(int vertex, float amount)
        {
            for (int i = offsets[vertex]; i < offsets[vertex + 1]; i++)
            { var w = weights[i]; blend.TryGetValue(w.boneIndex, out float value); blend[w.boneIndex] = value + w.weight * amount; }
        }
        public void Transfer(Mesh destination, Vector3[] samples = null, float limit = float.PositiveInfinity)
        {
            samples ??= destination.vertices;
            if (samples.Length != destination.vertexCount) throw new ArgumentException("One sample position per output vertex is required.");
            var counts = new List<byte>(samples.Length); var output = new List<BoneWeight1>();
            foreach (var p in samples) AppendWeights(Sample(p, limit), counts, output);
            SetWeights(destination, counts.ToArray(), output.ToArray()); destination.bindposes = source.bindposes;
        }
        public static void SetWeights(Mesh mesh, byte[] counts, BoneWeight1[] weights)
        {
            using var nc = new NativeArray<byte>(counts, Allocator.Temp);
            using var nw = new NativeArray<BoneWeight1>(weights, Allocator.Temp);
            mesh.SetBoneWeights(nc, nw);
        }
        public static void TransferCards(HairCardMeshBuildResult build, HairCharacterBindingAsset binding)
        {
            var transfer = new HairSurfaceSkinning(binding.donorMesh);
            var counts = new List<byte>(build.vertexCount); var output = new List<BoneWeight1>();
            // Card spans are in mesh vertex order, including duplicate backfaces.
            foreach (var card in build.cards)
            {
                if (card.vertexStart != counts.Count) throw new InvalidOperationException("Unexpected card vertex layout.");
                var sample = transfer.Sample(card.curve.points[0].position, binding.maximumDistance);
                var rootCounts = new List<byte>(); var rootWeights = new List<BoneWeight1>();
                transfer.AppendWeights(sample, rootCounts, rootWeights);
                for (int i = 0; i < card.vertexCount; i++) { counts.Add(rootCounts[0]); output.AddRange(rootWeights); }
            }
            if (counts.Count != build.mesh.vertexCount) throw new InvalidOperationException("Incomplete card skinning layout.");
            SetWeights(build.mesh, counts.ToArray(), output.ToArray()); build.mesh.bindposes = binding.donorMesh.bindposes;
        }
        public static Color32 Color(HairSurfaceSample s, IReadOnlyList<Color32> colors, Color32 fallback)
        {
            if (!s.valid || s.a >= colors.Count || s.b >= colors.Count || s.c >= colors.Count) return fallback;
            return (UnityEngine.Color)colors[s.a] * s.weights.x + (UnityEngine.Color)colors[s.b] * s.weights.y + (UnityEngine.Color)colors[s.c] * s.weights.z;
        }
    }
}
