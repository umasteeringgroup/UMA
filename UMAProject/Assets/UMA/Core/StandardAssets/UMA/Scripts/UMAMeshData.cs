#if !UNITY_STANDALONE
#undef USE_UNSAFE_CODE
#endif

using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.Dynamics;
using Unity.Collections;
using UnityEngine.Serialization;
using System.Text;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;

namespace UMA
{
    [Serializable]
    /// <summary>
    /// UMA version of Unity mesh triangle data.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SubMeshTriangles
	{
		[SerializeField]
		private int[] triangles;

		public int[] getBaseTriangles()
		{
			return triangles;
		}

		public void SetTriangles(int[] tris)
		{
			triangles = tris;
			if (nativeTriangles.IsCreated)
			{
				nativeTriangles.Dispose();
			}
		}

		public NativeArray<int> nativeTriangles;

		public NativeArray<int> GetTriangles()
        {
			if (nativeTriangles.IsCreated == false)
		    {
				nativeTriangles = new NativeArray<int>(triangles,Allocator.Persistent);
            }
			return nativeTriangles;
		}
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
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
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
            {
                deltaNormals = new Vector3[vertexCount];
            }
            else
            {
                deltaNormals = new Vector3[0];
            }

            if (hasTangents)
            {
                deltaTangents = new Vector3[vertexCount];
            }
            else
            {
                deltaTangents = new Vector3[0];
            }
        }

		public bool HasNormals()
		{
			if (deltaNormals != null && deltaNormals.Length > 0)
            {
                return true;
            }

            return false;
		}

		public bool HasTangents()
		{
			if (deltaTangents != null && deltaTangents.Length > 0)
            {
                return true;
            }

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
            {
                return true;
            }

            if (deltas.Length > 0)
            {
                return false;
            }

#if !ASSUME_EXPORTERS_KNOW_WHAT_THEY_ARE_DOING
            return true;
#else
			for(int i = 0; i < deltas.Length; i++)
			{
				if (deltas[i].sqrMagnitude > 0.0001f)
					return false;
			}

			return true;
#endif
		}
#if UNITY_EDITOR
		public UMABlendFrame Duplicate()
		{
			UMABlendFrame uMABlendFrame = new UMABlendFrame();
            uMABlendFrame.frameWeight = frameWeight;

            uMABlendFrame.deltaVertices = new Vector3[deltaVertices.Length];
            for (int i = 0; i < deltaVertices.Length; i++)
            {
                uMABlendFrame.deltaVertices[i] = deltaVertices[i];

            }

            if (deltaNormals != null)
            {
                uMABlendFrame.deltaNormals = new Vector3[deltaNormals.Length];
                for (int i = 0; i < deltaNormals.Length; i++)
                {
                    uMABlendFrame.deltaNormals[i] = deltaNormals[i];
                }

            }

            if (deltaTangents != null)
            {
                uMABlendFrame.deltaTangents = new Vector3[deltaTangents.Length];
                for (int i = 0; i < deltaTangents.Length; i++)
                {
                    uMABlendFrame.deltaTangents[i] = deltaTangents[i];
                }
            }
            return uMABlendFrame;
        }

