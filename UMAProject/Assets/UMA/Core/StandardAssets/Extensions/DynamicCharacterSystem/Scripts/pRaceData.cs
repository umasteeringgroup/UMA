using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.Serialization;
using System;
using System.Collections.Generic;

namespace UMA
{
    public partial class RaceData
    {
		[Tooltip("UMA Text recipe that holds the slots and overlays that are the default set up for this race.")]
		public UMARecipeBase baseRaceRecipe;
		[Tooltip("Wardobe slots that wardrobe recipes can be assigned to.")]
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

		//UMA26 we want to depricate this- how much do we need to worry about the fact that users could do backwardsCompatibleWith.Add/Remove via scripting before?
		[Obsolete("[RaceData backwardsCompatibleWith is deprecated and will be removed in a future version. Please use RaceData.CrossCompatibleRaces instead.")]
		public List<string> backwardsCompatibleWith = new List<string>();

		//CrossCompatibilitySettings allows this race to wear wardrobe slots from another race, if this race has a wardrobe slot that the recipe is set to.
		//You can further configure the compatibility settings for each compatible race to define 'equivalent' slotdatas in the races' base recipes. For example you could define
		//that this races 'highpolyMaleChest' slotdata in its base recipe is equivalent to HumanMales 'MaleChest' slot data in its base recipe. This would mean that any recipes which hid or applied an overlay to 'MaleChest'
		//would hide or apply an overlay to 'highPolyMaleChest' on this race. If 'Overlays Match' is unchecked then overlays in a recipe wont be applied.
		[SerializeField]
		private CrossCompatibilitySettingsList _crossCompatibilitySettings = new CrossCompatibilitySettingsList();

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
                AddDefaultWardrobeSlots(setToDefault);
                return setToDefault;
            }
            return true;
        }
		//For backwards compatibility
		[Obsolete("findBackwardsCompatibleWith has been depricated and will be removed in a future version. Please use 'IsCrossCompatibleWith' instead.")]
        public bool findBackwardsCompatibleWith(List<string> compatibleStrings)
        {
			return IsCrossCompatibleWith(compatibleStrings);
        }

		/// <summary>
		/// Given a raceNames returns whether this race has been set to be 'cross compatible' with that race.
		/// </summary>
		public bool IsCrossCompatibleWith(RaceData compatibleRace)
		{
			return GetCrossCompatibleRaces().Contains(compatibleRace.raceName);
		}
		/// <summary>
		/// Given a raceNames returns whether this race has been set to be 'cross compatible' with that race.
		/// </summary>
		public bool IsCrossCompatibleWith(string compatibleString)
		{
			return GetCrossCompatibleRaces().Contains(compatibleString);
		}
		/// <summary>
		/// Given a list of raceNames returns whether this race has been set to be 'cross compatible' with any of those races.
		/// </summary>
		public bool IsCrossCompatibleWith(List<string> compatibleStrings)
		{
			foreach (string val in compatibleStrings)
			{
				if (GetCrossCompatibleRaces().Contains(val))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// backwards compatibility: Updates any Races from before _crossCompatibilitySettings so their backwards compatible races are added automatically. If the race is being inspected these changes are saved - remove in a future version
		/// </summary>
		private void UpdateOldRace()
		{
#pragma warning disable 618
			if (_crossCompatibilitySettings.settingsData.Count == 0 && backwardsCompatibleWith.Count > 0)
			{
				SetCrossCompatibleRaces(backwardsCompatibleWith);
				//then clear the backwardsCompatibleWith list and save the asset- the user wont be able to use 'backwardsCompatibleWith' 
				//but they will have been shown a warning if their scripts access the field directly
#if UNITY_EDITOR
				if (Debug.isDebugBuild)
					Debug.Log("RaceData for " + raceName + " updated its backwardsCompatibleWith value to the new CrossCompatibilitySettings. All good.");
				if (!Application.isPlaying)
				{
					//Debug.Log("RaceData for " + raceName + " updated its backwardsCompatibleWith value to the new CrossCompatibilitySettings. All good.");
					backwardsCompatibleWith.Clear();
					EditorUtility.SetDirty(this);
					AssetDatabase.SaveAssets();
				}
#endif
			}
#pragma warning restore 618
		}

		/// <summary>
		/// Returns the list of raceNames that this race has set to be 'cross compatible' with
		/// </summary>
		public List<string> GetCrossCompatibleRaces()
		{
			UpdateOldRace();
			List<string> ccRaces = new List<string>();
			for (int i = 0; i < _crossCompatibilitySettings.settingsData.Count; i++)
				ccRaces.Add(_crossCompatibilitySettings.settingsData[i].ccRace);
			return ccRaces;
		}
		/// <summary>
		/// Sets the races that this race can be 'cross compatible with.
		/// </summary>
		public void SetCrossCompatibleRaces(List<string> ccRaces)
		{
			for (int i = 0; i < ccRaces.Count; i++)
				_crossCompatibilitySettings.Add(ccRaces[i]);
			//then remove any that were not in the list
			List<string> racesToRemove = new List<string>();
			for (int i = 0; i < _crossCompatibilitySettings.settingsData.Count; i++)
			{
				if (!ccRaces.Contains(_crossCompatibilitySettings.settingsData[i].ccRace))
					racesToRemove.Add(_crossCompatibilitySettings.settingsData[i].ccRace);
			}
			_crossCompatibilitySettings.Remove(racesToRemove);
		}
		/// <summary>
		/// Gets the cross compatibility settings that have been defined for this race
		/// </summary>
		protected List<CrossCompatibilityData> GetSettingsFor(string crossCompatibleRace)
		{
			for (int i = 0; i < _crossCompatibilitySettings.settingsData.Count; i++)
			{
				if (_crossCompatibilitySettings.settingsData[i].ccRace == crossCompatibleRace)
					return _crossCompatibilitySettings.settingsData[i].ccSettings;
			}
			return null;
		}
		/// <summary>
		/// If this race has been defined as being 'cross compatible' with any of the given races, this will return the slot in this races base recipe that has been defined as
		/// equivalent to the given slot in the given races (optionally only returning a value if the overlays have ALSO been defined as matching)
		/// </summary>
		/// <param name="races">The cross compatible races to check. i.e. is this race defined as compatible with any of these races</param>
		/// <param name="crossCompatibleSlot">The slot to check. i.e. if this race IS defined as compatible with one of the above races, does it define that it has a slot that is equivalent to the given slot</param>
		/// <param name="overlaysMustMatch">If this is true, an equivalent slot will only be returned if it has also been defined as having Overlay scales that match the cross compatible race</param>
		/// <returns></returns>
		public string FindEquivalentSlot(List<string> races, string crossCompatibleSlot, bool overlaysMustMatch = true)
		{
			for (int i = 0; i < races.Count; i++)
			{
				var foundEquivalent = FindEquivalentSlot(races[i], crossCompatibleSlot, overlaysMustMatch);
				if (foundEquivalent != "")
					return foundEquivalent;
			}
			return "";
		}
		/// <param name="race">The cross compatible race to check. i.e. is this race defined as compatible with the given race</param>
		public string FindEquivalentSlot(string race, string crossCompatibleSlot, bool overlaysMustMatch = true)
		{
			for (int i = 0; i < _crossCompatibilitySettings.settingsData.Count; i++)
			{
				if (_crossCompatibilitySettings.settingsData[i].ccRace == race)
				{
					return _crossCompatibilitySettings.settingsData[i].GetEquivalentSlot(crossCompatibleSlot, overlaysMustMatch);
				}
			}
			return "";
		}
		/// <summary>
		/// Searches this races Cross compatibility settings for the given slot and returns whether its equivalent slot (if defined) has been defined as having compatible overlays
		/// </summary>
		public bool GetOverlayCompatibility(string crossCompatibleSlot)
		{
			for (int i = 0; i < _crossCompatibilitySettings.settingsData.Count; i++)
			{
				var compatibilityForThisRace = _crossCompatibilitySettings.settingsData[i].GetOverlayCompatibility(crossCompatibleSlot);
				if (compatibilityForThisRace != -1)
					return compatibilityForThisRace == 1 ? true : false;
			}
			return false;
		}
		/// <summary>
		/// Searches this races Cross compatibility settings for the given race to find the given slot and returns whether its equivalent slot (if defined) has been defined as having compatible overlays
		/// </summary>
		public bool GetOverlayCompatibility(string race, string crossCompatibleSlot)
		{
			for (int i = 0; i < _crossCompatibilitySettings.settingsData.Count; i++)
			{
				if (_crossCompatibilitySettings.settingsData[i].ccRace == race)
				{
					return _crossCompatibilitySettings.settingsData[i].GetOverlayCompatibility(crossCompatibleSlot) == 1 ? true : false;
				}
			}
			return false;
		}

		#region SpecialTypes

		//new classes for CrossCompatibilitySettings
		//allows the user to define that a given slot in this races base recipe is equal to the named slot in the backwards compatible races base recipe
		[System.Serializable]
		protected class CrossCompatibilityData
		{
			//the slot in this race's baseRaceRecipe
			public string raceSlot = "";
			//the slot in the other races baseRaceRecipe that should be considered equivalent
			public string compatibleRaceSlot = "";
			//are the overlay resolutions the same, if not overlays in recipes on the compatible race will not be copied over
			public bool overlaysMatch;

			public CrossCompatibilityData() { }

			public CrossCompatibilityData(string _raceSlot, string _compatibleRaceSlot)
			{
				raceSlot = _raceSlot;
				compatibleRaceSlot = _compatibleRaceSlot;
			}

			public CrossCompatibilityData(string _raceSlot, string _compatibleRaceSlot, bool _overlaysMatch)
			{
				raceSlot = _raceSlot;
				compatibleRaceSlot = _compatibleRaceSlot;
				overlaysMatch = _overlaysMatch;
			}
		}
		[System.Serializable]
		protected class CrossCompatibilitySettings
		{
			public string ccRace = "";
			public List<CrossCompatibilityData> ccSettings = new List<CrossCompatibilityData>();

			public CrossCompatibilitySettings() { }

			public CrossCompatibilitySettings(string race)
			{
				ccRace = race;
			}
			public CrossCompatibilitySettings(string race, List<CrossCompatibilityData> settings)
			{
				ccRace = race;
				ccSettings = settings;
			}
			/// <summary>
			/// Returns the slot in the COMPATIBLE race that has been defined as equivalent to the given slot from THIS race
			/// </summary>
			public string GetCompatibleRacesSlot(string thisRacesSlot)
			{
				for (int i = 0; i < ccSettings.Count; i++)
				{
					if (ccSettings[i].raceSlot == thisRacesSlot)
						return ccSettings[i].compatibleRaceSlot;
				}
				return "";
			}
			/// <summary>
			/// Returns the slot in THIS race that has been defined as equivalent to the given slot from the COMPATIBLE race
			/// </summary>
			public string GetEquivalentSlot(string compatibleSlot, bool overlaysMustMatch = true)
			{
				for (int i = 0; i < ccSettings.Count; i++)
				{
					if (ccSettings[i].compatibleRaceSlot == compatibleSlot)
						if ((overlaysMustMatch == true && ccSettings[i].overlaysMatch == true) || overlaysMustMatch == false)
							return ccSettings[i].raceSlot;
				}
				return "";
			}
			/// <summary>
			/// Returns whether the given slot in THIS race has been defined as having compatible overlays with the given slot from the COMPATIBLE race
			/// </summary>
			public int GetOverlayCompatibility(string compatibleRaceSlot)
			{
				for (int i = 0; i < ccSettings.Count; i++)
				{
					if (ccSettings[i].compatibleRaceSlot == compatibleRaceSlot)
						return ccSettings[i].overlaysMatch ? 1 : 0;
				}
				return -1;
			}
			/// <summary>
			/// Defines that a slot in THIS race is compatible with the given slot from the COMPATIBLE race (optionally defining overlay compatibility as false)
			/// </summary>
			public void SetEquivalentSlot(string thisRacesSlot, string compatibleRacesSlot = "", bool overlayCompatibility = true)
			{
				bool found = false;
				for (int i = 0; i < ccSettings.Count; i++)
				{
					if (ccSettings[i].raceSlot == thisRacesSlot)
					{
						ccSettings[i].compatibleRaceSlot = compatibleRacesSlot;
						ccSettings[i].overlaysMatch = overlayCompatibility;
						found = true;
					}
				}
				if (!found)
					ccSettings.Add(new CrossCompatibilityData(thisRacesSlot, compatibleRacesSlot, overlayCompatibility));
			}
		}
		[System.Serializable]
		protected class CrossCompatibilitySettingsList
		{

			public List<CrossCompatibilitySettings> settingsData = new List<CrossCompatibilitySettings>();

			public bool Contains(string crossCompatibleRace)
			{
				for (int i = 0; i < settingsData.Count; i++)
				{
					if (settingsData[i].ccRace == crossCompatibleRace)
						return true;
				}
				return false;
			}

			public void Add(string crossCompatibleRace)
			{
				if (!Contains(crossCompatibleRace))
					settingsData.Add(new CrossCompatibilitySettings(crossCompatibleRace));
			}

			public void Remove(List<string> races)
			{
				for (int i = 0; i < races.Count; i++)
					Remove(races[i]);
			}

			public void Remove(string crossCompatibleRace)
			{
				int removeAt = -1;
				for (int i = 0; i < settingsData.Count; i++)
				{
					if (settingsData[i].ccRace == crossCompatibleRace)
						removeAt = i;
				}
				if (removeAt > -1)
					settingsData.RemoveAt(removeAt);
			}
		}
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
