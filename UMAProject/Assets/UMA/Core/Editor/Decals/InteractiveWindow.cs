using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UMA;
using UnityEditor.Compilation;

namespace UMA
{
#if UNITY_2020_1_OR_NEWER
    // Get rid of the annoying icon not found error.
    [EditorWindowTitle(icon = "", title = "InteractiveWindow", useTypeNameAsIconName = false)]
#endif
    public class InteractiveUMAWindow : SceneView
    {
        bool isInitialized;
        string UMAPrefab = "";
        GameObject EnvironmentPrefab;
        public GameObject libGo;
        public GameObject avatarGo;
        public DynamicCharacterAvatar avatar;
        public IEditorScene editor;
        public Scene CurrentScene;
        private static GUIStyle bsNormal = null;
        private static GUIStyle bsToggled = null;
        private string[] cameraModeNames = { "Textured", "Textured Wireframe", "Wireframe" };
        private DrawCameraMode[] cameraModes = { DrawCameraMode.Textured, DrawCameraMode.TexturedWire, DrawCameraMode.Wireframe };
        private int camMode = 0;
        private int savedLockedLayers;
        private static List<InteractiveUMAWindow> Windows = new List<InteractiveUMAWindow>();
        public GameObject Indicator;
        //private GameObject capsule;
        public static string WindowName;
        private bool showHelp = true;

        private static Dictionary<string, InteractiveUMAWindow> windows = new Dictionary<string, InteractiveUMAWindow>();

        public static void Init(string windowName, IEditorScene editor)
        {
            if (Application.isPlaying)
            {
                if (EditorUtility.DisplayDialog("Error", "The UMA Interactive window does not work in playmode", "OK"))
                {
                    return;
                }
            }

            if (windows.ContainsKey(windowName))
            {
                InteractiveUMAWindow window = windows[windowName];
                window.Close();
            }


            bsNormal = "Button";
            bsToggled = new GUIStyle(bsNormal);
            bsToggled.normal.background = bsToggled.active.background;
            WindowName = windowName;
            var w = GetWindow<InteractiveUMAWindow>(WindowName, true, typeof(SceneView));
            if (w == null)
            {
                EditorUtility.DisplayDialog("Error", "Unable to create or get Interactive UMA window!", "OK");
                return;
            }
            w.editor = editor;
            //w.InitializeIfNeeded();
            w.showGrid = false;
            w.drawGizmos = false;
            w.sceneLighting = true;
            w.SetCameraMode(w.camMode);
            w.Focus();
            AppDomain.CurrentDomain.DomainUnload += CurrentDomain_DomainUnload;
        }

        public bool ShowToggleButton(string txt, bool initialValue, params GUILayoutOption[] Options)
        {
            if (GUILayout.Button(txt, initialValue ? bsToggled : bsNormal, Options))
            {
                initialValue = !initialValue;
            }
            return initialValue;
        }

        public bool ShowToggleButton(GUIContent content, bool initialValue, params GUILayoutOption[] Options)
        {
            if (GUILayout.Button(content, initialValue ? bsToggled : bsNormal, Options))
            {
                initialValue = !initialValue;
            }
            return initialValue;
        }


        public void SetCameraMode(int mode)
        {
            DrawCameraMode newMode = cameraModes[mode];
            CameraMode currentMode = this.cameraMode;

            if (currentMode.drawMode == newMode)
            {
                return;
            }

            cameraMode = SceneView.GetBuiltinCameraMode(newMode);
        }

        protected override void OnSceneGUI()
        {
            InitializeIfNeeded();
            if (bsToggled == null)
            {
                // lost it all...
                Close();
                return;
            }

            using (new GUILayout.HorizontalScope())
            {
                drawGizmos = ShowToggleButton("Gizmos:" + drawGizmos, drawGizmos, GUILayout.ExpandWidth(false));
                sceneLighting = ShowToggleButton("Lighting:" + sceneLighting, sceneLighting, GUILayout.ExpandWidth(false));
                showHelp = ShowToggleButton("Help:" + showHelp, showHelp, GUILayout.ExpandWidth(false));
                camMode = EditorGUILayout.Popup(camMode, cameraModeNames, GUILayout.ExpandWidth(false));
                editor.ShowHelp(showHelp);
                SetCameraMode(camMode);
                GUILayout.Button("", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Focus Avatar", GUILayout.ExpandWidth(false)))
                {
                    Selection.activeGameObject = avatar.gameObject;
                    avatar.gameObject.SetActive(true);
                    FrameSelected();
                }
            }
            base.OnSceneGUI();
        }


        private void InitializeIfNeeded()
        {
            if (isInitialized)
            {
                return;
            }
            if (UMAPrefab == "")
            {
                string[] assets = AssetDatabase.FindAssets("UMADefaultUtilityEnvironment");
                if (assets == null || assets.Length < 1)
                {
                    EditorUtility.DisplayDialog("Error", "Unable to find UMADefaultUtilityEnvironment Prefab!", "OK");
                    Close();
                    return;
                }
                UMAPrefab = AssetDatabase.GUIDToAssetPath(assets[0]);
            }


            customScene = EditorSceneManager.NewPreviewScene();
            CurrentScene = customScene;
            sceneLighting = true;
            drawGizmos = true;
            editor.Initialize(this, customScene);

            EnvironmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UMAPrefab);
            libGo = GameObject.Instantiate(EnvironmentPrefab);
            SceneManager.MoveGameObjectToScene(libGo, customScene);

            avatar = libGo.GetComponentInChildren<DynamicCharacterAvatar>();
            avatarGo = avatar.gameObject;
            // cube = libGo.GetComponentInChildren<BoxCollider>().gameObject;
            // sphere = libGo.GetComponentInChildren<SphereCollider>().gameObject;
            //capsule = libGo.GetComponentInChildren<CapsuleCollider>().gameObject;

            Selection.activeObject = avatarGo;
            // Zoom the scene view into the new object
            bool val = FrameSelected(true, true);
            Repaint();

            avatar.InitialStartup();
            isInitialized = true;
            editor.InitializationComplete(libGo);
        }

