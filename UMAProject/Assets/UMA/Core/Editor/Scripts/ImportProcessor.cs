using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    public class ImportProcessor : AssetPostprocessor
    {
        // Start is called before the first frame update
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            string UMAVER = "UMA " + UmaAboutWindow.umaVersion;
            if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
            {
                return;
            }

            var settings = UMASettings.GetOrCreateSettings();
            if (settings == null)
            {
                return;
            }


            EnsureTags();
            if (!settings.Initialized)
            {
                settings.Initialized = true;
                EnsureUMAIndicators();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);

                UMAAssetIndexer UAI = UMAAssetIndexer.Instance;
                if (UAI == null)
                {
                    return;
                }

                /*int chosen = EditorUtility.DisplayDialogComplex("UMA " + UmaAboutWindow.umaVersion, "New UMA version imported. The global index should be rebuilt or restored (if you made a backup). (If you don't know what this means, choose 'Rebuild Index')", "Rebuild Index", "Restore from backup", "Do nothing");

                switch (chosen)
                {
                    case 0:
                        UAI.SaveKeeps();
                        UAI.Clear();
                        UAI.BuildStringTypes();
                        UAI.AddEverything(false);
                        UAI.RestoreKeeps();
                        UAI.ForceSave();
                        Resources.UnloadUnusedAssets();
                        EditorUtility.DisplayDialog(UMAVER, "Index rebuild complete", "OK");
                        break;

                    case 1:
                        string filename = EditorUtility.OpenFilePanel("Restore", "", "bak");
                        if (!string.IsNullOrEmpty(filename))
                        {
                            try
                            {
                                string backup = System.IO.File.ReadAllText(filename);
                                EditorUtility.DisplayProgressBar(UMAVER, "Restoring index", 0);
                                if (!UAI.Restore(backup))
                                {
                                    EditorUtility.DisplayDialog(UMAVER, "Error: Unable to restore index. Please review the console for more information.", "OK");
                                }
                                else
                                {
                                    EditorUtility.DisplayDialog(UMAVER, "Restore successful.", "OK");
                                }
                                backup = "";
                            }
                            catch (Exception ex)
                            {
                                Debug.LogException(ex);
                                EditorUtility.DisplayDialog("Error", "Error reading backup: " + ex.Message, "OK");
                            }
                            EditorUtility.ClearProgressBar();
                        }
                        break;

                    default:
                        EditorUtility.DisplayDialog("UMA " + UmaAboutWindow.umaVersion, "You can rebuild or restore the library from the Global Library window accessable from the UMA menu above.", "OK");
                        break;
                } */
            }
        }

        private static void EnsureUMAIndicators()
        {
            var defineSymbols = new HashSet<string>(PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup).Split(';'));
            if (!defineSymbols.Contains("UMA_INSTALLED"))
            {
                defineSymbols.Add("UMA_INSTALLED");
                PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, string.Join(";", defineSymbols));
            }
        }

        private static void EnsureTags()
        {
            // Open tag manager
            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            EnsureTag("UMAIgnore", tagManager);
            EnsureTag("UMAKeepChain", tagManager);
        }

        private static void EnsureTag(string s, SerializedObject tagManager)
        {
            SerializedProperty tagsProp = tagManager.FindProperty("tags");


            // First check if it is not already present
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                SerializedProperty t = tagsProp.GetArrayElementAtIndex(i);
                if (t.stringValue.Equals(s))
                {
                    return;
                }
            }

            tagsProp.InsertArrayElementAtIndex(0);
            SerializedProperty n = tagsProp.GetArrayElementAtIndex(0);
            n.stringValue = s;

            // and to save the changes
            tagManager.ApplyModifiedProperties();
        }
    }
}