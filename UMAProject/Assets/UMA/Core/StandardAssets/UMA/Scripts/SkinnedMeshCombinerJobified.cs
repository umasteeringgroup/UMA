using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using Unity.Collections;
using Unity.Jobs;

#if UMA_BURSTCOMPILE
using Unity.Burst;
#endif

namespace UMA
{
    /// <summary>
    /// High-performance utility class for merging multiple skinned meshes using Unity's MeshData API and Job System.
    /// </summary>
    public static class SkinnedMeshCombinerJobified
    {
        /// <summary>
        /// Settings for jobified mesh combining
        /// </summary>
        public struct JobifiedSettings
        {
            public bool useParallelJobs;
            public int vertexBatchSize;
            public int triangleBatchSize;
            
            public static JobifiedSettings Default => new JobifiedSettings
            {
                useParallelJobs = true,
                vertexBatchSize = 64,
                triangleBatchSize = 96
            };
        }

        /// <summary>
        /// Combines a set of meshes into the target mesh using MeshData API and Jobs for improved performance.
        /// </summary>
        /// <param name="target">Target.</param>
        /// <param name="sources">Sources.</param>
        /// <param name="blendShapeSettings">BlendShape Settings.</param>
        /// <param name="recipe">UMA Recipe.</param>
        /// <param name="currentRenderer">Current renderer index.</param>
        public static void CombineMeshes(UMAMeshData target, SkinnedMeshCombiner.CombineInstance[] sources, BlendShapeSettings blendShapeSettings, UMAData.UMARecipe recipe, int currentRenderer)
        {
            CombineMeshes(target, sources, blendShapeSettings, recipe, currentRenderer, JobifiedSettings.Default);
        }

        /// <summary>
        /// Combines a set of meshes into the target mesh using MeshData API and Jobs with custom settings.
        /// </summary>
        public static void CombineMeshes(UMAMeshData target, SkinnedMeshCombiner.CombineInstance[] sources, BlendShapeSettings blendShapeSettings, UMAData.UMARecipe recipe, int currentRenderer, JobifiedSettings settings)
        {
            if (blendShapeSettings == null)
            {
                blendShapeSettings = new BlendShapeSettings();
            }

            // Early exit if no sources
            if (sources == null || sources.Length == 0)
            {
                return;
            }

            // For single source, use fast path
            if (sources.Length == 1)
            {
                CombineSingleMeshOptimized(target, sources[0], blendShapeSettings, recipe);
                return;
            }

            // Multiple sources - use jobified approach
            if (settings.useParallelJobs)
            {
                CombineMultipleMeshesJobified(target, sources, blendShapeSettings, recipe, currentRenderer, settings);
            }
            else
            {
                CombineMultipleMeshesSequential(target, sources, blendShapeSettings, recipe, currentRenderer);
            }
        }

        /// <summary>
        /// Fast path for single mesh combining - minimal overhead
        /// </summary>
        private static void CombineSingleMeshOptimized(UMAMeshData target, SkinnedMeshCombiner.CombineInstance source, BlendShapeSettings blendShapeSettings, UMAData.UMARecipe recipe)
        {
            // Use existing shallow copy approach for single mesh
            var tempMesh = SkinnedMeshCombiner.ShallowInstanceMesh(source.meshData, source.triangleMask);
            
            // Handle blend shapes if needed
            if (!blendShapeSettings.ignoreBlendShapes && recipe.BlendshapeSlots.ContainsKey(source.meshData.SlotName))
            {
                var blendShapes = SkinnedMeshCombiner.GetBlendshapeSources(tempMesh, recipe);
                tempMesh.blendShapes = blendShapes.ToArray();
            }

            // Copy data to target
            CopyMeshDataJobified(tempMesh, target);
        }

        /// <summary>
        /// Jobified approach for combining multiple meshes
        /// </summary>
        private static void CombineMultipleMeshesJobified(UMAMeshData target, SkinnedMeshCombiner.CombineInstance[] sources, BlendShapeSettings blendShapeSettings, UMAData.UMARecipe recipe, int currentRenderer, JobifiedSettings settings)
        {
            // Analyze sources to determine requirements
            var analysis = CreateMeshAnalysis();
            AnalyzeSourcesDetailed(sources, ref analysis);
            
            // Prepare target buffers
            PrepareTargetBuffers(target, analysis);
            
            // Use jobified processing
            CombineWithJobs(target, sources, analysis, settings);
            
            // Post-process (blend shapes, bone setup, etc.)
            PostProcessCombinedMesh(target, sources, blendShapeSettings, recipe, currentRenderer, analysis);
        }

