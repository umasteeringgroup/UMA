using UnityEngine;
using System;
using System.IO;
using System.Runtime.Serialization;
using LitJson;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public partial class UMADna{
	
}

public class UMAData : MonoBehaviour {	
	public SkinnedMeshRenderer myRenderer;
	
	[System.Serializable]
	public class AtlasList
	{
		public List<AtlasElement> atlas;
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
	public class packedSlotData{
		public string slotID;
		public int overlayScale = 1;
		public int copyOverlayIndex = -1;
		public packedOverlayData[] OverlayDataList;
	}
	
	[System.Serializable]
	public class packedOverlayData{
		public string overlayID;
		public int[] colorList;
		public int[][] channelMaskList;
		public int[][] channelAdditiveMaskList;

		public int[] rectList;
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
    public class UMAPackedDna
    {
        public string dnaType;
        public string packedDna;
    }	
	
	[System.Serializable]
	public class UMAPackRecipe{
		public packedSlotData[] packedSlotDataList;
		public string race;
		public Dictionary<Type,UMADna> umaDna = new Dictionary<Type,UMADna>();

        public List<UMAPackedDna> packedDna = new List<UMAPackedDna>();
	}
	
	[System.Serializable]
	public class UMARecipe{
		public RaceData raceData;
		public Dictionary<Type,UMADna> umaDna = new Dictionary<Type,UMADna>();
		protected Dictionary<Type, Action<UMAData>> umaDnaConverter = new Dictionary<Type, Action<UMAData>>();
		public SlotData[] slotDataList;

        internal T GetDna<T>()
            where T : UMADna
        {
            UMADna dna;
            if(umaDna.TryGetValue(typeof(T), out dna))
            {
                return dna as T;               
            }
            return null;
        }

        internal void SetRace(RaceData raceData)
        {
            this.raceData = raceData;
            ClearDNAConverters();
        }

        internal void ApplyDNA(UMAData umaData)
        {
            foreach (var dnaEntry in umaDna)
            {            
                Action<UMAData> dnaConverter;
                if (umaDnaConverter.TryGetValue(dnaEntry.Key, out dnaConverter))
                {
                    dnaConverter(umaData);
                }
                else
                {
                    Debug.LogWarning("Cannot apply dna: " + dnaEntry.Key);
                }
            }
        }

        internal void ClearDNAConverters()
        {
            umaDnaConverter.Clear();
            foreach (var converter in raceData.dnaConverterList)
            {
                umaDnaConverter.Add(converter.DNAType, converter.ApplyDnaAction);
            }
        }

        internal void AddDNAUpdater(DnaConverterBehaviour dnaConverter)
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

    public bool dirty = false;
    private bool _hasUpdatedBefore = false;
	private bool onQuit = false;
    public event Action<UMAData> OnUpdated;
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
	
	public bool firstBake;
	
	public RaceLibrary raceLibrary;
	public SlotLibrary slotLibrary;
	public OverlayLibrary overlayLibrary;
	
	public UMAGenerator umaGenerator;
	
	public string streamedUMA;
	public UMARecipe umaRecipe;
	public UMAPackRecipe umaPackRecipe;
	
	public AtlasList atlasList;
	
	public float atlasResolutionScale;
	
	public bool isMeshDirty;
	public bool isShapeDirty;
	public bool isTextureDirty;
	
	public bool useLegacyCombiner;
	
	public CapsuleCollider capsuleCollider;
	public RuntimeAnimatorController animationController;
	
	public Dictionary<string,BoneData> boneList = new Dictionary<string,BoneData>();
    private BoneData[] updateBoneList = new BoneData[0];
	
	public BoneData[] tempBoneData; //Only while Dictionary can't be serialized
	
	void Awake () {
		umaPackRecipe = new UMAPackRecipe();
		
		firstBake = true;
		
		if(!umaGenerator){
			umaGenerator = GameObject.Find("UMAGenerator").GetComponent("UMAGenerator") as UMAGenerator;	
		}
        if(!slotLibrary){
            slotLibrary = GameObject.Find("SlotLibrary").GetComponent("SlotLibrary") as SlotLibrary;	
        }
        if(!raceLibrary){
            raceLibrary = GameObject.Find("RaceLibrary").GetComponent("RaceLibrary") as RaceLibrary;	
        }
        if(!overlayLibrary){
            overlayLibrary = GameObject.Find("OverlayLibrary").GetComponent("OverlayLibrary") as OverlayLibrary;	
        }
		
		UpdateBoneData();
	}