		public UMABlendFrame DuplicateAndTranslate(Dictionary<int, int> NewVertstoOurVerts, int NumNewVerts)
		{
            UMABlendFrame uMABlendFrame = new UMABlendFrame();
            uMABlendFrame.frameWeight = frameWeight;

            uMABlendFrame.deltaVertices = new Vector3[NumNewVerts];
            for (int i = 0; i < NumNewVerts; i++)
            {
                uMABlendFrame.deltaVertices[i] = deltaVertices[NewVertstoOurVerts[i]];
            }

			if (deltaNormals != null && deltaNormals.Length > 0)
			{
                uMABlendFrame.deltaNormals = new Vector3[NumNewVerts];
                for (int i = 0; i < NumNewVerts; i++)
				{
					uMABlendFrame.deltaNormals[i] = deltaNormals[NewVertstoOurVerts[i]];
                }
            }

            if (deltaTangents != null && deltaTangents.Length > 0)
            {
                uMABlendFrame.deltaTangents = new Vector3[NumNewVerts];
                for (int i = 0; i < NumNewVerts; i++)
                {
                    uMABlendFrame.deltaTangents[i] = deltaTangents[NewVertstoOurVerts[i]];
                }
            }

            return uMABlendFrame;
        }
#endif

    }

	[Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class UMABlendShape
	{
		public string shapeName;
		public UMABlendFrame[] frames;

#if UNITY_EDITOR
		public UMABlendShape Duplicate()
		{
            UMABlendShape newShape = new UMABlendShape();
			newShape.shapeName = shapeName;
            newShape.frames = new UMABlendFrame[frames.Length];
            for (int i = 0; i < frames.Length; i++)
            {
                newShape.frames[i] = frames[i].Duplicate();
            }
			return newShape;
        }

		public UMABlendShape DuplicateAndTranslate(Dictionary<int,int> newVertsToOurverts)
        {
            UMABlendShape newShape = new UMABlendShape();
            newShape.shapeName = shapeName;
            newShape.frames = new UMABlendFrame[frames.Length];
            for (int i = 0; i < frames.Length; i++)
            {

				newShape.frames[i] = frames[i].DuplicateAndTranslate(newVertsToOurverts, newVertsToOurverts.Count);
            }
            return newShape;
        }
#endif
    }


	public class MeshDetails
	{
		public Vector3[] vertices;
		public Vector3[] normals;
		public Vector4[] tangents;
		public Color32[] colors32;
		public Vector2[] uv;
		public Vector2[] uv2;
		public Vector2[] uv3;
		public Vector2[] uv4;
		public bool verticesModified;
		public bool normalsModified;
		public bool tangentsModified;
		public bool colors32Modified;
		public bool uvModified;
		public bool uv2Modified;
		public bool uv3Modified;
		public bool uv4Modified;

		public MeshDetails ShallowCopy()
		{
			MeshDetails copy = new MeshDetails();
			copy.vertices = vertices;
			copy.normals = normals;
			copy.tangents = tangents;
			copy.colors32 = colors32;
			copy.uv = uv;
			copy.uv2 = uv2;
			copy.uv3 = uv3;
			copy.uv4 = uv4;
			copy.verticesModified = false;
			copy.normalsModified = false;
			copy.tangentsModified = false;
			copy.colors32Modified = false;
			copy.uvModified = false;
			copy.uv2Modified = false;
			copy.uv3Modified = false;
			copy.uv4Modified = false;
			return copy;
		}
	}
    /// <summary>
    /// UMA version of Unity mesh data.
    /// </summary>
    [Serializable]
	//[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public class UMAMeshData : MeshDetails
	{
#if UNITY_EDITOR
		public string ID = "Base";
		public int iteration = 0;
#endif
		public Matrix4x4[] bindPoses;
		public UMABoneWeight[] boneWeights;
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

        public Vector3[] GetVertices()
		{
            return vertices;
        }

        // They forgot the List<> method for bone weights.
#if USE_UNSAFE_CODE
		static BoneWeight[] gBoneWeightsArray = new BoneWeight[MAX_VERTEX_COUNT];
#endif

        public static Dictionary<int, NativeArray<int>> SubmeshBuffers = new Dictionary<int, NativeArray<int>>();

		public int BoneWeightOffset(int vertexIndex)
        {
            int offset = 0;
            for (int i = 0; i < vertexIndex; i++)
            {
                offset += ManagedBonesPerVertex[i];
            }
            return offset;
        }

        static UMAMeshData()
		{
			AppDomain.CurrentDomain.DomainUnload += CurrentDomain_DomainUnload;
		}

        private static void CurrentDomain_DomainUnload(object sender, EventArgs e)
        {
			CleanupGlobalBuffers();
        }

        public static void CleanupGlobalBuffers()
        {
			foreach (var d in SubmeshBuffers.Values)
			{
				if (d.IsCreated)
				{
					d.Dispose();
				}
			}
			SubmeshBuffers = new Dictionary<int, NativeArray<int>>();
        }

		public void MirrorU(int channel)
		{
            if (channel == 0)
            {
                for (int i = 0; i < uv.Length; i++)
                {
                    uv[i].x = 1 - uv[i].x;
                }
            }
            else if (channel == 1)
            {
                for (int i = 0; i < uv2.Length; i++)
                {
                    uv2[i].x = 1 - uv2[i].x;
                }
            }
            else if (channel == 2)
            {
                for (int i = 0; i < uv3.Length; i++)
                {
                    uv3[i].x = 1 - uv3[i].x;
                }
            }
            else if (channel == 3)
            {
                for (int i = 0; i < uv4.Length; i++)
                {
                    uv4[i].x = 1 - uv4[i].x;
                }
            }
        }

		public void MirrorV(int channel)
        {
            if (channel == 0)
            {
                for (int i = 0; i < uv.Length; i++)
                {
                    uv[i].y = 1 - uv[i].y;
                }
            }
            else if (channel == 1)
            {
                for (int i = 0; i < uv2.Length; i++)
                {
                    uv2[i].y = 1 - uv2[i].y;
                }
            }
            else if (channel == 2)
            {
                for (int i = 0; i < uv3.Length; i++)
                {
                    uv3[i].y = 1 - uv3[i].y;
                }
            }
            else if (channel == 3)
            {
                for (int i = 0; i < uv4.Length; i++)
                {
                    uv4[i].y = 1 - uv4[i].y;
                }
            }
        }

		public void MirrorUV(int Channel)
		{
            // mirror both X and Y
            if (Channel == 0)
            {
                for (int i = 0; i < uv.Length; i++)
                {
                    uv[i].x = 1 - uv[i].x;
                    uv[i].y = 1 - uv[i].y;
                }
            }
            else if (Channel == 1)
            {
                for (int i = 0; i < uv2.Length; i++)
                {
                    uv2[i].x = 1 - uv2[i].x;
                    uv2[i].y = 1 - uv2[i].y;
                }
            }
            else if (Channel == 2)
            {
                for (int i = 0; i < uv3.Length; i++)
                {
                    uv3[i].x = 1 - uv3[i].x;
                    uv3[i].y = 1 - uv3[i].y;
                }
            }
            else if (Channel == 3)
            {
                for (int i = 0; i < uv4.Length; i++)
                {
                    uv4[i].x = 1 - uv4[i].x;
                    uv4[i].y = 1 - uv4[i].y;
                }
            }
        }

        /// <summary>
        /// Get an array for submesh triangle data.
        /// </summary>
        /// <returns>Either a shared or allocated int array for submesh triangles.</returns>
        public NativeArray<int> GetSubmeshBuffer(int size, int submeshIndex)
		{
			if (size < 1)
            {
				throw new Exception("Submesh has 0 size!!! Submesh: " + submeshIndex);
            }
			if (!SubmeshBuffers.ContainsKey(submeshIndex))
            {
				var n = new NativeArray<int>(size,Allocator.Persistent);
				SubmeshBuffers.Add(submeshIndex, n);
            }
			if (SubmeshBuffers[submeshIndex].Length < size)
            {
				if (SubmeshBuffers[submeshIndex].IsCreated)
				{
					SubmeshBuffers[submeshIndex].Dispose();
				}
				SubmeshBuffers[submeshIndex] = new NativeArray<int>(size, Allocator.Persistent);
			}

			return SubmeshBuffers[submeshIndex].GetSubArray(0, size);
			//return new int[size];
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
		public void RetrieveDataFromUnityMesh(SkinnedMeshRenderer renderer, int submeshIndex, bool udimAdjustment = false)
		{
			RetrieveDataFromUnityMesh(renderer.sharedMesh, udimAdjustment, submeshIndex);

			UpdateBones(renderer.rootBone, renderer.bones);
		}

        static T[] RemapArray<T>(ICollection<int> map, T[] src)
        {
            if (src == null || map == null || src.Length == 0)
                return src;

            var ret = new T[map.Count];
            var dstI = 0;
            foreach (var srcI in map)
                ret[dstI++] = src[srcI];

            return ret;
        }

		
        /// <summary>
        /// Initialize UMA mesh data from Unity mesh.
        /// </summary>
        /// <param name="sharedMesh">Source mesh.</param>
        public void RetrieveDataFromUnityMesh(Mesh sharedMesh, bool udimAdjustment=false, int subMeshInd = -1)
        {

            if (subMeshInd >= sharedMesh.subMeshCount)
            {
                Debug.LogError("Requested submesh index is invalid");
                subMeshInd = -1;
            }

            SortedSet<int> vertRemap = null;

            if (subMeshInd < 0 || sharedMesh.subMeshCount == 1)
            {
                vertexCount = sharedMesh.vertexCount;
                subMeshCount = sharedMesh.subMeshCount;
                submeshes = new SubMeshTriangles[subMeshCount];
                for (int i = 0; i < subMeshCount; i++)
                {
                    submeshes[i].SetTriangles(sharedMesh.GetTriangles(i));
                }
            }
            else
            {
                subMeshCount = 1;
                submeshes = new SubMeshTriangles[subMeshCount];
                var tris = sharedMesh.GetTriangles(subMeshInd);

                vertRemap = new SortedSet<int>(tris);
                var indRemap = new int[sharedMesh.vertexCount];

                vertexCount = 0;
                foreach (var vi in vertRemap)
                    indRemap[vi] = vertexCount++;

                for (var ii = 0; ii < tris.Length; ii++)
                    tris[ii] = indRemap[tris[ii]];
                submeshes[0].SetTriangles(tris);
            }

            bindPoses = sharedMesh.bindposes;
#if USE_NATIVE_ARRAYS
			unityBonesPerVertex =  sharedMesh.GetBonesPerVertex();
			unityBoneWeights = sharedMesh.GetAllBoneWeights();
			SerializedBoneWeights = vertRemap,unityBoneWeights.ToArray();
			SerializedBonesPerVertex = vertRemap,unityBonesPerVertex.ToArray();
			
			!you need to remap these!
#else
            var unityBonesPerVertex = sharedMesh.GetBonesPerVertex();
            var unityBoneWeights = sharedMesh.GetAllBoneWeights();
            ManagedBonesPerVertex = RemapArray(vertRemap, unityBonesPerVertex.ToArray());

            if (vertRemap == null || unityBoneWeights.Length == 0)
            {
                ManagedBoneWeights = unityBoneWeights.ToArray();
            }
            else
            {
                List<BoneWeight1> boneWeights = new List<BoneWeight1>();

                int srcI = 0, boneWI = 0;
                foreach (var nextI in vertRemap)
                {
                    for (; srcI < nextI; srcI++)
                        boneWI += unityBonesPerVertex[srcI];

                    var boneC = unityBonesPerVertex[nextI];
                    for (; boneC-- > 0;)
                        boneWeights.Add(unityBoneWeights[boneWI++]);

                    srcI++;
                }
                ManagedBoneWeights = boneWeights.ToArray();
            }

            //if (unityBonesPerVertex.IsCreated)
            //	unityBonesPerVertex.Dispose();
            //if (unityBoneWeights.IsCreated)
            //	unityBoneWeights.Dispose();
#endif

            vertices = RemapArray(vertRemap, sharedMesh.vertices);
            // vertexCount = vertices.Length;
            normals = RemapArray(vertRemap, sharedMesh.normals);
            tangents = RemapArray(vertRemap, sharedMesh.tangents);
            colors32 = RemapArray(vertRemap, sharedMesh.colors32);
            uv = RemapArray(vertRemap, sharedMesh.uv);
            uv2 = RemapArray(vertRemap, sharedMesh.uv2);
            uv3 = RemapArray(vertRemap, sharedMesh.uv3);
            uv4 = RemapArray(vertRemap, sharedMesh.uv4);
            if (udimAdjustment)
            {
                UMAUtils.UDIMAdjustUV(uv, uv);
                UMAUtils.UDIMAdjustUV(uv2, uv2);
                UMAUtils.UDIMAdjustUV(uv3, uv3);
                UMAUtils.UDIMAdjustUV(uv4, uv4);
            }

            //Create the blendshape data on the slot asset from the unity mesh
            #region Blendshape
            blendShapes = new UMABlendShape[sharedMesh.blendShapeCount];

            for (int shapeIndex = 0; shapeIndex < sharedMesh.blendShapeCount; shapeIndex++)
            {
                blendShapes[shapeIndex] = new UMABlendShape();
                blendShapes[shapeIndex].shapeName = sharedMesh.GetBlendShapeName(shapeIndex);

                int frameCount = sharedMesh.GetBlendShapeFrameCount(shapeIndex);
                blendShapes[shapeIndex].frames = new UMABlendFrame[frameCount];

                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    var deltaVertices = new Vector3[sharedMesh.vertexCount];
                    var deltaNormals = new Vector3[sharedMesh.vertexCount];
                    var deltaTangents = new Vector3[sharedMesh.vertexCount];

                    bool hasNormals = false;
                    bool hasTangents = false;

                    //Get the delta arrays first so we can determine if we don't need the delta normals or the delta tangents.
                    sharedMesh.GetBlendShapeFrameVertices(shapeIndex, frameIndex, deltaVertices, deltaNormals, deltaTangents);

                    deltaVertices = RemapArray(vertRemap, deltaVertices);
                    deltaNormals = RemapArray(vertRemap, deltaNormals);
                    deltaTangents = RemapArray(vertRemap, deltaTangents);

                    if (!UMABlendFrame.isAllZero(deltaNormals))
                        hasNormals = true;

                    if (!UMABlendFrame.isAllZero(deltaTangents))
                        hasTangents = true;

                    blendShapes[shapeIndex].frames[frameIndex] = new UMABlendFrame();
                    blendShapes[shapeIndex].frames[frameIndex].frameWeight = sharedMesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex);

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
        /// Initialize UMA mesh data from Unity mesh.
        /// </summary>
        /// <param name="sharedMesh">Source mesh.</param>
        public void OldetrieveDataFromUnityMesh(Mesh sharedMesh, bool udimAdjustment = false)
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
				submeshes[i].SetTriangles(sharedMesh.GetTriangles(i));
			}
			if (udimAdjustment)
			{
                UMAUtils.UDIMAdjustUV(uv, sharedMesh.uv);
                UMAUtils.UDIMAdjustUV(uv2, sharedMesh.uv2);
                UMAUtils.UDIMAdjustUV(uv3, sharedMesh.uv3);
                UMAUtils.UDIMAdjustUV(uv4, sharedMesh.uv4);
            }
            //SubMeshDescriptor subMeshDescriptor = new SubMeshDescriptor(0, submeshes[i].GetTriangles().Length, MeshTopology.Triangles);
            //mesh.SetSubMesh(i, subMeshDescriptor);
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
                    {
                        hasNormals = true;
                    }

                    if (!UMABlendFrame.isAllZero(deltaTangents))
                    {
                        hasTangents = true;
                    }

                    blendShapes [shapeIndex].frames [frameIndex] = new UMABlendFrame ();
					blendShapes[shapeIndex].frames[frameIndex].frameWeight = sharedMesh.GetBlendShapeFrameWeight( shapeIndex, frameIndex );

					blendShapes[shapeIndex].frames[frameIndex].deltaVertices = deltaVertices;
					if (hasNormals)
                    {
                        blendShapes[shapeIndex].frames[frameIndex].deltaNormals = deltaNormals;
                    }

                    if (hasTangents)
                    {
                        blendShapes[shapeIndex].frames[frameIndex].deltaTangents = deltaTangents;
                    }
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
            {
                SkinnedMeshCombiner.ConvertData(ref clothSkinning[i], ref clothSkinningSerialized[i]);
            }
        }

		/// <summary>
		/// Validates the skinned transform hierarchy.
		/// </summary>
		/// <param name="rootBone">Root transform.</param>
		/// <param name="bones">Transforms.</param>
		public void UpdateBones(Transform rootBone, Transform[] bones)
		{
			Transform lastBone = null;
			rootBone = FindRoot(rootBone, bones);
			
			var requiredBones = new Dictionary<Transform, UMATransform>();
            for (int i = 0; i < bones.Length; i++)
			{
                Transform bone = bones[i];
                if (bone == null)    
                {
					if (Debug.isDebugBuild)
                    {
						if (lastBone != null)
                        {
							Debug.Log("Bone is null updating skinned mesh. Last good bone is " + lastBone.name);
                        }
						else
                        {
							Debug.Log("Bone is null updating skinned mesh. Last good bone is null");
						}
					}
					continue;
                }
				lastBone = bone;
				if (requiredBones.ContainsKey(bone))
                {
                    continue;
                }

                var boneIterator = bone.parent;
				var boneIteratorChild = bone;
				var boneHash = UMAUtils.StringToHash(boneIterator.name);
				var childHash = UMAUtils.StringToHash(boneIteratorChild.name);
				while (boneIteratorChild != rootBone)
				{
					requiredBones.Add(boneIteratorChild, new UMATransform(boneIteratorChild, childHash, boneHash));
					if (requiredBones.ContainsKey(boneIterator))
                    {
                        break;
                    }

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
			if (bone.name == raceRoot)
            {
                return bone;
            }

            for (int i = 0; i < bone.childCount; i++)
			{
				var result = RecursiveFindBone(bone.GetChild(i), raceRoot);
				if (result != null)
                {
                    return result;
                }
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
            {
                rootBone = rootBone.parent;
            }

            return RecursiveFindBone(rootBone, RootBoneName);
		}

		/// <summary>
		/// Applies the data to a Unity mesh.
		/// </summary>
		/// <param name="renderer">Target renderer.</param>
		/// <param name="skeleton">Skeleton.</param>
		public void ApplyDataToUnityMesh(SkinnedMeshRenderer renderer, UMASkeleton skeleton, UMAData umaData)
		{
			var recipe = umaData.umaRecipe;
			if (renderer == null)
			{
				if (Debug.isDebugBuild)
                {
                    Debug.LogError("Renderer is null!");
                }

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
                {
                    Debug.LogError("Cannot apply changes to prefab!");
                }
            }
			if (mesh != null)
			{
				if (UnityEditor.AssetDatabase.IsSubAsset(mesh))
				{
					if (Debug.isDebugBuild)
                    {
                        Debug.LogError("Cannot apply changes to asset mesh!");
                    }
                }
			}
#endif
			mesh.subMeshCount = 1;
			mesh.triangles = new int[0];

				NativeArray<Vector3> verts = new NativeArray<Vector3>(vertices, Allocator.Temp);
				 
				mesh.SetVertices(verts);

				SetBoneWeightsFromMeshData(mesh);                //mesh.boneWeights = unityBoneWeights != null ? unityBoneWeights : UMABoneWeight.Convert(boneWeights);

				mesh.normals = normals;
				mesh.tangents = tangents;
				mesh.uv = uv;
				mesh.uv2 = uv2;
				mesh.uv3 = uv3;
				mesh.uv4 = uv4;
				mesh.colors32 = colors32;

			mesh.bindposes = bindPoses;

#if true
			var subMeshCount = submeshes.Length;
			mesh.subMeshCount = subMeshCount;
			var Descriptors = new SubMeshDescriptor[subMeshCount];
			for (int i = 0; i < subMeshCount; i++)
			{
                mesh.SetIndices(submeshes[i].GetTriangles(),MeshTopology.Triangles,i);
			}
#else
			// TODO: Gather all the triangles and vertexes here into a native arrays and then set them all at once.
			int[] triangles = new int[0];

            mesh.Clear();

            mesh.SetVertexBufferParams(vertices.Length, GetVertexLayout());
            mesh.SetVertexBufferData(vertices, 0, 0, vertexCount);

            mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
            mesh.SetIndexBufferData(meshData.Indices.AsArray(), 0, 0, indexCount);
            mesh.SetIndexBufferData(triangles,0,0, triangles.Length, MeshUpdateFlags.DontValidateIndices);
			mesh.SetIndexBufferParams(triangles.Length, UnityEngine.Rendering.IndexFormat.UInt32);

            var subMeshCount = submeshes.Length;
            mesh.subMeshCount = subMeshCount;
            var Descriptors = new SubMeshDescriptor[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
            {
                Descriptors[i] = new SubMeshDescriptor(0, submeshes[i].GetTriangles().Length, MeshTopology.Triangles);
            }
            mesh.SetSubMeshes(Descriptors, 0, subMeshCount,MeshUpdateFlags.DontValidateIndices);
#endif            
			//SubMeshDescriptor subMeshDescriptor = new SubMeshDescriptor(0, submeshes[i].GetTriangles().Length, MeshTopology.Triangles);
			//mesh.SetSubMesh(i, subMeshDescriptor);

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

					int frameIndex = 0;

					//if (blendshapeS)
					if (!umaData.blendShapeSettings.loadAllFrames)
					{
						frameIndex = blendShapes[shapeIndex].frames.Length - 1;
                    }

					for (; frameIndex < blendShapes[shapeIndex].frames.Length; frameIndex++)
					{
						//There might be an extreme edge case where someone has the same named blendshapes on different meshes that end up on different renderers.
						string name = blendShapes[shapeIndex].shapeName;

						float frameWeight = blendShapes[shapeIndex].frames[frameIndex].frameWeight;
						Vector3[] deltaVertices = blendShapes[shapeIndex].frames[frameIndex].deltaVertices;
						Vector3[] deltaNormals = blendShapes[shapeIndex].frames[frameIndex].deltaNormals;
						Vector3[] deltaTangents = blendShapes[shapeIndex].frames[frameIndex].deltaTangents;

						if (UMABlendFrame.isAllZero(deltaNormals))
                        {
                            deltaNormals = null;
                        }

                        if (UMABlendFrame.isAllZero(deltaTangents))
                        {
                            deltaTangents = null;
                        }

						try
						{
                        mesh.AddBlendShapeFrame(name, frameWeight, deltaVertices, deltaNormals, deltaTangents);
						}
						catch(Exception ex)
						{
                            Debug.LogError("Error adding blendshape frame: " + ex.Message);
                        }
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

        private VertexAttributeDescriptor[] GetVertexLayout()
        {
			List<VertexAttributeDescriptor> list = new List<VertexAttributeDescriptor>();
			list.Add(new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 4, stream: 0));
			list.Add(new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 4, stream: 1));
			list.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, stream: 2));
		//	list.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, stream: 3));
			list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4, stream: 4));
			list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4, stream: 5));
		//	list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4, stream: 6));
		//	list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4, stream: 7));
			list.Add(new VertexAttributeDescriptor(VertexAttribute.BlendWeight, VertexAttributeFormat.Float32, 4, stream: 8));
			list.Add(new VertexAttributeDescriptor(VertexAttribute.BlendIndices, VertexAttributeFormat.Float32, 4, stream: 9));
			return list.ToArray();
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
					Debug.LogError("Error! Boneweights and BonesPerVertex is invalid. The slot must be regenerated. ("+this.SlotName+")");
					return;
                }
                var unityBonesPerVertex = new NativeArray<byte>(ManagedBonesPerVertex, Allocator.Persistent);
                var unityBoneWeights = new NativeArray<BoneWeight1>(ManagedBoneWeights, Allocator.Persistent);
