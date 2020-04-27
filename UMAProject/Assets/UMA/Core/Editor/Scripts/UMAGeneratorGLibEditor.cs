using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.Editors 
{
    [CustomEditor(typeof(UMAGeneratorGLib))]
    public class UMAGeneratorGLibEditor : UMAGeneratorBuiltinEditor
    {
        SerializedProperty DefaultCacheLife;
        SerializedProperty EnableCacheCleanup;
        public override void OnEnable()
        {
            DefaultCacheLife = serializedObject.FindProperty("CachedItemsLife");
            EnableCacheCleanup = serializedObject.FindProperty("EnableCacheCleanup");
            base.OnEnable();
        }
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();
            EditorGUILayout.HelpBox("Enabling Cache Cleanup will mean that you cannot rebuild from the UMAData. In addition, you will incur more overhead when rebuilding or updating your DCA. Some functions are unavailable when automatically cleaning up the cache.", MessageType.Warning);
            EditorGUILayout.PropertyField(EnableCacheCleanup);
            EditorGUILayout.PropertyField(DefaultCacheLife);
            serializedObject.ApplyModifiedProperties();
        }
    }
}

