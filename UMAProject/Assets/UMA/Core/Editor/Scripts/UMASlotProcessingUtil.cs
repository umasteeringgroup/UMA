#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

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
        public static void UpdateSlotData( SlotDataAsset slot, SkinnedMeshRenderer mesh, UMAMaterial material, SkinnedMeshRenderer prefabMesh, string rootBone)
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
                resultingMesh = SeamRemoval.PerformSeamRemoval(resultingSkinnedMesh, prefabMesh, 0.0001f);
                resultingSkinnedMesh.sharedMesh = resultingMesh;
                SkinnedMeshAligner.AlignBindPose(prefabMesh, resultingSkinnedMesh);
            }
            else
            {
                resultingMesh = (Mesh)GameObject.Instantiate(resultingSkinnedMesh.sharedMesh);
            }

            var usedBonesDictionary = CompileUsedBonesDictionary(resultingMesh);
            if (usedBonesDictionary.Count != resultingSkinnedMesh.bones.Length)
            {
                resultingMesh = BuildNewReduceBonesMesh(resultingMesh, usedBonesDictionary);
            }

			string theMesh = path + '/' + mesh.name + ".asset";

			AssetDatabase.CreateAsset(resultingMesh, theMesh );

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
            var cloth = mesh.GetComponent<Cloth>();
            if (cloth != null)
            {
                slot.meshData.RetrieveDataFromUnityCloth(cloth);
            }
            AssetDatabase.SaveAssets();
			AssetDatabase.DeleteAsset(SkinnedName);
			AssetDatabase.DeleteAsset(theMesh);
		}

		public static SlotDataAsset CreateSlotData(string slotFolder, string assetFolder, string assetName, SkinnedMeshRenderer mesh, UMAMaterial material, SkinnedMeshRenderer prefabMesh, string rootBone, bool binarySerialization = false)
		{
			if (!System.IO.Directory.Exists(slotFolder + '/' + assetFolder))
			{
				System.IO.Directory.CreateDirectory(slotFolder + '/' + assetFolder);
			}

			if (!System.IO.Directory.Exists(slotFolder + '/' + assetName))
			{
				System.IO.Directory.CreateDirectory(slotFolder + '/' + assetName);
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
				resultingMesh = SeamRemoval.PerformSeamRemoval(resultingSkinnedMesh, prefabMesh, 0.0001f);
				resultingSkinnedMesh.sharedMesh = resultingMesh;
				SkinnedMeshAligner.AlignBindPose(prefabMesh, resultingSkinnedMesh);
			}
			else
			{
				resultingMesh = (Mesh)GameObject.Instantiate(resultingSkinnedMesh.sharedMesh);
			}

			var usedBonesDictionary = CompileUsedBonesDictionary(resultingMesh);
			if (usedBonesDictionary.Count != resultingSkinnedMesh.bones.Length)
			{
				resultingMesh = BuildNewReduceBonesMesh(resultingMesh, usedBonesDictionary);
			}

			string theMesh = slotFolder + '/' + assetName + '/' + mesh.name + ".asset";
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
			}

			string SkinnedName = slotFolder + '/' + assetName + '/' + assetName + "_Skinned.prefab";

#if UNITY_2018_3_OR_NEWER
			var skinnedResult = PrefabUtility.SaveAsPrefabAsset(newObject, SkinnedName);
#else
			var skinnedResult = UnityEditor.PrefabUtility.CreatePrefab(SkinnedName, newObject);