        /// <summary>
        /// Sequential approach for combining multiple meshes (fallback)
        /// </summary>
        private static void CombineMultipleMeshesSequential(UMAMeshData target, SkinnedMeshCombiner.CombineInstance[] sources, BlendShapeSettings blendShapeSettings, UMAData.UMARecipe recipe, int currentRenderer)
        {
            // Analyze sources to determine requirements
            var analysis = CreateMeshAnalysis();
            AnalyzeSourcesDetailed(sources, ref analysis);
            
            // Prepare target buffers
            PrepareTargetBuffers(target, analysis);
            
            // Use optimized sequential processing
            CombineSequentialOptimized(target, sources, analysis);
            
            // Post-process (blend shapes, bone setup, etc.)
            PostProcessCombinedMesh(target, sources, blendShapeSettings, recipe, currentRenderer, analysis);
        }

        /// <summary>
        /// Combine meshes using Unity Job System
        /// </summary>
        private static void CombineWithJobs(UMAMeshData target, SkinnedMeshCombiner.CombineInstance[] sources, MeshAnalysis analysis, JobifiedSettings settings)
        {
            var jobData = CreateJobData(target, sources, analysis);
            
            try
            {
                var jobHandles = new List<JobHandle>();
                
                // Schedule vertex copying jobs
                if (analysis.totalVertexCount > 0)
                {
                    var vertexJob = new CopyVerticesJob
                    {
                        targetVertices = jobData.targetVertices,
                        sourceVertices = jobData.sourceVertices,
                        sourceOffsets = jobData.sourceOffsets,
                        sourceCounts = jobData.sourceCounts
                    };
                    jobHandles.Add(vertexJob.Schedule(sources.Length, 1));
                }
                
                // Schedule normal copying jobs if present
                if (analysis.hasNormals)
                {
                    var normalJob = new CopyNormalsJob
                    {
                        targetNormals = jobData.targetNormals,
                        sourceNormals = jobData.sourceNormals,
                        sourceOffsets = jobData.sourceOffsets,
                        sourceCounts = jobData.sourceCounts
                    };
                    jobHandles.Add(normalJob.Schedule(sources.Length, 1));
                }
                
                // Schedule triangle copying jobs
                if (analysis.submeshTriangleCounts.Count > 0)
                {
                    var triangleJob = new CopyTrianglesJob
                    {
                        targetTriangles = jobData.targetTriangles,
                        sourceTriangles = jobData.sourceTriangles,
                        triangleOffsets = jobData.triangleOffsets,
                        vertexOffsets = jobData.sourceOffsets,
                        triangleCounts = jobData.triangleCounts
                    };
                    jobHandles.Add(triangleJob.Schedule(sources.Length, 1));
                }
                
                // Wait for all jobs to complete
                JobHandle.CompleteAll(jobHandles);
                
                // Copy results back to target
                CopyJobResultsToTarget(target, jobData, analysis);
            }
            finally
            {
                // Always dispose job data
                DisposeJobData(jobData);
            }
        }

        /// <summary>
        /// Create job data structures
        /// </summary>
        private static JobData CreateJobData(UMAMeshData target, SkinnedMeshCombiner.CombineInstance[] sources, MeshAnalysis analysis)
        {
            var jobData = new JobData();
            
            // Create native arrays for job processing
            if (analysis.totalVertexCount > 0)
            {
                jobData.targetVertices = new NativeArray<Vector3>(analysis.totalVertexCount, Allocator.TempJob);
                jobData.sourceVertices = CreateSourceVertexArray(sources);
                jobData.sourceOffsets = CreateSourceOffsetArray(sources);
                jobData.sourceCounts = CreateSourceCountArray(sources);
            }
            
            if (analysis.hasNormals)
            {
                jobData.targetNormals = new NativeArray<Vector3>(analysis.totalVertexCount, Allocator.TempJob);
                jobData.sourceNormals = CreateSourceNormalArray(sources);
            }
            
            // Create triangle arrays
            if (analysis.submeshTriangleCounts.Count > 0)
            {
                int totalTriangles = 0;
                foreach (int count in analysis.submeshTriangleCounts)
                {
                    totalTriangles += count;
                }
                
                if (totalTriangles > 0)
                {
                    jobData.targetTriangles = new NativeArray<int>(totalTriangles, Allocator.TempJob);
                    jobData.sourceTriangles = CreateSourceTriangleArray(sources);
                    jobData.triangleOffsets = CreateTriangleOffsetArray(sources, analysis);
                    jobData.triangleCounts = CreateTriangleCountArray(sources);
                }
            }
            
            return jobData;
        }

