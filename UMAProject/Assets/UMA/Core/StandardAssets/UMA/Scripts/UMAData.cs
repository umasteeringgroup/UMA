//#define TEST_INSERTFIX
using UnityEngine;
using System;
using System.Collections.Generic;
using static UMA.DNAInstanceCollection;
using UMA.CharacterSystem;

namespace UMA
{
    public class BlendShapeData
	{
		public float value;
		public bool isBaked;
	}

	public class BlendShapeSettings
	{
		public bool ignoreBlendShapes = false; //switch for the skinnedmeshcombiner to skip all blendshapes or not.
        public bool loadAllFrames = true;
        public bool loadNormals = true;
        public bool loadTangents = true;
		public bool forceBakedBlendShapeValue = false;
		public float forcedBakedBlendShapeValue = 0.5f;
		public HashSet<string> filteredBlendshapes = new HashSet<string>();
		public Dictionary<string, BlendShapeData> blendShapes = new Dictionary<string, BlendShapeData>();
	}

	public class UMASavedItem
    {
		public string ParentBoneName;
        public int ParentBoneNameHash;
		public Transform Object;
		public Quaternion rotation;
		public Vector3 position;
		public Vector3 scale;
		public bool replaceExisting = false;
        public UMASavedItem(string boneName, int hash, Transform obj, bool replaceExisting)
        {
            ParentBoneName = boneName;
            ParentBoneNameHash = hash;
            Object = obj;
            rotation = obj.localRotation;
            position = obj.localPosition;
            scale = obj.localScale;
            this.replaceExisting = replaceExisting;
        }
    }

    public struct SlotTracker
    {
        string SlotName;
        int VertexPosition;
        SkinnedMeshRenderer Renderer;
    }

	/// <summary>
	/// UMA data holds the recipe for creating a character and skeleton and Unity references for a built character.
	/// </summary>
	public class UMAData : MonoBehaviour
	{
		const string HolderObjectName = "UMA_MI_Holder";
		//TODO improve/cleanup the relationship between renderers and rendererAssets
		[SerializeField]
		private SkinnedMeshRenderer[] renderers;
		private UMARendererAsset[] rendererAssets = new UMARendererAsset[0];
		[NonSerialized]
		private Transform externalSkeletonRoot;
		[NonSerialized]
		private SkinnedMeshRenderer externalBaseRenderer;

		[Tooltip("The default renderer asset to use for this avatar. This lets you set parameters for the generated SkinnedMeshRenderer")]
		public UMARendererAsset defaultRendererAsset;
		public int RendererCount { get { return renderers == null ? 0 : renderers.Length; } }
		public bool HasExternalSkeletonRoot { get { return externalSkeletonRoot != null; } }

		//public List<SlotTracker> slotTrackers = new();
		private readonly List<UMASavedItem> savedItems = new();
		public string userInformation = "";

		// NEW DNA
		public DNAInstanceCollection dnaInstanceCollection
		{
			get
			{
				if (umaRecipe == null)
				{
					// How did we get here?
					umaRecipe = new UMARecipe();
				}
				return (umaRecipe.dnaInstanceCollection);
			}
			set
			{
				if (umaRecipe == null)
				{
					// How did we get here?
					umaRecipe = new UMARecipe();
				}
				umaRecipe.dnaInstanceCollection = value;
            }
        }
		public bool alwaysAdjustBounds;

#region MESH MODIFIERS
        // MeshModifers are used to modify the mesh during creation.
        // This array is built from the various recipes added during the build process.
        private readonly Dictionary<string,List<MeshModifier.Modifier>> meshModifiers = new Dictionary<string, List<MeshModifier.Modifier>>();
		private readonly Dictionary<string, List<MeshModifier.Modifier>> accumulatedModifiers = new Dictionary<string, List<MeshModifier.Modifier>>();

        // This array is not built from the recipes. It must be set manually. It is merged into the dictionary of MeshModifiers with the recipe driven modifiers.
        // It's general use case is for adding mesh modifiers that are not part of the normal UMA build process, such as during editing, etc.

        public Dictionary<string, List<MeshModifier.Modifier>> Modifiers
        {
            get
            {
                return meshModifiers;
            }
        }

#if UNITY_EDITOR

        private List<MeshModifier.Modifier> _manualMeshModifiers = new List<MeshModifier.Modifier>();
		public List<MeshModifier.Modifier> ManualMeshModifiers 
		{ 
			get 
			{ 
				return _manualMeshModifiers; 
			}
            set
            {
                _manualMeshModifiers = value;
            }
        }
#endif


        public void ClearModifiers()
        {
            meshModifiers.Clear();
			accumulatedModifiers.Clear();
        }

		public void AddMeshModifier(MeshModifier.Modifier modifier)
		{
			if (modifier == null || string.IsNullOrEmpty(modifier.SlotName))
			{
				return;
            }
          string slotKey = modifier.SlotName;
			if (!meshModifiers.ContainsKey(slotKey))
            {
                meshModifiers.Add(slotKey, new List<MeshModifier.Modifier>());
            }
            meshModifiers[slotKey].Add(modifier);
        }

		public void AddMeshModifiers(UMATextRecipe recipe)
		{
			if (recipe == null || recipe.MeshModifiers == null || recipe.MeshModifiers.Count == 0)
			{
				return;
            }

			foreach (var modifier in recipe.MeshModifiers)
			{
				if (modifier != null)
				{
					AddMeshModifiers(modifier.RuntimeModifiers);
				}
            }
        }

        public void AddMeshModifiers(List<MeshModifier.Modifier> modifiers)
        {
			if (modifiers == null)
			{
				return;
			}
            foreach (MeshModifier.Modifier modifier in modifiers)
            {
                AddMeshModifier(modifier);
            }
        }

        public void BuildActiveModifiers()
		{
			if (umaRecipe == null)
			{
				return;
			}
			accumulatedModifiers.Clear();
            // add all the existing meshModifiers to the accumulatedModifiers
            foreach (var kvp in meshModifiers)
            {
                if (!accumulatedModifiers.ContainsKey(kvp.Key))
                {
                    accumulatedModifiers.Add(kvp.Key, new List<MeshModifier.Modifier>());
                }
				List<MeshModifier.Modifier> mods = kvp.Value;
				//Debug.Log($"There are {mods.Count} modifiers for slot {kvp.Key}");	
                if (mods.Count > 0)
				{
					var modifier = mods[0];
					var adj = modifier.adjustments;
					var va = modifier.adjustments.vertexAdjustments;
					//Debug.Log($"Adding {va.Count} vertex adjustments for slot {kvp.Key}");	
                }
				
                accumulatedModifiers[kvp.Key].AddRange(kvp.Value);
            }
#if UNITY_EDITOR
            foreach (var m in _manualMeshModifiers)
            {
                if (m == null || string.IsNullOrEmpty(m.SlotName))
				{
					continue;
				}

				if (!accumulatedModifiers.ContainsKey(m.SlotName))
                {
                    accumulatedModifiers.Add(m.SlotName, new List<MeshModifier.Modifier>());
                }
				//Debug.Log($"Adding manual modifier {m.ModifierName} for slot {m.SlotName}. There are currently {accumulatedModifiers[m.SlotName].Count} modifiers.");
				accumulatedModifiers[m.SlotName].Add(m);
				//Debug.Log($"After adding {m.ModifierName}, there are now {accumulatedModifiers[m.SlotName].Count} modifiers for slot {m.SlotName}.");
            }
#endif

            // This function expects the umaRecipe to be set.
            // and for the meshModifiers from the wardrobe recipes to be set in the meshModifiers list.
            for (int i = 0; i < umaRecipe.slotDataList.Length; i++)
			{
				var slot = umaRecipe.slotDataList[i];
				if (slot != null && slot.meshModifiers != null)
				{
				slot.meshModifiers.Clear();
				if (accumulatedModifiers.ContainsKey(slot.asset.sourceSlot))
				{
					var modifiers = accumulatedModifiers[slot.asset.sourceSlot];
					//Debug.Log($"Adding {modifiers.Count} modifiers to slot {slot.asset.slotName}");
                        slot.meshModifiers.AddRange(modifiers);
                }
            }
			
        }
        }
        #endregion

        #region SAVE RESTORE ITEMS
        // Replace SaveMountedItems with this non-alloc version
        public void SaveMountedItems()
        {
            GameObject holder = null;
            string ignoreTag = UMASettings.GetValidatedIgnoreTag(gameObject);

            // Find or create holder using for loops (no foreach alloc)
            var rootTf = gameObject.transform;
            for (int i = 0; i < rootTf.childCount; i++)
            {
                var child = rootTf.GetChild(i);
                if (child.name == HolderObjectName)
                {
                    holder = child.gameObject;
                    break;
                }
            }

            if (holder == null)
            {
                holder = new GameObject(HolderObjectName);
                if (!string.IsNullOrEmpty(ignoreTag))
                {
                    holder.tag = ignoreTag;
                }
                holder.SetActive(false);
                holder.transform.parent = rootTf;
            }

            if (umaRoot == null) return;

            // Use iterative traversal to avoid recursion + per-node allocations
            string kpTag = ValidateTagForTraversal(
                umaRoot.transform,
                umaGenerator != null ? umaGenerator.keepTag : null,
                "keep");
            SaveBonesIterative(umaRoot.transform, holder.transform, ignoreTag, kpTag);
        }

        // Add this new helper inside class UMAData; it supersedes SaveBonesRecursively at call sites
        private void SaveBonesIterative(Transform root, Transform holder, string ignoreTag, string keepTag)
        {
            // Pre-size the stack modestly to avoid reallocations on typical rigs
            var stack = new Stack<Transform>(128);
            stack.Push(root);

            while (stack.Count > 0)
            {
                var bone = stack.Pop();

                bool isIgnored = !string.IsNullOrEmpty(ignoreTag) && bone.CompareTag(ignoreTag);
                bool keep = !string.IsNullOrEmpty(keepTag) && bone.CompareTag(keepTag);

                if (isIgnored || keep)
                {
                    if (bone.parent != null)
                    {
                        // Cache parent identity before reparenting
                        var parent = bone.parent;
                        savedItems.Add(new UMASavedItem(parent.name, UMAUtils.StringToHash(parent.name), bone, keep));
                        bone.SetParent(holder, false);
                    }
                    // Do not traverse children; reparenting moves the whole subtree
                    continue;
                }

                // Push children (reverse order keeps original traversal order roughly similar)
                for (int i = bone.childCount - 1; i >= 0; i--)
                {
                    stack.Push(bone.GetChild(i));
                }
            }
        }
        public void SaveBonesRecursively(Transform bone, Transform holder, string ignoreTag, string keepTag)
        {
            ignoreTag = ValidateTagForTraversal(bone, ignoreTag, "ignore");
            keepTag = ValidateTagForTraversal(bone, keepTag, "keep");
            SaveBonesRecursivelyInternal(bone, holder, ignoreTag, keepTag);
        }

        private void SaveBonesRecursivelyInternal(Transform bone, Transform holder, string ignoreTag, string keepTag)
        {
            List<Transform> childlist = new List<Transform>();

            bool isIgnored = !string.IsNullOrEmpty(ignoreTag) && bone.CompareTag(ignoreTag);
            bool keep = !string.IsNullOrEmpty(keepTag) && bone.CompareTag(keepTag);
            if (isIgnored || keep)
            {
                if (bone.parent != null)
                {
                    AddSavedItem(bone, keep);
                    bone.SetParent(holder, false);
                }
            }
            else
            {
                foreach (Transform child in bone)
                {
                    childlist.Add(child);
                }


                for (int i = 0; i < childlist.Count; i++)
                {
                    Transform child = childlist[i];
                    SaveBonesRecursivelyInternal(child, holder, ignoreTag, keepTag);
                }
            }
        }

