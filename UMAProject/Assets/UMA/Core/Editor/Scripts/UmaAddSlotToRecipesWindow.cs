using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// The dialog behind Assets > UMA > Add a slot to selected recipes: pick a slot, say when it
    /// applies (its matching criteria), optionally give it overlays and shared colors, and add all of
    /// that to every selected recipe.
    /// </summary>
    internal sealed class UmaAddSlotToRecipesWindow : EditorWindow
    {
        private const string OwnColor = "Its own color";
        private static readonly string[] QuickSharedColors = { "Hair", "Skin", "Eyes" };

        [SerializeField] private List<UMATextRecipe> targets = new List<UMATextRecipe>();
        [SerializeField] private UMARecipeSlotAddition addition = new UMARecipeSlotAddition();
        [SerializeField] private bool showRecipes;
        [SerializeField] private bool showCriteria = true;
        [SerializeField] private bool showOverlays = true;
        [SerializeField] private bool showSharedColors = true;
        [SerializeField] private string newTag = string.Empty;
        [SerializeField] private int raceToAdd;
        [SerializeField] private List<string> lastNotes = new List<string>();
        [SerializeField] private string lastResult = string.Empty;
        [SerializeField] private Vector2 scroll;

        [NonSerialized] private ReorderableList tagList;
        [NonSerialized] private ReorderableList raceList;
        [NonSerialized] private ReorderableList overlayList;
        [NonSerialized] private ReorderableList sharedColorList;
        [NonSerialized] private string[] raceNames;
        [NonSerialized] private SlotDataAsset libraryCheckedFor;
        [NonSerialized] private readonly List<string> libraryWarnings = new List<string>();
        [NonSerialized] private bool libraryWarningsDirty = true;

        public static UmaAddSlotToRecipesWindow Open(List<UMATextRecipe> recipes)
        {
            var window = GetWindow<UmaAddSlotToRecipesWindow>(true, "Add a Slot to Recipes", true);
            window.minSize = new Vector2(460f, 520f);
            window.targets.Clear();
            if (recipes != null)
            {
                for (int i = 0; i < recipes.Count; i++)
                {
                    if (recipes[i] != null && !window.targets.Contains(recipes[i]))
                    {
                        window.targets.Add(recipes[i]);
                    }
                }
            }
            window.lastResult = string.Empty;
            window.lastNotes.Clear();
            window.ShowUtility();
            window.Focus();
            return window;
        }

        private void OnEnable()
        {
            tagList = null;
            raceList = null;
            overlayList = null;
            sharedColorList = null;
            raceNames = null;
            libraryWarningsDirty = true;
        }

        private void OnGUI()
        {
            if (addition == null)
            {
                addition = new UMARecipeSlotAddition();
            }
            EnsureLists();

            EditorGUILayout.LabelField("Add a slot to selected recipes", EditorStyles.boldLabel);
            DrawRecipes();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawSlot();
            DrawCriteria();
            DrawOverlays();
            DrawSharedColors();
            EditorGUILayout.EndScrollView();

            DrawApply();
        }

        // --- the recipes ---------------------------------------------------------------------------

        private void DrawRecipes()
        {
            targets.RemoveAll(recipe => recipe == null);
            showRecipes = EditorGUILayout.Foldout(showRecipes, targets.Count == 1 ? "1 recipe selected" : targets.Count + " recipes selected", true);
            if (showRecipes)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUI.DisabledScope(true))
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        EditorGUILayout.ObjectField(targets[i], typeof(UMATextRecipe), false);
                    }
                }
                EditorGUI.indentLevel--;
            }
        }

        // --- the slot ------------------------------------------------------------------------------

        private void DrawSlot()
        {
            EditorGUILayout.Space(4f);
            EditorGUI.BeginChangeCheck();
            var picked = (SlotDataAsset)EditorGUILayout.ObjectField(
                new GUIContent("Slot", "The slot to add. Its tags and races are filled in below as a start, the way the recipe editor does when a slot is added."),
                addition.slot, typeof(SlotDataAsset), false);
            if (EditorGUI.EndChangeCheck() && picked != addition.slot)
            {
                addition.slot = picked;
                // Start from what the slot asset says, as a slot added in the recipe editor does.
                addition.tags.Clear();
                addition.races.Clear();
                if (picked != null)
                {
                    if (picked.tags != null) addition.tags.AddRange(picked.tags);
                    if (picked.Races != null) addition.races.AddRange(picked.Races);
                }
                libraryWarningsDirty = true;
                lastResult = string.Empty;
                lastNotes.Clear();
            }
            if (addition.slot != null && addition.slot.isWildCardSlot)
            {
                EditorGUILayout.LabelField("A wildcard slot: its overlays are applied to every slot or overlay that has one of the tags below.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        // --- matching criteria ---------------------------------------------------------------------

        private void DrawCriteria()
        {
            EditorGUILayout.Space(4f);
            showCriteria = EditorGUILayout.Foldout(showCriteria, "Matching criteria", true);
            if (!showCriteria)
            {
                return;
            }
            bool wildcard = addition.slot != null && addition.slot.isWildCardSlot;
            EditorGUILayout.LabelField(wildcard ? "Match tags" : "Tags for this slot", EditorStyles.miniBoldLabel);
            tagList.DoLayoutList();
            EditorGUILayout.BeginHorizontal();
            newTag = EditorGUILayout.TextField(new GUIContent("New tag", "Type a tag and press Add, or pick one of the project's tags from the list on the next row."), newTag);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newTag)))
            {
                if (GUILayout.Button("Add", GUILayout.Width(60f)))
                {
                    AddUnique(addition.tags, newTag.Trim());
                    newTag = string.Empty;
                    GUI.FocusControl(null);
                }
            }
            EditorGUILayout.EndHorizontal();
            CharacterBaseEditor.DoTagSelector(addition.tags);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Only add for these races (none: every race)", EditorStyles.miniBoldLabel);
            raceList.DoLayoutList();
            string[] names = RaceNames();
            if (names.Length == 0)
            {
                EditorGUILayout.LabelField("No races found in the Global Library.", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                raceToAdd = EditorGUILayout.Popup(Mathf.Clamp(raceToAdd, 0, names.Length - 1), names);
                if (GUILayout.Button("Add Race", GUILayout.Width(80f)))
                {
                    AddUnique(addition.races, names[raceToAdd]);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4f);
            addition.isSwapSlot = EditorGUILayout.ToggleLeft(
                new GUIContent("This is a swap slot", "A swap slot is only added when the character already has a slot with the tag below, and replaces that slot."),
                addition.isSwapSlot);
            if (addition.isSwapSlot)
            {
                // The label is longer than the usual label column.
                float labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 190f;
                EditorGUI.indentLevel++;
                addition.swapTag = EditorGUILayout.TextField("Swap slot(s) with this tag", addition.swapTag);
                EditorGUI.indentLevel--;
                EditorGUIUtility.labelWidth = labelWidth;
            }
        }

        // --- overlays ------------------------------------------------------------------------------

        private void DrawOverlays()
        {
            EditorGUILayout.Space(4f);
            showOverlays = EditorGUILayout.Foldout(showOverlays, "Overlays (optional)", true);
            if (!showOverlays)
            {
                return;
            }
            overlayList.DoLayoutList();
        }

        private void DrawOverlayRow(Rect rect, int index)
        {
            UMARecipeOverlayAddition row = addition.overlays[index];
            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;
            float colorWidth = Mathf.Min(150f, rect.width * 0.34f);
            float swatch = 54f;
            var assetRect = new Rect(rect.x, rect.y, rect.width - colorWidth - swatch - 8f, rect.height);
            var colorRect = new Rect(assetRect.xMax + 4f, rect.y, colorWidth, rect.height);
            var swatchRect = new Rect(colorRect.xMax + 4f, rect.y, swatch, rect.height);

            EditorGUI.BeginChangeCheck();
            row.overlay = (OverlayDataAsset)EditorGUI.ObjectField(assetRect, row.overlay, typeof(OverlayDataAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                libraryWarningsDirty = true;
            }

            // Its own color, or one of the shared colors listed below.
            var choices = new List<string> { OwnColor };
            for (int i = 0; i < addition.sharedColors.Count; i++)
            {
                string name = addition.sharedColors[i] != null ? addition.sharedColors[i].name : null;
                if (!string.IsNullOrWhiteSpace(name) && !choices.Contains(name.Trim()))
                {
                    choices.Add(name.Trim());
                }
            }
            int current = string.IsNullOrWhiteSpace(row.sharedColorName) ? 0 : choices.IndexOf(row.sharedColorName.Trim());
            if (current < 0)
            {
                // The shared color it followed was renamed or removed.
                row.sharedColorName = string.Empty;
                current = 0;
            }
            int chosen = EditorGUI.Popup(colorRect, current, choices.ToArray());
            if (chosen != current)
            {
                row.sharedColorName = chosen == 0 ? string.Empty : choices[chosen];
            }
            if (chosen == 0)
            {
                row.color = EditorGUI.ColorField(swatchRect, GUIContent.none, row.color, false, true, false);
            }
            else
            {
                EditorGUI.LabelField(swatchRect, "shared", EditorStyles.miniLabel);
            }
        }

        // --- shared colors -------------------------------------------------------------------------

        private void DrawSharedColors()
        {
            EditorGUILayout.Space(4f);
            showSharedColors = EditorGUILayout.Foldout(showSharedColors, "Shared colors (optional)", true);
            if (!showSharedColors)
            {
                return;
            }
            EditorGUILayout.LabelField(
                "Added to each recipe. A recipe that already has a shared color of the same name keeps its own, and the overlays above follow that one.",
                EditorStyles.wordWrappedMiniLabel);
            sharedColorList.DoLayoutList();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Quick add");
            for (int i = 0; i < QuickSharedColors.Length; i++)
            {
                string name = QuickSharedColors[i];
                using (new EditorGUI.DisabledScope(HasSharedColor(name)))
                {
                    if (GUILayout.Button(name))
                    {
                        addition.sharedColors.Add(new UMARecipeSharedColorAddition { name = name });
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSharedColorRow(Rect rect, int index)
        {
            UMARecipeSharedColorAddition row = addition.sharedColors[index];
            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;
            float swatch = 54f;
            string before = row.name;
            row.name = EditorGUI.TextField(new Rect(rect.x, rect.y, rect.width - swatch - 4f, rect.height), row.name);
            row.color = EditorGUI.ColorField(new Rect(rect.xMax - swatch, rect.y, swatch, rect.height), GUIContent.none, row.color, false, true, false);
            if (before != row.name && !string.IsNullOrWhiteSpace(before))
            {
                // Overlays that followed the old name follow the new one.
                for (int i = 0; i < addition.overlays.Count; i++)
                {
                    if (addition.overlays[i] != null && addition.overlays[i].sharedColorName == before.Trim())
                    {
                        addition.overlays[i].sharedColorName = string.IsNullOrWhiteSpace(row.name) ? string.Empty : row.name.Trim();
                    }
                }
            }
        }

        private bool HasSharedColor(string name)
        {
            for (int i = 0; i < addition.sharedColors.Count; i++)
            {
                if (addition.sharedColors[i] != null && addition.sharedColors[i].name == name)
                {
                    return true;
                }
            }
            return false;
        }

        // --- adding --------------------------------------------------------------------------------

        private void DrawApply()
        {
            EditorGUILayout.Space(6f);
            bool valid = UMARecipeSlotAdder.Validate(addition, out string problem);
            if (!valid)
            {
                EditorGUILayout.HelpBox(problem, addition.slot == null ? MessageType.Info : MessageType.Error);
            }
            else
            {
                List<string> warnings = LibraryWarnings();
                if (warnings.Count > 0)
                {
                    EditorGUILayout.HelpBox(string.Join("\n", warnings), MessageType.Warning);
                }
            }
            if (!string.IsNullOrEmpty(lastResult))
            {
                string text = lastResult;
                for (int i = 0; i < lastNotes.Count && i < 8; i++)
                {
                    text += "\n" + lastNotes[i];
                }
                if (lastNotes.Count > 8)
                {
                    text += $"\n... and {lastNotes.Count - 8} more (see the Console).";
                }
                EditorGUILayout.HelpBox(text, MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!valid || targets.Count == 0))
            {
                string label = targets.Count == 1 ? "Add to 1 recipe" : $"Add to {targets.Count} recipes";
                if (GUILayout.Button(new GUIContent(label, "Adds the slot to every selected recipe that does not have it yet, as one undo step, and saves those recipes."), GUILayout.Height(28f)))
                {
                    Apply();
                    GUIUtility.ExitGUI();
                }
            }
            if (GUILayout.Button("Close", GUILayout.Width(80f), GUILayout.Height(28f)))
            {
                Close();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void Apply()
        {
            UMARecipeSlotAddSummary summary = UMARecipeSlotAdder.AddToRecipes(targets, addition);
            lastNotes.Clear();
            lastNotes.AddRange(summary.Notes);
            lastResult = $"Added '{addition.slot.slotName}' to {summary.Added} recipe(s).";
            if (summary.AlreadyHadSlot > 0)
            {
                lastResult += $" {summary.AlreadyHadSlot} already had it and were left alone.";
            }
            if (summary.NotSupported > 0)
            {
                lastResult += $" {summary.NotSupported} could not take it.";
            }
            if (summary.Added > 0)
            {
                lastResult += " This can be undone.";
            }
            string log = "[UMA] " + lastResult;
            for (int i = 0; i < summary.Notes.Count; i++)
            {
                log += "\n  " + summary.Notes[i];
            }
            Debug.Log(log);
            Repaint();
        }

        /// <summary>
        /// A recipe names its slots and overlays; it finds them through the Global Library. One that is
        /// not there, or overlays made for different materials, is worth knowing before adding.
        /// </summary>
        private List<string> LibraryWarnings()
        {
            if (!libraryWarningsDirty && libraryCheckedFor == addition.slot)
            {
                return libraryWarnings;
            }
            libraryWarnings.Clear();
            libraryWarningsDirty = false;
            libraryCheckedFor = addition.slot;
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null || addition.slot == null)
            {
                return libraryWarnings;
            }
            if (!indexer.HasAsset<SlotDataAsset>(addition.slot.slotName))
            {
                libraryWarnings.Add($"The slot '{addition.slot.slotName}' is not in the Global Library. Recipes will not find it until it is added there.");
            }
            OverlayDataAsset first = null;
            for (int i = 0; i < addition.overlays.Count; i++)
            {
                OverlayDataAsset overlay = addition.overlays[i] != null ? addition.overlays[i].overlay : null;
                if (overlay == null)
                {
                    continue;
                }
                if (!indexer.HasAsset<OverlayDataAsset>(overlay.overlayName))
                {
                    libraryWarnings.Add($"The overlay '{overlay.overlayName}' is not in the Global Library. Recipes will not find it until it is added there.");
                }
                // The first overlay sets the slot's material; UMA rejects a later one made for another.
                if (first == null)
                {
                    first = overlay;
                }
                else if (first.material != null && overlay.material != null && !first.material.Equals(overlay.material))
                {
                    libraryWarnings.Add($"The overlay '{overlay.overlayName}' uses the material '{overlay.material.name}', the first overlay uses '{first.material.name}'. UMA rejects an overlay whose material does not match the slot's first overlay.");
                }
            }
            return libraryWarnings;
        }

        // --- lists ---------------------------------------------------------------------------------

        private void EnsureLists()
        {
            if (tagList == null || tagList.list != addition.tags)
            {
                tagList = StringList(addition.tags, "No tags");
            }
            if (raceList == null || raceList.list != addition.races)
            {
                raceList = StringList(addition.races, "Every race");
            }
            if (overlayList == null || overlayList.list != addition.overlays)
            {
                overlayList = new ReorderableList(addition.overlays, typeof(UMARecipeOverlayAddition), true, true, true, true)
                {
                    drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Overlay, and the color it takes"),
                    drawElementCallback = (rect, index, active, focused) => DrawOverlayRow(rect, index),
                    onAddCallback = list => addition.overlays.Add(new UMARecipeOverlayAddition()),
                    onChangedCallback = list => libraryWarningsDirty = true,
                    elementHeight = EditorGUIUtility.singleLineHeight + 4f,
                };
            }
            if (sharedColorList == null || sharedColorList.list != addition.sharedColors)
            {
                sharedColorList = new ReorderableList(addition.sharedColors, typeof(UMARecipeSharedColorAddition), true, true, true, true)
                {
                    drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Name, and its starting color"),
                    drawElementCallback = (rect, index, active, focused) => DrawSharedColorRow(rect, index),
                    onAddCallback = list => addition.sharedColors.Add(new UMARecipeSharedColorAddition { name = "Shared Color " + (addition.sharedColors.Count + 1) }),
                    elementHeight = EditorGUIUtility.singleLineHeight + 4f,
                };
            }
        }

        /// <summary>A list of names that can be reordered and removed from; adding is done by the controls under it.</summary>
        private static ReorderableList StringList(List<string> values, string whenEmpty)
        {
            return new ReorderableList(values, typeof(string), true, false, false, true)
            {
                headerHeight = 2f,
                drawElementCallback = (rect, index, active, focused) =>
                {
                    rect.y += 1f;
                    rect.height = EditorGUIUtility.singleLineHeight;
                    values[index] = EditorGUI.TextField(rect, values[index]);
                },
                drawNoneElementCallback = rect => EditorGUI.LabelField(rect, whenEmpty, EditorStyles.miniLabel),
                elementHeight = EditorGUIUtility.singleLineHeight + 2f,
            };
        }

        private string[] RaceNames()
        {
            if (raceNames == null)
            {
                var names = new List<string>();
                UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
                if (indexer != null)
                {
                    RaceData[] races = indexer.GetAllRaces();
                    if (races != null)
                    {
                        for (int i = 0; i < races.Length; i++)
                        {
                            if (races[i] != null && !string.IsNullOrEmpty(races[i].raceName) && !names.Contains(races[i].raceName))
                            {
                                names.Add(races[i].raceName);
                            }
                        }
                    }
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
                raceNames = names.ToArray();
            }
            return raceNames;
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value))
            {
                values.Add(value);
            }
        }
    }
}
