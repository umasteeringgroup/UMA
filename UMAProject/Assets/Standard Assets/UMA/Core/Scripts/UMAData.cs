using UnityEngine;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;

namespace UMA
{
	/// <summary>
	/// UMA data holds the recipe for creating a character and skeleton and Unity references for a built character.
	/// </summary>
	public class UMAData : MonoBehaviour {	
		public SkinnedMeshRenderer myRenderer;
		
		[NonSerialized]
		public bool firstBake;
		
		public UMAGeneratorBase umaGenerator;
		
		[NonSerialized]
		public GeneratedMaterials generatedMaterials = new GeneratedMaterials();
		
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
		[NonSerialized]
		[Obsolete("UMAData._hasUpdatedBefore is obsolete", false)]
		public bool _hasUpdatedBefore = false;
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
		public UMADataEvent CharacterCreated;
		public UMADataEvent CharacterDestroyed;
		public UMADataEvent CharacterUpdated;
		
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
		
		void Awake () {
			firstBake = true;
			
			if(!umaGenerator){
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
			myRenderer = other.myRenderer;
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
			if (umaGenerator == null) {
				Debug.LogError("UMA data missing required generator!");
				valid = false;
			}
			
			if (umaRecipe == null) {
				Debug.LogError("UMA data missing required recipe!");
				valid = false;
			}
			else {
				valid = valid && umaRecipe.Validate();
			}
			
			#if UNITY_EDITOR
			if (!valid && UnityEditor.EditorApplication.isPlaying) UnityEditor.EditorApplication.isPaused = true;
			#endif
			
			return valid;
		}
		
		[System.Serializable]
		public class GeneratedMaterials
		{
			public List<GeneratedMaterial> materials = new List<GeneratedMaterial>();
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
		}
		
		[System.Serializable]
		public class MaterialFragment
		{
			public int size;
			public Texture[] baseTexture;
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
		
		
		[System.Serializable]
		public class textureData {
			public Texture[] textureList;
		}
		
		[System.Serializable]
		public class resultAtlasTexture {
			public Texture[] textureList;
		}
		
		/// <summary>
		/// The UMARecipe class contains the race, DNA, and color data required to build a UMA character.
		/// </summary>
		[System.Serializable]
		public class UMARecipe
		{
			public RaceData raceData;
			protected Dictionary<Type, UMADnaBase> umaDna = new Dictionary<Type, UMADnaBase>();
			protected Dictionary<Type, DnaConverterBehaviour.DNAConvertDelegate> umaDnaConverter = new Dictionary<Type, DnaConverterBehaviour.DNAConvertDelegate>();
			protected Dictionary<string, int> mergedSharedColors = new Dictionary<string, int>();
			public SlotData[] slotDataList;
			public int additionalSlotCount;
			public OverlayColorData[] sharedColors;
			
			public bool Validate() 
			{
				bool valid = true;
				if (raceData == null) {
					Debug.LogError("UMA recipe missing required race!");
					valid = false;
				}
				else {
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
				if ((raceData == null) || (slotDataList == null)) {
					return new UMADnaBase[0];
				}
				
				UMADnaBase[] allDNA = new UMADnaBase[umaDna.Values.Count];
				umaDna.Values.CopyTo(allDNA, 0);
				return allDNA;
			}
			
			/// <summary>
			/// Adds the DNA specified.
			/// </summary>
			/// <param name="dna">DNA.</param>
			public void AddDna(UMADnaBase dna)
			{
				umaDna.Add(dna.GetType(), dna);
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
				if(umaDna.TryGetValue(typeof(T), out dna))
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
			}
			
			/// <summary>
			/// Removes the specified DNA.
			/// </summary>
			/// <param name="type">Type.</param>
			public void RemoveDna(Type type)
			{
				umaDna.Remove(type);
			}
			
			/// <summary>
			/// Get DNA of specified type.
			/// </summary>
			/// <returns>The DNA (or null if not found).</returns>
			/// <param name="type">Type.</param>
			public UMADnaBase GetDna(Type type)
			{
				UMADnaBase dna;
				if(umaDna.TryGetValue(type, out dna))
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
					umaDna.Add(typeof(T), res);
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
				if (umaDna.TryGetValue(type, out dna))
				{
					return dna;
				}
				
				dna = type.GetConstructor(System.Type.EmptyTypes).Invoke(null) as UMADnaBase;
				umaDna.Add(type, dna);
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
			/// <param name="additional">If set to <c>true</c> slot will not be serialized.</param>
			public void MergeSlot(SlotData slot, bool additional)
			{
				if ((slot == null) || (slot.asset == null))
					return;
				
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
							OverlayData originalOverlay = originalSlot.GetEquivalentOverlay(overlay);
							if (originalOverlay != null)
							{
								//								originalOverlay.CopyColors(overlay);
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
						return;
					}
				}
				
				int insertIndex = slotDataList.Length;
				System.Array.Resize<SlotData>(ref slotDataList, slotDataList.Length + 1);
				if (additional)
				{
					additionalSlotCount += 1;
				}
				else
				{
					for (int i = 0; i < additionalSlotCount; i++)
					{
						slotDataList[insertIndex] = slotDataList[insertIndex -1];
						insertIndex--;
					}
				}
				
				SlotData slotCopy = slot.Copy();
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
					for(int j = i + 1; j < slotDataList.Length; j++)
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
			public void ApplyDNA(UMAData umaData)
			{
				EnsureAllDNAPresent();
				foreach (var dnaEntry in umaDna)
				{
					DnaConverterBehaviour.DNAConvertDelegate dnaConverter;
					if (umaDnaConverter.TryGetValue(dnaEntry.Key, out dnaConverter))
					{
						dnaConverter(umaData, umaData.GetSkeleton());
					}
					else
					{
						Debug.LogWarning("Cannot apply dna: " + dnaEntry.Key);
					}
				}
			}
			
			/// <summary>
			/// Ensures all DNA convertes from slot and race data are defined.
			/// </summary>
			public void EnsureAllDNAPresent()
			{
				if (raceData != null)
				{
					foreach (var converter in raceData.dnaConverterList)
					{
						var dnaType = converter.DNAType;
						if (!umaDna.ContainsKey(dnaType))
						{
							umaDna.Add(dnaType, dnaType.GetConstructor(System.Type.EmptyTypes).Invoke(null) as UMADnaBase);
						}
					}
				}
				foreach (var slotData in slotDataList)
				{
					if (slotData != null && slotData.asset.slotDNA != null)
					{
						var dnaType = slotData.asset.slotDNA.DNAType;
						if (!umaDna.ContainsKey(dnaType))
						{
							umaDna.Add(dnaType, dnaType.GetConstructor(System.Type.EmptyTypes).Invoke(null) as UMADnaBase);
						}
					}
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
						umaDnaConverter.Add(converter.DNAType, converter.ApplyDnaAction);
					}
				}
			}
			
			/// <summary>
			/// Adds a DNA converter.
			/// </summary>
			/// <param name="dnaConverter">DNA converter.</param>
			public void AddDNAUpdater(DnaConverterBehaviour dnaConverter)
			{
				if( dnaConverter == null ) return;
				if (!umaDnaConverter.ContainsKey(dnaConverter.DNAType))
				{
					umaDnaConverter.Add(dnaConverter.DNAType, dnaConverter.ApplyDnaAction);
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
				newRecipe.slotDataList = slotDataList;
				return newRecipe;
			}
			
			/// <summary>
			/// Combine additional recipe with current data.
			/// </summary>
			/// <param name="recipe">Recipe.</param>
			/// <param name="additional">If set to <c>true</c> recipe will not be serialized.</param>
			public void Merge(UMARecipe recipe, bool additional)
			{
				if (recipe == null)
					return;
				
				if ((recipe.raceData != null) && (recipe.raceData != raceData))
				{
					Debug.LogWarning("Merging recipe with conflicting race data: " + recipe.raceData.name);
				}
				
				foreach (var dnaEntry in recipe.umaDna)
				{
					var destDNA = GetOrCreateDna(dnaEntry.Key);
					destDNA.Values = dnaEntry.Value.Values;
				}
				
				mergedSharedColors.Clear();
				if (sharedColors == null)
					sharedColors = new OverlayColorData[0];
				if (recipe.sharedColors != null)
				{
					for (int i = 0; i < sharedColors.Length; i++)
					{
						if ((sharedColors[i] != null) && sharedColors[i].HasName())
							mergedSharedColors.Add(sharedColors[i].name, i);
					}
					
					for (int i = 0; i < recipe.sharedColors.Length; i++)
					{
						OverlayColorData sharedColor = recipe.sharedColors[i];
						if (sharedColor.HasName())
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
						MergeSlot(recipe.slotDataList[i], additional);
					}
				}
			}
		}
		
		
		[System.Serializable]
		public class BoneData {
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
			if(umaRoot != null)
			{
				CleanTextures();
				CleanMesh(true);
				CleanAvatar();
				Destroy(umaRoot);
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
				if (animator.avatar) GameObject.Destroy(animator.avatar);
				if (animator) GameObject.Destroy(animator);
			}
		}
		
		/// <summary>
		/// Destroy textures used to render mesh.
		/// </summary>
		public void CleanTextures()
		{
			for(int atlasIndex = 0; atlasIndex < generatedMaterials.materials.Count; atlasIndex++)
			{
				if(generatedMaterials.materials[atlasIndex] != null && generatedMaterials.materials[atlasIndex].resultingAtlasList != null)
				{
					for(int textureIndex = 0; textureIndex < generatedMaterials.materials[atlasIndex].resultingAtlasList.Length; textureIndex++)
					{
						if(generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex] != null)
						{
							Texture tempTexture = generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex];
							if(tempTexture is RenderTexture)
							{
								RenderTexture tempRenderTexture = tempTexture as RenderTexture;
								tempRenderTexture.Release();
								Destroy(tempRenderTexture);
								tempRenderTexture = null;
							}
							else
							{
								Destroy(tempTexture);
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
			if (myRenderer)
			{
				var mats = myRenderer.sharedMaterials;
				for (int i = 0; i < mats.Length; i++)
				{
					if (mats[i])
					{
						Destroy(myRenderer.sharedMaterials[i]);
					}
				}
				if (destroyRenderer)
				{
					Destroy(myRenderer.sharedMesh);
					Destroy(myRenderer);
				}
			}
		}
		
		public Texture[] backUpTextures(){
			List<Texture> textureList = new List<Texture>();
			
			for(int atlasIndex = 0; atlasIndex < generatedMaterials.materials.Count; atlasIndex++){
				if(generatedMaterials.materials[atlasIndex] != null && generatedMaterials.materials[atlasIndex].resultingAtlasList != null){
					for(int textureIndex = 0; textureIndex < generatedMaterials.materials[atlasIndex].resultingAtlasList.Length; textureIndex++){
						
						if(generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex] != null){
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
		/// Ensures that all required bone transforms exist.
		/// </summary>
		/// <param name="umaBones">UMA bones.</param>
		/// <param name="boneMap">Bone map.</param>
		[Obsolete("UMAData.EnsureBoneData has been depricated and will be removed later.", false)]
		public void EnsureBoneData(Transform[] umaBones, Dictionary<Transform, Transform> boneMap)
		{
		}
		
		/// <summary>
		/// Ensures that all required bone transforms exist.
		/// </summary>
		/// <param name="umaBones">UMA bones.</param>
		/// <param name="animBones">Animated bones.</param>
		/// <param name="boneMap">Bone map.</param>
		[Obsolete("UMAData.EnsureBoneData has been depricated and will be removed later.", false)]
		public void EnsureBoneData(Transform[] umaBones, Transform[] animBones, Dictionary<Transform, Transform> boneMap)
		{
		}

		[Obsolete("UMAData.ClearBoneData has been depricated and will be removed later.", false)]
		public void ClearBoneData()
		{
			skeleton = null;
		}

		[Obsolete("UMAData.ClearBoneData has been depricated and will be removed later.", false)]
		public void UpdateBoneData()
		{
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
			isShapeDirty   |= dnaDirty;
			isTextureDirty |= textureDirty;
			isAtlasDirty |= textureDirty;
			isMeshDirty    |= meshDirty;
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
		/// Align skeleton to the default pose.
		/// </summary>
		[Obsolete("UMAData.GotoOriginalPose has been depricated and will be removed later, please use the skeleton helper if you want to access the skeleton.", false)]
		public void GotoOriginalPose()
		{
		}
		
		/// <summary>
		/// Align skeleton to the race data TPose.
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
					var go = skeleton.GetBoneGameObject(hash);
					if (go == null) continue;
					skeleton.SetPosition(hash, bone.position);
					skeleton.SetRotation(hash, bone.rotation);
					skeleton.SetScale(hash, bone.scale);
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
	}
}
