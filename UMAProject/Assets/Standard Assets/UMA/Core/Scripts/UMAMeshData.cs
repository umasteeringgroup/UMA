// We can dramatically reduce garbage by using shared buffers
// on desktop platforms and dynamically adjusting the
// size which the arrays appear to be to C# code
// See: http://feedback.unity3d.com/suggestions/allow-mesh-data-to-have-a-length
#if !UNITY_STANDALONE
#undef USE_UNSAFE_CODE
#endif 

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	[Serializable]
	/// <summary>
	/// UMA version of Unity mesh triangle data.
	/// </summary>
	public struct SubMeshTriangles
	{
		public int[] triangles;
	}

	/// <summary>
	/// UMA version of Unity transform data.
	/// </summary>
	[Serializable]
	public class UMATransform
	{
		public Vector3 position;
		public Quaternion rotation;
		public Vector3 scale;
		public string name;
		public int hash;
		public int parent;

		public UMATransform()
		{
		}

		public UMATransform(Transform transform, int nameHash, int parentHash)
		{
			this.hash = nameHash;
			this.parent = parentHash;
			position = transform.localPosition;
			rotation = transform.localRotation;
			scale = transform.localScale;
			name = transform.name;
		}

		/// <summary>
		/// Get a copy that is not part of an asset, to allow user manipulation.
		/// </summary>
		/// <returns>An identical copy</returns>
		public UMATransform Duplicate()
		{
			return new UMATransform() { hash = hash, name = name, parent = parent, position = position, rotation = rotation, scale = scale };
		}

		public static UMATransformComparer TransformComparer = new UMATransformComparer();
		public class UMATransformComparer : IComparer<UMATransform>
		{
			#region IComparer<UMATransform> Members

			public int Compare(UMATransform x, UMATransform y)
			{
				return x.hash < y.hash ? -1 : x.hash > y.hash ? 1 : 0;
			}

			#endregion
		}

		public void Assign(UMATransform other)
		{
			hash = other.hash;
			name = other.name;
			parent = other.parent;
			position = other.position;
			rotation = other.rotation;
			scale = other.scale;
		}
	}

	/// <summary>
	/// UMA version of Unity mesh bone weight.
	/// </summary>
	[Serializable]
	public struct UMABoneWeight
	{
		public int boneIndex0;
		public int boneIndex1;
		public int boneIndex2;
		public int boneIndex3;
		public float weight0;
		public float weight1;
		public float weight2;
		public float weight3;
		public void Set(int index, int bone, float weight)
		{
			switch(index)
			{
			case 0:
				boneIndex0 = bone;
				weight0 = weight;
				break;
			case 1:
				boneIndex1 = bone;
				weight1 = weight;
				break;
			case 2:
				boneIndex2 = bone;
				weight2 = weight;
				break;
			case 3:
				boneIndex3 = bone;
				weight3 = weight;
				break;
			default:
				throw new NotImplementedException();
			}
		}
		public float GetWeight(int index)
		{
			switch(index)
			{
			case 0:
				return weight0;
			case 1:
				return weight1;
			case 2:
				return weight2;
			case 3:
				return weight3;
			default:
				throw new NotImplementedException();
			}
		}
		public int GetBoneIndex(int index)
		{
			switch(index)
			{
			case 0:
				return boneIndex0;
			case 1:
				return boneIndex1;
			case 2:
				return boneIndex2;
			case 3:
				return boneIndex3;
			default:
				throw new NotImplementedException();
			}
		}		
		public static implicit operator UMABoneWeight(BoneWeight sourceWeight)
		{
			var res = new UMABoneWeight();
			res.boneIndex0 = sourceWeight.boneIndex0;
			res.boneIndex1 = sourceWeight.boneIndex1;
			res.boneIndex2 = sourceWeight.boneIndex2;
			res.boneIndex3 = sourceWeight.boneIndex3;
			res.weight0 = sourceWeight.weight0;
			res.weight1 = sourceWeight.weight1;
			res.weight2 = sourceWeight.weight2;
			res.weight3 = sourceWeight.weight3;
			return res;
		}
		public static implicit operator BoneWeight(UMABoneWeight sourceWeight)
		{
			var res = new BoneWeight();
			res.boneIndex0 = sourceWeight.boneIndex0;
			res.boneIndex1 = sourceWeight.boneIndex1;
			res.boneIndex2 = sourceWeight.boneIndex2;
			res.boneIndex3 = sourceWeight.boneIndex3;
			res.weight0 = sourceWeight.weight0;
			res.weight1 = sourceWeight.weight1;
			res.weight2 = sourceWeight.weight2;
			res.weight3 = sourceWeight.weight3;
			return res;
		}

		public static UMABoneWeight[] Convert(BoneWeight[] boneWeights)
		{
			if(boneWeights == null) return null;
			var res = new UMABoneWeight[boneWeights.Length];
			for (int i = 0; i < boneWeights.Length; i++)
			{
				res[i] = boneWeights[i];
			}
			return res;
		}
		public static UMABoneWeight[] Convert(List<BoneWeight> boneWeights)
		{
			if(boneWeights == null) return null;
			var res = new UMABoneWeight[boneWeights.Count];
			for (int i = 0; i < boneWeights.Count; i++)
			{
				res[i] = boneWeights[i];
			}
			return res;
		}
		public static BoneWeight[] Convert(UMABoneWeight[] boneWeights)
		{
			var res = new BoneWeight[boneWeights.Length];
			for (int i = 0; i < boneWeights.Length; i++)
			{
				res[i] = boneWeights[i];
			}
			return res;
		}
	}

	/// <summary>
	/// UMA version of Unity mesh data.
	/// </summary>
	[Serializable]
	public class UMAMeshData
	{
		public Matrix4x4[] bindPoses;
		public UMABoneWeight[] boneWeights;
		public BoneWeight[] unityBoneWeights;
		public Vector3[] vertices;
		public Vector3[] normals;
		public Vector4[] tangents;
		public Color32[] colors32;
		public Vector2[] uv;
		public Vector2[] uv2;
#if !UNITY_4_6
		public Vector2[] uv3;
		public Vector2[] uv4;
#endif
		public SubMeshTriangles[] submeshes;
		public Transform[] bones;
		public Transform rootBone;
		public UMATransform[] umaBones;
		public int umaBoneCount;
		public int rootBoneHash;
		public int[] boneNameHashes;
		public int subMeshCount;
		public int vertexCount;

		private bool OwnSharedBuffers()
		{
#if USE_UNSAFE_CODE
			return (this == bufferLockOwner);
#else
			return false;
#endif
		}

		/// <summary>
		/// Claims the static buffers.
		/// </summary>
		/// <returns><c>true</c>, if shared buffers was claimed, <c>false</c> otherwise.</returns>
		public bool ClaimSharedBuffers()
		{
#if USE_UNSAFE_CODE
			if (bufferLockOwner == null)
			{
				bufferLockOwner = this;
				vertices = gVertices;
				boneWeights = null;
				unityBoneWeights = gBoneWeights;
				normals = gNormals;
				tangents = gTangents;
				uv = gUV;
				uv2 = gUV2;
#if !UNITY_4_6
				uv3 = gUV3;
				uv4 = gUV4;
#endif
				colors32 = gColors32;
				boneHierarchy = gUMABones;
				return true;
			}

			Debug.LogWarning("Unable to claim UMAMeshData global buffers!");
#endif
			return false;
		}

		/// <summary>
		/// Releases the static buffers.
		/// </summary>
		public void ReleaseSharedBuffers()
		{
#if USE_UNSAFE_CODE
			if (bufferLockOwner == this)
			{
				vertices = null;
				boneWeights = null;
				unityBoneWeights = null;
				normals = null;
				tangents = null;
				uv = null;
				uv2 = null;
#if !UNITY_4_6
				uv3 = null;
				uv4 = null;
#endif
				colors32 = null;
				bufferLockOwner = null;
			}
#endif
		}

		public void PrepareVertexBuffers(int size)
		{
			vertexCount = size;
			boneWeights = new UMABoneWeight[size];
			vertices = new Vector3[size];
			normals = new Vector3[size];
			tangents = new Vector4[size];
			colors32 = new Color32[size];
			uv = new Vector2[size];
			uv2 = new Vector2[size];
#if !UNITY_4_6
			uv3 = new Vector2[size];
			uv4 = new Vector2[size];
#endif
		}
		
		/// <summary>
		/// Initialize UMA mesh data from Unity mesh.
		/// </summary>
		/// <param name="renderer">Source renderer.</param>
		public void RetrieveDataFromUnityMesh(SkinnedMeshRenderer renderer)
		{
			RetrieveDataFromUnityMesh(renderer.sharedMesh);

			UpdateBones(renderer.rootBone, renderer.bones);
		}

		
		/// <summary>
		/// Initialize UMA mesh data from Unity mesh.
		/// </summary>
		/// <param name="renderer">Source renderer.</param>
		public void RetrieveDataFromUnityMesh(Mesh sharedMesh)
		{
			bindPoses = sharedMesh.bindposes;
			boneWeights = UMABoneWeight.Convert(sharedMesh.boneWeights);
			vertices = sharedMesh.vertices;
			vertexCount = vertices.Length;
			normals = sharedMesh.normals;
			tangents = sharedMesh.tangents;
			colors32 = sharedMesh.colors32;
			uv = sharedMesh.uv;
			uv2 = sharedMesh.uv2;
#if !UNITY_4_6
			uv3 = sharedMesh.uv3;
			uv4 = sharedMesh.uv4;
#endif
			subMeshCount = sharedMesh.subMeshCount;
			submeshes = new SubMeshTriangles[subMeshCount];
			for (int i = 0; i < subMeshCount; i++)
			{
				submeshes[i].triangles = sharedMesh.GetTriangles(i);
			}
		}

		/// <summary>
		/// Validates the skinned transform hierarchy.
		/// </summary>
		/// <param name="rootBone">Root transform.</param>
		/// <param name="bones">Transforms.</param>
		public void UpdateBones(Transform rootBone, Transform[] bones)
		{
			var storedRootBone = rootBone;
			while (rootBone.name != "Global")
			{
				rootBone = rootBone.parent;
				if (rootBone == null)
				{
					rootBone = storedRootBone;
					break;
				}
			}
			
			var requiredBones = new Dictionary<Transform, UMATransform>();
			foreach (var bone in bones)
			{
				if (requiredBones.ContainsKey(bone)) continue;
				var boneIterator = bone.parent;
				var boneIteratorChild = bone;
				var boneHash = UMAUtils.StringToHash(boneIterator.name);
				var childHash = UMAUtils.StringToHash(boneIteratorChild.name);
				while (boneIteratorChild != rootBone)
				{
					requiredBones.Add(boneIteratorChild, new UMATransform(boneIteratorChild, childHash, boneHash));
					if (requiredBones.ContainsKey(boneIterator)) break;
					boneIteratorChild = boneIterator;
					boneIterator = boneIterator.parent;
					childHash = boneHash;
					boneHash = UMAUtils.StringToHash(boneIterator.name);
				}
			}

			var sortedBones = new List<UMATransform>(requiredBones.Values);
			sortedBones.Sort(UMATransform.TransformComparer);
			umaBones = sortedBones.ToArray();
			umaBoneCount = umaBones.Length;

			rootBoneHash = UMAUtils.StringToHash(rootBone.name);
			ComputeBoneNameHashes(bones);
			this.rootBone = rootBone;
			this.bones = bones;
		}

		/// <summary>
		/// Applies the data to a Unity mesh.
		/// </summary>
		/// <param name="renderer">Target renderer.</param>
		/// <param name="skeleton">Skeleton.</param>
		public void ApplyDataToUnityMesh(SkinnedMeshRenderer renderer, UMASkeleton skeleton)
		{
			CreateTransforms(skeleton);

			Mesh mesh = renderer.sharedMesh;
#if UNITY_EDITOR
			if (UnityEditor.PrefabUtility.IsComponentAddedToPrefabInstance(renderer))
			{
				Debug.LogError("Cannot apply changes to prefab!");
			}
			if (UnityEditor.AssetDatabase.IsSubAsset(mesh))
			{
				Debug.LogError("Cannot apply changes to asset mesh!");
			}
#endif
			mesh.subMeshCount = 1;
			mesh.triangles = new int[0];

			if (OwnSharedBuffers())
			{
				ApplySharedBuffers(mesh);
			}
			else
			{
				mesh.vertices = vertices;
				mesh.boneWeights = unityBoneWeights != null ? unityBoneWeights : UMABoneWeight.Convert(boneWeights);
				mesh.normals = normals;
				mesh.tangents = tangents;
				mesh.uv = uv;
				mesh.uv2 = uv2;
#if !UNITY_4_6
				mesh.uv3 = uv3;
				mesh.uv4 = uv4;
#endif
				mesh.colors32 = colors32;
			}
			mesh.bindposes = bindPoses;

			var subMeshCount = submeshes.Length;
			mesh.subMeshCount = subMeshCount;
			for (int i = 0; i < subMeshCount; i++)
			{
				mesh.SetTriangles(submeshes[i].triangles, i);
			}

			mesh.RecalculateBounds();
			renderer.bones = bones != null ? bones : skeleton.HashesToTransforms(boneNameHashes);
			renderer.sharedMesh = mesh;
			renderer.rootBone = rootBone;
		}

		/// <summary>
		/// Applies the data to a Unity mesh.
		/// </summary>
		/// <param name="renderer">Target renderer.</param>
		/// <param name="skeleton">Skeleton.</param>
		public void CopyDataToUnityMesh(SkinnedMeshRenderer renderer)
		{
			Mesh mesh = renderer.sharedMesh;
			mesh.subMeshCount = 1;
			mesh.triangles = new int[0];
			mesh.vertices = vertices;
			mesh.boneWeights = UMABoneWeight.Convert(boneWeights);
			mesh.normals = normals;
			mesh.tangents = tangents;
			mesh.uv = uv;
			mesh.uv2 = uv2;
#if !UNITY_4_6
			mesh.uv3 = uv3;
			mesh.uv4 = uv4;
#endif
			mesh.colors32 = colors32;
			mesh.bindposes = bindPoses;
			
			var subMeshCount = submeshes.Length;
			mesh.subMeshCount = subMeshCount;
			for (int i = 0; i < subMeshCount; i++)
			{
				mesh.SetTriangles(submeshes[i].triangles, i);
			}
			
			renderer.bones = bones;
			renderer.rootBone = rootBone;
			
			mesh.RecalculateBounds();
			renderer.sharedMesh = mesh;
		}

		private void CreateTransforms(UMASkeleton skeleton)
		{
			for(int i = 0; i < umaBoneCount; i++ )
			{
				skeleton.EnsureBone(umaBones[i]);
			}
			skeleton.EnsureBoneHierarchy();
		}

		private void ApplySharedBuffers(Mesh mesh)
		{
#if USE_UNSAFE_CODE
			unsafe
			{
				UIntPtr* lengthPtr;
				fixed (void* pVertices = gVertices) 
				{ 
					lengthPtr = (UIntPtr*)pVertices - 1; 
					try 
					{ 
						*lengthPtr = (UIntPtr)vertexCount; 
						mesh.vertices = gVertices; 
					} 
					finally 
					{ 
						*lengthPtr = (UIntPtr)MAX_VERTEX_COUNT; 
					} 
				} 
				fixed (void* pBoneWeights = gBoneWeights) 
				{ 
					lengthPtr = (UIntPtr*)pBoneWeights - 1; 
					try 
					{ 
						*lengthPtr = (UIntPtr)vertexCount; 
						mesh.boneWeights = gBoneWeights; 
					} 
					finally 
					{ 
						*lengthPtr = (UIntPtr)MAX_VERTEX_COUNT; 
					} 
				}
				if (normals != null)
				{
					fixed (void* pNormals = gNormals) 
					{ 
						lengthPtr = (UIntPtr*)pNormals - 1; 
						try 
						{ 
							*lengthPtr = (UIntPtr)vertexCount; 
							mesh.normals = gNormals; 
						} 
						finally 
						{ 
							*lengthPtr = (UIntPtr)MAX_VERTEX_COUNT; 
						} 
					}
				}
				if (tangents != null)
				{
					fixed (void* pTangents = gTangents) 
					{ 
						lengthPtr = (UIntPtr*)pTangents - 1; 
						try 
						{ 
							*lengthPtr = (UIntPtr)vertexCount; 
							mesh.tangents = gTangents; 
						} 
						finally 
						{ 
							*lengthPtr = (UIntPtr)MAX_VERTEX_COUNT; 
						} 
					}
				}
				if (uv != null)
				{
					fixed (void* pUV = gUV) 
					{ 
						lengthPtr = (UIntPtr*)pUV - 1; 
						try 
						{ 
							*lengthPtr = (UIntPtr)vertexCount; 
							mesh.uv = gUV; 
						} 
						finally 
						{ 
							*lengthPtr = (UIntPtr)MAX_VERTEX_COUNT; 
						} 
					}
				}
				if (uv2 != null)
				{
					fixed (void* pUV2 = gUV2) 
					{ 
						lengthPtr = (UIntPtr*)pUV2 - 1; 
						try 
						{ 
							*lengthPtr = (UIntPtr)vertexCount; 
							mesh.uv2 = gUV2; 
						} 
						finally 
						{ 
							*lengthPtr = (UIntPtr)MAX_VERTEX_COUNT; 
						} 
					}
				}
#if !UNITY_4_6
				if (uv3 != null)
				{
					fixed (void* pUV3 = gUV3) 
					{ 
						lengthPtr = (UIntPtr*)pUV3 - 1; 
						try 
						{ 
							*lengthPtr = (UIntPtr)vertexCount; 
							mesh.uv3 = gUV3; 
						} 
						finally 
						{ 
							*lengthPtr = (UIntPtr)MAX_VERTEX_COUNT; 
						} 
					}
				}
				if (uv4 != null)
				{
					fixed (void* pUV4 = gUV4) 
					{ 
						lengthPtr = (UIntPtr*)pUV4 - 1; 
						try 
						{ 
							*lengthPtr = (UIntPtr)vertexCount; 
							mesh.uv4 = gUV4; 
						} 
						finally 
						{ 
							*lengthPtr = (UIntPtr)MAX_VERTEX_COUNT; 
						} 
					}
				}
#endif
				if (colors32 != null)
				{
					fixed (void* pColors32 = gColors32) 
					{ 
						lengthPtr = (UIntPtr*)pColors32 - 1; 
						try 
						{ 
							*lengthPtr = (UIntPtr)vertexCount; 
							mesh.colors32 = gColors32; 
						} 
						finally 
						{ 
							*lengthPtr = (UIntPtr)MAX_VERTEX_COUNT; 
						} 
					}
				}
			}
#endif
		}

		private void ComputeBoneNameHashes(Transform[] bones)
		{
			boneNameHashes = new int[bones.Length];
			for (int i = 0; i < bones.Length; i++)
			{
				boneNameHashes[i] = UMAUtils.StringToHash(bones[i].name);
			}
		}

#if USE_UNSAFE_CODE
		private static UMAMeshData bufferLockOwner = null;
		const int MAX_VERTEX_COUNT = 65534;
		static Vector3[] gVertices = new Vector3[MAX_VERTEX_COUNT];
		static BoneWeight[] gBoneWeights = new BoneWeight[MAX_VERTEX_COUNT];
		static Vector3[] gNormals = new Vector3[MAX_VERTEX_COUNT];
		static Vector4[] gTangents = new Vector4[MAX_VERTEX_COUNT];
		static Vector2[] gUV = new Vector2[MAX_VERTEX_COUNT];
		static Vector2[] gUV2 = new Vector2[MAX_VERTEX_COUNT];
#if !UNITY_4_6
		static Vector2[] gUV3 = new Vector2[MAX_VERTEX_COUNT];
		static Vector2[] gUV4 = new Vector2[MAX_VERTEX_COUNT];
#endif
		static Color32[] gColors32 = new Color32[MAX_VERTEX_COUNT];
		static UMATransform gUMABones = new UMATransform[MAX_VERTEX_COUNT];
#endif


		#region operator ==, != and similar HACKS, seriously.....
		public static implicit operator bool(UMAMeshData obj)
		{
			return ((System.Object)obj) != null && obj.vertexCount != 0;
		}

		public bool Equals(UMAMeshData other)
		{
			return (this == other);
		}
		public override bool Equals(object other)
		{
			return Equals(other as UMAMeshData);
		}

		public static bool operator ==(UMAMeshData overlay, UMAMeshData obj)
		{
			if (overlay)
			{
				if (obj)
				{
					return System.Object.ReferenceEquals(overlay, obj);
				}
				return false;
			}
			return !((bool)obj);
		}

		public static bool operator !=(UMAMeshData overlay, UMAMeshData obj)
		{
			if (overlay)
			{
				if (obj)
				{
					return !System.Object.ReferenceEquals(overlay, obj);
				}
				return true;
			}
			return ((bool)obj);
		}
		public override int GetHashCode()
		{
			return base.GetHashCode();
		}
		#endregion

		internal void ReSortUMABones()
		{
			var newList = new List<UMATransform>(umaBones);
			newList.Sort(UMATransform.TransformComparer);
			umaBones = newList.ToArray();
		}
	}
}
