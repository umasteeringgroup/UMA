using UnityEngine;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;

namespace UMA
{
	public class UMAData : MonoBehaviour {	
		public SkinnedMeshRenderer myRenderer;
        
        [NonSerialized]
		public bool firstBake;

        public UMAGeneratorBase umaGenerator;

        [NonSerialized]
        public AtlasList atlasList = new AtlasList();
		
		public float atlasResolutionScale;
		
		public bool isMeshDirty;
		public bool isShapeDirty;
		public bool isTextureDirty;
		
		public RuntimeAnimatorController animationController;

		[Obsolete("Access to boneHashList will be removed, use BoneData() methods!", false)]
		public Dictionary<int, BoneData> boneHashList = new Dictionary<int, BoneData>();
		[Obsolete("Access to animatedBones will be removed, use BoneData() methods!", false)]
		public Transform[] animatedBones = new Transform[0];
		
		[Obsolete("Access to tempBoneData will be removed, use BoneData() methods!", false)]
		public BoneData[] tempBoneData; //Only while Dictionary can't be serialized

        public bool cancelled { get; private set; }
        [NonSerialized]
        public bool dirty = false;
        [NonSerialized]
        public bool _hasUpdatedBefore = false;
        private bool isOfficiallyCreated = false;
        [NonSerialized]
        public bool onQuit = false;
        [Obsolete("UMAData.OnUpdated is deprecated, please use OnCharacterUpdated instead.", false)]
        public event Action<UMAData> OnUpdated { add { OnCharacterUpdated += value; } remove { OnCharacterUpdated -= value; } }
		public event Action<UMAData> OnCharacterUpdated { add { if (CharacterUpdated == null) CharacterUpdated = new UMADataEvent(); CharacterUpdated.AddListener(new UnityAction<UMAData>(value)); } remove { CharacterUpdated.RemoveListener(new UnityAction<UMAData>(value)); } }
		public event Action<UMAData> OnCharacterCreated { add { if (CharacterUpdated == null) CharacterUpdated = new UMADataEvent(); CharacterCreated.AddListener(new UnityAction<UMAData>(value)); } remove { CharacterCreated.RemoveListener(new UnityAction<UMAData>(value)); } }
		public event Action<UMAData> OnCharacterDestroyed { add { if (CharacterUpdated == null) CharacterUpdated = new UMADataEvent(); CharacterDestroyed.AddListener(new UnityAction<UMAData>(value)); } remove { CharacterDestroyed.RemoveListener(new UnityAction<UMAData>(value)); } }
        public UMADataEvent CharacterCreated;
        public UMADataEvent CharacterDestroyed;
        public UMADataEvent CharacterUpdated;

        public GameObject umaRoot;

		public UMARecipe umaRecipe;
        public Animator animator;
        public UMASkeleton skeleton;


		void Awake () {
			firstBake = true;
			
			if(!umaGenerator){
				umaGenerator = GameObject.Find("UMAGenerator").GetComponent<UMAGeneratorBase>();	
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
            UpdateBoneData();
        }

#pragma warning disable 618
		public void Assign(UMAData other)
        {
            animator = other.animator;
            myRenderer = other.myRenderer;
            atlasResolutionScale = other.atlasResolutionScale;
            tempBoneData = other.tempBoneData;
            animatedBones = other.animatedBones;
            boneHashList = other.boneHashList;
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
				valid = valid && umaRecipe.Validate(umaGenerator);
			}
			
			#if UNITY_EDITOR
			if (!valid && UnityEditor.EditorApplication.isPlaying) UnityEditor.EditorApplication.isPaused = true;
			#endif
			
			return valid;
		}
		
		[System.Serializable]
		public class AtlasList
		{
			public List<AtlasElement> atlas = new List<AtlasElement>();
		}
		
		
		[System.Serializable]
		public class AtlasElement
		{
			public List<AtlasMaterialDefinition> atlasMaterialDefinitions;
			public Material materialSample;
			public Shader shader;
			public Texture[] resultingAtlasList;
			public Vector2 cropResolution;
			public float resolutionScale;
			public int mipmap;
            public string[] textureNameList;
		}
		
