using UnityEngine;
using System.Collections.Generic;
using System;

namespace UMA
{
	/// <summary>
	/// Base class for serializing recipes as "packed" int/byte based data.
	/// </summary>
	public abstract class UMAPackedRecipeBase : UMARecipeBase
	{
		/// <summary>
		/// Load data into the specified UMA recipe.
		/// </summary>
		/// <param name="umaRecipe">UMA recipe.</param>
		/// <param name="context">Context.</param>
		public override void Load(UMA.UMAData.UMARecipe umaRecipe, UMAContextBase context)
		{
			var packedRecipe = PackedLoad(context);
			UnpackRecipe(umaRecipe, packedRecipe, context);
		}

		public static UMAData.UMARecipe UnpackRecipe(UMAPackRecipe umaPackRecipe, UMAContextBase context)
		{
			UMAData.UMARecipe umaRecipe = new UMAData.UMARecipe();
			UnpackRecipe(umaRecipe, umaPackRecipe, context);
			return umaRecipe;
		}

		public static void UnpackRecipe(UMA.UMAData.UMARecipe umaRecipe, UMAPackRecipe umaPackRecipe, UMAContextBase context)
		{
			switch (umaPackRecipe.version)
			{
				case 3:
					UnpackRecipeVersion3(umaRecipe, umaPackRecipe, context);
					break;

				case 2:
					UnpackRecipeVersion2(umaRecipe, umaPackRecipe, context);
					break;

				case 1:
				default:
					if (UnpackRecipeVersion1(umaRecipe, umaPackRecipe, context))
					{
						umaRecipe.MergeMatchingOverlays();
					}
					break;
			}
		}

		/// <summary>
		/// Save data from the specified UMA recipe.
		/// </summary>
		/// <param name="umaRecipe">UMA recipe.</param>
		/// <param name="context">Context.</param>
		public override void Save(UMA.UMAData.UMARecipe umaRecipe, UMAContextBase context)
		{
			umaRecipe.MergeMatchingOverlays();
			var packedRecipe = PackRecipeV3(umaRecipe);
			PackedSave(packedRecipe, context);
		}

		/// <summary>
		/// Load serialized data into the packed recipe.
		/// </summary>
		/// <returns>The UMAPackRecipe.</returns>
		/// <param name="context">Context.</param>
		public abstract UMAPackRecipe PackedLoad(UMAContextBase context);

		/// <summary>
		/// Serialize the packed recipe.
		/// </summary>
		/// <param name="packedRecipe">Packed recipe.</param>
		/// <param name="context">Context.</param>
		public abstract void PackedSave(UMAPackRecipe packedRecipe, UMAContextBase context);

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
				color[0] = (byte)Mathf.FloorToInt(colorData.color.r * 255f);
				color[1] = (byte)Mathf.FloorToInt(colorData.color.g * 255f);
				color[2] = (byte)Mathf.FloorToInt(colorData.color.b * 255f);
				color[3] = (byte)Mathf.FloorToInt(colorData.color.a * 255f);
				if (UMAPackRecipe.ArrayHasData(colorData.channelMask))
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
				if (UMAPackRecipe.ArrayHasData(masks))
				{
					int channelCount = masks.Length;
					overlayColorData.channelMask = new Color[channelCount];
					overlayColorData.channelAdditiveMask = new Color[channelCount];
					for (int channel = 0; channel < channelCount; channel++)
					{
						overlayColorData.channelMask[channel].r = masks[channel][0] / 255f;
						overlayColorData.channelMask[channel].g = masks[channel][1] / 255f;
						overlayColorData.channelMask[channel].b = masks[channel][2] / 255f;
						overlayColorData.channelMask[channel].a = masks[channel][3] / 255f;
						overlayColorData.channelAdditiveMask[channel].r = addMasks[channel][0];
						overlayColorData.channelAdditiveMask[channel].g = addMasks[channel][1];
						overlayColorData.channelAdditiveMask[channel].b = addMasks[channel][2];
						overlayColorData.channelAdditiveMask[channel].a = addMasks[channel][3];
					}
				}
				else
				{
					overlayColorData.channelMask = new Color[1];
					overlayColorData.channelAdditiveMask = new Color[1];
					overlayColorData.channelMask[0] = new Color(color[0] / 255f, color[1] / 255f, color[2] / 255f, color[3] / 255f);
				}
			}
		}


		[System.Serializable]
		public class PackedSlotDataV3
		{
			public string id;
			public int scale = 1;
			public int copyIdx = -1;
			public PackedOverlayDataV3[] overlays;
		}

		[System.Serializable]
		public class PackedOverlayDataV3
		{
			public string id;
			public int colorIdx;
			// public int[] rect;
			public float[] rect;
#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
			public PackedOverlaySubstanceData[] data;
#endif
		}

