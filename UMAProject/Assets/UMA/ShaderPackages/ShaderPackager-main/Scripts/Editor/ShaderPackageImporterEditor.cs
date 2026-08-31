//////////////////////////////////////////////////////
// Shader Packager
// Copyright (c)2021 Jason Booth
//////////////////////////////////////////////////////

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA.ShaderPackager;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif
using System.IO;


namespace UMA.ShaderPackager
{
    [CustomEditor(typeof(ShaderPackageImporter))]
    [CanEditMultipleObjects]
    public class ShaderPackageImporterEditor : ScriptedImporterEditor
    {
        SerializedProperty m_autoUpdate;
        SerializedProperty m_entryProperties;
#if __BETTERSHADERS__
      SerializedProperty m_betterShader;
      SerializedProperty m_optionOverrides;
#endif

        // override extraDataType to return the type that will be used in the Editor.
        protected override System.Type extraDataType => typeof(ShaderPackage);

        // override InitializeExtraDataInstance to set up the data.
        protected override void InitializeExtraDataInstance(Object extraTarget, int targetIndex)
        {
            var stack = (ShaderPackage)extraTarget;

            string fileContent = File.ReadAllText(((AssetImporter)targets[targetIndex]).assetPath);
            EditorJsonUtility.FromJsonOverwrite(fileContent, stack);
        }

        protected override void Apply()
        {
            base.Apply();
            // After the Importer is applied, rewrite the file with the custom value.
            for (int i = 0; i < targets.Length; i++)
            {
                string path = ((AssetImporter)targets[i]).assetPath;
                File.WriteAllText(path, EditorJsonUtility.ToJson((ShaderPackage)extraDataTargets[i]));
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            // In OnEnable, retrieve the importerUserSerializedObject property and store it.
            m_entryProperties = extraDataSerializedObject.FindProperty("entries");
#if __BETTERSHADERS__
         m_betterShader = extraDataSerializedObject.FindProperty("betterShader");
         m_optionOverrides = extraDataSerializedObject.FindProperty("optionOverrides");
#endif
        }

        public override void OnInspectorGUI()
        {
            extraDataSerializedObject.Update();
            ShaderPackage sp = extraDataSerializedObject.targetObject as ShaderPackage;
#if __BETTERSHADERS__
         EditorGUILayout.PropertyField(m_betterShader);
         EditorGUILayout.PropertyField(m_optionOverrides);
#endif
            EditorGUILayout.PropertyField(m_entryProperties);

            if ((typeof(ShaderPackage).Namespace == "JBooth.ShaderPackager") ||
               ShaderPackageImporter.k_FileExtension == ".shaderpack")
            {
                EditorGUILayout.HelpBox("Warning: You must change the namespace and extension!", MessageType.Error);
            }

            if (GUILayout.Button("Pack"))
            {
                sp.Pack(true);
            }
            if (GUILayout.Button("Pack all in Project"))
            {
                var guids = AssetDatabase.FindAssets("t:Shader");
                List<string> shaders = new List<string>();
                for (int i = 0; i < guids.Length; ++i)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (path.EndsWith(ShaderPackageImporter.k_FileExtension))
                    {
                        shaders.Add(path);
                    }
                }

                for (int i = 0; i < shaders.Count; ++i)
                {
                    var path = shaders[i];
                    EditorUtility.DisplayProgressBar("Packing Shaders", Path.GetFileName(path), (float)i / shaders.Count);
                    try
                    {
                        ShaderPackage packed = ShaderPackage.CreateInstance<ShaderPackage>();
                        UnityEditor.EditorJsonUtility.FromJsonOverwrite(File.ReadAllText(path), packed);
                        packed.Pack(true);
                        File.WriteAllText(path, EditorJsonUtility.ToJson(packed));
                        EditorUtility.SetDirty(packed);
                        AssetDatabase.SaveAssets();
                        AssetDatabase.ImportAsset(path);

                    }
                    catch
                    {
                        EditorUtility.ClearProgressBar();
                    }
                }
                EditorUtility.ClearProgressBar();
            }

            extraDataSerializedObject.ApplyModifiedProperties();

            ApplyRevertGUI();
        }


        [MenuItem("Assets/Create/Shader Package", priority = 300)]
        static void CreateMenuItemShaderPackage()
        {
            string directoryPath = "Assets";
         foreach (Object obj in Selection.GetFiltered(typeof(Object), SelectionMode.Assets))
            {
                directoryPath = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(directoryPath) && File.Exists(directoryPath))
                {
                    directoryPath = Path.GetDirectoryName(directoryPath);
                    break;
                }
            }
            directoryPath = directoryPath.Replace("\\", "/");
            if (directoryPath.Length > 0 && directoryPath[directoryPath.Length - 1] != '/')
                directoryPath += "/";
            if (string.IsNullOrEmpty(directoryPath))
                directoryPath = "Assets/";

            var fileName = string.Format("New ShaderPackage{0}", ShaderPackageImporter.k_FileExtension);
            directoryPath = AssetDatabase.GenerateUniqueAssetPath(directoryPath + fileName);
            var content = ScriptableObject.CreateInstance<ShaderPackage>();
            File.WriteAllText(directoryPath, EditorJsonUtility.ToJson(content));
            AssetDatabase.Refresh();
        }
    }

}