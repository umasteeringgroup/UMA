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
	public class UMAMeshData
	{
		public Matrix4x4[] bindPoses;
		public BoneWeight[] boneWeights;
		public Vector3[] vertices;
		public Vector3[] normals;
		public Vector4[] tangents;
		public Color32[] colors32;
		public Vector2[] uv;
		public Vector2[] uv2;
#if !UNITY_4
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
			boneWeights = sharedMesh.boneWeights;
			bones = skinnedMeshRenderer.bones;
			vertices = sharedMesh.vertices;
			normals = sharedMesh.normals;
			tangents = sharedMesh.tangents;
			colors32 = sharedMesh.colors32;
			uv = sharedMesh.uv;
			uv2 = sharedMesh.uv2;
#if !UNITY_4
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
			mesh.boneWeights = boneWeights;
			mesh.bindposes = bindPoses;
			mesh.normals = normals;
			mesh.tangents = tangents;
			mesh.uv = uv;
			mesh.uv2 = uv2;
#if !UNITY_4
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