#if validateBoneWeights
				string theSlot = this.SlotName;
				if (mesh.vertexCount == 0)
                {
					Debug.LogError("Mesh has no vertexes setting bone weights!");
                }
				if (mesh.vertexCount != ManagedBonesPerVertex.Length)
				{
					Debug.LogError("Error validating ManagedBonesPerVertex - count and vertex count do not match!");
				}

				for(int i=0;i<ManagedBonesPerVertex.Length;i++)
                {
					if (ManagedBonesPerVertex[i] == 0)
                    {
						Debug.LogError("Boneweights are invalid - " + i + " has no weights!");
						break;
                    }
                }

				if (mesh.vertexCount != ManagedBonesPerVertex.Length)
				{
					Debug.LogError("Error validating ManagedBonesPerVertex - count and vertex count do not match!");
				}


#endif
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

		public Mesh ToUnityMesh()
        {
			Mesh mesh = new Mesh();
			mesh.vertices = vertices;
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
                int[] tris = submeshes[i].getBaseTriangles();
                mesh.SetTriangles(tris, i);
			}
			return mesh;
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
				//mesh.SetTriangles(submeshes[i].triangles, i);
				mesh.SetIndices(submeshes[i].GetTriangles(), MeshTopology.Triangles, i);
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

            for (int i = 0; i < boneWeights.Length; i++)
            {
                UMABoneWeight bw = boneWeights[i];
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
            for (int i = 0; i < submeshes.Length; i++)
            {
                SubMeshTriangles sm = submeshes[i];
                if (sm.nativeTriangles.IsCreated)
                {
                    sm.nativeTriangles.Dispose();
                }
            }

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

		private void ComputeBoneNameHashes(Transform[] bones)
		{
			boneNameHashes = new int[bones.Length];
			for (int i = 0; i < bones.Length; i++)
			{
                try
                {
                    boneNameHashes[i] = UMAUtils.StringToHash(bones[i].name);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error computing hash for bone {i} : " + ex.Message);
                }
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


		public UMAMeshData ShallowCopy(Vector3[] ReplacementVerts)
		{
			UMAMeshData newMeshData = new UMAMeshData();

			newMeshData.SlotName = SlotName;

			if (ReplacementVerts != null)
			{
				newMeshData.vertices = ReplacementVerts;
			}
			else
			{
				newMeshData.vertices = vertices;
			}

			newMeshData.ManagedBonesPerVertex = ManagedBonesPerVertex;
			newMeshData.ManagedBoneWeights = ManagedBoneWeights;
			newMeshData.bindPoses = bindPoses;
			newMeshData.normals = normals;
			newMeshData.tangents = tangents;
			newMeshData.colors32 = colors32;
			newMeshData.uv = uv;
			newMeshData.uv2 = uv2;
			newMeshData.uv3 = uv3;
			newMeshData.uv4 = uv4;
			newMeshData.blendShapes = blendShapes;
			newMeshData.clothSkinning = clothSkinning;
			newMeshData.clothSkinningSerialized = clothSkinningSerialized;
			newMeshData.submeshes = submeshes;
			newMeshData.bones = bones;
			newMeshData.rootBone = rootBone;
			newMeshData.umaBones = umaBones;
			newMeshData.umaBoneCount = umaBoneCount;
			newMeshData.rootBoneHash = rootBoneHash;
			newMeshData.boneNameHashes = boneNameHashes;
			newMeshData.subMeshCount = subMeshCount;
			newMeshData.vertexCount = vertexCount;
			newMeshData.RootBoneName = RootBoneName;

			return newMeshData;
		}


		public UMAMeshData ShallowClearCopy()
        {
            UMAMeshData newMeshData = new UMAMeshData();

            newMeshData.SlotName = SlotName; 
            newMeshData.vertices = vertices;
            newMeshData.ManagedBonesPerVertex = ManagedBonesPerVertex;
            newMeshData.ManagedBoneWeights = ManagedBoneWeights;
            newMeshData.bindPoses = bindPoses;
            newMeshData.normals = normals;
            newMeshData.tangents = tangents;
            newMeshData.colors32 = colors32;
            newMeshData.uv = uv;
            newMeshData.uv2 = uv2;
            newMeshData.uv3 = uv3;
            newMeshData.uv4 = uv4;
            newMeshData.blendShapes = blendShapes;
            newMeshData.clothSkinning = clothSkinning;
            newMeshData.clothSkinningSerialized = clothSkinningSerialized;
            newMeshData.submeshes = submeshes;
            newMeshData.bones = bones;
            newMeshData.rootBone = rootBone;
            newMeshData.umaBones = umaBones;
            newMeshData.umaBoneCount = umaBoneCount;
            newMeshData.rootBoneHash = rootBoneHash;
            newMeshData.boneNameHashes = boneNameHashes;
            newMeshData.subMeshCount = subMeshCount;
            newMeshData.vertexCount = vertexCount;
            newMeshData.RootBoneName = RootBoneName;


            newMeshData.verticesModified = false;
            newMeshData.normalsModified = false;
            newMeshData.tangentsModified = false;
            newMeshData.colors32Modified = false;
            newMeshData.uvModified = false;
            newMeshData.uv2Modified = false;
            newMeshData.uv3Modified = false;
            newMeshData.uv4Modified = false;

            return newMeshData;
        }
        /// <summary>
        /// Creates a deep copy of an UMAMeshData object.
        /// </summary>
        /// <returns>The new copy of the UMAMeshData</returns>
        public UMAMeshData DeepCopy()
		{
			UMAMeshData newMeshData = new UMAMeshData();

			newMeshData.SlotName = SlotName; 
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
				if (colors32.Length != 0)
				{
					errors.Append("; colors32 length of " + colors32.Length + " != vertexCount (" + vertexCount + ")");
				}
			}
			if (uv != null && uv.Length != vertices.Length)
			{
				errors.Append("; uv length of " + tangents.Length + " != vertexCount (" + vertexCount + ")");
			}
			if (uv2 != null && uv2.Length != vertices.Length)
			{
				if (uv2.Length != 0)
				{
                    errors.Append("; uv2 length of " + uv2.Length + " != vertexCount (" + vertexCount + ")");
                }
			}
			if (ManagedBonesPerVertex != null && ManagedBonesPerVertex.Length != vertices.Length)
			{
				errors.Append("; ManagedBonesPerVertex length of " + ManagedBonesPerVertex.Length + " != vertexCount (" + vertexCount + ")");
			}

            if (ManagedBoneWeights != null)
            {
                for (int i = 0; i < ManagedBoneWeights.Length; i++)
                {
                    BoneWeight1 bw = ManagedBoneWeights[i];
					if (bw == null)
					{
						errors.Append("; ManagedBoneWeights["+i+"] is null");
						break;
					}
					if (bones != null)
					{
                        if (bw.boneIndex >= bones.Length)
                        {
                            errors.Append("; Boneweight references invalid bone index " + bw.boneIndex);
                            break;
                        }
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
						if (smt.nativeTriangles != null && smt.nativeTriangles.IsCreated)
						{
							var tris = smt.GetTriangles();
							var tri = 0;
							for (int j = 0; i < tris.Length; i++)
							{
								tri = tris[j];
								if (tri >= vertexCount)
								{
									errors.Append("; triangle " + tri + " out of bounds on submesh " + i);
									break;
								}
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
