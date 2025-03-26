#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Unity.Collections;
using UMA.CharacterSystem;
using System;

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
			int subMesh = slot.subMeshIndex;
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
				if (calcTangents)
				{
					resultingMesh.RecalculateTangents();
				}
            }

			//CountBoneweights(resultingMesh);

            var usedBonesDictionary = CompileUsedBonesDictionary(resultingMesh, new List<int>());
            if (usedBonesDictionary.Count != resultingSkinnedMesh.bones.Length)
            {
                resultingMesh = BuildNewReduceBonesMesh(resultingMesh, usedBonesDictionary);
            }

			//CountBoneweights(resultingMesh);

			string meshAssetName = path + '/' + mesh.name + "_TempMesh.asset";

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

			string SkinnedName = path + '/' + assetName + "_TempSkinned.prefab";

			Debug.Log($"Saving prefab to {SkinnedName}");
            var skinnedResult = PrefabUtility.SaveAsPrefabAsset(newObject, SkinnedName);

#if false
			GameObject.DestroyImmediate(newObject);
#endif
            var meshgo = skinnedResult.transform.Find(mesh.name);
            var finalMeshRenderer = meshgo.GetComponent<SkinnedMeshRenderer>();

            slot.UpdateMeshData(finalMeshRenderer,rootBone, false, subMesh);
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

		public static SlotDataAsset CreateSlotData(SlotBuilderParameters sbp)
		//public static SlotDataAsset CreateSlotData(string slotFolder, string assetFolder, string assetName, string slotName, bool nameByMaterial, SkinnedMeshRenderer slotMesh, UMAMaterial material, SkinnedMeshRenderer seamsMesh, List<string> KeepList, string rootBone, bool binarySerialization = false, bool calcTangents = true, string stripBones = "", bool useRootFolder = false, bool adustForUDIM)
		{
			if (sbp.useRootFolder)
			{
				if (!System.IO.Directory.Exists(sbp.slotFolder))
				{
					System.IO.Directory.CreateDirectory(sbp.slotFolder);
				}
			}
			else
			{
				if (!System.IO.Directory.Exists(sbp.slotFolder + '/' + sbp.assetFolder))
				{
					System.IO.Directory.CreateDirectory(sbp.slotFolder + '/' + sbp.assetFolder);
				}

				if (!System.IO.Directory.Exists(sbp.slotFolder + '/' + sbp.assetName))
				{
					System.IO.Directory.CreateDirectory(sbp.slotFolder + '/' + sbp.assetName);
				}
			}

			GameObject tempGameObject = UnityEngine.Object.Instantiate(sbp.slotMesh.transform.parent.gameObject) as GameObject;

			var resultingSkinnedMeshes = tempGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
			SkinnedMeshRenderer resultingSkinnedMesh = null;
			foreach (var skinnedMesh in resultingSkinnedMeshes)
			{
				if (skinnedMesh.name == sbp.slotMesh.name)
				{
					resultingSkinnedMesh = skinnedMesh;
				}
			}

			Transform[] bones = resultingSkinnedMesh.bones;
			List<int> KeepBoneIndexes = new List<int>();

			int startBone = sbp.keepAllBones ? 1 : 0;
			for (int i = startBone; i < bones.Length; i++)
			{
				Transform t = bones[i];
				if (sbp.keepList.Contains(t.name) || sbp.keepAllBones)
				{
					if (!string.IsNullOrEmpty(t.name))
					{
						KeepBoneIndexes.Add(i);
					}
				}
			}


			Mesh resultingMesh;
			if (sbp.seamsMesh != null)
			{
				resultingMesh = SeamRemoval.PerformSeamRemoval(resultingSkinnedMesh, sbp.seamsMesh, 0.0001f, sbp.calculateTangents);
				resultingSkinnedMesh.sharedMesh = resultingMesh;
				SkinnedMeshAligner.AlignBindPose(sbp.seamsMesh, resultingSkinnedMesh);
			}
			else
			{
				resultingMesh = (Mesh)GameObject.Instantiate(resultingSkinnedMesh.sharedMesh);
			}
			if (sbp.calculateTangents)
			{
				resultingMesh.RecalculateTangents();
			}

			var usedBonesDictionary = CompileUsedBonesDictionary(resultingMesh, KeepBoneIndexes);
			if (usedBonesDictionary.Count != resultingSkinnedMesh.bones.Length)
			{
				resultingMesh = BuildNewReduceBonesMesh(resultingMesh, usedBonesDictionary);
			}

			string theMesh = sbp.slotFolder + '/' + sbp.assetName + '/' + sbp.slotMesh.name + "_TempMesh.asset";
			if (sbp.useRootFolder)
			{
				theMesh = sbp.slotFolder + '/' + sbp.slotMesh.name + "_TempMesh.asset";
			}
			if (sbp.binarySerialization)
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

			tempGameObject.name = sbp.slotMesh.transform.parent.gameObject.name;
			Transform[] transformList = tempGameObject.GetComponentsInChildren<Transform>();

			GameObject newObject = new GameObject();

			for (int i = 0; i < transformList.Length; i++)
			{
				if (!string.IsNullOrEmpty(sbp.stripBones))
				{
					string bname = transformList[i].name;
					if (bname.Contains(sbp.stripBones))
					{
						bname = bname.Replace(sbp.stripBones, "");
					}
					transformList[i].name = bname;
				}
				if (transformList[i].name == sbp.rootBone)
				{
					transformList[i].parent = newObject.transform;
				}
				else if (transformList[i].name == sbp.slotMesh.name)
				{
					transformList[i].parent = newObject.transform;
				}
			}

			resultingSkinnedMesh = newObject.GetComponentInChildren<SkinnedMeshRenderer>();
			if (resultingSkinnedMesh == null)
			{
				Debug.Log("Skinned mesh is null!!!");
				return null;
			}

			if (usedBonesDictionary.Count != resultingSkinnedMesh.bones.Length)
			{

				resultingSkinnedMesh.bones = BuildNewReducedBonesList(resultingSkinnedMesh.bones, usedBonesDictionary);
			}
			resultingSkinnedMesh.sharedMesh = resultingMesh;

			string SkinnedName = sbp.slotFolder + '/' + sbp.assetName + '/' + sbp.assetName + "_TempSkinned.prefab";

			if (sbp.useRootFolder)
			{
				SkinnedName = sbp.slotFolder + '/' + sbp.assetName + "_TempSkinned.prefab";
			}

			var skinnedResult = PrefabUtility.SaveAsPrefabAsset(newObject, SkinnedName,out bool success);
			if (!success)
			{
				Debug.Log($"failed saving {SkinnedName} prefab"); 
			}
			var meshgo = skinnedResult.transform.Find(sbp.slotMesh.name);
			var finalMeshRenderer = meshgo.GetComponent<SkinnedMeshRenderer>();
			if (finalMeshRenderer.sharedMesh == null)
			{
				Debug.Log("Final Mesh Renderer shareMesh is null!!!");
				finalMeshRenderer.sharedMesh = resultingMesh;
			}

			var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
			slot.slotName = sbp.slotName;
			//Make sure slots get created with a name hash
			slot.nameHash = UMAUtils.StringToHash(slot.slotName);
			slot.material = sbp.material;
			try
			{
				slot.UpdateMeshData(finalMeshRenderer, sbp.rootBone, sbp.udimAdjustment, 0);
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				return null;
			}
			TransformMeshData(slot, sbp);

			var cloth = sbp.slotMesh.GetComponent<Cloth>();
			if (cloth != null)
			{
				slot.meshData.RetrieveDataFromUnityCloth(cloth);
			}
			string slotPath = sbp.slotFolder + '/' + sbp.assetName + '/' + sbp.slotName + "_slot.asset";
			if (sbp.useRootFolder)
			{
				slotPath = sbp.slotFolder + '/' + sbp.slotName + "_slot.asset";
			}
			AssetDatabase.CreateAsset(slot, slotPath);
			for (int i = 1; i < finalMeshRenderer.sharedMesh.subMeshCount; i++)
			{
				string theSlotName = string.Format("{0}_{1}", sbp.slotName, i);

				if (i < sbp.slotMesh.sharedMaterials.Length && sbp.nameByMaterial)
				{
					if (!string.IsNullOrEmpty(sbp.slotMesh.sharedMaterials[i].name))
					{
						string titlecase = sbp.slotMesh.sharedMaterials[i].name.ToTitleCase();
						if (!string.IsNullOrWhiteSpace(titlecase))
						{
							theSlotName = titlecase;
						}
					}
				}
				var additionalSlot = ScriptableObject.CreateInstance<SlotDataAsset>();
				additionalSlot.slotName = theSlotName;//  string.Format("{0}_{1}", slotName, i);
				additionalSlot.material = sbp.material;
				additionalSlot.UpdateMeshData(finalMeshRenderer, sbp.rootBone, sbp.udimAdjustment, i);
				TransformMeshData(additionalSlot, sbp);

				//additionalSlot.subMeshIndex = i; 

				string theSlotPath = sbp.slotFolder + '/' + sbp.assetName + '/' + theSlotName + "_slot.asset";
				if (sbp.useRootFolder)
				{
					theSlotPath = sbp.slotFolder + '/' + theSlotName + "_slot.asset";
				}

				AssetDatabase.CreateAsset(additionalSlot, theSlotPath);
			}
			AssetDatabase.SaveAssets();
			GameObject.DestroyImmediate(tempGameObject);
			GameObject.DestroyImmediate(newObject);

			AssetDatabase.DeleteAsset(SkinnedName);
			AssetDatabase.DeleteAsset(theMesh);
			return slot;
		}

        private static void TransformMeshData(SlotDataAsset slot, SlotBuilderParameters sbp)
		{
            var meshData = slot.meshData;
			var Vertices = meshData.vertices;
			Vector3[] newVerts = new Vector3[meshData.vertices.Length];
			for (int i=0; i < Vertices.Length; i++)
            {
				if (sbp.rotationEnabled)
				{
					newVerts[i] = sbp.rotation * Vertices[i];
				}
				else
				{
					newVerts[i] = DoInversions(sbp, Vertices[i]);
                }
            }
			slot.meshData.vertices = newVerts;
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

		private static Vector3 DoInversions(SlotBuilderParameters sbp, Vector3 inVector)
		{
			float x = sbp.invertX ? -inVector.x : inVector.x;
            float y = sbp.invertY ? -inVector.y : inVector.y;
            float z = sbp.invertZ ? -inVector.z : inVector.z;
            return new Vector3(x, y, z);
		}
	}
}
#endif
