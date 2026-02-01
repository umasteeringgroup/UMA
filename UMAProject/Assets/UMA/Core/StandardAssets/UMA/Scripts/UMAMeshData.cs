//#define DEBUG_UNITY_MESHDATA
#if !UNITY_STANDALONE
#undef USE_UNSAFE_CODE
#endif

using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.Dynamics;
using Unity.Collections;
using UnityEngine.Serialization;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;
using UMA.PoseTools;
using System.Text;


namespace UMA
{
	[Serializable]
    public class UMALodRange
	{
		public uint offset;
		public uint count;
#if UNITY_6000_2_OR_NEWER
		public UMALodRange(MeshLodRange lodrange)
		{
			offset = lodrange.indexStart;
			count = lodrange.indexCount;
        }
#endif

        public UMALodRange(uint offset, uint count)
		{
			this.offset = offset;
			this.count = count;
		}

	}

    [Serializable]
	/// <summary>
	/// UMA version of Unity mesh triangle data.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public class SubMeshTriangles {
    public static int smtNumber;
    public int smtID;
    public List<UMALodRange> lodRanges;
		// Keep track of all allocated native arrays for proper disposal.
		public static List<SubMeshTriangles> nativeTrianglesAllocated = new List<SubMeshTriangles>();
    [SerializeField]
    private int[] triangles;
    public NativeArray<int> nativeTriangles;

    public SubMeshTriangles()
		{
			smtID = smtNumber++;
			triangles = null;
			nativeTriangles = default;
        }

        public SubMeshTriangles(int[] tris)
		{
			smtID = smtNumber++;
            triangles = tris;
			nativeTriangles = default;
        }

		public int[] GetBaseTriangles()
		{
			if (triangles == null)
			{
				return null;
			}

			// If this submesh has internal LODs, triangles contains appended LOD buffers.
			// LOD0 is stored as the first lodRanges entry.
			if (lodRanges != null && lodRanges.Count > 0)
			{
				var r = lodRanges[0];
				int start = (int)r.offset;
				int count = (int)r.count;
				if (start >= 0 && count > 0 && (start + count) <= triangles.Length)
				{
					return new ArraySegment<int>(triangles, start, count).ToArray();
				}
			}

			return triangles;
		}

		public void SetLodRanges(List<UMALodRange> ranges)
		{
			lodRanges = ranges;
        }


		public int LODCount()
		{
			if (lodRanges == null)
			{
				return 0;
            }
            return lodRanges.Count; 
		}

		public bool HasLODLevel(int lodLevel)
		{
			if (lodRanges == null)
			{
				return false;
			}
			return lodLevel >= 0 && lodLevel < lodRanges.Count;
		}

		public bool HasMultipleLODs()
		{
			return LODCount() > 1;
		}


        public int[] getManagedTriangles(int LODNumber)
		{
			if (lodRanges == null || lodRanges.Count == 0 || LODNumber < 0)
			{
				return triangles;
            }

			if (LODNumber >= lodRanges.Count)
			{
				LODNumber = lodRanges.Count - 1;
            }

			if (triangles == null)
			{
				return null;
			}

			int start = (int)lodRanges[LODNumber].offset;
			int count = (int)lodRanges[LODNumber].count;
			if (start < 0 || count <= 0)
			{
				return Array.Empty<int>();
			}
			if ((start + count) > triangles.Length)
			{
				// Invalid/stale/mismatched range; fall back to base triangles (avoids exceptions).
				return GetBaseTriangles();
			}

			return new ArraySegment<int>(triangles, start, count).ToArray();
		}

		public int GetTriangleCount(int lodLevel)
		{
			if (triangles == null)
			{
				return 0;
			}
			if (lodRanges == null || lodRanges.Count == 0 || lodLevel < 0)
			{
				return triangles.Length;
			}
			if (lodLevel >= lodRanges.Count)
			{
				lodLevel = lodRanges.Count - 1;
			}
			return (int)lodRanges[lodLevel].count;
		}

		public void DisposeNativeTriangles(bool remove)
		{
			if (nativeTriangles.IsCreated)
			{
#if DEBUG_UNITY_MESHDATA
				Debug.Log($"SMT {smtID} Disposing native triangles for submesh of size " + nativeTriangles.Length);
#endif
                nativeTriangles.Dispose();
                nativeTriangles = default;
#if DEBUG_UNITY_MESHDATA
				Debug.Log($"SMT {smtID} Native triangles disposed. IsCreated = " + nativeTriangles.IsCreated.ToString());
#endif
            }
            if (remove)
			{
				nativeTrianglesAllocated.Remove(this);
            }
		}

		public static void DisposeAllNativeTriangles()
		{
			for (int i = 0; i < nativeTrianglesAllocated.Count; i++)
			{
				SubMeshTriangles smt = nativeTrianglesAllocated[i];
				smt.DisposeNativeTriangles(false);
            }
			nativeTrianglesAllocated.Clear();
        }


		public void SetTriangles(int[] tris)
		{
#if DEBUG_UNITY_MESHDATA
            Debug.Log($"SMT {smtID} Setting triangles for submesh of size " + tris.Length);
#endif
			triangles = tris;
			DisposeNativeTriangles(true);
		}


		public UMALodRange GetLODRange(int LODNumber)
		{
			if (lodRanges == null || lodRanges.Count == 0 || LODNumber < 0)
			{
				return new UMALodRange(0,(uint)(triangles != null ? triangles.Length : 0));
			}
			if (LODNumber >= lodRanges.Count)
			{
				LODNumber = lodRanges.Count - 1;
			}
			return lodRanges[LODNumber];
        }


        public NativeArray<int> GetTriangles(int LODNumber)
		{
			// Ensure backing native array exists
			if (nativeTriangles.IsCreated == false)
			{
#if DEBUG_UNITY_MESHDATA
                Debug.Log($"SMT {smtID} Allocating native triangles for submesh of size " + (triangles != null ? triangles.Length : 0));
#endif
				nativeTriangles = (triangles != null && triangles.Length > 0) ? new NativeArray<int>(triangles, Allocator.Persistent) : default;
				if (nativeTriangles.IsCreated)
				{
					nativeTrianglesAllocated.Add(this);
				}
			}
			else
			{
#if DEBUG_UNITY_MESHDATA
                Debug.Log($"SMT {smtID} Using existing native triangles for submesh of size " + nativeTriangles.Length);
#endif
			}

			if (!nativeTriangles.IsCreated)
			{
				return default;
			}

			// If no LOD ranges, return full buffer
			if (lodRanges == null || lodRanges.Count == 0 || LODNumber < 0)
			{
				return nativeTriangles;
			}
			if (LODNumber >= lodRanges.Count)
			{
				LODNumber = lodRanges.Count - 1;
			}
			var lr = lodRanges[LODNumber];
			int start = (int)lr.offset;
			int count = (int)lr.count;
			// Clamp to avoid out-of-range
			start = Mathf.Clamp(start, 0, nativeTriangles.Length);
			count = Mathf.Clamp(count, 0, nativeTriangles.Length - start);
			return nativeTriangles.GetSubArray(start, count);
		}

