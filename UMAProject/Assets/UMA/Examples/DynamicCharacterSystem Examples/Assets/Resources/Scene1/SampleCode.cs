// #define PRELOAD_ALL_RACES

using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UMA.Examples;
using System.Runtime;
#if UMA_ADDRESSABLES
using UnityEngine.AddressableAssets;
#endif

namespace UMA.CharacterSystem.Examples
{
    public class SampleCode : MonoBehaviour
    {
        public DynamicCharacterAvatar Avatar;
        public GameObject SlotPrefab;
        public GameObject WardrobePrefab;
        public GameObject SlotPanel;
        public GameObject WardrobePanel;
        public GameObject ColorPrefab;
        public GameObject DnaPrefab;
        public GameObject LabelPrefab;
        public GameObject GeneralHelpText;
        public GameObject WardrobeHelpText;
        public GameObject ColorsHelpText;
        public GameObject DnaHelpText;
        public GameObject AvatarPrefab;
		public GameObject NoBuildPrefab;
		public UMAMouseOrbitImproved Orbiter;
        public SharedColorTable HairColor;
        public SharedColorTable SkinColor;
        public SharedColorTable EyesColor;
        public SharedColorTable ClothingColor;
		public Dropdown RaceDropdown;
		public GameObject CharacterUI;
        public bool PreloadAndUnload;
        public Slider TestSlider;
        public UMAWardrobeCollection CollectionToAdd;
        public bool UseHighresModels;


		private List<RaceData> races;

		public void Start()
		{
			UMAAssetIndexer index = UMAAssetIndexer.Instance;
            races = index.GetAllAssets<RaceData>();
#if UMA_ADDRESSABLES
            // Preload all the races.
            List<string> labels = new List<string>();
            labels.Add("UMA_Recipes");

            // Because of a bug in the current addressables implementation (crashes if you pass a
            // label that doesn't exist), we'll need to check first to see if they are there.
            // If so, they'll need to be loaded first, so they can be sorted into the availablerecipes. 
            // Your app will know this in advance, and know whether to just call 
            // LoadLabelList. 
            // But since we don't know that for this sample, we'll check for resource locators, which
            // doesn't crash.

            var op = Addressables.LoadResourceLocationsAsync("UMA_Recipes");

            if (op.Result != null && op.Result.Count > 1)
            {
                var recipeOp = UMAAssetIndexer.Instance.LoadLabelList(labels, true); // Load the recipes!
                recipeOp.Completed += Recipes_Loaded;
            }

            if (RaceDropdown != null)
            {

                RaceDropdown.options.Clear();
                foreach (RaceData race in races)
                {
                    RaceDropdown.options.Add(new Dropdown.OptionData(race.raceName));
                }
            }
            else
            {
                if (PreloadAndUnload)
                {
                    var asyncop = UMAAssetIndexer.Instance.Preload(races, true); // Base races will always be loaded.
                    asyncop.Completed += Asyncop_Completed;
                }
            }
#else
            Avatar.gameObject.SetActive(true);
            if (RaceDropdown != null)
            {
                int i = 0;
                int found = 0;
                RaceDropdown.options.Clear();
                for (int i1 = 0; i1 < races.Count; i1++)
                {
                    RaceData race = races[i1];
                    if (race.raceName == Avatar.activeRace.name)
                    {
                        found = i;
                    }

                    RaceDropdown.options.Add(new Dropdown.OptionData(race.raceName));
                    i++;
                }
                RaceDropdown.value = found;
            }
#endif
        }



#if UMA_ADDRESSABLES
        private void Recipes_Loaded(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<IList<Object>> obj)
		{
            Debug.Log("Recipes loaded: " + obj.Status.ToString());
            Avatar.gameObject.SetActive(true);
		}

