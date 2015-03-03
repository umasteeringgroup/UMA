using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Text;

namespace UMA
{
	public static class SkinnedMeshCombiner
	{
		public struct CombineInstance
		{
			public Mesh mesh { get; set; }
			public Transform[] bones { get; set; }
			public int[] destMesh { get; set; }
		}
		
		public static void CombineMeshes(SkinnedMeshRenderer target, CombineInstance[] sources, Dictionary<Transform, Transform> boneMap)
		{
			CombineInstance dest = new CombineInstance();
			dest.mesh = target.sharedMesh;
			CombineMeshes(ref dest, sources, target.rootBone, boneMap);
			target.sharedMesh = dest.mesh;
			target.bones = dest.bones;
		}
		
		private static Transform RecursivelyMapToNewRoot(Transform bone, Transform hierarchyRoot, Dictionary<Transform, Transform> boneMap)
		{
			Transform res;
			if (boneMap.TryGetValue(bone, out res))
			{
				return res;
			}
			if (string.Compare("Global", bone.name) == 0 || string.Compare(hierarchyRoot.name, bone.name) == 0)
			{
				boneMap.Add(bone, hierarchyRoot);
				return hierarchyRoot;
			}
			else
			{
				Transform parent = RecursivelyMapToNewRoot(bone.parent, hierarchyRoot, boneMap);
				Transform child = parent.FindChild(bone.name);
				if (child == null)
				{
					child = new GameObject().transform;
					child.parent = parent;
					child.localPosition = bone.localPosition;
					child.localRotation = bone.localRotation;
					child.localScale = bone.localScale;
					child.name = bone.name;
				}
				boneMap.Add(bone, child);
				return child;
			}
		}
		
		public static Transform[] CloneBoneListInNewHierarchy(Transform rootBone, Transform[] bones, Dictionary<Transform, Transform> boneMap)
		{
			var res = new Transform[bones.Length];
			for (int i = 0; i < bones.Length; i++)
			{
				res[i] = RecursivelyMapToNewRoot(bones[i], rootBone, boneMap);
			}
			return res;
		}
		
