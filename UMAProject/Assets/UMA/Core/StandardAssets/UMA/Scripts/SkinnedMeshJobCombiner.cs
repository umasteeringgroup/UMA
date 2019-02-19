using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using System.Collections;
using System.Collections.Generic;
using System;

// using Unity.Collections.LowLevel.Unsafe;

namespace UMA
{
	/// <summary>
	/// Utility class for merging multiple skinned meshes.
	/// </summary>
	public static class SkinnedMeshJobCombiner
	{
		public static float elapsedTime = 0f;

		/// <summary>
		/// Native array adapter.
		/// </summary>
		/// <remarks>
		/// This seems to work in 2018.3 although it's obviously not desirable
		/// to be using unsafe code. It would be better if the actual source
		/// data was already in the required format, since it's all read only.
		/// </remarks>
		/*
		public unsafe class NativeArrayAdapter<T> : IDisposable where T : struct
		{
			private readonly ulong pinHandle;
			private readonly AtomicSafetyHandle safetyHandle;
			public NativeArray<T> nativeArray;

			public NativeArrayAdapter(T[] managedArray)
			{
				UnityEngine.Assertions.Assert.IsTrue(UnsafeUtility.IsBlittable<T>());

				void* arrayPtr = UnsafeUtility.PinGCArrayAndGetDataAddress(managedArray, out pinHandle);
				nativeArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(arrayPtr, managedArray.Length, Allocator.None);

				safetyHandle = AtomicSafetyHandle.Create();
				NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeArray, safetyHandle);
			}

			public void Dispose()
			{
				AtomicSafetyHandle.Release(safetyHandle);
				UnsafeUtility.ReleaseGCObject(pinHandle);
				nativeArray = default;
			}
		}
		*/

		/// <summary>
		/// Container for source mesh data.
		/// </summary>
		public class CombineInstance
		{
			public UMAMeshData meshData;
			public int[] targetSubmeshIndices;
			public BitArray vertexMask = null;
			public BitArray[] triangleMask = null;

			public NativeArray<Vector3> nativeVertices;
			public NativeArray<Vector3> nativeNormals;
			public NativeArray<Vector4> nativeTangents;
			public NativeArray<Vector2> nativeUV;
			public NativeArray<Vector2> nativeUV2;
			public NativeArray<Vector2> nativeUV3;
			public NativeArray<Vector2> nativeUV4;
			public NativeArray<Color32> nativeColors32;
			public NativeArray<UMABoneWeight> nativeBoneWeights;
		}

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

		static NativeArray<int> vertexRemaps;
		static NativeArray<int> rebindIndices;
		//static NativeArray<Matrix4x4> rebindMatrices;
		static NativeArray<float4x4> rebindMatrices;
		static SkinnedMeshJobCombiner ()
		{
			vertexRemaps = new NativeArray<int>(UMAMeshData.MAX_VERTEX_COUNT, Allocator.Persistent);
			rebindIndices = new NativeArray<int>(256, Allocator.Persistent);
			//rebindMatrices = new NativeArray<Matrix4x4>(256, Allocator.Persistent);
			rebindMatrices = new NativeArray<float4x4>(256, Allocator.Persistent);
		}

		// Job rebinding vertices to new bones
		[BurstCompile]
		public struct RebindJob : IJob
		{
			[ReadOnly]
			public NativeArray<int> vertMap;
			[ReadOnly]
			public NativeArray<Vector3> vertSource;
			[ReadOnly]
			public NativeArray<UMABoneWeight> weightSource;

			[ReadOnly]
			public NativeArray<int> bindIndices;
			[ReadOnly]
			//public NativeArray<Matrix4x4> bindMatrices;
			public NativeArray<float4x4> bindMatrices;

			public int index;
			[WriteOnly]
			public NativeArray<Vector3> dest;
			[WriteOnly]
			public NativeArray<UMABoneWeight> weights;

