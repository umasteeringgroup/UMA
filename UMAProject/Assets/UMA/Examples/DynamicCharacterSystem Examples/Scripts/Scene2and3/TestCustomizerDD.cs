using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMA.Examples;

namespace UMA.CharacterSystem.Examples
{
	public class TestCustomizerDD : MonoBehaviour
	{
		//SharedColorTableItem makes it possible to have more color tables than just skin/hair/cloth
		//So a slot has a shared color name and this iterates over the shared color tables to see if a table with that name exists
		//if it does it uses it, if not it uses the GenericColorList
		[System.Serializable]
		public class SharedColorTableItem
		{
			public string name;
			public SharedColorTable sharedColorTable;
			public Sprite swatch;
			public Sprite swatchMetallic;
		}

		public DynamicCharacterAvatar Avatar;
		//public DynamicCharacterSystem characterSystem;
		//ConverterCustomizer is an editor only tool
		public DynamicDNAConverterCustomizer converterCustomizer;

		public SharedColorTable GenericColorList;
		public Sprite genericColorSwatch;
		public Sprite genericColorSwatchMetallic;
		[SerializeField]
		public List<SharedColorTableItem> sharedColorTables = new List<SharedColorTableItem>();

		public Dropdown changeRaceDropdown;
		List<string> raceDropdownOptions;

		public GameObject colorDropdownPrefab;
		public GameObject wardrobeDrodownPrefab;
		public GameObject raceDropdownPrefab;
		public GameObject loadableItemPrefab;

		public Button cancelLoadItem;
		public Button saveCompleteBut;
		public GameObject colorDropdownPanel;
		public GameObject wardrobeDropdownPanel;
		public DNAPanel faceEditor;
		public DNAPanel bodyEditor;

		[Tooltip("If set, ONLY the wardrobe slots specified have controls generated. TIP: you can limit this restriction to a race by prefixing the racename to the wardrobe slot name eg ToonFemale:Face")]
		public List<string> limitWardrobeOptions = new List<string>();
		[Tooltip("If set, this prevents the SPECIFIED controls from being generated. TIP: you can limit this restriction to a race by prefixing the racename to the wardrobe slot name eg ToonFemale:Face")]
		public List<string> hideWardrobeOptions = new List<string>();

		public UMAMouseOrbitImproved Orbitor;
		//Loading options when loading a recipe from a text file
		public bool _loadRace = true;
		public bool _loadDNA = true;
		public bool _loadWardrobe = true;
		public bool _loadBodyColors = true;
		public bool _loadWardrobeColors = true;
		public UMAContextBase Context;

		public bool LoadRace
		{
			get { return _loadRace; }
			set { _loadRace = value; }
		}
		public bool LoadDNA
		{
			get { return _loadDNA; }
			set { _loadDNA = value; }
		}
		public bool LoadWardrobe
		{
			get { return _loadWardrobe; }
			set { _loadWardrobe = value; }
		}
		public bool LoadBodyColors
		{
			get { return _loadBodyColors; }
			set { _loadBodyColors = value; }
		}
		public bool LoadWardrobeColors
		{
			get { return _loadWardrobeColors; }
			set { _loadWardrobeColors = value; }
		}

		//Loading options when Changing Race
		bool _keepDNA = false;
		bool _keepWardrobe = true;
		bool _keepBodyColors = true;

		public bool KeepDNA
		{
			get { return _keepDNA; }
			set { _keepDNA = value; }
		}
		public bool KeepWardrobe
		{
			get { return _keepWardrobe; }
			set { _keepWardrobe = value; }
		}
		public bool KeepBodyColors
		{
			get { return _keepBodyColors; }
			set { _keepBodyColors = value; }
		}

		//Loading options when Changing Race
		bool _saveDNA = true;
		bool _saveWardrobe = true;
		bool _saveColors = true;

		public bool SaveDNA
		{
			get { return _saveDNA; }
			set { _saveDNA = value; }
		}
		public bool SaveWardrobe
		{
			get { return _saveWardrobe; }
			set { _saveWardrobe = value; }
		}
		public bool SaveColors
		{
			get { return _saveColors; }
			set { _saveColors = value; }
		}

		string thisRace;

		public void Start()
		{
			if (Avatar == null)
			{
				return;
			}
			Avatar.CharacterCreated.AddListener(Init);
			//converterCustomizer is editor only
#if UNITY_EDITOR
			if (converterCustomizer == null)
			{
				converterCustomizer = GameObject.FindObjectOfType<DynamicDNAConverterCustomizer>();
			}
			else
			{
				converterCustomizer.BonesCreated.AddListener(BonesCreated);
			}
#endif
		}

		private void BonesCreated()
		{
			CloseAllPanels();
		}

		public void Init(UMA.UMAData umaData)
		{
			Avatar.CharacterCreated.RemoveListener(Init);
			thisRace = Avatar.activeRace.name;
		}

