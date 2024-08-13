using UnityEngine;
using UnityEngine.UI;

namespace UMA.Examples
{
    public class BlendShapeDnaSlider : MonoBehaviour
	{
		public int dnaTypeHash = 386317366;
		public string dnaName = "";
		public Text statusText;

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
                {
                    slider.value = dna.GetValue(dnaEntryIndex);
                }
            }
		}

		public void SetMorph(float value)
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

		public void BakeMorph( bool isBaked )
		{
			if (dna == null)
            {
                dna = data.GetDna(dnaTypeHash);
            }

            if (dna != null && dnaEntryIndex >= 0)
			{				
				data.SetBlendShapeData(dnaName, isBaked, true);

				if (statusText != null)
				{
					if( isBaked )
                    {
                        statusText.text = "(Baked)";
                    }
                    else
                    {
                        statusText.text = "(Unbaked)";
                    }
                }
			}
        }
	}
}
