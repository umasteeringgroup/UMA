using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    /// <summary>Immutable triangle BVH. Queries use world units and both sides of triangles;
    /// no scene colliders, physics layers, or changes to the edited mesh are required.</summary>
    public sealed class TexturePaintGeometryQueries
    {
        private struct Triangle { public Vector3 a, b, c, center; public Bounds bounds; }
        private struct Node { public Bounds bounds; public int start, count, left, right; }
        private readonly Triangle[] triangles;
        private readonly List<Node> nodes = new List<Node>();
        public Bounds Bounds => nodes.Count == 0 ? default : nodes[0].bounds;

        public TexturePaintGeometryQueries(Vector3[] vertices, int[] indices, Matrix4x4 transform)
        {
            triangles = new Triangle[indices.Length / 3];
            for (int i = 0; i < triangles.Length; i++)
            {
                Vector3 a = transform.MultiplyPoint3x4(vertices[indices[i * 3]]);
                Vector3 b = transform.MultiplyPoint3x4(vertices[indices[i * 3 + 1]]);
                Vector3 c = transform.MultiplyPoint3x4(vertices[indices[i * 3 + 2]]);
                var bounds = new Bounds(a, Vector3.zero); bounds.Encapsulate(b); bounds.Encapsulate(c);
                triangles[i] = new Triangle { a = a, b = b, c = c, center = (a + b + c) / 3, bounds = bounds };
            }
            if (triangles.Length > 0) Build(0, triangles.Length);
        }

        private int Build(int start, int count)
        {
            var bounds = triangles[start].bounds;
            for (int i = start + 1; i < start + count; i++) bounds.Encapsulate(triangles[i].bounds);
            int index = nodes.Count; nodes.Add(default);
            var node = new Node { bounds = bounds, start = start, count = count };
            if (count > 8)
            {
                Vector3 size = bounds.size;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Array.Sort(triangles, start, count, Comparer<Triangle>.Create((a, b) => a.center[axis].CompareTo(b.center[axis])));
                int half = count / 2;
                node.left = Build(start, half); node.right = Build(start + half, count - half); node.count = 0;
            }
            nodes[index] = node; return index;
        }

        public float Distance(Vector3 origin, Vector3 direction, float maximumDistance)
        {
            if (nodes.Count == 0 || direction.sqrMagnitude < 1e-12f || maximumDistance <= 0) return float.PositiveInfinity;
            direction.Normalize(); float closest = maximumDistance; bool hit = false;
            Visit(0, origin, direction, ref closest, ref hit);
            return hit ? closest : float.PositiveInfinity;
        }

        private void Visit(int index, Vector3 origin, Vector3 direction, ref float closest, ref bool hit)
        {
            Node node = nodes[index];
            if (!Intersects(node.bounds, origin, direction, closest)) return;
            if (node.count == 0)
            { Visit(node.left, origin, direction, ref closest, ref hit); Visit(node.right, origin, direction, ref closest, ref hit); return; }
            for (int i = node.start; i < node.start + node.count; i++)
            {
                Triangle triangle = triangles[i]; Vector3 e1 = triangle.b - triangle.a, e2 = triangle.c - triangle.a;
                Vector3 p = Vector3.Cross(direction, e2); float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-12f) continue;
                float inverse = 1 / det; Vector3 t = origin - triangle.a;
                float u = Vector3.Dot(t, p) * inverse; if (u < 0 || u > 1) continue;
                Vector3 q = Vector3.Cross(t, e1); float v = Vector3.Dot(direction, q) * inverse;
                if (v < 0 || u + v > 1) continue;
                float distance = Vector3.Dot(e2, q) * inverse;
                if (distance > 0 && distance <= closest) { closest = distance; hit = true; }
            }
        }

        private static bool Intersects(Bounds bounds, Vector3 origin, Vector3 direction, float maximum)
        {
            float near = 0, far = maximum;
            for (int axis = 0; axis < 3; axis++)
            {
                float d = direction[axis], o = origin[axis];
                if (Mathf.Abs(d) < 1e-12f) { if (o < bounds.min[axis] || o > bounds.max[axis]) return false; continue; }
                float a = (bounds.min[axis] - o) / d, b = (bounds.max[axis] - o) / d;
                near = Mathf.Max(near, Mathf.Min(a, b)); far = Mathf.Min(far, Mathf.Max(a, b));
                if (near > far) return false;
            }
            return true;
        }

        public float Occlusion(Vector3 position, Vector3 normal, int samples, float radius, float bias)
        {
            normal.Normalize();
            Vector3 tangent = Vector3.Cross(Mathf.Abs(normal.y) < .9f ? Vector3.up : Vector3.right, normal).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            float blocked = 0; samples = Mathf.Clamp(samples, 1, 256);
            for (int i = 0; i < samples; i++)
            {
                // Deterministic cosine-weighted hemisphere: no random noise between regenerations.
                float r = Mathf.Sqrt((i + .5f) / samples), angle = i * 2.39996323f;
                Vector3 direction = tangent * (r * Mathf.Cos(angle)) + bitangent * (r * Mathf.Sin(angle)) + normal * Mathf.Sqrt(1 - r * r);
                float distance = Distance(position + normal * bias, direction, radius);
                if (!float.IsPositiveInfinity(distance)) blocked += 1 - distance / radius;
            }
            return Mathf.Clamp01(1 - blocked / samples);
        }

        public float Thickness(Vector3 position, Vector3 normal, float maximumDistance, float bias)
        {
            normal.Normalize();
            float distance = Distance(position - normal * bias, -normal, maximumDistance);
            // An open sheet has no opposing surface. Never invent thickness from a box.
            return float.IsPositiveInfinity(distance) ? 0 : distance + bias;
        }
    }
}
