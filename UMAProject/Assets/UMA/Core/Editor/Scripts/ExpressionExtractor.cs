using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace UMA.PoseTools
{
    public class ExpressionExtractor : EditorWindow
    {
        public GameObject gameObject;
        public RuntimeAnimatorController Controller;
        public UnityEngine.Object expressionFolder;
        public string OutputPath = "Assets/UMA/Expressions/";
        private AnimationClip poseAnimation;

        [Serializable]
        public class AnimationPose
        {
            [XmlAttribute("ID")]
            public string ID = "";
            public int frame = 0;
        }

        private List<AnimationPose> poses;
        private bool animOpen = true;
        private Vector2 scrollPosition;

        // Persistence
        private const string PrefsKey = "UMA_ExpressionExtractor_State_v1";
        
        [System.Serializable]
        private class PersistedState
        {
            public string rootTransformPath;
            public string expressionFolderPath;
            public string poseAnimationPath;
            public bool animOpen;
            public float scrollX;
            public float scrollY;
            public List<AnimationPose> poses;
        }

        private void OnEnable()
        {
            LoadState();
            if (poses == null || poses.Count == 0)
            {
                poses = new List<AnimationPose> { new AnimationPose() };
            }
        }

        private void OnDisable()
        {
            SaveState();
        }

        public void SaveExpressionSet()
        {
            string folderPath = "";
            if (expressionFolder != null)
            {
                folderPath = AssetDatabase.GetAssetPath(expressionFolder);
            }
            else if (poseAnimation != null)
            {
                folderPath = AssetDatabase.GetAssetPath(poseAnimation);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    folderPath = folderPath.Substring(0, folderPath.LastIndexOf('/'));
                }
            }

            string defaultName = (poseAnimation != null ? poseAnimation.name : "ExpressionSet") + "_Expressions.xml";
            string filePath = EditorUtility.SaveFilePanel("Save expression set", folderPath, defaultName, "xml");

            if (!string.IsNullOrEmpty(filePath))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(List<AnimationPose>));
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    serializer.Serialize(stream, poses);
                }
                SaveState();
            }
        }

        public void LoadExpressionSet()
        {
            string folderPath = "";
            if (expressionFolder != null)
            {
                folderPath = AssetDatabase.GetAssetPath(expressionFolder);
            }
            else if (poseAnimation != null)
            {
                folderPath = AssetDatabase.GetAssetPath(poseAnimation);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    folderPath = folderPath.Substring(0, folderPath.LastIndexOf('/'));
                }
            }

            string filePath = EditorUtility.OpenFilePanel("Load expression set", folderPath, "xml");
            if (!string.IsNullOrEmpty(filePath))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(List<AnimationPose>));
                using (var stream = new FileStream(filePath, FileMode.Open))
                {
                    poses = serializer.Deserialize(stream) as List<AnimationPose>;
                }
                if (poses == null || poses.Count == 0)
                {
                    poses = new List<AnimationPose> { new AnimationPose() };
                }
                SaveState();
            }
        }

        public void EnforceFolder(ref UnityEngine.Object folderObject)
        {
            if (folderObject != null)
            {
                string destpath = AssetDatabase.GetAssetPath(folderObject);
                if (string.IsNullOrEmpty(destpath))
                {
                    folderObject = null;
                }
                else if (!Directory.Exists(destpath))
                {
                    destpath = destpath.Substring(0, destpath.LastIndexOf('/'));
                    folderObject = AssetDatabase.LoadMainAssetAtPath(destpath);
                }
            }
        }

        void OnGUI()
        {
            bool stateChanged = false;

            EditorGUI.BeginChangeCheck();
            gameObject = EditorGUILayout.ObjectField("GameObject", gameObject, typeof(GameObject), true) as GameObject;
            if (EditorGUI.EndChangeCheck())
            {
                stateChanged = true;
            }

            EditorGUI.BeginChangeCheck();
            expressionFolder = EditorGUILayout.ObjectField("Expression Folder", expressionFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
            if (EditorGUI.EndChangeCheck())
            {
                EnforceFolder(ref expressionFolder);
                stateChanged = true;
            }

            EditorGUILayout.Space();

            // Expression animation source
            if (animOpen = EditorGUILayout.Foldout(animOpen, "Animation Source"))
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                poseAnimation = EditorGUILayout.ObjectField("Expression Animation", poseAnimation, typeof(AnimationClip), false) as AnimationClip;
                if (EditorGUI.EndChangeCheck())
                {
                    stateChanged = true;
                }

                if (poses == null)
                {
                    poses = new List<AnimationPose> { new AnimationPose() };
                    stateChanged = true;
                }

                if (poseAnimation != null)
                {
                    int frameCount = Mathf.CeilToInt(poseAnimation.length * poseAnimation.frameRate);
                    EditorGUILayout.LabelField("Frame Count: " + frameCount);
                }

                bool validPose = false;
                AnimationPose deletedPose = null;
                scrollPosition = GUILayout.BeginScrollView(scrollPosition);
                for (int i = 0; i < poses.Count; i++)
                {
                    var pose = poses[i];
                    GUILayout.BeginHorizontal();
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.LabelField("ID", GUILayout.Width(50f));
                    string newID = EditorGUILayout.TextField(pose.ID);
                    EditorGUILayout.LabelField("Frame", GUILayout.Width(60f));
                    int newFrame = EditorGUILayout.IntField(pose.frame, GUILayout.Width(50f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        pose.ID = newID;
                        pose.frame = newFrame;
                        stateChanged = true;
                    }
                    if (!string.IsNullOrEmpty(pose.ID))
                    {
                        validPose = true;
                    }

                    bool canGotoFrame = poseAnimation != null && gameObject != null;
                    if (!canGotoFrame)
                    {
                        GUI.enabled = false;
                    }
                    if (GUILayout.Button("Go", GUILayout.Width(30f)))
                    {
                        float time = pose.frame / poseAnimation.frameRate;
                        GotoFrame(time);
                    }
                    GUI.enabled = true;

                    if (GUILayout.Button("-", GUILayout.Width(20f)))
                    {
                        deletedPose = pose;
                    }
                    GUILayout.EndHorizontal();
                }
                if (deletedPose != null)
                {
                    poses.Remove(deletedPose);
                    stateChanged = true;
                }
                GUILayout.EndScrollView();

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+", GUILayout.Width(30f)))
                {
                    poses.Add(new AnimationPose());
                    stateChanged = true;
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Load Expression Set"))
                {
                    LoadExpressionSet();
                    stateChanged = true;
                }
                if (!validPose)
                {
                    GUI.enabled = false;
                }
                if (GUILayout.Button("Save Expression Set"))
                {
                    SaveExpressionSet();
                    stateChanged = true;
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();

                if (poseAnimation == null || !validPose || gameObject == null)
                {
                    GUI.enabled = false;
                }

                if (GUILayout.Button("Build Expressions"))
                {
                    BuildExpressions();
                    stateChanged = true;
                }
                GUI.enabled = true;
                EditorGUI.indentLevel--;
            }

            if (Event.current.type == EventType.Repaint)
            {
                SaveState();
            }

            if (stateChanged)
            {
                SaveState();
            }
        }

        private void BuildExpressions()
        {
            if (gameObject == null || poseAnimation == null)
            {
                Debug.LogError("[ExpressionExtractor] Root transform and animation are required.");
                return;
            }

            string folderPath;
            if (expressionFolder != null)
            {
                folderPath = AssetDatabase.GetAssetPath(expressionFolder);
            }
            else
            {
                folderPath = AssetDatabase.GetAssetPath(poseAnimation);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    folderPath = folderPath.Substring(0, folderPath.LastIndexOf('/'));
                }
            }

            if (string.IsNullOrEmpty(folderPath))
            {
                folderPath = OutputPath;
            }

            foreach (AnimationPose pose in poses)
            {
                if (string.IsNullOrEmpty(pose.ID))
                {
                    Debug.LogWarning("[ExpressionExtractor] Bad pose identifier, skipping frame: " + pose.frame);
                    continue;
                }

                float time = pose.frame / poseAnimation.frameRate;
                if (time < 0f || time > poseAnimation.length)
                {
                    Debug.LogWarning("[ExpressionExtractor] Bad frame number, skipping pose: " + pose.ID);
                    continue;
                }

                // Sample animation at the specified frame
                GotoFrame(time);

                // TODO: Extract bone pose data and create UMABonePose asset
                Debug.Log($"[ExpressionExtractor] Extracted expression '{pose.ID}' at frame {pose.frame} (time {time})");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public void GotoFrame(float time)
        {
            if (poseAnimation != null && gameObject != null)
            {
                Debug.Log($"[ExpressionExtractor] Going to time: {time} GameObject {gameObject.GetInstanceID()} name {gameObject.name}");
                poseAnimation.SampleAnimation(gameObject, time);
                EditorUtility.SetDirty(gameObject);
                SceneView.RepaintAll();

            }
        }

        private void SaveState()
        {
            var state = new PersistedState
            {
                rootTransformPath = GetAssetPath(gameObject),
                expressionFolderPath = GetAssetPath(expressionFolder),
                poseAnimationPath = GetAssetPath(poseAnimation),
                animOpen = animOpen,
                scrollX = scrollPosition.x,
                scrollY = scrollPosition.y,
                poses = poses
            };
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(state));
        }

        private void LoadState()
        {
            if (!EditorPrefs.HasKey(PrefsKey))
            {
                return;
            }
            string json = EditorPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json))
            {
                return;
            }
            try
            {
                var state = JsonUtility.FromJson<PersistedState>(json);
                if (state == null)
                {
                    return;
                }
                gameObject = LoadTransform(state.rootTransformPath);
                expressionFolder = LoadObject(state.expressionFolderPath);
                poseAnimation = LoadObject(state.poseAnimationPath) as AnimationClip;
                animOpen = state.animOpen;
                scrollPosition = new Vector2(state.scrollX, state.scrollY);
                if (state.poses != null)
                {
                    poses = state.poses;
                }
            }
            catch { }
        }

        private static string GetAssetPath(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return null;
            }
            if (obj is Transform)
            {
                GameObject go = ((Transform)obj).gameObject;
                string path = AssetDatabase.GetAssetPath(go);
                if (!string.IsNullOrEmpty(path))
                {
                    return path;
                }
#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
                return PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
#else
                return null;
#endif
            }
            return AssetDatabase.GetAssetPath(obj);
        }

        private static GameObject LoadTransform(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return go;
        }

        private static UnityEngine.Object LoadObject(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        }

        [MenuItem("UMA/Pose Tools/Expression Extractor", priority = 2)]
        public static void OpenExpressionExtractor()
        {
            EditorWindow win = GetWindow(typeof(ExpressionExtractor));
            win.titleContent.text = "Expression Extractor";
        }
    }
}
