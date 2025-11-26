#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using UMA.PoseTools;

namespace UMA.Editors
{
    [CustomEditor(typeof(AnimationFramePreview))]
    public class AnimationFramePreviewEditor : Editor
    {
        private SerializedProperty clipProperty;
        private SerializedProperty normalizedTimeProperty;
        private SerializedProperty posesProperty;
        private bool isDelayCallRegistered;
        private Vector2 scrollPosition;

        private void OnEnable()
        {
            if (IsEditorBusy || target == null)
            {
                if (!isDelayCallRegistered)
                {
                    EditorApplication.delayCall += OnDelayCallEnable;
                    isDelayCallRegistered = true;
                }
                return;
            }

            clipProperty = serializedObject.FindProperty("clip");
            normalizedTimeProperty = serializedObject.FindProperty("normalizedTime");
            posesProperty = serializedObject.FindProperty("poses");
            
            // Initialize poses list if it's null
            if (posesProperty == null)
            {
                Debug.LogWarning("[AnimationFramePreview] poses property not found in serializedObject. The component may need to be re-added.");
            }
        }

        private void OnDelayCallEnable()
        {
            if (this != null)
            {
                OnEnable();
            }
        }

        private void OnDestroy()
        {
            if (isDelayCallRegistered)
            {
                EditorApplication.delayCall -= OnDelayCallEnable;
                isDelayCallRegistered = false;
            }
        }

        private static bool IsEditorBusy
        {
            get
            {
                return EditorApplication.isCompiling || EditorApplication.isUpdating;
            }
        }