        private void Asyncop_Completed(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<IList<Object>> obj)
        {
            //Debug.Log("Race Preload Completed.");
            // Preload any default wardrobe items on our avatar, now that the races are preloaded.
            UMAAssetIndexer.Instance.Preload(Avatar, false).Completed += Avatar_Completed;
        }
        private void Avatar_Completed(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<IList<Object>> obj)
		{
            // Ready to go, enable the character and build it.
            //Debug.Log("Avatar preload completed.");
            Avatar.gameObject.SetActive(true);
            // Avatar.BuildCharacter();
            Avatar.BuildCharacterEnabled = true;
            //  Avatar.ChangeRace(Avatar.RacePreset);
        }

        public void DestroyAvatar()
        {
            Destroy(Avatar.gameObject);
        }

        public void PreloadDNA()
        {
            Avatar.predefinedDNA = new UMAPredefinedDNA();
            Avatar.predefinedDNA.AddDNA("feetSize", 1.0f);
            Avatar.BuildCharacter(false);
        }
#endif

        public void SliderChange(float value)
        {
            int index = System.Convert.ToInt32(value);
            List<UMATextRecipe> theRecipes = Avatar.AvailableRecipes["Legs"];
            if (theRecipes.Count == 0)
            {
                return;
            }

            if (theRecipes.Count >= TestSlider.maxValue)
            {
                TestSlider.maxValue = theRecipes.Count - 1;
            }

            if (index > (theRecipes.Count - 1))
            {
                index = theRecipes.Count - 1;
            }

            Avatar.SetSlot(theRecipes[index]);
            Avatar.BuildCharacter();
        }


        /// <summary>
        /// Unloads all loaded items.
        /// </summary>
        /// <param name="force"></param>
        public void UnloadAllItems(bool force)
		{
#if UMA_ADDRESSABLES
			UMAAssetIndexer.Instance.UnloadAll(force);
#endif
            Resources.UnloadUnusedAssets();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            System.GC.Collect();
        }

		/// <summary>
		/// Remove any controls from the panels
		/// </summary>
		private void Cleanup()
        {
			if (GeneralHelpText != null)
            {
                GeneralHelpText.SetActive(false);
            }

            if (DnaHelpText != null)
            {
                DnaHelpText.SetActive(false);
            }

            if (WardrobeHelpText != null)
            {
                WardrobeHelpText.SetActive(false);
            }

            if (ColorsHelpText != null)
            {
                ColorsHelpText.SetActive(false);
            }

            foreach (Transform t in SlotPanel.transform)
            {
                UMAUtils.DestroySceneObject(t.gameObject);
            }
            foreach (Transform t in WardrobePanel.transform)
            {
                UMAUtils.DestroySceneObject(t.gameObject);
            }
        }

        public void HelpClick()
        {
            if (GeneralHelpText.activeSelf)
            {
                GeneralHelpText.SetActive(false);
            }
            else
            {
                Cleanup();
                GeneralHelpText.SetActive(true);
            }
        }

        public void WardrobeHelpClick()
        {
            if (WardrobeHelpText.activeSelf)
            {
                WardrobeHelpText.SetActive(false);
            }
            else
            {
                Cleanup();
                WardrobeHelpText.SetActive(true);
            }
        }

        public void ColorsHelpClick()
        {
            if (ColorsHelpText.activeSelf)
            {
                ColorsHelpText.SetActive(false);
            }
            else
            {
                Cleanup();
                ColorsHelpText.SetActive(true);
            }
        }

        public void DNAHelpClick()
        {
            if (DnaHelpText.activeSelf)
            {
                DnaHelpText.SetActive(false);
            }
            else
            {
                Cleanup();
                DnaHelpText.SetActive(true);
            }
        }
        /// <summary>
        /// DNA Button event Handler
        /// </summary>
        public void DnaClick()
        {
            Cleanup();
            Dictionary<string,DnaSetter> AllDNA = Avatar.GetDNA();
            foreach( KeyValuePair<string, DnaSetter> ds in AllDNA)
            {
                // create a button. 
                // set set the dna setter on it.
                GameObject go = GameObject.Instantiate(DnaPrefab);
                DNAHandler ch = go.GetComponent<DNAHandler>();
                ch.Setup(Avatar, ds.Value, WardrobePanel);

                Text txt = go.GetComponentInChildren<Text>();
                txt.text = ds.Value.Name;
                go.transform.SetParent(SlotPanel.transform);
            }
        }

