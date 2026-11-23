using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>Opens the Clothes Designer: on a race, or on a clothing slot that already exists.</summary>
    public static class ClothesDesignerLauncher
    {
        private const string Title = "Clothes Designer";

        [MenuItem("UMA/Clothes Designer/Create Clothes on Race...", priority = 130)]
        private static void CreateClothesMenu()
        {
            // A race or a character that is selected is the obvious choice; otherwise ask.
            RaceData race = Selection.activeObject as RaceData;
            if (race == null && Selection.activeGameObject != null)
            {
                race = GetRace(Selection.activeGameObject.GetComponent<DynamicCharacterAvatar>());
            }
            ClothesDesignerRacePicker.Show(race, picked => CreateClothesOnRace(picked));
        }

        /// <summary>
        /// Opens the Vertex Editor on a temporary character of the race, in the race's modelled pose,
        /// with the Clothes Designer active. Nothing needs to be in the scene.
        /// </summary>
        /// <returns>The stage, or null when it could not be opened (the user has been told why).</returns>
        public static VertexEditorStage CreateClothesOnRace(RaceData race)
        {
            if (race == null)
            {
                EditorUtility.DisplayDialog(Title, "Choose a race to make clothes for.", "OK");
                return null;
            }
            if (!VertexEditorStage.CloseOpenVertexEditor())
            {
                // The artist chose to stay in the Vertex Editor that is open.
                return null;
            }
            VertexEditorStage stage = VertexEditorStage.ShowStageForRace(race, out string message);
            if (stage == null)
            {
                EditorUtility.DisplayDialog(Title,
                    "A body for '" + race.raceName + "' could not be built.\n\n" + (message ?? "The race may have no base recipe."), "OK");
                return null;
            }
            return Activate(stage, ClothesDesignerWorkflow.Mode.NewClothes, null);
        }

        /// <summary>
        /// Opens the designer to refit existing clothing: the slots are put on a temporary character of
        /// the race.
        /// </summary>
        public static VertexEditorStage FitExistingSlots(RaceData race, IList<SlotDataAsset> slots)
        {
            if (race == null)
            {
                EditorUtility.DisplayDialog(Title, "Choose the race these clothes are worn by.", "OK");
                return null;
            }
            if (!VertexEditorStage.CloseOpenVertexEditor())
            {
                return null;
            }
            VertexEditorStage stage = VertexEditorStage.ShowStageForSlots(race, slots, out string message);
            if (stage == null)
            {
                EditorUtility.DisplayDialog(Title,
                    "The clothes could not be put on '" + race.raceName + "'.\n\n" + (message ?? string.Empty), "OK");
                return null;
            }
            var names = new List<string>();
            foreach (SlotDataAsset slot in slots)
            {
                if (slot != null && !names.Contains(slot.slotName))
                {
                    names.Add(slot.slotName);
                }
            }
            if (!string.IsNullOrEmpty(message))
            {
                // Some of the slots were left off; the stage opened on the others.
                Debug.LogWarning("[Clothes Designer] " + message);
            }
            return Activate(stage, ClothesDesignerWorkflow.Mode.FitExistingSlot, names);
        }

        private static VertexEditorStage Activate(VertexEditorStage stage, ClothesDesignerWorkflow.Mode mode, IEnumerable<string> slotNames)
        {
            ClothesDesignerWorkflow.Instance.SetMode(stage, mode, slotNames);
            if (!stage.ActivateWorkflow(ClothesDesignerWorkflow.WorkflowId))
            {
                EditorUtility.DisplayDialog(Title, "The Vertex Editor opened, but the Clothes Designer could not be started in it. See the Console for the reason.", "OK");
            }
            return stage;
        }

        internal static RaceData GetRace(DynamicCharacterAvatar avatar)
        {
            if (avatar == null || avatar.activeRace == null)
            {
                return null;
            }
            if (avatar.activeRace.data != null)
            {
                return avatar.activeRace.data;
            }
            if (avatar.activeRace.racedata != null)
            {
                return avatar.activeRace.racedata;
            }
            return FindRace(avatar.activeRace.name);
        }

        internal static RaceData FindRace(string raceName)
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            return indexer != null && !string.IsNullOrEmpty(raceName) ? indexer.GetRace(raceName) : null;
        }

        /// <summary>The races in the Global Library an artist can make clothes for, by name.</summary>
        internal static List<RaceData> GetAllRaces()
        {
            var races = new List<RaceData>();
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            RaceData[] all = indexer != null ? indexer.GetAllRaces() : null;
            if (all != null)
            {
                foreach (RaceData race in all)
                {
                    if (race != null && race.raceName != "RaceDataPlaceholder" && race.baseRaceRecipe != null && !races.Contains(race))
                    {
                        races.Add(race);
                    }
                }
            }
            races.Sort((a, b) => string.CompareOrdinal(a.raceName, b.raceName));
            return races;
        }

        /// <summary>The first race in the list that the Global Library knows.</summary>
        internal static RaceData FirstKnownRace(IEnumerable<string> raceNames)
        {
            if (raceNames != null)
            {
                foreach (string name in raceNames)
                {
                    RaceData race = FindRace(name);
                    if (race != null)
                    {
                        return race;
                    }
                }
            }
            return null;
        }
    }
}
