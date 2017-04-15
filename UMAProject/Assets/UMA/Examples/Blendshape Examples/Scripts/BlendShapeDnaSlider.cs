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
		public int dnaEntryIndex = 0;

		protected UMAData data;
		protected UMADnaBase dna;

		public void OnCharacterCreated(UMAData umaData)
		{
			this.data = umaData;
			Slider slider = gameObject.GetComponent<Slider>();

			dna = umaData.GetDna(dnaTypeHash);		
			if (dna != null)
			{
				slider.value = dna.GetValue(dnaEntryIndex);
			}
		}

		public void SetEarMorph(float value)
		{
			if (dna == null)
			{
				dna = data.GetDna(dnaTypeHash);		
			}

			if (dna != null)
			{
				dna.SetValue(dnaEntryIndex, value);
				data.Dirty(true, false, false);
			}
		}

        public void BakeEarMorph()
        {
            if (dna == null)
            {
                dna = data.GetDna(dnaTypeHash);     
            }

            if (dna != null)
            {
                data.blendShapeSettings.bakeBlendShapes = new Dictionary<string, float>();
                float dnaValue = dna.GetValue(dnaEntryIndex);
                float morphWeight = 0.0f;
                if (dnaValue > 0.51f)
                {
                    morphWeight = (dnaValue - 0.5f) * 2f;
                }
                data.blendShapeSettings.bakeBlendShapes.Add("MaleElvenEars", morphWeight );
                data.Dirty(true, true, true);
            }
        }

        public void UnbakeEarMorph()
        {
            data.blendShapeSettings.bakeBlendShapes.Clear();
            data.Dirty(true, true, true);
        }
	}
}
