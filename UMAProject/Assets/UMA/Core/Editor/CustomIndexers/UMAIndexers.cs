using UnityEngine;
using UMA.CharacterSystem;
using UnityEditor.Search;

namespace UMA
{
    public static class UMAIndexers
    {
        [CustomObjectIndexer(typeof(UMAWardrobeRecipe))]
        internal static void UMAWardrobeIndexer(CustomObjectIndexerTarget context, ObjectIndexer indexer)
        {

            var recipe = context.target as UMAWardrobeRecipe;
            if (recipe == null)
            {
                Debug.Log("Not indexing file: " + context.target.name);
                return;
            }

            foreach (var raceName in recipe.compatibleRaces)
            {
                Debug.Log("Indexing file: " + context.target.name);
                Debug.Log("Race is " + raceName);
                indexer.AddProperty("race", raceName, context.documentIndex);
            }
            /*
            var wardrobeRecipes = UMAAssetIndexer.GetAssets<UMAWardrobeRecipe>();
            foreach (var recipe in wardrobeRecipes)
            {
                context.AddObject(recipe.name, recipe, "Wardrobe Recipe");
            }*/
        }
    }
}
