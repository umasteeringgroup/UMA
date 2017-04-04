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
	public partial class RaceData : ScriptableObject, INameProvider
	{
	    public string raceName;

        #region INameProvider
        public string GetAssetName()
        {
            return raceName;
        }
        public int GetNameHash()
        {
            return 0;
        }
        #endregion

        /// <summary>
        /// The set of DNA converters for modifying characters of this race.
        /// </summary>
        public DnaConverterBehaviour[] dnaConverterList = new DnaConverterBehaviour[0];

		[System.Obsolete("UMA 2.2+ - RaceData.raceDictionary is obsolete use GetConverter or dnaConverterList instead", false)]
		public Dictionary<Type, DnaConverterBehaviour.DNAConvertDelegate> raceDictionary = new Dictionary<Type, DnaConverterBehaviour.DNAConvertDelegate>();
	    
        public DnaConverterBehaviour GetConverter(UMADnaBase DNA)
        {
            foreach (DnaConverterBehaviour dcb in dnaConverterList)
            {
                if (dcb.DNATypeHash == DNA.DNATypeHash)
                    return dcb;
            }
            return null;
        }

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
		/// An (optional) set of DNA ranges.
		/// </summary>
		/// <remarks>
		/// DNA range assets are needed when multiple races share the
		/// same DNA converters. For example many races could use the default
		/// HumanMaleDNAConverterBehaviour, and the valid range for actual
		/// humans on many entries may be only 0.4-0.6 rather than 0-1.
		/// </remarks>
		public DNARangeAsset[] dnaRanges;

		/// <summary>
		/// The height of the generic base mesh of the race. Adjusted by DNA converters.
		/// </summary>
		public float raceHeight = 2f;
		/// <summary>
		/// The radius of the generic base mesh of the race. Adjusted by DNA converters.
		/// </summary>
		public float raceRadius = 0.25f;
		/// <summary>
		/// The mass of a generic member of the race. Adjusted by DNA converters.
		/// </summary>
		public float raceMass = 50f;

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

		#pragma warning disable 618
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
		#pragma warning restore 618
	}
}