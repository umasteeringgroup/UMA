using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMAGeneratorBase), true)]
    public class UMAGeneratorBaseEditor : Editor
    {
        SerializedProperty fitAtlas;
        SerializedProperty convertRenderTexture;
        SerializedProperty convertMipMaps;
        SerializedProperty atlasResolution;
        SerializedProperty AtlasOverflowFitMethod;
        SerializedProperty FitPercentageDecrease;
        SerializedProperty SharperFitTextures;
        SerializedProperty useAsyncConversion;
        SerializedProperty asyncMipRegen;
        public static bool showAtlasSettings = false;
        public static bool showConversionSettings = false;

        GUIContent[] atlasLabels = new GUIContent[] { new GUIContent("512"), new GUIContent("1024"), new GUIContent("2048"), new GUIContent("4096"), new GUIContent("8192") };
        int[] atlasValues = new int[] { 512, 1024, 2048, 4096, 8192 };

        protected GUIStyle centeredLabel;

        public virtual void OnEnable()
        {
            fitAtlas = serializedObject.FindProperty("fitAtlas");
            convertRenderTexture = serializedObject.FindProperty("convertRenderTexture");
            convertMipMaps = serializedObject.FindProperty("convertMipMaps");
            atlasResolution = serializedObject.FindProperty("atlasResolution");
            AtlasOverflowFitMethod = serializedObject.FindProperty("AtlasOverflowFitMethod");
            FitPercentageDecrease = serializedObject.FindProperty("FitPercentageDecrease");
            SharperFitTextures = serializedObject.FindProperty("SharperFitTextures");
            useAsyncConversion = serializedObject.FindProperty("useAsyncConversion");
            asyncMipRegen = serializedObject.FindProperty("asyncMipRegen");
        }

        public override void OnInspectorGUI()
        {
            centeredLabel = new GUIStyle(GUI.skin.label);
            centeredLabel.fontStyle = FontStyle.Bold;
            centeredLabel.alignment = TextAnchor.MiddleCenter;

            serializedObject.Update();
            showAtlasSettings = EditorGUILayout.Foldout(showAtlasSettings, "Atlas Settings");

            if (showAtlasSettings)
            {
                EditorGUILayout.LabelField("Basic Configuration", centeredLabel);
                GUIHelper.BeginVerticalPadded();
                EditorGUILayout.PropertyField(fitAtlas);
                EditorGUILayout.PropertyField(SharperFitTextures);
                EditorGUILayout.PropertyField(AtlasOverflowFitMethod);
                EditorGUILayout.PropertyField(FitPercentageDecrease);
                EditorGUILayout.PropertyField(convertMipMaps);
                EditorGUILayout.IntPopup(atlasResolution, atlasLabels, atlasValues);
                GUIHelper.EndVerticalPadded();

            }
            showConversionSettings = EditorGUILayout.Foldout(showConversionSettings, "Conversion Settings");
            if (showConversionSettings)
            {
                GUIHelper.BeginVerticalPadded();
                EditorGUILayout.HelpBox("Convert RenderTextures to Texture2D. This will create a Texture2D from the render texture, so it can be modified or saved.\n" +
                                        "Use AsyncConversion will do an async copy to avoid a GPU stall.\n" 
                                        /*"Async Mip Regen will only copy the top level mip, and recalculate the mips when the texture is applied"*/, MessageType.None);
                EditorGUILayout.PropertyField(convertRenderTexture);
                EditorGUILayout.PropertyField(useAsyncConversion);
                //EditorGUILayout.PropertyField(asyncMipRegen);
                GUIHelper.EndVerticalPadded();
            }

            serializedObject.ApplyModifiedProperties();
        }

    }
}
