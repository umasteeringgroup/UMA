using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UMACharacterSystem;

public class TestCustomizer : MonoBehaviour
{

	//SharedColorTableItem makes it possible to have more color tables than just skin/hair/cloth
	//So a slot has a shared color name and this iterates over the shared color tables to see if a table with that name exists
	//if it does it uses it, if not it uses the GenericColorList
	[System.Serializable]
	public class SharedColorTableItem
	{
		public string name;
		public SharedColorTable sharedColorTable;
	}

	public DynamicCharacterAvatar Avatar;
	//using Dynamic Character System here as this gets slots/overlays/races from dynamicSlot/Race/Overlay library and also creates the disctionaries based on the wardrobe slots set up in each race
	public DynamicCharacterSystem characterSystem;

	public SharedColorTable GenericColorList;
	[SerializeField]
	public List<SharedColorTableItem> sharedColorTables = new List<SharedColorTableItem>();
	public SharedColorTable Eyes;
	public SharedColorTable Hair;
	public SharedColorTable Skin;
	public SharedColorTable Cloth;

	public Dropdown changeRaceDropdown;
	List<string> raceDropdownOptions;

	public GameObject colorSliderPrefab;
	public GameObject wardrobeSliderPrefab;
	public GameObject raceDropdownPrefab;

	public GameObject colorSlidersPanel;
	public GameObject wardrobeSlidersPanel;
	public GameObject raceDropdownPanel;
	public DNAPanel faceEditor;
	public DNAPanel bodyEditor;

	[Tooltip("If set, ONLY the wardrobe slots specified have sliders generated, else all possible sliders are generated.")]
	public List<string> limitWardrobeOptions = new List<string>();
	[Tooltip("If set, this prevents the SPECIFIED sliders from being generated.")]
	public List<string> hideWardrobeOptions = new List<string>();

	public MouseOrbitImproved Orbitor;

	private bool updatingWardrobeSliders;
	private bool updatingColorSliders = false;
	private bool canUpdateSlidersSetUp = true;

	string thisRace;

	public void Start()
	{
		Avatar.CharacterCreated.AddListener(Init);
	}

	public void Init(UMA.UMAData umaData)
	{
		Avatar.CharacterCreated.RemoveListener(Init);
		thisRace = Avatar.activeRace.racedata.raceName;
		if (changeRaceDropdown != null)
		{
			SetUpRacesDropdown(thisRace);
		}
		InitializeWardrobeSliders();
		SetUpWardrobeSliders();
		SetUpColorSlidersSetUpEvents();
	}

	//This is a dropdown that changes the race- replaces the 'ChangeSex' button
	//Changed to be compatible with Unity 5.2
	public void SetUpRacesDropdown(string selected)
	{
		changeRaceDropdown.options.Clear();
		changeRaceDropdown.onValueChanged.RemoveListener(ChangeRace);
		raceDropdownOptions = new List<string>(characterSystem.Recipes.Keys);
		for (int i = 0; i < raceDropdownOptions.Count; i++)
		{
			var thisddOption = new Dropdown.OptionData();
			thisddOption.text = raceDropdownOptions[i];
			changeRaceDropdown.options.Add(thisddOption);
		}
		for (int i = 0; i < raceDropdownOptions.Count; i++)
		{
			if (raceDropdownOptions[i] == selected)
			{
				changeRaceDropdown.value = i;
			}
		}
		changeRaceDropdown.onValueChanged.AddListener(ChangeRace);
		SetUpRaceChangeSetUpEvents();
	}
	//Replaces ChangeSex and makes it possible to choose from any number of races
	public void ChangeRace(int raceId)
	{
		if (updatingWardrobeSliders)
			return;
		if (updatingColorSliders)
			return;
		var RaceToSet = raceDropdownOptions[raceId];
		if (RaceToSet != Avatar.activeRace.name)
		{
			CloseAllPanels();
			thisRace = RaceToSet;
			UnsetRaceChangeSetUpEvents();
			UnsetColorSlidersSetUpEvents();
			canUpdateSlidersSetUp = true;
			Avatar.activeRace.name = RaceToSet;
			Avatar.activeRace.data = characterSystem.context.raceLibrary.GetRace(RaceToSet);
			Avatar.umaRecipe = Avatar.activeRace.racedata.baseRaceRecipe;
			Avatar.ClearSlots();
			Avatar.LoadDefaultWardrobe();
			SetUpColorSlidersSetUpEvents();
			Avatar.BuildCharacter(false);
			SetUpWardrobeSliders();
			SetUpRaceChangeSetUpEvents();
		}
	}
	//this sets the slider right when the race changes, but what if the race doesn't change but a recipe is loaded that should make the sliders change?
	public void ActiveRaceCheck(UMA.UMAData umadata)
	{
		if (Avatar.activeRace.name != thisRace)
		{
			UnsetRaceChangeSetUpEvents();
			thisRace = Avatar.activeRace.name;
			SetUpRacesDropdown(Avatar.activeRace.name);
			UnsetColorSlidersSetUpEvents();
			//we need to re-initialize in case more wardrobe recipes have been downloaded since Start
			InitializeWardrobeSliders();
			//Set up wardrobe sliders, not working quite right at the moment when the character has been loaded from a file
			SetUpWardrobeSliders();
			canUpdateSlidersSetUp = true;
			SetUpColorSliders(umadata);
		}
	}

