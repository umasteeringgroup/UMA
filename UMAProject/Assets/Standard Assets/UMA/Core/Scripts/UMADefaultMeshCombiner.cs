using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
	/// <summary>
	/// Default mesh combiner for UMA UMAMeshdata from slots.
	/// </summary>
    public class UMADefaultMeshCombiner : UMAMeshCombiner
    {
        protected List<SkinnedMeshCombiner.CombineInstance> combinedMeshList;
        protected List<Material> combinedMaterialList;

        UMAData umaData;
        int atlasResolution;

		protected void EnsureUMADataSetup(UMAData umaData)
		{
			if (umaData.umaRoot == null)
			{
				GameObject newRoot = new GameObject("Root");
				newRoot.transform.parent = umaData.transform;
				newRoot.transform.localPosition = Vector3.zero;
				newRoot.transform.localRotation = Quaternion.Euler(270f, 0, 0f);
				umaData.umaRoot = newRoot;

				GameObject newGlobal = new GameObject("Global");
				newGlobal.transform.parent = newRoot.transform;
				newGlobal.transform.localPosition = Vector3.zero;
				newGlobal.transform.localRotation = Quaternion.Euler(90f, 90f, 0f);

				umaData.skeleton = new UMASkeleton(newGlobal.transform);

				var newRenderer = umaData.umaRoot.AddComponent<SkinnedMeshRenderer>();
				newRenderer.rootBone = newGlobal.transform;
				umaData.myRenderer = newRenderer;
				umaData.myRenderer.enabled = false;
				umaData.myRenderer.sharedMesh = new Mesh();
			}
			else
			{
				umaData.CleanMesh(false);
			}
		}

		/// <summary>
		/// Updates the UMA mesh and skeleton to match current slots.
		/// </summary>
		/// <param name="updatedAtlas">If set to <c>true</c> atlas has changed.</param>
		/// <param name="umaData">UMA data.</param>
		/// <param name="atlasResolution">Atlas resolution.</param>
        public override void UpdateUMAMesh(bool updatedAtlas, UMAData umaData, int atlasResolution)
        {
            this.umaData = umaData;
            this.atlasResolution = atlasResolution;

            combinedMeshList = new List<SkinnedMeshCombiner.CombineInstance>();
            combinedMaterialList = new List<Material>();

            BuildCombineInstances();

			EnsureUMADataSetup(umaData);
			umaData.skeleton.BeginSkeletonUpdate();

			UMAMeshData umaMesh = new UMAMeshData();
			umaMesh.ClaimSharedBuffers();

			SkinnedMeshCombiner.CombineMeshes(umaMesh, combinedMeshList.ToArray());

            if (updatedAtlas)
            {
				RecalculateUV(umaMesh);
            }

			umaMesh.ApplyDataToUnityMesh(umaData.myRenderer, umaData.skeleton);
			umaMesh.ReleaseSharedBuffers();

            umaData.umaRecipe.ClearDNAConverters();
            for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
            {
                SlotData slotData = umaData.umaRecipe.slotDataList[i];
                if (slotData != null)
                {
//                    umaData.EnsureBoneData(slotData.umaBoneData, slotData.animatedBones, boneMap);

					umaData.umaRecipe.AddDNAUpdater(slotData.asset.slotDNA);
                }
            }

            umaData.myRenderer.quality = SkinQuality.Bone4;
            //umaData.myRenderer.useLightProbes = true;
            var materials = combinedMaterialList.ToArray();
            umaData.myRenderer.sharedMaterials = materials;
            //umaData.myRenderer.sharedMesh.RecalculateBounds();
            umaData.myRenderer.sharedMesh.name = "UMAMesh";

            umaData.firstBake = false;

            //FireSlotAtlasNotification(umaData, materials);
        }

		//private void FireSlotAtlasNotification(UMAData umaData, Material[] materials)
		//{
		//    for (int atlasIndex = 0; atlasIndex < umaData.atlasList.atlas.Count; atlasIndex++)
		//    {
		//        for (int materialDefinitionIndex = 0; materialDefinitionIndex < umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; materialDefinitionIndex++)
		//        {
		//            var materialDefinition = umaData.atlasList.atlas[atlasIndex].atlasMaterialDefinitions[materialDefinitionIndex];
		//            var slotData = materialDefinition.source.slotData;
		//            if (slotData.SlotAtlassed != null)
		//            {
		//                slotData.SlotAtlassed.Invoke(umaData, slotData, materials[atlasIndex], materialDefinition.atlasRegion);
		//            }
		//        }
		//    }
		//    SlotData[] slots = umaData.umaRecipe.slotDataList;
		//    for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
		//    {
		//        var slotData = slots[slotIndex];
		//        if (slotData == null) continue;
		//        if (slotData.textureNameList.Length == 1 && string.IsNullOrEmpty(slotData.textureNameList[0]))
		//        {
		//            if (slotData.SlotAtlassed != null)
		//            {
		//                slotData.SlotAtlassed.Invoke(umaData, slotData, materials[atlasIndex], materialDefinition.atlasRegion);
		//            }
		//        }
		//    }
		//}

        protected void BuildCombineInstances()
        {
            SkinnedMeshCombiner.CombineInstance combineInstance;

            for (int materialIndex = 0; materialIndex < umaData.generatedMaterials.materials.Count; materialIndex++)
            {
				var generatedMaterial = umaData.generatedMaterials.materials[materialIndex];
				combinedMaterialList.Add(generatedMaterial.material);

				for (int materialDefinitionIndex = 0; materialDefinitionIndex < generatedMaterial.materialFragments.Count; materialDefinitionIndex++)
                {
					var materialDefinition = generatedMaterial.materialFragments[materialDefinitionIndex];
					var slotData = materialDefinition.slotData;
                    combineInstance = new SkinnedMeshCombiner.CombineInstance();
					combineInstance.meshData = slotData.asset.meshData;
					combineInstance.targetSubmeshIndices = new int[combineInstance.meshData.subMeshCount];
					for (int i = 0; i < combineInstance.meshData.subMeshCount; i++)
					{
						combineInstance.targetSubmeshIndices[i] = -1;
					}
					combineInstance.targetSubmeshIndices[slotData.asset.subMeshIndex] = materialIndex;
                    combinedMeshList.Add(combineInstance);

					if (slotData.asset.SlotAtlassed != null)
					{
						slotData.asset.SlotAtlassed.Invoke(umaData, slotData, generatedMaterial.material, materialDefinition.atlasRegion);
					}
                }
            }
        }

		protected void RecalculateUV(UMAMeshData umaMesh)
        {
            int idx = 0;
            //Handle Atlassed Verts
            for (int materialIndex = 0; materialIndex < umaData.generatedMaterials.materials.Count; materialIndex++)
            {
				var generatedMaterial = umaData.generatedMaterials.materials[materialIndex];
				if (generatedMaterial.umaMaterial.materialType != UMAMaterial.MaterialType.Atlas) continue;

				for (int materialDefinitionIndex = 0; materialDefinitionIndex < generatedMaterial.materialFragments.Count; materialDefinitionIndex++)
                {
					var fragment = generatedMaterial.materialFragments[materialDefinitionIndex];
					var tempAtlasRect = fragment.atlasRegion;
					int vertexCount = fragment.slotData.asset.meshData.vertices.Length;
					float atlasXMin = tempAtlasRect.xMin / atlasResolution;
					float atlasXMax = tempAtlasRect.xMax / atlasResolution;
					float atlasXRange = atlasXMax - atlasXMin;
					float atlasYMin = tempAtlasRect.yMin / atlasResolution;
					float atlasYMax = tempAtlasRect.yMax / atlasResolution;
					float atlasYRange = atlasYMax - atlasYMin;
					while (vertexCount-- > 0)
                    {
						umaMesh.uv[idx].x = atlasXMin + atlasXRange * umaMesh.uv[idx].x;
						umaMesh.uv[idx].y = atlasYMin + atlasYRange * umaMesh.uv[idx].y;
						idx++;
                    }

                }
            }
        }
	}
}
