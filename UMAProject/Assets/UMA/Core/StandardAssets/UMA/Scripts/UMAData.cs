using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Events;

namespace UMA
{
	/// <summary>
	/// UMA data holds the recipe for creating a character and skeleton and Unity references for a built character.
	/// </summary>
	public class UMAData : MonoBehaviour
	{
		[Obsolete("UMA 2.5 myRenderer is now obsolete, an uma can have multiple renderers. Use int rendererCount { get; } and GetRenderer(int) instead.", false)]
		public SkinnedMeshRenderer myRenderer;

		private SkinnedMeshRenderer[] renderers;
		public int rendererCount { get { return renderers == null ? 0 : renderers.Length; } }

		public SkinnedMeshRenderer GetRenderer(int idx)
		{
			return renderers[idx];
		}

		public SkinnedMeshRenderer[] GetRenderers()
		{
			return renderers;
		}

		public void SetRenderers(SkinnedMeshRenderer[] renderers)
		{
#pragma warning disable 618
			myRenderer = (renderers != null && renderers.Length > 0) ? renderers[0] : null;
#pragma warning restore 618
			this.renderers = renderers;
		}

		[NonSerialized]
		public bool firstBake;

		public UMAGeneratorBase umaGenerator;

		[NonSerialized]
		public GeneratedMaterials generatedMaterials = new GeneratedMaterials();

		private LinkedListNode<UMAData> listNode;
		public void MoveToList(LinkedList<UMAData> list)
		{
			if (listNode.List != null)
			{
				listNode.List.Remove(listNode);
			}
			list.AddLast(listNode);
		}


		public float atlasResolutionScale = 1f;

		/// <summary>
		/// Has the character mesh changed?
		/// </summary>
		public bool isMeshDirty;
		/// <summary>
		/// Has the character skeleton changed?
		/// </summary>
		public bool isShapeDirty;
		/// <summary>
		/// Have the overlay textures changed?
		/// </summary>
		public bool isTextureDirty;
		/// <summary>
		/// Have the texture atlases changed?
		/// </summary>
		public bool isAtlasDirty;

        public BlendShapeSettings blendShapeSettings = new BlendShapeSettings();

		public RuntimeAnimatorController animationController;

		private Dictionary<int, int> animatedBonesTable;

		public void ResetAnimatedBones()
		{
			if (animatedBonesTable == null)
			{
				animatedBonesTable = new Dictionary<int, int>();
			}
			else
			{
				animatedBonesTable.Clear();
			}
		}

		public void RegisterAnimatedBone(int hash)
		{
			if (!animatedBonesTable.ContainsKey(hash))
			{
				animatedBonesTable.Add(hash, animatedBonesTable.Count);
			}
		}

		public Transform GetGlobalTransform()
		{
			return (renderers != null && renderers.Length > 0) ? renderers[0].rootBone : umaRoot.transform.Find("Global");
		}

		public void RegisterAnimatedBoneHierarchy(int hash)
		{
			if (!animatedBonesTable.ContainsKey(hash))
			{
				animatedBonesTable.Add(hash, animatedBonesTable.Count);
			}
		}

		public bool cancelled { get; private set; }
		[NonSerialized]
		public bool dirty = false;

		private bool isOfficiallyCreated = false;
		/// <summary>
		/// Callback event when character has been updated.
		/// </summary>
		public event Action<UMAData> OnCharacterUpdated { add { if (CharacterUpdated == null) CharacterUpdated = new UMADataEvent(); CharacterUpdated.AddListener(new UnityAction<UMAData>(value)); } remove { CharacterUpdated.RemoveListener(new UnityAction<UMAData>(value)); } }
		/// <summary>
		/// Callback event when character has been completely created.
		/// </summary>
		public event Action<UMAData> OnCharacterCreated { add { if (CharacterCreated == null) CharacterCreated = new UMADataEvent(); CharacterCreated.AddListener(new UnityAction<UMAData>(value)); } remove { CharacterCreated.RemoveListener(new UnityAction<UMAData>(value)); } }
		/// <summary>
		/// Callback event when character has been destroyed.
		/// </summary>
		public event Action<UMAData> OnCharacterDestroyed { add { if (CharacterDestroyed == null) CharacterDestroyed = new UMADataEvent(); CharacterDestroyed.AddListener(new UnityAction<UMAData>(value)); } remove { CharacterDestroyed.RemoveListener(new UnityAction<UMAData>(value)); } }

		/// Callback event when character DNA has been updated.
		/// </summary>
		public event Action<UMAData> OnCharacterDnaUpdated { add { if (CharacterDnaUpdated == null) CharacterDnaUpdated = new UMADataEvent(); CharacterDnaUpdated.AddListener(new UnityAction<UMAData>(value)); } remove { CharacterDnaUpdated.RemoveListener(new UnityAction<UMAData>(value)); } }
		public UMADataEvent CharacterCreated;
		public UMADataEvent CharacterDestroyed;
		public UMADataEvent CharacterUpdated;
		public UMADataEvent CharacterDnaUpdated;

		public GameObject umaRoot;

		public UMARecipe umaRecipe;
		public Animator animator;
		public UMASkeleton skeleton;

		/// <summary>
		/// The approximate height of the character. Calculated by DNA converters.
		/// </summary>
		public float characterHeight = 2f;
		/// <summary>
		/// The approximate radius of the character. Calculated by DNA converters.
		/// </summary>
		public float characterRadius = 0.25f;
		/// <summary>
		/// The approximate mass of the character. Calculated by DNA converters.
		/// </summary>
		public float characterMass = 50f;

		public UMAData()
		{
			listNode = new LinkedListNode<UMAData>(this);
		}

		void Awake()
		{
			firstBake = true;

			if (!umaGenerator)
			{
				var generatorGO = GameObject.Find("UMAGenerator");
				if (generatorGO == null) return;
				umaGenerator = generatorGO.GetComponent<UMAGeneratorBase>();
			}

			if (umaRecipe == null)
			{
				umaRecipe = new UMARecipe();
			}
			else
			{
				SetupOnAwake();
			}
		}

