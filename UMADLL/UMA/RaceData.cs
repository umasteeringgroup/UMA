using UnityEngine;
using System;
using System.Collections.Generic;


namespace UMA
{
	[Serializable]
	public class RaceData : ScriptableObject {
	    public string raceName;
	    public GameObject racePrefab;
	    public DnaConverterBehaviour[] dnaConverterList = new DnaConverterBehaviour[0];

		[Obsolete("AnimatedBones is deprecated, use animatedBones from baseSlot.", false)]
		public String[] AnimatedBones = new string[0];
		public SlotData baseSlot = null;

		public Dictionary<Type, DnaConverterBehaviour.DNAConvertDelegate> raceDictionary = new Dictionary<Type, DnaConverterBehaviour.DNAConvertDelegate>();
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
}