        private static string ValidateTagForTraversal(Transform probe, string tag, string purpose)
        {
            if (probe == null || string.IsNullOrWhiteSpace(tag) || string.Equals(tag, "Untagged", StringComparison.Ordinal))
            {
                return null;
            }

            tag = tag.Trim();
            try
            {
                probe.CompareTag(tag);
                return tag;
            }
            catch (UnityException exception)
            {
                Debug.LogError(
                    $"[UMA] The configured {purpose} tag '{tag}' is not defined in Unity's Tags and Layers settings. " +
                    $"That tag has been disabled for this traversal. {exception.Message}",
                    probe);
                return null;
            }
        }
        public void AddSavedItem(Transform transform, bool replace)
		{
			savedItems.Add(new UMASavedItem(transform.parent.name,UMAUtils.StringToHash(transform.parent.name), transform, replace));
		}

		public void RestoreSavedItems()
		{
			for (int i = 0; i < savedItems.Count; i++)
			{
				UMASavedItem usi = savedItems[i];
				Transform parent = skeleton.GetBoneTransform(usi.ParentBoneNameHash);
				if (usi.replaceExisting)
				{
					var newBone = skeleton.GetBoneTransform(usi.Object.name);
					if (newBone.gameObject.GetEntityId() != usi.Object.gameObject.GetEntityId())
					{
                        skeleton.ReplaceBone(usi);
						DestroyImmediate(newBone.gameObject);
                    }
				}
				if (parent != null)
				{
					usi.Object.SetParent(parent, false);
				}
				else
				{
					usi.Object.SetParent(umaRoot.transform, false);
				}
			}
			savedItems.Clear();
		}
        #endregion

        #region RENDERER AND RENDERER ASSETS
        //TODO Change these get functions to getter properties?
        public SkinnedMeshRenderer GetRenderer(int idx)
		{
			if (renderers != null && idx < renderers.Length)
			{
				return renderers[idx];
			}

			return null;
		}

		public int GetRendererIndex(SkinnedMeshRenderer renderer)
		{
			for (int i = 0; i < renderers.Length; i++)
			{
				if (renderer == renderers[i])
				{
					return i;
				}
			}

			return -1;
		}

		public UMARendererAsset GetRendererAsset(int idx)
		{
			if (idx >= rendererAssets.Length)
			{
				return null;
			}
			return rendererAssets[idx];
		}

		public SkinnedMeshRenderer[] GetRenderers()
		{
			return renderers;
		}

		public UMARendererAsset[] GetRendererAssets()
		{
			return rendererAssets;
		}

		public void SetRenderers(SkinnedMeshRenderer[] renderers)
		{
			this.renderers = renderers;
		}

		public void SetRendererAssets(UMARendererAsset[] assets)
		{
			rendererAssets = assets;
		}

		public bool AreRenderersEqual(List<UMARendererAsset> rendererList)
		{
			if (renderers == null || rendererList == null)
			{
				return false;
			}
			if (renderers.Length != rendererList.Count)
			{
				return false;
			}

			if (rendererAssets == null)
			{
				return false;
			}
			for (int i = 0; i < rendererAssets.Length; i++)
			{
				if (rendererAssets[i] != rendererList[i])
				{
					return false;
				}
			}
			return true;
		}

		public void ResetRendererSettings(int idx)
		{
			if (idx < 0 || idx >= renderers.Length)
			{
				return;
			}

			UMARendererAsset.ResetRenderer(renderers[idx]);
		}

		public void SetExternalSkeletonRoot(Transform root, SkinnedMeshRenderer baseRenderer)
		{
			externalSkeletonRoot = root;
			externalBaseRenderer = baseRenderer;
		}

		public void ClearExternalSkeletonRoot(SkinnedMeshRenderer owner = null)
		{
			if (owner != null && externalBaseRenderer != null && owner != externalBaseRenderer)
			{
				return;
			}

			externalSkeletonRoot = null;
			externalBaseRenderer = null;
			skeleton = null;
		}
        #endregion

        [NonSerialized]
		public bool staticCharacter = false;

		[NonSerialized]
		public bool firstBake;

		[NonSerialized]
		public bool RebuildSkeletonThisBuild;

        [Tooltip("If checked, will not animate or modify the vertexes")]
        public bool rawAvatar;

		[Tooltip("If checked, will disable animation on the UMA")]
        public bool disableAnimation;

		public bool raceChanged;

		public bool hideRenderers;

		public int currentLODLevel = 0;
        public UMAGenerator umaGenerator
        {
            get
            {
				UMAAssetIndexer instance = UMAAssetIndexer.Instance;
				if (instance == null)
				{
					Debug.LogError("AssetIndexer instance is NULL!!!!");
					return null;
				}
				UMAGenerator umaGenerator = instance.Generator;
				if (umaGenerator == null)
				{
#if UNITY_EDITOR //VES added so the error doesn't spam in prefab mode
					if(gameObject != null && UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null) {
						return null;
					}
#endif
					Debug.LogError("AssetIndexer generator instance is NULL!!!!");
				}
				return umaGenerator;
            }
        }

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


#region OVERRIDES
        [SerializeField]
		public UmaTPose OverrideTpose = null;

		// key: OverlayName, Channel
		public Dictionary<string, Dictionary<int, Texture>> TextureOverrides = new Dictionary<string, Dictionary<int, Texture>>();
		public Dictionary<string, Vector3[]> VertexOverrides = new Dictionary<string, Vector3[]>();
		public Dictionary<string, Vector2[]> UVOverrides = new Dictionary<string, Vector2[]>();

		public void ClearOverrides()
		{
			OverrideTpose = null;
			UVOverrides = new Dictionary<string, Vector2[]>();
			VertexOverrides = new Dictionary<string, Vector3[]>();

			if (TextureOverrides != null)
			{
				foreach (var kp in TextureOverrides.Values)
				{
					foreach (var tex in kp.Values)
					{
						if (tex != null)
						{
							UnityEngine.Object.DestroyImmediate(tex, false);
						}
					}
				}
			}
			TextureOverrides = new Dictionary<string, Dictionary<int, Texture>>();
		}

		public void AddOverrideTPose(UmaTPose thePose)
		{
			OverrideTpose = thePose;
		}

		public void AddUVOverride(SlotDataAsset theSlot, Vector2[] theUV)
		{
			if (UVOverrides.ContainsKey(theSlot.slotName))
			{
				UVOverrides[theSlot.slotName] = theUV;
			}
			else
			{
				UVOverrides.Add(theSlot.slotName, theUV);
			}
		}

		public void AddVertexOverride(SlotDataAsset theSlot, Vector3[] theVerts)
		{
			if (VertexOverrides.ContainsKey(theSlot.slotName))
			{
				VertexOverrides[theSlot.slotName] = theVerts;
			}
			else
			{
				VertexOverrides.Add(theSlot.slotName, theVerts);
			}
		}

		public void RemoveVertexOverride(SlotDataAsset theSlot)
		{
			if (VertexOverrides.ContainsKey(theSlot.slotName))
			{
				VertexOverrides.Remove(theSlot.slotName);
			}
		}

		public void AddTextureOverride(string OverlayName, int Channel, Texture2D theTexture)
		{
			// string theKey = OverlayOverideKey(OverlayName, Channel);
			if (!TextureOverrides.ContainsKey(OverlayName))
			{
				TextureOverrides.Add(OverlayName, new Dictionary<int, Texture>());
			}

			Dictionary<int, Texture> ChannelDictionary = TextureOverrides[OverlayName];
			if (ChannelDictionary.ContainsKey(Channel))
			{
				Texture tex = ChannelDictionary[Channel];
				UnityEngine.Object.DestroyImmediate(tex, false);
				ChannelDictionary.Remove(Channel);
			}
			ChannelDictionary.Add(Channel, theTexture);
		}

		/// <summary>
		/// Remove the texture override for the given overlay and channel.
		/// If you need the texture destroyed, do it yourself. It will NOT be destroyed
		/// automatically.
		/// </summary>
		/// <param name="OverlayName"></param>
		/// <param name="Channel"></param>
		public void RemoveTextureOverride(string OverlayName, int Channel)
		{
			if (TextureOverrides.ContainsKey(OverlayName))
			{
				Dictionary<int, Texture> ChannelDictionary = TextureOverrides[OverlayName];
				if (ChannelDictionary != null && ChannelDictionary.ContainsKey(Channel))
				{
					// DO NOT destroy the texture here!!!
					Texture tex = ChannelDictionary[Channel];
					ChannelDictionary.Remove(Channel);
				}
			}
		}

		public bool hasOverrides()
		{
			return TextureOverrides.Count > 0;
		}

		public void LogOverrides()
		{
			foreach (var t in TextureOverrides)
			{
				Debug.Log(t.Key + " " + t.Value[0].name);
			}
		}

		public Dictionary<int, Texture> GetTextureOverrides(string OverlayName)
		{
			if (TextureOverrides.ContainsKey(OverlayName))
			{
				return TextureOverrides[OverlayName];
			}

			return null;
		}
        #endregion

        public float atlasResolutionScale = 1f;

		public bool ForceRebindAnimator;
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
		/// <summary>
		/// Indicates whether the material needs to be cleared before rendering.
		/// </summary>
		public bool needsMaterialClear;
        /// <summary>
        /// Can the mesh be read after creation?
        /// </summary>
        [Tooltip("When this is true, the meshcombiner will upload the data and the mesh will no longer be readable. Set this to false if you use a 3rd party asset that needs to read the mesh data.")]
        public bool markNotReadable = true;
        /// <summary>
        /// Should the mesh use dynamic buffers?
        /// </summary>
        [Tooltip("When this is true, the meshcombiner will mark the mesh as dynamic. This will slightly decrease build time and slightly increase rendering.")]
        public bool markDynamic = false;

		// This UMAData will require 32 bit indexes
		// this is calculated from the slots when the mesh generation starts.
		public bool force32bit = false;

		public Bounds originalMeshBounds { get; set; }

        public BlendShapeSettings blendShapeSettings = new BlendShapeSettings();

		public RuntimeAnimatorController animationController;

		private Dictionary<int, int> animatedBonesTable;

		public void CheckSkeletonSetup()
		{
			if (externalSkeletonRoot != null)
			{
				EnsureUmaRootAndGlobal();
				skeleton = new UMASkeleton(externalSkeletonRoot);
				return;
			}

			// A UMAImprovedSkeleton (built by UMABoneBakingMeshCombiner) only creates real
			// Transforms for "preserved" (animated) bones and destroys the rest in
			// EndSkeletonUpdate. A standard skinning combiner (Default/Jobified) needs a
			// real Transform for every bone, so a leftover UMAImprovedSkeleton from a
			// previous bone-baking build must never be reused here - always rebuild a
			// plain UMASkeleton in that case so missing bone Transforms get recreated.
			bool recoveringFromBoneBaking = skeleton is UMAImprovedSkeleton;

			if (skeleton != null && !recoveringFromBoneBaking)
			{
				if (skeleton.isValid())
                {
                    return;
                }
            }

			Transform globalTransform = EnsureUmaRootAndGlobal();
			skeleton = new UMASkeleton(globalTransform);

			if (recoveringFromBoneBaking)
			{
				// The improved skeleton retains only preserved Transforms. Re-pin the plain
				// skeleton to its authored TPose + DNA pose after recreating the missing
				// source bones so Default/Jobified can use the authored bind poses.
				ResetToTPoseAndApplyDNA();
			}
		}


		public DNABuildType NewDNAPreApply()
		{
			if (dnaInstanceCollection == null || dnaInstanceCollection.dnaInstances.Count == 0)
			{
				return DNABuildType.None;
			}

			DNABuildType updateFlags = DNABuildType.None;

			foreach (var dnainstance in dnaInstanceCollection.dnaInstances)
			{
				//dnaInstanceCollection.
				if (dnainstance != null && dnainstance.enabled)
				{
					var dna = dnaInstanceCollection.GetDNA(dnainstance.Name);
					if (dna != null)
					{
						updateFlags |= dna.PreApply(this, dnainstance.Value);
					}
				}
			}
			return updateFlags;
        }

