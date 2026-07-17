#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA.PoseTools;
using System.IO;
using UnityEngine.SceneManagement;

namespace UMA.PoseTools
{
    /// <summary>
    /// Editor window for converting UMABonePose assets by remapping translation and rotation axes with optional inversion.
    /// Creates a backup ("<name>_Backup") if one does not already exist; reconversions always use the backup as source.
    /// Backups are stored in a dedicated subfolder inside the source asset's folder.
    /// </summary>
    public class BonePoseConversionWindow : EditorWindow
    {
        private const string BackupSuffix = "_Backup";
        private const string BackupFolderName = "BonePoseBackups"; // subfolder name
        private const string SettingsKey = "UMA_BonePoseConversion_Settings_v1"; // EditorPrefs key

        private enum Axis { X = 0, Y = 1, Z = 2 }

        [System.Serializable]
        private class AxisMap
        {
            public bool enabled;
            public Axis sourceAxis;
            public Axis targetAxis;
            public bool invert;
        }

        [System.Serializable]
        private class PersistedSettings
        {
            public AxisMap rotX;
            public AxisMap rotY;
            public AxisMap rotZ;
            public AxisMap posX;
            public AxisMap posY;
            public AxisMap posZ;
        }

        // Rotation axis mapping (applied in Euler space)
        [SerializeField] private AxisMap rotX = new AxisMap { sourceAxis = Axis.X, targetAxis = Axis.X, enabled = true };
        [SerializeField] private AxisMap rotY = new AxisMap { sourceAxis = Axis.Y, targetAxis = Axis.Y, enabled = true };
        [SerializeField] private AxisMap rotZ = new AxisMap { sourceAxis = Axis.Z, targetAxis = Axis.Z, enabled = true };

        // Translation axis mapping
        [SerializeField] private AxisMap posX = new AxisMap { sourceAxis = Axis.X, targetAxis = Axis.X, enabled = true };
        [SerializeField] private AxisMap posY = new AxisMap { sourceAxis = Axis.Y, targetAxis = Axis.Y, enabled = true };
        [SerializeField] private AxisMap posZ = new AxisMap { sourceAxis = Axis.Z, targetAxis = Axis.Z, enabled = true };

        private readonly List<UMABonePose> _queuedPoses = new List<UMABonePose>();
        private readonly List<int> _boneDropdownIndices = new List<int>(); // selection per queued pose
        private Vector2 _scroll;
        private bool _showRotation = true;
        private bool _showTranslation = true;
        private string _status = "Ready";
        private bool _settingsDirty;

        [MenuItem("UMA/Tools/Pose Tools/Bone Pose Converter", priority = 121)]
        public static void OpenWindow()
        {
            var win = GetWindow<BonePoseConversionWindow>(false, "Bone Pose Converter", true);
            win.minSize = new Vector2(420, 320);
            win.Show();
        }

        private void OnEnable()
        {
            LoadSettings();
            EditorApplication.update += DoInspectors;
        }

        private void OnDisable()
        {
            SaveSettings();
            EditorApplication.update -= DoInspectors;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Bone Pose Conversion", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Configure axis remapping then drag & drop UMABonePose assets into the drop area. Each pose will be converted using the backup as the source (or a backup will be created).", MessageType.Info);

            DrawAxisSection(ref _showRotation, "Rotation Conversion", rotX, rotY, rotZ);
            DrawAxisSection(ref _showTranslation, "Translation Conversion", posX, posY, posZ);

            // Quick self-test utility
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Self-Test Axis Mapping", GUILayout.Width(180)))
            {
                RunSelfTest();
            }
            EditorGUILayout.EndHorizontal();

            if (_settingsDirty && Event.current.type == EventType.Repaint)
            {
                SaveSettings();
                _settingsDirty = false;
            }

            EditorGUILayout.Space();
            DrawDropPad();
            DrawQueuedList();

