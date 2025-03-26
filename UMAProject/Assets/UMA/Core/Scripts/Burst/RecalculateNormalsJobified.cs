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

        public static void RecalculateNormalsTangentsJobified(this Mesh mesh, float angle)
        {
            //THIS IS JUST AN EXAMPLE
            //Inefficient getter api. It'd be better to pass in existing arrays or use MeshData to get the data off the mesh.
            NativeArray<Vector3> vertices = new NativeArray<Vector3>(mesh.vertices, Allocator.TempJob);
            NativeArray<Vector3> normals = new NativeArray<Vector3>(mesh.normals, Allocator.TempJob);
            NativeArray<Vector2> uvs = new NativeArray<Vector2>(mesh.uv, Allocator.TempJob);
            NativeArray<Vector4> tangents = new NativeArray<Vector4>(mesh.tangents, Allocator.TempJob);
            NativeArray<Vector3> tan1 = new NativeArray<Vector3>(mesh.vertexCount, Allocator.TempJob);
            NativeArray<Vector3> tan2 = new NativeArray<Vector3>(mesh.vertexCount, Allocator.TempJob);
            NativeArray<int> triangles = new NativeArray<int>(mesh.triangles, Allocator.TempJob);
            NativeArray<Vector3> triNormals = new NativeArray<Vector3>(triangles.Length, Allocator.TempJob);
            NativeParallelMultiHashMap<int, int2> map = new NativeParallelMultiHashMap<int, int2>(mesh.vertexCount * 10, Allocator.TempJob);
            NativeList<int> keys = new NativeList<int>(mesh.vertexCount, Allocator.TempJob);

            var normalsFirstJob = new RecalculateNormalsFirstJob
            {
                triangles = triangles.Reinterpret<int, int3>(),
                vertices = vertices,
                triNormals = triNormals,
                map = map.AsParallelWriter(),
            };
            var normalsFirstHandle = normalsFirstJob.Schedule(triangles.Length / 3, 32);

            var normalsSecondJob = new RecalculateNormalsSecondJob
            {
                keys = keys,
                map = map
            };
            var normalsSecondHandle = normalsSecondJob.Schedule(normalsFirstHandle);

            var normalsLastJob = new RecalculateNormalsLastJob
            {
                keys = keys.AsDeferredJobArray(),
                map = map,
                triNormals = triNormals,
                normals = normals,
                cosineThreshold = math.cos(angle * Mathf.Deg2Rad)
            };
            var normalsLastHandle = normalsLastJob.Schedule(keys, 8, normalsSecondHandle);

            var tangentsFirstJob = new RecalculateTangentsFirstJob
            {
                triangles = triangles.Reinterpret<int, int3>(),
                vertices = vertices,
                normals = normals,
                uvs = uvs,
                tan1 = tan1,
                tan2 = tan2
            };
            var tangentsFirstHandle = tangentsFirstJob.Schedule(triangles.Length / 3, 32, normalsLastHandle);

            var tangentsLastJob = new RecalculateTangentsLastJob
            {
                normals = normals,
                tan1 = tan1,
                tan2 = tan2,
                tangents = tangents
            };
            tangentsLastJob.Schedule(uvs.Length, 32, tangentsFirstHandle).Complete();

            mesh.SetNormals(normals);

            vertices.Dispose();
            normals.Dispose();
            uvs.Dispose();
            tangents.Dispose();
            tan1.Dispose();
            tan2.Dispose();
            keys.Dispose();
            map.Dispose();
            triangles.Dispose();
            triNormals.Dispose();
        }

        public static JobHandle RecalculateNormalsTangentsJobified(NativeArray<Vector3> vertices, NativeArray<Vector3> normals, NativeArray<Vector2> uvs, NativeArray<Vector4> tangents, NativeArray<int> triangles, float angle, JobHandle dependsOn = default)
        {
            NativeArray<Vector3> tan1 = new NativeArray<Vector3>(vertices.Length, Allocator.TempJob);
            NativeArray<Vector3> tan2 = new NativeArray<Vector3>(vertices.Length, Allocator.TempJob);
            NativeArray<Vector3> triNormals = new NativeArray<Vector3>(triangles.Length, Allocator.TempJob);
            NativeParallelMultiHashMap<int, int2> map = new NativeParallelMultiHashMap<int, int2>(vertices.Length * 10, Allocator.TempJob);
            NativeList<int> keys = new NativeList<int>(vertices.Length, Allocator.TempJob);

            var normalsFirstJob = new RecalculateNormalsFirstJob
            {
                triangles = triangles.Reinterpret<int, int3>(),
                vertices = vertices,
                triNormals = triNormals,
                map = map.AsParallelWriter(),
            };
            var normalsFirstHandle = normalsFirstJob.Schedule(triangles.Length / 3, 32, dependsOn);

            var normalsSecondJob = new RecalculateNormalsSecondJob
            {
                keys = keys,
                map = map
            };
            var normalsSecondHandle = normalsSecondJob.Schedule(normalsFirstHandle);

            var normalsLastJob = new RecalculateNormalsLastJob
            {
                keys = keys.AsDeferredJobArray(),
                map = map,
                triNormals = triNormals,
                normals = normals,
                cosineThreshold = math.cos(angle * Mathf.Deg2Rad)
            };
            var normalsLastHandle = normalsLastJob.Schedule(keys, 8, normalsSecondHandle);

            var tangentsFirstJob = new RecalculateTangentsFirstJob
            {
                triangles = triangles.Reinterpret<int, int3>(),
                vertices = vertices,
                normals = normals,
                uvs = uvs,
                tan1 = tan1,
                tan2 = tan2
            };
            var tangentsFirstHandle = tangentsFirstJob.Schedule(triangles.Length / 3, 32, normalsLastHandle);

            var tangentsLastJob = new RecalculateTangentsLastJob
            {
                normals = normals,
                tan1 = tan1,
                tan2 = tan2,
                tangents = tangents
            };

            keys.Dispose(normalsLastHandle);
            map.Dispose(normalsLastHandle);

            return tangentsLastJob.Schedule(uvs.Length, 32, tangentsFirstHandle);
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

        [BurstCompile(CompileSynchronously = true)]
        public struct RecalculateNormalsFirstJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int3> triangles;
            [ReadOnly] public NativeArray<Vector3> vertices;
            public NativeArray<Vector3> triNormals;
            public NativeParallelMultiHashMap<int, int2>.ParallelWriter map;

            public void Execute(int triIndex)
            {
                int3 tri = triangles[triIndex];

                float3 p1 = vertices[tri.y] - vertices[tri.x];
                float3 p2 = vertices[tri.z] - vertices[tri.x];
                float3 normal = math.cross(p1, p2);
                float magnitude = math.length(normal);
                if (magnitude > 0)
                {
                    normal /= magnitude;
                }

                triNormals[triIndex] = normal;

                int hash0 = Vector3Hash(vertices[tri.x]);
                int hash1 = Vector3Hash(vertices[tri.y]);
                int hash2 = Vector3Hash(vertices[tri.z]);

                map.Add(hash0, new int2 { x = tri.x, y = triIndex });
                map.Add(hash1, new int2 { x = tri.y, y = triIndex });
                map.Add(hash2, new int2 { x = tri.z, y = triIndex });
            }
        }
        [BurstCompile(CompileSynchronously = true)]
        public struct RecalculateNormalsSecondJob : IJob
        {
            public NativeList<int> keys;
            [ReadOnly] public NativeParallelMultiHashMap<int, int2> map;

            public void Execute()
            {
                keys.AddRange(map.GetKeyArray(Allocator.Temp));
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct RecalculateNormalsLastJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<int> keys;
            [ReadOnly] public NativeParallelMultiHashMap<int, int2> map;
            [ReadOnly][DeallocateOnJobCompletion] public NativeArray<Vector3> triNormals;
            [NativeDisableParallelForRestriction] public NativeArray<Vector3> normals;
            public float cosineThreshold;

            public void Execute(int index)
            {
                var values = map.GetValuesForKey(keys[index]);
                foreach (int2 lhsEntry in values)
                {
                    Vector3 sum = new Vector3();

                    foreach (int2 rhsEntry in values)
                    {
                        if (lhsEntry.x == rhsEntry.x)
                        {
                            sum += triNormals[rhsEntry.y];
                        }
                        else
                        {
                            // The dot product is the cosine of the angle between the two triangles.
                            // A larger cosine means a smaller angle.
                            float dot = math.dot(triNormals[lhsEntry.y], triNormals[rhsEntry.y]);
                            if (dot >= cosineThreshold)
                            {
                                sum += triNormals[rhsEntry.y];
                            }
                        }
                    }

                    normals[lhsEntry.x] = sum.normalized;
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct RecalculateTangentsFirstJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int3> triangles;
            [ReadOnly] public NativeArray<Vector3> vertices;
            [ReadOnly] public NativeArray<Vector3> normals;
            [ReadOnly] public NativeArray<Vector2> uvs;
            [NativeDisableParallelForRestriction] public NativeArray<Vector3> tan1;
            [NativeDisableParallelForRestriction] public NativeArray<Vector3> tan2;

            public void Execute(int index)
            {
                int3 tri = triangles[index];
                int i1 = tri.x;
                int i2 = tri.y;
                int i3 = tri.z;

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