		public void SetAvatar(GameObject newAvatarObject)
		{
			CloseAllPanels();
			if (Avatar == null || newAvatarObject != Avatar.gameObject)
			{
				Avatar = newAvatarObject.GetComponent<DynamicCharacterAvatar>();
				if (Avatar != null)
				{
					if (Orbitor != null)
						Orbitor.SwitchTarget(Avatar.gameObject.transform);
					thisRace = Avatar.activeRace.name;
				}
				else
				{
					//Its the Overview target object
					if (Orbitor != null)
					{
						Orbitor.SwitchTarget(newAvatarObject.transform);
						Orbitor.distance = 1.5f;
					}
					Avatar = null;
				}
			}
			else
			{
				if (newAvatarObject == Avatar.gameObject)
					return;
				if (Orbitor != null)
				{
					Orbitor.SwitchTarget(newAvatarObject.transform);
					Orbitor.distance = 1.5f;
				}
				Avatar = null;
			}
		}
		public void SetUpRacesDropdown(string selected = "")
		{
			string newSelected = selected;
			if (newSelected == "")
			{
				if (Avatar != null)
				{
					newSelected = Avatar.activeRace.name;
				}
			}
			changeRaceDropdown.options.Clear();
			changeRaceDropdown.onValueChanged.RemoveListener(ChangeRace);
			var raceDropdownOptionsArray = Avatar.context.GetAllRacesBase();
			raceDropdownOptions = new List<string>();
			//add the 'NoneSet'
			raceDropdownOptions.Add("None Set");
			foreach (RaceData r in raceDropdownOptionsArray)
			{
				if (r.raceName != "PlaceholderRace" && r.raceName != "RaceDataPlaceholder")
					raceDropdownOptions.Add(r.raceName);
			}
			for (int i = 0; i < raceDropdownOptions.Count; i++)
			{
				var thisddOption = new Dropdown.OptionData();
				thisddOption.text = raceDropdownOptions[i];
				changeRaceDropdown.options.Add(thisddOption);
			}
			for (int i = 0; i < raceDropdownOptions.Count; i++)
			{
				if (raceDropdownOptions[i] == newSelected)
				{
					changeRaceDropdown.value = i;
				}
			}
			// we also need to make the raceChangeOptions toggles match the settings in the component
			Toggle[] thisChangeRaceToggles = null;
			if(changeRaceDropdown.template.Find("ChangeRaceOptsHolder") != null)
				if(changeRaceDropdown.template.Find("ChangeRaceOptsHolder").Find("ChangeRaceToggles") != null)
					if(changeRaceDropdown.template.Find("ChangeRaceOptsHolder").Find("ChangeRaceToggles").GetComponentsInChildren<Toggle>().Length != 0)
						thisChangeRaceToggles = changeRaceDropdown.template.Find("ChangeRaceOptsHolder").Find("ChangeRaceToggles").GetComponentsInChildren<Toggle>();
			if(thisChangeRaceToggles != null)
			for(int i = 0; i < thisChangeRaceToggles.Length; i++)
			{
				if (thisChangeRaceToggles[i].gameObject.name == "KeepDNA")
					thisChangeRaceToggles[i].isOn = _keepDNA;
				else if (thisChangeRaceToggles[i].gameObject.name == "KeepWardrobe")
					thisChangeRaceToggles[i].isOn = _keepWardrobe;
				else if (thisChangeRaceToggles[i].gameObject.name == "KeepBodyColors")
					thisChangeRaceToggles[i].isOn = _keepBodyColors;
			}
			changeRaceDropdown.onValueChanged.AddListener(ChangeRace);
		}
		public void ChangeRace(string racename)
		{
			if (Avatar == null)
				return;
			CloseAllPanels();
			int raceInt = -1;
			for (int i = 0; i < changeRaceDropdown.options.Count; i++)
			{
				if (changeRaceDropdown.options[i].text == racename)
				{
					raceInt = i;
					break;
				}
			}
			if (raceInt != -1)
				ChangeRace(raceInt);
			else
			{
				//this must be a newly Downloaded Race so just let CharacterAvatar deal with it...
				DynamicCharacterAvatar.ChangeRaceOptions thisLoadOptions = DynamicCharacterAvatar.ChangeRaceOptions.none;
				if (_keepDNA || _keepWardrobe || _keepBodyColors)
				{
					if (_keepDNA)
						thisLoadOptions |= DynamicCharacterAvatar.ChangeRaceOptions.keepDNA;
					if (_keepWardrobe)
						thisLoadOptions |= DynamicCharacterAvatar.ChangeRaceOptions.keepWardrobe;
					if (_keepBodyColors)
						thisLoadOptions |= DynamicCharacterAvatar.ChangeRaceOptions.keepBodyColors;

					thisLoadOptions &= ~DynamicCharacterAvatar.ChangeRaceOptions.none;
				}
				Avatar.ChangeRace(racename, thisLoadOptions);
			}
		}
		public void ChangeRace(int raceId)
		{
			var RaceToSet = raceDropdownOptions[raceId];
			if (RaceToSet != Avatar.activeRace.name)
			{
				CloseAllPanels();
				thisRace = RaceToSet;
				//Force CharacterSystem to find the new race - unless its None Set
				if(RaceToSet != "None Set")
					UMAContextBase.Instance.GetRace(RaceToSet);
				DynamicCharacterAvatar.ChangeRaceOptions thisLoadOptions = DynamicCharacterAvatar.ChangeRaceOptions.none;
				if (_keepDNA || _keepWardrobe || _keepBodyColors)
				{
					if (_keepDNA)
						thisLoadOptions |= DynamicCharacterAvatar.ChangeRaceOptions.keepDNA;
					if (_keepWardrobe)
						thisLoadOptions |= DynamicCharacterAvatar.ChangeRaceOptions.keepWardrobe;
					if (_keepBodyColors)
						thisLoadOptions |= DynamicCharacterAvatar.ChangeRaceOptions.keepBodyColors;

					thisLoadOptions &= ~DynamicCharacterAvatar.ChangeRaceOptions.none;
				}
				Avatar.ChangeRace(RaceToSet, thisLoadOptions);
			}
		}