			public void Execute()
			{
				UMABoneWeight weight = new UMABoneWeight();

				for (int i = 0; i < vertSource.Length; i++)
				{
					if (vertMap[i] < 0) continue;

					//Vector3 vertexSrc = vertSource[i];
					float4 vertexSrc = new float4(vertSource[i], 1f);
					BoneWeight boneSrc = weightSource[i];

					// THEORY
					// Skeleton has a dictionary of <hash, preservedBoneIndex>
					// Build a skeleton from preserved transforms
					// Apply DNA to skeleton
					// 
					// UMATransform has a bind matrix
					// bindRemaps[] = dictionary lookup from current slot skinning (+ inherited ?)
					// bindTransforms[] = skinningBind.inv * skeleton bone to bone Matrix * slot umaTransoform bind
					// SMR binds and bones built from dictionary order

					// Rebind vertex to new bones

					weight.weight0 = boneSrc.weight0;			
					weight.weight1 = boneSrc.weight1;			
					weight.weight2 = boneSrc.weight2;			
					weight.weight3 = boneSrc.weight3;			
					weight.boneIndex0 = bindIndices[boneSrc.boneIndex0];
					weight.boneIndex1 = bindIndices[boneSrc.boneIndex1];
					weight.boneIndex2 = bindIndices[boneSrc.boneIndex2];
					weight.boneIndex3 = bindIndices[boneSrc.boneIndex3];
					weights[index] = weight;

					float4 vertex = float4.zero;
					vertex += math.mul(vertexSrc, bindMatrices[boneSrc.boneIndex0]) * boneSrc.weight0;
					vertex += math.mul(vertexSrc, bindMatrices[boneSrc.boneIndex1]) * boneSrc.weight1;
					vertex += math.mul(vertexSrc, bindMatrices[boneSrc.boneIndex2]) * boneSrc.weight2;
					vertex += math.mul(vertexSrc, bindMatrices[boneSrc.boneIndex3]) * boneSrc.weight3;
					dest[index++] = vertex.xyz;

					//float4x4 rebind =
					//	(bindMatrices[boneSrc.boneIndex0] * boneSrc.weight0) +
					//	(bindMatrices[boneSrc.boneIndex1] * boneSrc.weight1) +
					//	(bindMatrices[boneSrc.boneIndex2] * boneSrc.weight2) +
					//	(bindMatrices[boneSrc.boneIndex3] * boneSrc.weight3);
					//float4 vertex = math.mul(vertexSrc, rebind);
					//dest[index++] = vertex.xyz;

					//Vector3 vertex = Vector3.zero;
					//vertex += bindMatrices[boneSrc.boneIndex0].MultiplyPoint3x4(vertexSrc) * boneSrc.weight0;
					//vertex += bindMatrices[boneSrc.boneIndex1].MultiplyPoint3x4(vertexSrc) * boneSrc.weight1;
					//vertex += bindMatrices[boneSrc.boneIndex2].MultiplyPoint3x4(vertexSrc) * boneSrc.weight2;
					//vertex += bindMatrices[boneSrc.boneIndex3].MultiplyPoint3x4(vertexSrc) * boneSrc.weight3;
					//dest[index++] = vertex;
				}
			}
		}

		// Job remapping values in a native array
		[BurstCompile]
		public struct RemapJob<T> : IJob where T : struct
		{
			[ReadOnly]
			public NativeArray<int> map;
			[ReadOnly]
			public NativeArray<T> source;

			public int index;
			[WriteOnly]
			public NativeArray<T> dest;

			public void Execute()
			{
				for (int i = 0; i < source.Length; i++)
				{
					if (map[i] < 0) continue;

					dest[map[i]] = source[i];
				}
			}
		}

		// Job filling default values in a native array
		//[BurstCompile]
		//public struct FillJob<T> : IJobParallelFor where T : struct
		//{
		//	[ReadOnly]
		//	public T value;
		//	[WriteOnly]
		//	public NativeSlice<T> dest;

		//	public void Execute(int i)
		//	{
		//		dest[i] = value;
		//	}
		//}
		[BurstCompile]
		public struct FillJob<T> : IJob where T : struct
		{
			public int index;
			public int count;

			[ReadOnly]
			public T value;

			[WriteOnly]
			public NativeArray<T> dest;

			public void Execute()
			{
				for (int i = 0; i < count; i++)
				{
					dest[index++] = value;
				}
			}
		}

