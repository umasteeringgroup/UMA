using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.Serialization;
using UMA.CharacterSystem;

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
	public partial class RaceData : ScriptableObject, INameProvider, ISerializationCallbackReceiver
	{
	    public string raceName;
		public List<string> KeepBoneNames = new List<string>();

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

		//UMA 2.8 FixDNAPrefabs: made the old fields 'legacy'  and turned them into properties that use the new DNAConverterListField
		#region OBSOLETE FIELDS AND PROPERTIES and METHODS
		/// <summary>
		/// The set of DNA converters for modifying characters of this race.
		/// </summary>
		[Tooltip("The List of Dna Converter components on prefab gameobjects that store the DNA converter instance data.")]
		[FormerlySerializedAs("dnaConverterList")]
		[SerializeField]
		private DnaConverterBehaviour[] _dnaConverterListLegacy = new DnaConverterBehaviour[0];

		[System.Obsolete("UMA 2.8+ - RaceData.raceDictionary is obsolete use GetConverters or dnaConverterList instead", false)]
		public Dictionary<Type, DNAConvertDelegate> raceDictionary = new Dictionary<Type, DNAConvertDelegate>();

		//UMA2.8+ multiple converters can use the same DNA now
		[System.Obsolete("UMA 2.8+ - RaceData.GetConverter is obsolete because lots of converters can use the same DNA names now (DNAAsset). Use GetConverters or dnaConverterList instead", false)]
		public IDNAConverter GetConverter(UMADnaBase DNA)
		{
			/*foreach (DnaConverterBehaviour dcb in _dnaConverterList)
			{
				if (dcb.DNATypeHash == DNA.DNATypeHash)
					return dcb;
			}*/
			for(int i = 0; i < _dnaConverterList.Count; i++)
			{
				if (_dnaConverterList[i].DNATypeHash == DNA.DNATypeHash)
					return _dnaConverterList[i];
			}
			return null;
		}

		//UMA 2.8 FixDNAPrefabs: Swaps the legacy converter (DnaConverterBehaviour Prefab) for the new DNAConverterController
		/// <summary>
		/// Replaces a legacy DnaConverterBehaviour Prefab with a new DynamicDNAConverterController
		/// </summary>
		/// <returns>returns true if any converters were replaced.</returns>
		public bool UpgradeFromLegacy(DnaConverterBehaviour oldConverter, DynamicDNAConverterController newConverter)
		{
			if (_dnaConverterList.Contains(oldConverter))
			{
				if (_dnaConverterList.Replace(oldConverter, newConverter))
					return true;
			}
			return false;
		}

		#endregion

		[SerializeField]
		[Tooltip("The list of DNA Converters that this race uses. These are usually DynamicDNAConverterController assets.")]
		private DNAConverterList _dnaConverterList = new DNAConverterList();


		public List<string> GetDNANames()
		{
			List<string> Names = new List<string>();

			foreach(IDNAConverter converter in dnaConverterList)
            {
				if (converter is IDynamicDNAConverter)
				{
					var asset = ((IDynamicDNAConverter)converter).dnaAsset;
					Names.AddRange(asset.Names);
				}
			}
			return Names;
		}

		public void ResetDNA()
		{
			foreach (IDNAConverter converter in dnaConverterList)
			{
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
		public IDNAConverter[] dnaConverterList
		{
			get { return _dnaConverterList.ToArray(); }
			set { _dnaConverterList = new DNAConverterList(value); }
		}

		/// <summary>
		/// Returns any dna converters on the Race that use the given DNA
		/// </summary>
		/// <param name="DNA"></param>
		public IDNAConverter[] GetConverters(UMADnaBase DNA)
		{
			var ret = new List<IDNAConverter>();
			for(int i = 0; i < _dnaConverterList.Count; i++)
			{
				if (_dnaConverterList[i].DNATypeHash == DNA.DNATypeHash)
					ret.Add(_dnaConverterList[i]);
			}
			return ret.ToArray();
		}

		/// <summary>
		/// Adds a DNAConverter to this Races list of converters
		/// </summary>
		/// <param name="converter"></param>
		public void AddConverter(IDNAConverter converter)
		{
			_dnaConverterList.Add(converter);
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
					Debug.LogError("Humanoid UMA target missing required TPose data!");

				valid = false;
			}
			
			return valid;
		}

		#pragma warning disable 618
	    public void UpdateDictionary()
	    {
			//UMA2.8+ OBSOLETE CODE
	        raceDictionary.Clear();
	        for (int i = 0; i < _dnaConverterListLegacy.Length; i++)
	        {
	            if (_dnaConverterListLegacy[i])
	            {
					_dnaConverterListLegacy[i].Prepare();
	                if (!raceDictionary.ContainsKey(_dnaConverterListLegacy[i].DNAType))
	                {
	                    raceDictionary.Add(_dnaConverterListLegacy[i].DNAType, _dnaConverterListLegacy[i].ApplyDnaAction);
	                }
	            }
	        }
			//UMA2.8+ call Prepare() on the elements in _dnaConverterList now.
			for (int i = 0; i < _dnaConverterList.Count; i++)
			{
#if UNITY_EDITOR
				//Do we do update nagging here?
				if (_dnaConverterList[i] is UMA.CharacterSystem.DynamicDNAConverterBehaviour)
				{
					(_dnaConverterList[i] as UMA.CharacterSystem.DynamicDNAConverterBehaviour).DoUpgradeNag(this);
				}
#endif
				_dnaConverterList[i].Prepare();
			}
	    }
#pragma warning restore 618

		#region ISERIALIZATIONCALLBACKRECIEVER

		public void OnBeforeSerialize()
		{
			//do nothing
		}

		/// <summary>
		/// Converts DnaConverterBehaviour[] _dnaConverterListLegacy to  DNAConverterList _dnaConverterList to preserve legacy data
		/// </summary>
		public void OnAfterDeserialize()
		{
			if(_dnaConverterListLegacy.Length > 0 && _dnaConverterList.Length == 0)
			{
				for (int i = 0; i < _dnaConverterListLegacy.Length; i++)
					_dnaConverterList.Add(_dnaConverterListLegacy[i] as IDNAConverter);
			}
			//Clear _dnaConverterListLegacy?
		}

		#endregion
	}
}