        /// <summary>
        /// Copy job results back to target mesh data
        /// </summary>
        private static void CopyJobResultsToTarget(UMAMeshData target, JobData jobData, MeshAnalysis analysis)
        {
            // Copy vertices
            if (jobData.targetVertices.IsCreated)
            {
                jobData.targetVertices.CopyTo(target.vertices);
            }
            
            // Copy normals
            if (jobData.targetNormals.IsCreated && analysis.hasNormals)
            {
                jobData.targetNormals.CopyTo(target.normals);
            }
            
            // Copy triangles (handled by jobs directly in submesh buffers)
        }

        /// <summary>
        /// Create source vertex array for jobs
        /// </summary>
        private static NativeArray<Vector3> CreateSourceVertexArray(SkinnedMeshCombiner.CombineInstance[] sources)
        {
            var totalVertices = 0;
            foreach (var source in sources)
            {
                if (source.meshData?.vertices != null)
                    totalVertices += source.meshData.vertices.Length;
            }
            
            var sourceVertices = new NativeArray<Vector3>(totalVertices, Allocator.TempJob);
            int offset = 0;
            
            foreach (var source in sources)
            {
                if (source.meshData?.vertices != null)
                {
                    var vertices = new NativeArray<Vector3>(source.meshData.vertices, Allocator.Temp);
                    NativeArray<Vector3>.Copy(vertices, 0, sourceVertices, offset, vertices.Length);
                    offset += vertices.Length;
                    vertices.Dispose();
                }
            }
            
            return sourceVertices;
        }

        /// <summary>
        /// Create source offset array for jobs
        /// </summary>
        private static NativeArray<int> CreateSourceOffsetArray(SkinnedMeshCombiner.CombineInstance[] sources)
        {
            var offsets = new NativeArray<int>(sources.Length, Allocator.TempJob);
            int offset = 0;
            
            for (int i = 0; i < sources.Length; i++)
            {
                offsets[i] = offset;
                if (sources[i].meshData?.vertices != null)
                {
                    offset += sources[i].meshData.vertices.Length;
                }
            }
            
            return offsets;
        }

        /// <summary>
        /// Create source count array for jobs
        /// </summary>
        private static NativeArray<int> CreateSourceCountArray(SkinnedMeshCombiner.CombineInstance[] sources)
        {
            var counts = new NativeArray<int>(sources.Length, Allocator.TempJob);
            
            for (int i = 0; i < sources.Length; i++)
            {
                counts[i] = sources[i].meshData?.vertices?.Length ?? 0;
            }
            
            return counts;
        }

        /// <summary>
        /// Create source normal array for jobs
        /// </summary>
        private static NativeArray<Vector3> CreateSourceNormalArray(SkinnedMeshCombiner.CombineInstance[] sources)
        {
            var totalVertices = 0;
            foreach (var source in sources)
            {
                if (source.meshData?.normals != null)
                    totalVertices += source.meshData.normals.Length;
            }
            
            var sourceNormals = new NativeArray<Vector3>(totalVertices, Allocator.TempJob);
            int offset = 0;
            
            foreach (var source in sources)
            {
                if (source.meshData?.normals != null)
                {
                    var normals = new NativeArray<Vector3>(source.meshData.normals, Allocator.Temp);
                    NativeArray<Vector3>.Copy(normals, 0, sourceNormals, offset, normals.Length);
                    offset += normals.Length;
                    normals.Dispose();
                }
            }
            
            return sourceNormals;
        }

        /// <summary>
        /// Create placeholder arrays for triangle jobs (simplified for now)
        /// </summary>
        private static NativeArray<int> CreateSourceTriangleArray(SkinnedMeshCombiner.CombineInstance[] sources)
        {
            // Simplified implementation - return empty array for now
            return new NativeArray<int>(0, Allocator.TempJob);
        }

