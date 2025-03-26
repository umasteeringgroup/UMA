using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA
{
    public class CustomPreviewStage : PreviewSceneStage
    {
        public PreviewWindow ownerWindow;
        public GUIContent titleContent;
        public SceneView openedSceneView;
        public GameObject selectedObject;
        public GameObject m_GameObject;
        GameObject lightingObject = null;

        public void SetupScene(SceneView sceneView)
        {
            scene = EditorSceneManager.NewPreviewScene();
            GameObject lightingObject = new GameObject("Directional Light");
            lightingObject.transform.rotation = Quaternion.Euler(50, 330, 0);
            lightingObject.AddComponent<Light>().type = LightType.Directional;

            // Instantiate character
            m_GameObject = Instantiate(selectedObject);
            SceneManager.MoveGameObjectToScene(m_GameObject, scene);
            SceneManager.MoveGameObjectToScene(lightingObject, scene);
            Tools.hidden = true;
        }

        protected override void OnEnable()
        {

        }

        protected override void OnDisable()
        {
            Tools.current = Tool.Move;
            DestroyImmediate(m_GameObject);
            DestroyImmediate(lightingObject);
        }

        /*protected override bool OnOpenStage()
        {
            base.OnOpenStage();

            GameObject lightingObject = new GameObject("Directional Light");
            lightingObject.transform.rotation = Quaternion.Euler(50, 330, 0);
            lightingObject.AddComponent<Light>().type = LightType.Directional;

            // Instantiate character
            m_GameObject = Instantiate(selectedObject);
            SceneManager.MoveGameObjectToScene(m_GameObject, scene);
            SceneManager.MoveGameObjectToScene(lightingObject, scene);
            Tools.current = Tool.None;
            return true;
        }*/

        /*
        protected override void OnCloseStage()
        {
            Tools.current = Tool.Move;
            //DestroyImmediate(m_GameObject);
            //DestroyImmediate(lightingObject);
            //base.OnCloseStage();
        }*/

        protected override GUIContent CreateHeaderContent()
        {
            GUIContent headerContent = new GUIContent();
            headerContent.text = selectedObject.name;
            headerContent.image = titleContent.image;
            return headerContent;
        }

        protected override void OnFirstTimeOpenStageInSceneView(SceneView sceneView)
        {
            Selection.activeObject = m_GameObject;

            // Frame in scene view
            sceneView.FrameSelected(false, true);

            // Setup Scene view state
            sceneView.sceneViewState.showFlares = false;
            sceneView.sceneViewState.alwaysRefresh = false;
            sceneView.sceneViewState.showFog = false;
            sceneView.sceneViewState.showSkybox = false;
            sceneView.sceneViewState.showImageEffects = false;
            sceneView.sceneViewState.showParticleSystems = false;
            sceneView.sceneLighting = false;
        }
    }
}