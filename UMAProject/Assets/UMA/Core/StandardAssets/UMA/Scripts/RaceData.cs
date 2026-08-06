using UnityEngine;
using System;
using System.Collections.Generic;
using UMA.PoseTools;
using UnityEngine.Serialization;

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
	public partial class RaceData : ScriptableObject, INameProvider, IUMAIndexOptions
	{
		[FormerlySerializedAs("raceName")]
        public string _oldRaceName;

        public string raceName
        {
            get
            {
                if (!string.IsNullOrEmpty(_oldRaceName))
                {
                    return _oldRaceName;
                }
                return this.name;
            }
        }
		public List<string> KeepBoneNames = new List<string>();
		public List<string> tags = new List<string>();
		public  List<SlotBurnOptions> PrebakedBlendshapes = new List<SlotBurnOptions>();
		public List<string> UnbakedShapesToInclude = new List<string>();
		public bool keepUnbakedBlendshapes = false;
        public bool disableDNAConverters = false;
		public bool forceRebuildRaceSlots = false;

		[Tooltip("if true, this will not be added to the index when all items are scanned.")]
        public bool noAutoAdd = false;

        [Header("Renderer Bounds")]
        [Tooltip("When enabled, the UMA renderers will use this manual bounds (extents) instead of calculated bounds. The extents are scaled by the scale on the 'Position' bone.")]
        public bool useManualRendererBounds = false;
        [Tooltip("Manual bounds extents to apply to the UMA renderers when 'Use Manual Renderer Bounds' is enabled. Values are extents (half-size) in local space before scaling by the 'Position' bone.")]
        public Vector3 manualRendererBounds = Vector3.zero;
        [Tooltip("Manual bounds center offset to apply to the UMA renderers when 'Use Manual Renderer Bounds' is enabled. Value is in local space before scaling by the 'Position' bone.")]
        public Vector3 manualRendererBoundsCenter = Vector3.zero;

		public bool NoAutoAdd
		{
			get { return noAutoAdd; }
            set { noAutoAdd = value; }
        }
        #region NEW DNA
        /*
		 * New DNA
		 */
        [Tooltip("Use the new DNA system. The older DNA will not be used or called.")]
        public bool useNewDNA;
		[Tooltip("The DNA groups assigned to this race")]
        public DNACollection DNACollection = new DNACollection();


        #endregion
        #region INameProvider
        public string GetAssetName()
        {
            return raceName;
        }
        public int GetNameHash()
        {
            return UMAUtils.StringToHash(raceName);
        }
		#endregion

		[SerializeField]
		[Tooltip("The list of DNA Converters that this race uses. These are usually DynamicDNAConverterController assets.")]
		private DNAConverterList _dnaConverterList = new DNAConverterList();


        public List<string> GetDNANames()
		{
			if (useNewDNA)
			{
				return DNACollection.GetDNANames();
            }
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
			if (useNewDNA)
			{
				// New DNA does not need resetting currently.
				return;
            }
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
				if (useNewDNA)
				{
					return null;
				}
				if (disableDNAConverters)
					return new DynamicDNAConverterController[0];
				else
					return _dnaConverterList.ToArray(); 
			}
			set { _dnaConverterList = new DNAConverterList(value); }
		}

		public bool forceKeep;
        public bool ForceKeep { get => forceKeep; set => forceKeep = value; }
		public bool labelLocalFiles;
        public bool LabelLocalFiles { get => labelLocalFiles; set => labelLocalFiles = value; }


		[Tooltip("The compatibility pose for old UMA2 rigs. Old slots are converted to this race using this pose.")]		
		public UMABonePose LegacyCompatibilityPose;


        public DynamicDNAConverterController[] GetConverters(UMADnaBase DNA)
		{
			if (useNewDNA)
			{
				Debug.LogError("GetConverters should not be called when using New DNA system.");
                return null;
            }
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
            if (useNewDNA)
            {
                Debug.LogError("AddConverters should not be called when using New DNA system.");
                return;
            }
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
        /// DNA-based expressions available to this race.
        /// </summary>
        [Tooltip("DNA-based expression definitions used by DynamicExpressionPlayer.")]
        public UMAExpressionGroup expressionGroup;

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
#if UNITY_EDITOR
	    	if (!Application.isPlaying)
        		return;
#endif	
	        UpdateDictionary();
	    }

        public bool Validate()
		{
			bool valid = true;
			if (UsesFbxRoute)
			{
				if (baseFbxRenderer == null)
				{
					if (Debug.isDebugBuild)
					{
						Debug.LogError("FBX route missing required Base FBX Renderer!");
					}

					valid = false;
				}
				else
				{
					if (baseFbxRenderer.sharedMesh == null)
					{
						if (Debug.isDebugBuild)
						{
							Debug.LogError("FBX route Base FBX Renderer has no shared mesh!");
						}

						valid = false;
					}

					SkinnedMeshRenderer[] rootRenderers = baseFbxRenderer.transform.root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
					if (rootRenderers.Length != 1 || rootRenderers[0] != baseFbxRenderer)
					{
						if (Debug.isDebugBuild)
						{
							Debug.LogError("FBX route Base FBX Renderer must be the only SkinnedMeshRenderer under its prefab root!");
						}

						valid = false;
					}
				}
			}
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
#if UNITY_EDITOR
                else
                {
					Debug.LogWarning($"Null converter list on race: {raceName} object {this.name} ");
                }
#endif
			}
	    }
#pragma warning restore 618
	}
}
