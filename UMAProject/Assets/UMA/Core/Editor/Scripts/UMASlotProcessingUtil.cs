#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Unity.Collections;
using UMA.CharacterSystem;

namespace UMA.Editors
{
	public static class UMASlotProcessingUtil
	{
        /// <summary>
        ///  Updates an Existing SlotDataAsset.
        /// </summary>
        /// <param name="slot">The existing SlotDataAsset to be updated</param>
        /// <param name="mesh">Mesh.</param>
        /// <param name="material">Material.</param>
        /// <param name="prefabMesh">Prefab mesh.</param>
        /// <param name="rootBone">Root bone.</param>
        public static void UpdateSlotData( SlotDataAsset slot, SkinnedMeshRenderer mesh, UMAMaterial material, SkinnedMeshRenderer prefabMesh, string rootBone, bool calcTangents)
        {
            string path = UMAUtils.GetAssetFolder(AssetDatabase.GetAssetPath(slot));
            string assetName = slot.slotName;

            if (path.Length <= 0)
            {
                Debug.LogWarning("CreateSlotData: Path to existing asset is empty!");
                return;
            }

            GameObject tempGameObject = UnityEngine.Object.Instantiate(mesh.transform.parent.gameObject) as GameObject;
			var resultingSkinnedMeshes = tempGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            SkinnedMeshRenderer resultingSkinnedMesh = null;
            foreach (var skinnedMesh in resultingSkinnedMeshes)
            {
                if (skinnedMesh.name == mesh.name)
                {
                    resultingSkinnedMesh = skinnedMesh;
                }
            }

            Mesh resultingMesh;
            if (prefabMesh != null)
            {
                resultingMesh = SeamRemoval.PerformSeamRemoval(resultingSkinnedMesh, prefabMesh, 0.0001f,calcTangents);
                resultingSkinnedMesh.sharedMesh = resultingMesh;
                SkinnedMeshAligner.AlignBindPose(prefabMesh, resultingSkinnedMesh);
            }
            else
            {
                resultingMesh = (Mesh)GameObject.Instantiate(resultingSkinnedMesh.sharedMesh);
            }

			//CountBoneweights(resultingMesh);

            var usedBonesDictionary = CompileUsedBonesDictionary(resultingMesh, new List<int>());
            if (usedBonesDictionary.Count != resultingSkinnedMesh.bones.Length)
            {
                resultingMesh = BuildNewReduceBonesMesh(resultingMesh, usedBonesDictionary);
            }

			//CountBoneweights(resultingMesh);

			string meshAssetName = path + '/' + mesh.name + ".asset";

			AssetDatabase.CreateAsset(resultingMesh, meshAssetName );

            tempGameObject.name = mesh.transform.parent.gameObject.name;
            Transform[] transformList = tempGameObject.GetComponentsInChildren<Transform>();

            GameObject newObject = new GameObject();

            for (int i = 0; i < transformList.Length; i++)
            {
                if (transformList[i].name == rootBone)
                {
                    transformList[i].parent = newObject.transform;
                }
                else if (transformList[i].name == mesh.name)
                {
                    transformList[i].parent = newObject.transform;
                }
            }

            GameObject.DestroyImmediate(tempGameObject);
            resultingSkinnedMesh = newObject.GetComponentInChildren<SkinnedMeshRenderer>();
            if (resultingSkinnedMesh)
            {
                if (usedBonesDictionary.Count != resultingSkinnedMesh.bones.Length)
                {

                    resultingSkinnedMesh.bones = BuildNewReducedBonesList(resultingSkinnedMesh.bones, usedBonesDictionary);
                }
                resultingSkinnedMesh.sharedMesh = resultingMesh;
				//CountBoneweights(resultingMesh);
            }

			string SkinnedName = path + '/' + assetName + "_Skinned.prefab";

#if UNITY_2018_3_OR_NEWER
            var skinnedResult = PrefabUtility.SaveAsPrefabAsset(newObject, SkinnedName);
#else
			var skinnedResult = UnityEditor.PrefabUtility.CreatePrefab(SkinnedName, newObject);
#endif
            GameObject.DestroyImmediate(newObject);

            var meshgo = skinnedResult.transform.Find(mesh.name);
            var finalMeshRenderer = meshgo.GetComponent<SkinnedMeshRenderer>();

            slot.UpdateMeshData(finalMeshRenderer,rootBone);
			slot.meshData.SlotName = slot.slotName;
            var cloth = mesh.GetComponent<Cloth>();
            if (cloth != null)
            {
                slot.meshData.RetrieveDataFromUnityCloth(cloth);
            }
            AssetDatabase.SaveAssets();
			AssetDatabase.DeleteAsset(SkinnedName);
			AssetDatabase.DeleteAsset(meshAssetName);
		}