		[System.Serializable]
		public class AtlasMaterialDefinition
		{
			public MaterialDefinition source;
			public Rect atlasRegion;
			public bool isRectShared;
		}
		
		public class MaterialDefinition
		{
			public Texture2D[] baseTexture;
			public Color32 baseColor;
	        public Material materialSample;
			public Rect[] rects;
			public textureData[] overlays;
			public Color32[] overlayColors;
	        public Color32[][] channelMask;
	        public Color32[][] channelAdditiveMask;
			public SlotData slotData;

	        public Color32 GetMultiplier(int overlay, int textureType)
	        {
				
	            if (channelMask[overlay] != null && channelMask[overlay].Length > 0)
	            {
	                return channelMask[overlay][textureType];
	            }
	            else
	            {
	                if (textureType > 0) return new Color32(255, 255, 255, 255);
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
		public class textureData{
			public Texture2D[] textureList;
		}
		
		[System.Serializable]
		public class resultAtlasTexture{
			public Texture[] textureList;
		}

		[System.Serializable]
		public class UMARecipe{
			public RaceData raceData;
			[Obsolete("UMARecipe.umaDna will be hidden, use access methods instead.", false)]
			public Dictionary<Type, UMADnaBase> umaDna = new Dictionary<Type, UMADnaBase>();
            protected Dictionary<Type, DnaConverterBehaviour.DNAConvertDelegate> umaDnaConverter = new Dictionary<Type, DnaConverterBehaviour.DNAConvertDelegate>();
			public SlotData[] slotDataList;
			
			public bool Validate(UMAGeneratorBase generator) 
            {
				bool valid = true;
				if (raceData == null) {
					Debug.LogError("UMA recipe missing required race!");
					valid = false;
				}
				else {
                    valid = valid && raceData.Validate(generator);
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
                        valid = valid && slotData.Validate(generator);
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
			public UMADnaBase[] GetAllDna()
			{
				if ((raceData == null) || (slotDataList == null)) {
					return new UMADnaBase[0];
				}

				EnsureAllDNAPresent();
				UMADnaBase[] allDNA = new UMADnaBase[umaDna.Values.Count];
				umaDna.Values.CopyTo(allDNA, 0);
				return allDNA;
			}

            public void AddDna(UMADnaBase dna)
			{
				umaDna.Add(dna.GetType(), dna);
			}

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

            public void ClearDna()
            {
                umaDna.Clear();
            }

            public void RemoveDna(Type type)
            {
                umaDna.Remove(type);
            }

			public UMADnaBase GetDna(Type type)
			{
				UMADnaBase dna;
				if(umaDna.TryGetValue(type, out dna))
				{
					return dna;               
				}
				return null;
			}

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
			public void SetRace(RaceData raceData)
			{
				this.raceData = raceData;
				ClearDNAConverters();
			}

            public RaceData GetRace()
            {
                return this.raceData;
            }


            public void SetSlot(int index, SlotData slot)
            {
                if (index >= slotDataList.Length)
                {
                    SlotData[] tempArray = slotDataList;
                    slotDataList = new SlotData[index + 1];
                    for (int i = 0; i < tempArray.Length; i++)
                    {
                        slotDataList[i] = tempArray[i];
                    }
                }
                slotDataList[index] = slot;
            }
            public SlotData GetSlot(int index)
            {
                if (index < slotDataList.Length)
                    return slotDataList[index];
                return null;
            }

			public int GetSlotArraySize()
			{
				return slotDataList.Length;
			}
			
#pragma warning disable 618
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

			public void EnsureAllDNAPresent()
            {
                foreach (var converter in raceData.dnaConverterList)
                {
                    var dnaType = converter.DNAType;
                    if (!umaDna.ContainsKey(dnaType))
                    {
                        umaDna.Add(dnaType, dnaType.GetConstructor(System.Type.EmptyTypes).Invoke(null) as UMADnaBase);
                    }
                }
                foreach (var slotData in slotDataList)
                {
                    if (slotData != null && slotData.slotDNA != null)
                    {
                        var dnaType = slotData.slotDNA.DNAType;
                        if (!umaDna.ContainsKey(dnaType))
                        {
                            umaDna.Add(dnaType, dnaType.GetConstructor(System.Type.EmptyTypes).Invoke(null) as UMADnaBase);
                        }
                    }
                }
            }
#pragma warning restore 618

			public void ClearDNAConverters()
			{
				umaDnaConverter.Clear();
				foreach (var converter in raceData.dnaConverterList)
				{
					umaDnaConverter.Add(converter.DNAType, converter.ApplyDnaAction);
				}
			}
			
			public void AddDNAUpdater(DnaConverterBehaviour dnaConverter)
			{
				if( dnaConverter == null ) return;
				if (!umaDnaConverter.ContainsKey(dnaConverter.DNAType))
				{
					umaDnaConverter.Add(dnaConverter.DNAType, dnaConverter.ApplyDnaAction);
				}
			}

            public SlotData[] GetAllSlots()
            {
                return slotDataList;
            }
		}


		[System.Serializable]
		public class BoneData {
			public Transform boneTransform;
			public Vector3 originalBoneScale;
	        public Vector3 originalBonePosition;
			public Quaternion originalBoneRotation;
            [Obsolete("Access to actualBoneScale will be removed, no replacement planned!", false)]
            public Vector3 actualBoneScale;
            [Obsolete("Access to actualBonePosition will be removed, no replacement planned!", false)]
            public Vector3 actualBonePosition;
            [Obsolete("Access to actualBoneRotation will be removed, no replacement planned!", false)]
            public Quaternion actualBoneRotation;
		}

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
            if (!cancelled)
            {
                _hasUpdatedBefore = true;
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

		void OnApplicationQuit() {
			onQuit = true;
		}
		
		void OnDestroy(){
            if (isOfficiallyCreated)
            {
                if (CharacterDestroyed != null)
                {
                    CharacterDestroyed.Invoke(this);
                }
                isOfficiallyCreated = false;
            }
			if(_hasUpdatedBefore){
				cleanTextures();
                if (!onQuit)
                {
                    cleanMesh(true);
                    cleanAvatar();
                }   
			}
		}
		
		public void cleanAvatar(){
			animationController = null;
            if (animator != null)
            {
                if (animator.avatar) GameObject.Destroy(animator.avatar);
                if (animator) GameObject.Destroy(animator);
            }
		}

		public void cleanTextures(){
			for(int atlasIndex = 0; atlasIndex < atlasList.atlas.Count; atlasIndex++){
				if(atlasList.atlas[atlasIndex] != null && atlasList.atlas[atlasIndex].resultingAtlasList != null){
					for(int textureIndex = 0; textureIndex < atlasList.atlas[atlasIndex].resultingAtlasList.Length; textureIndex++){
						
						if(atlasList.atlas[atlasIndex].resultingAtlasList[textureIndex] != null){
							Texture tempTexture = atlasList.atlas[atlasIndex].resultingAtlasList[textureIndex];
							if(tempTexture is RenderTexture){
								RenderTexture tempRenderTexture = tempTexture as RenderTexture;
								tempRenderTexture.Release();
								Destroy(tempRenderTexture);
								tempRenderTexture = null;
							}else{
								Destroy(tempTexture);
							}
							atlasList.atlas[atlasIndex].resultingAtlasList[textureIndex] = null;
						}				
					}
				}
			}
		}
		
		public void cleanMesh(bool destroyRenderer){
			for(int i = 0; i < myRenderer.sharedMaterials.Length; i++){
				if(myRenderer){
					if(myRenderer.sharedMaterials[i]){
						DestroyImmediate(myRenderer.sharedMaterials[i]);
					}
				}
			}
			if(destroyRenderer) Destroy(myRenderer.sharedMesh);
		}
		
		
		public Texture[] backUpTextures(){
			List<Texture> textureList = new List<Texture>();
			
			for(int atlasIndex = 0; atlasIndex < atlasList.atlas.Count; atlasIndex++){
				if(atlasList.atlas[atlasIndex] != null && atlasList.atlas[atlasIndex].resultingAtlasList != null){
					for(int textureIndex = 0; textureIndex < atlasList.atlas[atlasIndex].resultingAtlasList.Length; textureIndex++){
						
						if(atlasList.atlas[atlasIndex].resultingAtlasList[textureIndex] != null){
							Texture tempTexture = atlasList.atlas[atlasIndex].resultingAtlasList[textureIndex];
							textureList.Add(tempTexture);
							atlasList.atlas[atlasIndex].resultingAtlasList[textureIndex] = null;
						}				
					}
				}
			}
			
			return textureList.ToArray();
		}

        public GameObject GetBoneGameObject(string boneName)
        {
            return GetBoneGameObject(UMASkeleton.StringToHash(boneName));
        }

        public GameObject GetBoneGameObject(int boneHash)
        {
            return skeleton.GetBoneGameObject(boneHash);
        }

		public void EnsureBoneData(Transform[] umaBones, Dictionary<Transform, Transform> boneMap)
		{
			EnsureBoneData(umaBones, null, boneMap);
		}
		
		public void EnsureBoneData(Transform[] umaBones, Transform[] animBones, Dictionary<Transform, Transform> boneMap)
		{
#pragma warning disable 618
			foreach (Transform bone in umaBones)
			{
				int nameHash = UMASkeleton.StringToHash(bone.name);
				if (!boneHashList.ContainsKey(nameHash))
				{
					Transform umaBone;
                    if( !boneMap.TryGetValue(bone, out umaBone ) )
                    {
                        continue;
                    }
                    
					BoneData newBoneData = new BoneData();
					newBoneData.originalBonePosition = umaBone.localPosition;
					newBoneData.originalBoneScale = umaBone.localScale;
                    newBoneData.originalBoneRotation = umaBone.localRotation;
                    newBoneData.boneTransform = umaBone;
                    newBoneData.actualBonePosition = umaBone.localPosition;
                    newBoneData.actualBoneScale = umaBone.localScale;
                    boneHashList.Add(UMASkeleton.StringToHash(umaBone.name), newBoneData);
				}
			}
			
			if (animBones != null) {
				List<Transform> newBones = new List<Transform>();
				foreach (Transform bone in animBones)
				{
					Transform umaBone = boneMap[bone];
					if ((umaBone != null) && (System.Array.IndexOf(animatedBones, umaBone) < 0)) {
						newBones.Add(umaBone);
					}
				}
				
				if (newBones.Count > 0) {
					int oldSize = animatedBones.Length;
					System.Array.Resize<Transform>(ref animatedBones, oldSize + newBones.Count);
					for (int i = 0; i < newBones.Count; i++) {
						animatedBones[oldSize + i] = newBones[i];
					}
				}
			}
#pragma warning restore 618
		}

		public void ClearBoneData()
		{
#pragma warning disable 618
			boneHashList.Clear();
			animatedBones = new Transform[0];
			tempBoneData = new UMAData.BoneData[0];
#pragma warning restore 618
			skeleton = null;
		}
		
		public void UpdateBoneData()
		{
#pragma warning disable 618
			if (tempBoneData == null) return;

			for (int i = 0; i < tempBoneData.Length; i++) {			
				boneHashList.Add(UMASkeleton.StringToHash(tempBoneData[i].boneTransform.gameObject.name), tempBoneData[i]);
			}
#pragma warning restore 618
		}

		public UMADnaBase[] GetAllDna()
		{
			return umaRecipe.GetAllDna();
		}
		
		public UMADnaBase GetDna(Type type)
		{
			return umaRecipe.GetDna(type);
		}

		public T GetDna<T>()
            where T : UMADnaBase
	    {
	        return umaRecipe.GetDna<T>();
	    }


	    public void Dirty(bool dnaDirty, bool textureDirty, bool meshDirty)
	    {
	        isShapeDirty   |= dnaDirty;
	        isTextureDirty |= textureDirty;
	        isMeshDirty    |= meshDirty;
			Dirty();
	    }
		
		public void SetSlot(int index, SlotData slot){
			
			if(index >= umaRecipe.slotDataList.Length){
				SlotData[] tempArray = umaRecipe.slotDataList;
				umaRecipe.slotDataList = new SlotData[index + 1];
				for(int i = 0; i < tempArray.Length; i++){
					umaRecipe.slotDataList[i] = tempArray[i];
				}			
			}
			umaRecipe.slotDataList[index] = slot;
		}
		
		public void SetSlots(SlotData[] slots){
			umaRecipe.slotDataList = slots;
		}
		
		public SlotData GetSlot(int index){
			return umaRecipe.GetSlot(index);	
		}

		public int GetSlotArraySize()
		{
			return umaRecipe.GetSlotArraySize();
		}

        public UMASkeleton GetSkeleton()
        {
            return skeleton;
        }

        public void GotoOriginalPose()
		{
#pragma warning disable 618
			foreach (BoneData bone in boneHashList.Values)
            {
				bone.boneTransform.localPosition = bone.originalBonePosition;
				bone.boneTransform.localScale = bone.originalBoneScale;
				bone.boneTransform.localRotation = bone.originalBoneRotation;
            }
#pragma warning restore 618
		}

        public void GotoTPose()
        {
            if (umaRecipe.raceData.TPose != null)
            {
                var tpose = umaRecipe.raceData.TPose;
                tpose.DeSerialize();
                for (int i = 0; i < tpose.boneInfo.Length; i++)
                {
                    var bone = tpose.boneInfo[i];
                    var hash = UMASkeleton.StringToHash(bone.name);
                    var go = skeleton.GetBoneGameObject(hash);
                    if (go == null) continue;
                    skeleton.SetPosition(hash, bone.position);
                    skeleton.SetRotation(hash, bone.rotation);
                    skeleton.SetScale(hash, bone.scale);
                }
            }
        }


        internal int[] GetAnimatedBones()
		{
#pragma warning disable 618
			List<int> res = new List<int>(tempBoneData.Length);
			Dictionary<int, int> resHash = new Dictionary<int, int>(tempBoneData.Length);
            for(int slotDataIndex = 0; slotDataIndex < umaRecipe.slotDataList.Length; slotDataIndex++)
            {
                var slotData = umaRecipe.slotDataList[slotDataIndex];
                if( slotData == null ) continue;
                if (slotData.animatedBones == null || slotData.animatedBones.Length == 0) continue;
                for (int animatedBoneIndex = 0; animatedBoneIndex < slotData.animatedBones.Length; animatedBoneIndex++)
                {
                    var animatedBone = slotData.animatedBones[animatedBoneIndex];
                    var hashName = UMASkeleton.StringToHash(animatedBone.name);
                    if( resHash.ContainsKey(hashName) ) continue;
                    resHash.Add(hashName, hashName);
                    res.Add(hashName);
                }
            }
            return res.ToArray();
#pragma warning restore 618
		}

		public void FireDNAAppliedEvents()
		{
			foreach (var slotData in umaRecipe.slotDataList)
			{
				if (slotData != null && slotData.DNAApplied != null)
				{
					slotData.DNAApplied.Invoke(this);
				}
			}
		}
	}
}
