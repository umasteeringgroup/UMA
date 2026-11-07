using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>Normal-only seam repair in the common slot bind-pose coordinate system.</summary>
    public sealed class UMANormalRepair
    {
        public enum FixMode { Average, Copy }

        public sealed class Group
        {
            public int First, Second;
            public readonly List<int> Seams = new List<int>();
            public FixMode Mode;
            public int Source;
            public bool Smooth;
            public int Rings = 2;
            public float Strength = .5f;
            public bool Pending;
        }

        public sealed class Source
        {
            public SlotDataAsset Asset;
            public Vector3[] Positions, Normals;
            public Vector4[] Tangents;
            public int[] Triangles;
            public HashSet<int>[] Neighbours;
        }

        public readonly List<Source> Sources = new List<Source>();
        public readonly List<Group> Groups = new List<Group>();
        private readonly List<List<(int slot, int vertex)>> seams = new List<List<(int, int)>>();
        public bool HasPending => Groups.Any(g => g.Pending);

        public static bool CanScan(SlotDataAsset asset)
        {
            var m = asset != null ? asset.meshData : null;
            return m != null && m.vertices != null && m.vertices.Length > 0 &&
                m.normals != null && m.normals.Length == m.vertices.Length &&
                m.submeshes != null && asset.subMeshIndex >= 0 && asset.subMeshIndex < m.submeshes.Length &&
                m.submeshes[asset.subMeshIndex] != null;
        }

        private static int[] Triangles(SlotDataAsset a) =>
            a.meshData.submeshes[a.subMeshIndex].GetBaseTriangles() ??
            a.meshData.submeshes[a.subMeshIndex].getManagedTriangles(0);

        public static UMANormalRepair Scan(IEnumerable<SlotDataAsset> assets, float distance, float angle,
            Func<float, bool> cancel = null)
        {
            if (!float.IsFinite(distance) || distance <= 0 || !float.IsFinite(angle) || angle < 0 || angle > 180)
                throw new ArgumentException("Use a positive position tolerance and an angle from 0 to 180 degrees.");
            var result = new UMANormalRepair();
            foreach (var a in assets.Where(a => a != null).Distinct())
            {
                if (!CanScan(a)) throw new InvalidOperationException(a.name + ": missing vertices, normals or submesh.");
                var s = new Source { Asset = a, Positions = (Vector3[])a.meshData.vertices.Clone(),
                    Normals = (Vector3[])a.meshData.normals.Clone(), Tangents = a.meshData.tangents == null ? null :
                    (Vector4[])a.meshData.tangents.Clone(), Triangles = (int[])(Triangles(a) ?? Array.Empty<int>()).Clone() };
                s.Neighbours = new HashSet<int>[s.Positions.Length];
                for (int i = 0; i < s.Positions.Length; i++)
                {
                    if (!Finite(s.Positions[i]) || !Finite(s.Normals[i]) || s.Normals[i].sqrMagnitude < 1e-12f)
                        throw new InvalidOperationException(a.name + ": invalid position or normal at vertex " + i + ".");
                    s.Neighbours[i] = new HashSet<int>();
                }
                if (s.Triangles.Length % 3 != 0 || s.Triangles.Any(i => i < 0 || i >= s.Positions.Length))
                    throw new InvalidOperationException(a.name + ": invalid triangle indices.");
                for (int i = 0; i < s.Triangles.Length; i += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        int v = s.Triangles[i + k], w = s.Triangles[i + (k + 1) % 3];
                        s.Neighbours[v].Add(w); s.Neighbours[w].Add(v);
                    }
                result.Sources.Add(s);
            }
            // Anchor-based clusters avoid chaining vertices across a large distance. Search adjacent
            // cells so points straddling a quantization boundary still match.
            var grid = new Dictionary<Vector3Int, List<int>>();
            var anchors = new List<Vector3>();
            var clusters = new List<List<(int slot, int vertex)>>();
            for (int slot = 0; slot < result.Sources.Count; slot++)
            {
                var s = result.Sources[slot];
                foreach (int v in s.Triangles.Distinct().OrderBy(i => i))
                {
                    if ((v & 255) == 0 && cancel != null && cancel((slot + (float)v / s.Positions.Length) / result.Sources.Count))
                        throw new OperationCanceledException();
                    var p = s.Positions[v];
                    var key = Cell(p, distance);
                    int match = -1;
                    float best = distance * distance;
                    for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                        if (grid.TryGetValue(key + new Vector3Int(x, y, z), out var candidates))
                            foreach (int c in candidates)
                            {
                                float d = (anchors[c] - p).sqrMagnitude;
                                if (d <= best && (d < best || match < 0 || c < match)) { best = d; match = c; }
                            }
                    if (match < 0)
                    {
                        match = clusters.Count; clusters.Add(new List<(int, int)>()); anchors.Add(p);
                        if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                        list.Add(match);
                    }
                    clusters[match].Add((slot, v));
                }
            }
            var groups = new Dictionary<(int, int), Group>();
            foreach (var cluster in clusters)
            {
                // Weld duplicate UV vertices in the adjacency graph, without changing positions.
                foreach (var sameSlot in cluster.GroupBy(v => v.slot))
                {
                    int first = sameSlot.First().vertex;
                    foreach (var v in sameSlot)
                        if (first != v.vertex)
                        { result.Sources[v.slot].Neighbours[first].Add(v.vertex); result.Sources[v.slot].Neighbours[v.vertex].Add(first); }
                }
                var slots = cluster.Select(v => v.slot).Distinct().OrderBy(i => i).ToArray();
                if (slots.Length < 2) continue;
                int seamIndex = result.seams.Count; result.seams.Add(cluster);
                for (int a = 0; a < slots.Length; a++) for (int b = a + 1; b < slots.Length; b++)
                {
                    int first = slots[a], second = slots[b];
                    bool different = cluster.Where(v => v.slot == first).Any(v => cluster.Where(w => w.slot == second)
                        .Any(w => Vector3.Angle(result.Sources[v.slot].Normals[v.vertex], result.Sources[w.slot].Normals[w.vertex]) > angle));
                    if (!different) continue;
                    if (!groups.TryGetValue((first, second), out var group))
                    {
                        group = new Group { First = first, Second = second, Source = first };
                        groups.Add((first, second), group); result.Groups.Add(group);
                    }
                    group.Seams.Add(seamIndex);
                }
            }
            return result;
        }

        private static Vector3Int Cell(Vector3 v, float size) => new Vector3Int(
            Mathf.FloorToInt(v.x / size), Mathf.FloorToInt(v.y / size), Mathf.FloorToInt(v.z / size));
        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        public void ValidateUnchanged()
        {
            foreach (var s in Sources)
                if (!CanScan(s.Asset) || !s.Positions.SequenceEqual(s.Asset.meshData.vertices) ||
                    !s.Normals.SequenceEqual(s.Asset.meshData.normals) ||
                    !(s.Tangents ?? Array.Empty<Vector4>()).SequenceEqual(s.Asset.meshData.tangents ?? Array.Empty<Vector4>()) ||
                    !s.Triangles.SequenceEqual(Triangles(s.Asset) ?? Array.Empty<int>()))
                    throw new InvalidOperationException("Slot data changed since scanning. Cancel and scan again before applying fixes.");
        }

        public Dictionary<SlotDataAsset, Vector3[]> Calculate()
        {
            ValidateUnchanged();
            var fixedNormals = new Dictionary<(int slot, int vertex), Vector3>();
            foreach (var group in Groups.Where(g => g.Pending))
                foreach (int index in group.Seams)
                {
                    var cluster = seams[index];
                    Vector3 Average(int slot)
                    {
                        Vector3 sum = Vector3.zero;
                        foreach (var v in cluster.Where(v => v.slot == slot)) sum += Sources[slot].Normals[v.vertex].normalized;
                        return SafeNormal(sum, Sources[slot].Normals[cluster.First(v => v.slot == slot).vertex]);
                    }
                    Vector3 target = group.Mode == FixMode.Copy ? Average(group.Source) :
                        SafeNormal(Average(group.First) + Average(group.Second), Average(group.First));
                    foreach (var v in cluster.Where(v => v.slot == group.First || v.slot == group.Second))
                    {
                        if (fixedNormals.TryGetValue(v, out var prior) && Vector3.Angle(prior, target) > .1f)
                            throw new InvalidOperationException("Conflicting fixes at a junction shared by more than two slots. Use compatible source normals or apply these groups separately and rescan.");
                        fixedNormals[v] = target;
                    }
                }
            var output = new Dictionary<SlotDataAsset, Vector3[]>();
            foreach (int slot in fixedNormals.Keys.Select(v => v.slot).Distinct())
            {
                var s = Sources[slot];
                var normals = (Vector3[])s.Normals.Clone();
                var sum = new Vector3[normals.Length]; var weights = new float[normals.Length];
                foreach (var group in Groups.Where(g => g.Pending && g.Smooth && (g.First == slot || g.Second == slot)))
                {
                    // Multi-source breadth-first diffusion is bounded by mesh edges, not vertex count squared.
                    var frontier = new List<int>(); var depth = Enumerable.Repeat(-1, normals.Length).ToArray();
                    var targets = new Vector3[normals.Length];
                    foreach (int seam in group.Seams)
                        foreach (var v in seams[seam].Where(v => v.slot == slot))
                            if (depth[v.vertex] < 0)
                            { depth[v.vertex] = 0; targets[v.vertex] = fixedNormals[v]; frontier.Add(v.vertex); }
                    int rings = Mathf.Clamp(group.Rings, 1, 8);
                    for (int ring = 1; ring <= rings; ring++)
                    {
                        var next = new HashSet<int>();
                        foreach (int v in frontier)
                            foreach (int w in s.Neighbours[v].OrderBy(i => i))
                                if (!fixedNormals.ContainsKey((slot, w)) && (depth[w] < 0 || depth[w] == ring))
                                { depth[w] = ring; targets[w] += targets[v].normalized; next.Add(w); }
                        foreach (int v in next)
                        {
                            targets[v] = SafeNormal(targets[v], normals[v]);
                            float weight = Mathf.Clamp01(group.Strength) * (1f - (float)ring / (rings + 1));
                            sum[v] += targets[v] * weight; weights[v] += weight;
                        }
                        frontier = next.OrderBy(i => i).ToList();
                    }
                }
                for (int i = 0; i < normals.Length; i++)
                    if (weights[i] > 0) normals[i] = SafeNormal(Vector3.Slerp(normals[i], SafeNormal(sum[i], normals[i]),
                        Mathf.Clamp01(weights[i])), normals[i]);
                foreach (var v in fixedNormals.Where(v => v.Key.slot == slot)) normals[v.Key.vertex] = v.Value;
                output.Add(s.Asset, normals);
            }
            return output;
        }

        private static Vector3 SafeNormal(Vector3 value, Vector3 fallback) => value.sqrMagnitude > 1e-12f ? value.normalized : fallback.normalized;

        public static void SetNormals(SlotDataAsset asset, Vector3[] normals)
        {
            var m = asset.meshData;
            var oldNormals = m.normals;
            m.normals = (Vector3[])normals.Clone();
            // Preserve each UV island's tangent handedness, but keep its tangent perpendicular to
            // the repaired normal. Copying tangents between slots would break their different UVs.
            if (m.tangents != null && m.tangents.Length == normals.Length)
            {
                var tangents = (Vector4[])m.tangents.Clone();
                for (int i = 0; i < tangents.Length; i++)
                {
                    if (oldNormals != null && oldNormals.Length == normals.Length && oldNormals[i] == normals[i]) continue;
                    Vector3 n = normals[i].normalized, t = tangents[i];
                    t -= n * Vector3.Dot(n, t);
                    if (t.sqrMagnitude < 1e-12f) t = Vector3.Cross(n, Mathf.Abs(n.y) < .9f ? Vector3.up : Vector3.right);
                    t.Normalize(); tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
                }
                m.tangents = tangents; m.tangentsModified = true;
            }
            m.normalsModified = true;
            m.InvalidateBuildData();
        }
    }
}
