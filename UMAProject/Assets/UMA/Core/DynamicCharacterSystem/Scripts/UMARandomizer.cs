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
        public RandomAvatar GetRandomAvatar() => GetRandomAvatar(null);

        /// <summary>Select a positive-weight definition, optionally restricted to one race.</summary>
        public RandomAvatar GetRandomAvatar(string raceName)
        {
            if (RandomAvatars == null) return null;
            double total = 0;
            foreach (var avatar in RandomAvatars)
                if (avatar != null && avatar.Chance > 0 && (raceName == null || avatar.RaceName == raceName))
                    total += avatar.Chance;
            if (total <= 0) return null;
            double roll = UnityEngine.Random.value * total;
            RandomAvatar last = null;
            foreach (var avatar in RandomAvatars)
            {
                if (avatar == null || avatar.Chance <= 0 || (raceName != null && avatar.RaceName != raceName)) continue;
                last = avatar;
                roll -= avatar.Chance;
                if (roll < 0) return avatar;
            }
            return last;
        }
    }
}
