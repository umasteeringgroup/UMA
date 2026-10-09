using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public sealed class TexturePaintProjectSettings : ScriptableObject
    {
        private const string SettingsPath = TexturePaintPaths.Root + "/Settings.asset";
        [SerializeField] private bool compactView = true;
        [SerializeField] private string recoveryFolder = TexturePaintPaths.RecoveryRoot;
        [SerializeField] private bool automaticRecovery = true;
        [SerializeField] private float recoveryIdleDelaySeconds = 120;
        [SerializeField] private float recoveryMinimumIntervalSeconds = 300;
        private static TexturePaintProjectSettings cached;

        internal static TexturePaintProjectSettings GetSettings()
        {
            if (cached != null) return cached;
            cached = AssetDatabase.LoadAssetAtPath<TexturePaintProjectSettings>(SettingsPath);
            if (cached != null) return cached;
            if (File.Exists(SettingsPath))
                throw new InvalidOperationException("Overlay Painter settings path contains a different asset: " + SettingsPath);
            var created = CreateInstance<TexturePaintProjectSettings>();
            try
            {
                created.ImportLegacy(UMASettings.GetSettings());
                UMAPathUtility.EnsureAssetFolder(TexturePaintPaths.Root);
                AssetDatabase.CreateAsset(created, SettingsPath);
                AssetDatabase.SaveAssetIfDirty(created);
                return cached = created;
            }
            catch
            {
                if (!EditorUtility.IsPersistent(created)) DestroyImmediate(created);
                throw;
            }
        }

        internal void ImportLegacy(UMASettings legacy)
        {
            if (legacy == null) return;
            compactView = legacy.texturePaintCompactView;
            recoveryFolder = legacy.texturePaintRecoveryFolder;
            automaticRecovery = legacy.texturePaintAutomaticRecovery;
            recoveryIdleDelaySeconds = legacy.texturePaintRecoveryIdleDelaySeconds;
            recoveryMinimumIntervalSeconds = legacy.texturePaintRecoveryMinimumIntervalSeconds;
        }

        private void SaveSettings()
        {
            recoveryIdleDelaySeconds = Mathf.Max(15, recoveryIdleDelaySeconds);
            recoveryMinimumIntervalSeconds = Mathf.Max(0, recoveryMinimumIntervalSeconds);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);
            var legacy = UMASettings.GetSettings();
            if (legacy == null) return;
            Undo.RecordObject(legacy, "Overlay Painter settings compatibility");
            legacy.texturePaintCompactView = compactView;
            legacy.texturePaintRecoveryFolder = recoveryFolder;
            legacy.texturePaintAutomaticRecovery = automaticRecovery;
            legacy.texturePaintRecoveryIdleDelaySeconds = recoveryIdleDelaySeconds;
            legacy.texturePaintRecoveryMinimumIntervalSeconds = recoveryMinimumIntervalSeconds;
            EditorUtility.SetDirty(legacy);
            AssetDatabase.SaveAssetIfDirty(legacy);
        }

        public static bool TexturePaintCompactView => GetSettings().compactView;
        public static bool TexturePaintAutomaticRecovery => GetSettings().automaticRecovery;
        public static double TexturePaintRecoveryIdleDelaySeconds => Math.Max(15, GetSettings().recoveryIdleDelaySeconds);
        public static double TexturePaintRecoveryMinimumIntervalSeconds => Math.Max(0, GetSettings().recoveryMinimumIntervalSeconds);
        public static string TexturePaintRecoveryFolder
        {
            get
            {
                string path = GetSettings().recoveryFolder?.Trim().Replace('\\', '/').TrimEnd('/');
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    Array.Exists(path.Split('/'), p => string.IsNullOrWhiteSpace(p) || p == "." || p == ".."))
                    return TexturePaintPaths.RecoveryRoot;
                return path;
            }
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider() => new SettingsProvider("Project/UMA/Overlay Painter", SettingsScope.Project)
        {
            label = "Overlay Painter",
            guiHandler = _ =>
            {
                var settings = GetSettings();
                using var serialized = new SerializedObject(settings);
                serialized.Update();
                EditorGUILayout.PropertyField(serialized.FindProperty("compactView"));
                EditorGUILayout.PropertyField(serialized.FindProperty("recoveryFolder"));
                EditorGUILayout.PropertyField(serialized.FindProperty("automaticRecovery"), new GUIContent("Enable Automatic Recovery"));
                EditorGUILayout.PropertyField(serialized.FindProperty("recoveryIdleDelaySeconds"), new GUIContent("Recovery Idle Delay (seconds)"));
                EditorGUILayout.PropertyField(serialized.FindProperty("recoveryMinimumIntervalSeconds"), new GUIContent("Minimum Save Interval (seconds)"));
                if (serialized.ApplyModifiedProperties()) settings.SaveSettings();
                EditorGUILayout.HelpBox("Settings and authored documents stay in UMAProjectData when the plugin is removed.", MessageType.Info);
            }
        };
    }
}
