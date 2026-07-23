using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA.Editors
{
    public sealed class UMAQuickFinderWindow : EditorWindow
    {
        private const string WindowTitle = "Quick Finder";
        private const string PersistedStateKey = "UMA.QuickFinderWindow.State.v1";
        private const float RowHeight = 22f;
        private const float RemoveButtonWidth = 24f;

        [Serializable]
        private sealed class QuickFinderItem
        {
            public string name;
            public string path;
            public string scenePath;
            public string sceneName;
            public bool hasCameraState;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public Vector3 sceneViewPivot;
            public Quaternion sceneViewRotation;
            public float sceneViewSize;
            public bool sceneViewOrthographic;
        }

        [Serializable]
        private sealed class PersistedState
        {
            public List<QuickFinderItem> items = new List<QuickFinderItem>();
        }

        [SerializeField]
        private List<QuickFinderItem> items = new List<QuickFinderItem>();

        [SerializeField]
        private Vector2 scrollPosition;

        private string statusMessage;

        [MenuItem("UMA/Asset Management/Quick Finder", priority = 122)]
        public static void ShowWindow()
        {
            UMAQuickFinderWindow window = GetWindow<UMAQuickFinderWindow>(WindowTitle);
            window.minSize = new Vector2(260f, 180f);
            window.Show();
        }

        private void OnEnable()
        {
            LoadState();
        }

        private void OnDisable()
        {
            SaveState();
        }

        private void OnGUI()
        {
            DrawAddCurrentButton();

            EditorGUILayout.Space();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawItemButtons();
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
            }
        }

        private void DrawAddCurrentButton()
        {
            if (GUILayout.Button("Add current", GUILayout.Height(26f)))
            {
                AddCurrentSelection();
            }
        }

        private void DrawItemButtons()
        {
            if (items == null)
            {
                items = new List<QuickFinderItem>();
            }

            int removeIndex = -1;
            for (int i = 0; i < items.Count; i++)
            {
                QuickFinderItem item = items[i];
                if (item == null)
                {
                    removeIndex = i;
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                string buttonText = string.IsNullOrEmpty(item.name) ? item.path : item.name;
                if (GUILayout.Button(new GUIContent(buttonText, item.path), GUILayout.Height(RowHeight)))
                {
                    SelectItem(item);
                }

                if (GUILayout.Button("x", GUILayout.Width(RemoveButtonWidth), GUILayout.Height(RowHeight)))
                {
                    removeIndex = i;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (removeIndex >= 0 && removeIndex < items.Count)
            {
                items.RemoveAt(removeIndex);
                SaveState();
                Repaint();
            }
        }

        private void AddCurrentSelection()
        {
            GameObject[] selectedGameObjects = Selection.gameObjects;
            if (selectedGameObjects == null || selectedGameObjects.Length == 0)
            {
                statusMessage = "No hierarchy objects are selected.";
                return;
            }

            SceneView sceneView = SceneView.lastActiveSceneView;
            int addedCount = 0;
            for (int i = 0; i < selectedGameObjects.Length; i++)
            {
                GameObject selected = selectedGameObjects[i];
                if (selected == null || EditorUtility.IsPersistent(selected) || !selected.scene.IsValid())
                {
                    continue;
                }

                items.Add(new QuickFinderItem()
                {
                    name = selected.name,
                    path = GetHierarchyPath(selected.transform),
                    scenePath = selected.scene.path,
                    sceneName = selected.scene.name
                });
                CaptureSceneView(items[items.Count - 1], sceneView);
                addedCount++;
            }

            if (addedCount == 0)
            {
                statusMessage = "No hierarchy objects were added.";
                return;
            }

            SaveState();
            statusMessage = addedCount == 1 ? "Added 1 item." : "Added " + addedCount + " items.";
        }

        private void SelectItem(QuickFinderItem item)
        {
            Transform found = FindTransform(item);
            if (found == null)
            {
                statusMessage = "Not found: " + item.path;
                return;
            }

            Selection.activeGameObject = found.gameObject;
            EditorGUIUtility.PingObject(found.gameObject);

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                RestoreSceneView(item, sceneView);
            }

            statusMessage = "Selected: " + item.path;
        }

        private static void CaptureSceneView(QuickFinderItem item, SceneView sceneView)
        {
            if (item == null || sceneView == null)
            {
                return;
            }

            item.hasCameraState = true;
            item.sceneViewPivot = sceneView.pivot;
            item.sceneViewRotation = sceneView.rotation;
            item.sceneViewSize = Mathf.Max(0.0001f, sceneView.size);
            item.sceneViewOrthographic = sceneView.orthographic;

            Camera camera = sceneView.camera;
            if (camera != null)
            {
                item.cameraPosition = camera.transform.position;
                item.cameraRotation = camera.transform.rotation;
            }
            else
            {
                item.cameraPosition = sceneView.pivot - (sceneView.rotation * Vector3.forward * item.sceneViewSize);
                item.cameraRotation = sceneView.rotation;
            }
        }

        private static void RestoreSceneView(QuickFinderItem item, SceneView sceneView)
        {
            if (item == null || sceneView == null || !item.hasCameraState)
            {
                return;
            }

            sceneView.orthographic = item.sceneViewOrthographic;
            sceneView.LookAtDirect(item.sceneViewPivot, item.sceneViewRotation, Mathf.Max(0.0001f, item.sceneViewSize));
            sceneView.Repaint();
        }

        private static Transform FindTransform(QuickFinderItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.path))
            {
                return null;
            }

            string[] pathSegments = SplitPath(item.path);
            if (pathSegments.Length == 0)
            {
                return null;
            }

            Scene preferredScene = FindStoredScene(item.scenePath, item.sceneName);
            Transform found = FindTransformInScene(preferredScene, pathSegments);
            if (found != null)
            {
                return found;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (preferredScene.IsValid() && scene == preferredScene)
                {
                    continue;
                }

                found = FindTransformInScene(scene, pathSegments);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Scene FindStoredScene(string scenePath, string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(scenePath) && string.Equals(scene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                {
                    return scene;
                }

                if (string.IsNullOrEmpty(scenePath) && !string.IsNullOrEmpty(sceneName) && string.Equals(scene.name, sceneName, StringComparison.Ordinal))
                {
                    return scene;
                }
            }

            return default(Scene);
        }

        private static Transform FindTransformInScene(Scene scene, string[] pathSegments)
        {
            if (!scene.IsValid() || !scene.isLoaded || pathSegments == null || pathSegments.Length == 0)
            {
                return null;
            }

            GameObject[] rootObjects = scene.GetRootGameObjects();
            for (int i = 0; i < rootObjects.Length; i++)
            {
                GameObject rootObject = rootObjects[i];
                if (rootObject == null || !string.Equals(rootObject.name, pathSegments[0], StringComparison.Ordinal))
                {
                    continue;
                }

                Transform found = FindChildPath(rootObject.transform, pathSegments, 1);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindChildPath(Transform current, string[] pathSegments, int segmentIndex)
        {
            if (current == null)
            {
                return null;
            }

            if (segmentIndex >= pathSegments.Length)
            {
                return current;
            }

            for (int i = 0; i < current.childCount; i++)
            {
                Transform child = current.GetChild(i);
                if (child != null && string.Equals(child.name, pathSegments[segmentIndex], StringComparison.Ordinal))
                {
                    return FindChildPath(child, pathSegments, segmentIndex + 1);
                }
            }

            return null;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            List<string> names = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static string[] SplitPath(string path)
        {
            return path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private void LoadState()
        {
            items = new List<QuickFinderItem>();
            string json = EditorPrefs.GetString(PersistedStateKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            try
            {
                PersistedState state = JsonUtility.FromJson<PersistedState>(json);
                if (state != null && state.items != null)
                {
                    items = state.items;
                }
            }
            catch (Exception)
            {
                items = new List<QuickFinderItem>();
            }
        }

        private void SaveState()
        {
            PersistedState state = new PersistedState()
            {
                items = items ?? new List<QuickFinderItem>()
            };
            EditorPrefs.SetString(PersistedStateKey, JsonUtility.ToJson(state));
        }
    }
}
