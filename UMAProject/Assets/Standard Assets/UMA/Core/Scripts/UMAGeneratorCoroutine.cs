using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
	public class UMAGeneratorCoroutine : WorkerCoroutine
	{
		TextureProcessBaseCoroutine textureProcessCoroutine;

		MaxRectsBinPack packTexture;

		List<UMAData.MaterialDefinition> materialDefinitionList;
		List<UMAData.AtlasMaterialDefinition> atlasMaterialDefinitionList;

		UMAGeneratorBase umaGenerator;
		UMAData umaData;
		Texture[] backUpTexture;
		bool updateMaterialList;
		MaterialDefinitionComparer comparer = new MaterialDefinitionComparer();

		public void Prepare(UMAGeneratorBase _umaGenerator, UMAData _umaData, TextureProcessBaseCoroutine textureProcessCoroutine, bool updateMaterialList)
		{
			umaGenerator = _umaGenerator;
			umaData = _umaData;
			this.textureProcessCoroutine = textureProcessCoroutine;
			this.updateMaterialList = updateMaterialList;
		}

		protected override void Start()
		{
			backUpTexture = umaData.backUpTextures();
			umaData.cleanTextures();

			materialDefinitionList = new List<UMAData.MaterialDefinition>();

			//Update atlas area can be handled here
			UMAData.MaterialDefinition tempMaterialDefinition = new UMAData.MaterialDefinition();

			SlotData[] slots = umaData.umaRecipe.slotDataList;
			for (int i = 0; i < slots.Length; i++)
			{
				if (slots[i] == null) continue;
				if (slots[i].asset.textureNameList.Length == 1 && string.IsNullOrEmpty(slots[i].asset.textureNameList[0]))
				{
					continue;
				}
				var requiredTextures = slots[i].asset.textureNameList.Length == 0 ? umaGenerator.textureNameList.Length : slots[i].asset.textureNameList.Length;

				if (slots[i].GetOverlay(0) != null)
				{
					tempMaterialDefinition = new UMAData.MaterialDefinition();
					tempMaterialDefinition.baseTexture = slots[i].GetOverlay(0).asset.textureList;
					tempMaterialDefinition.size = tempMaterialDefinition.baseTexture[0].width * tempMaterialDefinition.baseTexture[0].height;
					tempMaterialDefinition.baseColor = slots[i].GetOverlay(0).colorData.color;
					tempMaterialDefinition.materialSample = slots[i].asset.materialSample;
					int overlays = 0;
					for (int overlayCounter = 0; overlayCounter < slots[i].OverlayCount; overlayCounter++)
					{
						var overlay = slots[i].GetOverlay(overlayCounter);
						if (overlay != null)
						{
							overlays++;
							if (overlay.useAdvancedMasks)
							{
								overlay.EnsureChannels(slots[i].asset.textureNameList.Length);
							}
						}
					}

					tempMaterialDefinition.overlays = new UMAData.textureData[overlays - 1];
					tempMaterialDefinition.overlayColors = new Color32[tempMaterialDefinition.overlays.Length];
					tempMaterialDefinition.rects = new Rect[tempMaterialDefinition.overlays.Length];
					tempMaterialDefinition.channelMask = new Color32[tempMaterialDefinition.overlays.Length + 1][];
					tempMaterialDefinition.channelAdditiveMask = new Color32[tempMaterialDefinition.overlays.Length + 1][];
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
						if (overlay.asset.textureList.Length >= requiredTextures)
						{
							tempMaterialDefinition.overlays[overlayID].textureList = overlay.asset.textureList;
						}
						else
						{
							tempMaterialDefinition.overlays[overlayID].textureList = new Texture[requiredTextures];
							System.Array.Copy(overlay.asset.textureList, tempMaterialDefinition.overlays[overlayID].textureList, overlay.asset.textureList.Length);
						}
						tempMaterialDefinition.overlayColors[overlayID] = overlay.colorData.color;
						tempMaterialDefinition.channelMask[overlayID + 1] = overlay.colorData.channelMask;
						tempMaterialDefinition.channelAdditiveMask[overlayID + 1] = overlay.colorData.channelAdditiveMask;
						overlayID++;
					}

					materialDefinitionList.Add(tempMaterialDefinition);
				}
			}

			packTexture = new MaxRectsBinPack(umaGenerator.atlasResolution, umaGenerator.atlasResolution, false);
		}

		public class MaterialDefinitionComparer : IComparer<UMAData.MaterialDefinition>
		{
			public int Compare (UMAData.MaterialDefinition x, UMAData.MaterialDefinition y)
			{
				return y.size - x.size;
			}
		}
		protected override IEnumerator workerMethod()
		{
			materialDefinitionList.Sort(comparer);

			umaData.atlasList = new UMAData.AtlasList();
			umaData.atlasList.atlas = new List<UMAData.AtlasElement>();

			GenerateAtlasData();
			CalculateRects();
			if (umaGenerator.AtlasCrop)
			{
				OptimizeAtlas();
			}

			textureProcessCoroutine.Prepare(umaData, umaGenerator);
			yield return textureProcessCoroutine;

			CleanBackUpTextures();
			UpdateUV();

			if (updateMaterialList)
			{
				var mats = umaData.myRenderer.sharedMaterials;
				var atlasses = umaData.atlasList.atlas;
				for (int i = 0; i < atlasses.Count; i++)
				{
					Object.Destroy(mats[i]);
					mats[i] = atlasses[i].materialSample;
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
			for (int i = 0; i < materialDefinitionList.Count; i++)
			{
				var materialDefinition = materialDefinitionList[i];
				if( materialDefinition != null )
				{
					atlasMaterialDefinitionList = new List<UMAData.AtlasMaterialDefinition>();
					var atlasElement = new UMAData.AtlasElement();
					atlasElement.materialSample = materialDefinition.materialSample;
					var tempAtlasMaterialDefinition = new UMAData.AtlasMaterialDefinition();
					tempAtlasMaterialDefinition.source = materialDefinition;
					atlasMaterialDefinitionList.Add(tempAtlasMaterialDefinition);
					//All slots sharing same material are on same atlasElement
					atlasElement.atlasMaterialDefinitions = atlasMaterialDefinitionList;

					umaData.atlasList.atlas.Add(atlasElement);

					for (int i2 = i+1; i2 < materialDefinitionList.Count; i2++)
					{
						//Look for same material
						var materialDefinition2 = materialDefinitionList[i2];
						if (materialDefinition2 != null)
						{
							if (materialDefinition.materialSample == materialDefinition2.materialSample)
							{
								tempAtlasMaterialDefinition = new UMAData.AtlasMaterialDefinition();
								tempAtlasMaterialDefinition.source = materialDefinition2;
								atlasMaterialDefinitionList.Add(tempAtlasMaterialDefinition);
								materialDefinitionList[i2] = null;
							}
						}
					}
					materialDefinitionList[i] = null;
				}
			}
		}


		private void CalculateRects()
		{
			Rect nullRect = new Rect(0, 0, 0, 0);
			UMAData.AtlasList umaAtlasList = umaData.atlasList;


			for (int atlasIndex = 0; atlasIndex < umaAtlasList.atlas.Count; atlasIndex++)
			{
				umaAtlasList.atlas[atlasIndex].cropResolution = new Vector2(umaGenerator.atlasResolution, umaGenerator.atlasResolution);
				umaAtlasList.atlas[atlasIndex].resolutionScale = 1f;
				packTexture.Init(umaGenerator.atlasResolution, umaGenerator.atlasResolution, false);
				bool textureFit = true;

				for (int atlasElementIndex = 0; atlasElementIndex < umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; atlasElementIndex++)
				{
					UMAData.AtlasMaterialDefinition tempMaterialDef = umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex];

					if (tempMaterialDef.atlasRegion == nullRect)
					{

						tempMaterialDef.atlasRegion = packTexture.Insert(Mathf.FloorToInt(tempMaterialDef.source.baseTexture[0].width * umaAtlasList.atlas[atlasIndex].resolutionScale * tempMaterialDef.source.slotData.overlayScale), Mathf.FloorToInt(tempMaterialDef.source.baseTexture[0].height * umaAtlasList.atlas[atlasIndex].resolutionScale * tempMaterialDef.source.slotData.overlayScale), MaxRectsBinPack.FreeRectChoiceHeuristic.RectBestLongSideFit);
						tempMaterialDef.isRectShared = false;

						if (tempMaterialDef.atlasRegion == nullRect)
						{
							textureFit = false;

							if (umaGenerator.fitAtlas)
							{
								Debug.LogWarning("Atlas resolution is too small, Textures will be reduced.");
							}
							else
							{
								Debug.LogError("Atlas resolution is too small, not all textures will fit.");
							}
						}

						for (int atlasElementIndex2 = atlasElementIndex; atlasElementIndex2 < umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; atlasElementIndex2++)
						{
							if (atlasElementIndex != atlasElementIndex2)
							{
								if (tempMaterialDef.source.baseTexture[0] == umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex2].source.baseTexture[0])
								{
									umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex2].atlasRegion = tempMaterialDef.atlasRegion;
									umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex2].isRectShared = true;
								}
							}
						}

					}

					if (!textureFit && umaGenerator.fitAtlas)
					{
						//Reset calculation and reduce texture sizes
						textureFit = true;
						atlasElementIndex = -1;
						umaAtlasList.atlas[atlasIndex].resolutionScale = umaAtlasList.atlas[atlasIndex].resolutionScale * 0.5f;

						packTexture.Init(umaGenerator.atlasResolution, umaGenerator.atlasResolution, false);
						for (int atlasElementIndex2 = 0; atlasElementIndex2 < umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; atlasElementIndex2++)
						{
							umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex2].atlasRegion = nullRect;
						}
					}
				}
			}
		}

		private void OptimizeAtlas()
		{
			UMAData.AtlasList umaAtlasList = umaData.atlasList;
			for (int atlasIndex = 0; atlasIndex < umaAtlasList.atlas.Count; atlasIndex++)
			{
				Vector2 usedArea = new Vector2(0, 0);
				for (int atlasElementIndex = 0; atlasElementIndex < umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; atlasElementIndex++)
				{
					if (umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex].atlasRegion.xMax > usedArea.x)
					{
						usedArea.x = umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex].atlasRegion.xMax;
					}

					if (umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex].atlasRegion.yMax > usedArea.y)
					{
						usedArea.y = umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex].atlasRegion.yMax;
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

				umaAtlasList.atlas[atlasIndex].cropResolution = tempResolution;
			}
		}

		private void UpdateUV()
		{
			UMAData.AtlasList umaAtlasList = umaData.atlasList;

			for (int atlasIndex = 0; atlasIndex < umaAtlasList.atlas.Count; atlasIndex++)
			{
				Vector2 finalAtlasAspect = new Vector2(umaGenerator.atlasResolution / umaAtlasList.atlas[atlasIndex].cropResolution.x, umaGenerator.atlasResolution / umaAtlasList.atlas[atlasIndex].cropResolution.y);

				for (int atlasElementIndex = 0; atlasElementIndex < umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions.Count; atlasElementIndex++)
				{
					Rect tempRect = umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex].atlasRegion;
					tempRect.xMin = tempRect.xMin * finalAtlasAspect.x;
					tempRect.xMax = tempRect.xMax * finalAtlasAspect.x;
					tempRect.yMin = tempRect.yMin * finalAtlasAspect.y;
					tempRect.yMax = tempRect.yMax * finalAtlasAspect.y;
					umaAtlasList.atlas[atlasIndex].atlasMaterialDefinitions[atlasElementIndex].atlasRegion = tempRect;
				}
			}
		}
	}
}
