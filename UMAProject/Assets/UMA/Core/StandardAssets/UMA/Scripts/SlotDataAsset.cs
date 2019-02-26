using UnityEngine;
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
#endif

namespace UMA
{
	/// <summary>
	/// Contains the immutable data shared between slots of the same type.
	/// </summary>
	[System.Serializable]
	public partial class SlotDataAsset : UMADataAsset, ISerializationCallbackReceiver
    {
		public override string umaName
		{
			get { return slotName; }
		}

		public override int umaHash
		{
			get { return nameHash; }
		}

		public string slotName;
		[System.NonSerialized]
		public int nameHash;

        /// <summary>
        /// The UMA material.
        /// </summary>
        /// <remarks>
        /// The UMA material contains both a reference to the Unity material
        /// used for drawing and information needed for matching the textures
        /// and colors to the various material properties.
        /// </remarks>
        [UMAAssetFieldVisible]
		public UMAMaterial material;
		
	    [UMAAssetFieldVisible]
	    public UMANamedMaterial namedMaterial;

		/// <summary>
		/// Default overlay scale for slots using the asset.
		/// </summary>
		public float overlayScale = 1.0f;
		/// <summary>
		/// The animated bone names.
		/// </summary>
		/// <remarks>
		/// The animated bones array is required for cases where optimizations
		/// could remove transforms from the rig. Animated bones will always
		/// be preserved.
		/// </remarks>
		public string[] animatedBoneNames = new string[0];
		/// <summary>
		/// The animated bone name hashes.
		/// </summary>
		/// <remarks>
		/// The animated bones array is required for cases where optimizations
		/// could remove transforms from the rig. Animated bones will always
		/// be preserved.
		/// </remarks>
		[UnityEngine.HideInInspector]
		public int[] animatedBoneHashes = new int[0];

		/// <summary>
		/// Optional DNA converter specific to the slot.
		/// </summary>
		public DnaConverterBehaviour slotDNA;
		/// <summary>
		/// The mesh data.
		/// </summary>
		/// <remarks>
		/// The UMAMeshData contains all of the Unity mesh data and additional
		/// information needed for mesh manipulation while minimizing overhead
		/// from accessing Unity's managed memory.
		/// </remarks>
		public UMAMeshData meshData;
		public int subMeshIndex;
		/// <summary>
		/// Use this to identify slots that serves the same purpose
		/// Eg. ChestArmor, Helmet, etc.
		/// </summary>
		public string slotGroup;
		/// <summary>
		/// Use this to identify what kind of overlays fit this slotData
		/// Eg. BaseMeshSkin, BaseMeshOverlays, GenericPlateArmor01
		/// </summary>
		public string[] tags;

		/// <summary>
		/// Callback event when character update begins.
		/// </summary>
		public UMADataEvent CharacterBegun;
		/// <summary>
		/// Callback event when slot overlays are atlased.
		/// </summary>
		public UMADataSlotMaterialRectEvent SlotAtlassed;
		/// <summary>
		/// Callback event when character DNA is applied.
		/// </summary>
		public UMADataEvent DNAApplied;
		/// <summary>
		/// Callback event when character update is complete.
		/// </summary>
		public UMADataEvent CharacterCompleted;

		public SlotDataAsset()
		{
            
		}

		public int GetTextureChannelCount(UMAGeneratorBase generator)
		{
			return material.channels.Length;
		}
        
		public override string ToString()
		{
			return "SlotDataAsset: " + slotName;
		}