        public override void OnInspectorGUI()
        {
            if (IsEditorBusy)
            {
                EditorGUILayout.HelpBox("Unity is compiling/reloading. Please wait...", MessageType.Info);
                return;
            }

            if (target == null || serializedObject == null || serializedObject.targetObject == null)
            {
                EditorGUILayout.HelpBox("Inspector target is not available (asset reloading).", MessageType.Info);
                return;
            }

            serializedObject.Update();

            EditorGUI.BeginChangeCheck();

            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

            EditorGUILayout.LabelField("Animation Frame Preview", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox("This component allows you to preview animation frames in edit mode. Adjust the normalized time slider to scrub through the animation.", MessageType.Info);

            EditorGUILayout.PropertyField(clipProperty, new GUIContent("Animation Clip", "The animation clip to preview"));

            EditorGUILayout.Space();

            if (clipProperty.objectReferenceValue != null)
            {
                EditorGUILayout.PropertyField(normalizedTimeProperty, new GUIContent("Normalized Time", "0 = start of animation, 1 = end of animation"));

                AnimationClip clip = clipProperty.objectReferenceValue as AnimationClip;
                if (clip != null)
                {
                    float currentTime = normalizedTimeProperty.floatValue * clip.length;
                    EditorGUILayout.LabelField("Current Time", string.Format("{0:F3}s / {1:F3}s", currentTime, clip.length));
                    int frameCount = Mathf.CeilToInt(clip.length * clip.frameRate);
                    EditorGUILayout.LabelField("Frame Count: " + frameCount);
                }

                EditorGUILayout.Space();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Start"))
                {
                    normalizedTimeProperty.floatValue = 0f;
                }
                if (GUILayout.Button("Middle"))
                {
                    normalizedTimeProperty.floatValue = 0.5f;
                }
                if (GUILayout.Button("End"))
                {
                    normalizedTimeProperty.floatValue = 1f;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space();
                DrawPoseList(clip);
            }
            else
            {
                EditorGUILayout.HelpBox("Please assign an Animation Clip to preview.", MessageType.Warning);
            }

            GUIHelper.EndVerticalPadded(10);

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                if (!Application.isPlaying)
                {
                    EditorUtility.SetDirty(target);
                }
            }
        }

        private void DrawPoseList(AnimationClip clip)
        {
            EditorGUILayout.LabelField("Pose List", EditorStyles.boldLabel);

            if (posesProperty == null)
            {
                EditorGUILayout.HelpBox("Poses property not found. Try removing and re-adding the component, or check that the 'poses' field is properly serialized.", MessageType.Warning);
                
                // Try to reinitialize
                if (GUILayout.Button("Try to Reinitialize"))
                {
                    serializedObject.Update();
                    posesProperty = serializedObject.FindProperty("poses");
                    if (posesProperty != null)
                    {
                        EditorUtility.DisplayDialog("Success", "Poses property found!", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Failed", "Could not find poses property. Please remove and re-add the component.", "OK");
                    }
                }
                return;
            }

            bool validPose = false;

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(200));

            for (int i = 0; i < posesProperty.arraySize; i++)
            {
                SerializedProperty pose = posesProperty.GetArrayElementAtIndex(i);
                SerializedProperty idProperty = pose.FindPropertyRelative("ID");
                SerializedProperty frameProperty = pose.FindPropertyRelative("frame");

                GUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.LabelField("ID", GUILayout.Width(50f));
                string newID = EditorGUILayout.TextField(idProperty.stringValue);
                EditorGUILayout.LabelField("Frame", GUILayout.Width(60f));
                int newFrame = EditorGUILayout.IntField(frameProperty.intValue, GUILayout.Width(50f));
                
                if (EditorGUI.EndChangeCheck())
                {
                    idProperty.stringValue = newID;
                    frameProperty.intValue = newFrame;
                }

                if (!string.IsNullOrEmpty(idProperty.stringValue))
                {
                    validPose = true;
                }

                bool canGotoFrame = clip != null;
                EditorGUI.BeginDisabledGroup(!canGotoFrame);
                if (GUILayout.Button("Go", GUILayout.Width(30f)))
                {
                    AnimationFramePreview preview = target as AnimationFramePreview;
                    if (preview != null && clip != null)
                    {
                        float time = frameProperty.intValue / clip.frameRate;
                        preview.GotoFrame(time);
                    }
                }
                EditorGUI.EndDisabledGroup();

                if (GUILayout.Button("-", GUILayout.Width(20f)))
                {
                    posesProperty.DeleteArrayElementAtIndex(i);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+", GUILayout.Width(30f)))
            {
                posesProperty.InsertArrayElementAtIndex(posesProperty.arraySize);
                SerializedProperty newPose = posesProperty.GetArrayElementAtIndex(posesProperty.arraySize - 1);
                newPose.FindPropertyRelative("ID").stringValue = "";
                newPose.FindPropertyRelative("frame").intValue = 0;
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Export expression set"))
            {
                ExportExpressionSet();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load Pose Set"))
            {
                LoadPoseSet();
            }
            EditorGUI.BeginDisabledGroup(!validPose);
            if (GUILayout.Button("Save Pose Set"))
            {
                SavePoseSet();
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();
        }

        private void SavePoseSet()
        {
            AnimationFramePreview preview = target as AnimationFramePreview;
            if (preview == null)
            {
                return;
            }

            string folderPath = "";
            if (clipProperty.objectReferenceValue != null)
            {
                folderPath = AssetDatabase.GetAssetPath(clipProperty.objectReferenceValue);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    folderPath = folderPath.Substring(0, folderPath.LastIndexOf('/'));
                }
            }

            AnimationClip clip = clipProperty.objectReferenceValue as AnimationClip;
            string defaultName = (clip != null ? clip.name : "PoseSet") + "_Poses.xml";
            string filePath = EditorUtility.SaveFilePanel("Save pose set", folderPath, defaultName, "xml");

            if (!string.IsNullOrEmpty(filePath))
            {
                preview.SavePoseSet(filePath);
                EditorUtility.SetDirty(target);
            }
        }

        private void ExportExpressionSet()
        {
            AnimationFramePreview preview = target as AnimationFramePreview;
            AnimationClip clip = clipProperty.objectReferenceValue as AnimationClip;
            if (preview == null || clip == null || posesProperty == null || posesProperty.arraySize == 0)
            {
                Debug.LogWarning("[AnimationFramePreview] Cannot export expressions: missing clip, preview, or poses.");
                return;
            }

            // Choose output folder
            string baseFolder = AssetDatabase.GetAssetPath(clip);
            if (!string.IsNullOrEmpty(baseFolder))
            {
                baseFolder = baseFolder.Substring(0, baseFolder.LastIndexOf('/'));
            }
            else
            {
                baseFolder = "Assets";
            }

            string chosenFolder = EditorUtility.OpenFolderPanel("Select folder for expression poses", baseFolder, "");
            if (string.IsNullOrEmpty(chosenFolder))
            {
                Debug.Log("[AnimationFramePreview] Export cancelled.");
                return;
            }

            // Ensure folder is inside project Assets
            if (!chosenFolder.Replace("\\", "/").Contains("/Assets"))
            {
                EditorUtility.DisplayDialog("Invalid Folder", "Folder must be inside the project's Assets directory.", "OK");
                return;
            }
            string projectPath = Application.dataPath.Replace("\\", "/");
            string relFolder = chosenFolder.Replace("\\", "/");
            if (relFolder.StartsWith(projectPath))
            {
                relFolder = "Assets" + relFolder.Substring(projectPath.Length);
            }

            // Sample base frame (frame 0)
            preview.GotoFrame(0f);
            Transform root = preview.gameObject.transform;
            Transform[] allBones = root.GetComponentsInChildren<Transform>(true);
            var basePositions = new Dictionary<string, Vector3>(allBones.Length);
            var baseRotations = new Dictionary<string, Quaternion>(allBones.Length);
            var baseScales = new Dictionary<string, Vector3>(allBones.Length);

            foreach (Transform t in allBones)
            {
                string path = (t == root) ? root.name : AnimationUtility.CalculateTransformPath(t, root);
                if (!basePositions.ContainsKey(path))
                {
                    basePositions.Add(path, t.localPosition);
                    baseRotations.Add(path, t.localRotation);
                    baseScales.Add(path, t.localScale);
                }
            }

            int createdCount = 0;
            const float posTol = 0.0005f;
            const float scaleTol = 0.01f;
            const float rotAngleTol = 0.01f;

            for (int i = 0; i < posesProperty.arraySize; i++)
            {
                SerializedProperty poseProp = posesProperty.GetArrayElementAtIndex(i);
                SerializedProperty idProp = poseProp.FindPropertyRelative("ID");
                SerializedProperty frameProp = poseProp.FindPropertyRelative("frame");
                string poseID = idProp.stringValue;
                int frameIndex = frameProp.intValue;
                if (string.IsNullOrEmpty(poseID))
                {
                    continue; // skip unnamed
                }
                float time = frameIndex / clip.frameRate;
                if (time < 0f || time > clip.length)
                {
                    Debug.LogWarning("[AnimationFramePreview] Skipping pose '" + poseID + "' invalid frame " + frameIndex);
                    continue;
                }
                // Sample animation at this time
                preview.GotoFrame(time);

                List<UMABonePose.PoseBone> changedBones = new List<UMABonePose.PoseBone>();
                foreach (Transform t in allBones)
                {

                    string path = (t == root) ? root.name : AnimationUtility.CalculateTransformPath(t, root);
                    Vector3 basePos = basePositions[path];
                    Quaternion baseRot = baseRotations[path];
                    Vector3 baseScale = baseScales[path];
                    Vector3 curPos = t.localPosition;
                    Quaternion curRot = t.localRotation;
                    Vector3 curScale = t.localScale;

                    float posDiff = Vector3.Distance(curPos, basePos);
                    float posxdiff = curPos.x - basePos.x;
                    float posydiff = curPos.y - basePos.y;
                    float poszdiff = curPos.z - basePos.z;
                    float scaleDiff = Vector3.Distance(curScale, baseScale);
                    float scalexdiff = curScale.x - baseScale.x;
                    float scaleydiff = curScale.y - baseScale.y;
                    float scalezdiff = curScale.z - baseScale.z;
                    float rotDiff = Quaternion.Angle(baseRot,  curRot);
                    float rotxdiff = curRot.x - baseRot.x;
                    float rotydiff = curRot.y - baseRot.y;
                    float rotzdiff = curRot.z - baseRot.z;
                    float rotwdiff = curRot.w - baseRot.w;
                    if (poseID.Equals("NeckUp") && t.name.Equals("LeftLipsSuperiorMiddle"))
                    {
                        Debug.Log("Neckup/LeftLipsSuperiorMiddle Diff values for tolerance checking");
                        Debug.Log($"PosDiff = {posDiff}");
                        Debug.Log($"posxdiff = {posxdiff}");
                        Debug.Log($"posydiff = {posydiff}");
                        Debug.Log($"poszdiff = {poszdiff}");
                        Debug.Log($"scalexdiff = {scalexdiff}");
                        Debug.Log($"scaleydiff = {scaleydiff}");
                        Debug.Log($"scalezdiff = {scalezdiff}");
                        Debug.Log($"rotxdiff = {rotxdiff}");
                        Debug.Log($"rotydiff = {rotydiff}");
                        Debug.Log($"rotzdiff = {rotzdiff}");
                        Debug.Log($"rotwdiff = {rotwdiff}");
                    }

                    bool posChanged = Mathf.Abs(curPos.x - basePos.x) > posTol || Mathf.Abs(curPos.y - basePos.y) > posTol || Mathf.Abs(curPos.z - basePos.z) > posTol;
                    bool scaleChanged = Mathf.Abs(curScale.x - baseScale.x) > scaleTol || Mathf.Abs(curScale.y - baseScale.y) > scaleTol || Mathf.Abs(curScale.z - baseScale.z) > scaleTol;
                    bool rotChanged = Quaternion.Angle(baseRot, curRot) > rotAngleTol;

                    if (posChanged || rotChanged || scaleChanged)
                    {
                        Vector3 deltaPos = curPos - basePos;
                        Quaternion deltaRot = curRot * Quaternion.Inverse(baseRot);
                        Vector3 deltaScale = new Vector3(
                            baseScale.x != 0 ? curScale.x / baseScale.x : curScale.x,
                            baseScale.y != 0 ? curScale.y / baseScale.y : curScale.y,
                            baseScale.z != 0 ? curScale.z / baseScale.z : curScale.z);

                        changedBones.Add(new UMABonePose.PoseBone
                        {
                            bone = t.name,
                            hash = UMAUtils.StringToHash(t.name),
                            position = deltaPos,
                            rotation = deltaRot,
                            scale = deltaScale,
                            category = "",
                            enabled = true
                        });
                    }
                }

                if (changedBones.Count > 0)
                {
                    string assetPath = relFolder + "/" + poseID + ".asset";
                    if (File.Exists(assetPath) == false && AssetDatabase.LoadAssetAtPath<UMABonePose>(assetPath) != null)
                    {
                        // fallback unique path if somehow exists
                        assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
                    }

                    UMABonePose bonePose = ScriptableObject.CreateInstance<UMABonePose>();
                    bonePose.poses = changedBones.ToArray();
                    AssetDatabase.CreateAsset(bonePose, assetPath);
                    EditorUtility.SetDirty(bonePose);
                    AssetDatabase.SaveAssetIfDirty(bonePose);
                    AssetDatabase.ImportAsset(assetPath);
                    createdCount++;
                }
                else
                {
                    Debug.Log("[AnimationFramePreview] Pose '" + poseID + "' has no changed bones, skipped.");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[AnimationFramePreview] Exported " + createdCount + " expression pose asset(s) to " + relFolder);
        }

        private void LoadPoseSet()
        {
            AnimationFramePreview preview = target as AnimationFramePreview;
            if (preview == null) return;
            string folderPath = "";
            if (clipProperty.objectReferenceValue != null)
            {
                folderPath = AssetDatabase.GetAssetPath(clipProperty.objectReferenceValue);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    folderPath = folderPath.Substring(0, folderPath.LastIndexOf('/'));
                }
            }
            string filePath = EditorUtility.OpenFilePanel("Load pose set", folderPath, "xml");
            if (!string.IsNullOrEmpty(filePath))
            {
                preview.LoadPoseSet(filePath);
                serializedObject.Update();
                posesProperty = serializedObject.FindProperty("poses");
                EditorUtility.SetDirty(target);
                Repaint();
            }
        }
    }
}
#endif