        /// <summary>
        /// Colors Button event handler
        /// </summary>
        public void ColorsClick()
        {
            Cleanup();

            for (int i = 0; i < Avatar.CurrentSharedColors.Length; i++)
            {
                OverlayColorData ocd = Avatar.CurrentSharedColors[i];
                GameObject go = GameObject.Instantiate(ColorPrefab);
                AvailableColorsHandler ch = go.GetComponent<AvailableColorsHandler>();

                SharedColorTable currColors = ClothingColor;

                if (ocd.name.ToLower() == "skin")
                {
                    currColors = SkinColor;
                }
                else if (ocd.name.ToLower() == "hair")
                {
                    currColors = HairColor;
                }
                else if (ocd.name.ToLower() == "eyes")
                {
                    currColors = EyesColor;
                }

                ch.Setup(Avatar, ocd.name, WardrobePanel,currColors);

                Text txt = go.GetComponentInChildren<Text>();
                txt.text = ocd.name;
                go.transform.SetParent(SlotPanel.transform);
            }
        }

        /// <summary>
        /// Wardrobe Button event handler
        /// </summary>
        public void WardrobeClick()
        {
            Cleanup();

            Dictionary<string, List<UMATextRecipe>> recipes = Avatar.AvailableRecipes;

            foreach (string s in recipes.Keys)
            {
                GameObject go = GameObject.Instantiate(SlotPrefab);
                SlotHandler sh = go.GetComponent<SlotHandler>();
                sh.Setup(Avatar, s,WardrobePanel);
                Text txt = go.GetComponentInChildren<Text>();
                txt.text = s;
                go.transform.SetParent(SlotPanel.transform);
            }
        }

        public void DumpData()
        {
#if UMA_ADDRESSABLES
           foreach (var r in Addressables.ResourceLocators)
            {
                Debug.Log("Resource locator r = " + r.LocatorId);
                foreach(var k in r.Keys)
                {
                    Debug.Log(k.ToString());
                }
            }
#endif
        }

        public SharedColorTable SkinColors;
        public SharedColorTable HairColors;

        public void CreateFromPrefab()
        {
            float x = Random.Range(-8.0f, 8.0f);
            float z = Random.Range(1.0f, 12.0f);
            GameObject go = GameObject.Instantiate(AvatarPrefab);
            DynamicCharacterAvatar dca = go.GetComponent<DynamicCharacterAvatar>();
            dca.SetSlot(CollectionToAdd);
            go.transform.localPosition = new Vector3(x, 0, z);
            go.SetActive(true);
        }



        public void DynamicCreateClick()
        {
            string[] files = { "Fram", "Bob", "Gobs" };
            float x = Random.Range(-8.0f, 8.0f);
            float z = Random.Range(1.0f, 12.0f);
            GameObject go = GameObject.Instantiate(AvatarPrefab);
            DynamicCharacterAvatar dca = go.GetComponent<DynamicCharacterAvatar>();
#if false
            // this shows how to load it from a string at initialization
            TextAsset t = Resources.Load<TextAsset>("CharacterRecipes/Bob");
            dca.Preload(t.text);
#else
            // this shows how to load it from a resource file at initialization
            dca.loadPathType = DynamicCharacterAvatar.loadPathTypes.CharacterSystem;
            dca.loadFilename = files[Random.Range(0, 3)];
#endif
            go.transform.localPosition = new Vector3(x, 0, z);
            go.SetActive(true);
        }

