#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    public partial class UMAWardrobeRecipeEditor
    {
        private readonly UMAInspectorView wardrobeView = new UMAInspectorView(typeof(UMAWardrobeRecipe));
        private readonly List<Action> standardEdits = new List<Action>();
        private readonly Dictionary<SlotData, bool> standardSlotFoldouts = new Dictionary<SlotData, bool>();
        private readonly Dictionary<List<OverlayData>, int> standardOverlayUses = new Dictionary<List<OverlayData>, int>();
        private List<RaceData> standardRaces;
        private string standardLoadedRecipe;
        private bool standardNeedsLoad = true;

        public override void OnInspectorGUI()
        {
            if (wardrobeView.DrawSelector())
            {
                standardNeedsLoad = true;
                base.OnInspectorGUI();
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !Initialized)
            {
                EditorGUILayout.HelpBox("Loading wardrobe recipe...", MessageType.Info);
                return;
            }
            if (targets.Length != 1)
            {
                EditorGUILayout.HelpBox("Select one wardrobe recipe to edit its slots, overlays and colors.", MessageType.Info);
                return;
            }

            var wardrobe = (UMAWardrobeRecipe)target;
            // Reload after Undo/Redo, external saves and a return from Advanced View. Do not run
            // the legacy overlay editors' normalization/Validate routines in Standard View.
            if (Event.current.type == EventType.Layout &&
                (standardNeedsLoad || standardLoadedRecipe != wardrobe.recipeString))
                ReloadStandardRecipe(wardrobe);
            if (_errorMessage != null || _recipe == null)
            {
                EditorGUILayout.HelpBox("This recipe could not be fully loaded. Restore the missing assets before editing; no partial recipe will be saved.\n" + _errorMessage, MessageType.Error);
                if (GUILayout.Button("Retry loading recipe")) { standardNeedsLoad = true; Repaint(); }
                return;
            }

            serializedObject.Update();
            standardEdits.Clear();
            using (wardrobeView.Section("Wardrobe & presentation",
                "Display name is the artist-facing recipe name. Enabled controls whether the recipe may be equipped. Wardrobe region chooses the body area this item occupies; Append to region allows compatible recipes to coexist instead of replacing one another. Thumbnails provide picker artwork. Thumbnail from texture and Thumbnail crop generate that artwork from a selected area of the recipe texture."))
            {
                wardrobeView.Field(serializedObject, "DisplayValue", "Display name");
                var disabled = serializedObject.FindProperty("disabled");
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.Toggle("Enabled", !disabled.boolValue);
                if (EditorGUI.EndChangeCheck()) disabled.boolValue = !enabled;
                DrawStandardStringChoice(serializedObject.FindProperty("wardrobeSlot"), "Wardrobe region", StandardRegions());
                wardrobeView.Field(serializedObject, "Appended", "Append to region");
                wardrobeView.Field(serializedObject, "wardrobeRecipeThumbs", "Thumbnails");
                wardrobeView.Field(serializedObject, "thumbnailFromTexture", "Thumbnail from texture");
                if (serializedObject.FindProperty("thumbnailFromTexture").boolValue)
                    wardrobeView.Field(serializedObject, "thumbnailRect", "Thumbnail crop");
            }
            using (wardrobeView.Section("Compatible races",
                "Compatible races lists the races that may wear this recipe. An empty list is not automatically equivalent to every race in all workflows, so add each supported race explicitly. Add race copies a RaceData name into the list; Inspect opens the referenced race, and Refresh race choices reloads the project list."))
            {
                DrawStandardStrings(serializedObject.FindProperty("compatibleRaces"), StandardRaceNames(), true);
                var race = (RaceData)EditorGUILayout.ObjectField("Add race", null, typeof(RaceData), false);
                if (race != null) AppendStandardString(serializedObject.FindProperty("compatibleRaces"), race.raceName);
                if (GUILayout.Button("Refresh race choices")) standardRaces = null;
            }
            using (wardrobeView.Section("Hiding & suppression",
                "Hides removes body or clothing slots by slot name while this recipe is worn. Hide tags remove slots carrying a matching tag. Suppresses prevents recipes in the listed wardrobe regions from being equipped with this item. Incompatible recipes blocks specific recipe assets. These controls hide or exclude content; they do not alter the source assets."))
            {
                EditorGUILayout.LabelField("Hides (slot names)", EditorStyles.boldLabel);
                DrawStandardStrings(serializedObject.FindProperty("Hides"), null, false);
                var hide = (SlotDataAsset)EditorGUILayout.ObjectField("Add hidden slot", null, typeof(SlotDataAsset), false);
                if (hide != null) AppendStandardString(serializedObject.FindProperty("Hides"), hide.slotName);
                wardrobeView.Field(serializedObject, "HideTags", "Hide tags");
                EditorGUILayout.LabelField("Suppresses (wardrobe regions)", EditorStyles.boldLabel);
                DrawStandardStrings(serializedObject.FindProperty("suppressWardrobeSlots"), StandardRegions(), false);
                DrawStandardAssetList("IncompatibleRecipes", "Incompatible recipes");
            }
            using (wardrobeView.Section("Mesh modifications",
                "Mesh hides remove selected triangles from compatible body slots beneath the clothing. Mesh hide collections apply a reusable set of hide assets. Mesh modifiers perform additional supported mesh operations during generation. Use these to prevent body clipping while keeping the original slots unchanged."))
            {
                DrawStandardAssetList("MeshHideAssets", "Mesh hides");
                DrawStandardAssetList("MeshHideAssetCollections", "Mesh hide collections");
                DrawStandardAssetList("MeshModifiers", "Mesh modifiers");
            }
            using (wardrobeView.Section("Shared colors",
                "Shared colors give multiple overlays one named tint or shader-property value. Editing a shared color updates every overlay that refers to it. Removing one keeps each overlay's current appearance by converting its value to a local color. Add shared color creates another reusable named value.")) DrawStandardColors();
            using (wardrobeView.Section("Slots & overlays",
                "Slots are the skinned mesh parts added by this recipe. Enable controls inclusion; tags identify a slot for tag-based rules, and race restrictions limit where it can be used. Overlays provide the material layers on a slot. Each overlay shows its asset name, local or shared color source, tint, and primary material texture. Add Overlay attaches another existing overlay; Inspect opens the source asset without changing this recipe.")) DrawStandardSlots();
            EditorGUILayout.HelpBox("Edits affect this recipe. Inspect opens the source asset separately. Advanced View contains DNA overrides, material channels, UV controls and other specialized settings; existing values are preserved.", MessageType.None);

            bool metadataChanged = serializedObject.hasModifiedProperties;
            if (metadataChanged || standardEdits.Count != 0)
            {
                CommitStandardEdits();
                // List mutations change the IMGUI control tree.
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("Save As...")) SaveAsRecipe(target.GetType());
        }

        private void CommitStandardEdits()
        {
            if (_errorMessage != null || _recipe == null) return;
            Undo.RecordObject(target, "Edit wardrobe recipe");
            serializedObject.ApplyModifiedProperties();
            var wardrobe = (UMAWardrobeRecipe)target;
            if (standardEdits.Count != 0)
            {
                foreach (var edit in standardEdits) edit();
                DoUpdate();
                standardLoadedRecipe = wardrobe.recipeString;
            }
            else
            {
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssetIfDirty(target);
                UMAUpdateProcessor.UpdateRecipe(wardrobe);
            }
            standardEdits.Clear();
        }

        private void ReloadStandardRecipe(UMAWardrobeRecipe wardrobe)
        {
            var loaded = new UMAData.UMARecipe();
            _errorMessage = null;
            try { wardrobe.Load(loaded); }
            catch (UMAResourceNotFoundException ex) { _errorMessage = ex.Message; }
            _recipe = loaded;
            standardLoadedRecipe = wardrobe.recipeString;
            standardNeedsLoad = false;
            standardSlotFoldouts.Clear();
            _rebuildOnLayout = true;
        }

        private List<string> StandardRaceNames()
        {
            if (standardRaces == null)
                standardRaces = UMAAssetIndexer.Instance != null
                    ? UMAAssetIndexer.Instance.GetAllAssets<RaceData>() : new List<RaceData>();
            var names = new List<string>();
            foreach (var race in standardRaces)
                if (race != null && !names.Contains(race.raceName)) names.Add(race.raceName);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private List<string> StandardRegions()
        {
            StandardRaceNames();
            var result = new List<string> { "None" };
            var compatible = serializedObject.FindProperty("compatibleRaces");
            foreach (var race in standardRaces)
            {
                if (race == null || race.wardrobeSlots == null) continue;
                bool include = compatible.arraySize == 0;
                for (int i = 0; i < compatible.arraySize; i++)
                    include |= compatible.GetArrayElementAtIndex(i).stringValue == race.raceName;
                if (include) foreach (string region in race.wardrobeSlots)
                    if (!string.IsNullOrEmpty(region) && !result.Contains(region)) result.Add(region);
            }
            return result;
        }

        private static void DrawStandardStringChoice(SerializedProperty property, string label, List<string> choices)
        {
            string value = StandardStringChoice(label, property.stringValue, choices);
            if (value != property.stringValue) property.stringValue = value;
        }

        private static string StandardStringChoice(string label, string current, List<string> choices)
        {
            EditorGUI.BeginChangeCheck();
            if (choices == null || choices.Count == 0)
            {
                string text = EditorGUILayout.DelayedTextField(label, current);
                return EditorGUI.EndChangeCheck() ? text : current;
            }
            var options = new List<string>(choices);
            // Unknown/legacy values are deliberately retained, not coerced to the first choice.
            if (!options.Contains(current)) options.Insert(0, current ?? "");
            int index = options.IndexOf(current ?? "");
            string choice = options[EditorGUILayout.Popup(label, Mathf.Max(0, index), options.ToArray())];
            return EditorGUI.EndChangeCheck() ? choice : current;
        }

        private void DrawStandardStrings(SerializedProperty list, List<string> choices, bool races)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var item = list.GetArrayElementAtIndex(i);
                    DrawStandardStringChoice(item, "", choices);
                    Object asset = races ? (Object)FindStandardRace(item.stringValue) :
                        UMAAssetIndexer.Instance?.RawGetAsset<SlotDataAsset>(item.stringValue);
                    if (races || choices == null) StandardInspect(asset);
                    if (GUILayout.Button("-", GUILayout.Width(24))) { list.DeleteArrayElementAtIndex(i); break; }
                }
            }
            if (GUILayout.Button("Add entry"))
            {
                int index = list.arraySize++;
                list.GetArrayElementAtIndex(index).stringValue = "";
            }
        }

        private RaceData FindStandardRace(string name)
        {
            StandardRaceNames();
            return standardRaces.Find(r => r != null && r.raceName == name);
        }

        private static void AppendStandardString(SerializedProperty list, string value)
        {
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).stringValue == value) return;
            int index = list.arraySize++;
            list.GetArrayElementAtIndex(index).stringValue = value;
        }

        private void DrawStandardAssetList(string path, string label)
        {
            var list = serializedObject.FindProperty(path);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            for (int i = 0; i < list.arraySize; i++)
                using (new EditorGUILayout.HorizontalScope())
                {
                    var item = list.GetArrayElementAtIndex(i);
                    EditorGUILayout.PropertyField(item, GUIContent.none);
                    StandardInspect(item.objectReferenceValue);
                    if (GUILayout.Button("-", GUILayout.Width(24)))
                    {
                        item.objectReferenceValue = null;
                        list.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }
            if (GUILayout.Button("Add " + label.ToLowerInvariant()))
            {
                int index = list.arraySize++;
                list.GetArrayElementAtIndex(index).objectReferenceValue = null;
            }
        }

        private void StandardInspect(Object asset)
        {
            using (new EditorGUI.DisabledScope(asset == null))
                if (GUILayout.Button("Inspect", GUILayout.Width(54))) InspectMe.Add(asset);
        }

        private void DrawStandardColors()
        {
            var colors = _recipe.sharedColors;
            if (colors != null) for (int i = 0; i < colors.Length; i++)
            {
                var color = colors[i];
                if (color == null) { EditorGUILayout.LabelField("Missing color " + (i + 1)); continue; }
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(color.name) ? "Unnamed color " + (i + 1) : color.name, EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    string name = EditorGUILayout.DelayedTextField("Name", color.name);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (!string.IsNullOrWhiteSpace(name) && name != OverlayColorData.UNSHARED &&
                            !Array.Exists(colors, c => c != null && !ReferenceEquals(c, color) && c.name == name))
                            standardEdits.Add(() => color.name = name);
                        else Debug.LogWarning("Shared colors need a non-empty, unique name (not '-').", target);
                    }
                    DrawStandardColorValue(color);
                    if (GUILayout.Button("Remove color (keep overlay appearance)"))
                        standardEdits.Add(() => RemoveStandardColor(_recipe, color));
                }
            }
            if (GUILayout.Button("Add shared color"))
                standardEdits.Add(() =>
                {
                    var list = new List<OverlayColorData>(_recipe.sharedColors ?? Array.Empty<OverlayColorData>());
                    string name = "Color " + (list.Count + 1);
                    while (list.Exists(c => c != null && c.name == name)) name += " New";
                    list.Add(new OverlayColorData(1) { name = name });
                    _recipe.sharedColors = list.ToArray();
                });
        }

        private static void RemoveStandardColor(UMAData.UMARecipe recipe, OverlayColorData color)
        {
            if (recipe.slotDataList != null) foreach (var slot in recipe.slotDataList)
            {
                if (ReferenceEquals(slot, null) || slot.GetOverlayList() == null) continue;
                foreach (var overlay in slot.GetOverlayList())
                    if (overlay != null && ReferenceEquals(overlay.colorData, color))
                    {
                        overlay.colorData = color.Clone();
                        overlay.colorData.name = OverlayColorData.UNSHARED;
                    }
            }
            var colors = new List<OverlayColorData>(recipe.sharedColors ?? Array.Empty<OverlayColorData>());
            colors.RemoveAll(c => ReferenceEquals(c, color));
            recipe.sharedColors = colors.ToArray();
        }

        private void DrawStandardColorValue(OverlayColorData color)
        {
            if (color.channelMask != null && color.channelMask.Length > 0)
            {
                EditorGUI.BeginChangeCheck();
                Color value = EditorGUILayout.ColorField(UMAInspectorView.Label("Color"), color.color, true, true, true);
                if (EditorGUI.EndChangeCheck()) standardEdits.Add(() => color.color = value);
            }
            // Modern UMA colors can tint shader parameters instead of baked texture channels.
            if (color.PropertyBlock?.shaderProperties != null)
                foreach (var property in color.PropertyBlock.shaderProperties)
                    if (property is UMAColorProperty tint)
                    {
                        EditorGUI.BeginChangeCheck();
                        Color value = EditorGUILayout.ColorField(UMAInspectorView.Label(tint.name), tint.Value, true, true, true);
                        if (EditorGUI.EndChangeCheck()) standardEdits.Add(() => tint.Value = value);
                    }
        }

        private void DrawStandardSlots()
        {
            var slots = _recipe.slotDataList;
            standardOverlayUses.Clear();
            if (slots != null) foreach (var slot in slots)
            {
                if (ReferenceEquals(slot, null) || slot.GetOverlayList() == null) continue;
                var list = slot.GetOverlayList();
                standardOverlayUses.TryGetValue(list, out int uses);
                standardOverlayUses[list] = uses + 1;
            }
            if (slots != null) for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (ReferenceEquals(slot, null)) continue;
                int index = i;
                string name = slot.isPlaceholderSlot ? slot.placeholderSlotName : slot.asset != null ? slot.asset.slotName : "Missing slot asset";
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool open = !standardSlotFoldouts.TryGetValue(slot, out bool expanded) || expanded;
                        standardSlotFoldouts[slot] = EditorGUILayout.Foldout(open, name, true);
                        StandardInspect(slot.asset);
                        if (GUILayout.Button("Remove", GUILayout.Width(60))) standardEdits.Add(() =>
                        {
                            var list = new List<SlotData>(_recipe.slotDataList); list.RemoveAt(index); _recipe.SetSlots(list.ToArray());
                        });
                    }
                    if (!standardSlotFoldouts[slot]) continue;
                    EditorGUI.BeginChangeCheck();
                    bool enabled = EditorGUILayout.Toggle("Enabled", !slot.isDisabled);
                    if (EditorGUI.EndChangeCheck()) standardEdits.Add(() => slot.isDisabled = !enabled);
                    DrawStandardSlotStrings("Tags", slot.tags, values => slot.tags = values, false);
                    DrawStandardSlotStrings("Race filters (empty = all)", slot.Races, values => slot.Races = values, true);
                    var overlays = slot.GetOverlayList();
                    int sharedWith = overlays != null ? standardOverlayUses[overlays] - 1 : 0;
                    if (sharedWith > 0)
                        EditorGUILayout.HelpBox("This overlay list is shared with " + sharedWith + " other slot(s). Overlay edits affect those slots too.", MessageType.Info);
                    if (overlays != null) for (int j = 0; j < overlays.Count; j++)
                        DrawStandardOverlay(slot, overlays, j);
                    var overlay = (OverlayDataAsset)EditorGUILayout.ObjectField("Add overlay", null, typeof(OverlayDataAsset), false);
                    if (overlay != null) standardEdits.Add(() => slot.AddOverlay(new OverlayData(overlay)));
                }
            }
            var asset = (SlotDataAsset)EditorGUILayout.ObjectField("Add slot", null, typeof(SlotDataAsset), false);
            if (asset != null) standardEdits.Add(() =>
            {
                var list = new List<SlotData>(_recipe.slotDataList ?? Array.Empty<SlotData>()) { new SlotData(asset) };
                _recipe.SetSlots(list.ToArray());
            });
        }

        private void DrawStandardSlotStrings(string label, string[] values, Action<string[]> assign, bool races)
        {
            EditorGUILayout.LabelField(label);
            var list = new List<string>(values ?? Array.Empty<string>());
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
                using (new EditorGUILayout.HorizontalScope())
                {
                    string value = StandardStringChoice("", list[i], races ? StandardRaceNames() : null);
                    if (value != list[i]) { list[i] = value; changed = true; }
                    if (races) StandardInspect(FindStandardRace(list[i]));
                    if (GUILayout.Button("-", GUILayout.Width(24))) { list.RemoveAt(i); changed = true; break; }
                }
            if (GUILayout.Button(races ? "Add race filter" : "Add tag")) { list.Add(""); changed = true; }
            if (changed) standardEdits.Add(() => assign(list.ToArray()));
        }

        private void DrawStandardOverlay(SlotData slot, List<OverlayData> overlays, int index)
        {
            var overlay = overlays[index];
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(overlay?.asset != null ? overlay.overlayName : "Missing overlay", EditorStyles.boldLabel);
                    StandardInspect(overlay?.asset);
                    using (new EditorGUI.DisabledScope(index == 0))
                        if (GUILayout.Button("Up", GUILayout.Width(30))) standardEdits.Add(() =>
                        { var previous = overlays[index - 1]; overlays[index - 1] = overlay; overlays[index] = previous; });
                    if (GUILayout.Button("-", GUILayout.Width(24))) standardEdits.Add(() => overlays.RemoveAt(index));
                }
                if (overlay?.asset == null) return;
                DrawStandardOverlayColor(overlay);
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("Base map (preview)", StandardBaseTexture(slot, overlay), typeof(Texture), false);
            }
        }

        private void DrawStandardOverlayColor(OverlayData overlay)
        {
            var shared = _recipe.sharedColors ?? Array.Empty<OverlayColorData>();
            var names = new string[shared.Length + 1];
            names[0] = "Local color";
            int selected = 0;
            for (int i = 0; i < shared.Length; i++)
            {
                names[i + 1] = shared[i]?.name ?? "Missing color";
                if (ReferenceEquals(shared[i], overlay.colorData)) selected = i + 1;
            }
            int choice = EditorGUILayout.Popup("Color source", selected, names);
            if (choice != selected && (choice == 0 || shared[choice - 1] != null)) standardEdits.Add(() =>
            {
                if (choice == 0)
                {
                    overlay.colorData = overlay.colorData?.Clone() ?? new OverlayColorData(1);
                    overlay.colorData.name = OverlayColorData.UNSHARED;
                }
                else overlay.colorData = shared[choice - 1];
            });
            if (overlay.colorData != null) DrawStandardColorValue(overlay.colorData);
        }

        internal static Texture StandardBaseTexture(SlotData slot, OverlayData overlay)
        {
            var material = slot?.altMaterial != null ? slot.altMaterial : overlay?.asset?.GetMaterial();
            if (material != null && material.materialType == UMAMaterial.MaterialType.UseExistingMaterial)
            {
                var source = material.material;
                if (source == null) return null;
                // Respect [MainTexture] without querying a missing _MainTex on custom shaders.
                var shader = source.shader;
                if (shader != null) for (int i = 0; i < shader.GetPropertyCount(); i++)
                    if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture &&
                        (shader.GetPropertyFlags(i) & UnityEngine.Rendering.ShaderPropertyFlags.MainTexture) != 0)
                        return source.GetTexture(shader.GetPropertyName(i));
                if (source.HasProperty("_BaseMap")) return source.GetTexture("_BaseMap");
                if (source.HasProperty("_MainTex")) return source.GetTexture("_MainTex");
                return null;
            }
            var textures = overlay?.asset?.textureList;
            return textures != null && textures.Length > 0 ? textures[0] : null;
        }
    }
}
#endif
