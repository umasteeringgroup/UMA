#if UNITY_2021_3_OR_NEWER
#define UMA_MESHAPI_2021
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
using Unity.Collections.LowLevel.Unsafe;
#endif

namespace UMA
{
    /// <summary>
    /// Unity 2021.3+ MeshData API based combiner.
    /// - Writes directly to MeshData buffers to avoid large intermediates.
    /// - Uses 16-bit index buffers when possible.
    /// - Burst/Jobs for index copy (masked and unmasked).
    /// - Preserves cloth and blendshapes (unbaked added directly to Mesh).
    /// - UMA atlas UV remap operates on MeshData UV buffer.
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

        // Safe interleaved structs (multi-stream layout)
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NormTan { public Vector3 normal; public Vector4 tangent; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct ColUV01 { public Color32 color; public Vector2 uv0; public Vector2 uv1; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct UV23 { public Vector2 uv2; public Vector2 uv3; }

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
#if !UMA_MESHAPI_2021
            throw new NotSupportedException("Requires Unity 2021.3+ MeshData API.");
#else
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (sources == null || sources.Length == 0) throw new ArgumentException("sources empty", nameof(sources));
            if (umaData == null) throw new ArgumentNullException(nameof(umaData));

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

        public static ClothSkinningCoefficient[][] CombineIntoRenderers(
            RendererBatch[] batches,
            UMAData umaData,
            Dictionary<string, float> bakedBlendshapes,
            bool markDynamic = false,
            bool markNotReadable = false)
        {
#if !UMA_MESHAPI_2021
            throw new NotSupportedException("Requires Unity 2021.3+ MeshData API.");
#else
            if (batches == null || batches.Length == 0) throw new ArgumentException("batches empty", nameof(batches));
            if (umaData == null) throw new ArgumentNullException(nameof(umaData));

            var results = new ClothSkinningCoefficient[batches.Length][];
            for (int i = 0; i < batches.Length; i++)
            {
                if (batches[i].Renderer == null) throw new ArgumentNullException($"Renderer at {i}");
                if (batches[i].Sources == null || batches[i].Sources.Length == 0) throw new ArgumentException($"sources empty at {i}");

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

#if UMA_MESHAPI_2021
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

            // Blendshape analysis (unbaked only)
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

            // Prefix sums for submeshes and total index count
            int totalIndexCount = 0;
            var subIndexStart = new int[subMeshCount];
            for (int i = 0, run = 0; i < subMeshCount; i++)
            {
                subIndexStart[i] = run;
                run += subMeshTriangleLength[i];
                totalIndexCount = run;
            }

            // Allocate MeshData
            var mda = Mesh.AllocateWritableMeshData(1);
            var md = mda[0];

            // Vertex layout: multi-stream to keep copies minimal and contiguous
            var vDescs = BuildVertexLayout(hasNormals, hasTangents, hasUV, hasUV2, hasUV3, hasUV4, hasColors32);
            md.SetVertexBufferParams(vertexCount, vDescs);

            // Index buffer (16-bit if possible)
            var indexFormat = (vertexCount <= 65535) ? IndexFormat.UInt16 : IndexFormat.UInt32;
            md.SetIndexBufferParams(totalIndexCount, indexFormat);

            // Submeshes (ranges assigned after indices are written)
            md.subMeshCount = subMeshCount;

            // Grab vertex streams
            var vPos = md.GetVertexData<Vector3>(0);

            NativeArray<NormTan> vNT = default;
            NativeArray<ColUV01> vC01 = default;
            NativeArray<UV23> vUV23 = default;

            int stream = 1;
            if (hasNormals || hasTangents) vNT = md.GetVertexData<NormTan>(stream++);
            if (hasColors32 || hasUV || hasUV2) vC01 = md.GetVertexData<ColUV01>(stream++);
            if (hasUV3 || hasUV4) vUV23 = md.GetVertexData<UV23>(stream++);

            // Index buffer
            NativeArray<int> ibInt = default;
            NativeArray<ushort> ibU16 = default;
            if (indexFormat == IndexFormat.UInt16) ibU16 = md.GetIndexData<ushort>();
            else ibInt = md.GetIndexData<int>();

            // UMA transforms merged
            int boneCount = 0;
            var mergedUmaTransforms = new UMATransform[transformHierarchyCount];
            for (int i = 0; i < sources.Length; i++)
                MergeSortedTransforms(mergedUmaTransforms, ref boneCount, sources[i].meshData.umaBones);

            if (umaData != null && umaData.skeleton != null)
            {
                umaData.skeleton.BeginSkeletonUpdate();
                for (int i = 0; i < boneCount; i++) umaData.skeleton.EnsureBone(mergedUmaTransforms[i]);
                umaData.skeleton.EnsureBoneHierarchy();
                umaData.skeleton.EndSkeletonUpdate();
            }

            // Bones and weights collection
            var bonesCollection = new Dictionary<int, BoneIndexEntry>(Math.Max(64, bindPoseCount));
            var bindPoses = new List<Matrix4x4>(bindPoseCount);
            var bonesList = new List<int>(transformHierarchyCount);

            var nativeBoneWeights = new NativeArray<BoneWeight1>(boneWeightCount, Allocator.Temp);
            var nativeBonesPerVertex = new NativeArray<byte>(Math.Max(1, vertexCount), Allocator.Temp);

            // Track offsets and bounds
            int vertexOffset = 0;
            int boneWeightOffset = 0;
            var boundsMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var boundsMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            // Index write cursors per submesh
            var subWrite = new int[subMeshCount];

            // Collect vertex offsets per source (for blendshape pass later)
            var sourceVertexOffsets = new int[sources.Length];

            // Copy vertex attributes and indices directly into MeshData buffers
            for (int s = 0; s < sources.Length; s++)
            {
                var ci = sources[s];
                var src = ci.meshData;
                int srcCount = src.vertexCount;
                sourceVertexOffsets[s] = vertexOffset;

                // Bone weights: remap and copy into global buffers
                BuildBoneWeights(src, nativeBoneWeights, nativeBonesPerVertex, vertexOffset, boneWeightOffset, bonesCollection, bindPoses, bonesList);

                // Positions (with optional expandAlongNormal) + bounds
#if UMA_UNSAFE
                {
                    float expand = 0f;
                    if (ci.slotData != null && ci.slotData.expandAlongNormal > 0)
                    {
                        expand = ci.slotData.expandAlongNormal / 1000000f;
                    }

                    FastCopyPositionsAndBoundsUnsafe(
                        vPos, vertexOffset,
                        src.vertices, src.normals,
                        srcCount, expand,
                        ref boundsMin, ref boundsMax);
                }
#else
                if (ci.slotData != null && ci.slotData.expandAlongNormal > 0 && src.normals != null && src.normals.Length == srcCount)
                {
                    float expand = ci.slotData.expandAlongNormal / 1000000f;
                    for (int i = 0; i < srcCount; i++)
                    {
                        vPos[vertexOffset + i] = src.vertices[i] + (src.normals[i] * expand);
                    }
                }
                else
                {
                    NativeArray<Vector3>.Copy(src.vertices, 0, vPos, vertexOffset, srcCount);
                }
                // update bounds
                for (int i = 0; i < srcCount; i++)
                {
                    var v = vPos[vertexOffset + i];
                    if (v.x < boundsMin.x) boundsMin.x = v.x; if (v.x > boundsMax.x) boundsMax.x = v.x;
                    if (v.y < boundsMin.y) boundsMin.y = v.y; if (v.y > boundsMax.y) boundsMax.y = v.y;
                    if (v.z < boundsMin.z) boundsMin.z = v.z; if (v.z > boundsMax.z) boundsMax.z = v.z;
                }
#endif
                // Normals/Tangents
                if (hasNormals || hasTangents)
                {
#if UMA_UNSAFE
                    PackNormTanUnsafe(vNT, vertexOffset, src.normals, src.tangents, srcCount, hasNormals, hasTangents);
#else
                    for (int i = 0; i < srcCount; i++)
                    {
                        var nt = default(NormTan);
                        nt.normal = (hasNormals && src.normals != null && src.normals.Length == srcCount) ? src.normals[i] : Vector3.zero;
                        nt.tangent = (hasTangents && src.tangents != null && src.tangents.Length == srcCount) ? src.tangents[i] : new Vector4(0, 0, 0, 1);
                        vNT[vertexOffset + i] = nt;
                    }
#endif
                }

                // Colors, UV0, UV1
                if (hasColors32 || hasUV || hasUV2)
                {
#if UMA_UNSAFE
                    PackColUV01Unsafe(vC01, vertexOffset, src.colors32, src.uv, src.uv2, srcCount, hasColors32, hasUV, hasUV2);
#else
                    for (int i = 0; i < srcCount; i++)
                    {
                        var c01 = default(ColUV01);
                        c01.color = (hasColors32 && src.colors32 != null && src.colors32.Length == srcCount) ? src.colors32[i] : (Color32)Color.white;
                        c01.uv0 = (hasUV && src.uv != null && src.uv.Length >= srcCount) ? src.uv[i] : Vector2.zero;
                        c01.uv1 = (hasUV2 && src.uv2 != null && src.uv2.Length >= srcCount) ? src.uv2[i] : Vector2.zero;
                        vC01[vertexOffset + i] = c01;
                    }
#endif
                }
                // UV2, UV3 (TexCoord2, TexCoord3)
                if (hasUV3 || hasUV4)
                {
#if UMA_UNSAFE
                    // note: uv3 -> TexCoord2, uv4 -> TexCoord3
                    PackUV23Unsafe(vUV23, vertexOffset, src.uv3, src.uv4, srcCount, hasUV3, hasUV4);
#else
                    for (int i = 0; i < srcCount; i++)
                    {
                        var uv23 = default(UV23);
                        uv23.uv2 = (hasUV3 && src.uv3 != null && src.uv3.Length >= srcCount) ? src.uv3[i] : Vector2.zero;
                        uv23.uv3 = (hasUV4 && src.uv4 != null && src.uv4.Length >= srcCount) ? src.uv4[i] : Vector2.zero;
                        vUV23[vertexOffset + i] = uv23;
                    }
#endif
                }

                // Triangles -> index buffer (masked/unmasked), with offset
                for (int sm = 0; sm < src.subMeshCount; sm++)
                {
                    int dstSub = ci.targetSubmeshIndices[sm];
                    if (dstSub < 0) continue;

                    var srcTris = src.submeshes[sm].GetTriangles();
                    int triLen = srcTris.Length;
                    int dstStart = subIndexStart[dstSub] + subWrite[dstSub];

                    if (ci.triangleMask != null && sm < ci.triangleMask.Length)
                    {
                        int kept = triLen - (UMAUtils.GetCardinality(ci.triangleMask[sm]) * 3);
                        if (kept > 0)
                        {
                            if (indexFormat == IndexFormat.UInt16)
                            {
                                var job = new MaskedCopyIndicesJobU16
                                {
                                    Src = srcTris,
                                    Mask = BitArrayToNative(ci.triangleMask[sm], Allocator.TempJob),
                                    Dst = ibU16,
                                    DstStart = dstStart,
                                    Add = (ushort)vertexOffset
                                }.Schedule();
                                job.Complete();
                            }
                            else
                            {
                                var job = new MaskedCopyIndicesJobInt
                                {
                                    Src = srcTris,
                                    Mask = BitArrayToNative(ci.triangleMask[sm], Allocator.TempJob),
                                    Dst = ibInt,
                                    DstStart = dstStart,
                                    Add = vertexOffset
                                }.Schedule();
                                job.Complete();
                            }
                        }
                        subWrite[dstSub] += kept;
                    }
                    else
                    {
                        if (indexFormat == IndexFormat.UInt16)
                        {
                            var job = new CopyIndicesJobU16
                            {
                                Src = srcTris,
                                Dst = ibU16,
                                DstStart = dstStart,
                                Add = (ushort)vertexOffset
                            }.Schedule();
                            job.Complete();
                        }
                        else
                        {
                            var job = new CopyIndicesJobInt
                            {
                                Src = srcTris,
                                Dst = ibInt,
                                DstStart = dstStart,
                                Add = vertexOffset
                            }.Schedule();
                            job.Complete();
                        }
                        subWrite[dstSub] += triLen;
                    }
                }

                ci.slotData.vertexOffset = vertexOffset;
                ci.slotData.skinnedMeshRenderer = batch.CurrentRendererIndex;

                vertexOffset += srcCount;
                boneWeightOffset += src.ManagedBoneWeights.Length;
            }

            // UMA atlas UV remap — in place on MeshData UV buffer
            if (hasUV)
            {
                // we remap vC01.uv0
                RecalculateUVForUMA(vC01, umaData, batch.AtlasResolution, batch.CurrentRendererIndex);
            }

            // Submesh descriptors
            for (int i = 0; i < subMeshCount; i++)
            {
                var smd = new SubMeshDescriptor
                {
                    topology = MeshTopology.Triangles,
                    indexStart = subIndexStart[i],
                    indexCount = subMeshTriangleLength[i],
                    baseVertex = 0,
                    vertexCount = vertexCount
                };
                md.SetSubMesh(i, smd, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            }

            // Create Mesh and apply
            var mesh = new Mesh { indexFormat = indexFormat };
            Mesh.ApplyAndDisposeWritableMeshData(mda, new[] { mesh }, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            // Bindposes and weights
            mesh.bindposes = bindPoses.ToArray();
            mesh.SetBoneWeights(nativeBonesPerVertex, nativeBoneWeights);

            // Build and add unbaked blendshapes on-the-fly to Mesh
            if (hasBlendShapes && blendShapeNames != null && blendShapeNames.Count > 0)
            {
                AddBlendShapesDirect(mesh, sources, bakedBlendshapes, blendShapeNames, umaData.umaRecipe, sourceVertexOffsets, vertexCount);
            }

            // Bounds from streaming copy
            if (vertexCount > 0)
            {
                var size = boundsMax - boundsMin;
                var center = boundsMin + (size * 0.5f);
                mesh.bounds = new Bounds(center, size);
            }
            if (markDynamic) mesh.MarkDynamic();
            if (markNotReadable) mesh.UploadMeshData(true);

            // Assign to renderer
            UMAUtils.DestroySceneObject(batch.Renderer.sharedMesh);
            batch.Renderer.sharedMesh = mesh;
            batch.Renderer.sharedMesh.name = "UMAMesh (MeshAPI)";

            if (umaData != null && umaData.skeleton != null)
            {
                batch.Renderer.bones = umaData.skeleton.HashesToTransforms(bonesList.ToArray());
                if (batch.Renderer.rootBone == null)
                    batch.Renderer.rootBone = umaData.GetGlobalTransform();
            }

            clothCoeffs = hasCloth ? BuildClothCoefficients(sources) : null;

            nativeBonesPerVertex.Dispose();
            nativeBoneWeights.Dispose();
        }
#endif

        #region Jobs and helpers

#if UMA_UNSAFE
        private static unsafe void FastCopyPositionsAndBoundsUnsafe(
            NativeArray<Vector3> dst, int dstStart,
            Vector3[] srcVertices, Vector3[] srcNormals,
            int count, float expandAlongNormal,
            ref Vector3 boundsMin, ref Vector3 boundsMax)
        {
            var dstPtr = (Vector3*)((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst) + dstStart * UnsafeUtility.SizeOf<Vector3>());

            if (expandAlongNormal > 0f && srcNormals != null && srcNormals.Length >= count)
            {
                fixed (Vector3* sV = srcVertices)
                fixed (Vector3* sN = srcNormals)
                {
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 v = sV[i] + sN[i] * expandAlongNormal;
                        dstPtr[i] = v;

                        if (v.x < boundsMin.x) boundsMin.x = v.x; if (v.x > boundsMax.x) boundsMax.x = v.x;
                        if (v.y < boundsMin.y) boundsMin.y = v.y; if (v.y > boundsMax.y) boundsMax.y = v.y;
                        if (v.z < boundsMin.z) boundsMin.z = v.z; if (v.z > boundsMax.z) boundsMax.z = v.z;
                    }
                }
            }
            else
            {
                fixed (Vector3* sV = srcVertices)
                {
                    long bytes = (long)count * UnsafeUtility.SizeOf<Vector3>();
                    UnsafeUtility.MemCpy(dstPtr, sV, bytes);

                    for (int i = 0; i < count; i++)
                    {
                        Vector3 v = sV[i];
                        if (v.x < boundsMin.x) boundsMin.x = v.x; if (v.x > boundsMax.x) boundsMax.x = v.x;
                        if (v.y < boundsMin.y) boundsMin.y = v.y; if (v.y > boundsMax.y) boundsMax.y = v.y;
                        if (v.z < boundsMin.z) boundsMin.z = v.z; if (v.z > boundsMax.z) boundsMax.z = v.z;
                    }
                }
            }
        }

        private static unsafe void PackNormTanUnsafe(
            NativeArray<SkinnedMeshCombinerMeshAPI.NormTan> dst, int dstStart,
            Vector3[] normals, Vector4[] tangents, int count,
            bool hasNormals, bool hasTangents)
        {
            var dstPtr = (NormTan*)((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst) + dstStart * UnsafeUtility.SizeOf<NormTan>());
            bool nValid = hasNormals && normals != null && normals.Length >= count;
            bool tValid = hasTangents && tangents != null && tangents.Length >= count;

            Vector3 zeroN = default;
            Vector4 defT = new Vector4(0, 0, 0, 1);

            if (nValid && tValid)
            {
                fixed (Vector3* nP = normals)
                fixed (Vector4* tP = tangents)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].normal = nP[i];
                        dstPtr[i].tangent = tP[i];
                    }
                }
            }
            else if (nValid)
            {
                fixed (Vector3* nP = normals)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].normal = nP[i];
                        dstPtr[i].tangent = defT;
                    }
                }
            }
            else if (tValid)
            {
                fixed (Vector4* tP = tangents)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].normal = zeroN;
                        dstPtr[i].tangent = tP[i];
                    }
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    dstPtr[i].normal = zeroN;
                    dstPtr[i].tangent = defT;
                }
            }
        }

        private static unsafe void PackColUV01Unsafe(
            NativeArray<SkinnedMeshCombinerMeshAPI.ColUV01> dst, int dstStart,
            Color32[] colors, Vector2[] uv0, Vector2[] uv1, int count,
            bool hasColors32, bool hasUV0, bool hasUV1)
        {
            var dstPtr = (ColUV01*)((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst) + dstStart * UnsafeUtility.SizeOf<ColUV01>());
            bool cValid = hasColors32 && colors != null && colors.Length >= count;
            bool u0Valid = hasUV0 && uv0 != null && uv0.Length >= count;
            bool u1Valid = hasUV1 && uv1 != null && uv1.Length >= count;

            Color32 white = new Color32(255, 255, 255, 255);

            if (cValid && u0Valid && u1Valid)
            {
                fixed (Color32* cP = colors)
                fixed (Vector2* u0P = uv0)
                fixed (Vector2* u1P = uv1)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].color = cP[i];
                        dstPtr[i].uv0 = u0P[i];
                        dstPtr[i].uv1 = u1P[i];
                    }
                }
            }
            else if (cValid && u0Valid)
            {
                fixed (Color32* cP = colors)
                fixed (Vector2* u0P = uv0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].color = cP[i];
                        dstPtr[i].uv0 = u0P[i];
                        dstPtr[i].uv1 = default;
                    }
                }
            }
            else if (cValid && u1Valid)
            {
                fixed (Color32* cP = colors)
                fixed (Vector2* u1P = uv1)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].color = cP[i];
                        dstPtr[i].uv0 = default;
                        dstPtr[i].uv1 = u1P[i];
                    }
                }
            }
            else if (u0Valid && u1Valid)
            {
                fixed (Vector2* u0P = uv0)
                fixed (Vector2* u1P = uv1)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].color = white;
                        dstPtr[i].uv0 = u0P[i];
                        dstPtr[i].uv1 = u1P[i];
                    }
                }
            }
            else if (cValid)
            {
                fixed (Color32* cP = colors)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].color = cP[i];
                        dstPtr[i].uv0 = default;
                        dstPtr[i].uv1 = default;
                    }
                }
            }
            else if (u0Valid)
            {
                fixed (Vector2* u0P = uv0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].color = white;
                        dstPtr[i].uv0 = u0P[i];
                        dstPtr[i].uv1 = default;
                    }
                }
            }
            else if (u1Valid)
            {
                fixed (Vector2* u1P = uv1)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].color = white;
                        dstPtr[i].uv0 = default;
                        dstPtr[i].uv1 = u1P[i];
                    }
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    dstPtr[i].color = white;
                    dstPtr[i].uv0 = default;
                    dstPtr[i].uv1 = default;
                }
            }
        }

        private static unsafe void PackUV23Unsafe(
            NativeArray<SkinnedMeshCombinerMeshAPI.UV23> dst, int dstStart,
            Vector2[] uv2, Vector2[] uv3, int count,
            bool hasUV2, bool hasUV3)
        {
            var dstPtr = (UV23*)((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(dst) + dstStart * UnsafeUtility.SizeOf<UV23>());
            bool u2Valid = hasUV2 && uv2 != null && uv2.Length >= count;
            bool u3Valid = hasUV3 && uv3 != null && uv3.Length >= count;

            if (u2Valid && u3Valid)
            {
                fixed (Vector2* u2P = uv2)
                fixed (Vector2* u3P = uv3)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].uv2 = u2P[i];
                        dstPtr[i].uv3 = u3P[i];
                    }
                }
            }
            else if (u2Valid)
            {
                fixed (Vector2* u2P = uv2)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].uv2 = u2P[i];
                        dstPtr[i].uv3 = default;
                    }
                }
            }
            else if (u3Valid)
            {
                fixed (Vector2* u3P = uv3)
                {
                    for (int i = 0; i < count; i++)
                    {
                        dstPtr[i].uv2 = default;
                        dstPtr[i].uv3 = u3P[i];
                    }
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    dstPtr[i].uv2 = default;
                    dstPtr[i].uv3 = default;
                }
            }
        }