		// Backward compatibility overloads (no-arg defaults to LOD 0)
		public NativeArray<int> GetTriangles()
		{
			return GetTriangles(0);
		}
		public int GetTriangleCount()
		{
			return GetTriangleCount(0);
		}
		public int[] getManagedTriangles()
		{
			return getManagedTriangles(0);
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


		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void StaticInitializeOnLoad()
		{
			TransformComparer = new UMATransformComparer();
		}
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
	/// This is only used for compatibility in UMA2.11
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
		public float frameWeight = 100.0f; //should be100% for one frame
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
			for(int i =0; i < deltas.Length; i++)
			{
				if (deltas[i].sqrMagnitude >0.0001f)
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

		public UMABlendShape DuplicateAndTranslate(Dictionary<int, int> newVertsToOurverts)
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
	//[StructLayout(LayoutKind.Sequential, Pack =1)]
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


		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void StaticInitializeOnLoad()
		{
			SubmeshBuffers = new Dictionary<int, NativeArray<int>>();
		}

		public int BoneWeightOffset(int vertexIndex)
		{
			int offset = 0;
			for (int i = 0; i < vertexIndex; i++)
			{
				offset += ManagedBonesPerVertex[i];
			}
			return offset;
		}

		public UMAMeshData otherApplyBonePose(UMABonePose bonePose)
		{
			// Backward-compatible wrapper: no pre-rotation
			return ApplyBonePose(bonePose, Matrix4x4.identity, false );
		}


        /// <summary>
        /// helper providing corrected post-rotation behavior for ApplyBonePose.
        /// Use: var result = meshData.ApplyBonePoseWithPostRotate(bonePose, rotationMatrix, postRotateFlag);
        /// If postRotateFlag == false: delegates to existing ApplyBonePose(bonePose, rotationMatrix, false) (pre-rotation).
        /// If postRotateFlag == true: applies bone pose first (no pre-rotation), then rotates baked vertices/normals/tangents.
        /// </summary>

            public UMAMeshData ApplyBonePoseWithPostRotate(
                UMABonePose bonePose,
                Matrix4x4 rotation,
                bool postRotate)
            {


                // If we are NOT post rotating, keep original pre-rotation behavior
                if (!postRotate)
                {
                    return ApplyBonePose(bonePose, rotation, false);
                }

                // When postRotate == true:
                // 1. Bake bone pose WITHOUT any pre-rotation (identity).
                // 2. Apply rotation to resulting geometry (vertices, normals, tangents) afterward.
                var baked = ApplyBonePose(bonePose, Matrix4x4.identity, false);

                // If rotation is identity or mesh has no vertices, just return baked.
                if (rotation == Matrix4x4.identity || baked.vertices == null || baked.vertexCount == 0)
                    return baked;

                var vCount = baked.vertexCount;
                var inVerts = baked.vertices;
                var inNormals = baked.normals;
                var inTangents = baked.tangents;

                var outVerts = new Vector3[vCount];
                Vector3[] outNormals = (inNormals != null && inNormals.Length == vCount) ? new Vector3[vCount] : null;
                Vector4[] outTangents = (inTangents != null && inTangents.Length == vCount) ? new Vector4[vCount] : null;

                for (int i = 0; i < vCount; i++)
                {
                    outVerts[i] = rotation.MultiplyPoint3x4(inVerts[i]);

                    if (outNormals != null)
                        outNormals[i] = rotation.MultiplyVector(inNormals[i]);

                    if (outTangents != null)
                    {
                        var t3 = new Vector3(inTangents[i].x, inTangents[i].y, inTangents[i].z);
                        var rt = rotation.MultiplyVector(t3).normalized;
                        outTangents[i] = new Vector4(rt.x, rt.y, rt.z, inTangents[i].w);
                    }
                }

                baked.vertices = outVerts;
                if (outNormals != null) baked.normals = outNormals;
                if (outTangents != null) baked.tangents = outTangents;

                baked.verticesModified = true;
                if (outNormals != null) baked.normalsModified = true;
                if (outTangents != null) baked.tangentsModified = true;

                baked.vertexCount = outVerts.Length;
                return baked;
            }
        
    

    // New overload: apply a pre-rotation to geometry before baking the bone pose
    public UMAMeshData OlderApplyBonePose(UMABonePose bonePose, Matrix4x4 preRotation, bool postRotate)
		{
			// If no pose, still allow pre-rotation only
			if (bonePose == null)
			{
				var copyOnly = DeepCopy();
				if (vertices == null || vertexCount == 0)
					return copyOnly;

				if (preRotation != Matrix4x4.identity)
				{
					var rotVerts = new Vector3[vertexCount];
					Vector3[] rotNormals = (normals != null && normals.Length == vertexCount) ? new Vector3[vertexCount] : null;
					Vector4[] rotTangents = (tangents != null && tangents.Length == vertexCount) ? new Vector4[vertexCount] : null;
					for (int i = 0; i < vertexCount; i++)
					{
						rotVerts[i] = preRotation.MultiplyPoint3x4(vertices[i]);
						if (rotNormals != null) rotNormals[i] = preRotation.MultiplyVector(normals[i]);
						if (rotTangents != null)
						{
							var t3 = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
							var rt = preRotation.MultiplyVector(t3).normalized;
							rotTangents[i] = new Vector4(rt.x, rt.y, rt.z, tangents[i].w);
						}
					}
					copyOnly.vertices = rotVerts;
					if (rotNormals != null) copyOnly.normals = rotNormals;
					if (rotTangents != null) copyOnly.tangents = rotTangents;
					copyOnly.verticesModified = true;
					if (rotNormals != null) copyOnly.normalsModified = true;
					if (rotTangents != null) copyOnly.tangentsModified = true;
					copyOnly.vertexCount = rotVerts.Length;
				}
				return copyOnly;
			}

			// Validate required data (still apply pre-rotation only if skinning data missing)
			if (vertices == null || vertexCount == 0 || bindPoses == null || bindPoses.Length == 0 || boneNameHashes == null || boneNameHashes.Length == 0)
			{
				var copyOnly = DeepCopy();
				if (vertices == null || vertexCount == 0)
					return copyOnly;

				if (preRotation != Matrix4x4.identity)
				{
					var rotVerts = new Vector3[vertexCount];
					Vector3[] rotNormals = (normals != null && normals.Length == vertexCount) ? new Vector3[vertexCount] : null;
					Vector4[] rotTangents = (tangents != null && tangents.Length == vertexCount) ? new Vector4[vertexCount] : null;
					for (int i = 0; i < vertexCount; i++)
					{
						rotVerts[i] = preRotation.MultiplyPoint3x4(vertices[i]);
						if (rotNormals != null) rotNormals[i] = preRotation.MultiplyVector(normals[i]);
						if (rotTangents != null)
						{
							var t3 = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
							var rt = preRotation.MultiplyVector(t3).normalized;
							rotTangents[i] = new Vector4(rt.x, rt.y, rt.z, tangents[i].w);
						}
					}
					copyOnly.vertices = rotVerts;
					if (rotNormals != null) copyOnly.normals = rotNormals;
					if (rotTangents != null) copyOnly.tangents = rotTangents;
					copyOnly.verticesModified = true;
					if (rotNormals != null) copyOnly.normalsModified = true;
					if (rotTangents != null) copyOnly.tangentsModified = true;
					copyOnly.vertexCount = rotVerts.Length;
				}
				return copyOnly;

			}

			// Build a hash->UMATransform map from current bones
			var localByHash = new Dictionary<int, UMATransform>(umaBones != null ? umaBones.Length : 0);
			if (umaBones != null)
			{
				for (int i = 0; i < umaBones.Length; i++)
				{
					var b = umaBones[i];
					localByHash[b.hash] = new UMATransform()
					{
						hash = b.hash,
						parent = b.parent,
						name = b.name,
						position = b.position,
						rotation = b.rotation,
						scale = b.scale
					};
				}
			}

			// Apply pose deltas (weight =1)
			if (bonePose.poses != null)
			{
				for (int i = 0; i < bonePose.poses.Length; i++)
				{
					var pb = bonePose.poses[i];
					UMATransform lt;
					if (localByHash.TryGetValue(pb.hash, out lt))
					{
						lt.position += pb.position;
						lt.rotation = lt.rotation * pb.rotation;
						lt.scale = new Vector3(lt.scale.x * pb.scale.x, lt.scale.y * pb.scale.y, lt.scale.z * pb.scale.z);
						localByHash[pb.hash] = lt;
					}
				}
			}

			// Build world matrices for each bone hash using hierarchy
			var worldByHash = new Dictionary<int, Matrix4x4>(localByHash.Count);
			Func<UMATransform, Matrix4x4> toLocal = t => Matrix4x4.TRS(t.position, t.rotation, t.scale);
			Func<int, Matrix4x4> getWorld = null;
			getWorld = (hash) =>
			{
				Matrix4x4 w;
				if (worldByHash.TryGetValue(hash, out w)) return w;
				UMATransform lt;
				if (!localByHash.TryGetValue(hash, out lt))
				{
					w = Matrix4x4.identity;
					worldByHash[hash] = w;
					return w;
				}
				if (lt.parent == 0 || lt.parent == hash || !localByHash.ContainsKey(lt.parent))
				{
					w = toLocal(lt);
					worldByHash[hash] = w;
					return w;
				}
				var pw = getWorld(lt.parent);
				w = pw * toLocal(lt);
				worldByHash[hash] = w;
				return w;
			};

			// Skinning matrices per bone index (index -> hash)
			var skinMats = new Matrix4x4[boneNameHashes.Length];
			for (int i = 0; i < boneNameHashes.Length; i++)
			{
				int hash = boneNameHashes[i];
				var w = getWorld(hash);
				var bp = (i < bindPoses.Length) ? bindPoses[i] : Matrix4x4.identity;
				skinMats[i] = w * bp;
			}

			// Pre-rotate source geometry if requested
			var srcVertices = vertices;
			var srcNormals = normals;
			var srcTangents = tangents;

			if (preRotation != Matrix4x4.identity)
			{
				var rotVerts = new Vector3[vertexCount];
				Vector3[] rotNormals = (normals != null && normals.Length == vertexCount) ? new Vector3[vertexCount] : null;
				Vector4[] rotTangents = (tangents != null && tangents.Length == vertexCount) ? new Vector4[vertexCount] : null;

				for (int i = 0; i < vertexCount; i++)
				{
					rotVerts[i] = preRotation.MultiplyPoint3x4(vertices[i]);
					if (rotNormals != null) rotNormals[i] = preRotation.MultiplyVector(normals[i]);
					if (rotTangents != null)
					{
						var t3 = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
						var rt = preRotation.MultiplyVector(t3).normalized;
						rotTangents[i] = new Vector4(rt.x, rt.y, rt.z, tangents[i].w);
					}
				}

				srcVertices = rotVerts;
				srcNormals = rotNormals ?? srcNormals;
				srcTangents = rotTangents ?? srcTangents;
			}

			// Prepare output copy and arrays
			var outData = DeepCopy();
			var newVerts = new Vector3[vertexCount];
			Vector3[] newNormals = (srcNormals != null && srcNormals.Length == vertexCount) ? new Vector3[vertexCount] : null;
			Vector4[] newTangents = (srcTangents != null && srcTangents.Length == vertexCount) ? new Vector4[vertexCount] : null;

			Func<Matrix4x4, Vector3, Vector3> mulPoint = (m, v) => m.MultiplyPoint3x4(v);
			Func<Matrix4x4, Vector3, Vector3> mulVector = (m, v) => m.MultiplyVector(v);

			bool hasManaged = (ManagedBonesPerVertex != null && ManagedBonesPerVertex.Length == vertexCount && ManagedBoneWeights != null && ManagedBoneWeights.Length > 0);
			if (hasManaged)
			{
				int offset = 0;
				for (int vi = 0; vi < vertexCount; vi++)
				{
					var baseV = srcVertices[vi];
					Vector3 accV = Vector3.zero;
					Vector3 accN = Vector3.zero;
					Vector3 accT = Vector3.zero;
					int count = ManagedBonesPerVertex[vi];
					for (int j = 0; j < count; j++)
					{
						var bw = ManagedBoneWeights[offset + j];
						int bi = bw.boneIndex;
						float w = bw.weight;
						var sm = (bi >= 0 && bi < skinMats.Length) ? skinMats[bi] : Matrix4x4.identity;
						accV += mulPoint(sm, baseV) * w;
						if (newNormals != null) accN += mulVector(sm, srcNormals[vi]) * w;
						if (newTangents != null)
						{
							var t3 = new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
							accT += mulVector(sm, t3) * w;
						}
					}
					newVerts[vi] = accV;
					if (newNormals != null) newNormals[vi] = accN.sqrMagnitude > 0f ? accN.normalized : srcNormals[vi];
					if (newTangents != null)
					{
						var t = (accT.sqrMagnitude > 0f) ? accT.normalized : new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
						newTangents[vi] = new Vector4(t.x, t.y, t.z, srcTangents[vi].w);
					}
					offset += count;
				}
			}
			else if (boneWeights != null && boneWeights.Length == vertexCount)
			{
				for (int vi = 0; vi < vertexCount; vi++)
				{
					var bw = boneWeights[vi];
					var baseV = srcVertices[vi];
					Vector3 accV = Vector3.zero;
					Vector3 accN = Vector3.zero;
					Vector3 accT = Vector3.zero;
					Action<int, float> accum = (bi, w) =>
					{
						if (w <= 0f) return;
						var sm = (bi >= 0 && bi < skinMats.Length) ? skinMats[bi] : Matrix4x4.identity;
						accV += mulPoint(sm, baseV) * w;
						if (newNormals != null) accN += mulVector(sm, srcNormals[vi]) * w;
						if (newTangents != null)
						{
							var t3 = new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
							accT += mulVector(sm, t3) * w;
						}
					};
					accum(bw.boneIndex0, bw.weight0);
					accum(bw.boneIndex1, bw.weight1);
					accum(bw.boneIndex2, bw.weight2);
                    accum(bw.boneIndex3, bw.weight3);
                    newVerts[vi] = accV;
                    if (newNormals != null) newNormals[vi] = accN.sqrMagnitude > 0f ? accN.normalized : srcNormals[vi];
                    if (newTangents != null)
                    {
                        var t = (accT.sqrMagnitude > 0f) ? accT.normalized : new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
                        newTangents[vi] = new Vector4(t.x, t.y, t.z, srcTangents[vi].w);
                    }
                }
            }
            else
            {
                // No weights; return copy with pre-rotation applied if requested
                var noWeightsCopy = DeepCopy();
                if (preRotation != Matrix4x4.identity)
                {
                    noWeightsCopy.vertices = srcVertices;
                    if (srcNormals != null) noWeightsCopy.normals = srcNormals;
                    if (srcTangents != null) noWeightsCopy.tangents = srcTangents;
                    noWeightsCopy.verticesModified = true;
                    if (srcNormals != null) noWeightsCopy.normalsModified = true;
                    if (srcTangents != null) noWeightsCopy.tangentsModified = true;
                    noWeightsCopy.vertexCount = srcVertices.Length;
                }
                return noWeightsCopy;
            }

            outData.vertices = newVerts;
            if (newNormals != null) outData.normals = newNormals;
            if (newTangents != null) outData.tangents = newTangents;
            outData.verticesModified = true;
            if (newNormals != null) outData.normalsModified = true;
            if (newTangents != null) outData.tangentsModified = true;
            outData.vertexCount = newVerts.Length;
			return outData;
		}

		public UMAMeshData ApplyBonePose(UMABonePose bonePose, RaceData raceData)
		{
			// Start from a deep copy of this mesh data. If a bone pose is provided, bake it using existing bindposes first.
			UMAMeshData outData = (bonePose != null) ? ApplyBonePose(bonePose, Matrix4x4.identity, false) : DeepCopy();

			if (raceData == null || raceData.baseRaceRecipe == null)
			{
				return outData; // nothing to replace
			}

			// Build lookup tables from the RaceData base recipe across all slots.
			var recipe = raceData.baseRaceRecipe.GetCachedRecipe(loadSlots: true);
			if (recipe == null)
			{
				return outData;
			}

			var slots = recipe.GetAllSlots();
			if (slots == null || slots.Length ==0)
			{
				return outData;
			}

			// Hash -> bindpose and Hash -> UMATransform lookups gathered from all slots in the race's base recipe
			var bindPoseByHash = new Dictionary<int, Matrix4x4>(256);
			var transformByHash = new Dictionary<int, UMATransform>(256);

			// Also track first-available bones/root from the race's slots
			Transform[] firstBones = null;
			Transform firstRootBone = null;
			int firstRootBoneHash =0;
			string firstRootBoneName = null;

			for (int si =0; si < slots.Length; si++)
			{
				var s = slots[si];
				if (s == null || s.asset == null) continue;
				var md = s.asset.meshData;
				if (md == null) continue;

				// Capture first encountered bones/root info if present
				if (firstBones == null && md.bones != null && md.bones.Length >0)
				{
					firstBones = md.bones;
				}
				if (firstRootBone == null && md.rootBone != null)
				{
					firstRootBone = md.rootBone;
				}
				if (firstRootBoneHash ==0 && md.rootBoneHash !=0)
				{
					firstRootBoneHash = md.rootBoneHash;
				}
				if (string.IsNullOrEmpty(firstRootBoneName) && !string.IsNullOrEmpty(md.RootBoneName))
				{
					firstRootBoneName = md.RootBoneName;
				}

				// Bind poses by bone hash
				if (md.boneNameHashes != null && md.bindPoses != null)
				{
					var srcHashes = md.boneNameHashes;
					var srcBind = md.bindPoses;
					int len = Math.Min(srcHashes.Length, srcBind.Length);
					for (int i =0; i < len; i++)
					{
						int h = srcHashes[i];
						if (!bindPoseByHash.ContainsKey(h))
						{
							bindPoseByHash.Add(h, srcBind[i]);
						}
					}
				}

				// Bone transforms by hash
				var srcBones = md.umaBones;
				if (srcBones != null)
				{
					for (int i =0; i < srcBones.Length; i++)
					{
						var b = srcBones[i];
						if (b == null) continue;
						if (!transformByHash.ContainsKey(b.hash))
						{
							// Duplicate so we don't reference asset objects
							transformByHash.Add(b.hash, new UMATransform()
							{
								hash = b.hash,
								name = b.name,
								parent = b.parent,
								position = b.position,
								rotation = b.rotation,
								scale = b.scale
							});
						}
					}
				}
			}

			// Replace bind poses for matching hashes
			if (outData.boneNameHashes != null && outData.boneNameHashes.Length >0)
			{
				// Ensure we have a bindpose array long enough
				if (outData.bindPoses == null || outData.bindPoses.Length != outData.boneNameHashes.Length)
				{
					var newBinds = new Matrix4x4[outData.boneNameHashes.Length];
					// Initialize from existing where possible
					if (outData.bindPoses != null)
					{
						Array.Copy(outData.bindPoses, newBinds, Math.Min(outData.bindPoses.Length, newBinds.Length));
					}
					outData.bindPoses = newBinds;
				}

				for (int i =0; i < outData.boneNameHashes.Length; i++)
				{
					int h = outData.boneNameHashes[i];
					Matrix4x4 bp;
					if (bindPoseByHash.TryGetValue(h, out bp))
					{
						outData.bindPoses[i] = bp;
					}
				}
			}

			// Update UMA bone transforms (position/rotation/scale and parent) where hashes match
			if (outData.umaBones != null && outData.umaBones.Length >0)
			{
				for (int i =0; i < outData.umaBones.Length; i++)
				{
					var b = outData.umaBones[i];
					if (b == null) continue;
					UMATransform src;
					if (transformByHash.TryGetValue(b.hash, out src))
					{
						b.position = src.position;
						b.rotation = src.rotation;
						b.scale = src.scale;
						b.parent = src.parent; // adopt parent from race for consistency
						// leave name/hash as-is
					}
				}
			}

			// Apply root and bones from the race's slots if available
			if (!string.IsNullOrEmpty(firstRootBoneName))
			{
				outData.RootBoneName = firstRootBoneName;
			}
			if (firstRootBoneHash !=0)
			{
				outData.rootBoneHash = firstRootBoneHash;
			}
			else
			{
				// Fallback: compute from current RootBoneName
				try { outData.rootBoneHash = UMAUtils.StringToHash(outData.RootBoneName); } catch { }
			}
			if (firstRootBone != null)
			{
				outData.rootBone = firstRootBone;
			}
			if (firstBones != null && firstBones.Length >0)
			{
				outData.bones = firstBones;
			}

			return outData;
		}
		static UMAMeshData()
		{
#if UNITY_EDITOR
			AppDomain.CurrentDomain.DomainUnload += CurrentDomain_DomainUnload;
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += beforeAssemblyReload;
            UnityEditor.EditorApplication.quitting += Application_quitting;
#else
			Application.quitting += Application_quitting;

#endif
        }

        private static void beforeAssemblyReload()
        {
#if DEBUG_UNITY_MESHDATA
            Debug.Log("AppDomain unloading, cleaning up UMA global buffers");
#endif
            SubMeshTriangles.DisposeAllNativeTriangles();
        }

        private static void Application_quitting()
        {
#if DEBUG_UNITY_MESHDATA
            Debug.Log("Application quitting, cleaning up UMA global buffers");
#endif
            CleanupGlobalBuffers();
			SubMeshTriangles.DisposeAllNativeTriangles();
        }

        private static void CurrentDomain_DomainUnload(object sender, EventArgs e)
		{
#if DEBUG_UNITY_MESHDATA
            Debug.Log("AppDomain unloading, cleaning up UMA global buffers");
#endif
            SubMeshTriangles.DisposeAllNativeTriangles();
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
				throw new Exception("Submesh has0 size!!! Submesh: " + submeshIndex);
			}
			if (!SubmeshBuffers.ContainsKey(submeshIndex))
			{
				var n = new NativeArray<int>(size, Allocator.Persistent);
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
		/// <param name="submeshIndex">Submesh index.</param>
		/// <param name="udimAdjustment">Adjust for UDIM?</param>
		/// <param name="clearNormals">Clear normals?</param>
		/// <param name="clearTangents">Clear tangents?</param>
		public void RetrieveDataFromUnityMesh(SkinnedMeshRenderer renderer, int submeshIndex, bool udimAdjustment = false, bool clearNormals = false, bool clearTangents = false)
		{
			RetrieveDataFromUnityMesh(renderer.sharedMesh, udimAdjustment, submeshIndex, clearNormals, clearTangents);

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
		public void RetrieveDataFromUnityMesh(Mesh sharedMesh, bool udimAdjustment = false, int subMeshInd = -1, bool clearNormals = false, bool clearTangents = false)
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
				for (int subMeshNumber = 0; subMeshNumber < subMeshCount; subMeshNumber++)
				{
					submeshes[subMeshNumber] = new SubMeshTriangles();
					submeshes[subMeshNumber].SetTriangles(sharedMesh.GetTriangles(subMeshNumber));
                }
			}
			else
			{
				subMeshCount = 1;
				submeshes = new SubMeshTriangles[subMeshCount];
				var tris = sharedMesh.GetTriangles(subMeshInd);
				submeshes[0] = new SubMeshTriangles();

                vertRemap = new SortedSet<int>(tris);
				var indRemap = new int[sharedMesh.vertexCount];

				vertexCount = 0;
				foreach (var vi in vertRemap)
                {
                    indRemap[vi] = vertexCount++;
                }

                for (var ii = 0; ii < tris.Length; ii++)
                {
                    tris[ii] = indRemap[tris[ii]];
                }

                submeshes[0].SetTriangles(tris);
			}

			bindPoses = sharedMesh.bindposes;
#if USE_NATIVE_ARRAYS
			unityBonesPerVertex = sharedMesh.GetBonesPerVertex();
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
                    {
                        boneWeights.Add(unityBoneWeights[boneWI++]);
                    }

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
					if (clearNormals)
					{
						hasNormals = false;
						blendShapes[shapeIndex].frames[frameIndex].deltaNormals = new Vector3[0];
					}
					if (clearTangents)
					{
						hasTangents = false;
						blendShapes[shapeIndex].frames[frameIndex].deltaTangents = new Vector3[0];
					}
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
		public void OldRetrieveDataFromUnityMesh(Mesh sharedMesh, bool udimAdjustment = false)
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
				submeshes[i] = new SubMeshTriangles();
				submeshes[i].SetTriangles(sharedMesh.GetTriangles(i));
			}
			if (udimAdjustment)
			{
				UMAUtils.UDIMAdjustUV(uv, sharedMesh.uv);
				UMAUtils.UDIMAdjustUV(uv2, sharedMesh.uv2);
				UMAUtils.UDIMAdjustUV(uv3, sharedMesh.uv3);
				UMAUtils.UDIMAdjustUV(uv4, sharedMesh.uv4);
			}
			//Create the blendshape data on the slot asset from the unity mesh
            #region Blendshape
			blendShapes = new UMABlendShape[sharedMesh.blendShapeCount];

			Vector3[] deltaVertices;
			Vector3[] deltaNormals;
			Vector3[] deltaTangents;

			for (int shapeIndex = 0; shapeIndex < sharedMesh.blendShapeCount; shapeIndex++)
			{
				blendShapes[shapeIndex] = new UMABlendShape();
				blendShapes[shapeIndex].shapeName = sharedMesh.GetBlendShapeName(shapeIndex);

				int frameCount = sharedMesh.GetBlendShapeFrameCount(shapeIndex);
				blendShapes[shapeIndex].frames = new UMABlendFrame[frameCount];

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

					blendShapes[shapeIndex].frames[frameIndex] = new UMABlendFrame();
					blendShapes[shapeIndex].frames[frameIndex].frameWeight = sharedMesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex);

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
			if (UMAAssetIndexer.Instance.Generator.Use32BitBuffers)
			{
				mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			}
			else
			{
				mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
			}
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

			SetBoneWeightsFromMeshData(mesh); //mesh.boneWeights = unityBoneWeights != null ? unityBoneWeights : UMABoneWeight.Convert(boneWeights);

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
			var Descriptors = new SubMeshDescriptor[subMeshCount];
			for (int i = 0; i < subMeshCount; i++)
			{
				int LOD = umaData.currentLODLevel;
                // public void SetIndices(NativeArray<T> indices, int indicesStart, int indicesLength, MeshTopology topology, int submesh, bool calculateBounds, int baseVertex);
				UMALodRange lodRange = submeshes[i].GetLODRange(LOD);
				mesh.SetIndices(submeshes[i].GetTriangles(0), (int)lodRange.offset, (int)lodRange.count, MeshTopology.Triangles, i, false);
                // mesh.SetIndices(submeshes[i].GetTriangles(), MeshTopology.Triangles, i);
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
						catch (Exception ex)
						{
							Debug.LogError("Error adding blendshape frame: " + ex.Message);
						}
					}
				}
			}
            #endregion

