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

		#region FIELDS

		[FormerlySerializedAs("colorSets")]
		[SerializeField]
		private DNAColorSet[] _colorSets = new DNAColorSet[0];

		#endregion

		#region PRIVATE FIELDS

		//has dna been applied this cycle
		[System.NonSerialized]
		private List<GameObject> _dnaAppliedTo = new List<GameObject>();
		//have we added the extra listeners required by ColorDNA?
		//private lists in ScriptableObjects seem to need to be explicitly set as non serialized for some reason
		[System.NonSerialized]
		private List<GameObject> _listenersAddedTo = new List<GameObject>();

		#endregion

		#region PUBLIC PROPERTIES

		public DNAColorSet[] colorSets
		{
			get { return _colorSets; }
			set { _colorSets = value; }
		}

		public override ApplyPassOpts ApplyPass
		{
			get
			{
				return ApplyPassOpts.PrePass;
			}
		}

		public override Dictionary<string, List<int>> IndexesForDnaNames
		{
			get
			{
				var dict = new Dictionary<string, List<int>>();
				for (int i = 0; i < _colorSets.Length; i++)
				{
					for (int ci = 0; ci < _colorSets[i].UsedDNANames.Count; ci++)
					{
						if (!dict.ContainsKey(_colorSets[i].UsedDNANames[ci]))
							dict.Add(_colorSets[i].UsedDNANames[ci], new List<int>());

						dict[_colorSets[i].UsedDNANames[ci]].Add(i);
					}
				}
				return dict;
			}
		}

		#endregion

		#region PRIVATE METHODS

		private void ResetOnCharaterUpdated(UMAData umaData)
		{
			_dnaAppliedTo.Remove(umaData.gameObject);
		}

		#endregion

		#region REQUIRED DYNAMICDNAPLUGIN METHODS

		public override void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
		{
			//Add the reset listeners if we havent already
			//we need this because if 'fastGeneration' is false we may still get another loop
			//and we should not do this again if _dnaAppliedTo contains umaData.gameObject
			if (!_listenersAddedTo.Contains(umaData.gameObject))
			{
				umaData.CharacterUpdated.AddListener(ResetOnCharaterUpdated);
				_listenersAddedTo.Add(umaData.gameObject);
			}

			if(_dnaAppliedTo.Contains(umaData.gameObject))
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

			bool needsUpdate = false;

			for(int i = 0; i < _colorSets.Length; i++)
			{
				if (_colorSets[i].modifyingDNA.UsedDNANames.Count == 0 || string.IsNullOrEmpty(_colorSets[i].targetName))
					continue;
				var targetOverlays = new List<OverlayData>();
				for (int si = 0; si < umaData.umaRecipe.slotDataList.Length; si++)
				{
					var overlays = umaData.umaRecipe.slotDataList[si].GetOverlayList();
					for (int oi = 0; oi < overlays.Count; oi++)
					{
						if (overlays[oi] != null)
						{
							//we can target specific Overlays or SharedColors now
							if ((overlays[oi].colorData.IsASharedColor && overlays[oi].colorData.name == _colorSets[i].targetName) || overlays[oi].overlayName == _colorSets[i].targetName)
							{
								if(!targetOverlays.Contains(overlays[oi]))
								targetOverlays.Add(overlays[oi]);
							}
						}
					}
				}
				if (targetOverlays.Count == 0)
					continue;
				if (_colorSets[i].EvaluateAndApplyAdjustments(activeDNA, masterWeightCalc, targetOverlays))
					needsUpdate = true;
			}

			if (needsUpdate)
			{
				umaData.isTextureDirty = true;
				//pretty sure this doesn't affect the atlas
				//umaData.isAtlasDirty = true;
			}
			_dnaAppliedTo.Add(umaData.gameObject);
		}

		#endregion

		#region DYNAMICDNAPLUGIN EDITOR OVERRIDES