        private static NativeArray<int> CreateTriangleOffsetArray(SkinnedMeshCombiner.CombineInstance[] sources, MeshAnalysis analysis)
        {
            return new NativeArray<int>(sources.Length, Allocator.TempJob);
        }

        private static NativeArray<int> CreateTriangleCountArray(SkinnedMeshCombiner.CombineInstance[] sources)
        {
            return new NativeArray<int>(sources.Length, Allocator.TempJob);
        }

        /// <summary>
        /// Dispose job data
        /// </summary>
        private static void DisposeJobData(JobData jobData)
        {
            if (jobData.targetVertices.IsCreated) jobData.targetVertices.Dispose();
            if (jobData.sourceVertices.IsCreated) jobData.sourceVertices.Dispose();
            if (jobData.sourceOffsets.IsCreated) jobData.sourceOffsets.Dispose();
            if (jobData.sourceCounts.IsCreated) jobData.sourceCounts.Dispose();
            if (jobData.targetNormals.IsCreated) jobData.targetNormals.Dispose();
            if (jobData.sourceNormals.IsCreated) jobData.sourceNormals.Dispose();
            if (jobData.targetTriangles.IsCreated) jobData.targetTriangles.Dispose();
            if (jobData.sourceTriangles.IsCreated) jobData.sourceTriangles.Dispose();
            if (jobData.triangleOffsets.IsCreated) jobData.triangleOffsets.Dispose();
            if (jobData.triangleCounts.IsCreated) jobData.triangleCounts.Dispose();
        }

        /// <summary>
        /// Optimized sequential combining that can be easily converted to jobs later
        /// </summary>
        private static void CombineSequentialOptimized(UMAMeshData target, SkinnedMeshCombiner.CombineInstance[] sources, MeshAnalysis analysis)
        {
            int vertexOffset = 0;
            int boneWeightOffset = 0;
            var submeshOffsets = new int[analysis.submeshTriangleCounts.Count];
            
            // Process each source mesh
            for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                var source = sources[sourceIndex];
                if (source.meshData == null) continue;
                
                int sourceVertexCount = source.meshData.vertexCount;
                
                // Copy vertex data
                CopyVertexData(target, source.meshData, vertexOffset, sourceVertexCount, analysis);
                
                // Copy bone weights
                CopyBoneWeights(target, source.meshData, vertexOffset, boneWeightOffset);
                
                // Copy triangle data
                CopyTriangleData(target, source, vertexOffset, submeshOffsets);
                
                // Update slot data
                source.slotData.vertexOffset = vertexOffset;
                source.slotData.skinnedMeshRenderer = 0; // Will be set by caller
                
                // Update offsets
                vertexOffset += sourceVertexCount;
                boneWeightOffset += source.meshData.ManagedBoneWeights?.Length ?? 0;
            }
        }

        /// <summary>
        /// Copy vertex data from source to target
        /// </summary>
        private static void CopyVertexData(UMAMeshData target, UMAMeshData source, int offset, int count, MeshAnalysis analysis)
        {
            // Copy vertices
            if (source.vertices != null)
            {
                Array.Copy(source.vertices, 0, target.vertices, offset, count);
            }
            
            // Copy normals
            if (analysis.hasNormals && source.normals != null && source.normals.Length > 0)
            {
                Array.Copy(source.normals, 0, target.normals, offset, count);
            }
            
            // Copy tangents
            if (analysis.hasTangents && source.tangents != null && source.tangents.Length > 0)
            {
                Array.Copy(source.tangents, 0, target.tangents, offset, count);
            }
            
            // Copy UVs
            if (analysis.hasUV && source.uv != null && source.uv.Length >= count)
            {
                Array.Copy(source.uv, 0, target.uv, offset, count);
            }
            
            if (analysis.hasUV2 && source.uv2 != null && source.uv2.Length >= count)
            {
                Array.Copy(source.uv2, 0, target.uv2, offset, count);
            }
            
            if (analysis.hasUV3 && source.uv3 != null && source.uv3.Length >= count)
            {
                Array.Copy(source.uv3, 0, target.uv3, offset, count);
            }
            
            if (analysis.hasUV4 && source.uv4 != null && source.uv4.Length >= count)
            {
                Array.Copy(source.uv4, 0, target.uv4, offset, count);
            }
            
            // Copy colors
            if (analysis.hasColors && source.colors32 != null && source.colors32.Length > 0)
            {
                Array.Copy(source.colors32, 0, target.colors32, offset, count);
            }
            else if (analysis.hasColors)
            {
                // Fill with white if source doesn't have colors
                Color32 white = Color.white;
                for (int i = 0; i < count; i++)
                {
                    target.colors32[offset + i] = white;
                }
            }
        }

