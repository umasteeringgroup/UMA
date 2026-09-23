using System;
using System.Collections.Generic;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.CharacterSystem.Editors
{
    public partial class DynamicCharacterAvatarEditor
    {
        private readonly UMAInspectorView inspectorView = new UMAInspectorView(typeof(DynamicCharacterAvatar));
        private UMAPreset selectedPreset;

        private bool showAllStandardColors;

        private void DrawStandardColors()
        {
            bool guiChanged = GUI.changed;
            showAllStandardColors = EditorGUILayout.Toggle("Show all colors", showAllStandardColors);
            GUI.changed = guiChanged;
            var colors = thisDCA.characterColors?._colors;
            if (colors == null || colors.Count == 0)
            {
                EditorGUILayout.LabelField("No shared colors are defined.", EditorStyles.miniLabel);
                return;
            }
            int visibleCount = 0;
            for (int index = 0; index < colors.Count; index++)
            {
                var current = colors[index];
                if (current == null || (!showAllStandardColors && !current.isBaseColor)) continue;
                visibleCount++;
                var table = OverlayColorDataPropertyDrawer.FindStandardColorTable(current.name);
                var choices = new List<OverlayColorData>();
                string selectedLabel = "Custom";
                if (table != null && table.colors != null)
                {
                    foreach (var color in table.colors)
                    {
                        if (color == null) continue;
                        choices.Add(color);
                        if (SamePaletteColor(current, color)) selectedLabel = PaletteColorLabel(color, choices.Count);
                    }
                }
                if (choices.Count == 0) selectedLabel = "No palette colors";
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(current.name, GUILayout.Width(EditorGUIUtility.labelWidth));
                    using (new EditorGUI.DisabledScope(choices.Count == 0))
                    {
                        Rect dropdown = GUILayoutUtility.GetRect(60, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                        bool open = EditorGUI.DropdownButton(dropdown, GUIContent.none, FocusType.Keyboard);
                        DrawPaletteColorLabel(dropdown, current, selectedLabel);
                        if (open)
                        {
                            serializedObject.ApplyModifiedProperties();
                            PopupWindow.Show(dropdown, new SharedColorPicker(choices, current, dropdown.width, palette =>
                            {
                                if (this == null || thisDCA == null) return;
                                var currentColors = thisDCA.characterColors._colors;
                                int colorIndex = currentColors.FindIndex(entry => ReferenceEquals(entry, current));
                                if (colorIndex < 0) return;
                                serializedObject.ApplyModifiedProperties();
                                Undo.RecordObject(thisDCA, "Select shared color");
                                currentColors[colorIndex] = CopyPaletteColor(current, palette);
                                CommitStandardColorEdit();
                            }));
                            GUIUtility.ExitGUI();
                        }
                    }
                    Rect editRect = GUILayoutUtility.GetRect(new GUIContent("Edit"), GUI.skin.button, GUILayout.Width(48));
                    if (GUI.Button(editRect, "Edit"))
                    {
                        serializedObject.ApplyModifiedProperties();
                        PopupWindow.Show(editRect, new SharedColorEditorPopup(this, current));
                        GUIUtility.ExitGUI();
                    }
                    if (!current.isBaseColor)
                    {
                        if (GUILayout.Button(new GUIContent("x", "Delete shared color"), GUILayout.Width(22)))
                        {
                            serializedObject.ApplyModifiedProperties();
                            Undo.RecordObject(thisDCA, "Delete shared color");
                            colors.RemoveAt(index);
                            CommitStandardColorEdit();
                            GUIUtility.ExitGUI();
                        }
                    }
                    else GUILayout.Space(26);
                }
            }
            if (visibleCount == 0)
                EditorGUILayout.LabelField("No base colors. Enable Show all colors to see other colors.", EditorStyles.wordWrappedMiniLabel);
        }

        private static string PaletteColorLabel(OverlayColorData color, int index) =>
            string.IsNullOrEmpty(color.name) ? "Color " + index : color.name;

        private static void DrawPaletteColorLabel(Rect row, OverlayColorData color, string label)
        {
            Color swatch = color.showDisplayColor ? color.displayColor :
                color.channelMask != null && color.channelMask.Length > 0 ? color.channelMask[0] : Color.white;
            Rect swatchRect = new Rect(row.x + 5, row.y + 3, 28, row.height - 6);
            EditorGUI.DrawRect(swatchRect, Color.black);
            swatchRect = new Rect(swatchRect.x + 1, swatchRect.y + 1, swatchRect.width - 2, swatchRect.height - 2);
            EditorGUI.DrawTextureTransparent(swatchRect, Texture2D.whiteTexture);
            EditorGUI.DrawRect(swatchRect, swatch);
            GUI.Label(new Rect(row.x + 39, row.y, Mathf.Max(0, row.width - 57), row.height), label);
        }

        private abstract class StandardPopupContent : PopupWindowContent
        {
            protected abstract Vector2 GetContentSize();
            protected abstract void DrawPopupContent(Rect rect);
            protected virtual bool HasCancelFooter => false;
            public sealed override Vector2 GetWindowSize() => GetContentSize();
#if UNITY_EDITOR_WIN
            private NativePopupShadow shadow;
            private bool shadowFailed;

            public override void OnOpen()
            {
                EditorApplication.update += UpdateShadow;
                AssemblyReloadEvents.beforeAssemblyReload += CloseShadow;
                EditorApplication.quitting += CloseShadow;
            }

            public override void OnClose()
            {
                EditorApplication.update -= UpdateShadow;
                AssemblyReloadEvents.beforeAssemblyReload -= CloseShadow;
                EditorApplication.quitting -= CloseShadow;
                CloseShadow();
            }

            private void CloseShadow() { shadow?.Dispose(); shadow = null; }

            private void UpdateShadow()
            {
                if (editorWindow == null) { CloseShadow(); return; }
                try { shadow?.Update(); }
                catch (Exception exception) { FailShadow(exception); }
            }

            private void FailShadow(Exception exception)
            {
                CloseShadow();
                shadowFailed = true;
                Debug.LogWarning("UMA popup shadow could not be displayed: " + exception.Message);
            }
#endif
            public sealed override void OnGUI(Rect rect)
            {
#if UNITY_EDITOR_WIN
                if (!shadowFailed && shadow == null && Event.current.type == EventType.Repaint && EditorWindow.focusedWindow == editorWindow)
                {
                    try
                    {
                        float scale = EditorGUIUtility.pixelsPerPoint;
                        shadow = NativePopupShadow.TryAttach(Mathf.RoundToInt(rect.width * scale), Mathf.RoundToInt(rect.height * scale));
                        shadow?.Update();
                    }
                    catch (Exception exception) { FailShadow(exception); }
                }
#endif
                if (!HasCancelFooter)
                {
                    DrawPopupContent(rect);
                    return;
                }

                const float footerHeight = 44f;
                Rect contentRect = new Rect(rect.x, rect.y, rect.width, Mathf.Max(0f, rect.height - footerHeight));
                Rect footerRect = new Rect(rect.x, contentRect.yMax, rect.width, footerHeight);
                GUILayout.BeginArea(contentRect);
                try { DrawPopupContent(new Rect(0f, 0f, contentRect.width, contentRect.height)); }
                finally { GUILayout.EndArea(); }

                // Separate the fixed action area from the clipped, scrolling list.
                EditorGUI.DrawRect(footerRect, EditorGUIUtility.isProSkin
                    ? new Color(0.18f, 0.18f, 0.18f, 1f)
                    : new Color(0.70f, 0.70f, 0.70f, 1f));
                EditorGUI.DrawRect(new Rect(footerRect.x, footerRect.y, footerRect.width, 1f),
                    new Color(0f, 0f, 0f, 0.35f));
                float buttonHeight = EditorGUIUtility.singleLineHeight + 4f;
                Rect cancelRect = new Rect(footerRect.xMax - 110f,
                    footerRect.y + (footerRect.height - buttonHeight) * 0.5f, 100f, buttonHeight);
                if (GUI.Button(cancelRect, "Cancel")) editorWindow.Close();
            }
        }

        private sealed class SharedColorPicker : StandardPopupContent
        {
            private readonly List<OverlayColorData> choices;
            private readonly OverlayColorData current;
            private readonly float width;
            private readonly Action<OverlayColorData> select;
            private Vector2 scroll;

            internal SharedColorPicker(List<OverlayColorData> choices, OverlayColorData current,
                float width, Action<OverlayColorData> select)
            {
                this.choices = choices;
                this.current = current;
                this.width = width;
                this.select = select;
            }

            protected override Vector2 GetContentSize() => new Vector2(Mathf.Max(220, width), Mathf.Min(360, choices.Count * 24 + 8));

            protected override void DrawPopupContent(Rect rect)
            {
                using (var view = new EditorGUILayout.ScrollViewScope(scroll))
                {
                    scroll = view.scrollPosition;
                    for (int i = 0; i < choices.Count; i++)
                    {
                        var color = choices[i];
                        Rect row = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
                        bool clicked = GUI.Button(row, GUIContent.none, SamePaletteColor(current, color) ? EditorStyles.helpBox : GUIStyle.none);
                        DrawPaletteColorLabel(row, color, PaletteColorLabel(color, i + 1));
                        if (clicked)
                        {
                            editorWindow.Close();
                            select(color);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }

        internal static DynamicCharacterAvatar.ColorValue CopyPaletteColor(OverlayColorData current, OverlayColorData palette)
        {
            var copy = new DynamicCharacterAvatar.ColorValue(palette);
            copy.EnsureChannelsExact(palette.channelCount);
            copy.showDisplayColor = palette.showDisplayColor;
            copy.name = current.name;
            copy.isBaseColor = current.isBaseColor;
            return copy;
        }

        private static bool SamePaletteColor(OverlayColorData current, OverlayColorData palette)
        {
            if (current.showDisplayColor != palette.showDisplayColor || current.displayColor != palette.displayColor ||
                current.channelMask.Length != palette.channelMask.Length ||
                current.channelAdditiveMask.Length != palette.channelAdditiveMask.Length) return false;
            for (int i = 0; i < current.channelMask.Length; i++)
                if (current.channelMask[i] != palette.channelMask[i]) return false;
            for (int i = 0; i < current.channelAdditiveMask.Length; i++)
                if (current.channelAdditiveMask[i] != palette.channelAdditiveMask[i]) return false;
            return JsonUtility.ToJson(current.PropertyBlock) == JsonUtility.ToJson(palette.PropertyBlock);
        }

        private void CommitStandardColorEdit()
        {
            EditorUtility.SetDirty(thisDCA);
            PrefabUtility.RecordPrefabInstancePropertyModifications(thisDCA);
            if (Application.isPlaying) thisDCA.UpdateColors(true);
            else GenerateSingleUMA();
            serializedObject.Update();
            Repaint();
        }

        private sealed class SharedColorEditorPopup : StandardPopupContent
        {
            private readonly DynamicCharacterAvatarEditor owner;
            private readonly OverlayColorData color;
            private readonly OverlayColorDataPropertyDrawer drawer = new OverlayColorDataPropertyDrawer();
            private Vector2 scroll;
            private bool initialized;

            internal SharedColorEditorPopup(DynamicCharacterAvatarEditor owner, OverlayColorData color)
            {
                this.owner = owner;
                this.color = color;
            }

            protected override Vector2 GetContentSize() => new Vector2(560, 580);

            protected override void DrawPopupContent(Rect rect)
            {
                if (owner == null || owner.thisDCA == null) { editorWindow.Close(); return; }
                var colors = owner.thisDCA.characterColors._colors;
                int index = colors.FindIndex(entry => ReferenceEquals(entry, color));
                if (index < 0) { editorWindow.Close(); return; }
                var serialized = owner.serializedObject;
                serialized.Update();
                var property = serialized.FindProperty("characterColors._colors").GetArrayElementAtIndex(index);
                if (!initialized)
                {
                    property.FindPropertyRelative("name").isExpanded = true;
                    initialized = true;
                }
                EditorGUILayout.LabelField("Edit shared color", EditorStyles.boldLabel);
                Undo.RecordObject(owner.thisDCA, "Edit shared color");
                EditorGUI.BeginChangeCheck();
                using (var view = new EditorGUILayout.ScrollViewScope(scroll))
                using (new OverlayColorDataPropertyDrawer.DeferredApplyScope(true))
                {
                    scroll = view.scrollPosition;
                    drawer.OnGUI(Rect.zero, property, new GUIContent(color.name));
                }
                bool changed = EditorGUI.EndChangeCheck();
                // The drawer also edits channel counts and material properties directly.
                bool applied = serialized.ApplyModifiedProperties();
                if (changed || applied)
                {
                    owner.thisDCA.characterColors.RemoveDeletedItems();
                    owner.CommitStandardColorEdit();
                }
                if (GUILayout.Button("Done")) editorWindow.Close();
            }
        }

        private UMARandomAvatar selectedRandomAvatar;
        private bool randomizeRace;
        private bool randomizeDNA = true;
        private bool randomizeClothing = true;
        private bool randomizeColors = true;
        private string randomizationMessage;

        private void DrawStandardRandomization()
        {
            using (inspectorView.Section("Randomize",
                "Choose a UMARandomAvatar controller from the scene or a prefab. Only checked categories are randomized; the source controller and its other avatars are unchanged. Race allows a new race, otherwise selections match the current race. Clothing is saved as this avatar's default wardrobe. Colors also randomizes colors of currently equipped items when Clothing is off."))
            {
                bool changed = GUI.changed;
                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect sourceRect = GUILayoutUtility.GetRect(100, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                    if (EditorGUI.DropdownButton(sourceRect, new GUIContent(selectedRandomAvatar != null ? selectedRandomAvatar.name : "Select UMARandomAvatar"), FocusType.Keyboard))
                    {
                        ShowRandomAvatarMenu(sourceRect);
                        GUIUtility.ExitGUI();
                    }
                    using (new EditorGUI.DisabledScope(selectedRandomAvatar == null ||
                        !(randomizeRace || randomizeDNA || randomizeClothing || randomizeColors)))
                    {
                        if (GUILayout.Button("Randomize", GUILayout.Width(90)))
                        {
                            ApplyStandardRandomization();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    randomizeRace = GUILayout.Toggle(randomizeRace, "Race");
                    randomizeDNA = GUILayout.Toggle(randomizeDNA, "DNA");
                    randomizeClothing = GUILayout.Toggle(randomizeClothing, "Clothing");
                    randomizeColors = GUILayout.Toggle(randomizeColors, "Colors");
                }
                GUI.changed = changed;
                if (!string.IsNullOrEmpty(randomizationMessage)) EditorGUILayout.HelpBox(randomizationMessage, MessageType.Info);
            }
        }

        private void ShowRandomAvatarMenu(Rect rect)
        {
            var menu = new GenericMenu { allowDuplicateNames = true };
            menu.AddItem(new GUIContent("None"), selectedRandomAvatar == null, () => { selectedRandomAvatar = null; Repaint(); });
            int count = 0;
            foreach (var controller in UnityEngine.Object.FindObjectsByType<UMARandomAvatar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (EditorUtility.IsPersistent(controller) || !controller.gameObject.scene.IsValid()) continue;
                AddRandomAvatarMenuItem(menu, controller);
                count++;
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                foreach (var controller in prefab.GetComponentsInChildren<UMARandomAvatar>(true))
                {
                    AddRandomAvatarMenuItem(menu, controller);
                    count++;
                }
            }
            if (count == 0) menu.AddDisabledItem(new GUIContent("No UMARandomAvatar controllers found"));
            menu.DropDown(rect);
        }

        private void AddRandomAvatarMenuItem(GenericMenu menu, UMARandomAvatar controller)
        {
            // GenericMenu interprets '/' as a submenu separator, even inside an object name.
            string label = controller.name.Replace('/', '\u2215');
            menu.AddItem(new GUIContent(label), selectedRandomAvatar == controller, () =>
            {
                if (this == null) return;
                selectedRandomAvatar = controller;
                randomizationMessage = null;
                Repaint();
            });
        }

        private void ApplyStandardRandomization()
        {
            if (selectedRandomAvatar == null || thisDCA == null) return;
            serializedObject.ApplyModifiedProperties();
            Undo.RegisterCompleteObjectUndo(thisDCA, "Randomize avatar");
            Undo.undoRedoPerformed -= RefreshAfterRandomizationUndo;
            Undo.undoRedoPerformed += RefreshAfterRandomizationUndo;
            try
            {
                if (!Application.isPlaying && (randomizeClothing || randomizeColors))
                {
                    // Preview slots may have been discarded while editor generation was disabled.
                    thisDCA.ClearSlots();
                    thisDCA.WardrobeCollections.Clear();
                    thisDCA.LoadDefaultWardrobe();
                }
                if (!selectedRandomAvatar.RandomizeSelective(thisDCA, randomizeRace, randomizeDNA, randomizeClothing, randomizeColors))
                {
                    randomizationMessage = "No matching randomizer definition was found. Check the controller's sources and the avatar's race.";
                    return;
                }
                if (randomizeClothing) SaveRandomizedDefaultWardrobe(thisDCA);
                // The modern DNA collection is serialized directly; legacy DNA is consumed by builds.
                var pendingDNA = thisDCA.activeRace.data != null && thisDCA.activeRace.data.useNewDNA
                    ? null : thisDCA.predefinedDNA?.Clone();
                try
                {
                    if (Application.isPlaying) thisDCA.BuildCharacter(!randomizeDNA);
                    else if (thisDCA.editorTimeGeneration) GenerateSingleUMA(randomizeRace);
                }
                finally
                {
                    if (!Application.isPlaying && pendingDNA != null) thisDCA.predefinedDNA = pendingDNA;
                }
                randomizationMessage = null;
                EditorUtility.SetDirty(thisDCA);
                PrefabUtility.RecordPrefabInstancePropertyModifications(thisDCA);
            }
            catch (ExitGUIException) { throw; }
            catch (Exception exception)
            {
                randomizationMessage = "Randomization could not complete. See the Console for details.";
                Debug.LogException(exception, thisDCA);
            }
            finally
            {
                _hasInspectorLayout = false;
                _serializedRefresh.Invalidate();
                serializedObject.Update();
                Repaint();
                SceneView.RepaintAll();
            }
        }

        private void RefreshAfterRandomizationUndo()
        {
            EditorApplication.delayCall -= RebuildRandomizationPreview;
            EditorApplication.delayCall += RebuildRandomizationPreview;
        }

        private void RebuildRandomizationPreview()
        {
            if (this == null || thisDCA == null || IsEditorBusy()) return;
            _hasInspectorLayout = false;
            serializedObject.Update();
            if (!Application.isPlaying && thisDCA.editorTimeGeneration) GenerateSingleUMA(true);
            Repaint();
            SceneView.RepaintAll();
        }

        private void StopRandomizationPreviewCallbacks()
        {
            Undo.undoRedoPerformed -= RefreshAfterRandomizationUndo;
            EditorApplication.delayCall -= RebuildRandomizationPreview;
        }

        internal static void SaveRandomizedDefaultWardrobe(DynamicCharacterAvatar avatar)
        {
            avatar.preloadWardrobeRecipes ??= new DynamicCharacterAvatar.WardrobeRecipeList();
            avatar.preloadWardrobeRecipes.loadDefaultRecipes = true;
            avatar.preloadWardrobeRecipes.recipes.Clear();
            var seen = new HashSet<UMATextRecipe>();
            void Add(UMATextRecipe recipe)
            {
                if (recipe != null && seen.Add(recipe))
                    avatar.preloadWardrobeRecipes.recipes.Add(new DynamicCharacterAvatar.WardrobeRecipeListItem(recipe));
            }
            foreach (var recipe in avatar.WardrobeRecipes.Values) Add(recipe);
            foreach (var recipes in avatar.AdditiveRecipes.Values)
                if (recipes != null) foreach (var recipe in recipes) Add(recipe);
            // Save the effective outfit rather than reapplying collections over newly selected slots.
            avatar.WardrobeCollections.Clear();
        }

        private void DrawStandardAvatarOptions()
        {
            DrawStandardRandomization();
            using (inspectorView.Section("Presets", "Create a reusable preset from selected DNA, colors and wardrobe, with a face icon captured in Scene View. Apply changes the avatar to the preset race when needed, replaces its wardrobe, then applies the included DNA and colors. Edit opens the selected preset using this avatar's current values."))
            {
                selectedPreset = (UMAPreset)EditorGUILayout.ObjectField("Preset", selectedPreset, typeof(UMAPreset), false);
                if (selectedPreset != null && selectedPreset.Icon != null)
                    GUILayout.Label(selectedPreset.Icon, GUILayout.Width(64), GUILayout.Height(64));
                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect selectRect = GUILayoutUtility.GetRect(new GUIContent("Select"), GUI.skin.button);
                    if (GUI.Button(selectRect, "Select"))
                    {
                        PopupWindow.Show(selectRect, new PresetPicker(selectedPreset, preset =>
                        {
                            if (this == null) return;
                            selectedPreset = preset;
                            ApplySelectedPreset();
                        }));
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button("Create...")) UMAPresetEditorWindow.Open(thisDCA);
                    using (new EditorGUI.DisabledScope(selectedPreset == null))
                    {
                        if (GUILayout.Button("Edit...")) UMAPresetEditorWindow.Open(thisDCA, selectedPreset);
                        if (GUILayout.Button("Apply"))
                        {
                            ApplySelectedPreset();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
            DrawStandardDefaultWardrobe();
            using (inspectorView.Section("Animation",
                "Default Animator Controller is the fallback controller for this avatar. Race Animation Controllers can choose a controller per race. Disable Animation leaves the generated character without animation setup. Set Animator Parameters on Creation initializes UMA animator parameters when the Animator is added; Apply Root Motion lets that Animator move the character object."))
            {
                EditorGUI.BeginChangeCheck();
                inspectorView.Field(serializedObject, "animationController", "Default Animator Controller");
                var controllers = inspectorView.Property(serializedObject, "raceAnimationControllers");
                var rect = EditorGUILayout.GetControlRect(false, _animatorPropDrawer.GetPropertyHeight(controllers, GUIContent.none));
                _animatorPropDrawer.OnGUI(rect, controllers, UMAInspectorView.Label("Race Animation Controllers"));
                inspectorView.Field(serializedObject, "disableAnimation", "Disable Animation");
                inspectorView.Field(serializedObject, "initializeAnimatorWhenAdded", "Set Animator Parameters on Creation");
                if (inspectorView.Property(serializedObject, "initializeAnimatorWhenAdded").boolValue)
                    inspectorView.Field(serializedObject, "applyRootMotion", "Apply Root Motion");
                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    if (Application.isPlaying)
                    {
                        thisDCA.SetExpressionSet();
                        thisDCA.SetAnimatorController();
                    }
                }
            }
            using (inspectorView.Section("Generation & resource sharing",
                "Additional Utility Recipes add reusable runtime features such as colliders. Inherit Generator Detail Policy lets the active generator quality profile control character detail. Inherit Generator Reuse Policy lets it control sharing; otherwise Reuse Identical Meshes and Reuse Identical Atlases choose sharing for this avatar. Sharing only occurs for exactly compatible generated results, while pose and animation remain per character."))
            {
                inspectorView.Field(serializedObject, "umaAdditionalRecipes", "Additional Utility Recipes");
                inspectorView.Field(serializedObject, "inheritGeneratorQuality", "Inherit Generator Detail Policy");
                inspectorView.Field(serializedObject, "inheritGeneratorReusePolicy", "Inherit Generator Reuse Policy");
                inspectorView.Field(serializedObject, "reuseGeneratedMeshes", "Reuse Identical Meshes");
                inspectorView.Field(serializedObject, "reuseGeneratedTextures", "Reuse Identical Atlases");
                inspectorView.Field(serializedObject, "useNPCBuilds", "Use Completed NPC Builds");
                if (Application.isPlaying && !string.IsNullOrEmpty(thisDCA.NPCBuildStatus))
                    EditorGUILayout.LabelField(thisDCA.NPCBuildStatus, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("Events, diagnostics, specialized build settings and legacy controls are in Advanced View.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void ApplySelectedPreset()
        {
            if (thisDCA == null || selectedPreset == null) return;
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(thisDCA, "Apply UMA preset");
            try
            {
                thisDCA.ApplyPreset(selectedPreset, false);
                if (Application.isPlaying) thisDCA.BuildCharacter(true);
                else UpdateCharacter();
                EditorUtility.SetDirty(thisDCA);
                serializedObject.Update();
            }
            catch (ExitGUIException) { throw; }
            catch (Exception ex) { Debug.LogException(ex); }
            Repaint();
        }

        private sealed class PresetPicker : StandardPopupContent
        {
            protected override bool HasCancelFooter => true;
            private readonly List<UMAPreset> choices = new List<UMAPreset>();
            private readonly UMAPreset selected;
            private readonly Action<UMAPreset> onSelect;
            private Vector2 scroll;

            public PresetPicker(UMAPreset selected, Action<UMAPreset> onSelect)
            {
                this.selected = selected;
                this.onSelect = onSelect;
                foreach (string guid in AssetDatabase.FindAssets("t:UMAPreset"))
                {
                    var preset = AssetDatabase.LoadAssetAtPath<UMAPreset>(AssetDatabase.GUIDToAssetPath(guid));
                    if (preset != null) choices.Add(preset);
                }
                choices.Sort((left, right) => string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
            }

            protected override Vector2 GetContentSize() => new Vector2(440f, 430f);

            protected override void DrawPopupContent(Rect rect)
            {
                EditorGUILayout.LabelField("Select preset", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Choose a preset to apply it to the avatar immediately.",
                    EditorStyles.wordWrappedMiniLabel);
                using (var scrollView = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.ExpandHeight(true)))
                {
                    scroll = scrollView.scrollPosition;
                    if (choices.Count == 0)
                        EditorGUILayout.HelpBox("No avatar presets are available. Use Create to save a preset.", MessageType.Info);
                    foreach (UMAPreset preset in choices)
                    {
                        if (preset == null) continue;
                        Rect row = GUILayoutUtility.GetRect(0f, WardrobeCardHeight, GUILayout.ExpandWidth(true));
                        EditorGUI.DrawRect(row, preset == selected
                            ? new Color(0.38f, 0.68f, 1f, 1f)
                            : new Color(0.26f, 0.53f, 0.82f, 0.95f));
                        EditorGUI.DrawRect(new Rect(row.x + 1f, row.y + 1f, row.width - 2f, row.height - 2f),
                            new Color(0.24f, 0.24f, 0.24f, 1f));
                        Rect iconFrame = new Rect(row.x + 5f, row.y + 4f, WardrobeIconSize, WardrobeIconSize);
                        EditorGUI.DrawRect(iconFrame, new Color(0.15f, 0.49f, 0.85f, 1f));
                        Rect iconRect = new Rect(iconFrame.x + 2f, iconFrame.y + 2f, iconFrame.width - 4f, iconFrame.height - 4f);
                        EditorGUI.DrawRect(iconRect, new Color(0.24f, 0.24f, 0.24f, 1f));
                        if (preset.Icon != null) GUI.DrawTexture(iconRect, preset.Icon, ScaleMode.ScaleToFit, true);
                        GUI.Label(new Rect(iconFrame.xMax + 10f, row.y + 7f,
                            Mathf.Max(0f, row.xMax - iconFrame.xMax - 16f), 22f),
                            new GUIContent(preset.name, AssetDatabase.GetAssetPath(preset)), WardrobeRegionStyle);
                        if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                        {
                            editorWindow.Close();
                            onSelect?.Invoke(preset);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }
        }
    }
}
