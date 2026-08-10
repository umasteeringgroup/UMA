using System;
using System.Collections.Generic;
#if UMA_BURSTCOMPILE
using Unity.Burst;
#endif
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Owns the immutable native skinning data for one source mesh and executes the
    /// expensive per-vertex portion of bone baking in parallel. Unity object access
    /// and final Mesh assignment remain on the main thread.
    /// </summary>
    internal sealed class BoneBakingSkinningJobContext : IDisposable
    {
        private readonly int vertexCount;
        private readonly Matrix4x4 targetPose;
        private NativeArray<BoneWeight1> sourceWeights;
        private NativeArray<byte> sourceBonesPerVertex;
        private NativeArray<int> sourceWeightOffsets;
        private NativeArray<int> targetBoneIndices;
        private NativeArray<Matrix4x4> resolvedBoneMatrices;

        public BoneBakingSkinningJobContext(
            int vertexCount,
            BoneWeight1[] weights,
            byte[] bonesPerVertex,
            int[] weightOffsets,
            int[] boneIndices,
            Matrix4x4[] boneMatrices,
            Matrix4x4 targetPose)
        {
            if (vertexCount <= 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
            if (weights == null || weights.Length == 0) throw new ArgumentException("Source weights are empty.", nameof(weights));
            if (bonesPerVertex == null || bonesPerVertex.Length != vertexCount)
                throw new ArgumentException("Bones-per-vertex length does not match the source vertex count.", nameof(bonesPerVertex));
            if (weightOffsets == null || weightOffsets.Length != vertexCount)
                throw new ArgumentException("Weight-offset length does not match the source vertex count.", nameof(weightOffsets));
            if (boneIndices == null) throw new ArgumentNullException(nameof(boneIndices));
            if (boneMatrices == null) throw new ArgumentNullException(nameof(boneMatrices));

            this.vertexCount = vertexCount;
            this.targetPose = targetPose;
            sourceWeights = new NativeArray<BoneWeight1>(weights, Allocator.TempJob);
            sourceBonesPerVertex = new NativeArray<byte>(bonesPerVertex, Allocator.TempJob);
            sourceWeightOffsets = new NativeArray<int>(weightOffsets, Allocator.TempJob);
            targetBoneIndices = new NativeArray<int>(boneIndices, Allocator.TempJob);
            resolvedBoneMatrices = new NativeArray<Matrix4x4>(boneMatrices, Allocator.TempJob);
        }

        public void ProcessVertices(
            Vector3[] inputVertices,
            Vector3[] inputNormals,
            Vector4[] inputTangents,
            int inputOffset,
            Vector3[] outputVertices,
            Vector3[] outputNormals,
            Vector4[] outputTangents,
            int outputOffset,
            List<BoneWeight1> outputWeights,
            List<byte> outputBonesPerVertex)
        {
            ValidateGeometryRange(inputVertices, inputOffset, outputVertices, outputOffset);
            if (outputWeights == null) throw new ArgumentNullException(nameof(outputWeights));
            if (outputBonesPerVertex == null || outputBonesPerVertex.Count < outputOffset + vertexCount)
                throw new ArgumentException("The target bones-per-vertex buffer is too small.", nameof(outputBonesPerVertex));

            NativeArray<Vector3> vertices = CopySlice(inputVertices, inputOffset, vertexCount);
            NativeArray<Vector3> normals = CopyOptionalSlice(inputNormals, inputOffset, vertexCount, out bool hasNormals);
            NativeArray<Vector4> tangents = CopyOptionalSlice(inputTangents, inputOffset, vertexCount, out bool hasTangents);
            NativeArray<BoneWeight1> remappedWeights = new NativeArray<BoneWeight1>(sourceWeights.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            NativeArray<byte> remappedBonesPerVertex = new NativeArray<byte>(vertexCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            JobHandle handle = default;
            bool scheduled = false;
            try
            {
                handle = CreateJob(vertices, normals, tangents, hasNormals, hasTangents, true, remappedWeights, remappedBonesPerVertex)
                    .Schedule(vertexCount, 64);
                scheduled = true;
                handle.Complete();

                NativeArray<Vector3>.Copy(vertices, 0, outputVertices, outputOffset, vertexCount);
                if (outputNormals != null && outputNormals.Length >= outputOffset + vertexCount)
                    NativeArray<Vector3>.Copy(normals, 0, outputNormals, outputOffset, vertexCount);
                if (outputTangents != null && outputTangents.Length >= outputOffset + vertexCount)
                    NativeArray<Vector4>.Copy(tangents, 0, outputTangents, outputOffset, vertexCount);

                for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                {
                    byte count = remappedBonesPerVertex[vertexIndex];
                    outputBonesPerVertex[outputOffset + vertexIndex] = count;
                    int weightOffset = sourceWeightOffsets[vertexIndex];
                    for (int weightIndex = 0; weightIndex < count; weightIndex++)
                        outputWeights.Add(remappedWeights[weightOffset + weightIndex]);
                }
            }
            finally
            {
                if (scheduled) handle.Complete();
                DisposeArray(ref vertices);
                DisposeArray(ref normals);
                DisposeArray(ref tangents);
                DisposeArray(ref remappedWeights);
                DisposeArray(ref remappedBonesPerVertex);
            }
        }

        public void ProcessBlendShape(
            Vector3[] inputVertices,
            Vector3[] inputNormals,
            Vector3[] inputTangents,
            Vector3[] outputVertices,
            Vector3[] outputNormals,
            Vector3[] outputTangents,
            int outputOffset)
        {
            if (inputVertices == null || inputVertices.Length < vertexCount)
                throw new ArgumentException("Blendshape vertex buffer is too small.", nameof(inputVertices));
            if (outputVertices == null || outputVertices.Length < outputOffset + vertexCount)
                throw new ArgumentException("Target blendshape vertex buffer is too small.", nameof(outputVertices));

            NativeArray<Vector3> vertices = CopySlice(inputVertices, 0, vertexCount);
            NativeArray<Vector3> normals = CopyBlendShapeVectors(inputNormals, inputVertices, vertexCount);
            NativeArray<Vector4> tangents = CopyBlendShapeTangents(inputTangents, inputVertices, vertexCount);
            // Optional containers must still be valid when a job is scheduled, even though
            // the blendshape job never writes remapped bone weights.
            NativeArray<BoneWeight1> unusedWeights = new NativeArray<BoneWeight1>(1, Allocator.TempJob);
            NativeArray<byte> unusedCounts = new NativeArray<byte>(1, Allocator.TempJob);

            JobHandle handle = default;
            bool scheduled = false;
            try
            {
                handle = CreateJob(vertices, normals, tangents, true, true, false, unusedWeights, unusedCounts)
                    .Schedule(vertexCount, 64);
                scheduled = true;
                handle.Complete();

                NativeArray<Vector3>.Copy(vertices, 0, outputVertices, outputOffset, vertexCount);
                if (outputNormals != null && outputNormals.Length >= outputOffset + vertexCount)
                    NativeArray<Vector3>.Copy(normals, 0, outputNormals, outputOffset, vertexCount);
                if (outputTangents != null && outputTangents.Length >= outputOffset + vertexCount)
                {
                    for (int i = 0; i < vertexCount; i++)
                    {
                        Vector4 tangent = tangents[i];
                        outputTangents[outputOffset + i] = new Vector3(tangent.x, tangent.y, tangent.z);
                    }
                }
            }
            finally
            {
                if (scheduled) handle.Complete();
                DisposeArray(ref vertices);
                DisposeArray(ref normals);
                DisposeArray(ref tangents);
                DisposeArray(ref unusedWeights);
                DisposeArray(ref unusedCounts);
            }
        }

        private BoneBakingSkinningJob CreateJob(
            NativeArray<Vector3> vertices,
            NativeArray<Vector3> normals,
            NativeArray<Vector4> tangents,
            bool hasNormals,
            bool hasTangents,
            bool writeBoneWeights,
            NativeArray<BoneWeight1> outputWeights,
            NativeArray<byte> outputBonesPerVertex)
        {
            return new BoneBakingSkinningJob
            {
                Vertices = vertices,
                Normals = normals,
                Tangents = tangents,
                SourceWeights = sourceWeights,
                SourceBonesPerVertex = sourceBonesPerVertex,
                SourceWeightOffsets = sourceWeightOffsets,
                TargetBoneIndices = targetBoneIndices,
                ResolvedBoneMatrices = resolvedBoneMatrices,
                OutputWeights = outputWeights,
                OutputBonesPerVertex = outputBonesPerVertex,
                TargetPose = targetPose,
                HasNormals = hasNormals,
                HasTangents = hasTangents,
                WriteBoneWeights = writeBoneWeights
            };
        }

        private void ValidateGeometryRange(Vector3[] input, int inputOffset, Vector3[] output, int outputOffset)
        {
            if (input == null || inputOffset < 0 || input.Length < inputOffset + vertexCount)
                throw new ArgumentException("Source vertex buffer is too small.", nameof(input));
            if (output == null || outputOffset < 0 || output.Length < outputOffset + vertexCount)
                throw new ArgumentException("Target vertex buffer is too small.", nameof(output));
        }

        private static NativeArray<T> CopySlice<T>(T[] source, int offset, int count) where T : struct
        {
            var result = new NativeArray<T>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            NativeArray<T>.Copy(source, offset, result, 0, count);
            return result;
        }

        private static NativeArray<T> CopyOptionalSlice<T>(T[] source, int offset, int count, out bool hasContent) where T : struct
        {
            hasContent = source != null && offset >= 0 && source.Length >= offset + count;
            return hasContent
                ? CopySlice(source, offset, count)
                : new NativeArray<T>(count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        }

        private static NativeArray<Vector3> CopyBlendShapeVectors(Vector3[] preferred, Vector3[] fallback, int count)
        {
            return preferred != null && preferred.Length >= count
                ? CopySlice(preferred, 0, count)
                : CopySlice(fallback, 0, count);
        }

        private static NativeArray<Vector4> CopyBlendShapeTangents(Vector3[] preferred, Vector3[] fallback, int count)
        {
            Vector3[] source = preferred != null && preferred.Length >= count ? preferred : fallback;
            var result = new NativeArray<Vector4>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < count; i++)
            {
                Vector3 tangent = source[i];
                result[i] = new Vector4(tangent.x, tangent.y, tangent.z, 1f);
            }
            return result;
        }

        public void Dispose()
        {
            DisposeArray(ref sourceWeights);
            DisposeArray(ref sourceBonesPerVertex);
            DisposeArray(ref sourceWeightOffsets);
            DisposeArray(ref targetBoneIndices);
            DisposeArray(ref resolvedBoneMatrices);
        }

        private static void DisposeArray<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated) array.Dispose();
            array = default;
        }
    }

#if UMA_BURSTCOMPILE
    [BurstCompile]
#endif
    internal struct BoneBakingSkinningJob : IJobParallelFor
    {
        public NativeArray<Vector3> Vertices;
        public NativeArray<Vector3> Normals;
        public NativeArray<Vector4> Tangents;
        [ReadOnly] public NativeArray<BoneWeight1> SourceWeights;
        [ReadOnly] public NativeArray<byte> SourceBonesPerVertex;
        [ReadOnly] public NativeArray<int> SourceWeightOffsets;
        [ReadOnly] public NativeArray<int> TargetBoneIndices;
        [ReadOnly] public NativeArray<Matrix4x4> ResolvedBoneMatrices;
        [NativeDisableParallelForRestriction] public NativeArray<BoneWeight1> OutputWeights;
        public NativeArray<byte> OutputBonesPerVertex;
        public Matrix4x4 TargetPose;
        public bool HasNormals;
        public bool HasTangents;
        public bool WriteBoneWeights;

        public void Execute(int vertexIndex)
        {
            Vector3 sourceVertex = Vertices[vertexIndex];
            Vector3 sourceNormal = HasNormals ? Normals[vertexIndex] : Vector3.zero;
            Vector4 sourceTangent = HasTangents ? Tangents[vertexIndex] : Vector4.zero;
            Vector3 globalVertex = Vector3.zero;
            Vector3 globalNormal = Vector3.zero;
            Vector3 globalTangent = Vector3.zero;
            bool tangentSign = false;
            bool hasTangentSign = false;

            int sourceOffset = SourceWeightOffsets[vertexIndex];
            int sourceCount = SourceBonesPerVertex[vertexIndex];
            int written = 0;

            for (int influenceIndex = 0; influenceIndex < sourceCount; influenceIndex++)
            {
                BoneWeight1 influence = SourceWeights[sourceOffset + influenceIndex];
                float weight = influence.weight;
                if (weight <= 0f) continue;

                int sourceBoneIndex = influence.boneIndex;
                Matrix4x4 matrix = ResolvedBoneMatrices[sourceBoneIndex];
                AddSkinnedInfluence(
                    ref matrix,
                    ref sourceVertex,
                    ref sourceNormal,
                    ref sourceTangent,
                    weight,
                    ref globalVertex,
                    ref globalNormal,
                    ref globalTangent);

                if (!hasTangentSign)
                {
                    tangentSign = sourceTangent.w > 0f;
                    hasTangentSign = true;
                }

                if (!WriteBoneWeights) continue;

                int targetBoneIndex = TargetBoneIndices[sourceBoneIndex];
                int existing = -1;
                for (int outputIndex = 0; outputIndex < written; outputIndex++)
                {
                    if (OutputWeights[sourceOffset + outputIndex].boneIndex == targetBoneIndex)
                    {
                        existing = outputIndex;
                        break;
                    }
                }

                if (existing >= 0)
                {
                    BoneWeight1 merged = OutputWeights[sourceOffset + existing];
                    merged.weight += weight;
                    OutputWeights[sourceOffset + existing] = merged;
                }
                else
                {
                    OutputWeights[sourceOffset + written] = new BoneWeight1
                    {
                        boneIndex = targetBoneIndex,
                        weight = weight
                    };
                    written++;
                }
            }

            if (WriteBoneWeights)
            {
                SortWeightsDescending(sourceOffset, written);
                OutputBonesPerVertex[vertexIndex] = (byte)written;
            }

            Vertices[vertexIndex] = MultiplyPoint3x4(ref TargetPose, ref globalVertex);
            Normals[vertexIndex] = MultiplyVector(ref TargetPose, ref globalNormal);
            Vector3 bakedTangent = MultiplyVector(ref TargetPose, ref globalTangent);
            Tangents[vertexIndex] = new Vector4(
                bakedTangent.x,
                bakedTangent.y,
                bakedTangent.z,
                tangentSign ? 1f : -1f);
        }

        private void SortWeightsDescending(int offset, int count)
        {
            for (int i = 0; i < count - 1; i++)
            {
                int best = i;
                for (int j = i + 1; j < count; j++)
                {
                    if (OutputWeights[offset + j].weight > OutputWeights[offset + best].weight)
                        best = j;
                }

                if (best == i) continue;
                BoneWeight1 swap = OutputWeights[offset + i];
                OutputWeights[offset + i] = OutputWeights[offset + best];
                OutputWeights[offset + best] = swap;
            }
        }

        private static void AddSkinnedInfluence(
            ref Matrix4x4 matrix,
            ref Vector3 vertex,
            ref Vector3 normal,
            ref Vector4 tangent,
            float weight,
            ref Vector3 globalVertex,
            ref Vector3 globalNormal,
            ref Vector3 globalTangent)
        {
            globalVertex.x += (matrix.m00 * vertex.x + matrix.m01 * vertex.y + matrix.m02 * vertex.z + matrix.m03) * weight;
            globalVertex.y += (matrix.m10 * vertex.x + matrix.m11 * vertex.y + matrix.m12 * vertex.z + matrix.m13) * weight;
            globalVertex.z += (matrix.m20 * vertex.x + matrix.m21 * vertex.y + matrix.m22 * vertex.z + matrix.m23) * weight;

            globalNormal.x += (matrix.m00 * normal.x + matrix.m01 * normal.y + matrix.m02 * normal.z) * weight;
            globalNormal.y += (matrix.m10 * normal.x + matrix.m11 * normal.y + matrix.m12 * normal.z) * weight;
            globalNormal.z += (matrix.m20 * normal.x + matrix.m21 * normal.y + matrix.m22 * normal.z) * weight;

            globalTangent.x += (matrix.m00 * tangent.x + matrix.m01 * tangent.y + matrix.m02 * tangent.z) * weight;
            globalTangent.y += (matrix.m10 * tangent.x + matrix.m11 * tangent.y + matrix.m12 * tangent.z) * weight;
            globalTangent.z += (matrix.m20 * tangent.x + matrix.m21 * tangent.y + matrix.m22 * tangent.z) * weight;
        }

        private static Vector3 MultiplyPoint3x4(ref Matrix4x4 matrix, ref Vector3 point)
        {
            return new Vector3(
                matrix.m00 * point.x + matrix.m01 * point.y + matrix.m02 * point.z + matrix.m03,
                matrix.m10 * point.x + matrix.m11 * point.y + matrix.m12 * point.z + matrix.m13,
                matrix.m20 * point.x + matrix.m21 * point.y + matrix.m22 * point.z + matrix.m23);
        }

        private static Vector3 MultiplyVector(ref Matrix4x4 matrix, ref Vector3 vector)
        {
            return new Vector3(
                matrix.m00 * vector.x + matrix.m01 * vector.y + matrix.m02 * vector.z,
                matrix.m10 * vector.x + matrix.m11 * vector.y + matrix.m12 * vector.z,
                matrix.m20 * vector.x + matrix.m21 * vector.y + matrix.m22 * vector.z);
        }
    }
}
