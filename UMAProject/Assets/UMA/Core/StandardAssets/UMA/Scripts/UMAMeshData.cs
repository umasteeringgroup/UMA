#if !UNITY_STANDALONE
#undef USE_UNSAFE_CODE
#endif 

using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.Dynamics;
using UnityEngine.Profiling;
using Unity.Collections;
using UnityEngine.Serialization;
using System.Text;

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
	/// This is only used for compatibility in UMA 2.11
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
	}

	[Serializable]
	public class UMABlendFrame
	{
		public float frameWeight = 100.0f; //should be 100% for one frame
		public Vector3[] deltaVertices = null;
		public Vector3[] deltaNormals = null;
		public Vector3[] deltaTangents = null;

		public UMABlendFrame()
		{ }

		public UMABlendFrame(int vertexCount, bool hasNormals = true, bool hasTangents = true)
		{
			frameWeight = 100.0f;
			deltaVertices = new Vector3[vertexCount];

			if (hasNormals)
				deltaNormals = new Vector3[vertexCount];
			else
				deltaNormals = new Vector3[0];

			if (hasTangents)
				deltaTangents = new Vector3[vertexCount];
			else
				deltaTangents = new Vector3[0];
		}

		public bool HasNormals()
		{
			if (deltaNormals != null && deltaNormals.Length > 0)
				return true;

			return false;
		}

		public bool HasTangents()
		{
			if (deltaTangents != null && deltaTangents.Length > 0)
				return true;

			return false;
		}

		/// <summary>
		/// Determine whether the delta array has any non-zero vectors.
		/// </summary>
		/// <param name="deltas">Array of vector deltas.</param>
		/// <returns></returns>
		public static bool isAllZero(Vector3[] deltas)
		{
			if (deltas == null)
				return true;

			for(int i = 0; i < deltas.Length; i++)
			{
				if (deltas[i].sqrMagnitude > 0.0001f)
					return false;
			}

			return true;
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
#if USE_NATIVE_ARRAYS
		[NonSerialized]
		public NativeArray<BoneWeight1> unityBoneWeights;
		[NonSerialized]
		public NativeArray<byte> unityBonesPerVertex);
#endif
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
		//public int boneWeightCount;
        public string RootBoneName = "Global";
		[FormerlySerializedAs("SerializedBoneWeights")]
		public BoneWeight1[] ManagedBoneWeights;
		[FormerlySerializedAs("SerializedBonesPerVertex")]
		public byte[] ManagedBonesPerVertex;
		[System.NonSerialized]
		public bool LoadedBoneweights;
		public string SlotName; // the slotname. used for debugging.


		// Static shared data to reduce garbage
		// See: http://feedback.unity3d.com/suggestions/allow-mesh-data-to-have-a-length
		private static UMAMeshData bufferLockOwner = null;
		private static bool buffersInitialized = false;
		private static bool haveBackingArrays = false;
#if UMA_32BITBUFFERS
		const int MAX_VERTEX_COUNT = 262144;
#else
		const int MAX_VERTEX_COUNT = 65534;
#endif
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

		/*
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
			new List<int>(MAX_VERTEX_COUNT * 4),
		};
		static int[][] gSubmeshTriArrays;
		static int[] gSubmeshTriIndices =
		{
			UNUSED_SUBMESH,
			UNUSED_SUBMESH,
			UNUSED_SUBMESH,
			UNUSED_SUBMESH,
			UNUSED_SUBMESH,
			UNUSED_SUBMESH,
			UNUSED_SUBMESH,
			UNUSED_SUBMESH
		}; */

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

				/*
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
					if (Debug.isDebugBuild)
						Debug.LogError("Unable to access backing arrays for shared UMAMeshData!");
				}*/
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

				return true;
			}

			if (Debug.isDebugBuild)
				Debug.LogWarning("Unable to claim UMAMeshData global buffers!");
			return false;
		}

		/// <summary>
		/// Get an array for submesh triangle data.
		/// </summary>
		/// <returns>Either a shared or allocated int array for submesh triangles.</returns>
		public int[] GetSubmeshBuffer(int size, int submeshIndex)
		{
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

				/*
				for (int i = 0; i < gSubmeshTris.Length; i++)
				{
					gSubmeshTriIndices[i] = UNUSED_SUBMESH;
					gSubmeshTris[i].SetActiveSize(0);
				} */

				bufferLockOwner = null;
			}
			FreeBoneWeights();
		}

		public void PrepareVertexBuffers(int size)
		{
			vertexCount = size;
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
#if USE_NATIVE_ARRAYS
			unityBonesPerVertex = sharedMesh.GetBonesPerVertex();
			unityBoneWeights = sharedMesh.GetAllBoneWeights();
			SerializedBoneWeights = unityBoneWeights.ToArray();
			SerializedBonesPerVertex = unityBonesPerVertex.ToArray();

#else
			var unityBonesPerVertex = sharedMesh.GetBonesPerVertex();
			var unityBoneWeights = sharedMesh.GetAllBoneWeights();
			ManagedBoneWeights = unityBoneWeights.ToArray();
			ManagedBonesPerVertex = unityBonesPerVertex.ToArray();
			//if (unityBonesPerVertex.IsCreated)
			//	unityBonesPerVertex.Dispose();
			//if (unityBoneWeights.IsCreated)
			//	unityBoneWeights.Dispose();
#endif

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

			Vector3[] deltaVertices;
			Vector3[] deltaNormals;
			Vector3[] deltaTangents;

			for (int shapeIndex = 0; shapeIndex < sharedMesh.blendShapeCount; shapeIndex++) 
			{
				blendShapes [shapeIndex] = new UMABlendShape ();
				blendShapes [shapeIndex].shapeName = sharedMesh.GetBlendShapeName (shapeIndex);

				int frameCount = sharedMesh.GetBlendShapeFrameCount (shapeIndex);
				blendShapes [shapeIndex].frames = new UMABlendFrame[frameCount];

				for (int frameIndex = 0; frameIndex < frameCount; frameIndex++) 
				{
					deltaVertices = new Vector3[sharedMesh.vertexCount];
					deltaNormals = new Vector3[sharedMesh.vertexCount];
					deltaTangents = new Vector3[sharedMesh.vertexCount];

					bool hasNormals = false;
					bool hasTangents = false;

					//Get the delta arrays first so we can determine if we don't need the delta normals or the delta tangents.
					sharedMesh.GetBlendShapeFrameVertices(shapeIndex, frameIndex, deltaVertices, deltaNormals, deltaTangents);

					if (!UMABlendFrame.isAllZero(deltaNormals))
						hasNormals = true;

					if (!UMABlendFrame.isAllZero(deltaTangents))
						hasTangents = true;

					blendShapes [shapeIndex].frames [frameIndex] = new UMABlendFrame ();
					blendShapes[shapeIndex].frames[frameIndex].frameWeight = sharedMesh.GetBlendShapeFrameWeight( shapeIndex, frameIndex );

					blendShapes[shapeIndex].frames[frameIndex].deltaVertices = deltaVertices;
					if (hasNormals)
						blendShapes[shapeIndex].frames[frameIndex].deltaNormals = deltaNormals;
					if (hasTangents)
						blendShapes[shapeIndex].frames[frameIndex].deltaTangents = deltaTangents;

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
			if (renderer == null)
			{
				if (Debug.isDebugBuild)
					Debug.LogError("Renderer is null!");
				return;
			}

			CreateTransforms(skeleton);

			Mesh mesh = new Mesh();//renderer.sharedMesh;
#if UMA_32BITBUFFERS
			mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
#endif

#if UNITY_EDITOR
			if (UnityEditor.PrefabUtility.IsAddedComponentOverride(renderer))
			{
				if (Debug.isDebugBuild)
					Debug.LogError("Cannot apply changes to prefab!");
			}
			if (mesh != null)
			{
				if (UnityEditor.AssetDatabase.IsSubAsset(mesh))
				{
					if (Debug.isDebugBuild)
						Debug.LogError("Cannot apply changes to asset mesh!");
				}
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
				NativeArray<Vector3> verts = new NativeArray<Vector3>(vertices, Allocator.Temp);
				mesh.SetVertices(verts);
#if false
                ValidateNativeBuffers();
#endif
				SetBoneWeightsFromMeshData(mesh);                //mesh.boneWeights = unityBoneWeights != null ? unityBoneWeights : UMABoneWeight.Convert(boneWeights);
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
				/*
				bool sharedBuffer = false;
				for (int j = 0; j < gSubmeshTris.Length; j++)
				{
					if (gSubmeshTriIndices[j] == i)
					{
						sharedBuffer = true;
#if VALIDATE_TRIANGLES
#else
#endif
						mesh.SetTriangles(gSubmeshTris[j], i);
						gSubmeshTriIndices[j] = UNUSED_SUBMESH;
						break;
					}
				}

				if (!sharedBuffer)*/
				mesh.SetTriangles(submeshes[i].triangles, i);
			}

			//Apply the blendshape data from the slot asset back to the combined UMA unity mesh.
#region Blendshape
			mesh.ClearBlendShapes();
			if (blendShapes != null && blendShapes.Length > 0)
			{
				for (int shapeIndex = 0; shapeIndex < blendShapes.Length; shapeIndex++)
				{
					if (blendShapes[shapeIndex] == null)
					{
						//Debug.LogError ("blendShapes [shapeIndex] == null!");
						//No longer an error, this will be null if the blendshape got baked.
						break;
					}

					for (int frameIndex = 0; frameIndex < blendShapes[shapeIndex].frames.Length; frameIndex++)
					{
						//There might be an extreme edge case where someone has the same named blendshapes on different meshes that end up on different renderers.
						string name = blendShapes[shapeIndex].shapeName;

						float frameWeight = blendShapes[shapeIndex].frames[frameIndex].frameWeight;
						Vector3[] deltaVertices = blendShapes[shapeIndex].frames[frameIndex].deltaVertices;
						Vector3[] deltaNormals = blendShapes[shapeIndex].frames[frameIndex].deltaNormals;
						Vector3[] deltaTangents = blendShapes[shapeIndex].frames[frameIndex].deltaTangents;

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
			renderer.bones = bones != null ? bones : skeleton.HashesToTransforms(boneNameHashes);
			UMAUtils.DestroySceneObject(renderer.sharedMesh);
			//			GameObject.Destroy(renderer.sharedMesh);
			renderer.sharedMesh = mesh;
			renderer.rootBone = rootBone;

			if (clothSkinning != null && clothSkinning.Length > 0)
			{
				Cloth cloth = renderer.GetComponent<Cloth>();
				if (cloth != null)
				{
					GameObject.DestroyImmediate(cloth);
					cloth = null;
				}

				cloth = renderer.gameObject.AddComponent<Cloth>();
				UMAPhysicsAvatar physicsAvatar = renderer.gameObject.GetComponentInParent<UMAPhysicsAvatar>();
				if (physicsAvatar != null)
				{
					cloth.sphereColliders = physicsAvatar.SphereColliders.ToArray();
					cloth.capsuleColliders = physicsAvatar.CapsuleColliders.ToArray();
				}

				cloth.coefficients = clothSkinning;
			}
		}

        private void SetBoneWeightsFromMeshData(Mesh mesh)
        {
#if USE_NATIVE_ARRAYS
				if (unityBoneWeights != null)
				{
					mesh.SetBoneWeights(unityBonesPerVertex, unityBoneWeights);
				}
#else
            if (ManagedBoneWeights != null)
            {
                // It seems like a no-brainer here to use Allocator.Temp. But that data is actually not freed
                // until the end of the frame. 
				if (ManagedBonesPerVertex == null || ManagedBonesPerVertex.Length < 1 || ManagedBoneWeights == null || ManagedBoneWeights.Length < 1)
                {
					Debug.LogError("Error! Boneweights and BonesPerVertex is invalid. The slot must be regenerated.");
					return;
                }
                var unityBonesPerVertex = new NativeArray<byte>(ManagedBonesPerVertex, Allocator.Persistent);
                var unityBoneWeights = new NativeArray<BoneWeight1>(ManagedBoneWeights, Allocator.Persistent);
                mesh.SetBoneWeights(unityBonesPerVertex, unityBoneWeights);
                unityBonesPerVertex.Dispose();
                unityBoneWeights.Dispose();
            }
#endif
        }

        private void ValidateNativeBuffers()
        {
#if USE_NATIVE_ARRAYS
            if (unityBonesPerVertex == null)
            {
                Debug.LogError("Invalid bones per vertex! (null)");
                return;
            }
            if (unityBoneWeights == null)
            {
                Debug.LogError("Invalid bone weights! (null)");
                return;
            }
            if (!unityBonesPerVertex.IsCreated)
            {
                Debug.LogError("Unity bones per vertex not created!!");
                return;
            }
            if (!unityBoneWeights.IsCreated)
            {
                Debug.LogError("Unity bone weights not created!!");
                return;
            }
            if (unityBonesPerVertex.Length < 1)
            {
                Debug.LogError("Unity bones per vertex is empty!!");
                return;
            }
            if (unityBoneWeights.Length < 1)
            {
                Debug.LogError("Unity boneweights is empty!!");
                return;
            }
#endif
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

			SetBoneWeightsFromMeshData(mesh);

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

		/// <summary>
		/// This converts old BoneWeights to new ones at load time.
		/// </summary>
		public void LoadBoneWeights()
		{
			// it's at least this big
			List<BoneWeight1> oldWeights = new List<BoneWeight1>(boneWeights.Length);
			List<byte> oldBonesPerVertex = new List<byte>(boneWeights.Length);

			foreach(UMABoneWeight bw in boneWeights)
            {
				byte BonesPerVertex = 0;
				float totWeight = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
				if (bw.weight0 > 0.0f)
                {
					BoneWeight1 newWeight = new BoneWeight1();
					newWeight.boneIndex = bw.boneIndex0;
					newWeight.weight = bw.weight0 / totWeight;
					oldWeights.Add(newWeight);
					BonesPerVertex++;
                }
				if (bw.weight1 > 0.0f)
				{
					BoneWeight1 newWeight = new BoneWeight1();
					newWeight.boneIndex = bw.boneIndex1;
					newWeight.weight = bw.weight1 / totWeight;
					oldWeights.Add(newWeight);
					BonesPerVertex++;
				}
				if (bw.weight2 > 0.0f)
				{
					BoneWeight1 newWeight = new BoneWeight1();
					newWeight.boneIndex = bw.boneIndex2;
					newWeight.weight = bw.weight2 / totWeight;
					oldWeights.Add(newWeight);
					BonesPerVertex++;
				}
				if (bw.weight3 > 0.0f)
				{
					BoneWeight1 newWeight = new BoneWeight1();
					newWeight.boneIndex = bw.boneIndex3;
					newWeight.weight = bw.weight3 / totWeight;
					oldWeights.Add(newWeight);
					BonesPerVertex++;
				}
				oldBonesPerVertex.Add(BonesPerVertex);
			}
#if USE_NATIVE_ARRAYS
			FreeBoneWeights();
			unityBonesPerVertex = new NativeArray<byte>(oldBonesPerVertex.ToArray(), Allocator.Persistent);
			unityBoneWeights = new NativeArray<BoneWeight1>(oldWeights.ToArray(), Allocator.Persistent);
			NativeArray<BoneWeight1>.Copy(oldWeights.ToArray(), unityBoneWeights);
			NativeArray<byte>.Copy(oldBonesPerVertex.ToArray(), unityBonesPerVertex);
#else
			ManagedBoneWeights = oldWeights.ToArray();
			ManagedBonesPerVertex = oldBonesPerVertex.ToArray();
			oldWeights = null; // free these, in case this gets saved somehow.
			oldBonesPerVertex = null;
#endif
#if UNITY_EDITOR
			// set this dirty.
			// force save...?
#endif
			LoadedBoneweights = true;
		}

		public void LoadVariableBoneWeights()
        {
#if USE_NATIVE_ARRAYS
			FreeBoneWeights();
			unityBonesPerVertex = new NativeArray<byte>(SerializedBonesPerVertex, Allocator.Persistent);
			unityBoneWeights = new NativeArray<BoneWeight1>(SerializedBoneWeights, Allocator.Persistent);
			LoadedBoneweights = true;
#endif
        }

		public void FreeBoneWeights()
        {
#if USE_NATIVE_ARRAYS
			if (LoadedBoneweights)
			{
				if (unityBoneWeights != null && unityBoneWeights.IsCreated)
					unityBoneWeights.Dispose();
				if (unityBonesPerVertex != null && unityBonesPerVertex.IsCreated)
					unityBonesPerVertex.Dispose();
				LoadedBoneweights = false;
			}
#endif
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

			SetBoneWeightsFromMeshData(mesh);
 
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

		/// <summary>
		/// Creates a deep copy of an UMAMeshData object.
		/// </summary>
		/// <returns>The new copy of the UMAMeshData</returns>
		public UMAMeshData DeepCopy()
		{
			UMAMeshData newMeshData = new UMAMeshData();

			if (ManagedBonesPerVertex != null)
            {
				newMeshData.ManagedBonesPerVertex = new byte[ManagedBonesPerVertex.Length];
				Array.Copy(ManagedBonesPerVertex, newMeshData.ManagedBonesPerVertex, ManagedBonesPerVertex.Length);
            }

			if (ManagedBoneWeights != null)
			{
				newMeshData.ManagedBoneWeights = new BoneWeight1[ManagedBoneWeights.Length];
				Array.Copy(ManagedBoneWeights, newMeshData.ManagedBoneWeights, ManagedBoneWeights.Length);
			}

			if (bindPoses != null)
			{
				newMeshData.bindPoses = new Matrix4x4[bindPoses.Length];
				Array.Copy(bindPoses, newMeshData.bindPoses, bindPoses.Length);
			}

			if (vertices != null)
			{
				newMeshData.vertices = new Vector3[vertices.Length];
				Array.Copy(vertices, newMeshData.vertices, vertices.Length);
			}

			if (normals != null)
			{
				newMeshData.normals = new Vector3[normals.Length];
				Array.Copy(normals, newMeshData.normals, normals.Length);
			}

			if (tangents != null)
			{
				newMeshData.tangents = new Vector4[tangents.Length];
				Array.Copy(tangents, newMeshData.tangents, tangents.Length);
			}

			if (colors32 != null)
			{
				newMeshData.colors32 = new Color32[colors32.Length];
				Array.Copy(colors32, newMeshData.colors32, colors32.Length);
			}

			if (uv != null)
			{
				newMeshData.uv = new Vector2[uv.Length];
				Array.Copy(uv, newMeshData.uv, uv.Length);
			}

			if (uv2 != null)
			{
				newMeshData.uv2 = new Vector2[uv2.Length];
				Array.Copy(uv2, newMeshData.uv2, uv2.Length);
			}

			if (uv3 != null)
			{
				newMeshData.uv3 = new Vector2[uv3.Length];
				Array.Copy(uv3, newMeshData.uv3, uv3.Length);
			}

			if (uv4 != null)
			{
				newMeshData.uv4 = new Vector2[uv4.Length];
				Array.Copy(uv4, newMeshData.uv4, uv4.Length);
			}

			if(blendShapes != null)
			{
				newMeshData.blendShapes = new UMABlendShape[blendShapes.Length];
				Array.Copy(blendShapes, newMeshData.blendShapes, blendShapes.Length);
			}

			if(clothSkinning != null)
			{
				newMeshData.clothSkinning = new ClothSkinningCoefficient[clothSkinning.Length];
				Array.Copy(clothSkinning, newMeshData.clothSkinning, clothSkinning.Length);
			}

			if(clothSkinningSerialized != null)
			{
				newMeshData.clothSkinningSerialized = new Vector2[clothSkinningSerialized.Length];
				Array.Copy(clothSkinningSerialized, newMeshData.clothSkinningSerialized, clothSkinningSerialized.Length);
			}

			if(submeshes != null)
			{
				newMeshData.submeshes = new SubMeshTriangles[submeshes.Length];
				Array.Copy(submeshes, newMeshData.submeshes, submeshes.Length);
			}

			if(bones != null)
			{
				newMeshData.bones = bones.Clone() as Transform[];
			}

			if(rootBone != null)
			{
				newMeshData.rootBone = rootBone;
			}

			if(umaBones != null)
			{
				newMeshData.umaBones = new UMATransform[umaBones.Length];
				Array.Copy(umaBones, newMeshData.umaBones, umaBones.Length);
			}

			newMeshData.umaBoneCount = umaBoneCount;
			newMeshData.rootBoneHash = rootBoneHash;

			if(boneNameHashes != null)
			{
				newMeshData.boneNameHashes = new int[boneNameHashes.Length];
				Array.Copy(boneNameHashes, newMeshData.boneNameHashes, boneNameHashes.Length);
			}

			newMeshData.subMeshCount = subMeshCount;
			newMeshData.vertexCount = vertexCount;
			newMeshData.RootBoneName = RootBoneName;

			return newMeshData;
		}

#if UNITY_EDITOR
		internal string Validate()
        {
			StringBuilder errors = new StringBuilder();

			if (vertices.Length != vertexCount)
            {
				errors.Append("; Vertices length of " + vertices.Length+" != vertexCount (" +vertexCount+ ")");
            }
			if (normals != null && normals.Length != vertices.Length)
            {
				errors.Append("; Normals length of " + normals.Length + " != vertexCount (" + vertexCount + ")"); 
			}
			if (tangents != null && tangents.Length != vertices.Length)
			{
				errors.Append("; tangents length of " + tangents.Length + " != vertexCount (" + vertexCount + ")");
			}
			if (colors32 != null && colors32.Length != vertices.Length)
			{
				errors.Append("; colors32 length of " + colors32.Length + " != vertexCount (" + vertexCount + ")");
			}
			if (uv != null && uv.Length != vertices.Length)
			{
				errors.Append("; uv length of " + tangents.Length + " != vertexCount (" + vertexCount + ")");
			}
			if (uv2 != null && uv2.Length != vertices.Length)
			{
				errors.Append("; uv2 length of " + uv2.Length + " != vertexCount (" + vertexCount + ")");
			}
			if (ManagedBonesPerVertex != null && ManagedBonesPerVertex.Length != vertices.Length)
			{
				errors.Append("; ManagedBonesPerVertex length of " + ManagedBonesPerVertex.Length + " != vertexCount (" + vertexCount + ")");
			}

			if (ManagedBoneWeights != null)
            {
				foreach(BoneWeight1 bw in ManagedBoneWeights)
                {
					if (bw.boneIndex >= bones.Length)
                    {
						errors.Append("; Boneweight references invalid bone index "+bw.boneIndex);
						break;
                    }
                }
            }

			if (umaBones.Length != umaBoneCount)
            {
				errors.Append("; umaBones != umaBoneCount");
            }

			if (submeshes == null)
			{
				errors.Append("; Meshdata has no submeshes");
			}
			else
			{
				if (submeshes.Length == 0)
				{
					errors.Append("; Meshdata submesh length == 0");
				}
				else
				{
					for (int i = 0; i < submeshes.Length; i++)
					{
						SubMeshTriangles smt = submeshes[i];
						foreach(int tri in smt.triangles)
                        {
							if (tri >= vertexCount)
                            {
								errors.Append("; triangle "+tri+" out of bounds on submesh " + i);
								break;
                            }
                        }
					}
				}
			}

			if (errors.Length > 0)
            {
				errors.Remove(0, 2);
            }
			return errors.ToString();
		}
#endif
	}
}
