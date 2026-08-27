using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    [CustomEditor(typeof(TexturePaintMaterialPreset))]
    internal sealed class TexturePaintMaterialPresetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            TexturePaintMaterialPreset preset = (TexturePaintMaterialPreset)target;
            serializedObject.Update();
            EditorGUILayout.LabelField("Overlay Painter Material Preset", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "A portable copy of an Overlay Painter layer stack. Applying it creates independent layers and reruns available generators in composition order.",
                MessageType.Info);

            DrawPropertiesExcluding(serializedObject, "m_Script", "schemaVersion", "presetId",
                "revision", "layers", "channels", "plugins", "packaged",
                "packagedFromPresetId", "packagedUtc", "packagedDependencies",
                "packagedExternalDependencies");
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Contents", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Revision", preset.revision.ToString());
            EditorGUILayout.LabelField("Layers", (preset.layers?.Count ?? 0).ToString());
            EditorGUILayout.LabelField("Channels", preset.channels == null ? "None" :
                string.Join(", ", preset.channels.Where(channel => channel != null)
                    .Select(channel => channel.channel.ToString())));
            EditorGUILayout.LabelField("Portability", preset.portability.ToString());
            EditorGUILayout.LabelField("Plugin Dependencies", (preset.plugins?.Count ?? 0).ToString());
            EditorGUILayout.LabelField("Package", preset.packaged
                ? $"Self-contained ({preset.packagedDependencies?.Count ?? 0} embedded assets)"
                : "Source preset");

            if (preset.packaged && preset.packagedExternalDependencies != null &&
                preset.packagedExternalDependencies.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "This package still requires project-level code or shaders:\n" +
                    string.Join("\n", preset.packagedExternalDependencies), MessageType.Warning);
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!AssetDatabase.Contains(preset)))
            {
                if (GUILayout.Button(preset.packaged ? "Repackage" : "Package"))
                    PackagePreset(preset);
            }
            if (!AssetDatabase.Contains(preset))
                EditorGUILayout.HelpBox("Save this preset as an asset before packaging it.",
                    MessageType.Info);

            if (GUILayout.Button("Validate and Migrate Preset"))
            {
                Undo.RecordObject(preset, "Migrate Material Preset");
                preset.Migrate();
                EditorUtility.SetDirty(preset);
                AssetDatabase.SaveAssetIfDirty(preset);
            }
            if (preset.layers == null || preset.layers.Count == 0)
                EditorGUILayout.HelpBox("This preset does not contain a layer stack.", MessageType.Warning);
        }

        private static void PackagePreset(TexturePaintMaterialPreset preset)
        {
            string sourcePath = AssetDatabase.GetAssetPath(preset);
            string directory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory)) directory = "Assets";
            string defaultName = preset.packaged
                ? preset.name + " Repackaged" : preset.name + " Packaged";
            string assetPath = EditorUtility.SaveFilePanelInProject(
                "Package Overlay Painter Material Preset",
                defaultName,
                "asset",
                "Choose a location for the self-contained material preset.",
                directory);
            if (string.IsNullOrEmpty(assetPath)) return;

            try
            {
                TexturePaintMaterialPreset packaged =
                    TexturePaintMaterialPresetPackager.Package(preset, assetPath);
                Selection.activeObject = packaged;
                EditorGUIUtility.PingObject(packaged);
                EditorUtility.DisplayDialog("Material Preset Packaged",
                    $"Created '{assetPath}' with " +
                    $"{packaged.packagedDependencies?.Count ?? 0} embedded dependencies.\n\n" +
                    "The packaged preset can be selected directly from Overlay Painter's " +
                    "Material Preset picker.", "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Could Not Package Material Preset",
                    exception.Message, "OK");
            }
        }
    }
}
