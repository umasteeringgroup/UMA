using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using UMA;
using UMAAssetBundleManager;

namespace UMACharacterSystem
{
	public class WardrobeCollectionDemoUI : MonoBehaviour
	{

		public TestCustomizerDD thisCustomizer;

		public GameObject collectionButtonPrefab;

		public int coverImageIndex = 0;

		public UnityEvent onLoadCollection;

		public void OnEnable()
		{
			GenerateCollectionButtons();
		}

		public void GenerateCollectionButtons()
		{
			if (WardrobeCollectionLibrary.Instance == null)
				return;
			
			//clear any existing buttons
			foreach(Transform child in transform)
			{
				Destroy(child.gameObject);
			}
			var currentAvatarRace = "";
			if(thisCustomizer.Avatar != null)
				currentAvatarRace= thisCustomizer.Avatar.activeRace.name;
            foreach (UMAWardrobeCollection uwc in WardrobeCollectionLibrary.Instance.collectionList)
			{
				//dont create a button if the collection is not compatible with the currentAvatar Race
				if (uwc.compatibleRaces.Contains(currentAvatarRace) || currentAvatarRace == "")
				{
					var thisBtn = GameObject.Instantiate(collectionButtonPrefab);
					var thisBtnCtrl = thisBtn.GetComponent<WardrobeCollectionDemoBtn>();
					thisBtnCtrl.Setup(uwc.name, uwc.GetCoverImage(coverImageIndex), uwc.name, this);
					thisBtn.transform.SetParent(gameObject.transform, false);
				}
			}
		}
		//TODO TODO TODO when the collection is for races that we dont have yet the race gets downloaded- I dont want that- I just want the recipes added to DCS for that race but the race should not itself be in racelibrary
		public void LoadSelectedCollection(string collectionName)
		{
			var thisUWC = WardrobeCollectionLibrary.Instance.collectionIndex[collectionName];

			if(thisUWC != null)
			{
				thisUWC.EnsureLocalAvailability();
			}
			if(thisCustomizer.Avatar != null)
			{
				//is this UWC compatible with the current race of the avatar?
				//even if its not it should be made available to races that are?
				if (!thisUWC.compatibleRaces.Contains(thisCustomizer.Avatar.activeRace.name))
				{
					//show a messagebox- but for now
					Debug.LogWarning("This wardrobe collection was not compatible with that avatar");
					return;
				}
				//if not show a message otherwise load the recipe
				var thisContext = thisCustomizer.Avatar.context != null ? thisCustomizer.Avatar.context : UMAContext.FindInstance();
				if(thisContext != null)
				{
					var thisDCA = (thisContext.dynamicCharacterSystem as DynamicCharacterSystem);
					if(thisDCA != null)
					{
						thisDCA.GetRecipe(collectionName);
						thisCustomizer.Avatar.SetSlot(thisUWC);
					}
				}
				onLoadCollection.Invoke();
            }
		}
	}
}
