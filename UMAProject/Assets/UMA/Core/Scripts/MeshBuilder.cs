using UnityEngine;
using System.Collections.Generic;
using System;
using Unity.Collections;

namespace UMA
{
	public class MeshBuilder
	{
		public class SubMeshTriangles
		{
			public int triangleCount;
			public int[] triangles;
			public List<int> _triangles;

			public void Reset()
			{
				triangleCount = 0;
			}
			public void PrepareBuffer()
			{
				ListHelper<int>.AllocateArray(ref _triangles, out triangles, triangleCount);
				triangleCount = 0;
			}
		}

		public List<UMABlendShape> blendShapes;

		public Matrix4x4[] bindPoses;
		public BoneWeight[] boneWeights;

		// BoneWeight1 support: variable bone count per vertex (modern Unity format)
		public BoneWeight1[] boneWeightsManaged;
		public byte[] bonesPerVertexManaged;

		public Vector3[] vertices;
		public Vector3[] normals;
		public Vector4[] tangents;
		public Color32[] colors32;
		public Vector2[] uv;
		public Vector2[] uv2;
		public Vector2[] uv3;
		public Vector2[] uv4;

		public List<SubMeshTriangles> submeshes;
		public int[] boneNameHashes;
		public int subMeshCount;
		public int vertexCount;
		public int bonesCount;
		public int blendShapeCount;

		public List<Vector3> _vertices;
		private List<Vector3> _normals;
		private List<Vector4> _tangents;
		private List<Color32> _colors32;
		private List<Vector2> _uv;
		private List<Vector2> _uv2;
		private List<Vector2> _uv3;
		private List<Vector2> _uv4;
		private List<int> _boneNameHashes;
		private List<Matrix4x4> _bindPoses;
		private Dictionary<int, BoneWeight[]> _boneWeights;

		// BoneWeight1 buffer pooling (internal so SkinnedMeshCombinerRetargeting can access)
		internal List<BoneWeight1> _boneWeightsManagedList;
		internal List<byte> _bonesPerVertexManagedList;
		public bool cacheBoneWeights;

		public bool has_normals;
		public bool has_tangents;
		public bool has_colors32;
		public bool has_uv;
		public bool has_uv2;
		public bool has_uv3;
		public bool has_uv4;
		public bool has_blendShapes;

		public int CachedBoneWeights { get { return _boneWeights != null ? _boneWeights.Count : 0; } }
		public int CachedBoneWeightEntries
		{
			get
			{
				if (_boneWeights == null) return 0;
				int bytes = 0;
				foreach (var entry in _boneWeights)
				{
					bytes += entry.Key;
				}
				return bytes;
			}
		}

		public void Reset()
		{
			vertexCount = 0;
			has_normals = false;
			has_tangents = false;
			has_colors32 = false;
			has_uv = false;
			has_uv2 = false;
			has_uv3 = false;
			has_uv4 = false;
			has_blendShapes = false;
		}

		public void PrepareSubMeshCount(int subMeshCount)
		{
			this.subMeshCount = subMeshCount;
			if (submeshes == null)
				submeshes = new List<SubMeshTriangles>(subMeshCount);
			for (int i = 0; i < submeshes.Count; i++)
				submeshes[i].Reset();
			while (submeshes.Count < subMeshCount)
				submeshes.Add(new SubMeshTriangles());
		}

		public void PrepareBuffers()
		{
			for(int i = 0; i < subMeshCount; i++)
			{
				submeshes[i].PrepareBuffer();
			}

			if (_boneWeights != null && _boneWeights.TryGetValue(vertexCount, out boneWeights))
			{
				_boneWeights.Remove(vertexCount);
			}
			else
			{ 
				boneWeights = new BoneWeight[vertexCount];
			}

			// BoneWeight1: allocate managed list for variable-count weights and per-vertex counts
			if (_boneWeightsManagedList == null)
				_boneWeightsManagedList = new List<BoneWeight1>(vertexCount * 4);
			else
				_boneWeightsManagedList.Clear();
			if (_bonesPerVertexManagedList == null)
				_bonesPerVertexManagedList = new List<byte>(vertexCount);
			else
				_bonesPerVertexManagedList.Clear();
			// Fast pre-allocate: AddRange uses internal Array.Copy, not per-element Add
			_bonesPerVertexManagedList.AddRange(new byte[vertexCount]);

			ListHelper<Vector3>.AllocateArray(ref _vertices, out vertices, vertexCount);
			if (has_normals)
				ListHelper<Vector3>.AllocateArray(ref _normals, out normals, vertexCount);
			if (has_tangents)
				ListHelper<Vector4>.AllocateArray(ref _tangents, out tangents, vertexCount);
			if (has_colors32)
				ListHelper<Color32>.AllocateArray(ref _colors32, out colors32, vertexCount);
			if (has_uv)
				ListHelper<Vector2>.AllocateArray(ref _uv, out uv, vertexCount);
			if (has_uv2)
				ListHelper<Vector2>.AllocateArray(ref _uv2, out uv2, vertexCount);
			if (has_uv3)
				ListHelper<Vector2>.AllocateArray(ref _uv3, out uv3, vertexCount);
			if (has_uv4)
				ListHelper<Vector2>.AllocateArray(ref _uv4, out uv4, vertexCount);
			if (has_blendShapes)
				ListHelper<UMABlendShape>.AllocateList(ref blendShapes, blendShapeCount);
		}

