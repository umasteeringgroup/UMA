using UnityEngine;
using System;
using System.Collections.Generic;


namespace UMA
{
	[Serializable]
	public partial class RaceData : ScriptableObject {
	    public string raceName;
	    public GameObject racePrefab;
	    public DnaConverterBehaviour[] dnaConverterList = new DnaConverterBehaviour[0];

		[Obsolete("AnimatedBones is deprecated, use animatedBones from baseSlot.", false)]
		public String[] AnimatedBones = new string[0];
		public SlotData baseSlot = null;

		public Dictionary<Type, DnaConverterBehaviour.DNAConvertDelegate> raceDictionary = new Dictionary<Type, DnaConverterBehaviour.DNAConvertDelegate>();
	    public UmaTPose TPose;
        public enum UMATarget
        {
            Humanoid,
            Generic
        }
        public UMATarget umaTarget;
        public string genericRootMotionTransformName;
		public PoseTools.UMAExpressionSet expressionSet;

	    void Awake()
	    {
	        UpdateDictionary();
	    }

        public bool Validate(UMAGeneratorBase generator)
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