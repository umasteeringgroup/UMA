using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA
{
    public interface IEditorScene
    {
        void OnSceneGUI(InteractiveUMAWindow scene);
        void Initialize(InteractiveUMAWindow sceneView, Scene scene);
        void InitializationComplete(GameObject root);
        void Cleanup(InteractiveUMAWindow scene);
        void ShowHelp(bool isShown);
    }
}
