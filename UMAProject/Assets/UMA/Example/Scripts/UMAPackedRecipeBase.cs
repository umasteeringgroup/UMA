using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using UMA;

public abstract class UMAPackedRecipeBase : UMARecipeBase
{
	public override void Load(UMA.UMAData.UMARecipe umaRecipe, UMAContext context)
	{
		var packedRecipe = PackedLoad(context);
		switch (packedRecipe.version)
		{
			case 2:
				UnpackRecipeVersion2(umaRecipe, packedRecipe, context);
				break;

			case 1:
			default:
				UnpackRecipeVersion1(umaRecipe, packedRecipe, context);
				umaRecipe.MergeMatchingOverlays();
				break;
		}
	}

	public override void Save(UMA.UMAData.UMARecipe umaRecipe, UMAContext context)
	{
		umaRecipe.MergeMatchingOverlays();
		var packedRecipe = PackRecipeV2(umaRecipe);
		PackedSave(packedRecipe, context);
	}

	public abstract UMAPackRecipe PackedLoad(UMAContext context);
	public abstract void PackedSave(UMAPackRecipe packedRecipe, UMAContext context);

	#region Packing Related

	[System.Serializable]
	public class packedSlotData
	{
		public string slotID;
		public int overlayScale = 1;
		public int copyOverlayIndex = -1;
		public packedOverlayData[] OverlayDataList;
	}

	[System.Serializable]
	public class packedOverlayData
	{
		public string overlayID;
		public int[] colorList;
		public int[][] channelMaskList;
		public int[][] channelAdditiveMaskList;

		public int[] rectList;
	}

	[System.Serializable]
	public class PackedSlotDataV2
	{
		public string id;
		public int scale = 1;
		public int copyIdx = -1;
		public PackedOverlayDataV2[] overlays;
	}
	
	[System.Serializable]
	public class PackedOverlayDataV2
	{
		public string id;
		public int colorIdx;
		public int[] rect;
	}
	
	[System.Serializable]
	public class PackedOverlayColorDataV2
	{
		public string name;
		public byte[] color;
		public byte[][] masks;
		public byte[][] addMasks;

		public PackedOverlayColorDataV2()
		{
			name = "";
			color = new byte[4];
		}

		public PackedOverlayColorDataV2(OverlayColorData colorData)
		{
			name = colorData.name;
			color = new byte[4];
			color[0] = colorData.color.r;
			color[1] = colorData.color.g;
			color[2] = colorData.color.b;
			color[3] = colorData.color.a;
			if (colorData.channelMask != null && colorData.channelMask.Length > 0)
			{
				int channelCount = colorData.channelMask.Length;
				masks = new byte[channelCount][];
				addMasks = new byte[channelCount][];
				for (int channel = 0; channel < channelCount; channel++)
				{
					masks[channel] = new byte[4];
					addMasks[channel] = new byte[4];
					Color32 maskColor = colorData.channelMask[channel];
					masks[channel][0] = maskColor.r;
					masks[channel][1] = maskColor.g;
					masks[channel][2] = maskColor.b;
					masks[channel][3] = maskColor.a;
					Color32 additiveMaskColor = colorData.channelAdditiveMask[channel];
					addMasks[channel][0] = additiveMaskColor.r;
					addMasks[channel][1] = additiveMaskColor.g;
					addMasks[channel][2] = additiveMaskColor.b;
					addMasks[channel][3] = additiveMaskColor.a;
				}
			}
		}

		public void SetOverlayColorData(OverlayColorData overlayColorData)
		{
			overlayColorData.name = name;
			overlayColorData.color.r = color[0];
			overlayColorData.color.g = color[1];
			overlayColorData.color.b = color[2];
			overlayColorData.color.a = color[3];
			if (masks != null && masks.Length > 0)
			{
				int channelCount = masks.Length;
				overlayColorData.channelMask = new Color32[channelCount];
				overlayColorData.channelAdditiveMask = new Color32[channelCount];
				for (int channel = 0; channel < channelCount; channel++)
				{
					overlayColorData.channelMask[channel].r = masks[channel][0];
					overlayColorData.channelMask[channel].g = masks[channel][1];
					overlayColorData.channelMask[channel].b = masks[channel][2];
					overlayColorData.channelMask[channel].a = masks[channel][3];
					overlayColorData.channelAdditiveMask[channel].r = addMasks[channel][0];
					overlayColorData.channelAdditiveMask[channel].g = addMasks[channel][1];
					overlayColorData.channelAdditiveMask[channel].b = addMasks[channel][2];
					overlayColorData.channelAdditiveMask[channel].a = addMasks[channel][3];
				}
			}
		}
	}

