// TattooManager is a module that allows adding multiple overlays to a certain geometry slot and editing their transforms in slot's UV space.
// 1. Create a WardrobeSlot to hold tattoo recipes. Call it, for example: "Tattoo"
// 2. Create several WardrobeRecipes using this WardrobeSlot, each containing a unique tattoo overlay assigned to a same specific bodySlot.
// 3. Mark WardrobeRecipes as "appendable", each recipes should have only one overlay for the same one bodySlot.
// 4. Overlays MUST be named accordingly to the recipe. If the recipe is called "Tattoo1", the overlay must be named "Tattoo1_ovl".
// 5. Put TattooManager on the UMA avatar GameObject.
//
// *** How the system works ***
// For each unique tattoo recipe on the avatar TattooManager will create a corresponding SharedColor named exactly as overlay. 
// i.e. for the overlay "Tattoo1_ovl" will be created Shared Color named "Tattoo1_ovl". This Shared Color will be used not only to tint the overlay, 
// but also to control its Position, Rotation, and Scale with transform block encoded to ColorData. Transform applications happens in UMA RecipeUpdated event,
// when all the slots and overlays are added to the character but character is not yet built.
//
// To add new tattoo layer add a recipe use AddTattooLayer() method
// To remove a layer use RemoveTattooLayer() method


using System.Collections.Generic;
using UnityEngine;
using UMA.CharacterSystem;
using System;

namespace UMA.Examples
{

    public class UMA_TattooManager : MonoBehaviour
    {
        [Tooltip("Wardrobe slot to hold tattoos")]
        public String WardrobeSlot = "Tattoo";

        private DynamicCharacterAvatar _avatar;
        [Tooltip("Current tattoo recipes")]
        public List<UMATextRecipe> wearableItems;
        private string bodySlot;

        public void OnEnable()
        {
            _avatar = GetComponent<DynamicCharacterAvatar>();

            
            if (_avatar != null)
            {
                // Subscribe to RecipeUpdated event
                _avatar.RecipeUpdated.AddListener(DoTattooStuff);
                string race = _avatar.activeRace.name;

                // Determine body slot based on race. It's the actual geometry slot that the overlay will be applied to. 
                // It should match to the slot used in the WardrobeRecipe/Overlay we are using.
                switch (race)
                {
                    case "G8F":
                        bodySlot = "F_Skin";
                        break;
                    case "G8M":
                        bodySlot = "Skin";
                        break;
                }
            }

        }

        public void OnDisable()
        {
            // Unsubscribe from RecipeUpdated event
            if (_avatar != null)
            {
                _avatar.RecipeUpdated.RemoveListener(DoTattooStuff);
            }

        }


        public void DoTattooStuff(UMAData umaData)
        {
            Debug.Log("DoTattooStuff called");
            wearableItems = _avatar.GetAppendedWearableItems(WardrobeSlot);
            SlotData slot = umaData.umaRecipe.FindSlot(bodySlot);
            List<OverlayData> overlays = slot.GetOverlayList();

            foreach (var item in wearableItems)
            {
                foreach (var overlay in overlays)
                {
                    if (overlay.overlayName.Contains(item.name + "_ovl"))
                    {
                        OverlayColorData ColorData = _avatar.GetColor(item.name + "_ovl");
                        if (ColorData)
                        {
                            overlay.instanceTransformed = true;
                            overlay.colorData.color = ColorData.color;
                            UMAOverlayTransformProperty transform = (UMAOverlayTransformProperty)ColorData.PropertyBlock.GetProperty<UMAOverlayTransformProperty>("UVsrt");
                            overlay.Translate = transform.Translate/2;
                            overlay.Scale = transform.Scale;
                            overlay.Rotation = transform.Rotate;
                            Debug.Log($"Updated overlay {overlay.overlayName} with position {transform.Translate},rotation {transform.Rotate}, scale {transform.Scale}, color {ColorData.color}");
                        }

                    }
                }

            }


        }

        public void DeleteTattooLayer(int index)
        {
            if (wearableItems != null && index >= 0 && index < wearableItems.Count)
            {
                var item = wearableItems[index];
                //Remove corresponding shared color
                _avatar.ClearColor(item.name + "_ovl");

                //Remove tattoo recipe
                _avatar.RemoveWearableItem(item);
                _avatar.BuildCharacter(false, false, true);

            }
        }

