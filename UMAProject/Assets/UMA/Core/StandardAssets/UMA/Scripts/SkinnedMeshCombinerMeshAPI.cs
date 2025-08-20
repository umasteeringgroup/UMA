#if UNITY_2022_2_OR_NEWER
#define UMA_MESHAPI_2022
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
#if UMA_UNSAFE
using Unity.Collections.LowLevel.Unsafe; // UnsafeUtility for high-performance single-stream path
#endif

namespace UMA
{
    /// <summary>
    /// Unity 2022+ MeshData API based combiner.
    /// - Builds a new Mesh and assigns it to the provided SkinnedMeshRenderer(s).
    /// - Uses 32-bit index buffers when possible.
    /// - Supports partial Burst/Jobs (unmasked index copy).
    /// - Supports cloth and blendshape baking (names -> weights).
    /// - Owns UMA-style UV remap for atlased output.
    /// </summary>
    public static class SkinnedMeshCombinerMeshAPI
    {
        public struct RendererBatch
        {
            public SkinnedMeshRenderer Renderer;
            public SkinnedMeshCombiner.CombineInstance[] Sources;
            public int CurrentRendererIndex;
            public int AtlasResolution;
        }

        // Safe path structs (used when UMA_UNSAFE is NOT defined)
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NormTan { public Vector3 normal; public Vector4 tangent; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct ColUV01 { public Color32 color; public Vector2 uv0; public Vector2 uv1; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct UV23 { public Vector2 uv2; public Vector2 uv3; }

        /// <summary>
        /// Combine into one renderer. Returns Cloth coefficients (null if none).
        /// </summary>
        public static ClothSkinningCoefficient[] CombineIntoRenderer(
            SkinnedMeshRenderer renderer,
            SkinnedMeshCombiner.CombineInstance[] sources,
            UMAData umaData,
            int currentRendererIndex,
            int atlasResolution,
            Dictionary<string, float> bakedBlendshapes,
            bool markDynamic = false,
            bool markNotReadable = false)
        {
#if !UMA_MESHAPI_2022
            throw new NotSupportedException("Requires Unity 202.2+ MeshData API (define UNITY_2022_2_OR_NEWER).");
#else
            if (renderer == null) throw new ArgumentNullException("renderer");
            if (sources == null || sources.Length == 0) throw new ArgumentException("sources empty", "sources");
            if (umaData == null) throw new ArgumentNullException("umaData");

            CombineInternal(
                new RendererBatch
                {
                    Renderer = renderer,
                    Sources = sources,
                    CurrentRendererIndex = currentRendererIndex,
                    AtlasResolution = atlasResolution
                },
                umaData,
                bakedBlendshapes ?? new Dictionary<string, float>(),
                markDynamic,
                markNotReadable,
                out var clothCoeffs);

            return clothCoeffs;
#endif
        }

        /// <summary>
        /// Combine into multiple renderers. Returns Cloth coefficients per renderer.
        /// </summary>
        public static ClothSkinningCoefficient[][] CombineIntoRenderers(
            RendererBatch[] batches,
            UMAData umaData,
            Dictionary<string, float> bakedBlendshapes,
            bool markDynamic = false,
            bool markNotReadable = false)
        {
#if !UMA_MESHAPI_2022
            throw new NotSupportedException("Requires Unity 202.2+ MeshData API (define UNITY_2022_2_OR_NEWER).");
#else
            if (batches == null || batches.Length == 0) throw new ArgumentException("batches empty", "batches");
            if (umaData == null) throw new ArgumentNullException("umaData");

            var results = new ClothSkinningCoefficient[batches.Length][];
            for (int i = 0; i < batches.Length; i++)
            {
                if (batches[i].Renderer == null) throw new ArgumentNullException("Renderer at " + i);
                if (batches[i].Sources == null || batches[i].Sources.Length == 0) throw new ArgumentException("sources empty at " + i);

                CombineInternal(
                    batches[i],
                    umaData,
                    bakedBlendshapes ?? new Dictionary<string, float>(),
                    markDynamic,
                    markNotReadable,
                    out var clothCoeffs);

                results[i] = clothCoeffs;
            }
            return results;
#endif
        }

#if UMA_MESHAPI_2022
        private static void CombineInternal(
            RendererBatch batch,
            UMAData umaData,
            Dictionary<string, float> bakedBlendshapes,
            bool markDynamic,
            bool markNotReadable,
            out ClothSkinningCoefficient[] clothCoeffs)
        {
            var sources = batch.Sources;

            // Analyze
            int vertexCount = 0;
            int boneWeightCount = 0;
            int bindPoseCount = 0;
            int transformHierarchyCount = 0;
            int subMeshCount = FindTargetSubMeshCount(sources);
            int[] subMeshTriangleLength = new int[subMeshCount];

            MeshComponents flags = MeshComponents.none;
            AnalyzeSources(sources, subMeshTriangleLength, ref vertexCount, ref boneWeightCount, ref bindPoseCount, ref transformHierarchyCount, ref flags);

            // Blendshape analysis: collect unbaked shape infos
            Dictionary<string, BlendShapeVertexData> blendShapeNames;
            AnalyzeBlendShapeSources(sources, bakedBlendshapes, ref flags, out blendShapeNames, umaData.umaRecipe);

            bool hasNormals = (flags & MeshComponents.has_normals) != 0;
            bool hasTangents = (flags & MeshComponents.has_tangents) != 0;
            bool hasUV = (flags & MeshComponents.has_uv) != 0;
            bool hasUV2 = (flags & MeshComponents.has_uv2) != 0;
            bool hasUV3 = (flags & MeshComponents.has_uv3) != 0;
            bool hasUV4 = (flags & MeshComponents.has_uv4) != 0;
            bool hasColors32 = (flags & MeshComponents.has_colors32) != 0;
            bool hasBlendShapes = (flags & MeshComponents.has_blendShapes) != 0;
            bool hasCloth = (flags & MeshComponents.has_clothSkinning) != 0;

            // Attribute buffers
            var vertices = new Vector3[vertexCount];
            Vector3[] normals = hasNormals ? new Vector3[vertexCount] : null;
            Vector4[] tangents = hasTangents ? new Vector4[vertexCount] : null;
            Vector2[] uv = hasUV ? new Vector2[vertexCount] : null;
            Vector2[] uv2 = hasUV2 ? new Vector2[vertexCount] : null;
            Vector2[] uv3 = hasUV3 ? new Vector2[vertexCount] : null;
            Vector2[] uv4 = hasUV4 ? new Vector2[vertexCount] : null;
            Color32[] colors32 = hasColors32 ? new Color32[vertexCount] : null;

            // Blendshape frames (unbaked)
            UMABlendShape[] bsArray = null;
            if (hasBlendShapes)
            {
                bsArray = new UMABlendShape[blendShapeNames.Keys.Count];
                InitializeBlendShapeData(ref vertexCount, blendShapeNames, bsArray);
            }

            // Bones and bindposes
            var bonesCollection = new Dictionary<int, BoneIndexEntry>(Math.Max(64, bindPoseCount));
            var bindPoses = new List<Matrix4x4>(bindPoseCount);
            var bonesList = new List<int>(transformHierarchyCount);

            var nativeBoneWeights = new NativeArray<BoneWeight1>(boneWeightCount, Allocator.Temp);
            var nativeBonesPerVertex = new NativeArray<byte>(Math.Max(1, vertexCount), Allocator.Temp);

            // Merge UMA transforms (bone tree list)
            int boneCount = 0;
            var mergedUmaTransforms = new UMATransform[transformHierarchyCount];
            for (int i = 0; i < sources.Length; i++)
            {
                MergeSortedTransforms(mergedUmaTransforms, ref boneCount, sources[i].meshData.umaBones);
            }

            // Ensure those bones exist in the skeleton and are parented
            if (umaData != null && umaData.skeleton != null)
            {
                umaData.skeleton.BeginSkeletonUpdate();
                for (int i = 0; i < boneCount; i++)
                    umaData.skeleton.EnsureBone(mergedUmaTransforms[i]);
                umaData.skeleton.EnsureBoneHierarchy();
                umaData.skeleton.EndSkeletonUpdate();
            }

            // UMA-style atlas UV remap will run after we copy all uvs
            // Precompute index buffer totals and per-submesh base offsets (prefix sums)
            int totalIndexCount = 0;
            for (int i = 0; i < subMeshTriangleLength.Length; i++) totalIndexCount += subMeshTriangleLength[i];

            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            var md = meshDataArray[0];
            var indexFormat = IndexFormat.UInt32;

            NativeArray<int> idxDataInt = default;
            var subBase = new int[subMeshCount];          // base write offset per submesh
            var subWrite = new int[subMeshCount];         // running write offset per submesh
            bool hasAnyIndices = (subMeshCount > 0) && (totalIndexCount > 0);

            if (hasAnyIndices)
            {
                // prefix sum for submesh index starts
                int run = 0;
                for (int sm = 0; sm < subMeshCount; sm++)
                {
                    subBase[sm] = run;
                    subWrite[sm] = 0;
                    run += subMeshTriangleLength[sm];
                }

                md.SetIndexBufferParams(totalIndexCount, indexFormat);
                idxDataInt = md.GetIndexData<uint>().Reinterpret<int>();
                md.subMeshCount = subMeshCount; // set before SetSubMesh
            }
            else
            {
                md.SetIndexBufferParams(0, indexFormat);
                md.subMeshCount = 0;
            }

            // Fill vertex data and index buffer
            int vertexIndex = 0;
            int boneWeightIndex = 0;

            // track bounds while copying vertices
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int s = 0; s < sources.Length; s++)
            {
                var src = sources[s];
                int srcVertCount = src.meshData.vertices.Length;

                // Bone weights
                BuildBoneWeights(src.meshData, nativeBoneWeights, nativeBonesPerVertex, vertexIndex, boneWeightIndex, bonesCollection, bindPoses, bonesList);

                // Vertices (+ optional expansion)
                if (src.slotData.expandAlongNormal > 0 && src.meshData.normals != null && src.meshData.normals.Length == srcVertCount)
                {
                    ArrayCopyandExpand(src.meshData, src.slotData.expandAlongNormal, ref vertices, vertexIndex, srcVertCount);
                }
                else
                {
                    Array.Copy(src.meshData.vertices, 0, vertices, vertexIndex, srcVertCount);
                }

                // update bounds
                for (int i = vertexIndex, end = vertexIndex + srcVertCount; i < end; i++)
                {
                    var v = vertices[i];
                    if (v.x < min.x) min.x = v.x; if (v.x > max.x) max.x = v.x;
                    if (v.y < min.y) min.y = v.y; if (v.y > max.y) max.y = v.y;
                    if (v.z < min.z) min.z = v.z; if (v.z > max.z) max.z = v.z;
                }

                // Normals/Tangents
                if (hasNormals)
                {
                    if (src.meshData.normals != null && src.meshData.normals.Length == srcVertCount)
                        Array.Copy(src.meshData.normals, 0, normals, vertexIndex, srcVertCount);
                    else
                        FillArray(normals, vertexIndex, srcVertCount, Vector3.zero);
                }
                if (hasTangents)
                {
                    if (src.meshData.tangents != null && src.meshData.tangents.Length == srcVertCount)
                        Array.Copy(src.meshData.tangents, 0, tangents, vertexIndex, srcVertCount);
                    else
                        FillArray(tangents, vertexIndex, srcVertCount, Vector4.zero);
                }

                // UVs
                if (hasUV)
                {
                    if (src.meshData.uv != null && src.meshData.uv.Length >= srcVertCount)
                        Array.Copy(src.meshData.uv, 0, uv, vertexIndex, srcVertCount);
                    else
                        FillArray(uv, vertexIndex, srcVertCount, Vector2.zero);
                }
                if (hasUV2)
                {
                    if (src.meshData.uv2 != null && src.meshData.uv2.Length >= srcVertCount)
                        Array.Copy(src.meshData.uv2, 0, uv2, vertexIndex, srcVertCount);
                    else
                        FillArray(uv2, vertexIndex, srcVertCount, Vector2.zero);
                }
                if (hasUV3)
                {
                    if (src.meshData.uv3 != null && src.meshData.uv3.Length >= srcVertCount)
                        Array.Copy(src.meshData.uv3, 0, uv3, vertexIndex, srcVertCount);
                    else
                        FillArray(uv3, vertexIndex, srcVertCount, Vector2.zero);
                }
                if (hasUV4)
                {
                    if (src.meshData.uv4 != null && src.meshData.uv4.Length >= srcVertCount)
                        Array.Copy(src.meshData.uv4, 0, uv4, vertexIndex, srcVertCount);
                    else
                        FillArray(uv4, vertexIndex, srcVertCount, Vector2.zero);
                }

                // Colors
                if (hasColors32)
                {
                    if (src.meshData.colors32 != null && src.meshData.colors32.Length == srcVertCount)
                        Array.Copy(src.meshData.colors32, 0, colors32, vertexIndex, srcVertCount);
                    else
                        FillArray(colors32, vertexIndex, srcVertCount, (Color32)Color.white);
                }

                // Blendshapes: bake listed; accumulate others
                if (hasBlendShapes)
                {
                    var sourceShapes = SkinnedMeshCombiner.GetBlendshapeSources(src.meshData, umaData.umaRecipe);
                    for (int b = 0; b < sourceShapes.Count; b++)
                    {
                        var ubs = sourceShapes[b];
                        // Bake if listed
                        if (bakedBlendshapes.TryGetValue(ubs.shapeName, out float value))
                        {
                            if (BakeBlendShape(ubs, value, ref vertexIndex, vertices, normals, tangents, hasNormals, hasTangents))
                                continue;
                        }

                        // Unbaked -> copy frames into bsArray
                        if (blendShapeNames.TryGetValue(ubs.shapeName, out var meta))
                        {
                            int destIdx = meta.index;
                            if (bsArray[destIdx].frames.Length != ubs.frames.Length)
                            {
#if UNITY_EDITOR
                                if (Debug.isDebugBuild) Debug.LogError("BlendShape frame count mismatch!");
#endif
                                break;
                            }

                            for (int f = 0; f < ubs.frames.Length; f++)
                            {
                                var srcF = ubs.frames[f];
                                var dstF = bsArray[destIdx].frames[f];

                                Array.Copy(srcF.deltaVertices, 0, dstF.deltaVertices, vertexIndex, srcVertCount);

                                if (meta.hasNormals && srcF.deltaNormals != null && srcF.deltaNormals.Length > 0)
                                    Array.Copy(srcF.deltaNormals, 0, dstF.deltaNormals, vertexIndex, srcVertCount);
                                else
                                    dstF.deltaNormals = null; // avoid per-frame zero-length alloc

                                if (meta.hasTangents && srcF.deltaTangents != null && srcF.deltaTangents.Length > 0)
                                    Array.Copy(srcF.deltaTangents, 0, dstF.deltaTangents, vertexIndex, srcVertCount);
                                else
                                    dstF.deltaTangents = null; // avoid per-frame zero-length alloc
                            }
                        }
                    }
                }

                // Triangles per submesh -> write directly into final index buffer
                if (hasAnyIndices)
                {
                    for (int i = 0; i < src.meshData.subMeshCount; i++)
                    {
                        int destSub = src.targetSubmeshIndices[i];
                        if (destSub < 0) continue;

                        var srcTris = src.meshData.submeshes[i].GetTriangles();
                        int triLen = srcTris.Length;

                        int destStart = subBase[destSub] + subWrite[destSub];

                        if (src.triangleMask == null)
                        {
                            // Unmasked
                            for (int t = 0; t < triLen; t++)
                            {
                                idxDataInt[destStart + t] = srcTris[t] + vertexIndex;
                            }
                            subWrite[destSub] += triLen;
                        }
                        else
                        {
                            // Masked
                            int wrote = 0;
                            for (int t = 0; t < triLen; t += 3)
                            {
                                if (!src.triangleMask[i][t / 3])
                                {
                                    idxDataInt[destStart + wrote + 0] = srcTris[t + 0] + vertexIndex;
                                    idxDataInt[destStart + wrote + 1] = srcTris[t + 1] + vertexIndex;
                                    idxDataInt[destStart + wrote + 2] = srcTris[t + 2] + vertexIndex;
                                    wrote += 3;
                                }
                            }
                            subWrite[destSub] += wrote;
                        }
                    }
                }

                src.slotData.vertexOffset = vertexIndex;
                src.slotData.skinnedMeshRenderer = batch.CurrentRendererIndex;

                vertexIndex += srcVertCount;
                boneWeightIndex += src.meshData.ManagedBoneWeights.Length;
            }

            // UMA-style atlas UV remap (class owns the mapping)
            if (hasUV)
            {
                RecalculateUVForUMA(uv, umaData, batch.AtlasResolution, batch.CurrentRendererIndex);
            }

            // Vertex buffers (same as before)
#if UMA_UNSAFE
            // ... existing unsafe single-stream code (unchanged) ...
#else
            var descriptors = new List<VertexAttributeDescriptor>(8);
            descriptors.Add(new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0)); // stream 0

            bool anyNT = hasNormals || hasTangents;
            bool anyC01 = hasColors32 || hasUV || hasUV2;
            bool anyUV23 = hasUV3 || hasUV4;

            if (anyNT)
            {
                descriptors.Add(new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1));
                descriptors.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, 1));
            }
            if (anyC01)
            {
                descriptors.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 2));
                descriptors.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 2));
                descriptors.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, 2));
            }
            if (anyUV23)
            {
                descriptors.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2, 3));
                descriptors.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2, 3));
            }

            md.SetVertexBufferParams(vertices.Length, descriptors.ToArray());

            // Stream 0
            var vPos = md.GetVertexData<Vector3>(0);
            for (int i = 0; i < vertices.Length; i++) vPos[i] = vertices[i];

            if (anyNT)
            {
                var vNT = md.GetVertexData<NormTan>(1);
                for (int i = 0; i < vertices.Length; i++)
                {
                    vNT[i] = new NormTan
                    {
                        normal = hasNormals && normals != null && i < normals.Length ? normals[i] : Vector3.zero,
                        tangent = hasTangents && tangents != null && i < tangents.Length ? tangents[i] : Vector4.zero
                    };
                }
            }

            if (anyC01)
            {
                var vC01 = md.GetVertexData<ColUV01>(2);
                for (int i = 0; i < vertices.Length; i++)
                {
                    vC01[i] = new ColUV01
                    {
                        color = hasColors32 && colors32 != null && i < colors32.Length ? colors32[i] : (Color32)Color.white,
                        uv0 = hasUV && uv != null && i < uv.Length ? uv[i] : Vector2.zero,
                        uv1 = hasUV2 && uv2 != null && i < uv2.Length ? uv2[i] : Vector2.zero
                    };
                }
            }

            if (anyUV23)
            {
                var vUV23 = md.GetVertexData<UV23>(3);
                for (int i = 0; i < vertices.Length; i++)
                {
                    vUV23[i] = new UV23
                    {
                        uv2 = hasUV3 && uv3 != null && i < uv3.Length ? uv3[i] : Vector2.zero,
                        uv3 = hasUV4 && uv4 != null && i < uv4.Length ? uv4[i] : Vector2.zero
                    };
                }
            }
