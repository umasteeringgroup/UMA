using UnityEngine;
using System;
using System.Collections.Generic;

namespace UMA
{
    public partial class RaceData
    {
        public UMARecipeBase baseRaceRecipe;
        public List<string> wardrobeSlots = new List<string>(){
            "None",
            "Face",
            "Hair",
            "Complexion",
            "Eyebrows",
            "Beard",
            "Ears",
            "Helmet",
            "Shoulders",
            "Chest",
            "Arms",
            "Hands",
            "Waist",
            "Legs",
            "Feet"
        };
        //backwardsCompatibleWith (below) would be used in situations where you create a new race that can still wear slots for an existing race.
        //For example Fernando's Werewolf can still wear some slots from HumanMale 
        //By setting backwardsCompatibleWith to HumanMale and removing the 'Feet' WardrobeSlot you would enable 
        //the Werewolf race to wear any existing HumanMale slots apart from HumanMale Feet slots.
        //Then, by adding 'WarewolfFeet' to that races' wardrobeSlots you would be specifying a wardrobe slot that is
        //only be available to the Werewolf Race and not for Avatars set to be 'HumanMale' or 'HumanFemale' for example...
        public List<string> backwardsCompatibleWith = new List<string>();

        public RaceThumbnails raceThumbnails;

        //Not sure if this is needed I think I could just set the wardrobe slots property to be this by default?
        public void AddDefaultWardrobeSlots(bool forceOverride = false)
        {
            if (wardrobeSlots.Count == 0 || forceOverride)
            {
                wardrobeSlots = new List<string>() {
                    "None",
                    "Face",
                    "Hair",
                    "Complexion",
                    "Eyebrows",
                    "Beard",
                    "Ears",
                    "Helmet",
                    "Shoulders",
                    "Chest",
                    "Arms",
                    "Hands",
                    "Waist",
                    "Legs",
                    "Feet"
                };
            }
        }
        /// <summary>
        /// Validates the wardrobe slots.
        /// </summary>
        /// <returns><c>true</c>, if wardrobe slots was validated, <c>false</c> otherwise.</returns>
        /// <param name="setToDefault">If set to <c>true</c> wardrobeSlots are set to default (returns true).</param>
        public bool ValidateWardrobeSlots(bool setToDefault = false)
        {
            if (wardrobeSlots.Count == 0)
            {
                Debug.Log("wardrobeSlots count was 0");
                AddDefaultWardrobeSlots(setToDefault);
                return setToDefault;
            }
            return true;
        }
        public bool findBackwardsCompatibleWith(List<string> compatibleStrings)
        {
            foreach (string val in compatibleStrings)
            {
                if (backwardsCompatibleWith.Contains(val))
                    return true;
            }
            return false;
        }

        #region SpecialTypes
        //Race Thumbnails used in the GUI to give a visual representation of the race
        [System.Serializable]
        public class RaceThumbnails
        {
            [System.Serializable]
            public class WardrobeSlotThumb
            {
                [Tooltip("A comma separated list of wardrobe slots this is the base thumbnail for (no spaces)")]
                public string thumbIsFor = "";
                public Sprite thumb = null;
            }
            public Sprite fullThumb = null;
            public Sprite faceThumb = null;
            [SerializeField]
            List<WardrobeSlotThumb> wardrobeSlotThumbs = new List<WardrobeSlotThumb>();

            public Sprite GetThumbFor(string thumbToGet = "")
            {
                Sprite foundSprite = fullThumb != null ? fullThumb : null;
                foreach(WardrobeSlotThumb wardrobeThumb in wardrobeSlotThumbs)
                {
                    string[] thumbIsForArray = null;
                    wardrobeThumb.thumbIsFor.Replace(" ,", ",").Replace(", ", ",");
                    if (wardrobeThumb.thumbIsFor.IndexOf(",") == -1)
                    {
                        thumbIsForArray = new string[1] { wardrobeThumb.thumbIsFor };
                    }
                    else
                    {
                        thumbIsForArray = wardrobeThumb.thumbIsFor.Split(new string[1] { "," }, StringSplitOptions.RemoveEmptyEntries);
                    }
                    foreach(string thumbFor in thumbIsForArray)
                    {
                        if (thumbFor == thumbToGet)
                        {
                            foundSprite = wardrobeThumb.thumb;
                            break;
                        }
                    }
                }
                return foundSprite;
            }

        }

        #endregion
    }
}
