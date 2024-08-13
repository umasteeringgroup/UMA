using UnityEngine;
using UnityEditor;
using System;

namespace UMA.Editors
{
    public class MassChangeImporterSettings : EditorWindow
    {
        int MaxSize = 1024;
        int selectedFormat = 0;

        string[] ImportFormats = System.Enum.GetNames(typeof(TextureImporterFormat));

        bool updateSize;
        bool updateFormats;


        void OnGUI()
        {
            GUILayout.Label("UMA Texture Format Updater");
            GUILayout.Space(10);

            EditorGUILayout.LabelField("Select textures in project view");
            updateSize = EditorGUILayout.Toggle("Update Size", updateSize);
            if (updateSize)
            {
                MaxSize = EditorGUILayout.IntField("Enter Max Size", MaxSize);
            }
            updateFormats = EditorGUILayout.Toggle("Update Format", updateFormats);
            if (updateFormats)
            {
                selectedFormat = EditorGUILayout.Popup("Select Format", selectedFormat, ImportFormats);
            }

            if (updateFormats | updateSize)
            {
                if (GUILayout.Button("Update all selected textures "))
                {
                    int numChanges = 0;
                    TextureImporterFormat destFmt;
                    if (Enum.TryParse<TextureImporterFormat>(ImportFormats[selectedFormat], false, out destFmt))
                    {
                        var textures = Selection.GetFiltered(typeof(Texture2D), SelectionMode.Assets);
                        int processedTextures = 1;
                        int totalTextures = textures.Length;

                        foreach (var o in textures)
                        {
                            if (EditorUtility.DisplayCancelableProgressBar("Processing textures", $"{processedTextures} of {totalTextures}", processedTextures / (float)totalTextures))
                            {
                                EditorUtility.ClearProgressBar();
                                break;
                            }
                            string path = AssetDatabase.GetAssetPath(o);
                            TextureImporter importer = (TextureImporter)TextureImporter.GetAtPath(path);
                            if (importer == null)
                            {
                                continue;
                            }

                            var def = importer.GetDefaultPlatformTextureSettings();
                            var changed = false;


                            Action<TextureImporterPlatformSettings> maybeChange = (platSettings) =>
                            {
                                if (updateSize && platSettings.maxTextureSize != MaxSize)
                                {
                                    platSettings.maxTextureSize = MaxSize;
                                    changed = true;
                                }
                                if (updateFormats && platSettings.format != destFmt)
                                {
                                    platSettings.format = destFmt;
                                    changed = true;
                                }

                                if (changed == true)
                                {
                                    platSettings.overridden = true;
                                    importer.SetPlatformTextureSettings(platSettings);
                                }
                            };

                            maybeChange(importer.GetPlatformTextureSettings("iPhone"));
                            // Uncomment if you use Android
                            //maybeChange(importer.GetPlatformTextureSettings("Android"));

                            if (changed)
                            {
                                importer.SaveAndReimport();
                                ++numChanges;
                            }
                        }
                        EditorUtility.ClearProgressBar();
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("Nothing to update!");
            }
        }


        [MenuItem("UMA/Texture Format Updater")]
        public static void OpenUmaTexturePrepareWindow()
        {
            MassChangeImporterSettings window = (MassChangeImporterSettings)EditorWindow.GetWindow(typeof(MassChangeImporterSettings));
            
            window.titleContent.text = "Mass Set Importer Settings";
        }
    }
}