        public void SetRawColorTest()
        {
            OverlayColorData ColorData = Avatar.GetColor("HairColor");
            ColorData.channelMask[0] = new Color(1f, 1f, 1f, 1f);
            ColorData.channelAdditiveMask[0] = new Color(1f, 0f, 0f, 0f);
            Avatar.SetRawColor("HairColor", ColorData, true);
        }

        public void ChangeRace(int index)
		{
			if (Avatar.gameObject.activeSelf)
			{
#if UMA_ADDRESSABLES
                if (PreloadAndUnload)
                {
                    UMAAssetIndexer.Instance.UnloadAll(true);
                }
#endif
                string race = RaceDropdown.options[index].text;
                Avatar.ChangeRace(race);
            }
            else
            {
                string race = RaceDropdown.options[index].text;
                Avatar.RacePreset = race;
            }
#if UMA_ADDRESSABLES
            if (PreloadAndUnload)
            {
                var asyncop = UMAAssetIndexer.Instance.Preload(Avatar, false);
                asyncop.Completed += Avatar_Completed;
            }
            else
            {
                Avatar.gameObject.SetActive(true);
                Avatar.BuildCharacterEnabled = true;
            }
#else
            Avatar.gameObject.SetActive(true);
            Avatar.BuildCharacterEnabled = true;
#endif
        }

        public void ChangeSex()
        {
            if (Avatar.activeRace.name == "HumanMale") 
            {
                // if you do not pass true, then it might not change if you had never had a race set on this before. 
                Avatar.ChangeRace("HumanFemale", true);
            }
            else
            {
                Avatar.ChangeRace("HumanMale", true);
            }
        }

        public void CenterCam()
        {
            Orbiter.Reset();
        }

        public void ToggleUpdateBounds()
        {
            SkinnedMeshRenderer[] sm = FindObjectsOfType<SkinnedMeshRenderer>();
            for (int i = 0; i < sm.Length; i++)
            {
                SkinnedMeshRenderer smr = sm[i];
                smr.updateWhenOffscreen = !smr.updateWhenOffscreen;
            }
        }

        public void RandomClick()
        {
            RandomizeAvatar(Avatar);
        }

        private void RandomizeAvatar(DynamicCharacterAvatar Avatar)
        {
            Dictionary<string, List<UMATextRecipe>> recipes = Avatar.AvailableRecipes;

            // Set random wardrobe slots.
            foreach (string SlotName in recipes.Keys)
            {
                int cnt = recipes[SlotName].Count;
                if (cnt > 0)
                {
                    //Get a random recipe from the slot, and apply it
                    int min = -1;
                    if (SlotName == "Legs")
                    {
                        min = 0; // Don't allow pants removal in random test
                    }

                    int rnd = Random.Range(min, cnt);
                    if (rnd == -1)
                    {
                        Avatar.ClearSlot(SlotName);
                    }
                    else
                    {
                        Avatar.SetSlot(recipes[SlotName][rnd]);
                    }
                }
            }

            // Set Random DNA 
            Dictionary<string, DnaSetter> setters = Avatar.GetDNA();
            foreach (KeyValuePair<string, DnaSetter> dna in setters)
            {
                dna.Value.Set(0.35f + (Random.value * 0.3f));
            }

            // Set Random Colors for Skin and Hair
            int RandHair = Random.Range(0, HairColors.colors.Length);
            int RandSkin = Random.Range(0, SkinColors.colors.Length);

            Avatar.SetColor("Hair", HairColors.colors[RandHair]);
            Avatar.SetColor("Skin", SkinColors.colors[RandSkin]);
            Avatar.BuildCharacter(true);
            Avatar.ForceUpdate(true, true, true);
        }

        public void LinkToAssets()
        {
            Application.OpenURL("https://www.assetstore.unity3d.com/en/#!/search/page=1/sortby=popularity/query=publisher:5619");
        }

        public void ToggleAnimation()
        {
        // RuntimeAnimatorController rac = Avatar.gameObject.GetComponentInChildren<>
        }
    }
}
