using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public static class SetMeshReadWriteMenu
    {
        private const string MenuPath = "Assets/UMA/Set read flag to true";

        [MenuItem(MenuPath, false, 2010)]
        private static void SetReadWrite()
        {
            var modelPaths = new HashSet<string>();
            var serializedMeshPaths = new HashSet<string>();

            foreach (Object selectedObject in Selection.objects)
            {
                if (selectedObject is not Mesh mesh)
                {
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(selectedObject);
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                if (AssetImporter.GetAtPath(assetPath) is ModelImporter)
                {
                    modelPaths.Add(assetPath);
                    continue;
                }

                var serializedMesh = new SerializedObject(mesh);
                SerializedProperty isReadable = serializedMesh.FindProperty("m_IsReadable");
                if (isReadable != null)
                {
                    isReadable.boolValue = true;
                    serializedMesh.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.SaveAssetIfDirty(mesh);
                    serializedMeshPaths.Add(assetPath);
                }
            }

            foreach (string modelPath in modelPaths)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            foreach (string serializedMeshPath in serializedMeshPaths)
            {
                AssetDatabase.ImportAsset(serializedMeshPath, ImportAssetOptions.ForceUpdate);
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateSetReadWrite()
        {
            foreach (Object selectedObject in Selection.objects)
            {
                if (selectedObject is Mesh)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
