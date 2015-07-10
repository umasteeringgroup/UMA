using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

namespace UMA
{
	/// <summary>
	/// Utility class for generating texture atlases
	/// </summary>
	[Serializable]
	public class UMAGeneratorCoroutine : WorkerCoroutine
	{
		TextureProcessBaseCoroutine textureProcessCoroutine;

		MaxRectsBinPack packTexture;

		UMAGeneratorBase umaGenerator;
		UMAData umaData;
		Texture[] backUpTexture;
		bool updateMaterialList;

		MaterialDefinitionComparer comparer = new MaterialDefinitionComparer();
		List<UMAData.GeneratedMaterial> generatedMaterials;
		List<UMAData.GeneratedMaterial> atlassedMaterials = new List<UMAData.GeneratedMaterial>(20);
		Dictionary<List<OverlayData>, UMAData.GeneratedMaterial> generatedMaterialLookup;

		public void Prepare(UMAGeneratorBase _umaGenerator, UMAData _umaData, TextureProcessBaseCoroutine textureProcessCoroutine, bool updateMaterialList)
		{
			umaGenerator = _umaGenerator;
			umaData = _umaData;
			this.textureProcessCoroutine = textureProcessCoroutine;
			this.updateMaterialList = updateMaterialList;
		}

		private UMAData.GeneratedMaterial FindOrCreateGeneratedMaterial(UMAMaterial umaMaterial)
		{
			if (umaMaterial.materialType != UMAMaterial.MaterialType.Atlas)
			{
				var res = new UMAData.GeneratedMaterial();
				res.umaMaterial = umaMaterial;
				res.material = UnityEngine.Object.Instantiate(umaMaterial.material) as Material;
				res.material.name = umaMaterial.material.name;
				generatedMaterials.Add(res);
				return res;
			}
			else
			{
				foreach (var atlassedMaterial in atlassedMaterials)
				{
					if (atlassedMaterial.umaMaterial == umaMaterial)
					{
						return atlassedMaterial;
					}
				}
				
				var res = new UMAData.GeneratedMaterial();
				res.umaMaterial = umaMaterial;
				res.material = UnityEngine.Object.Instantiate(umaMaterial.material) as Material;
				res.material.name = umaMaterial.material.name;
				atlassedMaterials.Add(res);
				generatedMaterials.Add(res);
				return res;
			}
		}

		protected override void Start()
		{
			if (generatedMaterialLookup == null)
			{
				generatedMaterialLookup = new Dictionary<List<OverlayData>, UMAData.GeneratedMaterial>(20);
			}
			else
			{
				generatedMaterialLookup.Clear();
			}
			backUpTexture = umaData.backUpTextures();
			umaData.CleanTextures();
			generatedMaterials = new List<UMAData.GeneratedMaterial>(20);
			atlassedMaterials.Clear();

			SlotData[] slots = umaData.umaRecipe.slotDataList;
			for (int i = 0; i < slots.Length; i++)
			{
				var slot = slots[i];
				if (slot == null) continue;
				if (slot.asset.material != null && slot.GetOverlay(0) != null)
				{
					var overlayList = slot.GetOverlayList();
					UMAData.GeneratedMaterial generatedMaterial;
					if (!generatedMaterialLookup.TryGetValue(overlayList, out generatedMaterial))
					{
						generatedMaterial = FindOrCreateGeneratedMaterial(slots[i].asset.material);
						generatedMaterialLookup.Add(overlayList, generatedMaterial);
					}
					var tempMaterialDefinition = new UMAData.MaterialFragment();
					tempMaterialDefinition.baseTexture = slots[i].GetOverlay(0).asset.textureList;
					tempMaterialDefinition.size = tempMaterialDefinition.baseTexture[0].width * tempMaterialDefinition.baseTexture[0].height;
					tempMaterialDefinition.baseColor = slots[i].GetOverlay(0).colorData.color;
					tempMaterialDefinition.umaMaterial = slots[i].asset.material;
					int overlays = 0;
					for (int overlayCounter = 0; overlayCounter < slots[i].OverlayCount; overlayCounter++)
					{
						var overlay = slots[i].GetOverlay(overlayCounter);
						if (overlay != null)
						{
							overlays++;
						}
					}

					tempMaterialDefinition.overlays = new UMAData.textureData[overlays - 1];
					tempMaterialDefinition.overlayColors = new Color32[overlays - 1];
					tempMaterialDefinition.rects = new Rect[overlays - 1];
					tempMaterialDefinition.overlayData = new OverlayData[overlays];
					tempMaterialDefinition.channelMask = new Color[overlays][];
					tempMaterialDefinition.channelAdditiveMask = new Color[overlays][];
					tempMaterialDefinition.overlayData[0] = slots[i].GetOverlay(0);
					tempMaterialDefinition.channelMask[0] = slots[i].GetOverlay(0).colorData.channelMask;
					tempMaterialDefinition.channelAdditiveMask[0] = slots[i].GetOverlay(0).colorData.channelAdditiveMask;
					tempMaterialDefinition.slotData = slots[i];

					int overlayID = 0;
					for (int overlayCounter = 0; overlayCounter < slots[i].OverlayCount - 1; overlayCounter++)
					{
						var overlay = slots[i].GetOverlay(overlayCounter + 1);
						if (overlay == null) continue;
						tempMaterialDefinition.overlays[overlayID] = new UMAData.textureData();
						tempMaterialDefinition.rects[overlayID] = overlay.rect;
						tempMaterialDefinition.overlays[overlayID].textureList = overlay.asset.textureList;
						tempMaterialDefinition.overlayColors[overlayID] = overlay.colorData.color;
						tempMaterialDefinition.channelMask[overlayID + 1] = overlay.colorData.channelMask;
						tempMaterialDefinition.channelAdditiveMask[overlayID + 1] = overlay.colorData.channelAdditiveMask;
						tempMaterialDefinition.overlayData[overlayID + 1] = overlay;
						overlayID++;
					}

					tempMaterialDefinition.overlayList = slots[i].GetOverlayList();
					tempMaterialDefinition.isRectShared = false;
					for (int j = 0; j < generatedMaterial.materialFragments.Count; j++)
					{
						if (generatedMaterial.materialFragments[j].overlayList == tempMaterialDefinition.overlayList)
						{
							tempMaterialDefinition.isRectShared = true;
							tempMaterialDefinition.rectFragment = generatedMaterial.materialFragments[j];
							break;
						}
					}
					generatedMaterial.materialFragments.Add(tempMaterialDefinition);
				}
			}

			packTexture = new MaxRectsBinPack(umaGenerator.atlasResolution, umaGenerator.atlasResolution, false);
		}

