#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UMA.EditorTools
{
    [CustomEditor(typeof(NewUMAGUI))]
    public class NewUMAGuiEditor : Editor
    {
        // ---- Foldout states (collapsed by default) ----
        private static bool _umaFoldout;
        private static bool _guiPrefabsFoldout;
        private static bool _cameraAnimFoldout;
        private static bool _testFoldout;
        private static bool _colorTablesFoldout;
        private static bool _dnaFoldout;
        private static bool _itemsFoldout;
        private static bool _containersFoldout;
        private static bool _buttonsFoldout;
        private static bool _miscFoldout;
        private static bool _timingFoldout;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeStatics()
        {
            _umaFoldout = false;
            _guiPrefabsFoldout = false;
            _cameraAnimFoldout = false;
            _testFoldout = false;
            _colorTablesFoldout = false;
            _dnaFoldout = false;
            _itemsFoldout = false;
            _containersFoldout = false;
            _buttonsFoldout = false;
            _miscFoldout = false;
            _timingFoldout = false;
        }

        // ---- Reorderable list cache ----
        private readonly Dictionary<string, ReorderableList> _listCache = new Dictionary<string, ReorderableList>();

        // ---- Serialized properties ----
        // UMA
        private SerializedProperty _avatarProp;
        private SerializedProperty _showConsoleProp;
        // GUI Prefabs
        private SerializedProperty _colorSelectorProp;
        private SerializedProperty _dnaAdjusterProp;
        private SerializedProperty _colorLabelProp;
        private SerializedProperty _gridContainerProp;
        private SerializedProperty _itemProp;
        private SerializedProperty _itemContainerProp;
        private SerializedProperty _infoTextProp;
        private SerializedProperty _logLabelProp;
        // Camera Animation
        private SerializedProperty _facePosProp;
        private SerializedProperty _legsPosProp;
        private SerializedProperty _bodyPosProp;
        private SerializedProperty _faceBoneNameProp;
        private SerializedProperty _legsBoneNameProp;
        private SerializedProperty _faceBoneOffsetProp;
        private SerializedProperty _legsBoneOffsetProp;
        private SerializedProperty _lerpSpeedProp;
        private SerializedProperty _lerpCurveProp;
        // Test
        private SerializedProperty _labelsProp;
        // Color Tables
        private SerializedProperty _faceColorsProp;
        private SerializedProperty _hairColorsProp;
        private SerializedProperty _legsColorsProp;
        private SerializedProperty _bodyColorsProp;
        // DNA
        private SerializedProperty _faceDNAProp;
        private SerializedProperty _hairDNAProp;
        private SerializedProperty _legsDNAProp;
        private SerializedProperty _bodyDNAProp;
        // Items
        private SerializedProperty _faceItemsProp;
        private SerializedProperty _hairItemsProp;
        private SerializedProperty _legsItemsProp;
        private SerializedProperty _bodyItemsProp;
        // Containers
        private SerializedProperty _dnaContainerProp;
        private SerializedProperty _itemsContainerProp;
        private SerializedProperty _logDetailContainerProp;
        // Buttons
        private SerializedProperty _faceButtonProp;
        private SerializedProperty _legsButtonProp;
        private SerializedProperty _bodyButtonProp;
        private SerializedProperty _hairButtonProp;
        private SerializedProperty _backButtonProp;
        private SerializedProperty _clearImageProp;
        private SerializedProperty _poseImageProp;
        // Misc
        private SerializedProperty _animatorsProp;
        private SerializedProperty _currentAnimatorProp;
        // Timing
        private SerializedProperty _showTimingButtonsProp;
        private SerializedProperty _bakeAllBlendShapesForTimingProp;

        private void OnEnable()
        {
            // UMA
            _avatarProp = serializedObject.FindProperty("avatar");
            _showConsoleProp = serializedObject.FindProperty("showConsole");
            // GUI Prefabs
            _colorSelectorProp = serializedObject.FindProperty("ColorSelector");
            _dnaAdjusterProp = serializedObject.FindProperty("DNAAdjuster");
            _colorLabelProp = serializedObject.FindProperty("ColorLabel");
            _gridContainerProp = serializedObject.FindProperty("GridContainer");
            _itemProp = serializedObject.FindProperty("Item");
            _itemContainerProp = serializedObject.FindProperty("ItemContainer");
            _infoTextProp = serializedObject.FindProperty("InfoText");
            _logLabelProp = serializedObject.FindProperty("LogLabel");
            // Camera Animation
            _facePosProp = serializedObject.FindProperty("FacePos");
            _legsPosProp = serializedObject.FindProperty("LegsPos");
            _bodyPosProp = serializedObject.FindProperty("BodyPos");
            _faceBoneNameProp = serializedObject.FindProperty("FaceBoneName");
            _legsBoneNameProp = serializedObject.FindProperty("LegsBoneName");
            _faceBoneOffsetProp = serializedObject.FindProperty("FaceBoneOffset");
            _legsBoneOffsetProp = serializedObject.FindProperty("LegsBoneOffset");
            _lerpSpeedProp = serializedObject.FindProperty("lerpSpeed");
            _lerpCurveProp = serializedObject.FindProperty("lerpCurve");
            // Test
            _labelsProp = serializedObject.FindProperty("Labels");
            // Color Tables
            _faceColorsProp = serializedObject.FindProperty("FaceColors");
            _hairColorsProp = serializedObject.FindProperty("HairColors");
            _legsColorsProp = serializedObject.FindProperty("LegsColors");
            _bodyColorsProp = serializedObject.FindProperty("BodyColors");
            // DNA
            _faceDNAProp = serializedObject.FindProperty("FaceDNA");
            _hairDNAProp = serializedObject.FindProperty("HairDNA");
            _legsDNAProp = serializedObject.FindProperty("LegsDNA");
            _bodyDNAProp = serializedObject.FindProperty("BodyDNA");
            // Items
            _faceItemsProp = serializedObject.FindProperty("FaceItems");
            _hairItemsProp = serializedObject.FindProperty("HairItems");
            _legsItemsProp = serializedObject.FindProperty("LegsItems");
            _bodyItemsProp = serializedObject.FindProperty("BodyItems");
            // Containers
            _dnaContainerProp = serializedObject.FindProperty("DNAContainer");
            _itemsContainerProp = serializedObject.FindProperty("ItemsContainer");
            _logDetailContainerProp = serializedObject.FindProperty("LogDetailContainer");
            // Buttons
            _faceButtonProp = serializedObject.FindProperty("FaceButton");
            _legsButtonProp = serializedObject.FindProperty("LegsButton");
            _bodyButtonProp = serializedObject.FindProperty("BodyButton");
            _hairButtonProp = serializedObject.FindProperty("HairButton");
            _backButtonProp = serializedObject.FindProperty("BackButton");
            _clearImageProp = serializedObject.FindProperty("clearImage");
            _poseImageProp = serializedObject.FindProperty("PoseImage");
            // Misc
            _animatorsProp = serializedObject.FindProperty("Animators");
            _currentAnimatorProp = serializedObject.FindProperty("currentAnimator");
            // Timing
            _showTimingButtonsProp = serializedObject.FindProperty("showTimingButtons");
            _bakeAllBlendShapesForTimingProp = serializedObject.FindProperty("bakeAllBlendShapesForTiming");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // --- Script field (read-only) ---
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"), true);
            }

            // --- UMA ---
            if (DrawFoldoutHeader("UMA", ref _umaFoldout))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_avatarProp);
                EditorGUILayout.PropertyField(_showConsoleProp);
                EditorGUI.indentLevel--;
            }

            // --- GUI Prefabs ---
            if (DrawFoldoutHeader("GUI Prefabs", ref _guiPrefabsFoldout))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_colorSelectorProp);
                EditorGUILayout.PropertyField(_dnaAdjusterProp);
                EditorGUILayout.PropertyField(_colorLabelProp);
                EditorGUILayout.PropertyField(_gridContainerProp);
                EditorGUILayout.PropertyField(_itemProp);
                EditorGUILayout.PropertyField(_itemContainerProp);
                EditorGUILayout.PropertyField(_infoTextProp);
                EditorGUILayout.PropertyField(_logLabelProp);
                EditorGUI.indentLevel--;
            }

            // --- Camera Animation ---
            if (DrawFoldoutHeader("Camera Animation", ref _cameraAnimFoldout))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_facePosProp);
                EditorGUILayout.PropertyField(_legsPosProp);
                EditorGUILayout.PropertyField(_bodyPosProp);
                EditorGUILayout.PropertyField(_faceBoneNameProp);
                EditorGUILayout.PropertyField(_legsBoneNameProp);
                EditorGUILayout.PropertyField(_faceBoneOffsetProp);
                EditorGUILayout.PropertyField(_legsBoneOffsetProp);
                EditorGUILayout.PropertyField(_lerpSpeedProp);
                EditorGUILayout.PropertyField(_lerpCurveProp);
                EditorGUI.indentLevel--;
            }

            // --- Test ---
            if (DrawFoldoutHeader("Test", ref _testFoldout))
            {
                DrawListWithButtons(_labelsProp, "Labels", typeof(string));
            }

            // --- Color Tables ---
            if (DrawFoldoutHeader("Color Tables", ref _colorTablesFoldout))
            {
                EditorGUI.indentLevel++;
                DrawListWithButtons(_faceColorsProp, "Face Colors", typeof(SharedColorTable));
                DrawListWithButtons(_hairColorsProp, "Hair Colors", typeof(SharedColorTable));
                DrawListWithButtons(_legsColorsProp, "Legs Colors", typeof(SharedColorTable));
                DrawListWithButtons(_bodyColorsProp, "Body Colors", typeof(SharedColorTable));
                EditorGUI.indentLevel--;
            }

            // --- DNA ---
            if (DrawFoldoutHeader("DNA", ref _dnaFoldout))
            {
                EditorGUI.indentLevel++;
                DrawListWithButtons(_faceDNAProp, "Face DNA", typeof(string), false);
                DrawListWithButtons(_hairDNAProp, "Hair DNA", typeof(string), false);
                DrawListWithButtons(_legsDNAProp, "Legs DNA", typeof(string), false);
                DrawListWithButtons(_bodyDNAProp, "Body DNA", typeof(string), false);

                EditorGUILayout.Space(3);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent(
                    "Remove Duplicates",
                    "Remove duplicate DNA names across every DNA section. The first occurrence in Face, Hair, Legs, then Body is preserved.")))
                {
                    RemoveAllDnaDuplicates();
                }
                if (GUILayout.Button(new GUIContent(
                    "Add Missing DNA",
                    "Choose one or more races, review DNA not currently shown by this control, and assign each entry to a section.")))
                {
                    serializedObject.ApplyModifiedProperties();
                    NewUMAGuiMissingDnaWindow.Open(
                        target as NewUMAGUI,
                        RefreshDnaProperties);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
            }

            // --- Items ---
            if (DrawFoldoutHeader("Items", ref _itemsFoldout))
            {
                EditorGUI.indentLevel++;
                DrawListWithButtons(_faceItemsProp, "Face Items", typeof(UMAWardrobeRecipe));
                DrawListWithButtons(_hairItemsProp, "Hair Items", typeof(UMAWardrobeRecipe));
                DrawListWithButtons(_legsItemsProp, "Legs Items", typeof(UMAWardrobeRecipe));
                DrawListWithButtons(_bodyItemsProp, "Body Items", typeof(UMAWardrobeRecipe));
                EditorGUI.indentLevel--;
            }

            // --- Containers ---
            if (DrawFoldoutHeader("Containers", ref _containersFoldout))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_dnaContainerProp);
                EditorGUILayout.PropertyField(_itemsContainerProp);
                EditorGUILayout.PropertyField(_logDetailContainerProp);
                EditorGUI.indentLevel--;
            }

            // --- Buttons ---
            if (DrawFoldoutHeader("Buttons", ref _buttonsFoldout))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_faceButtonProp);
                EditorGUILayout.PropertyField(_legsButtonProp);
                EditorGUILayout.PropertyField(_bodyButtonProp);
                EditorGUILayout.PropertyField(_hairButtonProp);
                EditorGUILayout.PropertyField(_backButtonProp);
                EditorGUILayout.PropertyField(_clearImageProp);
                EditorGUILayout.PropertyField(_poseImageProp);
                EditorGUI.indentLevel--;
            }

            // --- Misc ---
            if (DrawFoldoutHeader("Animation", ref _miscFoldout))
            {
                EditorGUI.indentLevel++;
                DrawListWithButtons(_animatorsProp, "Animators", typeof(RuntimeAnimatorController));
                EditorGUILayout.PropertyField(_currentAnimatorProp);
                EditorGUI.indentLevel--;
            }

            // --- Timing ---
            if (DrawFoldoutHeader("Timing", ref _timingFoldout))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_showTimingButtonsProp, new GUIContent("Show Timing Buttons"));
                EditorGUILayout.PropertyField(_bakeAllBlendShapesForTimingProp, new GUIContent("Bake All Blendshapes At 0.5"));
                if (_showTimingButtonsProp.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "When enabled, five IMGUI button rows appear in the Game View " +
                        "allowing you to time builds with each mesh combiner. Blendshapes are all loaded " +
                        "when baking is unchecked, or all baked at weight 0.5 when checked. " +
                        "Designed for testing in game builds.",
                        MessageType.Info);
                }
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }

        #region Foldout header

        private static bool DrawFoldoutHeader(string title, ref bool foldout)
        {
            EditorGUILayout.Space(2);
            foldout = EditorGUILayout.Foldout(foldout, title, true, EditorStyles.foldoutHeader);
            return foldout;
        }

        #endregion

        #region List drawing

        private void DrawListWithButtons(
            SerializedProperty listProp,
            string label,
            Type elementType,
            bool showRemoveDuplicates = true)
        {
            if (listProp == null) return;

            ReorderableList list = GetOrCreateList(listProp, label);
            list.DoLayoutList();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Sort", GUILayout.Width(60)))
            {
                SortList(listProp, elementType, label);
            }

            if (showRemoveDuplicates &&
                GUILayout.Button("Remove Duplicates", GUILayout.Width(130)))
            {
                RemoveDuplicates(listProp, elementType, label);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        private ReorderableList GetOrCreateList(SerializedProperty listProp, string label)
        {
            string key = listProp.propertyPath;
            if (_listCache.TryGetValue(key, out ReorderableList existing))
            {
                existing.serializedProperty = listProp;
                return existing;
            }

            var rl = new ReorderableList(serializedObject, listProp, true, true, true, true);
            rl.drawHeaderCallback = rect => EditorGUI.LabelField(rect, label);
            rl.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var prop = listProp.GetArrayElementAtIndex(index);
                if (prop == null) return;
                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;
                EditorGUI.PropertyField(rect, prop, GUIContent.none);
            };
            rl.elementHeight = EditorGUIUtility.singleLineHeight + 2;
            _listCache[key] = rl;
            return rl;
        }

        #endregion

        #region Sort

        private void SortList(SerializedProperty listProp, Type elementType, string label)
        {
            int count = listProp.arraySize;
            if (count <= 1) return;

            if (elementType == typeof(string))
            {
                SortStringList(listProp, label);
            }
            else
            {
                SortObjectList(listProp, label);
            }
        }

        private void SortStringList(SerializedProperty listProp, string label)
        {
            int count = listProp.arraySize;
            var values = new string[count];
            for (int i = 0; i < count; i++)
                values[i] = listProp.GetArrayElementAtIndex(i).stringValue ?? "";

            Array.Sort(values, StringComparer.OrdinalIgnoreCase);

            Undo.RecordObject(target, "Sort " + label);
            for (int i = 0; i < count; i++)
                listProp.GetArrayElementAtIndex(i).stringValue = values[i];

            ApplyAndRefresh();
        }

        private void SortObjectList(SerializedProperty listProp, string label)
        {
            int count = listProp.arraySize;
            var objects = new UnityEngine.Object[count];
            for (int i = 0; i < count; i++)
                objects[i] = listProp.GetArrayElementAtIndex(i).objectReferenceValue;

            Array.Sort(objects, (a, b) =>
            {
                string na = a != null ? a.name : "";
                string nb = b != null ? b.name : "";
                return string.Compare(na, nb, StringComparison.OrdinalIgnoreCase);
            });

            Undo.RecordObject(target, "Sort " + label);
            listProp.arraySize = count;
            for (int i = 0; i < count; i++)
                listProp.GetArrayElementAtIndex(i).objectReferenceValue = objects[i];

            ApplyAndRefresh();
        }

        #endregion

        #region Remove Duplicates

        private void RemoveDuplicates(SerializedProperty listProp, Type elementType, string label)
        {
            if (listProp.arraySize <= 1) return;

            if (elementType == typeof(string))
            {
                RemoveStringDuplicates(listProp, label);
            }
            else
            {
                RemoveObjectDuplicates(listProp, label);
            }
        }

        private void RemoveStringDuplicates(SerializedProperty listProp, string label)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keep = new List<string>();

            for (int i = 0; i < listProp.arraySize; i++)
            {
                string val = listProp.GetArrayElementAtIndex(i).stringValue ?? "";
                if (seen.Add(val))
                    keep.Add(val);
            }

            if (keep.Count == listProp.arraySize) return;

            Undo.RecordObject(target, "Remove duplicates " + label);
            listProp.arraySize = keep.Count;
            for (int i = 0; i < keep.Count; i++)
                listProp.GetArrayElementAtIndex(i).stringValue = keep[i];

            ApplyAndRefresh();
        }

        private void RemoveObjectDuplicates(SerializedProperty listProp, string label)
        {
            var seen = new HashSet<string>();
            var keep = new List<UnityEngine.Object>();

            for (int i = 0; i < listProp.arraySize; i++)
            {
                var obj = listProp.GetArrayElementAtIndex(i).objectReferenceValue;
                string key = GetDedupeKey(obj);
                if (seen.Add(key))
                    keep.Add(obj);
            }

            if (keep.Count == listProp.arraySize) return;

            Undo.RecordObject(target, "Remove duplicates " + label);
            listProp.arraySize = keep.Count;
            for (int i = 0; i < keep.Count; i++)
                listProp.GetArrayElementAtIndex(i).objectReferenceValue = keep[i];

            ApplyAndRefresh();
        }

        private static string GetDedupeKey(UnityEngine.Object obj)
        {
            if (obj == null) return "__null__";
            string path = AssetDatabase.GetAssetPath(obj);
            return string.IsNullOrEmpty(path) ? obj.GetUmaObjectId().ToString() : path;
        }

        #endregion

        #region DNA maintenance

        private void RemoveAllDnaDuplicates()
        {
            serializedObject.ApplyModifiedProperties();

            NewUMAGUI gui = target as NewUMAGUI;
            if (gui == null)
            {
                return;
            }

            Undo.RecordObject(gui, "Remove duplicate UMA GUI DNA");
            int removed = RemoveDnaDuplicates(
                gui.FaceDNA,
                gui.HairDNA,
                gui.LegsDNA,
                gui.BodyDNA);

            if (removed == 0)
            {
                return;
            }

            RecordDnaChanges(gui);
            RefreshDnaProperties();
            Debug.Log(
                $"Removed {removed} duplicate DNA entr{(removed == 1 ? "y" : "ies")} " +
                $"from '{gui.name}'.",
                gui);
        }

        internal static int RemoveDnaDuplicates(params List<string>[] dnaLists)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int removed = 0;

            if (dnaLists == null)
            {
                return removed;
            }

            for (int listIndex = 0; listIndex < dnaLists.Length; listIndex++)
            {
                List<string> dnaList = dnaLists[listIndex];
                if (dnaList == null)
                {
                    continue;
                }

                for (int dnaIndex = 0; dnaIndex < dnaList.Count;)
                {
                    string dnaName = dnaList[dnaIndex] ?? string.Empty;
                    if (seen.Add(dnaName))
                    {
                        dnaIndex++;
                        continue;
                    }

                    dnaList.RemoveAt(dnaIndex);
                    removed++;
                }
            }

            return removed;
        }

        internal static void RecordDnaChanges(NewUMAGUI gui)
        {
            if (gui == null)
            {
                return;
            }

            EditorUtility.SetDirty(gui);
            if (PrefabUtility.IsPartOfPrefabInstance(gui))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(gui);
            }
        }

        private void RefreshDnaProperties()
        {
            serializedObject.Update();
            _listCache.Clear();
            GUI.changed = true;
            Repaint();
        }

        #endregion

        #region Utility

        private void ApplyAndRefresh()
        {
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            GUI.changed = true;
            Repaint();
        }

        #endregion
    }

    internal sealed class NewUMAGuiMissingDnaWindow : EditorWindow
    {
        private enum WorkflowStep
        {
            SelectRaces,
            AssignSections
        }

        private enum DnaSection
        {
            Face,
            Hair,
            Legs,
            Body
        }

        private sealed class RaceChoice
        {
            public RaceData race;
            public bool selected;
        }

        private sealed class MissingDnaChoice
        {
            public string dnaName;
            public readonly List<string> raceNames = new List<string>();
            public bool add = true;
            public DnaSection section;
        }

        private NewUMAGUI targetGui;
        private Action onApplied;
        private readonly List<RaceChoice> raceChoices = new List<RaceChoice>();
        private readonly List<MissingDnaChoice> missingDna = new List<MissingDnaChoice>();
        private WorkflowStep step;
        private Vector2 scrollPosition;
        private string raceFilter = string.Empty;
        private string dnaFilter = string.Empty;

        internal static void Open(NewUMAGUI gui, Action onApplied)
        {
            if (gui == null)
            {
                return;
            }

            NewUMAGuiMissingDnaWindow window =
                CreateInstance<NewUMAGuiMissingDnaWindow>();
            window.titleContent = new GUIContent("Add Missing UMA DNA");
            window.minSize = new Vector2(620f, 420f);
            window.targetGui = gui;
            window.onApplied = onApplied;
            window.LoadRaces();
            window.ShowUtility();
        }

        private void OnGUI()
        {
            if (targetGui == null)
            {
                EditorGUILayout.HelpBox(
                    "The NewUMAGUI target is no longer available.",
                    MessageType.Warning);
                if (GUILayout.Button("Close"))
                {
                    Close();
                }
                return;
            }

            switch (step)
            {
                case WorkflowStep.SelectRaces:
                    DrawRaceSelection();
                    break;
                case WorkflowStep.AssignSections:
                    DrawSectionAssignment();
                    break;
            }
        }

        private void LoadRaces()
        {
            raceChoices.Clear();

            string[] raceGuids = AssetDatabase.FindAssets("t:RaceData");
            var seen = new HashSet<RaceData>();
            for (int i = 0; i < raceGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(raceGuids[i]);
                RaceData race = AssetDatabase.LoadAssetAtPath<RaceData>(path);
                if (race == null || !seen.Add(race))
                {
                    continue;
                }

                raceChoices.Add(new RaceChoice
                {
                    race = race,
                    selected = IsCurrentAvatarRace(race)
                });
            }

            raceChoices.Sort((left, right) =>
            {
                string leftName = GetRaceDisplayName(left.race);
                string rightName = GetRaceDisplayName(right.race);
                return string.Compare(
                    leftName,
                    rightName,
                    StringComparison.OrdinalIgnoreCase);
            });

            if (raceChoices.Count == 1)
            {
                raceChoices[0].selected = true;
            }
        }

        private bool IsCurrentAvatarRace(RaceData race)
        {
            return targetGui != null &&
                targetGui.avatar != null &&
                targetGui.avatar.activeRace != null &&
                targetGui.avatar.activeRace.data == race;
        }

        private void DrawRaceSelection()
        {
            EditorGUILayout.LabelField(
                "Select Races",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Choose the races this control must support. UMA will combine their DNA names and compare them with all four DNA sections.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All", GUILayout.Width(90f)))
            {
                SetAllRacesSelected(true);
            }
            if (GUILayout.Button("Select None", GUILayout.Width(90f)))
            {
                SetAllRacesSelected(false);
            }
            GUILayout.FlexibleSpace();
            raceFilter = EditorGUILayout.TextField(
                new GUIContent("Filter"),
                raceFilter,
                GUILayout.Width(260f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3f);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            int shown = 0;
            for (int i = 0; i < raceChoices.Count; i++)
            {
                RaceChoice choice = raceChoices[i];
                string displayName = GetRaceDisplayName(choice.race);
                if (!MatchesFilter(displayName, raceFilter))
                {
                    continue;
                }

                shown++;
                choice.selected = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        displayName,
                        AssetDatabase.GetAssetPath(choice.race)),
                    choice.selected);
            }
            EditorGUILayout.EndScrollView();

            if (raceChoices.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No RaceData assets were found in the project.",
                    MessageType.Warning);
            }
            else if (shown == 0)
            {
                EditorGUILayout.HelpBox(
                    "No races match the current filter.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(
                !raceChoices.Any(choice => choice.selected)))
            {
                if (GUILayout.Button("Find Missing DNA", GUILayout.Width(150f)))
                {
                    BuildMissingDnaList();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSectionAssignment()
        {
            EditorGUILayout.LabelField(
                "Assign Missing DNA",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Choose which missing DNA entries to add and select the NewUMAGUI section that should display each one. Suggested sections are only a starting point.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All", GUILayout.Width(90f)))
            {
                SetAllMissingSelected(true);
            }
            if (GUILayout.Button("Select None", GUILayout.Width(90f)))
            {
                SetAllMissingSelected(false);
            }
            DrawSetAllSectionMenu();
            GUILayout.FlexibleSpace();
            dnaFilter = EditorGUILayout.TextField(
                new GUIContent("Filter"),
                dnaFilter,
                GUILayout.Width(260f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3f);
            DrawMissingDnaHeader();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            int shown = 0;
            for (int i = 0; i < missingDna.Count; i++)
            {
                MissingDnaChoice choice = missingDna[i];
                string raceSummary = string.Join(", ", choice.raceNames);
                if (!MatchesFilter(choice.dnaName, dnaFilter) &&
                    !MatchesFilter(raceSummary, dnaFilter))
                {
                    continue;
                }

                shown++;
                EditorGUILayout.BeginHorizontal();
                choice.add = EditorGUILayout.Toggle(
                    choice.add,
                    GUILayout.Width(24f));
                EditorGUILayout.LabelField(
                    new GUIContent(choice.dnaName, raceSummary),
                    GUILayout.MinWidth(220f));
                choice.section = (DnaSection)EditorGUILayout.EnumPopup(
                    choice.section,
                    GUILayout.Width(100f));
                EditorGUILayout.LabelField(
                    raceSummary,
                    EditorStyles.miniLabel,
                    GUILayout.MinWidth(180f));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            if (missingDna.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "The selected races do not contain any DNA missing from this control.",
                    MessageType.Info);
            }
            else if (shown == 0)
            {
                EditorGUILayout.HelpBox(
                    "No missing DNA matches the current filter.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Back", GUILayout.Width(90f)))
            {
                step = WorkflowStep.SelectRaces;
                scrollPosition = Vector2.zero;
            }
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(
                !missingDna.Any(choice => choice.add)))
            {
                if (GUILayout.Button("Add Selected DNA", GUILayout.Width(150f)))
                {
                    AddSelectedDna();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMissingDnaHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Space(24f);
            EditorGUILayout.LabelField(
                "DNA Name",
                EditorStyles.miniBoldLabel,
                GUILayout.MinWidth(220f));
            EditorGUILayout.LabelField(
                "Section",
                EditorStyles.miniBoldLabel,
                GUILayout.Width(100f));
            EditorGUILayout.LabelField(
                "Used By Race",
                EditorStyles.miniBoldLabel,
                GUILayout.MinWidth(180f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSetAllSectionMenu()
        {
            if (!GUILayout.Button(
                new GUIContent("Set Selected Section"),
                EditorStyles.miniButton,
                GUILayout.Width(145f)))
            {
                return;
            }

            var menu = new GenericMenu();
            foreach (DnaSection section in Enum.GetValues(typeof(DnaSection)))
            {
                DnaSection capturedSection = section;
                menu.AddItem(
                    new GUIContent(section.ToString()),
                    false,
                    () => SetSelectedMissingSection(capturedSection));
            }
            menu.ShowAsContext();
        }

        private void SetAllRacesSelected(bool selected)
        {
            for (int i = 0; i < raceChoices.Count; i++)
            {
                raceChoices[i].selected = selected;
            }
        }

        private void SetAllMissingSelected(bool selected)
        {
            for (int i = 0; i < missingDna.Count; i++)
            {
                missingDna[i].add = selected;
            }
        }

        private void SetSelectedMissingSection(DnaSection section)
        {
            for (int i = 0; i < missingDna.Count; i++)
            {
                if (missingDna[i].add)
                {
                    missingDna[i].section = section;
                }
            }
        }

        private void BuildMissingDnaList()
        {
            missingDna.Clear();
            var existing = new HashSet<string>(
                EnumerateCurrentDna(),
                StringComparer.OrdinalIgnoreCase);
            var choicesByName = new Dictionary<string, MissingDnaChoice>(
                StringComparer.OrdinalIgnoreCase);

            for (int raceIndex = 0; raceIndex < raceChoices.Count; raceIndex++)
            {
                RaceChoice raceChoice = raceChoices[raceIndex];
                if (!raceChoice.selected || raceChoice.race == null)
                {
                    continue;
                }

                List<string> raceDna = raceChoice.race.GetDNANames();
                if (raceDna == null)
                {
                    continue;
                }

                string raceName = GetRaceDisplayName(raceChoice.race);
                for (int dnaIndex = 0; dnaIndex < raceDna.Count; dnaIndex++)
                {
                    string dnaName = raceDna[dnaIndex];
                    if (string.IsNullOrWhiteSpace(dnaName) ||
                        existing.Contains(dnaName))
                    {
                        continue;
                    }

                    if (!choicesByName.TryGetValue(
                        dnaName,
                        out MissingDnaChoice choice))
                    {
                        choice = new MissingDnaChoice
                        {
                            dnaName = dnaName,
                            section = GuessSection(dnaName)
                        };
                        choicesByName.Add(dnaName, choice);
                        missingDna.Add(choice);
                    }

                    if (!choice.raceNames.Contains(raceName))
                    {
                        choice.raceNames.Add(raceName);
                    }
                }
            }

            missingDna.Sort((left, right) =>
                string.Compare(
                    left.dnaName,
                    right.dnaName,
                    StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < missingDna.Count; i++)
            {
                missingDna[i].raceNames.Sort(StringComparer.OrdinalIgnoreCase);
            }

            step = WorkflowStep.AssignSections;
            scrollPosition = Vector2.zero;
        }

        private IEnumerable<string> EnumerateCurrentDna()
        {
            if (targetGui.FaceDNA != null)
            {
                foreach (string dna in targetGui.FaceDNA)
                {
                    yield return dna ?? string.Empty;
                }
            }
            if (targetGui.HairDNA != null)
            {
                foreach (string dna in targetGui.HairDNA)
                {
                    yield return dna ?? string.Empty;
                }
            }
            if (targetGui.LegsDNA != null)
            {
                foreach (string dna in targetGui.LegsDNA)
                {
                    yield return dna ?? string.Empty;
                }
            }
            if (targetGui.BodyDNA != null)
            {
                foreach (string dna in targetGui.BodyDNA)
                {
                    yield return dna ?? string.Empty;
                }
            }
        }

        private void AddSelectedDna()
        {
            Undo.RecordObject(targetGui, "Add missing UMA GUI DNA");
            var existing = new HashSet<string>(
                EnumerateCurrentDna(),
                StringComparer.OrdinalIgnoreCase);
            int added = 0;

            for (int i = 0; i < missingDna.Count; i++)
            {
                MissingDnaChoice choice = missingDna[i];
                if (!choice.add || !existing.Add(choice.dnaName))
                {
                    continue;
                }

                GetSectionList(choice.section).Add(choice.dnaName);
                added++;
            }

            if (added > 0)
            {
                NewUMAGuiEditor.RecordDnaChanges(targetGui);
                onApplied?.Invoke();
                Debug.Log(
                    $"Added {added} missing DNA entr{(added == 1 ? "y" : "ies")} " +
                    $"to '{targetGui.name}'.",
                    targetGui);
            }

            Close();
        }

        private List<string> GetSectionList(DnaSection section)
        {
            switch (section)
            {
                case DnaSection.Face:
                    if (targetGui.FaceDNA == null)
                    {
                        targetGui.FaceDNA = new List<string>();
                    }
                    return targetGui.FaceDNA;
                case DnaSection.Hair:
                    if (targetGui.HairDNA == null)
                    {
                        targetGui.HairDNA = new List<string>();
                    }
                    return targetGui.HairDNA;
                case DnaSection.Legs:
                    if (targetGui.LegsDNA == null)
                    {
                        targetGui.LegsDNA = new List<string>();
                    }
                    return targetGui.LegsDNA;
                default:
                    if (targetGui.BodyDNA == null)
                    {
                        targetGui.BodyDNA = new List<string>();
                    }
                    return targetGui.BodyDNA;
            }
        }

        private static DnaSection GuessSection(string dnaName)
        {
            string lowerName = (dnaName ?? string.Empty).ToLowerInvariant();
            if (ContainsAny(lowerName, "hair", "brow", "beard", "mustache"))
            {
                return DnaSection.Hair;
            }
            if (ContainsAny(
                lowerName,
                "leg",
                "thigh",
                "calf",
                "knee",
                "ankle",
                "foot",
                "feet"))
            {
                return DnaSection.Legs;
            }
            if (ContainsAny(
                lowerName,
                "head",
                "face",
                "forehead",
                "eye",
                "ear",
                "nose",
                "mouth",
                "lip",
                "jaw",
                "chin",
                "cheek"))
            {
                return DnaSection.Face;
            }
            return DnaSection.Body;
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            for (int i = 0; i < terms.Length; i++)
            {
                if (value.Contains(terms[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool MatchesFilter(string value, string filter)
        {
            return string.IsNullOrWhiteSpace(filter) ||
                (!string.IsNullOrEmpty(value) &&
                    value.IndexOf(
                        filter,
                        StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GetRaceDisplayName(RaceData race)
        {
            if (race == null)
            {
                return "(Missing Race)";
            }
            return string.IsNullOrWhiteSpace(race.raceName)
                ? race.name
                : race.raceName;
        }
    }
}
#endif