	public void SetUpRaceChangeSetUpEvents()
	{
		Avatar.CharacterUpdated.AddListener(ActiveRaceCheck);
	}

	public void UnsetRaceChangeSetUpEvents()
	{
		Avatar.CharacterUpdated.RemoveListener(ActiveRaceCheck);
	}

	public void InitializeWardrobeSliders()
	{
		updatingWardrobeSliders = true;
		List<string> slotsFromAllRaces = new List<string>();
		foreach (string race in characterSystem.Recipes.Keys)
		{
			int i = 0;
			foreach (string slot in characterSystem.Recipes[race].Keys)
			{
				if (!slotsFromAllRaces.Contains(slot) && ((limitWardrobeOptions.Count == 0 || limitWardrobeOptions.Contains(slot)) && !hideWardrobeOptions.Contains(slot)))
				{
					slotsFromAllRaces.Insert(i, slot);
				}
				i++;
			}
		}
		foreach (string slot in slotsFromAllRaces)
		{
			if (slot == "None")
				continue;
			if (wardrobeSlidersPanel.transform.FindChild(slot + "SliderHolder") == null)
			{
				GameObject thisWardrobeSlider = Instantiate(wardrobeSliderPrefab) as GameObject;
				thisWardrobeSlider.transform.SetParent(wardrobeSlidersPanel.transform, false);
				thisWardrobeSlider.GetComponentInChildren<CSWardrobeSlotChanger>().customizerScript = this;
				thisWardrobeSlider.GetComponentInChildren<CSWardrobeSlotChanger>().wardrobeSlotToChange = slot;
				thisWardrobeSlider.name = slot + "SliderHolder";
				thisWardrobeSlider.GetComponentInChildren<Text>().text = slot;
				thisWardrobeSlider.GetComponentInChildren<Slider>().onValueChanged.AddListener(thisWardrobeSlider.GetComponentInChildren<CSWardrobeSlotChanger>().ChangeWardrobeSlot);
			}
		}
		updatingWardrobeSliders = false;
	}