		public void InitializeWardrobeDropDowns()
		{
			List<string> slotsFromAllRaces = new List<string>();

			List<RaceData> races = UMAAssetIndexer.Instance.GetAllAssets<RaceData>();
			foreach (RaceData rd in races)
			{
				string race = rd.raceName;
				int i = 0;
				var recipes = UMAAssetIndexer.Instance.GetRecipes(race);
				foreach (string slot in recipes.Keys)
				{
					if (!slotsFromAllRaces.Contains(slot) && ((limitWardrobeOptions.Count == 0 || limitWardrobeOptions.Contains(slot)) && !hideWardrobeOptions.Contains(slot)))
					{
						slotsFromAllRaces.Insert(i, slot);
						i++;
					}
				}
			}
			foreach (string slot in slotsFromAllRaces)
			{
				if (slot == "None")
					continue;
				if (wardrobeDropdownPanel.transform.Find(slot + "DropdownHolder") == null)
				{
					GameObject thisWardrobeDropdown = Instantiate(wardrobeDrodownPrefab) as GameObject;
					thisWardrobeDropdown.transform.SetParent(wardrobeDropdownPanel.transform, false);
					thisWardrobeDropdown.GetComponent<CSWardrobeSlotChangerDD>().customizerScript = this;
					thisWardrobeDropdown.GetComponent<CSWardrobeSlotChangerDD>().wardrobeSlotToChange = slot;
					thisWardrobeDropdown.name = slot + "DropdownHolder";
					thisWardrobeDropdown.transform.Find("SlotLabel").GetComponent<Text>().text = slot;
					thisWardrobeDropdown.GetComponent<Dropdown>().onValueChanged.AddListener(thisWardrobeDropdown.GetComponent<CSWardrobeSlotChangerDD>().ChangeWardrobeSlot);
				}
			}
		}