		public static SlotDataAsset CreateSlotData(string slotFolder, string assetFolder, string assetName, string slotName, bool nameByMaterial, SkinnedMeshRenderer slotMesh, UMAMaterial material, SkinnedMeshRenderer seamsMesh, List<string> KeepList, string rootBone, bool binarySerialization = false, bool calcTangents=true)
		{
			if (!System.IO.Directory.Exists(slotFolder + '/' + assetFolder))
			{
				System.IO.Directory.CreateDirectory(slotFolder + '/' + assetFolder);
			}

			if (!System.IO.Directory.Exists(slotFolder + '/' + assetName))
			{
				System.IO.Directory.CreateDirectory(slotFolder + '/' + assetName);
			}

			GameObject tempGameObject = UnityEngine.Object.Instantiate(slotMesh.transform.parent.gameObject) as GameObject;

			var resultingSkinnedMeshes = tempGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
			SkinnedMeshRenderer resultingSkinnedMesh = null;
			foreach (var skinnedMesh in resultingSkinnedMeshes)
			{
				if (skinnedMesh.name == slotMesh.name)
				{
					resultingSkinnedMesh = skinnedMesh;
					//CountBoneweights(skinnedMesh.sharedMesh);
				}
			}

			Transform[] bones = resultingSkinnedMesh.bones;
			List<int> KeepBoneIndexes = new List<int>();

			for(int i=0;i<bones.Length;i++)
            {
				Transform t = bones[i];
				foreach(string keep in KeepList)
                {
					if (t.name.Contains(keep))
                    {
						KeepBoneIndexes.Add(i);
						break; // only add to keeplist once.
                    }
                }
            }

			Mesh resultingMesh;
			if (seamsMesh != null)
			{
				resultingMesh = SeamRemoval.PerformSeamRemoval(resultingSkinnedMesh, seamsMesh, 0.0001f, calcTangents);
				resultingSkinnedMesh.sharedMesh = resultingMesh;
				//CountBoneweights(resultingMesh);
				SkinnedMeshAligner.AlignBindPose(seamsMesh, resultingSkinnedMesh);
			}
			else
			{
				resultingMesh = (Mesh)GameObject.Instantiate(resultingSkinnedMesh.sharedMesh);
				//CountBoneweights(resultingMesh);
			}

			var usedBonesDictionary = CompileUsedBonesDictionary(resultingMesh,KeepBoneIndexes);
			if (usedBonesDictionary.Count != resultingSkinnedMesh.bones.Length)
			{
				resultingMesh = BuildNewReduceBonesMesh(resultingMesh, usedBonesDictionary);
				//CountBoneweights(resultingMesh);
			}

			string theMesh = slotFolder + '/' + assetName + '/' + slotMesh.name + ".asset";
			if (binarySerialization)
			{
				//Work around for mesh being serialized as project format settings (text) when binary is much faster.
				//If Unity introduces a way to set mesh as binary serialization then this becomes unnecessary.
				BinaryAssetWrapper binaryAsset = ScriptableObject.CreateInstance<BinaryAssetWrapper>();
				AssetDatabase.CreateAsset(binaryAsset, theMesh);
				AssetDatabase.AddObjectToAsset(resultingMesh, binaryAsset);
			}
			else
			{
				AssetDatabase.CreateAsset(resultingMesh, theMesh);
			}

			tempGameObject.name = slotMesh.transform.parent.gameObject.name;
			Transform[] transformList = tempGameObject.GetComponentsInChildren<Transform>();

			GameObject newObject = new GameObject();

			for (int i = 0; i < transformList.Length; i++)
			{
				if (transformList[i].name == rootBone)
				{
					transformList[i].parent = newObject.transform;
				}
				else if (transformList[i].name == slotMesh.name)
				{
					transformList[i].parent = newObject.transform;
				}
			}

			GameObject.DestroyImmediate(tempGameObject);
			resultingSkinnedMesh = newObject.GetComponentInChildren<SkinnedMeshRenderer>();
			//CountBoneweights(resultingSkinnedMesh.sharedMesh);

			if (resultingSkinnedMesh)
			{
				if (usedBonesDictionary.Count != resultingSkinnedMesh.bones.Length)
				{

					resultingSkinnedMesh.bones = BuildNewReducedBonesList(resultingSkinnedMesh.bones, usedBonesDictionary);
				}
				resultingSkinnedMesh.sharedMesh = resultingMesh;
				//CountBoneweights(resultingMesh);
			}

			string SkinnedName = slotFolder + '/' + assetName + '/' + assetName + "_Skinned.prefab";

#if UNITY_2018_3_OR_NEWER
			var skinnedResult = PrefabUtility.SaveAsPrefabAsset(newObject, SkinnedName);
#else
			var skinnedResult = UnityEditor.PrefabUtility.CreatePrefab(SkinnedName, newObject);
#endif
			GameObject.DestroyImmediate(newObject);

			var meshgo = skinnedResult.transform.Find(slotMesh.name);
			var finalMeshRenderer = meshgo.GetComponent<SkinnedMeshRenderer>();

			var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
			slot.slotName = slotName;
			//Make sure slots get created with a name hash
			slot.nameHash = UMAUtils.StringToHash(slot.slotName);
			slot.material = material;
			slot.UpdateMeshData(finalMeshRenderer,rootBone);
			var cloth = slotMesh.GetComponent<Cloth>();
			if (cloth != null)
			{
				slot.meshData.RetrieveDataFromUnityCloth(cloth);
			}
			AssetDatabase.CreateAsset(slot, slotFolder + '/' + assetName + '/' + slotName + "_Slot.asset");
			for(int i = 1; i < slot.meshData.subMeshCount; i++)
			{
				string theSlotName = string.Format("{0}_{1}", slotName, i);

				if (i < slotMesh.sharedMaterials.Length && nameByMaterial)
                {
					if (!string.IsNullOrEmpty(slotMesh.sharedMaterials[i].name))
                    {
						string titlecase = slotMesh.sharedMaterials[i].name.ToTitleCase();
						if (!string.IsNullOrWhiteSpace(titlecase))
                        {
							theSlotName = titlecase; 
                        }
					}
                }
				var additionalSlot = ScriptableObject.CreateInstance<SlotDataAsset>();
				additionalSlot.slotName = theSlotName;//  string.Format("{0}_{1}", slotName, i);
				additionalSlot.material = material;
				additionalSlot.UpdateMeshData(finalMeshRenderer,rootBone);
				additionalSlot.subMeshIndex = i;
				AssetDatabase.CreateAsset(additionalSlot, slotFolder + '/' + assetName + '/' + theSlotName +"_Slot.asset");
			}
			AssetDatabase.SaveAssets();
			AssetDatabase.DeleteAsset(SkinnedName);
			AssetDatabase.DeleteAsset(theMesh);
			return slot;
		}