	public void SetUpWardrobeSliders(UMA.UMAData umadata = null)
	{
		updatingWardrobeSliders = true;
		foreach (Transform child in wardrobeSlidersPanel.transform)
		{
			child.gameObject.SetActive(true);
			var thisSlot = child.GetComponentInChildren<CSWardrobeSlotChanger>().wardrobeSlotToChange;
			if (characterSystem.Recipes[thisRace].ContainsKey(thisSlot))
			{
				child.GetComponentInChildren<Slider>().maxValue = characterSystem.Recipes[thisRace][thisSlot].Count - 1;
				bool valueSet = false;
				if (Avatar.WardrobeRecipes.Count > 0)
				{
					foreach (KeyValuePair<string, UMATextRecipe> kp in Avatar.WardrobeRecipes)
					{
						var recipeSlotName = kp.Value.wardrobeSlot;
						if (recipeSlotName == thisSlot && kp.Value.compatibleRaces.Contains(thisRace))
						{
							child.GetComponentInChildren<Slider>().value = characterSystem.Recipes[thisRace][recipeSlotName].FindIndex(s => s.Equals(kp.Value));
							valueSet = true;
						}
						else if (recipeSlotName == thisSlot && (Avatar.activeRace.racedata.findBackwardsCompatibleWith(kp.Value.compatibleRaces) && Avatar.activeRace.racedata.wardrobeSlots.Contains(thisSlot)))
						{
							//for backwards compatible Races- races can be backwards compatible with other races (set in the Race itself) and this enables one race to wear anothers wardrobe (if that race has the same wardrobe slots)
							child.GetComponentInChildren<Slider>().value = characterSystem.Recipes[thisRace][recipeSlotName].FindIndex(s => s.Equals(kp.Value));
							valueSet = true;
						}
					}
					if (!valueSet)
					{
						child.GetComponentInChildren<Slider>().value = -1;
					}
				}
				else
				{
					child.GetComponentInChildren<Slider>().value = -1;
				}
			}
			else
			{
				child.gameObject.SetActive(false);
			}
		}
		SetUpWardrobeSlidersSetUpEvents();
		updatingWardrobeSliders = false;
	}
	public void UpdateSuppressedWardrobeSliders(List<string> suppressedSlots)
	{
		updatingWardrobeSliders = true;
		foreach (Transform child in wardrobeSlidersPanel.transform)
		{
			var thisSlot = child.GetComponentInChildren<CSWardrobeSlotChanger>().wardrobeSlotToChange;
			if (suppressedSlots.Contains(thisSlot))
			{
				child.GetComponentInChildren<Slider>().value = -1;
			}
		}
		updatingWardrobeSliders = false;
	}
	//what if the race doesn't change but a recipe is loaded that should make the sliders change?
	//maybe we need some setupWardrobeSlidersSetUpEvents too? But the sliders will cause a charaterUpdated event that we dont want to respond to...
	//so maybe we unsetWardrobeSlidersSetUpEvents like we do with the others?
	public void SetUpWardrobeSlidersSetUpEvents()
	{
		Avatar.CharacterUpdated.AddListener(SetUpWardrobeSliders);
		Avatar.CharacterDestroyed.AddListener(UnsetWardrobeSlidersSetUpEvents);
	}

	public void UnsetWardrobeSlidersSetUpEvents(UMA.UMAData umadata)
	{
		UnsetWardrobeSlidersSetUpEvents();
	}
	public void UnsetWardrobeSlidersSetUpEvents()
	{
		Avatar.CharacterUpdated.RemoveListener(SetUpWardrobeSliders);
		Avatar.CharacterDestroyed.RemoveListener(UnsetWardrobeSlidersSetUpEvents);
	}


	public void SetUpColorSlidersSetUpEvents()
	{
		Avatar.CharacterUpdated.AddListener(SetUpColorSliders);
		Avatar.CharacterDestroyed.AddListener(UnsetColorSlidersSetUpEvents);
	}
	public void UnsetColorSlidersSetUpEvents(UMA.UMAData umadata)
	{
		UnsetColorSlidersSetUpEvents();
	}
	public void UnsetColorSlidersSetUpEvents()
	{
		Avatar.CharacterUpdated.RemoveListener(SetUpColorSliders);
		Avatar.CharacterDestroyed.RemoveListener(UnsetColorSlidersSetUpEvents);
	}
	//we need to wait for the character to be created/Updated before we can do this
	public void SetUpColorSliders(UMA.UMAData umaData)
	{
		if (!updatingColorSliders && canUpdateSlidersSetUp)
		{
			UnsetColorSlidersSetUpEvents();
			updatingColorSliders = true;
			canUpdateSlidersSetUp = false;
			var currentColorSliders = colorSlidersPanel.transform.GetComponentsInChildren<CSColorChanger>(true);
			List<string> activeColorSliders = new List<string>();
			foreach (DynamicCharacterAvatar.ColorValue colorType in Avatar.characterColors.Colors)
			{
				activeColorSliders.Add(colorType.Name);
				bool sliderExists = false;
				foreach (CSColorChanger colorSlider in currentColorSliders)
				{
					if (colorSlider.colorToChange == colorType.Name)
					{
						sliderExists = true;
						colorSlider.gameObject.transform.parent.gameObject.SetActive(true);
						SetUpColorSliderValue(colorSlider, colorType);
						break;
					}
				}
				if (!sliderExists)
				{
					GameObject thisColorSlider = Instantiate(colorSliderPrefab) as GameObject;
					thisColorSlider.transform.SetParent(colorSlidersPanel.transform, false);
					thisColorSlider.GetComponentInChildren<CSColorChanger>().customizerScript = this;
					thisColorSlider.GetComponentInChildren<CSColorChanger>().colorToChange = colorType.Name;
					thisColorSlider.name = colorType.Name + "SliderHolder";
					thisColorSlider.GetComponentInChildren<Text>().text = colorType.Name + " Color";
					thisColorSlider.GetComponentInChildren<Slider>().onValueChanged.AddListener(thisColorSlider.GetComponentInChildren<CSColorChanger>().ChangeColor);
					SetUpColorSliderValue(thisColorSlider.GetComponentInChildren<CSColorChanger>(), colorType);
				}
			}
			//Then hide any that are not actually used
			foreach (CSColorChanger colorSlider in colorSlidersPanel.transform.GetComponentsInChildren<CSColorChanger>())
			{
				bool keepSliderActive = false;
				foreach (UMA.OverlayColorData ucd in umaData.umaRecipe.sharedColors)
				{
					if (colorSlider.colorToChange == ucd.name)
					{
						keepSliderActive = true;
						break;
					}
				}
				if (!keepSliderActive)
				{
					colorSlider.gameObject.transform.parent.gameObject.SetActive(false);
				}
			}
		}
		//SetUpColorSlidersSetUpEvents ();//these only want to be here when we change race otherwise we get infinite loops!
		updatingColorSliders = false;
	}
	public void SetUpColorSliderValue(CSColorChanger colorSlider, DynamicCharacterAvatar.ColorValue colorType)
	{
		if (sharedColorTables.FindIndex(s => s.name == colorType.Name) > -1)
		{
			var thisColorTable = sharedColorTables[sharedColorTables.FindIndex(s => s.name == colorType.Name)].sharedColorTable;
			colorSlider.gameObject.GetComponent<Slider>().maxValue = thisColorTable.colors.Length - 1;
			for (int i = 0; i < thisColorTable.colors.Length; i++)
			{
				//Unfortunately float values dont compare well and colors dont always match even if they were set the same in the editor
				if (ColorToHex(thisColorTable.colors[i].color) == ColorToHex(colorType.Color))
				{//WORKS!
					colorSlider.gameObject.GetComponent<Slider>().value = i;
					break;
				}
			}
		}
		else
		{
			colorSlider.gameObject.GetComponent<Slider>().maxValue = GenericColorList.colors.Length - 1;
			for (int i = 0; i < GenericColorList.colors.Length; i++)
			{
				if (ColorToHex(GenericColorList.colors[i].color) == ColorToHex(colorType.Color))
				{
					colorSlider.gameObject.GetComponent<Slider>().value = i;
					break;
				}
			}
		}
	}

