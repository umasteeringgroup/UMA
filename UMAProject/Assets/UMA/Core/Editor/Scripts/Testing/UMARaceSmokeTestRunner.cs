#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors
{
    public static class UMARaceSmokeTestRunner
    {
        /// <summary>
        /// Runs every RaceData entry exposed by the UMA index. The optional callback is invoked
        /// before each race and returns false to cancel cleanly between races.
        /// </summary>
        public static UMATestReport RunAllIndexed(UMARaceSmokeTestOptions options = null,
            Func<int, int, RaceData, bool> continueCallback = null)
        {
            UMATestReport combined = new UMATestReport("UMA Indexed Race Smoke Test");
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null)
            {
                combined.AddError("Indexer", "UMAAssetIndexer.Instance is null.");
                return combined;
            }

            RaceData[] indexedRaces = indexer.GetAllRaces();
            if (indexedRaces == null || indexedRaces.Length == 0)
            {
                combined.AddError("Indexer", "UMAAssetIndexer.GetAllRaces returned no races.");
                return combined;
            }

            // Do not reorder an array that may be owned or cached by the indexer.
            RaceData[] sortedRaces = (RaceData[])indexedRaces.Clone();
            Array.Sort(sortedRaces, CompareIndexedRaces);
            int completed = 0;
            for (int raceIndex = 0; raceIndex < sortedRaces.Length; raceIndex++)
            {
                RaceData race = sortedRaces[raceIndex];
                if (continueCallback != null &&
                    !continueCallback(raceIndex, sortedRaces.Length, race))
                {
                    combined.AddInfo("Summary", "Cancelled after " + completed + " of " +
                        sortedRaces.Length + " indexed race(s).");
                    return combined;
                }

                if (race == null)
                {
                    combined.AddError("Indexed Race " + raceIndex,
                        "UMAAssetIndexer returned a null RaceData entry.");
                    completed++;
                    continue;
                }

                UMATestReport raceReport = Run(race, options);
                string raceLabel = RaceLabel(race);
                for (int messageIndex = 0; messageIndex < raceReport.Messages.Count;
                     messageIndex++)
                {
                    UMATestMessage message = raceReport.Messages[messageIndex];
                    combined.Add(message.Severity,
                        "Race: " + raceLabel + (string.IsNullOrEmpty(message.Category)
                            ? string.Empty : " / " + message.Category),
                        message.Message, message.Context != null ? message.Context : race);
                }
                completed++;
            }

            if (!combined.HasErrors)
            {
                combined.AddPass("Summary", "Completed smoke tests for all " + completed +
                    " indexed race(s).");
            }
            else
            {
                combined.AddInfo("Summary", "Completed smoke tests for all " + completed +
                    " indexed race(s); review the race-prefixed errors above.");
            }

            return combined;
        }

        public static UMATestReport Run(RaceData race, UMARaceSmokeTestOptions options = null)
        {
            UMARaceSmokeTestOptions resolvedOptions = options ?? UMARaceSmokeTestOptions.Default;
            UMATestReport report = new UMATestReport("UMA Race Smoke Test", race);

            if (race == null)
            {
                report.AddError("RaceData", "RaceData is null.");
                return report;
            }

            if (string.Equals(race.raceName, "RaceDataPlaceholder",
                StringComparison.OrdinalIgnoreCase))
            {
                report.AddInfo("RaceData", "Skipping placeholder race", race);
                return report;
            }

            try
            {
                UMARaceValidation.ValidateRaceData(race, report, false);
                if (race == null)
                {
                    return report;
                }

                ValidateIndexer(race, report);

                if (resolvedOptions.ValidateBaseRecipe)
                {
                    ValidateBaseRecipe(race, report);
                }

                if (resolvedOptions.GenerateTemporaryAvatar)
                {
                    ValidateTemporaryBuild(race, report);
                }

                if (resolvedOptions.IncludePassMessages && !report.HasErrors)
                {
                    report.AddPass("Summary", "Race smoke test completed without errors.", race);
                }
            }
            catch (Exception ex)
            {
                report.AddError("Exception", ex.GetType().Name + ": " + ex.Message, race);
            }

            return report;
        }

        private static int CompareIndexedRaces(RaceData left, RaceData right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;
            int byName = string.Compare(left.raceName, right.raceName,
                StringComparison.OrdinalIgnoreCase);
            if (byName != 0) return byName;
            return string.Compare(AssetDatabase.GetAssetPath(left), AssetDatabase.GetAssetPath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string RaceLabel(RaceData race)
        {
            if (race == null) return "(null)";
            if (!string.IsNullOrWhiteSpace(race.raceName)) return race.raceName;
            if (!string.IsNullOrWhiteSpace(race.name)) return race.name;
            string path = AssetDatabase.GetAssetPath(race);
            return string.IsNullOrEmpty(path) ? "(unnamed RaceData)" : path;
        }

        private static void ValidateIndexer(RaceData race, UMATestReport report)
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null)
            {
                report.AddError("Indexer", "UMAAssetIndexer.Instance is null.");
                return;
            }

            RaceData indexedRace = indexer.GetRace(race.raceName);
            if (indexedRace == null)
            {
                report.AddError("Indexer", "Race '" + race.raceName + "' is not available from UMAAssetIndexer.GetRace.", race);
                return;
            }

            if (indexedRace != race)
            {
                report.AddWarning("Indexer", "Race name '" + race.raceName + "' resolves to a different RaceData asset in the UMA index.", indexedRace);
                return;
            }

            report.AddPass("Indexer", "Race resolves through UMAAssetIndexer.", race);
        }

        private static void ValidateBaseRecipe(RaceData race, UMATestReport report)
        {
            if (race.UsesFbxRoute)
            {
                ValidateFbxRouteMesh(race, report);
                return;
            }

            if (race.baseRaceRecipe == null)
            {
                return;
            }

            UMAData.UMARecipe recipe = null;
            try
            {
                recipe = race.baseRaceRecipe.GetCachedRecipe(true);
            }
            catch (Exception ex)
            {
                report.AddError("Base Recipe", "baseRaceRecipe.GetCachedRecipe threw " + ex.GetType().Name + ": " + ex.Message, race.baseRaceRecipe);
                return;
            }

            if (recipe == null)
            {
                report.AddError("Base Recipe", "baseRaceRecipe.GetCachedRecipe returned null.", race.baseRaceRecipe);
                return;
            }

            SlotData[] slots = recipe.slotDataList;
            if (slots == null || slots.Length == 0)
            {
                report.AddError("Base Recipe", "Base race recipe has no slots.", race.baseRaceRecipe);
                return;
            }

            int inspectedSlotCount = 0;
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                SlotData slot = slots[slotIndex];
                if (slot == null)
                {
                    continue;
                }

                ValidateSlot(slot, slotIndex, report);
                inspectedSlotCount++;
            }

            if (inspectedSlotCount == 0)
            {
                report.AddError("Base Recipe", "Base race recipe has no non-null slot entries.", race.baseRaceRecipe);
                return;
            }

            if (inspectedSlotCount > 0)
            {
                report.AddPass("Base Recipe", "Validated " + inspectedSlotCount + " base recipe slot(s).", race.baseRaceRecipe);
            }
        }

        private static void ValidateFbxRouteMesh(RaceData race, UMATestReport report)
        {
            SkinnedMeshRenderer renderer = race.baseFbxRenderer;
            if (renderer == null || renderer.sharedMesh == null)
            {
                return;
            }

            Mesh mesh = renderer.sharedMesh;
            if (mesh.vertexCount <= 0)
            {
                report.AddError("FBX Route", "Base FBX Renderer mesh has no vertices.", renderer);
            }

            if (CountTriangleIndices(mesh) == 0)
            {
                report.AddError("FBX Route", "Base FBX Renderer mesh has no triangle topology.", renderer);
            }
            else
            {
                report.AddPass("FBX Route", "Base FBX Renderer mesh has vertices and triangles.", renderer);
            }
        }

        private static void ValidateSlot(SlotData slot, int slotIndex, UMATestReport report)
        {
            SlotDataAsset asset = slot.asset;
            string slotLabel = !string.IsNullOrEmpty(slot.slotName) ? slot.slotName : "slotDataList[" + slotIndex + "]";
            if (asset == null)
            {
                report.AddError("Base Recipe", slotLabel + " has no SlotDataAsset.");
                return;
            }

            List<string> reasons = new List<string>();
            if (!asset.ValidateMeshData(reasons))
            {
                for (int reasonIndex = 0; reasonIndex < reasons.Count; reasonIndex++)
                {
                    report.AddError("SlotDataAsset", asset.slotName + ": " + reasons[reasonIndex], asset);
                }
            }

            List<OverlayData> overlays = slot.GetOverlayList();
            if (overlays == null || overlays.Count == 0)
            {
                report.AddWarning("Base Recipe", asset.slotName + " has no overlays.", asset);
                return;
            }

            for (int overlayIndex = 0; overlayIndex < overlays.Count; overlayIndex++)
            {
                ValidateOverlay(overlays[overlayIndex], asset.slotName, overlayIndex, report);
            }
        }

        private static void ValidateOverlay(OverlayData overlay, string slotName, int overlayIndex, UMATestReport report)
        {
            if (overlay == null)
            {
                report.AddError("Overlay", slotName + " overlay[" + overlayIndex + "] is null.");
                return;
            }

            OverlayDataAsset asset = overlay.asset;
            if (asset == null)
            {
                report.AddError("Overlay", slotName + " overlay[" + overlayIndex + "] has no OverlayDataAsset.");
                return;
            }

            if (asset.material == null && string.IsNullOrEmpty(asset.materialName))
            {
                report.AddError("OverlayDataAsset", asset.overlayName + " has no UMAMaterial and no materialName fallback.", asset);
            }

            Texture[] textures = asset.textureList;
            if (textures == null)
            {
                report.AddError("OverlayDataAsset", asset.overlayName + " textureList is null.", asset);
                return;
            }

            if (asset.overlayBlend == null)
            {
                report.AddError("OverlayDataAsset", asset.overlayName + " overlayBlend is null.", asset);
            }
            else if (asset.overlayBlend.Length != textures.Length)
            {
                report.AddError("OverlayDataAsset", asset.overlayName + " overlayBlend length does not match textureList length.", asset);
            }

            if (asset.textureNames != null && asset.textureNames.Length != textures.Length)
            {
                report.AddWarning("OverlayDataAsset", asset.overlayName + " textureNames length does not match textureList length.", asset);
            }

            if (asset.material != null && asset.material.channels != null && textures.Length < asset.material.channels.Length)
            {
                report.AddWarning("OverlayDataAsset", asset.overlayName + " has fewer textures than its UMAMaterial channel count.", asset);
            }
        }

        private static void ValidateTemporaryBuild(RaceData race, UMATestReport report)
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null)
            {
                report.AddError("Build", "Cannot generate a temporary avatar because UMAAssetIndexer.Instance is null.", race);
                return;
            }

            UMAGenerator generator = indexer.Generator;
            if (generator == null)
            {
                report.AddError("Build", "Cannot generate a temporary avatar because UMAAssetIndexer.Generator is null.", race);
                return;
            }

            GameObject temporaryObject = null;
            DynamicCharacterAvatar avatar = null;
            try
            {
                temporaryObject = new GameObject("UMA_SmokeTest_" + MakeSafeObjectName(race.raceName));
                temporaryObject.hideFlags = HideFlags.HideAndDontSave;
                avatar = temporaryObject.AddComponent<DynamicCharacterAvatar>();
                avatar.hideFlags = HideFlags.HideAndDontSave;
                avatar.editorTimeGeneration = true;
                avatar.activeRace.name = race.raceName;
                avatar.activeRace.data = race;

                avatar.GenerateNow();
                ValidateGeneratedAvatar(avatar, race, report);
            }
            catch (Exception ex)
            {
                report.AddError("Build", "Temporary avatar generation threw " + ex.GetType().Name + ": " + ex.Message, race);
            }
            finally
            {
                if (avatar != null)
                {
                    try
                    {
                        avatar.CleanupGeneratedData();
                    }
                    catch
                    {
                    }
                }

                if (temporaryObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(temporaryObject);
                }
            }
        }

        private static void ValidateGeneratedAvatar(DynamicCharacterAvatar avatar, RaceData expectedRace, UMATestReport report)
        {
            if (avatar == null)
            {
                report.AddError("Build", "Temporary DynamicCharacterAvatar was not created.", expectedRace);
                return;
            }

            UMAData umaData = avatar.umaData;
            if (umaData == null)
            {
                report.AddError("Build", "Temporary DynamicCharacterAvatar has no UMAData after generation.", expectedRace);
                return;
            }

            if (umaData.umaRecipe == null)
            {
                report.AddError("Build", "Generated UMAData has no UMA recipe.", expectedRace);
            }
            else
            {
                RaceData builtRace = umaData.umaRecipe.GetRace();
                if (builtRace != expectedRace)
                {
                    report.AddError("Build", "Generated UMA recipe race does not match the requested race.", expectedRace);
                }
            }

            if (umaData.skeleton == null)
            {
                report.AddError("Build", "Generated UMAData has no skeleton.", expectedRace);
            }

            SkinnedMeshRenderer[] renderers = umaData.GetRenderers();
            if (renderers == null || renderers.Length == 0)
            {
                renderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            }

            ValidateRenderers(renderers, report, expectedRace);
        }

        private static void ValidateRenderers(SkinnedMeshRenderer[] renderers, UMATestReport report, RaceData race)
        {
            if (renderers == null || renderers.Length == 0)
            {
                report.AddError("Build", "Generated avatar has no SkinnedMeshRenderer.", race);
                return;
            }

            int validRendererCount = 0;
            int totalVertexCount = 0;
            int totalTriangleIndexCount = 0;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = renderers[rendererIndex];
                if (renderer == null)
                {
                    report.AddError("Build", "Generated renderer[" + rendererIndex + "] is null.", race);
                    continue;
                }

                Mesh mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    report.AddError("Build", "Generated renderer '" + renderer.name + "' has no shared mesh.", renderer);
                    continue;
                }

                if (mesh.vertexCount <= 0)
                {
                    report.AddError("Build", "Generated renderer '" + renderer.name + "' mesh has no vertices.", renderer);
                }

                int triangleIndexCount = CountTriangleIndices(mesh);
                if (triangleIndexCount == 0)
                {
                    report.AddError("Build", "Generated renderer '" + renderer.name + "' mesh has no triangle topology.", renderer);
                }

                if (renderer.bones == null || renderer.bones.Length == 0)
                {
                    report.AddError("Build", "Generated renderer '" + renderer.name + "' has no bones.", renderer);
                }

                if (mesh.bindposes == null || renderer.bones == null || mesh.bindposes.Length != renderer.bones.Length)
                {
                    report.AddError("Build", "Generated renderer '" + renderer.name + "' bindpose count does not match bone count.", renderer);
                }

                validRendererCount++;
                totalVertexCount += mesh.vertexCount;
                totalTriangleIndexCount += triangleIndexCount;
            }

            if (validRendererCount > 0 && totalVertexCount > 0 && totalTriangleIndexCount > 0)
            {
                report.AddPass("Build", "Generated " + validRendererCount + " renderer(s), " + totalVertexCount + " vertices, " + (totalTriangleIndexCount / 3) + " triangles.", race);
            }
        }

        private static int CountTriangleIndices(Mesh mesh)
        {
            if (mesh == null)
            {
                return 0;
            }

            int triangleIndexCount = 0;
            for (int submeshIndex = 0; submeshIndex < mesh.subMeshCount; submeshIndex++)
            {
                if (mesh.GetTopology(submeshIndex) == MeshTopology.Triangles)
                {
                    triangleIndexCount += mesh.GetTriangles(submeshIndex).Length;
                }
            }

            return triangleIndexCount;
        }

        private static string MakeSafeObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Race";
            }

            char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
            string safeName = value.Trim();
            for (int i = 0; i < invalidChars.Length; i++)
            {
                safeName = safeName.Replace(invalidChars[i], '_');
            }

            return safeName.Replace(' ', '_');
        }
    }
}

#endif