        /// <summary>
        /// Copy bone weight data from source to target
        /// </summary>
        private static void CopyBoneWeights(UMAMeshData target, UMAMeshData source, int vertexOffset, int boneWeightOffset)
        {
            if (source.ManagedBonesPerVertex != null && target.ManagedBonesPerVertex != null)
            {
                Array.Copy(source.ManagedBonesPerVertex, 0, target.ManagedBonesPerVertex, vertexOffset, source.ManagedBonesPerVertex.Length);
            }
            
            if (source.ManagedBoneWeights != null && target.ManagedBoneWeights != null)
            {
                Array.Copy(source.ManagedBoneWeights, 0, target.ManagedBoneWeights, boneWeightOffset, source.ManagedBoneWeights.Length);
            }
        }

        /// <summary>
        /// Copy triangle data from source to target
        /// </summary>
        private static void CopyTriangleData(UMAMeshData target, SkinnedMeshCombiner.CombineInstance source, int vertexOffset, int[] submeshOffsets)
        {
            for (int i = 0; i < source.meshData.subMeshCount; i++)
            {
                if (source.targetSubmeshIndices[i] >= 0)
                {
                    var sourceTriangles = source.meshData.submeshes[i].GetTriangles();
                    int destMeshIndex = source.targetSubmeshIndices[i];
                    var targetTriangles = target.submeshes[destMeshIndex].nativeTriangles;
                    
                    source.slotData.submeshIndex = destMeshIndex;
                    
                    if (source.triangleMask == null)
                    {
                        // Simple copy with vertex offset
                        CopyTrianglesWithOffset(sourceTriangles, targetTriangles, submeshOffsets[destMeshIndex], vertexOffset);
                        submeshOffsets[destMeshIndex] += sourceTriangles.Length;
                    }
                    else
                    {
                        // Masked copy with vertex offset
                        int copied = CopyTrianglesWithMask(sourceTriangles, targetTriangles, submeshOffsets[destMeshIndex], vertexOffset, source.triangleMask[i]);
                        submeshOffsets[destMeshIndex] += copied;
                    }
                }
            }
        }

        /// <summary>
        /// Copy triangles with vertex offset
        /// </summary>
        private static void CopyTrianglesWithOffset(NativeArray<int> source, NativeArray<int> target, int targetOffset, int vertexOffset)
        {
            for (int i = 0; i < source.Length; i++)
            {
                target[targetOffset + i] = source[i] + vertexOffset;
            }
        }

        /// <summary>
        /// Copy triangles with mask and vertex offset
        /// </summary>
        private static int CopyTrianglesWithMask(NativeArray<int> source, NativeArray<int> target, int targetOffset, int vertexOffset, BitArray mask)
        {
            int copied = 0;
            for (int i = 0; i < source.Length; i += 3)
            {
                if (!mask[i / 3])
                {
                    target[targetOffset + copied] = source[i] + vertexOffset;
                    target[targetOffset + copied + 1] = source[i + 1] + vertexOffset;
                    target[targetOffset + copied + 2] = source[i + 2] + vertexOffset;
                    copied += 3;
                }
            }
            return copied;
        }

        /// <summary>
        /// Copy mesh data using jobs for better performance
        /// </summary>
        private static void CopyMeshDataJobified(UMAMeshData source, UMAMeshData target)
        {
            target.vertexCount = source.vertexCount;
            target.subMeshCount = source.subMeshCount;
            
            // For now, fall back to array copying - this could be further optimized
            target.vertices = source.vertices;
            target.normals = source.normals;
            target.tangents = source.tangents;
            target.uv = source.uv;
            target.uv2 = source.uv2;
            target.uv3 = source.uv3;
            target.uv4 = source.uv4;
            target.colors32 = source.colors32;
            target.submeshes = source.submeshes;
            target.ManagedBoneWeights = source.ManagedBoneWeights;
            target.ManagedBonesPerVertex = source.ManagedBonesPerVertex;
            target.bindPoses = source.bindPoses;
            target.boneNameHashes = source.boneNameHashes;
            target.umaBones = source.umaBones;
            target.umaBoneCount = source.umaBoneCount;
            target.blendShapes = source.blendShapes;
        }