	public string ColorToHex(Color32 color)
	{
		string hex = color.r.ToString("X2") + color.g.ToString("X2") + color.b.ToString("X2") + color.a.ToString("X2");
		return hex;
	}

	public void SetColor(string colorName, float fColor)
	{
		if (!updatingColorSliders)
		{
			UnsetColorSlidersSetUpEvents();
			UnsetWardrobeSlidersSetUpEvents();
			//Avatar triggers characterUpdated after this happens but we dont need to update the color sliders unless the slots change
			int Col = (int)fColor;
			if (sharedColorTables.FindIndex(s => s.name == colorName) > -1)
			{
				Avatar.SetColor(colorName, sharedColorTables[sharedColorTables.FindIndex(s => s.name == colorName)].sharedColorTable.colors[Col]);
			}
			else
			{
				Avatar.SetColor(colorName, GenericColorList.colors[Col]);
			}
			SetUpColorSlidersSetUpEvents();
			SetUpWardrobeSlidersSetUpEvents();
		}
	}

	/// <summary>
	/// Sets any wardrobe slot. If a negative value is passed, then the slot is cleared.
	/// </summary>
	/// <param name="slotToChange">name of the wordrobe slot to change</param>
	/// <param name="fSlotNumber">Id number slot to change</param>
	public void SetSlot(string slotToChange, float fSlotNumber)
	{
		if (updatingWardrobeSliders)
			return;
		if (updatingColorSliders)
			return;
		UnsetColorSlidersSetUpEvents();
		UnsetWardrobeSlidersSetUpEvents();
		var thisRace = Avatar.activeRace.name;
		int slotNumber = (int)fSlotNumber;
		string prioritySlot = "";
		List<string> prioritySlotOver = new List<string>();
		if (slotNumber >= 0)
		{
			UMATextRecipe tr = characterSystem.Recipes[thisRace][slotToChange][slotNumber];
			prioritySlot = tr.wardrobeSlot;
			prioritySlotOver = tr.suppressWardrobeSlots;
			Avatar.SetSlot(tr);
		}
		else
		{
			Avatar.ClearSlot(slotToChange);
		}
		if (prioritySlotOver.Count > 0)
		{
			//if we have a slider for anything in prioritySlotOver we can remove it (since the user has a control to put it back again) 
			//otherwise it needs to stay as it is deeemed a 'Default' bit of clothing that should be there unless otherwise suppressed -for example I use 'female undies' this way
			foreach (Transform child in wardrobeSlidersPanel.transform)
			{
				if (child.gameObject.activeSelf)
				{
					var thisSlot = child.GetComponentInChildren<CSWardrobeSlotChanger>().wardrobeSlotToChange;
					if (prioritySlotOver.Contains(thisSlot))
					{
						Avatar.ClearSlot(thisSlot);
					}
				}
			}
			UpdateSuppressedWardrobeSliders(prioritySlotOver);
		}
		SetUpColorSlidersSetUpEvents();
		Avatar.BuildCharacter(true, prioritySlot, prioritySlotOver);
		SetUpWardrobeSlidersSetUpEvents();
		canUpdateSlidersSetUp = true;
	}