            using (new EditorGUI.DisabledScope(_queuedPoses.Count == 0))
            {
                if (GUILayout.Button("Convert Queued Bone Poses"))
                {
                    ConvertQueued();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status: " + _status, EditorStyles.helpBox);
        }

        private void DrawAxisSection(ref bool foldout, string title, AxisMap a1, AxisMap a2, AxisMap a3)
        {
            foldout = EditorGUILayout.Foldout(foldout, title, true);
            if (!foldout) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawAxisRow(a1, "X");
            DrawAxisRow(a2, "Y");
            DrawAxisRow(a3, "Z");
            if (GUILayout.Button("Validate Mapping"))
            {
                ValidateMapping(a1, a2, a3);
                SaveSettings();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawAxisRow(AxisMap map, string label)
        {
            EditorGUILayout.BeginHorizontal();
            bool prevEnabled = map.enabled;
            Axis prevTarget = map.targetAxis;
            bool prevInvert = map.invert;
            map.enabled = EditorGUILayout.Toggle(map.enabled, GUILayout.Width(18));
            EditorGUILayout.LabelField(label, GUILayout.Width(24));
            EditorGUILayout.LabelField("==>", GUILayout.Width(30));
            map.targetAxis = (Axis)EditorGUILayout.EnumPopup(map.targetAxis, GUILayout.Width(50));
            map.invert = EditorGUILayout.ToggleLeft("invert", map.invert, GUILayout.Width(60));
            if (prevEnabled != map.enabled || prevTarget != map.targetAxis || prevInvert != map.invert)
            {
                _settingsDirty = true;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDropPad()
        {
            Rect r = GUILayoutUtility.GetRect(0, 60, GUILayout.ExpandWidth(true));
            GUI.Box(r, "Drag UMABonePose assets here", EditorStyles.helpBox);
            Event e = Event.current;
            if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && r.Contains(e.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (Object obj in DragAndDrop.objectReferences)
                    {
                        if (obj is UMABonePose bp && !_queuedPoses.Contains(bp) && !IsBackupPose(bp))
                        {
                            _queuedPoses.Add(bp);
                        }
                    }
                    e.Use();
                }
            }
        }

        private void DrawQueuedList()
        {
            if (_queuedPoses.Count == 0)
            {
                EditorGUILayout.HelpBox("No bone poses queued.", MessageType.None);
                return;
            }
            EditorGUILayout.LabelField("Queued Bone Poses (" + _queuedPoses.Count + ")", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(150));
            for (int i = 0; i < _queuedPoses.Count; i++)
            {
                var poseAsset = _queuedPoses[i];
                if (_boneDropdownIndices.Count <= i) _boneDropdownIndices.Add(0);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(poseAsset, typeof(UMABonePose), false);
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    _queuedPoses.RemoveAt(i);
                    _boneDropdownIndices.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();

                // Restore button (before bone selection UI)
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = poseAsset != null;
                if (GUILayout.Button("Restore"))
                {
                    if (poseAsset != null)
                    {
                        if (RestoreFromBackup(poseAsset))
                        {
                            EditorUtility.SetDirty(poseAsset);
                            _status = "Restored " + poseAsset.name + " from backup";
                        }
                        else
                        {
                            _status = "No backup found for " + poseAsset.name;
                        }
                        Repaint();
                    }
                }
                GUI.enabled = true;
                if (GUILayout.Button("Edit Pose"))
                {
                    InspectMe.Add(poseAsset);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                // Bone selection row
                var bones = poseAsset != null ? poseAsset.poses : null;
                if (bones != null && bones.Length > 0)
                {
                    string[] boneNames = new string[bones.Length];
                    for (int b = 0; b < bones.Length; b++) boneNames[b] = bones[b].bone;
                    int sel = Mathf.Clamp(_boneDropdownIndices[i], 0, boneNames.Length - 1);
                    EditorGUILayout.BeginHorizontal();
                    sel = EditorGUILayout.Popup(sel, boneNames, GUILayout.Width(180));
                    if (sel != _boneDropdownIndices[i]) _boneDropdownIndices[i] = sel;
                    if (GUILayout.Button("Select Bone", GUILayout.Width(100)))
                    {
                        string boneName = boneNames[_boneDropdownIndices[i]];
                        SelectBoneInScene(boneName);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.LabelField("(No bones in pose)");
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Clear List"))
            {
                _queuedPoses.Clear();
                _boneDropdownIndices.Clear();
            }
        }

        private List<UnityEngine.Object> InspectMe = new List<UnityEngine.Object>();
        private void DoInspectors()
        {
            if (InspectMe.Count > 0)
            {
                for (int i = 0; i < InspectMe.Count; i++)
                {
                    InspectorUtlity.InspectTarget(InspectMe[i]);
                }
                InspectMe.Clear();
            }
        }

        private bool RestoreFromBackup(UMABonePose target)
        {
            if (target == null) return false;
            string assetPath = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(assetPath)) return false;
            string folder = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(folder)) return false;
            string backupFolder = folder + "/" + BackupFolderName;
            string backupPath = backupFolder + "/" + target.name + BackupSuffix + ".asset";
            var backup = AssetDatabase.LoadAssetAtPath<UMABonePose>(backupPath);
            if (backup == null) return false;
            if (backup.poses == null) return false;
            // Replace pose array with cloned data (preserve enabled & category)
            target.poses = ClonePoseArray(backup.poses);
            return true;
        }

        private void ConvertQueued()
        {
            if (_queuedPoses.Count == 0) return;
            _status = "Converting...";
            try
            {
                int total = _queuedPoses.Count;
                for (int i = 0; i < total; i++)
                {
                    var target = _queuedPoses[i];
                    EditorUtility.DisplayProgressBar("Bone Pose Conversion", target.name, (float)i / total);
                    ConvertBonePose(target);
                }
                _status = "Conversion complete";
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }
            int count = _queuedPoses.Count;
            if (count > 1)
            {
                int choice = EditorUtility.DisplayDialogComplex("Bone Pose Conversion", "Converted " + count + " bone pose(s). Clear queued list?", "Yes", "No", "Cancel");
                if (choice == 0)
                {
                    _queuedPoses.Clear();
                    Repaint();
                }
            }
        }

        private void ConvertBonePose(UMABonePose target)
        {
            if (target == null || target.poses == null) return;
            if (IsBackupPose(target)) return;
            string assetPath = AssetDatabase.GetAssetPath(target);
            string folder = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(folder)) return;
            string backupFolder = folder + "/" + BackupFolderName;
            if (!AssetDatabase.IsValidFolder(backupFolder))
            {
                AssetDatabase.CreateFolder(folder, BackupFolderName);
            }
            string newBackupPath = backupFolder + "/" + target.name + BackupSuffix + ".asset";
            UMABonePose backup = AssetDatabase.LoadAssetAtPath<UMABonePose>(newBackupPath);
            if (backup == null)
            {
                backup = ScriptableObject.CreateInstance<UMABonePose>();
                backup.poses = ClonePoseArray(target.poses);
                AssetDatabase.CreateAsset(backup, newBackupPath);
                EditorUtility.SetDirty(backup);
            }
            var sourceBones = backup.poses;
            if (sourceBones == null) return;
            var converted = new List<UMABonePose.PoseBone>(sourceBones.Length);
            foreach (var pb in sourceBones)
            {
                bool applyPos = AxisMapChanges(posX) || AxisMapChanges(posY) || AxisMapChanges(posZ);
                bool applyRot = AxisMapChanges(rotX) || AxisMapChanges(rotY) || AxisMapChanges(rotZ);
                Vector3 newPos = applyPos ? ConvertVector(pb.position, posX, posY, posZ) : pb.position;
                Quaternion newRot = applyRot ? Quaternion.Euler(ConvertEuler(pb.rotation.eulerAngles, rotX, rotY, rotZ)) : pb.rotation;
                converted.Add(new UMABonePose.PoseBone
                {
                    bone = pb.bone,
                    hash = pb.hash,
                    position = newPos,
                    rotation = newRot,
                    scale = pb.scale,
                    category = pb.category,
                    enabled = pb.enabled
                });
            }
            target.poses = converted.ToArray();
            EditorUtility.SetDirty(target);
        }

        private bool IsBackupPose(UMABonePose pose)
        {
            if (pose == null) return false;
            string assetPath = AssetDatabase.GetAssetPath(pose);
            if (string.IsNullOrEmpty(assetPath)) return pose.name.EndsWith(BackupSuffix, System.StringComparison.OrdinalIgnoreCase);
            if (pose.name.EndsWith(BackupSuffix, System.StringComparison.OrdinalIgnoreCase)) return true;
            return assetPath.Contains("/" + BackupFolderName + "/");
        }

        private static UMABonePose.PoseBone[] ClonePoseArray(UMABonePose.PoseBone[] src)
        {
            if (src == null) return null;
            var arr = new UMABonePose.PoseBone[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var s = src[i];
                arr[i] = new UMABonePose.PoseBone
                {
                    bone = s.bone,
                    hash = s.hash,
                    position = s.position,
                    rotation = s.rotation,
                    scale = s.scale,
                    category = s.category,
                    enabled = s.enabled
                };
            }
            return arr;
        }

        // Helpers for safe axis access to avoid any implicit index confusion
        private static float GetAxis(Vector3 v, Axis a)
        {
            switch (a)
            {
                case Axis.X: return v.x;
                case Axis.Y: return v.y;
                case Axis.Z: return v.z;
                default: return 0f;
            }
        }
        private static void SetAxis(ref Vector3 v, Axis a, float value)
        {
            switch (a)
            {
                case Axis.X: v.x = value; break;
                case Axis.Y: v.y = value; break;
                case Axis.Z: v.z = value; break;
            }
        }

        private static Vector3 ConvertVector(Vector3 src, AxisMap a1, AxisMap a2, AxisMap a3)
        {
            Vector3 dst = src; // start from identity mapping
            ApplyAxisMap(ref dst, src, a1);
            ApplyAxisMap(ref dst, src, a2);
            ApplyAxisMap(ref dst, src, a3);
            return dst;
        }

        private static void ApplyAxisMap(ref Vector3 dst, Vector3 src, AxisMap map)
        {
            if (map == null || !map.enabled) return;
            float v = GetAxis(src, map.sourceAxis);
            if (map.invert) v = -v;
            SetAxis(ref dst, map.targetAxis, v);
        }

        private static Vector3 ConvertEuler(Vector3 srcEuler, AxisMap a1, AxisMap a2, AxisMap a3)
        {
            Vector3 dst = srcEuler; // start from identity mapping
            ApplyEulerAxisMap(ref dst, srcEuler, a1);
            ApplyEulerAxisMap(ref dst, srcEuler, a2);
            ApplyEulerAxisMap(ref dst, srcEuler, a3);
            return dst;
        }

        private static void ApplyEulerAxisMap(ref Vector3 dst, Vector3 src, AxisMap map)
        {
            if (map == null || !map.enabled) return;
            float v = GetAxis(src, map.sourceAxis);
            if (map.invert) v = -v;
            SetAxis(ref dst, map.targetAxis, v);
        }

        private void ValidateMapping(params AxisMap[] maps)
        {
            var usedTargets = new Dictionary<Axis, int>();
            foreach (var m in maps)
            {
                if (m == null || !m.enabled) continue;
                if (usedTargets.TryGetValue(m.targetAxis, out int count)) usedTargets[m.targetAxis] = count + 1; else usedTargets.Add(m.targetAxis, 1);
            }
            foreach (var kv in usedTargets)
            {
                if (kv.Value > 1)
                {
                    EditorUtility.DisplayDialog("Mapping Warning", $"Multiple source axes are mapped onto {kv.Key}. Last one will win.", "OK");
                    return;
                }
            }
            EditorUtility.DisplayDialog("Mapping OK", "Axis mapping configuration looks valid.", "OK");
        }

        private void SaveSettings()
        {
            var settings = new PersistedSettings
            {
                rotX = rotX,
                rotY = rotY,
                rotZ = rotZ,
                posX = posX,
                posY = posY,
                posZ = posZ
            };
            string json = JsonUtility.ToJson(settings);
            EditorPrefs.SetString(SettingsKey, json);
        }

        private void LoadSettings()
        {
            if (!EditorPrefs.HasKey(SettingsKey)) return;
            string json = EditorPrefs.GetString(SettingsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var settings = JsonUtility.FromJson<PersistedSettings>(json);
                if (settings != null)
                {
                    // Defensive cloning to avoid null overwrites
                    rotX = settings.rotX ?? rotX;
                    rotY = settings.rotY ?? rotY;
                    rotZ = settings.rotZ ?? rotZ;
                    posX = settings.posX ?? posX;
                    posY = settings.posY ?? posY;
                    posZ = settings.posZ ?? posZ;
                }
            }
            catch { }
        }

        private void SelectBoneInScene(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return;
            Transform found = null;
            var allUmaData = Object.FindObjectsByType<UMAData>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var umaData in allUmaData)
            {
                if (umaData?.skeleton != null)
                {
                    int hash = UMASkeleton.StringToHash(boneName);
                    var t = umaData.skeleton.GetBoneTransform(hash);
                    if (t != null)
                    {
                        found = t; break;
                    }
                }
            }
            if (found == null)
            {
                var scene = SceneManager.GetActiveScene();
                foreach (var root in scene.GetRootGameObjects())
                {
                    found = FindChildRecursive(root.transform, boneName);
                    if (found != null) break;
                }
            }
            if (found != null)
            {
                Selection.activeTransform = found;
                EditorGUIUtility.PingObject(found);
                // Autofocus in Scene view
                var sv = SceneView.lastActiveSceneView;
                if (sv != null)
                {
                    sv.FrameSelected();
                }
            }
            else
            {
                Debug.LogWarning("BonePoseConverter: Could not locate bone '" + boneName + "' in scene.");
            }
        }

        private Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                var res = FindChildRecursive(child, name);
                if (res != null) return res;
            }
            return null;
        }

        private void RunSelfTest()
        {
            // Test position mapping on canonical unit vectors
            Vector3 testX = new Vector3(1, 0, 0);
            Vector3 testY = new Vector3(0, 1, 0);
            Vector3 testZ = new Vector3(0, 0, 1);
            var outX = ConvertVector(testX, posX, posY, posZ);
            var outY = ConvertVector(testY, posX, posY, posZ);
            var outZ = ConvertVector(testZ, posX, posY, posZ);
            Debug.Log($"[BonePoseConverter] Pos map X {testX} -> {outX}, Y {testY} -> {outY}, Z {testZ} -> {outZ}");

            // Test rotation mapping similarly (Euler degrees)
            Vector3 rX = new Vector3(10, 0, 0);
            Vector3 rY = new Vector3(0, 10, 0);
            Vector3 rZ = new Vector3(0, 0, 10);
            var routX = ConvertEuler(rX, rotX, rotY, rotZ);
            var routY = ConvertEuler(rY, rotX, rotY, rotZ);
            var routZ = ConvertEuler(rZ, rotX, rotY, rotZ);
            Debug.Log($"[BonePoseConverter] Rot map X {rX} -> {routX}, Y {rY} -> {routY}, Z {rZ} -> {routZ}");

            _status = "Self-test logged to Console";
            Repaint();
        }

        private bool AxisMapChanges(AxisMap map)
        {
            if (map == null || !map.enabled) return false;
            if (map.invert) return true;
            // A change only if remapping to a different target axis
            return map.sourceAxis != map.targetAxis;
        }
    }
}
#endif
