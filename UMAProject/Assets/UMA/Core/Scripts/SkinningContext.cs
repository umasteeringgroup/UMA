using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Text;

namespace UMA
{
#if true
	/// <summary>
	/// Utility class for merging multiple skinned meshes.
	/// </summary>
	public struct SkinningContext
	{
		internal SkinningSolver solver;
		public Matrix4x4[] targetEffectivePoses;
		public Vector3[] vertices;
		public Vector3[] normals;
		public Vector4[] tangents;
		public int[] targetBoneIndices;
		public Matrix4x4[] resolvedBoneMatrixes;

		// BoneWeight1 support: per-vertex variable bone count
		public BoneWeight1[] sourceManagedBoneWeights;
		public byte[] sourceManagedBonesPerVertex;
		public int[] sourceWeightOffsets;
		public int sourceBoneWeightBaseOffset;
		public List<BoneWeight1> targetManagedBoneWeights;
		public List<byte> targetManagedBonesPerVertex;
		public int targetVertexIndexOffset;

		public void ProcessBoneWeight(ref BoneWeight boneWeight, ref Vector3 vertex, ref Vector3 normal, ref Vector4 tangent)
		{
			var origBone = boneWeight.boneIndex0;
			var newBone = targetBoneIndices[origBone];
			solver.Add(ref resolvedBoneMatrixes[origBone], ref vertex, ref normal, ref tangent, boneWeight.weight0, newBone);

			if (boneWeight.weight1 > 0)
			{
				origBone = boneWeight.boneIndex1;
				newBone = targetBoneIndices[origBone];
				solver.Add(ref resolvedBoneMatrixes[origBone], ref vertex, ref normal, ref tangent, boneWeight.weight1, newBone);
				//if (float.IsNaN(solver.globalVertex.x))
				//	Debug.Log("Cry2");
				if (boneWeight.weight2 > 0)
				{
					origBone = boneWeight.boneIndex2;
					newBone = targetBoneIndices[origBone];
					solver.Add(ref resolvedBoneMatrixes[origBone], ref vertex, ref normal, ref tangent, boneWeight.weight2, newBone);
					//if (float.IsNaN(solver.globalVertex.x))
					//	Debug.Log("Cry3");

					if (boneWeight.weight3 > 0)
					{
						origBone = boneWeight.boneIndex3;
						newBone = targetBoneIndices[origBone];
						solver.Add(ref resolvedBoneMatrixes[origBone], ref vertex, ref normal, ref tangent, boneWeight.weight3, newBone);
						//if (float.IsNaN(solver.globalVertex.x))
						//	Debug.Log("Cry4");
					}
				}
			}
		}

		public void Initialize()
		{
			solver = new SkinningSolver();
			solver.Allocate();
		}

		public void ProcessVertex(ref BoneWeight umaBoneWeight, int sourceVertexIndex, ref BoneWeight boneWeight, ref Vector3 vertex, ref Vector3 normal, ref Vector4 tangent)
		{
			solver.Reset();
			ProcessBoneWeight(ref umaBoneWeight, ref vertices[sourceVertexIndex], ref normals[sourceVertexIndex], ref tangents[sourceVertexIndex]);
			solver.SortWeightsDescending();
			solver.UpdateBoneWeights(ref boneWeight);
			solver.SkinVertex(targetEffectivePoses, ref vertex);
			solver.SkinNormal(targetEffectivePoses, ref normal);
			solver.SkinTangent(targetEffectivePoses, ref tangent);
		}

		/// <summary>
		/// Process a vertex using variable-count BoneWeight1 source data, writing retargeted
		/// BoneWeight1 entries to the target managed lists.
		/// sourceVertexIndex: index in context.vertices/normals/tangents for geometry data.
		/// sourceBoneWeightIndex: index in sourceManagedBonesPerVertex for bone weight data.
		/// targetIndex: index in target arrays for output.
		/// </summary>
		public void ProcessVertexManaged(int sourceVertexIndex, int sourceBoneWeightIndex, int targetIndex, ref Vector3 outVertex, ref Vector3 outNormal, ref Vector4 outTangent)
		{
			solver.Reset();

			// Use precomputed prefix-sum offset (Phase 1 optimization)
			int weightOffset = sourceWeightOffsets != null ? sourceWeightOffsets[sourceBoneWeightIndex] : 0;

			byte boneCount = sourceManagedBonesPerVertex[sourceBoneWeightIndex];
			for (int b = 0; b < boneCount; b++)
			{
				var bw = sourceManagedBoneWeights[weightOffset + b];
				if (bw.weight <= 0f) continue;

				int origBone = bw.boneIndex;
				int newBone = targetBoneIndices[origBone];
				solver.Add(ref resolvedBoneMatrixes[origBone], ref vertices[sourceVertexIndex],
					ref normals[sourceVertexIndex], ref tangents[sourceVertexIndex], bw.weight, newBone);
			}

			// Unity requires bone weights in descending order
			solver.SortWeightsDescending();

			// Append retargeted bone weights to target managed list
			byte written = solver.UpdateBoneWeights(targetManagedBoneWeights);
			targetManagedBonesPerVertex[targetIndex] = written;

			solver.SkinVertex(targetEffectivePoses, ref outVertex);
			solver.SkinNormal(targetEffectivePoses, ref outNormal);
			solver.SkinTangent(targetEffectivePoses, ref outTangent);
		}
		/// <summary>
		/// Process a blendshape delta vertex using managed BoneWeight1 data without writing bone weights.
		/// </summary>
		public void ProcessBlendShapeVertexManaged(int sourceVertexIndex, int sourceBoneWeightIndex, ref Vector3 outDeltaVertex, ref Vector3 outDeltaNormal, ref Vector4 outDeltaTangent)
		{
			solver.Reset();

			// Use precomputed prefix-sum offset (Phase 1 optimization)
			int weightOffset = sourceWeightOffsets != null ? sourceWeightOffsets[sourceBoneWeightIndex] : 0;

			byte boneCount = sourceManagedBonesPerVertex[sourceBoneWeightIndex];
			for (int b = 0; b < boneCount; b++)
			{
				var bw = sourceManagedBoneWeights[weightOffset + b];
				if (bw.weight <= 0f) continue;

				int origBone = bw.boneIndex;
				int newBone = targetBoneIndices[origBone];
				solver.Add(ref resolvedBoneMatrixes[origBone], ref vertices[sourceVertexIndex],
					ref normals[sourceVertexIndex], ref tangents[sourceVertexIndex], bw.weight, newBone);
			}

			// Unity requires bone weights in descending order
			solver.SortWeightsDescending();

			solver.SkinVertex(targetEffectivePoses, ref outDeltaVertex);
			solver.SkinNormal(targetEffectivePoses, ref outDeltaNormal);
			solver.SkinTangent(targetEffectivePoses, ref outDeltaTangent);
		}
	}
#endif
}