#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
		[System.Serializable]
		public class PackedOverlaySubstanceData
		{
			const float floatIntScale = 1024f;
			const float intFloatScale = 1f / floatIntScale;

			public string name;
			public int type;
			public int[] vals;
			public string text;

			public PackedOverlaySubstanceData(OverlayData.OverlayProceduralData proceduralData)
			{
				this.name = proceduralData.name;
				this.type = (int)proceduralData.type;
				switch (proceduralData.type)
				{
					case ProceduralPropertyType.Boolean:
						vals = new int[1];
						vals[0] = proceduralData.booleanValue ? 1 : 0;
						break;
					case ProceduralPropertyType.Color3:
						vals = new int[3];
						Color32 color3 = proceduralData.colorValue;
						vals[0] = color3.r;
						vals[1] = color3.g;
						vals[2] = color3.b;
						break;
					case ProceduralPropertyType.Color4:
						vals = new int[4];
						Color32 color4 = proceduralData.colorValue;
						vals[0] = color4.r;
						vals[1] = color4.g;
						vals[2] = color4.b;
						vals[3] = color4.a;
						break;
					case ProceduralPropertyType.Enum:
						vals = new int[1];
						vals[0] = proceduralData.enumValue;
						break;
					case ProceduralPropertyType.Float:
						vals = new int[1];
						vals[0] = Mathf.FloorToInt(proceduralData.floatValue * floatIntScale);
						break;
					case ProceduralPropertyType.Vector2:
						vals = new int[2];
						vals[0] = Mathf.FloorToInt(proceduralData.vectorValue.x * floatIntScale);
						vals[1] = Mathf.FloorToInt(proceduralData.vectorValue.y * floatIntScale);
						break;
					case ProceduralPropertyType.Vector3:
						vals = new int[3];
						vals[0] = Mathf.FloorToInt(proceduralData.vectorValue.x * floatIntScale);
						vals[1] = Mathf.FloorToInt(proceduralData.vectorValue.y * floatIntScale);
						vals[2] = Mathf.FloorToInt(proceduralData.vectorValue.z * floatIntScale);
						break;
					case ProceduralPropertyType.Vector4:
						vals = new int[4];
						vals[0] = Mathf.FloorToInt(proceduralData.vectorValue.x * floatIntScale);
						vals[1] = Mathf.FloorToInt(proceduralData.vectorValue.y * floatIntScale);
						vals[2] = Mathf.FloorToInt(proceduralData.vectorValue.z * floatIntScale);
						vals[3] = Mathf.FloorToInt(proceduralData.vectorValue.w * floatIntScale);
						break;
					case ProceduralPropertyType.Texture:
						if (Debug.isDebugBuild)
							Debug.LogWarning("Unsupported Texture property on OverlayProceduralData.");
						break;
					default:
						if (Debug.isDebugBuild)
							Debug.LogError("Unsupported type enum in OverlayProceduralData! " + proceduralData.type);
						break;
				}
			}

			public void SetOverlayProceduralData(OverlayData.OverlayProceduralData proceduralData)
			{
				proceduralData.name = this.name;
				switch (this.type)
				{
					case (int)ProceduralPropertyType.Boolean:
						proceduralData.type = ProceduralPropertyType.Boolean;
						proceduralData.booleanValue = this.vals[0] > 0;
						break;
					case (int)ProceduralPropertyType.Color3:
						proceduralData.type = ProceduralPropertyType.Color3;
						proceduralData.colorValue = new Color32((byte)this.vals[0], (byte)this.vals[1], (byte)this.vals[2], 255);
						break;
					case (int)ProceduralPropertyType.Color4:
						proceduralData.type = ProceduralPropertyType.Color4;
						proceduralData.colorValue = new Color32((byte)this.vals[0], (byte)this.vals[1], (byte)this.vals[2], (byte)this.vals[3]);
						break;
					case (int)ProceduralPropertyType.Enum:
						proceduralData.type = ProceduralPropertyType.Enum;
						proceduralData.enumValue = this.vals[0];
						break;
					case (int)ProceduralPropertyType.Float:
						proceduralData.type = ProceduralPropertyType.Float;
						proceduralData.floatValue = intFloatScale * this.vals[0];
						break;
					case (int)ProceduralPropertyType.Vector2:
						proceduralData.type = ProceduralPropertyType.Vector2;
						proceduralData.vectorValue.x = intFloatScale * this.vals[0];
						proceduralData.vectorValue.y = intFloatScale * this.vals[1];
						break;
					case (int)ProceduralPropertyType.Vector3:
						proceduralData.type = ProceduralPropertyType.Vector3;
						proceduralData.vectorValue.x = intFloatScale * this.vals[0];
						proceduralData.vectorValue.y = intFloatScale * this.vals[1];
						proceduralData.vectorValue.z = intFloatScale * this.vals[2];
						break;
					case (int)ProceduralPropertyType.Vector4:
						proceduralData.type = ProceduralPropertyType.Vector4;
						proceduralData.vectorValue.x = intFloatScale * this.vals[0];
						proceduralData.vectorValue.y = intFloatScale * this.vals[1];
						proceduralData.vectorValue.z = intFloatScale * this.vals[2];
						proceduralData.vectorValue.w = intFloatScale * this.vals[3];
						break;
					case (int)ProceduralPropertyType.Texture:
						proceduralData.type = ProceduralPropertyType.Texture;
						if (Debug.isDebugBuild)
							Debug.LogWarning("Unsupported Texture property in PackedOverlaySubstanceData.");
						break;
					default:
						if (Debug.isDebugBuild)
							Debug.LogError("Bad type enum in PackedOverlaySubstanceData! " + this.type);
						break;
				}
			}
		}
