using UnityEngine;

namespace UMACharacterSystem
{
/*	public enum CharacterSlot
	{
		Head =0,  
		Hair=1,
		Beard,
		Torso,
		Legs,
		Feet,
		Arms,
		Hands,
		Tail,
		Horns,
		Wings
	} */

	public enum WardrobeSlot
	{
		None = 0,
		Face=1,
		Hair=2,
		Complexion=3,
		Eyebrows=4,
		Beard=5,
		Ears,
		Helmet,
		Shoulders,
		Chest,
		Arms,
		Hands,
		Waist,
		Legs,
		Feet
	}
	
	public enum Sex
	{
		Unisex = 0,
		Male = 1,
		Female = 2
	}

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
        public WardrobeRecipeThumb(string n_race,Sprite n_thumb)
        {
            race = n_race;
            filename = n_thumb.name;
            thumb = n_thumb;
        }
    }
}