#if UNITY_EDITOR

		//this could be runtime in DynamicDNAPlugin if it was ever needed
		public override bool ImportSettings(Object pluginToImport, int importMethod)
		{
			if (pluginToImport.GetType() == typeof(ColorDNAConverterPlugin))
			{
				List<DNAColorSet> thisColorSets = importMethod == 0 ? new List<DNAColorSet>(_colorSets) : new List<DNAColorSet>();
				for (int i = 0; i < ((ColorDNAConverterPlugin)pluginToImport)._colorSets.Length; i++)
				{
					thisColorSets.Add(new DNAColorSet(((ColorDNAConverterPlugin)pluginToImport)._colorSets[i]));
				}
				_colorSets = thisColorSets.ToArray();
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
				usedColorComponents = string.IsNullOrEmpty(usedColorComponents) ? "" : "Components: [" + usedColorComponents + "]";
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

		#region SPECIAL TYPES

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
			[Tooltip("The name of the overlay or shared color to target")]
			[FormerlySerializedAs("overlayEntryName")]
			public string targetName;
			[Tooltip("Texture Channel: For example PBR, 0 = Albedo, 1 = Normal, 2 = Metallic")]
			[FormerlySerializedAs("colorChannel")]
			public int textureChannel = 0;
			[Tooltip("Define the dna that influence these changes. Note: If no dna is defined nothing will happen!")]
			public DNAEvaluatorList modifyingDNA = new DNAEvaluatorList();
			[Tooltip("Define how you want to change the colors used on this overlay")]
			public DNAColorModifier colorModifier = new DNAColorModifier();

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
				//Color modifiers can be costly so only return true if anything actually changed
				bool adjusted = false;
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
						if ((colorModifier.R.Absolute && rAdj != 0 && rAdj != rCurr) || (!colorModifier.R.Absolute && rAdj != rCurr))
						{
							targetOverlays[oi].colorComponentAdjusters.Add(new OverlayData.ColorComponentAdjuster(textureChannel, 0, rAdj, colorModifier.R.adjustmentType));
							adjusted = true;
						}
					}
					if (colorModifier.G.enable)
					{
						gCurr = colorModifier.G.Additive ? ocd.channelAdditiveMask[textureChannel].g : ocd.channelMask[textureChannel].g;
						gAdj = colorModifier.G.EvaluateAdjustment(dnaVal, gCurr);
						if (colorModifier.G.Absolute)
							gAdj = Mathf.Lerp(gCurr, gAdj, masterWeight);
						else
							gAdj = gAdj * masterWeight;
						if ((colorModifier.G.Absolute && gAdj != 0 && gAdj != gCurr) || (!colorModifier.G.Absolute && gAdj != gCurr))
						{
							targetOverlays[oi].colorComponentAdjusters.Add(new OverlayData.ColorComponentAdjuster(textureChannel, 1, gAdj, colorModifier.G.adjustmentType));
							adjusted = true;
						}
					}
					if (colorModifier.B.enable)
					{
						bCurr = colorModifier.B.Additive ? ocd.channelAdditiveMask[textureChannel].b : ocd.channelMask[textureChannel].b;
						bAdj = colorModifier.B.EvaluateAdjustment(dnaVal, bCurr);
						if (colorModifier.B.Absolute)
							bAdj = Mathf.Lerp(bCurr, bAdj, masterWeight);
						else
							bAdj = bAdj * masterWeight;
						if ((colorModifier.B.Absolute && bAdj != 0 && bAdj != bCurr) || (!colorModifier.B.Absolute && bAdj != bCurr))
						{
							targetOverlays[oi].colorComponentAdjusters.Add(new OverlayData.ColorComponentAdjuster(textureChannel, 2, bAdj, colorModifier.B.adjustmentType));
							adjusted = true;
						}
					}
					if (colorModifier.A.enable)
					{
						aCurr = colorModifier.A.Additive ? ocd.channelAdditiveMask[textureChannel].a : ocd.channelMask[textureChannel].a;
						aAdj = colorModifier.A.EvaluateAdjustment(dnaVal, aCurr);
						if (colorModifier.A.Absolute)
							aAdj = Mathf.Lerp(aCurr, aAdj, masterWeight);
						else if(colorModifier.A.adjustmentType == AdjustmentType.BlendFactor)//BlendFactor is different and only used on the alpha channel
							aAdj = Mathf.Lerp(aCurr, aAdj * masterWeight, masterWeight);
						else
							aAdj = aAdj * masterWeight;
						if ((colorModifier.A.Absolute && aAdj != 0 && aAdj != aCurr) || (!colorModifier.A.Absolute && aAdj != aCurr))
						{
							targetOverlays[oi].colorComponentAdjusters.Add(new OverlayData.ColorComponentAdjuster(textureChannel, 3, aAdj, colorModifier.A.adjustmentType));
							adjusted = true;
						}
					}
				}
				return adjusted;
			}

			[System.Serializable]
			public class DNAColorComponent
			{
				[Tooltip("Change this component of the color")]
				public bool enable = true;
				[Tooltip("If Absolute the setting overrides the value of the component of the color. If Adjust, the setting is added to the value of the component of the color. Use BlendFactor to completely fade a texture in and out")]
				public AdjustmentType adjustmentType = AdjustmentType.Absolute;
				[Tooltip("If true the evaluated DNA value will be used when setting the color value for this component of the color")]
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
						else if (adjustmentType == AdjustmentType.BlendFactor)
							return Mathf.Lerp(0f, value, Mathf.Abs(dnaValue));
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

#pragma warning disable 0414
				//used in the editor for the preview tools
				[SerializeField]
				private float _testDNAVal = 0f;
#pragma warning restore 0414

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

		#endregion

	}
}