#endif

            // Finalize submeshes (just declare ranges) – we already wrote indices
            if (hasAnyIndices)
            {
                for (int i = 0; i < subMeshCount; i++)
                {
                    var sm = new SubMeshDescriptor
                    {
                        topology = MeshTopology.Triangles,
                        indexStart = subBase[i],
                        indexCount = subMeshTriangleLength[i],
                        baseVertex = 0
                    };
                    md.SetSubMesh(i, sm, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                }
            }

            // Create mesh and apply
            var mesh = new Mesh { indexFormat = indexFormat };
            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, new Mesh[] { mesh }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            // Bindposes and bone weights
            mesh.bindposes = bindPoses.ToArray();
            mesh.SetBoneWeights(nativeBonesPerVertex, nativeBoneWeights);

            // Add unbaked blendshapes
            if (hasBlendShapes && bsArray != null)
            {
                for (int s = 0; s < bsArray.Length; s++)
                {
                    var shape = bsArray[s];
                    for (int f = 0; f < shape.frames.Length; f++)
                    {
                        var frame = shape.frames[f];
                        var dn = (frame.deltaNormals != null && frame.deltaNormals.Length > 0) ? frame.deltaNormals : null;
                        var dt = (frame.deltaTangents != null && frame.deltaTangents.Length > 0) ? frame.deltaTangents : null;
                        mesh.AddBlendShapeFrame(shape.shapeName, frame.frameWeight, frame.deltaVertices, dn, dt);
                    }
                }
            }

            // Set bounds we computed (skip RecalculateBounds)
            if (vertexCount > 0)
            {
                var size = max - min;
                var center = min + (size * 0.5f);
                mesh.bounds = new Bounds(center, size);
            }
            if (markDynamic) mesh.MarkDynamic();
            if (markNotReadable) mesh.UploadMeshData(true);

            // Assign to SkinnedMeshRenderer
            UMAUtils.DestroySceneObject(batch.Renderer.sharedMesh);
            batch.Renderer.sharedMesh = mesh;
            batch.Renderer.sharedMesh.name = "UMAMesh (MeshAPI)";
            if (umaData != null && umaData.skeleton != null)
            {
                batch.Renderer.bones = umaData.skeleton.HashesToTransforms(bonesList.ToArray());
                if (batch.Renderer.rootBone == null)
                {
                    batch.Renderer.rootBone = umaData.GetGlobalTransform();
                }
            }

            // Cloth coefficients (returned to caller)
            clothCoeffs = hasCloth ? BuildClothCoefficients(sources, vertices) : null;

            // dispose temps
            nativeBonesPerVertex.Dispose();
            nativeBoneWeights.Dispose();
        }