		public class MaterialDefinitionComparer : IComparer<UMAData.MaterialFragment>
		{
			public int Compare (UMAData.MaterialFragment x, UMAData.MaterialFragment y)
			{
				return y.size - x.size;
			}
		}
		protected override IEnumerator workerMethod()
		{
			umaData.generatedMaterials = new UMAData.GeneratedMaterials();
			umaData.generatedMaterials.materials = generatedMaterials;

			GenerateAtlasData();
			OptimizeAtlas();

			textureProcessCoroutine.Prepare(umaData, umaGenerator);
			yield return textureProcessCoroutine;

			CleanBackUpTextures();
			UpdateUV();

			if (updateMaterialList)
			{
				var mats = umaData.myRenderer.sharedMaterials;
				var atlasses = umaData.generatedMaterials.materials;
				for (int i = 0; i < atlasses.Count; i++)
				{
					UnityEngine.Object.Destroy(mats[i]);
					mats[i] = atlasses[i].material;
				}
				umaData.myRenderer.sharedMaterials = new List<Material>(mats).ToArray();
			}
		}

		protected override void Stop()
		{

		}

		private void CleanBackUpTextures()
		{
			for (int textureIndex = 0; textureIndex < backUpTexture.Length; textureIndex++)
			{
				if (backUpTexture[textureIndex] != null)
				{
					Texture tempTexture = backUpTexture[textureIndex];
					if (tempTexture is RenderTexture)
					{
						RenderTexture tempRenderTexture = tempTexture as RenderTexture;
						tempRenderTexture.Release();
						UnityEngine.Object.Destroy(tempRenderTexture);
						tempRenderTexture = null;
					}
					else
					{
						UnityEngine.Object.Destroy(tempTexture);
					}
					backUpTexture[textureIndex] = null;
				}
			}
		}

		private void GenerateAtlasData()
		{
			for(int i = 0; i < atlassedMaterials.Count; i++)
			{
				var generatedMaterial = atlassedMaterials[i];
				generatedMaterial.materialFragments.Sort(comparer);
				generatedMaterial.resolutionScale = 1f;
				generatedMaterial.cropResolution = new Vector2(umaGenerator.atlasResolution, umaGenerator.atlasResolution);
				while (!CalculateRects(generatedMaterial))
				{
					generatedMaterial.resolutionScale = generatedMaterial.resolutionScale * 0.5f;
				}
				UpdateSharedRect(generatedMaterial);
			}
		}

