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
    /// any items are processed.
    /// </summary>
    /// <param name="recipe"></param>
    void ProcessRecipe(UMAPackedRecipeBase recipe);

    /// <summary>
    /// This is called once for every item in the index. It is called AFTER every recipe has been processed.
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
