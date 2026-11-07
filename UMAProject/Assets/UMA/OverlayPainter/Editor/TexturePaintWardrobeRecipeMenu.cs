using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public static class TexturePaintWardrobeRecipeMenu
    {
        private const string AssetMenu = "Assets/UMA/Open Wardrobe Recipe in Overlay Painter";
        private const string ContextMenu = "CONTEXT/UMAWardrobeRecipe/Open in Overlay Painter";

        [MenuItem(AssetMenu, false, 2100)]
        private static void OpenSelected() => Open(Selection.activeObject as UMAWardrobeRecipe);

        [MenuItem(AssetMenu, true)]
        private static bool ValidateSelection() => Selection.objects.Length == 1 &&
                                                  Selection.activeObject is UMAWardrobeRecipe;

        [MenuItem(ContextMenu)]
        private static void OpenContext(MenuCommand command) => Open(command.context as UMAWardrobeRecipe);

        public static void Open(UMAWardrobeRecipe recipe)
        {
            if (recipe == null) return;
            if (!TryCreateContext(recipe, out var context, out string error))
            {
                EditorUtility.DisplayDialog("Open Wardrobe Recipe in Overlay Painter", error, "OK");
                return;
            }
            TexturePaintStageWindow.ShowStage(context);
        }

        internal static bool TryCreateContext(UMAWardrobeRecipe recipe,
            out TexturePaintLaunchContext context, out string error, RaceData requestedRace = null)
        {
            context = null;
            error = null;
            try
            {
                List<SlotData> slots = MeshReconstructor.LoadWardrobeSlots(recipe, out _, out var previewRace, requestedRace);
                var result = new TexturePaintLaunchContext
                {
                    kind = TexturePaintLaunchKind.WardrobeRecipe,
                    sourceMode = TexturePaintStandaloneSourceMode.OverlayDataAsset,
                    wardrobeRecipe = recipe, wardrobeRecipeGuid = GuidFor(recipe),
                    wardrobePreviewRace = previewRace,
                    selectedSlot = slots[0].asset, selectedSlotGuid = GuidFor(slots[0].asset)
                };
                foreach (SlotData slot in slots)
                {
                    var sources = new List<string> { recipe.recipeString, Dependency(slot.asset), Dependency(slot.material) };
                    if (previewRace != null)
                    {
                        sources.Add(Dependency(previewRace));
                        sources.Add(Dependency(previewRace.baseRaceRecipe));
                    }
                    for (int i = 0; i < slot.OverlayCount; i++) sources.Add(Dependency(slot.GetOverlay(i)?.asset));
                    result.members.Add(new TexturePaintStandaloneMemberContext
                    {
                        slot = slot.asset, slotGuid = GuidFor(slot.asset),
                        tileNumber = slot.asset.IsUdimMember ? slot.asset.udimTileNumber : 0,
                        sourceFingerprint = Hash128.Compute(string.Join("|", sources)).ToString()
                    });
                }
                context = result;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static string GuidFor(UnityEngine.Object asset) => asset == null ? string.Empty :
            AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));

        private static string Dependency(UnityEngine.Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? string.Empty :
                AssetDatabase.AssetPathToGUID(path) + ":" + AssetDatabase.GetAssetDependencyHash(path);
        }
    }
}
