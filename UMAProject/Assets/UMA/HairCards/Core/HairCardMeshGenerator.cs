using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.HairCards
{
    public static class HairCardMeshGenerator
    {
        private sealed class MaterialBucket
        {
            public Material material;
            public readonly List<int> triangles = new List<int>();
        }

        public static HairCardMeshBuildResult Build(
            HairEvaluationResult evaluation,
            string meshName = "Generated Hair Cards")
        {
            HairCardMeshBuildResult result = new HairCardMeshBuildResult();
            Mesh mesh = new Mesh { name = string.IsNullOrWhiteSpace(meshName) ? "Generated Hair Cards" : meshName };
            result.mesh = mesh;
            if (evaluation == null || evaluation.curves.Count == 0)
            {
                mesh.Clear();
                return result;
            }

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector4> tangents = new List<Vector4>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> colors = new List<Color>();
            List<MaterialBucket> buckets = new List<MaterialBucket>();
            Dictionary<Material, int> materialLookup = new Dictionary<Material, int>();

            for (int curveIndex = 0; curveIndex < evaluation.curves.Count; curveIndex++)
            {
                HairEvaluatedCurve curve = evaluation.curves[curveIndex];
                if (curve == null || curve.points.Count < 2) continue;
                HairCardProfileAsset profile = curve.profile;
                int sampleCount = profile != null ? profile.SamplesPerCard : 12;
                List<HairCurvePoint> sampled = HairCurveUtility.Resample(curve.points, sampleCount);
                HairAtlasRegion region = curve.atlas?.GetWeightedRegion(HashCurve(curve));
                Material material = curve.atlas != null ? curve.atlas.material : null;
                int bucketIndex = GetMaterialBucket(material, buckets, materialLookup);
                int flips;
                if (profile != null && profile.Shape == HairCardShape.TaperedTube)
                {
                    int sides = profile.TubeSides;
                    AppendTube(curve, sampled, profile, region, sides, vertices, normals, tangents, uvs,
                        colors, buckets[bucketIndex].triangles, out flips, ref result.degenerateTriangleCount);
                }
                else
                {
                    AppendRibbon(curve, sampled, profile, region, vertices, normals, tangents, uvs,
                        colors, buckets[bucketIndex].triangles, out flips, ref result.degenerateTriangleCount);
                }
                result.frameFlipCount += flips;
                result.cardCount++;
            }

            mesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.subMeshCount = Mathf.Max(1, buckets.Count);
            if (buckets.Count == 0)
            {
                mesh.SetTriangles(Array.Empty<int>(), 0, false);
            }
            else
            {
                for (int bucketIndex = 0; bucketIndex < buckets.Count; bucketIndex++)
                {
                    mesh.SetTriangles(buckets[bucketIndex].triangles, bucketIndex, false);
                    result.materials.Add(buckets[bucketIndex].material);
                    result.materialNames.Add(buckets[bucketIndex].material != null
                        ? buckets[bucketIndex].material.name
                        : "Default Hair Material");
                }
            }
            mesh.RecalculateBounds();
            result.vertexCount = vertices.Count;
            int indexCount = 0;
            for (int bucketIndex = 0; bucketIndex < buckets.Count; bucketIndex++)
            {
                indexCount += buckets[bucketIndex].triangles.Count;
            }
            result.triangleCount = indexCount / 3;
            return result;
        }

        private static void AppendRibbon(
            HairEvaluatedCurve curve,
            IReadOnlyList<HairCurvePoint> points,
            HairCardProfileAsset profile,
            HairAtlasRegion region,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles,
            out int flipCount,
            ref int degenerateCount)
        {
            int start = vertices.Count;
            Vector3[] curveTangents = new Vector3[points.Count];
            Vector3[] sides = new Vector3[points.Count];
            Vector3[] frameNormals = new Vector3[points.Count];
            HairCurveUtility.BuildRotationMinimizingFrames(points, curve.rootNormal, curveTangents,
                sides, frameNormals, out flipCount);
            for (int i = 0; i < points.Count; i++)
            {
                float t = i / (points.Count - 1f);
                float profileWidth = profile != null ? profile.EvaluateWidth(t) : Mathf.Lerp(0.01f, 0f, t);
                float width = points[i].width > 0f ? points[i].width : profileWidth;
                if (profile != null) width *= Mathf.Max(0f, profileWidth / Mathf.Max(profile.DefaultWidth, 1e-6f));
                Vector3 half = sides[i] * (width * 0.5f);
                vertices.Add(points[i].position - half);
                vertices.Add(points[i].position + half);
                normals.Add(frameNormals[i]);
                normals.Add(frameNormals[i]);
                Vector4 tangent = new Vector4(curveTangents[i].x, curveTangents[i].y, curveTangents[i].z, 1f);
                tangents.Add(tangent);
                tangents.Add(tangent);
                uvs.Add(MapUv(region, 0f, t));
                uvs.Add(MapUv(region, 1f, t));
                colors.Add(curve.groupColor);
                colors.Add(curve.groupColor);
            }

            bool doubleSided = profile == null || profile.DoubleSided;
            for (int i = 0; i < points.Count - 1; i++)
            {
                int a = start + i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                AddTriangle(vertices, triangles, a, c, b, ref degenerateCount);
                AddTriangle(vertices, triangles, b, c, d, ref degenerateCount);
                if (doubleSided)
                {
                    AddTriangle(vertices, triangles, b, c, a, ref degenerateCount);
                    AddTriangle(vertices, triangles, d, c, b, ref degenerateCount);
                }
            }
        }

        private static void AppendTube(
            HairEvaluatedCurve curve,
            IReadOnlyList<HairCurvePoint> points,
            HairCardProfileAsset profile,
            HairAtlasRegion region,
            int sidesPerRing,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> triangles,
            out int flipCount,
            ref int degenerateCount)
        {
            int start = vertices.Count;
            Vector3[] curveTangents = new Vector3[points.Count];
            Vector3[] sides = new Vector3[points.Count];
            Vector3[] frameNormals = new Vector3[points.Count];
            HairCurveUtility.BuildRotationMinimizingFrames(points, curve.rootNormal, curveTangents,
                sides, frameNormals, out flipCount);
            int sidesCount = Mathf.Clamp(sidesPerRing, 3, 12);
            for (int ring = 0; ring < points.Count; ring++)
            {
                float t = ring / (points.Count - 1f);
                float profileWidth = profile.EvaluateWidth(t);
                float radius = points[ring].width > 0f ? points[ring].width * 0.5f : profileWidth * 0.5f;
                radius *= Mathf.Max(0f, profileWidth / Mathf.Max(profile.DefaultWidth, 1e-6f));
                for (int sideIndex = 0; sideIndex < sidesCount; sideIndex++)
                {
                    float u = sideIndex / (float)sidesCount;
                    float angle = u * Mathf.PI * 2f;
                    Vector3 radial = sides[ring] * Mathf.Cos(angle) + frameNormals[ring] * Mathf.Sin(angle);
                    vertices.Add(points[ring].position + radial * radius);
                    normals.Add(radial.normalized);
                    tangents.Add(new Vector4(curveTangents[ring].x, curveTangents[ring].y,
                        curveTangents[ring].z, 1f));
                    uvs.Add(MapUv(region, u, t));
                    colors.Add(curve.groupColor);
                }
            }

            for (int ring = 0; ring < points.Count - 1; ring++)
            {
                for (int sideIndex = 0; sideIndex < sidesCount; sideIndex++)
                {
                    int nextSide = (sideIndex + 1) % sidesCount;
                    int a = start + ring * sidesCount + sideIndex;
                    int b = start + ring * sidesCount + nextSide;
                    int c = start + (ring + 1) * sidesCount + sideIndex;
                    int d = start + (ring + 1) * sidesCount + nextSide;
                    AddTriangle(vertices, triangles, a, c, b, ref degenerateCount);
                    AddTriangle(vertices, triangles, b, c, d, ref degenerateCount);
                }
            }
        }

        private static int GetMaterialBucket(
            Material material,
            List<MaterialBucket> buckets,
            Dictionary<Material, int> lookup)
        {
            if (material == null)
            {
                for (int bucketIndex = 0; bucketIndex < buckets.Count; bucketIndex++)
                {
                    if (buckets[bucketIndex].material == null) return bucketIndex;
                }
                int nullIndex = buckets.Count;
                buckets.Add(new MaterialBucket());
                return nullIndex;
            }
            if (lookup.TryGetValue(material, out int existing)) return existing;
            int index = buckets.Count;
            buckets.Add(new MaterialBucket { material = material });
            lookup.Add(material, index);
            return index;
        }

        private static Vector2 MapUv(HairAtlasRegion region, float u, float v)
        {
            if (region == null) return new Vector2(u, v);
            float mappedU = region.flipU ? 1f - u : u;
            float mappedV = region.flipV ? 1f - v : v;
            return new Vector2(
                region.uvRect.x + mappedU * region.uvRect.width,
                region.uvRect.y + mappedV * region.uvRect.height);
        }

        private static void AddTriangle(
            IReadOnlyList<Vector3> vertices,
            List<int> triangles,
            int a,
            int b,
            int c,
            ref int degenerateCount)
        {
            Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (cross.sqrMagnitude < 1e-14f)
            {
                degenerateCount++;
                return;
            }
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        private static uint HashCurve(HairEvaluatedCurve curve)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string value = curve.curveId ?? curve.parentGuideId ?? string.Empty;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }
                hash ^= (uint)curve.seed;
                hash *= 16777619u;
                return hash;
            }
        }
    }
}
