using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.Editors.Weighting
{
    /// <summary>The closest point of the donor surface to a point, and what the body is like there.</summary>
    public struct UMAWeightHit
    {
        public bool Found;
        public int Triangle;
        public Vector3 Point;
        /// <summary>Barycentric coordinates of <see cref="Point"/> on the triangle's three vertices.</summary>
        public Vector3 Barycentric;
        public float Distance;
        /// <summary>The body's smooth (interpolated vertex) normal at the point.</summary>
        public Vector3 Normal;
        /// <summary>Signed distance along that normal: negative inside the body.</summary>
        public float Signed;

        /// <summary>
        /// Inside the body by more than <paramref name="minDepth"/>: behind the surface, and behind it squarely (the
        /// way to the surface runs along its normal). Past the open edge of a surface (a slot's border, the space
        /// beyond a patch) the way runs sideways, which is not inside.
        /// </summary>
        public bool IsInside(float minDepth) => Found && Signed < 0f && Distance > minDepth && -Signed >= 0.5f * Distance;
    }

    /// <summary>
    /// A donor surface's triangles in a uniform grid, for closest-point queries from many threads at once (each
    /// thread passes its own <see cref="Query"/> scratch). The answer is the same as testing every triangle.
    /// </summary>
    public sealed class UMAWeightSurface
    {
        private readonly Vector3[] positions;
        private readonly int[] triangles;
        private readonly Vector3[] normals;
        private readonly Vector3 origin;
        private readonly float cell;
        private readonly int nx, ny, nz;
        private readonly int[] cellStart;
        private readonly int[] cellTriangles;

        public int TriangleCount => triangles.Length / 3;
        public Bounds Bounds { get; }

        /// <summary>Per-thread scratch for queries.</summary>
        public sealed class Query
        {
            internal int[] Seen;
            internal int Stamp;
        }

        public Query NewQuery() => new Query { Seen = new int[Math.Max(1, TriangleCount)] };

        public UMAWeightSurface(Vector3[] positions, int[] triangles, Vector3[] vertexNormals)
        {
            this.positions = positions;
            this.triangles = triangles ?? new int[0];
            normals = vertexNormals;
            int count = this.triangles.Length / 3;
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            double edges = 0;
            for (int t = 0; t < count; t++)
            {
                Vector3 a = positions[this.triangles[t * 3]], b = positions[this.triangles[t * 3 + 1]], c = positions[this.triangles[t * 3 + 2]];
                min = Vector3.Min(min, Vector3.Min(a, Vector3.Min(b, c)));
                max = Vector3.Max(max, Vector3.Max(a, Vector3.Max(b, c)));
                edges += (b - a).magnitude + (c - b).magnitude + (a - c).magnitude;
            }
            if (count == 0) { min = max = Vector3.zero; }
            Bounds = new Bounds((min + max) * 0.5f, max - min);
            Vector3 size = max - min;
            float typical = (float)(edges / Math.Max(1, count * 3));
            cell = Mathf.Max(typical * 2f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)) / 96f, 1e-4f);
            origin = min;
            nx = Mathf.Max(1, Mathf.CeilToInt(size.x / cell) + 1);
            ny = Mathf.Max(1, Mathf.CeilToInt(size.y / cell) + 1);
            nz = Mathf.Max(1, Mathf.CeilToInt(size.z / cell) + 1);
            var counts = new int[nx * ny * nz + 1];
            for (int pass = 0; pass < 2; pass++)
            {
                int[] write = pass == 1 ? (int[])counts.Clone() : null;
                for (int t = 0; t < count; t++)
                {
                    Vector3 a = positions[this.triangles[t * 3]], b = positions[this.triangles[t * 3 + 1]], c = positions[this.triangles[t * 3 + 2]];
                    Vector3Int lo = Cell(Vector3.Min(a, Vector3.Min(b, c))), hi = Cell(Vector3.Max(a, Vector3.Max(b, c)));
                    for (int z = lo.z; z <= hi.z; z++)
                        for (int y = lo.y; y <= hi.y; y++)
                            for (int x = lo.x; x <= hi.x; x++)
                            {
                                int ci = x + nx * (y + ny * z);
                                if (pass == 0) counts[ci + 1]++;
                                else cellTriangles[write[ci]++] = t;
                            }
                }
                if (pass == 0)
                {
                    for (int ci = 0; ci < counts.Length - 1; ci++) counts[ci + 1] += counts[ci];
                    cellStart = counts;
                    cellTriangles = new int[counts[counts.Length - 1]];
                }
            }
        }

        private Vector3Int Cell(Vector3 p)
        {
            Vector3 q = (p - origin) / cell;
            return new Vector3Int(Mathf.Clamp(Mathf.FloorToInt(q.x), 0, nx - 1), Mathf.Clamp(Mathf.FloorToInt(q.y), 0, ny - 1), Mathf.Clamp(Mathf.FloorToInt(q.z), 0, nz - 1));
        }

        /// <summary>The closest point of the surface to <paramref name="point"/>.</summary>
        public UMAWeightHit Closest(Vector3 point, Query query) => Closest(point, query, float.MaxValue);

        /// <summary>
        /// The closest point of the surface within <paramref name="maxDistance"/> of <paramref name="point"/> (not
        /// found when there is none that close). Far cheaper than an unbounded search for points far from the body.
        /// </summary>
        public UMAWeightHit Closest(Vector3 point, Query query, float maxDistance)
        {
            var hit = new UMAWeightHit { Triangle = -1, Distance = float.MaxValue };
            int count = TriangleCount;
            if (count == 0) return hit;
            if (++query.Stamp == int.MaxValue) { Array.Clear(query.Seen, 0, query.Seen.Length); query.Stamp = 1; }
            int stamp = query.Stamp;
            Vector3Int c = Cell(point);
            float best = float.MaxValue;
            int bestTriangle = -1;
            Vector3 bestPoint = point;
            int maxRing = Mathf.Max(nx, Mathf.Max(ny, nz));
            // A point's cell is clamped into the grid: from outside, start counting the reach from the grid's edge.
            Vector3 outside = Vector3.Max(origin - point, Vector3.Max(point - (origin + new Vector3(nx, ny, nz) * cell), Vector3.zero));
            if (maxDistance < float.MaxValue)
            {
                if (outside.magnitude > maxDistance) return hit;
                maxRing = Mathf.Min(maxRing, Mathf.CeilToInt(maxDistance / cell) + 1);
            }
            for (int ring = 0; ring <= maxRing; ring++)
            {
                for (int z = c.z - ring; z <= c.z + ring; z++)
                {
                    if (z < 0 || z >= nz) continue;
                    for (int y = c.y - ring; y <= c.y + ring; y++)
                    {
                        if (y < 0 || y >= ny) continue;
                        for (int x = c.x - ring; x <= c.x + ring; x++)
                        {
                            if (x < 0 || x >= nx) continue;
                            if (Mathf.Abs(x - c.x) != ring && Mathf.Abs(y - c.y) != ring && Mathf.Abs(z - c.z) != ring) continue;
                            int ci = x + nx * (y + ny * z);
                            for (int k = cellStart[ci]; k < cellStart[ci + 1]; k++)
                            {
                                int t = cellTriangles[k];
                                if (query.Seen[t] == stamp) continue;
                                query.Seen[t] = stamp;
                                Vector3 q = UMASlotSkinTransfer.ClosestPointOnTriangle(point, positions[triangles[t * 3]], positions[triangles[t * 3 + 1]], positions[triangles[t * 3 + 2]]);
                                float d = (q - point).sqrMagnitude;
                                if (d < best || (d == best && t < bestTriangle)) { best = d; bestTriangle = t; bestPoint = q; }
                            }
                        }
                    }
                }
                float reach = ring * cell;
                if (bestTriangle >= 0 && best <= reach * reach) break;
            }
            if (bestTriangle < 0 || best > maxDistance * maxDistance) return hit;
            int ia = triangles[bestTriangle * 3], ib = triangles[bestTriangle * 3 + 1], ic = triangles[bestTriangle * 3 + 2];
            Vector3 bary = Barycentric(bestPoint, positions[ia], positions[ib], positions[ic]);
            Vector3 n = normals != null ? normals[ia] * bary.x + normals[ib] * bary.y + normals[ic] * bary.z : Vector3.zero;
            if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(positions[ib] - positions[ia], positions[ic] - positions[ia]);
            n = n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up;
            hit.Found = true;
            hit.Triangle = bestTriangle;
            hit.Point = bestPoint;
            hit.Barycentric = bary;
            hit.Distance = Mathf.Sqrt(best);
            hit.Normal = n;
            hit.Signed = Vector3.Dot(point - bestPoint, n);
            // When the smooth normal is nearly square to the offset, the triangle's own normal says which side the
            // point is on, and how squarely (a point beside an open edge stays beside it, not deep behind it).
            if (hit.Distance > 1e-6f && Mathf.Abs(hit.Signed) < hit.Distance * 0.05f)
            {
                Vector3 face = Vector3.Cross(positions[ib] - positions[ia], positions[ic] - positions[ia]);
                if (face.sqrMagnitude > 1e-20f) hit.Signed = Vector3.Dot(point - bestPoint, face.normalized);
            }
            return hit;
        }

        /// <summary>The vertices of a triangle.</summary>
        public void TriangleVertices(int triangle, out int a, out int b, out int c)
        {
            a = triangles[triangle * 3]; b = triangles[triangle * 3 + 1]; c = triangles[triangle * 3 + 2];
        }

        /// <summary>Barycentric coordinates of a point on (or projected into) a triangle, clamped to it.</summary>
        public static Vector3 Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
            double d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d11 = Vector3.Dot(v1, v1);
            double d20 = Vector3.Dot(v2, v0), d21 = Vector3.Dot(v2, v1);
            double denom = d00 * d11 - d01 * d01;
            if (Math.Abs(denom) < 1e-20)
            {
                // A sliver: share by inverse distance, as the old transfer did.
                Vector3 w = UMASlotSkinTransfer.CalculateHitVertexBlend(p, a, b, c);
                return w;
            }
            double v = (d11 * d20 - d01 * d21) / denom;
            double w2 = (d00 * d21 - d01 * d20) / denom;
            double u = 1.0 - v - w2;
            float fu = Mathf.Max(0f, (float)u), fv = Mathf.Max(0f, (float)v), fw = Mathf.Max(0f, (float)w2);
            float s = fu + fv + fw;
            return s > 0f ? new Vector3(fu / s, fv / s, fw / s) : new Vector3(1f, 0f, 0f);
        }

        /// <summary>The donor's weights at a hit, blended from the triangle's vertices: up to <paramref name="maxBones"/>, normalized.</summary>
        public static void WeightsAt(UMAWeightHit hit, UMAWeightSurface surface, BoneWeight1[][] vertexWeights, List<BoneWeight1> result, int maxBones = 12)
        {
            result.Clear();
            if (!hit.Found) return;
            surface.TriangleVertices(hit.Triangle, out int a, out int b, out int c);
            Accumulate(result, vertexWeights[a], hit.Barycentric.x);
            Accumulate(result, vertexWeights[b], hit.Barycentric.y);
            Accumulate(result, vertexWeights[c], hit.Barycentric.z);
            Finish(result, maxBones);
        }

        internal static void Accumulate(List<BoneWeight1> into, BoneWeight1[] weights, float scale)
        {
            if (weights == null || scale <= 0f) return;
            foreach (BoneWeight1 w in weights)
            {
                if (w.weight <= 0f) continue;
                int k = -1;
                for (int i = 0; i < into.Count; i++) if (into[i].boneIndex == w.boneIndex) { k = i; break; }
                if (k < 0) into.Add(new BoneWeight1 { boneIndex = w.boneIndex, weight = w.weight * scale });
                else into[k] = new BoneWeight1 { boneIndex = w.boneIndex, weight = into[k].weight + w.weight * scale };
            }
        }

        internal static void Finish(List<BoneWeight1> weights, int maxBones)
        {
            weights.Sort((x, y) => y.weight.CompareTo(x.weight));
            if (weights.Count > maxBones) weights.RemoveRange(maxBones, weights.Count - maxBones);
            float sum = 0f;
            foreach (BoneWeight1 w in weights) sum += w.weight;
            if (sum <= 0f) { weights.Clear(); return; }
            for (int i = 0; i < weights.Count; i++) weights[i] = new BoneWeight1 { boneIndex = weights[i].boneIndex, weight = weights[i].weight / sum };
        }
    }
}