        public void AddTattooLayer(string recipeName, Vector2 positionXY, float rotation, Vector2 scaleXY, Color color)
        {
            if (string.IsNullOrEmpty(recipeName)) return;

            UMATextRecipe recipe = UMAAssetIndexer.Instance.GetAsset<UMAWardrobeRecipe>(recipeName);

            if (recipe.wardrobeSlot != WardrobeSlot) return; //If not the tattoo slot

            if (!wearableItems.Contains(recipe))
            {
                //Add recipe + parameters
                OverlayColorData ocd = new OverlayColorData(1);
                ocd.PropertyBlock = new UMAMaterialPropertyBlock();
                var transform = new UMAOverlayTransformProperty(positionXY, rotation, scaleXY) { name = "UVsrt" };
                ocd.PropertyBlock.AddProperty(transform);
                ocd.color = color;
                Debug.Log($"Adding shared color: {recipeName}_ovl with position {positionXY}, rotation {rotation}, scale {scaleXY}, color {color}");

                _avatar.SetSlot(WardrobeSlot, recipeName);
                _avatar.SetRawColor(recipeName + "_ovl", ocd, false);
                _avatar.BuildCharacter(false, false, true);
            }
            else
            {
                // If recipe is already added, no need to add recipe, just update parameters
                OverlayColorData ocd = new OverlayColorData(1);
                ocd.PropertyBlock = new UMAMaterialPropertyBlock();
                var transform = new UMAOverlayTransformProperty(positionXY, rotation, scaleXY) { name = "UVsrt" };
                ocd.PropertyBlock.AddProperty(transform);
                ocd.color = color;
                Debug.Log($"Updated shared color: {recipeName}_ovl with position {positionXY}, rotation {rotation}, scale {scaleXY}, color {color}");

                _avatar.SetRawColor(recipeName + "_ovl", ocd, false);
                _avatar.BuildCharacter(false, false, true);

            }

        }


        public void ReplaceTattooLayer(int index, string recipeName)
        {
            // Validate inputs
            if (wearableItems == null || index < 0 || index >= wearableItems.Count)
            {
                Debug.LogWarning("Invalid index for ReplaceTattooLayer");
                return;
            }

            if (string.IsNullOrEmpty(recipeName))
            {
                Debug.LogWarning("Recipe name cannot be null or empty");
                return;
            }

            // Get the new recipe
            UMATextRecipe newRecipe = UMAAssetIndexer.Instance.GetAsset<UMAWardrobeRecipe>(recipeName);
            if (newRecipe == null)
            {
                Debug.LogWarning($"Recipe '{recipeName}' not found");
                return;
            }

            // Verify it's a tattoo slot recipe
            if (newRecipe.wardrobeSlot != WardrobeSlot)
            {
                Debug.LogWarning($"Recipe '{recipeName}' is not for the {WardrobeSlot} slot");
                return;
            }

            // Get the old recipe to replace
            var oldRecipe = wearableItems[index];
            string oldColorKey = oldRecipe.name + "_ovl";
            string newColorKey = recipeName + "_ovl";

            // Copy values from the old color data
            OverlayColorData oldColorData = _avatar.GetColor(oldColorKey);
            Vector2 position = Vector2.zero;
            float rotation = 0f;
            Vector2 scale = Vector2.one;
            Color color = Color.white;

            if (oldColorData != null)
            {
                // Copy color
                color = oldColorData.color;

                // Copy transform properties if they exist
                if (oldColorData.PropertyBlock != null)
                {
                    UMAOverlayTransformProperty oldTransform = (UMAOverlayTransformProperty)oldColorData.PropertyBlock.GetProperty<UMAOverlayTransformProperty>("UVsrt");
                    if (oldTransform != null)
                    {
                        position = oldTransform.Translate;
                        rotation = oldTransform.Rotate;
                        scale = oldTransform.Scale;
                    }
                }
            }

            // Remove the old color data
            _avatar.ClearColor(oldColorKey);

            // Replace the recipe in the wearableItems list
            wearableItems[index] = newRecipe;

            // Create new overlay color data with the copied values
            OverlayColorData newColorData = new OverlayColorData(1);
            newColorData.PropertyBlock = new UMAMaterialPropertyBlock();
            var newTransform = new UMAOverlayTransformProperty(position, rotation, scale) { name = "UVsrt" };
            newColorData.PropertyBlock.AddProperty(newTransform);
            newColorData.color = color;

            // Set the new color data
            _avatar.SetRawColor(newColorKey, newColorData, false);

            // Rebuild the character to apply all changes
            _avatar.BuildCharacter(false, false, true);
        }


    }
}