using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Text;

namespace UMA
{
	/// <summary>
	/// Utility class for merging multiple skinned meshes.
	/// </summary>
	public static class SkinnedMeshCombinerRetargeting
	{
		/// <summary>
		/// Container for source mesh data.
		/// </summary>
		public class CombineInstance
		{
			public UMAMeshData meshData;
			public int[] targetSubmeshIndices;
			public Matrix4x4[] resolvedBoneMatrixes;
			public int[] targetBoneIndices;
			public System.Int32[][] triangleOcclusion;
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
			has_uv4 = 64
		}

		/// <summary>
		/// Combines a set of meshes into the target mesh.
		/// </summary>
		/// <param name="target">Target.</param>
		/// <param name="sources">Sources.</param>
		public static void CombineMeshes(MeshBuilder target, CombineInstance[] sources, Matrix4x4[] inverseTargetBoneMatrixes, BlendShapeSettings blendShapeSettings, bool uniformTargetPoses = false)
		{
			if (blendShapeSettings == null)
				blendShapeSettings = new BlendShapeSettings();

			target.Reset();
			int vertexCount = 0;

			target.PrepareSubMeshCount(FindTargetSubMeshCount(sources));

			Dictionary<string, int> blendShapeNames = null;
			bool bakeBlendShapes;
			AnalyzeSources(sources, target, blendShapeSettings, ref blendShapeNames, out bakeBlendShapes);
			target.PrepareBuffers();

			int vertexIndex = 0;
			var context = new SkinningContext();
			context.targetEffectivePoses = inverseTargetBoneMatrixes;
			context.uniformTargetPoses = uniformTargetPoses;
			var blendShapeContext = new SkinningContext();
			blendShapeContext.targetEffectivePoses = inverseTargetBoneMatrixes;
			blendShapeContext.uniformTargetPoses = uniformTargetPoses;


			foreach (var source in sources)
			{
				vertexCount = source.meshData.vertices.Length;
				context.targetBoneIndices = source.targetBoneIndices;

				context.resolvedBoneMatrixes = source.resolvedBoneMatrixes;

				// Detect managed (BoneWeight1) bone weight data; convert legacy if needed
				// Use vertices.Length as the authoritative vertex count — meshData.vertexCount can be stale/zero
				int srcVertexCount = source.meshData.vertices != null ? source.meshData.vertices.Length : source.meshData.vertexCount;
				bool useManagedWeights = source.meshData.ManagedBoneWeights != null
					&& source.meshData.ManagedBonesPerVertex != null
					&& source.meshData.ManagedBoneWeights.Length > 0
					&& source.meshData.ManagedBonesPerVertex.Length == srcVertexCount;

				if (!useManagedWeights && source.meshData.boneWeights != null && source.meshData.boneWeights.Length > 0)
			{
				// Convert legacy UMABoneWeight to BoneWeight1 format on-the-fly
				ConvertLegacyBoneWeights(source.meshData);
				// Re-validate after conversion — lengths must match vertex count
				useManagedWeights = source.meshData.ManagedBoneWeights != null
					&& source.meshData.ManagedBonesPerVertex != null
					&& source.meshData.ManagedBoneWeights.Length > 0
					&& source.meshData.ManagedBonesPerVertex.Length == srcVertexCount;
			}

			// Fallback: if neither format is available, try loading/converting via the asset API.
			// Use srcVertexCount (not meshData.vertexCount which can be stale/zero) for the
			// entry guard and the post-conversion validation.
			if (!useManagedWeights && srcVertexCount > 0 && source.meshData.boneWeights != null && source.meshData.boneWeights.Length > 0)
			{
				source.meshData.LoadBoneWeights();
				useManagedWeights = source.meshData.ManagedBoneWeights != null
					&& source.meshData.ManagedBonesPerVertex != null
					&& source.meshData.ManagedBoneWeights.Length > 0
					&& source.meshData.ManagedBonesPerVertex.Length == srcVertexCount;
			}

	
			if (useManagedWeights)
			{
				context.sourceManagedBoneWeights = source.meshData.ManagedBoneWeights;
				context.sourceManagedBonesPerVertex = source.meshData.ManagedBonesPerVertex;				context.sourceWeightOffsets = BuildWeightOffsets(source.meshData.ManagedBonesPerVertex);					context.sourceBoneWeightBaseOffset = vertexIndex;
				context.targetManagedBoneWeights = target._boneWeightsManagedList;
				context.targetManagedBonesPerVertex = target._bonesPerVertexManagedList;
				context.targetVertexIndexOffset = vertexIndex;
				// Set up blendShapeContext with same source weights
				blendShapeContext.sourceManagedBoneWeights = context.sourceManagedBoneWeights;
				blendShapeContext.sourceManagedBonesPerVertex = context.sourceManagedBonesPerVertex;			blendShapeContext.sourceWeightOffsets = context.sourceWeightOffsets;				blendShapeContext.targetBoneIndices = context.targetBoneIndices;
				blendShapeContext.resolvedBoneMatrixes = context.resolvedBoneMatrixes;
			}

				bool isBakingBlendShapesOnThisSource = false;
				if (bakeBlendShapes && HasContent(source.meshData.blendShapes))
				{
					// we're baking some blendshapes and there are blendshapes on this source. Let's see if we need the complex logic
					for(int i = 0; i < source.meshData.blendShapes.Length; i++)
					{
						var blendShape = source.meshData.blendShapes[i];
						if (blendShapeNames[blendShape.shapeName] == -1 && blendShapeSettings.blendShapes[blendShape.shapeName].value >= 0f)
						{
							isBakingBlendShapesOnThisSource = true;
							break;
						}
					}

					if (isBakingBlendShapesOnThisSource)
					{
						// first lets copy the raw skinned vertex into the destination buffer
						for (int i = 0; i < vertexCount; i++)
						{
							target.vertices[vertexIndex + i] = source.meshData.vertices[i];
							if (target.has_normals && HasContent(source.meshData.normals))
							{
								target.normals[vertexIndex + i] = source.meshData.normals[i];
							}
							if (target.has_tangents && HasContent(source.meshData.tangents))
							{
								target.tangents[vertexIndex + i] = source.meshData.tangents[i];
							}
						}

						// then loop over the blendshapes and add the baked ones
						for (int i = 0; i < source.meshData.blendShapes.Length; i++)
						{
							var currentShape = source.meshData.blendShapes[i];
							var blendShapeData = blendShapeSettings.blendShapes[currentShape.shapeName];
							if (blendShapeNames[currentShape.shapeName] >= 0 || blendShapeData.value <= 0f)
							{
								continue; // this blendshape isn't being baked (or is baked at zero).
							}
							var weight = blendShapeData.value * 100f;
							if (weight <= 0) continue;

							// Let's find the frame this weight is in
							int frameIndex;
							for (frameIndex = 0; frameIndex < currentShape.frames.Length; frameIndex++)
							{
								if (currentShape.frames[frameIndex].frameWeight >= weight)
									break;
							}

							// Let's calculate the weight for the frame we're in
							float frameWeight = 1f;
							float prevWeight = 0f;
							bool doLerp = false;
							UMABlendFrame prevFrame = null;
							UMABlendFrame frame = null;
							// Weight is higher than the last frame, shape is over 100%
							if (frameIndex >= currentShape.frames.Length)
							{
								frameIndex = currentShape.frames.Length - 1;
								frame = currentShape.frames[frameIndex];
								frameWeight = (weight / frame.frameWeight);
							}
							else if (frameIndex > 0)
							{
								doLerp = true;
								prevWeight = currentShape.frames[frameIndex - 1].frameWeight;
								frame = currentShape.frames[frameIndex];
								frameWeight = ((weight - prevWeight) / (frame.frameWeight - prevWeight));
								prevWeight = 1f - frameWeight;
								prevFrame = currentShape.frames[frameIndex-1];
							}
							else
							{
								frameWeight = (weight / currentShape.frames[frameIndex].frameWeight);
								frame = currentShape.frames[frameIndex];
							}

							// The blend shape frames lerp between the deltas of two adjacent frames.
							int vertIndex = vertexIndex;
							for (int bakeIndex = 0; bakeIndex < frame.deltaVertices.Length; bakeIndex++, vertIndex++)
							{
								// Add the current frame's deltas
								target.vertices[vertIndex] += frame.deltaVertices[bakeIndex] * frameWeight;
								// Add in the previous frame's deltas
								if (doLerp)
									target.vertices[vertIndex] += prevFrame.deltaVertices[bakeIndex] * prevWeight;
							}

							if (target.has_normals && HasContent(frame.deltaNormals))
							{
								vertIndex = vertexIndex;
								for (int bakeIndex = 0; bakeIndex < frame.deltaNormals.Length; bakeIndex++, vertIndex++)
								{
									target.normals[vertIndex] += frame.deltaNormals[bakeIndex] * frameWeight;
									if (doLerp)
										target.normals[vertIndex] += prevFrame.deltaNormals[bakeIndex] * prevWeight;
								}
							}

							if (target.has_tangents && HasContent(frame.deltaTangents))
							{
								vertIndex = vertexIndex;
								for (int bakeIndex = 0; bakeIndex < frame.deltaTangents.Length; bakeIndex++, vertIndex++)
								{
									target.tangents[vertIndex] += (Vector4)frame.deltaTangents[bakeIndex] * frameWeight;
									if (doLerp)
										target.tangents[vertIndex] += (Vector4)prevFrame.deltaTangents[bakeIndex] * prevWeight;
								}
							}
						}

						// setup the context to work on the target arrays and process them
						context.vertices = target.vertices;
						context.normals = target.normals;
						context.tangents = target.tangents;
						if (useManagedWeights)
						{
							for (int i = 0; i < vertexCount; i++)
							{
								// Geometry is at target[vertexIndex+i]; bone weight source is at i
								context.ProcessVertexManaged(vertexIndex + i, i, vertexIndex + i,
									ref target.vertices[vertexIndex + i], ref target.normals[vertexIndex + i], ref target.tangents[vertexIndex + i]);
							}
						}
					}
				}

				if (!isBakingBlendShapesOnThisSource)
				{
					context.vertices = source.meshData.vertices;
					context.normals = source.meshData.normals;
					context.tangents = source.meshData.tangents;
					if (useManagedWeights)
					{
						for (int i = 0; i < vertexCount; i++)
						{
							// Geometry at source[i], bone weight at source[i], output to target[vertexIndex+i]
							context.ProcessVertexManaged(i, i, vertexIndex + i,
								ref target.vertices[vertexIndex + i], ref target.normals[vertexIndex + i], ref target.tangents[vertexIndex + i]);
						}
					}
					else
					{
						Debug.LogWarning($"[CombineMeshes] useManagedWeights=false for slot '{source.meshData.SlotName}' — vertices will not be skinned, bone weights not written");
					}
				}

				if (target.has_blendShapes)
				{
					//Debug.Log("Target BlendShapes");
					Vector3 Dummy = new Vector3();
					blendShapeContext.targetBoneIndices = source.targetBoneIndices;
					blendShapeContext.resolvedBoneMatrixes = source.resolvedBoneMatrixes;

					UMABlendShape[] sourceBlendShapes = source.meshData.blendShapes;
					for(int i = 0; i < sourceBlendShapes.Length; i++)
					{
						var blendShape = sourceBlendShapes[i];
						var blendShapeIndex = blendShapeNames[blendShape.shapeName];
						if (blendShapeIndex == -1)
						{
							continue; // this blendshape is being baked.
						}

						var targetBlendShape = target.blendShapes[blendShapeIndex];
						if (targetBlendShape == null)
						{
							targetBlendShape = new UMABlendShape();
							target.blendShapes[blendShapeIndex] = targetBlendShape;
							targetBlendShape.shapeName = blendShape.shapeName;
							targetBlendShape.frames = new UMABlendFrame[blendShape.frames.Length];
							for(int j = 0; j < blendShape.frames.Length; j++)
							{
								targetBlendShape.frames[j] = new UMABlendFrame(target.vertexCount, HasContent(blendShape.frames[0].deltaNormals), HasContent(blendShape.frames[0].deltaTangents));
								targetBlendShape.frames[j].frameWeight = blendShape.frames[j].frameWeight;
							}
						}
						else
						{
							targetBlendShape.shapeName = blendShape.shapeName;
							if (targetBlendShape.frames.Length != blendShape.frames.Length)
								targetBlendShape.frames = new UMABlendFrame[blendShape.frames.Length];
							for (int j = 0; j < blendShape.frames.Length; j++)
							{
								if (targetBlendShape.frames[j] == null)
								{
									targetBlendShape.frames[j] = new UMABlendFrame(target.vertexCount, HasContent(blendShape.frames[0].deltaNormals), HasContent(blendShape.frames[0].deltaTangents));
								}
								else
								{
									ResetBlendShape(targetBlendShape.frames[j], target.vertexCount, HasContent(blendShape.frames[0].deltaNormals), HasContent(blendShape.frames[0].deltaTangents));
								}
								targetBlendShape.frames[j].frameWeight = blendShape.frames[j].frameWeight;
							}
						}

						if (blendShape.frames.Length != targetBlendShape.frames.Length)
						{
							Debug.LogError("SkinnedMeshCombinerRetargeting: mesh blendShape frame counts don't match!");
							break;
						}
						for (int frameIndex = 0; frameIndex < blendShape.frames.Length; frameIndex++)
						{
							var frame = blendShape.frames[frameIndex];
							var targetFrame = targetBlendShape.frames[frameIndex];
							blendShapeContext.vertices = frame.deltaVertices;
							blendShapeContext.normals = HasContent(frame.deltaNormals) ? frame.deltaNormals : frame.deltaVertices;
							// Build Vector4 tangents from Vector3 deltas for the solver
							var srcTangents = new Vector4[frame.deltaVertices.Length];
							var srcT = HasContent(frame.deltaTangents) ? frame.deltaTangents : frame.deltaVertices;
							for (int t = 0; t < srcTangents.Length; t++)
								srcTangents[t] = new Vector4(srcT[t].x, srcT[t].y, srcT[t].z, 1f);
							blendShapeContext.tangents = srcTangents;

							if (useManagedWeights)
							{
								for (int j = 0; j < vertexCount; j++)
								{
									var outTan = new Vector4();
									blendShapeContext.ProcessBlendShapeVertexManaged(j, j,
										ref targetFrame.deltaVertices[vertexIndex + j],
										ref targetFrame.deltaNormals[vertexIndex + j],
										ref outTan);
									targetFrame.deltaTangents[vertexIndex + j] = new Vector3(outTan.x, outTan.y, outTan.z);
								}
							}
						}
					}
				}


				if (target.has_uv)
				{
					if (HasContent(source.meshData.uv))
					{
						Array.Copy(source.meshData.uv, 0, target.uv, vertexIndex, vertexCount);
					}
					else
					{
						FillArray(target.uv, vertexIndex, vertexCount, Vector4.zero);
					}
				}
				if (target.has_uv2)
				{
					if (HasContent(source.meshData.uv2))
					{
						Array.Copy(source.meshData.uv2, 0, target.uv2, vertexIndex, vertexCount);
					}
					else
					{
						FillArray(target.uv2, vertexIndex, vertexCount, Vector4.zero);
					}
				}
				if (target.has_uv3)
				{
					if (HasContent(source.meshData.uv3))
					{
						Array.Copy(source.meshData.uv3, 0, target.uv3, vertexIndex, vertexCount);
					}
					else
					{
						FillArray(target.uv3, vertexIndex, vertexCount, Vector4.zero);
					}
				}
				if (target.has_uv4)
				{
					if (HasContent(source.meshData.uv4))
					{
						Array.Copy(source.meshData.uv4, 0, target.uv4, vertexIndex, vertexCount);
					}
					else
					{
						FillArray(target.uv4, vertexIndex, vertexCount, Vector4.zero);
					}
				}
				if (target.has_colors32)
				{
					if (HasContent(source.meshData.colors32))
					{
						Array.Copy(source.meshData.colors32, 0, target.colors32, vertexIndex, vertexCount);
					}
					else
					{
						Color32 white32 = Color.white;
						FillArray(target.colors32, vertexIndex, vertexCount, white32);
					}
				}

				for (int i = 0; i < source.meshData.subMeshCount; i++)
				{
					if (source.targetSubmeshIndices[i] >= 0)
					{
						int[] subTriangles = source.meshData.submeshes[i].GetBaseTriangles();
						int triangleLength = subTriangles.Length;
						int destMesh = source.targetSubmeshIndices[i];

						var targetSubMesh = target.submeshes[destMesh];
						if (source.triangleOcclusion != null && source.triangleOcclusion[i] != null)
						{
							var triangles = CopyIntArrayAdd(subTriangles, 0, targetSubMesh.triangles, targetSubMesh.triangleCount, triangleLength, vertexIndex, source.triangleOcclusion[i]);
							targetSubMesh.triangleCount += triangles*3;
						}
						else
						{ 
							CopyIntArrayAdd(subTriangles, 0, targetSubMesh.triangles, targetSubMesh.triangleCount, triangleLength, vertexIndex);
							targetSubMesh.triangleCount += triangleLength;
						}
					}
				}

				vertexIndex += vertexCount;
			}
		}

