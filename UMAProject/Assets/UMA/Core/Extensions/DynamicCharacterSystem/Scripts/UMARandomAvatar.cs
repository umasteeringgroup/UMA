using System;
using System.Collections;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
	public class UMARandomAvatar : MonoBehaviour
	{ 
		public UMARandomizer Randomizer;
		private DynamicCharacterAvatar Avatar;
		private Dictionary<string, List<RandomWardrobeSlot>> RandomSlots;

		// Use this for initialization
		void Start()
		{
			Avatar = gameObject.GetComponent<DynamicCharacterAvatar>();
			Randomize();
		}

		public RandomWardrobeSlot GetRandomWardrobe(List<RandomWardrobeSlot> wardrobeSlots)
		{
			int total = 0;

			foreach (RandomWardrobeSlot rws in wardrobeSlots)
				total += rws.Chance;

			foreach(RandomWardrobeSlot rws in wardrobeSlots)
			{
				if (UnityEngine.Random.Range(0,total) < rws.Chance)
				{
					return rws;
				}
			}
			return wardrobeSlots[wardrobeSlots.Count - 1];
		}

		private OverlayColorData GetRandomColor(RandomColors rc)
		{
			int inx = UnityEngine.Random.Range(0, rc.ColorTable.colors.Length);
			return rc.ColorTable.colors[inx];
		}

		private void AddRandomSlot(RandomWardrobeSlot uwr)
		{
			//DynamicCharacterAvatar.WardrobeRecipeListItem item = new DynamicCharacterAvatar.WardrobeRecipeListItem(uwr.WardrobeSlot);
			//Avatar.preloadWardrobeRecipes.recipes.Add(item);	
			Avatar.SetSlot(uwr.WardrobeSlot);
		    if (uwr.Colors != null)
			{
				foreach(RandomColors rc in uwr.Colors)
				{
					OverlayColorData ocd = GetRandomColor(rc);
					Avatar.SetColor(rc.ColorName, ocd,false);
				}
			}
		}

		public void Randomize()
		{
			if (Avatar != null && Randomizer != null)
			{
				//Avatar.preloadWardrobeRecipes.recipes.Clear();
				//Avatar.preloadWardrobeRecipes.loadDefaultRecipes = true;
				RandomAvatar ra = Randomizer.GetRandomAvatar();
				Avatar.RacePreset = ra.RaceName;
				Avatar.BuildCharacterEnabled = true;
				var RandomSlots = ra.GetRandomSlots();
				foreach (string s in RandomSlots.Keys)
				{
					List<RandomWardrobeSlot> RandomWardrobe = RandomSlots[s];
					RandomWardrobeSlot uwr = GetRandomWardrobe(RandomWardrobe);
					AddRandomSlot(uwr);
				}
			}
		}

	}
}