		public static void OptimizeSlotDataMesh(SkinnedMeshRenderer smr, List<int> KeepBonesList)
		{
			if (smr == null) return;
			var mesh = smr.sharedMesh;

			var usedBonesDictionary = CompileUsedBonesDictionary(mesh,KeepBonesList);
			var smrOldBones = smr.bones.Length;
			if (usedBonesDictionary.Count != smrOldBones)
			{
				mesh.SetBoneWeights(mesh.GetBonesPerVertex(),BuildNewBoneWeights(mesh.GetAllBoneWeights(), usedBonesDictionary));
				mesh.bindposes = BuildNewBindPoses(mesh.bindposes, usedBonesDictionary);
				EditorUtility.SetDirty(mesh);
				smr.bones = BuildNewReducedBonesList(smr.bones, usedBonesDictionary);
				EditorUtility.SetDirty(smr);
				Debug.Log(string.Format("Optimized Mesh {0} from {1} bones to {2} bones.", smr.name, smrOldBones, usedBonesDictionary.Count), smr);
			}
		}

		/// <summary>
		/// This needs to generate new BoneWeight1 and new !!! BonesPerVertex !!!
		/// </summary>
		/// <param name="sourceMesh"></param>
		/// <param name="usedBonesDictionary"></param>
		/// <returns></returns>
		private static Mesh BuildNewReduceBonesMesh(Mesh sourceMesh, Dictionary<int, int> usedBonesDictionary)
		{
			Mesh newMesh = GameObject.Instantiate<Mesh>(sourceMesh);
			newMesh.SetBoneWeights(sourceMesh.GetBonesPerVertex(),BuildNewBoneWeights(sourceMesh.GetAllBoneWeights(), usedBonesDictionary));
			newMesh.bindposes = BuildNewBindPoses(sourceMesh.bindposes, usedBonesDictionary);

			return newMesh;
		}

