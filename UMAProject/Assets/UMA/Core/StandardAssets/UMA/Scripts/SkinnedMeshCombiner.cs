using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
#if UNITY_2019_3_OR_NEWER
using Unity.Collections;
#endif

namespace UMA
{
	/// <summary>
	/// Utility class for merging multiple skinned meshes.
	/// </summary>
	public static class SkinnedMeshCombiner
	{
#if UNITY_2019_3_OR_NEWER
		static SkinnedMeshCombiner()
		{
			AppDomain.CurrentDomain.DomainUnload += CurrentDomain_DomainUnload;
		}

		private static void CurrentDomain_DomainUnload(object sender, EventArgs e)
        {
            CleanupNativeArrays();
        }

        private static void CleanupNativeArrays()
        {
			//Debug.Log("Cleaned up SkinnedMesh Arrays in mesh combiner");
            if (nativeBoneWeights.IsCreated) nativeBoneWeights.Dispose();
            if (nativeBonesPerVertex.IsCreated) nativeBonesPerVertex.Dispose();
        }
#endif

        /// <summary>
        /// Container for source mesh data.
        /// </summary>
        public class CombineInstance
		{
			public UMAMeshData meshData;
			public int[] targetSubmeshIndices;
			public BitArray[] triangleMask;
		}

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

			//this is the saved index into the final blendshape area that we will copy this blendshape in to
			public int index;
		}

		static Dictionary<int, BoneIndexEntry> bonesCollection;
		static List<Matrix4x4> bindPoses;
		static List<int> bonesList;

		static NativeArray<BoneWeight1> nativeBoneWeights;
		static NativeArray<byte> nativeBonesPerVertex;

