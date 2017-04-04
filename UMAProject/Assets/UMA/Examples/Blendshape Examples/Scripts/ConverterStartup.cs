using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA;
using UMA.CharacterSystem;

namespace UMA.Examples
{
	public class ConverterStartup : MonoBehaviour
	{
		public DynamicCharacterAvatar avatar;
		public BlendShapeDnaConverterBehaviour converter;

		private DnaConverterBehaviour[] oldConverters;

		public void AddConverter(UMAData umaData )
		{
			oldConverters = umaData.umaRecipe.raceData.dnaConverterList;

			umaData.umaRecipe.raceData.dnaConverterList = new DnaConverterBehaviour[1];
			umaData.umaRecipe.raceData.dnaConverterList [0] = converter;
			umaData.umaRecipe.raceData.UpdateDictionary ();
		}

		public void ResetConverters(UMAData umaData)
		{
			umaData.umaRecipe.raceData.dnaConverterList = oldConverters;
			umaData.umaRecipe.raceData.UpdateDictionary ();
		}
	}
}
