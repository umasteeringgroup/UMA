using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    [Serializable]
    public sealed class HairGuideGenerationSettings
    {
        [Min(1)] public int guideCount = 50;
        [Range(2, 32)] public int pointsPerGuide = 6;
        [Min(0.001f)] public float defaultLength = 0.18f;
        [Min(0f)] public float minimumRootSpacing = 0.015f;
        [Range(0f, 1f)] public float surfaceFlow = 0.35f;
        [Range(0f, 1f)] public float lift = 0.85f;
        public int seed = 1729;
        [Min(1)] public int maximumAttemptsPerGuide = 32;

        public void EnsureIntegrity()
        {
            guideCount = Mathf.Max(1, guideCount);
            pointsPerGuide = Mathf.Clamp(pointsPerGuide, 2, 32);
            defaultLength = Mathf.Max(0.001f, defaultLength);
            minimumRootSpacing = Mathf.Max(0f, minimumRootSpacing);
            surfaceFlow = Mathf.Clamp01(surfaceFlow);
            lift = Mathf.Clamp01(lift);
            maximumAttemptsPerGuide = Mathf.Max(1, maximumAttemptsPerGuide);
        }
    }

    public sealed class HairGuideGenerationResult
    {
        public readonly List<HairGuide> guides = new List<HairGuide>();
        public readonly List<string> warnings = new List<string>();
        public int attemptedRoots;
        public int rejectedByMask;
        public int rejectedBySpacing;
    }

    /// <summary>
    /// Deterministically distributes authored guide candidates over the source mesh. It uses
    /// triangle area, Growth Area, Density, Length, Lift and optional tangent-space Flow maps.
    /// The returned guides are detached data; callers decide whether and how to commit them.
    /// </summary>
    public static class HairGuideGenerator
    {
        private struct TriangleSample
        {
            public int submesh;
            public int triangle;
            public int a;
            public int b;
            public int c;
            public float cumulativeWeight;
            public float maximumMaskDensity;
        }

        public static HairGuideGenerationResult Generate(
            HairGroomAsset groom,
            HairGroup group,
            HairGuideGenerationSettings settings)
        {
            HairGuideGenerationResult result = new HairGuideGenerationResult();
            if (groom == null || group == null || groom.SourceMesh == null)
            {
                result.warnings.Add("A groom, group, and readable source mesh are required to generate guides.");
                return result;
            }

            settings ??= new HairGuideGenerationSettings();
            settings.EnsureIntegrity();
            groom.EnsureIntegrity();

            Mesh mesh = groom.SourceMesh;
            Vector3[] vertices;
            Vector3[] normals;
            try
            {
                vertices = mesh.vertices;
                normals = mesh.normals;
            }
            catch (Exception)
            {
                result.warnings.Add("The source mesh must have Read/Write enabled to generate guides.");
                return result;
            }

            HairGrowthMap regionMap = group.FindMap(HairMapKind.GrowthArea);
            HairGrowthMap densityMap = group.FindMap(HairMapKind.Density);
            HairGrowthMap lengthMap = group.FindMap(HairMapKind.Length);
            HairGrowthMap liftMap = group.FindMap(HairMapKind.Lift);
            HairGrowthMap flowXMap = group.FindMap(HairMapKind.FlowX);
            HairGrowthMap flowYMap = group.FindMap(HairMapKind.FlowY);
            HairGrowthMap widthMap = group.FindMap(HairMapKind.Width);

            List<TriangleSample> triangles = BuildWeightedTriangles(mesh, vertices, regionMap, densityMap,
                out float totalWeight);
            if (triangles.Count == 0 || totalWeight <= 1e-8f)
            {
                result.warnings.Add("The active group has no non-zero Growth Area to receive guides.");
                return result;
            }

            HairDeterministicRandom random = new HairDeterministicRandom(settings.seed);
            List<Vector3> acceptedRoots = new List<Vector3>(settings.guideCount);
            int maximumAttempts = settings.guideCount * settings.maximumAttemptsPerGuide;
            for (int attempt = 0; attempt < maximumAttempts && result.guides.Count < settings.guideCount; attempt++)
            {
                result.attemptedRoots++;
                float target = random.Next01() * totalWeight;
                int triangleIndex = FindWeightedTriangle(triangles, target);
                TriangleSample triangle = triangles[triangleIndex];
                Vector3 barycentric = RandomBarycentric(ref random);
                Vector3 root = vertices[triangle.a] * barycentric.x + vertices[triangle.b] * barycentric.y +
                               vertices[triangle.c] * barycentric.z;
                if (!HasSpacing(root, acceptedRoots, settings.minimumRootSpacing))
                {
                    result.rejectedBySpacing++;
                    continue;
                }

                float region = Mathf.Clamp01(Sample(regionMap, triangle, barycentric, 1f));
                float density = Mathf.Max(0f, Sample(densityMap, triangle, barycentric, 1f));
                float acceptance = triangle.maximumMaskDensity > 1e-8f
                    ? Mathf.Clamp01(region * density / triangle.maximumMaskDensity)
                    : 0f;
                if (random.Next01() > acceptance)
                {
                    result.rejectedByMask++;
                    continue;
                }

                Vector3 normal = SampleNormal(normals, vertices, triangle, barycentric);
                BuildTangentFrame(vertices[triangle.a], vertices[triangle.b], vertices[triangle.c], normal,
                    out Vector3 tangent, out Vector3 bitangent);
                float flowX = Sample(flowXMap, triangle, barycentric, 0.5f) * 2f - 1f;
                float flowY = Sample(flowYMap, triangle, barycentric, 0.5f) * 2f - 1f;
                Vector3 flow = tangent * flowX + bitangent * flowY;
                if (flow.sqrMagnitude < 1e-8f) flow = tangent;
                flow.Normalize();
                float paintedLift = liftMap != null
                    ? Mathf.Clamp01(Sample(liftMap, triangle, barycentric, settings.lift))
                    : settings.lift;
                Vector3 direction = Vector3.Slerp(flow, normal, paintedLift).normalized;
                direction = Vector3.Slerp(normal, direction, settings.surfaceFlow).normalized;
                float length = settings.defaultLength * Mathf.Max(0.01f,
                    Sample(lengthMap, triangle, barycentric, 1f));
                float widthScale = Mathf.Max(0.01f, Sample(widthMap, triangle, barycentric, 1f));
                float width = (group.profile != null ? group.profile.DefaultWidth : 0.012f) * widthScale;

                HairSurfaceAnchor anchor = HairSurfaceAnchor.Create(groom.SourceMeshId, triangle.submesh,
                    triangle.triangle, barycentric, 0f, root, normal);
                HairGuide guide = new HairGuide
                {
                    name = $"Guide {result.guides.Count + 1:000}",
                    root = anchor,
                    seed = settings.seed + result.guides.Count * 7919,
                    includeGuideCard = true,
                    lodImportance = 1f
                };
                for (int pointIndex = 0; pointIndex < settings.pointsPerGuide; pointIndex++)
                {
                    float t = pointIndex / (settings.pointsPerGuide - 1f);
                    Vector3 curvedDirection = Vector3.Slerp(normal, direction, Mathf.SmoothStep(0f, 1f, t));
                    Vector3 position = root + curvedDirection.normalized * (length * t);
                    guide.points.Add(new HairGuidePoint
                    {
                        position = position,
                        width = Mathf.Lerp(width, group.profile != null ? group.profile.TipWidth : 0f, t),
                        stiffness = 1f - t
                    });
                }
                guide.EnsureIntegrity(width);
                acceptedRoots.Add(root);
                result.guides.Add(guide);
            }

            if (result.guides.Count < settings.guideCount)
            {
                result.warnings.Add($"Placed {result.guides.Count} of {settings.guideCount} guides. Reduce minimum spacing or expand the Growth Area.");
            }
            return result;
        }

        private static List<TriangleSample> BuildWeightedTriangles(
            Mesh mesh,
            IReadOnlyList<Vector3> vertices,
            HairGrowthMap region,
            HairGrowthMap density,
            out float totalWeight)
        {
            List<TriangleSample> result = new List<TriangleSample>();
            totalWeight = 0f;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetTriangles(submesh, true);
                for (int offset = 0, triangleIndex = 0; offset + 2 < indices.Length; offset += 3, triangleIndex++)
                {
                    int a = indices[offset];
                    int b = indices[offset + 1];
                    int c = indices[offset + 2];
                    float area = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude * 0.5f;
                    float fieldA = Mathf.Clamp01(Sample(region, a, 1f)) *
                                   Mathf.Max(0f, Sample(density, a, 1f));
                    float fieldB = Mathf.Clamp01(Sample(region, b, 1f)) *
                                   Mathf.Max(0f, Sample(density, b, 1f));
                    float fieldC = Mathf.Clamp01(Sample(region, c, 1f)) *
                                   Mathf.Max(0f, Sample(density, c, 1f));
                    float maximumField = Mathf.Max(fieldA, Mathf.Max(fieldB, fieldC));
                    float weight = area * maximumField;
                    if (weight <= 1e-10f) continue;
                    totalWeight += weight;
                    result.Add(new TriangleSample
                    {
                        submesh = submesh,
                        triangle = triangleIndex,
                        a = a,
                        b = b,
                        c = c,
                        cumulativeWeight = totalWeight,
                        maximumMaskDensity = maximumField
                    });
                }
            }
            return result;
        }

        private static int FindWeightedTriangle(IReadOnlyList<TriangleSample> triangles, float target)
        {
            int low = 0;
            int high = triangles.Count - 1;
            while (low < high)
            {
                int middle = (low + high) >> 1;
                if (target <= triangles[middle].cumulativeWeight) high = middle;
                else low = middle + 1;
            }
            return low;
        }

        private static Vector3 RandomBarycentric(ref HairDeterministicRandom random)
        {
            float u = random.Next01();
            float v = random.Next01();
            if (u + v > 1f)
            {
                u = 1f - u;
                v = 1f - v;
            }
            return new Vector3(1f - u - v, u, v);
        }

        private static float Sample(HairGrowthMap map, TriangleSample triangle, Vector3 barycentric,
            float fallback)
        {
            if (map == null) return fallback;
            return Sample(map, triangle.a, fallback) * barycentric.x +
                   Sample(map, triangle.b, fallback) * barycentric.y +
                   Sample(map, triangle.c, fallback) * barycentric.z;
        }

        private static float Sample(HairGrowthMap map, int vertex, float fallback)
        {
            return map != null ? map.SampleVertex(vertex) : fallback;
        }

        private static Vector3 SampleNormal(IReadOnlyList<Vector3> normals, IReadOnlyList<Vector3> vertices,
            TriangleSample triangle, Vector3 barycentric)
        {
            if (normals != null && normals.Count == vertices.Count)
            {
                Vector3 normal = normals[triangle.a] * barycentric.x + normals[triangle.b] * barycentric.y +
                                 normals[triangle.c] * barycentric.z;
                if (normal.sqrMagnitude > 1e-8f) return normal.normalized;
            }
            return Vector3.Cross(vertices[triangle.b] - vertices[triangle.a],
                vertices[triangle.c] - vertices[triangle.a]).normalized;
        }

        private static void BuildTangentFrame(Vector3 a, Vector3 b, Vector3 c, Vector3 normal,
            out Vector3 tangent, out Vector3 bitangent)
        {
            tangent = Vector3.ProjectOnPlane(b - a, normal).normalized;
            if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.ProjectOnPlane(c - a, normal).normalized;
            if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.Cross(normal, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.right;
            bitangent = Vector3.Cross(normal, tangent).normalized;
        }

        private static bool HasSpacing(Vector3 candidate, IReadOnlyList<Vector3> accepted, float spacing)
        {
            if (spacing <= 0f) return true;
            float square = spacing * spacing;
            for (int i = 0; i < accepted.Count; i++)
            {
                if ((accepted[i] - candidate).sqrMagnitude < square) return false;
            }
            return true;
        }
    }
}
