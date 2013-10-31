using UnityEngine;
using System.Collections.Generic;
using System;

public class RaceLibrary : MonoBehaviour {
    public RaceData[] raceElementList = new RaceData[0];
    public Dictionary<string, RaceData> raceDictionary = new Dictionary<string, RaceData>();

    void Awake(){
        UpdateDictionary();
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

    internal RaceData GetRace(string raceName)
    {
        return raceDictionary[raceName];
    }
}