        public DNABuildType NewDNAApply()
        {
            if (dnaInstanceCollection == null || dnaInstanceCollection.dnaInstances.Count == 0)
            {
                return DNABuildType.None;
            }

            DNABuildType updateFlags = DNABuildType.None;

            // Base bone poses must run after ResetAll but before the rest of the rig effects.
            foreach (var dnainstance in dnaInstanceCollection.dnaInstances)
            {
				if (dnainstance != null && dnainstance.enabled)
				{
					var dna = dnaInstanceCollection.GetDNA(dnainstance.Name);
					if (dna != null)
					{
						updateFlags |= dna.ApplyBaseBonePoseEffects(this, dnainstance.Value);
					}
				}
            }

            foreach (var dnainstance in dnaInstanceCollection.dnaInstances)
            {
				if (dnainstance != null && dnainstance.enabled)
				{
					var dna = dnaInstanceCollection.GetDNA(dnainstance.Name);
					if (dna != null)
					{
						updateFlags |= dna.ApplyNonBaseEffects(this, dnainstance.Value);
					}
				}
            }
            return updateFlags;
        }

		public void ResetToTPoseAndApplyDNA()
		{
			if (skeleton == null)
			{
				return;
			}

			skeleton.ResetAll();
			if (rawAvatar)
			{
				return;
			}

			GotoTPose();
			if (umaRecipe?.raceData != null && umaRecipe.raceData.useNewDNA)
			{
				NewDNAApply();
			}
			else
			{
				ApplyDNA();
			}
		}

        public DNABuildType NewDNAPostApply()
        {
            if (dnaInstanceCollection == null || dnaInstanceCollection.dnaInstances.Count == 0)
            {
                return DNABuildType.None;
            }

            DNABuildType updateFlags = DNABuildType.None;

            foreach (var dnainstance in dnaInstanceCollection.dnaInstances)
            {
				if (dnainstance != null && dnainstance.enabled)
				{
					var dna = dnaInstanceCollection.GetDNA(dnainstance.Name);
					if (dna != null)
					{
						updateFlags |= dna.PostApply(this, dnainstance.Value);
					}
				}
            }
            return updateFlags;
        }

		private Transform EnsureUmaRootAndGlobal()
		{
			Transform rootTransform = gameObject.transform.Find("Root");
			if (rootTransform)
			{
				umaRoot = rootTransform.gameObject;
			}
			else
			{
				GameObject newRoot = new GameObject("Root");
				//make root of the UMAAvatar respect the layer setting of the UMAAvatar so cameras can just target this layer
				newRoot.layer = gameObject.layer;
				newRoot.transform.parent = transform;
				newRoot.transform.localPosition = Vector3.zero;
				if (umaRecipe.raceData.FixupRotations)
				{
					newRoot.transform.localRotation = Quaternion.Euler(270f, 0, 0f);
				}
				else
				{
					newRoot.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
				}
				newRoot.transform.localScale = Vector3.one;
				umaRoot = newRoot;
			}

			Transform globalTransform = umaRoot.transform.Find("Global");
			if (!globalTransform)
			{
				GameObject newGlobal = new GameObject("Global");
				newGlobal.transform.parent = umaRoot.transform;
				newGlobal.transform.localPosition = Vector3.zero;
				if (umaRecipe.raceData.FixupRotations)
				{
					newGlobal.transform.localRotation = Quaternion.Euler(90f, 90f, 0f);
				}
				else
				{
					newGlobal.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
				}
				globalTransform = newGlobal.transform;
			}

			return globalTransform;
		}

        public void SetupSkeleton()
		{
			Transform globalTransform = EnsureUmaRootAndGlobal();
			Transform skeletonRoot = externalSkeletonRoot != null ? externalSkeletonRoot : globalTransform;
			skeleton = new UMASkeleton(skeletonRoot);
		}

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

		/// <summary>
		/// Reapplies the preserved-bone set after a skeleton reset. Bone-baking
		/// combiners register this set while assembling the mesh; the generator's
		/// later shape pass resets the skeleton cache and must restore the set before
		/// the improved skeleton removes unpreserved transforms.
		/// </summary>
		public void RestoreRegisteredAnimatedBones()
		{
			if (skeleton == null || animatedBonesTable == null)
				return;

			foreach (int hash in animatedBonesTable.Keys)
				skeleton.SetAnimatedBone(hash);
		}

		public Transform GetGlobalTransform()
		{
			if (externalSkeletonRoot != null)
			{
				return externalSkeletonRoot;
			}

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
		public event Action<UMAData> OnCharacterBegun 
		{ 
			add 
			{ 
				if (CharacterBegun == null) 
				{ 
					CharacterBegun = new UMADataEvent(); 
				} 
				CharacterBegun.AddAction(value); 
			} 
			remove 
			{ 
				if (CharacterBegun == null)
                {
                    return;
                }
                CharacterBegun.RemoveAction(value); 
			} 
		}
		/// <summary>
		/// Callback event when character has been updated.
		/// </summary>
		public event Action<UMAData> OnCharacterUpdated 
		{ 
			add 
			{ 
				if (CharacterUpdated == null) 
				{ 
					CharacterUpdated = new UMADataEvent(); 
				} 
				CharacterUpdated.AddAction(value); 
			} 
			remove 
			{ 
				if (CharacterUpdated == null)
                {
					return;
                }
                CharacterUpdated.RemoveAction(value); 
			} 
		}
		/// <summary>
		/// Callback event when character has been completely created.
		/// </summary>
		public event Action<UMAData> OnCharacterCreated { add { if (CharacterCreated == null) { CharacterCreated = new UMADataEvent(); } CharacterCreated.AddAction(value); } remove { if (CharacterCreated == null) { return; } CharacterCreated.RemoveAction(value); } }
		/// <summary>
		/// Callback event when character has been destroyed.
		/// </summary>
		public event Action<UMAData> OnCharacterDestroyed { add { if (CharacterDestroyed == null) { CharacterDestroyed = new UMADataEvent(); } CharacterDestroyed.AddAction(value); } remove { if (CharacterDestroyed == null) { return; } CharacterDestroyed.RemoveAction(value); } }

		/// <summary>
		/// Callback event when character DNA has been updated.
		/// </summary>
		public event Action<UMAData> OnCharacterDnaUpdated { add { if (CharacterDnaUpdated == null) { CharacterDnaUpdated = new UMADataEvent(); } CharacterDnaUpdated.AddAction(value); } remove { if (CharacterDnaUpdated == null) { return; } CharacterDnaUpdated.RemoveAction(value); } }
		/// <summary>
		/// Callback event used by UMA to make last minute tweaks
		/// </summary>
		public event Action<UMAData> OnCharacterBeforeUpdated { add { if (CharacterBeforeUpdated == null) { CharacterBeforeUpdated = new UMADataEvent(); } CharacterBeforeUpdated.AddAction(value);} remove { if (CharacterBeforeUpdated == null) { return; } CharacterBeforeUpdated.RemoveAction(value); } }
		/// <summary>
		/// Callback event used by UMA to make last minute tweaks
		/// </summary>
		public event Action<UMAData> OnCharacterBeforeDnaUpdated { add { if (CharacterBeforeDnaUpdated == null) { CharacterBeforeDnaUpdated = new UMADataEvent(); } CharacterBeforeDnaUpdated.AddAction(value);} remove { if (CharacterBeforeDnaUpdated == null) { return; } CharacterBeforeDnaUpdated.RemoveAction(value); } }

		public event Action<UMAData> OnAnimatorStateSaved { add { if (AnimatorStateSaved == null) { AnimatorStateSaved = new UMADataEvent(); } AnimatorStateSaved.AddAction(value); } remove { if (AnimatorStateSaved == null) { return; } AnimatorStateSaved.RemoveAction(value); } }
		public event Action<UMAData> OnAnimatorStateRestored { add { if (AnimatorStateRestored == null) { AnimatorStateRestored = new UMADataEvent(); } AnimatorStateRestored.AddAction(value); } remove { if (AnimatorStateRestored == null) { return; } AnimatorStateRestored.RemoveAction(value); } }
		public event Action<UMAData> OnPreUpdateUMABody { add { if(PreUpdateUMABody == null) { PreUpdateUMABody = new UMADataEvent(); } PreUpdateUMABody.AddAction(value); } remove { if (PreUpdateUMABody == null) { return; } PreUpdateUMABody.RemoveAction(value); } } //VES added
		public event Action<UMAData, TextureEventParms> OnAtlasUpdated { add { if (AtlasUpdated == null) { AtlasUpdated = new UMATextureEvent(); } AtlasUpdated.AddAction(value); } remove { if (AtlasUpdated == null) { return; } AtlasUpdated.RemoveAction(value); } }

        public UMADataEvent CharacterCreated;
		public UMADataEvent CharacterDestroyed;
		public UMADataEvent CharacterUpdated;
		public UMADataEvent CharacterBeforeUpdated;
		public UMADataEvent CharacterBeforeDnaUpdated;
		public UMADataEvent CharacterDnaUpdated;
		public UMADataEvent CharacterBegun;
		public UMADataEvent AnimatorStateSaved;
		public UMADataEvent AnimatorStateRestored;
		public UMADataEvent PreUpdateUMABody;
		public UMATextureEvent AtlasUpdated;

		public GameObject umaRoot;

		
			[UnityEngine.Serialization.FormerlySerializedAs("umaRecipe")]
			public UMARecipe _umaRecipe;

			public UMARecipe umaRecipe
			{
				get
				{
					return umaOverrideRecipe != null ? umaOverrideRecipe : _umaRecipe;
				}
				set
				{
					_umaRecipe = value;
				}
			}

			/// <summary>
			/// This field is intended for LOD systems to override what actually gets built. 
			/// </summary>
			[NonSerialized]
			public UMARecipe umaOverrideRecipe;

			public Animator animator;
		public UMASkeleton skeleton;

		/// <summary>
		/// If true, will not reconstruct the avatar.
		/// </summary>
		public bool KeepAvatar;

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
			Initialize();
		}

		public void Initialize()
		{
			firstBake = true;

            if (_umaRecipe == null)
			{
				_umaRecipe = new UMARecipe();
			}
			else
			{
				SetupOnAwake();
			}
		}

		public void SetupOnAwake()
		{
			animator = gameObject.GetComponent<Animator>();
		}

#pragma warning disable 618
		/// <summary>
		/// Shallow copy from another UMAData.
		/// </summary>
		/// <param name="other">Source UMAData.</param>
		public void Assign(UMAData other)
		{
			animator = other.animator;
			renderers = other.renderers;
			rendererAssets = other.rendererAssets;
			umaRoot = other.umaRoot;
			if (animationController == null)
			{
				animationController = other.animationController;
			}
		}
#pragma warning restore 618

		public bool Validate()
		{
            if (Application.isBatchMode)
            {
                return true;
            }

            bool valid = true;

            if (defaultRendererAsset == null && umaGenerator != null)
            {
                defaultRendererAsset = umaGenerator.defaultRendererAsset;
            }

            if (_umaRecipe == null)
			{
				if (Debug.isDebugBuild)
                {
                    Debug.LogError("UMA data missing required recipe!");
                }

                valid = false;
			}
			else
			{
				valid = valid && umaRecipe.Validate();
			}

			return valid;
		}

		[System.Serializable]
		public class GeneratedMaterials
		{
			public List<GeneratedMaterial> materials = new List<GeneratedMaterial>();
			public List<UMARendererAsset> rendererAssets = new List<UMARendererAsset>();