		private void UpdateSharedRect(UMAData.GeneratedMaterial generatedMaterial)
		{
			for (int i = 0; i < generatedMaterial.materialFragments.Count; i++)
			{
				var fragment = generatedMaterial.materialFragments[i];
				if (fragment.isRectShared)
				{
					fragment.atlasRegion = fragment.rectFragment.atlasRegion;
				}
			}
		}


		private bool CalculateRects(UMAData.GeneratedMaterial material)
		{
			Rect nullRect = new Rect(0, 0, 0, 0);
			packTexture.Init(umaGenerator.atlasResolution, umaGenerator.atlasResolution, false);

			for (int atlasElementIndex = 0; atlasElementIndex < material.materialFragments.Count; atlasElementIndex++)
			{
				var tempMaterialDef = material.materialFragments[atlasElementIndex];
				if( tempMaterialDef.isRectShared ) continue;

				tempMaterialDef.atlasRegion = packTexture.Insert(Mathf.FloorToInt(tempMaterialDef.baseTexture[0].width * material.resolutionScale * tempMaterialDef.slotData.overlayScale), Mathf.FloorToInt(tempMaterialDef.baseTexture[0].height * material.resolutionScale * tempMaterialDef.slotData.overlayScale), MaxRectsBinPack.FreeRectChoiceHeuristic.RectBestLongSideFit);

				if (tempMaterialDef.atlasRegion == nullRect)
				{
					if (umaGenerator.fitAtlas)
					{
						Debug.LogWarning("Atlas resolution is too small, Textures will be reduced.", umaData.gameObject);
						return false;
					}
					else
					{
						Debug.LogError("Atlas resolution is too small, not all textures will fit.", umaData.gameObject);
					}
				}
			}
			return true;
		}

		private void OptimizeAtlas()
		{
			for (int atlasIndex = 0; atlasIndex < atlassedMaterials.Count; atlasIndex++)
			{
				var material = atlassedMaterials[atlasIndex];
				Vector2 usedArea = new Vector2(0, 0);
				for (int atlasElementIndex = 0; atlasElementIndex < material.materialFragments.Count; atlasElementIndex++)
				{
					if (material.materialFragments[atlasElementIndex].atlasRegion.xMax > usedArea.x)
					{
						usedArea.x = material.materialFragments[atlasElementIndex].atlasRegion.xMax;
					}

					if (material.materialFragments[atlasElementIndex].atlasRegion.yMax > usedArea.y)
					{
						usedArea.y = material.materialFragments[atlasElementIndex].atlasRegion.yMax;
					}
				}

				Vector2 tempResolution = new Vector2(umaGenerator.atlasResolution, umaGenerator.atlasResolution);

				bool done = false;
				while (!done)
				{
					if (tempResolution.x * 0.5f >= usedArea.x)
					{
						tempResolution = new Vector2(tempResolution.x * 0.5f, tempResolution.y);
					}
					else
					{
						done = true;
					}
				}

				done = false;
				while (!done)
				{

					if (tempResolution.y * 0.5f >= usedArea.y)
					{
						tempResolution = new Vector2(tempResolution.x, tempResolution.y * 0.5f);
					}
					else
					{
						done = true;
					}
				}

				material.cropResolution = tempResolution;
			}
		}

		private void UpdateUV()
		{
			UMAData.GeneratedMaterials umaAtlasList = umaData.generatedMaterials;

			for (int atlasIndex = 0; atlasIndex < umaAtlasList.materials.Count; atlasIndex++)
			{
				Vector2 finalAtlasAspect = new Vector2(umaGenerator.atlasResolution / umaAtlasList.materials[atlasIndex].cropResolution.x, umaGenerator.atlasResolution / umaAtlasList.materials[atlasIndex].cropResolution.y);

				for (int atlasElementIndex = 0; atlasElementIndex < umaAtlasList.materials[atlasIndex].materialFragments.Count; atlasElementIndex++)
				{
					Rect tempRect = umaAtlasList.materials[atlasIndex].materialFragments[atlasElementIndex].atlasRegion;
					tempRect.xMin = tempRect.xMin * finalAtlasAspect.x;
					tempRect.xMax = tempRect.xMax * finalAtlasAspect.x;
					tempRect.yMin = tempRect.yMin * finalAtlasAspect.y;
					tempRect.yMax = tempRect.yMax * finalAtlasAspect.y;
					umaAtlasList.materials[atlasIndex].materialFragments[atlasElementIndex].atlasRegion = tempRect;
				}
			}
		}
	}
}