	[System.Serializable]
	public class UMAPackedDna
	{
		public string dnaType;
		public string packedDna;
	}

	[System.Serializable]
	public class UMAPackRecipe
	{
		public int version = 1;
		public packedSlotData[] packedSlotDataList;
		public PackedSlotDataV2[] slotsV2;
		public PackedOverlayColorDataV2[] colors;
		public int sharedColorCount;
		public string race;
		public Dictionary<Type, UMADna> umaDna = new Dictionary<Type, UMADna>();
		public List<UMAPackedDna> packedDna = new List<UMAPackedDna>();
	}

/*
	public static UMAPackRecipe PackRecipeV1(UMA.UMAData.UMARecipe umaRecipe)
	{
		UMAPackRecipe umaPackRecipe = new UMAPackRecipe();

		//var umaPackRecipe = new Packed
		int slotCount = umaRecipe.slotDataList.Length - umaRecipe.AdditionalSlots;
		umaPackRecipe.packedSlotDataList = new packedSlotData[slotCount];
		umaPackRecipe.race = umaRecipe.raceData.raceName;

		foreach (var dna in umaRecipe.GetAllDna())
		{
			UMAPackedDna packedDna = new UMAPackedDna();
			packedDna.dnaType = dna.GetType().Name;
			packedDna.packedDna = UMA.UMADna.SaveInstance(dna);
			umaPackRecipe.packedDna.Add(packedDna);
		}

		for (int i = 0; i < slotCount; i++)
		{
			if (umaRecipe.slotDataList[i] != null)
			{
				packedSlotData tempPackedSlotData = new packedSlotData();
				umaPackRecipe.packedSlotDataList[i] = tempPackedSlotData;

				tempPackedSlotData.slotID = umaRecipe.slotDataList[i].asset.slotName;
				tempPackedSlotData.overlayScale = Mathf.FloorToInt(umaRecipe.slotDataList[i].overlayScale * 100);

				bool copiedOverlays = false;
				for (int i2 = 0; i2 < i; i2++)
				{
					if (umaRecipe.slotDataList[i2] != null && umaPackRecipe.packedSlotDataList[i2] != null)
					{
						if (umaRecipe.slotDataList[i].GetOverlayList() == umaRecipe.slotDataList[i2].GetOverlayList())
						{
							tempPackedSlotData.copyOverlayIndex = i2;
							copiedOverlays = true;
							break;
						}
					}
				}
				if( copiedOverlays ) continue;

				tempPackedSlotData.OverlayDataList = new packedOverlayData[umaRecipe.slotDataList[i].OverlayCount];

				for (int overlayID = 0; overlayID < tempPackedSlotData.OverlayDataList.Length; overlayID++)
				{
					tempPackedSlotData.OverlayDataList[overlayID] = new packedOverlayData();
					tempPackedSlotData.OverlayDataList[overlayID].overlayID = umaRecipe.slotDataList[i].GetOverlay(overlayID).asset.overlayName;
					OverlayColorData colorData = umaRecipe.slotDataList[i].GetOverlay(overlayID).colorData;
					if (colorData.color != Color.white)
					{
						Color32 color = umaRecipe.slotDataList[i].GetOverlay(overlayID).colorData.color;
						tempPackedSlotData.OverlayDataList[overlayID].colorList = new int[4];
						tempPackedSlotData.OverlayDataList[overlayID].colorList[0] = color.r;
						tempPackedSlotData.OverlayDataList[overlayID].colorList[1] = color.g;
						tempPackedSlotData.OverlayDataList[overlayID].colorList[2] = color.b;
						tempPackedSlotData.OverlayDataList[overlayID].colorList[3] = color.a;
					}

					if (umaRecipe.slotDataList[i].GetOverlay(overlayID).rect != new Rect(0, 0, 0, 0))
					{
						//Might need float in next version
						tempPackedSlotData.OverlayDataList[overlayID].rectList = new int[4];
						tempPackedSlotData.OverlayDataList[overlayID].rectList[0] = (int)umaRecipe.slotDataList[i].GetOverlay(overlayID).rect.x;
						tempPackedSlotData.OverlayDataList[overlayID].rectList[1] = (int)umaRecipe.slotDataList[i].GetOverlay(overlayID).rect.y;
						tempPackedSlotData.OverlayDataList[overlayID].rectList[2] = (int)umaRecipe.slotDataList[i].GetOverlay(overlayID).rect.width;
						tempPackedSlotData.OverlayDataList[overlayID].rectList[3] = (int)umaRecipe.slotDataList[i].GetOverlay(overlayID).rect.height;
					}

					if (colorData.channelMask != null && colorData.channelMask.Length > 0)
					{
						tempPackedSlotData.OverlayDataList[overlayID].channelMaskList = new int[colorData.channelMask.Length][];

						for (int channelAdjust = 0; channelAdjust < colorData.channelMask.Length; channelAdjust++)
						{
							tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust] = new int[4];
							tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust][0] = colorData.channelMask[channelAdjust].r;
							tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust][1] = colorData.channelMask[channelAdjust].g;
							tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust][2] = colorData.channelMask[channelAdjust].b;
							tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust][3] = colorData.channelMask[channelAdjust].a;
						}

					}
					if (colorData.channelAdditiveMask != null)
					{
						tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList = new int[colorData.channelAdditiveMask.Length][];
						for (int channelAdjust = 0; channelAdjust < colorData.channelAdditiveMask.Length; channelAdjust++)
						{
							tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust] = new int[4];
							tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust][0] = colorData.channelAdditiveMask[channelAdjust].r;
							tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust][1] = colorData.channelAdditiveMask[channelAdjust].g;
							tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust][2] = colorData.channelAdditiveMask[channelAdjust].b;
							tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust][3] = colorData.channelAdditiveMask[channelAdjust].a;
						}

					}
				}
			}
		}
		return umaPackRecipe;
	}
*/