		/// <summary>
		/// Combines a set of meshes into the target mesh.
		/// </summary>
		/// <param name="target">Target.</param>
		/// <param name="sources">Sources.</param>
		/// <param name="blendShapeSettings">BlendShape Settings.</param>
		public static void CombineMeshes(UMAMeshData target, CombineInstance[] sources, UMASkeleton skeleton, UMAData.BlendShapeSettings blendShapeSettings = null)
		{
			foreach (CombineInstance source in sources)
			{
				source.nativeVertices = new NativeArray<Vector3>(source.meshData.vertices, Allocator.TempJob);
				source.nativeNormals = new NativeArray<Vector3>(source.meshData.normals, Allocator.TempJob);
				source.nativeTangents = new NativeArray<Vector4>(source.meshData.tangents, Allocator.TempJob);
				source.nativeUV = new NativeArray<Vector2>(source.meshData.uv, Allocator.TempJob);
				source.nativeUV2 = new NativeArray<Vector2>(source.meshData.uv2, Allocator.TempJob);
				source.nativeUV3 = new NativeArray<Vector2>(source.meshData.uv3, Allocator.TempJob);
				source.nativeUV4 = new NativeArray<Vector2>(source.meshData.uv4, Allocator.TempJob);
				source.nativeColors32 = new NativeArray<Color32>(source.meshData.colors32, Allocator.TempJob);
				source.nativeBoneWeights = new NativeArray<UMABoneWeight>(source.meshData.boneWeights, Allocator.TempJob);
			}

			Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = false;

			float startTime = Time.realtimeSinceStartup;

			int vertexCount = 0;
			int bindPoseCount = 0;
			int transformHierarchyCount = 0;
			int blendShapeCount = 0;
			int destIndex = 0;

			MeshComponents meshComponents = MeshComponents.none;
			int subMeshCount = FindTargetSubMeshCount(sources);
			var subMeshTriangleLength = new int[subMeshCount];
			AnalyzeSources(sources, subMeshTriangleLength, ref vertexCount, ref bindPoseCount, ref transformHierarchyCount, ref meshComponents, ref blendShapeCount);

			int[][] submeshTriangles = new int[subMeshCount][];
			for (int i = 0; i < subMeshTriangleLength.Length; i++)
			{
				submeshTriangles[i] = target.GetSubmeshBuffer(subMeshTriangleLength[i], i);
				subMeshTriangleLength[i] = 0;
			}

			bool has_normals = (meshComponents & MeshComponents.has_normals) != MeshComponents.none;
			bool has_tangents = (meshComponents & MeshComponents.has_tangents) != MeshComponents.none;
			bool has_uv = (meshComponents & MeshComponents.has_uv) != MeshComponents.none;
			bool has_uv2 = (meshComponents & MeshComponents.has_uv2) != MeshComponents.none;
			bool has_uv3 = (meshComponents & MeshComponents.has_uv3) != MeshComponents.none;
			bool has_uv4 = (meshComponents & MeshComponents.has_uv4) != MeshComponents.none;
			bool has_colors32 = (meshComponents & MeshComponents.has_colors32) != MeshComponents.none;

			UMATransform[] umaTransforms = EnsureArrayLength(target.umaBones, transformHierarchyCount);

			EnsureArrayLength(target.vertices, vertexCount);
			NativeArray<Vector3> vertices = new NativeArray<Vector3>(vertexCount, Allocator.TempJob);

			if (has_normals) EnsureArrayLength(target.normals, vertexCount);
			NativeArray<Vector3> normals = new NativeArray<Vector3>(vertexCount, Allocator.TempJob);
			if (has_tangents) EnsureArrayLength(target.tangents, vertexCount);
			NativeArray<Vector4> tangents = new NativeArray<Vector4>(vertexCount, Allocator.TempJob);

			if (has_uv) EnsureArrayLength(target.uv, vertexCount);
			NativeArray<Vector2> uv = new NativeArray<Vector2>(vertexCount, Allocator.TempJob);
			if (has_uv2) EnsureArrayLength(target.uv2, vertexCount);
			NativeArray<Vector2> uv2 = new NativeArray<Vector2>(vertexCount, Allocator.TempJob);
			if (has_uv3) EnsureArrayLength(target.uv3, vertexCount);
			NativeArray<Vector2> uv3 = new NativeArray<Vector2>(vertexCount, Allocator.TempJob);
			if (has_uv4) EnsureArrayLength(target.uv4, vertexCount);
			NativeArray<Vector2> uv4 = new NativeArray<Vector2>(vertexCount, Allocator.TempJob);

			if (has_colors32) EnsureArrayLength(target.colors32, vertexCount);
			NativeArray<Color32> colors32 = new NativeArray<Color32>(vertexCount, Allocator.TempJob);

			NativeArray<UMABoneWeight> boneWeights = new NativeArray<UMABoneWeight>(vertexCount, Allocator.TempJob);

			int boneCount = 0;
			int vertexIndex = 0;
			int blendShapeIndex = 0;

			foreach (CombineInstance source in sources)
			{
				bool has_vertexMask = (source.vertexMask != null);
				int sourceVertexCount = source.meshData.vertexCount;
				int sourceBoneCount = source.meshData.umaBones.Length;

				JobHandle rebindJobHandle = new JobHandle();
				JobHandle normalJobHandle = new JobHandle();
				JobHandle tangentJobHandle = new JobHandle();
				JobHandle uvJobHandle = new JobHandle();
				JobHandle uv2JobHandle = new JobHandle();
				JobHandle uv3JobHandle = new JobHandle();
				JobHandle uv4JobHandle = new JobHandle();
				JobHandle colorJobHandle = new JobHandle();

				if (sourceBoneCount > rebindIndices.Length)
				{
					Debug.LogWarning("Very high bone count may indicate problem with mesh data.");
					rebindIndices.Dispose();
					rebindMatrices.Dispose();
					rebindIndices = new NativeArray<int>(sourceBoneCount, Allocator.Persistent);
					//rebindMatrices = new NativeArray<Matrix4x4>(sourceBoneCount, Allocator.Persistent);
					rebindMatrices = new NativeArray<float4x4>(sourceBoneCount, Allocator.Persistent);
				}

				for (int i = 0; i < sourceBoneCount; i++)
				{
					UMATransform bone = source.meshData.umaBones[i];
					// HACK
					// This WILL NOT WORK in the case of eliminating a bone
					// if the bone parent doesn't have a valid bind
					// maybe those can be built from the child bone
					// which will have one at some level or wouldn't
					// require reskinning.
					rebindIndices[i] = skeleton.GetSkinningIndex(bone.hash);
					rebindMatrices[i] = skeleton.GetSkinningBindToBone(bone.hash).inverse * bone.bindToBone;
				}

				destIndex = vertexIndex;
				for (int i = 0; i < sourceVertexCount; i++)
				{
					if (has_vertexMask && source.vertexMask[i])
					{
						// Vertex is occluded
						vertexRemaps[i] = -1;
					}
					else
					{
						vertexRemaps[i] = destIndex++;
					}
				}

				// Apply BAKED blendshape data here

				RebindJob rebind = new RebindJob
				{
					vertMap = vertexRemaps,
					vertSource = source.nativeVertices,
					weightSource = source.nativeBoneWeights,

					bindIndices = rebindIndices,
					bindMatrices = rebindMatrices,

					index = vertexIndex,
					dest = vertices,
					weights = boneWeights
				};
				rebindJobHandle = rebind.Schedule();

				if (has_normals)
				{
					if (source.meshData.normals != null && source.meshData.normals.Length > 0)
					{
						RemapJob<Vector3> remap = new RemapJob<Vector3>
						{
							map = vertexRemaps,
							source = source.nativeNormals,
							index = vertexIndex,
							dest = normals
						};
						normalJobHandle = remap.Schedule();
					}
					else
					{
						FillJob<Vector3> clear = new FillJob<Vector3>
						{
							value = Vector3.zero,
							index = vertexIndex,
							count = sourceVertexCount,
							dest = normals
							//dest = normals.Slice(vertexIndex)
						};
						normalJobHandle = clear.Schedule();
						//normalJobHandle = clear.Schedule(sourceVertexCount, 64);
					}
				}

				if (has_tangents)
				{
					if (source.meshData.tangents != null && source.meshData.tangents.Length > 0)
					{
						RemapJob<Vector4> remap = new RemapJob<Vector4>
						{
							map = vertexRemaps,
							source = source.nativeTangents,
							index = vertexIndex,
							dest = tangents
						};
						tangentJobHandle = remap.Schedule();
					}
					else
					{
						FillJob<Vector4> clear = new FillJob<Vector4>
						{
							value = Vector4.zero,
							index = vertexIndex,
							count = sourceVertexCount,
							dest = tangents
							//dest = tangents.Slice(vertexIndex)
						};
						tangentJobHandle = clear.Schedule();
						//tangentJobHandle = clear.Schedule(sourceVertexCount, 64);
					}
				}

				if (has_uv)
				{
					if (source.meshData.uv != null && source.meshData.uv.Length >= sourceVertexCount)
					{
						RemapJob<Vector2> remap = new RemapJob<Vector2>
						{
							map = vertexRemaps,
							source = source.nativeUV,
							index = vertexIndex,
							dest = uv
						};
						uvJobHandle = remap.Schedule();
					}
					else
					{
						FillJob<Vector2> clear = new FillJob<Vector2>
						{
							value = Vector2.zero,
							index = vertexIndex,
							count = sourceVertexCount,
							dest = uv
							//dest = uv.Slice(vertexIndex)
						};
						uvJobHandle = clear.Schedule();
						//uvJobHandle = clear.Schedule(sourceVertexCount, 64);
					}
				}
				if (has_uv2)
				{
					if (source.meshData.uv2 != null && source.meshData.uv2.Length >= sourceVertexCount)
					{
						RemapJob<Vector2> remap = new RemapJob<Vector2>
						{
							map = vertexRemaps,
							source = source.nativeUV2,
							index = vertexIndex,
							dest = uv2
						};
						uv2JobHandle = remap.Schedule();
					}
					else
					{
						FillJob<Vector2> clear = new FillJob<Vector2>
						{
							value = Vector2.zero,
							index = vertexIndex,
							count = sourceVertexCount,
							dest = uv2
							//dest = uv2.Slice(vertexIndex)
						};
						uv2JobHandle = clear.Schedule();
						//uv2JobHandle = clear.Schedule(sourceVertexCount, 64);
					}
				}
				if (has_uv3)
				{
					if (source.meshData.uv3 != null && source.meshData.uv3.Length >= sourceVertexCount)
					{
						RemapJob<Vector2> remap = new RemapJob<Vector2>
						{
							map = vertexRemaps,
							source = source.nativeUV3,
							index = vertexIndex,
							dest = uv3
						};
						uv3JobHandle = remap.Schedule();
					}
					else
					{
						FillJob<Vector2> clear = new FillJob<Vector2>
						{
							value = Vector2.zero,
							index = vertexIndex,
							count = sourceVertexCount,
							dest = uv3
							//dest = uv3.Slice(vertexIndex)
						};
						uv3JobHandle = clear.Schedule();
						//uv3JobHandle = clear.Schedule(sourceVertexCount, 64);
					}
				}
				if (has_uv4)
				{
					if (source.meshData.uv4 != null && source.meshData.uv4.Length >= sourceVertexCount)
					{
						RemapJob<Vector2> remap = new RemapJob<Vector2>
						{
							map = vertexRemaps,
							source = source.nativeUV3,
							index = vertexIndex,
							dest = uv4
						};
						uv4JobHandle = remap.Schedule();
					}
					else
					{
						FillJob<Vector2> clear = new FillJob<Vector2>
						{
							value = Vector2.zero,
							index = vertexIndex,
							count = sourceVertexCount,
							dest = uv4
							//dest = uv4.Slice(vertexIndex)
						};
						uv4JobHandle = clear.Schedule();
						//uv4JobHandle = clear.Schedule(sourceVertexCount, 64);
					}
				}

				if (has_colors32)
				{
					if (source.meshData.colors32 != null && source.meshData.colors32.Length > 0)
					{
						RemapJob<Color32> remap = new RemapJob<Color32>
						{
							map = vertexRemaps,
							source = source.nativeColors32,
							index = vertexIndex,
							dest = colors32
						};
						colorJobHandle = remap.Schedule();
					}
					else
					{
						FillJob<Color32> clear = new FillJob<Color32>
						{
							value = Color.white,
							index = vertexIndex,
							count = sourceVertexCount,
							dest = colors32
							//dest = colors32.Slice(vertexIndex)
						};
						colorJobHandle = clear.Schedule();
						//colorJobHandle = clear.Schedule(sourceVertexCount, 64);
					}
				}

				for (int i = 0; i < source.meshData.subMeshCount; i++)
				{
					if (source.targetSubmeshIndices[i] >= 0)
					{
						int[] subTriangles = source.meshData.submeshes[i].triangles;
						int triangleLength = subTriangles.Length;

						int destMesh = source.targetSubmeshIndices[i];

						if (source.triangleMask == null)
						{
							// CopyIntArrayAdd(subTriangles, 0, submeshTriangles[destMesh], subMeshTriangleLength[destMesh], triangleLength, vertexIndex);
							destIndex = subMeshTriangleLength[destMesh];
							for (int j = 0; j < triangleLength; j++)
							{
								submeshTriangles[destMesh][destIndex++] = vertexRemaps[subTriangles[j]];
							}

							subMeshTriangleLength[destMesh] += triangleLength;
						}
						else
						{
							MaskedCopyIntArrayAdd(subTriangles, 0, submeshTriangles[destMesh], subMeshTriangleLength[destMesh], triangleLength, vertexIndex, source.triangleMask[i]);
							subMeshTriangleLength[destMesh] += (triangleLength - (UMAUtils.GetCardinality(source.triangleMask[i]) * 3));
						}
					}
				}

				vertexIndex += sourceVertexCount;

				rebindJobHandle.Complete();
				normalJobHandle.Complete();
				tangentJobHandle.Complete();
				uvJobHandle.Complete();
				uv2JobHandle.Complete();
				uv3JobHandle.Complete();
				uv4JobHandle.Complete();
				colorJobHandle.Complete();
			}

			if (vertexCount != vertexIndex)
			{
				Debug.LogError("Combined vertices size didn't match precomputed value!");
			}

			// fill in new values.
			target.vertexCount = vertexCount;
			
			float combineTime = (Time.realtimeSinceStartup - startTime) * 1000f;
			elapsedTime += combineTime;
			Debug.Log("Job combine took: " + combineTime + " ms");

			NativeArray<Vector3>.Copy(vertices, target.vertices, vertexCount);

			NativeArray<UMABoneWeight>.Copy(boneWeights, target.boneWeights, vertexCount);

			if (has_normals)
			{
				NativeArray<Vector3>.Copy(normals, target.normals, vertexCount);
			}
			else target.normals = null;

			if (has_tangents)
			{
				NativeArray<Vector4>.Copy(tangents, target.tangents, vertexCount);
			}
			else target.tangents = null;

			if (has_uv)
			{
				NativeArray<Vector2>.Copy(uv, target.uv, vertexCount);
			}
			else target.uv = null;

			if (has_uv2)
			{
				NativeArray<Vector2>.Copy(uv2, target.uv2, vertexCount);
			}
			else target.uv2 = null;

			if (has_uv3)
			{
				NativeArray<Vector2>.Copy(uv3, target.uv3, vertexCount);
			}
			else target.uv3 = null;

			if (has_uv4)
			{
				NativeArray<Vector2>.Copy(uv4, target.uv4, vertexCount);
			}
			else target.uv4 = null;

			if (has_colors32)
			{
				NativeArray<Color32>.Copy(colors32, target.colors32, vertexCount);
			}
			else target.colors32 = null;

			target.subMeshCount = subMeshCount;
			target.submeshes = new SubMeshTriangles[subMeshCount];
			for (int i = 0; i < subMeshCount; i++)
			{
				target.submeshes[i].triangles = submeshTriangles[i];
			}

			vertices.Dispose();
			boneWeights.Dispose();
			normals.Dispose();
			tangents.Dispose();
			uv.Dispose();
			uv2.Dispose();
			uv3.Dispose();
			uv4.Dispose();
			colors32.Dispose();

			foreach (CombineInstance source in sources)
			{
				source.nativeVertices.Dispose();
				source.nativeNormals.Dispose();
				source.nativeTangents.Dispose();
				source.nativeUV.Dispose();
				source.nativeUV2.Dispose();
				source.nativeUV3.Dispose();
				source.nativeUV4.Dispose();
				source.nativeColors32.Dispose();
				source.nativeBoneWeights.Dispose();
			}
		}

