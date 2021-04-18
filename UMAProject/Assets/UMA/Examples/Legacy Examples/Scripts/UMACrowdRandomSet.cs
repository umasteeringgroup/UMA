using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UMA;

namespace UMA.Examples
{
	public class UMACrowdRandomSet : ScriptableObject 
	{
		public CrowdRaceData data;

		[System.Serializable]
		public class CrowdRaceData
		{
			public string raceID;
			public CrowdSlotElement[] slotElements;
		}

		[System.Serializable]
		public class CrowdSlotElement
		{
			public string Info;
			public CrowdSlotData[] possibleSlots;
			public string requirement;
			public string condition;
		}

		[System.Serializable]
		public class CrowdSlotData
		{
			public string slotID;
			public bool useSharedOverlayList;
			public int overlayListSource;
			public CrowdOverlayElement[] overlayElements;
		}

		[System.Serializable]
		public class CrowdOverlayElement
		{
			public CrowdOverlayData[] possibleOverlays;
		}

		public enum OverlayType
		{
			Unknown, 
			Random,
			Texture,
			Color,
			Skin,
			Hair,		
		}

		public enum ChannelUse
		{
			None,
			Color,
			InverseColor
		}

		[System.Serializable]
		public class CrowdOverlayData
		{
			public string overlayID;
			public Color maxRGB;
			public Color minRGB;
			public bool useSkinColor;
			public bool useHairColor;
			public float hairColorMultiplier;
			public ChannelUse colorChannelUse;
			public int colorChannel;
			public OverlayType overlayType;
			public void UpdateVersion()
			{
				if (overlayType == UMACrowdRandomSet.OverlayType.Unknown)
				{
					if (useSkinColor)
					{
						overlayType = UMACrowdRandomSet.OverlayType.Skin;
					}
					else if (useHairColor)
					{
						overlayType = UMACrowdRandomSet.OverlayType.Hair;
					}
					else
					{
						if (minRGB == maxRGB)
						{
							if (minRGB == Color.white)
							{
								overlayType = UMACrowdRandomSet.OverlayType.Texture;
							}
							else
							{
								overlayType = UMACrowdRandomSet.OverlayType.Color;
							}
						}
						else
						{
							overlayType = UMACrowdRandomSet.OverlayType.Random;
						}
					}
				}
			}
		}


		public static void Apply(UMA.UMAData umaData, CrowdRaceData race, Color skinColor, Color HairColor, Color Shine, HashSet<string> Keywords, UMAContextBase context)
		{
			var slotParts = new HashSet<string>();
			umaData.umaRecipe.slotDataList = new SlotData[race.slotElements.Length];
			for (int i = 0; i < race.slotElements.Length; i++)
			{
				var currentElement = race.slotElements[i];
				if (!string.IsNullOrEmpty(currentElement.requirement) && !slotParts.Contains(currentElement.requirement)) continue;
				if (!string.IsNullOrEmpty(currentElement.condition))
				{
					if (currentElement.condition.StartsWith("!"))
					{
						if (Keywords.Contains(currentElement.condition.Substring(1))) continue;
					}
					else
					{
						if (!Keywords.Contains(currentElement.condition)) continue;
					}
				}
				if (currentElement.possibleSlots.Length == 0) continue;
				int randomResult = Random.Range(0, currentElement.possibleSlots.Length);
				var slot = currentElement.possibleSlots[randomResult];
				if (string.IsNullOrEmpty(slot.slotID)) continue;
				slotParts.Add(slot.slotID);
				SlotData slotData;
				if (slot.useSharedOverlayList && slot.overlayListSource >= 0 && slot.overlayListSource < i)
				{
					slotData = context.InstantiateSlot(slot.slotID, umaData.umaRecipe.slotDataList[slot.overlayListSource].GetOverlayList());
				}
				else
				{
					if (slot.useSharedOverlayList)
					{
						Debug.LogError("UMA Crowd: Invalid overlayListSource for " + slot.slotID);
					}
					slotData = context.InstantiateSlot(slot.slotID);
				}
				umaData.umaRecipe.slotDataList[i] = slotData;
				for (int overlayIdx = 0; overlayIdx < slot.overlayElements.Length; overlayIdx++)
				{
					var currentOverlayElement = slot.overlayElements[overlayIdx];
					randomResult = Random.Range(0, currentOverlayElement.possibleOverlays.Length);
					var overlay = currentOverlayElement.possibleOverlays[randomResult];
					if (string.IsNullOrEmpty(overlay.overlayID)) continue;
					overlay.UpdateVersion();
					slotParts.Add(overlay.overlayID);
					Color overlayColor = Color.black;
					var overlayData = context.InstantiateOverlay(overlay.overlayID, overlayColor);


					switch (overlay.overlayType)
					{
						case UMACrowdRandomSet.OverlayType.Color:
							overlayColor = overlay.minRGB;
							overlayData.colorData.color = overlayColor;

							break;
						case UMACrowdRandomSet.OverlayType.Texture:
							overlayColor = Color.white;
							overlayData.colorData.color = overlayColor;
							break;
						case UMACrowdRandomSet.OverlayType.Hair:
							overlayColor = HairColor * overlay.hairColorMultiplier;
							overlayColor.a = 1.0f;
							overlayData.colorData.color = overlayColor;
							break;
						case UMACrowdRandomSet.OverlayType.Skin:
							overlayColor = skinColor;//  + new Color(Random.Range(overlay.minRGB.r, overlay.maxRGB.r), Random.Range(overlay.minRGB.g, overlay.maxRGB.g), Random.Range(overlay.minRGB.b, overlay.maxRGB.b), 1);
							overlayData.colorData.color = overlayColor;
							if (overlayData.colorData.channelAdditiveMask.Length > 2)
							{
								overlayData.colorData.channelAdditiveMask[2] = Shine;
							}
							else
							{
								break;
							}
							break;
						case UMACrowdRandomSet.OverlayType.Random:
							{
								float randomShine = Random.Range(0.05f, 0.25f);
								float randomMetal = Random.Range(0.1f, 0.3f);

								overlayColor = new Color(Random.Range(overlay.minRGB.r, overlay.maxRGB.r), Random.Range(overlay.minRGB.g, overlay.maxRGB.g), Random.Range(overlay.minRGB.b, overlay.maxRGB.b), Random.Range(overlay.minRGB.a, overlay.maxRGB.a));
								overlayData.colorData.color = overlayColor;
								if (overlayData.colorData.channelAdditiveMask.Length > 2)
								{
									overlayData.colorData.channelAdditiveMask[2] = new Color(randomMetal, randomMetal, randomMetal, randomShine);
								}
							}
							break;
						default:
							Debug.LogError("Unknown RandomSet overlayType: "+((int)overlay.overlayType));
							overlayColor = overlay.minRGB;
							overlayData.colorData.color = overlayColor;
							break;
					}
		
					slotData.AddOverlay(overlayData);
					if (overlay.colorChannelUse != ChannelUse.None)
					{
						overlayColor.a *= overlay.minRGB.a;
						if (overlay.colorChannelUse == ChannelUse.InverseColor)
						{
							Vector3 color = new Vector3(overlayColor.r, overlayColor.g, overlayColor.b);
							var len = color.magnitude;
							if (len < 1f) len = 1f;
							color = new Vector3(1.001f, 1.001f, 1.001f) - color;
							color = color.normalized* len;
							overlayColor = new Color(color.x, color.y, color.z, overlayColor.a);
						}
						overlayData.SetColor(overlay.colorChannel, overlayColor);
					}
				}
			}
		}
	}
}
