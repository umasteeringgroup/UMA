#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Writes a fitted mesh to the project as UMA content: a SlotDataAsset and, when asked for, an
    /// overlay and a wardrobe recipe that wears the slot. The Scene Mesh Slot Builder publishes through
    /// it, and so can any other tool that makes clothing.
    /// </summary>
    /// <remarks>
    /// <see cref="Validate"/> checks a request without writing anything. <see cref="Publish"/> builds
    /// and checks the slot in memory before it touches the project, creates the assets, and only then
    /// adds them to the Global Library. When a step fails, the assets this call created are removed
    /// again, so a failed publish does not leave half a wardrobe item behind. An asset that
    /// <see cref="Request.OverwriteExisting"/> replaced is not brought back.
    /// </remarks>
    public static class UMASlotPublisher
    {
        /// <summary>What to publish and where.</summary>
        public sealed class Request
        {
            /// <summary>The slot's name. The assets are named after it.</summary>
            public string SlotName;
            /// <summary>The project folder the assets are written to, for example "Assets/MyClothes".</summary>
            public string FolderPath;
            public string SlotGroup;
            public string[] Tags;
            public int MaxLOD = -1;
            /// <summary>The source submesh the slot was made from, kept on the slot for reference.</summary>
            public int SourceSubmeshIndex;
            public bool ClearNormals;
            public bool ClearTangents;
            /// <summary>Add the created assets to the UMA Global Library.</summary>
            public bool AddToGlobalLibrary = true;
            /// <summary>Replace assets of the same name. Off: the new assets get a unique name.</summary>
            public bool OverwriteExisting;
            /// <summary>
            /// Update assets of the same name in place, keeping their GUIDs, so recipes, libraries and
            /// scenes that use them keep working. Takes precedence over <see cref="OverwriteExisting"/>.
            /// An updated asset is not put back if a later step fails.
            /// </summary>
            public bool UpdateInPlace;

            /// <summary>Also create a wardrobe recipe that wears the slot.</summary>
            public bool CreateWardrobeRecipe;
            /// <summary>The race the recipe is for.</summary>
            public RaceData Race;
            /// <summary>The wardrobe slot (location) of the recipe, for example "Chest".</summary>
            public string WardrobeSlot;
            /// <summary>An overlay to draw the slot with. None: one is created from <see cref="OverlayMaterial"/>.</summary>
            public OverlayDataAsset ExistingOverlay;
            /// <summary>The UMAMaterial of the overlay that is created when no existing overlay is given.</summary>
            public UMAMaterial OverlayMaterial;
            /// <summary>Optional. A material whose textures fill the created overlay's channels.</summary>
            public Material SourceMaterial;
        }

        /// <summary>What a publish made.</summary>
        public sealed class Result
        {
            public SlotDataAsset Slot;
            public string SlotPath;
            /// <summary>The overlay on the recipe: the existing one, or the one created.</summary>
            public OverlayDataAsset Overlay;
            public UMAWardrobeRecipe Recipe;
            public string RecipePath;
            /// <summary>The assets this publish created, in the order they were made.</summary>
            public readonly List<string> CreatedPaths = new List<string>();
            /// <summary>Existing assets this publish updated in place (their GUIDs kept).</summary>
            public readonly List<string> UpdatedPaths = new List<string>();
        }

        /// <summary>
        /// Says what is wrong with a request, in words for the person filling it in, or returns null
        /// when it can be published. Nothing is written.
        /// </summary>
        public static string Validate(Request request)
        {
            if (request == null)
            {
                return "Nothing to publish.";
            }
            if (string.IsNullOrWhiteSpace(request.SlotName))
            {
                return "Enter a Slot Name.";
            }
            if (string.IsNullOrEmpty(request.FolderPath) || !AssetDatabase.IsValidFolder(request.FolderPath))
            {
                return "Choose a valid project output folder.";
            }
            if (request.CreateWardrobeRecipe)
            {
                if (request.Race == null)
                {
                    return "Choose a race before creating a wardrobe recipe.";
                }
                if (string.IsNullOrWhiteSpace(request.WardrobeSlot) || string.Equals(request.WardrobeSlot, "None", StringComparison.OrdinalIgnoreCase))
                {
                    return "Choose a Wardrobe Slot (Location) other than None.";
                }
                if (request.ExistingOverlay == null && request.OverlayMaterial == null)
                {
                    return "Select an existing OverlayDataAsset or choose an UMAMaterial for the generated overlay.";
                }
            }
            return null;
        }

        /// <summary>
        /// Publishes the mesh on <paramref name="renderer"/> as a slot, with its overlay and wardrobe
        /// recipe when the request asks for them.
        /// </summary>
        /// <param name="renderer">A renderer whose mesh, bones and root bone are the slot's.</param>
        /// <param name="target">The character skin the mesh was fitted to.</param>
        /// <exception cref="InvalidOperationException">The request or the slot is not valid. Nothing this call created is left in the project.</exception>
        public static Result Publish(Request request, SkinnedMeshRenderer renderer, UMASlotSkinTransfer.TargetData target)
        {
            string problem = Validate(request);
            if (problem != null)
            {
                throw new InvalidOperationException(problem);
            }
            if (renderer == null || renderer.sharedMesh == null)
            {
                throw new InvalidOperationException("There is no mesh to publish.");
            }
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var result = new Result();
            SlotDataAsset pendingSlot = null;
            try
            {
                string safeSlotName = UMASlotSkinTransfer.MakeSafeAssetName(request.SlotName.Trim());
                string folderPath = request.FolderPath.TrimEnd('/');

                // The slot is built and checked in memory first: a slot that fails changes nothing.
                pendingSlot = BuildSlot(request, renderer, target, safeSlotName);

                string slotPath = folderPath + "/" + safeSlotName + "_slot.asset";
                SlotDataAsset existingSlot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(slotPath);
                pendingSlot.PrepareForAssetPath(slotPath, safeSlotName);
                UMASlotPreparationUtility.PrepareSlot(pendingSlot);
                if (existingSlot != null && request.UpdateInPlace)
                {
                    EditorUtility.CopySerialized(pendingSlot, existingSlot);
                    existingSlot.name = System.IO.Path.GetFileNameWithoutExtension(slotPath);
                    UnityEngine.Object.DestroyImmediate(pendingSlot);
                    pendingSlot = null;
                    result.Slot = existingSlot;
                    result.SlotPath = slotPath;
                    result.UpdatedPaths.Add(slotPath);
                }
                else
                {
                    if (existingSlot != null)
                    {
                        if (request.OverwriteExisting) AssetDatabase.DeleteAsset(slotPath);
                        else slotPath = AssetDatabase.GenerateUniqueAssetPath(slotPath);
                        pendingSlot.PrepareForAssetPath(slotPath, safeSlotName);
                    }
                    AssetDatabase.CreateAsset(pendingSlot, slotPath);
                    result.Slot = pendingSlot;
                    result.SlotPath = slotPath;
                    result.CreatedPaths.Add(slotPath);
                    pendingSlot = null;
                }
                EditorUtility.SetDirty(result.Slot);
                AssetDatabase.SaveAssetIfDirty(result.Slot);

                if (request.CreateWardrobeRecipe)
                {
                    result.Overlay = request.ExistingOverlay != null
                        ? request.ExistingOverlay
                        : CreateOverlay(request, result, folderPath);
                    CreateRecipe(request, result, folderPath);
                }
            }
            catch
            {
                if (pendingSlot != null)
                {
                    UnityEngine.Object.DestroyImmediate(pendingSlot);
                }
                Rollback(result);
                throw;
            }

            // The library comes last: everything it is told about exists and is complete.
            if (request.AddToGlobalLibrary)
            {
                AddToGlobalLibrary(result);
            }
            return result;
        }

        /// <summary>Removes the assets a publish created. Assets that existed before are left alone.</summary>
        public static void Rollback(Result result)
        {
            if (result == null)
            {
                return;
            }
            for (int i = result.CreatedPaths.Count - 1; i >= 0; i--)
            {
                string path = result.CreatedPaths[i];
                if (!string.IsNullOrEmpty(path) && AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
            result.CreatedPaths.Clear();
            result.Slot = null;
            result.Recipe = null;
        }

        private static SlotDataAsset BuildSlot(Request request, SkinnedMeshRenderer renderer, UMASlotSkinTransfer.TargetData target, string safeSlotName)
        {
            SlotDataAsset slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                slot.name = safeSlotName;
                slot.subMeshIndex = 0;
                slot.sourceSubmeshIndex = request.SourceSubmeshIndex;
                slot.slotGroup = string.IsNullOrWhiteSpace(request.SlotGroup) ? null : request.SlotGroup.Trim();
                slot.tags = request.Tags ?? new string[0];
                slot.maxLOD = request.MaxLOD;
                slot.UpdateMeshData(renderer, target.RootBoneName, false, 0, request.ClearNormals, request.ClearTangents);

                if (!UMAMeshData.IsNullOrEmptyMeshData(slot.meshData))
                {
                    slot.meshData.RootBoneName = target.RootBoneName;
                    slot.meshData.rootBoneHash = target.RootBoneHash;
                    slot.meshData.boneNameHashes = (int[])target.BoneNameHashes.Clone();
                    slot.meshData.SlotName = slot.slotName;
                }

                List<string> reasons = new List<string>();
                if (!slot.ValidateMeshData(reasons))
                {
                    throw new InvalidOperationException("Created SlotDataAsset failed validation: " + string.Join("; ", reasons));
                }
                return slot;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(slot);
                throw;
            }
        }

        private static OverlayDataAsset CreateOverlay(Request request, Result result, string folderPath)
        {
            UMAMaterial material = request.OverlayMaterial;
            if (material == null)
            {
                throw new InvalidOperationException("Choose an UMAMaterial for the generated overlay.");
            }

            string overlayName = UMASlotSkinTransfer.MakeSafeAssetName(result.Slot.slotName) + "_overlay";
            OverlayDataAsset existing = request.UpdateInPlace ? AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(folderPath + "/" + overlayName + ".asset") : null;
            string overlayPath = existing != null ? folderPath + "/" + overlayName + ".asset" : GetAvailableAssetPath(folderPath, overlayName + ".asset", request.OverwriteExisting);

            var overlayAsset = ScriptableObject.CreateInstance<OverlayDataAsset>();
            overlayAsset.name = overlayName;
            overlayAsset.material = material;
            overlayAsset.materialName = material.name;

            int channelCount = material.channels != null ? material.channels.Length : 0;
            overlayAsset.textureList = new Texture[channelCount];
            overlayAsset.textureNames = new string[channelCount];
            overlayAsset.overlayBlend = new OverlayDataAsset.OverlayBlend[channelCount];

            for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                Texture texture = GetTextureForChannel(request.SourceMaterial, material.channels[channelIndex]);
                overlayAsset.textureList[channelIndex] = texture;
                overlayAsset.textureNames[channelIndex] = texture != null ? texture.name : string.Empty;
                overlayAsset.overlayBlend[channelIndex] = OverlayDataAsset.OverlayBlend.Normal;
            }

            overlayAsset.ValidateBlendList();
            if (existing != null)
            {
                // Keep the textures someone has painted or assigned since: only fill empty channels.
                for (int i = 0; i < channelCount && existing.textureList != null && i < existing.textureList.Length; i++)
                {
                    if (existing.textureList[i] != null) overlayAsset.textureList[i] = existing.textureList[i];
                }
                EditorUtility.CopySerialized(overlayAsset, existing);
                existing.name = overlayName;
                UnityEngine.Object.DestroyImmediate(overlayAsset);
                overlayAsset = existing;
                result.UpdatedPaths.Add(overlayPath);
            }
            else
            {
                AssetDatabase.CreateAsset(overlayAsset, overlayPath);
                result.CreatedPaths.Add(overlayPath);
            }
            EditorUtility.SetDirty(overlayAsset);
            UMAUpdateProcessor.UpdateOverlay(overlayAsset);
            AssetDatabase.SaveAssetIfDirty(overlayAsset);
            return overlayAsset;
        }

        private static void CreateRecipe(Request request, Result result, string folderPath)
        {
            if (result.Overlay == null)
            {
                throw new InvalidOperationException("The wardrobe recipe needs an OverlayDataAsset.");
            }

            SlotDataAsset slotAsset = result.Slot;
            string recipeName = UMASlotSkinTransfer.MakeSafeAssetName(slotAsset.slotName) + "_Wardrobe";
            UMAWardrobeRecipe existingRecipe = request.UpdateInPlace ? AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(folderPath + "/" + recipeName + ".asset") : null;
            string recipePath = existingRecipe != null ? folderPath + "/" + recipeName + ".asset" : GetAvailableAssetPath(folderPath, recipeName + ".asset", request.OverwriteExisting);
            bool hasRaceName = request.Race != null && !string.IsNullOrWhiteSpace(request.Race.raceName);

            UMAData.UMARecipe recipe = new UMAData.UMARecipe();
            recipe.ClearDna();
            recipe.SetRace(request.Race);

            SlotData slotData = new SlotData(slotAsset);
            if (hasRaceName)
            {
                slotData.Races = new[] { request.Race.raceName };
            }
            slotData.AddOverlay(new OverlayData(result.Overlay));
            recipe.SetSlot(0, slotData);

            var wardrobeRecipe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            wardrobeRecipe.name = recipeName;
            wardrobeRecipe.recipeType = "Wardrobe";
            wardrobeRecipe.DisplayValue = slotAsset.slotName;
            wardrobeRecipe.wardrobeSlot = request.WardrobeSlot;
            wardrobeRecipe.compatibleRaces = new List<string>();
            if (hasRaceName)
            {
                wardrobeRecipe.compatibleRaces.Add(request.Race.raceName);
            }
            wardrobeRecipe.Save(recipe);

            if (existingRecipe != null)
            {
                // Keep what was added to the recipe since (mesh hides, tags, extra compatible races...): only
                // its own slot and the wardrobe settings are written.
                existingRecipe.recipeType = wardrobeRecipe.recipeType;
                existingRecipe.wardrobeSlot = wardrobeRecipe.wardrobeSlot;
                existingRecipe.DisplayValue = wardrobeRecipe.DisplayValue;
                if (existingRecipe.compatibleRaces == null) existingRecipe.compatibleRaces = new List<string>();
                foreach (string raceName in wardrobeRecipe.compatibleRaces) if (!existingRecipe.compatibleRaces.Contains(raceName)) existingRecipe.compatibleRaces.Add(raceName);
                existingRecipe.Save(recipe);
                UnityEngine.Object.DestroyImmediate(wardrobeRecipe);
                wardrobeRecipe = existingRecipe;
                result.UpdatedPaths.Add(recipePath);
            }
            else
            {
                AssetDatabase.CreateAsset(wardrobeRecipe, recipePath);
                result.CreatedPaths.Add(recipePath);
            }
            result.Recipe = wardrobeRecipe;
            result.RecipePath = recipePath;
            EditorUtility.SetDirty(wardrobeRecipe);
            AssetDatabase.SaveAssetIfDirty(wardrobeRecipe);
        }

        private static void AddToGlobalLibrary(Result result)
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null)
            {
                return;
            }
            // Slots and overlays are written to the library one item at a time, so the project's other
            // unsaved changes and the library's in-memory caches are not saved with them.
            if (result.Slot != null)
            {
                UMAGlobalLibraryAutoRegistration.AddAndSave(result.Slot);
            }
            if (result.Overlay != null)
            {
                UMAGlobalLibraryAutoRegistration.AddAndSave(result.Overlay);
            }
            if (result.Recipe != null)
            {
                indexer.EvilAddAsset(typeof(UMAWardrobeRecipe), result.Recipe);
                indexer.ForceSave();
            }
        }

        private static string GetAvailableAssetPath(string folderPath, string fileName, bool overwrite)
        {
            string assetPath = folderPath.TrimEnd('/') + "/" + fileName;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) == null)
            {
                return assetPath;
            }
            if (overwrite)
            {
                AssetDatabase.DeleteAsset(assetPath);
                return assetPath;
            }
            return AssetDatabase.GenerateUniqueAssetPath(assetPath);
        }

        private static Texture GetTextureForChannel(Material sourceMaterial, UMAMaterial.MaterialChannel channel)
        {
            if (sourceMaterial == null)
            {
                return null;
            }

            string materialPropertyName = !string.IsNullOrEmpty(channel.materialPropertyName)
                ? channel.materialPropertyName
                : channel.sourceTextureName;
            if (string.IsNullOrEmpty(materialPropertyName) || !sourceMaterial.HasProperty(materialPropertyName))
            {
                return null;
            }

            return sourceMaterial.GetTexture(materialPropertyName);
        }
    }
}

#endif
