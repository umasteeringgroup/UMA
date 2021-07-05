using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;
using System;
using static UMA.Editors.GUIHelper;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMARandomAvatarV2))]
    public class UMARandomAvatarV2Editor : Editor
    {
        UMARandomAvatarV2 currentTarget = null;     // Randomizer Inspector target

        SerializedProperty generationProp;

        public static class ContextMenu
        {
            private const string MenuName = "CONTEXT/UMARandomAvatarV2/Show Help";
            private const string SettingName = "UMARandomAvatarV2ShowHelp";

            public static bool IsEnabled
            {
                get { return EditorPrefs.GetBool(SettingName, true); }
                set { EditorPrefs.SetBool(SettingName, value); ToggleActionValidate(); }
            }


            [MenuItem(MenuName, priority = 101)]
            private static void ToggleAction()
            {
                IsEnabled = !IsEnabled;
            }

            [MenuItem(MenuName, true, priority = 101)]
            private static bool ToggleActionValidate()
            {
                Menu.SetChecked(MenuName, IsEnabled);
                return true;
            }
        }

        private static class Tooltips
        {
            internal static GUIContent CharacterRandomizers = new GUIContent("Character Randomizers", "When randomizing, a Randomizer is picked randomly in CharacterRandomizer's list.\nThen a randomAvatar is picked in that random avatar using RandomAvatar weights, unless \"Keep Existing Race\".");
            internal static GUIContent CharacterEmpty = new GUIContent("Drag and Drop Randomizers to add");
            internal static GUIContent WardrobeRandomizers = new GUIContent("Wardrobe Randomizers", "A Randomizer is randomly picked in Wardrobe's Randomizer's list and applied after Character Randomizer.\nOnly RandomAvatars matching Character's Race are picked");
            internal static GUIContent WardrobeEmpty = new GUIContent("Drag and Drop Randomizers to add");

            internal static GUIContent KeepRace = new GUIContent("Keep existing Race", "Will keep existing race when randomized. \nOnly RandomAvatars of existing race will used during the randomization.");
            internal static GUIContent KeepWardrobe = new GUIContent("Keep existing Wardrobe", "Already equiped wardrobe recipes are not cleared before applying randomizers. \nBest used in conjunction with null random wardrobes in RandomAvatar definitions to keep total probability per slot at 100%.");
            internal static GUIContent RandomizeRace = new GUIContent("Randomize existing Race", "Race will be randomized using RandomAvatars.");
            internal static GUIContent ClearWardrobe = new GUIContent("Clear existing Wardrobe", "Wardrobe slots are cleared before randomizing (either Characters or Wardrobes).");
            internal static GUIContent DCAReferences = new GUIContent("Scene DCAs :", "Drag and drop Dynamic Character Avatars references from the scene.");
            internal static GUIContent DCAReferencesEmpty = new GUIContent("Drag and Drop DynamicCharacterAvatars from the scene.");
            internal static string[] Help = new string[5] {
                "UMA Random Avatar V2 allows more flexibility with the randomizations :",
                "1° - Use \"Character Randomizers\" to define how characters traits are randomized. For example, use feature to create factions, clans or groups with distinctive traits like skin color, height, or even hair cuts",
                "2° - Use the \"Wardrobe Randomizers\" to define any additional traits or wardrobes to apply on top (after) of the Character Randomizer. For Example, use feature to create random armor sets with color variations.",
                "> Use \"Keep Existing Race\" option to keep already set Race. Use it to avoid un-equipping Race specific recipes when randomizing.",
                "> Use \"Keep Existing Wardrobe\" option to avoid clearing recipes. Use it to randomize Character without losing it's Wardrobe. Use \"Clear Existing Wardrobe\" to make sure all wardrobes slots are cleared before Randomizing Wardrobe (Alternatively, define null slots in those Randomizers)."
            };
        }


        protected void OnEnable()
        {
            // -- Inspector Vars --
            currentTarget = target as UMARandomAvatarV2;

            generationProp = serializedObject.FindProperty("Generation");
        }


        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Rect helpIconRect = ContextMenu.IsEnabled ? new Rect(EditorGUIUtility.currentViewWidth - 30f, 9f, 30f, 35f) : new Rect(EditorGUIUtility.currentViewWidth - 35f, 5f, 35f, 35f);

            ContextMenu.IsEnabled = HelpGUI(helpIconRect, Tooltips.Help, ContextMenu.IsEnabled);

            RandomizerListGUI(currentTarget.CharacterRandomizers, Tooltips.CharacterRandomizers, Tooltips.CharacterEmpty);
            RandomizerListGUI(currentTarget.WardrobeRandomizers, Tooltips.WardrobeRandomizers, Tooltips.WardrobeEmpty);

            Separator();

            GlobalOptionsGUI();

            BeginVerticalPadded(3f, new Color(0.75f, 0.75f, 0.75f));
            ModeTabsGUI();
            GUILayout.Space(3f);

            switch (currentTarget.mode)
            {
                case UMARandomAvatarV2.Mode.Generate:
                    GenerateOptionsGUI();
                    break;
                case UMARandomAvatarV2.Mode.UseExisting:
                    ExistingDCAListGUI(currentTarget.ExistingDCAs, Tooltips.DCAReferences, Tooltips.DCAReferencesEmpty);
                    break;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Randomize Characters")) currentTarget.RandomizeCharacterButton();
            if (GUILayout.Button("Randomize Wardrobe")) currentTarget.RandomizeWardrobeButton();
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Randomize")) currentTarget.RandomizeButton();

            EndVerticalPadded(3f);

            serializedObject.ApplyModifiedProperties();
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
            Rect areaRect = new Rect(labelRect.xMin, labelRect.yMin, listRect.xMax, listRect.yMax);
            DropAreaGUI((x) => addItemToList(randomizersList, x), areaRect, new GUIContent("", ""), new GUIStyle(GUI.skin.label));
        }

        private static void RandomizerGUI(List<UMARandomizer> randomizersList, int i, UMARandomizer randomizer)
        {
            EditorGUILayout.BeginHorizontal();
            if (randomizer.useDefinition)
                EditorGUILayout.LabelField(randomizer.Definition.Name, EditorStyles.miniLabel, GUILayout.Width(120f));
            EditorGUILayout.ObjectField(randomizer, typeof(UMARandomizer), false, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                randomizersList.RemoveAt(i);

            EditorGUILayout.EndHorizontal();
        }

        private void GlobalOptionsGUI()
        {
            BeginVerticalPadded(2f, new Color(0.75f, 0.75f, 0.75f));

            Rect hRect = EditorGUILayout.GetControlRect();

            GUIStyle keepRaceStyle = currentTarget.KeepExistingRace ? Styles.ToggleButtonToggled : Styles.ToggleButtonNormal;
            GUIContent keepRaceGUI = currentTarget.KeepExistingRace ? Tooltips.KeepRace : Tooltips.RandomizeRace;
            Rect keepRaceRect = new Rect(hRect.xMin, hRect.yMin, hRect.width / 2 - 2.5f, hRect.height);

            GUIStyle keepWardrobeStyle = currentTarget.KeepExistingWardrobe ? Styles.ToggleButtonToggled : Styles.ToggleButtonNormal;
            GUIContent keepWardrobeGUI = currentTarget.KeepExistingWardrobe ? Tooltips.KeepWardrobe : Tooltips.ClearWardrobe;
            Rect keepWardrobeRect = new Rect(keepRaceRect.xMax + 5f, hRect.yMin, hRect.width / 2 - 2.5f, hRect.height);

            EditorGUI.BeginChangeCheck();
            bool keepRace = EditorGUI.Toggle(keepRaceRect, currentTarget.KeepExistingRace, keepRaceStyle);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(currentTarget, "Toggle Keep Race");
                currentTarget.KeepExistingRace = keepRace;
            }

            EditorGUI.BeginChangeCheck();
            bool keepWardrobe = EditorGUI.Toggle(keepWardrobeRect, currentTarget.KeepExistingWardrobe, keepWardrobeStyle);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(currentTarget, "Toggle Keep Wardrobe");
                currentTarget.KeepExistingWardrobe = keepWardrobe;
            }

            EditorGUI.LabelField(keepRaceRect, keepRaceGUI, keepRaceStyle);
            EditorGUI.LabelField(keepWardrobeRect, keepWardrobeGUI, keepWardrobeStyle);

            if (EditorGUI.EndChangeCheck())
            {
                currentTarget.KeepExistingRace = keepRace;
                currentTarget.KeepExistingWardrobe = keepWardrobe;
            }

            EndVerticalPadded(2f);
        }

        //Draws the 'View' tabs allowing the user to switch between viewing data 'By Plugin' or 'By DNA'
        private void ModeTabsGUI()
        {
            //Tabs for viewing by modifier or by dna
            var tabsRect = EditorGUILayout.GetControlRect();
            var tabsLabel = new Rect(tabsRect.xMin, tabsRect.yMin, 60f, tabsRect.height);
            var tabsButRect = new Rect(tabsLabel.xMax, tabsRect.yMin, (tabsRect.width - tabsLabel.width), tabsRect.height);

            EditorGUI.LabelField(tabsLabel, "Mode:", EditorStyles.toolbarButton);

            var modeVal = (int)currentTarget.mode;
            EditorGUI.BeginChangeCheck();
            modeVal = GUI.Toolbar(tabsButRect, modeVal, Enum.GetNames(typeof(UMARandomAvatarV2.Mode)), EditorStyles.toolbarButton);
            if (EditorGUI.EndChangeCheck())
            {
                currentTarget.mode = (UMARandomAvatarV2.Mode)modeVal;
            }
        }

        private void GenerateOptionsGUI()
        {
            EditorGUILayout.PropertyField(generationProp, GUIContent.none);
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
            DropAreaGUI((x) => addItemToList(DCAList, x, Unique: true), areaRect, new GUIContent("", ""), new GUIStyle(GUI.skin.label));
        }

        private static void DCAGUI(List<DynamicCharacterAvatar> DCAList, int i, DynamicCharacterAvatar DCA)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(DCA.name, EditorStyles.miniLabel, GUILayout.Width(120f));
            EditorGUILayout.ObjectField(DCA, typeof(UMARandomizer), false, GUILayout.ExpandWidth(true));
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