#endif

		[System.Serializable]
		public class PackedOverlayColorDataV3
		{
			public string name;
			// Put everything in one array
			public short[] colors;

			public PackedOverlayColorDataV3()
			{
				name = "";
				colors = new short[0];
			}

			public PackedOverlayColorDataV3(OverlayColorData colorData)
			{
				name = colorData.name;
				if (UMAPackRecipe.ArrayHasData(colorData.channelMask))
				{
					int channelCount = colorData.channelMask.Length;
					colors = new short[channelCount * 8];
					int colorIndex = 0;
					for (int channel = 0; channel < channelCount; channel++)
					{
						Color maskColor = colorData.channelMask[channel];
						colors[colorIndex++] = (short)Mathf.FloorToInt(maskColor.r * 255f);
						colors[colorIndex++] = (short)Mathf.FloorToInt(maskColor.g * 255f);
						colors[colorIndex++] = (short)Mathf.FloorToInt(maskColor.b * 255f);
						colors[colorIndex++] = (short)Mathf.FloorToInt(maskColor.a * 255f);
						Color additiveMaskColor = colorData.channelAdditiveMask[channel];
						colors[colorIndex++] = (short)Mathf.FloorToInt(additiveMaskColor.r * 255f);
						colors[colorIndex++] = (short)Mathf.FloorToInt(additiveMaskColor.g * 255f);
						colors[colorIndex++] = (short)Mathf.FloorToInt(additiveMaskColor.b * 255f);
						colors[colorIndex++] = (short)Mathf.FloorToInt(additiveMaskColor.a * 255f);
					}
				}
			}

			public void SetOverlayColorData(OverlayColorData overlayColorData)
			{
				overlayColorData.name = name;
				if (UMAPackRecipe.ArrayHasData(colors))
				{
					int channelCount = colors.Length / 8;
					overlayColorData.channelMask = new Color[channelCount];
					overlayColorData.channelAdditiveMask = new Color[channelCount];
					int colorIndex = 0;
					for (int channel = 0; channel < channelCount; channel++)
					{
						overlayColorData.channelMask[channel].r = colors[colorIndex++] / 255f;
						overlayColorData.channelMask[channel].g = colors[colorIndex++] / 255f;
						overlayColorData.channelMask[channel].b = colors[colorIndex++] / 255f;
						overlayColorData.channelMask[channel].a = colors[colorIndex++] / 255f;
						overlayColorData.channelAdditiveMask[channel].r = colors[colorIndex++] / 255f;
						overlayColorData.channelAdditiveMask[channel].g = colors[colorIndex++] / 255f;
						overlayColorData.channelAdditiveMask[channel].b = colors[colorIndex++] / 255f;
						overlayColorData.channelAdditiveMask[channel].a = colors[colorIndex++] / 255f;
					}
				}
			}
		}

		[System.Serializable]
		public class UMAPackedDna
		{
			public string dnaType;
			//DynamicUmaDna:: needs type hash
			public int dnaTypeHash;
			public string packedDna;
		}

		[System.Serializable]
		public class UMAPackRecipe
		{
			public int version = 1;
			public packedSlotData[] packedSlotDataList;
			public PackedSlotDataV2[] slotsV2;
			public PackedSlotDataV3[] slotsV3;
			public PackedOverlayColorDataV2[] colors;
			public PackedOverlayColorDataV3[] fColors;
			public int sharedColorCount;
			public string race;
			public Dictionary<Type, UMADna> umaDna = new Dictionary<Type, UMADna>();
			public List<UMAPackedDna> packedDna = new List<UMAPackedDna>();

			public static bool ArrayHasData(Array array)
			{
				return array != null && array.Length > 0;
			}

			public static bool SlotIsValid(SlotData slotData)
			{
				return slotData != null && slotData.asset != null && !string.IsNullOrEmpty(slotData.asset.slotName);
			}

			public static bool SlotIsValid(packedSlotData packedSlotData)
			{
				return packedSlotData != null && !string.IsNullOrEmpty(packedSlotData.slotID);
			}

			public static bool SlotIsValid(PackedSlotDataV2 packedSlot)
			{
				return packedSlot != null && !string.IsNullOrEmpty(packedSlot.id);
			}

			public static bool SlotIsValid(PackedSlotDataV3 packedSlot)
			{
				return packedSlot != null && !string.IsNullOrEmpty(packedSlot.id);
			}

			public static bool MaterialIsValid(UMAMaterial material)
			{
				return material != null && !string.IsNullOrEmpty(material.name);
			}

			public static bool RaceIsValid(RaceData raceData)
			{
				return raceData != null && !string.IsNullOrEmpty(raceData.raceName);
			}
		}

		public static List<UMAPackedDna> GetPackedDNA(UMAData.UMARecipe umaRecipe)
		{
			List<UMAPackedDna> PackedDNAlist = new List<UMAPackedDna>();

			foreach (var dna in umaRecipe.GetAllDna())
			{
				UMAPackedDna packedDna = new UMAPackedDna();
				//DynamicUMADna:: needs the typeHash as this is randomly generated by the DynamicDnaConverter
				packedDna.dnaTypeHash = dna.DNATypeHash;
				packedDna.dnaType = dna.GetType().Name;
				packedDna.packedDna = UMA.UMADna.SaveInstance(dna);
				PackedDNAlist.Add(packedDna);
			}
			return PackedDNAlist;
		}

		/*
 	public static UMAPackRecipe PackRecipeV2(UMA.UMAData.UMARecipe umaRecipe)
	{
		UMAPackRecipe umaPackRecipe = new UMAPackRecipe();
		umaPackRecipe.version = 2;

		int slotCount = umaRecipe.slotDataList.Length;
		umaPackRecipe.slotsV2 = new PackedSlotDataV2[slotCount];
		if (UMAPackRecipe.RaceIsValid(umaRecipe.raceData))
		{
			umaPackRecipe.race = umaRecipe.raceData.raceName;
		}

        umaPackRecipe.packedDna = GetPackedDNA(umaRecipe);

		umaPackRecipe.sharedColorCount = 0;
		if (UMAPackRecipe.ArrayHasData(umaRecipe.sharedColors))
			umaPackRecipe.sharedColorCount = umaRecipe.sharedColors.Length;
		List<OverlayColorData> colorEntries = new List<OverlayColorData>(umaPackRecipe.sharedColorCount);
		List<PackedOverlayColorDataV3> packedColorEntries = new List<PackedOverlayColorDataV3>(umaPackRecipe.sharedColorCount);
		for (int i = 0; i < umaPackRecipe.sharedColorCount; i++)
		{
			colorEntries.Add(umaRecipe.sharedColors[i]);
			packedColorEntries.Add(new PackedOverlayColorDataV3(umaRecipe.sharedColors[i]));
		}

		for (int i = 0; i < slotCount; i++)
		{
			if (UMAPackRecipe.SlotIsValid(umaRecipe.slotDataList[i]) && !umaRecipe.slotDataList[i].dontSerialize)
			{
				PackedSlotDataV2 tempPackedSlotData = new PackedSlotDataV2();
				umaPackRecipe.slotsV2[i] = tempPackedSlotData;

				tempPackedSlotData.id = umaRecipe.slotDataList[i].asset.slotName;
				tempPackedSlotData.scale = Mathf.FloorToInt(umaRecipe.slotDataList[i].overlayScale * 100);

				bool copiedOverlays = false;
				for (int i2 = 0; i2 < i; i2++)
				{
					if (UMAPackRecipe.SlotIsValid(umaRecipe.slotDataList[i2]) && UMAPackRecipe.SlotIsValid(umaPackRecipe.slotsV2[i2]))
					{
						if (umaRecipe.slotDataList[i].GetOverlayList() == umaRecipe.slotDataList[i2].GetOverlayList())
						{
							tempPackedSlotData.copyIdx = i2;
							copiedOverlays = true;
							break;
						}
					}
				}
				if (copiedOverlays) continue;

				tempPackedSlotData.overlays = new PackedOverlayDataV2[umaRecipe.slotDataList[i].OverlayCount];

				for (int overlayIdx = 0; overlayIdx < tempPackedSlotData.overlays.Length; overlayIdx++)
				{
					PackedOverlayDataV2 tempPackedOverlay = new PackedOverlayDataV2();

					OverlayData overlayData = umaRecipe.slotDataList[i].GetOverlay(overlayIdx);
					tempPackedOverlay.id = overlayData.overlayName;
					tempPackedOverlay.rect = new int[4];
					tempPackedOverlay.rect[0] = Mathf.FloorToInt(overlayData.rect.x);
					tempPackedOverlay.rect[1] = Mathf.FloorToInt(overlayData.rect.y);
					tempPackedOverlay.rect[2] = Mathf.FloorToInt(overlayData.rect.width);
					tempPackedOverlay.rect[3] = Mathf.FloorToInt(overlayData.rect.height);

					OverlayColorData colorData = overlayData.colorData;
					int colorIndex = -1;
					int cIndex = 0;
					foreach (OverlayColorData cData in colorEntries)
					{
						if (cData.name != null && cData.name.Equals(colorData.name) && cData.Equals(colorData))
						{
							colorIndex = cIndex;
							break;
						}
						cIndex++;
					}

					if (colorIndex < 0)
					{
						PackedOverlayColorDataV3 newColorEntry = new PackedOverlayColorDataV3(colorData);
						packedColorEntries.Add(newColorEntry);
						colorIndex = colorEntries.Count;
						colorEntries.Add(colorData);

					}

					tempPackedOverlay.colorIdx = colorIndex;

					tempPackedSlotData.overlays[overlayIdx] = tempPackedOverlay;
				}
			}
		}

		umaPackRecipe.fColors = packedColorEntries.ToArray();
		return umaPackRecipe;
	}
*/

		public static UMAPackRecipe PackRecipeV3(UMA.UMAData.UMARecipe umaRecipe)
		{
			UMAPackRecipe umaPackRecipe = new UMAPackRecipe();
			umaPackRecipe.version = 3;

			int slotCount = umaRecipe.slotDataList.Length;
			umaPackRecipe.slotsV3 = new PackedSlotDataV3[slotCount];
			if (UMAPackRecipe.RaceIsValid(umaRecipe.raceData))
			{
				umaPackRecipe.race = umaRecipe.raceData.raceName;
			}

			umaPackRecipe.packedDna = GetPackedDNA(umaRecipe);

			umaPackRecipe.sharedColorCount = 0;
			if (UMAPackRecipe.ArrayHasData(umaRecipe.sharedColors))
				umaPackRecipe.sharedColorCount = umaRecipe.sharedColors.Length;
			List<OverlayColorData> colorEntries = new List<OverlayColorData>(umaPackRecipe.sharedColorCount);
			List<PackedOverlayColorDataV3> packedColorEntries = new List<PackedOverlayColorDataV3>(umaPackRecipe.sharedColorCount);
			for (int i = 0; i < umaPackRecipe.sharedColorCount; i++)
			{
				colorEntries.Add(umaRecipe.sharedColors[i]);
				packedColorEntries.Add(new PackedOverlayColorDataV3(umaRecipe.sharedColors[i]));
			}

			for (int i = 0; i < slotCount; i++)
			{
				if (UMAPackRecipe.SlotIsValid(umaRecipe.slotDataList[i]) && !umaRecipe.slotDataList[i].dontSerialize)
				{
					PackedSlotDataV3 tempPackedSlotData = new PackedSlotDataV3();
					umaPackRecipe.slotsV3[i] = tempPackedSlotData;

					tempPackedSlotData.id = umaRecipe.slotDataList[i].asset.slotName;
					tempPackedSlotData.scale = Mathf.FloorToInt(umaRecipe.slotDataList[i].overlayScale * 100);

					bool copiedOverlays = false;
					for (int i2 = 0; i2 < i; i2++)
					{
						if (UMAPackRecipe.SlotIsValid(umaRecipe.slotDataList[i2]) && UMAPackRecipe.SlotIsValid(umaPackRecipe.slotsV3[i2]))
						{
							if (umaRecipe.slotDataList[i].GetOverlayList() == umaRecipe.slotDataList[i2].GetOverlayList())
							{
								tempPackedSlotData.copyIdx = i2;
								copiedOverlays = true;
								break;
							}
						}
					}
					if (copiedOverlays)
						continue;

					tempPackedSlotData.overlays = new PackedOverlayDataV3[umaRecipe.slotDataList[i].OverlayCount];

					for (int overlayIdx = 0; overlayIdx < tempPackedSlotData.overlays.Length; overlayIdx++)
					{
						PackedOverlayDataV3 tempPackedOverlay = new PackedOverlayDataV3();

						OverlayData overlayData = umaRecipe.slotDataList[i].GetOverlay(overlayIdx);
						tempPackedOverlay.id = overlayData.overlayName;
						/*
												tempPackedOverlay.rect = new int[4];
												tempPackedOverlay.rect[0] = Mathf.FloorToInt(overlayData.rect.x);
												tempPackedOverlay.rect[1] = Mathf.FloorToInt(overlayData.rect.y);
												tempPackedOverlay.rect[2] = Mathf.FloorToInt(overlayData.rect.width);
												tempPackedOverlay.rect[3] = Mathf.FloorToInt(overlayData.rect.height);
						*/
						tempPackedOverlay.rect = new float[4];
						tempPackedOverlay.rect[0] = overlayData.rect.x;
						tempPackedOverlay.rect[1] = overlayData.rect.y;
						tempPackedOverlay.rect[2] = overlayData.rect.width;
						tempPackedOverlay.rect[3] = overlayData.rect.height;
#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
						if (overlayData.isProcedural && (overlayData.proceduralData != null))
						{
							tempPackedOverlay.data = new PackedOverlaySubstanceData[overlayData.proceduralData.Length];
							for (int dataIdx = 0; dataIdx < overlayData.proceduralData.Length; dataIdx++)
							{
								tempPackedOverlay.data[dataIdx] = new PackedOverlaySubstanceData(overlayData.proceduralData[dataIdx]);
							}
						}
#endif

						OverlayColorData colorData = overlayData.colorData;
						int colorIndex = -1;
						int cIndex = 0;
						foreach (OverlayColorData cData in colorEntries)
						{
							if (cData.name != null && cData.name.Equals(colorData.name) && cData.Equals(colorData))
							{
								colorIndex = cIndex;
								break;
							}
							cIndex++;
						}

						if (colorIndex < 0)
						{
							PackedOverlayColorDataV3 newColorEntry = new PackedOverlayColorDataV3(colorData);
							packedColorEntries.Add(newColorEntry);
							colorIndex = colorEntries.Count;
							colorEntries.Add(colorData);

						}

						tempPackedOverlay.colorIdx = colorIndex;

						tempPackedSlotData.overlays[overlayIdx] = tempPackedOverlay;
					}
				}
			}

			umaPackRecipe.fColors = packedColorEntries.ToArray();
			return umaPackRecipe;
		}

		public static bool UnpackRecipeVersion1(UMA.UMAData.UMARecipe umaRecipe, UMAPackRecipe umaPackRecipe, UMAContextBase context)
		{
			if (!UMAPackRecipe.ArrayHasData(umaPackRecipe.packedSlotDataList))
				return false;

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
				if (UMAPackRecipe.SlotIsValid(umaPackRecipe.packedSlotDataList[i]))
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

							if (UMAPackRecipe.ArrayHasData(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList))
							{
								tempColor = new Color(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList[0] / 255.0f, umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList[1] / 255.0f, umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList[2] / 255.0f, umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].colorList[3] / 255.0f);
							}
							else
							{
								tempColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
							}

							if (UMAPackRecipe.ArrayHasData(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList))
							{
								Rect originalRect = context.InstantiateOverlay(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].overlayID).rect;
								tempRect = new Rect(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList[0], umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList[1], umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList[2], umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].rectList[3]);

								Vector2 aspectRatio = new Vector2(tempRect.width / originalRect.width, tempRect.height / originalRect.height);

								tempRect = new Rect(tempRect.x / aspectRatio.x, tempRect.y / aspectRatio.y, tempRect.width / aspectRatio.x, tempRect.height / aspectRatio.y);

							}
							else
							{
								tempRect = new Rect(0, 0, 0, 0);
							}

							tempSlotData.AddOverlay(context.InstantiateOverlay(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].overlayID));
							tempSlotData.GetOverlay(tempSlotData.OverlayCount - 1).colorData.color = tempColor;
							tempSlotData.GetOverlay(tempSlotData.OverlayCount - 1).rect = tempRect;

							if (UMAPackRecipe.ArrayHasData(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].channelMaskList))
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

							if (UMAPackRecipe.ArrayHasData(umaPackRecipe.packedSlotDataList[i].OverlayDataList[overlay].channelAdditiveMaskList))
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
			return true;
		}

		public static List<UMADnaBase> UnPackDNA(List<UMAPackedRecipeBase.UMAPackedDna> DNA)
		{
			List<UMADnaBase> UnpackedDNA = new List<UMADnaBase>();

			foreach (UMAPackedDna packedDna in DNA)
			{
				Type dnaType = UMADna.GetType(packedDna.dnaType);
				UnpackedDNA.Add(UMADna.LoadInstance(dnaType, packedDna.packedDna));
			}
			return UnpackedDNA;
		}

		public static UMAData.UMARecipe UnpackRecipeVersion2(UMAPackRecipe umaPackRecipe, UMAContextBase context)
		{
			UMAData.UMARecipe umaRecipe = new UMAData.UMARecipe();
			UnpackRecipeVersion2(umaRecipe, umaPackRecipe, context);
			return umaRecipe;
		}

		public static void UnpackRecipeVersion2(UMA.UMAData.UMARecipe umaRecipe, UMAPackRecipe umaPackRecipe, UMAContextBase context)
		{
			umaRecipe.slotDataList = new SlotData[umaPackRecipe.slotsV2.Length];
			umaRecipe.SetRace(context.GetRace(umaPackRecipe.race));

			umaRecipe.ClearDna();
			List<UMADnaBase> packedDna = UnPackDNA(umaPackRecipe.packedDna);

			foreach (UMADnaBase umd in packedDna)
			{
				umaRecipe.AddDna(umd);
			}

			OverlayColorData[] colorData;
			if (UMAPackRecipe.ArrayHasData(umaPackRecipe.fColors))
			{
				colorData = new OverlayColorData[umaPackRecipe.fColors.Length];
				for (int i = 0; i < colorData.Length; i++)
				{
					colorData[i] = new OverlayColorData();
					umaPackRecipe.fColors[i].SetOverlayColorData(colorData[i]);
				}
			}
			else if (UMAPackRecipe.ArrayHasData(umaPackRecipe.colors))
			{
				colorData = new OverlayColorData[umaPackRecipe.colors.Length];
				for (int i = 0; i < colorData.Length; i++)
				{
					colorData[i] = new OverlayColorData();
					umaPackRecipe.colors[i].SetOverlayColorData(colorData[i]);
				}
			}
			else
			{
				colorData = new OverlayColorData[0];
			}

			umaRecipe.sharedColors = new OverlayColorData[umaPackRecipe.sharedColorCount];
			for (int i = 0; i < umaRecipe.sharedColors.Length; i++)
			{
				umaRecipe.sharedColors[i] = colorData[i];
			}

			for (int i = 0; i < umaPackRecipe.slotsV2.Length; i++)
			{
				PackedSlotDataV2 packedSlot = umaPackRecipe.slotsV2[i];
				if (UMAPackRecipe.SlotIsValid(packedSlot))
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
								overlayData.colorData = colorData[packedOverlay.colorIdx].Duplicate();
								overlayData.colorData.name = OverlayColorData.UNSHARED;
							}
							if (UMAPackRecipe.MaterialIsValid(overlayData.asset.material))
								overlayData.EnsureChannels(overlayData.asset.material.channels.Length);
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

		public static UMAData.UMARecipe UnpackRecipeVersion3(UMAPackRecipe umaPackRecipe, UMAContextBase context)
		{
			UMAData.UMARecipe umaRecipe = new UMAData.UMARecipe();
			UnpackRecipeVersion3(umaRecipe, umaPackRecipe, context);
			return umaRecipe;
		}

		public static void UnpackRecipeVersion3(UMA.UMAData.UMARecipe umaRecipe, UMAPackRecipe umaPackRecipe, UMAContextBase context)
		{
			umaRecipe.slotDataList = new SlotData[umaPackRecipe.slotsV3.Length];
			umaRecipe.SetRace(context.GetRace(umaPackRecipe.race));

			umaRecipe.ClearDna();
			List<UMADnaBase> packedDna = UnPackDNA(umaPackRecipe.packedDna);

			foreach (UMADnaBase umd in packedDna)
			{
				umaRecipe.AddDna(umd);
			}

			OverlayColorData[] colorData;
			if (UMAPackRecipe.ArrayHasData(umaPackRecipe.fColors))
			{
				colorData = new OverlayColorData[umaPackRecipe.fColors.Length];
				for (int i = 0; i < colorData.Length; i++)
				{
					colorData[i] = new OverlayColorData();
					umaPackRecipe.fColors[i].SetOverlayColorData(colorData[i]);
				}
			}
			else if (UMAPackRecipe.ArrayHasData(umaPackRecipe.colors))
			{
				colorData = new OverlayColorData[umaPackRecipe.colors.Length];
				for (int i = 0; i < colorData.Length; i++)
				{
					colorData[i] = new OverlayColorData();
					umaPackRecipe.colors[i].SetOverlayColorData(colorData[i]);
				}
			}
			else
			{
				colorData = new OverlayColorData[0];
			}

			umaRecipe.sharedColors = new OverlayColorData[umaPackRecipe.sharedColorCount];
			for (int i = 0; i < umaRecipe.sharedColors.Length; i++)
			{
				umaRecipe.sharedColors[i] = colorData[i];
			}

			for (int i = 0; i < umaPackRecipe.slotsV3.Length; i++)
			{
				PackedSlotDataV3 packedSlot = umaPackRecipe.slotsV3[i];
				if (UMAPackRecipe.SlotIsValid(packedSlot))
				{
					var tempSlotData = context.InstantiateSlot(packedSlot.id);
					tempSlotData.overlayScale = packedSlot.scale * 0.01f;
					umaRecipe.slotDataList[i] = tempSlotData;

					if (packedSlot.copyIdx == -1)
					{
						for (int i2 = 0; i2 < packedSlot.overlays.Length; i2++)
						{
							PackedOverlayDataV3 packedOverlay = packedSlot.overlays[i2];
							OverlayData overlayData = context.InstantiateOverlay(packedOverlay.id);
							overlayData.rect = new Rect(
								packedOverlay.rect[0],
								packedOverlay.rect[1],
								packedOverlay.rect[2],
								packedOverlay.rect[3]);
						
							if (packedOverlay.colorIdx < umaPackRecipe.sharedColorCount)
							{
								overlayData.colorData = umaRecipe.sharedColors[packedOverlay.colorIdx];
							}
							else
							{
								overlayData.colorData = colorData[packedOverlay.colorIdx].Duplicate();
								overlayData.colorData.name = OverlayColorData.UNSHARED;
							}

							if (UMAPackRecipe.MaterialIsValid(overlayData.asset.material))
								overlayData.EnsureChannels(overlayData.asset.material.channels.Length);
						
							#if (UNITY_STANDALONE || UNITY_IOS || UNITY_ANDROID || UNITY_PS4 || UNITY_XBOXONE) && !UNITY_2017_3_OR_NEWER //supported platforms for procedural materials
							if(packedOverlay.data == null)
								overlayData.proceduralData = new OverlayData.OverlayProceduralData[0];
							else
							{
								overlayData.proceduralData = new OverlayData.OverlayProceduralData[packedOverlay.data.Length];

    								for (int dataIdx = 0; dataIdx < packedOverlay.data.Length; dataIdx++)
    								{
	    								OverlayData.OverlayProceduralData proceduralData = new OverlayData.OverlayProceduralData();
	    								packedOverlay.data[dataIdx].SetOverlayProceduralData(proceduralData);
    									overlayData.proceduralData[dataIdx] = proceduralData;
								}
							}
                            #endif

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
}