		public static UMAMeshData ShallowInstanceMesh(UMAMeshData source)
		{
			var target = new UMAMeshData();
			target.bindPoses = source.bindPoses;
			target.boneNameHashes = source.boneNameHashes;
			target.boneWeights = source.boneWeights;
			target.colors32 = source.colors32;
			target.normals = source.normals;
			target.rootBoneHash = source.rootBoneHash;
			target.subMeshCount = source.subMeshCount;
			target.submeshes = source.submeshes;
			target.tangents = source.tangents;
			target.umaBones = source.umaBones;
			target.uv = source.uv;
			target.uv2 = source.uv2;
			target.uv3 = source.uv3;
			target.uv4 = source.uv4;
			target.vertexCount = source.vertexCount;
			target.vertices = source.vertices;

			if (source.clothSkinningSerialized != null && source.clothSkinningSerialized.Length != 0)
			{
				target.clothSkinning = new ClothSkinningCoefficient[source.clothSkinningSerialized.Length];
				for (int i = 0; i < source.clothSkinningSerialized.Length; i++)
				{
					ConvertData(ref source.clothSkinningSerialized[i], ref target.clothSkinning[i]);
				}
			}
			else
			{
				target.clothSkinning = null;
			}
			return target;
		}

		public static void ConvertData(ref Vector2 source, ref ClothSkinningCoefficient dest)
		{
			dest.collisionSphereDistance = source.x;
			dest.maxDistance = source.y;
		}