			/// <summary>
			/// Gets the generated textures on the UMA matching umaMaterial and in the textureChannel.
			/// </summary>
			/// <param name="umaMaterial">Matching UMAMaterial to search for.</param>
			/// <param name="textureChannel">Texture channel in the UMAMaterial to find the texture on.</param>
			/// <returns></returns>
			public List<Texture> GetTextures(UMAMaterial umaMaterial, int textureChannel)
			{
				if (umaMaterial == null)
                {
                    return null;
                }

                if (textureChannel < 0 || textureChannel > umaMaterial.channels.Length)
                {
                    return null;
                }

                List<Texture> textures = new List<Texture>();

				for (int i = 0; i < materials.Count; i++)
                {
                    GeneratedMaterial generatedMaterial = materials[i];
                    if (generatedMaterial.umaMaterial.Equals(umaMaterial))
                    {
                        if (textureChannel < generatedMaterial.resultingAtlasList.Length)
                        {
                            textures.Add(generatedMaterial.resultingAtlasList[textureChannel]);
                        }
                    }
                }

				return textures;
			}

			/// <summary>
			/// Gets the Generated texture on the UMA matching the RendererAsset, Material, and textureChannel.
			/// </summary>
			/// <param name="rendererAsset"></param>
			/// <param name="material"></param>
			/// <param name="textureChannel"></param>
			/// <returns></returns>
			public Texture GetTexture(UMARendererAsset rendererAsset, Material material, int textureChannel)
			{
				for (int i = 0; i < materials.Count; i++)
                {
                    GeneratedMaterial generatedMaterial = materials[i];
                    if (rendererAsset == null && generatedMaterial.rendererAsset == null && generatedMaterial.material.Equals(material))
                    {
                        if (textureChannel < generatedMaterial.resultingAtlasList.Length)
                        {
                            return generatedMaterial.resultingAtlasList[textureChannel];
                        }
                    }
                    if (rendererAsset != null)
                    {
                        if (rendererAsset == generatedMaterial.rendererAsset && generatedMaterial.material.Equals(material))
                        {
                            if (textureChannel < generatedMaterial.resultingAtlasList.Length)
                            {
                                return generatedMaterial.resultingAtlasList[textureChannel];
                            }
                        }
                    }
                }
				return null;
			}
		}


		[System.Serializable]
		public class GeneratedMaterial
		{
			public UMAMaterial umaMaterial;
			public Material material;
			public Material secondPassMaterial;
			public List<MaterialFragment> materialFragments = new List<MaterialFragment>();
			public Texture[] resultingAtlasList;
			public Vector2 cropResolution;
			public Vector2 resolutionScale;
			public string[] textureNameList;
			public UMARendererAsset rendererAsset;
			public SkinnedMeshRenderer skinnedMeshRenderer;
			public int materialIndex;
		}

		[System.Serializable]
		public class MaterialFragment
		{
			public int size;
			public Color baseColor;
			public UMAMaterial umaMaterial;
			public Rect[] rects;
			public textureData[] AdditionalOverlays; // additional overlays to blend on top of the base overlay
            public Color32[] overlayColors;
			public Color[][] channelMask;
			public Color[][] channelAdditiveMask;
			public SlotData slotData;
			public OverlayData[] overlayData; 
			public Rect atlasRegion;
			public bool isRectShared;
			public bool isNoTextures;
			public List<OverlayData> overlayList; // all overlays on slot (base overlay + additional overlays)
            public MaterialFragment rectFragment;
			public textureData baseOverlay;       // the base overlay for this fragment
            public int baseVertexInMesh;
			public List<Dictionary<int, Texture>> overrides = new List<Dictionary<int,Texture>>();

			public Color GetMultiplier(int overlay, int textureType)
			{
				var c = Color.white;
				if (overlay >= overlayData.Length)
                {
                    return c;
                }

                if (channelMask[overlay] != null && channelMask[overlay].Length > 0)
				{
					if (textureType < channelMask[overlay].Length)
					{
						c = channelMask[overlay][textureType];
						c.r = Mathf.Clamp((c.r + overlayData[overlay].GetComponentAdjustmentsForChannel(c.r, textureType, 0)), 0, 1);
						c.g = Mathf.Clamp((c.g + overlayData[overlay].GetComponentAdjustmentsForChannel(c.g, textureType, 1)), 0, 1);
						c.b = Mathf.Clamp((c.b + overlayData[overlay].GetComponentAdjustmentsForChannel(c.b, textureType, 2)), 0, 1);
						c.a = Mathf.Clamp((c.a + overlayData[overlay].GetComponentAdjustmentsForChannel(c.a, textureType, 3)), 0, 1);
					}
					return c;
				}
				else
				{
					if (textureType > 0)
					{
						c.r = Mathf.Clamp((c.r + overlayData[overlay].GetComponentAdjustmentsForChannel(c.r, textureType, 0)), 0, 1);
						c.g = Mathf.Clamp((c.g + overlayData[overlay].GetComponentAdjustmentsForChannel(c.g, textureType, 1)), 0, 1);
						c.b = Mathf.Clamp((c.b + overlayData[overlay].GetComponentAdjustmentsForChannel(c.b, textureType, 2)), 0, 1);
						c.a = Mathf.Clamp((c.a + overlayData[overlay].GetComponentAdjustmentsForChannel(c.a, textureType, 3)), 0, 1);
						//return Color.white;
						return c;
					}
					if (overlay == 0)
					{
						c = baseColor;
						c.r = Mathf.Clamp((c.r + overlayData[overlay].GetComponentAdjustmentsForChannel(c.r, textureType, 0)), 0, 1);
						c.g = Mathf.Clamp((c.g + overlayData[overlay].GetComponentAdjustmentsForChannel(c.g, textureType, 1)), 0, 1);
						c.b = Mathf.Clamp((c.b + overlayData[overlay].GetComponentAdjustmentsForChannel(c.b, textureType, 2)), 0, 1);
						c.a = Mathf.Clamp((c.a + overlayData[overlay].GetComponentAdjustmentsForChannel(c.a, textureType, 3)), 0, 1);
						//return baseColor;
						return c;
					}
					c = overlayColors[overlay - 1];
					c.r = Mathf.Clamp((c.r + overlayData[overlay].GetComponentAdjustmentsForChannel(c.r, textureType, 0)), 0, 1);
					c.g = Mathf.Clamp((c.g + overlayData[overlay].GetComponentAdjustmentsForChannel(c.g, textureType, 1)), 0, 1);
					c.b = Mathf.Clamp((c.b + overlayData[overlay].GetComponentAdjustmentsForChannel(c.b, textureType, 2)), 0, 1);
					c.a = Mathf.Clamp((c.a + overlayData[overlay].GetComponentAdjustmentsForChannel(c.a, textureType, 3)), 0, 1);
					//return overlayColors[overlay - 1];
					return c;
				}
			}
			public Color32 GetAdditive(int overlay, int textureType)
			{
				if (channelAdditiveMask[overlay] != null && channelAdditiveMask[overlay].Length > 0 && channelAdditiveMask.Length >= overlay)
				{
					if (textureType < channelAdditiveMask[overlay].Length)
					{
						var c = channelAdditiveMask[overlay][textureType];
						c.r = Mathf.Clamp((c.r + overlayData[overlay].GetComponentAdjustmentsForChannel(c.r, textureType, 0, true)), 0, 1);
						c.g = Mathf.Clamp((c.g + overlayData[overlay].GetComponentAdjustmentsForChannel(c.g, textureType, 1, true)), 0, 1);
						c.b = Mathf.Clamp((c.b + overlayData[overlay].GetComponentAdjustmentsForChannel(c.b, textureType, 2, true)), 0, 1);
						c.a = Mathf.Clamp((c.a + overlayData[overlay].GetComponentAdjustmentsForChannel(c.a, textureType, 3, true)), 0, 1);
						return c;
					}
				}
				return new Color32(0, 0, 0, 0);
			}
		}

		public void Show()
		{
			if (hideRenderers)
			{
				Hide();
			}
			else
			{
				if (externalBaseRenderer != null)
				{
					externalBaseRenderer.enabled = true;
				}
				for (int i = 0; i < RendererCount; i++)
                {
                    GetRenderer(i).enabled = true;
                }
            }
		}

