using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Serialization;
using AdjustmentType = UMA.OverlayData.ColorComponentAdjuster.AdjustmentType;

namespace UMA
{
	public class ColorDNAConverterPlugin : DynamicDNAPlugin
	{

		[System.Serializable]
		public class DNAColorSet
		{
			public enum Mode
			{
				Overlay,
				SharedColor
			}
			[Tooltip("A Color DNA Converter can target a specific overlay or a sharedColor")]
			public Mode mode = Mode.Overlay;
			[FormerlySerializedAs("overlayEntryName")]
			public string targetName;
			[Tooltip("Texture Channel: For example PBR, 0 = Albedo, 1 = Normal, 2 = Metallic")]
			[FormerlySerializedAs("colorChannel")]
			public int textureChannel = 0;
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
				mode = other.mode;
				targetName = other.targetName;
				textureChannel = other.textureChannel;
				colorModifier = new DNAColorModifier(other.colorModifier);
				modifyingDNA = new DNAEvaluatorList(other.modifyingDNA);
			}

			public bool EvaluateAndApplyAdjustments(UMADnaBase activeDNA, float masterWeight, List<OverlayData> targetOverlays)
			{
				var dnaVal = modifyingDNA.Evaluate(activeDNA);
				float rAdj = 0f;
				float gAdj = 0f;
				float bAdj = 0f;
				float aAdj = 0f;
				float rCurr = 0f;
				float gCurr = 0f;
				float bCurr = 0f;
				float aCurr = 0f;
				OverlayColorData ocd;
				for (int oi = 0; oi < targetOverlays.Count; oi++)
				{
					ocd = targetOverlays[oi].colorData;
					if (colorModifier.R.enable)
					{
						rCurr = colorModifier.R.Additive ? ocd.channelAdditiveMask[textureChannel].r : ocd.channelMask[textureChannel].r;
						rAdj = colorModifier.R.EvaluateAdjustment(dnaVal, rCurr);
						if (colorModifier.R.Absolute)
							rAdj = Mathf.Lerp(rCurr, rAdj, masterWeight);
						else
							rAdj = rAdj * masterWeight;
						if ((colorModifier.R.Absolute && rAdj != 0) || (!colorModifier.R.Absolute && rAdj != rCurr))
							targetOverlays[oi].colorComponentAdjusters.Add(new OverlayData.ColorComponentAdjuster(textureChannel, 0, rAdj, colorModifier.R.adjustmentType));
					}
					if (colorModifier.G.enable)
					{
						gCurr = colorModifier.G.Additive ? ocd.channelAdditiveMask[textureChannel].g : ocd.channelMask[textureChannel].g;
						gAdj = colorModifier.G.EvaluateAdjustment(dnaVal, gCurr);
						if (colorModifier.G.Absolute)
							gAdj = Mathf.Lerp(gCurr, gAdj, masterWeight);
						else
							gAdj = gAdj * masterWeight;
						if ((colorModifier.G.Absolute && gAdj != 0) || (!colorModifier.G.Absolute && gAdj != gCurr))
							targetOverlays[oi].colorComponentAdjusters.Add(new OverlayData.ColorComponentAdjuster(textureChannel, 1, gAdj, colorModifier.G.adjustmentType));
					}
					if (colorModifier.B.enable)
					{
						bCurr = colorModifier.B.Additive ? ocd.channelAdditiveMask[textureChannel].b : ocd.channelMask[textureChannel].b;
						bAdj = colorModifier.B.EvaluateAdjustment(dnaVal, bCurr);
						if (colorModifier.B.Absolute)
							bAdj = Mathf.Lerp(bCurr, bAdj, masterWeight);
						else
							bAdj = bAdj * masterWeight;
						if ((colorModifier.B.Absolute && bAdj != 0) || (!colorModifier.B.Absolute && bAdj != bCurr))
							targetOverlays[oi].colorComponentAdjusters.Add(new OverlayData.ColorComponentAdjuster(textureChannel, 2, bAdj, colorModifier.B.adjustmentType));
					}
					if (colorModifier.A.enable)
					{
						aCurr = colorModifier.A.Additive ? ocd.channelAdditiveMask[textureChannel].a : ocd.channelMask[textureChannel].a;
						aAdj = colorModifier.A.EvaluateAdjustment(dnaVal, aCurr);
						if (colorModifier.A.Absolute)
							aAdj = Mathf.Lerp(aCurr, aAdj, masterWeight);
						else
							aAdj = aAdj * masterWeight;
						if ((colorModifier.A.Absolute && aAdj != 0) || (!colorModifier.A.Absolute && aAdj != aCurr))
							targetOverlays[oi].colorComponentAdjusters.Add(new OverlayData.ColorComponentAdjuster(textureChannel, 3, aAdj, colorModifier.A.adjustmentType));
					}
				}
				return true;
			}

