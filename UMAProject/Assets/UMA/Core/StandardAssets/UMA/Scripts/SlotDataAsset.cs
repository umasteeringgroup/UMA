#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditorInternal;
#endif
using UnityEngine;
using UnityEngine.Serialization;

using boneWeld = System.Collections.Generic.List<UnityEngine.BoneWeight1>;

namespace UMA
{
	/// <summary>
	/// Contains the immutable data shared between slots of the same type.
	/// </summary>
	[System.Serializable]
	[PreferBinarySerialization] 
	public partial class SlotDataAsset : ScriptableObject, ISerializationCallbackReceiver, INameProvider
    {
		public string slotName;
		[System.NonSerialized]
		public int nameHash;
#if UNITY_EDITOR
		[Tooltip("This is only used when updating the slot with drag and drop below. It is not used at runtime nor is it included in the build")]
		public SkinnedMeshRenderer normalReferenceMesh;
		[HideInInspector]
		public bool ConvertTangents;

		private StringBuilder errorBuilder  = new StringBuilder();

		[System.Serializable]
		public class WeldPoint
        {
			public int ourVertex;
			public int theirVertex;
			public Vector3 newNormal;
			public bool misMatch;
			public WeldPoint(int ours, int theirs, Vector3 newNormal, bool misMatch)
            {
				ourVertex = ours;
				theirVertex = theirs;
				this.newNormal = newNormal;
				this.misMatch = misMatch;
            }
        }

		[System.Serializable]
		public class Welding
        {
			public string WeldedToSlot;
			public int MisMatchCount = 0;
			public List<WeldPoint> WeldPoints = new List<WeldPoint>();
        }

		public List<Welding> Welds = new List<Welding>();


		public Welding CalculateWelds(SlotDataAsset slot, bool CopyNormals, bool CopyBoneWeights)
        {
			Welding thisWeld = new Welding();
			

			thisWeld.MisMatchCount = 0;
			thisWeld.WeldedToSlot = slot.slotName;
			for (int Dest=0;Dest< slot.meshData.vertices.Length; Dest++)
            {
				for (int Src = 0; Src < meshData.vertices.Length; Src++)
                {
					Vector3 DestVert = slot.meshData.vertices[Dest];
					Vector3 Srcvert = meshData.vertices[Src];
					float Len = (DestVert - Srcvert).magnitude;
					if (Len < Vector3.kEpsilon)
                    {
						bool misMatch = false;
						float Normaldiff = (meshData.normals[Src] - slot.meshData.normals[Dest]).magnitude;
						if (Normaldiff > Vector3.kEpsilon)
						{
							thisWeld.MisMatchCount++;
							if (CopyNormals)
							{
								meshData.normals[Src] = slot.meshData.normals[Dest];
								if (meshData.tangents != null && slot.meshData.tangents != null)
                                {
									meshData.tangents[Src] = slot.meshData.tangents[Dest];
                                }
							}
							misMatch = true;
						}
						WeldPoint wp = new WeldPoint(Src, Dest, slot.meshData.normals[Dest], misMatch);
						thisWeld.WeldPoints.Add(wp);
                    }
                }
            }

			if (CopyBoneWeights)
            {
				EnsureBoneWeights();
				slot.EnsureBoneWeights();

				Dictionary<int, int> theirBonePositionInBoneWeights = new Dictionary<int, int>();
				//Dictionary<int, int> ourBonePositionInBoneWeights = new Dictionary<int, int>();
				Dictionary<int, WeldPoint> ourWeldPoints = new Dictionary<int, WeldPoint>();
				//Dictionary<int,string> theirBoneNames = new Dictionary<int,string>();
				Dictionary<string, int> theirBoneIndexByName = new Dictionary<string, int>();
				//Dictionary<string,int> ourBoneIndexes = new Dictionary<string,int>();

				/*for(int i=0;i<slot.meshData.umaBones.Length;i++)
                {
					theirBoneNames.Add(i, slot.meshData.umaBones[i].name);
                }

				for (int i=0;i<meshData.umaBones.Length;i++)
                {
					ourBoneIndexes.Add(meshData.umaBones[i].name, i);
                }*/

				int bonePos = 0;
				int bone = 0;
				foreach(byte WeightCount in slot.meshData.ManagedBonesPerVertex)
                {
					theirBonePositionInBoneWeights.Add(bone, bonePos);
					theirBoneIndexByName.Add(slot.meshData.umaBones[bone].name, bone);
					bonePos += slot.meshData.ManagedBonesPerVertex[bone];
					bone++;
				}

/*				bonePos = 0;
				bone = 0;
				foreach (byte WeightCount in meshData.ManagedBonesPerVertex)
				{
					ourBonePositionInBoneWeights.Add(bone, bonePos);
					bonePos += meshData.ManagedBonesPerVertex[bone];
					bone++;
				}*/

				foreach (WeldPoint p in thisWeld.WeldPoints)
                {
					ourWeldPoints.Add(p.ourVertex, p);
                }



				List<boneWeld> BoneWelds = new List<boneWeld>();
				int ourBonePos = 0;
				boneWeld b = new boneWeld();
				for (int i=0;i<meshData.ManagedBonesPerVertex.Length;i++)
                {

					if (ourWeldPoints.ContainsKey(i))
                    {
						WeldPoint p = ourWeldPoints[i];
						// 

						// copy translated bones and weights and BonesPerVertex.
						// get bone name
						// find new bone index
						// get THEIR bone index for OUR bone name.
						// Add the weights for THEIR bone index to our BoneWeights, but use OUR INDEX

						string ourBoneName = meshData.umaBones[i].name;
						int translatedBoneIndex = theirBoneIndexByName[ourBoneName];
						int theirBonePos = theirBonePositionInBoneWeights[translatedBoneIndex];

						// get the number of weights for their bone.
						int weightcount = slot.meshData.ManagedBonesPerVertex[translatedBoneIndex];

						for (int bpi = 0; bpi<weightcount; bpi++)
                        {
							BoneWeight1 bw = new BoneWeight1();
							bw.boneIndex = i; // this is in "our" bonespace
							bw.weight = meshData.ManagedBoneWeights[bpi+theirBonePos].weight;
							b.Add(bw);
						}

						// advance through our bone weights
						ourBonePos += meshData.ManagedBonesPerVertex[i];
					}
					else
                    {
						for(int bpi = 0;bpi < meshData.ManagedBonesPerVertex[i]; bpi++)
                        {
							BoneWeight1 bw = new BoneWeight1();
							bw.boneIndex = i;
							bw.weight = meshData.ManagedBoneWeights[ourBonePos].weight;
							b.Add(bw);
							ourBonePos++;
                        }
                    }
                }
				// copy bonewelds to bone arrays

				int boneIndex = 0;
				boneWeld newBoneWeights = new boneWeld();
				foreach(boneWeld bw in BoneWelds)
                {
					meshData.ManagedBonesPerVertex[boneIndex] = (byte)bw.Count;
					newBoneWeights.AddRange(bw);
                }

				meshData.ManagedBoneWeights = newBoneWeights.ToArray();
				meshData.boneWeights = null;
			}


			//for (int i=0;i<Welds.Count;i++)
            //{
			//	if (Welds[i].WeldedToSlot == slot.slotName)
            //    {
			//		Welds[i] = thisWeld;
			//		return thisWeld.WeldPoints.Count;
            //    }
           // }
			//Welds.Add(thisWeld);
			return thisWeld;
		}

