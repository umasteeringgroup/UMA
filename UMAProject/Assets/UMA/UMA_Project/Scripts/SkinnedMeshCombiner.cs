using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Text;

namespace UMA
{
	public static class SkinnedMeshCombiner
	{
		public class CombineInstance
		{
			public Mesh mesh { get; set; }
			public Transform[] bones { get; set; }
			public int[] destMesh { get; set; }
			
			internal Vector3[] normals = null;
			internal Vector4[] tangents = null;
			internal Vector2[] uv = null;
			internal Vector2[] uv2 = null;
			internal Color32[] colors32 = null;
			
			internal Matrix4x4[] binds = null;
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
				Transform parent = hierarchyRoot;
				if (bone.parent != null) {
					parent = RecursivelyMapToNewRoot(bone.parent, hierarchyRoot, boneMap);
				}
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
//				if (child.localToWorldMatrix != bone.localToWorldMatrix)
//				{
//					Debug.LogWarning("bone match in name only: " + bone.name);
//				}
				boneMap.Add(bone, child);
				return child;
			}
		}
		
		public static Transform[] SkinnedBonesInInstance(CombineInstance instance)
		{
			BoneWeight[] boneWeights = instance.mesh.boneWeights;
			Transform[] bones = instance.bones;
			
			BitArray skinnedBinds = new BitArray(bones.Length);
			for (int i = 0; i < boneWeights.Length; i++) {
				BoneWeight weight = boneWeights[i];
				skinnedBinds[weight.boneIndex0] = true;
				skinnedBinds[weight.boneIndex1] = true;
				skinnedBinds[weight.boneIndex2] = true;
				skinnedBinds[weight.boneIndex3] = true;
			}
			
			List<Transform> skinnedBones = new List<Transform>();
			for (int i = 0; i < skinnedBinds.Length; i++) {
				if (skinnedBinds[i])
				{
					Transform skinnedBone = instance.bones[i];
					while ((skinnedBone != null) && !skinnedBones.Contains(skinnedBone))
					{
						skinnedBones.Add(skinnedBone);
						skinnedBone = skinnedBone.parent;
					}
				}
			}
			
			return skinnedBones.ToArray();
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
		
		public static Transform[] CloneSkinnedBonesInNewHierarchy(Mesh mesh, Transform rootBone, Transform[] bones, Dictionary<Transform, Transform> boneMap)
		{
			BoneWeight[] boneWeights = mesh.boneWeights;
			
			BitArray skinnedBinds = new BitArray(bones.Length);
			for (int i = 0; i < boneWeights.Length; i++) {
				BoneWeight weight = boneWeights[i];
				skinnedBinds[weight.boneIndex0] = true;
				skinnedBinds[weight.boneIndex1] = true;
				skinnedBinds[weight.boneIndex2] = true;
				skinnedBinds[weight.boneIndex3] = true;
			}
			
			var res = new Transform[bones.Length];
			for (int i = 0; i < bones.Length; i++)
			{
				if (skinnedBinds[i])
				{
					res[i] = RecursivelyMapToNewRoot(bones[i], rootBone, boneMap);
				}
			}
			for (int i = 0; i < bones.Length; i++)
			{
				if (!skinnedBinds[i])
				{
					Transform mappedBone;
					if ( boneMap.TryGetValue(bones[i], out mappedBone))
					{
						res[i] = mappedBone;
					}
					else
					{
						res[i] = bones[i];
					}
				}
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
				source.binds = sMesh.bindposes;
				bindPoseCount += source.binds.Length;
				source.normals = sMesh.normals;
				has_normals |= source.normals != null && source.normals.Length != 0;
				source.tangents = sMesh.tangents;
				has_tangents |= source.tangents != null && source.tangents.Length != 0;
				source.uv = sMesh.uv;
				has_uv |= source.uv != null && source.uv.Length != 0;
				source.uv2 = sMesh.uv2;
				has_uv2 |= source.uv2 != null && source.uv2.Length != 0;
				source.colors32 = sMesh.colors32;
				has_colors32 |= source.colors32 != null && source.colors32.Length != 0;
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
			
			Vector3[] vertices = GetArray(dest.vertices, vertexCount);
			BoneWeight[] boneWeights = GetArray(dest.boneWeights, vertexCount);
			Vector3[] normals = has_normals ? GetArray(dest.normals, vertexCount) : null;
			Vector4[] tangents = has_tangents ? GetArray(dest.tangents, vertexCount) : null;
			Vector2[] uv = has_uv ? GetArray(dest.uv, vertexCount) : null;
			Vector2[] uv2 = has_uv2 ? GetArray(dest.uv2, vertexCount) : null;
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
				var sourceBones = rootBone == null ? source.bones : CloneSkinnedBonesInNewHierarchy(sMesh, rootBone, source.bones, boneMap);
				
				BuildBoneWeights(sMesh.boneWeights, 0, boneWeights, vertexIndex, vertexCount, sourceBones, source.binds, bonesCollection, bindPoses, bonesList);
				
				Array.Copy(sMesh.vertices, 0, vertices, vertexIndex, vertexCount);
				
				if (has_normals)
				{
					if(source.normals != null && source.normals.Length > 0)
					{
						Array.Copy(source.normals, 0, normals, vertexIndex, vertexCount);
					}
					else 
					{
						FillArray(tangents, vertexIndex, vertexCount, Vector3.zero);
					}
				}
				if (has_tangents)
				{
					if(source.tangents != null && source.tangents.Length > 0)
					{
						Array.Copy(source.tangents, 0, tangents, vertexIndex, vertexCount);
					}
					else 
					{
						FillArray(tangents, vertexIndex, vertexCount, Vector4.zero);
					}
				}
				if (has_uv)
				{
					if (source.uv != null)
					{
						Array.Copy(source.uv, 0, uv, vertexIndex, vertexCount);
					}
					else 
					{
						FillArray(uv, vertexIndex, vertexCount, Vector4.zero);
					}
				}
				if (has_uv2)
				{
					if (source.uv2 != null)
					{
						Array.Copy(source.uv2, 0, uv2, vertexIndex, vertexCount);
					}
					else 
					{
						FillArray(uv2, vertexIndex, vertexCount, Vector4.zero);
					}
				}
				if (has_colors32)
				{
					if (source.colors32 != null && source.colors32.Length > 0)
					{
						Array.Copy(source.colors32, 0, colors32, vertexIndex, vertexCount);
					}
					else 
					{
						Color32 white32 = Color.white;
						FillArray(colors32, vertexIndex, vertexCount, white32);
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
			dest.colors32 = colors32;
			
			dest.subMeshCount = subMeshCount;
			for (int i = 0; i < subMeshCount; i++)
			{
				dest.SetTriangles(submeshTriangles[i], i);
			}
			
			target.bones = bonesList.ToArray();
			target.mesh = dest;
		}
		
		#if (true)		
		private static void BuildBoneWeights(BoneWeight[] source, int sourceIndex, BoneWeight[] dest, int destIndex, int count, Transform[] bones, Matrix4x4[] bindPoses, Dictionary<Transform, BoneIndexEntry> bonesCollection, List<Matrix4x4> bindPosesList, List<Transform> bonesList)
		{
			int[] boneMapping = new int[bones.Length];
			for (int i = 0; i < boneMapping.Length; i++)
			{
				boneMapping[i] = TranslateBoneIndex(i, bones, bindPoses, bonesCollection, bindPosesList, bonesList);
			}
			
			BoneWeight sourceWeight;
			while (count-- > 0)
			{
				sourceWeight = source[sourceIndex++];
				dest[destIndex].boneIndex0 = boneMapping[sourceWeight.boneIndex0];
				dest[destIndex].boneIndex1 = boneMapping[sourceWeight.boneIndex1];
				dest[destIndex].boneIndex2 = boneMapping[sourceWeight.boneIndex2];
				dest[destIndex].boneIndex3 = boneMapping[sourceWeight.boneIndex3];
				dest[destIndex].weight0 = sourceWeight.weight0;
				dest[destIndex].weight1 = sourceWeight.weight1;
				dest[destIndex].weight2 = sourceWeight.weight2;
				dest[destIndex++].weight3 = sourceWeight.weight3;
			}
		}
		#else
		private static void BuildBoneWeights(BoneWeight[] source, int sourceIndex, BoneWeight[] dest, int destIndex, int count, Transform[] bones, Matrix4x4[] bindPoses, Dictionary<Transform, BoneIndexEntry> bonesCollection, List<Matrix4x4> bindPosesList, List<Transform> bonesList)
		{
			while (count-- > 0)
			{
				ProcessBoneWeight(ref source[sourceIndex++], ref dest[destIndex++], bones, bindPoses, bonesCollection, bindPosesList, bonesList);
			}
		}
		
		private static void ProcessBoneWeight(ref BoneWeight source, ref BoneWeight dest, Transform[] bones, Matrix4x4[] bindPoses, Dictionary<Transform, BoneIndexEntry> bonesCollection, List<Matrix4x4> bindPosesList, List<Transform> bonesList)
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
		#endif
		
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
//				Debug.LogWarning("Multiple binds for bone: " + boneTransform.name);
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
		
		private static T[] GetArray<T>(T[] oldArray, int newLength)
		{
			return new T[newLength];
		}
	}
}