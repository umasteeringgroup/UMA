using UnityEngine;
using System;
using System.Collections.Generic;

[Serializable]
public class RaceData : ScriptableObject {
    public string raceName;
    public GameObject racePrefab;
    public DnaConverterBehaviour[] dnaConverterList = new DnaConverterBehaviour[0];
    public String[] AnimatedBones = new string[0];
    public Dictionary<Type, System.Action<UMAData>> raceDictionary = new Dictionary<Type, System.Action<UMAData>>();
    public UmaTPose TPose;

    void Awake()
    {
        UpdateDictionary();
    }

    public void UpdateDictionary()
    {
        raceDictionary.Clear();
        for (int i = 0; i < dnaConverterList.Length; i++)
        {
            if (dnaConverterList[i])
            {
                if (!raceDictionary.ContainsKey(dnaConverterList[i].DNAType))
                {
                    raceDictionary.Add(dnaConverterList[i].DNAType, dnaConverterList[i].ApplyDnaAction);
                }
            }
        }
    }

    internal void UpdateAnimatedBones()
    {
        throw new NotImplementedException();
    }
}