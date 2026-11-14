//Adapted to Jobs/Burst by Kenamis
/* 
 * The following code was adapted from: https://schemingdeveloper.com
 *
 * Visit our game studio website: http://stopthegnomes.com
 *
 * License: You may use this code however you see fit, as long as you include this notice
 *          without any modifications.
 *
 *          You may not publish a paid asset on Unity store if its main function is based on
 *          the following code, but you may publish a paid asset that uses this code.
 *
 *          If you intend to use this in a Unity store asset or a commercial project, it would
 *          be appreciated, but not required, if you let me know with a link to the asset. If I
 *          don't get back to you just go ahead and use it anyway!
 */
namespace UMA
{
#if UMA_BURSTCOMPILE
    using Unity.Collections;
    using Unity.Mathematics;
    using Unity.Jobs;
    using Unity.Burst;
    using UnityEngine;

    public static class MeshUtilities
    {
        public static JobHandle BakeOneFramePositionBlendShape(NativeArray<Vector3> vertices, NativeArray<Vector3> deltaVertices, float weight, JobHandle dependsOn = default)
        {
            var job = new BakeBlendShape
            {
                weight = weight,
                vertices = vertices,
                deltaVertices = deltaVertices
            };

            return job.Schedule(vertices.Length, 32, dependsOn);
        }

        public struct BakeBlendShape : IJobParallelFor
        {
            public float weight;
            public NativeArray<Vector3> vertices;
            [ReadOnly][DeallocateOnJobCompletion] public NativeArray<Vector3> deltaVertices;

            public void Execute(int index)
            {
                vertices[index] += (deltaVertices[index] * weight);
            }
        }

        /// <summary>
        /// Recalculates the normals of a mesh and writes them back. A convenience wrapper that copies the mesh
        /// arrays; code that already holds the data should call the NativeArray overload, which also returns
        /// the tangents.
        /// </summary>
        public static void RecalculateNormalsTangentsJobified(this Mesh mesh, float angle)
        {
            int vertexCount = mesh.vertexCount;
            Vector3[] meshNormals = mesh.normals;
            Vector2[] meshUVs = mesh.uv;
            Vector4[] meshTangents = mesh.tangents;

            NativeArray<Vector3> vertices = new NativeArray<Vector3>(mesh.vertices, Allocator.TempJob);
            NativeArray<Vector3> normals = meshNormals.Length == vertexCount ? new NativeArray<Vector3>(meshNormals, Allocator.TempJob) : new NativeArray<Vector3>(vertexCount, Allocator.TempJob);
            NativeArray<Vector2> uvs = meshUVs.Length == vertexCount ? new NativeArray<Vector2>(meshUVs, Allocator.TempJob) : new NativeArray<Vector2>(vertexCount, Allocator.TempJob);
            NativeArray<Vector4> tangents = meshTangents.Length == vertexCount ? new NativeArray<Vector4>(meshTangents, Allocator.TempJob) : new NativeArray<Vector4>(vertexCount, Allocator.TempJob);
            NativeArray<int> triangles = new NativeArray<int>(mesh.triangles, Allocator.TempJob);
            try
            {
                RecalculateNormalsTangentsJobified(vertices, normals, uvs, tangents, triangles, angle).Complete();
                mesh.SetNormals(normals);
            }
            finally
            {
                vertices.Dispose();
                normals.Dispose();
                uvs.Dispose();
                tangents.Dispose();
                triangles.Dispose();
            }
        }

