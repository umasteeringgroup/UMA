using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace UMA
{
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
		
        public UMAMeshCombiner meshCombiner;

		public float unityVersion;

		void Awake () {
			
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
                    meshCombiner.UpdateUMAMesh(false, this);
				}
				
				umaData.isMeshDirty = false;
			}
	        if(umaData.isTextureDirty){
				umaGeneratorCoroutine.Prepare(this);

	            if (umaGeneratorCoroutine.Work())
	            {
                    meshCombiner.UpdateUMAMesh(true, this);
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

	
	    private struct AnimationState
	    {
	        public int stateHash;
	        public float stateTime;
	    }
	
	    public virtual void UpdateUMABody (UMAData umaData){
			if(umaData)
	        {
				AnimationState[] snapshot = null;
	            if (umaData.animationController)
	            {
					if(unityVersion >= 4.3f){
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
					}
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
	                
					bool applyRootMotion = false;
					bool animatePhysics = false;
					AnimatorCullingMode cullingMode = AnimatorCullingMode.AlwaysAnimate;

					if(animator){
						applyRootMotion = animator.applyRootMotion;
						animatePhysics = animator.animatePhysics;
						cullingMode = animator.cullingMode;
						Object.DestroyImmediate(animator);
					}
					var oldParent = umaData.transform.parent;
	                umaData.transform.parent = null;
	                CreateAnimator(umaData.gameObject, umaData.umaRecipe.raceData.TPose, umaData.animationController,applyRootMotion,animatePhysics,cullingMode);
			        umaData.transform.parent = oldParent;
	                animator = umaData.GetComponent<Animator>();
					if(unityVersion >= 4.3f){
		                if (snapshot != null)
		                {
		                    for (int i = 0; i < animator.layerCount; i++)
		                    {
		                        animator.Play(snapshot[i].stateHash, i, snapshot[i].stateTime);
		                    }
		                    animator.Update(0);
		                }
					}
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
	}
}