		/// <summary>
		/// Convert legacy UMABoneWeight data to BoneWeight1 (ManagedBoneWeights) format.
		/// </summary>
		/// <summary>
	/// Build a prefix-sum array of bone weight offsets for O(1) lookup per vertex.
	/// weightOffsets[i] = sum of bonesPerVertex[0..i-1], weightOffsets[0] = 0.
	/// </summary>
	private static int[] BuildWeightOffsets(byte[] bonesPerVertex)
	{
		if (bonesPerVertex == null || bonesPerVertex.Length == 0)
			return null;

		var offsets = new int[bonesPerVertex.Length];
		int running = 0;
		for (int i = 0; i < bonesPerVertex.Length; i++)
		{
			offsets[i] = running;
			running += bonesPerVertex[i];
		}
		return offsets;
	}

	private static void ConvertLegacyBoneWeights(UMAMeshData meshData)
		{
			if (meshData.boneWeights == null || meshData.boneWeights.Length == 0)
				return;

			var legacyWeights = meshData.boneWeights;
			int vertexCount = legacyWeights.Length;

			var weightsList = new List<BoneWeight1>(vertexCount * 4);
			var bpvList = new byte[vertexCount];

			for (int i = 0; i < vertexCount; i++)
			{
				var bw = legacyWeights[i];
				float totWeight = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
				byte count = 0;

				if (bw.weight0 > 0f)
				{
					weightsList.Add(new BoneWeight1 { boneIndex = bw.boneIndex0, weight = bw.weight0 / totWeight });
					count++;
				}
				if (bw.weight1 > 0f)
				{
					weightsList.Add(new BoneWeight1 { boneIndex = bw.boneIndex1, weight = bw.weight1 / totWeight });
					count++;
				}
				if (bw.weight2 > 0f)
				{
					weightsList.Add(new BoneWeight1 { boneIndex = bw.boneIndex2, weight = bw.weight2 / totWeight });
					count++;
				}
				if (bw.weight3 > 0f)
				{
					weightsList.Add(new BoneWeight1 { boneIndex = bw.boneIndex3, weight = bw.weight3 / totWeight });
					count++;
				}
				bpvList[i] = count;
			}

			meshData.ManagedBoneWeights = weightsList.ToArray();
			meshData.ManagedBonesPerVertex = bpvList;
			meshData.vertexCount = vertexCount;
		}

