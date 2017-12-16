using UnityEngine;
using System.Collections.Generic;
using System;

namespace UMA
{
	public class RaceLibrary : RaceLibraryBase
	{
		[SerializeField]
		protected RaceData[] raceElementList = new RaceData[0];
		private Dictionary<string, RaceData> raceDictionary;

		void Awake(){
			ValidateDictionary();
		}

		public override void ValidateDictionary()
		{
			if (raceDictionary == null)
			{
				raceDictionary = new Dictionary<string, RaceData>();
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
					raceElementList[i].UpdateDictionary();
					if (!raceDictionary.ContainsKey(raceElementList[i].raceName)){
						raceDictionary.Add(raceElementList[i].raceName, raceElementList[i]);
					}
				}
			}
		}

		override public void AddRace(RaceData race)
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
			var list = new RaceData[raceElementList.Length + 1];
			Array.Copy(raceElementList, list, raceElementList.Length );
			list[raceElementList.Length] = race;
			raceElementList = list;
			raceDictionary.Add(race.raceName, race);
		}
#pragma warning restore 618

		public override RaceData HasRace(string raceName)
		{
			if ((raceName == null) || (raceName.Length == 0))
				return null;

			ValidateDictionary();
			RaceData res;
			if (!raceDictionary.TryGetValue(raceName, out res))
			{
				return null;
			}
			return res;
		}

		public override RaceData HasRace(int raceHash)
		{
			if (raceHash == 0)
				return null;

			ValidateDictionary();

			foreach (string name in raceDictionary.Keys)
			{
				int hash = UMAUtils.StringToHash(name);

				if (hash == raceHash)
				{
					return raceDictionary[name];
				}
			}

			return null;
		}

		override public RaceData GetRace(string raceName)
		{
			if ((raceName == null) || (raceName.Length == 0))
				return null;

			ValidateDictionary();
			RaceData res;
			if (!raceDictionary.TryGetValue(raceName, out res))
			{
				return null;
			}
			return res;
		}

		override public RaceData GetRace(int raceHash)
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

		public override RaceData[] GetAllRaces()
		{
	#pragma warning disable 618
			return raceElementList;
	#pragma warning restore 618
		}
	}
}
