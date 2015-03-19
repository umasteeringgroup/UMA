using UnityEngine;
using System.Collections;

namespace UMA
{
	public class RandomizeDNASlotScript : MonoBehaviour 
	{
		public void OnCharacterBegun(UMAData umaData)
		{
			UMADnaBase[] allDNA = umaData.GetAllDna();
			for (int i = 0; i < allDNA.Length; i++)
			{
				int valueCount = allDNA[i].Count;
				for (int j = 0; j < valueCount; j++)
				{
					allDNA[i].SetValue(j, Random.value);
				}
			}
		}
	}
}