using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace UMA {
	public class ColorDNAConverterPlugin : DynamicDNAPlugin
	{

		[System.Serializable]
		public class DNAColorSet
		{
			//public string dnaEntryName;
			public string overlayEntryName;
			[Tooltip("Color Channel: For example PBR, 0 = Albedo, 1 = Normal, 2 = Metallic")]
			public int colorChannel = 0;
			//when changing the diffuse color of an overlay you might want to change its actual color and or change its opacity
			public bool changeColor = true;
			public Color32 color = new Color(1, 1, 1, 0);
			public bool changeAlpha = true;
			[Range(0f, 1f)]
			public float alpha = 1f;
			public DNAEvaluatorList modifyingDNA = new DNAEvaluatorList();

			private Color32 _currentColor;
			private bool _currentColorStored = false;

			public bool currentColorStored
			{
				get { return _currentColorStored; }
			}
			public Color32 currentColor
			{
				get { return _currentColor; }
				set { _currentColor = value;
					_currentColorStored = true;
				}
			}

			public List<string> UsedDNANames
			{
				get
				{
					return modifyingDNA.UsedDNANames;
				}
			}
		}

		public DNAColorSet[] colorSets = new DNAColorSet[0];

		private Dictionary<string, Color> _changedColors = new Dictionary<string, Color>();

		public override Dictionary<string, List<int>> IndexesForDnaNames
		{
			get
			{
				var dict = new Dictionary<string, List<int>>();
				for (int i= 0; i < colorSets.Length; i++)
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

		public override void ApplyDNA(UMAData umaData, UMASkeleton skeleton, int dnaTypeHash)
		{
			_changedColors.Clear();
			UMADnaBase activeDNA = umaData.GetDna(dnaTypeHash);
			if (activeDNA == null)
			{
				Debug.LogError("Could not get DNA values for: " + this.name);
				return;
			}
			var masterWeightCalc = masterWeight.GetWeight(activeDNA);
			if (masterWeightCalc == 0)
				return;

			float[] dnaValues = activeDNA.Values;
			string[] dnaNames = activeDNA.Names;

			for (int i = 0; i < colorSets.Length; i++)
			{
				var dnaVal = colorSets[i].modifyingDNA.Evaluate(activeDNA);
				bool found = false;
				foreach (SlotData slot in umaData.umaRecipe.slotDataList)
				{
					OverlayData overlay = slot.GetOverlay(colorSets[i].overlayEntryName);
					if (overlay != null)
					{
						found = true;
						//Color newColor = Color.Lerp(colorSets[i].minColor, colorSets[i].maxColor, dnaValues[i]);//assumes a dna evaluator of Raw
						//The above isn't right
						//what we need to do here is change from the UNMODIFIED color to the new color
						//but for that the unmodified color needs to be stored somewhere, cos we are going to change the overlayColor
						//colorDNA is kind of based on the premise that you will only be using the color to fade new overlays in and out
						//but what if all you want to do is fade in and out a color CHANGE on an EXISTING overlay
						//I know there are other ways already to do this, setting the shared color on the dynamicAvatar for example
						//but this whole plugins thing is about making these changes possible with dna
						if (!colorSets[i].currentColorStored)
						{
							colorSets[i].currentColor = overlay.GetColor(colorSets[i].colorChannel);
						}
						var currentColor = colorSets[i].currentColor;
						//now the problem is that another item further up the list (or elsewhere) might have changed the color and we dont want to undo its changes
						if (_changedColors.ContainsKey(colorSets[i].overlayEntryName))
							currentColor = _changedColors[colorSets[i].overlayEntryName];
						var newColor = currentColor;
						if (colorSets[i].changeColor)
						{
							newColor.r = colorSets[i].color.r;
							newColor.g = colorSets[i].color.g;
							newColor.b = colorSets[i].color.b;
						}
						if (colorSets[i].changeAlpha)
						{
							//I'm sure im just being stupid here, please rewrite if theres a nicer way
							Color alpha = new Color(0, 0, 0, colorSets[i].alpha);
							Color32 alpha32 = alpha;
							newColor.a = alpha32.a;
						}
						//how much do we change the color based on dna
						newColor = Color.Lerp(currentColor, newColor, dnaVal);
						//how much to we change the color based on masterweight
						newColor = Color.Lerp(currentColor, newColor, masterWeightCalc);
						overlay.SetColor(colorSets[i].colorChannel, newColor);
						if (!_changedColors.ContainsKey(colorSets[i].overlayEntryName))
						{
							_changedColors.Add(colorSets[i].overlayEntryName, newColor);
						}
					}
				}
				if (found)
				{
					//Should this be here or require the use to set it after setting DNA?
					umaData.isTextureDirty = true;
					umaData.isAtlasDirty = true;
				}
				else
				{
					Debug.LogWarning(colorSets[i].overlayEntryName + " was not found on the avatar");
				}
			}
		}

	}
}
