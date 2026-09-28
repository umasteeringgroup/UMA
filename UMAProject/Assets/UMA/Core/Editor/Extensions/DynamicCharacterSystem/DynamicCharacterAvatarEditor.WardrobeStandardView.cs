using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.CharacterSystem.Editors
{
    public partial class DynamicCharacterAvatarEditor
    {
        private const float WardrobeCardHeight = 72f;
        private const float WardrobeIconSize = 64f;
        private static GUIStyle wardrobeRegionStyle;
        private static GUIStyle wardrobeItemStyle;
        private bool standardWardrobeExpanded = true;
        private int standardWardrobeFilter;
        private static readonly string[] StandardWardrobeFilters = { "All", "Assigned only" };

        private static GUIStyle WardrobeRegionStyle => wardrobeRegionStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            alignment = TextAnchor.UpperLeft
        };

        private static GUIStyle WardrobeItemStyle => wardrobeItemStyle ??= new GUIStyle(EditorStyles.label)
        {
            fontSize = 12,
            wordWrap = false,
            clipping = TextClipping.Clip
        };

        private void DrawStandardDefaultWardrobe()
        {
            using (inspectorView.Section("Default wardrobe",
                "Each Wardrobe Region shows its currently equipped default recipe. Click a row to choose one compatible wardrobe recipe for that region. Choose None to remove the active default. Choosing a recipe makes it the single active default for that region; the existing Advanced View retains all legacy and append controls."))
            {
                standardWardrobeExpanded = EditorGUILayout.Foldout(standardWardrobeExpanded, "Wardrobe items", true);
                if (!standardWardrobeExpanded) return;

                bool changed = GUI.changed;
                standardWardrobeFilter = EditorGUILayout.Popup("Show regions", standardWardrobeFilter, StandardWardrobeFilters);
                GUI.changed = changed; // Filtering changes the view, not the avatar.

                RaceData race = thisDCA != null && thisDCA.activeRace != null
                    ? thisDCA.activeRace.data
                    : null;
                if (race == null)
                {
                    EditorGUILayout.HelpBox("Choose an Active Race before selecting default wardrobe recipes.", MessageType.Info);
                    return;
                }

                SerializedProperty recipes = serializedObject.FindProperty("preloadWardrobeRecipes")
                    ?.FindPropertyRelative("recipes");
                if (recipes == null)
                {
                    EditorGUILayout.HelpBox("Default wardrobe storage is unavailable while Unity reloads the avatar.", MessageType.Info);
                    return;
                }

                IList<string> regions = race.wardrobeSlots;
                if (regions == null || regions.Count == 0)
                {
                    EditorGUILayout.HelpBox("This race has no Wardrobe Regions.", MessageType.Info);
                    return;
                }

                int visibleRegions = 0;
                var seenRegions = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < regions.Count; i++)
                {
                    string region = regions[i];
                    if (string.IsNullOrWhiteSpace(region) || !seenRegions.Add(region)) continue;
                    List<UMATextRecipe> choices = GetWardrobeChoices(region);
                    if (choices.Count == 0) continue;
                    DefaultWardrobeEntry equipped = FindEquippedDefaultWardrobe(recipes, region);
                    if (standardWardrobeFilter == 1 && equipped.recipe == null) continue;
                    visibleRegions++;
                    Rect row = GUILayoutUtility.GetRect(0f, WardrobeCardHeight, GUILayout.ExpandWidth(true));
                    if (DrawWardrobeCard(row, region, equipped.recipe, equipped.name, race.raceName))
                    {
                        PopupWindow.Show(row, new DefaultWardrobeRegionPicker(
                            region,
                            choices,
                            equipped.recipe != null ? equipped.recipe.name : equipped.name,
                            race.raceName,
                            selected => SetDefaultWardrobeRecipe(region, selected)));
                        GUIUtility.ExitGUI();
                    }
                }
                if (standardWardrobeFilter == 1 && visibleRegions == 0)
                    EditorGUILayout.LabelField("No assigned wardrobe regions. Choose All to assign items.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        private List<UMATextRecipe> GetWardrobeChoices(string region)
        {
            var result = new List<UMATextRecipe>();
            if (thisDCA == null || string.IsNullOrEmpty(region)) return result;
            Dictionary<string, List<UMATextRecipe>> available = thisDCA.AvailableRecipes;
            if (available == null || !available.TryGetValue(region, out List<UMATextRecipe> source) || source == null)
                return result;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Count; i++)
            {
                UMATextRecipe recipe = source[i];
                if (recipe == null || recipe.Appended ||
                    !string.Equals(recipe.wardrobeSlot, region, StringComparison.Ordinal) ||
                    !seen.Add(recipe.name)) continue;
                result.Add(recipe);
            }
            result.Sort((left, right) => string.Compare(RecipeDisplayName(left), RecipeDisplayName(right), StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private DefaultWardrobeEntry FindEquippedDefaultWardrobe(SerializedProperty recipes, string region)
        {
            if (recipes == null) return default;
            for (int i = 0; i < recipes.arraySize; i++)
            {
                SerializedProperty entry = recipes.GetArrayElementAtIndex(i);
                if (entry == null || !entry.FindPropertyRelative("_enabledInDefaultWardrobe").boolValue) continue;
                string name = entry.FindPropertyRelative("_recipeName").stringValue;
                UMATextRecipe recipe = ResolveRecipe(name);
                if (recipe != null && !recipe.Appended && string.Equals(recipe.wardrobeSlot, region, StringComparison.Ordinal))
                    return new DefaultWardrobeEntry(recipe, name);
            }
            return default;
        }

        private void SetDefaultWardrobeRecipe(string region, UMATextRecipe selected)
        {
            if (string.IsNullOrEmpty(region)) return;
            serializedObject.UpdateIfRequiredOrScript();
            SerializedProperty wardrobe = serializedObject.FindProperty("preloadWardrobeRecipes");
            SerializedProperty recipes = wardrobe?.FindPropertyRelative("recipes");
            if (recipes == null) return;

            Undo.RecordObject(thisDCA, "Set default wardrobe " + region);
            int selectedIndex = -1;
            for (int i = 0; i < recipes.arraySize; i++)
            {
                SerializedProperty entry = recipes.GetArrayElementAtIndex(i);
                if (entry == null) continue;
                string recipeName = entry.FindPropertyRelative("_recipeName").stringValue;
                UMATextRecipe existing = ResolveRecipe(recipeName);
                bool sameRegion = (selected != null && string.Equals(recipeName, selected.name, StringComparison.Ordinal)) ||
                    (existing != null && !existing.Appended &&
                     string.Equals(existing.wardrobeSlot, region, StringComparison.Ordinal));
                if (!sameRegion) continue;

                bool isSelected = selected != null && selectedIndex < 0 && string.Equals(recipeName, selected.name, StringComparison.Ordinal);
                entry.FindPropertyRelative("_enabledInDefaultWardrobe").boolValue = isSelected;
                if (isSelected)
                {
                    selectedIndex = i;
                    SyncDefaultWardrobeRecipe(entry, selected);
                }
            }

            if (selected != null && selectedIndex < 0)
            {
                selectedIndex = recipes.arraySize;
                recipes.InsertArrayElementAtIndex(selectedIndex);
                SerializedProperty entry = recipes.GetArrayElementAtIndex(selectedIndex);
                SyncDefaultWardrobeRecipe(entry, selected);
                entry.FindPropertyRelative("_enabledInDefaultWardrobe").boolValue = true;
                entry.FindPropertyRelative("ForceLoad").boolValue = false;
            }

            // Selecting a visible default is an explicit request for defaults to load at build time.
            if (selected != null) wardrobe.FindPropertyRelative("loadDefaultRecipes").boolValue = true;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(thisDCA);
            RefreshDefaultWardrobePreview();
        }

        private static void SyncDefaultWardrobeRecipe(SerializedProperty entry, UMATextRecipe recipe)
        {
            entry.FindPropertyRelative("_recipeName").stringValue = recipe.name;
            SerializedProperty compatibleRaces = entry.FindPropertyRelative("_compatibleRaces");
            int count = recipe.compatibleRaces != null ? recipe.compatibleRaces.Count : 0;
            compatibleRaces.arraySize = count;
            for (int i = 0; i < count; i++)
                compatibleRaces.GetArrayElementAtIndex(i).stringValue = recipe.compatibleRaces[i];
        }

        private void RefreshDefaultWardrobePreview()
        {
            if (Application.isPlaying)
            {
                thisDCA.ClearSlots();
                thisDCA.LoadDefaultWardrobe();
                thisDCA.BuildCharacter(false);
            }
            else
            {
                GenerateSingleUMA();
            }
            Repaint();
        }

        private static UMATextRecipe ResolveRecipe(string name)
        {
            if (string.IsNullOrEmpty(name) || UMAAssetIndexer.Instance == null) return null;
            try { return UMAAssetIndexer.Instance.GetRecipe(name, false); }
            catch { return null; }
        }

        private static string RecipeDisplayName(UMATextRecipe recipe)
        {
            if (recipe == null) return string.Empty;
            return string.IsNullOrWhiteSpace(recipe.DisplayValue) ? recipe.name : recipe.DisplayValue;
        }

        private static bool DrawWardrobeCard(Rect row, string region, UMATextRecipe recipe, string serializedName,
            string raceName, bool selected = false)
        {
            EditorGUI.DrawRect(row, selected
                ? new Color(0.38f, 0.68f, 1f, 1f)
                : new Color(0.26f, 0.53f, 0.82f, 0.95f));
            Rect background = new Rect(row.x + 1f, row.y + 1f, row.width - 2f, row.height - 2f);
            EditorGUI.DrawRect(background, new Color(0.24f, 0.24f, 0.24f, 1f));

            Rect iconFrame = new Rect(row.x + 5f, row.y + 4f, WardrobeIconSize, WardrobeIconSize);
            EditorGUI.DrawRect(iconFrame, new Color(0.15f, 0.49f, 0.85f, 1f));
            Rect iconRect = new Rect(iconFrame.x + 2f, iconFrame.y + 2f, iconFrame.width - 4f, iconFrame.height - 4f);
            DrawWardrobeIcon(iconRect, recipe, raceName);

            Rect textRect = new Rect(iconFrame.xMax + 10f, row.y + 7f,
                Mathf.Max(0f, row.xMax - iconFrame.xMax - 16f), 22f);
            GUI.Label(textRect, "Region: " + region, WardrobeRegionStyle);
            textRect.y += 27f;
            string item = recipe != null ? RecipeDisplayName(recipe) : serializedName;
            GUI.Label(textRect, string.IsNullOrWhiteSpace(item) ? "Unequipped, Click to select" : "Item: " + item,
                WardrobeItemStyle);
            return GUI.Button(row, GUIContent.none, GUIStyle.none);
        }

        private static void DrawWardrobeIcon(Rect rect, UMATextRecipe recipe, string raceName)
        {
            if (recipe == null)
            {
                EditorGUI.DrawRect(rect, new Color(0.24f, 0.24f, 0.24f, 1f));
                return;
            }
            Sprite sprite = recipe != null ? recipe.GetWardrobeRecipeThumbFor(raceName) : null;
            if (sprite != null && sprite.texture != null)
            {
                Rect source = sprite.rect;
                Texture2D texture = sprite.texture;
                Rect uv = new Rect(source.x / texture.width, source.y / texture.height,
                    source.width / texture.width, source.height / texture.height);
                GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
                return;
            }

            Texture icon = recipe != null ? AssetPreview.GetMiniThumbnail(recipe) : null;
            icon ??= EditorGUIUtility.IconContent("d_Prefab Icon").image;
            if (icon != null) GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, true);
        }

        private readonly struct DefaultWardrobeEntry
        {
            public readonly UMATextRecipe recipe;
            public readonly string name;

            public DefaultWardrobeEntry(UMATextRecipe recipe, string name)
            {
                this.recipe = recipe;
                this.name = name;
            }
        }

        private sealed class DefaultWardrobeRegionPicker : StandardPopupContent
        {
            protected override bool HasCancelFooter => true;
            private readonly string region;
            private readonly List<UMATextRecipe> choices;
            private readonly string selectedName;
            private readonly string raceName;
            private readonly Action<UMATextRecipe> onSelect;
            private Vector2 scroll;

            public DefaultWardrobeRegionPicker(string region, List<UMATextRecipe> choices,
                string selectedName, string raceName, Action<UMATextRecipe> onSelect)
            {
                this.region = region;
                this.choices = choices ?? new List<UMATextRecipe>();
                this.selectedName = selectedName;
                this.raceName = raceName;
                this.onSelect = onSelect;
            }

            protected override Vector2 GetContentSize() => new Vector2(440f, 430f);

            protected override void DrawPopupContent(Rect rect)
            {
                EditorGUILayout.LabelField("Select default " + region, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Choose one recipe. This replaces the active default for this region.",
                    EditorStyles.wordWrappedMiniLabel);
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
                Rect noneRow = GUILayoutUtility.GetRect(0f, WardrobeCardHeight, GUILayout.ExpandWidth(true));
                if (DrawWardrobeCard(noneRow, region, null, "None", raceName, string.IsNullOrEmpty(selectedName)))
                {
                    onSelect?.Invoke(null);
                    editorWindow.Close();
                    GUIUtility.ExitGUI();
                }
                if (choices.Count == 0)
                {
                    EditorGUILayout.HelpBox("No compatible wardrobe recipes are available for this region.", MessageType.Info);
                }
                else
                {
                    for (int i = 0; i < choices.Count; i++)
                    {
                        UMATextRecipe recipe = choices[i];
                        Rect row = GUILayoutUtility.GetRect(0f, WardrobeCardHeight, GUILayout.ExpandWidth(true));
                        bool isSelected = recipe != null && string.Equals(recipe.name, selectedName, StringComparison.Ordinal);
                        if (DrawWardrobeCard(row, region, recipe, recipe != null ? recipe.name : string.Empty,
                                raceName, isSelected))
                        {
                            onSelect?.Invoke(recipe);
                            editorWindow.Close();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }
    }
}
