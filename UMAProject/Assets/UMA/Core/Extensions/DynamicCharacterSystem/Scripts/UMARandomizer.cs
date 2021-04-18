using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
	// A Random DNA 
	[Serializable]
	public class RandomDNA
	{
		public string DnaName;
		public float MinValue;
		public float MaxValue;
#if UNITY_EDITOR
		public bool Delete { get; set; }  = false;
#endif
		public RandomDNA(string name)
		{
			DnaName = name;
			MinValue = 0.0f;
			MaxValue = 1.0f;
		}
	}

	// A list of possible colors for a shared color.
	[Serializable]
	public class RandomColors
	{
		public string ColorName;
		public SharedColorTable ColorTable;
		// public List<RandomColor> Colors;
#if UNITY_EDITOR
		public bool GuiFoldout;
		public bool Delete;

		public int CurrentColor;
#endif
		public RandomColors(string name, SharedColorTable sct)
		{
#if UNITY_EDITOR
			CurrentColor = 0;
			GuiFoldout = true;
			Delete = false;
#endif
			ColorName = name;
			ColorTable = sct;
		}

		public RandomColors(RandomWardrobeSlot rws)
		{
#if UNITY_EDITOR
			CurrentColor = 0;
			GuiFoldout = true;
			Delete = false;
#endif
		}
	}

	[Serializable]
	public class RandomWardrobeSlot
	{
		public UMAWardrobeRecipe WardrobeSlot;
		[Range(1, 100)]
		public int Chance = 1;
		public List<RandomColors> Colors;
		public string _slotName;
		public string SlotName
		{
			get
			{
				if (WardrobeSlot != null)
					return WardrobeSlot.wardrobeSlot;
				return _slotName;
			}
		}
#if UNITY_EDITOR

		public bool GuiFoldout;
		public bool Delete;
		public bool AddColorTable;
		public string[] PossibleColors;

		public string SortName
        {
			get
            {
				string slot = "";
				if (WardrobeSlot != null)
					slot = WardrobeSlot.name;
				return SlotName + slot;
            }
        }
#endif
		public RandomWardrobeSlot(UMAWardrobeRecipe slot, string slotName)
		{
#if UNITY_EDITOR
			GuiFoldout = true;
			Delete = false;
			_slotName = slotName;
			if (slot == null)
			{
				PossibleColors = new string[0];
			}
			else
			{
				UMAPackedRecipeBase.UMAPackRecipe upr = slot.PackedLoad();

				List<string> cols = new List<string>();
				foreach (UMAPackedRecipeBase.PackedOverlayColorDataV3 pcd in upr.fColors)
				{
					if (pcd.name.Trim() != "-")
					{
						cols.Add(pcd.name);
					}
				}
				PossibleColors = cols.ToArray();
			}
#endif
			Colors = new List<RandomColors>();
			WardrobeSlot = slot;
		}
	}

	[Serializable]
	public class RandomAvatar
	{
		public string RaceName;
		[Range(1, 100)]
		public int Chance = 1;
		public List<RandomColors> SharedColors;
		public List<RandomWardrobeSlot> RandomWardrobeSlots;
		public List<RandomDNA> RandomDna;
		public RaceData raceData;
#if UNITY_EDITOR
		public bool GuiFoldout;
		public bool ColorsFoldout;
		public bool WardrobeFoldout;
		public bool DnaFoldout;
		public bool Delete;
		public bool AddColorTable;
		public bool DnaChanged;
		public string[] PossibleColors;
		public string[] PossibleDNA;
		public int SelectedDNA;
		public string DNAAdd;
		public int DNADel;
		public int currentWardrobeSlot;
#endif
		public UMAPredefinedDNA GetRandomDNA()
		{
			UMAPredefinedDNA theDNA = new UMAPredefinedDNA();
			foreach(RandomDNA rd in this.RandomDna)
			{
				theDNA.AddDNA(rd.DnaName, UnityEngine.Random.Range(rd.MinValue, rd.MaxValue));
			}
			return theDNA;
		}

		public Dictionary<string, List<RandomWardrobeSlot>> GetRandomSlots()
		{
			Dictionary<string, List<RandomWardrobeSlot>> RandomSlots = new Dictionary<string, List<RandomWardrobeSlot>>();
			foreach (RandomWardrobeSlot rws in RandomWardrobeSlots)
			{
				string wslot = rws.SlotName;//rws.WardrobeSlot.wardrobeSlot;
				if (!RandomSlots.ContainsKey(wslot))
				{
					RandomSlots.Add(wslot, new List<RandomWardrobeSlot>());
				}
				RandomSlots[wslot].Add(rws);
			}
			return RandomSlots;
		}

		private List<RandomColors> GetColorListForRace(RaceData rc)
		{
			UMATextRecipe utr = rc.baseRaceRecipe as UMATextRecipe;
			UMAPackedRecipeBase.UMAPackRecipe upr = utr.PackedLoad();

			List<string> cols = new List<string>();
			foreach (UMAPackedRecipeBase.PackedOverlayColorDataV3 pcd in upr.fColors)
			{
				if (pcd.name.Trim() != "-")
				{
					cols.Add(pcd.name);
				}
			}

			List<RandomColors> newColors = new List<RandomColors>();
			foreach (string s in cols)
			{
				RandomColors rcs = new RandomColors(s, null);
				newColors.Add(rcs);
			}
			return newColors;
		}

#if UNITY_EDITOR
		public void SetupDNA(RaceData rc)
		{
			List<string> DNAList = new List<string>();
			foreach (IDNAConverter cvt in rc.dnaConverterList)
			{
				if (cvt.DNAType == typeof(DynamicUMADna))
				{
					DNAList.AddRange(((IDynamicDNAConverter)cvt).dnaAsset.Names);
				}
				else
				{
					if (cvt is DnaConverterBehaviour)
					{
						var legacyDNA = (cvt as DnaConverterBehaviour).DNAType.GetConstructor(System.Type.EmptyTypes).Invoke(null) as UMADnaBase;
						if (legacyDNA != null)
						{
							DNAList.AddRange(legacyDNA.Names);
						}
					}
				}
			}
			PossibleDNA = DNAList.ToArray();
		}
#endif
		public RandomAvatar(RaceData race)
		{
			raceData = race;
			RaceName = race.raceName;
			SharedColors = new List<RandomColors>();
			RandomWardrobeSlots = new List<RandomWardrobeSlot>();
			RandomDna = new List<RandomDNA>();
			SharedColors = GetColorListForRace(race);
#if UNITY_EDITOR
			GuiFoldout = true;
			Delete = false;
			SetupDNA(race);
#endif
		}
	}

	public class UMARandomizer : ScriptableObject
	{
#if UNITY_EDITOR
		public int currentRace { get; set; } = 0;
		public string[] races { get; set; } = new string[0];
		public List<RaceData> raceDatas { get; set; } = new List<RaceData>();
		public List<UMAWardrobeRecipe> droppedItems { get; set; } = new List<UMAWardrobeRecipe>();

#if UMA_HOTKEYS
        [UnityEditor.MenuItem("Assets/Create/UMA/Misc/Randomizer %#h")]
#else
		[UnityEditor.MenuItem("Assets/Create/UMA/Misc/Randomizer")]
#endif
		public static void CreatePreloadAsset()
		{
			UMA.CustomAssetUtility.CreateAsset<UMARandomizer>();
		}
#endif

		public int RandomCount
		{
			get
			{
				return RandomAvatars.Count;
			}
		}

		public List<RandomAvatar> RandomAvatars = new List<RandomAvatar>();

		public RandomAvatar GetRandomAvatar()
		{
			if (RandomAvatars.Count == 1) return RandomAvatars[0];
			if (RandomAvatars.Count == 0) return null;
			int total = 0;

			// find the total number of chances.
			foreach(RandomAvatar ra in RandomAvatars )
			{
				total += ra.Chance;
			}

			foreach (RandomAvatar ra in RandomAvatars)
			{
				int rval = UnityEngine.Random.Range(0, total);

				if (rval < ra.Chance)
				{
					return ra;
				}
			}
			return RandomAvatars[RandomAvatars.Count - 1];
		}
	}
}