		public void SetUpWardrobeDropdowns()
		{
			if (Avatar != null)
				thisRace = Avatar.activeRace.name;
			var raceRecipes = UMAAssetIndexer.Instance.GetRecipes(thisRace);
			InitializeWardrobeDropDowns();
			foreach (Transform child in wardrobeDropdownPanel.transform)
			{
				child.gameObject.SetActive(true);
				var thisDD = child.GetComponent<Dropdown>();
				thisDD.captionImage.overrideSprite = null;
				var thisSlot = child.GetComponent<CSWardrobeSlotChangerDD>().wardrobeSlotToChange;
				//We want to have the option of being able to write limitWardrobeOptions and hideWardrobeOptions like raceName:wrdrobeSlot
				bool showOption = false;
				if (!hideWardrobeOptions.Contains(thisSlot) && !hideWardrobeOptions.Contains(thisRace + ":" + thisSlot))
				{
					showOption = true;
				}
				if (limitWardrobeOptions.Contains(thisSlot) || limitWardrobeOptions.Contains(thisRace + ":" + thisSlot))
				{
					showOption = true;
				}
				if (raceRecipes.ContainsKey(thisSlot) && showOption)
				{
					if (thisSlot == "WardrobeCollection")
					{
						SetUpWardrobeCollectionDropdown(child, thisDD);
					}
					else
					{
						thisDD.options.Clear();
						thisDD.onValueChanged.RemoveAllListeners();
						var wardrobeOptions = new List<UMATextRecipe>(raceRecipes[thisSlot]);
						var thisUnsetThumb = Avatar.activeRace.racedata.raceThumbnails.GetThumbFor(thisSlot);
						var thisUnsetOption = new Dropdown.OptionData();
						thisUnsetOption.text = thisSlot == "Face" ? "Standard" : "None";
						thisUnsetOption.image = thisUnsetThumb;
						thisDD.options.Add(thisUnsetOption);
						for (int i = 0; i < wardrobeOptions.Count; i++)
						{
							var thisddOption = new Dropdown.OptionData();
							thisddOption.text = wardrobeOptions[i].DisplayValue != "" ? wardrobeOptions[i].DisplayValue : wardrobeOptions[i].name;
							thisddOption.image = wardrobeOptions[i].GetWardrobeRecipeThumbFor(thisRace);
							thisDD.options.Add(thisddOption);
						}
						int selected = 0;
						UMATextRecipe thisDDRecipe = null;
						if (Avatar.WardrobeRecipes.Count > 0)
						{
							foreach (KeyValuePair<string, UMATextRecipe> kp in Avatar.WardrobeRecipes)
							{
								var recipeSlotName = kp.Value.wardrobeSlot;
								if (recipeSlotName == thisSlot && kp.Value.compatibleRaces.Contains(thisRace))
								{
									for (int ri = 0; ri < raceRecipes[recipeSlotName].Count; ri++)
									{
										if (raceRecipes[recipeSlotName][ri].name == kp.Value.name)
										{
											//we could do alot more checks here to check equalness if this is the only way to make this work...
											selected = ri + 1;
										}
									}
									thisDDRecipe = kp.Value;
								}
								else if (recipeSlotName == thisSlot && (Avatar.activeRace.racedata.IsCrossCompatibleWith(kp.Value.compatibleRaces) && Avatar.activeRace.racedata.wardrobeSlots.Contains(thisSlot)))
								{
									//for cross compatible Races- races can be cross compatible with other races (set in the Race itself) and this enables one race to wear anothers wardrobe (if that race has the same wardrobe slots)
									selected = (raceRecipes[recipeSlotName].FindIndex(s => s.Equals(kp.Value)) + 1);
									thisDDRecipe = kp.Value;
								}
							}
						}
						thisDD.value = selected;
						if (selected == 0)
						{
							thisDD.captionImage.sprite = thisUnsetThumb;
							thisDD.captionImage.enabled = true;
						}
						else
						{
							thisDD.captionImage.sprite = thisDDRecipe.GetWardrobeRecipeThumbFor(thisRace);
							thisDD.captionImage.enabled = true;
						}
						thisDD.onValueChanged.AddListener(child.GetComponent<CSWardrobeSlotChangerDD>().ChangeWardrobeSlot);
						var thisSuppressedTextGO = thisDD.gameObject.transform.Find("ActiveWSlot").Find("SuppressedIndicator");
						thisDD.interactable = true;
						if (thisSuppressedTextGO)
						{
							thisSuppressedTextGO.GetComponent<Image>().enabled = false;
							thisSuppressedTextGO.GetComponentInChildren<Text>().text = "";
						}
					}
				}
				else
				{
					child.gameObject.SetActive(false);
				}
			}
			if (Avatar != null)
				UpdateSuppressedWardrobeDropdowns();
		}

		private void SetUpWardrobeCollectionDropdown(Transform childGO, Dropdown thisDD)
		{
			var raceRecipes = UMAAssetIndexer.Instance.GetRecipes(thisRace);
			var thisSlot = "WardrobeCollection";
			thisDD.options.Clear();
			thisDD.onValueChanged.RemoveAllListeners();
			var wardrobeOptions = new List<UMATextRecipe>(raceRecipes[thisSlot]);
			var thisUnsetThumb = Avatar.activeRace.racedata.raceThumbnails.GetThumbFor(thisSlot);
			var thisDummyOption = new Dropdown.OptionData();
			thisDummyOption.text = "Dummy";
			thisDummyOption.image = thisUnsetThumb;
			thisDD.options.Add(thisDummyOption);
			var thisUnsetOption = new Dropdown.OptionData();
			thisUnsetOption.text = "Remove All";
			thisUnsetOption.image = thisUnsetThumb;
			thisDD.options.Add(thisUnsetOption);
			int activeWCs = 0;
			int appliedWCs = 0;
			for (int i = 0; i < wardrobeOptions.Count; i++)
			{
				var thisddOption = new Dropdown.OptionData();
				thisddOption.text = wardrobeOptions[i].DisplayValue != "" ? wardrobeOptions[i].DisplayValue : wardrobeOptions[i].name;
				thisddOption.image = wardrobeOptions[i].GetWardrobeRecipeThumbFor(thisRace);
				thisDD.options.Add(thisddOption);
				if (Avatar.GetWardrobeCollection(wardrobeOptions[i].name))
				{
					activeWCs++;
					if(Avatar.IsCollectionApplied(wardrobeOptions[i].name))
						appliedWCs++;
				}
			}
			thisDD.value = 0;
			thisDD.transform.Find("SlotLabel").GetComponent<Text>().text = "";
			thisDD.captionImage.sprite = thisUnsetThumb;
			thisDD.captionImage.enabled = true;
			if (thisDD.gameObject.GetComponent<EventTrigger>() == null)
			{
				var trigger = thisDD.gameObject.AddComponent<EventTrigger>();
				EventTrigger.Entry entry = new EventTrigger.Entry();
				entry.eventID = EventTriggerType.PointerClick;
				entry.callback.AddListener(UpdateWardrobeCollectionDropdownOpts);
				trigger.triggers.Add(entry);
			}
			thisDD.onValueChanged.AddListener(childGO.GetComponent<CSWardrobeSlotChangerDD>().ChangeWardrobeSlot);
			var thisSuppressedTextGO = thisDD.gameObject.transform.Find("ActiveWSlot").Find("SuppressedIndicator");
			thisDD.interactable = true;
			if (thisSuppressedTextGO)
			{
				thisSuppressedTextGO.GetComponent<Image>().enabled = true;
				var activeString = activeWCs > 0 ? "\r\n(" + activeWCs + " ACTIVE)" : "";
				var appliedString = activeWCs > 0 ? "\r\n(" + appliedWCs + " APPLIED)" : "";
				thisSuppressedTextGO.GetComponentInChildren<Text>().text = "WARDROBE COLLECTIONS "+ activeString + appliedString;
				thisSuppressedTextGO.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
            }
		}