		public static void ConvertData(ref ClothSkinningCoefficient source, ref Vector2 dest)
		{
			dest.x = source.collisionSphereDistance;
			dest.y = source.maxDistance;
		}

		private static void AnalyzeSources(CombineInstance[] sources, int[] subMeshTriangleLength, ref int vertexCount, ref int bindPoseCount, ref int transformHierarchyCount, ref MeshComponents meshComponents, ref int blendShapeCount)
		{
			HashSet<string> blendShapeNames = new HashSet<string>(); //Hash to find all the unique blendshape names

			for (int i = 0; i < subMeshTriangleLength.Length; i++)
			{
				subMeshTriangleLength[i] = 0;
			}

			foreach (var source in sources)
			{
				int vertexLength = source.meshData.vertices.Length;
				if (source.vertexMask != null)
				{
					vertexLength -= UMAUtils.GetCardinality(source.vertexMask);
				}

				vertexCount += vertexLength;
				bindPoseCount += source.meshData.bindPoses.Length;
				transformHierarchyCount += source.meshData.umaBones.Length;

				if (source.meshData.normals != null && source.meshData.normals.Length != 0) meshComponents |= MeshComponents.has_normals;
				if (source.meshData.tangents != null && source.meshData.tangents.Length != 0) meshComponents |= MeshComponents.has_tangents;
				if (source.meshData.uv != null && source.meshData.uv.Length != 0) meshComponents |= MeshComponents.has_uv;
				if (source.meshData.uv2 != null && source.meshData.uv2.Length != 0) meshComponents |= MeshComponents.has_uv2;
				if (source.meshData.uv3 != null && source.meshData.uv3.Length != 0) meshComponents |= MeshComponents.has_uv3;
				if (source.meshData.uv4 != null && source.meshData.uv4.Length != 0) meshComponents |= MeshComponents.has_uv4;
				if (source.meshData.colors32 != null && source.meshData.colors32.Length != 0) meshComponents |= MeshComponents.has_colors32;
				if (source.meshData.clothSkinningSerialized != null && source.meshData.clothSkinningSerialized.Length != 0) meshComponents |= MeshComponents.has_clothSkinning;

				// If we find a blendshape on this mesh then lets add it to the blendShapeNames hash to get all the unique names
				if (source.meshData.blendShapes != null && source.meshData.blendShapes.Length != 0)
				{
					for (int shapeIndex = 0; shapeIndex < source.meshData.blendShapes.Length; shapeIndex++)
						blendShapeNames.Add(source.meshData.blendShapes[shapeIndex].shapeName);
				}

				for (int i = 0; i < source.meshData.subMeshCount; i++)
				{
					if (source.targetSubmeshIndices[i] >= 0)
					{
						int triangleLength = source.meshData.submeshes[i].triangles.Length;
						if (source.triangleMask != null)
						{
							triangleLength -= UMAUtils.GetCardinality(source.triangleMask[i]) * 3;
						}

						subMeshTriangleLength[source.targetSubmeshIndices[i]] += triangleLength;
					}
				}
			}

			//If our blendshape hash has at least 1 name, then we have a blendshape!
			if (blendShapeNames.Count > 0)
			{
				blendShapeCount = blendShapeNames.Count;
				meshComponents |= MeshComponents.has_blendShapes;
			}
		}

