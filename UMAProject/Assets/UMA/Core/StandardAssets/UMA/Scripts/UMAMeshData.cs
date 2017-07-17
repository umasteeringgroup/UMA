#if !UNITY_STANDALONE
#undef USE_UNSAFE_CODE
#endif 

using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.Dynamics;

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

	[Serializable]
	public class UMABlendFrame
	{
		public float frameWeight; //should be 100% for one frame
		public Vector3[] deltaVertices;
		public Vector3[] deltaNormals;
		public Vector3[] deltaTangents;

		public UMABlendFrame(int vertexCount)
		{
			frameWeight = 100.0f;
			deltaVertices = new Vector3[vertexCount];
			deltaNormals = new Vector3[vertexCount];
			deltaTangents = new Vector3[vertexCount];
		}
	}

	[Serializable]
	public class UMABlendShape
	{
		public string shapeName;
		public UMABlendFrame[] frames;
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
		public Vector2[] uv3;
		public Vector2[] uv4;
		public UMABlendShape[] blendShapes;
		public ClothSkinningCoefficient[] clothSkinning;
		public Vector2[] clothSkinningSerialized;
		public SubMeshTriangles[] submeshes;
		[NonSerialized]
		public Transform[] bones;
		[NonSerialized]
		public Transform rootBone;
		public UMATransform[] umaBones;
		public int umaBoneCount;
		public int rootBoneHash;
		public int[] boneNameHashes;
		public int subMeshCount;
		public int vertexCount;
        public string RootBoneName = "Global";

		// Static shared data to reduce garbage
		// See: http://feedback.unity3d.com/suggestions/allow-mesh-data-to-have-a-length
		private static UMAMeshData bufferLockOwner = null;
		private static bool buffersInitialized = false;
		private static bool haveBackingArrays = false;
		const int MAX_VERTEX_COUNT = 65534;
		static List<Vector3> gVertices = new List<Vector3>(MAX_VERTEX_COUNT);
		static Vector3[] gVerticesArray;
		static List<Vector3> gNormals = new List<Vector3>(MAX_VERTEX_COUNT);
		static Vector3[] gNormalsArray;
		static List<Vector4> gTangents = new List<Vector4>(MAX_VERTEX_COUNT);
		static Vector4[] gTangentsArray;
		static List<Vector2> gUV = new List<Vector2>(MAX_VERTEX_COUNT);
		static Vector2[] gUVArray;
		static List<Vector2> gUV2 = new List<Vector2>(MAX_VERTEX_COUNT);
		static Vector2[] gUV2Array;
		static List<Vector2> gUV3 = new List<Vector2>(MAX_VERTEX_COUNT);
		static Vector2[] gUV3Array;
		static List<Vector2> gUV4 = new List<Vector2>(MAX_VERTEX_COUNT);
		static Vector2[] gUV4Array;
		static List<Color32> gColors32 = new List<Color32>(MAX_VERTEX_COUNT);
		static Color32[] gColors32Array;

		const int UNUSED_SUBMESH = -1;
		static List<int>[] gSubmeshTris = {
			// Order gSubmeshTris list from smallest to largest so they can be
			// efficiently assigned to the smallest valid array
			new List<int>(MAX_VERTEX_COUNT / 4),
			new List<int>(MAX_VERTEX_COUNT / 4),
			new List<int>(MAX_VERTEX_COUNT / 2),
			new List<int>(MAX_VERTEX_COUNT / 2),
			new List<int>(MAX_VERTEX_COUNT),
			new List<int>(MAX_VERTEX_COUNT),
			new List<int>(MAX_VERTEX_COUNT * 2),
		};
		static int[][] gSubmeshTriArrays;
		static int[] gSubmeshTriIndices;

		// They forgot the List<> method for bone weights.
#if USE_UNSAFE_CODE
		static BoneWeight[] gBoneWeightsArray = new BoneWeight[MAX_VERTEX_COUNT];
#endif

		/// <summary>
		/// Does this UMAMeshData own the shared buffers?
		/// </summary>
		/// <returns><c>true</c>, if this is the owner of the shared buffers.</returns>
		private bool OwnSharedBuffers()
		{
			return (this == bufferLockOwner);
		}

		/// <summary>
		/// Claims the static buffers.
		/// </summary>
		/// <returns><c>true</c>, if shared buffers were claimed, <c>false</c> otherwise.</returns>
		public bool ClaimSharedBuffers()
		{
			if (!buffersInitialized)
			{
				buffersInitialized = true;
				haveBackingArrays = true;

				gVerticesArray = gVertices.GetBackingArray();
				if (gVerticesArray == null) haveBackingArrays = false;
				gNormalsArray = gNormals.GetBackingArray();
				if (gNormalsArray == null) haveBackingArrays = false;
				gTangentsArray = gTangents.GetBackingArray();
				if (gTangentsArray == null) haveBackingArrays = false;
				gUVArray = gUV.GetBackingArray();
				if (gUVArray == null) haveBackingArrays = false;
				gUV2Array = gUV2.GetBackingArray();
				if (gUV2Array == null) haveBackingArrays = false;
				gUV3Array = gUV3.GetBackingArray();
				if (gUV3Array == null) haveBackingArrays = false;
				gUV4Array = gUV4.GetBackingArray();
				if (gUV4Array == null) haveBackingArrays = false;
				gColors32Array = gColors32.GetBackingArray();
				if (gColors32Array == null) haveBackingArrays = false;

				gSubmeshTriIndices = new int[gSubmeshTris.Length];
				gSubmeshTriArrays = new int[gSubmeshTris.Length][];
				for (int i = 0; i < gSubmeshTris.Length; i++)
				{
					gSubmeshTriIndices[i] = UNUSED_SUBMESH;
					gSubmeshTriArrays[i] = gSubmeshTris[i].GetBackingArray();
					if (gSubmeshTriArrays[i] == null) haveBackingArrays = false;
				}

				if (haveBackingArrays == false)
				{
					Debug.LogError("Unable to access backing arrays for shared UMAMeshData!");
				}
			}

			if (!haveBackingArrays)
				return false;
			
			if (bufferLockOwner == null)
			{
				bufferLockOwner = this;

				vertices = gVerticesArray;
				normals = gNormalsArray;
				tangents = gTangentsArray;
				uv = gUVArray;
				uv2 = gUV2Array;
				uv3 = gUV3Array;
				uv4 = gUV4Array;
				colors32 = gColors32Array;

				boneWeights = null;
#if USE_UNSAFE_CODE
				unityBoneWeights = gBoneWeightsArray;
#endif

				return true;
			}

			Debug.LogWarning("Unable to claim UMAMeshData global buffers!");
			return false;
		}

		/// <summary>
		/// Get an array for submesh triangle data.
		/// </summary>
		/// <returns>Either a shared or allocated int array for submesh triangles.</returns>
		public int[] GetSubmeshBuffer(int size, int submeshIndex)
		{
			if (OwnSharedBuffers())
			{
				for (int i = 0; i < gSubmeshTris.Length; i++)
				{
					if ((gSubmeshTriIndices[i] == UNUSED_SUBMESH) && (size < gSubmeshTris[i].Capacity))
					{
						gSubmeshTriIndices[i] = submeshIndex;
						gSubmeshTris[i].SetActiveSize(size);
						return gSubmeshTriArrays[i];
					}
				}

				Debug.LogWarning("Could not claim shared submesh buffer of size: " + size);
			}

			return new int[size];
		}

		/// <summary>
		/// Releases the static buffers.
		/// </summary>
		public void ReleaseSharedBuffers()
		{
			if (OwnSharedBuffers())
			{
				gVertices.SetActiveSize(0);
				vertices = null;
				gNormals.SetActiveSize(0);
				normals = null;
				gTangents.SetActiveSize(0);
				tangents = null;
				gUV.SetActiveSize(0);
				uv = null;
				gUV2.SetActiveSize(0);
				uv2 = null;
				gUV3.SetActiveSize(0);
				uv3 = null;
				gUV4.SetActiveSize(0);
				uv4 = null;
				gColors32.SetActiveSize(0);
				colors32 = null;

				for (int i = 0; i < gSubmeshTris.Length; i++)
				{
					gSubmeshTriIndices[i] = UNUSED_SUBMESH;
					gSubmeshTris[i].SetActiveSize(0);
				}

				boneWeights = null;
				unityBoneWeights = null;
				bufferLockOwner = null;
			}
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
			uv3 = new Vector2[size];
			uv4 = new Vector2[size];
			clothSkinning = new ClothSkinningCoefficient[size];
			clothSkinningSerialized = new Vector2[size];
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
		/// <param name="sharedMesh">Source mesh.</param>
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
			uv3 = sharedMesh.uv3;
			uv4 = sharedMesh.uv4;
			subMeshCount = sharedMesh.subMeshCount;
			submeshes = new SubMeshTriangles[subMeshCount];
			for (int i = 0; i < subMeshCount; i++)
			{
				submeshes[i].triangles = sharedMesh.GetTriangles(i);
			}

			//Create the blendshape data on the slot asset from the unity mesh
			#region Blendshape
			blendShapes = new UMABlendShape[sharedMesh.blendShapeCount];
			for (int shapeIndex = 0; shapeIndex < sharedMesh.blendShapeCount; shapeIndex++) 
			{
				blendShapes [shapeIndex] = new UMABlendShape ();
				blendShapes [shapeIndex].shapeName = sharedMesh.GetBlendShapeName (shapeIndex);

				int frameCount = sharedMesh.GetBlendShapeFrameCount (shapeIndex);
				blendShapes [shapeIndex].frames = new UMABlendFrame[frameCount];

				for (int frameIndex = 0; frameIndex < frameCount; frameIndex++) 
				{
					blendShapes [shapeIndex].frames [frameIndex] = new UMABlendFrame (sharedMesh.vertexCount);
					blendShapes[shapeIndex].frames[frameIndex].frameWeight = sharedMesh.GetBlendShapeFrameWeight( shapeIndex, frameIndex );
					sharedMesh.GetBlendShapeFrameVertices (shapeIndex, frameIndex, 
						blendShapes [shapeIndex].frames [frameIndex].deltaVertices,
						blendShapes [shapeIndex].frames [frameIndex].deltaNormals,
						blendShapes [shapeIndex].frames [frameIndex].deltaTangents);
				}
			}
			#endregion
		}

		/// <summary>
		/// Initialize UMA mesh cloth data from Unity Cloth
		/// </summary>
		/// <param name="cloth"></param>
		public void RetrieveDataFromUnityCloth(Cloth cloth)
		{
			clothSkinning = cloth.coefficients;
			clothSkinningSerialized = new Vector2[clothSkinning.Length];
			for (int i = 0; i < clothSkinning.Length; i++)
				SkinnedMeshCombiner.ConvertData(ref clothSkinning[i], ref clothSkinningSerialized[i]);
		}

		/// <summary>
		/// Validates the skinned transform hierarchy.
		/// </summary>
		/// <param name="rootBone">Root transform.</param>
		/// <param name="bones">Transforms.</param>
		public void UpdateBones(Transform rootBone, Transform[] bones)
		{
			rootBone = FindRoot(rootBone, bones);
			
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

		private static Transform RecursiveFindBone(Transform bone, string raceRoot)
		{
			if (bone.name == raceRoot) return bone;
			for (int i = 0; i < bone.childCount; i++)
			{
				var result = RecursiveFindBone(bone.GetChild(i), raceRoot);
				if (result != null)
					return result;
			}
			return null;
		}

		private Transform FindRoot(Transform rootBone, Transform[] bones)
		{
			if (rootBone == null)
			{
				for (int i = 0; i < bones.Length; i++)
				{
					if (bones[i] != null)
					{
						rootBone = bones[i];
						break;
					}
				}
			}
				
			while (rootBone.parent != null)
				rootBone = rootBone.parent;

			return RecursiveFindBone(rootBone, RootBoneName);
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
				mesh.uv3 = uv3;
				mesh.uv4 = uv4;
				mesh.colors32 = colors32;
			}
			mesh.bindposes = bindPoses;

			var subMeshCount = submeshes.Length;
			mesh.subMeshCount = subMeshCount;
			for (int i = 0; i < subMeshCount; i++)
			{
				bool sharedBuffer = false;
				for (int j = 0; j < gSubmeshTris.Length; j++)
				{
					if (gSubmeshTriIndices[j] == i)
					{
						sharedBuffer = true;
						mesh.SetTriangles(gSubmeshTris[j], i);
						gSubmeshTriIndices[j] = UNUSED_SUBMESH;
						break;
					}
				}

				if (!sharedBuffer)
					mesh.SetTriangles(submeshes[i].triangles, i);
			}

			//Apply the blendshape data from the slot asset back to the combined UMA unity mesh.
			#region Blendshape
			mesh.ClearBlendShapes();
			if (blendShapes != null && blendShapes.Length > 0 ) 
			{
				for (int shapeIndex = 0; shapeIndex < blendShapes.Length; shapeIndex++) 
				{
					if (blendShapes [shapeIndex] == null) 
					{
						//Debug.LogError ("blendShapes [shapeIndex] == null!");
                        //No longer an error, this will be null if the blendshape got baked.
						break;
					}

					for( int frameIndex = 0; frameIndex < blendShapes[shapeIndex].frames.Length; frameIndex++)
					{
						//There might be an extreme edge case where someone has the same named blendshapes on different meshes that end up on different renderers.
						string name = blendShapes[shapeIndex].shapeName; 
						mesh.AddBlendShapeFrame (name,
							blendShapes[shapeIndex].frames[frameIndex].frameWeight,
							blendShapes[shapeIndex].frames[frameIndex].deltaVertices,
							blendShapes[shapeIndex].frames[frameIndex].deltaNormals,
							blendShapes[shapeIndex].frames[frameIndex].deltaTangents);
					}
				}
			}
			#endregion

			mesh.RecalculateBounds();
			renderer.bones = bones != null ? bones : skeleton.HashesToTransforms(boneNameHashes);
			renderer.sharedMesh = mesh;
			renderer.rootBone = rootBone;

			if (clothSkinning != null && clothSkinning.Length > 0)
			{
				var cloth = renderer.GetComponent<Cloth>();
				if (cloth == null)
				{
					cloth = renderer.gameObject.AddComponent<Cloth>();
                    UMAPhysicsAvatar physicsAvatar = renderer.gameObject.GetComponentInParent<UMAPhysicsAvatar> ();
                    if (physicsAvatar != null)
                    {
                        cloth.sphereColliders = physicsAvatar.SphereColliders.ToArray();
                        cloth.capsuleColliders = physicsAvatar.CapsuleColliders.ToArray();
                    }
                    else
                        Debug.Log("PhysicsAvatar is null!");
				}
				cloth.coefficients = clothSkinning;
			}
		}

		/// <summary>
		/// Applies the data to a Unity mesh.
		/// </summary>
		/// <param name="renderer">Target renderer.</param>
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
			mesh.uv3 = uv3;
			mesh.uv4 = uv4;
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
			for(int i = 0; i < umaBoneCount; i++)
			{
				skeleton.EnsureBone(umaBones[i]);
			}
			skeleton.EnsureBoneHierarchy();
		}

		private void ApplySharedBuffers(Mesh mesh)
		{
			// Thanks for providing these awesome List<> methods rather than listening
			// to every one of your users who told you to use Array and size, Unity!
			gVertices.SetActiveSize(vertexCount);
			mesh.SetVertices(gVertices);

			// Whoops, looks like they also forgot one! Job well done.
#if USE_UNSAFE_CODE
			unsafe
			{
				fixed (void* pBoneWeights = gBoneWeightsArray) 
				{ 
					UIntPtr* lengthPtr = (UIntPtr*)pBoneWeights - 1; 
					try 
					{ 
						*lengthPtr = (UIntPtr)vertexCount; 
						mesh.boneWeights = gBoneWeightsArray; 
					} 
					finally 
					{ 
						*lengthPtr = (UIntPtr)MAX_VERTEX_COUNT; 
					} 
				}
			}
#else
			if (unityBoneWeights != null)
			{
				mesh.boneWeights = unityBoneWeights;
			}
			else
			{
				mesh.boneWeights = UMABoneWeight.Convert(boneWeights);
			}
#endif
			if (normals != null)
			{
				gNormals.SetActiveSize(vertexCount);
				mesh.SetNormals(gNormals);
			}
			if (tangents != null)
			{
				gTangents.SetActiveSize(vertexCount);
				mesh.SetTangents(gTangents);
			}
			if (uv != null)
			{
				gUV.SetActiveSize(vertexCount);
				mesh.SetUVs(0, gUV);
			}
			if (uv2 != null)
			{
				gUV2.SetActiveSize(vertexCount);
				mesh.SetUVs(1, gUV2);
			}
			if (uv3 != null)
			{
				gUV3.SetActiveSize(vertexCount);
				mesh.SetUVs(2, gUV3);
			}
			if (uv4 != null)
			{
				gUV4.SetActiveSize(vertexCount);
				mesh.SetUVs(3, gUV4);
			}
			if (colors32 != null)
			{
				gColors32.SetActiveSize(vertexCount);
				mesh.SetColors(gColors32);
			}

		}

		private void ComputeBoneNameHashes(Transform[] bones)
		{
			boneNameHashes = new int[bones.Length];
			for (int i = 0; i < bones.Length; i++)
			{
				boneNameHashes[i] = UMAUtils.StringToHash(bones[i].name);
			}
		}

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
