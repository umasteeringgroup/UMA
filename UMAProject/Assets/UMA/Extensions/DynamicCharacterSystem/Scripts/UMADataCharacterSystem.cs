using UnityEngine;
using System.Collections;
using System.Collections.Generic;

//This adds two wardrobeRecipes field to UMARecipe for use with dynamic character System so we can save like a standard txt asset but with this extra field
namespace UMA
{
    public class UMADataCharacterSystem : UMAData
    {
        [System.Serializable]
        public class UMACharacterSystemRecipe : UMARecipe
        {
            public Dictionary<string, string> wardrobeRecipes = new Dictionary<string, string>();
        }

    }
}
