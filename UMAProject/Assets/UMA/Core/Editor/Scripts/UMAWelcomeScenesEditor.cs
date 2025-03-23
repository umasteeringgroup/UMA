using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


namespace UMA
{
    [CustomEditor(typeof(UMAWelcomeScenes))]
    public class UMAWelcomeScenesEditor : Editor
    {
        SceneAsset sceneAsset = null;


        [MenuItem("Assets/Create/UMA/Core/UMAWelcomeScenes")]
        public static void CreateUMAWelcomeScenes()
        {
            var scenes = CustomAssetUtility.CreateAsset<UMAWelcomeScenes>("", true, "UMAWelcomeScenes", true);
            EditorUtility.SetDirty(scenes);
            AssetDatabase.SaveAssetIfDirty(scenes);
        }
        public override void OnInspectorGUI()
        {
            GUIStyle description = new GUIStyle(EditorStyles.textArea);
            description.wordWrap = true;
            description.richText = true;
            description.fixedHeight = 48;

            GUILayout.Label("Add a scene to the welcome scenes by selecting the scene asset and clicking the add button.", EditorStyles.boldLabel);
            UMAWelcomeScenes uws = (UMAWelcomeScenes)target;

            GUILayout.BeginHorizontal();
            sceneAsset = (SceneAsset)EditorGUILayout.ObjectField("Add Scene Asset", sceneAsset, typeof(SceneAsset), false);
            if (GUILayout.Button("Add", GUILayout.Width(60)))
            {
                if (sceneAsset != null)
                {
                    UMAWelcomeScenes.UMAScene newScene = new UMAWelcomeScenes.UMAScene();
                    //newScene.sceneAsset = sceneAsset;
                    newScene.sceneName = sceneAsset.name;
                    newScene.scenePath = AssetDatabase.GetAssetPath(sceneAsset);
                    newScene.sceneDescription = "<description>";
                    uws.umaScenes.Add(newScene);
                    sceneAsset = null;
                }
                serializedObject.Update();
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Save Asset!"))
            {
                EditorUtility.SetDirty(uws);
                AssetDatabase.SaveAssetIfDirty(uws);
                AssetDatabase.Refresh();
            }


            GUILayout.Space(20);

            int delme = -1;
            float currentWidth = EditorGUIUtility.currentViewWidth;
            for (int i=0;i< uws.umaScenes.Count; i++)
            {
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(uws.umaScenes[i].sceneName, GUILayout.Width(120));
                if (GUILayout.Button("Load", GUILayout.Width(60)))
                {
                    uws.LoadScene(i);
                }
                if (GUILayout.Button("Cap", GUILayout.Width(40)))
                {
                   
                    // screen capture the image of the game window.
                    // create a new texture2D of 256x256
                    // copy the pixels from the game window into the texture2D

                    RenderTexture rt = new RenderTexture(256, 256, 24);
                    Camera.main.targetTexture = rt;
                    RenderTexture.active = Camera.main.targetTexture;
                    Camera.main.Render();
                    Texture2D tex = new Texture2D(256, 256);
                    tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                    tex.Apply();
                    Camera.main.targetTexture = null;
                    RenderTexture.active = null; //added to avoid errors
                    DestroyImmediate(rt);


                    // save it to the InternalDataStore/InEditor folder
                    // with the name based on the short name
                    // then add that texture to sceneTexture.

                    tex.name = uws.umaScenes[i].shortName + ".png";
                    byte[] bytes = tex.EncodeToPNG();
                    string path = "Assets/UMA/InternalDataStore/InEditor/" + tex.name;
                    System.IO.File.WriteAllBytes(path, bytes);
                    AssetDatabase.ImportAsset(path);
                    uws.umaScenes[i].sceneTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                }
                uws.umaScenes[i].shortName = EditorGUILayout.TextField(uws.umaScenes[i].shortName, GUILayout.Width(120));                
                uws.umaScenes[i].sceneTexture = (Texture2D)EditorGUILayout.ObjectField(uws.umaScenes[i].sceneTexture, typeof(Texture2D), false, GUILayout.Width(48), GUILayout.Height(48));
                uws.umaScenes[i].sceneDescription = EditorGUILayout.TextArea(uws.umaScenes[i].sceneDescription,description, GUILayout.Width(360),GUILayout.Height(48));
                if(GUILayout.Button("X", GUILayout.Width(20)))
                {
                    delme = i;
                }
                GUILayout.EndHorizontal();
            }

            if (delme != -1)
            {
                uws.umaScenes.RemoveAt(delme);
            }
            serializedObject.Update();
        }
    }
}