		public void UpdateWardrobeCollectionDropdownOpts(BaseEventData eventData)
		{
			var selectedItem = eventData.selectedObject;
			var selectedItemCheckMark = selectedItem.transform.Find("Item Checkmark");
			//var selectedSprite = selectedItemCheckMark.GetComponent<Image>().sprite;
			selectedItemCheckMark.GetComponent<Image>().enabled = false;
			selectedItem.SetActive(false);
			var allItems = selectedItem.transform.parent.GetComponentsInChildren<Toggle>();
			for (int i = 0; i < allItems.Length; i++)
			{
				if (allItems[i].gameObject == selectedItem)
					continue;
				var collectionName = allItems[i].gameObject.name.Split(new string[] { ": " }, StringSplitOptions.RemoveEmptyEntries)[1];
				if (Avatar.GetWardrobeCollection(collectionName))
				{
					var forcedCheckmark = Instantiate<GameObject>(selectedItemCheckMark.gameObject);
					forcedCheckmark.name = "forcedCheckmark";
					forcedCheckmark.transform.SetParent(allItems[i].gameObject.transform, false);
					forcedCheckmark.GetComponent<Image>().enabled = true;
				}
				else
				{
					if (allItems[i].gameObject.transform.Find("forcedCheckMark") != null)
					{
						Destroy(allItems[i].gameObject.transform.Find("forcedCheckMark"));
					}
				}
			}
		}

		public void UpdateSuppressedWardrobeDropdowns()
		{
			if (Avatar.WardrobeRecipes.Count == 0)
				return;
			foreach (KeyValuePair<string, UMATextRecipe> kp in Avatar.WardrobeRecipes)
			{
				if (kp.Value.suppressWardrobeSlots == null)
					continue;
				var suppressedSlots = kp.Value.suppressWardrobeSlots;
				foreach (Transform child in wardrobeDropdownPanel.transform)
				{
					var thisDD = child.GetComponent<Dropdown>();
					var thisSuppressedTextGO = thisDD.gameObject.transform.Find("ActiveWSlot").Find("SuppressedIndicator");
					thisDD.captionImage.overrideSprite = null;
					var thisSlot = child.GetComponent<CSWardrobeSlotChangerDD>().wardrobeSlotToChange;
					if (suppressedSlots.Contains(thisSlot))
					{
						thisDD.onValueChanged.RemoveAllListeners();
						thisDD.value = 0;
						//make the suppressed slot show the image of the item it is being suppressed by
						thisDD.captionImage.overrideSprite = kp.Value.GetWardrobeRecipeThumbFor(thisRace);
						thisDD.interactable = false;
						if (thisSuppressedTextGO)
						{
							thisSuppressedTextGO.GetComponent<Image>().enabled = true;
							thisSuppressedTextGO.GetComponentInChildren<Text>().text = "Suppressed by " + kp.Value.name + " on " + kp.Value.wardrobeSlot;
						}
					}
				}
			}
		}