		public void SetupOnAwake()
		{
			umaRoot = gameObject;
			animator = umaRoot.GetComponent<Animator>();
		}

#pragma warning disable 618
		/// <summary>
		/// Shallow copy from another UMAData.
		/// </summary>
		/// <param name="other">Source UMAData.</param>
		public void Assign(UMAData other)
		{
			animator = other.animator;
			//myRenderer = other.myRenderer;
			renderers = other.renderers;
			umaRoot = other.umaRoot;
			if (animationController == null)
			{
				animationController = other.animationController;
			}
		}
#pragma warning restore 618

		public bool Validate()
		{
			bool valid = true;
			if (umaGenerator == null)
			{
				Debug.LogError("UMA data missing required generator!");
				valid = false;
			}

			if (umaRecipe == null)
			{
				Debug.LogError("UMA data missing required recipe!");
				valid = false;
			}
			else
			{
				valid = valid && umaRecipe.Validate();
			}

			if (animationController == null)
			{
				if (Application.isPlaying)
					Debug.LogWarning("No animation controller supplied.");
			}

#if UNITY_EDITOR
			if (!valid && UnityEditor.EditorApplication.isPlaying)
			{
				Debug.LogError("UMAData: Recipe or Generator is not valid!");
				UnityEditor.EditorApplication.isPaused = true;
			}
#endif

			return valid;
		}

		[System.Serializable]
		public class GeneratedMaterials
		{
			public List<GeneratedMaterial> materials = new List<GeneratedMaterial>();
			public int rendererCount;
		}


		[System.Serializable]
		public class GeneratedMaterial
		{
			public UMAMaterial umaMaterial;
			public Material material;
			public List<MaterialFragment> materialFragments = new List<MaterialFragment>();
			public Texture[] resultingAtlasList;
			public Vector2 cropResolution;
			public float resolutionScale;
			public string[] textureNameList;
			public int renderer;
		}

		[System.Serializable]
		public class MaterialFragment
		{
			public int size;
			public Color baseColor;
			public UMAMaterial umaMaterial;
			public Rect[] rects;
			public textureData[] overlays;
			public Color32[] overlayColors;
			public Color[][] channelMask;
			public Color[][] channelAdditiveMask;
			public SlotData slotData;
			public OverlayData[] overlayData;
			public Rect atlasRegion;
			public bool isRectShared;
			public List<OverlayData> overlayList;
			public MaterialFragment rectFragment;
			public textureData baseOverlay;

			public Color GetMultiplier(int overlay, int textureType)
			{
				if (channelMask[overlay] != null && channelMask[overlay].Length > 0)
				{
					return channelMask[overlay][textureType];
				}
				else
				{
					if (textureType > 0) return Color.white;
					if (overlay == 0) return baseColor;
					return overlayColors[overlay - 1];
				}
			}
			public Color32 GetAdditive(int overlay, int textureType)
			{
				if (channelAdditiveMask[overlay] != null && channelAdditiveMask[overlay].Length > 0)
				{
					return channelAdditiveMask[overlay][textureType];
				}
				else
				{
					return new Color32(0, 0, 0, 0);
				}
			}
		}

		public void Show()
		{
			for (int i = 0; i < rendererCount; i++)
				GetRenderer(i).enabled = true;
		}

		public void Hide()
		{
			for (int i = 0; i < rendererCount; i++)
				GetRenderer(i).enabled = false;
		}

		[System.Serializable]
		public class textureData
		{
			public Texture[] textureList;
			public Texture alphaTexture;
			public OverlayDataAsset.OverlayType overlayType;
		}

		[System.Serializable]
		public class resultAtlasTexture
		{
			public Texture[] textureList;
		}

		/// <summary>
		/// The UMARecipe class contains the race, DNA, and color data required to build a UMA character.
		/// </summary>
		[System.Serializable]
		public class UMARecipe
		{
			public RaceData raceData;
			Dictionary<int, UMADnaBase> _umaDna;
			protected Dictionary<int, UMADnaBase> umaDna
			{
				get
				{
					if (_umaDna == null)
					{
						_umaDna = new Dictionary<int, UMADnaBase>();
						for (int i = 0; i < dnaValues.Count; i++)
							_umaDna.Add(dnaValues[i].DNATypeHash, dnaValues[i]);
					}
					return _umaDna;
				}
				set
				{
					_umaDna = value;
				}
			}
			protected Dictionary<int, DnaConverterBehaviour.DNAConvertDelegate> umaDnaConverter = new Dictionary<int, DnaConverterBehaviour.DNAConvertDelegate>();
			protected Dictionary<string, int> mergedSharedColors = new Dictionary<string, int>();
			public List<UMADnaBase> dnaValues = new List<UMADnaBase>();
			public SlotData[] slotDataList;
			public OverlayColorData[] sharedColors;

			public bool Validate()
			{
				bool valid = true;
				if (raceData == null)
				{
					Debug.LogError("UMA recipe missing required race!");
					valid = false;
				}
				else
				{
					valid = valid && raceData.Validate();
				}

				if (slotDataList == null || slotDataList.Length == 0)
				{
					Debug.LogError("UMA recipe slot list is empty!");
					valid = false;
				}
				int slotDataCount = 0;
				for (int i = 0; i < slotDataList.Length; i++)
				{
					var slotData = slotDataList[i];
					if (slotData != null)
					{
						slotDataCount++;
						valid = valid && slotData.Validate();
					}
				}
				if (slotDataCount < 1)
				{
					Debug.LogError("UMA recipe slot list contains only null objects!");
					valid = false;
				}
				return valid;
			}

#pragma warning disable 618
			/// <summary>
			/// Gets the DNA array.
			/// </summary>
			/// <returns>The DNA array.</returns>
			public UMADnaBase[] GetAllDna()
			{
				if ((raceData == null) || (slotDataList == null))
				{
					return new UMADnaBase[0];
				}
				return dnaValues.ToArray();
			}