		/// <summary>
		/// Combines a set of meshes into the target mesh.
		/// </summary>
		/// <param name="target">Target.</param>
		/// <param name="sources">Sources.</param>
		/// <param name="blendShapeSettings">BlendShape Settings.</param>
		public static void CombineMeshes(UMAMeshData target, CombineInstance[] sources, BlendShapeSettings blendShapeSettings = null)
		{
			if (blendShapeSettings == null)
				blendShapeSettings = new BlendShapeSettings();

			int boneWeightCount = 0;
			int vertexCount = 0;
			int bindPoseCount = 0;
			int transformHierarchyCount = 0;
			Dictionary<string, BlendShapeVertexData> blendShapeNames = new Dictionary<string, BlendShapeVertexData>();

			MeshComponents meshComponents = MeshComponents.none;
			int subMeshCount = FindTargetSubMeshCount(sources);
			var subMeshTriangleLength = new int[subMeshCount];
			AnalyzeSources(sources, subMeshTriangleLength, ref vertexCount, ref boneWeightCount, ref bindPoseCount, ref transformHierarchyCount, ref meshComponents);

			if(!blendShapeSettings.ignoreBlendShapes)
				AnalyzeBlendShapeSources(sources, blendShapeSettings, ref meshComponents, out blendShapeNames);

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
			bool has_blendShapes = (meshComponents & MeshComponents.has_blendShapes) != MeshComponents.none;
			if (blendShapeSettings.ignoreBlendShapes)
				has_blendShapes = false;
			bool has_clothSkinning = (meshComponents & MeshComponents.has_clothSkinning) != MeshComponents.none;


			if (nativeBoneWeights.Length < boneWeightCount || nativeBonesPerVertex.Length < vertexCount)
			{
				CleanupNativeArrays();

				//Debug.Log("Creating native arrays in mesh combiner");
				nativeBoneWeights = new NativeArray<BoneWeight1>(boneWeightCount, Allocator.Persistent);
				nativeBonesPerVertex = new NativeArray<byte>(vertexCount, Allocator.Persistent);
			}

			Vector3[] vertices = EnsureArrayLength(target.vertices, vertexCount);
			Vector3[] normals = has_normals ? EnsureArrayLength(target.normals, vertexCount) : null;
			Vector4[] tangents = has_tangents ? EnsureArrayLength(target.tangents, vertexCount) : null;
			Vector2[] uv = has_uv ? EnsureArrayLength(target.uv, vertexCount) : null;
			Vector2[] uv2 = has_uv2 ? EnsureArrayLength(target.uv2, vertexCount) : null;
			Vector2[] uv3 = has_uv3 ? EnsureArrayLength(target.uv3, vertexCount) : null;
			Vector2[] uv4 = has_uv4 ? EnsureArrayLength(target.uv4, vertexCount) : null;
			Color32[] colors32 = has_colors32 ? EnsureArrayLength(target.colors32, vertexCount) : null;
			UMABlendShape[] blendShapes = has_blendShapes ? new UMABlendShape[blendShapeNames.Keys.Count] : null;
			UMATransform[] umaTransforms = EnsureArrayLength(target.umaBones, transformHierarchyCount);
			ClothSkinningCoefficient[] clothSkinning = has_clothSkinning ? EnsureArrayLength(target.clothSkinning, vertexCount) : null;
			Dictionary<Vector3, int> clothVertices = has_clothSkinning ? new Dictionary<Vector3, int>(vertexCount) : null;
			Dictionary<Vector3, int> localClothVertices = has_clothSkinning ? new Dictionary<Vector3, int>(vertexCount) : null;

			InitializeBlendShapeData(ref vertexCount, blendShapeNames, blendShapes);

			int boneCount = 0;
			foreach (var source in sources)
			{
				MergeSortedTransforms(umaTransforms, ref boneCount, source.meshData.umaBones);
			}
			int vertexIndex = 0;
			int boneWeightIndex = 0;

			if (bonesCollection == null)
				bonesCollection = new Dictionary<int, BoneIndexEntry>(boneCount);
			else
				bonesCollection.Clear();
			if (bindPoses == null)
				bindPoses = new List<Matrix4x4>(bindPoseCount);
			else
				bindPoses.Clear();
			if (bonesList == null)
				bonesList = new List<int>(boneCount);
			else
				bonesList.Clear();

			foreach (var source in sources)
			{
				int sourceVertexCount = source.meshData.vertices.Length;
				BuildBoneWeights(source.meshData, nativeBoneWeights, nativeBonesPerVertex, vertexIndex, boneWeightIndex, sourceVertexCount, source.meshData.boneNameHashes, source.meshData.bindPoses, bonesCollection, bindPoses, bonesList);
				Array.Copy(source.meshData.vertices, 0, vertices, vertexIndex, sourceVertexCount);

				if (has_normals)
				{
					if (source.meshData.normals != null && source.meshData.normals.Length > 0)
					{
						Array.Copy(source.meshData.normals, 0, normals, vertexIndex, sourceVertexCount);
					}
					else
					{
						FillArray(tangents, vertexIndex, sourceVertexCount, Vector3.zero);
					}
				}
				if (has_tangents)
				{
					if (source.meshData.tangents != null && source.meshData.tangents.Length > 0)
					{
						Array.Copy(source.meshData.tangents, 0, tangents, vertexIndex, sourceVertexCount);
					}
					else
					{
						FillArray(tangents, vertexIndex, sourceVertexCount, Vector4.zero);
					}
				}
				if (has_uv)
				{
					if (source.meshData.uv != null && source.meshData.uv.Length >= sourceVertexCount)
					{
						Array.Copy(source.meshData.uv, 0, uv, vertexIndex, sourceVertexCount);
					}
					else
					{
						FillArray(uv, vertexIndex, sourceVertexCount, Vector4.zero);
					}
				}
				if (has_uv2)
				{
					if (source.meshData.uv2 != null && source.meshData.uv2.Length >= sourceVertexCount)
					{
						Array.Copy(source.meshData.uv2, 0, uv2, vertexIndex, sourceVertexCount);
					}
					else
					{
						FillArray(uv2, vertexIndex, sourceVertexCount, Vector4.zero);
					}
				}
				if (has_uv3)
				{
					if (source.meshData.uv3 != null && source.meshData.uv3.Length >= sourceVertexCount)
					{
						Array.Copy(source.meshData.uv3, 0, uv3, vertexIndex, sourceVertexCount);
					}
					else
					{
						FillArray(uv3, vertexIndex, sourceVertexCount, Vector4.zero);
					}
				}
				if (has_uv4)
				{
					if (source.meshData.uv4 != null && source.meshData.uv4.Length >= sourceVertexCount)
					{
						Array.Copy(source.meshData.uv4, 0, uv4, vertexIndex, sourceVertexCount);
					}
					else
					{
						FillArray(uv4, vertexIndex, sourceVertexCount, Vector4.zero);
					}
				}

				if (has_colors32)
				{
					if (source.meshData.colors32 != null && source.meshData.colors32.Length > 0)
					{
						Array.Copy(source.meshData.colors32, 0, colors32, vertexIndex, sourceVertexCount);
					}
					else
					{
						Color32 white32 = Color.white;
						FillArray(colors32, vertexIndex, sourceVertexCount, white32);
					}
				}

				if (has_blendShapes) 
				{
					if (source.meshData.blendShapes != null && source.meshData.blendShapes.Length > 0) 
					{
						int sourceBlendShapeLength = source.meshData.blendShapes.Length;
						for (int shapeIndex = 0; shapeIndex < sourceBlendShapeLength; shapeIndex++)
						{
							string shapeName = source.meshData.blendShapes[shapeIndex].shapeName;

							//If we aren't loading all blendshapes and we don't find the blendshape name in the list of explicit blendshapes to combine, then skip to the next one.
							if (!blendShapeSettings.loadAllBlendShapes && !blendShapeSettings.blendShapes.ContainsKey(shapeName))
								continue;

#region BlendShape Baking
							if(BakeBlendShape(blendShapeSettings.blendShapes, source.meshData.blendShapes[shapeIndex], ref vertexIndex, vertices, normals, tangents, has_normals, has_tangents))
								continue; //If we baked this blendshape, then continue to the next one and skip adding the regular blendshape.
#endregion
							
							//If our dictionary contains the shape name, which it should
							if (blendShapeNames.ContainsKey(shapeName))
							{
								UMABlendShape[] sourceBlendShapes = source.meshData.blendShapes;
								int i = blendShapeNames[shapeName].index;

								if (blendShapes[i].frames.Length != sourceBlendShapes[shapeIndex].frames.Length)
								{
									if (Debug.isDebugBuild)
										Debug.LogError("SkinnedMeshCombiner: mesh blendShape frame counts don't match!");
									break;
								}

								for (int frameIndex = 0; frameIndex < sourceBlendShapes[shapeIndex].frames.Length; frameIndex++)
								{
									Array.Copy(sourceBlendShapes[shapeIndex].frames[frameIndex].deltaVertices, 0, blendShapes[i].frames[frameIndex].deltaVertices, vertexIndex, sourceVertexCount);

									Vector3[] sourceDeltaNormals = sourceBlendShapes[shapeIndex].frames[frameIndex].deltaNormals;
									Vector3[] sourceDeltaTangents = sourceBlendShapes[shapeIndex].frames[frameIndex].deltaTangents;

									//if out dictionary says at least one source has normals or tangents and the current source has normals or tangents then copy them.
									if (blendShapeNames[shapeName].hasNormals && sourceDeltaNormals.Length > 0)
										Array.Copy(sourceDeltaNormals, 0, blendShapes[i].frames[frameIndex].deltaNormals, vertexIndex, sourceVertexCount);

									if (blendShapeNames[shapeName].hasTangents && sourceDeltaTangents.Length > 0)
										Array.Copy(sourceDeltaTangents, 0, blendShapes[i].frames[frameIndex].deltaTangents, vertexIndex, sourceVertexCount);
								}
							}
							else
							{
								if(Debug.isDebugBuild)
									Debug.LogError("BlendShape " + shapeName + " not found in dictionary!");
							}
						}
					}
				}
				if (has_clothSkinning)
				{
					localClothVertices.Clear();
					if (source.meshData.clothSkinningSerialized != null && source.meshData.clothSkinningSerialized.Length > 0)
					{
						for (int i = 0; i < source.meshData.vertexCount; i++)
						{
							var vertice = source.meshData.vertices[i];
							if (!localClothVertices.ContainsKey(vertice))
							{
								int localCount = localClothVertices.Count;
								localClothVertices.Add(vertice, localCount);
								if (!clothVertices.ContainsKey(vertice))
								{
									ConvertData(ref source.meshData.clothSkinningSerialized[localCount], ref clothSkinning[clothVertices.Count]);
									clothVertices.Add(vertice, clothVertices.Count);
								}
								else
								{
									ConvertData(ref source.meshData.clothSkinningSerialized[localCount], ref clothSkinning[clothVertices[vertice]]);
								}
							}
						}
					}
					else
					{
						for (int i = 0; i < source.meshData.vertexCount; i++)
						{
							var vertice = source.meshData.vertices[i];
							if (!clothVertices.ContainsKey(vertice))
							{
								clothSkinning[clothVertices.Count].maxDistance = 0;
								clothSkinning[clothVertices.Count].collisionSphereDistance = float.MaxValue;
								clothVertices.Add(vertice, clothVertices.Count);
								localClothVertices.Add(vertice, clothVertices.Count);
							}
						}
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
							CopyIntArrayAdd(subTriangles, 0, submeshTriangles[destMesh], subMeshTriangleLength[destMesh], triangleLength, vertexIndex);
							subMeshTriangleLength[destMesh] += triangleLength;
						}
						else
						{
							MaskedCopyIntArrayAdd(subTriangles, 0, submeshTriangles[destMesh], subMeshTriangleLength[destMesh], triangleLength, vertexIndex, source.triangleMask[i] );
							subMeshTriangleLength[destMesh] += (triangleLength - (UMAUtils.GetCardinality(source.triangleMask[i])*3));
						}
					}
				}

				vertexIndex += sourceVertexCount;
#if USE_NATIVE_ARRAYS
				boneWeightIndex += source.meshData.unityBoneWeights.Length;
#else
				boneWeightIndex += source.meshData.ManagedBoneWeights.Length;
#endif
			}

