#if UMA_BURSTCOMPILE
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA
{
    /// <summary>
    /// Recalculates the normals and tangents of a generated renderer mesh for the renderer's current blendshape
    /// weights. Used by <see cref="UMARendererAsset"/> (Recalculate Normals) and <see cref="RecalculationSlot"/>.
    ///
    /// The result depends only on the mesh's vertex and index data, the smoothing angle, and the weight and
    /// position deltas of the blendshapes that take part. A fingerprint of all of those is kept for each mesh after
    /// it has been recalculated; while the fingerprint still matches, another recalculation would write the same
    /// values again and is skipped. Anything that changes the mesh (a rebuild by a mesh combiner, an editing tool
    /// writing vertices) changes the fingerprint, so the next call recalculates.
    /// </summary>
    [BurstCompile]
    internal static unsafe class UMARendererNormals
    {
        private sealed class State
        {
            // Smoothing angle, blendshape prefix, and the weight and deltas of every blendshape that takes part.
            public ulong inputs;
            // The mesh's vertex streams and index data as they were right after the recalculated values were written.
            public ulong content;
        }

        // Weak keys: an entry goes away with its mesh.
        private static readonly ConditionalWeakTable<Mesh, State> states = new ConditionalWeakTable<Mesh, State>();

        // GetBlendShapeFrameVertices needs a managed array of exactly the vertex count. One is kept between calls.
        private static Vector3[] deltaBuffer;

        /// <summary>
        /// Forgets what is known about a mesh. Call when the mesh has just been given new geometry; it only saves
        /// the comparison, the next recalculation would notice the change anyway.
        /// </summary>
        internal static void Invalidate(Mesh mesh)
        {
            if (!ReferenceEquals(mesh, null))
            {
                states.Remove(mesh);
            }
        }

        /// <summary>
        /// Recalculates normals and tangents of the renderer's mesh, with the position deltas of every blendshape
        /// whose weight is above zero and whose name starts with <paramref name="blendShapeNameStartsWith"/>
        /// applied first. Vertex positions are not changed.
        /// </summary>
        /// <param name="recalculated">Called with the new normals before they are disposed; for tests.</param>
        /// <returns>True when the mesh was written; false when it was missing, not readable, or already holds
        /// the result for exactly these inputs.</returns>
        internal static bool Recalculate(SkinnedMeshRenderer smr, float angle, string blendShapeNameStartsWith, System.Action<NativeArray<Vector3>> recalculated = null)
        {
            Mesh mesh = smr != null ? smr.sharedMesh : null;
            if (mesh == null || mesh.vertexCount == 0 || !mesh.isReadable)
            {
                return false;
            }

            if (blendShapeNameStartsWith == null)
            {
                blendShapeNameStartsWith = string.Empty;
            }

            int vertexCount = mesh.vertexCount;
            var deltas = new NativeList<NativeArray<Vector3>>(4, Allocator.Temp);
            var weights = new NativeList<float>(4, Allocator.Temp);
            int deltasScheduled = 0;
            NativeArray<Vector3> vertices = default;
            NativeArray<Vector3> normals = default;
            NativeArray<Vector2> uvs = default;
            NativeArray<Vector4> tangents = default;
            NativeArray<int> triangles = default;
            JobHandle handle = default;
            try
            {
                ulong inputs = Mix(Mix(0x554D414E6F726D73UL, math.asuint(angle)), (ulong)mesh.blendShapeCount);
                inputs = Mix(inputs, HashString(blendShapeNameStartsWith));

                int blendShapeCount = mesh.blendShapeCount;
                for (int shapeIndex = 0; shapeIndex < blendShapeCount; shapeIndex++)
                {
                    float weight = smr.GetBlendShapeWeight(shapeIndex);
                    if (!(weight > 0f))
                    {
                        continue;
                    }

                    string blendShapeName = mesh.GetBlendShapeName(shapeIndex);
                    if (!blendShapeName.StartsWith(blendShapeNameStartsWith))
                    {
                        continue;
                    }

                    if (deltaBuffer == null || deltaBuffer.Length != vertexCount)
                    {
                        deltaBuffer = new Vector3[vertexCount];
                    }
                    mesh.GetBlendShapeFrameVertices(shapeIndex, 0, deltaBuffer, null, null);
                    var delta = new NativeArray<Vector3>(deltaBuffer, Allocator.TempJob);
                    deltas.Add(delta);
                    weights.Add(weight / 100f); //bring the weight into 0-1 range.

                    inputs = Mix(inputs, ((ulong)(uint)shapeIndex << 32) | math.asuint(weight));
                    inputs = Mix(inputs, HashBytes(NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(delta), (long)vertexCount * sizeof(Vector3)));
                }

                if (states.TryGetValue(mesh, out State state) && state.inputs == inputs && state.content == HashContent(mesh))
                {
                    return false;
                }

                bool hasNormals, hasUVs, hasTangents;
                using (var meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    Mesh.MeshData data = meshDataArray[0];
                    hasNormals = data.HasVertexAttribute(VertexAttribute.Normal);
                    hasUVs = data.HasVertexAttribute(VertexAttribute.TexCoord0);
                    hasTangents = data.HasVertexAttribute(VertexAttribute.Tangent);

                    vertices = new NativeArray<Vector3>(vertexCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    data.GetVertices(vertices);
                    // A stream the mesh does not have starts as zeros.
                    normals = new NativeArray<Vector3>(vertexCount, Allocator.TempJob, hasNormals ? NativeArrayOptions.UninitializedMemory : NativeArrayOptions.ClearMemory);
                    if (hasNormals) data.GetNormals(normals);
                    uvs = new NativeArray<Vector2>(vertexCount, Allocator.TempJob, hasUVs ? NativeArrayOptions.UninitializedMemory : NativeArrayOptions.ClearMemory);
                    if (hasUVs) data.GetUVs(0, uvs);
                    tangents = new NativeArray<Vector4>(vertexCount, Allocator.TempJob, hasTangents ? NativeArrayOptions.UninitializedMemory : NativeArrayOptions.ClearMemory);
                    if (hasTangents) data.GetTangents(tangents);

                    // Every triangle of every submesh, in submesh order, as Mesh.triangles returns them.
                    int indexCount = 0;
                    for (int subMesh = 0; subMesh < data.subMeshCount; subMesh++)
                    {
                        SubMeshDescriptor descriptor = data.GetSubMesh(subMesh);
                        if (descriptor.topology == MeshTopology.Triangles) indexCount += descriptor.indexCount;
                    }
                    triangles = new NativeArray<int>(indexCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    int indexOffset = 0;
                    for (int subMesh = 0; subMesh < data.subMeshCount; subMesh++)
                    {
                        SubMeshDescriptor descriptor = data.GetSubMesh(subMesh);
                        if (descriptor.topology != MeshTopology.Triangles || descriptor.indexCount == 0) continue;
                        data.GetIndices(triangles.GetSubArray(indexOffset, descriptor.indexCount), subMesh);
                        indexOffset += descriptor.indexCount;
                    }
                }

                // In blendshape order. Each delta array is deallocated by its job once that job is scheduled.
                for (; deltasScheduled < deltas.Length; deltasScheduled++)
                {
                    handle = MeshUtilities.BakeOneFramePositionBlendShape(vertices, deltas[deltasScheduled], weights[deltasScheduled], handle);
                }

                handle = MeshUtilities.RecalculateNormalsTangentsJobified(vertices, normals, uvs, tangents, triangles, angle, handle);
                handle.Complete();

                // Positions are left alone: the blendshapes keep being applied by the renderer.
                mesh.SetNormals(normals);
                // Tangents follow the UVs. Without UVs there is nothing to derive them from.
                if (hasUVs)
                {
                    mesh.SetTangents(tangents);
                }
                recalculated?.Invoke(normals);

                if (state == null)
                {
                    state = new State();
                    states.Add(mesh, state);
                }
                state.inputs = inputs;
                state.content = HashContent(mesh);
                return true;
            }
            finally
            {
                handle.Complete();
                for (int i = deltasScheduled; i < deltas.Length; i++)
                {
                    deltas[i].Dispose();
                }
                deltas.Dispose();
                weights.Dispose();
                if (vertices.IsCreated) vertices.Dispose();
                if (normals.IsCreated) normals.Dispose();
                if (uvs.IsCreated) uvs.Dispose();
                if (tangents.IsCreated) tangents.Dispose();
                if (triangles.IsCreated) triangles.Dispose();
            }
        }

        // Fingerprint of everything in the mesh that the recalculation reads or writes: all vertex streams, the
        // index data, the submesh ranges and where the attributes it uses live.
        private static ulong HashContent(Mesh mesh)
        {
            return HashMesh(mesh, true);
        }

        /// <summary>
        /// Fingerprint of a readable mesh's vertex and index data without its normals and tangents, which a
        /// recalculation rewrites. Used to tell whether anything else has written to a mesh since it was built.
        /// </summary>
        internal static ulong HashGeometry(Mesh mesh)
        {
            return HashMesh(mesh, false);
        }

        private static ulong HashMesh(Mesh mesh, bool withNormalsAndTangents)
        {
            using (var meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                Mesh.MeshData data = meshDataArray[0];
                ulong hash = Mix((ulong)data.vertexCount, (ulong)data.subMeshCount);
                hash = MixAttribute(hash, data, VertexAttribute.Position);
                hash = MixAttribute(hash, data, VertexAttribute.Normal);
                hash = MixAttribute(hash, data, VertexAttribute.Tangent);
                hash = MixAttribute(hash, data, VertexAttribute.TexCoord0);

                for (int stream = 0; stream < data.vertexBufferCount; stream++)
                {
                    NativeArray<byte> bytes = data.GetVertexData<byte>(stream);
                    hash = Mix(hash, (ulong)bytes.Length);
                    if (bytes.Length == 0)
                    {
                        continue;
                    }

                    void* pointer = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(bytes);
                    int stride = data.GetVertexBufferStride(stream);
                    int normalStart = 0, normalEnd = 0, tangentStart = 0, tangentEnd = 0;
                    if (!withNormalsAndTangents)
                    {
                        AttributeRange(data, VertexAttribute.Normal, stream, out normalStart, out normalEnd);
                        AttributeRange(data, VertexAttribute.Tangent, stream, out tangentStart, out tangentEnd);
                    }
                    if (normalEnd == normalStart && tangentEnd == tangentStart)
                    {
                        hash = Mix(hash, HashBytes(pointer, bytes.Length));
                    }
                    else if (stride > 0)
                    {
                        hash = Mix(hash, HashVerticesWithout(pointer, bytes.Length / stride, stride, normalStart, normalEnd, tangentStart, tangentEnd));
                    }
                }

                if (data.indexFormat == IndexFormat.UInt16)
                {
                    NativeArray<ushort> indices = data.GetIndexData<ushort>();
                    hash = Mix(hash, (ulong)indices.Length);
                    if (indices.Length > 0)
                    {
                        hash = Mix(hash, HashBytes(NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(indices), (long)indices.Length * sizeof(ushort)));
                    }
                }
                else
                {
                    NativeArray<uint> indices = data.GetIndexData<uint>();
                    hash = Mix(hash, (ulong)indices.Length);
                    if (indices.Length > 0)
                    {
                        hash = Mix(hash, HashBytes(NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(indices), (long)indices.Length * sizeof(uint)));
                    }
                }

                for (int subMesh = 0; subMesh < data.subMeshCount; subMesh++)
                {
                    SubMeshDescriptor descriptor = data.GetSubMesh(subMesh);
                    hash = Mix(hash, ((ulong)(uint)descriptor.indexStart << 32) | (uint)descriptor.indexCount);
                    hash = Mix(hash, ((ulong)(uint)descriptor.baseVertex << 32) | (uint)descriptor.topology);
                }
                return hash;
            }
        }

        // The bytes an attribute occupies inside each vertex of a stream; an empty range when it is not in that stream.
        private static void AttributeRange(Mesh.MeshData data, VertexAttribute attribute, int stream, out int start, out int end)
        {
            start = end = 0;
            if (!data.HasVertexAttribute(attribute) || data.GetVertexAttributeStream(attribute) != stream)
            {
                return;
            }
            start = data.GetVertexAttributeOffset(attribute);
            end = start + data.GetVertexAttributeDimension(attribute) * FormatSize(data.GetVertexAttributeFormat(attribute));
        }

        private static int FormatSize(VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                case VertexAttributeFormat.UInt32:
                case VertexAttributeFormat.SInt32:
                    return 4;
                case VertexAttributeFormat.Float16:
                case VertexAttributeFormat.UNorm16:
                case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16:
                case VertexAttributeFormat.SInt16:
                    return 2;
                default:
                    return 1;
            }
        }

        private static ulong MixAttribute(ulong hash, Mesh.MeshData data, VertexAttribute attribute)
        {
            if (!data.HasVertexAttribute(attribute))
            {
                return Mix(hash, 0xFFFFFFFFUL);
            }
            ulong layout = (ulong)(uint)data.GetVertexAttributeStream(attribute)
                | ((ulong)(uint)data.GetVertexAttributeOffset(attribute) << 8)
                | ((ulong)(uint)data.GetVertexAttributeDimension(attribute) << 32)
                | ((ulong)(uint)data.GetVertexAttributeFormat(attribute) << 40);
            return Mix(hash, layout);
        }

        private static ulong HashString(string text)
        {
            ulong hash = (ulong)text.Length;
            for (int i = 0; i < text.Length; i++)
            {
                hash = Mix(hash, text[i]);
            }
            return hash;
        }

        private static ulong Mix(ulong hash, ulong value)
        {
            hash ^= value + 0x9E3779B97F4A7C15UL + (hash << 6) + (hash >> 2);
            return hash * 0xFF51AFD7ED558CCDUL;
        }

        // Burst-compiled: the vertex data of a character is a few megabytes.
        [BurstCompile]
        private static ulong HashBytes(void* data, long length)
        {
            uint2 hash = xxHash3.Hash64(data, length);
            return ((ulong)hash.y << 32) | hash.x;
        }

        // Every byte of every vertex except the two given ranges (normal and tangent) inside each vertex. The
        // bytes that count are packed into one block first, so they can be hashed in a single pass.
        [BurstCompile]
        private static ulong HashVerticesWithout(void* data, int vertexCount, int stride, int skipStartA, int skipEndA, int skipStartB, int skipEndB)
        {
            // Put the two skipped ranges in order; an empty one sits at the end of the vertex.
            if (skipEndA == skipStartA) { skipStartA = skipEndA = stride; }
            if (skipEndB == skipStartB) { skipStartB = skipEndB = stride; }
            if (skipStartB < skipStartA)
            {
                int start = skipStartA, end = skipEndA;
                skipStartA = skipStartB; skipEndA = skipEndB;
                skipStartB = start; skipEndB = end;
            }

            // The three pieces that are kept: before the first range, between the two, after the second.
            int firstLength = math.max(0, skipStartA);
            int middleStart = math.min(skipEndA, stride);
            int middleLength = math.max(0, skipStartB - middleStart);
            int lastStart = math.min(math.max(skipEndB, middleStart), stride);
            int lastLength = math.max(0, stride - lastStart);
            int kept = firstLength + middleLength + lastLength;
            if (kept == 0 || vertexCount == 0)
            {
                return 0xCBF29CE484222325UL;
            }

            long total = (long)vertexCount * kept;
            byte* packed = (byte*)UnsafeUtility.Malloc(total, 16, Allocator.Temp);
            byte* source = (byte*)data;
            byte* target = packed;
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                if (firstLength > 0) { UnsafeUtility.MemCpy(target, source, firstLength); target += firstLength; }
                if (middleLength > 0) { UnsafeUtility.MemCpy(target, source + middleStart, middleLength); target += middleLength; }
                if (lastLength > 0) { UnsafeUtility.MemCpy(target, source + lastStart, lastLength); target += lastLength; }
                source += stride;
            }
            uint2 hash = xxHash3.Hash64(packed, total);
            UnsafeUtility.Free(packed, Allocator.Temp);
            return ((ulong)hash.y << 32) | hash.x;
        }
    }
}
#endif
