using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA;
using UMA.CharacterSystem;

namespace UMA.Examples
{
    public class NetworkDcaGUI : MonoBehaviour
    {
        public UMAWardrobeCollectionSelector hairDropDownSelector;
        public UMAWardrobeCollectionSelector helmetDropDownSelector;
        public UMAWardrobeCollectionSelector chestDropDownSelector;
        public UMAWardrobeCollectionSelector handsDropDownSelector;
        public UMAWardrobeCollectionSelector legsDropDownSelector;
        public UMAWardrobeCollectionSelector feetDropDownSelector;
        public UMAWardrobeCollectionSelector underwearDropDownSelector;
        public ChangeRaceButton raceButton;
        public DynamicColorList skinColorList;
        public DynamicColorList hairColorList;
        public RandomDNA randomDNA;

        private DynamicCharacterAvatar _avatar;
        private NetworkDCA _networkDCA;

        void Start()
        {
            _avatar = GetComponent<DynamicCharacterAvatar>();
            _networkDCA = GetComponent<NetworkDCA>();

            if(_networkDCA.isLocal)
                _avatar.CharacterCreated.AddListener(OnCharacterCreated);
        }

        //====================================================================
        //  UMAData Callbacks
        //====================================================================
        private void OnCharacterCreated(UMAData umaData)
        {
            if (_avatar == null)
            {
                Debug.LogError("Avatar is null!");
                return;
            }

            //TODO remove these
            raceButton = GameObject.Find("RaceButton").GetComponent<ChangeRaceButton>();
            raceButton.avatar = _avatar;

            hairDropDownSelector = GameObject.Find("HairDropdown").GetComponent<UMAWardrobeCollectionSelector>();
            hairDropDownSelector.avatar = _avatar;

            helmetDropDownSelector = GameObject.Find("HelmetDropdown").GetComponent<UMAWardrobeCollectionSelector>();
            helmetDropDownSelector.avatar = _avatar;

            chestDropDownSelector = GameObject.Find("ChestDropdown").GetComponent<UMAWardrobeCollectionSelector>();
            chestDropDownSelector.avatar = _avatar;

            handsDropDownSelector = GameObject.Find("HandsDropdown").GetComponent<UMAWardrobeCollectionSelector>();
            handsDropDownSelector.avatar = _avatar;

            legsDropDownSelector = GameObject.Find("LegsDropdown").GetComponent<UMAWardrobeCollectionSelector>();
            legsDropDownSelector.avatar = _avatar;

            feetDropDownSelector = GameObject.Find("FeetDropdown").GetComponent<UMAWardrobeCollectionSelector>();
            feetDropDownSelector.avatar = _avatar;

            underwearDropDownSelector = GameObject.Find("UnderwearDropdown").GetComponent<UMAWardrobeCollectionSelector>();
            underwearDropDownSelector.avatar = _avatar;

            skinColorList = GameObject.Find("SkinColorList").GetComponent<DynamicColorList>();
            skinColorList.avatar = _avatar;
            skinColorList.Initialize("Skin");

            hairColorList = GameObject.Find("HairColorList").GetComponent<DynamicColorList>();
            hairColorList.avatar = _avatar;
            hairColorList.Initialize("Hair");

            randomDNA = GameObject.Find("RandomDnaButton").GetComponent<RandomDNA>();
            randomDNA.avatar = _avatar;
        }
    }
}
