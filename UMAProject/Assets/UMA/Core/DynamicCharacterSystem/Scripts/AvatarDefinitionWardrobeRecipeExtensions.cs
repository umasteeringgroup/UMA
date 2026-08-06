using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.CharacterSystem
{
    /// <summary>
    /// The wardrobe-recipe data which cannot be represented by an
    /// <see cref="AvatarDefinition"/>.
    /// </summary>
    [Serializable]
    public sealed class WardrobeRecipeAdditionalData
    {
        public string RecipeName;
        public UMAPackedRecipeBase.UMAPackRecipe PackedRecipe;

        public string DisplayValue;
        public List<string> CompatibleRaces = new List<string>();
        public List<WardrobeRecipeThumb> WardrobeRecipeThumbs = new List<WardrobeRecipeThumb>();
        public bool ThumbnailFromTexture;
        public Rect ThumbnailRect = new Rect(0.25f, 0.25f, 0.5f, 0.5f);
        public string WardrobeSlot;
        public bool Appended;
        public List<string> Hides = new List<string>();
        public List<string> HideTags = new List<string>();
        public List<string> SuppressWardrobeSlots = new List<string>();
        public List<WardrobeSettings> ActiveWardrobeSet = new List<WardrobeSettings>();
        public List<MeshHideAsset> MeshHideAssets = new List<MeshHideAsset>();
        public List<MeshHideAssetCollection> MeshHideAssetCollections = new List<MeshHideAssetCollection>();
        public List<MeshModifier> MeshModifiers = new List<MeshModifier>();
        public bool Disabled;

        public List<UMAWardrobeRecipe> IncompatibleRecipes = new List<UMAWardrobeRecipe>();
        public string UserField;
        public string Replaces;

        public bool ForceKeep;
        public bool LabelLocalFiles;
        public bool NoAutoAdd;
    }

    /// <summary>
    /// The result of converting a wardrobe recipe to an avatar definition.
    /// <see cref="AdditionalData"/> retains the recipe data which has no
    /// equivalent field in <see cref="AvatarDefinition"/>.
    /// </summary>
    [Serializable]
    public sealed class WardrobeRecipeAvatarDefinition
    {
        public AvatarDefinition AvatarDefinition;
        public WardrobeRecipeAdditionalData AdditionalData;
    }

    /// <summary>
    /// Runtime conversion helpers for creating an in-memory wardrobe recipe
    /// from an <see cref="AvatarDefinition"/> and converting it back again.
    /// </summary>
    public static class AvatarDefinitionWardrobeRecipeExtensions
    {
        /// <summary>
        /// Creates a runtime wardrobe recipe from an existing wardrobe recipe.
        /// The source recipe supplies all slots, overlays, and wardrobe-only
        /// metadata. The avatar definition supplies the race, override DNA,
        /// and first color.
        /// </summary>
        /// <remarks>
        /// The returned recipe is a separate in-memory ScriptableObject. The
        /// source recipe is not modified, and neither recipe needs to be
        /// available through the UMA Asset Indexer.
        /// </remarks>
        public static UMAWardrobeRecipe ToWardrobeRecipe(
            this AvatarDefinition avatarDefinition,
            UMAWardrobeRecipe sourceRecipe)
        {
            if (sourceRecipe == null)
            {
                throw new ArgumentNullException(nameof(sourceRecipe));
            }

            RequireFirstColor(avatarDefinition);

            WardrobeRecipeAdditionalData additionalData =
                CaptureAdditionalData(
                    sourceRecipe,
                    sourceRecipe.PackedLoad());
            return avatarDefinition.ToWardrobeRecipe(additionalData);
        }

        /// <summary>
        /// Creates a wardrobe recipe at runtime from the recipes named in
        /// <see cref="AvatarDefinition.Wardrobe"/>.
        /// The first color in <paramref name="avatarDefinition"/> is assigned
        /// to every imported overlay. DNA is stored as wardrobe override DNA.
        /// </summary>
        /// <remarks>
        /// The referenced recipes must be available through the UMA Asset
        /// Indexer. The returned recipe is an in-memory ScriptableObject; this
        /// method does not add it to the indexer or save it as an asset.
        /// </remarks>
        public static UMAWardrobeRecipe ToWardrobeRecipe(
            this AvatarDefinition avatarDefinition,
            string recipeName,
            string displayValue,
            string wardrobeSlot,
            IEnumerable<string> compatibleRaces)
        {
            ValidateRecipeParameters(
                avatarDefinition,
                recipeName,
                wardrobeSlot,
                compatibleRaces);

            var additionalData = new WardrobeRecipeAdditionalData
            {
                RecipeName = recipeName,
                DisplayValue = displayValue,
                WardrobeSlot = wardrobeSlot,
                CompatibleRaces = new List<string>(compatibleRaces),
                PackedRecipe = CreateEmptyPackedRecipe()
            };
            AppendAvatarWardrobeContent(
                avatarDefinition,
                additionalData.PackedRecipe);

            return avatarDefinition.ToWardrobeRecipe(additionalData);
        }

        /// <summary>
        /// Creates a wardrobe recipe at runtime from the recipes named in
        /// <see cref="AvatarDefinition.Wardrobe"/>, then adds the explicitly
        /// supplied slot and overlay.
        /// The first color in <paramref name="avatarDefinition"/> is assigned
        /// to every overlay. DNA is stored as wardrobe override DNA.
        /// </summary>
        /// <remarks>
        /// The returned recipe is an in-memory ScriptableObject. This method
        /// does not add it to the UMA Asset Indexer or save it as an asset.
        /// </remarks>
        public static UMAWardrobeRecipe ToWardrobeRecipe(
            this AvatarDefinition avatarDefinition,
            SlotDataAsset slotAsset,
            OverlayDataAsset overlayAsset,
            string recipeName,
            string displayValue,
            string wardrobeSlot,
            IEnumerable<string> compatibleRaces)
        {
            if (slotAsset == null)
            {
                throw new ArgumentNullException(nameof(slotAsset));
            }

            if (overlayAsset == null)
            {
                throw new ArgumentNullException(nameof(overlayAsset));
            }

            ValidateRecipeParameters(
                avatarDefinition,
                recipeName,
                wardrobeSlot,
                compatibleRaces);

            var slot = new SlotData(slotAsset);
            slot.AddOverlay(new OverlayData(overlayAsset));

            var umaRecipe = new UMAData.UMARecipe
            {
                slotDataList = new[] { slot },
                sharedColors = Array.Empty<OverlayColorData>()
            };
            umaRecipe.ClearDna();

            var additionalData = new WardrobeRecipeAdditionalData
            {
                RecipeName = recipeName,
                DisplayValue = displayValue,
                WardrobeSlot = wardrobeSlot,
                CompatibleRaces = new List<string>(compatibleRaces),
                PackedRecipe = UMAPackedRecipeBase.PackRecipeV3(umaRecipe)
            };
            AppendAvatarWardrobeContent(
                avatarDefinition,
                additionalData.PackedRecipe);

            return avatarDefinition.ToWardrobeRecipe(additionalData);
        }

        /// <summary>
        /// Creates a wardrobe recipe from an avatar definition and a complete
        /// wardrobe-recipe data snapshot. This overload is suitable for
        /// recreating the result of <see cref="ToAvatarDefinition"/>.
        /// </summary>
        public static UMAWardrobeRecipe ToWardrobeRecipe(
            this AvatarDefinition avatarDefinition,
            WardrobeRecipeAdditionalData additionalData)
        {
            if (additionalData == null)
            {
                throw new ArgumentNullException(nameof(additionalData));
            }

            if (string.IsNullOrWhiteSpace(additionalData.RecipeName))
            {
                throw new ArgumentException(
                    "Additional data must contain a recipe name.",
                    nameof(additionalData));
            }

            RequireFirstColor(avatarDefinition);

            UMAPackedRecipeBase.UMAPackRecipe packedRecipe =
                ClonePackedRecipe(additionalData.PackedRecipe) ??
                new UMAPackedRecipeBase.UMAPackRecipe
                {
                    version = 3,
                    slotsV3 = Array.Empty<UMAPackedRecipeBase.PackedSlotDataV3>()
                };

            OverlayColorData firstColor =
                ToOverlayColorData(avatarDefinition.Colors[0]);
            packedRecipe.version = 3;
            packedRecipe.race = avatarDefinition.RaceName;
            packedRecipe.isWardrobe = true;
            packedRecipe.sharedColorCount = 1;
            packedRecipe.fColors = new[]
            {
                new UMAPackedRecipeBase.PackedOverlayColorDataV3(firstColor)
            };
            packedRecipe.packedDna =
                new List<UMAPackedRecipeBase.UMAPackedDna>();
            PointAllOverlaysAtFirstColor(packedRecipe);

            UMAWardrobeRecipe wardrobeRecipe =
                ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            ApplyAdditionalData(wardrobeRecipe, additionalData);
            wardrobeRecipe.OverrideDNA = ToPredefinedDna(avatarDefinition.Dna);
            wardrobeRecipe.PackedSave(packedRecipe);
            return wardrobeRecipe;
        }

        /// <summary>
        /// Converts a wardrobe recipe to an avatar definition and returns all
        /// recipe-only data beside it so no wardrobe metadata or packed
        /// slot/overlay data is discarded.
        /// </summary>
        public static WardrobeRecipeAvatarDefinition ToAvatarDefinition(
            this UMAWardrobeRecipe wardrobeRecipe)
        {
            if (wardrobeRecipe == null)
            {
                throw new ArgumentNullException(nameof(wardrobeRecipe));
            }

            UMAPackedRecipeBase.UMAPackRecipe packedRecipe =
                wardrobeRecipe.PackedLoad() ??
                new UMAPackedRecipeBase.UMAPackRecipe();

            string raceName = packedRecipe.race;
            if (string.IsNullOrEmpty(raceName) &&
                wardrobeRecipe.compatibleRaces != null &&
                wardrobeRecipe.compatibleRaces.Count > 0)
            {
                raceName = wardrobeRecipe.compatibleRaces[0];
            }

            var avatarDefinition = new AvatarDefinition
            {
                RaceName = raceName,
                Wardrobe = string.IsNullOrEmpty(wardrobeRecipe.name)
                    ? Array.Empty<string>()
                    : new[] { wardrobeRecipe.name },
                Colors = GetFirstColor(packedRecipe),
                Dna = ToDnaDefinitions(wardrobeRecipe.OverrideDNA)
            };

            return new WardrobeRecipeAvatarDefinition
            {
                AvatarDefinition = avatarDefinition,
                AdditionalData = CaptureAdditionalData(
                    wardrobeRecipe,
                    packedRecipe)
            };
        }

        private static void RequireFirstColor(AvatarDefinition avatarDefinition)
        {
            if (avatarDefinition.Colors == null ||
                avatarDefinition.Colors.Length == 0)
            {
                throw new ArgumentException(
                    "The AvatarDefinition must contain at least one color.",
                    nameof(avatarDefinition));
            }
        }

        private static void ValidateRecipeParameters(
            AvatarDefinition avatarDefinition,
            string recipeName,
            string wardrobeSlot,
            IEnumerable<string> compatibleRaces)
        {
            if (string.IsNullOrWhiteSpace(recipeName))
            {
                throw new ArgumentException(
                    "A recipe name is required.",
                    nameof(recipeName));
            }

            if (string.IsNullOrWhiteSpace(wardrobeSlot))
            {
                throw new ArgumentException(
                    "A wardrobe slot is required.",
                    nameof(wardrobeSlot));
            }

            if (compatibleRaces == null)
            {
                throw new ArgumentNullException(nameof(compatibleRaces));
            }

            RequireFirstColor(avatarDefinition);
        }

        private static UMAPackedRecipeBase.UMAPackRecipe
            CreateEmptyPackedRecipe()
        {
            return new UMAPackedRecipeBase.UMAPackRecipe
            {
                version = 3,
                slotsV3 =
                    Array.Empty<UMAPackedRecipeBase.PackedSlotDataV3>()
            };
        }

        private static void AppendAvatarWardrobeContent(
            AvatarDefinition avatarDefinition,
            UMAPackedRecipeBase.UMAPackRecipe target)
        {
            if (avatarDefinition.Wardrobe == null ||
                avatarDefinition.Wardrobe.Length == 0)
            {
                return;
            }

            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null)
            {
                throw new InvalidOperationException(
                    "The UMA Asset Indexer is required to resolve the " +
                    "wardrobe recipes in the AvatarDefinition.");
            }

            for (int i = 0; i < avatarDefinition.Wardrobe.Length; i++)
            {
                string recipeName = avatarDefinition.Wardrobe[i];
                if (string.IsNullOrWhiteSpace(recipeName))
                {
                    continue;
                }

                UMATextRecipe sourceRecipe =
                    indexer.GetRecipe(recipeName, false);
                if (sourceRecipe == null)
                {
                    throw new UMAResourceNotFoundException(
                        $"Wardrobe recipe '{recipeName}' from the " +
                        "AvatarDefinition was not found in the UMA Asset " +
                        "Indexer.");
                }

                if (!(sourceRecipe is UMAWardrobeRecipe) &&
                    !string.Equals(
                        sourceRecipe.recipeType,
                        "Wardrobe",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Recipe '{recipeName}' is not a wardrobe recipe.");
                }

                AppendPackedSlots(target, GetVersion3Recipe(sourceRecipe));
            }
        }

        private static UMAPackedRecipeBase.UMAPackRecipe GetVersion3Recipe(
            UMATextRecipe sourceRecipe)
        {
            UMAPackedRecipeBase.UMAPackRecipe packed =
                sourceRecipe.PackedLoad();
            if (packed != null && packed.slotsV3 != null)
            {
                return packed;
            }

            UMAData.UMARecipe unpacked = sourceRecipe.GetCachedRecipe();
            return unpacked != null
                ? UMAPackedRecipeBase.PackRecipeV3(unpacked)
                : CreateEmptyPackedRecipe();
        }

        private static void AppendPackedSlots(
            UMAPackedRecipeBase.UMAPackRecipe target,
            UMAPackedRecipeBase.UMAPackRecipe source)
        {
            if (source == null ||
                source.slotsV3 == null ||
                source.slotsV3.Length == 0)
            {
                return;
            }

            UMAPackedRecipeBase.UMAPackRecipe sourceCopy =
                ClonePackedRecipe(source);
            int targetSlotCount = target.slotsV3 != null
                ? target.slotsV3.Length
                : 0;
            var mergedSlots =
                new UMAPackedRecipeBase.PackedSlotDataV3[
                    targetSlotCount + sourceCopy.slotsV3.Length];

            if (targetSlotCount > 0)
            {
                Array.Copy(
                    target.slotsV3,
                    mergedSlots,
                    targetSlotCount);
            }

            for (int i = 0; i < sourceCopy.slotsV3.Length; i++)
            {
                UMAPackedRecipeBase.PackedSlotDataV3 slot =
                    sourceCopy.slotsV3[i];
                if (slot != null && slot.copyIdx >= 0)
                {
                    slot.copyIdx += targetSlotCount;
                }

                mergedSlots[targetSlotCount + i] = slot;
            }

            target.slotsV3 = mergedSlots;
        }

        private static OverlayColorData ToOverlayColorData(
            SharedColorDef colorDefinition)
        {
            int channelCount = Math.Max(1, colorDefinition.count);
            if (colorDefinition.channels != null)
            {
                for (int i = 0; i < colorDefinition.channels.Length; i++)
                {
                    channelCount = Math.Max(
                        channelCount,
                        colorDefinition.channels[i].chan + 1);
                }
            }

            var colorData = new OverlayColorData(channelCount)
            {
                name = colorDefinition.name
            };

            if (colorDefinition.channels != null)
            {
                for (int i = 0; i < colorDefinition.channels.Length; i++)
                {
                    ColorDef channel = colorDefinition.channels[i];
                    if (channel.chan < 0 || channel.chan >= channelCount)
                    {
                        continue;
                    }

                    colorData.channelMask[channel.chan] =
                        ColorDef.ToColor(channel.mCol);
                    colorData.channelAdditiveMask[channel.chan] =
                        ColorDef.ToColor(channel.aCol);
                }
            }

            if (colorDefinition.shaderParms != null &&
                colorDefinition.shaderParms.Length > 0)
            {
                colorData.PropertyBlock = new UMAMaterialPropertyBlock();
                colorData.PropertyBlock.SetPropertyStrings(
                    colorDefinition.shaderParms);
            }

            return colorData;
        }

        private static UMAPredefinedDNA ToPredefinedDna(DnaDef[] definitions)
        {
            var result = new UMAPredefinedDNA();
            if (definitions == null)
            {
                return result;
            }

            for (int i = 0; i < definitions.Length; i++)
            {
                if (!string.IsNullOrEmpty(definitions[i].Name))
                {
                    result.AddDNA(
                        definitions[i].Name,
                        definitions[i].Value);
                }
            }

            return result;
        }

        private static DnaDef[] ToDnaDefinitions(UMAPredefinedDNA dna)
        {
            if (dna == null || dna.PreloadValues == null)
            {
                return Array.Empty<DnaDef>();
            }

            var result = new DnaDef[dna.PreloadValues.Count];
            for (int i = 0; i < dna.PreloadValues.Count; i++)
            {
                DnaValue value = dna.PreloadValues[i];
                result[i] = value == null
                    ? new DnaDef()
                    : new DnaDef(value.Name, value.Value);
            }

            return result;
        }

        private static SharedColorDef[] GetFirstColor(
            UMAPackedRecipeBase.UMAPackRecipe packedRecipe)
        {
            if (packedRecipe.fColors == null ||
                packedRecipe.fColors.Length == 0 ||
                packedRecipe.fColors[0] == null)
            {
                return Array.Empty<SharedColorDef>();
            }

            UMAPackedRecipeBase.PackedOverlayColorDataV3 packedColor =
                packedRecipe.fColors[0];
            int channelCount = packedColor.colors != null
                ? packedColor.colors.Length / 8
                : 0;
            var colorDefinition =
                new SharedColorDef(packedColor.name, channelCount)
                {
                    shaderParms = packedColor.ShaderParms != null
                        ? (string[])packedColor.ShaderParms.Clone()
                        : Array.Empty<string>(),
                    channels = new ColorDef[channelCount]
                };

            int valueIndex = 0;
            for (int channel = 0; channel < channelCount; channel++)
            {
                var mask = new Color32(
                    ToByte(packedColor.colors[valueIndex++]),
                    ToByte(packedColor.colors[valueIndex++]),
                    ToByte(packedColor.colors[valueIndex++]),
                    ToByte(packedColor.colors[valueIndex++]));
                var additive = new Color32(
                    ToByte(packedColor.colors[valueIndex++]),
                    ToByte(packedColor.colors[valueIndex++]),
                    ToByte(packedColor.colors[valueIndex++]),
                    ToByte(packedColor.colors[valueIndex++]));
                colorDefinition.channels[channel] = new ColorDef(
                    channel,
                    ColorDef.ToUInt(mask),
                    ColorDef.ToUInt(additive));
            }

            return new[] { colorDefinition };
        }

        private static byte ToByte(short value)
        {
            return (byte)Mathf.Clamp(value, byte.MinValue, byte.MaxValue);
        }

        private static void PointAllOverlaysAtFirstColor(
            UMAPackedRecipeBase.UMAPackRecipe packedRecipe)
        {
            if (packedRecipe.slotsV3 == null)
            {
                return;
            }

            for (int slotIndex = 0;
                slotIndex < packedRecipe.slotsV3.Length;
                slotIndex++)
            {
                UMAPackedRecipeBase.PackedSlotDataV3 slot =
                    packedRecipe.slotsV3[slotIndex];
                if (slot == null || slot.overlays == null)
                {
                    continue;
                }

                for (int overlayIndex = 0;
                    overlayIndex < slot.overlays.Length;
                    overlayIndex++)
                {
                    if (slot.overlays[overlayIndex] != null)
                    {
                        slot.overlays[overlayIndex].colorIdx = 0;
                    }
                }
            }
        }

        private static WardrobeRecipeAdditionalData CaptureAdditionalData(
            UMAWardrobeRecipe recipe,
            UMAPackedRecipeBase.UMAPackRecipe packedRecipe)
        {
            return new WardrobeRecipeAdditionalData
            {
                RecipeName = recipe.name,
                PackedRecipe = ClonePackedRecipe(packedRecipe),
                DisplayValue = recipe.DisplayValue,
                CompatibleRaces = CloneList(recipe.compatibleRaces),
                WardrobeRecipeThumbs =
                    CloneList(recipe.wardrobeRecipeThumbs),
                ThumbnailFromTexture = recipe.thumbnailFromTexture,
                ThumbnailRect = recipe.thumbnailRect,
                WardrobeSlot = recipe.wardrobeSlot,
                Appended = recipe.Appended,
                Hides = CloneList(recipe.Hides),
                HideTags = CloneList(recipe.HideTags),
                SuppressWardrobeSlots =
                    CloneList(recipe.suppressWardrobeSlots),
                ActiveWardrobeSet =
                    CloneList(recipe.activeWardrobeSet),
                MeshHideAssets = CloneList(recipe.MeshHideAssets),
                MeshHideAssetCollections =
                    CloneList(recipe.MeshHideAssetCollections),
                MeshModifiers = CloneList(recipe.MeshModifiers),
                Disabled = recipe.disabled,
                IncompatibleRecipes =
                    CloneList(recipe.IncompatibleRecipes),
                UserField = recipe.UserField,
                Replaces = recipe.replaces,
                ForceKeep = recipe.forceKeep,
                LabelLocalFiles = recipe.labelLocalFiles,
                NoAutoAdd = recipe.noAutoAdd
            };
        }

        private static void ApplyAdditionalData(
            UMAWardrobeRecipe recipe,
            WardrobeRecipeAdditionalData data)
        {
            recipe.name = data.RecipeName;
            recipe.recipeType = "Wardrobe";
            recipe.DisplayValue = data.DisplayValue;
            recipe.compatibleRaces = CloneList(data.CompatibleRaces);
            recipe.wardrobeRecipeThumbs =
                CloneList(data.WardrobeRecipeThumbs);
            recipe.thumbnailFromTexture = data.ThumbnailFromTexture;
            recipe.thumbnailRect = data.ThumbnailRect;
            recipe.wardrobeSlot = data.WardrobeSlot;
            recipe.Appended = data.Appended;
            recipe.Hides = CloneList(data.Hides);
            recipe.HideTags = CloneList(data.HideTags);
            recipe.suppressWardrobeSlots =
                CloneList(data.SuppressWardrobeSlots);
            recipe.activeWardrobeSet =
                CloneList(data.ActiveWardrobeSet);
            recipe.MeshHideAssets = CloneList(data.MeshHideAssets);
            recipe.MeshHideAssetCollections =
                CloneList(data.MeshHideAssetCollections);
            recipe.MeshModifiers = CloneList(data.MeshModifiers);
            recipe.disabled = data.Disabled;
            recipe.IncompatibleRecipes =
                CloneList(data.IncompatibleRecipes);
            recipe.UserField = data.UserField;
            recipe.replaces = data.Replaces;
            recipe.forceKeep = data.ForceKeep;
            recipe.labelLocalFiles = data.LabelLocalFiles;
            recipe.noAutoAdd = data.NoAutoAdd;
        }

        private static UMAPackedRecipeBase.UMAPackRecipe ClonePackedRecipe(
            UMAPackedRecipeBase.UMAPackRecipe packedRecipe)
        {
            return packedRecipe == null
                ? null
                : JsonUtility.FromJson<UMAPackedRecipeBase.UMAPackRecipe>(
                    JsonUtility.ToJson(packedRecipe));
        }

        private static List<T> CloneList<T>(List<T> source)
        {
            return source == null
                ? new List<T>()
                : new List<T>(source);
        }
    }
}
