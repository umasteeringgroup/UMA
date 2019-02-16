using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
	[Serializable]
	public class ColorChannel
	{
		public Color Multiplier;
		public Color Additive;
	}

	// A specific color chance
	[Serializable]
	public class RandomColor
	{
		public List<ColorChannel> Channels;
		[Range(1, 100)]
		public int Chance = 1;
#if UNITY_EDITOR
		public bool GuiFoldout;
		public bool Delete;
#endif
		public RandomColor()
		{
#if UNITY_EDITOR
			GuiFoldout = true;
			Delete = false;
#endif
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
#if UNITY_EDITOR
		public bool GuiFoldout;
		public bool Delete;
		public bool AddColorTable;
		public string[] PossibleColors;
#endif
		public RandomWardrobeSlot(UMAWardrobeRecipe slot)
		{
#if UNITY_EDITOR
			GuiFoldout = true;
			Delete = false;
			UMAPackedRecipeBase.UMAPackRecipe upr = slot.PackedLoad();

			List<string> cols = new List<string>();
			foreach (UMAPackedRecipeBase.PackedOverlayColorDataV3 pcd in upr.fColors)
			{
				cols.Add(pcd.name);
			}
			PossibleColors = cols.ToArray();
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
#if UNITY_EDITOR
		public bool GuiFoldout;
		public bool Delete;
#endif
		public Dictionary<string, List<RandomWardrobeSlot>> GetRandomSlots()
		{
			Dictionary<string, List<RandomWardrobeSlot>> RandomSlots = new Dictionary<string, List<RandomWardrobeSlot>>();
			foreach (RandomWardrobeSlot rws in RandomWardrobeSlots)
			{
				string wslot = rws.WardrobeSlot.wardrobeSlot;
				if (!RandomSlots.ContainsKey(wslot))
				{
					RandomSlots.Add(wslot, new List<RandomWardrobeSlot>());
				}
				RandomSlots[wslot].Add(rws);
			}
			return RandomSlots;
		}

		public RandomAvatar(string raceName)
		{
			RaceName = raceName;
			SharedColors = new List<RandomColors>();
			RandomWardrobeSlots = new List<RandomWardrobeSlot>();
#if UNITY_EDITOR
			GuiFoldout = true;
			Delete = false;
#endif
		}
	}

	public class UMARandomizer : ScriptableObject
	{
#if UNITY_EDITOR
#if UMA_HOTKEYS
        [UnityEditor.MenuItem("Assets/Create/UMA/Misc/UMARandomizer %#h")]
#else
		[UnityEditor.MenuItem("Assets/Create/UMA/Misc/UMARandomizer")]
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
