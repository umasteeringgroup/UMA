using UnityEngine;

namespace UMA.CharacterSystem
{
    public partial class UMAWardrobeRecipe
    {
        /// <summary>
        /// Creates an independent in-memory copy of this wardrobe recipe.
        /// </summary>
        /// <remarks>
        /// Serialized managed data, including lists, list elements, override
        /// DNA, and packed recipe text, is deep-copied. References to Unity
        /// assets such as sprites, mesh-hide assets, mesh modifiers, and other
        /// wardrobe recipes remain shared references; cloning those assets
        /// would change the meaning of the recipe.
        ///
        /// The clone is not added to the UMA Asset Indexer and is not saved as
        /// an asset.
        /// </remarks>
        public UMAWardrobeRecipe DeepClone()
        {
            UMAWardrobeRecipe clone = Object.Instantiate(this);
            clone.name = name;

            // Never share or retain a runtime unpacked-recipe cache.
            clone.umaRecipe = null;
            clone.cached = false;
            return clone;
        }
    }
}
