using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class PreviewSceneDebugger
{
    [MenuItem("UMA/Debug/Log Preview Scenes")]
    static void LogPreviewScenes()
    {
        var count = EditorSceneManager.previewSceneCount;
        Debug.Log("Preview scenes: " + count);
        Debug.Log("Total scenes: " + EditorSceneManager.sceneCount);
        
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var scene = EditorSceneManager.GetSceneAt(i);
            Debug.Log($"[{i}] {scene.name} isPreview: { EditorSceneManager.IsPreviewScene(scene)} - root count: {scene.rootCount}");
        }

//        for (int i = 0; i < count; i++)
//      {
//          var scene = EditorSceneManager.previewSceneCount > i ? EditorSceneManager.GetPreviewSceneAt(i) : default;
//          Debug.Log($"[{i}] {scene.name} - root count: {scene.rootCount}");
//      }
  }
}