			[System.Serializable]
			public class DNAColorComponent
			{
				[Tooltip("Change this component of the color")]
				public bool enable = true;
				[Tooltip("If Absolute the setting overrides the value of the component of the color. If Adjust, the setting is added to the value of the component of the color. Use BlendFactor to completely fade a texture in and out")]
				public AdjustmentType adjustmentType = AdjustmentType.Absolute;
				[Tooltip("If true the evaluated DNA value will be used when setting the color value")]
				public bool useDNAValue = false;
				[Tooltip("The value for this component of the color")]
				[Range(0f, 1f)]
				public float value;
				[Tooltip("The amount to adjust this component of the color by. This can be negative, for example value of -0.5 on the red component would turn an incoming color of (1,1,1,1) into (0.5f,1,1,1)")]
				[Range(-1f, 1f)]
				public float adjustValue;
				[Tooltip("A multiplier to apply to the evaluated dnaValue. This allows you to use the same dna to affect components of the color by different amounts")]
				public float multiplier = 1f;

				public bool Additive
				{
					get { return adjustmentType == AdjustmentType.AbsoluteAdditive || adjustmentType == AdjustmentType.AdjustAdditive; }
				}

				public bool Absolute
				{
					get { return adjustmentType == AdjustmentType.Absolute || adjustmentType == AdjustmentType.AbsoluteAdditive; }
				}

				public DNAColorComponent() { }

				public DNAColorComponent(DNAColorComponent other)
				{
					enable = other.enable;
					adjustmentType = other.adjustmentType;
					useDNAValue = other.useDNAValue;
					value = other.value;
					adjustValue = other.adjustValue;
					multiplier = other.multiplier;
				}

				public float Evaluate(float dnaValue, float current)
				{
					if (!enable)
						return current;

					float newVal = current;
					if (useDNAValue)
					{
						if (adjustmentType == AdjustmentType.Absolute || adjustmentType == AdjustmentType.AbsoluteAdditive)
							newVal = dnaValue * multiplier;
						else //AdjustmentType.Adjust
							newVal = Mathf.Clamp(current + (dnaValue * multiplier), 0, 1f);
					}
					else
					{
						if (adjustmentType == AdjustmentType.Absolute || adjustmentType == AdjustmentType.AbsoluteAdditive)
							newVal = value;
						else //AdjustmentType.Adjust
							newVal = Mathf.Clamp(current + adjustValue, 0, 1f);
					}
					return Mathf.Lerp(current, newVal, dnaValue);
				}

