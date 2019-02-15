using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace UMA
{
    /// <summary>
    /// Base class for Legacy Dynamic DNA converter behaviours.
    /// </summary>
    public abstract class DynamicDNAConverterBehaviourBase : DnaConverterBehaviour, IDynamicDNAConverter
    {
		[SerializeField]
		[FormerlySerializedAs("dnaAsset")]
		protected DynamicUMADnaAsset _dnaAsset;

		#region IDynamicDNAConverter IMPLIMENTATION

		public DynamicUMADnaAsset dnaAsset
		{
			get { return _dnaAsset; }
			set { _dnaAsset = value; }
		}

		#endregion

		//Why do we still need this? Its for updating recipes using old dna (no type hash)
		//It tries to find a new home for the dna value in the recipe
		//but obviously I had to add it to make something work on 22/12/2018?!?
		//This method is fucked anyway because it doesn't account for the fact that a RaceData (or slotData)
		//may contain multiple converters that use the same dna...
		//Sooo maybe its ok to stay here?
		public static void FixUpUMADnaToDynamicUMADna(UMAData.UMARecipe _recipe)
        {
			var recipeDNA = _recipe.GetAllDna();
			//22/12/2018 we also need to check slots using DynamicDNA
			var slotDNABehaviours = new Dictionary<int, IDNAConverter>();
			foreach(SlotData slot in _recipe.GetAllSlots())
			{
				if (slot != null && slot.asset != null && slot.asset.slotDNA != null && !slotDNABehaviours.ContainsKey(slot.asset.slotDNA.DNATypeHash))
					slotDNABehaviours.Add(slot.asset.slotDNA.DNATypeHash, slot.asset.slotDNA);
			}
			for (int i = 0; i < recipeDNA.Length; i++)
			{
				int dnaToImport = recipeDNA[i].Count;
				int dnaImported = 0;
				//if (!_recipe.raceData.raceDictionary.ContainsKey(recipeDNA[i].GetType()))
				//A RaceData may contain multiple DynamicDnaConverters use GetConverter instead and use the hash
				//FixDNAPrefabs: A RaceData may contain multiple converters that use the same dna names now - check GetConverters instead
				if (_recipe.raceData.GetConverters(recipeDNA[i]).Length == 0 && !slotDNABehaviours.ContainsKey(recipeDNA[i].DNATypeHash))
				{
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
						if(_recipe.GetDna(recipeDNA[i].DNATypeHash) != null)
							_recipe.RemoveDna(recipeDNA[i].DNATypeHash);
					}
				}
			 }
		}
    }
}
