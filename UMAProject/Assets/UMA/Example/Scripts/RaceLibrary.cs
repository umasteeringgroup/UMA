using UnityEngine;
using System.Collections.Generic;
using System;
using UMA;


public class RaceLibrary : RaceLibraryBase {
    public RaceData[] raceElementList = new RaceData[0];
    private Dictionary<string, RaceData> raceDictionary;

    void Awake(){
		ValidateDictionary();
	}

	private void ValidateDictionary()
	{
		if (raceDictionary == null)
		{
			raceDictionary = new Dictionary<string, RaceData>();
			UpdateDictionary();
		}
	}

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

	override public void AddRace(RaceData race)
    {
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

	override public RaceData GetRace(string raceName)
    {
		ValidateDictionary();
		RaceData res;
		if (!raceDictionary.TryGetValue(raceName, out res))
		{
			Debug.LogError("Could not find race: " + raceName);
		}
        return res;
    }

	override public RaceData GetRace(int raceHash)
	{
		ValidateDictionary();

		foreach (string name in raceDictionary.Keys) {
			int hash = UMASkeleton.StringToHash(name);

			if (hash == raceHash) {
				return raceDictionary[name];
			}
		}

		Debug.LogError("Could not find race: " + raceHash);
		return null;
	}
}
