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
#if USE_JOB_COMBINER
		protected List<SkinnedMeshJobCombiner.CombineInstance> combinedMeshList;
#else
		protected List<SkinnedMeshCombiner.CombineInstance> combinedMeshList;
#endif
		protected List<Material> combinedMaterialList;

		UMAData umaData;
		int atlasResolution;
		private UMAClothProperties clothProperties;
		int currentRendererIndex;
		SkinnedMeshRenderer[] renderers;

		protected void EnsureUMADataSetup(UMAData umaData)
		{
			if (umaData.umaRoot != null)
			{
				umaData.CleanMesh(false);
				if (umaData.rendererCount == umaData.generatedMaterials.rendererCount)
				{
					renderers = umaData.GetRenderers();
				}
				else
				{
					var oldRenderers = umaData.GetRenderers();
					var globalTransform = umaData.GetGlobalTransform();

					renderers = new SkinnedMeshRenderer[umaData.generatedMaterials.rendererCount];

					for (int i = 0; i < umaData.generatedMaterials.rendererCount; i++)
					{
						if (oldRenderers != null && oldRenderers.Length > i)
						{
							renderers[i] = oldRenderers[i];
							continue;
						}
						renderers[i] = MakeRenderer(i, globalTransform);
					}

					if (oldRenderers != null)
					{
						for (int i = umaData.generatedMaterials.rendererCount; i < oldRenderers.Length; i++)
						{
							Destroy(oldRenderers[i].gameObject);
							//For cloth, be aware of issue: 845868
							//https://issuetracker.unity3d.com/issues/cloth-repeatedly-destroying-objects-with-cloth-components-causes-a-crash-in-unity-cloth-updatenormals
						}
					}
					umaData.SetRenderers(renderers);
				}
				return;
			}

			if (umaData.umaRoot == null)
			{
				umaData.skeleton = new UMASkeleton();
				umaData.skeleton.Initialize(umaData, umaData.umaRecipe.raceData.TPose);

				renderers = new SkinnedMeshRenderer[umaData.generatedMaterials.rendererCount];

				for (int i = 0; i < umaData.generatedMaterials.rendererCount; i++)
				{
					renderers[i] = MakeRenderer(i, umaData.umaRoot.transform);
				}
				umaData.SetRenderers(renderers);
			}

			//Clear out old cloth components
			for (int i = 0; i < umaData.rendererCount; i++)
			{
				Cloth cloth = renderers[i].GetComponent<Cloth>();
				if (cloth != null)
					DestroyImmediate(cloth,false); //Crashes if trying to use Destroy()
			}
		}

		private SkinnedMeshRenderer MakeRenderer(int i, Transform rootBone)
		{
			GameObject newSMRGO = new GameObject(i == 0 ? "UMARenderer" : ("UMARenderer " + i));
			newSMRGO.transform.parent = umaData.transform;
			newSMRGO.transform.localPosition = Vector3.zero;
			newSMRGO.transform.localRotation = Quaternion.Euler(0, 0, 0f);
			newSMRGO.transform.localScale = Vector3.one;
			newSMRGO.gameObject.layer = umaData.gameObject.layer;

			var newRenderer = newSMRGO.AddComponent<SkinnedMeshRenderer>();
			newRenderer.enabled = false;
			newRenderer.sharedMesh = new Mesh();
			newRenderer.rootBone = rootBone;
			newRenderer.quality = SkinQuality.Bone4;
			newRenderer.sharedMesh.name = i == 0 ? "UMAMesh" : ("UMAMesh " + i);
			return newRenderer;
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

#if USE_JOB_COMBINER
			combinedMeshList = new List<SkinnedMeshJobCombiner.CombineInstance>(umaData.umaRecipe.slotDataList.Length);
#else
			combinedMeshList = new List<SkinnedMeshCombiner.CombineInstance>(umaData.umaRecipe.slotDataList.Length);
#endif
			combinedMaterialList = new List<Material>();

			EnsureUMADataSetup(umaData);
			umaData.skeleton.BeginSkeletonUpdate();

			for (currentRendererIndex = 0; currentRendererIndex < umaData.generatedMaterials.rendererCount; currentRendererIndex++)
			{
				//Move umaMesh creation to with in the renderer loops
				//May want to make sure to set all it's buffers to null instead of creating a new UMAMeshData
				UMAMeshData umaMesh = new UMAMeshData();
				umaMesh.ClaimSharedBuffers();

				umaMesh.subMeshCount = 0;
				umaMesh.vertexCount = 0;

				combinedMeshList.Clear();
				combinedMaterialList.Clear();
				clothProperties = null;

				BuildCombineInstances();

				if (combinedMeshList.Count == 1)
				{
					// fast track
#if USE_JOB_COMBINER
					var tempMesh = SkinnedMeshJobCombiner.ShallowInstanceMesh(combinedMeshList[0].meshData);
#else
					var tempMesh = SkinnedMeshCombiner.ShallowInstanceMesh(combinedMeshList[0].meshData);
#endif
					tempMesh.ApplyDataToUnityMesh(renderers[currentRendererIndex], umaData.skeleton);
				}
				else
				{
#if USE_JOB_COMBINER
					SkinnedMeshJobCombiner.CombineMeshes(umaMesh, combinedMeshList.ToArray(), umaData.skeleton, umaData.blendShapeSettings);
#else
					SkinnedMeshCombiner.CombineMeshes(umaMesh, combinedMeshList.ToArray(), umaData.skeleton, umaData.blendShapeSettings);
#endif
					if (updatedAtlas)
					{
						RecalculateUV(umaMesh);
					}

					umaMesh.ApplyDataToUnityMesh(renderers[currentRendererIndex], umaData.skeleton);
				}
				var cloth = renderers[currentRendererIndex].GetComponent<Cloth>();
				if (clothProperties != null)
				{
					if (cloth != null)
					{
						clothProperties.ApplyValues(cloth);
					}
				}
				else
				{
					UMAUtils.DestroySceneObject(cloth);
				}

				var materials = combinedMaterialList.ToArray();
				renderers[currentRendererIndex].sharedMaterials = materials;
				umaMesh.ReleaseSharedBuffers();
			}

			umaData.umaRecipe.ClearDNAConverters();
			for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
			{
				SlotData slotData = umaData.umaRecipe.slotDataList[i];
				if (slotData != null)
				{
					umaData.umaRecipe.AddDNAUpdater(slotData.asset.slotDNA);
				}
			}

			umaData.firstBake = false;
		}

		protected void BuildCombineInstances()
		{
#if USE_JOB_COMBINER
			SkinnedMeshJobCombiner.CombineInstance combineInstance;
#else
			SkinnedMeshCombiner.CombineInstance combineInstance;
#endif

			//Since BuildCombineInstances is called within a renderer loop, use a variable to keep track of the materialIndex per renderer
			int rendererMaterialIndex = 0;

			for (int materialIndex = 0; materialIndex < umaData.generatedMaterials.materials.Count; materialIndex++)
			{
				var generatedMaterial = umaData.generatedMaterials.materials[materialIndex];
				if (generatedMaterial.renderer != currentRendererIndex)
					continue;
				combinedMaterialList.Add(generatedMaterial.material);

				for (int materialDefinitionIndex = 0; materialDefinitionIndex < generatedMaterial.materialFragments.Count; materialDefinitionIndex++)
				{
					var materialDefinition = generatedMaterial.materialFragments[materialDefinitionIndex];
					var slotData = materialDefinition.slotData;
#if USE_JOB_COMBINER
					combineInstance = new SkinnedMeshJobCombiner.CombineInstance();
#else
					combineInstance = new SkinnedMeshCombiner.CombineInstance();
#endif
					combineInstance.meshData = slotData.asset.meshData;

					//New MeshHiding
					if (slotData.meshHideMask != null)
						combineInstance.triangleMask = slotData.meshHideMask;

					combineInstance.targetSubmeshIndices = new int[combineInstance.meshData.subMeshCount];
					for (int i = 0; i < combineInstance.meshData.subMeshCount; i++)
					{
						combineInstance.targetSubmeshIndices[i] = -1;
					}
					combineInstance.targetSubmeshIndices[slotData.asset.subMeshIndex] = rendererMaterialIndex;
					combinedMeshList.Add(combineInstance);

					if (slotData.asset.SlotAtlassed != null)
					{
						slotData.asset.SlotAtlassed.Invoke(umaData, slotData, generatedMaterial.material, materialDefinition.atlasRegion);
					}
					if (slotData.asset.material.clothProperties != null)
					{
						clothProperties = slotData.asset.material.clothProperties;
					}
				}
				rendererMaterialIndex++;
			}
		}

		protected void RecalculateUV(UMAMeshData umaMesh)
		{
			int idx = 0;
			//Handle Atlassed Verts
			for (int materialIndex = 0; materialIndex < umaData.generatedMaterials.materials.Count; materialIndex++)
			{
				var generatedMaterial = umaData.generatedMaterials.materials[materialIndex];

				if (generatedMaterial.renderer != currentRendererIndex)
					continue;
				
				if (generatedMaterial.umaMaterial.materialType != UMAMaterial.MaterialType.Atlas)
				{
					var fragment = generatedMaterial.materialFragments[0];
					int vertexCount = fragment.slotData.asset.meshData.vertices.Length;
					idx += vertexCount;
					continue;
				}

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
