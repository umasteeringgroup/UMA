using UnityEngine;
using System.Collections.Generic;
using System;

namespace UMA
{
	public class RaceLibrary : RaceLibraryBase
	{
		[SerializeField]
		protected RaceDataAsset[] raceElementList = new RaceDataAsset[0];
		private Dictionary<string, RaceDataAsset> raceDictionary;

		void Awake(){
			ValidateDictionary();
		}

		public override void ValidateDictionary()
		{
			if (raceDictionary == null)
			{
				raceDictionary = new Dictionary<string, RaceDataAsset>();
				UpdateDictionary();
			}
		}

	#pragma warning disable 618
		override public void UpdateDictionary()
		{
			ValidateDictionary();
			raceDictionary.Clear();
			for (int i = 0; i < raceElementList.Length; i++){
				if (raceElementList[i]){
					if (!raceDictionary.ContainsKey(raceElementList[i].raceName)){
						raceDictionary.Add(raceElementList[i].raceName, raceElementList[i]);
					}
				}
			}
		}

		override public void AddRace(RaceDataAsset race)
		{
			if (race == null) return;

			ValidateDictionary();
			for (int i = 0; i < raceElementList.Length; i++)
			{
				if (raceElementList[i].raceName == race.raceName)
				{
					raceElementList[i] = race;
					return;
				}
			}
			var list = new RaceDataAsset[raceElementList.Length + 1];
			Array.Copy(raceElementList, list, raceElementList.Length );
			list[raceElementList.Length] = race;
			raceElementList = list;
			raceDictionary.Add(race.raceName, race);
		}
	#pragma warning restore 618

		override public RaceDataAsset GetRace(string raceName)
		{
			if ((raceName == null) || (raceName.Length == 0))
				return null;

			ValidateDictionary();
			RaceDataAsset res;
			if (!raceDictionary.TryGetValue(raceName, out res))
			{
				return null;
			}
			return res;
		}

		override public RaceDataAsset GetRace(int raceHash)
		{
			if (raceHash == 0)
				return null;

			ValidateDictionary();

			foreach (string name in raceDictionary.Keys) {
				int hash = UMAUtils.StringToHash(name);

				if (hash == raceHash) {
					return raceDictionary[name];
				}
			}

			return null;
		}

		public override RaceDataAsset[] GetAllRaces()
		{
	#pragma warning disable 618
			return raceElementList;
	#pragma warning restore 618
		}
	}
}
