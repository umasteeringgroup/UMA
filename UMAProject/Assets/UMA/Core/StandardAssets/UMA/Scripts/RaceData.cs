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
    [PreferBinarySerialization]
	[Serializable]
	public partial class RaceData : ScriptableObject, INameProvider
	{
	    public string raceName;
		public List<string> KeepBoneNames = new List<string>();
		public List<string> tags = new List<string>();

		public bool disableDNAConverters = false;
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

		[SerializeField]
		[Tooltip("The list of DNA Converters that this race uses. These are usually DynamicDNAConverterController assets.")]
		private DNAConverterList _dnaConverterList = new DNAConverterList();


		public List<string> GetDNANames()
		{
			List<string> Names = new List<string>();

            for (int i = 0; i < dnaConverterList.Length; i++)
            {
                IDNAConverter converter = dnaConverterList[i];
                if (converter is IDynamicDNAConverter)
				{
					var asset = ((IDynamicDNAConverter)converter).dnaAsset;
					Names.AddRange(asset.Names);
				}
			}
			return Names;
		}

		public bool HasTag(string tag)
		{
            return tags.Contains(tag);
        }


		public void ResetDNA()
		{
            for (int j = 0; j < dnaConverterList.Length; j++)
			{
                IDNAConverter converter = dnaConverterList[j];
                if (converter is DynamicDNAConverterController)
				{
					var c = converter as DynamicDNAConverterController;
					for (int i=0;i<c.PluginCount;i++)
                    {
						DynamicDNAPlugin ddp = c.GetPlugin(i);
						ddp.Reset();
                    }
				}
			}
		}

		/// <summary>
		/// Returns the list of DNA Converters that this race uses. These are usually DynamicDNAConverterController assets
		/// </summary>
		public DynamicDNAConverterController[] dnaConverterList
		{
			get {
				if (disableDNAConverters)
					return new DynamicDNAConverterController[0];
				else
					return _dnaConverterList.ToArray(); 
			}
			set { _dnaConverterList = new DNAConverterList(value); }
		}

		public DynamicDNAConverterController[] GetConverters(UMADnaBase DNA)
		{
			if (disableDNAConverters)
			{
				return new DynamicDNAConverterController[0];
			}
			return _dnaConverterList.ToArray();
		}

		/// <summary>
		/// Adds a DNAConverter to this Races list of converters
		/// </summary>
		/// <param name="converter"></param>
		public void AddConverter(IDNAConverter converter)
		{
			if (disableDNAConverters)
				return;
			_dnaConverterList.Add(converter as DynamicDNAConverterController);
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
			if ((umaTarget == UMATarget.Humanoid) && (TPose == null))
			{
				if (Debug.isDebugBuild)
                {
                    Debug.LogError("Humanoid UMA target missing required TPose data!");
                }

                valid = false;
			}
			
			return valid;
		}

		#pragma warning disable 618
	    public void UpdateDictionary()
	    {
			if (disableDNAConverters) return;
			//UMA2.8+ call Prepare() on the elements in _dnaConverterList now.
			
			for (int i = 0; i < _dnaConverterList.Count; i++)
			{
				if (_dnaConverterList[i] != null)
                {
                    _dnaConverterList[i].Prepare();
                }
                else
                {
					Debug.LogWarning($"Null converter list on race: {raceName} object {this.name} ");
                }
			}
	    }
#pragma warning restore 618
	}
}