	public static UMAPackRecipe PackRecipeV2(UMA.UMAData.UMARecipe umaRecipe)
	{
		UMAPackRecipe umaPackRecipe = new UMAPackRecipe();
		umaPackRecipe.version = 2;
		
		int slotCount = umaRecipe.slotDataList.Length - umaRecipe.additionalSlotCount;
		umaPackRecipe.slotsV2 = new PackedSlotDataV2[slotCount];
		if (umaRecipe.raceData != null)
		{
			umaPackRecipe.race = umaRecipe.raceData.raceName;
		}
		
		foreach (var dna in umaRecipe.GetAllDna())
		{
			UMAPackedDna packedDna = new UMAPackedDna();
			packedDna.dnaType = dna.GetType().Name;
			packedDna.packedDna = UMA.UMADna.SaveInstance(dna);
			umaPackRecipe.packedDna.Add(packedDna);
		}

		umaPackRecipe.sharedColorCount = 0;
		if (umaRecipe.sharedColors != null)
			umaPackRecipe.sharedColorCount = umaRecipe.sharedColors.Length;
		List<OverlayColorData> colorEntries = new List<OverlayColorData>(umaPackRecipe.sharedColorCount);
		List<PackedOverlayColorDataV2> packedColorEntries = new List<PackedOverlayColorDataV2>(umaPackRecipe.sharedColorCount);
		for (int i = 0; i < umaPackRecipe.sharedColorCount; i++)
		{
			colorEntries.Add(umaRecipe.sharedColors[i]);
			packedColorEntries.Add(new PackedOverlayColorDataV2(umaRecipe.sharedColors[i]));
		}

		for (int i = 0; i < slotCount; i++)
		{
			if (umaRecipe.slotDataList[i] != null)
			{
				PackedSlotDataV2 tempPackedSlotData = new PackedSlotDataV2();
				umaPackRecipe.slotsV2[i] = tempPackedSlotData;
				
				tempPackedSlotData.id = umaRecipe.slotDataList[i].asset.slotName;
				tempPackedSlotData.scale = Mathf.FloorToInt(umaRecipe.slotDataList[i].overlayScale * 100);
				
				bool copiedOverlays = false;
				for (int i2 = 0; i2 < i; i2++)
				{
					if (umaRecipe.slotDataList[i2] != null && umaPackRecipe.slotsV2[i2] != null)
					{
						if (umaRecipe.slotDataList[i].GetOverlayList() == umaRecipe.slotDataList[i2].GetOverlayList())
						{
							tempPackedSlotData.copyIdx = i2;
							copiedOverlays = true;
							break;
						}
					}
				}
				if( copiedOverlays ) continue;

				tempPackedSlotData.overlays = new PackedOverlayDataV2[umaRecipe.slotDataList[i].OverlayCount];
				
				for (int overlayIdx = 0; overlayIdx < tempPackedSlotData.overlays.Length; overlayIdx++)
				{
					PackedOverlayDataV2 tempPackedOverlay = new PackedOverlayDataV2();

					OverlayData overlayData = umaRecipe.slotDataList[i].GetOverlay(overlayIdx);
					tempPackedOverlay.id = overlayData.asset.overlayName;
					tempPackedOverlay.rect = new int[4];
					tempPackedOverlay.rect[0] = Mathf.FloorToInt(overlayData.rect.x);
					tempPackedOverlay.rect[1] = Mathf.FloorToInt(overlayData.rect.y);
                    tempPackedOverlay.rect[2] = Mathf.FloorToInt(overlayData.rect.width);
					tempPackedOverlay.rect[3] = Mathf.FloorToInt(overlayData.rect.height);

					OverlayColorData colorData = overlayData.colorData;
					int colorIndex = colorEntries.IndexOf(colorData);
					if (colorIndex < 0)
					{
						PackedOverlayColorDataV2 newColorEntry = new PackedOverlayColorDataV2(colorData);
						packedColorEntries.Add(newColorEntry);
						colorIndex = colorEntries.Count;
						colorEntries.Add(colorData);
					}
					tempPackedOverlay.colorIdx = colorIndex;

					tempPackedSlotData.overlays[overlayIdx] = tempPackedOverlay;
				}
			}
		}

		umaPackRecipe.colors = packedColorEntries.ToArray();
		return umaPackRecipe;
	}