		private static void ResetBlendShape(UMABlendFrame blendFrame, int vertexCount, bool hasNormals = true, bool hasTangents = true)
		{
			blendFrame.frameWeight = 100.0f;
			SetArrayLength(ref blendFrame.deltaVertices, vertexCount);
			SetArrayLength(ref blendFrame.deltaNormals, hasNormals ? vertexCount : 0);
			SetArrayLength(ref blendFrame.deltaTangents, hasTangents ? vertexCount : 0);
		}

		private static bool HasContent<T>(T[] array)
		{
			return array != null && array.Length > 0;
		}

		public static void MergeSortedTransforms(UMATransform[] mergedTransforms, ref int transformCount, UMATransform[] umaTransforms)
		{
			int newBones = 0;
			int pos1 = 0;
			int pos2 = 0;
			int len2 = umaTransforms.Length;

			while(pos1 < transformCount && pos2 < len2 )
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
			pos1 = transformCount - 1;
			pos2 = len2 - 1;

			transformCount += newBones;

			int dest = transformCount-1;
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

		private static void AnalyzeSources(CombineInstance[] sources, MeshBuilder target, BlendShapeSettings blendShapeSettings, ref Dictionary<string, int> blendShapeNames, out bool blendshapeBaking)
		{
			var ignoreBlendShapes = blendShapeSettings.ignoreBlendShapes || blendShapeSettings.blendShapes.Count == 0;
			int blendShapeIndex = 0;
			blendshapeBaking = false;

			if (!ignoreBlendShapes)
			{
				blendShapeNames = new Dictionary<string, int>(); //Dictionary to find all the unique blendshape names
				foreach (var blendshapeData in blendShapeSettings.blendShapes)
				{
					if (blendshapeData.Value.isBaked)
					{
						blendshapeBaking = true;
						blendShapeNames[blendshapeData.Key] = -1;
					}
				}
			}

			foreach (var source in sources)
			{
				target.vertexCount += source.meshData.vertices.Length;
				target.has_normals |= HasContent(source.meshData.normals);
				target.has_tangents |= HasContent(source.meshData.tangents);
				target.has_uv |= HasContent(source.meshData.uv);
				target.has_uv2 |= HasContent(source.meshData.uv2);
				target.has_uv3 |= HasContent(source.meshData.uv3);
				target.has_uv4 |= HasContent(source.meshData.uv4);
				target.has_colors32 |= HasContent(source.meshData.colors32);

				//If we find a blendshape on this mesh then lets add it to the blendShapeNames hash to get all the unique names
				if (!ignoreBlendShapes && HasContent(source.meshData.blendShapes))
				{
					for (int shapeIndex = 0; shapeIndex < source.meshData.blendShapes.Length; shapeIndex++)
					{
						var blendShape = source.meshData.blendShapes[shapeIndex];
						if (!blendShapeNames.ContainsKey(blendShape.shapeName))
						{
							blendShapeNames.Add(blendShape.shapeName, blendShapeIndex++);
						}
					}
				}

				for (int i = 0; i < source.meshData.subMeshCount; i++)
				{
					if (source.targetSubmeshIndices[i] >= 0)
					{
						target.submeshes[source.targetSubmeshIndices[i]].triangleCount += source.meshData.submeshes[i].GetBaseTriangles().Length;
					}
				}
			}

			target.blendShapeCount = blendShapeIndex;
			target.has_blendShapes = blendShapeIndex > 0;
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

		private static void BuildBoneWeights(UMABoneWeight[] source, int sourceIndex, BoneWeight[] dest, int destIndex, int count, int[] bones, Matrix4x4[] bindPoses, Dictionary<int, BoneIndexEntry> bonesCollection, List<Matrix4x4> bindPosesList, List<int> bonesList)
		{
			int[] boneMapping = new int[bones.Length];
			for (int i = 0; i < boneMapping.Length; i++)
			{
				boneMapping[i] = TranslateBoneIndex(i, bones, bindPoses, bonesCollection, bindPosesList, bonesList);
			}

			while (count-- > 0)
			{
				TranslateBoneWeight(ref source[sourceIndex++], ref dest[destIndex++], boneMapping);
			}
		}

		private static void TranslateBoneWeight(ref UMABoneWeight source, ref BoneWeight dest, int[] boneMapping)
		{
			dest.weight0 = source.weight0;
			dest.weight1 = source.weight1;
			dest.weight2 = source.weight2;
			dest.weight3 = source.weight3;

			dest.boneIndex0 = boneMapping[source.boneIndex0];
			dest.boneIndex1 = boneMapping[source.boneIndex1];
			dest.boneIndex2 = boneMapping[source.boneIndex2];
			dest.boneIndex3 = boneMapping[source.boneIndex3];
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

		private static int CopyIntArrayAdd(int[] source, int sourceIndex, int[] dest, int destIndex, int count, int add, Int32[] mask)
		{
			int copied = 0;
			UInt32 maskValue;
			UInt32 maskIterator;
			int maskIndex = 0;
			count += sourceIndex;
			while (sourceIndex < count)
			{
				maskValue = (System.UInt32)mask[maskIndex++];
				maskIterator = 1;
				while (maskIterator != 0)
				{
					if ((maskValue & maskIterator) != 0)
					{
						dest[destIndex++] = source[sourceIndex++] + add;
						dest[destIndex++] = source[sourceIndex++] + add;
						dest[destIndex++] = source[sourceIndex++] + add;
						copied++;
					}
					else
					{
						sourceIndex += 3;
					}
					maskIterator = maskIterator << 1;
				}
			}
			return copied;
		}

		private static void CopyIntArrayAdd(int[] source, int sourceIndex, int[] dest, int destIndex, int count, int add)
		{
			for (int i = 0; i < count; i++)
			{
				dest[destIndex++] = source[sourceIndex++] + add;
			}
		}

		private static T[] EnsureArrayLength<T>(T[] oldArray, int newLength)
		{
			if (newLength <= 0)
				return null;

			if (oldArray != null && oldArray.Length >= newLength)
				return oldArray;

			return new T[newLength];
		}

		private static void SetArrayLength<T>(ref T[] array, int newLength)
		{
			if (newLength <= 0)
			{
				array = null;
				return;
			}

			if (array == null || array.Length == newLength)
				array = new T[newLength];
		}
	}
}