			Debug.Log("Recalculating bounds");
			mesh.RecalculateBounds();
			renderer.bones = bones != null ? bones : skeleton.HashesToTransforms(boneNameHashes);
			UMAUtils.DestroySceneObject(renderer.sharedMesh);
			//			GameObject.Destroy(renderer.sharedMesh);
			renderer.sharedMesh = mesh;
			renderer.rootBone = rootBone;

			ApplyCloth(renderer);
		}

		private void ApplyCloth(SkinnedMeshRenderer renderer)
		{
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
			//		list.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32,4, stream:3));
			list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4, stream: 4));
			list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4, stream: 5));
			//		list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32,4, stream:6));
			//		list.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32,4, stream:7));
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
					Debug.LogError("Error! Boneweights and BonesPerVertex is invalid. The slot must be regenerated. (" + this.SlotName + ")");
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
					if (ManagedBonesPerVertex[i] ==0)
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
 if (unityBonesPerVertex.Length <1)
 {
 Debug.LogError("Unity bones per vertex is empty!!");
 return;
 }
 if (unityBoneWeights.Length <1)
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
				int[] tris = submeshes[i].getManagedTriangles(0);
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
                mesh.SetIndices(submeshes[i].GetTriangles(0), MeshTopology.Triangles, i);
			}

			renderer.bones = bones;
			renderer.rootBone = rootBone;

			Debug.Log("Recalculating bounds");
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
			SubMeshTriangles.DisposeAllNativeTriangles();
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
			for (int i = 0; i < umaBoneCount; i++)
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

			if (blendShapes != null)
			{
				newMeshData.blendShapes = new UMABlendShape[blendShapes.Length];
				Array.Copy(blendShapes, newMeshData.blendShapes, blendShapes.Length);
			}

			if (clothSkinning != null)
			{
				newMeshData.clothSkinning = new ClothSkinningCoefficient[clothSkinning.Length];
				Array.Copy(clothSkinning, newMeshData.clothSkinning, clothSkinning.Length);
			}

			if (clothSkinningSerialized != null)
			{
				newMeshData.clothSkinningSerialized = new Vector2[clothSkinningSerialized.Length];
				Array.Copy(clothSkinningSerialized, newMeshData.clothSkinningSerialized, clothSkinningSerialized.Length);
			}

			if (submeshes != null)
			{
				newMeshData.submeshes = new SubMeshTriangles[submeshes.Length];
				Array.Copy(submeshes, newMeshData.submeshes, submeshes.Length);
			}

			if (bones != null)
			{
				newMeshData.bones = bones.Clone() as Transform[];
			}

			if (rootBone != null)
			{
				newMeshData.rootBone = rootBone;
			}

			if (umaBones != null)
			{
				newMeshData.umaBones = new UMATransform[umaBones.Length];
				Array.Copy(umaBones, newMeshData.umaBones, umaBones.Length);
			}

			newMeshData.umaBoneCount = umaBoneCount;
			newMeshData.rootBoneHash = rootBoneHash;

			if (boneNameHashes != null)
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
				errors.Append("; Vertices length of " + vertices.Length + " != vertexCount (" + vertexCount + ")");
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

				for (int i = 0; i < ManagedBonesPerVertex.Length; i++)
				{
					if (ManagedBonesPerVertex[i] == 0)
					{
						errors.Append("; ManagedBonesPerVertex[" + i + "] is zero!");
					}
				}
			}

			if (ManagedBoneWeights != null)
			{
				for (int i =0; i < ManagedBoneWeights.Length; i++)
				{
					BoneWeight1 bw = ManagedBoneWeights[i];
					// BoneWeight1 is a struct; no null check required. Validate basic range instead.
					if (bw.boneIndex <0)
					{
						errors.Append("; ManagedBoneWeights[" + i + "] has invalid boneIndex <0");
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
					errors.Append("; Meshdata submesh length ==0");
				}
				else
				{
					for (int i = 0; i < submeshes.Length; i++)
					{
						SubMeshTriangles smt = submeshes[i];
						if (smt.nativeTriangles != null && smt.nativeTriangles.IsCreated)
						{
							var tris = smt.GetTriangles(0);
							var tri = 0;
							for (int j = 0; j < tris.Length; j++)
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

        // Place these helpers inside the UMAMeshData class (e.g., above ApplyBonePose methods)
        private static Quaternion NormalizeSafe(Quaternion q)
        {
            // Guard against NaN/Infinity and zero length
            if (float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w) ||
                float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w))
            {
                return Quaternion.identity;
            }
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag < 1e-8f) return Quaternion.identity;
            float inv = 1.0f / mag;
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        private static Vector3 SanitizeScale(Vector3 s)
        {
            // Avoid invalid/degenerate scale values
            if (float.IsNaN(s.x) || float.IsInfinity(s.x)) s.x = 1f;
            if (float.IsNaN(s.y) || float.IsInfinity(s.y)) s.y = 1f;
            if (float.IsNaN(s.z) || float.IsInfinity(s.z)) s.z = 1f;
            return s;
        }


        public UMAMeshData ApplyBonePose(RaceData race, UMABonePose pose)
        {
            // Simple, in-place baker: returns 'this' after updating vertices/normals/tangents.
            // Assumptions:
            // - Uses current UMA bone hierarchy (umaBones) as the "default race" transforms.
            // - Applies pose deltas in local-space per bone, then skins with existing bindposes.
            // - Does not call other ApplyBonePose methods; no pre/post rotation; no race bindpose swap.

            // Guard: nothing to do
            if (pose == null || pose.poses == null || pose.poses.Length == 0)
                return this;
            if (vertices == null || vertexCount == 0 || bindPoses == null || bindPoses.Length == 0 || boneNameHashes == null || boneNameHashes.Length == 0)
                return this;
            if (umaBones == null || umaBones.Length == 0)
                return this;

            // Local copy of default bone transforms mapped by hash
            var localByHash = new Dictionary<int, UMATransform>(umaBones.Length);
            for (int i = 0; i < umaBones.Length; i++)
            {
                var b = umaBones[i];
                if (b == null) continue;
                localByHash[b.hash] = new UMATransform()
                {
                    hash = b.hash,
                    parent = b.parent,
                    name = b.name,
                    position = b.position,
                    rotation = b.rotation,
                    scale = b.scale
                };
            }

            // Apply pose deltas (local-space)
            for (int i = 0; i < pose.poses.Length; i++)
            {
                var pb = pose.poses[i];
                UMATransform lt;
                if (localByHash.TryGetValue(pb.hash, out lt))
                {
                    lt.position += pb.position;

                    // Normalize quaternion to avoid invalid TRS
                    {
                        var r = lt.rotation * pb.rotation;
                        float m = Mathf.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w);
                        if (m > 1e-8f && float.IsFinite(m))
                        {
                            float inv = 1.0f / m;
                            lt.rotation = new Quaternion(r.x * inv, r.y * inv, r.z * inv, r.w * inv);
                        }
                        else
                        {
                            lt.rotation = Quaternion.identity;
                        }
                    }

                    lt.scale = new Vector3(lt.scale.x * pb.scale.x, lt.scale.y * pb.scale.y, lt.scale.z * pb.scale.z);
                    localByHash[pb.hash] = lt;
                }
            }

            // Build world matrices per bone hash by traversing parents
            var worldByHash = new Dictionary<int, Matrix4x4>(localByHash.Count);
            Func<UMATransform, Matrix4x4> toLocal = t =>
            {
                // Sanitize scale
                var s = t.scale;
                if (!float.IsFinite(s.x)) s.x = 1f;
                if (!float.IsFinite(s.y)) s.y = 1f;
                if (!float.IsFinite(s.z)) s.z = 1f;
                return Matrix4x4.TRS(t.position, t.rotation, s);
            };

            Func<int, Matrix4x4> getWorld = null;
            getWorld = (hash) =>
            {
                Matrix4x4 w;
                if (worldByHash.TryGetValue(hash, out w)) return w;

                UMATransform lt;
                if (!localByHash.TryGetValue(hash, out lt))
                {
                    w = Matrix4x4.identity;
                    worldByHash[hash] = w;
                    return w;
                }

                if (lt.parent == 0 || lt.parent == hash || !localByHash.ContainsKey(lt.parent))
                {
                    w = toLocal(lt);
                    worldByHash[hash] = w;
                    return w;
                }

                var pw = getWorld(lt.parent);
                w = pw * toLocal(lt);
                worldByHash[hash] = w;
                return w;
            };

            // Skinning matrices per bone index (index aligns with boneNameHashes/bindPoses)
            var skinMats = new Matrix4x4[boneNameHashes.Length];
            for (int i = 0; i < boneNameHashes.Length; i++)
            {
                int h = boneNameHashes[i];
                var w = getWorld(h);
                var bp = (i < bindPoses.Length) ? bindPoses[i] : Matrix4x4.identity;
                skinMats[i] = w * bp;
            }

            // Source geometry (no pre-rotation in this simple version)
            var srcVerts = vertices;
            var srcNormals = normals;
            var srcTangents = tangents;

            // Output buffers
            var outVerts = new Vector3[vertexCount];
            Vector3[] outNormals = (srcNormals != null && srcNormals.Length == vertexCount) ? new Vector3[vertexCount] : null;
            Vector4[] outTangents = (srcTangents != null && srcTangents.Length == vertexCount) ? new Vector4[vertexCount] : null;

            // Skin using either ManagedBoneWeights (vtx-count-matched) or legacy boneWeights
            bool hasManaged = (ManagedBonesPerVertex != null && ManagedBonesPerVertex.Length == vertexCount &&
                               ManagedBoneWeights != null && ManagedBoneWeights.Length > 0);

            if (hasManaged)
            {
                int offset = 0;
                for (int vi = 0; vi < vertexCount; vi++)
                {
                    var baseV = srcVerts[vi];
                    Vector3 accV = Vector3.zero;
                    Vector3 accN = Vector3.zero;
                    Vector3 accT = Vector3.zero;

                    int count = ManagedBonesPerVertex[vi];
                    for (int j = 0; j < count; j++)
                    {
                        var bw = ManagedBoneWeights[offset + j];
                        int bi = bw.boneIndex;
                        float w = bw.weight;
                        if (w <= 0f) continue;

                        var sm = (bi >= 0 && bi < skinMats.Length) ? skinMats[bi] : Matrix4x4.identity;
                        accV += sm.MultiplyPoint3x4(baseV) * w;

                        if (outNormals != null)
                            accN += sm.MultiplyVector(srcNormals[vi]) * w;

                        if (outTangents != null)
                        {
                            var t3 = new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
                            accT += sm.MultiplyVector(t3) * w;
                        }
                    }

                    outVerts[vi] = accV;
                    if (outNormals != null)
                        outNormals[vi] = (accN.sqrMagnitude > 0f) ? accN.normalized : srcNormals[vi];

                    if (outTangents != null)
                    {
                        var t = (accT.sqrMagnitude > 0f) ? accT.normalized : new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
                        outTangents[vi] = new Vector4(t.x, t.y, t.z, srcTangents[vi].w);
                    }

                    offset += count;
                }
            }
            else if (boneWeights != null && boneWeights.Length == vertexCount)
            {
                for (int vi = 0; vi < vertexCount; vi++)
                {
                    var bw = boneWeights[vi];
                    var baseV = srcVerts[vi];
                    Vector3 accV = Vector3.zero;
                    Vector3 accN = Vector3.zero;
                    Vector3 accT = Vector3.zero;

                    void accum(int bi, float w)
                    {
                        if (w <= 0f) return;
                        var sm = (bi >= 0 && bi < skinMats.Length) ? skinMats[bi] : Matrix4x4.identity;
                        accV += sm.MultiplyPoint3x4(baseV) * w;

                        if (outNormals != null)
                            accN += sm.MultiplyVector(srcNormals[vi]) * w;

                        if (outTangents != null)
                        {
                            var t3 = new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
                            accT += sm.MultiplyVector(t3) * w;
                        }
                    }

                    accum(bw.boneIndex0, bw.weight0);
                    accum(bw.boneIndex1, bw.weight1);
                    accum(bw.boneIndex2, bw.weight2);
                    accum(bw.boneIndex3, bw.weight3);

                    outVerts[vi] = accV;
                    if (outNormals != null)
                        outNormals[vi] = (accN.sqrMagnitude > 0f) ? accN.normalized : srcNormals[vi];

                    if (outTangents != null)
                    {
                        var t = (accT.sqrMagnitude > 0f) ? accT.normalized : new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
                        outTangents[vi] = new Vector4(t.x, t.y, t.z, srcTangents[vi].w);
                    }
                }
            }
            else
            {
                // No weights; nothing to bake
                return this;
            }

            // Commit results in-place
            vertices = outVerts;
            if (outNormals != null) normals = outNormals;
            if (outTangents != null) tangents = outTangents;

            verticesModified = true;
            if (outNormals != null) normalsModified = true;
            if (outTangents != null) tangentsModified = true;

            // vertexCount unchanged
            return this;
        }

        // Add inside the UMAMeshData class

        /// <summary>
        /// Applies a UMABonePose while treating the specified bone name as the pose root.
        /// - If postRotate is true, the pose is baked first (no pre-rotation) then the preRotation is applied to the baked vertices.
        /// - The bone whose name hashes to 'rootBoneName' is considered the root for hierarchy traversal when composing world matrices.
        /// </summary>
        public UMAMeshData ApplyBonePoseWithRoot(UMABonePose bonePose, Matrix4x4 preRotation, bool postRotate, string rootBoneName)
        {
            // Post-rotation branch: bake with identity first, then rotate baked geometry.
            if (postRotate && preRotation != Matrix4x4.identity)
            {
                var baked = ApplyBonePoseWithRoot(bonePose, Matrix4x4.identity, false, rootBoneName);
                if (baked == null || baked.vertices == null || baked.vertexCount == 0)
                    return baked;

                int vCount = baked.vertexCount;
                var inVerts = baked.vertices;
                var inNormals = baked.normals;
                var inTangents = baked.tangents;

                var outVerts = new Vector3[vCount];
                Vector3[] outNormals = (inNormals != null && inNormals.Length == vCount) ? new Vector3[vCount] : null;
                Vector4[] outTangents = (inTangents != null && inTangents.Length == vCount) ? new Vector4[vCount] : null;

                for (int i = 0; i < vCount; i++)
                {
                    outVerts[i] = preRotation.MultiplyPoint3x4(inVerts[i]);

                    if (outNormals != null)
                        outNormals[i] = preRotation.MultiplyVector(inNormals[i]);

                    if (outTangents != null)
                    {
                        var t3 = new Vector3(inTangents[i].x, inTangents[i].y, inTangents[i].z);
                        var rt = preRotation.MultiplyVector(t3).normalized;
                        outTangents[i] = new Vector4(rt.x, rt.y, rt.z, inTangents[i].w);
                    }
                }

                baked.vertices = outVerts;
                if (outNormals != null) baked.normals = outNormals;
                if (outTangents != null) baked.tangents = outTangents;

                baked.verticesModified = true;
                if (outNormals != null) baked.normalsModified = true;
                if (outTangents != null) baked.tangentsModified = true;

                baked.vertexCount = outVerts.Length;
                return baked;
            }

            // No pose: allow simple pre-rotation only
            if (bonePose == null)
            {
                var copyOnly = DeepCopy();
                if (vertices == null || vertexCount == 0)
                    return copyOnly;

                if (preRotation != Matrix4x4.identity)
                {
                    var rotVerts = new Vector3[vertexCount];
                    Vector3[] rotNormals = (normals != null && normals.Length == vertexCount) ? new Vector3[vertexCount] : null;
                    Vector4[] rotTangents = (tangents != null && tangents.Length == vertexCount) ? new Vector4[vertexCount] : null;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        rotVerts[i] = preRotation.MultiplyPoint3x4(vertices[i]);
                        if (rotNormals != null) rotNormals[i] = preRotation.MultiplyVector(normals[i]);
                        if (rotTangents != null)
                        {
                            var t3 = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                            var rt = preRotation.MultiplyVector(t3).normalized;
                            rotTangents[i] = new Vector4(rt.x, rt.y, rt.z, tangents[i].w);
                        }
                    }
                    copyOnly.vertices = rotVerts;
                    if (rotNormals != null) copyOnly.normals = rotNormals;
                    if (rotTangents != null) copyOnly.tangents = rotTangents;
                    copyOnly.verticesModified = true;
                    if (rotNormals != null) copyOnly.normalsModified = true;
                    if (rotTangents != null) copyOnly.tangentsModified = true;
                    copyOnly.vertexCount = rotVerts.Length;
                }
                return copyOnly;
            }

            // Validate data needed for skinning
            if (vertices == null || vertexCount == 0 || bindPoses == null || bindPoses.Length == 0 || boneNameHashes == null || boneNameHashes.Length == 0)
            {
                var copyOnly = DeepCopy();
                if (vertices == null || vertexCount == 0)
                    return copyOnly;

                if (preRotation != Matrix4x4.identity)
                {
                    var rotVerts = new Vector3[vertexCount];
                    Vector3[] rotNormals = (normals != null && normals.Length == vertexCount) ? new Vector3[vertexCount] : null;
                    Vector4[] rotTangents = (tangents != null && tangents.Length == vertexCount) ? new Vector4[vertexCount] : null;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        rotVerts[i] = preRotation.MultiplyPoint3x4(vertices[i]);
                        if (rotNormals != null) rotNormals[i] = preRotation.MultiplyVector(normals[i]);
                        if (rotTangents != null)
                        {
                            var t3 = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                            var rt = preRotation.MultiplyVector(t3).normalized;
                            rotTangents[i] = new Vector4(rt.x, rt.y, rt.z, tangents[i].w);
                        }
                    }
                    copyOnly.vertices = rotVerts;
                    if (rotNormals != null) copyOnly.normals = rotNormals;
                    if (rotTangents != null) copyOnly.tangents = rotTangents;
                    copyOnly.verticesModified = true;
                    if (rotNormals != null) copyOnly.normalsModified = true;
                    if (rotTangents != null) copyOnly.tangentsModified = true;
                    copyOnly.vertexCount = rotVerts.Length;
                }
                return copyOnly;
            }

            // Build local transforms by hash
            var localByHash = new Dictionary<int, UMATransform>(umaBones != null ? umaBones.Length : 0);
            if (umaBones != null)
            {
                for (int i = 0; i < umaBones.Length; i++)
                {
                    var b = umaBones[i];
                    localByHash[b.hash] = new UMATransform()
                    {
                        hash = b.hash,
                        parent = b.parent,
                        name = b.name,
                        position = b.position,
                        rotation = b.rotation,
                        scale = b.scale
                    };
                }
            }

            // Apply pose deltas (normalize rotations to avoid invalid TRS quaternions)
            if (bonePose.poses != null)
            {
                for (int i = 0; i < bonePose.poses.Length; i++)
                {
                    var pb = bonePose.poses[i];
                    UMATransform lt;
                    if (localByHash.TryGetValue(pb.hash, out lt))
                    {
                        lt.position += pb.position;
                        lt.rotation = NormalizeSafe(lt.rotation * pb.rotation);
                        lt.scale = new Vector3(lt.scale.x * pb.scale.x, lt.scale.y * pb.scale.y, lt.scale.z * pb.scale.z);
                        localByHash[pb.hash] = lt;
                    }
                }
            }

            // Forced root (by name) — stop hierarchy traversal when reaching this hash
            int forcedRootHash = 0;
            if (!string.IsNullOrEmpty(rootBoneName))
            {
                try { forcedRootHash = UMAUtils.StringToHash(rootBoneName); } catch { forcedRootHash = 0; }
            }

            // Local TRS with normalized rotation and sanitized scale
            Func<UMATransform, Matrix4x4> toLocal = t =>
            {
                var r = NormalizeSafe(t.rotation);
                var s = SanitizeScale(t.scale);
                return Matrix4x4.TRS(t.position, r, s);
            };

            // Build world matrices honoring the forced root
            var worldByHash = new Dictionary<int, Matrix4x4>(localByHash.Count);
            Func<int, Matrix4x4> getWorld = null;
            getWorld = (hash) =>
            {
                Matrix4x4 w;
                if (worldByHash.TryGetValue(hash, out w)) return w;
                UMATransform lt;
                if (!localByHash.TryGetValue(hash, out lt))
                {
                    w = Matrix4x4.identity;
                    worldByHash[hash] = w;
                    return w;
                }

                // Treat the requested bone as root
                if (forcedRootHash != 0 && hash == forcedRootHash)
                {
                    w = toLocal(lt);
                    worldByHash[hash] = w;
                    return w;
                }

                // Default roots (no/invalid parent)
                if (lt.parent == 0 || lt.parent == hash || !localByHash.ContainsKey(lt.parent))
                {
                    w = toLocal(lt);
                    worldByHash[hash] = w;
                    return w;
                }

                var pw = getWorld(lt.parent);
                w = pw * toLocal(lt);
                worldByHash[hash] = w;
                return w;
            };

            // Skinning matrices (index -> hash)
            var skinMats = new Matrix4x4[boneNameHashes.Length];
            for (int i = 0; i < boneNameHashes.Length; i++)
            {
                int hash = boneNameHashes[i];
                var w = getWorld(hash);
                var bp = (i < bindPoses.Length) ? bindPoses[i] : Matrix4x4.identity;
                skinMats[i] = w * bp;
            }

            // Pre-rotate source geometry if requested (pre-rotation path)
            var srcVertices = vertices;
            var srcNormals = normals;
            var srcTangents = tangents;

            if (preRotation != Matrix4x4.identity)
            {
                var rotVerts = new Vector3[vertexCount];
                Vector3[] rotNormals = (normals != null && normals.Length == vertexCount) ? new Vector3[vertexCount] : null;
                Vector4[] rotTangents = (tangents != null && tangents.Length == vertexCount) ? new Vector4[vertexCount] : null;

                for (int i = 0; i < vertexCount; i++)
                {
                    rotVerts[i] = preRotation.MultiplyPoint3x4(vertices[i]);
                    if (rotNormals != null) rotNormals[i] = preRotation.MultiplyVector(normals[i]);
                    if (rotTangents != null)
                    {
                        var t3 = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                        var rt = preRotation.MultiplyVector(t3).normalized;
                        rotTangents[i] = new Vector4(rt.x, rt.y, rt.z, tangents[i].w);
                    }
                }

                srcVertices = rotVerts;
                srcNormals = rotNormals ?? srcNormals;
                srcTangents = rotTangents ?? srcTangents;
            }

            // Prepare output and skin
            var outData = DeepCopy();
            var newVerts = new Vector3[vertexCount];
            Vector3[] newNormals = (srcNormals != null && srcNormals.Length == vertexCount) ? new Vector3[vertexCount] : null;
            Vector4[] newTangents = (srcTangents != null && srcTangents.Length == vertexCount) ? new Vector4[vertexCount] : null;

            Func<Matrix4x4, Vector3, Vector3> mulPoint = (m, v) => m.MultiplyPoint3x4(v);
            Func<Matrix4x4, Vector3, Vector3> mulVector = (m, v) => m.MultiplyVector(v);

            bool hasManaged = (ManagedBonesPerVertex != null && ManagedBonesPerVertex.Length == vertexCount && ManagedBoneWeights != null && ManagedBoneWeights.Length > 0);
            if (hasManaged)
            {
                int offset = 0;
                for (int vi = 0; vi < vertexCount; vi++)
                {
                    var baseV = srcVertices[vi];
                    Vector3 accV = Vector3.zero;
                    Vector3 accN = Vector3.zero;
                    Vector3 accT = Vector3.zero;
                    int count = ManagedBonesPerVertex[vi];
                    for (int j = 0; j < count; j++)
                    {
                        var bw = ManagedBoneWeights[offset + j];
                        int bi = bw.boneIndex;
                        float w = bw.weight;
                        var sm = (bi >= 0 && bi < skinMats.Length) ? skinMats[bi] : Matrix4x4.identity;
                        accV += mulPoint(sm, baseV) * w;
                        if (newNormals != null) accN += mulVector(sm, srcNormals[vi]) * w;
                        if (newTangents != null)
                        {
                            var t3 = new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
                            accT += mulVector(sm, t3) * w;
                        }
                    }
                    newVerts[vi] = accV;
                    if (newNormals != null) newNormals[vi] = accN.sqrMagnitude > 0f ? accN.normalized : srcNormals[vi];
                    if (newTangents != null)
                    {
                        var t = (accT.sqrMagnitude > 0f) ? accT.normalized : new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
                        newTangents[vi] = new Vector4(t.x, t.y, t.z, srcTangents[vi].w);
                    }
                    offset += count;
                }
            }
            else if (boneWeights != null && boneWeights.Length == vertexCount)
            {
                for (int vi = 0; vi < vertexCount; vi++)
                {
                    var bw = boneWeights[vi];
                    var baseV = srcVertices[vi];
                    Vector3 accV = Vector3.zero;
                    Vector3 accN = Vector3.zero;
                    Vector3 accT = Vector3.zero;
                    void accum(int bi, float w)
                    {
                        if (w <= 0f) return;
                        var sm = (bi >= 0 && bi < skinMats.Length) ? skinMats[bi] : Matrix4x4.identity;
                        accV += mulPoint(sm, baseV) * w;
                        if (newNormals != null) accN += mulVector(sm, srcNormals[vi]) * w;
                        if (newTangents != null)
                        {
                            var t3 = new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
                            accT += mulVector(sm, t3) * w;
                        }
                    }
                    accum(bw.boneIndex0, bw.weight0);
                    accum(bw.boneIndex1, bw.weight1);
                    accum(bw.boneIndex2, bw.weight2);
                    accum(bw.boneIndex3, bw.weight3);

                    newVerts[vi] = accV;
                    if (newNormals != null) newNormals[vi] = accN.sqrMagnitude > 0f ? accN.normalized : srcNormals[vi];
                    if (newTangents != null)
                    {
                        var t = (accT.sqrMagnitude > 0f) ? accT.normalized : new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
                        newTangents[vi] = new Vector4(t.x, t.y, t.z, srcTangents[vi].w);
                    }
                }
            }
            else
            {
                // No weights; return copy with pre-rotation if any
                var noWeightsCopy = DeepCopy();
                if (preRotation != Matrix4x4.identity)
                {
                    noWeightsCopy.vertices = srcVertices;
                    if (srcNormals != null) noWeightsCopy.normals = srcNormals;
                    if (srcTangents != null) noWeightsCopy.tangents = srcTangents;
                    noWeightsCopy.verticesModified = true;
                    if (srcNormals != null) noWeightsCopy.normalsModified = true;
                    if (srcTangents != null) noWeightsCopy.tangentsModified = true;
                    noWeightsCopy.vertexCount = srcVertices.Length;
                }
                return noWeightsCopy;
            }

            outData = DeepCopy();
            outData.vertices = newVerts;
            if (newNormals != null) outData.normals = newNormals;
            if (newTangents != null) outData.tangents = newTangents;
            outData.verticesModified = true;
            if (newNormals != null) outData.normalsModified = true;
            if (newTangents != null) outData.tangentsModified = true;
            outData.vertexCount = newVerts.Length;
            return outData;
        }

        /// <summary>
        /// Convenience overload: identity pre-rotation, no post-rotate.
        /// </summary>
        public UMAMeshData ApplyBonePoseWithRoot(UMABonePose bonePose, string rootBoneName)
        {
            return ApplyBonePoseWithRoot(bonePose, Matrix4x4.identity, false, rootBoneName);
        }

        /// <summary>
        /// Convenience overload: bake pose with root, then apply RaceData replacements.
        /// </summary>
        public UMAMeshData ApplyBonePoseWithRoot(UMABonePose bonePose, Matrix4x4 preRotation, RaceData raceData, bool postRotate, string rootBoneName)
        {
            var baked = ApplyBonePoseWithRoot(bonePose, preRotation, postRotate, rootBoneName);
            return baked.ApplyBonePose(null, raceData);
        }

        public UMAMeshData ApplyBonePose(UMABonePose bonePose, Matrix4x4 preRotation, bool postRotate)
        {
            // If post-rotation is requested, bake the pose first (no pre-rotation), then rotate the result.
            if (postRotate && preRotation != Matrix4x4.identity)
            {
                // Bake pose without pre-rotation
                var baked = ApplyBonePose(bonePose, Matrix4x4.identity, false);
                if (baked == null) return null;

                // Nothing to rotate?
                if (baked.vertices == null || baked.vertexCount == 0)
                    return baked;

                int vCount = baked.vertexCount;
                var inVerts = baked.vertices;
                var inNormals = baked.normals;
                var inTangents = baked.tangents;

                var outVerts = new Vector3[vCount];
                Vector3[] outNormals = (inNormals != null && inNormals.Length == vCount) ? new Vector3[vCount] : null;
                Vector4[] outTangents = (inTangents != null && inTangents.Length == vCount) ? new Vector4[vCount] : null;

                for (int i = 0; i < vCount; i++)
                {
                    outVerts[i] = preRotation.MultiplyPoint3x4(inVerts[i]);

                    if (outNormals != null)
                        outNormals[i] = preRotation.MultiplyVector(inNormals[i]);

                    if (outTangents != null)
                    {
                        var t3 = new Vector3(inTangents[i].x, inTangents[i].y, inTangents[i].z);
                        var rt = preRotation.MultiplyVector(t3).normalized;
                        outTangents[i] = new Vector4(rt.x, rt.y, rt.z, inTangents[i].w);
                    }
                }

                baked.vertices = outVerts;
                if (outNormals != null) baked.normals = outNormals;
                if (outTangents != null) baked.tangents = outTangents;

                baked.verticesModified = true;
                if (outNormals != null) baked.normalsModified = true;
                if (outTangents != null) baked.tangentsModified = true;

                baked.vertexCount = outVerts.Length;
                return baked;
            }

            // Existing behavior: pre-rotation occurs before skinning (unchanged path)
            // If no pose, still allow pre-rotation only
            if (bonePose == null)
            {
                var copyOnly = DeepCopy();
                if (vertices == null || vertexCount == 0)
                    return copyOnly;

                if (preRotation != Matrix4x4.identity)
                {
                    var rotVerts = new Vector3[vertexCount];
                    Vector3[] rotNormals = (normals != null && normals.Length == vertexCount) ? new Vector3[vertexCount] : null;
                    Vector4[] rotTangents = (tangents != null && tangents.Length == vertexCount) ? new Vector4[vertexCount] : null;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        rotVerts[i] = preRotation.MultiplyPoint3x4(vertices[i]);
                        if (rotNormals != null) rotNormals[i] = preRotation.MultiplyVector(normals[i]);
                        if (rotTangents != null)
                        {
                            var t3 = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                            var rt = preRotation.MultiplyVector(t3).normalized;
                            rotTangents[i] = new Vector4(rt.x, rt.y, rt.z, tangents[i].w);
                        }
                    }
                    copyOnly.vertices = rotVerts;
                    if (rotNormals != null) copyOnly.normals = rotNormals;
                    if (rotTangents != null) copyOnly.tangents = rotTangents;
                    copyOnly.verticesModified = true;
                    if (rotNormals != null) copyOnly.normalsModified = true;
                    if (rotTangents != null) copyOnly.tangentsModified = true;
                    copyOnly.vertexCount = rotVerts.Length;
                }
                return copyOnly;
            }

            // Validate required data (still apply pre-rotation only if skinning data missing)
            if (vertices == null || vertexCount == 0 || bindPoses == null || bindPoses.Length == 0 || boneNameHashes == null || boneNameHashes.Length == 0)
            {
                var copyOnly = DeepCopy();
                if (vertices == null || vertexCount == 0)
                    return copyOnly;

                if (preRotation != Matrix4x4.identity)
                {
                    var rotVerts = new Vector3[vertexCount];
                    Vector3[] rotNormals = (normals != null && normals.Length == vertexCount) ? new Vector3[vertexCount] : null;
                    Vector4[] rotTangents = (tangents != null && tangents.Length == vertexCount) ? new Vector4[vertexCount] : null;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        rotVerts[i] = preRotation.MultiplyPoint3x4(vertices[i]);
                        if (rotNormals != null) rotNormals[i] = preRotation.MultiplyVector(normals[i]);
                        if (rotTangents != null)
                        {
                            var t3 = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                            var rt = preRotation.MultiplyVector(t3).normalized;
                            rotTangents[i] = new Vector4(rt.x, rt.y, rt.z, tangents[i].w);
                        }
                    }
                    copyOnly.vertices = rotVerts;
                    if (rotNormals != null) copyOnly.normals = rotNormals;
                    if (rotTangents != null) copyOnly.tangents = rotTangents;
                    copyOnly.verticesModified = true;
                    if (rotNormals != null) copyOnly.normalsModified = true;
                    if (rotTangents != null) copyOnly.tangentsModified = true;
                    copyOnly.vertexCount = rotVerts.Length;
                }
                return copyOnly;
            }

            // Build a hash->UMATransform map from current bones
            var localByHash = new Dictionary<int, UMATransform>(umaBones != null ? umaBones.Length : 0);
            if (umaBones != null)
            {
                for (int i = 0; i < umaBones.Length; i++)
                {
                    var b = umaBones[i];
                    localByHash[b.hash] = new UMATransform()
                    {
                        hash = b.hash,
                        parent = b.parent,
                        name = b.name,
                        position = b.position,
                        rotation = b.rotation,
                        scale = b.scale
                    };
                }
            }

            // 1) When composing bone rotations, normalize the result
            if (bonePose.poses != null)
            {
                for (int i = 0; i < bonePose.poses.Length; i++)
                {
                    var pb = bonePose.poses[i];
                    UMATransform lt;
                    if (localByHash.TryGetValue(pb.hash, out lt))
                    {
                        lt.position += pb.position;
                        // Keep existing order; normalize to avoid drift
                        lt.rotation = NormalizeSafe(lt.rotation * pb.rotation);
                        lt.scale = new Vector3(lt.scale.x * pb.scale.x, lt.scale.y * pb.scale.y, lt.scale.z * pb.scale.z);
                        localByHash[pb.hash] = lt;
                    }
                }
            }


            // Build world matrices for each bone hash using hierarchy
            var worldByHash = new Dictionary<int, Matrix4x4>(localByHash.Count);
            //Func<UMATransform, Matrix4x4> toLocal = t => Matrix4x4.TRS(t.position, t.rotation, t.scale);


            // 2) Build local TRS using normalized rotation and sanitized scale
            Func<UMATransform, Matrix4x4> toLocal = t =>
            {
                var r = NormalizeSafe(t.rotation);
                var s = SanitizeScale(t.scale);
                return Matrix4x4.TRS(t.position, r, s);
            };


            Func<int, Matrix4x4> getWorld = null;
            getWorld = (hash) =>
            {
                Matrix4x4 w;
                if (worldByHash.TryGetValue(hash, out w)) return w;
                UMATransform lt;
                if (!localByHash.TryGetValue(hash, out lt))
                {
                    w = Matrix4x4.identity;
                    worldByHash[hash] = w;
                    return w;
                }
                if (lt.parent == 0 || lt.parent == hash || !localByHash.ContainsKey(lt.parent))
                {
                    w = toLocal(lt);
                    worldByHash[hash] = w;
                    return w;
                }
                var pw = getWorld(lt.parent);
                w = pw * toLocal(lt);
                worldByHash[hash] = w;
                return w;
            };

            // Skinning matrices per bone index (index -> hash)
            var skinMats = new Matrix4x4[boneNameHashes.Length];
            for (int i = 0; i < boneNameHashes.Length; i++)
            {
                int hash = boneNameHashes[i];
                var w = getWorld(hash);
                var bp = (i < bindPoses.Length) ? bindPoses[i] : Matrix4x4.identity;
                skinMats[i] = w * bp;
            }

            // Pre-rotate source geometry if requested (pre-rotation path)
            var srcVertices = vertices;
            var srcNormals = normals;
            var srcTangents = tangents;

            if (preRotation != Matrix4x4.identity)
            {
                var rotVerts = new Vector3[vertexCount];
                Vector3[] rotNormals = (normals != null && normals.Length == vertexCount) ? new Vector3[vertexCount] : null;
                Vector4[] rotTangents = (tangents != null && tangents.Length == vertexCount) ? new Vector4[vertexCount] : null;

                for (int i = 0; i < vertexCount; i++)
                {
                    rotVerts[i] = preRotation.MultiplyPoint3x4(vertices[i]);
                    if (rotNormals != null) rotNormals[i] = preRotation.MultiplyVector(normals[i]);
                    if (rotTangents != null)
                    {
                        var t3 = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                        var rt = preRotation.MultiplyVector(t3).normalized;
                        rotTangents[i] = new Vector4(rt.x, rt.y, rt.z, tangents[i].w);
                    }
                }

                srcVertices = rotVerts;
                srcNormals = rotNormals ?? srcNormals;
                srcTangents = rotTangents ?? srcTangents;
            }

            // Prepare output copy and arrays
            var outData = DeepCopy();
            var newVerts = new Vector3[vertexCount];
            Vector3[] newNormals = (srcNormals != null && srcNormals.Length == vertexCount) ? new Vector3[vertexCount] : null;
            Vector4[] newTangents = (srcTangents != null && srcTangents.Length == vertexCount) ? new Vector4[vertexCount] : null;

            Func<Matrix4x4, Vector3, Vector3> mulPoint = (m, v) => m.MultiplyPoint3x4(v);
            Func<Matrix4x4, Vector3, Vector3> mulVector = (m, v) => m.MultiplyVector(v);

            bool hasManaged = (ManagedBonesPerVertex != null && ManagedBonesPerVertex.Length == vertexCount && ManagedBoneWeights != null && ManagedBoneWeights.Length > 0);
            if (hasManaged)
            {
                int offset = 0;
                for (int vi = 0; vi < vertexCount; vi++)
                {
                    var baseV = srcVertices[vi];
                    Vector3 accV = Vector3.zero;
                    Vector3 accN = Vector3.zero;
                    Vector3 accT = Vector3.zero;
                    int count = ManagedBonesPerVertex[vi];
                    for (int j = 0; j < count; j++)
                    {
                        var bw = ManagedBoneWeights[offset + j];
                        int bi = bw.boneIndex;
                        float w = bw.weight;
                        var sm = (bi >= 0 && bi < skinMats.Length) ? skinMats[bi] : Matrix4x4.identity;
                        accV += mulPoint(sm, baseV) * w;
                        if (newNormals != null) accN += mulVector(sm, srcNormals[vi]) * w;
                        if (newTangents != null)
                        {
                            var t3 = new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
                            accT += mulVector(sm, t3) * w;
                        }
                    }
                    newVerts[vi] = accV;
                    if (newNormals != null) newNormals[vi] = accN.sqrMagnitude > 0f ? accN.normalized : srcNormals[vi];
                    if (newTangents != null)
                    {
                        var t = (accT.sqrMagnitude > 0f) ? accT.normalized : new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
                        newTangents[vi] = new Vector4(t.x, t.y, t.z, srcTangents[vi].w);
                    }
                    offset += count;
                }
            }
            else if (boneWeights != null && boneWeights.Length == vertexCount)
            {
                for (int vi = 0; vi < vertexCount; vi++)
                {
                    var bw = boneWeights[vi];
                    var baseV = srcVertices[vi];
                    Vector3 accV = Vector3.zero;
                    Vector3 accN = Vector3.zero;
                    Vector3 accT = Vector3.zero;
                    Action<int, float> accum = (bi, w) =>
                    {
                        if (w <= 0f) return;
                        var sm = (bi >= 0 && bi < skinMats.Length) ? skinMats[bi] : Matrix4x4.identity;
                        accV += mulPoint(sm, baseV) * w;
                        if (newNormals != null) accN += mulVector(sm, srcNormals[vi]) * w;
                        if (newTangents != null)
                        {
                            var t3 = new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
                            accT += mulVector(sm, t3) * w;
                        }
                    };
                    accum(bw.boneIndex0, bw.weight0);
                    accum(bw.boneIndex1, bw.weight1);
                    accum(bw.boneIndex2, bw.weight2);
                    accum(bw.boneIndex3, bw.weight3);
                    newVerts[vi] = accV;
                    if (newNormals != null) newNormals[vi] = accN.sqrMagnitude > 0f ? accN.normalized : srcNormals[vi];
                    if (newTangents != null)
                    {
                        var t = (accT.sqrMagnitude > 0f) ? accT.normalized : new Vector3(srcTangents[vi].x, srcTangents[vi].y, srcTangents[vi].z);
                        newTangents[vi] = new Vector4(t.x, t.y, t.z, srcTangents[vi].w);
                    }
                }
            }
            else
            {
                // No weights; return copy with pre-rotation applied if requested
                var noWeightsCopy = DeepCopy();
                if (preRotation != Matrix4x4.identity)
                {
                    noWeightsCopy.vertices = srcVertices;
                    if (srcNormals != null) noWeightsCopy.normals = srcNormals;
                    if (srcTangents != null) noWeightsCopy.tangents = srcTangents;
                    noWeightsCopy.verticesModified = true;
                    if (srcNormals != null) noWeightsCopy.normalsModified = true;
                    if (srcTangents != null) noWeightsCopy.tangentsModified = true;
                    noWeightsCopy.vertexCount = srcVertices.Length;
                }
                return noWeightsCopy;
            }

            outData.vertices = newVerts;
            if (newNormals != null) outData.normals = newNormals;
            if (newTangents != null) outData.tangents = newTangents;
            outData.verticesModified = true;
            if (newNormals != null) outData.normalsModified = true;
            if (newTangents != null) outData.tangentsModified = true;
            outData.vertexCount = newVerts.Length;
            return outData;
        }

//What changed:
//- Added an early post-rotate branch that bakes with identity pre-rotation, then rotates the baked result.
//- Left the existing pre-rotation path untouched for backward compatibility.

//Why this fixes twisting:
//- With both a pose and a global 180° Z correction, pre-rotating raw vertices and then skinning effectively double-transform bones versus geometry. Post-rotating applies the global correction in the same space as the baked (skinned) geometry, avoiding compounded rotation artifacts (e.g., twisted shoes).

        // Convenience overload: apply pre-rotation and race replacements in one call
        public UMAMeshData ApplyBonePose(UMABonePose bonePose, Matrix4x4 preRotation, RaceData raceData, bool postRotate)
		{
			// First bake the bone pose with the requested pre-rotation
			var baked = ApplyBonePose(bonePose, preRotation, postRotate);
			// Then apply race data replacements (bindposes + UMA transforms + root/bones)
			return baked.ApplyBonePose(null, raceData);
		}
	}
}
