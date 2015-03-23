using UnityEngine;
using System.Collections;


namespace UMA
{
	[System.Serializable]
	public class DNARangeAsset : ScriptableObject
	{
		public DnaConverterBehaviour dnaConverter;

		public float[] means;
		public float[] deviations;
		public float[] spreads;

		private float[] values;

		public void RandomizeDNA(UMAData data)
		{
			if (dnaConverter == null)
				return;
			
			UMADnaBase dna = data.GetDna(dnaConverter.DNAType);
			if (dna == null)
				return;

			int entryCount = dna.Count;
			if (means.Length != entryCount)
			{
				Debug.LogWarning("Range settings out of sync with DNA, cannot apply!");
				return;
			}

			if ((values == null) || (values.Length != entryCount))
				values = new float[entryCount];

			for (int i = 0; i < entryCount; i++)
			{
				values[i] = means[i] + (Random.value - 0.5f) * spreads[i];
			}

			dna.Values = values;
		}

		public void RandomizeDNAGaussian(UMAData data)
		{
			if (dnaConverter == null)
				return;

			UMADnaBase dna = data.GetDna(dnaConverter.DNAType);
			if (dna == null)
				return;
			
			int entryCount = dna.Count;
			if (means.Length != entryCount)
			{
				Debug.LogWarning("Range settings out of sync with DNA, cannot apply!");
				return;
			}
			
			if (values == null)
				values = new float[entryCount];
			
			for (int i = 0; i < entryCount; i++)
			{
				values[i] = UMAUtils.GaussianRandom(means[i], deviations[i]);
			}
			
			dna.Values = values;
		}
	}
}
