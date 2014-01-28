using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UMA;


namespace UMA
{
	[ExecuteInEditMode]
	public class UMAProcessBones : MonoBehaviour {	
		
		public bool runScript;
		public UMAData umaData;
		public Transform boneStructure;
		
		void Update () {
			if(runScript && umaData){
				
				umaData.boneHashList = new Dictionary<int, UMAData.BoneData>();
						
				Transform[] umaBones  = boneStructure.gameObject.GetComponentsInChildren<Transform>();
				umaData.tempBoneData = new UMAData.BoneData[umaBones.Length];
				
				
				
				
				for(int i = 0; i < umaBones.Length; i++){
					UMAData.BoneData tempBone = new UMAData.BoneData();
					
					tempBone.boneTransform = umaBones[i];
					
					tempBone.originalBonePosition = umaBones[i].localPosition;
					tempBone.originalBoneRotation = umaBones[i].localRotation;
					tempBone.originalBoneScale = umaBones[i].localScale;

                    umaData.boneHashList.Add(UMASkeleton.StringToHash(umaBones[i].name), tempBone);
					
					umaData.tempBoneData[i] = tempBone;//Only while Dictionary can't be serialized
				}
                Debug.Log(umaData.boneHashList.Count + " bones have been included");
			}
			runScript = false;
		}
	}
}