        public void OnFocus()
        {
            if (avatarGo != null)
            {
                Selection.activeTransform = avatarGo.transform;
                FrameSelected();
            }
            savedLockedLayers = Tools.lockedLayers;
            Tools.lockedLayers = ~LayerMask.GetMask("Player");
        }

        public void OnLostFocus()
        {
            Debug.Log("lost focus");
            Tools.lockedLayers = savedLockedLayers;
        }

        private void EditorChanged(PlayModeStateChange state)
        {
            // stop the gazillion errors trying to get the controls

            CloseCleanup();
        }

        private static void CurrentDomain_DomainUnload(object sender, EventArgs e)
        {

        }

        public new void OnDestroy()
        {
            Cleanup();
            base.OnDestroy();
        }

        public override void OnEnable()
        {
            try
            {
                //InitializeIfNeeded(); Don't ever do this
                base.OnEnable();
                titleContent = new GUIContent(WindowName);
                EditorApplication.playModeStateChanged += EditorChanged;
                CompilationPipeline.compilationStarted += CompilationPipeline_assemblyCompilationStarted;
                duringSceneGui += InteractiveUMAWindow_duringSceneGui;
            }
            catch (Exception) { };
        }

        private void InteractiveUMAWindow_duringSceneGui(SceneView obj)
        {
            if (this != null)
            {
                //Rect rect = new Rect(0, 0, position.width, position.height);
                //using (new GUILayout.AreaScope(rect))
                //{
                if (obj == this)
                {
                    editor.OnSceneGUI(this);
                }
                //}
            }
        }

        public Vector2 GUIPointToScreenPixelCoordinate(Vector2 guiPoint)
        {
            return HandleUtility.GUIPointToScreenPixelCoordinate(guiPoint);
        }

        public Ray GUIPointToWorldRay(Vector2 position, float startZ = float.NegativeInfinity)
        {
            if (float.IsNegativeInfinity(startZ))
            {
                startZ = camera.nearClipPlane;
            }

            Vector2 screenPixelPos = GUIPointToScreenPixelCoordinate(position);
            Rect viewport = camera.pixelRect;

            Matrix4x4 camToWorld = camera.cameraToWorldMatrix;
            Matrix4x4 camToClip = camera.projectionMatrix;
            Matrix4x4 clipToCam = camToClip.inverse;

            // calculate ray origin and direction in world space
            Vector3 rayOriginWorldSpace;
            Vector3 rayDirectionWorldSpace;

            // first construct an arbitrary point that is on the ray through this screen pixel (remap screen pixel point to clip space [-1, 1])
            Vector3 rayPointClipSpace = new Vector3(
                (screenPixelPos.x - viewport.x) * 2.0f / viewport.width - 1.0f,
                (screenPixelPos.y - viewport.y) * 2.0f / viewport.height - 1.0f,
                0.95f
            );

            // and convert that point to camera space
            Vector3 rayPointCameraSpace = clipToCam.MultiplyPoint(rayPointClipSpace);


            // in projective mode, the ray passes through the origin in camera space
            // so the ray direction is just (ray point - origin) == (ray point)
            Vector3 rayDirectionCameraSpace = rayPointCameraSpace;
            rayDirectionCameraSpace.Normalize();

            rayDirectionWorldSpace = camToWorld.MultiplyVector(rayDirectionCameraSpace);

            // calculate the correct startZ offset from the camera by moving a distance along the ray direction
            // this assumes camToWorld is a pure rotation/offset, with no scale, so we can use rayDirection.z to calculate how far we need to move
            Vector3 cameraPositionWorldSpace = camToWorld.MultiplyPoint(Vector3.zero);
            // The camera/projection matrices follow OpenGL convention: positive Z is towards the viewer.
            // So negate it to get into Unity convention.
            Vector3 originOffsetWorldSpace = rayDirectionWorldSpace * -startZ / rayDirectionCameraSpace.z;
            rayOriginWorldSpace = cameraPositionWorldSpace + originOffsetWorldSpace;

            return new Ray(rayOriginWorldSpace, rayDirectionWorldSpace);
        }


        private void CompilationPipeline_assemblyCompilationStarted(object obj)
        {
            CloseCleanup();
        }

        public void CloseCleanup()
        {
            Cleanup();
            Close();
        }

        public override void OnDisable()
        {
            Debug.Log("On Disable");
            base.OnDisable();
            Cleanup();
        }

        private void Cleanup()
        {
            if (isInitialized)
            {
                isInitialized = false;
                editor.Cleanup(this);
                if (customScene != null)
                {
                    Scene s = customScene;
                    EditorSceneManager.ClosePreviewScene(s);
                }
                EditorApplication.playModeStateChanged -= EditorChanged;
                CompilationPipeline.compilationStarted -= CompilationPipeline_assemblyCompilationStarted;
                duringSceneGui -= InteractiveUMAWindow_duringSceneGui;
            }
        }
    }
}