	public static void UnpackRecipeVersion1(UMA.UMAData.UMARecipe umaRecipe, UMAPackRecipe umaPackRecipe, UMAContext context)
	{
		umaRecipe.slotDataList = new SlotData[umaPackRecipe.packedSlotDataList.Length];
		umaRecipe.SetRace(context.GetRace(umaPackRecipe.race));

		umaRecipe.ClearDna();
		for (int dna = 0; dna < umaPackRecipe.packedDna.Count; dna++)
		{
			Type dnaType = UMADna.GetType(umaPackRecipe.packedDna[dna].dnaType);
			umaRecipe.AddDna(UMADna.LoadInstance(dnaType, umaPackRecipe.packedDna[dna].packedDna));
		}

		for (int i = 0; i < umaPackRecipe.packedSlotDataList.Length; i++)
		{
			if (umaPackRecipe.packedSlotDataList[i] != null && umaPackRecipe.packedSlotDataList[i].slotID != null)
			{
				var tempSlotData = context.InstantiateSlot(umaPackRecipe.packedSlotDataList[i].slotID);
				tempSlotData.overlayScale = umaPackRecipe.packedSlotDataList[i].overlayScale * 0.01f;
				umaRecipe.slotDataList[i] = tempSlotData;

				if (umaPackRecipe.packedSlotDataList[i].copyOverlayIndex == -1)
				{

					for (int overlay = 0; overlay < umaPackRecipe.packedSlotDataList[i].OverlayDataList.Length; overlay++)
					{
						Color tempColor;
						Rect tempRect;

						if (umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList != null)
						{
							tempColor = new Color(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList[0] / 255.0f, umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList[1] / 255.0f, umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList[2] / 255.0f, umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList[3] / 255.0f);
						}
						else
						{
							tempColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
						}

						if (umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList != null)
						{
							Rect originalRect = context.InstantiateOverlay(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].overlayID).rect;
							tempRect = new Rect(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList[0], umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList[1], umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList[2], umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList[3]);
							
							Vector2 aspectRatio = new Vector2(tempRect.width/originalRect.width,tempRect.height/originalRect.height);
							
							tempRect = new Rect(tempRect.x/aspectRatio.x,tempRect.y/aspectRatio.y,tempRect.width/aspectRatio.x,tempRect.height/aspectRatio.y);
						
						}
						else
						{
							tempRect = new Rect(0, 0, 0, 0);
						}

						tempSlotData.AddOverlay(context.InstantiateOverlay(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].overlayID));
						tempSlotData.GetOverlay(tempSlotData.OverlayCount - 1).colorData.color = tempColor;
						tempSlotData.GetOverlay(tempSlotData.OverlayCount - 1).rect = tempRect;

						if (umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].channelMaskList != null)
						{
							for (int channelAdjust = 0; channelAdjust < umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].channelMaskList.Length; channelAdjust++)
							{
								packedOverlayData tempData = umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay];
								tempSlotData.GetOverlay(tempSlotData.OverlayCount - 1).SetColor(channelAdjust, new Color32((byte)tempData.channelMaskList[channelAdjust][0],
																														   (byte)tempData.channelMaskList[channelAdjust][1],
																														   (byte)tempData.channelMaskList[channelAdjust][2],
																														   (byte)tempData.channelMaskList[channelAdjust][3]));
							}
						}