	public void CloseAllPanels()
	{
		faceEditor.transform.parent.gameObject.SetActive(false);
		bodyEditor.transform.parent.gameObject.SetActive(false);
		colorSlidersPanel.SetActive(false);
		wardrobeSlidersPanel.SetActive(false);
	}

	public void ShowHideWardrobeSliders()
	{
		if (wardrobeSlidersPanel.activeSelf)
		{
			wardrobeSlidersPanel.SetActive(false);
		}
		else
		{
			if (Orbitor != null)
			{
				TargetBody();
			}
			wardrobeSlidersPanel.SetActive(true);
			colorSlidersPanel.SetActive(false);
			faceEditor.transform.parent.gameObject.SetActive(false);
			bodyEditor.transform.parent.gameObject.SetActive(false);
		}
	}

	public void ShowHideColorSliders()
	{
		if (colorSlidersPanel.activeSelf)
		{
			colorSlidersPanel.SetActive(false);
		}
		else
		{
			if (Orbitor != null)
			{
				TargetBody();
			}
			colorSlidersPanel.SetActive(true);
			wardrobeSlidersPanel.SetActive(false);
			faceEditor.transform.parent.gameObject.SetActive(false);
			bodyEditor.transform.parent.gameObject.SetActive(false);
		}
	}

	public void ShowHideFaceDNA()
	{
		if (faceEditor.transform.parent.gameObject.activeSelf)
		{
			SetUpColorSlidersSetUpEvents();
			SetUpWardrobeSlidersSetUpEvents();
			faceEditor.transform.parent.gameObject.SetActive(false);
			if (Orbitor != null)
			{
				TargetBody();
			}
		}
		else
		{
			UnsetColorSlidersSetUpEvents();
			UnsetWardrobeSlidersSetUpEvents();
			faceEditor.Initialize(Avatar);
			if (Orbitor != null)
			{
				TargetFace();
			}
			faceEditor.transform.parent.gameObject.SetActive(true);
			bodyEditor.transform.parent.gameObject.SetActive(false);
			colorSlidersPanel.SetActive(false);
			wardrobeSlidersPanel.SetActive(false);
		}
	}

	public void ShowHideBodyDNA()
	{
		if (bodyEditor.transform.parent.gameObject.activeSelf)
		{
			SetUpColorSlidersSetUpEvents();
			SetUpWardrobeSlidersSetUpEvents();
			bodyEditor.transform.parent.gameObject.SetActive(false);
		}
		else
		{
			UnsetColorSlidersSetUpEvents();
			UnsetWardrobeSlidersSetUpEvents();
			bodyEditor.Initialize(Avatar);
			if (Orbitor != null)
			{
				TargetBody();
			}
			bodyEditor.transform.parent.gameObject.SetActive(true);
			faceEditor.transform.parent.gameObject.SetActive(false);
			colorSlidersPanel.SetActive(false);
			wardrobeSlidersPanel.SetActive(false);
		}
	}

	/// <summary>
	/// Point the mouse orbitor at the body center
	/// </summary>
	public void TargetBody()
	{
		Orbitor.distance = 1.4f;
		Orbitor.TargetBone = "Root/Global/Position/Hips/LowerBack/Spine/Spine1";
	}

	/// <summary>
	/// Point the mouse orbitor at the neck, so you can see the face.
	/// </summary>
	public void TargetFace()
	{
		Orbitor.distance = 0.5f;
		Orbitor.TargetBone = "Root/Global/Position/Hips/LowerBack/Spine/Spine1/Neck/Head";
	}

}
