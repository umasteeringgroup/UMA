using UnityEngine;
using System.Collections.Generic;
using System;
using UMA;


public class RaceLibrary : MonoBehaviour {
    public RaceData[] raceElementList = new RaceData[0];
    public Dictionary<string, RaceData> raceDictionary = new Dictionary<string, RaceData>();

    void Awake(){
        UpdateDictionary();
    }

	private void ValidateDictionary()
	{
		if (raceDictionary == null)
		{
			raceDictionary = new Dictionary<string, RaceData>();
			UpdateDictionary();
		}
	}
	


    public void UpdateDictionary(){
        raceDictionary.Clear();
        for (int i = 0; i < raceElementList.Length; i++){
            if (raceElementList[i]){
                if (!raceDictionary.ContainsKey(raceElementList[i].raceName)){
                    raceDictionary.Add(raceElementList[i].raceName, raceElementList[i]);
                }
            }
        }
    }

    public void AddRace(RaceData race)
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

	public RaceData GetRace(string raceName)
    {
		ValidateDictionary();
		RaceData res;
		if (!raceDictionary.TryGetValue(raceName, out res))
		{
			Debug.LogError("Could not find race: " + raceName);
		}
        return res;
    }
}