			if (vertexCount != vertexIndex)
			{
				if (Debug.isDebugBuild)
					Debug.LogError("Combined vertices size didn't match precomputed value!");
			}

			// fill in new values.
			target.vertexCount = vertexCount;
			target.vertices = vertices;
#if USE_NATIVE_ARRAYS
			target.unityBoneWeights = nativeBoneWeights.GetSubArray(0, boneWeightIndex);
			target.unityBonesPerVertex = nativeBonesPerVertex.GetSubArray(0, vertexCount);
#else
			target.ManagedBoneWeights = nativeBoneWeights.GetSubArray(0, boneWeightIndex).ToArray();
			target.ManagedBonesPerVertex = nativeBonesPerVertex.GetSubArray(0, vertexCount).ToArray();
#endif
			target.bindPoses = bindPoses.ToArray();
			target.normals = normals;
			target.tangents = tangents;
			target.uv = uv;
			target.uv2 = uv2;
			target.uv3 = uv3;
			target.uv4 = uv4;
			target.colors32 = colors32;

			if (has_blendShapes) 
				target.blendShapes = blendShapes;

			if (has_clothSkinning)
			{
				Array.Resize(ref clothSkinning, clothVertices.Count);
			}
			target.clothSkinning = clothSkinning;

			target.subMeshCount = subMeshCount;
			target.submeshes = new SubMeshTriangles[subMeshCount];
			target.umaBones = umaTransforms;
			target.umaBoneCount = boneCount;
			for (int i = 0; i < subMeshCount; i++)
			{
				target.submeshes[i].triangles = submeshTriangles[i];
			}
			target.boneNameHashes = bonesList.ToArray();
		}

		public static UMAMeshData ShallowInstanceMesh(UMAMeshData source, BitArray[] triangleMask = null)
		{
			var target = new UMAMeshData();
			target.bindPoses = source.bindPoses;
			target.boneNameHashes = source.boneNameHashes;
#if USE_NATIVE_ARRAYS
			target.unityBoneWeights = source.unityBoneWeights;
			target.unityBonesPerVertex = source.unityBonesPerVertex;
#endif
			target.colors32 = source.colors32;
			target.normals = source.normals;
			target.rootBoneHash = source.rootBoneHash;
			target.subMeshCount = source.subMeshCount;
			target.tangents = source.tangents;
			target.umaBoneCount = source.umaBoneCount;
			target.umaBones = source.umaBones;
			target.uv = source.uv;
			target.uv2 = source.uv2;
			target.uv3 = source.uv3;
			target.uv4 = source.uv4;
			target.vertexCount = source.vertexCount;
			target.vertices = source.vertices;
			target.blendShapes = source.blendShapes;
			target.SlotName = source.SlotName + " (shallow copy)";
			target.ManagedBonesPerVertex = source.ManagedBonesPerVertex;
			target.ManagedBoneWeights = source.ManagedBoneWeights;

			if (triangleMask != null)
			{
				target.submeshes = new SubMeshTriangles[source.subMeshCount];

				for (int i = 0; i < source.subMeshCount; i++)
				{

					int sourceLength = source.submeshes[i].triangles.Length;
					int triangleLength = sourceLength - (UMAUtils.GetCardinality(triangleMask[i]) * 3);
					int[] destTriangles = new int[triangleLength];

					MaskedCopyIntArrayAdd(source.submeshes[i].triangles, 0, destTriangles, 0, sourceLength, 0, triangleMask[i]);
					target.submeshes[i].triangles = destTriangles;
				}
			}
			else
				target.submeshes = source.submeshes;

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

		public static bool BakeBlendShape(Dictionary<string, BlendShapeData> blendShapes, UMABlendShape currentShape, ref int vertexIndex, Vector3[] vertices, Vector3[] normals, Vector4[] tangents, bool has_Normals, bool has_Tangents)
		{
			//If we can't find this blendshape then it can't have been baked so return false.
			BlendShapeData data;
			if (!blendShapes.TryGetValue(currentShape.shapeName, out data))
				return false;

			//If we find this blendshape but it is not set to be baked, then return false.
			if (!data.isBaked)
				return false;

			float weight = blendShapes[currentShape.shapeName].value * 100.0f;
			
			// Allow < 0 weights.
			// if (weight <= 0f) return true; // Baking in nothing, so skip it entirely
			if (Mathf.Abs(weight) <= Mathf.Epsilon) return true;

			// Let's find the frame this weight is in
			int frameIndex;
			int prevIndex;
			for (frameIndex = 0; frameIndex < currentShape.frames.Length; frameIndex++)
			{
				if (currentShape.frames[frameIndex].frameWeight >= weight)
					break;
			}

			// Let's calculate the weight for the frame we're in
			float frameWeight = 1f;
			float prevWeight = 0f;
			bool doLerp = false;
			// Weight is higher than the last frame, shape is over 100%
			if (frameIndex >= currentShape.frames.Length)
			{
				frameIndex = currentShape.frames.Length - 1;
				frameWeight = (weight / currentShape.frames[frameIndex].frameWeight);
			}
			else if (frameIndex > 0)
			{
				doLerp = true;
				prevWeight = currentShape.frames[frameIndex - 1].frameWeight;
				frameWeight = ((weight - prevWeight) / (currentShape.frames[frameIndex].frameWeight - prevWeight));
				prevWeight = 1f - frameWeight;
			}
			else
			{
				frameWeight = (weight / currentShape.frames[frameIndex].frameWeight);
			}
			prevIndex = (frameIndex > 0) ? (frameIndex - 1) : 0;

			// The blend shape frames lerp between the deltas of two adjacent frames.
			Vector3[] currentFrameVertices = currentShape.frames[frameIndex].deltaVertices;
			Vector3[] previousFrameVertices = currentShape.frames[prevIndex].deltaVertices;

			Vector3[] currentFrameNormals = null;
			Vector3[] previousFrameNormals = null;

			Vector3[] currentFrameTangents = null;
			Vector3[] previousFrameTangents = null;

			bool has_deltaNormals = (has_Normals && currentShape.frames[frameIndex].deltaNormals != null && currentShape.frames[frameIndex].deltaNormals.Length > 0);
			if (has_deltaNormals)
			{
				currentFrameNormals = currentShape.frames[frameIndex].deltaNormals;
				previousFrameNormals = currentShape.frames[prevIndex].deltaNormals;
			}

			bool has_deltaTangents = (has_Tangents && currentShape.frames[frameIndex].deltaTangents != null && currentShape.frames[frameIndex].deltaTangents.Length > 0);
			if (has_deltaTangents)
			{
				currentFrameTangents = currentShape.frames[frameIndex].deltaTangents;
				previousFrameTangents = currentShape.frames[prevIndex].deltaTangents;
			}

			int vertIndex = vertexIndex;
			for (int bakeIndex = 0; bakeIndex < currentFrameVertices.Length; bakeIndex++, vertIndex++)
			{
				// Add the current frame's deltas
				if (currentFrameVertices[bakeIndex].sqrMagnitude > 0.0000001f)
				{
					vertices[vertIndex] += currentFrameVertices[bakeIndex] * frameWeight;
					// Add in the previous frame's deltas
					if (doLerp)
						vertices[vertIndex] += previousFrameVertices[bakeIndex] * prevWeight;
				}

				if (has_deltaNormals)
				{
					if (currentFrameNormals[bakeIndex].sqrMagnitude > 0.0000001f)
					{
						normals[vertIndex] += currentFrameNormals[bakeIndex] * frameWeight;
						if (doLerp)
							normals[vertIndex] += previousFrameNormals[bakeIndex] * prevWeight;
					}
				}

				if (has_deltaTangents)
				{
					if (currentFrameTangents[bakeIndex].sqrMagnitude > 0.0000001f)
					{
						tangents[vertIndex] += (Vector4)currentFrameTangents[bakeIndex] * frameWeight;
						if (doLerp)
							tangents[vertIndex] += (Vector4)previousFrameTangents[bakeIndex] * prevWeight;
					}
				}
			}
			return true;
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

		private static void MergeSortedTransforms(UMATransform[] mergedTransforms, ref int len1, UMATransform[] umaTransforms)
		{
			int newBones = 0;
			int pos1 = 0;
			int pos2 = 0;
			int len2 = umaTransforms.Length;

			while(pos1 < len1 && pos2 < len2 )
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

			int dest = len1-1;

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

		private static void AnalyzeBlendShapeSources(CombineInstance[] sources, BlendShapeSettings blendShapeSettings, ref MeshComponents meshComponents, out Dictionary<string, BlendShapeVertexData> blendShapeNames)
		{
			blendShapeNames = new Dictionary<string, BlendShapeVertexData>();

			if (blendShapeSettings.ignoreBlendShapes)
				return;

			int bakedCount = 0;

			foreach (var source in sources)
			{
				//If we find a blendshape on this mesh then lets add it to the blendShapeNames hash to get all the unique names
				if (source.meshData.blendShapes == null)
					continue;

				if (source.meshData.blendShapes.Length == 0)
					continue;

				for (int shapeIndex = 0; shapeIndex < source.meshData.blendShapes.Length; shapeIndex++)
				{
					string shapeName = source.meshData.blendShapes[shapeIndex].shapeName;

					//if we are baking this blendshape then skip and don't add to the blendshape names.
					BlendShapeData data;
					if(blendShapeSettings.blendShapes.TryGetValue(shapeName, out data))
					{
						if (data.isBaked)
						{
							bakedCount++;
							continue;
						}
					}

					if (!blendShapeNames.ContainsKey(shapeName))
					{
						BlendShapeVertexData newData = new BlendShapeVertexData();
						blendShapeNames.Add(shapeName, newData);
					}

					blendShapeNames[shapeName].hasNormals |= source.meshData.blendShapes[shapeIndex].frames[0].HasNormals();
					blendShapeNames[shapeName].hasTangents |= source.meshData.blendShapes[shapeIndex].frames[0].HasTangents();

					if (source.meshData.blendShapes[shapeIndex].frames.Length > blendShapeNames[shapeName].frameCount)
					{
						blendShapeNames[shapeName].frameCount = source.meshData.blendShapes[shapeIndex].frames.Length;
						blendShapeNames[shapeName].frameWeights = new float[blendShapeNames[shapeName].frameCount];

						for (int i = 0; i < blendShapeNames[shapeName].frameCount; i++)
						{
							//technically two sources could have different frame weights for the same blendshape, but then thats a problem with the source.
							blendShapeNames[shapeName].frameWeights[i] = source.meshData.blendShapes[shapeIndex].frames[i].frameWeight;
						}
					}
				}
			}

			//If our blendshape hash has at least 1 name, then we have a blendshape!
			if (blendShapeNames.Count > 0 || bakedCount > 0)
			{
				meshComponents |= MeshComponents.has_blendShapes;
			}
		}

		private static void InitializeBlendShapeData(ref int vertexCount, Dictionary<string, BlendShapeVertexData> blendShapeNames, UMABlendShape[] blendShapes)
		{
			int blendShapeIndex = 0;
			foreach( string shapeName in blendShapeNames.Keys)
			{
				blendShapeNames[shapeName].index = blendShapeIndex;
				blendShapes[blendShapeIndex] = new UMABlendShape();
				blendShapes[blendShapeIndex].shapeName = shapeName;
				blendShapes[blendShapeIndex].frames = new UMABlendFrame[blendShapeNames[shapeName].frameCount];

				for (int frameIndex = 0; frameIndex < blendShapes[blendShapeIndex].frames.Length; frameIndex++)
				{
					blendShapes[blendShapeIndex].frames[frameIndex] = new UMABlendFrame(vertexCount, blendShapeNames[shapeName].hasNormals, blendShapeNames[shapeName].hasTangents);
					blendShapes[blendShapeIndex].frames[frameIndex].frameWeight = blendShapeNames[shapeName].frameWeights[frameIndex];
				}

				blendShapeIndex++;
			}
		}

		private static void AnalyzeSources(CombineInstance[] sources, int[] subMeshTriangleLength, ref int vertexCount, ref int boneweightcount, ref int bindPoseCount, ref int transformHierarchyCount, ref MeshComponents meshComponents)
		{
			for (int i = 0; i < subMeshTriangleLength.Length; i++)
			{
				subMeshTriangleLength[i] = 0;
			}

			foreach (var source in sources)
			{
#if USE_NATIVE_ARRAYS
				boneweightcount += source.meshData.unityBoneWeights.Length;
#else
				boneweightcount += source.meshData.ManagedBoneWeights.Length;
#endif
				vertexCount += source.meshData.vertices.Length;
				bindPoseCount += source.meshData.bindPoses.Length;
				transformHierarchyCount += source.meshData.umaBones.Length;
				if (source.meshData.normals != null && source.meshData.normals.Length != 0) meshComponents |= MeshComponents.has_normals;
				if (source.meshData.tangents != null && source.meshData.tangents.Length != 0) meshComponents |= MeshComponents.has_tangents;
				if (source.meshData.uv != null && source.meshData.uv.Length != 0) meshComponents |= MeshComponents.has_uv;
				if (source.meshData.uv2 != null && source.meshData.uv2.Length != 0) meshComponents |= MeshComponents.has_uv2;
				if (source.meshData.uv3 != null && source.meshData.uv3.Length != 0) meshComponents |= MeshComponents.has_uv3;
				if (source.meshData.uv4 != null && source.meshData.uv4.Length != 0) meshComponents |= MeshComponents.has_uv4;
				if (source.meshData.colors32 != null && source.meshData.colors32.Length != 0) meshComponents |= MeshComponents.has_colors32;
				if (source.meshData.clothSkinningSerialized != null && source.meshData.clothSkinningSerialized.Length != 0)	meshComponents |= MeshComponents.has_clothSkinning;

				for (int i = 0; i < source.meshData.subMeshCount; i++)
				{
					if (source.targetSubmeshIndices[i] >= 0)
					{
						int triangleLength = (source.triangleMask == null) ? source.meshData.submeshes[i].triangles.Length :
							(source.meshData.submeshes[i].triangles.Length - (UMAUtils.GetCardinality(source.triangleMask[i]) * 3));

						subMeshTriangleLength[source.targetSubmeshIndices[i]] += triangleLength;
					}
				}
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


		/*private static void BuildBoneWeights(NativeArray<BoneWeight1> source, NativeArray<BoneWeight1> dest, int destIndex, int destBoneweightIndex, int count, int[] bones, Matrix4x4[] bindPoses, Dictionary<int, BoneIndexEntry> bonesCollection, List<Matrix4x4> bindPosesList, List<int> bonesList)
		{
			int[] boneMapping = new int[bones.Length];

			for (int i = 0; i < boneMapping.Length; i++)
			{
				boneMapping[i] = TranslateBoneIndex(i, bones, bindPoses, bonesCollection, bindPosesList, bonesList);
			}

			NativeArray<BoneWeight1>.Copy(source, 0, dest, destBoneweightIndex, source.Length);
			BoneWeight1 b = new BoneWeight1();
			for (int i=0;i<source.Length;i++)
            {
				b.boneIndex = boneMapping[source[i].boneIndex];
				b.weight = source[i].weight;

				dest[i + destBoneweightIndex] = b;
            }
		} */

		private static void BuildBoneWeights(UMAMeshData data, NativeArray<BoneWeight1> dest, NativeArray<byte> destBonesPerVertex, int destIndex, int destBoneweightIndex, int count, int[] bones, Matrix4x4[] bindPoses, Dictionary<int, BoneIndexEntry> bonesCollection, List<Matrix4x4> bindPosesList, List<int> bonesList)
		{
			int[] boneMapping = new int[bones.Length];

			for (int i = 0; i < boneMapping.Length; i++)
			{
				boneMapping[i] = TranslateBoneIndex(i, bones, bindPoses, bonesCollection, bindPosesList, bonesList);
			}

#if USE_NATIVE_ARRAYS
			NativeArray<byte> sourceBonesPerIndex = data.unityBonesPerVertex;
			int sourcecount = sourceBonesPerIndex.Length;
			int destcount = destBonesPerVertex.Length; // should be 0.

			NativeArray<byte>.Copy(sourceBonesPerIndex, 0,destBonesPerVertex,destIndex, sourceBonesPerIndex.Length);
			NativeArray<BoneWeight1>.Copy(data.unityBoneWeights, 0, dest, destBoneweightIndex, data.unityBoneWeights.Length);
			BoneWeight1 b = new BoneWeight1();
			for (int i = 0; i < data.unityBoneWeights.Length; i++)
			{
				b.boneIndex = boneMapping[data.unityBoneWeights[i].boneIndex];
				b.weight = data.unityBoneWeights[i].weight;

				dest[i + destBoneweightIndex] = b;
			}
#else
			NativeArray<byte>.Copy(data.ManagedBonesPerVertex, 0, destBonesPerVertex, destIndex, data.ManagedBonesPerVertex.Length);
			NativeArray<BoneWeight1>.Copy(data.ManagedBoneWeights, 0, dest, destBoneweightIndex, data.ManagedBoneWeights.Length);

			BoneWeight1 b = new BoneWeight1();
			for (int i = 0; i < data.ManagedBoneWeights.Length; i++)
			{
				b.boneIndex = boneMapping[data.ManagedBoneWeights[i].boneIndex];
				b.weight = data.ManagedBoneWeights[i].weight;
				dest[i + destBoneweightIndex] = b;
			}

#endif
		}

		private struct BoneIndexEntry
		{
			public int index;
			public List<int> indices;
			public int Count { get { return index >= 0 ? 1 : indices.Count; } }
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
			// These never change in a TRS Matrix4x4
//			if (Mathf.Abs(m1.m30 - m2.m30) > 0.0001) return false;
//			if (Mathf.Abs(m1.m31 - m2.m31) > 0.0001) return false;
//			if (Mathf.Abs(m1.m32 - m2.m32) > 0.0001) return false;
//			if (Mathf.Abs(m1.m33 - m2.m33) > 0.0001) return false;
			return true;
		}

		private static int TranslateBoneIndex(int index, int[] bonesHashes, Matrix4x4[] bindPoses, Dictionary<int, BoneIndexEntry> bonesCollection, List<Matrix4x4> bindPosesList, List<int> bonesList)
		{
			var boneTransform = bonesHashes[index];
			BoneIndexEntry entry;
			if (bonesCollection.TryGetValue(boneTransform, out entry))
			{
				for (int i = 0; i < entry.Count; i++)
				{
					var res = entry[i];
					if (CompareSkinningMatrices(bindPosesList[res], ref bindPoses[index]))
					{
						return res;
					}
				}
				var idx = bindPosesList.Count;
				entry.AddIndex(idx);
				bindPosesList.Add(bindPoses[index]);
				bonesList.Add(boneTransform);
				return idx;
			}
			else
			{
				var idx = bindPosesList.Count;
				bonesCollection.Add(boneTransform, new BoneIndexEntry() { index = idx });
				bindPosesList.Add(bindPoses[index]);
				bonesList.Add(boneTransform);
				return idx;
			}
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
			if ((mask.Count*3) != source.Length || (mask.Count*3) != count)
			{
				if (Debug.isDebugBuild)
					Debug.LogError("MaskedCopyIntArrayAdd: mask and source count do not match!");
				return;
			}
                
			for (int i = 0; i < count; i+=3)
			{
				if (!mask[(i/3)])
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
