using System.Collections;
using System.Collections.Generic;
using UMA;
using UnityEngine;

public interface IUMAAddressablePlugin 
{
    /// <summary>
    /// Menu is the Menu name for the generator. It will appear under the "Addressable Generators" menu item in the Asset Index window
    /// </summary>
    string Menu { get; }

    /// <summary>
    /// This begins the process. 
    /// </summary>
    /// <returns>True - continue processing. False - abort.</returns>
    bool Prepare(); 
     
    /// <summary>
    /// This is called once for every recipe. Every recipe is processed before
    /// any items are processed. If you process recipes instead of processing items, 
    /// then you are responsible for adding the items to groups, labelling them, etc.
    /// For an example, The SingleGroupGenerator class processes recipes and manually adds items to groups.
    /// </summary>
    /// <param name="recipe"></param>
    void ProcessRecipe(UMAPackedRecipeBase recipe);

    /// <summary>
    /// This is called once for every item in the index. It is called AFTER every recipe has been processed.
    /// If you process the items, and return a list of labels for that item, then the generator will label the item
    /// for you and add it to the shared group. Any overlay will also add it's textures to the shared group.
    /// </summary>
    /// <param name="ai"></param>
    /// <returns></returns>
    List<string> ProcessItem(AssetItem ai);

    /// <summary>
    /// Finalize is called after every recipe and every item has been processed.
    /// </summary>
    /// <returns></returns>
    void Complete();
}
