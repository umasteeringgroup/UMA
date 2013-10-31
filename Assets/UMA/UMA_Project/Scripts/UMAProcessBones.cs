using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[ExecuteInEditMode]

public class UMAProcessBones : MonoBehaviour {	
	
	public bool runScript;
	public UMAData umaData;
	public Transform boneStructure;
	
	void Update () {
		if(runScript && umaData){
			
			umaData.boneList = new Dictionary<string, UMAData.BoneData>();
					
			Transform[] umaBones  = boneStructure.gameObject.GetComponentsInChildren<Transform>();
			umaData.tempBoneData = new UMAData.BoneData[umaBones.Length];
			
			
			
			
			for(int i = 0; i < umaBones.Length; i++){
				UMAData.BoneData tempBone = new UMAData.BoneData();
				
				tempBone.boneTransform = umaBones[i];
				
				tempBone.actualBonePosition = umaBones[i].localPosition;
				tempBone.actualBoneScale = umaBones[i].localScale;
				tempBone.originalBonePosition = umaBones[i].localPosition;
				tempBone.originalBoneRotation = umaBones[i].localRotation;
				tempBone.originalBoneScale = umaBones[i].localScale;
				
				
				umaData.boneList.Add(umaBones[i].name,tempBone);
				
				umaData.tempBoneData[i] = tempBone;//Only while Dictionary can't be serialized
			}
			Debug.Log(umaData.boneList.Count+" bones have been included");
		}
		runScript = false;
	}
}