		private static int FindTargetSubMeshCount(CombineInstance[] sources)
		{
			int highestTargetIndex = -1;
			foreach (var source in sources)
			{
				foreach (var targetIndex in source.targetSubmeshIndices)
				{
					if (highestTargetIndex < targetIndex)
					{
						highestTargetIndex = targetIndex;
					}
				}
			}
			return highestTargetIndex + 1;
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
			// These never change in a TRS Matrix4x4
			//			if (Mathf.Abs(m1.m30 - m2.m30) > 0.0001) return false;
			//			if (Mathf.Abs(m1.m31 - m2.m31) > 0.0001) return false;
			//			if (Mathf.Abs(m1.m32 - m2.m32) > 0.0001) return false;
			//			if (Mathf.Abs(m1.m33 - m2.m33) > 0.0001) return false;
			return true;
		}

		private static void CopyColorsToColors32(Color[] source, int sourceIndex, Color32[] dest, int destIndex, int count)
		{
			while (count-- > 0)
			{
				var sColor = source[sourceIndex++];
				dest[destIndex++] = new Color32((byte)Mathf.RoundToInt(sColor.r * 255f), (byte)Mathf.RoundToInt(sColor.g * 255f), (byte)Mathf.RoundToInt(sColor.b * 255f), (byte)Mathf.RoundToInt(sColor.a * 255f));
			}
		}