						if (umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].channelAdditiveMaskList != null)
						{
							for (int channelAdjust = 0; channelAdjust < umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].channelAdditiveMaskList.Length; channelAdjust++)
							{
								packedOverlayData tempData = umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay];
								tempSlotData.GetOverlay(tempSlotData.OverlayCount - 1).SetAdditive(channelAdjust, new Color32((byte)tempData.channelAdditiveMaskList[channelAdjust][0],
																															  (byte)tempData.channelAdditiveMaskList[channelAdjust][1],
																															  (byte)tempData.channelAdditiveMaskList[channelAdjust][2],
																															  (byte)tempData.channelAdditiveMaskList[channelAdjust][3]));
							}
						}

					}
				}
				else
				{
					tempSlotData.SetOverlayList(umaRecipe.slotDataList[umaPackRecipe.packedSlotDataList[i].copyOverlayIndex].GetOverlayList());
				}
			}
		}
	}
	
	public static void UnpackRecipeVersion2(UMA.UMAData.UMARecipe umaRecipe, UMAPackRecipe umaPackRecipe, UMAContext context)
	{
		umaRecipe.slotDataList = new SlotData[umaPackRecipe.slotsV2.Length];
		umaRecipe.SetRace(context.GetRace(umaPackRecipe.race));
		
		umaRecipe.ClearDna();
		for (int dna = 0; dna < umaPackRecipe.packedDna.Count; dna++)
		{
			Type dnaType = UMADna.GetType(umaPackRecipe.packedDna[dna].dnaType);
			umaRecipe.AddDna(UMADna.LoadInstance(dnaType, umaPackRecipe.packedDna[dna].packedDna));
		}

		OverlayColorData[] colorData = new OverlayColorData[umaPackRecipe.colors.Length];
		for (int i = 0; i < colorData.Length; i++)
		{
			colorData[i] = new OverlayColorData();
			umaPackRecipe.colors[i].SetOverlayColorData(colorData[i]);
		}
		umaRecipe.sharedColors = new OverlayColorData[umaPackRecipe.sharedColorCount];
		for (int i = 0; i < umaRecipe.sharedColors.Length; i++)
		{
			umaRecipe.sharedColors[i] = colorData[i];
		}
		
		for (int i = 0; i < umaPackRecipe.slotsV2.Length; i++)
		{
			PackedSlotDataV2 packedSlot = umaPackRecipe.slotsV2[i];
			if (packedSlot != null && packedSlot.id != null)
			{
				var tempSlotData = context.InstantiateSlot(packedSlot.id);
				tempSlotData.overlayScale = packedSlot.scale * 0.01f;
				umaRecipe.slotDataList[i] = tempSlotData;
				
				if (packedSlot.copyIdx == -1)
				{
					for (int i2 = 0; i2 < packedSlot.overlays.Length; i2++)
					{
						PackedOverlayDataV2 packedOverlay = packedSlot.overlays[i2];
						OverlayData overlayData = context.InstantiateOverlay(packedOverlay.id);
						overlayData.rect = new Rect(packedOverlay.rect[0],
						                            packedOverlay.rect[1], 
						                            packedOverlay.rect[2],
						                            packedOverlay.rect[3]);
						if (packedOverlay.colorIdx < umaPackRecipe.sharedColorCount)
						{
							overlayData.colorData = umaRecipe.sharedColors[packedOverlay.colorIdx];
						}
						else
						{
							overlayData.colorData = colorData[packedOverlay.colorIdx];
						}
						tempSlotData.AddOverlay(overlayData);
					}
				}
				else
				{
					tempSlotData.SetOverlayList(umaRecipe.slotDataList[packedSlot.copyIdx].GetOverlayList());
				}
			}
		}
	}

	#endregion
}
