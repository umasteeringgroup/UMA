using UnityEngine;
using System.Collections.Generic;
using System;
using System.Runtime.CompilerServices;

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
		public override void Load(UMA.UMAData.UMARecipe umaRecipe, UMAContextBase context, bool loadSlots = true)
		{
			var packedRecipe = PackedLoad(context);
			UnpackRecipe(umaRecipe, packedRecipe, context, loadSlots);
		}

		public static UMAData.UMARecipe UnpackRecipe(UMAPackRecipe umaPackRecipe, UMAContextBase context)
		{
			UMAData.UMARecipe umaRecipe = new UMAData.UMARecipe();
			UnpackRecipe(umaRecipe, umaPackRecipe, context);
			return umaRecipe;
		}

		public static void UnpackRecipe(UMA.UMAData.UMARecipe umaRecipe, UMAPackRecipe umaPackRecipe, UMAContextBase context, bool loadSlots = true)
		{
			switch (umaPackRecipe.version)
			{
				case 3:
					UnpackRecipeVersion3(umaRecipe, umaPackRecipe, context, loadSlots);
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
			public string[] Tags; // Any recipe specific tags.
			public string[] Races;
			public string blendShapeTarget;
			public float overSmoosh;
			public float smooshDistance;
            public bool smooshInvertX;
            public bool smooshInvertY;
            public bool smooshInvertZ;
			public bool smooshInvertDist;
			public string smooshTargetTag;
			public string smooshableTag;
            public bool isSwapSlot;
            public string swapTag;
			public int uvOverride;
			public bool isDisabled;
		    public int expandAlongNormal; // Fixed point expansion along normals. divided by 10,000,000
	
		}

        [System.Serializable]
		public class PackedOverlayDataV3
		{
			public string id;
			public int colorIdx;
			// public int[] rect;
			public float[] rect;
			public bool isTransformed;
			public Vector3 scale;
			public float rotation;
			public int[] blendModes;
            public string[] Tags; // Any recipe specific tags.
			public bool[] tiling;
			public int uvOverride;
			public Vector2 translate;
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
			public string[] ShaderParms;
			public bool alwaysUpdate;
			public bool alwaysUpdateParms;
			public bool isBaseColor;
			public int displayColor;
			public PackedOverlayColorDataV3()
			{
				name = "";
				colors = new short[0];
				ShaderParms = new string[0];
#if UNITY_EDITOR
				alwaysUpdate = false;
                isBaseColor = false;
#endif
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
				Color32 color32 = colorData.color;
				displayColor = color32.r | (color32.g << 8) | (color32.b << 16) | (color32.a << 24);
				if (colorData.HasPropertyBlock)
                {
					alwaysUpdate = colorData.PropertyBlock.alwaysUpdate;
					alwaysUpdateParms = colorData.PropertyBlock.alwaysUpdateParms;
                }
#if UNITY_EDITOR
				isBaseColor = colorData.isBaseColor;
#endif
				if (colorData.HasProperties)
                {
					ShaderParms = new string[colorData.PropertyBlock.shaderProperties.Count];
					for (int i= 0; i < colorData.PropertyBlock.shaderProperties.Count; i++)
                    {
						UMAProperty up = colorData.PropertyBlock.shaderProperties[i];
						if (up != null)
						{
							ShaderParms[i] = up.ToString();
						}
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
				    overlayColorData.displayColor = new Color32((byte)(displayColor & 0xFF), (byte)((displayColor >> 8) & 0xFF), (byte)((displayColor >> 16) & 0xFF), (byte)((displayColor >> 24) & 0xFF));
#if UNITY_EDITOR
					overlayColorData.isBaseColor = isBaseColor;
#endif
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
					overlayColorData.PropertyBlock = null;
					if (ShaderParms.Length > 0)
                    {
						overlayColorData.PropertyBlock = new UMAMaterialPropertyBlock();
						overlayColorData.PropertyBlock.alwaysUpdate = alwaysUpdate; 
						overlayColorData.PropertyBlock.alwaysUpdateParms = alwaysUpdateParms;
						for(int i=0;i<ShaderParms.Length;i++)
                        {
							overlayColorData.PropertyBlock.shaderProperties.Add(UMAProperty.FromString(ShaderParms[i]));
                        }
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
			public int uvOverride = 0;

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

            UMADnaBase[] array = umaRecipe.GetAllDna();
            for (int i = 0; i < array.Length; i++)
			{
                UMADnaBase dna = array[i];
				UMAPackedDna packedDna = new UMAPackedDna();
				//DynamicUMADna:: needs the typeHash as this is randomly generated by the DynamicDnaConverter
				packedDna.dnaTypeHash = dna.DNATypeHash;
				packedDna.dnaType = dna.GetType().Name;
				packedDna.packedDna = UMA.UMADna.SaveInstance(dna);
				PackedDNAlist.Add(packedDna);
			}
			return PackedDNAlist;
		}


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
            {
				umaPackRecipe.sharedColorCount = umaRecipe.sharedColors.Length;
            }

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
                    tempPackedSlotData.Tags = umaRecipe.slotDataList[i].tags.Clone() as string[];
					tempPackedSlotData.Races = umaRecipe.slotDataList[i].Races;
					tempPackedSlotData.blendShapeTarget = umaRecipe.slotDataList[i].blendShapeTargetSlot;
					tempPackedSlotData.overSmoosh = umaRecipe.slotDataList[i].overSmoosh;
					tempPackedSlotData.smooshDistance = umaRecipe.slotDataList[i].smooshDistance;
					tempPackedSlotData.smooshInvertDist = umaRecipe.slotDataList[i].smooshInvertDist;
					tempPackedSlotData.smooshInvertX = umaRecipe.slotDataList[i].smooshInvertX;
					tempPackedSlotData.smooshInvertY = umaRecipe.slotDataList[i].smooshInvertY;
					tempPackedSlotData.smooshInvertZ = umaRecipe.slotDataList[i].smooshInvertZ;
					tempPackedSlotData.smooshableTag = umaRecipe.slotDataList[i].smooshableTag;
					tempPackedSlotData.smooshTargetTag = umaRecipe.slotDataList[i].smooshTargetTag;
                    tempPackedSlotData.swapTag = umaRecipe.slotDataList[i].swapTag;
                    tempPackedSlotData.isSwapSlot = umaRecipe.slotDataList[i].isSwapSlot;
					tempPackedSlotData.uvOverride = umaRecipe.slotDataList[i].UVSet;
					tempPackedSlotData.isDisabled = umaRecipe.slotDataList[i].isDisabled;
                    tempPackedSlotData.expandAlongNormal = umaRecipe.slotDataList[i].expandAlongNormal;

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
                    {
						continue;
                    }

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
						tempPackedOverlay.isTransformed = overlayData.instanceTransformed;
						tempPackedOverlay.tiling = new bool[overlayData.ChannelCount];
						for (int t = 0; t < overlayData.ChannelCount; t++)
						{
							tempPackedOverlay.tiling[t] = overlayData.IsTextureTiled(t);
                        }

						tempPackedOverlay.scale = overlayData.Scale;
						tempPackedOverlay.rotation = overlayData.Rotation;
						tempPackedOverlay.translate = new Vector2(overlayData.Translate.x, overlayData.Translate.y);
						tempPackedOverlay.blendModes = new int[overlayData.GetOverlayBlendsLength()];
                        tempPackedOverlay.Tags = overlayData.tags.Clone() as string[];
						tempPackedOverlay.uvOverride = overlayData.UVSet;

						for (int b=0;b< overlayData.GetOverlayBlendsLength(); b++)
						{
							tempPackedOverlay.blendModes[b] = (int)overlayData.GetOverlayBlend(b);
                        }

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
                        for (int i1 = 0; i1 < colorEntries.Count; i1++)
						{
                            OverlayColorData cData = colorEntries[i1];
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
            {
				return false;
            }

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

            for (int i = 0; i < DNA.Count; i++)
			{
                UMAPackedDna packedDna = DNA[i];
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
			if (!String.IsNullOrWhiteSpace(umaPackRecipe.race))
			{
				umaRecipe.SetRace(context.GetRace(umaPackRecipe.race));
			}
			umaRecipe.ClearDna();
			List<UMADnaBase> packedDna = UnPackDNA(umaPackRecipe.packedDna);

            for (int i = 0; i < packedDna.Count; i++)
			{
                UMADnaBase umd = packedDna[i];
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
                            {
								overlayData.EnsureChannels(overlayData.asset.material.channels.Length);
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

		public static UMAData.UMARecipe UnpackRecipeVersion3(UMAPackRecipe umaPackRecipe, UMAContextBase context, bool loadSlots = true)
		{
			UMAData.UMARecipe umaRecipe = new UMAData.UMARecipe();
			UnpackRecipeVersion3(umaRecipe, umaPackRecipe, context, loadSlots);
			return umaRecipe;
		}

		public static void UnpackRecipeVersion3(UMA.UMAData.UMARecipe umaRecipe, UMAPackRecipe umaPackRecipe, UMAContextBase context, bool loadSlots = true)
        {
            umaRecipe.slotDataList = new SlotData[umaPackRecipe.slotsV3.Length];

            if (!string.IsNullOrEmpty(umaPackRecipe.race))
            {
				var race = context.GetRace(umaPackRecipe.race);
				if (race != null)
				{
					umaRecipe.SetRace(race);
				}
            }
 
			umaRecipe.ClearDna();
            List<UMADnaBase> packedDna = UnPackDNA(umaPackRecipe.packedDna);

            for (int i = 0; i < packedDna.Count; i++)
            {
                UMADnaBase umd = packedDna[i];
                umaRecipe.AddDna(umd);
            }

            OverlayColorData[] colorData = UnpackColors(umaPackRecipe);

            umaRecipe.sharedColors = new OverlayColorData[umaPackRecipe.sharedColorCount];
            for (int i = 0; i < umaRecipe.sharedColors.Length; i++)
            {
                umaRecipe.sharedColors[i] = colorData[i];
            }

			if (loadSlots)
			{
				for (int i = 0; i < umaPackRecipe.slotsV3.Length; i++)
				{
					PackedSlotDataV3 packedSlot = umaPackRecipe.slotsV3[i];
					if (UMAPackRecipe.SlotIsValid(packedSlot))
					{
						var tempSlotData = context.InstantiateSlot(packedSlot.id);
						if (tempSlotData == null)
						{
							if (Debug.isDebugBuild)
							{
								throw new UMAResourceNotFoundException("Slot " + packedSlot.id + " not found in context. Skipping.");
							}
							continue;
						}
						if (packedSlot.Tags != null)
						{
							tempSlotData.tags = packedSlot.Tags.Clone() as string[];
						}
						else
						{
							tempSlotData.tags = new string[0];
						}
						if (packedSlot.Races != null)
						{
							tempSlotData.Races = packedSlot.Races;
						}
						tempSlotData.blendShapeTargetSlot = packedSlot.blendShapeTarget;
						tempSlotData.overlayScale = packedSlot.scale * 0.01f;
						tempSlotData.overSmoosh = packedSlot.overSmoosh;
						tempSlotData.smooshDistance = packedSlot.smooshDistance;
						tempSlotData.smooshInvertDist = packedSlot.smooshInvertDist;
						tempSlotData.smooshInvertX = packedSlot.smooshInvertX;
						tempSlotData.smooshInvertY = packedSlot.smooshInvertY;
						tempSlotData.smooshInvertZ = packedSlot.smooshInvertZ;
						tempSlotData.smooshableTag = packedSlot.smooshableTag;
						tempSlotData.smooshTargetTag = packedSlot.smooshTargetTag;
						tempSlotData.isSwapSlot = packedSlot.isSwapSlot;
						tempSlotData.swapTag = packedSlot.swapTag;
						tempSlotData.UVSet = packedSlot.uvOverride;
						tempSlotData.isDisabled = packedSlot.isDisabled;
						tempSlotData.expandAlongNormal = packedSlot.expandAlongNormal;


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

								overlayData.instanceTransformed = packedOverlay.isTransformed;
								overlayData.Scale = new Vector2(packedOverlay.scale.x, packedOverlay.scale.y);
								overlayData.Rotation = packedOverlay.rotation;
								overlayData.Translate = new Vector2(packedOverlay.translate.x, packedOverlay.translate.y);
								overlayData.UVSet = packedSlot.uvOverride;

								if (packedOverlay.colorIdx < umaPackRecipe.sharedColorCount)
								{
									overlayData.colorData = umaRecipe.sharedColors[packedOverlay.colorIdx];
								}
								else
								{
									overlayData.colorData = colorData[packedOverlay.colorIdx].Duplicate();
									overlayData.colorData.name = OverlayColorData.UNSHARED;
								}

								if (packedOverlay.blendModes != null)
								{
									overlayData.SetOverlayBlendsLength(packedOverlay.blendModes.Length);
									for (int blendModeIdx = 0; blendModeIdx < packedOverlay.blendModes.Length; blendModeIdx++)
									{
										overlayData.SetOverlayBlend(blendModeIdx, (OverlayDataAsset.OverlayBlend)packedOverlay.blendModes[blendModeIdx]);
									}
								}

								if (packedOverlay.Tags != null)
								{
									overlayData.tags = packedOverlay.Tags.Clone() as string[];
								}
								else
								{
									overlayData.tags = new string[0];
								}

								if (UMAPackRecipe.MaterialIsValid(overlayData.asset.material))
								{
									overlayData.EnsureChannels(overlayData.asset.material.channels.Length);
								}

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
        }

        public static OverlayColorData[] UnpackColors(UMAPackRecipe umaPackRecipe)
        {
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

            return colorData;
        }

        #endregion
    }
}
