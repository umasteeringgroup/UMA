using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
	public class UMARandomizer : ScriptableObject
	{
		public bool useDefinition = true;
		public bool useGlobalColors = true;
		[SerializeField] private RandomizerDefinition definition = new RandomizerDefinition();
		[SerializeField] private RandomizerGlobal global = new RandomizerGlobal();

		public RandomizerDefinition Definition => definition;
		public RandomizerGlobal Global => global;

		[Serializable]
		public class RandomizerDefinition
		{
			public Sprite Icon;
			public string Name;
			[TextArea] public string Note;
		}

		[Serializable]
		public class RandomizerGlobal
		{
			public List<RandomColors> SharedColors = new List<RandomColors>();
#if UNITY_EDITOR
			public bool ColorsFoldout;
			public bool UtilityFoldout;
#endif
		}

#if UNITY_EDITOR
		public int currentRace { get; set; } = 0;
		public string[] races { get; set; } = new string[0];
		public List<RaceData> raceDatas { get; set; } = new List<RaceData>();
		public List<UMAWardrobeRecipe> droppedItems { get; set; } = new List<UMAWardrobeRecipe>();
		public List<UMAWardrobeCollection> droppedCollections { get; set; } = new List<UMAWardrobeCollection>();
		public bool hasDrop => droppedItems.Count > 0 || droppedCollections.Count > 0;

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

		public List<RandomColors> GlobalSharedColors = new List<RandomColors>();
		public List<RandomAvatar> RandomAvatars = new List<RandomAvatar>();

		/// <summary>
		/// Randomly Get a Random Avatar from Scriptable Object List of Random Avatars
		/// </summary>
		/// <returns> Random Avatar, null if not none</returns>
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

		/// <summary>
		/// Get Random Avatar for given Race from a List of Random Avatars
		/// Returns null if there is no Random Avatar for selected Race
		/// </summary>
		/// <param name="raceName"> Race to select </param>
		/// <returns> RandomAvatar selected, null if none </returns>
		public RandomAvatar GetRandomAvatar(string raceName)
		{
			if (RandomAvatars.Count == 0) return null;

			if (RandomAvatars.Count == 1)
			{
				if (RandomAvatars[0].RaceName != raceName) return null;
				return RandomAvatars[0];
			}

			int total = 0;

			// find the total number of chances.
			foreach (RandomAvatar ra in RandomAvatars)
			{
				total += ra.RaceName == raceName ? ra.Chance : 0;
			}

			foreach (RandomAvatar ra in RandomAvatars)
			{
				int rval = UnityEngine.Random.Range(0, total);

				if (rval < ra.Chance && ra.RaceName == raceName)
				{
					return ra;
				}
			}
			return RandomAvatars[RandomAvatars.Count - 1].RaceName == raceName ? RandomAvatars[RandomAvatars.Count - 1] : null;
		}
	}
}
