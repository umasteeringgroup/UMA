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
	public partial class SlotDataAsset : UMADataAsset, ISerializationCallbackReceiver, INameProvider
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

        #region INameProvider

        public string GetAssetName()
        {
            return slotName;
        }
        public int GetNameHash()
        {
            return nameHash;
        }

        #endregion
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
		//		Dictionary<int, Matrix4x4> bonesToRoot;
		//		private void CalcBoneToRoot(UMATransform bone)
		//		{
		//			if (bonesToRoot.ContainsKey(bone.hash)) return;
		//
		//			if (!umaBones.ContainsKey(bone.parent))
		//			{
		//				Matrix4x4 boneToRoot = Matrix4x4.TRS(bone.position, bone.rotation, bone.scale).inverse;
		//				bonesToRoot.Add(bone.hash, boneToRoot);
		////				Debug.Log("Top level bone " + umaBones[bone.hash].name + "\n" + boneToRoot);
		//				return;
		//			}
		//
		//			if (!bonesToRoot.ContainsKey(bone.parent))
		//			{
		//				CalcBoneToRoot(umaBones[bone.parent]);
		//			}
		//			bonesToRoot.Add(bone.hash, bonesToRoot[bone.parent] * Matrix4x4.TRS(bone.position, bone.rotation, bone.scale).inverse);
		//		}

		//Dictionary<int, Matrix4x4> bindsToBone;
		//private void CalcBindToBone(UMATransform bone)
		//{
		//	if (bindsToBone.ContainsKey(bone.hash)) return;

		//	bindsToBone.Add(bone.hash, bone.bindToBone);

		//	if (!bindsToBone.ContainsKey(bone.parent))
		//	{
		//		UMATransform parent;
		//		if (umaBones.TryGetValue(bone.parent, out parent))
		//		{
		//			if (parent.bindToBone == Matrix4x4.zero)
		//			{
		//				Matrix4x4 childToParent = Matrix4x4.TRS(bone.position, bone.rotation, bone.scale);
		//				Matrix4x4 bindToBone = childToParent * bone.bindToBone;
		//				parent.bindToBone = bindToBone;
		//				CalcBindToBone(parent);
		//			}
		//		}
		//	}

		//}

#endif
		public void OnAfterDeserialize()
		{
			nameHash = UMAUtils.StringToHash(slotName);

#if UNITY_EDITOR
			// HACK - screw with the stored data to match new formats
			if ((meshData != null) && (meshData.bindPoses != null))
			{
				umaBones = new Dictionary<int, UMATransform>(meshData.umaBones.Length);
				//bonesToRoot = new Dictionary<int, Matrix4x4>(meshData.umaBones.Length);
				//bindsToBone = new Dictionary<int, Matrix4x4>(meshData.umaBones.Length);

				//Debug.LogWarning("Hacking UMAMeshData for " + this.GetAssetName());

				int boneCount = meshData.umaBones.Length;
				for (int i = 0; i < meshData.umaBones.Length; i++)
				{
					meshData.umaBones[i].bindToBone = Matrix4x4.zero;
					meshData.umaBones[i].retained = true;
					umaBones.Add(meshData.umaBones[i].hash, meshData.umaBones[i]);
				}

//				bonesToRoot.Add(meshData.rootBoneHash, Matrix4x4.identity);
//				for (int i = 0; i < meshData.umaBones.Length; i++)
//				{
//					try
//					{
//						CalcBoneToRoot(meshData.umaBones[i]);
//						meshData.umaBones[i].boneToRoot = bonesToRoot[meshData.umaBones[i].hash];
//					}
//					catch
//					{
//						Debug.LogError("Error looking for bone: " + meshData.umaBones[i].name);
//					}
//				}

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

					if (!sortedBones.Contains(bone))
					{
						sortedBones.Add(bone);
					}
					//else
					//{
					//	CalcBindToBone(bone);
					//}
				}

				meshData.umaBones = sortedBones.ToArray();

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
	}
}
