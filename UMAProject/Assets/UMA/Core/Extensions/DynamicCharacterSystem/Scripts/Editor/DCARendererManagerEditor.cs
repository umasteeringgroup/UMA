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

        DynamicCharacterAvatar avatar;
        UMAData.UMARecipe umaRecipe = new UMAData.UMARecipe();
        List<string> wardrobeOptions = new List<string>();
        List<SlotDataAsset> slotOptions = new List<SlotDataAsset>();
        UMAContext context;

        void OnEnable()
        {
            RendererElements = serializedObject.FindProperty("RendererElements");
            context = UMAContext.FindInstance();

            DCARendererManager manager = target as DCARendererManager;
            avatar = manager.GetComponent<DynamicCharacterAvatar>();

            wardrobeOptions.Clear();
            slotOptions.Clear();

            if (avatar != null && avatar.activeRace != null && avatar.activeRace.data != null)
            {
                wardrobeOptions.AddRange(avatar.activeRace.data.wardrobeSlots);
                wardrobeOptions.Insert(0, "Add Wardrobe Slot");

                avatar.activeRace.data.baseRaceRecipe.Load(umaRecipe, context);
                for(int i = 0; i < umaRecipe.slotDataList.Length; i++)
                {
                    if (umaRecipe.slotDataList[i] != null && umaRecipe.slotDataList[i].asset != null)
                    {
                        slotOptions.Add(umaRecipe.slotDataList[i].asset);
                    }
                }                
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            GUILayout.Space(10);
            if(GUILayout.Button("Add New Renderer Element"))
            {
                RendererElements.arraySize++;
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
                EditorGUILayout.PropertyField(RendererElements.GetArrayElementAtIndex(i).FindPropertyRelative("rendererAsset"));
                GUILayout.Space(10);

                EditorGUILayout.BeginHorizontal();
                int newSlot = EditorGUILayout.Popup(0, SlotOptionsToArray(slotOptions));
                if(newSlot > 0)
                {
                    slotAssets.arraySize++;
                    slotAssets.GetArrayElementAtIndex(slotAssets.arraySize - 1).objectReferenceValue = slotOptions[newSlot-1];
                }

                int newWardrobe = EditorGUILayout.Popup(0, wardrobeOptions.ToArray());
                if(newWardrobe > 0)
                {
                    if (!ArrayContains(wardrobeSlots, wardrobeOptions[newWardrobe]))
                    {
                        wardrobeSlots.arraySize++;
                        wardrobeSlots.GetArrayElementAtIndex(wardrobeSlots.arraySize-1).stringValue = wardrobeOptions[newWardrobe];
                    }
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(10);

                EditorGUILayout.PropertyField(slotAssets, true);
                EditorGUILayout.PropertyField(wardrobeSlots, true);
                EditorGUI.indentLevel--;
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
    }
}