		public void Hide()
		{
			if (externalBaseRenderer != null)
			{
				externalBaseRenderer.enabled = false;
			}
			for (int i = 0; i < RendererCount; i++)
            {
				var renderer = GetRenderer(i);
				if (renderer == null)
				{
					Debug.Log($"Renderer {i} is null on GameObject {gameObject.name}");
					continue;
                }
                renderer.enabled = false;
            }
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
			public string recipeName; // only used when DynamicCharacterAvatar merges the recipe with the avatar.
			public DNAInstanceCollection dnaInstanceCollection;
            Dictionary<int, UMADnaBase> _umaDna;
			protected Dictionary<int, UMADnaBase> umaDna
			{
				get
				{
					if (_umaDna == null)
					{
						_umaDna = new Dictionary<int, UMADnaBase>();
						for (int i = 0; i < dnaValues.Count; i++)
                        {
                            _umaDna.Add(dnaValues[i].DNATypeHash, dnaValues[i]);
                        }
                    }
					return _umaDna;
				}
				set
				{
					_umaDna = value;
				}
			}

			protected Dictionary<int, List<DNAConvertDelegate>> umaDNAConverters = new Dictionary<int, List<DNAConvertDelegate>>();
			protected Dictionary<int, List<DNAConvertDelegate>> umaDNAPreApplyConverters = new Dictionary<int, List<DNAConvertDelegate>>();
			protected Dictionary<int, List<DNAConvertDelegate>> umaDNAPostApplyConverters = new Dictionary<int, List<DNAConvertDelegate>>();
			protected Dictionary<string, int> mergedSharedColors = new Dictionary<string, int>();
			[SerializeField]
			public List<UMADnaBase> dnaValues = new List<UMADnaBase>();
			public SlotData[] slotDataList;
			public OverlayColorData[] sharedColors;
			public Dictionary<string, List<MeshHideAsset>> MeshHideDictionary { get; set; } = new Dictionary<string, List<MeshHideAsset>>();
			public Dictionary<string, List<UMAMeshData>> BlendshapeSlots { get; set; } = new Dictionary<string, List<UMAMeshData>>();

			public void UpdateMeshHideMasks(int LODNumber)
			{
                for (int i = 0; i < slotDataList.Length; i++)
				{
                    SlotData sd = slotDataList[i];
                    if (!sd)
                    {
                        continue;
                    }

                    sd.meshHideMask = null;
					
					//Add MeshHideAsset here
                    if (MeshHideDictionary.ContainsKey(sd.slotName))
                    {   //If this slotDataAsset is found in the MeshHideDictionary then we need to supply the SlotData with the bitArray.
#if UNITY_6000_2_OR_NEWER
                        sd.meshHideMask = MeshHideAsset.GenerateMask(MeshHideDictionary[sd.slotName], Mathf.Max(0, LODNumber));
#else
                        sd.meshHideMask = MeshHideAsset.GenerateMask(MeshHideDictionary[sd.slotName]);
#endif

                        // TODO: We need to handle multiple LOD in Mesh Hide Assets
#if UNITY_6000_2_OR_NEWER
                        int triLen = sd.asset.meshData.submeshes[sd.asset.subMeshIndex].GetTriangles(Mathf.Max(0, LODNumber)).Length;
#else
                        int triLen = sd.asset.meshData.submeshes[sd.asset.subMeshIndex].GetTriangles().Length;
#endif
                        if (sd.meshHideMask != null && sd.meshHideMask.Length != triLen)
                        {
							var mha = MeshHideDictionary[sd.slotName];
                        }
                    }
				}
			}

			// Back-compat overload for older callers
			public void UpdateMeshHideMasks()
			{
				UpdateMeshHideMasks(0);
			}

			public OverlayData FindFirstOverlay(string name)
			{
                if (slotDataList == null || slotDataList.Length == 0)
                {
                    return null;
                }
                for (int i = 0; i < slotDataList.Length; i++)
                {
                    var slotData = slotDataList[i];
                    if (slotData != null)
                    {
						var overlayList = slotData.GetOverlayList();
                        for (int j = 0; j < overlayList.Count; j++)
                        {
                            var overlay = overlayList[j];
                            if (overlay != null && overlay.overlayName == name)
                            {
                                return overlay;
                            }
                        }
                    }
                }
                return null;
            }

			public bool Validate()
			{
				bool valid = true;
				if (raceData == null)
				{
					if (Debug.isDebugBuild)
                    {
                        Debug.LogError("UMA recipe missing required race!");
                    }

                    valid = false;
				}
				else
				{
					valid = valid && raceData.Validate();
				}

				bool allowEmptySlots = raceData != null && raceData.HasValidFbxRoute;
				if (slotDataList == null || slotDataList.Length == 0)
				{
					if (!allowEmptySlots)
                    {
						if (Debug.isDebugBuild)
                        {
                            Debug.LogError("UMA recipe slot list is empty!");
                        }

						valid = false;
					}

					return valid;
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
				if (slotDataCount < 1 && !allowEmptySlots)
				{
					if (Debug.isDebugBuild)
                    {
                        Debug.LogError("UMA recipe slot list contains only null objects!");
                    }

                    valid = false;
				}
				return valid;
			}

            /// <summary>
            /// Checks to see if the sharedColors array contains the passed color
            /// </summary>
            /// <param name="col"></param>
            /// <returns></returns>
            public bool HasSharedColor(OverlayColorData col)
            {
                for (int i = 0; i < sharedColors.Length; i++)
                {
                    OverlayColorData ocd = sharedColors[i];
                    if (ocd.Equals(col))
                    {
                        return true;
                    }
                }
                return false;
            }

            public void InitializeDNA()
            {
				if (raceData == null || !raceData.useNewDNA)
				{
					return;
				}

				var collection = raceData.DNACollection;
				if (collection == null)
				{
					collection = new DNACollection();
					raceData.DNACollection = collection;
				}

				collection.LoadDictionary();

				if (dnaInstanceCollection == null)
				{
					dnaInstanceCollection = new DNAInstanceCollection();
				}
				dnaInstanceCollection.Initialize(collection);

				if (dnaInstanceCollection.dnaInstances == null)
				{
					dnaInstanceCollection.dnaInstances = new List<DNAInstance>();
				}

				PruneDNAInstancesForRace(collection);

				var groups = collection.DNAGroups;
				if (groups == null || groups.Count == 0)
				{
					dnaInstanceCollection.ClearAll();
					return;
				}

				var dict = collection.dnaDictionary;
				Dictionary<string, float> overrides = null;
				try { overrides = raceData.GetDefaultDNA(); } catch { overrides = null; }
				var assignedDNA = dnaInstanceCollection.ToDictionary();

				foreach (var group in groups)
				{
					if (group != null && group.dnaList != null)
					{
						foreach (var d in group.dnaList)
						{
							if (d != null)
							{
								float defaultValue = d.defaultValue;
								if (overrides != null && overrides.TryGetValue(d.name, out var overrideValue))
								{									
									defaultValue = Mathf.Clamp01(overrideValue);
								}
								else if (dict != null && dict.TryGetValue(d.name, out var dnaAsset) && dnaAsset != null)
								{
									defaultValue = Mathf.Clamp01(dnaAsset.defaultValue);
								}
								if (assignedDNA.ContainsKey(d.name))
								{
									assignedDNA[d.name].Value = defaultValue;
									assignedDNA[d.name].parentGroup = group;
									continue;
								}

								var inst = new DNAInstance(d.name, defaultValue, group);
								dnaInstanceCollection.dnaInstances.Add(inst);
								assignedDNA.Add(d.name, inst);
							}
						}
					}
				}
            }

            // AddMissingDNAForRace: ensure all DNA from Race DNACollection exists, using overrides where provided
            public void AddMissingDNAForRace()
            {
                if (raceData == null || !raceData.useNewDNA)
                {
                    return;
                }

                var collection = raceData.DNACollection;
                if (collection == null)
                {
                    collection = new DNACollection();
                    raceData.DNACollection = collection;
                }
                collection.LoadDictionary();

                if (dnaInstanceCollection == null)
                {
                    dnaInstanceCollection = new DNAInstanceCollection();
                }
                dnaInstanceCollection.Initialize(collection);

				if (dnaInstanceCollection.dnaInstances == null)
				{
					dnaInstanceCollection.dnaInstances = new List<DNAInstance>();
				}

				PruneDNAInstancesForRace(collection);

                var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (dnaInstanceCollection.dnaInstances != null)
                {
                    for (int i = 0; i < dnaInstanceCollection.dnaInstances.Count; i++)
                    {
                        var inst = dnaInstanceCollection.dnaInstances[i];
                        if (inst != null && !string.IsNullOrEmpty(inst.Name))
                        {
                            assigned.Add(inst.Name);
                        }
                    }
                }

                Dictionary<string, float> overrides = null;
                try { overrides = raceData.GetDefaultDNA(); } catch { overrides = null; }

                // Iterate groups to preserve parentGroup association
                var groups = collection.DNAGroups;
                if (groups == null)
                {
                    return;
                }
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    var group = groups[gi];
                    if (group == null || group.dnaList == null) { continue; }
                    for (int di = 0; di < group.dnaList.Count; di++)
                    {
                        var dnaDef = group.dnaList[di];
                        if (dnaDef == null) { continue; }
                        var name = dnaDef.name;
                        if (string.IsNullOrEmpty(name) || assigned.Contains(name))
                        {
                            continue;
                        }
                        float value = dnaDef.defaultValue;
                        if (overrides != null && overrides.TryGetValue(name, out var ov))
                        {
                            value = Mathf.Clamp01(ov);
                        }
                        else
                        {
                            value = Mathf.Clamp01(dnaDef.defaultValue);
                        }
                        dnaInstanceCollection.AddDNAInstance(new DNAInstance(name, value, group));
                    }
                }
            }

			private void PruneDNAInstancesForRace(DNACollection collection)
			{
				if (dnaInstanceCollection == null || dnaInstanceCollection.dnaInstances == null || collection == null)
				{
					return;
				}

				var dict = collection.dnaDictionary;
				bool removedAny = false;

				for (int i = dnaInstanceCollection.dnaInstances.Count - 1; i >= 0; i--)
				{
					var inst = dnaInstanceCollection.dnaInstances[i];
					if (inst == null || string.IsNullOrEmpty(inst.Name) || dict == null || !dict.ContainsKey(inst.Name))
					{
						dnaInstanceCollection.dnaInstances.RemoveAt(i);
						removedAny = true;
					}
				}

				if (removedAny)
				{
					dnaInstanceCollection.dnaGroupInstances?.Clear();
					dnaInstanceCollection.dnaInstanceDictionary = null;
				}
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

            public UMADnaBase[] GetDefinedDna()
            {
                if (raceData != null && raceData.useNewDNA)
                {
                    AddMissingDNAForRace();
                    dnaValues = new List<UMADnaBase>
                    {
                        new UMADnaInstance(dnaInstanceCollection)
                    };
                }
                else
                {
                    if ((dnaValues == null) || dnaValues.Count == 0)
                    {
                        return new UMADnaBase[0];
                    }
                }
                return dnaValues.ToArray();
            }

			/// <summary>
			/// Adds the DNA specified.
			/// </summary>
			/// <param name="dna">DNA.</param>
			public void AddDna(UMADnaBase dna)
			{
				if (umaDna == null)
				{
					umaDna = new Dictionary<int, UMADnaBase>();
				}
				if (umaDna.ContainsKey(dna.DNATypeHash))
				{
					return;
                }
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
                if (raceData != null && raceData.useNewDNA)
                {
                    return null;
                }
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
				if (umaDna == null)
				{
					return null;
				}
                if (umaDna.TryGetValue(UMAUtils.StringToHash(type.Name), out var dna))
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
					res = UMADnaBase.CreateInstance<T>();
					if (res == null)
					{
						return null;
					}
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

				dna = UMADnaBase.CreateInstance(type);
				if (dna == null)
				{
					return null;
				}
				umaDna.Add(typeNameHash, dna);
				dnaValues.Add(dna);
				return dna;
			}
			/// <summary>
			/// Get DNA of specified type, adding if not found.
			/// </summary>
			/// <returns>The DNA.</returns>
			/// <param name="type">Type.</param>
			/// <param name="dnaTypeHash">The DNAType's hash."</param>
			public UMADnaBase GetOrCreateDna(Type type, int dnaTypeHash)
			{
				UMADnaBase dna;
				if (umaDna.TryGetValue(dnaTypeHash, out dna))
				{
					return dna;
				}

				dna = UMADnaBase.CreateInstance(type);
				if (dna == null)
				{
					return null;
				}
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

			public void RemoveSlot(SlotData sd)
            {
				if (sd == null)
                {
                    return;
                }

                for (int i = 0; i < slotDataList.Length; i++)
				{
					if (slotDataList[i] == null)
                    {
                        continue;
                    }

                    if (slotDataList[i].slotName == sd.slotName)
                    {
						slotDataList[i] = null;
                    }
				}
			}

			public SlotData FindSlot(string slotName)
			{
                // find the vertex in the slot
                for (int i = 0; i < slotDataList.Length; i++)
                {
                    var slot = slotDataList[i];
                    if ( slot.slotName == slotName)
                    {
						return slot;
                    }
                }
                return null;
            }

            public SlotData FindSlotForVertex(int vert)
			{
                // find the vertex in the slot
                for (int i = 0; i < slotDataList.Length; i++)
                {
                    var slot = slotDataList[i];
                    if (vert >= slot.vertexOffset)
                    {
                        int LocalToSlot = vert - slot.vertexOffset;
                        if (LocalToSlot < slot.asset.meshData.vertexCount)
                        {
							return slot;
                        }
                    }
                }
				return null;
            }

			/// <summary>
			/// Combine additional slot with current data.
			/// </summary>
			/// <param name="slot">Slot.</param>
			/// <param name="dontSerialize">If set to <c>true</c> slot will not be serialized.</param>
			public SlotData MergeSlot(SlotData slot, bool dontSerialize, bool mergeMatchingOverlays = true, string recipeName = "")
			{
				if ((slot == null) || (slot.asset == null && !slot.isPlaceholderSlot))
				{
					if (slot == null)
					{
						Debug.LogWarning($"MergeSlot called with null slot in recipe {recipeName}.");
					}
					else if (slot.asset == null)
					{
						Debug.LogWarning($"MergeSlot called with null slot asset in recipe {recipeName} Slot {slot.slotName}.");
					}
					else
					{
						Debug.LogWarning($"MergeSlot called with placeholder slot asset in recipe {recipeName} Slot {slot.slotName}. Placeholder slot ignored.");
					}
					return null;
				}

				//Debug.Log($"Merging slot {slot.slotName} into recipe {recipeName}.");

				int overlayCount = 0;
#if TEST_INSERTFIX
				int nullFound = -1;
#endif
				for (int i = 0; i < slotDataList.Length; i++)
				{
					if (slotDataList[i] == null)
					{
#if TEST_INSERTFIX
						if (nullFound == -1) nullFound = i;
#endif
						continue;
					}

					// Placeholder slots match by name, not by asset reference.
					if (slot.isPlaceholderSlot)
					{
						if (slotDataList[i].isPlaceholderSlot && slotDataList[i].placeholderSlotName == slot.placeholderSlotName)
						{
							SlotData originalSlot = slotDataList[i];
							overlayCount = slot.OverlayCount;
							for (int j = 0; j < overlayCount; j++)
							{
								OverlayData overlay = slot.GetOverlay(j);
								OverlayData overlayCopy = overlay.Duplicate();
								overlayCopy.mergedFromSlot = slot;
								overlayCopy.mergedFromRecipe = recipeName;
								originalSlot.AddOverlay(overlayCopy);
							}
							originalSlot.dontSerialize = dontSerialize;
							return originalSlot;
						}
						continue;
					}

					if (slot.asset == slotDataList[i].asset)
					{
						SlotData originalSlot = slotDataList[i];
						overlayCount = slot.OverlayCount;
						for (int j = 0; j < overlayCount; j++)
						{
							OverlayData overlay = slot.GetOverlay(j);
							//DynamicCharacterSystem:: Needs to use alternative methods that find equivalent overlays since they may not be Equal if they were in an assetBundle
							OverlayData originalOverlay = originalSlot.GetEquivalentUsedOverlay(overlay);
							if (originalOverlay != null && overlay.asset.dontMergeDuplicates != true)
							{
								originalOverlay.CopyColors(overlay);//also copies textures
								originalOverlay.Supressed = overlay.Supressed;
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
                                overlayCopy.mergedFromSlot = slot;
								overlayCopy.mergedFromRecipe = recipeName;
                                originalSlot.AddOverlay(overlayCopy);
							}
						}
						originalSlot.dontSerialize = dontSerialize;
						return originalSlot;
					}
				}

#if TEST_INSERTFIX
				int insertIndex;

				if (nullFound != -1)
				{
					insertIndex = nullFound;
				}
				else
                {
					insertIndex = slotDataList.Length;
					System.Array.Resize<SlotData>(ref slotDataList, slotDataList.Length + 1);
				}
#else
				int insertIndex = slotDataList.Length;
				System.Array.Resize<SlotData>(ref slotDataList, slotDataList.Length + 1);
#endif
				SlotData slotCopy = slot.Copy();
				slotCopy.dontSerialize = dontSerialize;
				overlayCount = slotCopy.OverlayCount;
				for (int j = 0; j < overlayCount; j++)
				{
					OverlayData overlay = slotCopy.GetOverlay(j);
					overlay.mergedFromRecipe = recipeName;
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
				if (mergeMatchingOverlays)
				{
					MergeMatchingOverlays();
				}
                return slotCopy;
			}

			/// <summary>
			/// Gets a slot by index.
			/// </summary>
			/// <returns>The slot.</returns>
			/// <param name="index">Index.</param>
			public SlotData GetSlotByIndex(int index)
			{
				if (index < slotDataList.Length)
                {
                    return slotDataList[index];
                }

                return null;
			}


			public SlotData GetSlotByHash(int nameHash)
			{
				for (int i = 0; i < slotDataList.Length; i++)
				{
					if (slotDataList[i] == null)
					{
						continue;
					}
					if (slotDataList[i].asset.nameHash == nameHash)
					{
						return slotDataList[i];
					}
				}
				return null;
            }


			public SlotData GetSlotBySlotGroup(string slotGroup)
			{
				for (int i = 0; i < slotDataList.Length; i++)
				{
					if (slotDataList[i] == null)
					{
						continue;
					}
					if (slotDataList[i].asset != null && slotDataList[i].asset.slotGroup == slotGroup)
					{
						return slotDataList[i];
					}
				}
				return null;
            }

			public SlotData GetSlot(string name)
            {
				int hash = UMAUtils.StringToHash(name);
				return GetSlotByHash(hash);
            }

            /// <summary>
            /// Gets the first slot in the slotdatalist that is not null
            /// </summary>
            /// <returns></returns>
            public SlotData GetFirstSlot()
			{
				if (slotDataList == null)
				{
					return null;
				}
				for(int i=0;i<slotDataList.Length;i++)
				{
					if (slotDataList[i] != null)
					{
						return slotDataList[i];
					}
				}
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
			/// Returns an Dictionary of slots, indexed by slotName
			/// Use this when you need to make multiple passes to find slots
			/// </summary>
			/// <returns></returns>
			public Dictionary<string,SlotData> GetIndexedSlots()
			{
				Dictionary<string,SlotData> indexedSlots = new Dictionary<string,SlotData>();
                for (int i = 0; i < slotDataList.Length; i++)
				{
                    SlotData slotData = slotDataList[i];
                    if (slotData != null)
					{
						indexedSlots.Add(slotData.slotName, slotData);
					}
				}
				return indexedSlots;
			}



            public Dictionary<string, SlotData> GetFirstIndexedSlotsByTag()
            {
                Dictionary<string, SlotData> indexedSlots = new Dictionary<string, SlotData>();
                foreach (SlotData slotData in slotDataList)
                {
                    if (slotData != null)
                    {
						foreach (string t in slotData.tags)
						{
							if (!string.IsNullOrEmpty(t))
							{
								if (!indexedSlots.ContainsKey(t))
								{
									indexedSlots.Add(t, slotData);
								}
                    }
                }
                    }
                }
                return indexedSlots;
			}

            public Dictionary<string, List<SlotData>> GetIndexedSlotsByTag()
            {
                Dictionary<string, List<SlotData>> indexedSlots = new Dictionary<string, List<SlotData>>();
                foreach (SlotData slotData in slotDataList)
                {
                    if (slotData != null)
                    {
                        foreach (string t in slotData.tags)
                        {
                            if (!string.IsNullOrEmpty(t))
                            {
                                if (!indexedSlots.ContainsKey(t))
                                {
                                    indexedSlots.Add(t, new List<SlotData>());
                                }
                                indexedSlots[t].Add(slotData);
                            }
                        }
                    }
                }
                return indexedSlots;
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
                {
                    return false;
                }

                if ((list1.Count == 0) || (list1.Count != list2.Count))
                {
                    return false;
                }

                for (int i = 0; i < list1.Count; i++)
				{
					OverlayData overlay1 = list1[i];
					if (!(overlay1))
                    {
                        continue;
                    }

                    bool found = false;
					for (int j = 0; j < list2.Count; j++)
					{
						OverlayData overlay2 = list2[i];
						if (!(overlay2))
                        {
                            continue;
                        }

                        if (OverlayData.Equivalent(overlay1, overlay2))
						{
							found = true;
							break;
						}
					}
					if (!found)
                    {
                        return false;
                    }
                }

				return true;
			}

			/// <summary>
			/// Clears any currently applied ColorAdjusters on all overlays
			/// </summary>
			public void ClearOverlayColorAdjusters()
			{
				for (int i = 0; i < slotDataList.Length; i++)
				{
					if (slotDataList[i] == null)
                    {
                        continue;
                    }

                    List<OverlayData> slotOverlays = slotDataList[i].GetOverlayList();
					for(int oi= 0; oi < slotOverlays.Count; oi++)
					{
						slotOverlays[oi].colorComponentAdjusters.Clear();
					}
				}
			}

			/// <summary>
			/// Ensures slots with matching overlays will share the same references.
			/// </summary>
			public void MergeMatchingOverlays()
			{
				for (int i = 0; i < slotDataList.Length; i++)
				{
					if (slotDataList[i] == null)
                    {
                        continue;
                    }

                    List<OverlayData> slotOverlays = slotDataList[i].GetOverlayList();
					for (int j = i + 1; j < slotDataList.Length; j++)
					{
						if (slotDataList[j] == null)
                        {
                            continue;
                        }

                        List<OverlayData> slot2Overlays = slotDataList[j].GetOverlayList();
						if (OverlayListsMatch(slotOverlays, slot2Overlays))
						{
							slotDataList[j].SetOverlayList(slotOverlays);
						}
					}
				}
			}

#pragma warning disable 618
			public void PreApplyDNA(UMAData umaData, bool fixUpUMADnaToDynamicUMADna = false)
			{
				Debug.Log("PreApplyDNA called for recipe " + umaData.umaRecipe.recipeName);

                EnsureAllDNAPresent();
				//clear any color adjusters from all overlays in the recipe
				umaData.umaRecipe.ClearOverlayColorAdjusters();
				foreach (var dnaEntry in umaDna)
				{
					//DynamicDNAPlugins FEATURE: Allow more than one converter to use the same dna
					List<DNAConvertDelegate> dnaConverters;
					this.umaDNAPreApplyConverters.TryGetValue(dnaEntry.Key, out dnaConverters);
					//DynamicUMADna:: when loading an older recipe that has UMADnaHumanoid/Tutorial into a race that now uses DynamicUmaDna the following wont work
					//so check that and fix it if it happens
					if (dnaConverters != null && dnaConverters.Count > 0)
					{
						for (int i = 0; i < dnaConverters.Count; i++)
						{
							dnaConverters[i](umaData, umaData.GetSkeleton());
						}
					}
				}
			}


			/// <summary>
			/// Applies each DNA converter to the UMA data and skeleton.
			/// </summary>
			/// <param name="umaData">UMA data.</param>
			public void ApplyDNA(UMAData umaData)
			{
				Debug.Log("ApplyDNA called for recipe " + umaData.umaRecipe.recipeName);
				if (umaData.umaRecipe.raceData != null && umaData.umaRecipe.raceData.useNewDNA)
				{
					return;
				}
				foreach (var dnaEntry in umaDna)
				{
					//DynamicDNAPlugins FEATURE: Allow more than one converter to use the same dna
					List<DNAConvertDelegate> dnaConverters;
					umaDNAConverters.TryGetValue(dnaEntry.Key, out dnaConverters);
				
					if (dnaConverters != null && dnaConverters.Count > 0)
					{
						for (int i = 0; i < dnaConverters.Count; i++)
						{
							dnaConverters[i](umaData, umaData.GetSkeleton());
						}
					}
				}
			}

			/// <summary>
			/// Applies each DNA converter to the UMA data and skeleton.
			/// </summary>
			/// <param name="umaData">UMA data.</param>
			public void ApplyPostpassDNA(UMAData umaData)
			{
				foreach (var dnaEntry in umaDna)
				{
					//DynamicDNAPlugins FEATURE: Allow more than one converter to use the same dna
					List<DNAConvertDelegate> dnaConverters;
					this.umaDNAPostApplyConverters.TryGetValue(dnaEntry.Key, out dnaConverters);

					if (dnaConverters != null && dnaConverters.Count > 0)
					{
						for (int i = 0; i < dnaConverters.Count; i++)
						{
							dnaConverters[i](umaData, umaData.GetSkeleton());
						}
					}
#if UNITY_EDITOR
					else
					{
						if (Debug.isDebugBuild)
                        {
                            Debug.LogWarning("**UMA: Cannot apply dna: " + dnaEntry.Value.GetType().Name + " using key " + dnaEntry.Key);
                        }
                    }
#endif
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
                    for (int i = 0; i < raceData.dnaConverterList.Length; i++)
					{
                        DynamicDNAConverterController converter = raceData.dnaConverterList[i];
                        var dnaTypeHash = converter.DNATypeHash;
						//'old' dna converters return a typehash based on the type name. 
						//Dynamic DNA Converters return the typehash of their dna asset or 0 if none is assigned- we dont want to include those
						if (dnaTypeHash == 0)
                        {
                            continue;
                        }
                        //DynamicDNAPlugins FEATURE: Allow more than one converter to use the same dna
                        //check the hash isn't already in the list
                        if (!requiredDnas.Contains(dnaTypeHash))
                        {
                            requiredDnas.Add(dnaTypeHash);
                        }

						if (!umaDna.ContainsKey(dnaTypeHash))
						{
							var dna = UMADnaBase.CreateInstance(converter);
							if (dna == null)
							{
								continue;
							}
							dna.DNATypeHash = dnaTypeHash;
							umaDna.Add(dnaTypeHash, dna);
							dnaValues.Add(dna);
						}
						else if (converter is IDynamicDNAConverter)
						{
                            UMADnaBase dna = umaDna[dnaTypeHash];
							if (dna is DynamicUMADnaBase)
							{
								((DynamicUMADnaBase)dna).dnaAsset = ((IDynamicDNAConverter)converter).dnaAsset;
							}
							else
                            {
								// Debug.LogError("Invalid converter "+converter.name+" on race " + raceData.raceName);
                            }
						}
					}
				}
                for (int i = 0; i < slotDataList.Length; i++)
				{
                    SlotData slotData = slotDataList[i];
                    if (slotData != null && slotData.asset.slotDNA != null)
					{
						var dnaTypeHash = slotData.asset.slotDNA.DNATypeHash;
						//'old' dna converters return a typehash based on the type name. 
						//Dynamic DNA Converters return the typehash of their dna asset or 0 if none is assigned- we dont want to include those
						if (dnaTypeHash == 0)
                        {
                            continue;
                        }
                        //DynamicDNAPlugins FEATURE: Allow more than one converter to use the same dna
                        //check the hash isn't already in the list
                        if (!requiredDnas.Contains(dnaTypeHash))
                        {
                            requiredDnas.Add(dnaTypeHash);
                        }

						if (!umaDna.ContainsKey(dnaTypeHash))
						{
							var dna = UMADnaBase.CreateInstance(slotData.asset.slotDNA);
							if (dna == null)
							{
								continue;
							}
							dna.DNATypeHash = dnaTypeHash;
							umaDna.Add(dnaTypeHash, dna);
							dnaValues.Add(dna);
						}
						else if (slotData.asset.slotDNA is IDynamicDNAConverter)
						{
							var dna = umaDna[dnaTypeHash];
							((DynamicUMADnaBase)dna).dnaAsset = ((IDynamicDNAConverter)slotData.asset.slotDNA).dnaAsset;
						}
						//When dna is added from slots Prepare doesn't seem to get called for some reason
						slotData.asset.slotDNA.Prepare();
					}
				}
				foreach (int addedDNAHash in umaDNAConverters.Keys)
				{
					if(!requiredDnas.Contains(addedDNAHash))
                    {
                        requiredDnas.Add(addedDNAHash);
                    }
                }

				//now remove any we no longer need
				var keysToRemove = new List<int>();
				foreach(var kvp in umaDna)
				{
					if (!requiredDnas.Contains(kvp.Key))
                    {
                        keysToRemove.Add(kvp.Key);
                    }
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
				umaDNAConverters.Clear();
				umaDNAPreApplyConverters.Clear();
				umaDNAPostApplyConverters.Clear();
				if (raceData != null && raceData.useNewDNA == false) 
				{
                    for (int i = 0; i < raceData.dnaConverterList.Length; i++)
					{
                        DynamicDNAConverterController converter = raceData.dnaConverterList[i];
                        if (converter == null)
						{
#if UNITY_EDITOR
							if (Debug.isDebugBuild)
                            {
                                Debug.LogWarning("RaceData " + raceData.raceName + " has a missing DNAConverter");
                            }
#endif
							continue;
						}
						//'old' dna converters return a typehash based on the type name. 
						//Dynamic DNA Converters return the typehash of their dna asset or 0 if none is assigned- we dont want to include those
						if (converter.DNATypeHash == 0)
                        {
                            continue;
                        }

                        AddDNAUpdater(converter);
					}
				}
			}

			/// <summary>
			/// Adds a DNA converter.
			/// </summary>
			/// <param name="dnaConverter">DNA converter.</param>
			public void AddDNAUpdater(IDNAConverter dnaConverter)
			{
				if (dnaConverter == null)
                {
                    return;
                }
                //DynamicDNAConverter:: We need to SET these values using the TypeHash since 
                //just getting the hash of the DNAType will set the same value for all instance of a DynamicDNAConverter
                //DynamicDNAPlugins FEATURE: Allow more than one converter to use the same dna
                if (dnaConverter.PreApplyDnaAction != null)
				{
					if (!umaDNAPreApplyConverters.ContainsKey(dnaConverter.DNATypeHash)) 
                    {
                        umaDNAPreApplyConverters.Add(dnaConverter.DNATypeHash, new List<DNAConvertDelegate>());
                    }

                    if (!umaDNAPreApplyConverters[dnaConverter.DNATypeHash].Contains(dnaConverter.PreApplyDnaAction))
					{
						umaDNAPreApplyConverters[dnaConverter.DNATypeHash].Add(dnaConverter.PreApplyDnaAction);
					}
				}
				if (dnaConverter.PostApplyDnaAction != null)
				{
					if (!umaDNAPostApplyConverters.ContainsKey(dnaConverter.DNATypeHash))
                    {
                        umaDNAPostApplyConverters.Add(dnaConverter.DNATypeHash, new List<DNAConvertDelegate>());
                    }

                    if (!umaDNAPostApplyConverters[dnaConverter.DNATypeHash].Contains(dnaConverter.PostApplyDnaAction))
					{
						umaDNAPostApplyConverters[dnaConverter.DNATypeHash].Add(dnaConverter.PostApplyDnaAction);
					}
				}

				if (!umaDNAConverters.ContainsKey(dnaConverter.DNATypeHash))
                {
                    umaDNAConverters.Add(dnaConverter.DNATypeHash, new List<DNAConvertDelegate>());
                }

                if (!umaDNAConverters[dnaConverter.DNATypeHash].Contains(dnaConverter.ApplyDnaAction))
				{
					umaDNAConverters[dnaConverter.DNATypeHash].Add(dnaConverter.ApplyDnaAction);
				}
				else
                {
#if UNITY_EDITOR
					Debug.LogWarning("The applyAction for " + dnaConverter + " already existed in the list");
#endif
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

			public void Compress()
			{
				List<SlotData> slots = new List<SlotData>();
				for (int i = 0; i < slotDataList.Length; i++)
				{
					if (slotDataList[i] != null)
					{
						slots.Add(slotDataList[i]);
					}
				}
				slotDataList = slots.ToArray();
			}


			/// <summary>
			/// Combine additional recipe with current data.
			/// </summary>
			/// <param name="recipe">Recipe.</param>
			/// <param name="dontSerialize">If set to <c>true</c> recipe will not be serialized.</param>
			public void Merge(UMARecipe recipe, bool dontSerialize, bool mergeMatchingOverlays = true, bool mergeDNA = true, string raceName = null)
			{
				if (recipe == null)
                {
                    return;
                }

                if (mergeDNA)
				{
#if UNITY_EDITOR
                    if ((recipe.raceData != null) && (recipe.raceData != raceData))
					{
                        if (Debug.isDebugBuild)
                        {
                            Debug.LogWarning("Merging recipe with conflicting race data: " + recipe.raceData.name);
                        }
                    }
#endif
					foreach (var dnaEntry in recipe.umaDna)
					{
						var destDNA = GetOrCreateDna(dnaEntry.Value.GetType(), dnaEntry.Key);
						destDNA.Values = dnaEntry.Value.Values;
					}
				}

				mergedSharedColors.Clear();
				if (sharedColors == null)
                {
                    sharedColors = new OverlayColorData[0];
                }

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
								sharedColors[index] = sharedColor.Clone();
							}
						}
					}
				}

				if (slotDataList == null)
                {
                    slotDataList = new SlotData[0];
                }

                if (raceName != null)
				{
					if (recipe.slotDataList != null)
					{
						for (int i = 0; i < recipe.slotDataList.Length; i++)
						{
							SlotData sd = recipe.slotDataList[i];

							if (sd == null)
                            {
                                continue;
                            }

                            if (sd.HasRace(raceName))
							{
								MergeSlot(sd, dontSerialize, mergeMatchingOverlays, recipe.recipeName );
							}
						}
					}
				}
				else
				{
					if (recipe.slotDataList != null)
					{
						for (int i = 0; i < recipe.slotDataList.Length; i++)
						{
							var sd = recipe.slotDataList[i];
							if (sd == null) continue;
							MergeSlot(sd, dontSerialize, mergeMatchingOverlays, recipe.recipeName);
						}
					}
				}
			}
		}


		List<BaseUpdatedObject> boneAnimators = new List<BaseUpdatedObject>();

        public void FixedUpdate()
        {
			for(int i=0; i < boneAnimators.Count; i++)
			{
				boneAnimators[i].FixedUpdate(); 
			}
        }

        public void SetupEmbeddedPhysics()
        {
			boneAnimators.Clear();
            var slotDataList = umaRecipe.slotDataList;
			
			if (slotDataList == null || slotDataList.Length < 1)
            {
                return;
            }
            for (int i = 0; i < slotDataList.Length; i++)
			{
                SlotData slot = slotDataList[i];
                if (slot != null && slot.asset != null && slot.asset.animatedBones != null && slot.asset.animatedBones.Length > 0)
                {
                    for (int j = 0; j < slot.asset.animatedBones.Length; j++)
                    {
                        BaseUpdatedObject boneAnimator = slot.asset.animatedBones[j];
                        boneAnimator.Initialize(this,slot);
						boneAnimators.Add(boneAnimator);
                    }
                }
            }
        }

        /// <summary>
        /// Fire the Pre Update event.
        /// This happens before the Animator State is saved.
        /// </summary>
        public void FirePreUpdateUMABody()
		{
			if (PreUpdateUMABody != null)
			{
				PreUpdateUMABody.Invoke(this);
			}
		}

		public void FireAtlasUpdatedEvent(TextureEventParms tp)
		{
			if (AtlasUpdated != null)
			{
				//Debug.Log("Invoking AtlasUpdated Event");
                AtlasUpdated.Invoke(this,tp);
            }
        }


        /// <summary>
        /// Fire the Animator State Saved event.
        /// This happens before the Animator State is saved.
        /// </summary>
        public void FireAnimatorStateSavedEvent()
		{
			if (AnimatorStateSaved != null)
			{
				AnimatorStateSaved.Invoke(this);
			}
		}

		/// <summary>
		/// Fire the Animator State Restored event.
		/// This happens after the Animator State is restored.
		/// </summary>
		public void FireAnimatorStateRestoredEvent()
		{
			if (AnimatorStateRestored != null)
			{
				AnimatorStateRestored.Invoke(this);
			}
		}

		/// <summary>
		/// Calls character updated and/or created events.
		/// </summary>
		public void FireUpdatedEvent(bool cancelled)
		{
			this.cancelled = cancelled;
			if (CharacterBeforeUpdated != null)
			{
				CharacterBeforeUpdated.Invoke(this);
			}

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

		public void PreApplyDNA()
		{
			umaRecipe.PreApplyDNA(this);
		}

		public void ApplyDNA()
		{
			umaRecipe.ApplyDNA(this);
		}

		public void PostApplyDNA()
        {
			// Blendshape DNA has to be applied after the
			// skeleton is done, and the avatar is rebuilt
			// otherwise some caching issue in the animator can
			// restore bad blendshapes.
			umaRecipe.ApplyPostpassDNA(this);
		}

		public virtual void Dirty()
		{
			//Debug.Log($"Setting Dirty");
			if (dirty)
            {
                return;
            }

            dirty = true;
			umaGenerator.addDirtyUMA(this);
		}

		void OnDestroy()
		{
			if (staticCharacter)
            {
                return;
            }

            if (isOfficiallyCreated)
			{
				if (CharacterDestroyed != null)
				{
					CharacterDestroyed.Invoke(this);
				}
				isOfficiallyCreated = false;
			}
            ClearOverrides();
			CleanTextures();
			CleanMesh(true);
			CleanAvatar();			
			if (umaRoot != null)
			{
				// Edit time UMAs
				if (Application.isPlaying)
				{
					UMAUtils.DestroySceneObject(umaRoot);
				}
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
				if (!KeepAvatar)
				{
					if (animator.avatar)
					{
						UMAUtils.DestroyAvatar(animator.avatar);
						animator.avatar = null;
					}
				}
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
					if (generatedMaterials.materials[atlasIndex].secondPassMaterial != null)
					{
						UMAUtils.DestroySceneObject(generatedMaterials.materials[atlasIndex].secondPassMaterial);
						generatedMaterials.materials[atlasIndex].secondPassMaterial = null;
					}
					if (generatedMaterials.materials[atlasIndex].umaMaterial.materialType != UMAMaterial.MaterialType.UseExistingMaterial)
                    {
						UMAUtils.DestroySceneObject(generatedMaterials.materials[atlasIndex].material);
                    }
					for (int textureIndex = 0; textureIndex < generatedMaterials.materials[atlasIndex].resultingAtlasList.Length; textureIndex++)
					{
						if (generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex] != null)
						{
							Texture tempTexture = generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex];
                            generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex] = null;

                            if (tempTexture is RenderTexture)
							{
								RenderTexture tempRenderTexture = tempTexture as RenderTexture;
								var entityId = tempRenderTexture.GetEntityId();
								if (!RenderTexToCPU.renderTexturesToCPU.ContainsKey(entityId))
								{
                                    // this will be cleared up when the async call is completed.
                                    tempTexture = null;
									bool safe = RenderTexToCPU.SafeToFree(tempRenderTexture);
									if (safe)
									{
										UMARenderTextureTracker.Untrack(tempRenderTexture);
										if (tempRenderTexture.IsCreated())
										{
											tempRenderTexture.Release();
										}
										RenderTexToCPU.renderTexturesCleanedUMAData++;
										UMAUtils.DestroySceneObject(tempRenderTexture);

                                    }
								}
							}
							else
							{
								UMAUtils.DestroySceneObject(tempTexture);
							}
						}
					}
					if (generatedMaterials.materials[atlasIndex].umaMaterial.materialType != UMAMaterial.MaterialType.UseExistingMaterial)
					{
						UMAUtils.DestroySceneObject(generatedMaterials.materials[atlasIndex].material);
						generatedMaterials.materials[atlasIndex] = null;
					}
					else
					{
						//Debug.Log("Not removing material " + generatedMaterials.materials[atlasIndex].material.name);
                        generatedMaterials.materials[atlasIndex] = null;
                    }
                }
			}
			generatedMaterials.materials.Clear();
        }