			/// <summary>
			/// Adds the DNA specified.
			/// </summary>
			/// <param name="dna">DNA.</param>
			public void AddDna(UMADnaBase dna)
			{
				umaDna.Add(dna.DNATypeHash, dna);
				dnaValues.Add(dna);
			}

			/// <summary>
			/// Get DNA of specified type.
			/// </summary>
			/// <returns>The DNA (or null if not found).</returns>
			/// <typeparam name="T">Type.</typeparam>
			public T GetDna<T>()
				where T : UMADnaBase
			{
				UMADnaBase dna;
				if (umaDna.TryGetValue(UMAUtils.StringToHash(typeof(T).Name), out dna))
				{
					return dna as T;
				}
				return null;
			}

			/// <summary>
			/// Removes all DNA.
			/// </summary>
			public void ClearDna()
			{
				umaDna.Clear();
				dnaValues.Clear();
			}
			/// <summary>
			/// DynamicUMADna:: a version of RemoveDna that uses the dnaTypeNameHash
			/// </summary>
			/// <param name="dnaTypeNameHash"></param>
			public void RemoveDna(int dnaTypeNameHash)
			{
				dnaValues.Remove(umaDna[dnaTypeNameHash]);
				umaDna.Remove(dnaTypeNameHash);
			}
			/// <summary>
			/// Removes the specified DNA.
			/// </summary>
			/// <param name="type">Type.</param>
			public void RemoveDna(Type type)
			{
				int dnaTypeNameHash = UMAUtils.StringToHash(type.Name);
				dnaValues.Remove(umaDna[dnaTypeNameHash]);
				umaDna.Remove(dnaTypeNameHash);
			}

			/// <summary>
			/// Get DNA of specified type.
			/// </summary>
			/// <returns>The DNA (or null if not found).</returns>
			/// <param name="type">Type.</param>
			public UMADnaBase GetDna(Type type)
			{
				UMADnaBase dna;
				if (umaDna.TryGetValue(UMAUtils.StringToHash(type.Name), out dna))
				{
					return dna;
				}
				return null;
			}
			/// <summary>
			/// Get DNA of specified type.
			/// </summary>
			/// <returns>The DNA (or null if not found).</returns>
			/// <param name="dnaTypeNameHash">Type.</param>
			public UMADnaBase GetDna(int dnaTypeNameHash)
			{
				UMADnaBase dna;
				if (umaDna.TryGetValue(dnaTypeNameHash, out dna))
				{
					return dna;
				}
				return null;
			}

			/// <summary>
			/// Get DNA of specified type, adding if not found.
			/// </summary>
			/// <returns>The DNA.</returns>
			/// <typeparam name="T">Type.</typeparam>
			public T GetOrCreateDna<T>()
				where T : UMADnaBase
			{
				T res = GetDna<T>();
				if (res == null)
				{
					res = typeof(T).GetConstructor(System.Type.EmptyTypes).Invoke(null) as T;
					umaDna.Add(res.DNATypeHash, res);
					dnaValues.Add(res);
				}
				return res;
			}

			/// <summary>
			/// Get DNA of specified type, adding if not found.
			/// </summary>
			/// <returns>The DNA.</returns>
			/// <param name="type">Type.</param>
			public UMADnaBase GetOrCreateDna(Type type)
			{
				UMADnaBase dna;
				var typeNameHash = UMAUtils.StringToHash(type.Name);
				if (umaDna.TryGetValue(typeNameHash, out dna))
				{
					return dna;
				}

				dna = type.GetConstructor(System.Type.EmptyTypes).Invoke(null) as UMADnaBase;
				umaDna.Add(typeNameHash, dna);
				dnaValues.Add(dna);
				return dna;
			}
			/// <summary>
			/// Get DNA of specified type, adding if not found.
			/// </summary>
			/// <returns>The DNA.</returns>
			/// <param name="type">Type.</param>
			public UMADnaBase GetOrCreateDna(Type type, int dnaTypeHash)
			{
				UMADnaBase dna;
				if (umaDna.TryGetValue(dnaTypeHash, out dna))
				{
					return dna;
				}

				dna = type.GetConstructor(System.Type.EmptyTypes).Invoke(null) as UMADnaBase;
				dna.DNATypeHash = dnaTypeHash;
				umaDna.Add(dnaTypeHash, dna);
				dnaValues.Add(dna);
				return dna;
			}
#pragma warning restore 618
			/// <summary>
			/// Sets the race.
			/// </summary>
			/// <param name="raceData">Race.</param>
			public void SetRace(RaceData raceData)
			{
				this.raceData = raceData;
				ClearDNAConverters();
			}

			/// <summary>
			/// Gets the race.
			/// </summary>
			/// <returns>The race.</returns>
			public RaceData GetRace()
			{
				return this.raceData;
			}

			/// <summary>
			/// Sets the slot at a given index.
			/// </summary>
			/// <param name="index">Index.</param>
			/// <param name="slot">Slot.</param>
			public void SetSlot(int index, SlotData slot)
			{
				if (slotDataList == null)
				{
					slotDataList = new SlotData[1];
				}

				if (index >= slotDataList.Length)
				{
					System.Array.Resize<SlotData>(ref slotDataList, index + 1);
				}
				slotDataList[index] = slot;
			}

			/// <summary>
			/// Sets the entire slot array.
			/// </summary>
			/// <param name="slots">Slots.</param>
			public void SetSlots(SlotData[] slots)
			{
				slotDataList = slots;
			}