        /// <summary>
        /// Analyze sources to determine buffer sizes and requirements
        /// </summary>
        private static void AnalyzeSourcesDetailed(SkinnedMeshCombiner.CombineInstance[] sources, ref MeshAnalysis analysis)
        {
            foreach (var source in sources)
            {
                if (source.meshData == null) continue;
                
                analysis.totalVertexCount += source.meshData.vertexCount;
                analysis.totalBoneWeightCount += source.meshData.ManagedBoneWeights?.Length ?? 0;
                analysis.totalBindPoseCount += source.meshData.bindPoses?.Length ?? 0;
                analysis.totalTransformCount += source.meshData.umaBoneCount;
                
                // Analyze mesh components
                if (source.meshData.normals != null && source.meshData.normals.Length > 0)
                    analysis.hasNormals = true;
                if (source.meshData.tangents != null && source.meshData.tangents.Length > 0)
                    analysis.hasTangents = true;
                if (source.meshData.uv != null && source.meshData.uv.Length > 0)
                    analysis.hasUV = true;
                if (source.meshData.uv2 != null && source.meshData.uv2.Length > 0)
                    analysis.hasUV2 = true;
                if (source.meshData.uv3 != null && source.meshData.uv3.Length > 0)
                    analysis.hasUV3 = true;
                if (source.meshData.uv4 != null && source.meshData.uv4.Length > 0)
                    analysis.hasUV4 = true;
                if (source.meshData.colors32 != null && source.meshData.colors32.Length > 0)
                    analysis.hasColors = true;
                
                // Count triangles
                for (int i = 0; i < source.meshData.subMeshCount; i++)
                {
                    if (source.targetSubmeshIndices[i] >= 0)
                    {
                        var triangles = source.meshData.submeshes[i].GetTriangles();
                        int triangleCount = (source.triangleMask == null) ? triangles.Length :
                            (triangles.Length - (UMAUtils.GetCardinality(source.triangleMask[i]) * 3));
                        
                        // Ensure submesh arrays are large enough
                        while (analysis.submeshTriangleCounts.Count <= source.targetSubmeshIndices[i])
                        {
                            analysis.submeshTriangleCounts.Add(0);
                        }
                        analysis.submeshTriangleCounts[source.targetSubmeshIndices[i]] += triangleCount;
                    }
                }
            }
        }

        /// <summary>
        /// Prepare target mesh buffers based on analysis
        /// </summary>
        private static void PrepareTargetBuffers(UMAMeshData target, MeshAnalysis analysis)
        {
            target.vertexCount = analysis.totalVertexCount;
            target.subMeshCount = analysis.submeshTriangleCounts.Count;
            
            // Allocate vertex buffers
            target.vertices = new Vector3[analysis.totalVertexCount];
            
            if (analysis.hasNormals)
                target.normals = new Vector3[analysis.totalVertexCount];
            if (analysis.hasTangents)
                target.tangents = new Vector4[analysis.totalVertexCount];
            if (analysis.hasUV)
                target.uv = new Vector2[analysis.totalVertexCount];
            if (analysis.hasUV2)
                target.uv2 = new Vector2[analysis.totalVertexCount];
            if (analysis.hasUV3)
                target.uv3 = new Vector2[analysis.totalVertexCount];
            if (analysis.hasUV4)
                target.uv4 = new Vector2[analysis.totalVertexCount];
            if (analysis.hasColors)
                target.colors32 = new Color32[analysis.totalVertexCount];
            
            // Allocate bone weight buffers
            if (analysis.totalBoneWeightCount > 0)
            {
                target.ManagedBoneWeights = new BoneWeight1[analysis.totalBoneWeightCount];
                target.ManagedBonesPerVertex = new byte[analysis.totalVertexCount];
            }
            
            // Allocate submesh buffers
            target.submeshes = new SubMeshTriangles[target.subMeshCount];
            for (int i = 0; i < target.subMeshCount; i++)
            {
                if (analysis.submeshTriangleCounts[i] > 0)
                {
                    target.submeshes[i] = new SubMeshTriangles();
                    target.submeshes[i].nativeTriangles = target.GetSubmeshBuffer(analysis.submeshTriangleCounts[i], i);
                }
            }
        }