		public bool HasErrors
        {
			get
            {
				return (!string.IsNullOrEmpty(Errors));
            }
        }
		public string Errors;

		/// <summary>
		/// Returns true if meshdata is valid or null (a utility slot).
		/// </summary>
		/// <returns></returns>
		public bool ValidateMeshData()
        {
			Errors = "";
			errorBuilder.Clear();

			if (meshData == null)
            {
				return true;
            }
			if (material == null)
            {
				AddError("material is null. A valid UMAMaterial that matches the overlay should be assigned.");
            }
			Errors = meshData.Validate();
			return true;
        }

        private void AddError(string v)
        {
			if (errorBuilder.Length == 0)
			{
				errorBuilder.Append(v);
			}
			else
            {
				errorBuilder.Append("; ");
				errorBuilder.Append(v);
            }
        }

        public ReorderableList tagList { get; set; }
		public bool eventsFoldout { get; set; } = false;
#endif

		public UMARendererAsset RendererAsset { get { return _rendererAsset; } }
		[SerializeField] private UMARendererAsset _rendererAsset=null;

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
		[SerializeField]
		public UMAMaterial material;

		/// <summary>
		/// materialName is used to save the name of the material, but ONLY if we have cleared the material when building bundles.
		/// You can't count on this field to contain a value unless it was set during the cleanup phase by the indexer!
		/// </summary>
		public string materialName;

		/// <summary>
		/// This SlotDataAsset will not be included after this LOD level.
		/// Set high by default so behavior is the same.
		/// </summary>
		[Tooltip("If you are using an LOD system, this is the maximum LOD that this slot will be displayed. After that, it will be discarded during mesh generation. a value of -1 will never be dropped.")]
		public int maxLOD=-1;

		/// <summary>
		/// 
		/// </summary>
		public bool useAtlasOverlay;

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

		[Tooltip("This object can process events ")]
		public GameObject SlotObject;
		private bool SlotObjectHookedUp = false;

#pragma warning disable 649
		//UMA2.8+ we need to use DNAConverterField now because that can contain Behaviours and the new controllers
		//we need this because we need the old data out of it on deserialize
		/// <summary>
		/// Optional DNA converter specific to the slot.
		/// </summary>
		[FormerlySerializedAs("slotDNA")]
		[SerializeField]
		private DnaConverterBehaviour _slotDNALegacy;
#pragma warning restore 649

		//UMA 2.8 FixDNAPrefabs: this is a new field that can take DNAConverter Prefabs *and* DNAConverterControllers
		[SerializeField]
		[Tooltip("Optional DNA converter specific to the slot. Accepts a DNAConverterController asset or a legacy DNAConverterBehaviour prefab.")]
		private DNAConverterField _slotDNA = new DNAConverterField();