#endif

        [BurstCompile]
        private struct CopyIndicesJobInt : IJob
        {
            [ReadOnly] public NativeArray<int> Src;
            [NativeDisableParallelForRestriction] public NativeArray<int> Dst;
            public int DstStart;
            public int Add;

            public void Execute()
            {
                for (int i = 0; i < Src.Length; i++)
                    Dst[DstStart + i] = Src[i] + Add;
            }
        }

        [BurstCompile]
        private struct CopyIndicesJobU16 : IJob
        {
            [ReadOnly] public NativeArray<int> Src;
            [NativeDisableParallelForRestriction] public NativeArray<ushort> Dst;
            public int DstStart;
            public ushort Add;

            public void Execute()
            {
                for (int i = 0; i < Src.Length; i++)
                    Dst[DstStart + i] = (ushort)(Src[i] + Add);
            }
        }

        [BurstCompile]
        private struct MaskedCopyIndicesJobInt : IJob
        {
            [ReadOnly] public NativeArray<int> Src;
            [ReadOnly] public NativeArray<byte> Mask;
            [NativeDisableParallelForRestriction] public NativeArray<int> Dst;
            public int DstStart;
            public int Add;

            public void Execute()
            {
                int dst = DstStart;
                int triCount = Mask.Length;
                for (int t = 0; t < triCount; t++)
                {
                    if (Mask[t] != 0) continue;
                    int i3 = t * 3;
                    Dst[dst++] = Src[i3 + 0] + Add;
                    Dst[dst++] = Src[i3 + 1] + Add;
                    Dst[dst++] = Src[i3 + 2] + Add;
                }
            }
        }

        [BurstCompile]
        private struct MaskedCopyIndicesJobU16 : IJob
        {
            [ReadOnly] public NativeArray<int> Src;
            [ReadOnly] public NativeArray<byte> Mask;
            [NativeDisableParallelForRestriction] public NativeArray<ushort> Dst;
            public int DstStart;
            public ushort Add;

            public void Execute()
            {
                int dst = DstStart;
                int triCount = Mask.Length;
                for (int t = 0; t < triCount; t++)
                {
                    if (Mask[t] != 0) continue;
                    int i3 = t * 3;
                    Dst[dst++] = (ushort)(Src[i3 + 0] + Add);
                    Dst[dst++] = (ushort)(Src[i3 + 1] + Add);
                    Dst[dst++] = (ushort)(Src[i3 + 2] + Add);
                }
            }
        }

        private static NativeArray<byte> BitArrayToNative(BitArray ba, Allocator allocator)
        {
            var arr = new NativeArray<byte>(ba.Count, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < ba.Count; i++)
                arr[i] = ba[i] ? (byte)1 : (byte)0;
            return arr;
        }

        private static void AddBlendShapesDirect(
            Mesh mesh,
            SkinnedMeshCombiner.CombineInstance[] sources,
            Dictionary<string, float> baked,
            Dictionary<string, BlendShapeVertexData> meta,
            UMAData.UMARecipe recipe,
            int[] sourceVertexOffsets,
            int vertexCount)
        {
            // Aggregate per shape per frame into temp arrays, then add to Mesh, discard immediately.
            foreach (var kv in meta)
            {
                string shapeName = kv.Key;
                var info = kv.Value;

                for (int f = 0; f < info.frameCount; f++)
                {
                    var dv = new Vector3[vertexCount];
                    Vector3[] dn = info.hasNormals ? new Vector3[vertexCount] : Array.Empty<Vector3>();
                    Vector3[] dt = info.hasTangents ? new Vector3[vertexCount] : Array.Empty<Vector3>();

                    for (int s = 0; s < sources.Length; s++)
                    {
                        var srcShapes = SkinnedMeshCombiner.GetBlendshapeSources(sources[s].meshData, recipe);
                        if (srcShapes == null || srcShapes.Count == 0) continue;

                        for (int i = 0; i < srcShapes.Count; i++)
                        {
                            var ubs = srcShapes[i];
                            if (ubs.shapeName != shapeName) continue;

                            int vo = sourceVertexOffsets[s];
                            int vc = sources[s].meshData.vertexCount;

                            int frameIdx = Mathf.Clamp(f, 0, ubs.frames.Length - 1);
                            var fr = ubs.frames[frameIdx];

                            Array.Copy(fr.deltaVertices, 0, dv, vo, vc);

                            if (info.hasNormals && fr.deltaNormals != null && fr.deltaNormals.Length == vc)
                                Array.Copy(fr.deltaNormals, 0, dn, vo, vc);

                            if (info.hasTangents && fr.deltaTangents != null && fr.deltaTangents.Length == vc)
                                Array.Copy(fr.deltaTangents, 0, dt, vo, vc);
                        }
                    }

                    float w = (info.frameWeights != null && f < info.frameWeights.Length) ? info.frameWeights[f] : 100f;
                    mesh.AddBlendShapeFrame(shapeName, w, dv, dn, dt);
                }
            }
        }

        #endregion

        #region UMA helpers and retained logic

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
            public bool hasNormals = false;
            public bool hasTangents = false;
            public int frameCount = 0;
            public float[] frameWeights;
            public int index;
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
                        blendShapeNames.Add(shapeName, new BlendShapeVertexData());

                    var meta = blendShapeNames[shapeName];
                    meta.hasNormals |= ubs.frames[0].HasNormals();
                    meta.hasTangents |= ubs.frames[0].HasTangents();

                    if (ubs.frames.Length > meta.frameCount)
                    {
                        meta.frameCount = ubs.frames.Length;
                        meta.frameWeights = new float[meta.frameCount];
                        for (int i = 0; i < meta.frameCount; i++)
                            meta.frameWeights[i] = ubs.frames[i].frameWeight;
                    }
                }
            }

            if (blendShapeNames.Count > 0 || bakedCount > 0)
                meshComponents |= MeshComponents.has_blendShapes;
        }

        private static ClothSkinningCoefficient[] BuildClothCoefficients(SkinnedMeshCombiner.CombineInstance[] sources)
        {
            var clothDict = new Dictionary<Vector3, int>(1024);
            var result = new List<ClothSkinningCoefficient>(1024);

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
            if (bonesCollection.TryGetValue(boneHash, out var entry))
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
                boneMapping[i] = TranslateBoneIndex(i, bones, bindPoses, bonesCollection, bindPosesList, bonesList);

            NativeArray<byte>.Copy(data.ManagedBonesPerVertex, 0, destBonesPerVertex, destIndex, data.ManagedBonesPerVertex.Length);
            NativeArray<BoneWeight1>.Copy(data.ManagedBoneWeights, 0, dest, destBoneWeightIndex, data.ManagedBoneWeights.Length);

            for (int i = 0; i < data.ManagedBoneWeights.Length; i++)
            {
                var bw = dest[destBoneWeightIndex + i];
                bw.boneIndex = boneMapping[bw.boneIndex];
                dest[destBoneWeightIndex + i] = bw;
            }
        }

        private static VertexAttributeDescriptor[] BuildVertexLayout(
            bool hasNormals, bool hasTangents, bool hasUV, bool hasUV2, bool hasUV3, bool hasUV4, bool hasColors32)
        {
            var list = new List<VertexAttributeDescriptor>(8)
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0)
            };
            int stream = 1;
            if (hasNormals || hasTangents) { list.Add(new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, stream)); stream++; }
            if (hasColors32 || hasUV || hasUV2) { list.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, stream)); stream++; }
            if (hasUV3 || hasUV4) { list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 2, stream)); list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2, stream)); }
            return list.ToArray();
        }

        private static void RecalculateUVForUMA(NativeArray<ColUV01> vC01, UMAData umaData, int atlasResolution, int currentRendererIndex)
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
                            var ov = fragment.overlayList[i];
                            if (fragment.slotData.slotName != null && ov.overlayName != null && ov.overlayName.Contains(fragment.slotData.slotName))
                            {
                                foundRect = ov; break;
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

                    for (int i = 0; i < vertexCount; i++)
                    {
                        var c01 = vC01[idx];
                        c01.uv0.x = atlasXMin + atlasXRange * c01.uv0.x;
                        c01.uv0.y = atlasYMin + atlasYRange * c01.uv0.y;
                        vC01[idx] = c01;
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

        private static void MergeSortedTransforms(UMATransform[] mergedTransforms, ref int len1, UMATransform[] umaTransforms)
{
    int newBones = 0;
    int pos1 = 0;
    int pos2 = 0;
    int len2 = umaTransforms.Length;

    while (pos1 < len1 && pos2 < len2)
    {
        long i = ((long)mergedTransforms[pos1].hash) - ((long)umaTransforms[pos2].hash);
        if (i == 0)
        {
            pos1++;
            pos2++;
        }
        else if (i < 0)
        {
            pos1++;
        }
        else
        {
            pos2++;
            newBones++;
        }
    }
    newBones += len2 - pos2;
    pos1 = len1 - 1;
    pos2 = len2 - 1;

    len1 += newBones;

    int dest = len1 - 1;

    while (pos1 >= 0 && pos2 >= 0)
    {
        long i = ((long)mergedTransforms[pos1].hash) - ((long)umaTransforms[pos2].hash);
        if (i == 0)
        {
            mergedTransforms[dest] = mergedTransforms[pos1];
            pos1--;
            pos2--;
        }
        else if (i > 0)
        {
            mergedTransforms[dest] = mergedTransforms[pos1];
            pos1--;
        }
        else
        {
            mergedTransforms[dest] = umaTransforms[pos2];
            pos2--;
        }
        dest--;
    }
    while (pos2 >= 0)
    {
        mergedTransforms[dest] = umaTransforms[pos2];
        pos2--;
        dest--;
    }
}

        #endregion
    }
}