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
		int scaleFactor;
		MaterialDefinitionComparer comparer = new MaterialDefinitionComparer();
		List<UMAData.GeneratedMaterial> generatedMaterials;
		int rendererCount;
		List<UMAData.GeneratedMaterial> atlassedMaterials = new List<UMAData.GeneratedMaterial>(20);
		Dictionary<List<OverlayData>, UMAData.GeneratedMaterial> generatedMaterialLookup;

		public void Prepare(UMAGeneratorBase _umaGenerator, UMAData _umaData, TextureProcessBaseCoroutine textureProcessCoroutine, bool updateMaterialList, int InitialScaleFactor)
		{
			umaGenerator = _umaGenerator;
			umaData = _umaData;
			this.textureProcessCoroutine = textureProcessCoroutine;
			this.updateMaterialList = updateMaterialList;
			scaleFactor = InitialScaleFactor;
		}

		private UMAData.GeneratedMaterial FindOrCreateGeneratedMaterial(UMAMaterial umaMaterial)
		{
			if (umaMaterial.materialType == UMAMaterial.MaterialType.Atlas)
			{
				foreach (var atlassedMaterial in atlassedMaterials)
				{
					if (atlassedMaterial.umaMaterial == umaMaterial)
					{
						return atlassedMaterial;
					}
					else
					{
						if (atlassedMaterial.umaMaterial.Equals(umaMaterial))
						{
							return atlassedMaterial;
						}
					}
				}
			}

			var res = new UMAData.GeneratedMaterial();
			if (umaMaterial.RequireSeperateRenderer)
			{
				res.renderer = rendererCount++;
			}
			res.umaMaterial = umaMaterial;
			res.material = UnityEngine.Object.Instantiate(umaMaterial.material) as Material;
			res.material.name = umaMaterial.material.name;
			atlassedMaterials.Add(res);
			generatedMaterials.Add(res);
			return res;
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
			rendererCount = 0;

			SlotData[] slots = umaData.umaRecipe.slotDataList;

			for (int i = 0; i < slots.Length; i++)
			{
				var slot = slots[i];
				if (slot == null)
					continue;
				
				if ((slot.asset.material != null) && (slot.GetOverlay(0) != null))
				{
					if (!slot.asset.material.RequireSeperateRenderer)
					{
						// At least one slot that doesn't require a seperate renderer, so we reserve renderer 0 for those.
						rendererCount = 1;
						break;
					}
				}
			}

			for (int i = 0; i < slots.Length; i++)
			{
				SlotData slot = slots[i];
				if (slot == null)
					continue;

				// Let's only add the default overlay if the slot has overlays and NO meshData
                if ((slot.asset.meshData != null) && (slot.OverlayCount == 0))
				{
                    if (umaGenerator.defaultOverlaydata != null)
                        slot.AddOverlay(umaGenerator.defaultOverlaydata);
				}

                OverlayData overlay0 = slot.GetOverlay(0);
				if ((slot.asset.material != null) && (overlay0 != null))
				{
					List<OverlayData> overlayList = slot.GetOverlayList();
					UMAData.GeneratedMaterial generatedMaterial;
					if (!generatedMaterialLookup.TryGetValue(overlayList, out generatedMaterial))
					{
						generatedMaterial = FindOrCreateGeneratedMaterial(slot.asset.material);
						generatedMaterialLookup.Add(overlayList, generatedMaterial);
					}

					int validOverlayCount = 0;
					for (int j = 0; j < slot.OverlayCount; j++)
					{
						var overlay = slot.GetOverlay(j);
						if (overlay != null)
						{
							validOverlayCount++;
							#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
							if (overlay.isProcedural)
								overlay.GenerateProceduralTextures();
                            #endif
						}
					}

					UMAData.MaterialFragment tempMaterialDefinition = new UMAData.MaterialFragment();
					tempMaterialDefinition.baseOverlay = new UMAData.textureData();
					tempMaterialDefinition.baseOverlay.textureList = overlay0.textureArray;
					tempMaterialDefinition.baseOverlay.alphaTexture = overlay0.alphaMask;
					tempMaterialDefinition.baseOverlay.overlayType = overlay0.overlayType;

					tempMaterialDefinition.umaMaterial = slot.asset.material;
					tempMaterialDefinition.baseColor = overlay0.colorData.color;
					tempMaterialDefinition.size = overlay0.pixelCount;

					tempMaterialDefinition.overlays = new UMAData.textureData[validOverlayCount - 1];
					tempMaterialDefinition.overlayColors = new Color32[validOverlayCount - 1];
					tempMaterialDefinition.rects = new Rect[validOverlayCount - 1];
					tempMaterialDefinition.overlayData = new OverlayData[validOverlayCount];
					tempMaterialDefinition.channelMask = new Color[validOverlayCount][];
					tempMaterialDefinition.channelAdditiveMask = new Color[validOverlayCount][];
					tempMaterialDefinition.overlayData[0] = slot.GetOverlay(0);
					tempMaterialDefinition.channelMask[0] = slot.GetOverlay(0).colorData.channelMask;
					tempMaterialDefinition.channelAdditiveMask[0] = slot.GetOverlay(0).colorData.channelAdditiveMask;
					tempMaterialDefinition.slotData = slot;

					int overlayID = 0;
					for (int j = 1; j < slot.OverlayCount; j++)
					{
						OverlayData overlay = slot.GetOverlay(j);
						if (overlay == null)
							continue;

						tempMaterialDefinition.rects[overlayID] = overlay.rect;
						tempMaterialDefinition.overlays[overlayID] = new UMAData.textureData();
						tempMaterialDefinition.overlays[overlayID].textureList = overlay.textureArray;
						tempMaterialDefinition.overlays[overlayID].alphaTexture = overlay.alphaMask;
						tempMaterialDefinition.overlays[overlayID].overlayType = overlay.overlayType;
						tempMaterialDefinition.overlayColors[overlayID] = overlay.colorData.color;

						overlayID++;
						tempMaterialDefinition.overlayData[overlayID] = overlay;
						tempMaterialDefinition.channelMask[overlayID] = overlay.colorData.channelMask;
						tempMaterialDefinition.channelAdditiveMask[overlayID] = overlay.colorData.channelAdditiveMask;
					}

					tempMaterialDefinition.overlayList = overlayList;
					tempMaterialDefinition.isRectShared = false;
					for (int j = 0; j < generatedMaterial.materialFragments.Count; j++)
					{
						if (tempMaterialDefinition.overlayList == generatedMaterial.materialFragments[j].overlayList)
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
			public int Compare(UMAData.MaterialFragment x, UMAData.MaterialFragment y)
			{
				return y.size - x.size;
			}
		}

		protected override IEnumerator workerMethod()
		{
			umaData.generatedMaterials.rendererCount = rendererCount;
			umaData.generatedMaterials.materials = generatedMaterials;

			GenerateAtlasData();
			OptimizeAtlas();

			textureProcessCoroutine.Prepare(umaData, umaGenerator);
			yield return textureProcessCoroutine;

			CleanBackUpTextures();
			UpdateUV();

			// HACK - is this the right place?
			SlotData[] slots = umaData.umaRecipe.slotDataList;
			for (int i = 0; i < slots.Length; i++)
			{
				var slot = slots[i];
				if (slot == null)
					continue;

#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
				for (int j = 1; j < slot.OverlayCount; j++)
				{
					OverlayData overlay = slot.GetOverlay(j);
					if ((overlay != null) && (overlay.isProcedural))
						overlay.ReleaseProceduralTextures();
				}
#endif
			}

			if (updateMaterialList)
			{
				for (int j = 0; j < umaData.rendererCount; j++)
				{
					var renderer = umaData.GetRenderer(j);
					var mats = renderer.sharedMaterials;
					var newMats = new Material[mats.Length];
					var atlasses = umaData.generatedMaterials.materials;
					int materialIndex = 0;
					for (int i = 0; i < atlasses.Count; i++)
					{
						if (atlasses[i].renderer == j)
						{
							UMAUtils.DestroySceneObject(mats[materialIndex]);
							newMats[materialIndex] = atlasses[i].material;
							materialIndex++;
						}
					}
					renderer.sharedMaterials = newMats;
				}
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
						UMAUtils.DestroySceneObject(tempRenderTexture);
						tempRenderTexture = null;
					}
					else
					{
						UMAUtils.DestroySceneObject(tempTexture);
					}
					backUpTexture[textureIndex] = null;
				}
			}
		}

		private void GenerateAtlasData()
		{
			float StartScale = 1.0f / (float)scaleFactor;
			for (int i = 0; i < atlassedMaterials.Count; i++)
			{
				var generatedMaterial = atlassedMaterials[i];
				generatedMaterial.materialFragments.Sort(comparer);
				generatedMaterial.resolutionScale = StartScale;
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
				if (tempMaterialDef.isRectShared)
					continue;

				tempMaterialDef.atlasRegion = packTexture.Insert(Mathf.FloorToInt(tempMaterialDef.baseOverlay.textureList[0].width * material.resolutionScale * tempMaterialDef.slotData.overlayScale), Mathf.FloorToInt(tempMaterialDef.baseOverlay.textureList[0].height * material.resolutionScale * tempMaterialDef.slotData.overlayScale), MaxRectsBinPack.FreeRectChoiceHeuristic.RectBestLongSideFit);

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
				var material = umaAtlasList.materials[atlasIndex];
				if (material.umaMaterial.materialType == UMAMaterial.MaterialType.NoAtlas)
					continue;

				Vector2 finalAtlasAspect = new Vector2(umaGenerator.atlasResolution / material.cropResolution.x, umaGenerator.atlasResolution / material.cropResolution.y);

				for (int atlasElementIndex = 0; atlasElementIndex < material.materialFragments.Count; atlasElementIndex++)
				{
					Rect tempRect = material.materialFragments[atlasElementIndex].atlasRegion;
					tempRect.xMin = tempRect.xMin * finalAtlasAspect.x;
					tempRect.xMax = tempRect.xMax * finalAtlasAspect.x;
					tempRect.yMin = tempRect.yMin * finalAtlasAspect.y;
					tempRect.yMax = tempRect.yMax * finalAtlasAspect.y;
					material.materialFragments[atlasElementIndex].atlasRegion = tempRect;
				}
			}
		}
	}
}