		private static void FillArray(Vector4[] array, int index, int count, Vector4 value)
		{
			while (count-- > 0)
			{
				array[index++] = value;
			}
		}

		private static void FillArray(Vector3[] array, int index, int count, Vector3 value)
		{
			while (count-- > 0)
			{
				array[index++] = value;
			}
		}

		private static void FillArray(Vector2[] array, int index, int count, Vector2 value)
		{
			while (count-- > 0)
			{
				array[index++] = value;
			}
		}

		private static void FillArray(Color[] array, int index, int count, Color value)
		{
			while (count-- > 0)
			{
				array[index++] = value;
			}
		}

		private static void FillArray(Color32[] array, int index, int count, Color32 value)
		{
			while (count-- > 0)
			{
				array[index++] = value;
			}
		}

		private static void CopyIntArrayAdd(int[] source, int sourceIndex, int[] dest, int destIndex, int count, int add)
		{
			for (int i = 0; i < count; i++)
			{
				dest[destIndex++] = source[sourceIndex++] + add;
			}
		}

		public static void MaskedCopyIntArrayAdd(int[] source, int sourceIndex, int[] dest, int destIndex, int count, int add, BitArray mask)
		{
			if ((mask.Count * 3) != source.Length || (mask.Count * 3) != count)
			{
				Debug.LogError("MaskedCopyIntArrayAdd: mask and source count do not match!");
				return;
			}

			for (int i = 0; i < count; i += 3)
			{
				if (!mask[(i / 3)])
				{
					dest[destIndex++] = source[sourceIndex + i + 0] + add;
					dest[destIndex++] = source[sourceIndex + i + 1] + add;
					dest[destIndex++] = source[sourceIndex + i + 2] + add;
				}
			}
		}

		private static T[] EnsureArrayLength<T>(T[] oldArray, int newLength)
		{
			if (newLength <= 0)
				return null;

			if (oldArray != null && oldArray.Length >= newLength)
				return oldArray;

			//			Debug.Log("EnsureArrayLength allocating array of " + newLength + " of type: " + typeof(T));
			return new T[newLength];
		}
	}
}