		public void SetUpColorDropdowns()
		{
			UMA.UMAData umaData = Avatar.umaData;
			thisRace = Avatar.activeRace.name;
			var currentColorDropdowns = colorDropdownPanel.transform.GetComponentsInChildren<CSColorChangerDD>(true);
			List<string> activeColorDropdowns = new List<string>();
			//foreach (DynamicCharacterAvatar.ColorValue colorType in Avatar.characterColors.Colors)
			//using new colorvaluestuff
			foreach (OverlayColorData colorType in Avatar.characterColors.Colors)
			{
				activeColorDropdowns.Add(colorType.name);
				bool dropdownExists = false;
				foreach (CSColorChangerDD colorDropdown in currentColorDropdowns)
				{
					if (colorDropdown.colorToChange == colorType.name)
					{
						dropdownExists = true;
						colorDropdown.gameObject.SetActive(true);
						SetUpColorDropdownValue(colorDropdown, colorType);
						break;
					}
				}
				if (!dropdownExists)
				{
					GameObject thisColorDropdown = Instantiate(colorDropdownPrefab) as GameObject;
					thisColorDropdown.transform.SetParent(colorDropdownPanel.transform, false);
					thisColorDropdown.GetComponent<CSColorChangerDD>().customizerScript = this;
					thisColorDropdown.GetComponent<CSColorChangerDD>().colorToChange = colorType.name;
					thisColorDropdown.name = colorType.name + "DropdownHolder";
					thisColorDropdown.transform.Find("SlotLabel").GetComponent<Text>().text = colorType.name + " Color";
					thisColorDropdown.GetComponent<DropdownWithColor>().onValueChanged.AddListener(thisColorDropdown.GetComponent<CSColorChangerDD>().ChangeColor);
					SetUpColorDropdownValue(thisColorDropdown.GetComponent<CSColorChangerDD>(), colorType);
				}
			}
			foreach (CSColorChangerDD colorDropdown in colorDropdownPanel.transform.GetComponentsInChildren<CSColorChangerDD>())
			{
				bool keepOptionActive = false;
				foreach (UMA.OverlayColorData ucd in umaData.umaRecipe.sharedColors)
				{
					if (colorDropdown.colorToChange == ucd.name)
					{
						keepOptionActive = true;
						break;
					}
				}
				if (!keepOptionActive)
				{
					colorDropdown.gameObject.SetActive(false);
				}
			}
		}
		public void SetUpColorDropdownValue(CSColorChangerDD colorDropdown, OverlayColorData colorType)
		{
			if (GenericColorList == null)
			{
				if (Debug.isDebugBuild)
					Debug.LogWarning("[TestCustomizerDD] the GenericColorList was null or missing, this must be set.");
				return;
			}
			int colorTableSelected = -1;
			SharedColorTable thisColorTable = null;
			if (sharedColorTables.FindIndex(s => s.name == colorType.name) > -1)
			{
				thisColorTable = sharedColorTables[sharedColorTables.FindIndex(s => s.name == colorType.name)].sharedColorTable;
				if (thisColorTable == null)
				{
					if (Debug.isDebugBuild)
						Debug.LogWarning("[TestCustomizerDD] the colorList for " + colorType.name + " was null or missing, please set this or remove it from the list.");
					return;
				}
				for (int i = 0; i < thisColorTable.colors.Length; i++)
				{
					if (ColorToHex(thisColorTable.colors[i].color) == ColorToHex(colorType.color))
					{
						colorTableSelected = i;
						break;
					}
				}
			}
			else
			{
				thisColorTable = GenericColorList;
				for (int i = 0; i < GenericColorList.colors.Length; i++)
				{
					if (ColorToHex(GenericColorList.colors[i].color) == ColorToHex(colorType.color))
					{
						colorTableSelected = i;
						break;
					}
				}
			}
			SetUpColorDropdownOptions(colorDropdown, thisColorTable, colorTableSelected, colorType);
		}

		public void SetUpColorDropdownOptions(CSColorChangerDD colorDropdown, SharedColorTable colorTable, int colorTableSelected, OverlayColorData activeColor)
		{
			var thisDD = colorDropdown.gameObject.GetComponent<DropdownWithColor>();
			thisDD.ClearOptions();
			thisDD.onValueChanged.RemoveAllListeners();
			Color selectedColor = activeColor.color;
			var colorBlack = new Color(0, 0, 0, 0);
			bool selectedColorFound = false;
			for (int i = 0; i < colorTable.colors.Length; i++)
			{
				var thisddOption = new DropdownWithColor.OptionData();
				thisddOption.text = colorTable.colors[i].name;
				thisddOption.color = colorTable.colors[i].color;
				Sprite spriteToUse = genericColorSwatch;
				if (colorTable.colors[i].channelAdditiveMask.Length >= 3)
				{
					if (colorTable.colors[i].channelAdditiveMask[2] != colorBlack)
					{
						spriteToUse = genericColorSwatchMetallic;
					}

				}
				if (i == colorTableSelected)
				{
					selectedColorFound = true;
					selectedColor = colorTable.colors[i].color;
				}
				thisddOption.image = spriteToUse;
				thisDD.options.Add(thisddOption);
			}
			if (selectedColorFound)
			{
				thisDD.value = colorTableSelected;
				thisDD.captionImage.color = selectedColor;
			}
			
			thisDD.RefreshShownValue();
			thisDD.onValueChanged.AddListener(colorDropdown.ChangeColor);
		}

		public string ColorToHex(Color32 color)
		{
			string hex = color.r.ToString("X2") + color.g.ToString("X2") + color.b.ToString("X2") + color.a.ToString("X2");
			return hex;
		}

		public void SetColor(string colorName, float fColor)
		{
			int Col = (int)fColor;
			if (sharedColorTables.FindIndex(s => s.name == colorName) > -1)
			{
				Avatar.SetColor(colorName, sharedColorTables[sharedColorTables.FindIndex(s => s.name == colorName)].sharedColorTable.colors[Col]);
			}
			else
			{
				Avatar.SetColor(colorName, GenericColorList.colors[Col]);
			}
		}

