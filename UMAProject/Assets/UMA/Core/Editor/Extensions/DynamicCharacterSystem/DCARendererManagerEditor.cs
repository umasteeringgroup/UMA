using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA.CharacterSystem;

namespace UMA.Editors
{
    [CustomEditor(typeof(DCARendererManager))]
    public class DCARendererManagerEditor : Editor
    {
        readonly UMAInspectorView inspectorView =
            new UMAInspectorView(typeof(DCARendererManager));
        SerializedProperty RendererElements;
        SerializedProperty showHelp;
        SerializedProperty State;

        DynamicCharacterAvatar avatar;
        UMAData.UMARecipe umaRecipe = new UMAData.UMARecipe();
        List<string> wardrobeOptions = new List<string>();
        List<SlotDataAsset> slotOptions = new List<SlotDataAsset>();
        RaceData currentRaceData;

        void OnEnable()
        {
            State = serializedObject.FindProperty("RenderersEnabled");

            RendererElements = serializedObject.FindProperty("RendererElements");
            showHelp = serializedObject.FindProperty("showHelp");

            DCARendererManager manager = target as DCARendererManager;
            avatar = manager.GetComponent<DynamicCharacterAvatar>();

            UpdateOptions();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (avatar != null && avatar.activeRace != null && avatar.activeRace.data != null)
            {
                if (currentRaceData != avatar.activeRace.data)
                {
                    UpdateOptions();
                }
            }

            bool advanced = inspectorView.DrawSelector();
            using (inspectorView.Section("Renderer routing",
                "Renderers Enabled activates renderer separation for this avatar. Each element sends matching source slots or wardrobe regions to every Renderer Asset in that element. A source may deliberately appear in multiple elements for effects that need duplicate renderers."))
            {
                EditorGUILayout.PropertyField(State,
                    new GUIContent("Renderers Enabled"));
                if (advanced)
                    EditorGUILayout.PropertyField(showHelp,
                        new GUIContent("Legacy Help Flag"));
            }

            if (advanced)
            {
                using (inspectorView.Section("Renderer element data",
                    "Advanced View exposes the serialized renderer-element list directly. Renderer Assets are output configurations, Slot Assets are explicit base-slot matches, and Regions match the active race's wardrobe-region names."))
                    EditorGUILayout.PropertyField(RendererElements, true);
                DrawValidation();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            using (inspectorView.Section("Slot and region selection",
                "Add an element for each distinct renderer route. Assign at least one Renderer Asset, then choose base slots and wardrobe regions. The pickers are populated from the avatar's active race; the lists below retain the exact serialized selections."))
                DrawRendererElements();

            DrawValidation();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRendererElements()
        {

            if(GUILayout.Button("Add New Renderer Element Set"))
            {
                RendererElements.arraySize++;
                ClearRendererElement(RendererElements.GetArrayElementAtIndex(RendererElements.arraySize - 1));                
            }
            GUILayout.Space(20);

            for(int i = RendererElements.arraySize - 1; i >= 0; i--)
            {
                EditorGUILayout.BeginVertical("HelpBox");
                SerializedProperty wardrobeSlots = RendererElements.GetArrayElementAtIndex(i).FindPropertyRelative("Regions");
                SerializedProperty slotAssets = RendererElements.GetArrayElementAtIndex(i).FindPropertyRelative("slotAssets");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Renderer Element " + i.ToString());
                if(GUILayout.Button("Remove"))
                {
                    RendererElements.DeleteArrayElementAtIndex(i);
                    continue;
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(10);

                EditorGUI.indentLevel++;
                SerializedProperty rendererAssets = RendererElements.GetArrayElementAtIndex(i).FindPropertyRelative("rendererAssets");
                EditorGUILayout.PropertyField(rendererAssets, true);
                GUILayout.Space(10);
                bool disable = false;

                if (rendererAssets.arraySize <= 0)
                {
                    disable = true;
                    EditorGUILayout.HelpBox("An UMARendererAsset needs to be assigned!", MessageType.Error);
                    GUILayout.Space(10);
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(disable);
                int newSlot = EditorGUILayout.Popup(0, SlotOptionsToArray(slotOptions));
                if (newSlot > 0)
                {
                    slotAssets.arraySize++;
                    slotAssets.GetArrayElementAtIndex(slotAssets.arraySize - 1).objectReferenceValue = slotOptions[newSlot - 1];
                }

                int newWardrobe = EditorGUILayout.Popup(0, wardrobeOptions.ToArray());
                if (newWardrobe > 0)
                {
                    if (!ArrayContains(wardrobeSlots, wardrobeOptions[newWardrobe]))
                    {
                        wardrobeSlots.arraySize++;
                        wardrobeSlots.GetArrayElementAtIndex(wardrobeSlots.arraySize - 1).stringValue = wardrobeOptions[newWardrobe];
                    }
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(10);

                EditorGUILayout.PropertyField(slotAssets, true);
                EditorGUILayout.PropertyField(wardrobeSlots, true);

                EditorGUI.indentLevel--;

                bool unassigned = false;
                for(int k = 0; k < rendererAssets.arraySize; k++)
                {
                    if(rendererAssets.GetArrayElementAtIndex(k).objectReferenceValue == null)
                    {
                        unassigned = true;
                        break;
                    }
                }
                if(unassigned)
                {
                    EditorGUILayout.HelpBox("There are unassigned UMARendererAssets!", MessageType.Error);
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(10);
            }
        }

        private void DrawValidation()
        {
            using (inspectorView.Section("Validation and refresh",
                "Validation reports missing renderer outputs and unavailable race context. Refresh Choices reloads base slots and wardrobe regions after changing the avatar's race or base recipe."))
            {
                if (avatar == null)
                    EditorGUILayout.HelpBox(
                        "This component must be on the same GameObject as a Dynamic Character Avatar.",
                        MessageType.Error);
                else if (avatar.activeRace == null ||
                    avatar.activeRace.data == null)
                    EditorGUILayout.HelpBox(
                        "Choose an active race to populate slot and wardrobe-region choices.",
                        MessageType.Warning);
                int missing = 0;
                for (int i = 0; i < RendererElements.arraySize; i++)
                {
                    SerializedProperty renderers = RendererElements
                        .GetArrayElementAtIndex(i)
                        .FindPropertyRelative("rendererAssets");
                    if (renderers.arraySize == 0) missing++;
                    for (int r = 0; r < renderers.arraySize; r++)
                        if (renderers.GetArrayElementAtIndex(r)
                            .objectReferenceValue == null) missing++;
                }
                if (missing > 0)
                    EditorGUILayout.HelpBox(
                        missing + " renderer route assignment(s) are empty.",
                        MessageType.Warning);
                if (GUILayout.Button("Refresh Choices")) UpdateOptions();
            }
        }

        private bool ArrayContains(SerializedProperty array, string item)
        {
            for(int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).stringValue == item)
                {
                    return true;
                }
            }
            return false;
        }

        private string[] SlotOptionsToArray(List<SlotDataAsset> assets)
        {
            string[] options = new string[assets.Count + 1];
            options[0] = "Add Base Slot";

            for (int i = 0; i < assets.Count; i++)
            {
                options[i+1] = assets[i].slotName;
            }
            return options;
        }

        private void ClearRendererElement(SerializedProperty element)
        {
            element.FindPropertyRelative("rendererAssets").ClearArray();
            element.FindPropertyRelative("slotAssets").ClearArray();
            element.FindPropertyRelative("Regions").ClearArray();
        }

        private void UpdateOptions()
        {
            wardrobeOptions.Clear();
            slotOptions.Clear();
            wardrobeOptions.Add("Add Wardrobe Region");

            if (avatar != null && avatar.activeRace != null && avatar.activeRace.data != null)
            {
#if UMA_ADDRESSABLES
                if (avatar.AddressableBuildPending)
                    return;
#endif
                currentRaceData = avatar.activeRace.data;
                wardrobeOptions.AddRange(avatar.activeRace.data.wardrobeSlots);

                avatar.activeRace.data.baseRaceRecipe.Load(umaRecipe);
                for (int i = 0; i < umaRecipe.slotDataList.Length; i++)
                {
                    if (umaRecipe.slotDataList[i] != null && umaRecipe.slotDataList[i].asset != null)
                    {
                        slotOptions.Add(umaRecipe.slotDataList[i].asset);
                    }
                }
            }
        }
    }
}