#endif
        #region Helpers (adapted from UMA combiner)

        [Flags]
        private enum MeshComponents
        {
            none = 0,
            has_normals = 1,
            has_tangents = 2,
            has_colors32 = 4,
            has_uv = 8,
            has_uv2 = 16,
            has_uv3 = 32,
            has_uv4 = 64,
            has_blendShapes = 128,
            has_clothSkinning = 256,
        }

        private class BlendShapeVertexData
        {
            public bool hasNormals;
            public bool hasTangents;
            public int frameCount;
            public float[] frameWeights;
            public int index;
        }

        [BurstCompile]
        private struct CopyIntArrayAddJob : IJob
        {
            [ReadOnly] public NativeArray<int> Source;
            public NativeArray<int> Dest;
            public int SourceIndex;
            public int DestIndex;
            public int Count;
            public int Add;

            public void Execute()
            {
                for (int i = 0; i < Count; i++)
                {
                    Dest[DestIndex + i] = Source[SourceIndex + i] + Add;
                }
            }
        }

        [BurstCompile]
        private struct BoneWeightsRemapJob : IJob
        {
            [ReadOnly] public NativeArray<byte> SrcBonesPerVertex;
            [ReadOnly] public NativeArray<BoneWeight1> SrcBoneWeights;
            [ReadOnly] public NativeArray<int> BoneRemap; // local -> global

            // Dest (global) buffers
            public NativeArray<byte> DestBonesPerVertex;
            public NativeArray<BoneWeight1> DestBoneWeights;

            public int DestVertexStart;
            public int DestBoneWeightStart;

            public void Execute()
            {
                // Copy bones-per-vertex
                NativeArray<byte>.Copy(SrcBonesPerVertex, 0, DestBonesPerVertex, DestVertexStart, SrcBonesPerVertex.Length);

                // Remap bone weights
                for (int i = 0; i < SrcBoneWeights.Length; i++)
                {
                    var bw = SrcBoneWeights[i];
                    bw.boneIndex = BoneRemap[bw.boneIndex];
                    DestBoneWeights[DestBoneWeightStart + i] = bw;
                }
            }
        }

        private static void AnalyzeSources(
            SkinnedMeshCombiner.CombineInstance[] sources,
            int[] subMeshTriangleLength,
            ref int vertexCount,
            ref int boneWeightCount,
            ref int bindPoseCount,
            ref int transformHierarchyCount,
            ref MeshComponents meshComponents)
        {
            Array.Fill(subMeshTriangleLength, 0);

            for (int j = 0; j < sources.Length; j++)
            {
                var src = sources[j];

                boneWeightCount += src.meshData.ManagedBoneWeights.Length;
                vertexCount += src.meshData.vertices.Length;
                bindPoseCount += src.meshData.bindPoses.Length;
                transformHierarchyCount += src.meshData.umaBones.Length;

                if (src.meshData.normals != null && src.meshData.normals.Length != 0) meshComponents |= MeshComponents.has_normals;
                if (src.meshData.tangents != null && src.meshData.tangents.Length != 0) meshComponents |= MeshComponents.has_tangents;
                if (src.meshData.uv != null && src.meshData.uv.Length != 0) meshComponents |= MeshComponents.has_uv;
                if (src.meshData.uv2 != null && src.meshData.uv2.Length != 0) meshComponents |= MeshComponents.has_uv2;
                if (src.meshData.uv3 != null && src.meshData.uv3.Length != 0) meshComponents |= MeshComponents.has_uv3;
                if (src.meshData.uv4 != null && src.meshData.uv4.Length != 0) meshComponents |= MeshComponents.has_uv4;
                if (src.meshData.colors32 != null && src.meshData.colors32.Length != 0) meshComponents |= MeshComponents.has_colors32;
                if (src.meshData.clothSkinningSerialized != null && src.meshData.clothSkinningSerialized.Length != 0) meshComponents |= MeshComponents.has_clothSkinning;

                for (int i = 0; i < src.meshData.subMeshCount; i++)
                {
                    int dest = src.targetSubmeshIndices[i];
                    if (dest < 0) continue;

                    int subLen = src.meshData.submeshes[i].GetTriangles().Length;
                    int triLen = (src.triangleMask == null) ? subLen : (subLen - (UMAUtils.GetCardinality(src.triangleMask[i]) * 3));
                    subMeshTriangleLength[dest] += triLen;
                }
            }
        }

        private static void AnalyzeBlendShapeSources(
            SkinnedMeshCombiner.CombineInstance[] sources,
            Dictionary<string, float> bakedBlendshapes,
            ref MeshComponents meshComponents,
            out Dictionary<string, BlendShapeVertexData> blendShapeNames,
            UMAData.UMARecipe recipe)
        {
            blendShapeNames = new Dictionary<string, BlendShapeVertexData>();

            int bakedCount = 0;
            for (int k = 0; k < sources.Length; k++)
            {
                var src = sources[k];
                var sourceShapes = SkinnedMeshCombiner.GetBlendshapeSources(src.meshData, recipe);
                if (sourceShapes.Count == 0) continue;

                for (int j = 0; j < sourceShapes.Count; j++)
                {
                    var ubs = sourceShapes[j];
                    string shapeName = ubs.shapeName;

                    if (bakedBlendshapes.ContainsKey(shapeName))
                    {
                        bakedCount++;
                        continue;
                    }

                    if (!blendShapeNames.ContainsKey(shapeName))
                    {
                        blendShapeNames.Add(shapeName, new BlendShapeVertexData());
                    }

                    var meta = blendShapeNames[shapeName];
                    meta.hasNormals |= ubs.frames[0].HasNormals();
                    meta.hasTangents |= ubs.frames[0].HasTangents();

                    if (ubs.frames.Length > meta.frameCount)
                    {
                        meta.frameCount = ubs.frames.Length;
                        meta.frameWeights = new float[meta.frameCount];
                        for (int i = 0; i < meta.frameCount; i++)
                        {
                            meta.frameWeights[i] = ubs.frames[i].frameWeight;
                        }
                    }
                }
            }

            if (blendShapeNames.Count > 0 || bakedCount > 0)
                meshComponents |= MeshComponents.has_blendShapes;
        }

        private static void InitializeBlendShapeData(ref int vertexCount, Dictionary<string, BlendShapeVertexData> blendShapeNames, UMABlendShape[] blendShapes)
        {
            int idx = 0;
            foreach (var kv in blendShapeNames)
            {
                string name = kv.Key;
                var meta = kv.Value;

                meta.index = idx;
                blendShapes[idx] = new UMABlendShape
                {
                    shapeName = name,
                    frames = new UMABlendFrame[meta.frameCount]
                };

                for (int f = 0; f < meta.frameCount; f++)
                {
                    blendShapes[idx].frames[f] = new UMABlendFrame(vertexCount, meta.hasNormals, meta.hasTangents);
                    blendShapes[idx].frames[f].frameWeight = meta.frameWeights[f];
                }
                idx++;
            }
        }

        private static bool MaskedCopyIntArrayAdd(NativeArray<int> source, int sourceIndex, NativeArray<int> dest, int destIndex, int count, int add, BitArray mask)
        {
#if DEBUG
            if ((mask.Count * 3) != source.Length)
            {
                if (Debug.isDebugBuild) Debug.LogError("MaskedCopyIntArrayAdd: mask count and source length do not match!");
                return false;
            }
            if ((mask.Count * 3) != count)
            {
                if (Debug.isDebugBuild) Debug.LogError("MaskedCopyIntArrayAdd: mask count and count do not match!");
                return false;
            }
#endif
            for (int i = 0; i < count; i += 3)
            {
                if (!mask[i / 3])
                {
                    dest[destIndex++] = source[sourceIndex + i + 0] + add;
                    dest[destIndex++] = source[sourceIndex + i + 1] + add;
                    dest[destIndex++] = source[sourceIndex + i + 2] + add;
                }
            }
            return true;
        }

        [BurstCompile]
        private static void ArrayCopyandExpand(UMAMeshData meshData, int expandAlongNormal, ref Vector3[] vertices, int vertexIndex, int sourceVertexCount)
        {
            float expandAlongNormalF = ((float)expandAlongNormal) / 1000000f;
            for (int i = vertexIndex; i < vertexIndex + sourceVertexCount; i++)
            {
                Vector3 v = meshData.vertices[i - vertexIndex];
                vertices[i] = v + (meshData.normals[i - vertexIndex] * expandAlongNormalF);
            }
        }

        private static void FillArray(Vector3[] array, int index, int count, Vector3 value)
        {
            if (array == null || count <= 0) return;
#if UMA_UNSAFE
            unsafe
            {
                fixed (Vector3* dst = &array[index])
                {
                    UnsafeUtility.MemCpyReplicate(dst, &value, sizeof(Vector3), count);
                }
            }
#else
            // Set first, then double-copy
            int filled = 1;
            array[index] = value;
            while (filled < count)
            {
                int toCopy = Math.Min(filled, count - filled);
                Array.Copy(array, index, array, index + filled, toCopy);
                filled += toCopy;
            }
#endif
        }

        private static void FillArray(Vector4[] array, int index, int count, Vector4 value)
        {
            if (array == null || count <= 0) return;
#if UMA_UNSAFE
            unsafe
            {
                fixed (Vector4* dst = &array[index])
                {
                    UnsafeUtility.MemCpyReplicate(dst, &value, sizeof(Vector4), count);
                }
            }
#else
            int filled = 1;
            array[index] = value;
            while (filled < count)
            {
                int toCopy = Math.Min(filled, count - filled);
                Array.Copy(array, index, array, index + filled, toCopy);
                filled += toCopy;
            }
#endif
        }

        private static void FillArray(Vector2[] array, int index, int count, Vector2 value)
        {
            if (array == null || count <= 0) return;
#if UMA_UNSAFE
            unsafe
            {
                fixed (Vector2* dst = &array[index])
                {
                    UnsafeUtility.MemCpyReplicate(dst, &value, sizeof(Vector2), count);
                }
            }
#else
            int filled = 1;
            array[index] = value;
            while (filled < count)
            {
                int toCopy = Math.Min(filled, count - filled);
                Array.Copy(array, index, array, index + filled, toCopy);
                filled += toCopy;
            }
#endif
        }

        private static void FillArray(Color32[] array, int index, int count, Color32 value)
        {
            if (array == null || count <= 0) return;
#if UMA_UNSAFE
            unsafe
            {
                fixed (Color32* dst = &array[index])
                {
                    UnsafeUtility.MemCpyReplicate(dst, &value, sizeof(Color32), count);
                }
            }
#else
            int filled = 1;
            array[index] = value;
            while (filled < count)
            {
                int toCopy = Math.Min(filled, count - filled);
                Array.Copy(array, index, array, index + filled, toCopy);
                filled += toCopy;
            }
#endif
        }

        private static bool BakeBlendShape(
            UMABlendShape currentShape,
            float value,
            ref int vertexIndex,
            Vector3[] vertices,
            Vector3[] normals,
            Vector4[] tangents,
            bool has_Normals,
            bool has_Tangents)
        {
            float weight = value * 100.0f;
            if (Mathf.Abs(weight) <= Mathf.Epsilon) return true;

            int frameIndex;
            for (frameIndex = 0; frameIndex < currentShape.frames.Length; frameIndex++)
            {
                if (currentShape.frames[frameIndex].frameWeight >= weight) break;
            }

            float frameWeight;
            float prevWeight = 0f;
            bool doLerp = false;
            int prevIndex;

            if (frameIndex >= currentShape.frames.Length)
            {
                frameIndex = currentShape.frames.Length - 1;
                frameWeight = (weight / currentShape.frames[frameIndex].frameWeight);
            }
            else if (frameIndex > 0)
            {
                doLerp = true;
                float prevFrameWeight = currentShape.frames[frameIndex - 1].frameWeight;
                frameWeight = (weight - prevFrameWeight) / (currentShape.frames[frameIndex].frameWeight - prevFrameWeight);
                prevWeight = 1f - frameWeight;
            }
            else
            {
                frameWeight = (weight / currentShape.frames[frameIndex].frameWeight);
            }
            prevIndex = (frameIndex > 0) ? (frameIndex - 1) : 0;

            var currVerts = currentShape.frames[frameIndex].deltaVertices;
            var prevVerts = currentShape.frames[prevIndex].deltaVertices;

            Vector3[] currNormals = null, prevNormals = null;
            Vector3[] currTangents = null, prevTangents = null;

            bool has_deltaNormals = (has_Normals && currentShape.frames[frameIndex].deltaNormals != null && currentShape.frames[frameIndex].deltaNormals.Length > 0);
            bool has_deltaTangents = (has_Tangents && currentShape.frames[frameIndex].deltaTangents != null && currentShape.frames[frameIndex].deltaTangents.Length > 0);

            if (has_deltaNormals)
            {
                currNormals = currentShape.frames[frameIndex].deltaNormals;
                prevNormals = currentShape.frames[prevIndex].deltaNormals;
            }
            if (has_deltaTangents)
            {
                currTangents = currentShape.frames[frameIndex].deltaTangents;
                prevTangents = currentShape.frames[prevIndex].deltaTangents;
            }

            int vi = vertexIndex;
            for (int i = 0; i < currVerts.Length; i++, vi++)
            {
                if (currVerts[i].sqrMagnitude > 0.0000001f)
                {
                    vertices[vi] += currVerts[i] * frameWeight;
                    if (doLerp) vertices[vi] += prevVerts[i] * prevWeight;
                }
                if (has_deltaNormals && normals != null)
                {
                    if (currNormals[i].sqrMagnitude > 0.0000001f)
                    {
                        normals[vi] += currNormals[i] * frameWeight;
                        if (doLerp) normals[vi] += prevNormals[i] * prevWeight;
                    }
                }
                if (has_deltaTangents && tangents != null)
                {
                    if (currTangents[i].sqrMagnitude > 0.0000001f)
                    {
                        tangents[vi] += (Vector4)currTangents[i] * frameWeight;
                        if (doLerp) tangents[vi] += (Vector4)prevTangents[i] * prevWeight;
                    }
                }
            }
            return true;
        }

        private static ClothSkinningCoefficient[] BuildClothCoefficients(SkinnedMeshCombiner.CombineInstance[] sources, Vector3[] combinedVertices)
        {
            var clothDict = new Dictionary<Vector3, int>(combinedVertices.Length);
            var result = new List<ClothSkinningCoefficient>(combinedVertices.Length);

            int vertexIndex = 0;
            for (int k = 0; k < sources.Length; k++)
            {
                var src = sources[k];
                int count = src.meshData.vertexCount;

                if (src.meshData.clothSkinningSerialized != null && src.meshData.clothSkinningSerialized.Length > 0)
                {
                    var local = new Dictionary<Vector3, int>(count);
                    for (int i = 0; i < count; i++)
                    {
                        var v = src.meshData.vertices[i];
                        if (!local.ContainsKey(v))
                        {
                            local.Add(v, local.Count);
                            if (!clothDict.TryGetValue(v, out var globalIndex))
                            {
                                var coeff = new ClothSkinningCoefficient();
                                ConvertData(ref src.meshData.clothSkinningSerialized[local[v]], ref coeff);
                                clothDict.Add(v, result.Count);
                                result.Add(coeff);
                            }
                            else
                            {
                                var coeff = result[clothDict[v]];
                                ConvertData(ref src.meshData.clothSkinningSerialized[local[v]], ref coeff);
                                result[clothDict[v]] = coeff;
                            }
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        var v = src.meshData.vertices[i];
                        if (!clothDict.ContainsKey(v))
                        {
                            clothDict.Add(v, result.Count);
                            result.Add(new ClothSkinningCoefficient { maxDistance = 0, collisionSphereDistance = float.MaxValue });
                        }
                    }
                }

                vertexIndex += count;
            }

            return (result.Count > 0) ? result.ToArray() : null;
        }

        private class BoneIndexEntry
        {
            public int index;
            public List<int> indices;
            public int Count => index >= 0 ? 1 : indices.Count;
            public int this[int idx]
            {
                get
                {
                    if (index >= 0)
                    {
                        if (idx == 0) return index;
                        throw new ArgumentOutOfRangeException();
                    }
                    return indices[idx];
                }
            }
            internal void AddIndex(int idx)
            {
                if (index >= 0)
                {
                    indices = new List<int>(10);
                    indices.Add(index);
                    index = -1;
                }
                indices.Add(idx);
            }
        }

        private static bool CompareSkinningMatrices(Matrix4x4 m1, ref Matrix4x4 m2)
        {
            if (Mathf.Abs(m1.m00 - m2.m00) > 0.0001) return false;
            if (Mathf.Abs(m1.m01 - m2.m01) > 0.0001) return false;
            if (Mathf.Abs(m1.m02 - m2.m02) > 0.0001) return false;
            if (Mathf.Abs(m1.m03 - m2.m03) > 0.0001) return false;
            if (Mathf.Abs(m1.m10 - m2.m10) > 0.0001) return false;
            if (Mathf.Abs(m1.m11 - m2.m11) > 0.0001) return false;
            if (Mathf.Abs(m1.m12 - m2.m12) > 0.0001) return false;
            if (Mathf.Abs(m1.m13 - m2.m13) > 0.0001) return false;
            if (Mathf.Abs(m1.m20 - m2.m20) > 0.0001) return false;
            if (Mathf.Abs(m1.m21 - m2.m21) > 0.0001) return false;
            if (Mathf.Abs(m1.m22 - m2.m22) > 0.0001) return false;
            if (Mathf.Abs(m1.m23 - m2.m23) > 0.0001) return false;
            return true;
        }

        private static int TranslateBoneIndex(
            int index,
            int[] bonesHashes,
            Matrix4x4[] bindPoses,
            Dictionary<int, BoneIndexEntry> bonesCollection,
            List<Matrix4x4> bindPosesList,
            List<int> bonesList)
        {
            int boneHash = bonesHashes[index];
            BoneIndexEntry entry;
            if (bonesCollection.TryGetValue(boneHash, out entry))
            {
                for (int i = 0; i < entry.Count; i++)
                {
                    var res = entry[i];
                    if (CompareSkinningMatrices(bindPosesList[res], ref bindPoses[index]))
                        return res;
                }
                var idx = bindPosesList.Count;
                entry.AddIndex(idx);
                bindPosesList.Add(bindPoses[index]);
                bonesList.Add(boneHash);
                return idx;
            }
            else
            {
                var idx = bindPosesList.Count;
                bonesCollection.Add(boneHash, new BoneIndexEntry() { index = idx });
                bindPosesList.Add(bindPoses[index]);
                bonesList.Add(boneHash);
                return idx;
            }
        }

        private static void BuildBoneWeights(
            UMAMeshData data,
            NativeArray<BoneWeight1> dest,
            NativeArray<byte> destBonesPerVertex,
            int destIndex,
            int destBoneWeightIndex,
            Dictionary<int, BoneIndexEntry> bonesCollection,
            List<Matrix4x4> bindPosesList,
            List<int> bonesList)
        {
            var bones = data.boneNameHashes;
            var bindPoses = data.bindPoses;

            int[] boneMapping = new int[bones.Length];
            for (int i = 0; i < boneMapping.Length; i++)
            {
                boneMapping[i] = TranslateBoneIndex(i, bones, bindPoses, bonesCollection, bindPosesList, bonesList);
            }

            NativeArray<byte>.Copy(data.ManagedBonesPerVertex, 0, destBonesPerVertex, destIndex, data.ManagedBonesPerVertex.Length);
            NativeArray<BoneWeight1>.Copy(data.ManagedBoneWeights, 0, dest, destBoneWeightIndex, data.ManagedBoneWeights.Length);

            var bw = new BoneWeight1();
            for (int i = 0; i < data.ManagedBoneWeights.Length; i++)
            {
                bw.boneIndex = boneMapping[data.ManagedBoneWeights[i].boneIndex];
                bw.weight = data.ManagedBoneWeights[i].weight;
                dest[i + destBoneWeightIndex] = bw;
            }
        }

        [BurstCompile]
        private static void MergeSortedTransforms(UMATransform[] mergedTransforms, ref int len1, UMATransform[] umaTransforms)
        {
            int newBones = 0, p1 = 0, p2 = 0, len2 = umaTransforms.Length;
            while (p1 < len1 && p2 < len2)
            {
                long diff = ((long)mergedTransforms[p1].hash) - ((long)umaTransforms[p2].hash);
                if (diff == 0) { p1++; p2++; }
                else if (diff < 0) { p1++; }
                else { p2++; newBones++; }
            }
            newBones += len2 - p2;
            p1 = len1 - 1; p2 = len2 - 1;
            len1 += newBones;
            int dest = len1 - 1;

            while (p1 >= 0 && p2 >= 0)
            {
                long diff = ((long)mergedTransforms[p1].hash) - ((long)umaTransforms[p2].hash);
                if (diff == 0) { mergedTransforms[dest--] = mergedTransforms[p1--]; p2--; }
                else if (diff > 0) { mergedTransforms[dest--] = mergedTransforms[p1--]; }
                else { mergedTransforms[dest--] = umaTransforms[p2--]; }
            }
            while (p2 >= 0) mergedTransforms[dest--] = umaTransforms[p2--];
        }

        private static VertexAttributeDescriptor[] BuildVertexLayout(
            bool hasNormals, bool hasTangents, bool hasUV, bool hasUV2, bool hasUV3, bool hasUV4, bool hasColors32)
        {
            var list = new List<VertexAttributeDescriptor>(8)
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0)
            };
            int stream = 1;
            if (hasNormals) list.Add(new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream++));
            if (hasTangents) list.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, stream++));
            if (hasColors32) list.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, stream++));
            if (hasUV) list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, stream++));
            if (hasUV2) list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, stream++));
            if (hasUV3) list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2, stream++));
            if (hasUV4) list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2, stream++));
            return list.ToArray();
        }

        private static void RecalculateUVForUMA(Vector2[] uv, UMAData umaData, int atlasResolution, int currentRendererIndex)
        {
            int idx = 0;
            for (int materialIndex = 0; materialIndex < umaData.generatedMaterials.materials.Count; materialIndex++)
            {
                var generatedMaterial = umaData.generatedMaterials.materials[materialIndex];
                if (generatedMaterial.rendererAsset != umaData.GetRendererAsset(currentRendererIndex))
                {
                    for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                    {
                        var fragment = generatedMaterial.materialFragments[i];
                        idx += fragment.slotData.asset.meshData.vertices.Length;
                    }
                    continue;
                }

                if (!generatedMaterial.umaMaterial.IsGeneratedTextures)
                {
                    for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
                    {
                        var fragment = generatedMaterial.materialFragments[i];
                        idx += fragment.slotData.asset.meshData.vertices.Length;
                    }
                    continue;
                }

                for (int m = 0; m < generatedMaterial.materialFragments.Count; m++)
                {
                    var fragment = generatedMaterial.materialFragments[m];
                    var tempAtlasRect = fragment.atlasRegion;
                    int vertexCount = fragment.slotData.asset.meshData.vertices.Length;

                    float atlasXMin = tempAtlasRect.xMin / atlasResolution;
                    float atlasXMax = tempAtlasRect.xMax / atlasResolution;
                    float atlasXRange = atlasXMax - atlasXMin;
                    float atlasYMin = tempAtlasRect.yMin / atlasResolution;
                    float atlasYMax = tempAtlasRect.yMax / atlasResolution;
                    float atlasYRange = atlasYMax - atlasYMin;

                    if (fragment.isRectShared && fragment.slotData.useAtlasOverlay)
                    {
                        OverlayData foundRect = null;
                        for (int i = 0; i < fragment.overlayList.Count; i++)
                        {
                            OverlayData ov = fragment.overlayList[i];
                            if (fragment.slotData.slotName != null && ov.overlayName != null && ov.overlayName.Contains(fragment.slotData.slotName))
                            {
                                foundRect = ov;
                                break;
                            }
                        }
                        if (foundRect != null && foundRect.rect != Rect.zero)
                        {
                            var size = foundRect.rect.size * generatedMaterial.resolutionScale;
                            var offsetX = foundRect.rect.x * generatedMaterial.resolutionScale.x;
                            var offsetY = foundRect.rect.y * generatedMaterial.resolutionScale.x;

                            atlasXMin += (offsetX / generatedMaterial.cropResolution.x);
                            atlasXRange = size.x / generatedMaterial.cropResolution.x;

                            atlasYMin += (offsetY / generatedMaterial.cropResolution.y);
                            atlasYRange = size.y / generatedMaterial.cropResolution.y;
                        }
                    }

                    while (vertexCount-- > 0)
                    {
                        uv[idx].x = atlasXMin + atlasXRange * uv[idx].x;
                        uv[idx].y = atlasYMin + atlasYRange * uv[idx].y;
                        idx++;
                    }
                }
            }
        }

        public static void ConvertData(ref Vector2 source, ref ClothSkinningCoefficient dest)
        {
            dest.collisionSphereDistance = source.x;
            dest.maxDistance = source.y;
        }

        private static int FindTargetSubMeshCount(SkinnedMeshCombiner.CombineInstance[] sources)
        {
            int highestTargetIndex = -1;
            for (int i = 0; i < sources.Length; i++)
            {
                var s = sources[i];
                for (int j = 0; j < s.targetSubmeshIndices.Length; j++)
                {
                    int t = s.targetSubmeshIndices[j];
                    if (highestTargetIndex < t) highestTargetIndex = t;
                }
            }
            return highestTargetIndex + 1;
        }

        #endregion
    }
}