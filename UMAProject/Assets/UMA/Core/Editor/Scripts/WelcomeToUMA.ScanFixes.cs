using System;
using System.Collections.Generic;
using System.Linq;
using UMA.Editors;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    public partial class WelcomeToUMA
    {
        private sealed class BoundsFix
        {
            public RaceData Race;
            public bool Selected = true;
            public readonly List<LogLine> Lines = new List<LogLine>();
        }

        private sealed class MissingRecipeRaceFix
        {
            public UMATextRecipe Recipe;
            public string MissingRace;
            public bool Selected = true;
            public readonly List<LogLine> Lines = new List<LogLine>();
        }

        private readonly List<BoundsFix> boundsFixes = new List<BoundsFix>();
        private readonly List<RaceData> boundsSources = new List<RaceData>();
        private readonly Dictionary<OverlayDataAsset, List<LogLine>> blendModeFixes = new Dictionary<OverlayDataAsset, List<LogLine>>();
        private readonly Dictionary<UMAWardrobeRecipe, List<LogLine>> emptySlotFixes = new Dictionary<UMAWardrobeRecipe, List<LogLine>>();
        private RaceData boundsSource;
        private readonly List<MissingRecipeRaceFix> missingRecipeRaceFixes = new List<MissingRecipeRaceFix>();
        private readonly List<RaceData> replacementRaces = new List<RaceData>();
        private RaceData replacementRace;
        private int missingRecipeRaceAction = 1;
        private static readonly string[] MissingRaceActions = { "Remove race reference", "Change to selected race" };

        private void ClearScanFixes()
        {
            boundsFixes.Clear(); boundsSources.Clear(); blendModeFixes.Clear(); boundsSource = null;
            emptySlotFixes.Clear();
            missingRecipeRaceFixes.Clear(); replacementRaces.Clear(); replacementRace = null;
        }

        private void RecordBoundsFix(RaceData race, LogLine line)
        {
            var entry = boundsFixes.FirstOrDefault(f => f.Race == race);
            if (entry == null) { entry = new BoundsFix { Race = race }; boundsFixes.Add(entry); }
            entry.Lines.Add(line);
        }

        private void RecordBlendModeFix(OverlayDataAsset overlay, LogLine line)
        {
            if (!blendModeFixes.TryGetValue(overlay, out var lines))
                blendModeFixes.Add(overlay, lines = new List<LogLine>());
            lines.Add(line);
        }

        private void RecordEmptySlotFix(UMAWardrobeRecipe recipe, LogLine line)
        {
            if (!emptySlotFixes.TryGetValue(recipe, out var lines))
                emptySlotFixes.Add(recipe, lines = new List<LogLine>());
            lines.Add(line);
        }

        private void DrawScanFixes()
        {
            if (boundsFixes.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Fix all — manual renderer bounds", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Select the races to repair, then choose a source race. Copies its bounds extents and center and saves each selected race. Choose bounds suitable for the target characters' size.", MessageType.Info);
                foreach (var fix in boundsFixes)
                {
                    if (fix.Race == null) continue;
                    fix.Selected = EditorGUILayout.ToggleLeft(new GUIContent(fix.Race.raceName,
                        AssetDatabase.GetAssetPath(fix.Race)), fix.Selected);
                }
                var validSources = boundsSources.Where(UMAScanRepairUtility.HasValidManualBounds).OrderBy(r => r.raceName).ToList();
                var labels = new List<string> { "Choose source race…" };
                labels.AddRange(validSources.Select(r => r.raceName + " (" + r.name + ")"));
                int current = validSources.IndexOf(boundsSource) + 1;
                int choice = EditorGUILayout.Popup("Copy good values from", current, labels.ToArray());
                if (choice != current) boundsSource = choice > 0 ? validSources[choice - 1] : null;
                boundsSource = (RaceData)EditorGUILayout.ObjectField("Source race asset", boundsSource, typeof(RaceData), false);
                bool valid = UMAScanRepairUtility.HasValidManualBounds(boundsSource);
                if (valid)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.Vector3Field("Extents to copy", boundsSource.manualRendererBounds);
                        EditorGUILayout.Vector3Field("Center to copy", boundsSource.manualRendererBoundsCenter);
                    }
                }
                else if (boundsSource != null)
                    EditorGUILayout.HelpBox("The source must have manual bounds enabled, positive finite extents, and a finite center.", MessageType.Warning);
                using (new EditorGUI.DisabledScope(!valid || !boundsFixes.Any(f => f.Selected && f.Race != null)))
                    if (GUILayout.Button("Fix all selected races and save")) FixSelectedBounds();
                EditorGUILayout.EndVertical();
            }
            if (blendModeFixes.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Fix all — overlay blend modes", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Repair " + blendModeFixes.Count + " overlay(s) found by this scan. Keep existing blend modes, add Normal for missing entries, and remove entries beyond the texture count. Textures are unchanged.", MessageType.Info);
                if (GUILayout.Button("Fix all overlay blend modes and save")) FixAllBlendModes();
                EditorGUILayout.EndVertical();
            }
            if (emptySlotFixes.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Fix all — empty wardrobe slot entries", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Remove unnamed slot entries from " + emptySlotFixes.Count +
                    " wardrobe recipe(s) found by this scan and save them. Preserve named slots, placeholders, and shared overlays.", MessageType.Info);
                if (GUILayout.Button("Fix all empty slot entries and save")) FixAllEmptySlots();
                EditorGUILayout.EndVertical();
            }
            DrawMissingRecipeRaceFixes();
        }

        private void RecordMissingRecipeRaceFix(UMATextRecipe recipe, string race, LogLine line)
        {
            if (missingRecipeRaceFixes.Count == 0)
            {
                replacementRaces.Clear();
                foreach (var item in UMAAssetIndexer.Instance.GetAssetItems<RaceData>())
                {
                    var candidate = item?.GetItem<RaceData>();
                    if (candidate != null && !string.IsNullOrWhiteSpace(candidate.raceName)) replacementRaces.Add(candidate);
                }
                replacementRaces.Sort((a, b) => string.Compare(a.raceName, b.raceName, StringComparison.OrdinalIgnoreCase));
            }
            var fix = missingRecipeRaceFixes.FirstOrDefault(f => f.Recipe == recipe);
            if (fix == null)
            {
                fix = new MissingRecipeRaceFix { Recipe = recipe, MissingRace = race };
                missingRecipeRaceFixes.Add(fix);
            }
            fix.Lines.Add(line);
        }

        private void DrawMissingRecipeRaceFixes()
        {
            if (missingRecipeRaceFixes.Count == 0) return;
            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Fix all — missing character recipe races", EditorStyles.boldLabel);
                foreach (var fix in missingRecipeRaceFixes)
                {
                    if (fix.Recipe == null) continue;
                    fix.Selected = EditorGUILayout.ToggleLeft(new GUIContent(fix.Recipe.name + " — " + fix.MissingRace,
                        AssetDatabase.GetAssetPath(fix.Recipe)), fix.Selected);
                }
                missingRecipeRaceAction = EditorGUILayout.Popup("Action", missingRecipeRaceAction, MissingRaceActions);
                bool remove = missingRecipeRaceAction == 0;
                if (remove)
                    EditorGUILayout.HelpBox("Clear the race reference and save. Placeholder and utility recipes can omit a race; regular character recipes will still need a valid race.", MessageType.Info);
                else
                {
                    var choices = replacementRaces.Where(r => r != null).ToList();
                    var labels = new List<string> { "Choose replacement race…" };
                    labels.AddRange(choices.Select(r => r.raceName + " (" + r.name + ")"));
                    int current = choices.IndexOf(replacementRace) + 1;
                    int selected = EditorGUILayout.Popup("Replacement race", current, labels.ToArray());
                    if (current != selected) replacementRace = selected > 0 ? choices[selected - 1] : null;
                }
                using (new EditorGUI.DisabledScope((!remove && replacementRace == null) ||
                    !missingRecipeRaceFixes.Any(f => f.Selected && f.Recipe != null)))
                    if (GUILayout.Button("Fix all selected recipe races and save")) FixAllMissingRecipeRaces();
            }
        }

        private void FixAllMissingRecipeRaces()
        {
            var lib = UMAAssetIndexer.Instance;
            bool remove = missingRecipeRaceAction == 0;
            if (lib == null || (!remove && replacementRace == null)) return;
            int saved = 0, skipped = 0, failed = 0;
            Undo.IncrementCurrentGroup(); int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fix missing character recipe races");
            foreach (var fix in missingRecipeRaceFixes.Where(f => f.Selected).ToArray())
            {
                try
                {
                    if (fix.Recipe == null) throw new InvalidOperationException("An affected recipe was deleted. Run the scan again.");
                    bool changed = UMAScanRepairUtility.FixMissingRecipeRaceAndSave(fix.Recipe, fix.MissingRace,
                        remove ? null : replacementRace, name => lib.HasAsset<RaceData>(name));
                    if (changed) saved++; else skipped++;
                    string result = !changed ? "missing race reference already resolved."
                        : remove ? "removed the missing race reference and saved."
                        : $"changed race to '{replacementRace.raceName}' and saved.";
                    foreach (var line in fix.Lines) line.Resolve($"Character recipe '{fix.Recipe.name}': {result}");
                    missingRecipeRaceFixes.Remove(fix);
                }
                catch (Exception e) { failed++; AddText(e.Message, LogType.Error); }
            }
            Undo.CollapseUndoOperations(undoGroup);
            AddText($"Recipe race fix complete: {saved} saved, {skipped} already resolved, {failed} failed."); Repaint();
        }

        private void FixAllEmptySlots()
        {
            int saved = 0, removed = 0, skipped = 0, failed = 0;
            Undo.IncrementCurrentGroup(); int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Remove empty wardrobe recipe slots");
            foreach (var fix in emptySlotFixes.ToArray())
            {
                try
                {
                    if (fix.Key == null) throw new InvalidOperationException("An affected wardrobe recipe was deleted. Run the scan again.");
                    int count = UMAScanRepairUtility.FixEmptySlotsAndSave(fix.Key);
                    if (count > 0) { saved++; removed += count; } else skipped++;
                    foreach (var line in fix.Value) line.Resolve(count > 0
                        ? $"Wardrobe Recipe '{fix.Key.name}': removed {count} empty slot entries and saved."
                        : $"Wardrobe Recipe '{fix.Key.name}' no longer has empty slot entries.");
                    emptySlotFixes.Remove(fix.Key);
                }
                catch (Exception e) { failed++; AddText(e.Message, LogType.Error); }
            }
            Undo.CollapseUndoOperations(undoGroup);
            AddText($"Empty slot fix complete: {removed} entries removed, {saved} recipes saved, {skipped} already resolved, {failed} failed."); Repaint();
        }

        private void FixSelectedBounds()
        {
            if (!UMAScanRepairUtility.HasValidManualBounds(boundsSource)) return;
            int saved = 0, skipped = 0, failed = 0;
            Undo.IncrementCurrentGroup(); int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fix race manual renderer bounds");
            foreach (var fix in boundsFixes.Where(f => f.Selected).ToArray())
            {
                try
                {
                    if (fix.Race == null) throw new InvalidOperationException("A selected race was deleted. Run the scan again.");
                    bool changed = UMAScanRepairUtility.FixBoundsAndSave(fix.Race, boundsSource);
                    if (changed) saved++; else skipped++;
                    foreach (var line in fix.Lines) line.Resolve(changed
                        ? "Race '" + fix.Race.raceName + "': copied manual renderer bounds from '" + boundsSource.raceName + "' and saved."
                        : "Race '" + fix.Race.raceName + "' no longer needs a manual bounds repair.");
                    boundsFixes.Remove(fix);
                }
                catch (Exception e) { failed++; AddText(e.Message, LogType.Error); }
            }
            Undo.CollapseUndoOperations(undoGroup);
            AddText($"Manual bounds fix complete: {saved} saved, {skipped} already resolved, {failed} failed."); Repaint();
        }

        private void FixAllBlendModes()
        {
            int saved = 0, skipped = 0, failed = 0;
            Undo.IncrementCurrentGroup(); int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fix overlay blend-mode counts");
            foreach (var fix in blendModeFixes.ToArray())
            {
                try
                {
                    if (fix.Key == null) throw new InvalidOperationException("An affected overlay was deleted. Run the scan again.");
                    bool changed = UMAScanRepairUtility.FixBlendModesAndSave(fix.Key);
                    if (changed) saved++; else skipped++;
                    foreach (var line in fix.Value) line.Resolve(changed
                        ? "Overlay '" + fix.Key.overlayName + "': blend-mode count repaired and saved."
                        : "Overlay '" + fix.Key.overlayName + "' no longer needs a blend-mode count repair.");
                    blendModeFixes.Remove(fix.Key);
                }
                catch (Exception e) { failed++; AddText(e.Message, LogType.Error); }
            }
            Undo.CollapseUndoOperations(undoGroup);
            AddText($"Overlay blend-mode fix complete: {saved} saved, {skipped} already resolved, {failed} failed."); Repaint();
        }
    }
}
