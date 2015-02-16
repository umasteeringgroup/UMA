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
		UnpackRecipe(umaRecipe, packedRecipe, context);
	}

	public override void Save(UMA.UMAData.UMARecipe umaRecipe, UMAContext context)
	{
		var packedRecipe = PackRecipe(umaRecipe);
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
	public class UMAPackedDna
	{
		public string dnaType;
		public string packedDna;
	}

	[System.Serializable]
	public class UMAPackRecipe
	{
		public packedSlotData[] packedSlotDataList;
		public string race;
		public Dictionary<Type, UMADna> umaDna = new Dictionary<Type, UMADna>();
		public List<UMAPackedDna> packedDna = new List<UMAPackedDna>();
	}

	public static UMAPackRecipe PackRecipe(UMA.UMAData.UMARecipe umaRecipe)
	{
		UMAPackRecipe umaPackRecipe = new UMAPackRecipe();

		//var umaPackRecipe = new Packed
		umaPackRecipe.packedSlotDataList = new packedSlotData[umaRecipe.slotDataList.Length];
		umaPackRecipe.race = umaRecipe.raceData.raceName;

		umaPackRecipe.packedDna.Clear();

		foreach (var dna in umaRecipe.GetAllDna())
		{
			UMAPackedDna packedDna = new UMAPackedDna();
			packedDna.dnaType = dna.GetType().Name;
			packedDna.packedDna = UMA.UMADna.SaveInstance(dna);
			umaPackRecipe.packedDna.Add(packedDna);
		}

		for (int i = 0; i < umaRecipe.slotDataList.Length; i++)
		{
			if (umaRecipe.slotDataList[i] != null)
			{
				packedSlotData tempPackedSlotData;

				tempPackedSlotData = new packedSlotData();
				umaPackRecipe.packedSlotDataList[i] = tempPackedSlotData;

				tempPackedSlotData.slotID = umaRecipe.slotDataList[i].slotName;
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
					tempPackedSlotData.OverlayDataList[overlayID].overlayID = umaRecipe.slotDataList[i].GetOverlay(overlayID).overlayName;

					if (umaRecipe.slotDataList[i].GetOverlay(overlayID).color != new Color(1.0f, 1.0f, 1.0f, 1.0f))
					{
						//Color32 instead of Color?
						tempPackedSlotData.OverlayDataList[overlayID].colorList = new int[4];
						tempPackedSlotData.OverlayDataList[overlayID].colorList[0] = Mathf.FloorToInt(umaRecipe.slotDataList[i].GetOverlay(overlayID).color.r * 255.0f);
						tempPackedSlotData.OverlayDataList[overlayID].colorList[1] = Mathf.FloorToInt(umaRecipe.slotDataList[i].GetOverlay(overlayID).color.g * 255.0f);
						tempPackedSlotData.OverlayDataList[overlayID].colorList[2] = Mathf.FloorToInt(umaRecipe.slotDataList[i].GetOverlay(overlayID).color.b * 255.0f);
						tempPackedSlotData.OverlayDataList[overlayID].colorList[3] = Mathf.FloorToInt(umaRecipe.slotDataList[i].GetOverlay(overlayID).color.a * 255.0f);
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

					if (umaRecipe.slotDataList[i].GetOverlay(overlayID).channelMask != null)
					{
						tempPackedSlotData.OverlayDataList[overlayID].channelMaskList = new int[umaRecipe.slotDataList[i].GetOverlay(overlayID).channelMask.Length][];

						for (int channelAdjust = 0; channelAdjust < umaRecipe.slotDataList[i].GetOverlay(overlayID).channelMask.Length; channelAdjust++)
						{
							tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust] = new int[4];
							tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust][0] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelMask[channelAdjust].r;
							tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust][1] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelMask[channelAdjust].g;
							tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust][2] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelMask[channelAdjust].b;
							tempPackedSlotData.OverlayDataList[overlayID].channelMaskList[channelAdjust][3] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelMask[channelAdjust].a;
						}

					}
					if (umaRecipe.slotDataList[i].GetOverlay(overlayID).channelAdditiveMask != null)
					{
						tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList = new int[umaRecipe.slotDataList[i].GetOverlay(overlayID).channelAdditiveMask.Length][];
						for (int channelAdjust = 0; channelAdjust < umaRecipe.slotDataList[i].GetOverlay(overlayID).channelAdditiveMask.Length; channelAdjust++)
						{
							tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust] = new int[4];
							tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust][0] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelAdditiveMask[channelAdjust].r;
							tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust][1] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelAdditiveMask[channelAdjust].g;
							tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust][2] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelAdditiveMask[channelAdjust].b;
							tempPackedSlotData.OverlayDataList[overlayID].channelAdditiveMaskList[channelAdjust][3] = umaRecipe.slotDataList[i].GetOverlay(overlayID).channelAdditiveMask[channelAdjust].a;
						}

					}
				}
			}
		}
		return umaPackRecipe;
	}

	public static void UnpackRecipe(UMA.UMAData.UMARecipe umaRecipe, UMAPackRecipe umaPackRecipe, UMAContext context)
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
				SlotData tempSlotData = SlotData.CreateInstance<SlotData>();
				tempSlotData = context.InstantiateSlot(umaPackRecipe.packedSlotDataList[i].slotID);
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
						tempSlotData.GetOverlay(tempSlotData.OverlayCount - 1).color = tempColor;
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
	
	#endregion
}