		public static void CombineMeshes(ref CombineInstance target, CombineInstance[] sources, Transform rootBone, Dictionary<Transform, Transform> boneMap)
		{
			Mesh dest = target.mesh;
			int vertexCount = 0;
			int bindPoseCount = 0;
			bool has_normals = false;
			bool has_tangents = false;
			bool has_uv = false;
			bool has_uv2 = false;
			bool has_colors = false;
			bool has_colors32 = false;
			int subMeshCount = 0;
			
			foreach (var source in sources)
			{
				foreach(var destIndex in source.destMesh)
				{
					if( subMeshCount < destIndex )
					{
						subMeshCount = destIndex;
					}
				}           
			}
			subMeshCount++;
			int[] subMeshTriangleLength = new int[subMeshCount];
			for(int i=0; i< subMeshTriangleLength.Length; i++)
			{
				subMeshTriangleLength[i] = 0;
			}
			
			Bounds bounds = sources[0].mesh.bounds;
			
			foreach (var source in sources)
			{
				var sMesh = source.mesh;
				vertexCount += sMesh.vertexCount;
				bindPoseCount += sMesh.bindposes.Length;
				has_normals |= sMesh.normals != null && sMesh.normals.Length != 0;
				has_tangents |= sMesh.tangents != null && sMesh.tangents.Length != 0;
				has_uv |= sMesh.uv != null && sMesh.uv.Length != 0;
				has_uv2 |= sMesh.uv2 != null && sMesh.uv2.Length != 0;
				has_colors |= sMesh.colors != null && sMesh.colors.Length != 0;
				has_colors32 |= sMesh.colors32 != null && sMesh.colors32.Length != 0;
				bounds.Encapsulate(sMesh.bounds);
				
				for (int i = 0; i < source.mesh.subMeshCount; i++)
				{
					if (source.destMesh[i] >= 0)
					{
						int triangleLength = sMesh.GetTriangles(i).Length;
						subMeshTriangleLength[source.destMesh[i]] += triangleLength;
					}
				}
			}
			int[][] submeshTriangles = new int[subMeshCount][];
			for(int i=0; i< subMeshTriangleLength.Length; i++)
			{
				submeshTriangles[i] = new int[subMeshTriangleLength[i]];
				subMeshTriangleLength[i] = 0;
			}
			
			if (has_colors32) has_colors = false;
			
			Vector3[] vertices = GetArray(dest.vertices, vertexCount);
			BoneWeight[] boneWeights = GetArray(dest.boneWeights, vertexCount);
			Vector3[] normals = has_normals ? GetArray(dest.normals, vertexCount) : null;
			Vector4[] tangents = has_tangents ? GetArray(dest.tangents, vertexCount) : null;
			Vector2[] uv = has_uv ? GetArray(dest.uv, vertexCount) : null;
			Vector2[] uv2 = has_uv2 ? GetArray(dest.uv2, vertexCount) : null;
			Color[] colors = has_colors ? GetArray(dest.colors, vertexCount) : null;
			Color32[] colors32 = has_colors32 ? GetArray(dest.colors32, vertexCount) : null;
			
			int vertexIndex = 0;
			
			var bonesCollection = new Dictionary<Transform, BoneIndexEntry>(bindPoseCount);
			List<Matrix4x4> bindPoses = new List<Matrix4x4>(bindPoseCount);
			List<Transform> bonesList = new List<Transform>(bindPoseCount);
			
			if (boneMap == null)
			{
				boneMap = new Dictionary<Transform, Transform>(bindPoseCount);
			}
			
			foreach (var source in sources)
			{
				var sMesh = source.mesh;
				vertexCount = sMesh.vertexCount;
				var sourceBones = rootBone == null ? source.bones : CloneBoneListInNewHierarchy(rootBone, source.bones, boneMap);
				
				BuildBoneWeights(sMesh.boneWeights, 0, boneWeights, vertexIndex, vertexCount, sourceBones, sMesh.bindposes, bonesCollection, bindPoses, bonesList);
				
				Array.Copy(sMesh.vertices, 0, vertices, vertexIndex, vertexCount);
				
				Array.Copy(sMesh.normals, 0, normals, vertexIndex, vertexCount);
				if (tangents != null)
				{
					if( sMesh.tangents != null && sMesh.tangents.Length > 0)
					{
						Array.Copy(sMesh.tangents, 0, tangents, vertexIndex, vertexCount);
					}
					else 
					{
						FillArray(tangents, vertexIndex, vertexCount, Vector4.zero);
					}
				}
				if( uv != null )
				{
					if (sMesh.uv != null)
					{
						Array.Copy(sMesh.uv, 0, uv, vertexIndex, vertexCount);
					}
					else 
					{
						FillArray(uv, vertexIndex, vertexCount, Vector4.zero);
					}
				}
				if( uv2 != null )
				{
					if( sMesh.uv2 != null )
					{
						Array.Copy(sMesh.uv2, 0, uv2, vertexIndex, vertexCount);
					}
					else 
					{
						FillArray(uv2, vertexIndex, vertexCount, Vector4.zero);
					}
				}
				if( colors != null )
				{
					if (sMesh.colors != null)
					{
						Array.Copy(sMesh.colors, 0, colors, vertexIndex, vertexCount);
					}
					else 
					{
						FillArray(colors, vertexIndex, vertexCount, Vector4.zero);
					}
				}
				
				
				for (int i = 0; i < source.mesh.subMeshCount; i++)
				{
					if (source.destMesh[i] >= 0)
					{
						int[] subTriangles = source.mesh.GetTriangles(i);
						int triangleLength = subTriangles.Length;
						int destMesh = source.destMesh[i];
						
						CopyIntArrayAdd(subTriangles, 0, submeshTriangles[destMesh], subMeshTriangleLength[destMesh], triangleLength, vertexIndex);
						subMeshTriangleLength[destMesh] += triangleLength;
					}
				}
				
				vertexIndex += vertexCount;
			}
			
			// empty destination to avoid conflicts
			dest.subMeshCount = 1;
			dest.triangles = new int[0];
			
			// fill in new values.
			dest.vertices = vertices;
			dest.boneWeights = boneWeights;
			dest.bindposes = bindPoses.ToArray();
			dest.normals = normals;
			dest.tangents = tangents;
			dest.uv = uv;
			dest.uv2 = uv2;
			dest.colors = colors;
			dest.colors32 = colors32;
			
			dest.subMeshCount = subMeshCount;
			for (int i = 0; i < subMeshCount; i++)
			{
				dest.SetTriangles(submeshTriangles[i], i);
			}
			
			target.bones = bonesList.ToArray();
			target.mesh = dest;
		}
		
