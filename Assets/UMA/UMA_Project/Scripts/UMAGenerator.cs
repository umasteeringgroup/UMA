using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Object = UnityEngine.Object;

public class UMAGenerator : MonoBehaviour {	
	public bool usePRO;
	public bool convertRenderTexture;
	public bool fitAtlas;
	public bool AtlasCrop;
	public UMAData umaData;
	public List<UMAData> umaDirtyList;
	
	public string[] textureNameList;
	
	public int meshUpdates;
	public int maxMeshUpdates;
	
	public int atlasResolution;
	public int maxPixels;
	public UMAGeneratorCoroutine umaGeneratorCoroutine;
	
	public Transform textureMergePrefab;
	public TextureMerge textureMerge;	
	public Matrix4x4 tempMatrix;
	
    List<SkinnedMeshCombiner.CombineInstance> combinedMeshList;
	List<Material> combinedMaterialList;
	
	public LegacyCombineInstances legacyCombineInstances;
	
	void Awake () {
		
		legacyCombineInstances = new LegacyCombineInstances();
		legacyCombineInstances.umaGenerator = this;
		
		
		#if UNITY_EDITOR     
			if(usePRO && !UnityEditorInternal.InternalEditorUtility.HasPro()){
				Debug.LogWarning("You might need to disable usePRO option at" + this.name);
				usePRO = false;
			}
		#endif
		
		maxMeshUpdates = 1;
		if( atlasResolution == 0 ) atlasResolution = 256;
		umaGeneratorCoroutine = new UMAGeneratorCoroutine();
		
		if(!textureMerge){
			Transform tempTextureMerger = Instantiate(textureMergePrefab,Vector3.zero,Quaternion.identity) as Transform;
			textureMerge = tempTextureMerger.GetComponent("TextureMerge") as TextureMerge;
			textureMerge.transform.parent = transform;
			textureMerge.gameObject.SetActive(false);
		}
		
		//Garbage Collection hack
        var mb = (System.GC.GetTotalMemory(false) / (1024 * 1024));
        if (mb < 10)
        {
            byte[] data = new byte[10 * 1024 * 1024];
            data[0] = 0;
            data[10 * 1024 * 1024 - 1] = 0;
        }
	}
	
	void Update () {
		if(umaDirtyList.Count > 0){
			OnDirtyUpdate();	
		}
		meshUpdates = 0;	
	}

	
	public virtual void OnDirtyUpdate() {
		umaData = umaDirtyList[0];
		
		if(umaData.isMeshDirty){
			if(!umaData.isTextureDirty){
				if(!umaData.useLegacyCombiner){
					UpdateUMAMesh(false);
				}else{
					legacyCombineInstances.UpdateUMAMesh(false);
				}
			}
			
			umaData.isMeshDirty = false;
		}
        if(umaData.isTextureDirty){
			umaGeneratorCoroutine.Prepare(this);

            if (umaGeneratorCoroutine.Work())
            {
				if(!umaData.useLegacyCombiner){
                	UpdateUMAMesh(true);
				}else{
					legacyCombineInstances.UpdateUMAMesh(true);
				}
                umaData.isTextureDirty = false;
            }
            else
            {
                return;
            }
		}else if(umaData.isShapeDirty){
			UpdateUMABody(umaData);
			umaData.isShapeDirty = false;
			
			UMAReady();
		
		}else{
			
			UMAReady();
			
		}
			
	}	
	public virtual void addDirtyUMA(UMAData umaToAdd) {	
		if(umaToAdd){
			umaDirtyList.Add(umaToAdd);
		}
	}
	
	public virtual void UMAReady(){	
		if(umaData){
			umaData.myRenderer.enabled = true;
		    umaDirtyList.RemoveAt(0);
		    umaData.FireUpdatedEvent(); 
        }
    }

#if !UNITY_4_2
    private struct AnimationState
    {
        public int stateHash;
        public float stateTime;
    }
#endif