    void Update()
    {
        if (ownedRenderTextures != null)
        {
            foreach (var rt in ownedRenderTextures)
            {
                if (!rt.IsCreated())
                {
                    isTextureDirty = true;
                    umaGenerator.addDirtyUMA(this);
                }
            }
        }
    }

	
	void UpdateBoneData(){
		for(int i = 0; i < tempBoneData.Length; i++){			
			boneList.Add(tempBoneData[i].boneTransform.gameObject.name,tempBoneData[i]);
		}
	}
	
    //void LateUpdate () {
    //    //foreach (BoneData bone in updateBoneList)
    //    //{
    //    //    bone.boneTransform.localPosition = bone.actualBonePosition;
    //    //    bone.boneTransform.localScale = bone.actualBoneScale;
    //    //}
    //}

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

    internal void ChangeBoneMoveRelative(string boneName, Vector3 positionToChange)
    {
        BoneData tempBoneData;
        if (boneList.TryGetValue(boneName, out tempBoneData))
        {
            tempBoneData.actualBonePosition = tempBoneData.originalBonePosition + positionToChange;
            tempBoneData.boneTransform.localPosition = tempBoneData.actualBonePosition;
        }
    }

	public virtual void PackRecipe() {
		umaPackRecipe.packedSlotDataList = new packedSlotData[umaRecipe.slotDataList.Length];
		umaPackRecipe.race = umaRecipe.raceData.raceName;
		
		umaPackRecipe.packedDna.Clear();
		
		foreach(var dna in umaRecipe.umaDna.Values)
		{
            UMAPackedDna packedDna = new UMAPackedDna();
            packedDna.dnaType = dna.GetType().Name;
            packedDna.packedDna = UMADna.SaveInstance(dna);
            umaPackRecipe.packedDna.Add(packedDna);
		}

        for (int i = 0; i < umaRecipe.slotDataList.Length; i++)
        {
            if (umaRecipe.slotDataList[i] != null)
            {
                if (umaRecipe.slotDataList[i].listID != -1 && umaPackRecipe.packedSlotDataList[i] == null)
                {
                    packedSlotData tempPackedSlotData;

                    tempPackedSlotData = new packedSlotData();

                    tempPackedSlotData.slotID = umaRecipe.slotDataList[i].slotName;
					tempPackedSlotData.overlayScale = Mathf.FloorToInt(umaRecipe.slotDataList[i].overlayScale*100);
                    tempPackedSlotData.OverlayDataList = new UMAData.packedOverlayData[umaRecipe.slotDataList[i].OverlayCount];

                    for (int overlayID = 0; overlayID < tempPackedSlotData.OverlayDataList.Length; overlayID++)
                    {
                        tempPackedSlotData.OverlayDataList[overlayID] = new packedOverlayData();
                        tempPackedSlotData.OverlayDataList[overlayID].overlayID = umaRecipe.slotDataList[i].GetOverlay(overlayID).overlayName;

                        if (umaRecipe.slotDataList[i].GetOverlay(overlayID).color != new Color(1.0f, 1.0f, 1.0f, 1.0f))
                        {
                            //Color32 instead of Color?
                            tempPackedSlotData.OverlayDataList[overlayID].colorList = new int[4];
                            tempPackedSlotData.OverlayDataList[overlayID].colorList[0] = Mathf.FloorToInt(umaRecipe.slotDataList[i].GetOverlay(overlayID).color.r * 255.0f);
                            tempPackedSlotData.OverlayDataList[overlayID].colorList[1] = Mathf.FloorToInt(umaRecipe.slotDataList[i].GetOverlay(overlayID).color.g * 255.0f);
                            tempPackedSlotData.OverlayDataList[overlayID].colorList[2] = Mathf.FloorToInt(umaRecipe.slotDataList[i].GetOverlay(overlayID).color.b * 255.0f);
                            tempPackedSlotData.OverlayDataList[overlayID].colorList[3] = Mathf.FloorToInt(umaRecipe.slotDataList[i].GetOverlay(overlayID).color.a * 255.0f);
                        }

                        if (umaRecipe.slotDataList[i].GetOverlay(overlayID).rect != new Rect(0, 0, 0, 0))
                        {
                            //Might need float in next version
                            tempPackedSlotData.OverlayDataList[overlayID].rectList = new int[4];
                            tempPackedSlotData.OverlayDataList[overlayID].rectList[0] = (int)umaRecipe.slotDataList[i].GetOverlay(overlayID).rect.x;
                            tempPackedSlotData.OverlayDataList[overlayID].rectList[1] = (int)umaRecipe.slotDataList[i].GetOverlay(overlayID).rect.y;
                            tempPackedSlotData.OverlayDataList[overlayID].rectList[2] = (int)umaRecipe.slotDataList[i].GetOverlay(overlayID).rect.width;
                            tempPackedSlotData.OverlayDataList[overlayID].rectList[3] = (int)umaRecipe.slotDataList[i].GetOverlay(overlayID).rect.height;
                        }

                        if (umaRecipe.slotDataList[i].GetOverlay(overlayID).channelMask != null)
                        {
                            tempPackedSlotData.OverlayDataList[overlayID].channelMaskList = new int[umaRecipe.slotDataList[i].GetOverlay(overlayID).channelMask.Length][];

                            for (int channelAdjust = 0; channelAdjust < umaRecipe.slotDataList[i].GetOverlay(overlayID).channelMask.Length; channelAdjust++)
                            {
                                tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust] = new int[4];
                                tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust][0] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelMask[channelAdjust].r;
                                tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust][1] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelMask[channelAdjust].g;
                                tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust][2] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelMask[channelAdjust].b;
                                tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust][3] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelMask[channelAdjust].a;
                            }

                        }
                        if (umaRecipe.slotDataList[i].GetOverlay(overlayID).channelAdditiveMask != null)
                        {
                            tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList = new int[umaRecipe.slotDataList[i].GetOverlay(overlayID).channelAdditiveMask.Length][];
                            for (int channelAdjust = 0; channelAdjust < umaRecipe.slotDataList[i].GetOverlay(overlayID).channelAdditiveMask.Length; channelAdjust++)
                            {
                                tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust] = new int[4];
                                tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust][0] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelAdditiveMask[channelAdjust].r;
                                tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust][1] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelAdditiveMask[channelAdjust].g;
                                tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust][2] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelAdditiveMask[channelAdjust].b;
                                tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust][3] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelAdditiveMask[channelAdjust].a;
                            }

                        }
                    }

                    umaPackRecipe.packedSlotDataList[i] = tempPackedSlotData;

                    //Shared overlays wont generate duplicated data
                    for (int i2 = i + 1; i2 < umaRecipe.slotDataList.Length; i2++)
                    {
                        if (umaRecipe.slotDataList[i2] != null)
                        {
                            if (umaPackRecipe.packedSlotDataList[i2] == null)
                            {
                                if (umaRecipe.slotDataList[i].GetOverlayList() == umaRecipe.slotDataList[i2].GetOverlayList())
                                {
                                    tempPackedSlotData = new packedSlotData();
                                    tempPackedSlotData.slotID = umaRecipe.slotDataList[i2].slotName;
                                    tempPackedSlotData.copyOverlayIndex = i;
                                    //umaPackRecipe.packedSlotDataList[i2] = tempPackedSlotData;
                                }
                            }
                        }
                    }
                }
            }
        }
	}
	
	public virtual void SaveToMemoryStream() {	
		PackRecipe();
		streamedUMA = JsonMapper.ToJson(umaPackRecipe);
	}
	
	
	
	public virtual void UnpackRecipe() {			
		umaRecipe.slotDataList = new SlotData[umaPackRecipe.packedSlotDataList.Length];
		umaRecipe.SetRace(raceLibrary.GetRace(umaPackRecipe.race));
		
		umaRecipe.umaDna.Clear();
		for(int dna = 0; dna < umaPackRecipe.packedDna.Count; dna++){
            Type dnaType = UMADna.GetType(umaPackRecipe.packedDna[dna].dnaType);
            umaRecipe.umaDna.Add(dnaType, UMADna.LoadInstance(dnaType, umaPackRecipe.packedDna[dna].packedDna));
		}

        for (int i = 0; i < umaPackRecipe.packedSlotDataList.Length; i++)
        {
            if (umaPackRecipe.packedSlotDataList[i] != null && umaPackRecipe.packedSlotDataList[i].slotID != null)
            {
                SlotData tempSlotData = SlotData.CreateInstance<SlotData>();
				tempSlotData = slotLibrary.InstantiateSlot(umaPackRecipe.packedSlotDataList[i].slotID);
                tempSlotData.overlayScale = umaPackRecipe.packedSlotDataList[i].overlayScale*0.01f;
				umaRecipe.slotDataList[i] = tempSlotData;

                if (umaPackRecipe.packedSlotDataList[i].copyOverlayIndex == -1)
                {

                    for (int overlay = 0; overlay < umaPackRecipe.packedSlotDataList[i].OverlayDataList.Length; overlay++)
                    {
                        Color tempColor;
                        Rect tempRect;

                        if (umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList != null)
                        {
                            tempColor = new Color(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList[0] / 255.0f, umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList[1] / 255.0f, umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList[2] / 255.0f, umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList[3] / 255.0f);
                        }
                        else
                        {
                            tempColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
                        }

                        if (umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList != null)
                        {
                            tempRect = new Rect(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList[0], umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList[1], umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList[2], umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList[3]);
                        }
                        else
                        {
                            tempRect = new Rect(0, 0, 0, 0);
                        }

						tempSlotData.AddOverlay(overlayLibrary.InstantiateOverlay(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].overlayID));
						tempSlotData.GetOverlay(tempSlotData.OverlayCount-1).color = tempColor;
						tempSlotData.GetOverlay(tempSlotData.OverlayCount-1).rect = tempRect;
						
                        if (umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].channelMaskList != null)
                        {
                            for (int channelAdjust = 0; channelAdjust < umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].channelMaskList.Length; channelAdjust++)
                            {
                                packedOverlayData tempData = umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay];
                                tempSlotData.GetOverlay(tempSlotData.OverlayCount - 1).SetColor(channelAdjust, new Color32((byte)tempData.channelMaskList[channelAdjust][0],
                                (byte)tempData.channelMaskList[channelAdjust][1],
                                (byte)tempData.channelMaskList[channelAdjust][2],
                                (byte)tempData.channelMaskList[channelAdjust][3]));
                            }
                        }

                        if (umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].channelAdditiveMaskList != null)
                        {
                            for (int channelAdjust = 0; channelAdjust < umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].channelAdditiveMaskList.Length; channelAdjust++)
                            {
                                packedOverlayData tempData = umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay];
                                tempSlotData.GetOverlay(tempSlotData.OverlayCount - 1).SetAdditive(channelAdjust, new Color32((byte)tempData.channelAdditiveMaskList[channelAdjust][0],
                                (byte)tempData.channelAdditiveMaskList[channelAdjust][1],
                                (byte)tempData.channelAdditiveMaskList[channelAdjust][2],
                                (byte)tempData.channelAdditiveMaskList[channelAdjust][3]));
                            }
                        }

                    }
                }
                else
                {

                    tempSlotData.SetOverlayList(umaRecipe.slotDataList[umaPackRecipe.packedSlotDataList[i].copyOverlayIndex].GetOverlayList());

                }
            }
        }		
	}
	
	public virtual void LoadFromMemoryStream() {		
		umaPackRecipe = JsonMapper.ToObject<UMAPackRecipe>(streamedUMA);
		UnpackRecipe();
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
	

    RenderTexture[] ownedRenderTextures;
    internal RenderTexture[] RetrieveRenderTextures()
    {
        return ownedRenderTextures;
    }

	internal void StoreRenderTextures(RenderTexture[] resultingRenderTextures)
    {
        ownedRenderTextures = resultingRenderTextures;
    }


	internal void EnsureBoneData(Transform[] umaBones, Dictionary<Transform, Transform> boneMap)
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
        where T : UMADna
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
}