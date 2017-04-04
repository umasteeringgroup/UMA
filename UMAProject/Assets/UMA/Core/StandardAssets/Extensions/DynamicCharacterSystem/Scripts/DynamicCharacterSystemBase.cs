using UnityEngine;
using System.Collections.Generic;

namespace UMA.CharacterSystem
{
	public class DynamicCharacterSystemBase : MonoBehaviour
	{
		//just here really so that there is something that can be assigned in UMAContext. 
		//We can make some methods available here tho
		public virtual void Awake() { }

		public virtual void OnEnable() { }

		public virtual void Start() { }

		public virtual void Refresh(bool forceUpdateRaceLibrary = true, string bundleToGather = "") { }

		public virtual void Update() { }

		public virtual void Init() { }
		//Unfortunately we cant do anything useful that uses UMATextRecipe because that doesn't exist here
		//but we can define some methods that can be overidden so they can be used
		//except we BLOODY CANT because  UMATextRecipe doesn't exist at this point
		public virtual UMARecipeBase GetBaseRecipe(string filename, bool dynamicallyAdd = true) { return null; }

		//Again for RecipeEditor to be able to get usable info
		public virtual List<string> GetRecipeNamesForRaceSlot(string race, string slot) { return null; }

		//Again for RecipeEditor to be able to get usable info
		public virtual List<UMARecipeBase> GetRecipesForRaceSlot(string race, string slot) { return null; }

		//Again for RecipeEditor to get info
		public virtual bool CheckRecipeAvailability(string recipeName) { return true; }
    }
}
