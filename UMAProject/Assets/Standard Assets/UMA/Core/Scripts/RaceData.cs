using UnityEngine;
using System;
using System.Collections.Generic;


namespace UMA
{
	/// <summary>
	/// Data for a UMA "race".
	/// </summary>
	/// <remarks>
	/// A "race" in UMA is nothing more than a specific TPose and set of DNA
	/// converters. For example there are RaceData entries for Male Humans and
	/// Female Humans, because they have slightly different TPoses and gender
	/// specific DNA converters, despite sharing the same DNA types.
	/// </remarks>
	[Serializable]
	public partial class RaceData : ScriptableObject
	{
	    public string raceName;
		[System.Obsolete("RaceData.racePrefab is obsolete. It is no longer used.", false)]
		public GameObject racePrefab;
		/// <summary>
		/// The set of DNA converters for modifying characters of this race.
		/// </summary>
	    public DnaConverterBehaviour[] dnaConverterList = new DnaConverterBehaviour[0];

		public Dictionary<Type, DnaConverterBehaviour.DNAConvertDelegate> raceDictionary = new Dictionary<Type, DnaConverterBehaviour.DNAConvertDelegate>();
	    
		/// <summary>
		/// The TPose data for the race rig.
		/// </summary>
		public UmaTPose TPose;
        public enum UMATarget
        {
            Humanoid,
            Generic
        }
		/// <summary>
		/// Mecanim avatar type used by race (Humanoid or Generic).
		/// </summary>
        public UMATarget umaTarget;
        public string genericRootMotionTransformName;
		/// <summary>
		/// The (optional) expression set used for facial animation.
		/// </summary>
		public PoseTools.UMAExpressionSet expressionSet;

		/// <summary>
		/// An optional set of DNA ranges.
		/// </summary>
		/// <remarks>
		/// DNA range assets are needed when multiple races share the
		/// same DNA converters. For example many races could use the default
		/// HumanMaleDNAConverterBehaviour, and the valid range for actual
		/// humans on many entries may be only 0.4-0.6 rather than 0-1.
		/// </remarks>
		public DNARangeAsset[] dnaRanges;

	    void Awake()
	    {
	        UpdateDictionary();
	    }

        public bool Validate()
	    {
	    	bool valid = true;
			if ((umaTarget == UMATarget.Humanoid) && (TPose == null)) {
				Debug.LogError("Humanoid UMA target missing required TPose data!");
				valid = false;
			}
			
			return valid;
		}

	    public void UpdateDictionary()
	    {
	        raceDictionary.Clear();
	        for (int i = 0; i < dnaConverterList.Length; i++)
	        {
	            if (dnaConverterList[i])
	            {
                    dnaConverterList[i].Prepare();
	                if (!raceDictionary.ContainsKey(dnaConverterList[i].DNAType))
	                {
	                    raceDictionary.Add(dnaConverterList[i].DNAType, dnaConverterList[i].ApplyDnaAction);
	                }
	            }
	        }
	    }
	}
}