		private static void BuildBoneWeights(BoneWeight[] source, int sourceIndex, BoneWeight[] dest, int destIndex, int count, Transform[] bones, Matrix4x4[] bindPoses, Dictionary<Transform, BoneIndexEntry> bonesCollection, List<Matrix4x4> bindPosesList, List<Transform> bonesList)
		{
			while (count-- > 0)
			{
				processBoneWeight(ref source[sourceIndex++], ref dest[destIndex++], bones, bindPoses, bonesCollection, bindPosesList, bonesList);
			}
		}
		
		private static void processBoneWeight(ref BoneWeight source, ref BoneWeight dest, Transform[] bones, Matrix4x4[] bindPoses, Dictionary<Transform, BoneIndexEntry> bonesCollection, List<Matrix4x4> bindPosesList, List<Transform> bonesList)
		{
			dest.boneIndex0 = TranslateBoneIndex(source.boneIndex0, bones, bindPoses, bonesCollection, bindPosesList, bonesList);
			dest.boneIndex1 = TranslateBoneIndex(source.boneIndex1, bones, bindPoses, bonesCollection, bindPosesList, bonesList);
			dest.boneIndex2 = TranslateBoneIndex(source.boneIndex2, bones, bindPoses, bonesCollection, bindPosesList, bonesList);
			dest.boneIndex3 = TranslateBoneIndex(source.boneIndex3, bones, bindPoses, bonesCollection, bindPosesList, bonesList);
			dest.weight0 = source.weight0;
			dest.weight1 = source.weight1;
			dest.weight2 = source.weight2;
			dest.weight3 = source.weight3;
		}
		
		private struct BoneIndexEntry
		{
			public int index;
			public List<int> indices;
			public int Count { get { return index >= 0 ? 1 : indices.Count; }}
			public int this[int idx] 
			{
				get 
				{
					if( index >= 0 )
					{
						if( idx == 0 ) return index;
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
		
		private static bool CompareMatrixes(Matrix4x4 m1, ref Matrix4x4 m2)
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
			if (Mathf.Abs(m1.m30 - m2.m30) > 0.0001) return false;
			if (Mathf.Abs(m1.m31 - m2.m31) > 0.0001) return false;
			if (Mathf.Abs(m1.m32 - m2.m32) > 0.0001) return false;
			if (Mathf.Abs(m1.m33 - m2.m33) > 0.0001) return false;
			return true;
		}

		private static int TranslateBoneIndex(int index, Transform[] bones, Matrix4x4[] bindPoses, Dictionary<Transform, BoneIndexEntry> bonesCollection, List<Matrix4x4> bindPosesList, List<Transform> bonesList)
		{
			var boneTransform = bones[index];
			BoneIndexEntry entry;
			if (bonesCollection.TryGetValue(boneTransform, out entry))
			{
				for (int i = 0; i < entry.Count; i++)
				{
					var res = entry[i];
					if (CompareMatrixes(bindPosesList[res], ref bindPoses[index]))
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
		
		private static void FillArray(Vector2[] array, int index, int count, Vector2 value)
		{
			while (count-- > 0)
			{
				array[index++] = value;
			}
		}
		
		
		private static void FillArray(Color[] array, int index, int count, Vector4 value)
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
		
		private static T[] GetArray<T>(T[] oldArray, int newLength)
		{
			return new T[newLength];
		}
	}
}