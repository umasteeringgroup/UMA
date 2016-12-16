using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UMA;

namespace UMACharacterSystem
{

	//refrenced by 'SlotMaker.cs' which is itself never refrenced
	/*public enum WardrobeSlot
	{
		None = 0,
		Face = 1,
		Hair = 2,
		Complexion = 3,
		Eyebrows = 4,
		Beard = 5,
		Ears,
		Helmet,
		Shoulders,
		Chest,
		Arms,
		Hands,
		Waist,
		Legs,
		Feet
	}*/

	//not used any more now we set things based on race
	/*public enum Sex
	{
		Unisex = 0,
		Male = 1,
		Female = 2
	}*/

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
	
}
