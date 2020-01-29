using UnityEngine;

namespace UMA
{
    [CreateAssetMenu(menuName = "UMA/Rendering/PostProcess")]
    public class UMAPostProcess : ScriptableObject
    {
        public Shader shader;

        Material material;

        public void Process(RenderTexture source, RenderTexture destination)
        {
            if (shader == null)
            {
                if (Debug.isDebugBuild)
                    Debug.LogError("UMAPostProcess: " + name + " has no shader assigned!");
                return;
            }

            if (material == null)
                material = new Material(shader);
#if UNITY_ANDROID || UMA_IOS
            destination.DiscardContents();
#endif
            Graphics.Blit(source, destination, material);
        }
    }
}