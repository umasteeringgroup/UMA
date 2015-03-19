using UnityEngine;
using System.Collections;

namespace UMA
{
	public class RandomizeDNASlotScript : MonoBehaviour 
	{
		public void OnCharacterBegun(UMAData umaData)
		{
			var markerDNA = umaData.GetDna<RandomizeMarkerDNA>();
			if (markerDNA == null)
			{
				UMADnaBase[] allDNA = umaData.GetAllDna();
				for (int i = 0; i < allDNA.Length; i++)
				{
					int valueCount = allDNA[i].Count;
					for (int j = 0; j < valueCount; j++)
					{
						allDNA[i].SetValue(j, UMAUtils.GaussianRandom(0.5f, 0.16f));
					}
				}

				umaData.umaRecipe.AddDna(new RandomizeMarkerDNA());
				umaData.Dirty(true, false, false);
			}
		}
	}

	public class RandomizeMarkerDNA : UMADnaBase
	{
		private float randomizedDNA = 1f;
		public override float GetValue(int idx)
		{
			return randomizedDNA;
		}
		public override void SetValue(int idx, float value)
		{
			randomizedDNA = value;
		}
		public override int Count
		{
			get {return 1;}
		}
		public override float[] Values
		{
			get {return new float[] {randomizedDNA};}
			set {randomizedDNA = value[0];}
		}
		public override string[] Names
		{
			get {return new string[] {"RandomizedDNA"};}
		}
	}
	
}