        /// <summary>
        /// Schedules the recalculation of normals and tangents and returns the handle to complete.
        ///
        /// Vertices at the same position (to 1 / Tolerance) are smoothed together where their faces are within
        /// <paramref name="angle"/> degrees of each other, so seams between split vertices stay closed. The same
        /// input always gives the same output.
        ///
        /// <paramref name="normals"/> and <paramref name="tangents"/> are written; a vertex that no triangle uses
        /// keeps the normal it had. All five arrays must have one element per vertex (three per triangle for
        /// <paramref name="triangles"/>) and stay allocated until the handle has completed.
        /// </summary>
        public static JobHandle RecalculateNormalsTangentsJobified(NativeArray<Vector3> vertices, NativeArray<Vector3> normals, NativeArray<Vector2> uvs, NativeArray<Vector4> tangents, NativeArray<int> triangles, float angle, JobHandle dependsOn = default)
        {
            int triangleCount = triangles.Length / 3;
            int cornerCount = triangleCount * 3;

            NativeArray<Vector3> tan1 = new NativeArray<Vector3>(vertices.Length, Allocator.TempJob);
            NativeArray<Vector3> tan2 = new NativeArray<Vector3>(vertices.Length, Allocator.TempJob);
            NativeArray<float3> triNormals = new NativeArray<float3>(triangleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            // One entry per triangle corner; see RecalculateNormalsFirstJob.
            NativeArray<ulong> corners = new NativeArray<ulong>(cornerCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            NativeArray<ulong> sortScratch = new NativeArray<ulong>(cornerCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            var normalsFirstJob = new RecalculateNormalsFirstJob
            {
                triangles = triangles,
                vertices = vertices,
                triNormals = triNormals,
                corners = corners
            };
            var normalsFirstHandle = normalsFirstJob.Schedule(triangleCount, 64, dependsOn);

            var normalsSecondJob = new RecalculateNormalsSecondJob
            {
                corners = corners,
                scratch = sortScratch
            };
            var normalsSecondHandle = normalsSecondJob.Schedule(normalsFirstHandle);

            var normalsLastJob = new RecalculateNormalsLastJob
            {
                corners = corners,
                triangles = triangles,
                triNormals = triNormals,
                normals = normals,
                cosineThreshold = math.cos(angle * Mathf.Deg2Rad)
            };
            var normalsLastHandle = normalsLastJob.Schedule(cornerCount, 256, normalsSecondHandle);

            // Does not read the normals, so it runs alongside the normal jobs.
            var tangentsFirstJob = new RecalculateTangentsFirstJob
            {
                triangles = triangles,
                vertices = vertices,
                uvs = uvs,
                tan1 = tan1,
                tan2 = tan2
            };
            var tangentsFirstHandle = tangentsFirstJob.Schedule(dependsOn);

            var tangentsLastJob = new RecalculateTangentsLastJob
            {
                normals = normals,
                tan1 = tan1,
                tan2 = tan2,
                tangents = tangents
            };

            return tangentsLastJob.Schedule(uvs.Length, 32, JobHandle.CombineDependencies(normalsLastHandle, tangentsFirstHandle));
        }

        // Change this if you require a different precision.
        private const int Tolerance = 100000;
        // Magic FNV values. Do not change these.
        private const long FNV32Init = 0x811c9dc5;
        private const long FNV32Prime = 0x01000193;

        public static int Vector3Hash(Vector3 vector)
        {
            long rv = FNV32Init;
            rv ^= (long)math.round(vector.x * Tolerance);
            rv *= FNV32Prime;
            rv ^= (long)math.round(vector.y * Tolerance);
            rv *= FNV32Prime;
            rv ^= (long)math.round(vector.z * Tolerance);
            rv *= FNV32Prime;

            return rv.GetHashCode();
        }

        // Works out the face normal of each triangle, and one sort key per triangle corner: the hash of the
        // corner's position in the high 32 bits and the corner's index (triangle * 3 + corner) in the low 32.
        [BurstCompile(CompileSynchronously = true)]
        public struct RecalculateNormalsFirstJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> triangles;
            [ReadOnly] public NativeArray<Vector3> vertices;
            [WriteOnly] public NativeArray<float3> triNormals;
            // Each triangle writes its own three corners.
            [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<ulong> corners;

            public void Execute(int triIndex)
            {
                int corner = triIndex * 3;
                Vector3 v0 = vertices[triangles[corner]];
                Vector3 v1 = vertices[triangles[corner + 1]];
                Vector3 v2 = vertices[triangles[corner + 2]];

                float3 p1 = v1 - v0;
                float3 p2 = v2 - v0;
                float3 normal = math.cross(p1, p2);
                float magnitude = math.length(normal);
                if (magnitude > 0)
                {
                    normal /= magnitude;
                }

                triNormals[triIndex] = normal;

                corners[corner] = ((ulong)(uint)Vector3Hash(v0) << 32) | (uint)corner;
                corners[corner + 1] = ((ulong)(uint)Vector3Hash(v1) << 32) | (uint)(corner + 1);
                corners[corner + 2] = ((ulong)(uint)Vector3Hash(v2) << 32) | (uint)(corner + 2);
            }
        }

        // Sorts the corners by position hash, so that all corners at one position lie next to each other. A radix
        // sort on the hash, one byte at a time: it keeps the corners of one position in corner order, which is
        // what makes the result the same on every run.
        [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
        public struct RecalculateNormalsSecondJob : IJob
        {
            public NativeArray<ulong> corners;
            [DeallocateOnJobCompletion] public NativeArray<ulong> scratch;

            public void Execute()
            {
                const int Passes = 4;
                const int Buckets = 256;
                int count = corners.Length;

                var offsets = new NativeArray<int>(Passes * Buckets, Allocator.Temp);
                for (int i = 0; i < count; i++)
                {
                    ulong key = corners[i];
                    offsets[(int)((key >> 32) & 0xFF)]++;
                    offsets[Buckets + (int)((key >> 40) & 0xFF)]++;
                    offsets[2 * Buckets + (int)((key >> 48) & 0xFF)]++;
                    offsets[3 * Buckets + (int)((key >> 56) & 0xFF)]++;
                }
                for (int pass = 0; pass < Passes; pass++)
                {
                    int sum = 0;
                    for (int bucket = 0; bucket < Buckets; bucket++)
                    {
                        int size = offsets[pass * Buckets + bucket];
                        offsets[pass * Buckets + bucket] = sum;
                        sum += size;
                    }
                }

                // An even number of passes, so the sorted corners end up back in 'corners'.
                NativeArray<ulong> source = corners;
                NativeArray<ulong> target = scratch;
                for (int pass = 0; pass < Passes; pass++)
                {
                    int shift = 32 + pass * 8;
                    int first = pass * Buckets;
                    for (int i = 0; i < count; i++)
                    {
                        ulong key = source[i];
                        int bucket = first + (int)((key >> shift) & 0xFF);
                        target[offsets[bucket]++] = key;
                    }
                    NativeArray<ulong> swap = source;
                    source = target;
                    target = swap;
                }
                offsets.Dispose();
            }
        }

        // Smooths the normals of one position at a time. Every index is visited, and the one that starts a run
        // of corners with the same position hash handles the whole run, so each position is done exactly once.
        [BurstCompile(CompileSynchronously = true)]
        public struct RecalculateNormalsLastJob : IJobParallelFor
        {
            [ReadOnly][DeallocateOnJobCompletion] public NativeArray<ulong> corners;
            [ReadOnly] public NativeArray<int> triangles;
            [ReadOnly][DeallocateOnJobCompletion] public NativeArray<float3> triNormals;
            // Two positions never share a vertex, so no two runs write the same element.
            [NativeDisableParallelForRestriction] public NativeArray<Vector3> normals;
            public float cosineThreshold;

            public void Execute(int index)
            {
                uint hash = (uint)(corners[index] >> 32);
                if (index > 0 && (uint)(corners[index - 1] >> 32) == hash)
                {
                    return; // inside a run that an earlier index handles
                }
                int end = index + 1;
                while (end < corners.Length && (uint)(corners[end] >> 32) == hash)
                {
                    end++;
                }

                for (int lhs = index; lhs < end; lhs++)
                {
                    int lhsCorner = (int)(uint)corners[lhs];
                    int vertex = triangles[lhsCorner];

                    // A vertex has one corner in the run for each of its triangles. Do it once, from its first
                    // triangle: where the triangles of one vertex meet at more than the angle, that is the
                    // triangle the faces of the other vertices are compared with. (The hash map this replaced
                    // left that to chance, and mostly landed on the first triangle.)
                    bool firstCornerOfVertex = true;
                    for (int earlier = index; earlier < lhs; earlier++)
                    {
                        if (triangles[(int)(uint)corners[earlier]] == vertex)
                        {
                            firstCornerOfVertex = false;
                            break;
                        }
                    }
                    if (!firstCornerOfVertex)
                    {
                        continue;
                    }

                    float3 lhsNormal = triNormals[lhsCorner / 3];
                    float3 sum = float3.zero;
                    for (int rhs = index; rhs < end; rhs++)
                    {
                        int rhsCorner = (int)(uint)corners[rhs];
                        float3 rhsNormal = triNormals[rhsCorner / 3];
                        // The vertex's own triangles always count. Those of another vertex at this position
                        // count when the faces are within the angle: the dot product is its cosine, and a
                        // larger cosine means a smaller angle.
                        if (triangles[rhsCorner] == vertex || math.dot(lhsNormal, rhsNormal) >= cosineThreshold)
                        {
                            sum += rhsNormal;
                        }
                    }

                    float length = math.length(sum);
                    normals[vertex] = length > 1e-5f ? (Vector3)(sum / length) : Vector3.zero;
                }
            }
        }

        // Adds each triangle's tangent directions to its three vertices. On one thread: triangles that share a
        // vertex add into the same element, and doing that from several threads loses additions.
        [BurstCompile(CompileSynchronously = true)]
        public struct RecalculateTangentsFirstJob : IJob
        {
            [ReadOnly] public NativeArray<int> triangles;
            [ReadOnly] public NativeArray<Vector3> vertices;
            [ReadOnly] public NativeArray<Vector2> uvs;
            public NativeArray<Vector3> tan1;
            public NativeArray<Vector3> tan2;

            public void Execute()
            {
                int triangleCount = triangles.Length / 3;
                for (int index = 0; index < triangleCount; index++)
                {
                    int i1 = triangles[index * 3];
                    int i2 = triangles[index * 3 + 1];
                    int i3 = triangles[index * 3 + 2];

                    Vector3 v1 = vertices[i1];
                    Vector3 v2 = vertices[i2];
                    Vector3 v3 = vertices[i3];

                    Vector2 w1 = uvs[i1];
                    Vector2 w2 = uvs[i2];
                    Vector2 w3 = uvs[i3];

                    float x1 = v2.x - v1.x;
                    float x2 = v3.x - v1.x;
                    float y1 = v2.y - v1.y;
                    float y2 = v3.y - v1.y;
                    float z1 = v2.z - v1.z;
                    float z2 = v3.z - v1.z;

                    float s1 = w2.x - w1.x;
                    float s2 = w3.x - w1.x;
                    float t1 = w2.y - w1.y;
                    float t2 = w3.y - w1.y;

                    float div = s1 * t2 - s2 * t1;
                    float r = div == 0.0f ? 0.0f : 1.0f / div;

                    Vector3 sDir = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, (t2 * z1 - t1 * z2) * r);
                    Vector3 tDir = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, (s1 * z2 - s2 * z1) * r);

                    tan1[i1] += sDir;
                    tan1[i2] += sDir;
                    tan1[i3] += sDir;

                    tan2[i1] += tDir;
                    tan2[i2] += tDir;
                    tan2[i3] += tDir;
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct RecalculateTangentsLastJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Vector3> normals;
            [ReadOnly][DeallocateOnJobCompletion] public NativeArray<Vector3> tan1;
            [ReadOnly][DeallocateOnJobCompletion] public NativeArray<Vector3> tan2;
            public NativeArray<Vector4> tangents;

            public void Execute(int index)
            {
                Vector3 n = normals[index];
                Vector3 t = tan1[index];

                Vector3.OrthoNormalize(ref n, ref t);
                Vector4 tangent = new Vector4();
                tangent.x = t.x;
                tangent.y = t.y;
                tangent.z = t.z;
                tangent.w = (math.dot(math.cross(n, t), tan2[index]) < 0.0f) ? -1.0f : 1.0f;

                tangents[index] = tangent;
            }
        }
    }
#endif
}