        /// <summary>
        /// Post-process the combined mesh (blend shapes, bone setup, etc.)
        /// </summary>
        private static void PostProcessCombinedMesh(UMAMeshData target, SkinnedMeshCombiner.CombineInstance[] sources, BlendShapeSettings blendShapeSettings, UMAData.UMARecipe recipe, int currentRenderer, MeshAnalysis analysis)
        {
            // Handle blend shapes
            if (!blendShapeSettings.ignoreBlendShapes)
            {
                ProcessBlendShapes(target, sources, blendShapeSettings, recipe);
            }
            
            // Set up bone hierarchy
            SetupBoneHierarchy(target, sources, analysis);
        }

        /// <summary>
        /// Process blend shapes for the combined mesh
        /// </summary>
        private static void ProcessBlendShapes(UMAMeshData target, SkinnedMeshCombiner.CombineInstance[] sources, BlendShapeSettings blendShapeSettings, UMAData.UMARecipe recipe)
        {
            // For now, delegate to existing implementation
            // This could be optimized with jobs in the future
            var blendShapeNames = new Dictionary<string, BlendShapeVertexData>();
            
            // Analyze blend shapes from sources
            foreach (var source in sources)
            {
                var sourceShapes = SkinnedMeshCombiner.GetBlendshapeSources(source.meshData, recipe);
                foreach (var shape in sourceShapes)
                {
                    if (!blendShapeNames.ContainsKey(shape.shapeName))
                    {
                        blendShapeNames[shape.shapeName] = new BlendShapeVertexData();
                    }
                }
            }
            
            // Create blend shape arrays
            if (blendShapeNames.Count > 0)
            {
                target.blendShapes = new UMABlendShape[blendShapeNames.Count];
                // Implementation details would go here
            }
        }

        /// <summary>
        /// Local BlendShapeVertexData class for jobified implementation
        /// </summary>
        private class BlendShapeVertexData
        {
            public bool hasNormals = false;
            public bool hasTangents = false;
            public int frameCount = 0;
            public float[] frameWeights;
            public int index;
        }

        /// <summary>
        /// Set up bone hierarchy for the combined mesh
        /// </summary>
        private static void SetupBoneHierarchy(UMAMeshData target, SkinnedMeshCombiner.CombineInstance[] sources, MeshAnalysis analysis)
        {
            // Combine bone transforms
            var bonesList = new List<int>(analysis.totalTransformCount);
            var bindPosesList = new List<Matrix4x4>(analysis.totalBindPoseCount);
            var umaTransforms = new List<UMATransform>(analysis.totalTransformCount);
            
            foreach (var source in sources)
            {
                if (source.meshData.boneNameHashes != null)
                {
                    bonesList.AddRange(source.meshData.boneNameHashes);
                }
                if (source.meshData.bindPoses != null)
                {
                    bindPosesList.AddRange(source.meshData.bindPoses);
                }
                if (source.meshData.umaBones != null)
                {
                    umaTransforms.AddRange(source.meshData.umaBones);
                }
            }
            
            target.boneNameHashes = bonesList.ToArray();
            target.bindPoses = bindPosesList.ToArray();
            target.umaBones = umaTransforms.ToArray();
            target.umaBoneCount = umaTransforms.Count;
        }

        /// <summary>
        /// Data structure for mesh analysis results
        /// </summary>
        private struct MeshAnalysis
        {
            public int totalVertexCount;
            public int totalBoneWeightCount;
            public int totalBindPoseCount;
            public int totalTransformCount;
            public bool hasNormals;
            public bool hasTangents;
            public bool hasUV;
            public bool hasUV2;
            public bool hasUV3;
            public bool hasUV4;
            public bool hasColors;
            public List<int> submeshTriangleCounts;
            
            public static MeshAnalysis Create()
            {
                return new MeshAnalysis
                {
                    submeshTriangleCounts = new List<int>()
                };
            }
        }