			/// <summary>
			/// Combine additional slot with current data.
			/// </summary>
			/// <param name="slot">Slot.</param>
			/// <param name="dontSerialize">If set to <c>true</c> slot will not be serialized.</param>
			public SlotData MergeSlot(SlotData slot, bool dontSerialize)
			{
				if ((slot == null) || (slot.asset == null))
					return null;

				int overlayCount = 0;
				for (int i = 0; i < slotDataList.Length; i++)
				{
					if (slotDataList[i] == null)
						continue;
					if (slot.asset == slotDataList[i].asset)
					{
						SlotData originalSlot = slotDataList[i];
						overlayCount = slot.OverlayCount;
						for (int j = 0; j < overlayCount; j++)
						{
							OverlayData overlay = slot.GetOverlay(j);
							//DynamicCharacterSystem:: Needs to use alternative methods that find equivalent overlays since they may not be Equal if they were in an assetBundle
							OverlayData originalOverlay = originalSlot.GetEquivalentUsedOverlay(overlay);
							if (originalOverlay != null)
							{
								originalOverlay.CopyColors(overlay);//also copies textures
								if (overlay.colorData.HasName())
								{
									int sharedIndex;
									if (mergedSharedColors.TryGetValue(overlay.colorData.name, out sharedIndex))
									{
										originalOverlay.colorData = sharedColors[sharedIndex];
									}
								}
							}
							else
							{
								OverlayData overlayCopy = overlay.Duplicate();
								if (overlayCopy.colorData.HasName())
								{
									int sharedIndex;
									if (mergedSharedColors.TryGetValue(overlayCopy.colorData.name, out sharedIndex))
									{
										overlayCopy.colorData = sharedColors[sharedIndex];
									}
								}
								originalSlot.AddOverlay(overlayCopy);
							}
						}
						originalSlot.dontSerialize = dontSerialize;
						return originalSlot;
					}
				}

				int insertIndex = slotDataList.Length;
				System.Array.Resize<SlotData>(ref slotDataList, slotDataList.Length + 1);

				SlotData slotCopy = slot.Copy();
				slotCopy.dontSerialize = dontSerialize;
				overlayCount = slotCopy.OverlayCount;
				for (int j = 0; j < overlayCount; j++)
				{
					OverlayData overlay = slotCopy.GetOverlay(j);
					if (overlay.colorData.HasName())
					{
						int sharedIndex;
						if (mergedSharedColors.TryGetValue(overlay.colorData.name, out sharedIndex))
						{
							overlay.colorData = sharedColors[sharedIndex];
						}
					}
				}
				slotDataList[insertIndex] = slotCopy;
				MergeMatchingOverlays();
                return slotCopy;
			}

			/// <summary>
			/// Gets a slot by index.
			/// </summary>
			/// <returns>The slot.</returns>
			/// <param name="index">Index.</param>
			public SlotData GetSlot(int index)
			{
				if (index < slotDataList.Length)
					return slotDataList[index];
				return null;
			}

			/// <summary>
			/// Gets the complete array of slots.
			/// </summary>
			/// <returns>The slot array.</returns>
			public SlotData[] GetAllSlots()
			{
				return slotDataList;
			}

			/// <summary>
			/// Gets the number of slots.
			/// </summary>
			/// <returns>The slot array size.</returns>
			public int GetSlotArraySize()
			{
				return slotDataList.Length;
			}

			/// <summary>
			/// Are two overlay lists the same?
			/// </summary>
			/// <returns><c>true</c>, if lists match, <c>false</c> otherwise.</returns>
			/// <param name="list1">List1.</param>
			/// <param name="list2">List2.</param>
			public static bool OverlayListsMatch(List<OverlayData> list1, List<OverlayData> list2)
			{
				if ((list1 == null) || (list2 == null))
					return false;
				if ((list1.Count == 0) || (list1.Count != list2.Count))
					return false;

				for (int i = 0; i < list1.Count; i++)
				{
					OverlayData overlay1 = list1[i];
					if (!(overlay1))
						continue;
					bool found = false;
					for (int j = 0; j < list2.Count; j++)
					{
						OverlayData overlay2 = list2[i];
						if (!(overlay2))
							continue;

						if (OverlayData.Equivalent(overlay1, overlay2))
						{
							found = true;
							break;
						}
					}
					if (!found)
						return false;
				}

				return true;
			}

			/// <summary>
			/// Ensures slots with matching overlays will share the same references.
			/// </summary>
			public void MergeMatchingOverlays()
			{
				for (int i = 0; i < slotDataList.Length; i++)
				{
					if (slotDataList[i] == null)
						continue;
					List<OverlayData> slotOverlays = slotDataList[i].GetOverlayList();
					for (int j = i + 1; j < slotDataList.Length; j++)
					{
						if (slotDataList[j] == null)
							continue;
						List<OverlayData> slot2Overlays = slotDataList[j].GetOverlayList();
						if (OverlayListsMatch(slotOverlays, slot2Overlays))
						{
							slotDataList[j].SetOverlayList(slotOverlays);
						}
					}
				}
			}

#pragma warning disable 618
			/// <summary>
			/// Applies each DNA converter to the UMA data and skeleton.
			/// </summary>
			/// <param name="umaData">UMA data.</param>
			public void ApplyDNA(UMAData umaData, bool fixUpUMADnaToDynamicUMADna = false)
			{
				EnsureAllDNAPresent();
				//DynamicUMADna:: when loading an older recipe that has UMADnaHumanoid/Tutorial into a race that now uses DynamicUmaDna the following wont work
				//so check that and fix it if it happens
				if (fixUpUMADnaToDynamicUMADna)
					DynamicDNAConverterBehaviourBase.FixUpUMADnaToDynamicUMADna(this);
				foreach (var dnaEntry in umaDna)
				{
					DnaConverterBehaviour.DNAConvertDelegate dnaConverter;
					if (umaDnaConverter.TryGetValue(dnaEntry.Key, out dnaConverter))
					{
						dnaConverter(umaData, umaData.GetSkeleton());
					}
					else
					{
						//DynamicUMADna:: try again this time calling FixUpUMADnaToDynamicUMADna first
						if (fixUpUMADnaToDynamicUMADna == false)
						{
							ApplyDNA(umaData, true);
							break;
						}
						else
						{
							Debug.LogWarning("Cannot apply dna: " + dnaEntry.Value.GetType().Name + " using key " + dnaEntry.Key);
						}
					}
				}
			}

