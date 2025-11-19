#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA.PoseTools;
using System.IO;

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

        private enum Axis { X = 0, Y = 1, Z = 2 }

        [System.Serializable]
        private class AxisMap
        {
            public bool enabled;
            public Axis sourceAxis;
            public Axis targetAxis;
            public bool invert;
        }

        // Rotation axis mapping (applied in Euler space)
        [SerializeField] private AxisMap rotX = new AxisMap { sourceAxis = Axis.X, targetAxis = Axis.X };
        [SerializeField] private AxisMap rotY = new AxisMap { sourceAxis = Axis.Y, targetAxis = Axis.Y };
        [SerializeField] private AxisMap rotZ = new AxisMap { sourceAxis = Axis.Z, targetAxis = Axis.Z };

        // Translation axis mapping
        [SerializeField] private AxisMap posX = new AxisMap { sourceAxis = Axis.X, targetAxis = Axis.X };
        [SerializeField] private AxisMap posY = new AxisMap { sourceAxis = Axis.Y, targetAxis = Axis.Y };
        [SerializeField] private AxisMap posZ = new AxisMap { sourceAxis = Axis.Z, targetAxis = Axis.Z };

        private readonly List<UMABonePose> _queuedPoses = new List<UMABonePose>();
        private Vector2 _scroll;
        private bool _showRotation = true;
        private bool _showTranslation = true;
        private bool _confirmOverwrite;
        private string _status = "Ready";

        [MenuItem("UMA/Pose Tools/Bone Pose Converter", priority = 2)]
        public static void OpenWindow()
        {
            var win = GetWindow<BonePoseConversionWindow>(false, "Bone Pose Converter", true);
            win.minSize = new Vector2(420, 320);
            win.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Bone Pose Conversion", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Configure axis remapping then drag & drop UMABonePose assets into the drop area. Each pose will be converted using the backup as the source (or a backup will be created).", MessageType.Info);

            DrawAxisSection(ref _showRotation, "Rotation Conversion", rotX, rotY, rotZ);
            DrawAxisSection(ref _showTranslation, "Translation Conversion", posX, posY, posZ);

            EditorGUILayout.Space();
            _confirmOverwrite = EditorGUILayout.ToggleLeft("Force overwrite existing converted data (rebuild from backup)", _confirmOverwrite);

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
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawAxisRow(AxisMap map, string label)
        {
            EditorGUILayout.BeginHorizontal();
            map.enabled = EditorGUILayout.Toggle(map.enabled, GUILayout.Width(18));
            EditorGUILayout.LabelField(label, GUILayout.Width(24));
            EditorGUILayout.LabelField("==>", GUILayout.Width(30));
            map.targetAxis = (Axis)EditorGUILayout.EnumPopup(map.targetAxis, GUILayout.Width(50));
            map.invert = EditorGUILayout.ToggleLeft("invert", map.invert, GUILayout.Width(60));
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
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(120));
            for (int i = 0; i < _queuedPoses.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(_queuedPoses[i], typeof(UMABonePose), false);
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    _queuedPoses.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Clear List"))
            {
                _queuedPoses.Clear();
            }
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

            // Prompt to clear queue
            int count = _queuedPoses.Count; // number originally converted
            if (count > 0)
            {
                int choice = EditorUtility.DisplayDialogComplex("Bone Pose Conversion", "Converted " + count + " bone pose(s). Clear queued list?", "Yes", "No", "Cancel");
                if (choice == 0) // Yes
                {
                    _queuedPoses.Clear();
                    Repaint();
                }
            }
        }

        private void ConvertBonePose(UMABonePose target)
        {
            if (target == null || target.poses == null) return;
            if (IsBackupPose(target))
            {
                // Skip converting backup itself to avoid *_Backup_Backup
                return;
            }

            // Locate source folder
            string assetPath = AssetDatabase.GetAssetPath(target);
            string folder = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(folder)) return;

            // Ensure backup subfolder exists
            string backupFolder = folder + "/" + BackupFolderName;
            if (!AssetDatabase.IsValidFolder(backupFolder))
            {
                AssetDatabase.CreateFolder(folder, BackupFolderName);
            }

            string newBackupPath = backupFolder + "/" + target.name + BackupSuffix + ".asset";
            string legacyBackupPath = folder + "/" + target.name + BackupSuffix + ".asset";

            UMABonePose backup = AssetDatabase.LoadAssetAtPath<UMABonePose>(newBackupPath);
            if (backup == null)
            {
                UMABonePose legacyBackup = AssetDatabase.LoadAssetAtPath<UMABonePose>(legacyBackupPath);
                if (legacyBackup != null && !IsBackupPose(legacyBackup))
                {
                    string moveResult = AssetDatabase.MoveAsset(legacyBackupPath, newBackupPath);
                    if (!string.IsNullOrEmpty(moveResult))
                    {
                        Debug.LogWarning("BonePoseConversion: Unable to move legacy backup: " + moveResult);
                    }
                    backup = AssetDatabase.LoadAssetAtPath<UMABonePose>(newBackupPath);
                }
            }

            if (backup == null)
            {
                backup = ScriptableObject.CreateInstance<UMABonePose>();
                backup.poses = ClonePoseArray(target.poses);
                AssetDatabase.CreateAsset(backup, newBackupPath);
                EditorUtility.SetDirty(backup);
            }

            if (_confirmOverwrite && backup != null)
            {
                backup.poses = ClonePoseArray(target.poses);
                EditorUtility.SetDirty(backup);
            }

            UMABonePose.PoseBone[] sourceBones = backup.poses;
            if (sourceBones == null) return;

            var converted = new List<UMABonePose.PoseBone>(sourceBones.Length);
            foreach (var pb in sourceBones)
            {
                var newBone = new UMABonePose.PoseBone
                {
                    bone = pb.bone,
                    hash = pb.hash,
                    scale = pb.scale // scale untouched
                };

                // Translation conversion
                newBone.position = ConvertVector(pb.position, posX, posY, posZ);

                // Rotation conversion (Euler remap)
                Vector3 srcEuler = pb.rotation.eulerAngles; // degrees
                Vector3 convertedEuler = ConvertEuler(srcEuler, rotX, rotY, rotZ);
                newBone.rotation = Quaternion.Euler(convertedEuler);

                converted.Add(newBone);
            }

            target.poses = converted.ToArray();
            EditorUtility.SetDirty(target);
        }

        private bool IsBackupPose(UMABonePose pose)
        {
            if (pose == null) return false;
            string assetPath = AssetDatabase.GetAssetPath(pose);
            if (string.IsNullOrEmpty(assetPath)) return pose.name.EndsWith(BackupSuffix, System.StringComparison.OrdinalIgnoreCase);
            // If it's inside the backup folder OR its name already ends with the suffix, treat as backup
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
                    category = s.category
                };
            }
            return arr;
        }

        private static Vector3 ConvertVector(Vector3 src, AxisMap a1, AxisMap a2, AxisMap a3)
        {
            Vector3 dst = src; // start with identity
            ApplyAxisMap(ref dst, src, a1);
            ApplyAxisMap(ref dst, src, a2);
            ApplyAxisMap(ref dst, src, a3);
            return dst;
        }

        private static void ApplyAxisMap(ref Vector3 dst, Vector3 src, AxisMap map)
        {
            if (!map.enabled) return;
            float v = src[(int)map.sourceAxis];
            if (map.invert) v = -v;
            switch (map.targetAxis)
            {
                case Axis.X: dst.x = v; break;
                case Axis.Y: dst.y = v; break;
                case Axis.Z: dst.z = v; break;
            }
        }

        private static Vector3 ConvertEuler(Vector3 srcEuler, AxisMap a1, AxisMap a2, AxisMap a3)
        {
            Vector3 dst = srcEuler;
            ApplyEulerAxisMap(ref dst, srcEuler, a1);
            ApplyEulerAxisMap(ref dst, srcEuler, a2);
            ApplyEulerAxisMap(ref dst, srcEuler, a3);
            return dst;
        }

        private static void ApplyEulerAxisMap(ref Vector3 dst, Vector3 src, AxisMap map)
        {
            if (!map.enabled) return;
            float v = src[(int)map.sourceAxis];
            if (map.invert) v = -v;
            switch (map.targetAxis)
            {
                case Axis.X: dst.x = v; break;
                case Axis.Y: dst.y = v; break;
                case Axis.Z: dst.z = v; break;
            }
        }

        private void ValidateMapping(params AxisMap[] maps)
        {
            var usedTargets = new Dictionary<Axis, int>();
            foreach (var m in maps)
            {
                if (!m.enabled) continue;
                if (usedTargets.TryGetValue(m.targetAxis, out int count))
                {
                    usedTargets[m.targetAxis] = count + 1;
                }
                else
                {
                    usedTargets.Add(m.targetAxis, 1);
                }
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
    }
}
#endif
