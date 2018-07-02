using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UMA;
using UMA.CharacterSystem;

namespace UMA.Examples
{
	public class BlendShapeDnaSlider : MonoBehaviour
	{
		public int dnaTypeHash = 386317366;
		public string dnaName = "";

		protected UMAData data;
		protected UMADnaBase dna;
		int dnaEntryIndex = -1;

		public void OnCharacterCreated(UMAData umaData)
		{
			this.data = umaData;
			Slider slider = gameObject.GetComponent<Slider>();

			dna = umaData.GetDna(dnaTypeHash);
			if (dna != null)
			{
				string[] dnaNames = dna.Names;
				for(int i = 0; i < dnaNames.Length; i++)
				{
					if (dnaName == dnaNames[i])
					{
						dnaEntryIndex = i;
						break;
					}
				}

				if(dnaEntryIndex >= 0)
					slider.value = dna.GetValue(dnaEntryIndex);
			}
		}

		public void SetMorph(float value)
		{
			if (dna == null)
				dna = data.GetDna(dnaTypeHash);

			if (dna != null)
			{
				dna.SetValue(dnaEntryIndex, value);
				data.Dirty(true, false, false);
			}
		}

        public void BakeMorph()
        {
			if (dna == null)
				dna = data.GetDna(dnaTypeHash);

			if (dna != null && dnaEntryIndex >= 0)
            {
				if( data.blendShapeSettings.bakeBlendShapes == null)
                	data.blendShapeSettings.bakeBlendShapes = new Dictionary<string, float>();
				
                float dnaValue = dna.GetValue(dnaEntryIndex);
                float morphWeight = 0.0f;
                if (dnaValue > 0.51f)
                {
                    morphWeight = (dnaValue - 0.5f) * 2f;
                }

				if(!data.blendShapeSettings.bakeBlendShapes.ContainsKey(dnaName))
					data.blendShapeSettings.bakeBlendShapes.Add(dnaName, morphWeight );
				
                data.Dirty(true, true, true);
            }
        }

        public void UnbakeMorph()
        {
			if (data.blendShapeSettings != null)
			{
				if (data.blendShapeSettings.bakeBlendShapes.ContainsKey(dnaName))
				{
					data.blendShapeSettings.bakeBlendShapes.Remove(dnaName);
					data.Dirty(true, true, true);
				}
			}
        }
	}
}