			/// <summary>
			/// Ensures all DNA convertes from slot and race data are defined.
			/// </summary>
			public void EnsureAllDNAPresent()
			{
				List<int> requiredDnas = new List<int>();
				if (raceData != null)
				{
					foreach (var converter in raceData.dnaConverterList)
					{
						var dnaTypeHash = converter.DNATypeHash;
						//'old' dna converters return a typehash based on the type name. 
						//Dynamic DNA Converters return the typehash of their dna asset or 0 if none is assigned- we dont want to include those
						if (dnaTypeHash == 0)
						continue;
						requiredDnas.Add(dnaTypeHash);
                        if (!umaDna.ContainsKey(dnaTypeHash))
						{
							var dna = converter.DNAType.GetConstructor(System.Type.EmptyTypes).Invoke(null) as UMADnaBase;
							dna.DNATypeHash = dnaTypeHash;
							//DynamicUMADna:: needs the DNAasset from the converter - moved because this might change
							if (converter is DynamicDNAConverterBehaviourBase)
							{
								((DynamicUMADnaBase)dna).dnaAsset = ((DynamicDNAConverterBehaviourBase)converter).dnaAsset;
							}
							umaDna.Add(dnaTypeHash, dna);
							dnaValues.Add(dna);
						}
						else if (converter is DynamicDNAConverterBehaviourBase)
						{
							var dna = umaDna[dnaTypeHash];
							((DynamicUMADnaBase)dna).dnaAsset = ((DynamicDNAConverterBehaviourBase)converter).dnaAsset;
						}
					}
				}
				foreach (var slotData in slotDataList)
				{
					if (slotData != null && slotData.asset.slotDNA != null)
					{
						var dnaTypeHash = slotData.asset.slotDNA.DNATypeHash;
						//'old' dna converters return a typehash based on the type name. 
						//Dynamic DNA Converters return the typehash of their dna asset or 0 if none is assigned- we dont want to include those
						if (dnaTypeHash == 0)
							continue;
						requiredDnas.Add(dnaTypeHash);
						if (!umaDna.ContainsKey(dnaTypeHash))
						{
							var dna = slotData.asset.slotDNA.DNAType.GetConstructor(System.Type.EmptyTypes).Invoke(null) as UMADnaBase;
							dna.DNATypeHash = dnaTypeHash;
							//DynamicUMADna:: needs the DNAasset from the converter TODO are there other places where I heed to sort out this slotDNA?
							if (slotData.asset.slotDNA is DynamicDNAConverterBehaviourBase)
							{
								((DynamicUMADnaBase)dna).dnaAsset = ((DynamicDNAConverterBehaviourBase)slotData.asset.slotDNA).dnaAsset;
							}
							umaDna.Add(dnaTypeHash, dna);
							dnaValues.Add(dna);
						}
						else if (slotData.asset.slotDNA is DynamicDNAConverterBehaviourBase)
						{
							var dna = umaDna[dnaTypeHash];
							((DynamicUMADnaBase)dna).dnaAsset = ((DynamicDNAConverterBehaviourBase)slotData.asset.slotDNA).dnaAsset;
						}
                    }
				}
				foreach (int addedDNAHash in umaDnaConverter.Keys)
				{
					requiredDnas.Add(addedDNAHash);
				}

				//now remove any we no longer need
				var keysToRemove = new List<int>();
				foreach(var kvp in umaDna)
				{
					if (!requiredDnas.Contains(kvp.Key))
						keysToRemove.Add(kvp.Key);
				}
				for(int i = 0; i < keysToRemove.Count; i++)
				{
					RemoveDna(keysToRemove[i]);
				}
				
			}
#pragma warning restore 618
			/// <summary>
			/// Resets the DNA converters to those defined in the race.
			/// </summary>
			public void ClearDNAConverters()
			{
				umaDnaConverter.Clear();
				if (raceData != null)
				{
					foreach (var converter in raceData.dnaConverterList)
					{
						if(converter == null)
						{
							Debug.LogWarning("RaceData " + raceData.raceName + " has a missing DNAConverter");
							continue;
						}
						//'old' dna converters return a typehash based on the type name. 
						//Dynamic DNA Converters return the typehash of their dna asset or 0 if none is assigned- we dont want to include those
						if (converter.DNATypeHash == 0)
							continue;
						if (!umaDnaConverter.ContainsKey(converter.DNATypeHash))
						{
							umaDnaConverter.Add(converter.DNATypeHash, converter.ApplyDnaAction);
						}
						else
						{
							//We MUST NOT give DynamicDNA the same hash a UMADnaHumanoid or else we loose the values
							Debug.Log(raceData.raceName + " has multiple dna converters that are trying to use the same dna (" + converter.DNATypeHash + "). This is not allowed.");
						}
					}
				}
			}

			/// <summary>
			/// Adds a DNA converter.
			/// </summary>
			/// <param name="dnaConverter">DNA converter.</param>
			public void AddDNAUpdater(DnaConverterBehaviour dnaConverter)
			{
				if (dnaConverter == null) return;
				//DynamicDNAConverter:: We need to SET these values using the TypeHash since 
				//just getting the hash of the DNAType will set the same value for all instance of a DynamicDNAConverter
				if (!umaDnaConverter.ContainsKey(dnaConverter.DNATypeHash))
				{
					umaDnaConverter.Add(dnaConverter.DNATypeHash, dnaConverter.ApplyDnaAction);
				}
				else
				{
					Debug.Log(raceData.raceName + " has multiple dna converters that are trying to use the same dna ("+ dnaConverter.DNATypeHash+"). This is not allowed.");
				}
			}

			/// <summary>
			/// Shallow copy of UMARecipe.
			/// </summary>
			public UMARecipe Mirror()
			{
				var newRecipe = new UMARecipe();
				newRecipe.raceData = raceData;
				newRecipe.umaDna = umaDna;
				newRecipe.dnaValues = dnaValues;
				newRecipe.slotDataList = slotDataList;
				return newRecipe;
			}

