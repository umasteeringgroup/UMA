using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA.TexturePaint
{
    public static partial class MeshReconstructor
    {
        public static List<SlotData> LoadWardrobeSlots(UMAWardrobeRecipe recipe,
            out OverlayColorData[] sharedColors)
        {
            return LoadWardrobeSlots(recipe, out sharedColors, out _);
        }

        public static List<SlotData> LoadWardrobeSlots(UMAWardrobeRecipe recipe,
            out OverlayColorData[] sharedColors, out RaceData previewRace, RaceData requestedRace = null)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            // Unpack into session-owned data; never paint into the recipe's cached SlotData.
            var unpacked = new UMAData.UMARecipe();
            recipe.Load(unpacked);
            sharedColors = unpacked.sharedColors;
            previewRace = null;
            var slots = new List<SlotData>();
            var wildcards = new List<SlotData>();
            foreach (var slot in unpacked.slotDataList ?? Array.Empty<SlotData>())
            {
                if (slot == null || slot.isBlendShapeSource || slot.Suppressed || slot.isDisabled)
                    continue;
                if (IsWardrobeWildcard(slot))
                {
                    wildcards.Add(slot);
                    continue;
                }
                if (slot.asset == null)
                    throw new InvalidOperationException($"Recipe '{recipe.name}' contains a missing slot asset.");
                // Utility slots have no paintable geometry.
                if (UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData)) continue;
                // Recipe slots can share an overlay list. Wildcard matching is per slot.
                slot.SetOverlayList(new List<OverlayData>(slot.GetOverlayList()));
                slots.Add(slot);
            }
            if (wildcards.Count > 0)
            {
                var candidates = new List<RaceData>();
                if (requestedRace != null) candidates.Add(requestedRace);
                else
                {
                    var indexedRaces = UMAAssetIndexer.Instance.GetAllAssets<RaceData>();
                    if (recipe.compatibleRaces != null && recipe.compatibleRaces.Count > 0)
                    {
                        foreach (string raceName in recipe.compatibleRaces)
                            foreach (var race in indexedRaces)
                                if (race != null && race.raceName == raceName && !candidates.Contains(race))
                                    candidates.Add(race);
                    }
                    else
                    {
                        indexedRaces.RemoveAll(race => race == null);
                        indexedRaces.Sort((a, b) => StringComparer.Ordinal.Compare(a.raceName, b.raceName));
                        candidates.AddRange(indexedRaces);
                    }
                    // Self-contained recipes need no race if their own meshes match all wildcards.
                    candidates.Insert(0, null);
                }

                List<SlotData> resolved = null;
                foreach (var race in candidates)
                {
                    if (TryResolveWardrobeWildcards(slots, wildcards, race, out resolved))
                    { previewRace = race; break; }
                }
                if (resolved == null)
                    throw new InvalidOperationException($"Recipe '{recipe.name}' contains wildcard overlays, but no " +
                        "matching preview mesh was found. Check the wildcard tags and race filters, and ensure a " +
                        "compatible race with a matching base-recipe slot is in the UMA index. " +
                        "Alternatively, open Overlay Painter on an avatar wearing this recipe.");
                slots = resolved;
            }
            if (slots.Count == 0)
                throw new InvalidOperationException($"Recipe '{recipe.name}' contains no paintable slots.");
            foreach (var slot in slots)
                if (slot.material == null || slot.material.material == null)
                    throw new InvalidOperationException($"Slot '{slot.slotName}' has no active UMAMaterial.");
            return slots;
        }

        private static bool IsWardrobeWildcard(SlotData slot) =>
            slot.isPlaceholderSlot || (slot.asset != null && slot.asset.isWildCardSlot);

        private static bool TryResolveWardrobeWildcards(List<SlotData> recipeSlots,
            List<SlotData> wildcards, RaceData race, out List<SlotData> resolved)
        {
            resolved = null;
            var targets = new List<SlotData>();
            foreach (var slot in recipeSlots)
            {
                var target = slot.Copy();
                target.altMaterial = slot.altMaterial;
                targets.Add(target);
            }
            var baseRecipe = new UMAData.UMARecipe();
            if (race != null && !race.UsesFbxRoute && race.baseRaceRecipe != null)
                race.baseRaceRecipe.Load(baseRecipe);

            bool applied = false;
            foreach (var wildcard in wildcards)
            {
                if (wildcard.Races != null && wildcard.Races.Length > 0 &&
                    (race == null || Array.IndexOf(wildcard.Races, race.raceName) < 0))
                {
                    if (race == null) return false;
                    continue;
                }
                bool matched = false;
                foreach (var candidate in baseRecipe.slotDataList ?? Array.Empty<SlotData>())
                {
                    if (!MatchesWardrobeWildcard(candidate, wildcard)) continue;
                    if (targets.Exists(slot => slot.asset == candidate.asset)) continue;
                    // Borrow only the matching geometry. The wardrobe overlays determine the
                    // source textures and material, not the race's default skin overlays.
                    var target = candidate.Copy();
                    target.altMaterial = null;
                    target.SetOverlayList(new List<OverlayData>());
                    targets.Add(target);
                }
                foreach (var target in targets)
                {
                    if (!MatchesWardrobeWildcard(target, wildcard)) continue;
                    foreach (var overlay in wildcard.GetOverlayList())
                        if (overlay != null) target.AddOverlay(overlay.Duplicate());
                    matched = true;
                }
                if (!matched) return false;
                applied = true;
            }
            if (!applied) return false;
            resolved = targets;
            return true;
        }

        private static bool MatchesWardrobeWildcard(SlotData target, SlotData wildcard) =>
            target != null && !IsWardrobeWildcard(target) && !target.isBlendShapeSource &&
            !target.Suppressed && !target.isDisabled && target.asset != null &&
            !UMAMeshData.IsNullOrEmptyMeshData(target.asset.meshData) && target.asset.meshData.subMeshCount > 0 &&
            target.tags != null && target.tags.Length > 0 && wildcard.tags != null && target.HasTag(wildcard.tags);

        internal static UMAData.MaterialFragment BuildWardrobeFragment(SlotData slot)
        {
            var overlays = new List<OverlayData>();
            for (int i = 0; i < slot.OverlayCount; i++)
            {
                var overlay = slot.GetOverlay(i);
                if (overlay?.asset != null && (i == 0 || !overlay.Supressed)) overlays.Add(overlay);
            }
            if (overlays.Count == 0 && slot.material.IsGeneratedTextures)
                throw new InvalidOperationException($"Slot '{slot.slotName}' has no source overlays to paint.");
            int additional = Math.Max(0, overlays.Count - 1);
            var fragment = new UMAData.MaterialFragment
            {
                slotData = slot, umaMaterial = slot.material, overlayList = overlays,
                overlayData = overlays.ToArray(), baseColor = overlays.Count > 0 ? overlays[0].colorData.color : Color.white,
                baseOverlay = overlays.Count > 0 ? WardrobeTextureData(overlays[0]) : new UMAData.textureData(),
                AdditionalOverlays = new UMAData.textureData[additional],
                overlayColors = new Color32[additional], rects = new Rect[additional],
                channelMask = new Color[overlays.Count][], channelAdditiveMask = new Color[overlays.Count][],
                overrides = new List<Dictionary<int, Texture>>()
            };
            for (int i = 0; i < overlays.Count; i++)
            {
                var overlay = overlays[i];
                fragment.channelMask[i] = overlay.colorData.channelMask;
                fragment.channelAdditiveMask[i] = overlay.colorData.channelAdditiveMask;
                fragment.overrides.Add(null);
                if (i == 0) continue;
                fragment.AdditionalOverlays[i - 1] = WardrobeTextureData(overlay);
                fragment.overlayColors[i - 1] = overlay.colorData.color;
                Rect rect = overlay.rect;
                if (rect.width != 0f && rect.height != 0f && rect.width <= 1f && rect.height <= 1f)
                {
                    Texture reference = fragment.baseOverlay.textureList != null && fragment.baseOverlay.textureList.Length > 0
                        ? fragment.baseOverlay.textureList[0] : null;
                    if (reference != null)
                        rect = new Rect(rect.x * reference.width, rect.y * reference.height,
                            rect.width * reference.width, rect.height * reference.height);
                }
                fragment.rects[i - 1] = rect;
            }
            return fragment;
        }

        private static UMAData.textureData WardrobeTextureData(OverlayData overlay)
        {
            return new UMAData.textureData
            {
                textureList = overlay.textureArray,
                alphaTexture = overlay.asset.alphaMask != null ? overlay.asset.alphaMask :
                    overlay.textureArray != null && overlay.textureArray.Length > 0 ? overlay.textureArray[0] : null,
                overlayType = overlay.overlayType
            };
        }

        private static void ApplyWardrobeMaterialProperties(UMAData.GeneratedMaterial generated,
            OverlayColorData[] sharedColors, Material preview)
        {
            var overlays = generated.materialFragments[0].overlayData;
            for (int i = 0; i < overlays.Length; i++)
            {
                if (!overlays[i].colorData.HasProperties) continue;
                foreach (var property in overlays[i].colorData.PropertyBlock.shaderProperties)
                {
                    if (property == null) continue;
                    property.Apply(preview, -1);
                    if (preview.HasProperty("_OverlayCount")) property.Apply(preview, i);
                }
            }
            foreach (var parameter in generated.umaMaterial.shaderParms ?? Array.Empty<UMAMaterial.ShaderParms>())
                foreach (var color in sharedColors ?? Array.Empty<OverlayColorData>())
                    if (color != null && color.name == parameter.ColorName && preview.HasProperty(parameter.ParameterName))
                    { preview.SetColor(parameter.ParameterName, color.color); break; }
        }
    }
}