		/// <summary>
		/// Sets any wardrobe slot. If a negative value is passed, then the slot is cleared.
		/// </summary>
		/// <param name="slotToChange">name of the wordrobe slot to change</param>
		/// <param name="fSlotNumber">Id number slot to change</param>
		public void SetSlot(string slotToChange, float fSlotNumber)
		{
			var raceRecipes = UMAAssetIndexer.Instance.GetRecipes(thisRace);
			if (slotToChange == "WardrobeCollection")
			{
				SetWardrobeCollectionSlot(slotToChange, fSlotNumber);
			}
			else
			{
				var thisRace = Avatar.activeRace.name;
				int slotNumber = (int)fSlotNumber;
				UMATextRecipe tr = null;
				if (slotNumber >= 0)
				{
					tr = raceRecipes[slotToChange][slotNumber];
					Avatar.SetSlot(tr);
				}
				else
				{
					Avatar.ClearSlot(slotToChange);
					Avatar.ReapplyWardrobeCollections();
				}
			}
			Avatar.BuildCharacter(true);
			//Update the dropdowns to reflect any changes after the character has built
			SetUpWardrobeDropdowns();
		}

		public void SetWardrobeCollectionSlot(string slotToChange, float fSlotNumber)
		{
			var raceRecipes = UMAAssetIndexer.Instance.GetRecipes(thisRace);
			int slotNumber = ((int)fSlotNumber) - 1;
			if (slotNumber >= 0)
			{
				var wc = raceRecipes[slotToChange][slotNumber];
				if (Avatar.GetWardrobeCollection(wc.name))
				{
					Avatar.UnloadWardrobeCollection(wc.name);
				}
				else
				{
					Avatar.SetSlot(wc);
				}
			}
			else
			{
				Avatar.UnloadAllWardrobeCollections();
			}

		}

		public void CloseAllPanels()
		{
			faceEditor.transform.parent.gameObject.SetActive(false);
			bodyEditor.transform.parent.gameObject.SetActive(false);
			colorDropdownPanel.SetActive(false);
			wardrobeDropdownPanel.SetActive(false);
		}

		public void ShowHideWardrobeDropdowns()
		{
			if (wardrobeDropdownPanel.activeSelf)
			{
				wardrobeDropdownPanel.SetActive(false);
			}
			else
			{
				SetUpWardrobeDropdowns();
				if (Orbitor != null)
				{
					TargetBody();
				}
				wardrobeDropdownPanel.SetActive(true);
				colorDropdownPanel.SetActive(false);
				faceEditor.transform.parent.gameObject.SetActive(false);
				bodyEditor.transform.parent.gameObject.SetActive(false);
			}
		}

		public void ShowHideColorDropdowns()
		{
			if (colorDropdownPanel.activeSelf)
			{
				colorDropdownPanel.SetActive(false);
			}
			else
			{
				SetUpColorDropdowns();
				if (Orbitor != null)
				{
					TargetBody();
				}
				colorDropdownPanel.SetActive(true);
				wardrobeDropdownPanel.SetActive(false);
				faceEditor.transform.parent.gameObject.SetActive(false);
				bodyEditor.transform.parent.gameObject.SetActive(false);
			}
		}

		public void ShowHideFaceDNA()
		{
			if (faceEditor.transform.parent.gameObject.activeSelf)
			{
				faceEditor.transform.parent.gameObject.SetActive(false);
				if (Orbitor != null)
				{
					TargetBody();
				}
			}
			else
			{
				faceEditor.Initialize(Avatar);
				if (Orbitor != null)
				{
					TargetFace();
				}
				faceEditor.transform.parent.gameObject.SetActive(true);
				bodyEditor.transform.parent.gameObject.SetActive(false);
				colorDropdownPanel.SetActive(false);
				wardrobeDropdownPanel.SetActive(false);
			}
		}

		public void ShowHideBodyDNA()
		{
			if (bodyEditor.transform.parent.gameObject.activeSelf)
			{
				bodyEditor.transform.parent.gameObject.SetActive(false);
			}
			else
			{
				bodyEditor.Initialize(Avatar);
				if (Orbitor != null)
				{
					TargetBody();
				}
				bodyEditor.transform.parent.gameObject.SetActive(true);
				faceEditor.transform.parent.gameObject.SetActive(false);
				colorDropdownPanel.SetActive(false);
				wardrobeDropdownPanel.SetActive(false);
			}
		}

		/// <summary>
		/// Point the mouse orbitor at the body center
		/// </summary>
		public void TargetBody()
		{
			if (Orbitor != null)
			{
				Orbitor.distance = 1.4f;
				Orbitor.TargetBone = UMAMouseOrbitImproved.targetOpts.Chest;
			}
		}

		/// <summary>
		/// Point the mouse orbitor at the neck, so you can see the face.
		/// </summary>
		public void TargetFace()
		{
			if (Orbitor != null)
			{
				Orbitor.distance = 0.5f;
				Orbitor.TargetBone = UMAMouseOrbitImproved.targetOpts.Head;
			}
		}

		#region Load and Save
		public void LoadRecipe()
		{
			if (Avatar != null)
				Avatar.DoLoad();
		}

		public void SaveRecipe()
		{
			if (Avatar != null)
				Avatar.DoSave();
		}