			/// <summary>
			/// Combine additional recipe with current data.
			/// </summary>
			/// <param name="recipe">Recipe.</param>
			/// <param name="dontSerialize">If set to <c>true</c> recipe will not be serialized.</param>
			public void Merge(UMARecipe recipe, bool dontSerialize)
			{
				if (recipe == null)
					return;

				if ((recipe.raceData != null) && (recipe.raceData != raceData))
				{
					Debug.LogWarning("Merging recipe with conflicting race data: " + recipe.raceData.name);
				}

				foreach (var dnaEntry in recipe.umaDna)
				{
					var destDNA = GetOrCreateDna(dnaEntry.Value.GetType(), dnaEntry.Key);
					destDNA.Values = dnaEntry.Value.Values;
				}

				mergedSharedColors.Clear();
				if (sharedColors == null)
					sharedColors = new OverlayColorData[0];
				if (recipe.sharedColors != null)
				{
					for (int i = 0; i < sharedColors.Length; i++)
					{
						if (sharedColors[i] != null && sharedColors[i].HasName())
						{
							while (mergedSharedColors.ContainsKey(sharedColors[i].name))
							{
								sharedColors[i].name = sharedColors[i].name + ".";
							}
							mergedSharedColors.Add(sharedColors[i].name, i);
						}
					}

					for (int i = 0; i < recipe.sharedColors.Length; i++)
					{
						OverlayColorData sharedColor = recipe.sharedColors[i];
						if (sharedColor != null && sharedColor.HasName())
						{
							int sharedIndex;
							if (!mergedSharedColors.TryGetValue(sharedColor.name, out sharedIndex))
							{
								int index = sharedColors.Length;
								mergedSharedColors.Add(sharedColor.name, index);
								Array.Resize<OverlayColorData>(ref sharedColors, index + 1);
								sharedColors[index] = sharedColor.Duplicate();
							}
						}
					}
				}

				if (slotDataList == null)
					slotDataList = new SlotData[0];
				if (recipe.slotDataList != null)
				{
					for (int i = 0; i < recipe.slotDataList.Length; i++)
					{
						MergeSlot(recipe.slotDataList[i], dontSerialize);
					}
				}
			}
		}


		[System.Serializable]
		public class BoneData
		{
			public Transform boneTransform;
			public Vector3 originalBoneScale;
			public Vector3 originalBonePosition;
			public Quaternion originalBoneRotation;
		}

		/// <summary>
		/// Calls character updated and/or created events.
		/// </summary>
		public void FireUpdatedEvent(bool cancelled)
		{
			this.cancelled = cancelled;
			if (!this.cancelled && !isOfficiallyCreated)
			{
				isOfficiallyCreated = true;
				if (CharacterCreated != null)
				{
					CharacterCreated.Invoke(this);
				}
			}
			if (CharacterUpdated != null)
			{
				CharacterUpdated.Invoke(this);
			}
			dirty = false;
		}

		public void ApplyDNA()
		{
			umaRecipe.ApplyDNA(this);
		}

		public virtual void Dirty()
		{
			if (dirty) return;
			dirty = true;
			if (!umaGenerator)
			{
				umaGenerator = GameObject.Find("UMAGenerator").GetComponent<UMAGeneratorBase>();
			}
			if (umaGenerator)
			{
				umaGenerator.addDirtyUMA(this);
			}
		}

		void OnDestroy()
		{
			if (isOfficiallyCreated)
			{
				if (CharacterDestroyed != null)
				{
					CharacterDestroyed.Invoke(this);
				}
				isOfficiallyCreated = false;
			}
			if (umaRoot != null)
			{
				CleanTextures();
				CleanMesh(true);
				CleanAvatar();
				UMAUtils.DestroySceneObject(umaRoot);
			}
		}

		/// <summary>
		/// Destory Mecanim avatar and animator.
		/// </summary>
		public void CleanAvatar()
		{
			animationController = null;
			if (animator != null)
			{
				if (animator.avatar) UMAUtils.DestroySceneObject(animator.avatar);
				if (animator) UMAUtils.DestroySceneObject(animator);
			}
		}

