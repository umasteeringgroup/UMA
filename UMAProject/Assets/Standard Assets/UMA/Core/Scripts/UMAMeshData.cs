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
	public struct SubMeshTriangles
	{
		public int[] triangles;
	}

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
			var res = new UMABoneWeight[boneWeights.Length];
			for (int i = 0; i < boneWeights.Length; i++)
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
				return true;
			}

			Debug.LogWarning("Unable to claim UMAMeshData global buffers!");
#endif
			return false;
		}

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

		public void RetrieveDataFromUnityMesh(SkinnedMeshRenderer skinnedMeshRenderer)
		{
			var sharedMesh = skinnedMeshRenderer.sharedMesh;
			bindPoses = sharedMesh.bindposes;
			boneWeights = UMABoneWeight.Convert(sharedMesh.boneWeights);
			bones = skinnedMeshRenderer.bones;
			vertices = sharedMesh.vertices;
			vertexCount = vertices.Length;
			normals = sharedMesh.normals;
			tangents = sharedMesh.tangents;
			colors32 = sharedMesh.colors32;
			uv = sharedMesh.uv;
			uv2 = sharedMesh.uv2;
#if !UNITY_4_6
			uv3 = sharedMesh.uv4;
			uv3 = sharedMesh.uv4;
#endif
			subMeshCount = sharedMesh.subMeshCount;
			submeshes = new SubMeshTriangles[subMeshCount];
			rootBone = skinnedMeshRenderer.rootBone;
			for (int i = 0; i < subMeshCount; i++)
			{
				submeshes[i].triangles = sharedMesh.GetTriangles(i);
			}

			ComputeBoneNameHashes();
		}

		public void ApplyDataToUnityMesh(SkinnedMeshRenderer renderer)
		{
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
				mesh.boneWeights = unityBoneWeights;
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
			renderer.bones = bones;
			renderer.sharedMesh = mesh;
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

		private void ComputeBoneNameHashes()
		{
			boneNameHashes = new int[bones.Length];
			for (int i = 0; i < bones.Length; i++)
			{
				boneNameHashes[i] = UMASkeleton.StringToHash(bones[i].name);
			}
		}

		public void DebugDrawSkeleton(Color color, float duration)
		{
			for (int i = 0; i < bones.Length; i++)
			{
				var bone = bones[i];
				for (int j = 0; j < bone.childCount; j++)
				{
					Debug.DrawLine(bone.position, bone.GetChild(j).position, color, duration, false);
				}
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
		#endregion


	}
}
