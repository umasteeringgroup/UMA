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


		public void RetrieveDataFromUnityMesh(SkinnedMeshRenderer skinnedMeshRenderer)
		{
			var sharedMesh = skinnedMeshRenderer.sharedMesh;
			bindPoses = sharedMesh.bindposes;
			boneWeights = UMABoneWeight.Convert(sharedMesh.boneWeights);
			bones = skinnedMeshRenderer.bones;
			vertices = sharedMesh.vertices;
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
				Debug.LogError("Cannot apply changes to prefab");
			}
			if (UnityEditor.AssetDatabase.IsSubAsset(mesh))
			{
				Debug.LogError("Cannot apply changes to asset mesh");
			}
#endif
			mesh.subMeshCount = 1;
			mesh.triangles = new int[0];

			mesh.vertices = vertices;
			mesh.boneWeights = unityBoneWeights;
			mesh.bindposes = bindPoses;
			mesh.normals = normals;
			mesh.tangents = tangents;
			mesh.uv = uv;
			mesh.uv2 = uv2;
#if !UNITY_4_6
			mesh.uv3 = uv3;
			mesh.uv4 = uv4;
#endif
			mesh.colors32 = colors32;

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
	}
}