		[Tooltip("If isWildCardSlot = true, then the overlays on this slot are applied to any slot or overlay with a matching tag when the recipe is built. This is used in Wardrobe Recipes to apply overlays to other slots.")]
		public bool isWildCardSlot;

		//UMA 2.8 FixDNAPrefabs: I'm putting the required property for this here because theres no properties anywhere else!
		public IDNAConverter slotDNA
		{
			get { return _slotDNA.Value; }
			set { _slotDNA.Value = value; }
		}

		public bool isUtilitySlot
		{
			get
			{
				if (meshData != null || meshData.vertexCount > 0) return false;

				if (material == null) return true;
				if (CharacterBegun != null && CharacterBegun.GetPersistentEventCount() > 0) return true;
				if (SlotAtlassed != null && SlotAtlassed.GetPersistentEventCount() > 0) return true;
				if (DNAApplied != null && DNAApplied.GetPersistentEventCount() > 0) return true;
				if (CharacterCompleted != null && CharacterCompleted.GetPersistentEventCount() > 0) return true;

				return false;
			}
		}

		public void LoadFromIndex()
        {
			material = UMAAssetIndexer.Instance.GetAsset<UMAMaterial>(materialName);
        }


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
		/// This can be used for hiding, matching etc. 
		/// It's used by the DynamicCharacterSystem to hide slots by tag.
		/// </summary>
		public string[] tags;

		// Wildcard slot race matches
		public string[] Races;

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


		/// <summary>
		/// This slot was auto generated as a LOD slot based on another slot.
		/// </summary>
		[SerializeField]
		[HideInInspector]
		public bool autoGeneratedLOD;

		public SlotDataAsset()
		{

		}

		private List<IUMAEventHookup> EventHookups = new List<IUMAEventHookup>();

        public void Awake()
        {
        }

		public void Begin(UMAData umaData)
        {
			if (SlotObject != null)
			{
				HookupObjectEvents();
				foreach(var ih in EventHookups)
                {
					ih.Begun(umaData);
                }
			}
        }

		public void Completed(UMAData umaData)
        {
			if (SlotObject != null)
            {
				foreach(var ih in EventHookups)
                {
					ih.Completed(umaData,this.SlotObject);	
                }
            }
        }

		private void HookupObjectEvents()
        {
			if (this.SlotObject != null)
			{
				if (SlotObjectHookedUp && EventHookups.Count > 0)
					return;

				SlotObjectHookedUp = true;
				var Behaviors = SlotObject.GetComponents<MonoBehaviour>();
				Debug.Log($"There are {Behaviors.Length} components");

				foreach (var mb in Behaviors)
				{
					if (mb is IUMAEventHookup)
					{
						Debug.Log("SDA Hooking up events");
						EventHookups.Add(mb as IUMAEventHookup);
						(mb as IUMAEventHookup).HookupEvents(this);
					}
				}
			}
		}

		public void OnDestroy()
        {
			if (meshData != null)
				meshData.FreeBoneWeights();
        }

		public void OnDisable()
		{ 
			if (meshData != null)
				meshData.FreeBoneWeights();
		}

		public int GetTextureChannelCount(UMAGeneratorBase generator)
		{
			return material.channels.Length;
		}
        
		public override string ToString()
		{
			return "SlotData: " + slotName;
		}

        public void UpdateMeshData(SkinnedMeshRenderer meshRenderer, string rootBoneName)
        {
            meshData = new UMAMeshData();
			meshData.SlotName = this.slotName;
            meshData.RootBoneName = rootBoneName;
            meshData.RetrieveDataFromUnityMesh(meshRenderer);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        public void UpdateMeshData(SkinnedMeshRenderer meshRenderer)
		{
			meshData = new UMAMeshData();
			meshData.SlotName = this.slotName;
			meshData.RetrieveDataFromUnityMesh(meshRenderer);
#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(this);
#endif
		}


        public void OnEnable()
        {
			if (meshData == null)
				return;

			if (meshData.LoadedBoneweights)
            {
				// already loaded. just return.
				return;
            }
			if (meshData.ManagedBoneWeights != null && meshData.ManagedBoneWeights.Length > 0)
            {
				meshData.LoadVariableBoneWeights();
            }
			else if (meshData.boneWeights != null && meshData.boneWeights.Length > 0)
			{
				meshData.LoadBoneWeights(); 
			}
		}

		public void EnsureBoneWeights()
        {
			if (meshData.ManagedBonesPerVertex == null || meshData.ManagedBonesPerVertex.Length == 0)
            {
				meshData.LoadBoneWeights();
            }
        }


        public void UpdateMeshData()
		{
		}

		public void OnAfterDeserialize()
		{
			nameHash = UMAUtils.StringToHash(slotName);
		}

		public void OnBeforeSerialize() 
		{ 

		}

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
			Races = source.Races;
		}
	}
}