        public void UpdateMeshData(SkinnedMeshRenderer meshRenderer, string rootBoneName)
        {
            meshData = new UMAMeshData();
            meshData.RootBoneName = rootBoneName;
            meshData.RetrieveDataFromUnityMesh(meshRenderer);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        public void UpdateMeshData(SkinnedMeshRenderer meshRenderer)
		{
			meshData = new UMAMeshData();
			meshData.RetrieveDataFromUnityMesh(meshRenderer);
#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(this);
#endif
		}
#if UNITY_EDITOR
		
		public void UpdateMeshData()
		{
		}

		// HACK
		Dictionary<int, UMATransform> umaBones;
		Dictionary<int, Matrix4x4> bonesToRoot;
		private void CalcBoneToRoot(UMATransform bone)
		{
			if (bonesToRoot.ContainsKey(bone.hash)) return;

			if (!umaBones.ContainsKey(bone.parent))
			{
				//Debug.Log("Top level bone " + umaBones[bone.hash].name);

				Matrix4x4 boneToRoot = Matrix4x4.TRS(bone.position, bone.rotation, bone.scale);
				bonesToRoot.Add(bone.hash, boneToRoot);
				return;
			}

			if (!bonesToRoot.ContainsKey(bone.parent))
			{
				CalcBoneToRoot(umaBones[bone.parent]);
			}
			bonesToRoot.Add(bone.hash, bonesToRoot[bone.parent] * Matrix4x4.TRS(bone.position, bone.rotation, bone.scale));
		}

		Dictionary<int, Matrix4x4> bindsToBone;
		private void CalcBindToBone(UMATransform bone)
		{
			if (bindsToBone.ContainsKey(bone.hash)) return;

			bindsToBone.Add(bone.hash, bone.bindToBone);

			if (!bindsToBone.ContainsKey(bone.parent))
			{
				UMATransform parent;
				if (umaBones.TryGetValue(bone.parent, out parent))
				{
					if (parent.bindToBone == Matrix4x4.zero)
					{
						parent.bindToBone = Matrix4x4.TRS(bone.position, bone.rotation, bone.scale) * bone.bindToBone;
					}
					//parent.retained = true;
					CalcBindToBone(parent);
				}
			}
		}

#endif
		public void OnAfterDeserialize()
		{
			nameHash = UMAUtils.StringToHash(slotName);

#if UNITY_EDITOR
			// HACK - screw with the stored data to match new formats
			if ((meshData != null) && (meshData.bindPoses != null))
			{
				umaBones = new Dictionary<int, UMATransform>(meshData.umaBones.Length);
				bonesToRoot = new Dictionary<int, Matrix4x4>(meshData.umaBones.Length);
				bindsToBone = new Dictionary<int, Matrix4x4>(meshData.umaBones.Length);

				//Debug.LogWarning("Hacking UMAMeshData for " + this.GetAssetName());

				int boneCount = meshData.umaBones.Length;
				for (int i = 0; i < meshData.umaBones.Length; i++)
				{
					meshData.umaBones[i].bindToBone = Matrix4x4.zero;
					meshData.umaBones[i].retained = false;
					umaBones.Add(meshData.umaBones[i].hash, meshData.umaBones[i]);
				}

				bonesToRoot.Add(meshData.rootBoneHash, Matrix4x4.identity);
				for (int i = 0; i < meshData.umaBones.Length; i++)
				{
					try
					{
						CalcBoneToRoot(meshData.umaBones[i]);
						meshData.umaBones[i].boneToRoot = bonesToRoot[meshData.umaBones[i].hash];
					}
					catch
					{
						Debug.LogError("Error looking for bone: " + meshData.umaBones[i].name);
					}
				}

				List<UMATransform> sortedBones = new List<UMATransform>();

				int bindCount = meshData.bindPoses.Length;
//				int globalHash = UMAUtils.StringToHash("Global");
//				int rootHash = UMAUtils.StringToHash("Root");
				for (int i = 0; i < bindCount; i++)
				{
					UMATransform bone;
					if (umaBones.TryGetValue(meshData.boneNameHashes[i], out bone))
					{
						bone.bindToBone = meshData.bindPoses[i];
						sortedBones.Add(bone);
						if (meshData.bindPoses[i] == Matrix4x4.zero)
						{
							Debug.LogError("Adding bad bind on bone: " + bone.name + " to slot: " + slotName);
						}
					}
					else
					{
						Debug.LogError("Missing bind bone");
					}
//
//					if (meshData.boneNameHashes[i] == globalHash)
//					{
//						Debug.LogWarning("Skinning to Global bone");
//					}
//					if (meshData.boneNameHashes[i] == rootHash)
//					{
//						Debug.LogWarning("Skinning to Root bone");
//					}
				}

				for (int i = 0; i < meshData.umaBones.Length; i++)
				{
					UMATransform bone = meshData.umaBones[i];

					if (sortedBones.Contains(bone))
					{
						CalcBindToBone(bone);
						//bone.retained = true;
					}
				}
				for (int i = 0; i < meshData.umaBones.Length; i++)
				{
					UMATransform bone = meshData.umaBones[i];
					if (bone.bindToBone == Matrix4x4.zero)
					{
						Debug.LogFormat("Bone {0} on slot {1} has no bind", bone.name, name);
					}

					if (!sortedBones.Contains(bone))
					{
						//if (bone.retained)
						{
							sortedBones.Add(bone);
							bone.retained = false;
						}
					}
				}

				meshData.umaBones = sortedBones.ToArray();

				//if ((meshData.normalTriangles == null) || (meshData.normalTriangles.Length != meshData.vertexCount))
				//{
				//	// Calculate all the new normal generating data
				//	Matrix4x4[] bindsToRoot = new Matrix4x4[meshData.umaBones.Length];
				//	for (int i = 0; i < bindsToRoot.Length; i++)
				//	{
				//		int boneHash = meshData.umaBones[i].hash;
				//		bindsToRoot[i] = bonesToRoot[boneHash] * bindsToBone[boneHash];
				//	}

				//	UpdateNormalTriangles(meshData, bindsToRoot);
				//}

//				for (int i = 0; i < meshData.boneWeights.Length; i++)
//				{
//					if (meshData.boneWeights[i].boneIndex0 >= meshData.boneNameHashes.Length)
//					{
//						Debug.LogError("Bad bind bone");
//					}
//					if (meshData.boneWeights[i].boneIndex1 >= meshData.boneNameHashes.Length)
//					{
//						Debug.LogError("Bad bind bone");
//					}
//					if (meshData.boneWeights[i].boneIndex2 >= meshData.boneNameHashes.Length)
//					{
//						Debug.LogError("Bad bind bone");
//					}
//					if (meshData.boneWeights[i].boneIndex3 >= meshData.boneNameHashes.Length)
//					{
//						Debug.LogError("Bad bind bone");
//					}
//				}

//				meshData.bindPoses = null;
//				UnityEditor.EditorUtility.SetDirty(this);
			}
#endif
		}
		public void OnBeforeSerialize() { }

		public void Assign(SlotDataAsset source)
		{
			slotName = source.slotName;
			nameHash = source.nameHash;
			material = source.material;
			overlayScale = source.overlayScale;
			animatedBoneNames = source.animatedBoneNames;
			animatedBoneHashes = source.animatedBoneHashes;
			meshData = source.meshData;
			subMeshIndex = source.subMeshIndex;
			slotGroup = source.slotGroup;
			tags = source.tags;
		}

		static void UpdateNormalTriangles(UMAMeshData meshData, Matrix4x4[] bindsToRoot)
		{
			meshData.normalTriangles = new int[meshData.vertexCount];
			meshData.normalAdjustments = new Quaternion[meshData.vertexCount];

			// Calculate all the vertices and normals in root space
			Vector3[] rootVerts = new Vector3[meshData.vertexCount];
			Vector3[] rootNorms = new Vector3[meshData.vertexCount];
			for (int i = 0; i < meshData.vertexCount; i++)
			{
				Vector3 vertex = meshData.vertices[i];
				UMABoneWeight weight = meshData.boneWeights[i];
				rootVerts[i] = Vector3.zero;
				rootVerts[i] += bindsToRoot[weight.boneIndex0].MultiplyPoint3x4(vertex) * weight.weight0;
				rootVerts[i] += bindsToRoot[weight.boneIndex1].MultiplyPoint3x4(vertex) * weight.weight1;
				rootVerts[i] += bindsToRoot[weight.boneIndex2].MultiplyPoint3x4(vertex) * weight.weight2;
				rootVerts[i] += bindsToRoot[weight.boneIndex3].MultiplyPoint3x4(vertex) * weight.weight3;

				Vector3 normal = meshData.normals[i];
				rootNorms[i] = Vector3.zero;
				rootNorms[i] += bindsToRoot[weight.boneIndex0].MultiplyVector(normal) * weight.weight0;
				rootNorms[i] += bindsToRoot[weight.boneIndex1].MultiplyVector(normal) * weight.weight1;
				rootNorms[i] += bindsToRoot[weight.boneIndex2].MultiplyVector(normal) * weight.weight2;
				rootNorms[i] += bindsToRoot[weight.boneIndex3].MultiplyVector(normal) * weight.weight3;
			}

			// Move the submesh arrays into a single merged array and store the offsets
			int triangleCount = 0;
			meshData.submeshIndices = new int[meshData.submeshes.Length];
			for (int i = 0; i < meshData.submeshes.Length; i++)
			{
				meshData.submeshIndices[i] = triangleCount * 3;
				triangleCount += meshData.submeshes[i].TriangleCount;
			}
			if ((meshData.triangles == null) || (meshData.triangles.Length != triangleCount * 3))
			{
				meshData.triangles = new int[triangleCount * 3];
				int index = 0;
				for (int i = 0; i < meshData.submeshes.Length; i++)
				{
					meshData.submeshes[i].triangles.CopyTo(meshData.triangles, index);
					index += meshData.submeshes[i].TriangleCount * 3;
				}
			}

			// Calculate the best triangle for each vertex normal
			float[] scores = new float[meshData.vertexCount];
			for (int i = 0; i < meshData.triangles.Length; i += 3)
			{
				int index0 = i;
				Vector3 pt0 = rootVerts[index0];
				UMABoneWeight weight0 = meshData.boneWeights[index0];

				int index1 = i + 1;
				Vector3 pt1 = rootVerts[index1];
				UMABoneWeight weight1 = meshData.boneWeights[index1];

				int index2 = i + 2;
				Vector3 pt2 = rootVerts[index2];
				UMABoneWeight weight2 = meshData.boneWeights[index2];

				Vector3 normal = Vector3.Cross((pt0 - pt1), (pt1 - pt2)).normalized;

				float score0_1 = CompareBoneWeights(ref weight0, ref weight1);
				float score0_2 = CompareBoneWeights(ref weight0, ref weight2);
				float score1_2 = CompareBoneWeights(ref weight1, ref weight2);

				float score0 = score0_1 + score0_2 + 1f;
				float score1 = score0_1 + score1_2 + 1f;
				float score2 = score0_2 + score1_2 + 1f;

				if (score0 > scores[index0])
				{
					scores[index0] = score0;
					meshData.normalTriangles[index0] = i; // NOT the index, all point to first in triangle
					meshData.normalAdjustments[index0] = Quaternion.FromToRotation(normal, rootNorms[index0]);
				}
				if (score1 > scores[index1])
				{
					scores[index1] = score1;
					meshData.normalTriangles[index1] = i; // NOT the index, all point to first in triangle
					meshData.normalAdjustments[index1] = Quaternion.FromToRotation(normal, rootNorms[index1]);
				}
				if (score2 > scores[index2])
				{
					scores[index2] = score2;
					meshData.normalTriangles[index2] = i; // NOT the index, all point to first in triangle
					meshData.normalAdjustments[index2] = Quaternion.FromToRotation(normal, rootNorms[index2]);
				}
			}

			for (int i = 0; i < meshData.vertexCount; i++)
			{
				if (scores[i] < Mathf.Epsilon)
				{
					Debug.LogWarning("No triangle found for vertex: " + i);
				}
			}
		}

		static float CompareBoneWeights(ref UMABoneWeight weightA, ref UMABoneWeight weightB)
		{
			float score = 0;

			if (weightB.boneIndex0 == weightA.boneIndex0) score += (weightA.weight0 * weightB.weight0);
			if (weightB.boneIndex1 == weightA.boneIndex0) score += (weightA.weight0 * weightB.weight1);
			if (weightB.boneIndex2 == weightA.boneIndex0) score += (weightA.weight0 * weightB.weight2);
			if (weightB.boneIndex3 == weightA.boneIndex0) score += (weightA.weight0 * weightB.weight3);
			if (weightB.boneIndex0 == weightA.boneIndex1) score += (weightA.weight1 * weightB.weight0);
			if (weightB.boneIndex1 == weightA.boneIndex1) score += (weightA.weight1 * weightB.weight1);
			if (weightB.boneIndex2 == weightA.boneIndex1) score += (weightA.weight1 * weightB.weight2);
			if (weightB.boneIndex3 == weightA.boneIndex1) score += (weightA.weight1 * weightB.weight3);
			if (weightB.boneIndex0 == weightA.boneIndex2) score += (weightA.weight2 * weightB.weight0);
			if (weightB.boneIndex1 == weightA.boneIndex2) score += (weightA.weight2 * weightB.weight1);
			if (weightB.boneIndex2 == weightA.boneIndex2) score += (weightA.weight2 * weightB.weight2);
			if (weightB.boneIndex3 == weightA.boneIndex2) score += (weightA.weight2 * weightB.weight3);
			if (weightB.boneIndex0 == weightA.boneIndex3) score += (weightA.weight3 * weightB.weight0);
			if (weightB.boneIndex1 == weightA.boneIndex3) score += (weightA.weight3 * weightB.weight1);
			if (weightB.boneIndex2 == weightA.boneIndex3) score += (weightA.weight3 * weightB.weight2);
			if (weightB.boneIndex3 == weightA.boneIndex3) score += (weightA.weight3 * weightB.weight3);

			return score;
		}

	}
}
