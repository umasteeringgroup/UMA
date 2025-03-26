using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    public class UMAWelcomeScenes : ScriptableObject
    {
        [System.Serializable]
        public class UMAScene
        {
            //public SceneAsset sceneAsset;
            public string sceneName;
            public string shortName;
            public string scenePath;
            public Texture2D sceneTexture;
            public string sceneDescription;
        }

        public List<UMAScene> umaScenes = new List<UMAScene>();
        public void LoadScene(int index)
        {
            if (index >= 0 && index < umaScenes.Count)
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(umaScenes[index].scenePath);
            }
        }
        public void LoadScene(string sceneName)
        {
            for (int i = 0; i < umaScenes.Count; i++)
            {
                if (umaScenes[i].sceneName == sceneName)
                {
                    LoadScene(i);
                    return;
                }
            }
        }
    }
}