    public virtual void UpdateUMABody (UMAData umaData){
		if(umaData)
        {
#if !UNITY_4_2
            AnimationState[] snapshot = null;
#endif
            if (umaData.animationController)
            {
#if !UNITY_4_2
                var animator = umaData.GetComponent<Animator>();
                if (animator != null)
                {

                    snapshot = new AnimationState[animator.layerCount];
                    for (int i = 0; i < animator.layerCount; i++)
                    {
                        var state = animator.GetCurrentAnimatorStateInfo(i);
                        snapshot[i].stateHash = state.nameHash;
                        snapshot[i].stateTime = Mathf.Max(0, state.normalizedTime - Time.deltaTime / state.length);
                    }
                }
#endif
                foreach (var entry in umaData.boneList)
                {
                    entry.Value.boneTransform.localPosition = entry.Value.originalBonePosition;
					entry.Value.boneTransform.localScale = entry.Value.originalBoneScale;
                    entry.Value.boneTransform.localRotation = entry.Value.originalBoneRotation;
                }
            }
		    umaData.ApplyDNA();
		    if (umaData.animationController)
		    {
                var animator = umaData.GetComponent<Animator>();
                
				bool applyRootMotion = animator.applyRootMotion;
				bool animatePhysics = animator.animatePhysics;
				AnimatorCullingMode cullingMode = animator.cullingMode;
	
				if (animator) Object.DestroyImmediate(animator);
		        var oldParent = umaData.transform.parent;
                umaData.transform.parent = null;
                CreateAnimator(umaData.gameObject, umaData.umaRecipe.raceData.TPose, umaData.animationController,applyRootMotion,animatePhysics,cullingMode);
		        umaData.transform.parent = oldParent;
                animator = umaData.GetComponent<Animator>();
#if !UNITY_4_2
                if (snapshot != null)
                {
                    for (int i = 0; i < animator.layerCount; i++)
                    {
                        animator.Play(snapshot[i].stateHash, i, snapshot[i].stateTime);
                    }
                    animator.Update(0);
                }
#endif
		    }
		}
	}
	
    public static void CreateAnimator(GameObject root, UmaTPose umaTPose, RuntimeAnimatorController controller,bool applyRootMotion, bool animatePhysics,AnimatorCullingMode cullingMode)
    {
        umaTPose.DeSerialize();
        var animator = root.AddComponent<Animator>();
        animator.avatar = CreateAvatar(root, umaTPose);
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = applyRootMotion;
        animator.animatePhysics = animatePhysics;
        animator.cullingMode = cullingMode;
    }

    public static Avatar CreateAvatar(GameObject root, UmaTPose umaTPose)
    {
        HumanDescription description = CreateHumanDescription(root, umaTPose);
        Avatar res = AvatarBuilder.BuildHumanAvatar(root, description);
        return res;
    }

    public static HumanDescription CreateHumanDescription(GameObject root, UmaTPose umaTPose)
    {
        var res = new HumanDescription();
        res.armStretch = 0;
        res.feetSpacing = 0;
        res.legStretch = 0;
        res.lowerArmTwist = 0.2f;
        res.lowerLegTwist = 1f;
        res.upperArmTwist = 0.5f;
        res.upperLegTwist = 0.1f;

        res.human = umaTPose.humanInfo;
        res.skeleton = umaTPose.boneInfo;
        res.skeleton[0].name = root.name;
        SkeletonModifier(root, ref res.skeleton);
        return res;
    }

    private static void SkeletonModifier(GameObject root, ref SkeletonBone[] bones)
    {
        var umaData = root.GetComponent<UMAData>();
        for(var i = 0; i < bones.Length; i++)
        {
            var skeletonbone = bones[i];
            UMAData.BoneData entry;
            if (umaData.boneList.TryGetValue(skeletonbone.name, out entry))
            {
                //var entry = umaData.boneList[skeletonbone.name];
                skeletonbone.position = entry.boneTransform.localPosition;
                //skeletonbone.rotation = entry.boneTransform.localRotation;
                skeletonbone.scale = entry.boneTransform.localScale;
                bones[i] = skeletonbone;
            }
        }
    }

	public virtual void UpdateUMAMesh(bool updatedAtlas){
        combinedMeshList = new List<SkinnedMeshCombiner.CombineInstance>();
        combinedMaterialList = new List<Material>();

        if (updatedAtlas)
        {
            CombineByShader();
        }
        else
        {
			CombineByMaterial();
        }
			
		if( umaData.firstBake )
        {
            umaData.myRenderer.sharedMesh = new Mesh();
        }else{
			umaData.cleanMesh(false);	
		}
		
		var boneMap = new Dictionary<Transform, Transform>();
        SkinnedMeshCombiner.CombineMeshes(umaData.myRenderer, combinedMeshList.ToArray(), boneMap);

        if (updatedAtlas)
        {
            RecalculateUV();
        }

        umaData.umaRecipe.ClearDNAConverters();
        for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
        {
            SlotData slotData = umaData.umaRecipe.slotDataList[i];
			if(slotData != null){

            	umaData.EnsureBoneData(slotData.umaBoneData, boneMap);
				
            	umaData.umaRecipe.AddDNAUpdater(slotData.slotDNA);
			}
        }

        umaData.myRenderer.quality = SkinQuality.Bone4;
        umaData.myRenderer.useLightProbes = true;
		umaData.myRenderer.sharedMaterials = combinedMaterialList.ToArray();
		//umaData.myRenderer.sharedMesh.RecalculateBounds();
        umaData.myRenderer.sharedMesh.name = "UMAMesh";

        umaData.firstBake = false;
    }

