using UnityEngine;
using System.Collections.Generic;

namespace UMA.CharacterSystem
{
	//The following classes are used by the pUMATextRecipe extension but also need to be available in RecipeEditor
	public enum recipeTypeOpts { Standard, WardrobeItem, DynamicCharacterAvatar, WardrobeCollection }

	[System.Serializable]
	public class WardrobeRecipeThumb
	{
		public string race = "";
		public string filename = "";
		public Sprite thumb = null;

		public WardrobeRecipeThumb()
		{

		}
		public WardrobeRecipeThumb(string n_race)
		{
			race = n_race;
		}
		public WardrobeRecipeThumb(string n_race, Sprite n_thumb)
		{
			race = n_race;
			filename = n_thumb.name;
			thumb = n_thumb;
		}
	}

	[System.Serializable]
	public class WardrobeSettings
	{
		public string slot;
		public string recipe;
		public WardrobeSettings()
		{

		}
		public WardrobeSettings(string _slot, string _recipe)
		{
			slot = _slot;
			recipe = _recipe;
		}
	}
	[System.Serializable]
	public class WardrobeSet
	{
		public string targetRace = "";
		public List<WardrobeSettings> wardrobeSet = new List<WardrobeSettings>();

		public WardrobeSet() { }
		public WardrobeSet(string race)
		{
			targetRace = race;
			wardrobeSet = new List<WardrobeSettings>();
		}
		public WardrobeSet(string race, List<WardrobeSettings> settings)
		{
			targetRace = race;
			wardrobeSet = settings;
		}
	}

	[System.Serializable]
	public class WardrobeCollectionList
	{
		public List<WardrobeSet> sets = new List<WardrobeSet>();

		public List<WardrobeSettings> this[string key]
		{
			get
			{
				return GetValue(key);
			}
			set
			{
				SetValue(key, value);
			}
		}

		public void Clear()
		{
            sets = new List<WardrobeSet>();
		}

		public bool Contains(string race)
		{
			bool contained = false;
			for (int i = 0; i < sets.Count; i++)
			{
				if (sets[i].targetRace == race)
				{
					contained = true;
					break;
				}
			}
			return contained;
		}
		public void Add(string race)
		{
			if (!Contains(race))
            {
                sets.Add(new WardrobeSet(race));
            }
        }
		public void Add(string race, List<WardrobeSettings> settings)
		{
			if (!Contains(race))
            {
                sets.Add(new WardrobeSet(race, settings));
            }
        }

		public void Remove(string race)
		{
			if (Contains(race))
			{
				var newSets = new List<WardrobeSet>(sets.Count - 1);
				for (int i = 0; i < sets.Count; i++)
				{
					if (sets[i].targetRace != race)
					{
						newSets.Add(new WardrobeSet(sets[i].targetRace, sets[i].wardrobeSet));
					}
				}
				sets = newSets;
			}
		}

		public List<string> GetAllRacesInCollection()
		{
			List<string> ret = new List<string>();
			for (int i = 0; i < sets.Count; i++)
			{
				ret.Add(sets[i].targetRace);
			}
			return ret;
		}

		public List<string> GetAllRecipeNamesInCollection(string forRace = "")
		{
			var collectionNames = new List<string>();
			for (int i = 0; i < sets.Count; i++)
			{
				if (forRace != "" && sets[i].targetRace != forRace)
                {
                    continue;
                }

                for (int si = 0; si < sets[i].wardrobeSet.Count; si++)
				{
					if (sets[i].wardrobeSet[si].recipe != "")
					{
						collectionNames.Add(sets[i].wardrobeSet[si].recipe);
					}
				}
			}
			return collectionNames;
		}

		protected List<WardrobeSettings> GetValue(string key)
		{
			for (int i = 0; i < sets.Count; i++)
			{
				if (sets[i].targetRace == key)
				{
					return sets[i].wardrobeSet;
				}
			}
			return new List<WardrobeSettings>();
		}

		protected void SetValue(string key, List<WardrobeSettings> value)
		{
			for (int i = 0; i < sets.Count; i++)
			{
				if (sets[i].targetRace == key)
				{
					sets[i].wardrobeSet = value;
				}
			}
		}
	}
}