		private static Matrix4x4[] BuildNewBindPoses(Matrix4x4[] bindPoses, Dictionary<int, int> usedBonesDictionary)
		{
			var res = new Matrix4x4[usedBonesDictionary.Count];
			foreach (var entry in usedBonesDictionary)
			{
				res[entry.Value] = bindPoses[entry.Key];
			}
			return res;
		}

		private static NativeArray<BoneWeight1> BuildNewBoneWeights(NativeArray<BoneWeight1> boneWeight, Dictionary<int, int> usedBonesDictionary)
		{
			var newBoneWeights = new BoneWeight1[boneWeight.Length];
			for (int i = 0; i < boneWeight.Length; i++)
			{
				BoneWeight1 bone = boneWeight[i];

				if (usedBonesDictionary.ContainsKey(boneWeight[i].boneIndex))
                {
					bone.boneIndex = usedBonesDictionary[boneWeight[i].boneIndex]; 
                }
				newBoneWeights[i] = bone;
			}
			var weightsArray = new NativeArray<BoneWeight1>(newBoneWeights, Allocator.Temp);
			return weightsArray;
		}

		private static Transform[] BuildNewReducedBonesList(Transform[] bones, Dictionary<int, int> usedBonesDictionary)
		{
			var res = new Transform[usedBonesDictionary.Count];
			foreach (var entry in usedBonesDictionary)
			{
				res[entry.Value] = bones[entry.Key];
			}
			return res;
		}

		private static Dictionary<int, int> CompileUsedBonesDictionary(Mesh resultingMesh, List<int> keepBones)
		{
			var usedBones = new Dictionary<int, int>();
			var boneWeights = resultingMesh.GetAllBoneWeights();

			foreach(int boneIndex in keepBones)
            {
				usedBones.Add(boneIndex, usedBones.Count);
			}
			for (int i = 0; i < boneWeights.Length; i++)
			{
				
				BoneWeight1 boneWeight = boneWeights[i];
				if (boneWeight.weight > 0 && !usedBones.ContainsKey(boneWeight.boneIndex))
				{
					usedBones.Add(boneWeight.boneIndex, usedBones.Count);
				}
			}
			return usedBones;
		}
	}
}
#endif