		/// <summary>
		/// Destroy materials used to render mesh.
		/// </summary>
		/// <param name="destroyRenderer">If set to <c>true</c> destroy mesh renderer.</param>
		public void CleanMesh(bool destroyRenderer)
		{
			for(int j = 0; j < RendererCount; j++)
			{
				var renderer = GetRenderer(j);
				if (renderer == null)
                {
                    continue;
                }
				if (renderer.sharedMesh != null)
				{
					if (destroyRenderer)
					{
						// need to kill cloth first if it exists.
						var cloth = renderer.gameObject.GetComponent<Cloth>();
						if (cloth != null)
						{
							UMAUtils.DestroySceneObject(cloth);
						}
						UMAUtils.DestroySceneObject(renderer.sharedMesh);
						UMAUtils.DestroySceneObject(renderer);
					}
					else
					{
                        for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                        {
                            if (renderer.GetBlendShapeWeight(i) != 0.0f)
                            {
                                renderer.SetBlendShapeWeight(i, 0.0f);
                            }
                        }
                    }
				}
			}
		}

		/*
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
		}*/

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
		public SlotData GetSlotByIndex(int index)
		{
			return umaRecipe.GetSlotByIndex(index);
		}

		public SlotData GetSlotByHash(int nameHash)
		{
			return umaRecipe.GetSlotByHash(nameHash);
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

		public UmaTPose GetTPose()
        {
			UmaTPose tpose = OverrideTpose;

			if ((umaRecipe.raceData != null) && (umaRecipe.raceData.TPose != null) && (tpose == null))
			{
				tpose = umaRecipe.raceData.TPose;
			}
			return tpose;
		}

        /// <summary>
        /// Align skeleton to the TPose.
        /// </summary>
        public void GotoTPose()
        {
            UmaTPose tpose = GetTPose();
            if (tpose == null || skeleton == null) return;

            var hashes = GetOrBuildTPoseHashes(tpose);
            var bones = tpose.boneInfo;
            for (int i = 0; i < bones.Length; i++)
            {
                int hash = hashes[i];
                if (!skeleton.HasBone(hash)) continue;

                var bone = bones[i];
                skeleton.Set(hash, bone.position, bone.scale, bone.rotation);
            }
        }


        /// <summary>
        /// Calls character begun events on slots.
        /// </summary>
        public void FireCharacterBegunEvents()
		{
            if (CharacterBegun != null)
            {
                CharacterBegun.Invoke(this);
            }

            for (int i = 0; i < umaRecipe.slotDataList.Length; i++)
			{
                SlotData slotData = umaRecipe.slotDataList[i];
                if (slotData != null && slotData.asset.CharacterBegun != null)
				{
                    slotData.asset.Begin(this);
					slotData.asset.CharacterBegun.Invoke(this);
                    slotData.asset.SlotBeginProcessing.Invoke(this,slotData);
                }
			}
		}

		/// <summary>
		/// Calls DNA applied events on slots.
		/// </summary>
		public void FireDNAAppliedEvents()
		{
			if (CharacterBeforeDnaUpdated != null)
			{
				CharacterBeforeDnaUpdated.Invoke(this);
			}

			if (CharacterDnaUpdated != null)
			{
				CharacterDnaUpdated.Invoke(this);
			}

            for (int i = 0; i < umaRecipe.slotDataList.Length; i++)
			{
                SlotData slotData = umaRecipe.slotDataList[i];
                if (slotData != null && slotData.asset.DNAApplied != null)
				{
					slotData.asset.DNAApplied.Invoke(this);
                    if (slotData.asset.SlotProcessed != null)
                    {
						int count = slotData.asset.SlotProcessed.GetPersistentEventCount();
						if(count > 0) 
						{
                        	slotData.asset.SlotProcessed.Invoke(this, slotData);
                    	}
                    }
                }
			}
		}

		/// <summary>
		/// Calls character completed events on slots.
		/// </summary>
		public void FireCharacterCompletedEvents(bool fireEvents = true)
		{
            for (int i = 0; i < umaRecipe.slotDataList.Length; i++)
			{
                SlotData slotData = umaRecipe.slotDataList[i];
                if (slotData != null && slotData.asset.CharacterCompleted != null)
				{
					slotData.asset.Completed(this);
					if (fireEvents)
                    {
						if (slotData.asset.CharacterCompleted != null)
						{
							slotData.asset.CharacterCompleted.Invoke(this);
						}
                    }
                }
			}
		}

		/// <summary>
		/// Adds additional, non serialized, recipes.
		/// </summary>
		/// <param name="umaAdditionalRecipes">Additional recipes.</param>
		/// <param name="context">Context.</param>
		public void AddAdditionalRecipes(UMARecipeBase[] umaAdditionalRecipes, bool mergeMatchingOverlays=true)
		{
			if (umaAdditionalRecipes != null)
			{
                for (int i = 0; i < umaAdditionalRecipes.Length; i++)
				{
                    UMARecipeBase umaAdditionalRecipe = umaAdditionalRecipes[i];
                    if (umaAdditionalRecipe != null)
					{
						UMARecipe cachedRecipe = umaAdditionalRecipe.GetCachedRecipe();
						umaRecipe.Merge(cachedRecipe, true, mergeMatchingOverlays);
					}
				}
			}
		}

#region BlendShape Support

		[Obsolete("AddBakedBlendShape has been replaced with SetBlendShapeData", true)]
		public void AddBakedBlendShape(float dnaValue, string blendShapeZero, string blendShapeOne, bool rebuild = false)
		{ }

		[Obsolete("RemoveBakedBlendShape has been replaced with RemoveBlendShapeData", true)]
		public void RemoveBakedBlendShape(string name, bool rebuild = false)
		{ }

        /// <summary>
        /// Adds a named blendshape to be combined or baked to the UMA.
        /// </summary>
        /// <param name="name">string name of the blendshape.</param>
        /// <param name="bake">bool whether to bake the blendshape or not.</param>
        /// <param name="rebuild">Set to true to rebuild the UMA after after baking.  Use false to control when to rebuild to submit other changes.</param>
        public void SetBlendShapeData(string name, bool bake, bool rebuild = false)
        {
			BlendShapeData data;
#if DEBUG_BAKING
            Debug.Log("SetBlendShapeData: " + name + " bake: " + bake + " rebuild: " + rebuild);
#endif
            if (blendShapeSettings.blendShapes.TryGetValue(name, out data))
			{
				data.isBaked = bake;
            }
            else
            {
                data = new BlendShapeData
				{
					isBaked = bake,
				};

                blendShapeSettings.blendShapes.Add(name, data);
            }

            if (rebuild)
            {
                Dirty(true, true, true);
            }
        }

        /// <summary>
        /// Remove named blendshape from being baked during UMA combining.
        /// </summary>
        /// <param name="name">string name of the blendshape</param>
        /// <param name="rebuild">Set to true to rebuild the UMA after after baking.  Use false to control when to rebuild to submit other changes.</param>
        public void RemoveBlendShapeData(string name, bool rebuild = false)
        {
            if (blendShapeSettings.blendShapes.ContainsKey(name))
            {
                blendShapeSettings.blendShapes.Remove(name);
            }

            if (rebuild)
            {
                Dirty(true, true, true);
            }
        }

		/// <summary>
		/// Set the blendshape by it's name.  This is used for setting the unity blendshape directly on the skinnedMeshRenderer.
		/// Use SetBlendShapeData to set the data for the skinnedMeshCombiner and for baking blendshapes
		/// </summary>
		/// <param name="name">Name of the blendshape.</param>
		/// <param name="weight">Weight(float) to set this blendshape to.</param>
		/// <param name="allowRebuild">Triggers a rebuild of the uma character if the blendshape is baked</param>
		/// <param name="bakedOnly">Only set the value if the blendshape is baked</param>
		public void SetBlendShape(string name, float weight, bool allowRebuild = false, bool bakedOnly = false)
		{
			BlendShapeData data;
			if (blendShapeSettings.blendShapes.TryGetValue(name, out data))
			{
				float appliedWeight = data.isBaked && blendShapeSettings.forceBakedBlendShapeValue
					? blendShapeSettings.forcedBakedBlendShapeValue
					: weight;
				if (bakedOnly)
				{
					if (data.isBaked)
					{
						data.value = appliedWeight;
					}
					return;
				}
				// not baked only pass...
				data.value = appliedWeight;
			}
			else
			{
				if (bakedOnly)
				{
					// only working on baked stuff... but this isn't.
					return;
				}
                data = new BlendShapeData
				{
					value = weight,
					isBaked = false,
				};

				blendShapeSettings.blendShapes.Add(name, data);
			}

			if (data.isBaked)
			{
				if (allowRebuild)
				{
					Dirty(true, true, true);
				}
			}
			else
			{
				weight *= 100.0f; //Scale up to 1-100 for SetBlendShapeWeight.

                for (int i = 0; i < renderers.Length; i++)
				{
                    SkinnedMeshRenderer renderer = renderers[i];
                    int index = renderer.sharedMesh.GetBlendShapeIndex(name);
					if (index >= 0)
					{
						renderer.SetBlendShapeWeight(index, weight);
					}
				}
			}
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
				if (Debug.isDebugBuild)
                {
                    Debug.LogError ("GetBlendShapeName: Index is less than zero!");
                }

                return "";
			}
				
			if (rendererIndex >= RendererCount) //for multi-renderer support
			{
				if (Debug.isDebugBuild)
                {
                    Debug.LogError ("GetBlendShapeName: This renderer doesn't exist!");
                }

                return "";
			}

			//for multi-renderer support
			if( shapeIndex < renderers [rendererIndex].sharedMesh.blendShapeCount )
            {
                return renderers [rendererIndex].sharedMesh.GetBlendShapeName (shapeIndex);
            }

            if (Debug.isDebugBuild)
            {
                Debug.LogError ("GetBlendShapeName: no blendshape at index " + shapeIndex + "!");
            }

            return "";
		}
			
#endregion

		// Add this field inside class UMAData (near other private fields)
private readonly Dictionary<UmaTPose, int[]> _tposeHashCache = new Dictionary<UmaTPose, int[]>();

// Add this helper method inside class UMAData
private int[] GetOrBuildTPoseHashes(UmaTPose tpose)
{
    if (tpose == null) return Array.Empty<int>();

    int[] hashes;
    if (!_tposeHashCache.TryGetValue(tpose, out hashes))
	{
		// DeSerialize once per TPose; subsequent calls reuse cached hashes.
		tpose.DeSerialize();
		var bones = tpose.boneInfo ?? Array.Empty<SkeletonBone>();
		hashes = new int[bones.Length];
		for (int i = 0; i < bones.Length; i++)
		{
			hashes[i] = UMAUtils.StringToHash(bones[i].name);
		}
		_tposeHashCache[tpose] = hashes;
	}
    return hashes;
}
	}
}