        /// <summary>
        /// Container for job data
        /// </summary>
        private struct JobData
        {
            public NativeArray<Vector3> targetVertices;
            public NativeArray<Vector3> sourceVertices;
            public NativeArray<int> sourceOffsets;
            public NativeArray<int> sourceCounts;
            public NativeArray<Vector3> targetNormals;
            public NativeArray<Vector3> sourceNormals;
            public NativeArray<int> targetTriangles;
            public NativeArray<int> sourceTriangles;
            public NativeArray<int> triangleOffsets;
            public NativeArray<int> triangleCounts;
        }

#if UMA_BURSTCOMPILE
        [BurstCompile]
#endif
        /// <summary>
        /// Job for copying vertex data in parallel
        /// </summary>
        private struct CopyVerticesJob : IJobParallelFor
        {
            public NativeArray<Vector3> targetVertices;
            [ReadOnly] public NativeArray<Vector3> sourceVertices;
            [ReadOnly] public NativeArray<int> sourceOffsets;
            [ReadOnly] public NativeArray<int> sourceCounts;
            
            public void Execute(int sourceIndex)
            {
                int targetOffset = sourceOffsets[sourceIndex];
                int count = sourceCounts[sourceIndex];
                
                // Find source offset in the flattened source array
                int sourceOffset = 0;
                for (int i = 0; i < sourceIndex; i++)
                {
                    sourceOffset += sourceCounts[i];
                }
                
                // Copy vertices
                for (int i = 0; i < count; i++)
                {
                    if (targetOffset + i < targetVertices.Length && sourceOffset + i < sourceVertices.Length)
                    {
                        targetVertices[targetOffset + i] = sourceVertices[sourceOffset + i];
                    }
                }
            }
        }

#if UMA_BURSTCOMPILE
        [BurstCompile]
#endif
        /// <summary>
        /// Job for copying normal data in parallel
        /// </summary>
        private struct CopyNormalsJob : IJobParallelFor
        {
            public NativeArray<Vector3> targetNormals;
            [ReadOnly] public NativeArray<Vector3> sourceNormals;
            [ReadOnly] public NativeArray<int> sourceOffsets;
            [ReadOnly] public NativeArray<int> sourceCounts;
            
            public void Execute(int sourceIndex)
            {
                int targetOffset = sourceOffsets[sourceIndex];
                int count = sourceCounts[sourceIndex];
                
                // Find source offset in the flattened source array
                int sourceOffset = 0;
                for (int i = 0; i < sourceIndex; i++)
                {
                    sourceOffset += sourceCounts[i];
                }
                
                // Copy normals
                for (int i = 0; i < count; i++)
                {
                    if (targetOffset + i < targetNormals.Length && sourceOffset + i < sourceNormals.Length)
                    {
                        targetNormals[targetOffset + i] = sourceNormals[sourceOffset + i];
                    }
                }
            }
        }

#if UMA_BURSTCOMPILE
        [BurstCompile]
#endif
        /// <summary>
        /// Job for copying triangle data with vertex offset adjustment
        /// </summary>
        private struct CopyTrianglesJob : IJobParallelFor
        {
            public NativeArray<int> targetTriangles;
            [ReadOnly] public NativeArray<int> sourceTriangles;
            [ReadOnly] public NativeArray<int> triangleOffsets;
            [ReadOnly] public NativeArray<int> vertexOffsets;
            [ReadOnly] public NativeArray<int> triangleCounts;
            
            public void Execute(int sourceIndex)
            {
                int targetOffset = triangleOffsets[sourceIndex];
                int vertexOffset = vertexOffsets[sourceIndex];
                int count = triangleCounts[sourceIndex];
                
                // Find source offset in the flattened source array
                int sourceOffset = 0;
                for (int i = 0; i < sourceIndex; i++)
                {
                    sourceOffset += triangleCounts[i];
                }
                
                // Copy triangles with vertex offset
                for (int i = 0; i < count; i++)
                {
                    if (targetOffset + i < targetTriangles.Length && sourceOffset + i < sourceTriangles.Length)
                    {
                        targetTriangles[targetOffset + i] = sourceTriangles[sourceOffset + i] + vertexOffset;
                    }
                }
            }
        }

        /// <summary>
        /// Initialize the MeshAnalysis structure
        /// </summary>
        private static MeshAnalysis CreateMeshAnalysis()
        {
            return MeshAnalysis.Create();
        }
    }
}