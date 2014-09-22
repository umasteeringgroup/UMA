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

#pragma warning disable 618
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
				
				ReplaceAnimatedBones();
			}
			runScript = false;
		}
		
		/// <summary>
		/// If you create a new Race in UMA, you must set the Animated Bones of UMAData manually. 
		/// This is described here https://www.youtube.com/watch?v=_pzrU_2G0qs#t=136
		/// We can automate this partially, if the new race has the same naming schema for the bones.
		/// In case of the Werewolf race, it will replace all animated bones.
		/// </summary>
		public void ReplaceAnimatedBones()
		{
			int replacedCount = 0;

			for(int i=0; i < umaData.animatedBones.Length; i++)
			{
				Transform boneTransform = umaData.animatedBones[i];
				Transform newBoneTransform = FindChildRecursive(boneStructure, boneTransform.name);
				if (newBoneTransform == null)
				{
					Debug.LogWarning("You must set the animated bone " + boneTransform.name + " manually");
				}
				else
				{
					replacedCount++;
					umaData.animatedBones[i] = newBoneTransform;
				}
			}

			Debug.Log(replacedCount + " animated bones have been replaced");
		}
#pragma warning restore 618

		public static Transform FindChildRecursive(Transform current, string name)   
		{
			if (current.name == name) return current;
			
			for (int i = 0; i < current.childCount; ++i)
			{
				Transform found = FindChildRecursive(current.GetChild(i), name);
				if (found != null) return found;
			}
			
			return null;
		}	
		
	}
}