#endif
			GameObject.DestroyImmediate(newObject);

			var meshgo = skinnedResult.transform.Find(mesh.name);
			var finalMeshRenderer = meshgo.GetComponent<SkinnedMeshRenderer>();

			var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
			slot.slotName = assetName;
			//Make sure slots get created with a name hash
			slot.nameHash = UMAUtils.StringToHash(slot.slotName);
			slot.material = material;
			slot.UpdateMeshData(finalMeshRenderer,rootBone);
			var cloth = mesh.GetComponent<Cloth>();
			if (cloth != null)
			{
				slot.meshData.RetrieveDataFromUnityCloth(cloth);
			}
			AssetDatabase.CreateAsset(slot, slotFolder + '/' + assetName + '/' + assetName + "_Slot.asset");
			for(int i = 1; i < slot.meshData.subMeshCount; i++)
			{
				var additionalSlot = ScriptableObject.CreateInstance<SlotDataAsset>();
				additionalSlot.slotName = string.Format("{0}_{1}", assetName, i);
				additionalSlot.material = material;
				additionalSlot.UpdateMeshData(finalMeshRenderer,rootBone);
				additionalSlot.subMeshIndex = i;
				AssetDatabase.CreateAsset(additionalSlot, slotFolder + '/' + assetName + '/' + assetName + "_"+ i +"_Slot.asset");
			}
			AssetDatabase.SaveAssets();
			AssetDatabase.DeleteAsset(SkinnedName);
			AssetDatabase.DeleteAsset(theMesh);
			return slot;
		}

		public static void OptimizeSlotDataMesh(SkinnedMeshRenderer smr)
		{
			if (smr == null) return;
			var mesh = smr.sharedMesh;

			var usedBonesDictionary = CompileUsedBonesDictionary(mesh);
			var smrOldBones = smr.bones.Length;
			if (usedBonesDictionary.Count != smrOldBones)
			{
				mesh.boneWeights = BuildNewBoneWeights(mesh.boneWeights, usedBonesDictionary);
				mesh.bindposes = BuildNewBindPoses(mesh.bindposes, usedBonesDictionary);
				EditorUtility.SetDirty(mesh);
				smr.bones = BuildNewReducedBonesList(smr.bones, usedBonesDictionary);
				EditorUtility.SetDirty(smr);
				Debug.Log(string.Format("Optimized Mesh {0} from {1} bones to {2} bones.", smr.name, smrOldBones, usedBonesDictionary.Count), smr);
			}
		}

		private static Mesh BuildNewReduceBonesMesh(Mesh sourceMesh, Dictionary<int, int> usedBonesDictionary)
		{
			Mesh newMesh = GameObject.Instantiate<Mesh>(sourceMesh);
			newMesh.boneWeights = BuildNewBoneWeights(sourceMesh.boneWeights, usedBonesDictionary);
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

		private static BoneWeight[] BuildNewBoneWeights(BoneWeight[] boneWeight, Dictionary<int, int> usedBonesDictionary)
		{
			var res = new BoneWeight[boneWeight.Length];
			for (int i = 0; i < boneWeight.Length; i++)
			{
				UpdateBoneWeight(ref boneWeight[i], ref res[i], usedBonesDictionary);
			}
			return res;
		}

		private static void UpdateBoneWeight(ref BoneWeight source, ref BoneWeight dest, Dictionary<int, int> usedBonesDictionary)
		{
			dest.weight0 = source.weight0;
			dest.weight1 = source.weight1;
			dest.weight2 = source.weight2;
			dest.weight3 = source.weight3;
			dest.boneIndex0 = UpdateBoneIndex(source.boneIndex0, usedBonesDictionary);
			dest.boneIndex1 = UpdateBoneIndex(source.boneIndex1, usedBonesDictionary);
			dest.boneIndex2 = UpdateBoneIndex(source.boneIndex2, usedBonesDictionary);
			dest.boneIndex3 = UpdateBoneIndex(source.boneIndex3, usedBonesDictionary);
		}

		private static int UpdateBoneIndex(int boneIndex, Dictionary<int, int> usedBonesDictionary)
		{
			int res;
			if (usedBonesDictionary.TryGetValue(boneIndex, out res)) return res;
			return 0;
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

		private static Dictionary<int, int> CompileUsedBonesDictionary(Mesh resultingMesh)
		{
			var res = new Dictionary<int, int>();
			var boneWeights = resultingMesh.boneWeights;
			for (int i = 0; i < boneWeights.Length; i++)
			{
				AddBoneWeightToUsedBones(res, ref boneWeights[i]);
			}
			return res;
		}

		private static void AddBoneWeightToUsedBones(Dictionary<int, int> res, ref BoneWeight boneWeight)
		{
			if (boneWeight.weight0 > 0 && !res.ContainsKey(boneWeight.boneIndex0)) res.Add(boneWeight.boneIndex0, res.Count);
			if (boneWeight.weight1 > 0 && !res.ContainsKey(boneWeight.boneIndex1)) res.Add(boneWeight.boneIndex1, res.Count);
			if (boneWeight.weight2 > 0 && !res.ContainsKey(boneWeight.boneIndex2)) res.Add(boneWeight.boneIndex2, res.Count);
			if (boneWeight.weight3 > 0 && !res.ContainsKey(boneWeight.boneIndex3)) res.Add(boneWeight.boneIndex3, res.Count);
		}
	}
}
#endif
