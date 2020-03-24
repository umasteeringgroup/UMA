using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA.CharacterSystem;

namespace UMA.Editors
{
    [CustomEditor(typeof(DCARendererManager))]
    public class DCARendererManagerEditor : Editor
    {
        SerializedProperty RendererElements;
        SerializedProperty showHelp;

        DynamicCharacterAvatar avatar;
        UMAData.UMARecipe umaRecipe = new UMAData.UMARecipe();
        List<string> wardrobeOptions = new List<string>();
        List<SlotDataAsset> slotOptions = new List<SlotDataAsset>();
        UMAContextBase context;
        RaceData currentRaceData;

        void OnEnable()
        {
            RendererElements = serializedObject.FindProperty("RendererElements");
            showHelp = serializedObject.FindProperty("showHelp");
            context = UMAContextBase.Instance;

            DCARendererManager manager = target as DCARendererManager;
            avatar = manager.GetComponent<DynamicCharacterAvatar>();

            UpdateOptions();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            GUILayout.Space(10);
            showHelp.boolValue = EditorGUILayout.Toggle("Show Help", showHelp.boolValue);
            if (showHelp.boolValue)
            {
                EditorGUILayout.HelpBox("This manager will create skinned mesh renderers for each Renderer Asset in each Renderer Element Set." +
                    "\n\n For each slot Asset listed that is found in the generating UMA, it will be separated onto each RendererAsset listed (so there could be duplicates created for various effects)." +
                    "\n\n This manager will also look for any slots assigned to any list Wardrobe slots and perform the same separation.", MessageType.Info);
            }

            if (avatar != null && avatar.activeRace != null && avatar.activeRace.data != null)
            {
                if (currentRaceData != avatar.activeRace.data)
                    UpdateOptions();
            }

            GUILayout.Space(10);
            if(GUILayout.Button("Add New Renderer Element Set"))
            {
                RendererElements.arraySize++;
                ClearRendererElement(RendererElements.GetArrayElementAtIndex(RendererElements.arraySize - 1));                
            }
            GUILayout.Space(20);

            for(int i = RendererElements.arraySize - 1; i >= 0; i--)
            {
                EditorGUILayout.BeginVertical("HelpBox");
                SerializedProperty wardrobeSlots = RendererElements.GetArrayElementAtIndex(i).FindPropertyRelative("wardrobeSlots");
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
                    EditorGUILayout.HelpBox("There are unassigned UMARendererAssets!", MessageType.Error);

                EditorGUILayout.EndVertical();
                GUILayout.Space(10);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private bool ArrayContains(SerializedProperty array, string item)
        {
            for(int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).stringValue == item)
                    return true;
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
            element.FindPropertyRelative("wardrobeSlots").ClearArray();
        }

        private void UpdateOptions()
        {
            wardrobeOptions.Clear();
            slotOptions.Clear();

            if (avatar != null && avatar.activeRace != null && avatar.activeRace.data != null)
            {
                currentRaceData = avatar.activeRace.data;
                wardrobeOptions.AddRange(avatar.activeRace.data.wardrobeSlots);
                wardrobeOptions.Insert(0, "Add Wardrobe Slot");

                avatar.activeRace.data.baseRaceRecipe.Load(umaRecipe, context);
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
