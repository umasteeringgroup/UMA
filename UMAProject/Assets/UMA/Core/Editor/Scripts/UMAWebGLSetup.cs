using UnityEditor;

namespace UMA
{
    public class UMAWebGLSetup
    {
        [MenuItem("UMA/WebGL/Enable Embedded Resources")]
        public static void UMAEnableWebGLResources()
        {
            if (PlayerSettings.WebGL.useEmbeddedResources == true)
            {
                EditorUtility.DisplayDialog("WebGL Setup", "Embedded resources are already enabled in this project", "OK");
                return;
            }
            if (EditorUtility.DisplayDialog("WebGL Setup", "This enables embedded resources for WebGL. This is a requirement for the global library to work correctly. Continue?", "OK", "Cancel"))
            {
                PlayerSettings.WebGL.useEmbeddedResources = true;
                EditorUtility.DisplayDialog("WebGL Setup", "Embedded resources are now enabled for WebGL.", "OK");
            }
        }
    }
}