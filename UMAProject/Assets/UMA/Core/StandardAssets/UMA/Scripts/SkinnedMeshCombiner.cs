using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

namespace UMA
{
	/// <summary>
	/// Utility class for merging multiple skinned meshes.
	/// </summary>
	public static class SkinnedMeshCombiner
	{
		/// <summary>
		/// Container for source mesh data.
		/// </summary>
		public class CombineInstance
		{
			public UMAMeshData meshData;
			public int[] targetSubmeshIndices;
			public BitArray vertexMask = null;
			public BitArray[] triangleMask = null;
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

		static int[] vertexRemaps = new int[UMAMeshData.MAX_VERTEX_COUNT];
		static int[] rebindIndices = new int[256];
		static Matrix4x4[] rebindMatrices = new Matrix4x4[256];

		/// <summary>
		/// Combines a set of meshes into the target mesh.
		/// </summary>
		/// <param name="target">Target.</param>
		/// <param name="sources">Sources.</param>
		/// <param name="blendShapeSettings">BlendShape Settings.</param>
		public static void CombineMeshes(UMAMeshData target, CombineInstance[] sources, UMASkeleton skeleton, UMAData.BlendShapeSettings blendShapeSettings = null)
		{
			if (blendShapeSettings == null)
				blendShapeSettings = new UMAData.BlendShapeSettings();
            
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
			bool has_blendShapes = (meshComponents & MeshComponents.has_blendShapes) != MeshComponents.none;
			if (blendShapeSettings.ignoreBlendShapes)
				has_blendShapes = false;
			bool has_clothSkinning = (meshComponents & MeshComponents.has_clothSkinning) != MeshComponents.none;

			Vector3[] vertices = EnsureArrayLength(target.vertices, vertexCount);
			UMABoneWeight[] boneWeights = EnsureArrayLength(target.boneWeights, vertexCount);
			Vector3[] normals = has_normals ? EnsureArrayLength(target.normals, vertexCount) : null;
			Vector4[] tangents = has_tangents ? EnsureArrayLength(target.tangents, vertexCount) : null;
			Vector2[] uv = has_uv ? EnsureArrayLength(target.uv, vertexCount) : null;
			Vector2[] uv2 = has_uv2 ? EnsureArrayLength(target.uv2, vertexCount) : null;
			Vector2[] uv3 = has_uv3 ? EnsureArrayLength(target.uv3, vertexCount) : null;
			Vector2[] uv4 = has_uv4 ? EnsureArrayLength(target.uv4, vertexCount) : null;
			Color32[] colors32 = has_colors32 ? EnsureArrayLength(target.colors32, vertexCount) : null;
			UMABlendShape[] blendShapes = has_blendShapes ? new UMABlendShape[blendShapeCount] : null;
			UMATransform[] umaTransforms = EnsureArrayLength(target.umaBones, transformHierarchyCount);
			ClothSkinningCoefficient[] clothSkinning = has_clothSkinning ? EnsureArrayLength(target.clothSkinning, vertexCount) : null;
			Dictionary<Vector3, int> clothVertices = has_clothSkinning ? new Dictionary<Vector3, int>(vertexCount) : null;
			Dictionary<Vector3, int> localClothVertices = has_clothSkinning ? new Dictionary<Vector3, int>(vertexCount) : null;

			int boneCount = 0;
			int vertexIndex = 0;
			int blendShapeIndex = 0;

			foreach (var source in sources)
			{
				bool has_vertexMask = (source.vertexMask != null);
				int sourceVertexCount = source.meshData.vertexCount;
				int sourceBoneCount = source.meshData.umaBones.Length;

				if (sourceBoneCount > rebindIndices.Length)
				{
					Debug.LogWarning("Very high bone count may indicate problem with mesh data.");
					rebindIndices = new int[sourceBoneCount];
					rebindMatrices = new Matrix4x4[sourceBoneCount];
				}

				for (int i = 0; i < sourceBoneCount; i++)
				{
					UMATransform bone = source.meshData.umaBones[i];
					// HACK - needs to include remap to non-retained bones
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
						vertexRemaps[i] = destIndex;
						Vector3 vertexSrc = source.meshData.vertices[i];
						BoneWeight boneSrc = source.meshData.boneWeights[i];

						// Apply BAKED blendshape data here

						// THEORY
						// Skeleton has a dictionary of <hash, preservedBoneIndex>
						// Build as skeleton is built from preserved transforms
						// Apply DNA to skeleton
						// 
						// UMATransform has a bind matrix
						// bindRemaps[] = dictionary lookup from current slot skinning (+ inherited ?)
						// bindTransforms[] = skinningBind.inv * skeleton bone to bone Matrix * slot umaTransofrm bind
						// SMR binds and bones built from dictionary order

						// Rebind vertex to new bones
						int boneIndex = boneSrc.boneIndex0;
						float boneWeight = boneSrc.weight0;
						Vector3 vertexDst = Vector3.zero;
						boneWeights[destIndex].boneIndex0 = rebindIndices[boneIndex];
						boneWeights[destIndex].weight0 = boneWeight;
						vertexDst += rebindMatrices[boneIndex].MultiplyPoint(vertexSrc) * boneWeight;
						boneIndex = boneSrc.boneIndex1;
						boneWeight = boneSrc.weight1;
						boneWeights[destIndex].boneIndex1 = rebindIndices[boneIndex];
						boneWeights[destIndex].weight1 = boneWeight;
						vertexDst += rebindMatrices[boneIndex].MultiplyPoint(vertexSrc) * boneWeight;
						boneIndex = boneSrc.boneIndex2;
						boneWeight = boneSrc.weight2;
						boneWeights[destIndex].boneIndex2 = rebindIndices[boneIndex];
						boneWeights[destIndex].weight2 = boneWeight;
						vertexDst += rebindMatrices[boneIndex].MultiplyPoint(vertexSrc) * boneWeight;
						boneIndex = boneSrc.boneIndex3;
						boneWeight = boneSrc.weight3;
						boneWeights[destIndex].boneIndex3 = rebindIndices[boneIndex];
						boneWeights[destIndex].weight3 = boneWeight;
						vertexDst += rebindMatrices[boneIndex].MultiplyPoint(vertexSrc) * boneWeight;
						vertices[destIndex++] = vertexDst;
					}
				}

				if (has_normals)
				{
					if (source.meshData.normals != null && source.meshData.normals.Length > 0)
					{
						//Array.Copy(source.meshData.normals, 0, normals, vertexIndex, sourceVertexCount);
						destIndex = vertexIndex;
						for (int i = 0; i < sourceVertexCount; i++)
						{
							if (vertexRemaps[i] >= 0)
							{
								normals[destIndex++] = source.meshData.normals[i];
							}
						}
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
						//Array.Copy(source.meshData.tangents, 0, tangents, vertexIndex, sourceVertexCount);
						destIndex = vertexIndex;
						for (int i = 0; i < sourceVertexCount; i++)
						{
							if (vertexRemaps[i] >= 0)
							{
								tangents[destIndex++] = source.meshData.tangents[i];
							}
						}
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
						//Array.Copy(source.meshData.uv, 0, uv, vertexIndex, sourceVertexCount);
						destIndex = vertexIndex;
						for (int i = 0; i < sourceVertexCount; i++)
						{
							if (vertexRemaps[i] >= 0)
							{
								uv[destIndex++] = source.meshData.uv[i];
							}
						}
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
						//Array.Copy(source.meshData.uv2, 0, uv2, vertexIndex, sourceVertexCount);
						destIndex = vertexIndex;
						for (int i = 0; i < sourceVertexCount; i++)
						{
							if (vertexRemaps[i] >= 0)
							{
								uv2[destIndex++] = source.meshData.uv2[i];
							}
						}
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
						//Array.Copy(source.meshData.uv3, 0, uv3, vertexIndex, sourceVertexCount);
						destIndex = vertexIndex;
						for (int i = 0; i < sourceVertexCount; i++)
						{
							if (vertexRemaps[i] >= 0)
							{
								uv3[destIndex++] = source.meshData.uv3[i];
							}
						}
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
						//Array.Copy(source.meshData.uv4, 0, uv4, vertexIndex, sourceVertexCount);
						destIndex = vertexIndex;
						for (int i = 0; i < sourceVertexCount; i++)
						{
							if (vertexRemaps[i] >= 0)
							{
								uv4[destIndex++] = source.meshData.uv4[i];
							}
						}
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
						//Array.Copy(source.meshData.colors32, 0, colors32, vertexIndex, sourceVertexCount);
						destIndex = vertexIndex;
						for (int i = 0; i < sourceVertexCount; i++)
						{
							if (vertexRemaps[i] >= 0)
							{
								colors32[destIndex++] = source.meshData.colors32[i];
							}
						}
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
						for (int shapeIndex = 0; shapeIndex < source.meshData.blendShapes.Length; shapeIndex++)
						{
                            #region BlendShape Baking
                            if(blendShapeSettings.bakeBlendShapes != null || blendShapeSettings.bakeBlendShapes.Count == 0)
                            {
                                // If there are names in the bakeBlendShape dictionary and we find them in the meshData blendshape list, then lets bake them instead of adding them.
                                UMABlendShape currentShape = source.meshData.blendShapes[shapeIndex];
                                if( blendShapeSettings.bakeBlendShapes.ContainsKey(currentShape.shapeName))
                                {
                                    float weight = blendShapeSettings.bakeBlendShapes[currentShape.shapeName] * 100.0f;
									if (weight <= 0f) continue; // Baking in nothing, so skip it entirely

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
									prevIndex = frameIndex - 1;

                                    // The blend shape frames lerp between the deltas of two adjacent frames.
									int vertIndex = vertexIndex;
									for (int bakeIndex = 0; bakeIndex < currentShape.frames[frameIndex].deltaVertices.Length; bakeIndex++, vertIndex++)
                                    {
                                        // Add the current frame's deltas
										vertices[vertIndex] += currentShape.frames[frameIndex].deltaVertices[bakeIndex] * frameWeight;
                                        // Add in the previous frame's deltas
										if (doLerp)
											vertices[vertIndex] += currentShape.frames[prevIndex].deltaVertices[bakeIndex] * prevWeight;
                                    }

                                    if (has_normals)
                                    {
										vertIndex = vertexIndex;
										for (int bakeIndex = 0; bakeIndex < currentShape.frames[frameIndex].deltaNormals.Length; bakeIndex++, vertIndex++)
                                        {
											normals[vertIndex] += currentShape.frames[frameIndex].deltaNormals[bakeIndex] * frameWeight;
											if (doLerp)
												normals[vertIndex] += currentShape.frames[prevIndex].deltaNormals[bakeIndex] * prevWeight;
                                        }
                                    }

                                    if (has_tangents)
                                    {
										vertIndex = vertexIndex;
										for (int bakeIndex = 0; bakeIndex < currentShape.frames[frameIndex].deltaTangents.Length; bakeIndex++, vertIndex++)
                                        {
											tangents[vertIndex] += (Vector4)currentShape.frames[frameIndex].deltaTangents[bakeIndex] * frameWeight;
											if (doLerp)
												tangents[vertIndex] += (Vector4)currentShape.frames[prevIndex].deltaTangents[bakeIndex] * prevWeight;    
                                        }
                                    }
                                    continue; // If we bake then don't perform the rest of this interation of the loop.
                                }                                
                            }
                            #endregion

							bool nameAlreadyExists = false;
							int i = 0;
							//Probably this would be better with a dictionary
							for (i = 0; i < blendShapeIndex; i++) 
							{
								if (blendShapes[i].shapeName == source.meshData.blendShapes[shapeIndex].shapeName) 
								{
									nameAlreadyExists = true;
									break;
								}
							}

							if (nameAlreadyExists)//Lets add the vertices data to the existing blendShape
							{ 
								if (blendShapes[i].frames.Length != source.meshData.blendShapes[shapeIndex].frames.Length) 
								{
									Debug.LogError("SkinnedMeshCombiner: mesh blendShape frame counts don't match!");
									break;
								}
								for (int frameIndex = 0; frameIndex < source.meshData.blendShapes[shapeIndex].frames.Length; frameIndex++) {
									Array.Copy(source.meshData.blendShapes[shapeIndex].frames[frameIndex].deltaVertices, 0, blendShapes[i].frames[frameIndex].deltaVertices, vertexIndex, sourceVertexCount);
									Array.Copy(source.meshData.blendShapes[shapeIndex].frames[frameIndex].deltaNormals, 0, blendShapes[i].frames[frameIndex].deltaNormals, vertexIndex, sourceVertexCount);
									Array.Copy(source.meshData.blendShapes[shapeIndex].frames[frameIndex].deltaTangents, 0, blendShapes[i].frames[frameIndex].deltaTangents, vertexIndex, sourceVertexCount);
								}
							} 
							else
							{
								blendShapes[blendShapeIndex] = new UMABlendShape();
								blendShapes[blendShapeIndex].shapeName = source.meshData.blendShapes[shapeIndex].shapeName;
								blendShapes[blendShapeIndex].frames = new UMABlendFrame[source.meshData.blendShapes[shapeIndex].frames.Length];

								for (int frameIndex = 0; frameIndex < source.meshData.blendShapes[shapeIndex].frames.Length; frameIndex++) {
									blendShapes[blendShapeIndex].frames[frameIndex] = new UMABlendFrame(vertexCount); 
									blendShapes[blendShapeIndex].frames[frameIndex].frameWeight = source.meshData.blendShapes[shapeIndex].frames[frameIndex].frameWeight;
									Array.Copy(source.meshData.blendShapes[shapeIndex].frames[frameIndex].deltaVertices, 0, blendShapes[blendShapeIndex].frames[frameIndex].deltaVertices, vertexIndex, sourceVertexCount);
									Array.Copy(source.meshData.blendShapes[shapeIndex].frames[frameIndex].deltaNormals, 0, blendShapes[blendShapeIndex].frames[frameIndex].deltaNormals, vertexIndex, sourceVertexCount);
									Array.Copy(source.meshData.blendShapes[shapeIndex].frames[frameIndex].deltaTangents, 0, blendShapes[blendShapeIndex].frames[frameIndex].deltaTangents, vertexIndex, sourceVertexCount);
								}
								blendShapeIndex++;
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
							MaskedCopyIntArrayAdd(subTriangles, 0, submeshTriangles[destMesh], subMeshTriangleLength[destMesh], triangleLength, vertexIndex, source.triangleMask[i] );
							subMeshTriangleLength[destMesh] += (triangleLength - (UMAUtils.GetCardinality(source.triangleMask[i])*3));
						}
					}
				}

				vertexIndex += sourceVertexCount;
			}

			if (vertexCount != vertexIndex)
			{
				Debug.LogError("Combined vertices size didn't match precomputed value!");
			}

			// fill in new values.
			target.vertexCount = vertexCount;
			target.vertices = vertices;
			target.boneWeights = boneWeights;
			target.normals = normals;
			target.tangents = tangents;
			target.uv = uv;
			target.uv2 = uv2;
			target.uv3 = uv3;
			target.uv4 = uv4;
			target.colors32 = colors32;

			if (has_blendShapes)
			{
				target.blendShapes = blendShapes;
			}

			if (has_clothSkinning)
			{
				Array.Resize(ref clothSkinning, clothVertices.Count);
			}
			target.clothSkinning = clothSkinning;

			target.subMeshCount = subMeshCount;
			target.submeshes = new SubMeshTriangles[subMeshCount];
			for (int i = 0; i < subMeshCount; i++)
			{
				target.submeshes[i].triangles = submeshTriangles[i];
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
			HashSet<string> blendShapeNames = new HashSet<string> (); //Hash to find all the unique blendshape names

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
				if (source.meshData.clothSkinningSerialized != null && source.meshData.clothSkinningSerialized.Length != 0)	meshComponents |= MeshComponents.has_clothSkinning;

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
			if ((mask.Count*3) != source.Length || (mask.Count*3) != count)
			{
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
