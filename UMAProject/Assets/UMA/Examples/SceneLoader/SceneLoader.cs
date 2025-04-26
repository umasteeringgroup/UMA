using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    [System.Serializable]
    public struct SceneData
    {
        public string sceneName;
        public int sceneIndex;
    }

    public List<SceneData> sceneList = new List<SceneData>();
    // Start is called before the first frame update

    private void OnGUI()
    {
        Rect centerScreen = new Rect(Screen.width / 2 - 200, Screen.height / 2 - 200, 400, 400);
        GUILayout.BeginArea(centerScreen, GUI.skin.box);
        foreach (var scene in sceneList)
            {
                if (GUILayout.Button(scene.sceneName,GUILayout.ExpandWidth(true)))
                {
                    SceneManager.LoadScene(scene.sceneIndex);
                }
            }
        GUILayout.EndArea();
    }

}