				public float EvaluateAdjustment(float dnaValue, float currentColor)
				{
					if (!enable)
						return 0f;

					if (useDNAValue)
					{
						if (Absolute)
							return Mathf.Lerp(currentColor, Mathf.Clamp(dnaValue * multiplier, 0f, 1f), Mathf.Clamp(dnaValue, 0f, 1f));
						else if (adjustmentType == AdjustmentType.BlendFactor)
							return Mathf.Clamp(dnaValue * multiplier, 0f, 1f);
						else
							return Mathf.Lerp(0f, dnaValue * multiplier, Mathf.Abs(dnaValue));
					}
					else
					{
						if (Absolute)
							return Mathf.Lerp(currentColor, value, Mathf.Clamp(dnaValue, 0f, 1f));
						else
							return Mathf.Lerp(0f, adjustValue, Mathf.Abs(dnaValue));
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

		private Dictionary<string, Dictionary<int, List<DNAColorSet>>> _compiledModifiers = new Dictionary<string, Dictionary<int, List<DNAColorSet>>>();

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
			//for shared color dna it may have been applied by a dna change OR a recipe/texture change so if its already been done dont do it again
			if (_dnaApplied)
				return;

			UMADnaBase activeDNA = umaData.GetDna(dnaTypeHash);
			if (activeDNA == null)
			{
				Debug.LogError("Could not get DNA values for: " + this.name);
				return;
			}
			var masterWeightCalc = masterWeight.GetWeight(activeDNA);
			if (masterWeightCalc == 0f)
				return;

			_compiledModifiers.Clear();
			CompileModifiers();

			bool needsUpdate = false;

			foreach (KeyValuePair<string, Dictionary<int, List<DNAColorSet>>> kp in _compiledModifiers)
			{
				var targetOverlays = new List<OverlayData>();
				for (int i = 0; i < umaData.umaRecipe.slotDataList.Length; i++)
				{
					var overlays = umaData.umaRecipe.slotDataList[i].GetOverlayList();
					for (int oi = 0; oi < overlays.Count; oi++)
					{
						if (overlays[oi] != null)
						{
							//we can target specific Overlays or SharedColors now
							if ((overlays[oi].colorData.IsASharedColor && overlays[oi].colorData.name == kp.Key) || overlays[oi].overlayName == kp.Key)
							{
								targetOverlays.Add(overlays[oi]);
							}
						}
					}
				}
				//loop through each channel we are changing
				foreach (KeyValuePair<int, List<DNAColorSet>> kpi in kp.Value)
				{
					for (int i = 0; i < kpi.Value.Count; i++)
					{
						if (kpi.Value[i].modifyingDNA.UsedDNANames.Count == 0)
							continue;

						if (kpi.Value[i].EvaluateAndApplyAdjustments(activeDNA, masterWeightCalc, targetOverlays))
							needsUpdate = true;
					}
				}
			}

			if (needsUpdate)
			{
				umaData.isTextureDirty = true;
				umaData.isAtlasDirty = true;
			}
			_dnaApplied = true;
		}

		private void CompileModifiers()
		{
			for (int i = 0; i < colorSets.Length; i++)
			{
				if (!_compiledModifiers.ContainsKey(colorSets[i].targetName))
					_compiledModifiers.Add(colorSets[i].targetName, new Dictionary<int, List<DNAColorSet>>());
				if (!_compiledModifiers[colorSets[i].targetName].ContainsKey(colorSets[i].textureChannel))
					_compiledModifiers[colorSets[i].targetName].Add(colorSets[i].textureChannel, new List<DNAColorSet>());
				_compiledModifiers[colorSets[i].targetName][colorSets[i].textureChannel].Add(colorSets[i]);
			}
		}

		#region DYNAMICDNAPLUGIN EDITOR OVERRIDES

#if UNITY_EDITOR

		//this could be runtime in DynamicDNAPlugin if it was ever needed
		public override bool ImportSettings(Object pluginToImport, int importMethod)
		{
			if (pluginToImport.GetType() == typeof(ColorDNAConverterPlugin))
			{
				List<DNAColorSet> thisColorSets = importMethod == 0 ? new List<DNAColorSet>(colorSets) : new List<DNAColorSet>();
				for (int i = 0; i < ((ColorDNAConverterPlugin)pluginToImport).colorSets.Length; i++)
				{
					thisColorSets.Add(new DNAColorSet(((ColorDNAConverterPlugin)pluginToImport).colorSets[i]));
				}
				colorSets = thisColorSets.ToArray();
				return true;
			}
			return false;
		}

		public override GUIContent GetPluginEntryLabel(SerializedProperty entry, SerializedObject pluginSO, int entryIndex)
		{
			if (entry != null)
			{
				List<string> usedColorProps = new List<string>();
				if (entry.FindPropertyRelative("colorModifier").FindPropertyRelative("R").FindPropertyRelative("enable").boolValue == true)
					usedColorProps.Add("R");
				if (entry.FindPropertyRelative("colorModifier").FindPropertyRelative("G").FindPropertyRelative("enable").boolValue == true)
					usedColorProps.Add("G");
				if (entry.FindPropertyRelative("colorModifier").FindPropertyRelative("B").FindPropertyRelative("enable").boolValue == true)
					usedColorProps.Add("B");
				if (entry.FindPropertyRelative("colorModifier").FindPropertyRelative("A").FindPropertyRelative("enable").boolValue == true)
					usedColorProps.Add("A");
				var usedColorComponents = string.Join(", ", usedColorProps.ToArray());
				usedColorComponents = string.IsNullOrEmpty(usedColorComponents) ? "" : "Channels: [" + usedColorComponents + "]";
				return new GUIContent("("+ entry.FindPropertyRelative("mode").enumNames[entry.FindPropertyRelative("mode").enumValueIndex] + ") "+ entry.FindPropertyRelative("targetName").stringValue + " - Texture: [" + entry.FindPropertyRelative("textureChannel").intValue + "] "+ usedColorComponents);
			}
			return GUIContent.none;
		}

		public override string PluginHelp
		{
			get
			{
				return "ColorDNA Converters convert DNA values into color changes on an overlay texture. You can define which texture on the overlay you wish to affect to achieve things like changing the diffuse color, fading normal maps in and out, making a character more or less metallic and so forth. The changes do not change 'Shared Colors' but are applied to them at the dna stage.";
			}
		}

#endif

		#endregion

	}
}