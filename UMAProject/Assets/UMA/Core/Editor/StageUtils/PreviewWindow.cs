using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UMA
{
    public class PreviewWindow : SceneView
    {

        public CustomPreviewStage stage;
        public GameObject selectedObj;

        [MenuItem("Assets/Preview/Preview Asset")]
        public static void ShowWindow()
        {
            if (Selection.activeGameObject == null)
            {
                Debug.LogError("No object selected");
                return;
            }
            PreviewWindow window = CreateWindow<PreviewWindow>("Preview");
            window.drawGizmos = false;
            window.selectedObj = Selection.activeGameObject;
            window.SetupWindow();
            window.Repaint();
        }

        private void SetupWindow()
        {
            if (selectedObj == null)
            {
                return;
            }

            titleContent = new GUIContent();
            titleContent.text = selectedObj.name;
            titleContent.image = EditorGUIUtility.IconContent("GameObject Icon").image;

            stage = ScriptableObject.CreateInstance<CustomPreviewStage>();
            stage.ownerWindow = this;
            stage.titleContent = titleContent;
            stage.selectedObject = selectedObj as GameObject;
            stage.SetupScene(this);

            StageUtility.GoToStage(stage, true);
        }

        private void OnFocus()
        {
            //if (StageUtility.GetCurrentStage() != stage)
            //{
            //    SetupWindow();
            //}
        }

        private void OnLostFocus()
        {
            //StageUtility.GoToMainStage();
            //DestroyImmediate(stage);
        }
    }
}