		/// <summary>
		/// Destroy textures used to render mesh.
		/// </summary>
		public void CleanTextures()
		{
			for (int atlasIndex = 0; atlasIndex < generatedMaterials.materials.Count; atlasIndex++)
			{
				if (generatedMaterials.materials[atlasIndex] != null && generatedMaterials.materials[atlasIndex].resultingAtlasList != null)
				{
					for (int textureIndex = 0; textureIndex < generatedMaterials.materials[atlasIndex].resultingAtlasList.Length; textureIndex++)
					{
						if (generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex] != null)
						{
							Texture tempTexture = generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex];
							if (tempTexture is RenderTexture)
							{
								RenderTexture tempRenderTexture = tempTexture as RenderTexture;
								tempRenderTexture.Release();
								UMAUtils.DestroySceneObject(tempRenderTexture);
							}
							else
							{
								UMAUtils.DestroySceneObject(tempTexture);
							}
							generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex] = null;
						}
					}
				}
			}
		}

		/// <summary>
		/// Destroy materials used to render mesh.
		/// </summary>
		/// <param name="destroyRenderer">If set to <c>true</c> destroy mesh renderer.</param>
		public void CleanMesh(bool destroyRenderer)
		{
			for(int j = 0; j < rendererCount; j++)
			{
				var renderer = GetRenderer(j);
				var mats = renderer.sharedMaterials;
				for (int i = 0; i < mats.Length; i++)
				{
					if (mats[i])
					{
						UMAUtils.DestroySceneObject(mats[i]);
					}
				}
				if (destroyRenderer)
				{
					UMAUtils.DestroySceneObject(renderer.sharedMesh);
					UMAUtils.DestroySceneObject(renderer);
				}
			}
		}

		public Texture[] backUpTextures()
		{
			List<Texture> textureList = new List<Texture>();

			for (int atlasIndex = 0; atlasIndex < generatedMaterials.materials.Count; atlasIndex++)
			{
				if (generatedMaterials.materials[atlasIndex] != null && generatedMaterials.materials[atlasIndex].resultingAtlasList != null)
				{
					for (int textureIndex = 0; textureIndex < generatedMaterials.materials[atlasIndex].resultingAtlasList.Length; textureIndex++)
					{

						if (generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex] != null)
						{
							Texture tempTexture = generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex];
							textureList.Add(tempTexture);
							generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex] = null;
						}
					}
				}
			}

			return textureList.ToArray();
		}

		public RenderTexture GetFirstRenderTexture()
		{
			for (int atlasIndex = 0; atlasIndex < generatedMaterials.materials.Count; atlasIndex++)
			{
				if (generatedMaterials.materials[atlasIndex] != null && generatedMaterials.materials[atlasIndex].resultingAtlasList != null)
				{
					for (int textureIndex = 0; textureIndex < generatedMaterials.materials[atlasIndex].resultingAtlasList.Length; textureIndex++)
					{
						if (generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex] != null)
						{
							RenderTexture tempTexture = generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex] as RenderTexture;
							if (tempTexture != null)
							{
								return tempTexture;
							}
						}
					}
				}
			}
			return null;
		}

		/// <summary>
		/// Gets the game object for a bone by name.
		/// </summary>
		/// <returns>The game object (or null if hash not in skeleton).</returns>
		/// <param name="boneName">Bone name.</param>
		public GameObject GetBoneGameObject(string boneName)
		{
			return GetBoneGameObject(UMAUtils.StringToHash(boneName));
		}

		/// <summary>
		/// Gets the game object for a bone by name hash.
		/// </summary>
		/// <returns>The game object (or null if hash not in skeleton).</returns>
		/// <param name="boneHash">Bone name hash.</param>
		public GameObject GetBoneGameObject(int boneHash)
		{
			return skeleton.GetBoneGameObject(boneHash);
		}

		/// <summary>
		/// Gets the complete DNA array.
		/// </summary>
		/// <returns>The DNA array.</returns>
		public UMADnaBase[] GetAllDna()
		{
			return umaRecipe.GetAllDna();
		}

		/// <summary>
		/// DynamicUMADna:: Retrieve DNA by dnaTypeNameHash.
		/// </summary>
		/// <returns>The DNA (or null if not found).</returns>
		/// <param name="dnaTypeNameHash">dnaTypeNameHash.</param>
		public UMADnaBase GetDna(int dnaTypeNameHash)
		{
			return umaRecipe.GetDna(dnaTypeNameHash);
		}

		/// <summary>
		/// Retrieve DNA by type.
		/// </summary>
		/// <returns>The DNA (or null if not found).</returns>
		/// <param name="type">Type.</param>
		public UMADnaBase GetDna(Type type)
		{
			return umaRecipe.GetDna(type);
		}

		/// <summary>
		/// Retrieve DNA by type.
		/// </summary>
		/// <returns>The DNA (or null if not found).</returns>
		/// <typeparam name="T">The type od DNA requested.</typeparam>
		public T GetDna<T>()
			where T : UMADnaBase
		{
			return umaRecipe.GetDna<T>();
		}

		/// <summary>
		/// Marks portions of the UMAData as modified.
		/// </summary>
		/// <param name="dnaDirty">If set to <c>true</c> DNA has changed.</param>
		/// <param name="textureDirty">If set to <c>true</c> texture has changed.</param>
		/// <param name="meshDirty">If set to <c>true</c> mesh has changed.</param>
		public void Dirty(bool dnaDirty, bool textureDirty, bool meshDirty)
		{
			isShapeDirty |= dnaDirty;
			isTextureDirty |= textureDirty;
			isMeshDirty |= meshDirty;
			Dirty();
		}

		/// <summary>
		/// Sets the slot at a given index.
		/// </summary>
		/// <param name="index">Index.</param>
		/// <param name="slot">Slot.</param>
		public void SetSlot(int index, SlotData slot)
		{
			umaRecipe.SetSlot(index, slot);
		}

		/// <summary>
		/// Sets the entire slot array.
		/// </summary>
		/// <param name="slots">Slots.</param>
		public void SetSlots(SlotData[] slots)
		{
			umaRecipe.SetSlots(slots);
		}

		/// <summary>
		/// Gets a slot by index.
		/// </summary>
		/// <returns>The slot.</returns>
		/// <param name="index">Index.</param>
		public SlotData GetSlot(int index)
		{
			return umaRecipe.GetSlot(index);
		}

		/// <summary>
		/// Gets the number of slots.
		/// </summary>
		/// <returns>The slot array size.</returns>
		public int GetSlotArraySize()
		{
			return umaRecipe.GetSlotArraySize();
		}

		/// <summary>
		/// Gets the skeleton.
		/// </summary>
		/// <returns>The skeleton.</returns>
		public UMASkeleton GetSkeleton()
		{
			return skeleton;
		}

		/// <summary>
		/// Align skeleton to the TPose.
		/// </summary>
		public void GotoTPose()
		{
			if ((umaRecipe.raceData != null) && (umaRecipe.raceData.TPose != null))
			{
				var tpose = umaRecipe.raceData.TPose;
				tpose.DeSerialize();
				for (int i = 0; i < tpose.boneInfo.Length; i++)
				{
					var bone = tpose.boneInfo[i];
					var hash = UMAUtils.StringToHash(bone.name);
					if (!skeleton.HasBone(hash)) continue;
					skeleton.Set(hash, bone.position, bone.scale, bone.rotation);
				}
			}
		}

		public int[] GetAnimatedBones()
		{
			var res = new int[animatedBonesTable.Count];
			foreach (var entry in animatedBonesTable)
			{
				res[entry.Value] = entry.Key;
			}
			return res;
		}

		/// <summary>
		/// Calls character begun events on slots.
		/// </summary>
		public void FireCharacterBegunEvents()
		{
			foreach (var slotData in umaRecipe.slotDataList)
			{
				if (slotData != null && slotData.asset.CharacterBegun != null)
				{
					slotData.asset.CharacterBegun.Invoke(this);
				}
			}
		}

		/// <summary>
		/// Calls DNA applied events on slots.
		/// </summary>
		public void FireDNAAppliedEvents()
		{
			if (CharacterDnaUpdated != null)
			{
				CharacterDnaUpdated.Invoke(this);
			}
			
			foreach (var slotData in umaRecipe.slotDataList)
			{
				if (slotData != null && slotData.asset.DNAApplied != null)
				{
					slotData.asset.DNAApplied.Invoke(this);
				}
			}
		}

		/// <summary>
		/// Calls character completed events on slots.
		/// </summary>
		public void FireCharacterCompletedEvents()
		{
			foreach (var slotData in umaRecipe.slotDataList)
			{
				if (slotData != null && slotData.asset.CharacterCompleted != null)
				{
					slotData.asset.CharacterCompleted.Invoke(this);
				}
			}
		}

		/// <summary>
		/// Adds additional, non serialized, recipes.
		/// </summary>
		/// <param name="umaAdditionalRecipes">Additional recipes.</param>
		/// <param name="context">Context.</param>
		public void AddAdditionalRecipes(UMARecipeBase[] umaAdditionalRecipes, UMAContext context)
		{
			if (umaAdditionalRecipes != null)
			{
				foreach (var umaAdditionalRecipe in umaAdditionalRecipes)
				{
					UMARecipe cachedRecipe = umaAdditionalRecipe.GetCachedRecipe(context);
					umaRecipe.Merge(cachedRecipe, true);
				}
			}
		}

		#region BlendShape Support
        public class BlendShapeSettings
        {
            public bool ignoreBlendShapes; //default false
            public Dictionary<string,float> bakeBlendShapes;

            public BlendShapeSettings()
            {
                ignoreBlendShapes = false;
                bakeBlendShapes = new Dictionary<string, float>();
            }
        }

		//For future multiple renderer support
		public struct BlendShapeLocation
		{
			public int shapeIndex;
			public int rendererIndex;
		}

		/// <summary>
		/// Sets the blendshape by index and renderer.
		/// </summary>
		/// <param name="shapeIndex">Name of the blendshape.</param>
		/// <param name="weight">Weight(float) to set this blendshape to.</param>
		/// <param name="rIndex">index (default first) of the renderer this blendshape is on.</param>
		public void SetBlendShape(int shapeIndex, float weight, int rIndex = 0)
		{
			if (rIndex >= rendererCount) //for multi-renderer support
			{
				Debug.LogError ("SetBlendShape: This renderer doesn't exist!");
				return;
			}

			if (shapeIndex < 0) 
			{
				Debug.LogError ("SetBlendShape: Index is less than zero!");
				return;
			}

			if (shapeIndex >= renderers [rIndex].sharedMesh.blendShapeCount) //for multi-renderer support
			{
				Debug.LogError ("SetBlendShape: Index is greater than blendShapeCount!");
				return;
			}

			if (weight < 0.0f || weight > 1.0f)
				Debug.LogError ("SetBlendShape: Weight is out of range, clamping...");

			weight = Mathf.Clamp01 (weight);
			weight *= 100.0f; //Scale up to 1-100 for SetBlendShapeWeight.

			renderers [rIndex].SetBlendShapeWeight (shapeIndex, weight);//for multi-renderer support
		}

		/// <summary>
		/// Set the blendshape by it's name.
		/// </summary>
		/// <param name="name">Name of the blendshape.</param
		/// <param name="weight">Weight(float) to set this blendshape to.</param>
		public void SetBlendShape(string name, float weight)
		{
			BlendShapeLocation loc = GetBlendShapeIndex (name);
			if (loc.shapeIndex < 0)
				return;

			if (weight < 0.0f || weight > 1.0f)
				Debug.LogError ("SetBlendShape: Weight is out of range, clamping...");

			weight = Mathf.Clamp01 (weight);
			weight *= 100.0f; //Scale up to 1-100 for SetBlendShapeWeight.

			renderers [loc.rendererIndex].SetBlendShapeWeight (loc.shapeIndex, weight);//for multi-renderer support
		}
		/// <summary>
		/// Gets the first found index of the blendshape by name in the renderers
		/// </summary>
		/// <param name="name">Name of the blendshape.</param>
		public BlendShapeLocation GetBlendShapeIndex(string name)
		{
			BlendShapeLocation loc = new BlendShapeLocation ();
			loc.shapeIndex = -1;
			loc.rendererIndex = -1;

			for (int i = 0; i < rendererCount; i++) //for multi-renderer support
			{
				int index = renderers [i].sharedMesh.GetBlendShapeIndex (name);
				if (index >= 0) 
				{
					loc.shapeIndex = index;
					loc.rendererIndex = i;
					return loc;
				}
			}

			//Debug.LogError ("GetBlendShapeIndex: blendshape " + name + " not found!");
			return loc;
		}
		/// <summary>
		/// Gets the name of the blendshape by index and renderer
		/// </summary>
		/// <param name="shapeIndex">Index of the blendshape.</param>
		/// <param name="rendererIndex">Index of the renderer (default = 0).</param>
		public string GetBlendShapeName(int shapeIndex, int rendererIndex = 0)
		{
			if (shapeIndex < 0) 
			{
				Debug.LogError ("GetBlendShapeName: Index is less than zero!");
				return "";
			}
				
			if (rendererIndex >= rendererCount) //for multi-renderer support
			{
				Debug.LogError ("GetBlendShapeName: This renderer doesn't exist!");
				return "";
			}

			//for multi-renderer support
			if( shapeIndex < renderers [rendererIndex].sharedMesh.blendShapeCount )
				return renderers [rendererIndex].sharedMesh.GetBlendShapeName (shapeIndex);

			Debug.LogError ("GetBlendShapeName: no blendshape at index " + shapeIndex + "!");
			return "";
		}
			
		#endregion
	}
}
