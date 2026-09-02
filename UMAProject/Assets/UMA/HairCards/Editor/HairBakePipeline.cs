using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    public sealed class HairBakeOutcome
    {
        public HairValidationReport validation;
        public readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
        public readonly List<string> warnings = new List<string>();
        public bool succeeded;
        public int cardCount;
        public int vertexCount;
        public int triangleCount;
    }

    public static class HairBakePipeline
    {
        private sealed class BakeTransaction : IDisposable
        {
            private readonly Dictionary<UnityEngine.Object, UnityEngine.Object> backups =
                new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            private readonly List<string> createdPaths = new List<string>();
            private bool committed;

            public void Backup(UnityEngine.Object target)
            {
                if (target == null || backups.ContainsKey(target)) return;
                UnityEngine.Object snapshot = UnityEngine.Object.Instantiate(target);
                snapshot.hideFlags = HideFlags.HideAndDontSave;
                backups.Add(target, snapshot);
            }

            public void Created(string path)
            {
                if (!string.IsNullOrEmpty(path) && !createdPaths.Contains(path)) createdPaths.Add(path);
            }

            public void Commit()
            {
                committed = true;
                DisposeSnapshots();
                createdPaths.Clear();
            }

            public void Dispose()
            {
                if (!committed) Rollback();
                DisposeSnapshots();
            }

            private void Rollback()
            {
                foreach (KeyValuePair<UnityEngine.Object, UnityEngine.Object> pair in backups)
                {
                    if (pair.Key == null || pair.Value == null) continue;
                    EditorUtility.CopySerialized(pair.Value, pair.Key);
                    EditorUtility.SetDirty(pair.Key);
                }
                for (int i = createdPaths.Count - 1; i >= 0; i--)
                {
                    AssetDatabase.DeleteAsset(createdPaths[i]);
                }
                createdPaths.Clear();
            }

            private void DisposeSnapshots()
            {
                foreach (UnityEngine.Object snapshot in backups.Values)
                    if (snapshot != null) UnityEngine.Object.DestroyImmediate(snapshot);
                backups.Clear();
            }
        }

        public static HairBakeOutcome DryRun(HairGroomAsset groom, int lodLevel = 0)
        {
            HairBakeOutcome outcome = new HairBakeOutcome();
            if (groom == null)
            {
                outcome.validation = HairValidator.Validate(null);
                return outcome;
            }
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom,
                new HairEvaluationOptions { lodLevel = lodLevel });
            using (HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation, groom.name + " Dry Run"))
            {
                outcome.validation = HairValidator.Validate(groom, evaluation, build, ValidationOptions(groom));
                outcome.cardCount = evaluation.CardCount;
                outcome.vertexCount = build.vertexCount;
                outcome.triangleCount = build.triangleCount;
                outcome.succeeded = outcome.validation.CanBake;
            }
            return outcome;
        }

        public static HairBakeOutcome Bake(HairGroomAsset groom, DynamicCharacterAvatar avatar = null)
        {
            HairBakeOutcome outcome = new HairBakeOutcome();
            if (groom == null)
            {
                outcome.validation = HairValidator.Validate(null);
                return outcome;
            }
            groom.EnsureIntegrity();
            HairBakeSettings settings = groom.BakeSettings;
            string folder = NormalizeFolder(settings.outputFolder);
            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets", StringComparison.Ordinal))
            {
                outcome.warnings.Add("The bake output folder must be inside Assets.");
                return outcome;
            }
            EnsureFolder(folder);

            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom,
                new HairEvaluationOptions { lodLevel = 0 });
            using (HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation, settings.assetName))
            {
                outcome.validation = HairValidator.Validate(groom, evaluation, build, ValidationOptions(groom));
                outcome.cardCount = evaluation.CardCount;
                outcome.vertexCount = build.vertexCount;
                outcome.triangleCount = build.triangleCount;
                if (!outcome.validation.CanBake)
                {
                    outcome.warnings.Add("Bake stopped because validation contains blocking errors.");
                    return outcome;
                }

                Mesh sourceSkinMesh = ResolveSkinMesh(groom, avatar);
                if (!HairSkinningUtility.TransferClosestVertexWeights(build.mesh, sourceSkinMesh,
                        out string skinningWarning))
                {
                    outcome.warnings.Add(skinningWarning);
                }

                try
                {
                    using BakeTransaction transaction = new BakeTransaction();
                    AssetDatabase.StartAssetEditing();
                    Mesh meshAsset = null;
                    if (settings.createMesh)
                    {
                        meshAsset = WriteMesh(build.mesh, folder, Sanitize(settings.assetName) + "_LOD0.asset",
                            settings.overwriteExisting, transaction);
                        outcome.assets.Add(meshAsset);
                        WriteAdditionalLods(groom, folder, settings, outcome, transaction);
                    }

                    SlotDataAsset slot = null;
                    if (settings.createSlot)
                    {
                        slot = WriteSlot(groom, build.mesh, avatar, folder, settings, outcome, transaction);
                        if (slot != null) outcome.assets.Add(slot);
                    }

                    OverlayDataAsset overlay = null;
                    if (settings.createOverlay)
                    {
                        overlay = WriteOverlay(groom, folder, settings, outcome, transaction);
                        if (overlay != null && !outcome.assets.Contains(overlay)) outcome.assets.Add(overlay);
                    }

                    if (settings.createWardrobeRecipe)
                    {
                        UMAWardrobeRecipe recipe = WriteWardrobeRecipe(groom, slot, overlay, folder, settings, outcome,
                            transaction);
                        if (recipe != null) outcome.assets.Add(recipe);
                    }
                    outcome.succeeded = meshAsset != null || slot != null;
                    if (outcome.succeeded) transaction.Commit();
                }
                catch (Exception exception)
                {
                    outcome.succeeded = false;
                    outcome.warnings.Add("Bake failed without replacing the groom source: " + exception.Message);
                    Debug.LogException(exception);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
            }

            if (outcome.succeeded && settings.updateGlobalLibrary)
            {
                try
                {
                    AddAssetsToIndexer(outcome.assets);
                }
                catch (Exception exception)
                {
                    outcome.warnings.Add("Assets were baked, but UMA global-library registration failed: " +
                                         exception.Message);
                }
            }
            if (outcome.succeeded && outcome.assets.Count > 0)
            {
                Selection.activeObject = outcome.assets[0];
                EditorGUIUtility.PingObject(outcome.assets[0]);
            }
            return outcome;
        }

        private static void WriteAdditionalLods(HairGroomAsset groom, string folder,
            HairBakeSettings settings, HairBakeOutcome outcome, BakeTransaction transaction)
        {
            for (int lodIndex = 1; lodIndex < groom.Lods.Count; lodIndex++)
            {
                HairLodSettings lod = groom.Lods[lodIndex];
                HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom,
                    new HairEvaluationOptions { lodLevel = lod.level });
                using (HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation,
                           settings.assetName + "_LOD" + lod.level))
                {
                    HairSkinningUtility.TransferClosestVertexWeights(build.mesh, ResolveSkinMesh(groom, null), out _);
                    Mesh asset = WriteMesh(build.mesh, folder,
                        Sanitize(settings.assetName) + "_LOD" + lod.level + ".asset", settings.overwriteExisting,
                        transaction);
                    outcome.assets.Add(asset);
                }
            }
        }

        private static Mesh WriteMesh(Mesh source, string folder, string fileName, bool overwrite,
            BakeTransaction transaction)
        {
            string path = folder + "/" + fileName;
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                if (!overwrite) path = AssetDatabase.GenerateUniqueAssetPath(path);
                else
                {
                    transaction.Backup(existing);
                    Undo.RecordObject(existing, "Update Hair Card Mesh");
                    EditorUtility.CopySerialized(source, existing);
                    existing.name = System.IO.Path.GetFileNameWithoutExtension(path);
                    EditorUtility.SetDirty(existing);
                    return existing;
                }
            }
            Mesh mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(mesh, path);
            transaction.Created(path);
            Undo.RegisterCreatedObjectUndo(mesh, "Create Hair Card Mesh");
            return mesh;
        }

        private static SlotDataAsset WriteSlot(HairGroomAsset groom, Mesh generated,
            DynamicCharacterAvatar avatar, string folder, HairBakeSettings settings, HairBakeOutcome outcome,
            BakeTransaction transaction)
        {
            string logicalName = Sanitize(settings.assetName);
            string path = folder + "/" + logicalName + "_Slot.asset";
            SlotDataAsset slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(path);
            bool created = slot == null;
            if (slot == null)
            {
                slot = ScriptableObject.CreateInstance<SlotDataAsset>();
                slot.name = logicalName + "_Slot";
            }
            else if (!settings.overwriteExisting)
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                slot = ScriptableObject.CreateInstance<SlotDataAsset>();
                slot.name = System.IO.Path.GetFileNameWithoutExtension(path);
                created = true;
            }
            else
            {
                transaction.Backup(slot);
                Undo.RecordObject(slot, "Update Hair Card UMA Slot");
            }

            GameObject temporary = new GameObject("Hair Slot Bake");
            temporary.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                SkinnedMeshRenderer renderer = temporary.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = generated;
                SkinnedMeshRenderer sourceRenderer = ResolveRenderer(avatar);
                if (sourceRenderer != null)
                {
                    renderer.bones = sourceRenderer.bones;
                    renderer.rootBone = sourceRenderer.rootBone;
                }
                string rootName = renderer.rootBone != null ? renderer.rootBone.name : "Global";
                slot.UpdateMeshData(renderer, rootName, false, -1, false, false);
                slot.subMeshIndex = 0;
                slot.PrepareForAssetPath(path, logicalName);
                List<string> reasons = new List<string>();
                if (!slot.ValidateMeshData(reasons))
                    outcome.warnings.Add("The generated SlotDataAsset needs attention: " + string.Join("; ", reasons));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(temporary);
            }

            if (created)
            {
                AssetDatabase.CreateAsset(slot, path);
                transaction.Created(path);
                Undo.RegisterCreatedObjectUndo(slot, "Create Hair Card UMA Slot");
            }
            EditorUtility.SetDirty(slot);
            return slot;
        }

        private static OverlayDataAsset WriteOverlay(HairGroomAsset groom, string folder,
            HairBakeSettings settings, HairBakeOutcome outcome, BakeTransaction transaction)
        {
            if (settings.overlayTemplate is OverlayDataAsset template)
            {
                return template;
            }
            if (!(settings.umaMaterial is UMAMaterial umaMaterial))
            {
                outcome.warnings.Add("Overlay creation skipped: assign an UMA Material or an existing OverlayDataAsset in Bake settings.");
                return null;
            }

            string path = folder + "/" + Sanitize(settings.assetName) + "_Overlay.asset";
            OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(path);
            bool created = overlay == null;
            if (overlay == null) overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            else if (!settings.overwriteExisting)
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
                created = true;
            }
            else
            {
                transaction.Backup(overlay);
                Undo.RecordObject(overlay, "Update Hair Card Overlay");
            }

            overlay.name = Sanitize(settings.assetName) + "_Overlay";
            overlay.material = umaMaterial;
            int channelCount = umaMaterial.channels != null ? umaMaterial.channels.Length : 1;
            overlay.textureList = new Texture[Mathf.Max(1, channelCount)];
            overlay.overlayBlend = new OverlayDataAsset.OverlayBlend[overlay.textureList.Length];
            HairAtlasProfileAsset atlas = FirstAtlas(groom);
            for (int channel = 0; channel < overlay.textureList.Length; channel++)
            {
                string property = channel < channelCount
                    ? umaMaterial.channels[channel].materialPropertyName ?? string.Empty
                    : string.Empty;
                string lower = property.ToLowerInvariant();
                overlay.textureList[channel] = lower.Contains("normal") ? atlas?.normal :
                    lower.Contains("mask") || lower.Contains("metal") ? atlas?.mask : atlas?.albedo;
                overlay.overlayBlend[channel] = OverlayDataAsset.OverlayBlend.Normal;
            }
            if (created)
            {
                AssetDatabase.CreateAsset(overlay, path);
                transaction.Created(path);
                Undo.RegisterCreatedObjectUndo(overlay, "Create Hair Card Overlay");
            }
            EditorUtility.SetDirty(overlay);
            return overlay;
        }

        private static UMAWardrobeRecipe WriteWardrobeRecipe(HairGroomAsset groom, SlotDataAsset slot,
            OverlayDataAsset overlay, string folder, HairBakeSettings settings, HairBakeOutcome outcome,
            BakeTransaction transaction)
        {
            if (slot == null || overlay == null)
            {
                outcome.warnings.Add("Wardrobe recipe creation skipped because both a slot and overlay are required.");
                return null;
            }
            RaceData race = settings.raceData as RaceData;
            if (race == null)
            {
                outcome.warnings.Add("Wardrobe recipe creation skipped: assign a compatible RaceData in Bake settings.");
                return null;
            }

            UMAData.UMARecipe recipeData = new UMAData.UMARecipe();
            recipeData.ClearDna();
            recipeData.SetRace(race);
            SlotData slotData = new SlotData(slot) { Races = new[] { race.raceName } };
            slotData.AddOverlay(new OverlayData(overlay));
            recipeData.SetSlot(0, slotData);

            string path = folder + "/" + Sanitize(settings.assetName) + "_Wardrobe.asset";
            UMAWardrobeRecipe recipe = AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(path);
            bool created = recipe == null;
            if (recipe == null) recipe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            else if (!settings.overwriteExisting)
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                recipe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
                created = true;
            }
            else
            {
                transaction.Backup(recipe);
                Undo.RecordObject(recipe, "Update Hair Card Wardrobe Recipe");
            }
            recipe.name = Sanitize(settings.assetName) + "_Wardrobe";
            recipe.recipeType = "Wardrobe";
            recipe.DisplayValue = settings.assetName;
            recipe.wardrobeSlot = string.IsNullOrWhiteSpace(settings.wardrobeSlot) ? "Hair" : settings.wardrobeSlot;
            recipe.compatibleRaces = new List<string> { race.raceName };
            recipe.Save(recipeData);
            if (created)
            {
                AssetDatabase.CreateAsset(recipe, path);
                transaction.Created(path);
                Undo.RegisterCreatedObjectUndo(recipe, "Create Hair Card Wardrobe Recipe");
            }
            EditorUtility.SetDirty(recipe);
            return recipe;
        }

        private static void AddAssetsToIndexer(IReadOnlyList<UnityEngine.Object> assets)
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null) return;
            for (int i = 0; i < assets.Count; i++)
            {
                UnityEngine.Object asset = assets[i];
                if (asset is SlotDataAsset) indexer.EvilAddAsset(typeof(SlotDataAsset), asset);
                else if (asset is OverlayDataAsset) indexer.EvilAddAsset(typeof(OverlayDataAsset), asset);
                else if (asset is UMAWardrobeRecipe) indexer.EvilAddAsset(typeof(UMAWardrobeRecipe), asset);
            }
            indexer.ForceSave();
        }

        private static Mesh ResolveSkinMesh(HairGroomAsset groom, DynamicCharacterAvatar avatar)
        {
            SkinnedMeshRenderer renderer = ResolveRenderer(avatar);
            return renderer != null && renderer.sharedMesh != null ? renderer.sharedMesh : groom.SourceMesh;
        }

        private static SkinnedMeshRenderer ResolveRenderer(DynamicCharacterAvatar avatar)
        {
            if (avatar?.umaData != null)
            {
                SkinnedMeshRenderer renderer = avatar.umaData.GetRenderer(0);
                if (renderer != null) return renderer;
            }
            return avatar != null ? avatar.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        }

        private static HairAtlasProfileAsset FirstAtlas(HairGroomAsset groom)
        {
            for (int i = 0; i < groom.Groups.Count; i++)
                if (groom.Groups[i]?.atlas != null) return groom.Groups[i].atlas;
            return null;
        }

        private static HairValidationOptions ValidationOptions(HairGroomAsset groom)
        {
            return new HairValidationOptions
            {
                triangleBudget = groom.BakeSettings.triangleBudget,
                cardBudget = groom.BakeSettings.cardBudget,
                requireAtlas = groom.BakeSettings.requireAtlas
            };
        }

        private static string NormalizeFolder(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/').TrimEnd('/');
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "HairCards";
            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value.Trim();
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
