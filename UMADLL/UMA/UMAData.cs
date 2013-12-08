using UnityEngine;
using System;
using System.IO;
using System.Runtime.Serialization;
using LitJson;
using System.Collections;
using System.Collections.Generic;

namespace UMA
{
	public class UMAData : MonoBehaviour {	
		public SkinnedMeshRenderer myRenderer;
		public bool firstBake;
		
		public UMAGenerator umaGenerator;
		
		public AtlasList atlasList;
		
		public float atlasResolutionScale;
		
		public bool isMeshDirty;
		public bool isShapeDirty;
		public bool isTextureDirty;
		
		public RuntimeAnimatorController animationController;

        public Dictionary<int, BoneData> boneHashList = new Dictionary<int, BoneData>();
        internal Dictionary<string, BoneData> boneList = new Dictionary<string, BoneData>();
		public BoneData[] updateBoneList = new BoneData[0];
		
		public BoneData[] tempBoneData; //Only while Dictionary can't be serialized

		public bool dirty = false;
		public bool _hasUpdatedBefore = false;
		public bool onQuit = false;
		public event Action<UMAData> OnUpdated;
        public GameObject umaRoot;

		public UMARecipe umaRecipe;
        public Animator animator;
        public UMASkeleton skeleton;


		void Awake () {
			firstBake = true;
			
			if(!umaGenerator){
				umaGenerator = GameObject.Find("UMAGenerator").GetComponent("UMAGenerator") as UMAGenerator;	
			}

            if (umaRecipe == null)
            {
                umaRecipe = new UMARecipe();
                atlasList = new AtlasList();
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

        public void Assign(UMAData other)
        {
            animator = other.animator;
            myRenderer = other.myRenderer;
            atlasResolutionScale = other.atlasResolutionScale;
            updateBoneList = other.updateBoneList;
            boneList = other.boneList;
            umaRoot = other.umaRoot;
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

	        internal Color32 GetMultiplier(int overlay, int textureType)
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
	        internal Color32 GetAdditive(int overlay, int textureType)
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
            public Dictionary<Type, UMADnaBase> umaDna = new Dictionary<Type, UMADnaBase>();
            protected Dictionary<Type, DnaConverterBehaviour.DNAConvertDelegate> umaDnaConverter = new Dictionary<Type, DnaConverterBehaviour.DNAConvertDelegate>();
			public SlotData[] slotDataList;
			
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
			
			public void SetRace(RaceData raceData)
			{
				this.raceData = raceData;
				ClearDNAConverters();
			}
			
			public void ApplyDNA(UMAData umaData)
			{
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
		}


		[System.Serializable]
		public class BoneData{
			public Transform boneTransform;
			public Vector3 actualBoneScale;
			public Vector3 originalBoneScale;
			public Vector3 actualBonePosition;
			public Quaternion actualBoneRotation;
	        public Vector3 originalBonePosition;
			public Quaternion originalBoneRotation;
		}

	    public void FireUpdatedEvent()
	    {
	        if (OnUpdated != null)
	        {
	            OnUpdated(this);
	        }
			_hasUpdatedBefore = true;
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
	            umaGenerator = GameObject.Find("UMAGenerator").GetComponent("UMAGenerator") as UMAGenerator;
	        }
	        if (umaGenerator)
	        {
	            umaGenerator.addDirtyUMA(this);
	        }
	    }

		
		public void UpdateBoneData()
        {
            if (tempBoneData == null) return;
			for(int i = 0; i < tempBoneData.Length; i++){			
				boneList.Add(tempBoneData[i].boneTransform.gameObject.name,tempBoneData[i]);
                boneHashList.Add(UMASkeleton.StringToHash(tempBoneData[i].boneTransform.gameObject.name), tempBoneData[i]);
			}
		}

	    public void ChangeBone(string boneName, Vector3 positionToChange, Vector3 scaleToChange)
	    {
	        BoneData tempBoneData;
	        if (boneList.TryGetValue(boneName, out tempBoneData))
	        {
	            tempBoneData.actualBoneScale = scaleToChange;
	            tempBoneData.actualBonePosition = positionToChange;
	            tempBoneData.boneTransform.localPosition = positionToChange;
	            tempBoneData.boneTransform.localScale = scaleToChange;
	        }
	    }
		
		public void ChangeBonePosition(string boneName,Vector3 positionToChange) {
	        BoneData tempBoneData;
	        if (boneList.TryGetValue(boneName, out tempBoneData))
	        {
	            tempBoneData.actualBonePosition = positionToChange;
	            tempBoneData.boneTransform.localPosition = positionToChange;
	        }
	    }

	    public void ChangeBoneScale(string boneName, Vector3 scaleToChange)
	    {
	        BoneData tempBoneData;
	        if (boneList.TryGetValue(boneName, out tempBoneData))
	        {
	            tempBoneData.actualBoneScale = scaleToChange;
	            tempBoneData.boneTransform.localScale = scaleToChange;
	        }
	    }

		public void ChangeBoneMoveRelative(string boneName, Vector3 positionToChange)
	    {
	        BoneData tempBoneData;
	        if (boneList.TryGetValue(boneName, out tempBoneData))
	        {
	            tempBoneData.actualBonePosition = tempBoneData.originalBonePosition + positionToChange;
	            tempBoneData.boneTransform.localPosition = tempBoneData.actualBonePosition;
	        }
	    }

		
		void OnApplicationQuit() {
			onQuit = true;
		}
		
		void OnDestroy(){
			if(_hasUpdatedBefore){
				cleanTextures();
				if(!onQuit)cleanMesh(true);
				cleanAvatar();
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

		public void EnsureBoneData(Transform[] umaBones, Dictionary<Transform, Transform> boneMap)
	    {
	        foreach (var bone in umaBones)
	        {
	            var umaBone = boneMap[bone];
	            if (!boneList.ContainsKey(umaBone.name))
	            {
	                BoneData newBoneData = new BoneData();
	                newBoneData.actualBonePosition = umaBone.localPosition;
	                newBoneData.originalBonePosition = umaBone.localPosition;
	                newBoneData.actualBoneScale = umaBone.localScale;
					newBoneData.originalBoneScale = umaBone.localScale;
	                newBoneData.boneTransform = umaBone;
	                boneList.Add(umaBone.name, newBoneData);
                    boneHashList.Add(UMASkeleton.StringToHash(umaBone.name), newBoneData);
	            }
	        }

	        if (updateBoneList.Length != umaRecipe.raceData.AnimatedBones.Length)
	        {
	            updateBoneList = new BoneData[umaRecipe.raceData.AnimatedBones.Length];
	        }
	        int i = 0;
	        foreach (var updateName in umaRecipe.raceData.AnimatedBones)
	        {
	            updateBoneList[i++] = boneList[updateName];
	        }
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
			return umaRecipe.slotDataList[index];	
		}

        public UMASkeleton GetSkeleton()
        {
            return skeleton;
        }
    }
}