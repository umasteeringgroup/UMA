using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UMA;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

public class SingleGroupGenerator : IUMAAddressablePlugin
{
    public UMAAssetIndexer Index;
    List<UMAPackedRecipeBase> Recipes;
    const string SharedGroupName = "UMA_SharedItems";

    public string Menu
    {
        get
        {
            return "Generate Single group (fast)";
        }
    }

    public void Finalize()
    {
        try
        {
            Dictionary<AssetItem, List<string>> theItems = new Dictionary<AssetItem, List<string>>();

            float pos = 0.0f;
            float inc = 1.0f / Recipes.Count;
            foreach (UMAPackedRecipeBase uwr in Recipes)
            {
                EditorUtility.DisplayProgressBar("Generating", "processing recipe: " + uwr.name, pos);
                List<AssetItem> items = Index.GetAssetItems(uwr, true);
                foreach (AssetItem ai in items)
                {
                    if (theItems.ContainsKey(ai) == false)
                    {
                        theItems.Add(ai, new List<string>());
                    }
                    theItems[ai].Add(uwr.AssignedLabel);
                }
                pos += inc;
            }

            // Create the shared group that has each item packed separately.
            AddressableAssetGroup sharedGroup = Index.AddressableSettings.FindGroup(SharedGroupName);
            if (sharedGroup == null)
            {
                sharedGroup = Index.AddressableSettings.CreateGroup(SharedGroupName, false, false, true, Index.AddressableSettings.DefaultGroup.Schemas);
                sharedGroup.GetSchema<BundledAssetGroupSchema>().BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            }

            pos = 0.0f;
            inc = 1.0f / theItems.Count;

            StringBuilder sb = new StringBuilder();
            foreach (AssetItem ai in theItems.Keys)
            {
                ai.IsAddressable = true;
                ai.AddressableAddress = ""; // let the system assign it if we are generating.
                ai.AddressableGroup = sharedGroup.name;
                EditorUtility.DisplayProgressBar("Generating", "Processing Asset: " + ai.Item.name, pos);

                sb.Clear();
                foreach (string s in theItems[ai])
                {
                    sb.Append(s);
                    sb.Append(';');
                }
                ai.AddressableLabels = sb.ToString();

                bool found = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ai.Item.GetInstanceID(), out string itemGUID, out long localID);

                Index.AddItemToSharedGroup(itemGUID, ai.AddressableAddress, theItems[ai], sharedGroup);
                if (ai._Type == typeof(OverlayDataAsset))
                {
                    OverlayDataAsset od = ai.Item as OverlayDataAsset;
                    if (od == null)
                    {
                        Debug.Log("Invalid overlay in recipe: " + ai._Name + ". Skipping.");
                        continue;
                    }
                    foreach (Texture tex in od.textureList)
                    {
                        if (tex == null) continue;
                        if (tex as Texture2D == null)
                        {
                            Debug.Log("Texture is not Texture2D!!! " + tex.name);
                            continue;
                        }
                        string Address = "Texture2D-" + tex.name + "-" + tex.GetInstanceID();

                        found = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tex.GetInstanceID(), out string texGUID, out long texlocalID);
                        if (found)
                        {
                            Index.AddItemToSharedGroup(texGUID, AssetItem.AddressableFolder + Address, theItems[ai], sharedGroup);
                        }
                    }
                }
                pos += inc;
            }

            Index.AssignAddressableInformation();

            Type[] types = Index.GetTypes();

            foreach (Type t in types)
            {
                Index.ReleaseReferences(t);
            }

            Index.CleanupAddressables(true);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    public bool Prepare()
    {
        Index = UMAAssetIndexer.Instance;

        foreach (Type t in Index.GetTypes())
        {
            Index.ClearAddressableFlags(t);
        }

        Recipes = new List<UMAPackedRecipeBase>();
        return true; 
    }

    public List<string> ProcessItem(AssetItem ai)
    {
        // This generator does not process single items.
        return null;
    }

    /// <summary>
    /// Process the recipes. This generator simply accumulates the recipes for processing
    /// once all recipes are in the list.
    /// </summary>
    /// <param name="recipe"></param>
    public void ProcessRecipe(UMAPackedRecipeBase recipe)
    {
        Recipes.Add(recipe);
    }
}