	void CombineByShader(){
		SkinnedMeshCombiner.CombineInstance combineInstance;
		
		for(int atlasIndex = 0; atlasIndex < umaData.atlasList.atlas.Count; atlasIndex++){
			combinedMaterialList.Add(umaData.atlasList.atlas[atlasIndex].materialSample);
			
			for(int materialDefinitionIndex = 0; materialDefinitionIndex < umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++){
			
				combineInstance = new SkinnedMeshCombiner.CombineInstance();
	           
				combineInstance.destMesh = new int[1];
	            combineInstance.mesh = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.meshRenderer.sharedMesh;
	            combineInstance.bones = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.meshRenderer.bones;
	           
	            combineInstance.destMesh[0]=atlasIndex;
	            combinedMeshList.Add(combineInstance);
			}
		}
	}
	
	void CombineByMaterial()
    {		
        SlotData[] slots = umaData.umaRecipe.slotDataList;
        bool[] shareMaterial = new bool[slots.Length];
		
		SkinnedMeshCombiner.CombineInstance combineInstance;
        
		int indexCount = 0;
        for(int slotIndex = 0; slotIndex < slots.Length; slotIndex++){
			if(slots[slotIndex] != null){
				if(!shareMaterial[slotIndex]){
					combineInstance = new SkinnedMeshCombiner.CombineInstance();
					combineInstance.destMesh = new int[1];
		            combineInstance.mesh = slots[slotIndex].meshRenderer.sharedMesh;
		            combineInstance.bones = slots[slotIndex].meshRenderer.bones;
		            
		            combineInstance.destMesh[0]=indexCount;
		            combinedMeshList.Add(combineInstance);
					
					Material tempMaterial = Instantiate(slots[slotIndex].materialSample) as Material;
					tempMaterial.name = slots[slotIndex].slotName;
					for(int textureType = 0; textureType < textureNameList.Length; textureType++){
						if(tempMaterial.HasProperty(textureNameList[textureType])){
							slots[slotIndex].GetOverlay(0).textureList[textureType].filterMode = FilterMode.Bilinear;
							tempMaterial.SetTexture(textureNameList[textureType],slots[slotIndex].GetOverlay(0).textureList[textureType]);
						}
					}
					combinedMaterialList.Add(tempMaterial);
					
					
					shareMaterial[slotIndex] = true;
					
					for(int slotIndex2 = slotIndex; slotIndex2 < slots.Length; slotIndex2++){
						if(slots[slotIndex2] != null){
							if(slotIndex2 != slotIndex && !shareMaterial[slotIndex2]){
								if(slots[slotIndex].GetOverlay(0).textureList[0].name == slots[slotIndex2].GetOverlay(0).textureList[0].name){	
									combineInstance = new SkinnedMeshCombiner.CombineInstance();
									combineInstance.destMesh = new int[1];
						            combineInstance.mesh = slots[slotIndex2].meshRenderer.sharedMesh;
						            combineInstance.bones = slots[slotIndex2].meshRenderer.bones;
						            
						            combineInstance.destMesh[0]=indexCount;
						            combinedMeshList.Add(combineInstance);
									
									shareMaterial[slotIndex2] = true;
								}
							}
						}
					}
					indexCount++;	
					
				}
			}else{
				shareMaterial[slotIndex] = true;
			}
		}
	}
	
	void RecalculateUV(){
		List<Rect> tempAtlasRect = new List<Rect>();
		List<int> meshVertexAmount = new List<int>();
		
		for(int atlasIndex = 0; atlasIndex < umaData.atlasList.atlas.Count; atlasIndex++){
			for(int materialDefinitionIndex = 0; materialDefinitionIndex < umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++){
				tempAtlasRect.Add(umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].atlasRegion);
				meshVertexAmount.Add(umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex].source.slotData.meshRenderer.sharedMesh.vertexCount);
			}
		}

		Vector2[] originalUVs = umaData.myRenderer.sharedMesh.uv;
        Vector2[] atlasUVs = new Vector2[originalUVs.Length];
		
        int rectIndex = 0;
        int vertTracker = 0;
		
		for(int i = 0; i < atlasUVs.Length; i++ ) {
			
			atlasUVs[i].x = Mathf.Lerp( tempAtlasRect[rectIndex].xMin/atlasResolution, tempAtlasRect[rectIndex].xMax/atlasResolution, originalUVs[i].x );
            atlasUVs[i].y = Mathf.Lerp( tempAtlasRect[rectIndex].yMin/atlasResolution, tempAtlasRect[rectIndex].yMax/atlasResolution, originalUVs[i].y );            
			
			if(originalUVs[i].x > 1 || originalUVs[i].y > 1){
				Debug.Log(i);	
			}
			
            if(i >= (meshVertexAmount[rectIndex] + vertTracker) - 1) {
				vertTracker = vertTracker + meshVertexAmount[rectIndex];
                rectIndex++;
            }
        }
		umaData.myRenderer.sharedMesh.uv = atlasUVs;	
	}	
}