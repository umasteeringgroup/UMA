using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
    /// <summary>
    /// Base class for DNA converters.
    /// </summary>
    public abstract class DynamicDNAConverterBehaviourBase : DnaConverterBehaviour
    {
    	public DynamicUMADnaAsset dnaAsset;

    	public static void FixUpUMADnaToDynamicUMADna(UMAData.UMARecipe _recipe)
        {
			var recipeDNA = _recipe.GetAllDna();

			for (int i = 0; i < recipeDNA.Length; i++)
			{
				if (!_recipe.raceData.raceDictionary.ContainsKey(recipeDNA[i].GetType()))
				{
					int dnaToImport = recipeDNA[i].Count;
					int dnaImported = 0;

					for (int j = 0; j < recipeDNA.Length; j++)
					{
						if (recipeDNA[j] is DynamicUMADnaBase)
						{
							// Keep trying to find a new home for DNA values until they have all been set
							dnaImported += ((DynamicUMADnaBase)recipeDNA[j]).ImportUMADnaValues(recipeDNA[i]);
							if (dnaImported >= dnaToImport)
								break;
						}
					}

					if (dnaImported > 0)
					{
						_recipe.RemoveDna(recipeDNA[i].GetType());
					}
				}
			 }
		}
    }
}
