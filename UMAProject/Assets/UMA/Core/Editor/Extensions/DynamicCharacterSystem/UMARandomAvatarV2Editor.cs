using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;
using System;
using static UMA.Editors.GUIHelper;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMARandomAvatar))]
    public sealed class UMARandomAvatarEditor : Editor
    {
        private readonly UMAInspectorView inspectorView =
            new UMAInspectorView(typeof(UMARandomAvatar));

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            inspectorView.Field(serializedObject, "mode", "Targets");
            inspectorView.Field(serializedObject, "RandomizeOnStart", "Run on Start");
            bool generate = serializedObject.FindProperty("mode").enumValueIndex == (int)UMARandomAvatar.Mode.Generate;
            if (!generate)
                inspectorView.Field(serializedObject, "ExistingDCAs", "Scene Avatars");

            using (inspectorView.Section("Randomization source",
                "Randomizers are the weighted race, DNA, color, and wardrobe libraries used to create each character. Maximum Unique Characters limits how many complete randomized setups are created before later characters reuse one of those setups. Zero creates a unique setup for every character; resource sharing still requires compatible Cache and Reuse settings on the avatar prefab."))
            {
                inspectorView.Field(serializedObject, "Randomizers", "Character Randomizers");
                inspectorView.Field(serializedObject, "WardrobeRandomizers", "Wardrobe Randomizers (optional)");
                EditorGUILayout.LabelField("Leave wardrobe randomizers empty to use the character setup's clothing. Wardrobe-only rerolls then use a matching character definition for the current race.", EditorStyles.wordWrappedMiniLabel);
                inspectorView.Field(serializedObject, "KeepExistingRace", "Keep Existing Race");
                inspectorView.Field(serializedObject, "KeepExistingWardrobe", "Keep Unselected Wardrobe");
                EditorGUILayout.LabelField("Selected slots always replace their current items. Partial rerolls preserve unselected slots.", EditorStyles.wordWrappedMiniLabel);
                if (generate)
                {
                    bool preservesState = serializedObject.FindProperty("KeepExistingRace").boolValue || serializedObject.FindProperty("KeepExistingWardrobe").boolValue;
                    using (new EditorGUI.DisabledScope(preservesState))
                    {
                        inspectorView.Field(serializedObject, "MaximumUniqueCharacters", "Maximum Unique Characters");
                        inspectorView.Field(serializedObject, "UseNPCBuilds", "Use Completed NPC Builds");
                        if (inspectorView.Property(serializedObject, "UseNPCBuilds").boolValue)
                            EditorGUILayout.HelpBox("Requires a non-zero character pool and mesh/atlas reuse. Each appearance builds once; supported repeats instantiate the completed rig and outputs. Custom build callbacks fall back to ordinary generation.", MessageType.Info);
                    }
                    if (preservesState) EditorGUILayout.HelpBox("Setup pooling is bypassed while preserving each avatar's race or wardrobe.", MessageType.Info);
                }
                if (inspectorView.Property(serializedObject, "Randomizers").arraySize == 0 &&
                    inspectorView.Property(serializedObject, "WardrobeRandomizers").arraySize == 0)
                    EditorGUILayout.HelpBox("Assign at least one UMARandomizer before generating characters.", MessageType.Warning);
            }

            if (generate)
            {
                using (inspectorView.Section("Character prefab and ownership",
                    "Prefab is instantiated for every generated character and must contain a DynamicCharacterAvatar. Parent Object receives the generated instances; when empty, this controller is used. Name Base supplies the single-character name or the prefix used for a generated grid."))
                {
                    inspectorView.Field(serializedObject, "prefab", "Character Prefab");
                    inspectorView.Field(serializedObject, "ParentObject", "Parent Object");
                    inspectorView.Field(serializedObject, "NameBase", "Name Base");
                    if (inspectorView.Property(serializedObject, "prefab").objectReferenceValue == null)
                        EditorGUILayout.HelpBox("A character prefab is required.", MessageType.Warning);
                }

                using (inspectorView.Section("Placement",
                    "Show Placeholder draws a scene gizmo at the controller. Generate Grid creates Grid X Size by Grid Z Size characters centered on this object; otherwise one character is generated here. Grid Distance controls spacing, Random Offset adds planar position variation, and Random Rotation varies each character's world Y rotation."))
                {
                    inspectorView.Field(serializedObject, "ShowPlaceholder", "Show Placeholder");
                    inspectorView.Field(serializedObject, "GenerateGrid", "Generate Grid");
                    if (inspectorView.Property(serializedObject, "GenerateGrid").boolValue)
                    {
                        inspectorView.Field(serializedObject, "GridXSize", "Grid X Size");
                        inspectorView.Field(serializedObject, "GridZSize", "Grid Z Size");
                        inspectorView.Field(serializedObject, "GridDistance", "Grid Distance");
                        inspectorView.Field(serializedObject, "RandomOffset", "Random Offset");
                    }
                    inspectorView.Field(serializedObject, "RandomRotation", "Random Rotation");
                }

                using (inspectorView.Section("Spawn scheduling",
                    "Spawn Budget limits instantiation and recipe setup time per frame, independently of the UMA generator's build scheduling. Zero preserves synchronous spawning. Try 4 ms for a responsive crowd spawn. One complete character can exceed the budget. A positive budget isolates the crowd's random sequence from unrelated random calls between frames. This reduces the initial freeze, not necessarily total completion time."))
                    inspectorView.Field(serializedObject, "SpawnBudgetMilliseconds", "Spawn Budget (ms/frame)");

                using (inspectorView.Section("Events",
                    "Random Avatar Generated fires immediately after an instance is created and named, before its randomized UMA build completes. The event receives this controller object and the new character object, making it suitable for registration or setup that must happen before generation finishes."))
                    inspectorView.Field(serializedObject, "RandomAvatarGenerated", "Random Avatar Generated");

            }
            serializedObject.ApplyModifiedProperties();

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                var controller = (UMARandomAvatar)target;
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Reroll Character")) controller.RandomizeCharacterButton();
                    if (GUILayout.Button("Reroll Wardrobe")) controller.RandomizeWardrobeButton();
                }
                if (GUILayout.Button("Reroll All")) controller.RandomizeButton();
                if (generate && GUILayout.Button("Generate Characters")) controller.GenerateCharacters(false);
            }
            if (!Application.isPlaying)
                EditorGUILayout.LabelField("Enter Play mode to generate or reroll characters.", EditorStyles.miniLabel);

            if (Application.isPlaying && generate)
            {
                var randomAvatar = (UMARandomAvatar)target;
                using (inspectorView.Section("Runtime crowd status",
                    "Generated Characters counts live instances owned by this controller. Unique Character Setups counts the immutable randomized appearance setups in its pool. Clear Setup Pool makes future characters build a new pool without changing existing characters. Destroy Generated Characters removes this controller's crowd and clears its setup pool."))
                {
                    EditorGUILayout.LabelField("Generated Characters", randomAvatar.GeneratedCharacterCount.ToString());
                    EditorGUILayout.LabelField("Unique Character Setups", randomAvatar.UniqueCharacterSetupCount.ToString());
                    EditorGUILayout.LabelField("Spawning", randomAvatar.IsGenerating ? "In progress" : "Finished");
                    if (GUILayout.Button("Clear Character Setup Pool")) randomAvatar.ClearCharacterSetupPool();
                    if (GUILayout.Button("Destroy Generated Characters")) randomAvatar.DestroyGeneratedCharacters();
                }
            }
        }
    }

    [CustomEditor(typeof(UMARandomAvatarV2))]
    public class UMARandomAvatarV2Editor : Editor
    {
        UMARandomAvatarV2 currentTarget = null;     // Randomizer Inspector target
        private readonly UMAInspectorView inspectorView =
            new UMAInspectorView(typeof(UMARandomAvatarV2));
        private static GUIStyle dropAreaStyle;
        private static GUIStyle DropAreaStyle =>
            dropAreaStyle ??= new GUIStyle(GUI.skin.label);

        private static class Tooltips
        {
            internal static GUIContent CharacterRandomizers = new GUIContent("Character Randomizers", "When randomizing, a Randomizer is picked randomly in CharacterRandomizer's list.\nThen a randomAvatar is picked in that random avatar using RandomAvatar weights, unless \"Keep Existing Race\".");
            internal static GUIContent CharacterEmpty = new GUIContent("Drag and Drop Randomizers to add");
            internal static GUIContent WardrobeRandomizers = new GUIContent("Wardrobe Randomizers", "A Randomizer is randomly picked in Wardrobe's Randomizer's list and applied after Character Randomizer.\nOnly RandomAvatars matching Character's Race are picked");
            internal static GUIContent WardrobeEmpty = new GUIContent("Drag and Drop Randomizers to add");

            internal static GUIContent KeepRace = new GUIContent("Keep existing Race", "Will keep existing race when randomized. \nOnly RandomAvatars of existing race will used during the randomization.");
            internal static GUIContent KeepWardrobe = new GUIContent("Keep existing Wardrobe", "Unselected wardrobe regions are preserved. Selected slots replace their items; a None entry removes its region.");
            internal static GUIContent RandomizeRace = new GUIContent("Randomize existing Race", "Race will be randomized using RandomAvatars.");
            internal static GUIContent ClearWardrobe = new GUIContent("Clear existing Wardrobe", "Full rerolls clear wardrobe first. Partial rerolls replace selected regions only.");
            internal static GUIContent DCAReferences = new GUIContent("Scene DCAs :", "Drag and drop Dynamic Character Avatars references from the scene.");
            internal static GUIContent DCAReferencesEmpty = new GUIContent("Drag and Drop DynamicCharacterAvatars from the scene.");
        }


        protected void OnEnable()
        {
            // -- Inspector Vars --
            currentTarget = target as UMARandomAvatarV2;

        }


        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox("UMARandomAvatar now includes these controls and crowd optimizations. V2 is retained for existing scenes.", MessageType.Info);
            using (new EditorGUI.DisabledScope(Application.isPlaying || currentTarget.GetComponent<UMARandomAvatar>() != null))
            {
                if (GUILayout.Button("Copy to unified UMARandomAvatar and disable V2"))
                {
                    serializedObject.ApplyModifiedProperties();
                    CopyToUnifiedController(currentTarget);
                    GUIUtility.ExitGUI();
                }
            }

            using (inspectorView.Section("Randomization sources",
                "Character Randomizers choose race, DNA, character colors, hair, and other character traits. Wardrobe Randomizers run afterward and add race-compatible clothing and wardrobe colors. When a list contains several assets, one randomizer is chosen randomly; weights inside that asset then choose its per-race setup and items. Drag assets into either list to add them."))
            {
                RandomizerListGUI(currentTarget.CharacterRandomizers, Tooltips.CharacterRandomizers, Tooltips.CharacterEmpty);
                RandomizerListGUI(currentTarget.WardrobeRandomizers, Tooltips.WardrobeRandomizers, Tooltips.WardrobeEmpty);
            }

            using (inspectorView.Section("Preservation rules",
                "Keep Existing Race restricts character randomization to entries matching the avatar's current race. Keep Existing Wardrobe prevents currently equipped recipes from being cleared before randomization, allowing new selections to layer onto them. Disable it when the random result should fully replace existing wardrobe."))
                GlobalOptionsGUI();

            UMARandomAvatarV2.Mode mode;
            using (inspectorView.Section("Targets and generation",
                "Generate mode instantiates the configured DynamicCharacterAvatar prefab. Use Existing mode randomizes the listed scene avatars when they are created. In Generate mode, Parent Object receives new instances; Show Placeholder draws the origin, Sequential randomizes and builds one character as it is created, and the grid settings control crowd layout. Random Avatar Generated fires for each new instance."))
            {
                mode = ModeTabsGUI();
                GUILayout.Space(3f);
                switch (mode)
                {
                    case UMARandomAvatarV2.Mode.Generate:
                        GenerateOptionsGUI();
                        break;
                    case UMARandomAvatarV2.Mode.UseExisting:
                        ExistingDCAListGUI(currentTarget.ExistingDCAs, Tooltips.DCAReferences, Tooltips.DCAReferencesEmpty);
                        break;
                }
            }

            using (inspectorView.Section("Randomization actions",
                "Randomize Characters changes race, DNA, character colors, hair, and character-level slots while leaving the wardrobe pass disabled. Randomize Wardrobe applies only the wardrobe randomizer pass. Randomize performs both passes. In Generate mode these buttons affect characters previously created by this component; in Use Existing mode they affect the configured scene avatars."))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Randomize Characters")) currentTarget.RandomizeCharacterButton();
                if (GUILayout.Button("Randomize Wardrobe")) currentTarget.RandomizeWardrobeButton();
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("Randomize")) currentTarget.RandomizeButton();
            }

            serializedObject.ApplyModifiedProperties();
        }

        internal static UMARandomAvatar CopyToUnifiedController(UMARandomAvatarV2 source)
        {
            if (source == null || source.GetComponent<UMARandomAvatar>() != null) return null;
            var result = Undo.AddComponent<UMARandomAvatar>(source.gameObject);
            Undo.RecordObject(result, "Copy random avatar settings");
            Undo.RecordObject(source, "Disable legacy random avatar controller");
            result.Randomizers = source.CharacterRandomizers == null ? new List<UMARandomizer>() : new List<UMARandomizer>(source.CharacterRandomizers);
            result.WardrobeRandomizers = source.WardrobeRandomizers == null ? new List<UMARandomizer>() : new List<UMARandomizer>(source.WardrobeRandomizers);
            result.ExistingDCAs = source.ExistingDCAs == null ? new List<DynamicCharacterAvatar>() : new List<DynamicCharacterAvatar>(source.ExistingDCAs);
            result.mode = (UMARandomAvatar.Mode)source.mode;
            result.KeepExistingRace = source.KeepExistingRace;
            result.KeepExistingWardrobe = source.KeepExistingWardrobe;
            var generation = source.Generation;
            result.prefab = generation.Prefab;
            result.ParentObject = generation.ParentObject;
            result.ShowPlaceholder = generation.ShowPlaceholder;
            result.GenerateGrid = generation.GenerateGrid;
            result.GridXSize = generation.GridXSize;
            result.GridZSize = generation.GridZSize;
            result.GridDistance = generation.GridDistance;
            result.RandomOffset = generation.GridRandomOffset;
            result.RandomRotation = generation.RandomRotation;
            result.NameBase = generation.NameBase;
            result.RandomAvatarGenerated = generation.RandomAvatarGenerated;
            result.enabled = source.enabled;
            source.enabled = false;
            source.UnifiedController = result;
            EditorUtility.SetDirty(result);
            EditorUtility.SetDirty(source);
            PrefabUtility.RecordPrefabInstancePropertyModifications(result);
            PrefabUtility.RecordPrefabInstancePropertyModifications(source);
            return result;
        }

        private void RandomizerListGUI(List<UMARandomizer> randomizersList, GUIContent label, GUIContent emptyLabel)
        {

            Rect labelRect = EditorGUILayout.GetControlRect();
            EditorGUI.LabelField(labelRect, label, EditorStyles.boldLabel);
            BeginVerticalPadded(2f);
            for (int i = randomizersList.Count - 1; i >= 0; i--)
            {
                UMARandomizer randomizer = randomizersList[i];
                RandomizerGUI(randomizersList, i, randomizer);
            }
            // Add a default message if list is empty
            if (randomizersList.Count == 0)
                EditorGUILayout.LabelField(emptyLabel);

            EndVerticalPadded(2f);

            Rect listRect = GUILayoutUtility.GetLastRect();
            Rect areaRect = new Rect(labelRect.xMin, labelRect.yMin, listRect.width, listRect.height + labelRect.height);
            bool updated = false;
            DropAreaGUI((x) => updated = addItemToList(randomizersList, x), areaRect, GUIContent.none, DropAreaStyle);
            if (updated)
            {
                EditorUtility.SetDirty(currentTarget);
                AssetDatabase.SaveAssetIfDirty(currentTarget);
            }
        }

        private static void RandomizerGUI(List<UMARandomizer> randomizersList, int i, UMARandomizer randomizer)
        {
            EditorGUILayout.BeginHorizontal();
            if (randomizer != null && randomizer.useDefinition)
                EditorGUILayout.LabelField(randomizer.Definition.Name, EditorStyles.miniLabel, GUILayout.Width(120f));
            EditorGUILayout.ObjectField(randomizer, typeof(UMARandomizer), false, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                randomizersList.RemoveAt(i);

            EditorGUILayout.EndHorizontal();
        }

        private void GlobalOptionsGUI()
        {
            Rect hRect = EditorGUILayout.GetControlRect();

            GUIStyle keepRaceStyle = currentTarget.KeepExistingRace ? Styles.ToggleButtonToggled : Styles.ToggleButtonNormal;
            GUIContent keepRaceGUI = currentTarget.KeepExistingRace ? Tooltips.KeepRace : Tooltips.RandomizeRace;
            Rect keepRaceRect = new Rect(hRect.xMin, hRect.yMin, hRect.width / 2 - 2.5f, hRect.height);

            GUIStyle keepWardrobeStyle = currentTarget.KeepExistingWardrobe ? Styles.ToggleButtonToggled : Styles.ToggleButtonNormal;
            GUIContent keepWardrobeGUI = currentTarget.KeepExistingWardrobe ? Tooltips.KeepWardrobe : Tooltips.ClearWardrobe;
            Rect keepWardrobeRect = new Rect(keepRaceRect.xMax + 5f, hRect.yMin, hRect.width / 2 - 2.5f, hRect.height);

            var keepRaceProperty = serializedObject.FindProperty("KeepExistingRace");
            var keepWardrobeProperty = serializedObject.FindProperty("KeepExistingWardrobe");
            keepRaceProperty.boolValue = EditorGUI.Toggle(keepRaceRect, keepRaceProperty.boolValue, keepRaceStyle);
            keepWardrobeProperty.boolValue = EditorGUI.Toggle(keepWardrobeRect, keepWardrobeProperty.boolValue, keepWardrobeStyle);

            EditorGUI.LabelField(keepRaceRect, keepRaceGUI, keepRaceStyle);
            EditorGUI.LabelField(keepWardrobeRect, keepWardrobeGUI, keepWardrobeStyle);

        }

        //Draws the 'View' tabs allowing the user to switch between viewing data 'By Plugin' or 'By DNA'
        private UMARandomAvatarV2.Mode ModeTabsGUI()
        {
            //Tabs for viewing by modifier or by dna
            var tabsRect = EditorGUILayout.GetControlRect();
            var tabsLabel = new Rect(tabsRect.xMin, tabsRect.yMin, 60f, tabsRect.height);
            var tabsButRect = new Rect(tabsLabel.xMax, tabsRect.yMin, (tabsRect.width - tabsLabel.width), tabsRect.height);

            EditorGUI.LabelField(tabsLabel, "Mode:", EditorStyles.toolbarButton);

            var modeProperty = serializedObject.FindProperty("mode");
            var modeVal = modeProperty.enumValueIndex;
            EditorGUI.BeginChangeCheck();
            modeVal = GUI.Toolbar(tabsButRect, modeVal, Enum.GetNames(typeof(UMARandomAvatarV2.Mode)), EditorStyles.toolbarButton);
            if (EditorGUI.EndChangeCheck()) modeProperty.enumValueIndex = modeVal;
            return (UMARandomAvatarV2.Mode)modeVal;
        }

        private void GenerateOptionsGUI()
        {
            var generation = serializedObject.FindProperty("Generation");
            EditorGUILayout.PropertyField(generation.FindPropertyRelative("Prefab"), UMAInspectorView.Label("Character Prefab"));
            EditorGUILayout.PropertyField(generation.FindPropertyRelative("ParentObject"), UMAInspectorView.Label("Parent Object"));
            EditorGUILayout.PropertyField(generation.FindPropertyRelative("NameBase"), UMAInspectorView.Label("Name Base"));
            EditorGUILayout.PropertyField(generation.FindPropertyRelative("ShowPlaceholder"), UMAInspectorView.Label("Show Placeholder"));
            EditorGUILayout.PropertyField(generation.FindPropertyRelative("RandomRotation"), UMAInspectorView.Label("Random Rotation"));
            EditorGUILayout.PropertyField(generation.FindPropertyRelative("Sequential"), UMAInspectorView.Label("Sequential Generation"));
            var grid = generation.FindPropertyRelative("GenerateGrid");
            EditorGUILayout.PropertyField(grid, UMAInspectorView.Label("Generate Grid"));
            if (grid.boolValue)
            {
                EditorGUILayout.PropertyField(generation.FindPropertyRelative("GridXSize"), UMAInspectorView.Label("Grid X Size"));
                EditorGUILayout.PropertyField(generation.FindPropertyRelative("GridZSize"), UMAInspectorView.Label("Grid Z Size"));
                EditorGUILayout.PropertyField(generation.FindPropertyRelative("GridDistance"), UMAInspectorView.Label("Grid Distance"));
                EditorGUILayout.PropertyField(generation.FindPropertyRelative("GridRandomOffset"), UMAInspectorView.Label("Grid Random Offset"));
            }
            EditorGUILayout.PropertyField(generation.FindPropertyRelative("RandomAvatarGenerated"), UMAInspectorView.Label("Random Avatar Generated"));
        }

        private void ExistingDCAListGUI(List<DynamicCharacterAvatar> DCAList, GUIContent label, GUIContent emptyLabel)
        {
            Rect labelRect = EditorGUILayout.GetControlRect();
            EditorGUI.LabelField(labelRect, label, EditorStyles.boldLabel);
            BeginVerticalPadded(2f);
            for (int i = DCAList.Count - 1; i >= 0; i--)
            {
                DynamicCharacterAvatar DCA = DCAList[i];
                DCAGUI(DCAList, i, DCA);
            }
            // Add a default message if list is empty
            if (DCAList.Count == 0)
                EditorGUILayout.LabelField(emptyLabel);

            EndVerticalPadded(2f);

            Rect listRect = GUILayoutUtility.GetLastRect();
            Rect areaRect = new Rect(labelRect.xMin, labelRect.yMin, listRect.xMax, listRect.yMax);
            DropAreaGUI((x) => addItemToList(DCAList, x, Unique: true), areaRect, GUIContent.none, DropAreaStyle);
        }

        private static void DCAGUI(List<DynamicCharacterAvatar> DCAList, int i, DynamicCharacterAvatar DCA)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(DCA != null ? DCA.name : "Missing Avatar", EditorStyles.miniLabel, GUILayout.Width(120f));
            EditorGUILayout.ObjectField(DCA, typeof(DynamicCharacterAvatar), true, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                DCAList.RemoveAt(i);

            EditorGUILayout.EndHorizontal();
        }

        private bool addItemToList<T>(List<T> items, UnityEngine.Object dropedObject, bool Unique = false)
        {
            if (dropedObject is T)
            {
                T item = (T)(object)dropedObject;
                if (!(Unique && items.Contains(item))) items.Add(item);
                return true;
            }

            // Check if GameObject -> Component
            if (dropedObject is GameObject && typeof(MonoBehaviour).IsAssignableFrom(typeof(T)))
            {
                GameObject Go = (GameObject)dropedObject;
                T component = Go.GetComponent<T>();
                if (component != null)
                {
                    if (!(Unique && items.Contains(component))) items.Add(component);
                    return true;
                }
            }

            return false;
        }
    }
}
