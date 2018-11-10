using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace UMA
{
	public class ColorDNAConverterPlugin : DynamicDNAPlugin
	{

		[System.Serializable]
		public class DNAColorSet
		{
			public string overlayEntryName;
			[Tooltip("Color Channel: For example PBR, 0 = Albedo, 1 = Normal, 2 = Metallic")]
			public int colorChannel = 0;
			[Tooltip("Define how you want to change the colors used on this overlay")]
			public DNAColorModifier colorModifier = new DNAColorModifier();
			[Tooltip("Define the dna that influence these changes")]
			public DNAEvaluatorList modifyingDNA = new DNAEvaluatorList();

			public List<string> UsedDNANames
			{
				get
				{
					return modifyingDNA.UsedDNANames;
				}
			}

			public DNAColorSet() { }

			public DNAColorSet(DNAColorSet other)
			{
				overlayEntryName = other.overlayEntryName;
				colorChannel = other.colorChannel;
				colorModifier = new DNAColorModifier(other.colorModifier);
				modifyingDNA = new DNAEvaluatorList(other.modifyingDNA);
			}

			[System.Serializable]
			public class DNAColorComponent
			{
				[Tooltip("Change this component of the color")]
				public bool enable = true;
				[Tooltip("If false the dna value determines how much the current value is changed to the set value. If true the dna value is used *as* the value")]
				public bool useDNAValue = false;
				[Tooltip("The value for this component of the color")]
				[Range(0f, 1f)]
				public float value;
				[Tooltip("A multiplier to apply to the evaluated dnaValue")]
				public float multiplier = 1f;

				public DNAColorComponent() { }

				public DNAColorComponent(DNAColorComponent other)
				{
					enable = other.enable;
					useDNAValue = other.useDNAValue;
					value = other.value;
					multiplier = other.multiplier;
				}

				public float Evaluate(float dnaValue, float current)
				{
					if (!enable)
						return current;
					else if (useDNAValue)
					{
						return dnaValue * multiplier;
					}
					else
					{
						return Mathf.Lerp(current, value, dnaValue);
					}
				}
			}
			[System.Serializable]
			public class DNAColorModifier
			{
				public DNAColorComponent R = new DNAColorComponent();
				public DNAColorComponent G = new DNAColorComponent();
				public DNAColorComponent B = new DNAColorComponent();
				public DNAColorComponent A = new DNAColorComponent();

				public DNAColorModifier() { }

				public DNAColorModifier(DNAColorModifier other)
				{
					R = new DNAColorComponent(other.R);
					G = new DNAColorComponent(other.G);
					B = new DNAColorComponent(other.B);
					A = new DNAColorComponent(other.A);
				}
			}
		}

		public DNAColorSet[] colorSets = new DNAColorSet[0];

		private Dictionary<string, Dictionary<int, Color32>> _changedColors = new Dictionary<string, Dictionary<int, Color32>>();

		private Dictionary<string, OverlayColorData> _referenceColorDatas = new Dictionary<string, OverlayColorData>();

		//has dna been applied this cycle
		[System.NonSerialized]
		private bool _dnaApplied = false;
		//have we added the extra listeners required by ColorDNA?
		[System.NonSerialized]
		private bool _listenersAdded = false;

		public override Dictionary<string, List<int>> IndexesForDnaNames
		{
			get
			{
				var dict = new Dictionary<string, List<int>>();
				for (int i = 0; i < colorSets.Length; i++)
				{
					for (int ci = 0; ci < colorSets[i].UsedDNANames.Count; ci++)
					{
						if (!dict.ContainsKey(colorSets[i].UsedDNANames[ci]))
							dict.Add(colorSets[i].UsedDNANames[ci], new List<int>());

						dict[colorSets[i].UsedDNANames[ci]].Add(i);
					}
				}
				return dict;
			}
		}

		private void UpdateOnCharacterBegun(UMAData umaData)
		{
			if (!_dnaApplied)
			{
				ApplyDNA(umaData, umaData.skeleton, converterController.DNAAsset.dnaTypeHash);
			}
			_dnaApplied = true;
		}

		private void ResetOnCharaterUpdated(UMAData umaData)
		{
			_dnaApplied = false;
		}

		public override void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
		{
			//this needs to sign up to CharacterBegun because not all updates trigger dna changes and we need to update on texture changes too
			//eli suggested that converters could subscribe to different events 'They could have a chance at each stage, like a skeleton job, a mesh job, a texture job'
			//so this would be a texture job, but till then...
			if (!_listenersAdded)
			{
				umaData.CharacterBegun.AddListener(UpdateOnCharacterBegun);
				umaData.CharacterUpdated.AddListener(ResetOnCharaterUpdated);
				_listenersAdded = true;
			}
			//for color dna it may have been applied by a dna change OR a recipe/texture change so if its already been done dont do it again
			if (_dnaApplied)
				return;

			_changedColors.Clear();
			UMADnaBase activeDNA = umaData.GetDna(dnaTypeHash);
			if (activeDNA == null)
			{
				Debug.LogError("Could not get DNA values for: " + this.name);
				return;
			}
			var masterWeightCalc = masterWeight.GetWeight(activeDNA);
			if (masterWeightCalc == 0f)
				return;

			float[] dnaValues = activeDNA.Values;
			string[] dnaNames = activeDNA.Names;

			UpdateReferencedColorDatas(umaData.umaRecipe.slotDataList);

			for (int i = 0; i < colorSets.Length; i++)
			{
				if (colorSets[i].modifyingDNA.UsedDNANames.Count == 0)
					continue;

				var dnaVal = colorSets[i].modifyingDNA.Evaluate(activeDNA);
				bool found = false;

				foreach (SlotData slot in umaData.umaRecipe.slotDataList)
				{
					OverlayData overlay = slot.GetOverlay(colorSets[i].overlayEntryName);
					if (overlay != null)
					{
						found = true;

						//Never change a shared color but store it as a reference
						if (overlay.colorData.IsASharedColor)
						{
							_referenceColorDatas.Add(colorSets[i].overlayEntryName, overlay.colorData);
							//set the overlay to use an unshared version, so other overlays are not affected
							overlay.colorData = overlay.colorData.Duplicate();
							overlay.colorData.name = OverlayColorData.UNSHARED;
						}

						Color32 currentColor = overlay.GetColor(colorSets[i].colorChannel);

						//if the overlay originally used a shared color, use its data for setting currentColor
						if (_referenceColorDatas.ContainsKey(colorSets[i].overlayEntryName))
						{
							currentColor = _referenceColorDatas[colorSets[i].overlayEntryName].channelMask[colorSets[i].colorChannel];
						}

						//If the colour has been overidden this cycle use that instead
						if (_changedColors.ContainsKey(colorSets[i].overlayEntryName))
							if (_changedColors[colorSets[i].overlayEntryName].ContainsKey(colorSets[i].colorChannel))
								currentColor = _changedColors[colorSets[i].overlayEntryName][colorSets[i].colorChannel];

						Color newColor = currentColor;
						newColor.r = colorSets[i].colorModifier.R.Evaluate(dnaVal, newColor.r);
						newColor.g = colorSets[i].colorModifier.G.Evaluate(dnaVal, newColor.g);
						newColor.b = colorSets[i].colorModifier.B.Evaluate(dnaVal, newColor.b);
						newColor.a = colorSets[i].colorModifier.A.Evaluate(dnaVal, newColor.a);

						newColor = Color.Lerp(currentColor, newColor, masterWeightCalc);
						Color32 newColor32 = newColor;

						overlay.SetColor(colorSets[i].colorChannel, newColor32);

						if (!_changedColors.ContainsKey(colorSets[i].overlayEntryName))
						{
							_changedColors.Add(colorSets[i].overlayEntryName, new Dictionary<int, Color32>());
						}
						if (!_changedColors[colorSets[i].overlayEntryName].ContainsKey(colorSets[i].colorChannel))
						{
							_changedColors[colorSets[i].overlayEntryName].Add(colorSets[i].colorChannel, newColor32);
						}
						else
						{
							_changedColors[colorSets[i].overlayEntryName][colorSets[i].colorChannel] = newColor;
						}
						break;
					}
				}
				if (found)
				{
					//let generator know we made changes
					umaData.isTextureDirty = true;
					umaData.isAtlasDirty = true;
				}
				else
				{
					Debug.LogWarning(colorSets[i].overlayEntryName + " was not found on the avatar");
				}
			}
			_dnaApplied = true;
		}

		/// <summary>
		/// Updates any previously referenced sharedColors to the latest version and ensures the overlay is using a non-shared version
		/// </summary>
		private void UpdateReferencedColorDatas(SlotData[] slotDataList)
		{
			if (_referenceColorDatas.Count > 0)
			{
				var updatedRefs = new Dictionary<string, OverlayColorData>();
				foreach (KeyValuePair<string, OverlayColorData> kp in _referenceColorDatas)
				{
					foreach (SlotData slot in slotDataList)
					{
						OverlayData overlay = slot.GetOverlay(kp.Key);
						if (overlay != null)
						{
							if (overlay.colorData.IsASharedColor)
							{
								//the shared color was recreated on the overlay after a wardrobe change
								//store and use the new version
								updatedRefs.Add(kp.Key, overlay.colorData);
								overlay.colorData = overlay.colorData.Duplicate();
								overlay.colorData.name = OverlayColorData.UNSHARED;
							}
							else
							{
								//update the overlay with colors from the reference
								updatedRefs.Add(kp.Key, kp.Value);
								overlay.colorData = kp.Value.Duplicate();
								overlay.colorData.name = OverlayColorData.UNSHARED;
							}
							break;
						}
					}
				}
				_referenceColorDatas = updatedRefs;
			}
		}

		public override GUIContent GetPluginEntryLabel(SerializedProperty entry, SerializedObject pluginSO, int entryIndex)
		{
			if (entry != null)
			{
				return new GUIContent(entry.displayName + " Channel: [" + entry.FindPropertyRelative("colorChannel").intValue + "]");
			}
			return GUIContent.none;
		}

		public override bool ImportSettings(Object pluginToImport, int importMethod)
		{
			if (pluginToImport.GetType() == typeof(ColorDNAConverterPlugin))
			{
				List<DNAColorSet> thisColorSets = importMethod == 0 ? new List<DNAColorSet>(colorSets) : new List<DNAColorSet>();
				for (int i = 0; i < ((ColorDNAConverterPlugin)pluginToImport).colorSets.Length; i++)
				{
					thisColorSets.Add(((ColorDNAConverterPlugin)pluginToImport).colorSets[i]);
				}
				colorSets = thisColorSets.ToArray();
				return true;
			}
			return false;
		}

		public override string PluginHelp
		{
			get
			{
				return "ColorDNA Converters convert DNA values into color changes on an overlay. You can define which channel on the overlay you wish to affect to achieve things like changing the diffuse color, fading normal maps in and out, making a character more or less metallic and so forth. The changes do not change 'Shared Colors' but if the overlay was using a shared color, any changes to that will be respected.";
			}
		}

	}
}