#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.Editors
{
    public static class UMARaceValidation
    {
        public static UMATestReport ValidateRaceData(RaceData race, bool includeSuccessMessage = true)
        {
            UMATestReport report = new UMATestReport("RaceData Validation", race);
            ValidateRaceData(race, report, includeSuccessMessage);
            return report;
        }

        public static void ValidateRaceData(RaceData race, UMATestReport report, bool includeSuccessMessage = true)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (race.raceName.ToLower() == "racedataplaceholder")
            {
                report.AddInfo("RaceData", "Skipping placeholder race", race);
                return;
            }

            int startingMessageCount = report.Messages.Count;
            if (race == null)
            {
                report.AddError("RaceData", "RaceData is null.");
                return;
            }

            ValidateBaseDefinition(race, report);
            ValidateWardrobeSlots(race, report);
            ValidateDna(race, report);

            if (includeSuccessMessage && report.Messages.Count == startingMessageCount)
            {
                report.AddInfo("RaceData", "No problems found. This RaceData looks good!", race);
            }
        }

        public static List<string> GetInspectorMessages(RaceData race)
        {
            UMATestReport report = ValidateRaceData(race, true);
            List<string> messages = new List<string>(report.Messages.Count);
            for (int i = 0; i < report.Messages.Count; i++)
            {
                messages.Add(report.Messages[i].ToInspectorString());
            }

            return messages;
        }

        private static void ValidateBaseDefinition(RaceData race, UMATestReport report)
        {
            if (race.UsesFbxRoute)
            {
                if (race.baseFbxRenderer == null)
                {
                    report.AddError("RaceData", "FBX route is enabled but Base FBX Renderer is not set", race);
                }
                else if (race.baseFbxRenderer.sharedMesh == null)
                {
                    report.AddError("RaceData", "FBX route Base FBX Renderer has no shared mesh", race.baseFbxRenderer);
                }
                else
                {
                    SkinnedMeshRenderer[] rootRenderers = race.baseFbxRenderer.transform.root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    if (rootRenderers.Length != 1 || rootRenderers[0] != race.baseFbxRenderer)
                    {
                        report.AddError("RaceData", "FBX route Base FBX Renderer must be the only SkinnedMeshRenderer under its prefab root", race.baseFbxRenderer);
                    }
                }
            }
            else if (race.baseRaceRecipe == null)
            {
                report.AddError("RaceData", "baseRaceRecipe is null", race);
            }

            if (race.TPose == null && race.umaTarget != RaceData.UMATarget.Generic)
            {
                report.AddError("RaceData", "TPose is not set. This is required to build a humanoid avatar and store the base bone positions", race);
            }

            if (race.umaTarget == RaceData.UMATarget.Generic && string.IsNullOrWhiteSpace(race.genericRootMotionTransformName))
            {
                report.AddError("RaceData", "genericRootMotionTransformName is null or empty. This is required for Generic UMA Targets.", race);
            }
        }

        private static void ValidateWardrobeSlots(RaceData race, UMATestReport report)
        {
            if (race.wardrobeSlots == null)
            {
                report.AddError("RaceData", "wardrobeSlots is null", race);
                return;
            }

            for (int i = 0; i < race.wardrobeSlots.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(race.wardrobeSlots[i]))
                {
                    report.AddError("RaceData", "wardrobeSlots[" + i + "] is null or empty. This could cause a problem with recipes loading.", race);
                }
            }
        }

        private static void ValidateDna(RaceData race, UMATestReport report)
        {
            if (race.useNewDNA)
            {
                if (race.DNACollection == null)
                {
                    report.AddError("RaceData", "DNACollection is null", race);
                }
                else if (race.DNACollection.DNAGroups == null || race.DNACollection.DNAGroups.Count == 0)
                {
                    report.AddError("RaceData", "DNACollection has no DNAGroups", race);
                }

                return;
            }

            DynamicDNAConverterController[] dnaConverterList = race.dnaConverterList;
            if (dnaConverterList == null)
            {
                report.AddError("RaceData", "dnaConverterList is null", race);
                return;
            }

            for (int i = 0; i < dnaConverterList.Length; i++)
            {
                DynamicDNAConverterController converter = dnaConverterList[i];
                if (converter == null)
                {
                    report.AddError("RaceData", "dnaConverterList[" + i + "] is null", race);
                    continue;
                }

                if (converter.dnaAsset == null)
                {
                    report.AddError("RaceData", "dnaConverterList[" + i + "] has a null dnaAsset", converter);
                    continue;
                }

                if (converter.dnaAsset.Names == null || converter.dnaAsset.Names.Length == 0)
                {
                    report.AddError("RaceData", "dnaConverterList[" + i + "] has a dnaAsset with no DNA names", converter.dnaAsset);
                }

                if (converter.dnaAsset.dnaTypeHash == 0)
                {
                    report.AddError("RaceData", "dnaConverterList[" + i + "] has a dnaAsset with a 0 dnaType hash", converter.dnaAsset);
                }

                if (converter.PluginCount == 0)
                {
                    report.AddWarning("RaceData", "dnaConverterList[" + i + "] has no DNA Converter Plugins. Is that intentional?", converter);
                }

                for (int pluginIndex = 0; pluginIndex < converter.PluginCount; pluginIndex++)
                {
                    DynamicDNAPlugin plugin = converter.GetPlugin(pluginIndex);
                    if (plugin == null)
                    {
                        report.AddError("RaceData", "dnaConverterList[" + i + "] has a null plugin at index " + pluginIndex, converter);
                    }
                }
            }
        }
    }
}

#endif