		public void ListLoadableFiles(ScrollRect ItemList)
		{
			var thisSubFolder = Avatar.loadPath.TrimStart('\\', '/').TrimEnd('\\', '/').Trim();//trim this at the start and the end of slashes. And then we may need to switch any slashes in the middle depending on the platform- for explode on them or something
																							//we can find things in resources/thisSubFolder and in persistentDataPath/thisSubFolder
																							//Clear the item list
			ItemList.content.GetComponent<VerticalLayoutGroup>().enabled = false;//dont seem to be able to clear the content with this on...
			foreach (Transform child in ItemList.content.transform)
			{
				Destroy(child.gameObject);
			}
			ItemList.content.GetComponent<VerticalLayoutGroup>().enabled = true;
			//- I need to basically get rid of the other oprions in CharacterAvatars enumerator since they are effectively useless apart from in the editor
			var persistentPath = Path.Combine(Application.persistentDataPath, thisSubFolder);
			if (Directory.Exists(persistentPath))
			{
				string[] persistentDataFiles = Directory.GetFiles(persistentPath, "*.txt");
				foreach (string path in persistentDataFiles)
				{
					GameObject thisLoadableItem = Instantiate(loadableItemPrefab) as GameObject;
					thisLoadableItem.transform.SetParent(ItemList.content.transform, false);
					thisLoadableItem.GetComponent<CSLoadableItem>().customizerScript = this;
					thisLoadableItem.GetComponent<CSLoadableItem>().filepath = path;
					thisLoadableItem.GetComponent<CSLoadableItem>().filename = Path.GetFileNameWithoutExtension(path);
					thisLoadableItem.GetComponentInChildren<Text>().text = Path.GetFileNameWithoutExtension(path);
				}
			}
			
			foreach (string s in UMAContext.Instance.GetRecipeFiles())
			{
				GameObject thisLoadableItem = Instantiate(loadableItemPrefab) as GameObject;
				thisLoadableItem.transform.SetParent(ItemList.content.transform, false);
				thisLoadableItem.GetComponent<CSLoadableItem>().customizerScript = this;
				thisLoadableItem.GetComponent<CSLoadableItem>().filename = Path.GetFileNameWithoutExtension(s);
				thisLoadableItem.GetComponentInChildren<Text>().text = Path.GetFileNameWithoutExtension(s);
			}
		}

		public void LoadListedFile(string filename, string filepath = "")
		{
			cancelLoadItem.onClick.Invoke();
			string recipeText = "";
			if (filepath != "")
			{
				recipeText = FileUtils.ReadAllText(filepath);
			}
			else
			{
				recipeText = UMAContext.Instance.GetCharacterRecipe(filename);
			}
			if (recipeText != "")
			{
				DynamicCharacterAvatar.LoadOptions thisLoadOptions = DynamicCharacterAvatar.LoadOptions.useDefaults;
				if (_loadRace || _loadDNA || _loadWardrobe || _loadBodyColors || _loadWardrobeColors)
				{
					if(_loadRace)
						thisLoadOptions |= DynamicCharacterAvatar.LoadOptions.loadRace;
					if (_loadDNA)
						thisLoadOptions |= DynamicCharacterAvatar.LoadOptions.loadDNA;
					if (_loadWardrobe)
						thisLoadOptions |= DynamicCharacterAvatar.LoadOptions.loadWardrobe;
					if (_loadBodyColors)
						thisLoadOptions |= DynamicCharacterAvatar.LoadOptions.loadBodyColors;
					if (_loadWardrobeColors)
						thisLoadOptions |= DynamicCharacterAvatar.LoadOptions.loadWardrobeColors;

					thisLoadOptions &= ~DynamicCharacterAvatar.LoadOptions.useDefaults;
				}

				Avatar.LoadFromRecipeString(recipeText, thisLoadOptions);
			}
		}

		public void SaveFile(InputField inputField)
		{
			var thisFilename = inputField.text;
			if (thisFilename != "")
			{
				thisFilename = Path.GetFileNameWithoutExtension(thisFilename.Replace(" ", ""));
				if (Debug.isDebugBuild)
					Debug.Log("Saved File with filename " + thisFilename);
				Avatar.saveFilename = thisFilename;

				DynamicCharacterAvatar.SaveOptions thisSaveOptions = DynamicCharacterAvatar.SaveOptions.useDefaults;
				if (_saveDNA || _saveWardrobe || _saveColors)
				{
					if (_saveDNA)
						thisSaveOptions |= DynamicCharacterAvatar.SaveOptions.saveDNA;
					if (_saveWardrobe)
						thisSaveOptions |= DynamicCharacterAvatar.SaveOptions.saveWardrobe;
					if (_saveColors)
						thisSaveOptions |= DynamicCharacterAvatar.SaveOptions.saveColors;

					thisSaveOptions &= ~DynamicCharacterAvatar.SaveOptions.useDefaults;
				}

				Avatar.DoSave(false,"", thisSaveOptions);
			}
			StartCoroutine(FinishSaveFile());
		}

		IEnumerator FinishSaveFile()
		{
			yield return new WaitForSeconds(1f);
			saveCompleteBut.interactable = true;
			saveCompleteBut.onClick.Invoke();
		}
		#endregion
	}
}