		public void ReleaseBuffers()
		{
			if (cacheBoneWeights)
			{
				if (_boneWeights == null)
					_boneWeights = new Dictionary<int, BoneWeight[]>(50);

				_boneWeights[vertexCount] = boneWeights;
			}

			boneWeights = null;

			// Finalize BoneWeight1 arrays from managed lists
			if (_boneWeightsManagedList != null && _boneWeightsManagedList.Count > 0)
			{
				boneWeightsManaged = _boneWeightsManagedList.ToArray();
				bonesPerVertexManaged = _bonesPerVertexManagedList.ToArray();
			}
			else
			{
				boneWeightsManaged = null;
				bonesPerVertexManaged = null;
			}
		}

		/// <summary>
		/// Applies the data to a Unity mesh.
		/// </summary>
		/// <param name="renderer">Target renderer.</param>
		/// <param name="skeleton">Skeleton.</param>
		public void ApplyDataToUnityMesh(SkinnedMeshRenderer renderer, UMASkeleton skeleton)
		{
			Mesh mesh = renderer.sharedMesh;
#if UNITY_EDITOR
			if (UnityEditor.AssetDatabase.IsSubAsset(mesh))
			{
				Debug.LogError("Cannot apply changes to asset mesh!");
			}
#endif
			mesh.subMeshCount = 1;
			mesh.triangles = new int[0];

			mesh.SetVertices(_vertices);
			//Debug.Log(_vertices.Count);

			// Use BoneWeight1 if available, otherwise fall back to legacy BoneWeight
			if (boneWeightsManaged != null && bonesPerVertexManaged != null && boneWeightsManaged.Length > 0)
			{
				var nativeBPV = new Unity.Collections.NativeArray<byte>(bonesPerVertexManaged, Unity.Collections.Allocator.Temp);
				var nativeBW = new Unity.Collections.NativeArray<BoneWeight1>(boneWeightsManaged, Unity.Collections.Allocator.Temp);
				mesh.SetBoneWeights(nativeBPV, nativeBW);
				nativeBPV.Dispose();
				nativeBW.Dispose();
			}
			else
			{
				mesh.boneWeights = boneWeights;
			}
			if (has_normals)
				mesh.SetNormals(_normals);
			if (has_tangents)
				mesh.SetTangents(_tangents);
			if (has_colors32)
				mesh.SetColors(_colors32);
			if (has_uv)
				mesh.SetUVs(0, _uv);
			if (has_uv2)
				mesh.SetUVs(1, _uv2);
			if (has_uv3)
				mesh.SetUVs(2, _uv3);
			if (has_uv4)
				mesh.SetUVs(3, _uv4);

			mesh.subMeshCount = subMeshCount;
			for (int i = 0; i < subMeshCount; i++)
			{
				mesh.SetTriangles(submeshes[i]._triangles, i);
			}

			mesh.bindposes = bindPoses;

			//Apply the blendshape data from the slot asset back to the combined UMA unity mesh.
			#region Blendshape support, copied from UMAMeshData
			mesh.ClearBlendShapes();
			if (blendShapes != null && blendShapeCount > 0)
			{
				for (int shapeIndex = 0; shapeIndex < blendShapes.Count; shapeIndex++)
				{
					var blendShape = blendShapes[shapeIndex];
					if (blendShape == null)
					{
						// Skip baked blendshapes
						break;
					}

					for (int frameIndex = 0; frameIndex < blendShape.frames.Length; frameIndex++)
					{
						var frame = blendShape.frames[frameIndex];
						//There might be an extreme edge case where someone has the same named blendshapes on different meshes that end up on different renderers.
						string name = blendShape.shapeName;

						float frameWeight = frame.frameWeight;
						Vector3[] deltaVertices = frame.deltaVertices;
						Vector3[] deltaNormals = frame.deltaNormals;
						Vector3[] deltaTangents = frame.deltaTangents;

						if (UMABlendFrame.isAllZero(deltaNormals))
							deltaNormals = null;

						if (UMABlendFrame.isAllZero(deltaTangents))
							deltaTangents = null;

						mesh.AddBlendShapeFrame(name, frameWeight, deltaVertices, deltaNormals, deltaTangents);
					}
				}
			}
			#endregion

			mesh.RecalculateBounds();
			skeleton.EnsureBoneHierarchy();
			renderer.bones = skeleton.HashesToTransforms(_boneNameHashes);
			renderer.sharedMesh = mesh;
		}

		private bool IsVector3Bad(Vector3 vertex)
		{
			return IsFloatBad(vertex.x) || IsFloatBad(vertex.y) || IsFloatBad(vertex.z);
		}

		private bool IsFloatBad(float value)
		{
			return value == float.NaN || value == float.NegativeInfinity || value == float.PositiveInfinity;
		}

		public void PrepareBones(int count)
		{
			bonesCount = count;
			ListHelper<int>.AllocateArray(ref _boneNameHashes, out boneNameHashes, count);
			ListHelper<Matrix4x4>.AllocateArray(ref _bindPoses, out bindPoses, count);
		}
	}
}