using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace UMA.CharacterSystem.Examples
{
    public class WardrobeCollectionDemoUI : MonoBehaviour
	{

		public TestCustomizerDD thisCustomizer;
		public GameObject collectionButtonPrefab;
		public int coverImageIndex = 0;

		//You would probably have some messageBox system in your actual app but for demo purposes I'll just specify some GameObjects
		public GameObject dialogBoxes;
		public GameObject messageBox;
		public Text messageHeader;
		public Text messageBody;

		public UnityEvent onLoadCollection;

		public void OnEnable()
		{
			GenerateCollectionButtons();
		}

		public void GenerateCollectionButtons()
		{
			if (WardrobeCollectionLibrary.Instance == null)
            {
                return;
            }

            //clear any existing buttons
            foreach (Transform child in transform)
			{
				Destroy(child.gameObject);
			}
			var currentAvatarRace = "";
			if (thisCustomizer.Avatar != null)
            {
                currentAvatarRace = thisCustomizer.Avatar.activeRace.name;
            }

            for (int i = 0; i < WardrobeCollectionLibrary.Instance.collectionList.Count; i++)
			{
                UMAWardrobeCollection uwc = WardrobeCollectionLibrary.Instance.collectionList[i];
                //dont create a button if the collection is not compatible with the currentAvatar Race
                if (uwc.compatibleRaces.Contains(currentAvatarRace) || currentAvatarRace == "" || uwc.compatibleRaces.Count == 0)
				{
					var thisBtn = GameObject.Instantiate(collectionButtonPrefab);
					var thisBtnCtrl = thisBtn.GetComponent<WardrobeCollectionDemoBtn>();
					thisBtnCtrl.Setup(uwc.name, uwc.GetCoverImage(coverImageIndex), uwc.name, this);
					thisBtn.transform.SetParent(gameObject.transform, false);
				}
			}
		}

		public void LoadSelectedCollection(string collectionName)
		{
			var thisUWC = WardrobeCollectionLibrary.Instance.collectionIndex[collectionName];

			if (thisUWC != null)
			{
				thisUWC.EnsureLocalAvailability();
			}
			if (thisCustomizer.Avatar != null)
			{
				//is this UWC compatible with the current race of the avatar?
				//even if its not it should be made available to races that are?
				if (!thisUWC.compatibleRaces.Contains(thisCustomizer.Avatar.activeRace.name) && thisUWC.compatibleRaces.Count > 0)
				{
					//show a messagebox- but for now
					if (Debug.isDebugBuild)
                    {
                        Debug.LogWarning("This wardrobe collection was not compatible with that avatar");
                    }

                    return;
				}
				//if not show a message otherwise load the recipe
				var thisContext = thisCustomizer.Avatar.context != null ? thisCustomizer.Avatar.context : UMAContextBase.FindInstance();
				if (thisContext != null)
				{
						// make sure it's downloaded... probably don't need this now.
						UMAContext.Instance.GetRecipe(collectionName, true);
						//if there is actually a 'FullOutfit' defined for the current avatar(i.e. the WardrobeSet for this race is not empty) load it
						if (thisUWC.wardrobeCollection[thisCustomizer.Avatar.activeRace.name].Count > 0)
						{
							thisCustomizer.Avatar.SetSlot(thisUWC);
							thisCustomizer.Avatar.BuildCharacter(true);
						}
				}
				onLoadCollection.Invoke();
				//if this was not a recipe that will actually load a FullOutfit onto this race, show a message saying the assets have been added to the library
				if (thisUWC.wardrobeCollection[thisCustomizer.Avatar.activeRace.name].Count == 0 && thisUWC.arbitraryRecipes.Count > 0)
				{
					dialogBoxes.SetActive(true);
					messageBox.SetActive(true);
					messageHeader.text = thisUWC.name + " Loaded!";
					messageBody.text = "The wardrobe recipes in " + thisUWC.name + " have been added to the DCS libraries. Compatible recipes can now be applied to your character using the 'Wardrobe' section of the UI.";
				}
			}
		}
	}
}
