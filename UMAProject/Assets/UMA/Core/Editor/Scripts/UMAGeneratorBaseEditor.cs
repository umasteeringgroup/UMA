using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMAGeneratorBase), true)]
    public class UMAGeneratorBaseEditor : Editor
    {
        SerializedProperty fitAtlas;
        SerializedProperty convertRenderTexture;
        SerializedProperty convertMipMaps;
        SerializedProperty atlasResolution;
        SerializedProperty defaultOverlayAsset;
        SerializedProperty AtlasOverflowFitMethod;
        SerializedProperty FitPercentageDecrease;
        SerializedProperty SharperFitTextures;

        GUIContent[] atlasLabels = new GUIContent[] { new GUIContent("512"), new GUIContent("1024"), new GUIContent("2048"), new GUIContent("4096"), new GUIContent("8192") };
        int[] atlasValues = new int[] { 512, 1024, 2048, 4096, 8192 };

        protected GUIStyle centeredLabel;

        public virtual void OnEnable()
        {
            fitAtlas = serializedObject.FindProperty("fitAtlas");
            convertRenderTexture = serializedObject.FindProperty("convertRenderTexture");
            convertMipMaps = serializedObject.FindProperty("convertMipMaps");
            atlasResolution = serializedObject.FindProperty("atlasResolution");
            defaultOverlayAsset = serializedObject.FindProperty("defaultOverlayAsset");
            AtlasOverflowFitMethod = serializedObject.FindProperty("AtlasOverflowFitMethod");
            FitPercentageDecrease = serializedObject.FindProperty("FitPercentageDecrease");
            SharperFitTextures = serializedObject.FindProperty("SharperFitTextures");
        }

        public override void OnInspectorGUI()
        {
            centeredLabel = new GUIStyle(GUI.skin.label);
            centeredLabel.fontStyle = FontStyle.Bold;
            centeredLabel.alignment = TextAnchor.MiddleCenter;

            serializedObject.Update();

            EditorGUILayout.LabelField("Basic Configuration", centeredLabel);
            GUIHelper.BeginVerticalPadded();
            EditorGUILayout.PropertyField(fitAtlas);
            EditorGUILayout.PropertyField(SharperFitTextures);
            EditorGUILayout.PropertyField(AtlasOverflowFitMethod);
            EditorGUILayout.HelpBox("Note: Atlas Overflow parameters only work with coroutines disabled below.",MessageType.None);
            EditorGUILayout.PropertyField(FitPercentageDecrease);
            GUIHelper.EndVerticalPadded();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("SaveAndRestoreIgnoredItems"));
            EditorGUILayout.PropertyField(convertRenderTexture);
            EditorGUILayout.PropertyField(convertMipMaps);
            EditorGUILayout.IntPopup(atlasResolution, atlasLabels, atlasValues );
            EditorGUILayout.PropertyField(defaultOverlayAsset);

            serializedObject.ApplyModifiedProperties();
        }

    }
}
