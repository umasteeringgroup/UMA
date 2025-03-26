using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UMA;

namespace UMA
{

    public class InteractiveWindow : SceneView
    {
        public IEditorScene editorScene;
        public CustomPreviewStage stage;
        public GameObject selectedObj;

        public static void Init(string WindowName, IEditorScene editor)
        {
            InteractiveWindow window = CreateWindow<InteractiveWindow>(WindowName);
            window.editorScene = editor;
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

            stage = ScriptableObject.CreateInstance<CustomPreviewStage>();
            //stage.ownerWindow = this;
            stage.titleContent = titleContent;
            stage.selectedObject = selectedObj as GameObject;
            stage.SetupScene(this);

            StageUtility.GoToStage(stage, true);
        }

        private void OnFocus()
        {
            if (StageUtility.GetCurrentStage() != stage)
            {
                SetupWindow();
            }
        }

        private void OnLostFocus()
        {
            StageUtility.GoToMainStage();
            DestroyImmediate(stage);
        }
    }
}
